"""Render measured timing and Nsight cache / dependency changes."""
import json
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

HERE = Path(__file__).resolve().parent
timing = json.loads((HERE / "timing-results.json").read_text())
old = json.loads((HERE.parent / "VSMNsight_20260913/results.json").read_text())
new = json.loads((HERE / "nsight-results.json").read_text())
fig, axes = plt.subplots(1, 3, figsize=(13.5, 4.4))
colors = ["#a9b5c5", "#187a69"]
positions = [0, 1]
series = [
    ([timing[7]["VSM.ResolveTrace"]["median_ms"], timing[5]["VSM.ResolveTrace"]["median_ms"]],
     [timing[6]["VSM.ResolveTrace"]["median_ms"], timing[4]["VSM.ResolveTrace"]["median_ms"]]),
    ([old[f"{r}_recheck"]["trace_metrics"]["l1_hit_pct"]["median"] for r in (2048, 4096)],
     [new[str(r)]["trace_metrics"]["l1_hit_pct"]["median"] for r in (2048, 4096)]),
    ([old[f"{r}_recheck"]["stall_sample_shares_pct"]["long_scoreboard_pipe_l1tex"]["median"] for r in (2048, 4096)],
     [new[str(r)]["stall_sample_shares_pct"]["long_scoreboard_pipe_l1tex"]["median"] for r in (2048, 4096)])]
for ax, (before, after), title, unit in zip(axes, series,
        ("ResolveTrace | uninstrumented", "L1 hit rate | Nsight", "Long scoreboard | Nsight"),
        ("GPU time (ms), lower is better", "Percent, higher is better", "Share of sampled warp states (%)")):
    for values, offset, color, label in zip((before, after), (-.19, .19), colors, ("Linear scan", "Ordered search")):
        bars = ax.bar([p + offset for p in positions], values, .36, color=color, label=label)
        ax.bar_label(bars, fmt="%.1f", padding=3, fontsize=10)
    ax.set_title(title, fontsize=12, pad=12)
    ax.set_ylabel(unit, fontsize=10)
    ax.set_xticks(positions, ["2048", "4096"])
    ax.set_ylim(0, max(before + after) * 1.22)
    ax.grid(axis="y", alpha=.17)
    ax.set_axisbelow(True)
    ax.spines[["top", "right"]].set_visible(False)
handles, labels = axes[0].get_legend_handles_labels()
fig.legend(handles, labels, ncol=2, frameon=False, loc="upper center", bbox_to_anchor=(.5, .94))
fig.suptitle("VSM depth search: 16 layers / 1024 pages / 4 fixed rays", fontsize=15, y=1.015)
fig.text(.5, -.035, "RTX 5070 Ti, 1920x1080. Timing: reverse A/B windows (128 observations each). Nsight: 4 frames per run.", ha="center", fontsize=10)
fig.tight_layout(rect=[0, .02, 1, .91])
fig.savefig(HERE / "comparison.png", dpi=160, bbox_inches="tight", facecolor="white")
