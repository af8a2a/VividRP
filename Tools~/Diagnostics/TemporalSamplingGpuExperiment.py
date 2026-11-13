"""Reject unavailable, stale, aggregated or uncalibrated Unity GPU marker data."""
import argparse
import json
from pathlib import Path

import numpy as np


def analyze(root):
    capture = json.loads((root / "capture.json").read_text())
    assert not capture["errors"] and not capture["textureReadbacks"]
    names = ["DirectionalGuideSampler", "ScreenTraceSampler", "GatherSampler", "TotalSampler"]
    report = {"scope": "Unity GPU marker ABBA; one profiler publication per retained sample",
              "all_arms_valid": True, "arms": {}, "pairs": {}}
    arrays = {}
    for arm in ["white-a1", "blocked-b1", "blocked-b2", "white-a2"]:
        rows = [r for r in capture["records"] if r["arm"] == arm]
        assert len(rows) == capture["measuredFrames"]
        # Edit-mode Time.frameCount may stand still while scripted cameras repaint.
        # A GPU profiler publication index is required; wall time cannot substitute.
        by_frame = {r["profilerFrame"]: r for r in rows if r.get("profilerFrame", -1) >= 0}
        scoped = []
        for row in by_frame.values():
            markers = {s["name"]: s for s in row["samples"]}
            if not all(markers[n]["valid"] and markers[n]["count"] == 1
                       and np.isfinite(markers[n]["milliseconds"]) and 0 < markers[n]["milliseconds"] < 1000 for n in names):
                continue
            total = markers["TotalSampler"]["milliseconds"]
            if sum(markers[n]["milliseconds"] for n in names[:3]) > total * 1.05 + .05:
                continue
            scoped.append(row)
        report["arms"][arm] = {"profiler_publications": len(by_frame), "consistent_publications": len(scoped), "markers": {}}
        for name in names:
            samples = [s for r in scoped for s in r["samples"] if s["name"] == name]
            values = np.asarray([s["milliseconds"] for s in samples if s["valid"] and s["count"] == 1
                                 and np.isfinite(s["milliseconds"]) and 0 < s["milliseconds"] < 1000])
            valid = len(values) >= 64 and len(np.unique(values)) >= 8
            report["all_arms_valid"] &= valid
            arrays[arm, name] = values
            report["arms"][arm]["markers"][name] = {"valid": valid, "samples": len(values),
                "median_ms": float(np.median(values)) if valid else None,
                "p95_ms": float(np.quantile(values, .95)) if valid else None}
    if report["all_arms_valid"]:
        for name in names:
            before = np.mean([np.median(arrays[a, name]) for a in ["white-a1", "white-a2"]])
            after = np.mean([np.median(arrays[a, name]) for a in ["blocked-b1", "blocked-b2"]])
            report["pairs"][name] = {"mean_arm_median_before_ms": float(before), "mean_arm_median_after_ms": float(after),
                                     "change_percent": float(100 * (after / before - 1))}
    else:
        report["rejection"] = "GPU recording did not provide enough fresh, calibrated, count=1 samples in every arm. No A/B timing claim."
    (root / "analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    analyze(parser.parse_args().capture)
