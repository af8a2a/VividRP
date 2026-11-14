"""Same-input native interpolation-noise A/B after the real temporal/Atrous path."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from LinearGiExport import exr_read
from ScalarAnchorAgeExperiment import motion
from TemporalSamplingExperiment import LUMA, paired, read


def analyze(root, linear, static_only=False):
    capture = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    assert capture["complete"] and not capture["errors"] and capture["schema"] == "screen-probe-stbn.v1"
    assert (capture["warmup"], capture["measuredFrames"], capture["spacing"], capture["rays"]) == (1024, 256, 16, 8)
    qualification = json.loads((root / "qualification.json").read_text(encoding="utf-8"))
    assert qualification["files"] > 0 and not qualification["mismatch"]
    frames = range(1024, 1280)
    files = {(r["arm"], r["frame"], r["tag"]): r for r in capture["records"] if "tag" in r}
    matrices = {(r["arm"], r["frame"]): np.asarray(r["vp"]).reshape(4, 4) for r in capture["records"] if "vp" in r}
    arms = ["baseline-a", "baseline-b", "candidate", "baseline-motion", "candidate-motion"]
    sources = []
    for arm in arms:
        states = [r for r in capture["records"] if r.get("arm") == arm and "state" in r]
        assert [r["frame"] for r in states] == list(frames)
        assert all(r["state"]["preFilter"] and r["state"]["blueNoise"] == arm.startswith("candidate")
                   and r["state"]["anchorMaxAge"] == 128 and r["state"]["order"] == 8 and r["state"]["batch"] == 32
                   and r["state"]["samples"] == 64 and r["state"]["seed"] == 0 and r["state"]["guide"]
                   and not r["state"]["restir"] and r["state"]["atrous"] == 4
                   and r["state"]["sun"] == [0, 0, 0] and r["state"]["sky"] == 0 for r in states)
        source = Path(files[arm, 1024, "radiosity"]["path"]).read_bytes()
        assert source == Path(files[arm, 1279, "radiosity"]["path"]).read_bytes()
        sources.append(source)
    assert all(s == sources[0] for s in sources)
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    report = dict(complete=True, scope="Consumed linear E/pi, white versus STBN interpolation; same 16px/8-ray probes",
                  aa=qualification, sourceSha256=hashlib.sha256(sources[0]).hexdigest(), static={}, contacts={}, motion={})
    pairs = [("baseline-b", "candidate")] if static_only else [("baseline-b", "candidate"), ("baseline-motion", "candidate-motion")]
    for a, b in pairs:
        for i in frames:
            assert np.array_equal(matrices[a, i], matrices[b, i])
            for tag in ["geometry", "raw", "anchor", "statistics", "sh", "reconstructed"]:
                if not np.array_equal(read(files[a, i, tag]), read(files[b, i, tag])):
                    report["complete"] = False
                    report["input_qualification"] = dict(qualified=False, arms=[a, b], frame=i, tag=tag)
                    (root / "invalid-input-analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
                    raise AssertionError("Probe input changed: " + tag)
    report["probe_inputs_exact"] = True
    stack = lambda arm, tag: np.stack([read(files[arm, i, tag]) for i in frames])
    a, b = "baseline-b", "candidate"
    for region, label in [("left", 4), ("right", 5), ("back", 3), ("floor", 1), ("ceiling", 2)]:
        meta = files[a, 1024, "final-" + region]
        x, y, w, h = [meta[k] for k in ["x", "y", "width", "height"]]
        mask = ids[y:y+h, x:x+w] == label
        report["static"][region] = {}
        for stage in ["resolved", "temporal", "final"]:
            values = [stack(arm, stage + "-" + region) for arm in [a, b]]
            rgb = [v[..., :3]*abs(v[..., 3:4]) for v in values]
            row = paired(*[(v @ LUMA)[:, mask] for v in rgb])
            means = [v[:, mask].mean((0, 1), dtype=float) for v in rgb]
            row["mean_rgb_change_percent"] = (100*(means[1]-means[0])/np.maximum(abs(means[0]), 1e-8)).tolist()
            row["coverage"] = dict(newly_uncovered_samples=int(np.count_nonzero((values[0][..., 3][:, mask] > 0) & (values[1][..., 3][:, mask] == 0))),
                                   mean_before=float(values[0][..., 3][:, mask].mean()), mean_after=float(values[1][..., 3][:, mask].mean()))
            report["static"][region][stage] = row
    for region in ["short-contact", "tall-contact", "wall-corner"]:
        meta = files[a, 1024, "final-" + region]
        x, y, w, h = [meta[k] for k in ["x", "y", "width", "height"]]
        labels = ids[y:y+h, x:x+w]
        values = [stack(arm, "final-" + region) for arm in [a, b]]
        rgb = [v[..., :3]*abs(v[..., 3:4]) for v in values]
        report["contacts"][region] = {}
        for label in np.unique(labels):
            mask = labels == label
            if label == 0 or mask.sum() < 8:
                continue
            row = paired(*[(v @ LUMA)[:, mask] for v in rgb])
            means = [v[:, mask].mean((0, 1), dtype=float) for v in rgb]
            row["mean_rgb_change_percent"] = (100*(means[1]-means[0])/np.maximum(abs(means[0]), 1e-8)).tolist()
            report["contacts"][region][str(int(label))] = row
    if static_only:
        report.update(scope="Qualified static arms only; motion input comparison failed in the full analysis",
                      overallAcceptance=False, motionQualified=False)
        (root / "static-qualified-analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(json.dumps({r: {k: row["final"][k] for k in ["variance_reduction_percent", "mean_change_percent"]}
                          for r, row in report["static"].items()}, indent=2))
        return
    cycles = []
    for arm in ["baseline-motion", "candidate-motion"]:
        report["motion"][arm], cycle = motion(arm, files, matrices, 1024)
        cycles.append(cycle)
    before, after = [np.mean(v*v, axis=(1, 2)).reshape(10, 8).mean(1) for v in cycles]
    draws = np.random.default_rng(20261114).integers(0, 10, (4096, 10))
    report["motion_comparison"] = dict(cycle_rms_change_percent=float(100*(np.sqrt(after.mean()/before.mean())-1)),
        conditional_ci95=np.quantile(100*(np.sqrt(after[draws].mean(1)/before[draws].mean(1))-1), [.025, .975]).tolist(),
        limitation="The 128-frame trajectory repeats the 64-frame STBN phase; require a noncommensurate trajectory and independent pose errors before acceptance")
    (root / "analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(dict(static={r: {k: row["final"][k] for k in ["variance_reduction_percent", "mean_change_percent", "block_bootstrap_variance_reduction_95_percent"]}
                                  for r, row in report["static"].items()}, motion=report["motion"], motion_comparison=report["motion_comparison"]), indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--linear", type=Path, required=True)
    parser.add_argument("--static-only", action="store_true", help="Save separately qualified static evidence without accepting motion")
    args = parser.parse_args()
    analyze(args.capture, args.linear, args.static_only)
