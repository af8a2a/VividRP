import json
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

root = Path(__file__).resolve().parent
data = json.loads((root / "results.json").read_text())
panels = [
    ("ResolveTrace time", "ms", "timings_ms", "VSM.ResolveTrace", None),
    ("DRAM throughput", "% of sustained peak", "trace_metrics", "dram_throughput_pct", 100),
    ("L1 cache hit rate", "%", "trace_metrics", "l1_hit_pct", 100),
    ("Waiting for L1TEX load results", "% of sampled warp states", "stall_sample_shares_pct", "long_scoreboard_pipe_l1tex", 100),
    ("Effective lanes per instruction", "predicate-on threads / 32", "trace_metrics", "pred_on_threads_per_inst", 32),
    ("SM throughput", "% of sustained peak", "trace_metrics", "sm_throughput_pct", 100),
]
colors = ["#226da8", "#c25827"]
fig, axes = plt.subplots(3, 2, figsize=(11.5, 9.5))
for ax, (title, unit, category, key, limit) in zip(axes.flat, panels):
    for index, resolution in enumerate(("2048", "4096")):
        vals = [data[resolution][category][key]["values"], data[resolution + "_recheck"][category][key]["values"]]
        for run, samples in enumerate(vals):
            x = index + (run - 0.5) * 0.19
            ax.scatter(x + np.linspace(-0.025, 0.025, len(samples)), samples,
                       color=colors[index], marker="o" if run == 0 else "x", s=25, alpha=0.8)
            median = np.median(samples)
            ax.plot([x - 0.05, x + 0.05], [median, median], color=colors[index], lw=2)
        medians = [np.median(v) for v in vals]
        ax.text(index, max(max(v) for v in vals) + (limit or 25) * 0.035,
                f"{min(medians):.1f}–{max(medians):.1f}", ha="center", fontsize=10)
    ax.set_title(title, loc="left", fontsize=12, weight="bold")
    ax.set_ylabel(unit, fontsize=9)
    ax.set_xticks([0, 1], ["2048", "4096"])
    ax.set_xlim(-0.45, 1.45)
    ax.set_ylim(0, limit or 26)
    ax.grid(axis="y", alpha=0.17)
    ax.spines[["top", "right"]].set_visible(False)
fig.suptitle("VSM resolution scaling | Nsight GPU Trace", fontsize=17, weight="bold", y=0.985)
fig.text(0.5, 0.95, "RTX 5070 Ti · DX12 · 1920×1080 · 1024 pages · fixed 4 rays · 16 depth layers", ha="center", fontsize=10)
fig.text(0.5, 0.016, "Each point = one captured frame.  ○ First run   × Repeat run   Labels = range of run medians.\nNsight clocks/instrumentation differ from the uninstrumented Unity performance baseline.", ha="center", fontsize=9)
fig.tight_layout(rect=(0.015, 0.065, 0.99, 0.93), h_pad=2.1)
fig.savefig(root / "comparison.png", dpi=150, facecolor="white")
