"""Compare directional distribution changes, with measured sky readiness kept explicit."""
import argparse
import json
from pathlib import Path

import numpy as np

from LinearGiExport import exr_read
from ProbeAnchorCalibration import LUMA
from TemporalSamplingExperiment import read, verify_blocked_proposals


def analyze(root, linear):
    manifest = json.loads((root / "capture.json").read_text())
    assert manifest["skyShift"] and manifest["blockedComparison"] and not manifest["errors"]
    assert manifest["rays"] == 8 and manifest["spacing"] == 16
    assert not manifest["scene_dirty_before"] and not manifest["scene_dirty_after"]
    frames = list(range(manifest["warmup"], manifest["warmup"] + manifest["measuredFrames"]))
    records = manifest["records"]
    files = {(r["arm"], r["frame"], r["tag"]): r for r in records if "tag" in r}
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    report = {"scope": "emitter off plus constant HDRI on; fixed geometry, current renderer reference",
              "requested_step_frame": 32, "arms": {}, "comparison": {}}
    report["proposals"] = verify_blocked_proposals(manifest, files, frames,
                                                   ["white-light-skyshift", "blocked-light-skyshift"],
                                                   allow_fixed_cosine_resets=True)
    curves, emissions, bounds, matrices = {}, [], [], []
    for mode in ["white", "blocked"]:
        arm = mode + "-light-skyshift"
        rows = [r for r in records if r["arm"] == arm and "emission" in r]
        assert [r["frame"] for r in rows] == frames
        emission = np.asarray([r["emission"] for r in rows])
        bound = np.asarray([r["skyBound"] if r["skyBoundValid"] else 0 for r in rows])
        changed = np.flatnonzero(emission[:, 0] < 1)
        assert len(changed) and changed[0] in [32, 33]
        assert np.allclose(emission[:changed[0]], 200, rtol=1e-5) and np.all(emission[changed[0]:] == 0)
        states = [r["state"] for r in records if r["arm"] == arm and "state" in r]
        sky_distance = np.asarray([s["sky"] for s in states])
        assert len(states) == len(frames)
        assert np.all(sky_distance[:32] == 0) and np.all(sky_distance[32:] == 64)
        # The cached CPU bound can remain valid while the sky is disabled.
        ready = np.flatnonzero((bound > 0) & (sky_distance > 0))
        assert len(ready) and ready[0] >= 32 and np.all(bound[-64:] > 0)
        emissions.append(emission); bounds.append(bound[-64:])
        matrices.append(np.asarray([r["vp"] for r in records if r["arm"] == arm and "vp" in r]))
        for row in (r for r in records if r["arm"] == arm and "state" in r):
            s = row["state"]
            assert s["directionalCache"] and not s["restir"] and s["guide"]
            assert s["samplingFlags"] == (0 if mode == "white" else 8388608)
            assert s["frozenProposal"] == (mode == "blocked")
        summary = {"emission_off_frame": int(changed[0]), "first_positive_cpu_sky_bound_frame": int(ready[0]),
                   "late_sky_bound_mean": float(bound[-64:].mean()), "regions": {}}
        for region, oid in [("ceiling", 2), ("floor", 1), ("right", 5)]:
            metadata = files[arm, frames[0], "final-" + region]
            mask = ids[metadata["y"]:metadata["y"]+metadata["height"], metadata["x"]:metadata["x"]+metadata["width"]] == oid
            stages = {}
            for stage in ["resolved", "pixel-temporal", "final"]:
                stages[stage] = np.asarray([float(((a[..., :3] * a[..., 3:4]) @ LUMA)[mask].mean())
                                           for a in (read(files[arm, f, stage + "-" + region]) for f in frames)])
            before = stages["final"][:32].mean()
            target = stages["resolved"][-64:].mean()
            assert abs(target-before) > .1 * max(before, 1e-5), "Insufficient lighting contrast for a response measurement"
            response = (stages["final"][32:] - before) / (target-before)
            reached = [i for i in range(len(response)-3) if np.all(response[i:i+4] >= .9)]
            summary["regions"][region] = {"before_mean": float(before), "late_resolve_mean": float(target),
                "late_final_mean": float(stages["final"][-64:].mean()),
                "frames_to_90_from_request": reached[0] if reached else None,
                "first32_response_overshoot": float(max(0, response[:32].max()-1)),
                "late_temporal_bias_percent": float(100*(stages["pixel-temporal"][-64:].mean()/target-1))}
            curves[mode, region] = stages["final"] / max(before, 1e-5)
        report["arms"][mode] = summary
    assert np.array_equal(*matrices) and np.allclose(*emissions, rtol=1e-6, atol=1e-5)
    assert np.allclose(*bounds, rtol=1e-5), "Late sky inputs differ"
    report["cpu_sky_ready_frame_delta"] = report["arms"]["blocked"]["first_positive_cpu_sky_bound_frame"] - report["arms"]["white"]["first_positive_cpu_sky_bound_frame"]
    report["source_readiness_matched"] = report["cpu_sky_ready_frame_delta"] == 0
    report["limitation"] = "CPU sky-bound readiness is not GPU texture publication; unequal readiness prevents a clean source-latency A/B"
    for region in ["ceiling", "floor", "right"]:
        a, b = (report["arms"][m]["regions"][region] for m in ["white", "blocked"])
        report["comparison"][region] = {"frames_to_90_delta": b["frames_to_90_from_request"]-a["frames_to_90_from_request"]
            if a["frames_to_90_from_request"] is not None and b["frames_to_90_from_request"] is not None else None,
            "late_final_mean_change_percent": 100*(b["late_final_mean"]/a["late_final_mean"]-1)}
    (root.parent / "skyshift-analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(1, 3, figsize=(12, 3.5), layout="constrained")
    for ax, region in zip(axes, ["ceiling", "floor", "right"]):
        for mode in ["white", "blocked"]:
            ax.plot(np.arange(256)-32, curves[mode, region], label=mode, linewidth=.9)
        ax.axvline(0, color="gray", linewidth=.7); ax.set_xlim(-8, 160); ax.set_title(region); ax.grid(alpha=.2)
    axes[0].legend(); fig.suptitle("Emitter to sky - source readiness reported separately")
    fig.savefig(root.parent / "skyshift-response.png", dpi=150); plt.close(fig)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--linear", type=Path, required=True)
    args = parser.parse_args(); analyze(args.capture, args.linear)
