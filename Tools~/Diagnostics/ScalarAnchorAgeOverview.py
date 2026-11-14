"""Render a common-scale scalar-anchor GI comparison and temporal deviation maps."""
import argparse
import json
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

from TemporalSamplingExperiment import LUMA, read


def draw(root):
    capture = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    assert capture["complete"] and not capture["errors"]
    files = {(r["arm"], r["frame"], r["tag"]): r for r in capture["records"] if "tag" in r}
    arms = ["age32-b", "age128"]
    fig, axes = plt.subplots(3, 2, figsize=(14, 10), constrained_layout=True)
    for col, arm in enumerate(arms):
        full = read(files[arm, 767, "full-final"])
        rgb = full[..., :3] * abs(full[..., 3:4]) / 20.0
        image = np.where(rgb <= .0031308, 12.92 * rgb, 1.055 * np.maximum(rgb, 0) ** (1 / 2.4) - .055)
        axes[0, col].imshow(np.clip(image, 0, 1)); axes[0, col].set_title(arm + ": frame 767")
        for row, region in enumerate(["ceiling", "right"], start=1):
            values = np.stack([read(files[arm, f, "final-" + region]) for f in range(512, 768)])
            y = (values[..., :3] * abs(values[..., 3:4])) @ LUMA
            shown = axes[row, col].imshow(y.std(0), cmap="magma", vmin=0, vmax=.05 if region == "ceiling" else .6)
            axes[row, col].set_title(region + ": temporal standard deviation")
            fig.colorbar(shown, ax=axes[row, col], shrink=.7, label="Linear Y")
    for axis in axes.flat:
        axis.set_xticks([]); axis.set_yticks([])
    fig.suptitle("Native Screen Probe, 16 px / 8 rays, frozen 8-order Radiosity\n"
                 "Identical display: sRGB(RGB / 20), clipped. Temporal maps share each row's scale.")
    target = root / "scalar-anchor-overview.png"
    fig.savefig(target, dpi=150); plt.close(fig)
    print(target)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    draw(parser.parse_args().capture)
