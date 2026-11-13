"""Compare measured emissive lighting steps through native pixel history."""
import argparse
import json
from pathlib import Path

import numpy as np

from LinearGiExport import exr_read
from ProbeAnchorCalibration import LUMA
from TemporalSamplingExperiment import read


def analyze(root, linear):
    manifest = json.loads((root / "capture.json").read_text())
    assert not manifest["errors"] and manifest["spacing"] == 16 and manifest["rays"] == 8
    assert not manifest["scene_dirty_before"] and not manifest["scene_dirty_after"]
    files = {(r["arm"], r["frame"], r["tag"]): r for r in manifest["records"] if "tag" in r}
    source = {(r["arm"], r["frame"]): r["emission"] for r in manifest["records"] if "emission" in r}
    frames = list(range(512, 768))
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    report = {"scope": "pure emissive global scaling, same camera and sample phase; not all dynamic lighting", "arms": {}}
    curves = {}
    for change, factor in [("up", 4), ("down", .25), ("off", 0)]:
        emission_arms, step_frames = [], []
        for mode in ["legacy", "corrected"]:
            arm = mode + "-light-" + change
            emission = np.asarray([source[arm, f] for f in frames])
            emission_arms.append(emission)
            assert np.allclose(emission[:32], 200, rtol=1e-5)
            changed = np.flatnonzero(np.abs(emission[:, 0] - 200) > 1)
            assert len(changed) and changed[0] in [32, 33]
            step = int(changed[0])
            step_frames.append(step)
            assert np.allclose(emission[step:], 200 * factor, rtol=1e-5, atol=1e-5)
            report["arms"][arm] = {"measured_source_step_frame": step, "regions": {}}
            for region, object_id in [("ceiling", 2), ("floor", 1), ("right", 5)]:
                m = files[arm, frames[0], "resolved-" + region]
                mask = ids[m["y"]:m["y"]+m["height"], m["x"]:m["x"]+m["width"]] == object_id
                stages = {}
                for stage in ["resolved", "pixel-temporal", "final"]:
                    means = []
                    for f in frames:
                        rgba = read(files[arm, f, stage + "-" + region])
                        y = (rgba[..., :3] * rgba[..., 3:4]) @ LUMA
                        means.append(float(y[mask].mean()))
                    stages[stage] = np.asarray(means)
                before = stages["final"][:32].mean()
                target = stages["resolved"][-64:].mean()
                after = stages["final"][step:]
                response = (after-before) / (target-before)
                reached = [i for i in range(len(response)-3) if np.all(response[i:i+4] >= .9)]
                values = {"first_frame_response_fraction": float(response[0]),
                          "frames_to_90_percent_for_four_frames": reached[0] if reached else None,
                          "first32_response_overshoot_fraction": float(max(0, response[:32].max()-1)),
                          "last64_resolve_mean": float(target), "last64_final_mean": float(stages["final"][-64:].mean())}
                if factor == 0:
                    values["off_max_residue_fraction"] = float(after.max()/before)
                else:
                    values["last64_temporal_bias_percent"] = float(100*(stages["pixel-temporal"][-64:].mean()/target-1))
                report["arms"][arm]["regions"][region] = values
                curves[arm, region] = stages["final"] / max(before, 1e-8)
        assert step_frames[0] == step_frames[1], "A/B source step timing differs"
        assert np.array_equal(emission_arms[0][step_frames[0]:], emission_arms[1][step_frames[1]:]), "A/B changed inputs differ"
        # Initial unity-color gamma roundtrips can differ by a few float32 ULPs.
        # Preserve and bound that difference instead of claiming bitwise equivalence.
        assert np.allclose(*emission_arms, rtol=1e-6, atol=1e-5), "A/B measured emission inputs differ materially"
        report.setdefault("input_pre_step_maximum_difference", {})[change] = float(np.max(np.abs(emission_arms[0]-emission_arms[1])))
    (root.parent / "temporal-relight-analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(3, 3, figsize=(11, 8), constrained_layout=True)
    for row, region in enumerate(["ceiling", "floor", "right"]):
        for col, change in enumerate(["up", "down", "off"]):
            ax = axes[row, col]
            for mode in ["legacy", "corrected"]:
                ax.plot(np.arange(256)-32, curves[mode+"-light-"+change, region], label=mode, linewidth=.9)
            ax.set_title(region + " / " + change); ax.set_xlim(-8, 80); ax.grid(alpha=.2)
    axes[0, 0].legend(); fig.savefig(root.parent / "temporal-relight.png", dpi=150); plt.close(fig)
    print(json.dumps(report["arms"], indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--linear", type=Path, default=Path("E:/VividRP_Reborn/Logs/LinearGI-20261113/linear"))
    args = parser.parse_args(); analyze(args.capture, args.linear)
