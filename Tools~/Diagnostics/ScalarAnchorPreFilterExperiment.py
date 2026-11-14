"""Validate native half-step anchor reconstruction with GPU A/A and same-input A/B."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from EmissiveRadiosityConvergence import plane_masks
from LinearGiExport import exr_read
from ProbeAnchorCalibration import evaluate_sh, normal_from_packed
from ScalarAnchorAgeExperiment import motion
from TemporalSamplingExperiment import LUMA, metrics, paired, read


def protected_probes(geometry):
    """Independent CPU evaluation of the guard using actual GPU probe geometry."""
    g = geometry[:68, :120]; adaptive = geometry[68:136, :120]
    n = normal_from_packed(np.maximum(g[..., 3], 0))
    yy, xx = np.mgrid[:68, :120]
    protected = g[..., 3] < 0
    for dy in range(-4, 5):
        for dx in range(-4, 5):
            ox, oy = xx+dx, yy+dy
            protected |= (ox < 0) | (ox >= 120) | (oy < 0) | (oy >= 68)
            ox, oy = np.clip(ox, 0, 119), np.clip(oy, 0, 67)
            other, on = g[oy, ox], n[oy, ox]
            delta = other[..., :3]-g[..., :3]
            tolerance = .002+.008*np.linalg.norm(delta, axis=-1)
            plane = np.maximum(abs(np.sum(delta*n, -1)), abs(np.sum(delta*on, -1)))
            protected |= (other[..., 3] < 0) | (np.sum(n*on, -1) < .995) | (plane >= tolerance) | (adaptive[oy, ox, 3] >= 0)
    return protected


def grid_reference(root, source, geometry):
    m = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    assert m["complete"] and not m["errors"] and m["measuredFrames"] == 1024
    f = {(r["arm"], r["frame"], r["tag"]): r for r in m["records"] if "tag" in r}
    blocks = []
    for arm in ["uniform-a", "uniform-b"]:
        states = [r["state"] for r in m["records"] if r.get("arm") == arm and "state" in r]
        assert len(states) == 1024 and all(v["order"] == 8 and v["batch"] == 32 and v["samples"] == 64 and v["seed"] == 0
            and not v["guide"] and not v["restir"] and v["sun"] == [0, 0, 0] and v["sky"] == 0 for v in states)
        for frame in [512, 1535]:
            assert np.array_equal(read(f[arm, frame, "geometry"]), geometry)
            assert Path(f[arm, frame, "radiosity"]["path"]).read_bytes() == source
        for first in range(512, 1536, 64):
            values = np.stack([read(f[arm, i, "raw"])[:68, :120] for i in range(first, first+64)])
            blocks.append((values[..., :3]*values[..., 3:4]).mean(0, dtype=float))
    blocks = np.asarray(blocks)
    return blocks.mean(0), (blocks@LUMA).var(0, ddof=1)/len(blocks), blocks


def analyze(root, reference, linear, partial=False):
    m = json.loads((root / ("capture-progress.json" if partial else "capture.json")).read_text(encoding="utf-8"))
    assert not m["errors"] and m["warmup"] in (512, 1024) and m["measuredFrames"] == 256
    first = m["warmup"]; last = first+255
    if not partial:
        assert m["complete"] and m["schema"] == "scalar-anchor-prefilter.v1"
        assert (m["width"], m["height"], m["spacing"], m["rays"]) == (1920, 1080, 16, 8)
    f = {(r["arm"], r["frame"], r["tag"]): r for r in m["records"] if "tag" in r}
    states = [r for r in m["records"] if "state" in r]
    frames = range(first, last+1)
    expected = ["baseline-a", "baseline-b", "candidate", "baseline-motion", "candidate-motion"]
    arms = [a for a in expected if all((a, i, "raw") in f for i in frames)]
    if not partial:
        assert arms == expected
    matrices = {(r["arm"], r["frame"]): np.asarray(r["vp"]).reshape(4, 4) for r in m["records"] if "vp" in r}
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    stack = lambda arm, tag: np.stack([read(f[arm, i, tag]) for i in frames])
    report = {"scope": "Same-input native half-step anchor reconstruction, scene-linear consumed E/pi", "complete": not partial,
              "arms": {}, "static": {}, "grid": {}, "contacts": {}, "motion": {}, "reference_scope": "Independent current-frame same-renderer grid; not physical truth"}
    sources = []
    for arm in arms:
        rows = [r["state"] for r in states if r["arm"] == arm]
        assert len(rows) == 256 and all(v["anchorMaxAge"] == 128 and v["preFilter"] == arm.startswith("candidate")
            and v["order"] == 8 and v["batch"] == 32 and v["samples"] == 64 and v["seed"] == 0 and v["guide"]
            and not v["restir"] and v["atrous"] == 4 and v["sun"] == [0, 0, 0] and v["sky"] == 0 for v in rows)
        source = Path(f[arm, first, "radiosity"]["path"]).read_bytes()
        assert source == Path(f[arm, last, "radiosity"]["path"]).read_bytes()
        sources.append(source); report["arms"][arm] = {"source_sha256": hashlib.sha256(source).hexdigest()}
    assert sources and all(v == sources[0] for v in sources)
    if "baseline-b" in arms:
        qa = json.loads((root / "qualification.json").read_text(encoding="utf-8"))
        assert qa["files"] > 0 and not qa["mismatch"]; report["aa"] = qa
    if "candidate" in arms:
        a, b = "baseline-b", "candidate"
        for tag in ["geometry", "raw", "anchor", "statistics"]:
            assert all(np.array_equal(read(f[a, i, tag]), read(f[b, i, tag])) for i in frames), "Upstream differs: "+tag
        report["static_upstream_exact"] = True
        g = read(f[a, first, "geometry"]); masks = plane_masks(g)
        target, target_variance, reference_blocks = grid_reference(reference, sources[0], g)
        normal = normal_from_packed(np.maximum(g[:68, :120, 3], 0)).reshape(1, -1, 3)
        grids = []
        sh_values = []
        for arm in [a, b]:
            sh = stack(arm, "sh")[:, :68].reshape(256, 68, 7, 120, 4).transpose(0, 2, 1, 3, 4).reshape(256, 7, -1, 4)
            sh_values.append(sh)
            rgb = evaluate_sh(sh, np.broadcast_to(normal, (256, *normal.shape[1:])))
            grids.append((rgb*sh[:, 6, :, 3:4]).reshape(256, 68, 120, 3))
        unchanged = np.ones((7, 4), bool); unchanged[:3, 3] = False
        assert np.array_equal(sh_values[0].transpose(0, 2, 1, 3)[..., unchanged], sh_values[1].transpose(0, 2, 1, 3)[..., unchanged]), "Directional shape or trace coverage changed"
        report["only_constant_sh_coefficients_changed"] = True
        if m.get("geometryGuard"):
            protected = protected_probes(g).ravel()
            assert np.array_equal(sh_values[0][:, :, protected], sh_values[1][:, :, protected]), "Protected probes changed"
            changed = np.any(sh_values[0] != sh_values[1], axis=(0, 1, 3))
            assert np.any(changed & ~protected), "Prefilter did not affect any unprotected probe"
            report["guard"] = {"protected_probes": int(protected.sum()), "protected_frames": 256,
                "protected_sh_exact": True, "changed_unprotected_probes": int(np.count_nonzero(changed & ~protected))}
        for name, mask in masks.items():
            result = paired(*[(v@LUMA)[:, mask] for v in grids]); result["reference"] = {}
            for arm, value in zip([a, b], grids):
                difference = ((value-target)@LUMA)[:, mask]
                result["reference"][arm] = {"per_frame_rmse": float(np.sqrt(np.mean(difference**2))),
                    "mean_field_rmse": float(np.sqrt(np.mean(difference.mean(0)**2))),
                    "mean_error_percent": float(100*difference.mean()/(target@LUMA)[mask].mean()),
                    "reference_se_rms": float(np.sqrt(target_variance[mask].mean()))}
            # Difference of squared errors is linear in the independent
            # reference mean; cancel its common squared term before resampling.
            y0, y1 = [(v@LUMA)[:, mask] for v in grids]
            square_change = (y1*y1-y0*y0).mean(0)
            mean_change = (y1-y0).mean(0)
            mse_blocks = (square_change[None, :] - 2*(reference_blocks@LUMA)[:, mask]*mean_change).mean(1)
            draws = np.random.default_rng(20261114).integers(0, len(mse_blocks), (4096, len(mse_blocks)))
            result["reference_mse_change"] = {"absolute": float(mse_blocks.mean()),
                "conditional_reference_ci95": np.quantile(mse_blocks[draws].mean(1), [.025, .975]).tolist(),
                "scope": "Independent-reference block uncertainty only; fixed captured candidates"}
            report["grid"][name] = result
        for region, label in [("left", 4), ("right", 5), ("back", 3), ("floor", 1), ("ceiling", 2)]:
            meta = f[a, first, "final-"+region]; x, y, w, h = [meta[k] for k in ["x", "y", "width", "height"]]
            mask = ids[y:y+h, x:x+w] == label
            report["static"][region] = {}
            for stage in ["resolved", "temporal", "final"]:
                values = [stack(arm, stage+"-"+region) for arm in [a, b]]
                coverage_delta = values[1][..., 3]-values[0][..., 3]
                # Resolve coverage is an input contract. Pixel temporal age is
                # also driven by coherent luminance changes, so its smoothed
                # coverage can differ after an RGB-only upstream improvement.
                if stage == "resolved":
                    assert not np.any(coverage_delta), "Current Resolve coverage changed"
                assert np.array_equal(values[0][..., 3] > 0, values[1][..., 3] > 0), "Covered receivers were lost or invented"
                rgb = [v[..., :3]*abs(v[..., 3:4]) for v in values]
                row = paired(*[(v@LUMA)[:, mask] for v in rgb])
                means = [v[:, mask].mean((0, 1), dtype=float) for v in rgb]
                row["mean_rgb_change_percent"] = (100*(means[1]-means[0])/np.maximum(abs(means[0]), 1e-8)).tolist()
                row["before"] = metrics((rgb[0]@LUMA)[:, mask]); row["after"] = metrics((rgb[1]@LUMA)[:, mask])
                row["coverage"] = {"exact": not bool(np.any(coverage_delta)), "positive_mask_exact": True,
                    "changed_samples": int(np.count_nonzero(coverage_delta)), "max_absolute_change": float(abs(coverage_delta).max()),
                    "mean_absolute_change": float(abs(coverage_delta).mean())}
                report["static"][region][stage] = row
        for region in ["short-contact", "tall-contact", "wall-corner"]:
            meta = f[a, first, "final-"+region]; x, y, w, h = [meta[k] for k in ["x", "y", "width", "height"]]
            labels = ids[y:y+h, x:x+w]; values = [stack(arm, "final-"+region) for arm in [a, b]]
            coverage_delta = values[1][..., 3]-values[0][..., 3]
            assert np.array_equal(values[0][..., 3] > 0, values[1][..., 3] > 0)
            rgb = [v[..., :3]*abs(v[..., 3:4]) for v in values]
            report["contacts"][region] = {"coverage_exact": not bool(np.any(coverage_delta)), "positive_mask_exact": True,
                "coverage_changed_samples": int(np.count_nonzero(coverage_delta)),
                "max_absolute_coverage_change": float(abs(coverage_delta).max()), "objects": {}}
            for label in np.unique(labels):
                mask = labels == label
                if label == 0 or mask.sum() < 8:
                    continue
                row = paired(*[(v@LUMA)[:, mask] for v in rgb])
                means = [v[:, mask].mean((0, 1), dtype=float) for v in rgb]
                row["mean_rgb_change_percent"] = (100*(means[1]-means[0])/np.maximum(abs(means[0]), 1e-8)).tolist()
                report["contacts"][region]["objects"][str(int(label))] = row
    if "candidate-motion" in arms:
        a, b = "baseline-motion", "candidate-motion"
        for i in frames:
            assert np.array_equal(matrices[a, i], matrices[b, i])
            for tag in ["geometry", "raw", "anchor", "statistics"]:
                if not np.array_equal(read(f[a, i, tag]), read(f[b, i, tag])):
                    report["motion_qualification"] = {"exact": False, "first_mismatch_frame": i, "tag": tag,
                        "motion_quality_accepted": False}
                    (root / "invalid-input-analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
                    raise AssertionError("Motion upstream differs: "+tag)
        cycles = []
        for arm in [a, b]:
            report["motion"][arm], cycle = motion(arm, f, matrices, first); cycles.append(cycle)
        before, after = [np.mean(v**2, axis=(1, 2)).reshape(10, 8).mean(1) for v in cycles]
        draws = np.random.default_rng(20261114).integers(0, 10, (4096, 10))
        report["motion_comparison"] = {"cycle_rms_change_percent": float(100*(np.sqrt(after.mean()/before.mean())-1)),
            "conditional_block8_ci95": np.quantile(100*(np.sqrt(after[draws].mean(1)/before[draws].mean(1))-1), [.025, .975]).tolist(),
            "scope": "Matched-pose consistency, not independent dynamic truth"}
        report["motion_upstream_exact"] = True
    (root / ("partial-analysis.json" if partial else "analysis.json")).write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"arms": arms, "static": {k: v["final"] for k, v in report["static"].items()},
        "contacts": {k: {label: v["mean_change_percent"] for label, v in row["objects"].items()} for k, row in report["contacts"].items()},
        "motion": report["motion"], "motion_comparison": report.get("motion_comparison")}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path); parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--linear", type=Path, required=True); parser.add_argument("--partial", action="store_true")
    args = parser.parse_args(); analyze(args.capture, args.reference, args.linear, args.partial)
