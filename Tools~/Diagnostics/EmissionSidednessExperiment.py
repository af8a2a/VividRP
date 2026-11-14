"""Validate Cornell emission sidedness from real GPU captures, without display exposure."""
import argparse
import json
from pathlib import Path

import numpy as np
from scipy.ndimage import minimum_filter, maximum_filter

from LinearGiExport import FIXTURE, expected_geometry
from ProbeAnchorCalibration import normal_from_packed

LUMA = np.array([.2126, .7152, .0722], np.float32)
MATERIAL_BACKFACE = 2048


def run(root):
    capture = json.loads((root / "capture/capture.json").read_text())
    assert capture.get("complete") is True, "Incomplete capture; see completedArm and errors"
    assert capture["width"] == 1920 and capture["height"] == 1080
    assert not capture["errors"] and capture["spacing"] == 16 and capture["rays"] == 8
    assert capture["camera"]["position"] == [0, 1.5, -3.8] and capture["camera"]["vertical_fov"] == 42
    fixture = json.loads(FIXTURE.read_text())
    ids, _, _, _, _, _, _ = expected_geometry(fixture, 1920, 1080)
    stable = minimum_filter(ids, 5) == maximum_filter(ids, 5)
    masks = {"ceiling": (ids == 2) & stable, "floor": (ids == 1) & stable}
    records = capture["records"]
    refined_path = root / "capture-fixed-geometry/capture.json"
    if refined_path.exists():
        refined = json.loads(refined_path.read_text())
        assert refined.get("complete") is True, "Incomplete refined capture"
        assert not refined["errors"] and refined["camera"] == capture["camera"]
        records = [r for r in records if not r["arm"].startswith("fixed-")] + refined["records"]
    files = {(r["arm"], r["frame"], r["tag"]): r for r in records if "tag" in r}
    states = [r for r in records if "state" in r]
    surfaces = {r["arm"]: r for r in records if r.get("bufferTag") == "surfaces"}

    def read(arm, frame, tag):
        r = files[(arm, frame, tag)]
        dtype = {"R16G16B16A16_SFloat": "<f2", "R32G32B32A32_SFloat": "<f4"}[r["format"]]
        value = np.fromfile(r["path"], dtype).reshape(r["height"], r["width"], 4).astype(np.float32)
        assert np.isfinite(value).all(), (arm, frame, tag, "Non-finite GPU result")
        return value

    report = {"contract": "scene-linear consumed E/pi; no exposure or gain fitting",
              "scope": "fixed camera, eight samples four frames apart after 512 warmup frames",
              "spatial_denoise": capture.get("spatialDenoise", "historical Atrous2"),
              "anchor_max_age": capture.get("anchorMaxAge", 32),
              "anchor_pre_filter": capture.get("anchorPreFilter", False),
              "floor_mean_gate_percent": 1.0, "arms": {}, "pairs": {}}
    report["capture_sources"] = [str(root / "capture/capture.json")] + ([str(refined_path)] if refined_path.exists() else [])
    images = {}
    for arm in dict.fromkeys(r["arm"] for r in states):
        rows = [r for r in states if r["arm"] == arm]
        frames = [r["frame"] for r in rows]
        assert frames == list(range(512, 544, 4)), (arm, frames)
        legacy = arm.startswith("legacy")
        for row in rows:
            s = row["state"]
            assert s["rays"] == 8 and s["spacing"] == 16 and s["forceTwoSided"] == legacy
            assert s["directionalCache"] and not s["restir"]
            if capture.get("schema") in ("emission-sidedness.v2", "emission-sidedness.v3", "emission-sidedness.v4"):
                assert s["spatialDenoise"] == capture["spatialDenoise"]
                expected_iterations = {"Atrous2": 2, "Atrous3": 3, "Atrous4": 4}.get(capture["spatialDenoise"], 0)
                assert s["atrousIterations"] == expected_iterations, (arm, s["atrousIterations"])
            if capture.get("schema") in ("emission-sidedness.v3", "emission-sidedness.v4"):
                assert s["anchorMaxAge"] == capture["anchorMaxAge"], (arm, s["anchorMaxAge"])
            if capture.get("schema") == "emission-sidedness.v4":
                assert s["anchorPreFilter"] == capture["anchorPreFilter"], (arm, s["anchorPreFilter"])
            assert s["traceMode"] == ("SdfOnly" if "sdf" in arm else "Hybrid")
            assert abs(s["rayBias"] - (.005 if "stress" in arm else .04)) < 1e-6
            assert s["sky"] == 0 and s["sun"][:3] == [0, 0, 0]
            if "order8" in arm: assert s["order"] == 8 and s["batch"] == 32
        buffer = surfaces[arm]
        assert buffer["stride"] == 64
        surface_data = np.fromfile(buffer["path"], "<f4").reshape(buffer["count"], 16)
        culls = surface_data[:, 7]
        emissive = np.max(surface_data[:, 4:7], axis=1) > 100
        assert emissive.sum() == 1 and np.all(culls[emissive] == (0 if legacy else 2)), (arm, culls)
        values = {key: [] for key in masks}
        coverage = {key: [] for key in masks}
        probe_values = {key: [] for key in masks}
        mean_image = np.zeros(ids.shape, np.float32)
        bright_back = black_count = black_covered = black_wrong_rgb = 0
        black_by_path = {"screen": 0, "sdf": 0}
        all_black_count = all_black_covered = all_black_wrong_rgb = 0
        ray_count = 0
        coverage_mismatch = 0
        front_radiances = []
        for frame in frames:
            final = read(arm, frame, "final")[::-1]
            assert np.isfinite(final).all()
            y = (final[..., :3] * final[..., 3:4]) @ LUMA
            mean_image += y / len(frames)
            for key, mask in masks.items():
                values[key].append(float(y[mask].mean()))
                coverage[key].append(float(final[..., 3][mask].mean()))
            geo = read(arm, frame, "probe-geometry")
            h, w = geo.shape[:2]
            assert (h, w) == (136, 120)
            normal = normal_from_packed(np.maximum(geo[..., 3], 0))
            probe_masks = {"ceiling": (geo[..., 3] >= 0) & (abs(geo[..., 1] - 3) < .002) & (normal[..., 1] < -.99),
                           "floor": (geo[..., 3] >= 0) & (abs(geo[..., 1]) < .002) & (normal[..., 1] > .99)}
            lighting = read(arm, frame, "hit-lighting")
            for key, mask in probe_masks.items():
                probe_values[key].append(float(((lighting[..., :3] * lighting[..., 3:4]) @ LUMA)[mask].mean()))
            rays = read(arm, frame, "rays").reshape(h, w, 8, 4)
            first = read(arm, frame, "first-hit").reshape(h, 3, w, 8, 4).transpose(0, 2, 3, 1, 4)
            flags = np.floor(rays[..., 3]).astype(np.uint32)
            ray_y = rays[..., :3] @ LUMA
            direction = first[..., 2, :3]
            cm = probe_masks["ceiling"][..., None]
            ray_count += int(cm.sum()) * 8
            bright_back += int((cm & (ray_y > 100) & (direction[..., 1] < 0)).sum())
            rejected = cm & ((flags & MATERIAL_BACKFACE) != 0)
            black_count += int(rejected.sum())
            valid = ((flags & 7) == 1) | ((flags & 7) == 2)
            for path, source in [("screen", 1), ("sdf", 2)]:
                black_by_path[path] += int((rejected & ((flags & 7) == source)).sum())
            all_rejected = (flags & MATERIAL_BACKFACE) != 0
            all_black_count += int(all_rejected.sum())
            all_black_covered += int((all_rejected & valid & (lighting[..., 3:4] > 0)).sum())
            all_black_wrong_rgb += int((all_rejected & (np.max(abs(rays[..., :3]), -1) != 0)).sum())
            black_covered += int((rejected & valid & (lighting[..., 3:4] > 0)).sum())
            black_wrong_rgb += int((rejected & (np.max(abs(rays[..., :3]), -1) != 0)).sum())
            sources = read(arm, frame, "sources")
            if "order0" in arm:
                expected_valid = np.rint((sources[..., 0] + sources[..., 1]) * 8).astype(int)
                coverage_mismatch += int((expected_valid[probe_masks["ceiling"]] != valid.sum(-1)[probe_masks["ceiling"]]).sum())
                front = probe_masks["floor"][..., None] & (ray_y > 100) & (direction[..., 1] > 0)
                front_radiances.extend(ray_y[front].tolist())
        assert black_wrong_rgb == 0 and black_count == black_covered and coverage_mismatch == 0
        assert all_black_wrong_rgb == 0 and all_black_count == all_black_covered
        if not legacy: assert bright_back == 0, (arm, bright_back)
        if not legacy and "order0" in arm: assert black_count > 0, "No covered black samples exercised"
        if "order0" in arm:
            assert front_radiances and np.max(np.abs(np.asarray(front_radiances) - 200)) < .001
        summary = {"gpu_cull_modes": sorted(set(culls.tolist())), "ray_bias": rows[0]["state"]["rayBias"],
                   "regions": {key: {"mean_y": float(np.mean(value)), "frame_mean_range": [min(value), max(value)],
                                      "coverage_mean": float(np.mean(coverage[key])), "probe_raw_mean_y": float(np.mean(probe_values[key]))}
                               for key, value in values.items()},
                   "ceiling_rays": ray_count, "bright_back_hits": bright_back,
                   "material_black_hits": black_count, "material_black_valid_and_covered": black_covered,
                   "ceiling_material_black_hits_by_path": black_by_path,
                   "all_material_black_hits": all_black_count,
                   "all_material_black_valid_and_covered": all_black_covered,
                   "source_coverage_mismatches": coverage_mismatch,
                   "floor_front_radiance_samples": len(front_radiances),
                   "floor_front_radiance_range": [min(front_radiances), max(front_radiances)] if front_radiances else None}
        report["arms"][arm] = summary
        images[arm] = mean_image
    for kind in ["hybrid-order0", "sdf-order0", "hybrid-order8", "stress-sdf-order0"]:
        a = report["arms"]["legacy-" + kind]["regions"]
        b = report["arms"]["fixed-" + kind]["regions"]
        changes = {key: 100 * (b[key]["mean_y"] / max(a[key]["mean_y"], 1e-8) - 1) for key in masks}
        report["pairs"][kind] = {"final_mean_change_percent": changes,
            "floor_raw_mean_change_percent": 100 * (b["floor"]["probe_raw_mean_y"] / max(a["floor"]["probe_raw_mean_y"], 1e-8) - 1),
            "floor_within_1_percent": abs(changes["floor"]) <= 1.0}
    assert report["arms"]["fixed-hybrid-order0"]["ceiling_material_black_hits_by_path"]["screen"] > 0
    assert report["arms"]["fixed-sdf-order0"]["ceiling_material_black_hits_by_path"]["sdf"] > 0
    (root / "analysis.json").write_text(json.dumps(report, indent=2))
    assert all(pair["floor_within_1_percent"] for pair in report["pairs"].values()), "Floor mean energy gate failed; see analysis.json"
    plot(root, images, masks)
    print(json.dumps({"pairs": report["pairs"], "arms": {k: {p: v[p] for p in ["bright_back_hits", "material_black_hits", "material_black_valid_and_covered"]} for k, v in report["arms"].items()}}, indent=2))


def plot(root, images, masks):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(2, 2, figsize=(12, 6), layout="constrained")
    for ax, arm in zip(axes.flat, ["legacy-hybrid-order0", "fixed-hybrid-order0", "legacy-stress-sdf-order0", "fixed-stress-sdf-order0"]):
        image = np.where(masks["ceiling"], images[arm], np.nan)
        im = ax.imshow(image[60:280, 550:1370], cmap="magma", vmin=0, vmax=10)
        ax.set_title(arm + f"\nCeiling Y={images[arm][masks['ceiling']].mean():.4f}", fontsize=10)
        ax.set_axis_off()
    fig.colorbar(im, ax=axes, label="Linear consumed E/pi Y (display clipped at 10)", shrink=.85)
    fig.suptitle("One-sided ceiling emitter - fixed camera, 16px / 8 rays, zero radiosity")
    fig.savefig(root / "emission-sidedness.png", dpi=150)
    plt.close(fig)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True, help="evidenceRoot returned by EmissionSidednessCapture.cs")
    run(parser.parse_args().root)
