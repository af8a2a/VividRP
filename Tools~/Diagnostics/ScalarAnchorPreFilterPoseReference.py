"""Compare the prefilter output to independent matched-pose raw Resolve references."""
import argparse
import json
from pathlib import Path

import numpy as np

from LinearGiExport import exr_read
from ScalarAnchorPoseReferenceExperiment import error, mean_reference, recent_exposure
from TemporalSamplingExperiment import LUMA, read


def analyze(root, reference, linear):
    m = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    r = json.loads((reference / "capture.json").read_text(encoding="utf-8"))
    assert m["complete"] and r["complete"] and not m["errors"] and not r["errors"]
    assert m["schema"] == "scalar-anchor-prefilter.v1" and r["schema"] == "scalar-anchor-pose-reference-combined.v1"
    assert r["isComposite"] and len(r["source_runs"]) == 2 and r["matched_production_files"] == 2835
    first = m["warmup"]
    files = {(v["arm"], v["frame"], v["tag"]): v for v in m["records"] if "tag" in v}
    rf = {(v["arm"], v["frame"], v["tag"]): v for v in r["records"] if "tag" in v}
    matrices = {(v["arm"], v["frame"]): np.asarray(v["vp"]).reshape(4, 4) for v in m["records"] if "vp" in v}
    rm = {(v["arm"], v["frame"]): np.asarray(v["vp"]).reshape(4, 4) for v in r["records"] if "vp" in v}
    source = Path(files["baseline-b", first, "radiosity"]["path"]).read_bytes()
    for i in range(first, first+256):
        for tag in ["geometry", "raw", "anchor", "statistics"]:
            assert np.array_equal(read(files["baseline-motion", i, tag]), read(files["candidate-motion", i, tag])), "Unmatched motion input: "+tag
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    report = {"scope": "Independent uniform raw Resolve, same renderer, before pixel denoising; includes its original one-pass probe reconstruction",
              "limits": "Not a physical truth; also use independent unfiltered grid reference and preserve contact regions", "static": {}, "poses": {}}
    pooled = {a: [] for a in ["baseline-motion", "candidate-motion"]}
    pooled_values = {a: [] for a in pooled}; pooled_blocks = []
    for point in [0, 24, 48, 80, 104]:
        pose = "pose%03d" % point
        arm = "baseline-b" if point == 0 else "baseline-motion"
        assert all(np.array_equal(rm[pose, i], matrices[arm, first+point]) for i in range(512, 1536))
        for i in [512, 1535]:
            assert Path(rf[pose, i, "radiosity"]["path"]).read_bytes() == source
            assert np.array_equal(read(rf[pose, i, "geometry"]), read(files[arm, first+point, "geometry"]))
        if point == 0:
            for region, label in [("left", 4), ("right", 5), ("back", 3), ("floor", 1), ("ceiling", 2)]:
                target, variance, blocks = mean_reference(rf, pose, "resolved-"+region)
                meta = files[arm, first, "final-"+region]; x, y, w, h = [meta[k] for k in ["x", "y", "width", "height"]]
                mask = ids[y:y+h, x:x+w] == label
                report["static"][region] = {}
                for candidate in ["baseline-b", "candidate"]:
                    values = np.stack([read(files[candidate, i, "final-"+region]) for i in range(first, first+256)])
                    rgb = values[..., :3]*abs(values[..., 3:4]); average = rgb.mean(0, dtype=float)
                    row = error(average, target, variance, mask)
                    ref_rgb, value_rgb = target[mask].mean(0), average[mask].mean(0)
                    row.update(mean_energy_error_percent=float(100*((value_rgb@LUMA)/(ref_rgb@LUMA)-1)),
                        mean_rgb_error_percent=(100*(value_rgb-ref_rgb)/np.maximum(abs(ref_rgb), 1e-8)).tolist(),
                        per_frame_rmse=float(np.sqrt(np.mean((((rgb-target)@LUMA)[:, mask])**2))))
                    report["static"][region][candidate] = row
            continue
        target, variance, blocks = mean_reference(rf, pose, "resolved-motion")
        recent = recent_exposure(files, matrices, point, "baseline-motion", first)
        assert recent.sum() > 0
        packed = read(files["baseline-motion", first+point, "pixel-geometry-motion"])[..., 0]
        visible = (packed & 65535) != 0
        report["poses"][str(point)] = {"recently_exposed_pixels": int(recent.sum()), "candidates": {}}
        pooled_blocks.append((blocks@LUMA)[:, recent])
        for candidate in pooled:
            value = read(files[candidate, first+point, "final-motion"]); value = value[..., :3]*abs(value[..., 3:4])
            report["poses"][str(point)]["candidates"][candidate] = {"whole_roi": error(value, target, variance, visible),
                "recent_exposure": error(value, target, variance, recent)}
            pooled[candidate].extend(((value-target)@LUMA)[recent].tolist())
            pooled_values[candidate].append((value@LUMA)[recent])
    report["pooled_recent_exposure"] = {a: {"pixels": len(v), "rmse": float(np.sqrt(np.mean(np.asarray(v)**2))),
        "absolute_p99": float(np.quantile(abs(np.asarray(v)), .99)), "absolute_max": float(max(abs(np.asarray(v)))),
        "signed_mean": float(np.mean(v))} for a, v in pooled.items()}
    block_y = np.concatenate(pooled_blocks, axis=1)
    weights = np.random.default_rng(20261114).multinomial(16, [1/16]*16, 4096)/16
    targets = weights@block_y
    errors = [np.concatenate(v)[None, :]-targets for v in pooled_values.values()]
    rms = [np.sqrt(np.mean(v**2, axis=1)) for v in errors]
    p99 = [np.quantile(abs(v), .99, axis=1) for v in errors]
    report["reference_uncertainty_comparison"] = {"rms_change_percent_conditional_ci95": np.quantile(100*(rms[1]/rms[0]-1), [.025, .975]).tolist(),
        "p99_change_percent_conditional_ci95": np.quantile(100*(p99[1]/p99[0]-1), [.025, .975]).tolist(),
        "scope": "Reference block uncertainty only; fixed captured candidate frames"}
    (root / "pose-reference-analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"static_energy": {k: {a: v["mean_energy_error_percent"] for a, v in row.items()} for k, row in report["static"].items()},
        "pooled_recent_exposure": report["pooled_recent_exposure"], "reference_uncertainty_comparison": report["reference_uncertainty_comparison"]}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path); parser.add_argument("reference", type=Path); parser.add_argument("--linear", type=Path, required=True)
    args = parser.parse_args(); analyze(args.capture, args.reference, args.linear)
