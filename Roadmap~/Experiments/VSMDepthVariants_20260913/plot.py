import json
import statistics
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

HERE = Path(__file__).resolve().parent
data = json.loads((HERE / "timing-results.json").read_text())
fig, axes = plt.subplots(1, 3, figsize=(13.8, 4.6))
colors = ["#54768f", "#d89d41", "#a76268", "#487f6b"]
labels = ["Ordered baseline", "Page layer bound", "Last-front reuse", "4-layer interleave"]
groups = ["r4096_p1024_l0_m0", "r4096_p1024_l0_m1", "r4096_p1024_l0_m2", "r4096_p1024_l1_m0"]
for ax, run, marker, title in zip(axes[:2], ("timing", "timing-moving-light"),
        ("VSM.ResolveTrace", "VSM.StaticRaster"), ("Steady view: ResolveTrace", "Moving light: StaticRaster")):
    values = [data[run]["groups"][g][marker] for g in groups]
    centers = [statistics.median(v) for v in values]
    bars = ax.bar(range(4), centers, color=colors)
    ax.bar_label(bars, fmt="%.2f", padding=4, fontsize=10)
    for i, v in enumerate(values):
        ax.plot([i]*len(v), v, "k_", markersize=12)
    ax.set_ylim(0, max(max(v) for v in values)*1.23)
    ax.set_xticks(range(4), ["Baseline", "Bounds", "Reuse", "Interleave"], rotation=18)
    ax.set_ylabel("GPU milliseconds")
    ax.set_title(title, fontsize=12)
ax=axes[2]
values=[]
for run, key in zip(("specialized-0a", "specialized-1", "specialized-2"), groups):
    values.append(data[run]["groups"][key]["VSM.ResolveTrace"])
bars=ax.bar(range(3), [statistics.median(v) for v in values], color=colors[:3])
ax.bar_label(bars, fmt="%.2f", padding=4, fontsize=10)
for i,v in enumerate(values): ax.plot([i]*len(v),v,"k_",markersize=12)
ax.set_xticks(range(3),["Original source", "Bounds only", "Reuse only"],rotation=18)
ax.set_ylim(0,max(max(v) for v in values)*1.23)
ax.set_ylabel("GPU milliseconds")
ax.set_title("Separate shader builds: ResolveTrace",fontsize=12)
for ax in axes:
    ax.grid(axis="y",alpha=.16);ax.set_axisbelow(True);ax.spines[["top","right"]].set_visible(False)
fig.suptitle("4096 VSM: tested candidates did not justify adoption",fontsize=15)
fig.text(.5,-.03,"16 layers / 1024 pages / 4 fixed rays / 1920x1080. Marks: per-window medians, 128 observations each.",ha="center",fontsize=10)
fig.tight_layout(rect=[0,.02,1,.92])
fig.savefig(HERE/"comparison.png",dpi=160,bbox_inches="tight",facecolor="white")
