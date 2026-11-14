"""Plot measured native GI time noise, without synthesizing a final scene image."""
import argparse
import json
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

from LinearGiExport import exr_read
from ScreenProbeAnchorStorageExperiment import read
from ScreenProbeAnchorWindowExperiment import FRAMES, LUMA


def plot(root, linear):
    report = json.loads((root / "analysis.json").read_text())
    assert report["qualifiedInputs"]
    manifest = json.loads((root / "capture.json").read_text())
    assert manifest["complete"] and not manifest["errors"]
    rows = {(r["arm"], r["frame"], r["tag"]): r for r in manifest["records"] if "tag" in r}
    arms = ("half128-b", "float128", "float512")
    roi = rows[arms[0], FRAMES[0], "final-ceiling"]
    x, y, w, h = [roi[k] for k in ("x", "y", "width", "height")]
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    mask = ids[y:y+h, x:x+w] == 2
    assert mask.sum() > 8
    signals, rms = {}, {}
    for arm in arms:
        values = np.stack([read(rows[arm, f, "final-ceiling"]) for f in FRAMES])
        signal = (values[..., :3] * abs(values[..., 3:4])) @ LUMA
        signals[arm] = signal
        rms[arm] = np.sqrt(np.mean((signal-signal.mean(0))**2, axis=0))
    figure = plt.figure(figsize=(13, 7), layout="constrained")
    grid = figure.add_gridspec(2, 3, height_ratios=(1, 1.4))
    vmax = float(np.quantile(rms[arms[0]][mask], .99))
    top_axes = []
    for i, (arm, title) in enumerate(zip(arms, ("F16 / 128", "F32 / 128", "F32 / 512"))):
        ax = figure.add_subplot(grid[0, i]); top_axes.append(ax)
        chart = ax.imshow(np.ma.array(rms[arm], mask=~mask), vmin=0, vmax=vmax, cmap="magma")
        ax.set_title(title + " - ceiling temporal RMS")
        ax.set_xticks([]); ax.set_yticks([])
    figure.colorbar(chart, ax=top_axes, shrink=.85, label="Linear Y E/pi (shared scale)")
    ax = figure.add_subplot(grid[1, :2])
    baseline = signals[arms[0]][:, mask].mean()
    for arm, title in zip(arms, ("F16 / 128", "F32 / 128", "F32 / 512")):
        mean = signals[arm][:, mask].mean(1)
        ax.plot(np.array(FRAMES)-4096, 100*(mean/baseline-1), label=title, linewidth=1.1)
    ax.set_xlabel("Rendered frames after warm-up (stored every 9 frames)")
    ax.set_ylabel("Ceiling ROI mean Y vs baseline window mean (%)")
    ax.grid(alpha=.25); ax.legend()
    ax = figure.add_subplot(grid[1, 2])
    planes = ("left", "right", "back", "floor", "ceiling")
    reduction = [report["pixels"][p]["final"]["float512VsBaseline"]["variance_reduction_percent"] for p in planes]
    bars = ax.barh(planes, reduction)
    ax.bar_label(bars, fmt="%.1f%%", padding=3)
    ax.set_xlim(0, 100); ax.set_xlabel("Final Y variance reduction (%)")
    ax.set_title("F32/512 relative to F16/128")
    figure.suptitle("Cornell native Screen Probe Gather: 16 px / 8 rays, frozen 8/64/32 Radiosity\n"
        "4096 warm-up + 2304 observed renders; GI signal only, not a final SceneColor screenshot")
    target = root / "window-overview.png"
    assert not target.exists(), "Refusing to overwrite an existing figure"
    figure.savefig(target, dpi=150); plt.close(figure)
    print(target)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--linear", type=Path, default=Path("E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113/linear"))
    args = parser.parse_args()
    plot(args.root, args.linear)
