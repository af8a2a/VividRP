"""Verify deadband, non-starvation, card compatibility and bounded camera-motion budgets."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import source_cards
from ProbeAnchorCalibration import LUMA


def analyze(capture, output):
    output.mkdir(parents=True, exist_ok=True)
    rows = list(csv.DictReader((capture / "frames.csv").open(encoding="utf-8-sig")))
    arms = {name: sorted((r for r in rows if r["arm"] == name), key=lambda r: int(r["frame"]))
            for name in sorted({r["arm"] for r in rows})}
    report = {}
    for arm, records in arms.items():
        assert [int(r["frame"]) for r in records] == list(range(len(records)))
        for row in records:
            path = capture / f"{arm}-{int(row['frame']):04}-atlas.f16"
            if path.exists():
                assert np.isfinite(np.fromfile(path, "<f2")).all(), path
        report[arm] = {"frames": len(records), "history_accepted_frames": sum(r["history"] == "True" for r in records),
            "clear_frames": [int(r["frame"]) for r in records if r["clear"] == "True"],
            "layout_versions": sorted({int(r["layout"]) for r in records}),
            "discontinuity_versions": sorted({int(r["discontinuity"]) for r in records}),
            "scene_revisions": len({r["revision"] for r in records}),
            "working_order_zero_frames": sum(r["order"] == "0" for r in records),
            "published_order_zero_frames": sum(r["publishedOrder"] == "0" for r in records),
            "light_r_range": [min(float(r["lightR"]) for r in records), max(float(r["lightR"]) for r in records)]}
    for arm in ("new-jitter", "new-atmosphere"):
        a = np.fromfile(capture / f"{arm}-0000-atlas.f16", "<u2")
        b = np.fromfile(capture / f"{arm}-0127-atlas.f16", "<u2")
        assert np.array_equal(a, b)
        assert all(r["order"] == "8" and r["updated"] == "0" and r["history"] == "True" for r in arms[arm])
        report[arm]["frozen_atlas_bitwise_equal"] = True
    ramp = arms["new-ramp"]
    energy = np.array([float((source_cards(capture / f"new-ramp-{int(r['frame']):04}-atlas.f16")
                             .astype(float)[..., :3] @ LUMA).mean()) for r in ramp])
    # Controlled fixed samples, stationary non-emissive geometry and a white sun.
    # This is a linear same-solver prediction, not independent physical ground truth.
    scale = np.array([float(r["lightR"]) / float(ramp[0]["lightR"]) for r in ramp])
    error = 100 * (energy / energy[0] / scale - 1)
    complete = [int(r["frame"]) for r in ramp if r["order"] == "7" and int(r["updated"]) > 0
                and int(r["cursor"]) + int(r["updated"]) == int(r["cards"])]
    assert len(complete) >= 10 and all(b - a <= 8 for a, b in zip(complete, complete[1:]))
    assert max(abs(error)) < 1.5
    accepted = np.array([float(r["acceptedR"]) for r in ramp])
    assert np.all(accepted[:6] == accepted[0]) and accepted[6] != accepted[0]
    report["new-ramp"].update({"target_completion_frames": complete,
        "source_mean_error_vs_linear_scaling_min_max_percent": [float(error.min()), float(error.max())],
        "source_mean_error_vs_linear_scaling_average_percent": float(error.mean()),
        "accepted_input_changes": int(np.count_nonzero(np.diff(accepted)))})
    for arm in ("new-geometry", "new-geometry-continuous"):
        result = report[arm]
        assert result["scene_revisions"] > 1 and len(result["layout_versions"]) == len(result["discontinuity_versions"]) == 1
        assert not result["clear_frames"] and result["published_order_zero_frames"] == 0
        assert all(r["publishedOrder"] == "8" for r in arms[arm])
    assert len(report["new-large-geometry"]["discontinuity_versions"]) == 2
    assert len(report["new-large-geometry"]["layout_versions"]) == 1
    assert not report["new-large-geometry"]["clear_frames"]
    assert report["new-topology"]["clear_frames"] == [16, 48]
    assert len(report["new-topology"]["layout_versions"]) == 3
    camera = arms["new-fast-camera"]
    for i in range(96):
        expected_boost = max(0, 8 - (i - 16)) if 16 <= i < 48 else max(0, 8 - (i - 48)) if i >= 48 else 0
        assert int(camera[i]["boost"]) == expected_boost
        remaining = int(camera[i]["cards"]) - int(camera[i]["cursor"])
        assert int(camera[i]["updated"]) == min(remaining, 16 if expected_boost else 4)
    report["new-fast-camera"]["boost_windows"] = [[16, 23], [48, 55]]
    (output / "controls-summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(1, 3, figsize=(16, 4), constrained_layout=True)
    axes[0].plot(error, label="Retain high order")
    axes[0].set(title="Smooth light ramp: 0.1% per frame", xlabel="Frame", ylabel="Source energy vs linear prediction (%)")
    axes[1].plot([float(r["lightR"]) for r in ramp], label="Current sun")
    axes[1].step(range(128), accepted, label="Accepted restart baseline", where="post")
    axes[1].set(title="Deadband does not hide cumulative changes", xlabel="Frame", ylabel="Linear light R")
    axes[2].plot([int(r["updated"]) for r in camera], label="Updated cards")
    axes[2].set(title="Camera motion: bounded 4x budget", xlabel="Frame", ylabel="Cards")
    for axis in axes:
        axis.legend(fontsize=8)
    fig.savefig(output / "controls-convergence.png", dpi=160)
    plt.close(fig)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    analyze(args.capture, args.output)
