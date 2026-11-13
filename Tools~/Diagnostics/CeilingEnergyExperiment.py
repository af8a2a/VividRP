"""Split the Cornell ceiling discrepancy using measured, scene-linear GPU output.

This is an ablation report, not an additive decomposition or a physical reference.
VividRP stays at 16px / 8 rays; Unreal retains its original native Epic budget.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from scipy.ndimage import minimum_filter, maximum_filter, distance_transform_edt

from LinearGiExport import exr_read
from ProbeAnchorCalibration import evaluate_sh, normal_from_packed

LUMA = np.array([0.2126, 0.7152, 0.0722], np.float32)


def summary(y, mask):
    values = y[mask]
    if not np.isfinite(values).all():
        raise ValueError("Non-finite receiver values")
    return {"pixels": int(mask.sum()), "mean_y": float(values.mean()),
            "median_y": float(np.median(values)), "p99_y": float(np.quantile(values, .99)),
            "max_y": float(values.max())}


def read_texture(record):
    dtype = {"R16G16B16A16_SFloat": "<f2", "R32G32B32A32_SFloat": "<f4"}[record["format"]]
    return np.fromfile(record["path"], dtype).reshape(record["height"], record["width"], 4).astype(np.float32)


def analyze(linear, experiment):
    ids = exr_read(linear / "vivid-ev4/object_id.exr")
    stable = minimum_filter(ids, 5) == maximum_filter(ids, 5)
    ceiling = (ids == 2) & stable
    distance = distance_transform_edt(ids != 8)
    masks = {"ceiling": ceiling, "near_emitter_32px": ceiling & (distance < 32),
             "far_from_emitter_32px": ceiling & (distance >= 32), "floor": (ids == 1) & stable}
    images = {}
    report = {"contract": "consumed E/pi, linear sRGB Y; no exposure or gain fitting",
              "scope": "UE one frame per case; Vivid eight frames four frames apart after 512 warmup frames; not a converged truth",
              "mask": "common analytic labels, native receiver validity, 5x5 object-stable interior; emitter excluded",
              "interpretation": "ablations interact; their deltas must not be added as independent contributions",
              "unreal": {}, "unity": {}}
    cases = {"vivid-original": linear / "vivid-ev4", "unreal-original": linear / "unreal-ev4"}
    cases.update({"unreal-" + name: experiment / "unreal" / name for name in
                  ["sdf", "sdf-no-radiosity", "no-radiosity", "no-spatial-filter", "no-pixel-temporal", "thin-screen-depth"]})
    for name, root in cases.items():
        metadata = json.loads((root / "metadata.json").read_text())
        assert metadata["quantity"] == "irradiance_over_pi" and metadata["pre_exposure_removed"]
        assert metadata["camera"] == json.loads((linear / "vivid-ev4/metadata.json").read_text())["camera"]
        assert not metadata["receiving_albedo_in_e_over_pi"] and not metadata["display_transform_applied"]
        y = exr_read(root / "e_over_pi.exr") @ LUMA
        images[name] = y
        report["unreal"][name] = {"metadata": str(root / "metadata.json"),
                                  "regions": {key: summary(y, mask) for key, mask in masks.items()}}
    delta = images["unreal-original"] - images["vivid-original"]
    report["original_gap"] = {
        "ceiling_percent": 100 * (images["unreal-original"][ceiling].mean() / images["vivid-original"][ceiling].mean() - 1),
        "near32_share_of_signed_excess": float(delta[masks["near_emitter_32px"]].sum() / delta[ceiling].sum()),
        "bright_ue_over10_share_of_signed_excess": float(delta[ceiling & (images["unreal-original"] > 10)].sum() / delta[ceiling].sum()),
        "distance_bands": []}
    for lo, hi in [(0, 8), (8, 16), (16, 32), (32, 64), (64, 128), (128, 256), (256, 10000)]:
        mask = ceiling & (distance >= lo) & (distance < hi)
        report["original_gap"]["distance_bands"].append({"distance_px": [lo, hi], "pixels": int(mask.sum()),
            "vivid_mean_y": float(images["vivid-original"][mask].mean()), "unreal_mean_y": float(images["unreal-original"][mask].mean()),
            "share_of_signed_excess": float(delta[mask].sum() / delta[ceiling].sum())})

    capture_paths = [experiment / "unity-verified/capture.json"]
    if (experiment / "unity-bias5mm/capture.json").exists():
        capture_paths.append(experiment / "unity-bias5mm/capture.json")
    records = []
    for path in capture_paths:
        capture = json.loads(path.read_text())
        assert not capture["errors"] and capture["spacing"] == 16 and capture["rays"] == 8
        assert capture["camera"]["position"] == [0.0, 1.5, -3.8] and capture["camera"]["vertical_fov"] == 42
        records.extend(capture["records"])
    files = {(r["arm"], r["frame"], r["tag"]): r for r in records if "tag" in r}
    states = [r for r in records if "state" in r]
    for arm in dict.fromkeys(r["arm"] for r in states):
        frames = sorted(r["frame"] for r in states if r["arm"] == arm)
        assert frames == list(range(512, 544, 4)), (arm, frames)
        for r in (r for r in states if r["arm"] == arm):
            state = r["state"]
            assert state["directionalCache"] and not state["restir"] and state["radioSamples"] == 64
            assert state["sun"][:3] == [0, 0, 0] and state["sky"] == 0
            if "order0" not in arm:
                assert state["order"] == 8 and state["batch"] == 32, state
            if "bias5mm" in arm:
                assert state["rayBias"] == .005 and state["order"] == 0
        get = lambda frame, tag: read_texture(files[(arm, frame, tag)])
        arm_result = {"frames": frames, "pixel_stages": {}, "probe_stages": {}}
        for stage in ["resolved", "pixel-temporal", "final"]:
            region_values = {key: [] for key in masks}
            mean_image = np.zeros(ids.shape, np.float32)
            for frame in frames:
                rgba = get(frame, stage)[::-1]
                y = (rgba[..., :3] * rgba[..., 3:4]) @ LUMA
                mean_image += y / len(frames)
                for key, mask in masks.items():
                    region_values[key].append(y[mask])
            arm_result["pixel_stages"][stage] = {}
            for key, values in region_values.items():
                values = np.stack(values)
                arm_result["pixel_stages"][stage][key] = {"mean_y": float(values.mean()),
                    "temporal_rms": float(values.std(axis=0).mean()),
                    "frame_mean_range": [float(values.mean(1).min()), float(values.mean(1).max())]}
            if stage == "final":
                images["vivid-" + arm] = mean_image
        probe_values = {}
        ray_total, bright_hits = 0, []
        for frame in frames:
            geometry = get(frame, "probe-geometry")
            normal = normal_from_packed(np.maximum(geometry[..., 3], 0))
            mask = (geometry[..., 3] >= 0) & (np.abs(geometry[..., 1] - 3.0) < .002) & (normal[..., 1] < -.99)
            h, w = geometry.shape[:2]
            sh = get(frame, "sh").reshape(h, 22, w, 4).transpose(1, 0, 2, 3)[:, mask, :]
            anchor = get(frame, "anchor")[:, :w][mask]
            hits = get(frame, "hit-lighting")[mask]
            values = {"raw_hit": hits[..., :3] * hits[..., 3:4],
                      "raw_anchor": sh[21, :, :3] * sh[21, :, 3:4],
                      "temporal_anchor": anchor[..., :3] * anchor[..., 3:4]}
            for name, offset in [("unfiltered_sh", 14), ("pre_calibration_sh", 7), ("calibrated_sh", 0)]:
                block = sh[offset:offset+7]
                values[name] = evaluate_sh(block[None], normal[mask][None])[0] * block[6, :, 3:4]
            for name, value in values.items():
                probe_values.setdefault(name, []).append(float((value @ LUMA).mean()))
            if "order0" in arm:
                rays = get(frame, "rays").reshape(h, w, 8, 4)
                first = get(frame, "first-hit").reshape(h, 3, w, 8, 4).transpose(0, 2, 3, 1, 4)
                y = rays[..., :3] @ LUMA
                ray_total += int(mask.sum()) * 8
                for row, column, ray in np.argwhere(mask[..., None] & (y > 100)):
                    direction = first[row, column, ray, 2, :3]
                    bright_hits.append({"frame": frame, "position": geometry[row, column, :3].tolist(),
                        "radiance_y": float(y[row, column, ray]), "state": int(rays[row, column, ray, 3]),
                        "direction": direction.tolist(), "shell_normal": normal_from_packed(first[row, column, ray, 1, 0]).tolist(),
                        # The fixture's only emitter is a one-sided quad facing -Y.
                        "emitter_n_dot_outgoing": float(direction[1])})
        for name, values in probe_values.items():
            arm_result["probe_stages"][name] = {"mean_y": float(np.mean(values)), "frame_mean_range": [min(values), max(values)]}
        radio_files = [Path(files[(arm, f, "radiosity")]["path"]) for f in frames if (arm, f, "radiosity") in files]
        arm_result["radiosity_sha256"] = sorted(set(hashlib.sha256(p.read_bytes()).hexdigest() for p in radio_files))
        if "order0" in arm:
            arm_result["zero_order_emissive_hits"] = {"ceiling_rays": ray_total, "bright_hits": bright_hits,
                "unweighted_ray_mean_y_from_bright_hits": float(sum(hit["radiance_y"] for hit in bright_hits) / ray_total),
                "note": "Ray count mean does not include guiding PDF weights; use raw_hit for the estimator energy."}
        report["unity"][arm] = arm_result
    report["radiosity_identical_hybrid_sdf"] = report["unity"]["hybrid"]["radiosity_sha256"] == report["unity"]["sdf"]["radiosity_sha256"]
    report["excluded_captures"] = ["Unreal sdf and sdf-no-radiosity before verified reruns: diagnostic switches failed actual CVar validation",
                                   "Unity first run under unity/: GPU data captured but parameter metadata serialization failed"]
    def native(value):
        if isinstance(value, np.generic): return value.item()
        raise TypeError(type(value))
    (experiment / "analysis.json").write_text(json.dumps(report, indent=2, default=native))
    plot(experiment, images, masks, ids == 8)
    print(json.dumps({"report": str(experiment / "analysis.json"), "original_gap": report["original_gap"],
                      "radiosity_identical_hybrid_sdf": report["radiosity_identical_hybrid_sdf"]}, indent=2, default=native))


def plot(output, images, masks, emitter):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(2, 3, figsize=(15, 4.4), layout="constrained")
    ey, ex = np.where(emitter)
    crop = (slice(max(0, ey.min() - 65), ey.max() + 66), slice(max(0, ex.min() - 120), ex.max() + 121))
    keys = [("vivid-hybrid", "Vivid - Hybrid (8 frames)"), ("unreal-original", "Lumen - baseline"),
            ("unreal-thin-screen-depth", "Lumen - depth thickness 0.001"),
            ("vivid-sdf", "Vivid - SDF (8 frames)"), ("unreal-sdf", "Lumen - Screen Traces off"),
            ("unreal-no-pixel-temporal", "Lumen - pixel temporal off")]
    for ax, (key, title) in zip(axes.flat, keys):
        # Display ceiling receivers only, with one shared linear color scale.
        values = np.where(masks["ceiling"], images[key], np.nan)[crop]
        im = ax.imshow(values, vmin=0, vmax=20, cmap="magma")
        ax.set_title(title + f"\nCeiling mean Y = {images[key][masks['ceiling']].mean():.3f}", fontsize=10)
        ax.set_axis_off()
    fig.colorbar(im, ax=axes, label="Consumed E/pi, linear Y (display clipped at 20)", shrink=.85)
    fig.suptitle("Emitter neighbourhood - shared linear scale; means over entire ceiling; ablations interact")
    fig.savefig(output / "ceiling-ablation.png", dpi=150)
    plt.close(fig)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--linear", type=Path, default=Path("E:/VividRP_Reborn/Logs/LinearGI-20261113/linear"))
    parser.add_argument("--experiment", type=Path, default=Path("E:/VividRP_Reborn/Logs/CeilingEnergy-20261113"))
    args = parser.parse_args()
    analyze(args.linear, args.experiment)
