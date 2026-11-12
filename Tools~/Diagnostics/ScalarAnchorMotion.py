"""Validate scalar and pixel history at disocclusions against matched-pose Monte Carlo resolves."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import source_cards, unpack_pixel_history
from GatherHistoryMotion import world
from ProbeAnchorCalibration import LUMA, normal_from_packed


def metric(error):
    assert error.size
    return {"samples": int(error.size), "mean": float(error.mean()),
            "rms": float(np.sqrt(np.mean(error ** 2))),
            "absolute_p99": float(np.percentile(np.abs(error), 99)),
            "absolute_max": float(np.max(np.abs(error)))}


def exposed(position, normal, previous_vp, previous_inverse, previous_geometry, valid):
    """Conservative, unambiguous foreground disappearance; same predicate for all arms."""
    clip = np.concatenate([position, np.ones((*position.shape[:-1], 1))], -1) @ previous_vp.T
    projected = clip[..., :3] / clip[..., 3:4]
    nearest = np.rint(np.stack([(projected[..., 0] + 1) * 960 - .5,
                               (1 - projected[..., 1]) * 540 - .5], -1)).astype(int)
    inside = ((clip[..., 3] > 0) & (nearest[..., 0] >= 940) & (nearest[..., 0] < 1260)
              & (nearest[..., 1] >= 480) & (nearest[..., 1] < 736))
    ix, iy = np.clip(nearest[..., 0] - 940, 0, 319), np.clip(nearest[..., 1] - 480, 0, 255)
    old_geometry = previous_geometry[iy, ix]
    old_depth = (old_geometry & 65535).astype("<u2").view("<f2").astype(float)
    old_normal = normal_from_packed((old_geometry >> 16).astype(float))
    delta = world(nearest, old_depth, previous_inverse) - world(nearest, projected[..., 2], previous_inverse)
    plane = np.maximum(np.abs(np.sum(delta * old_normal, -1)), np.abs(np.sum(delta * normal, -1)))
    return (valid & inside & (old_depth > projected[..., 2]) & (plane > .075)
            & (np.linalg.norm(delta, axis=-1) > .10))


def analyze(capture, reference, output):
    output.mkdir(parents=True, exist_ok=True)
    rows = list(csv.DictReader((capture / "frames.csv").open(encoding="utf-8-sig")))
    ref_rows = list(csv.DictReader((reference / "frames.csv").open(encoding="utf-8-sig")))
    report = {"scope": "128-frame fixed trajectory; unambiguous disocclusions in a 320x256 panel-edge patch. "
                       "Reference is this renderer's 512-frame current-only resolve, not a physical ground truth.",
              "arms": {}}
    canonical_vp, canonical_geometry, canonical_source = None, None, None
    yy, xx = np.mgrid[480:736, 940:1260]
    pixels = np.stack([xx, yy], -1)
    previews = {}
    for mode in ("legacy", "novis", "fixed"):
        arm = "grid5-directional-hybrid-bounce8-spatial-" + mode + "-diagnostics"
        records = sorted((r for r in rows if r["arm"] == arm), key=lambda r: int(r["frame"]))
        assert [int(r["frame"]) for r in records] == list(range(128))
        assert all(r["order"] == "8" and (r["gw"], r["gh"]) == ("120", "136") for r in records)
        vp = np.array([np.fromstring(r["vp"], sep=" ").reshape(4, 4) for r in records])
        inverse = np.linalg.inv(vp)
        geometry = np.stack([np.fromfile(capture / f"{arm}-{i:02}-edge-geometry.u32", "<u4").reshape(256, 320)
                             for i in range(128)])
        source = source_cards(capture / f"{arm}-00-radiosity.f16")
        assert np.array_equal(source.view("<u2"), source_cards(capture / f"{arm}-127-radiosity.f16").view("<u2"))
        if canonical_vp is None:
            canonical_vp, canonical_geometry, canonical_source = vp, geometry, source
        assert np.array_equal(vp, canonical_vp), "Camera trajectories differ"
        assert np.array_equal(geometry, canonical_geometry), "Receiver geometry differs"
        assert np.array_equal(source.view("<u2"), canonical_source.view("<u2")), "Radiosity differs between arms"
        pixel_mask = np.zeros(geometry.shape, bool)
        probe_mask = np.zeros((128, 20, 26), bool)
        pixel_age, pixel_status, pixel_error, anchor_age, anchor_class, anchor_error = [], [], [], [], [], []
        comparisons = {}
        reference_errors = {stage: [] for stage in ("resolve", "temporal", "final")}

        def read(i, stage):
            return np.fromfile(capture / f"{arm}-{i:02}-{stage}.f16", "<f2").reshape(256, 320, 4).astype(float)

        def covered(value):
            assert np.isfinite(value).all()
            return value[..., :3] * value[..., 3:4]

        for i in range(1, 128):
            depth = (geometry[i] & 65535).astype("<u2").view("<f2").astype(float)
            normal = normal_from_packed((geometry[i] >> 16).astype(float))
            raw, temporal = read(i, "edge-resolve"), read(i, "edge-temporal")
            mask = exposed(world(pixels, depth, inverse[i]), normal, vp[i - 1], inverse[i - 1],
                           geometry[i - 1], (depth > 0) & (np.abs(temporal[..., 3]) > 0))
            pixel_mask[i] = mask
            history = read(i, "edge-history").astype("<f2")
            age, _, status = unpack_pixel_history(history[..., 3])
            pixel_age.extend(age[mask])
            pixel_status.extend(status[mask])
            pixel_error.extend(((covered(temporal) - covered(raw)) @ LUMA)[mask])
            p = np.fromfile(capture / f"{arm}-{i:02}-probe-geometry.f32", "<f4").reshape(20, 26, 4)
            state = np.fromfile(capture / f"{arm}-{i:02}-probe-stats.f16", "<f2").reshape(20, 26, 4)
            anchor = np.fromfile(capture / f"{arm}-{i:02}-probe-anchor.f16", "<f2").reshape(20, 26, 4).astype(float)
            raw_anchor = np.fromfile(capture / f"{arm}-{i:02}-probe-raw.f16", "<f2").reshape(20, 26, 4).astype(float)
            pmask = exposed(p[..., :3], normal_from_packed(p[..., 3]), vp[i - 1], inverse[i - 1],
                            geometry[i - 1], raw_anchor[..., 3] > 0)
            probe_mask[i] = pmask
            anchor_age.extend(state[..., 0][pmask])
            anchor_class.extend(state[..., 3][pmask])
            anchor_error.extend(((covered(anchor) - covered(raw_anchor)) @ LUMA)[pmask])
            if i not in (24, 48, 80, 104):
                continue
            ref_arm = "grid5-directional-hybrid-bounce8-spatial-reference" + str(i) + "-diagnostics"
            matrices = [np.fromstring(r["vp"], sep=" ").reshape(4, 4) for r in ref_rows if r["arm"] == ref_arm]
            assert len(matrices) >= 512 and all(np.array_equal(m, vp[i]) for m in matrices), "Reference pose differs"
            counts = json.loads((reference / f"reference{i}-counts.json").read_text())
            assert counts["samples"] == 512
            for endpoint in ("first", "last"):
                ref_source = source_cards(reference / f"reference{i}-{endpoint}-source.f16")
                assert np.array_equal(ref_source.view("<u2"), source.view("<u2")), "Reference radiosity differs"
            ref = np.fromfile(reference / f"reference{i}-mean.f32", "<f4").reshape(256, 320, 4)
            se = np.fromfile(reference / f"reference{i}-se.f32", "<f4").reshape(256, 320, 4)
            # Include the preceding three newly revealed pixel strips, reprojected to this exact pose.
            region = mask.copy()
            for previous in range(max(1, i - 3), i):
                old_depth = (geometry[previous] & 65535).astype("<u2").view("<f2").astype(float)
                old_position = world(pixels, old_depth, inverse[previous])
                clip = np.concatenate([old_position, np.ones((*old_position.shape[:-1], 1))], -1) @ vp[i].T
                projected = clip[..., :3] / clip[..., 3:4]
                nearest = np.rint(np.stack([(projected[..., 0] + 1) * 960 - .5,
                                           (1 - projected[..., 1]) * 540 - .5], -1)).astype(int)
                valid = (pixel_mask[previous] & (nearest[..., 0] >= 940) & (nearest[..., 0] < 1260)
                         & (nearest[..., 1] >= 480) & (nearest[..., 1] < 736))
                region[nearest[..., 1][valid] - 480, nearest[..., 0][valid] - 940] = True
            comparisons[str(i)] = {"newly_revealed_pixels": int(region.sum()),
                                    "reference_y_iid_se_mean": float(se[..., 3][region].mean()), "stages": {}}
            for stage in ("resolve", "temporal", "final"):
                rgb = covered(read(i, "edge-" + stage))
                y = rgb @ LUMA
                reference_errors[stage].append((y - ref[..., 3])[region])
                comparisons[str(i)]["stages"][stage] = {
                    "disoccluded_y": metric((y - ref[..., 3])[region]),
                    "whole_patch_y": metric((y - ref[..., 3])[ref[..., 3] > 1]),
                    "disoccluded_mean_y": float(y[region].mean()),
                    "reference_disoccluded_mean_y": float(ref[..., 3][region].mean())}
                if stage == "final":
                    previews[(mode, i)] = rgb
        age, status, a_age, a_class = map(np.asarray, (pixel_age, pixel_status, anchor_age, anchor_class))
        report["arms"][mode] = {
            "disoccluded_pixel_samples": int(pixel_mask.sum()),
            "disoccluded_pixel_frames": int(pixel_mask.any((1, 2)).sum()),
            "pixel_age_gt_one_percent": float(100 * np.mean(age > 1)),
            "pixel_history_accepted_percent": float(100 * np.mean(status == 1)),
            "pixel_temporal_minus_current_y": metric(np.asarray(pixel_error)),
            "disoccluded_probe_samples": int(probe_mask.sum()),
            "probe_age_gt_one_percent": float(100 * np.mean(a_age > 1)),
            "probe_rejection_class3_percent": float(100 * np.mean(a_class == 3)),
            "probe_temporal_minus_raw_y": metric(np.asarray(anchor_error)),
            "reference_disoccluded_aggregate": {stage: metric(np.concatenate(error))
                                                 for stage, error in reference_errors.items()},
            "matched_reference": comparisons}
    report["geometry_trajectory_source_identical"] = True
    (output / "motion-summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(3, 4, figsize=(13, 8), constrained_layout=True)
    for row, mode in enumerate(("legacy", "novis", "fixed")):
        for col, i in enumerate((24, 48, 80, 104)):
            value = previews[(mode, i)][::-1].clip(0) / 256
            axes[row, col].imshow((value / (1 + value)) ** (1 / 2.2))
            axes[row, col].set_title(f"{mode}, frame {i}")
            axes[row, col].axis("off")
    fig.savefig(output / "motion-comparison.png", dpi=160)
    plt.close(fig)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("reference", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    analyze(args.capture, args.reference, args.output)
