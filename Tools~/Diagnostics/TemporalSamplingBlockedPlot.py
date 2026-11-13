"""Plot measured noise reduction and the separate energy-equivalence gate."""
import argparse
import json
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np


def plot(root):
    short = json.loads((root / "analysis.json").read_text())
    reference = json.loads((root / "energy-reference.json").read_text())
    noise, energy, low, high, labels = [], [], [], [], []
    ci = []
    for mode in ["hybrid", "sdf"]:
        for region in ["ceiling", "floor", "right"]:
            n = short["pairs"][mode]["final-" + region]
            e = reference["pairs"][mode][region]
            noise.append(n["variance_reduction_percent"])
            ci.append(n["block_bootstrap_variance_reduction_95_percent"])
            energy.append(e["mean_change_percent"])
            low.append(e["block128_conditional_95_percent"][0])
            high.append(e["block128_conditional_95_percent"][1])
            labels.append(mode.title() + "\n" + region.title())
    x = np.arange(len(labels))
    fig, axes = plt.subplots(1, 2, figsize=(13, 4.7), layout="constrained")
    noise, ci, energy = np.asarray(noise), np.asarray(ci), np.asarray(energy)
    axes[0].bar(x, noise, color=["#2f7aa8"] * 3 + ["#347e66"] * 3)
    axes[0].errorbar(x, noise, yerr=np.stack([noise-ci[:, 0], ci[:, 1]-noise]), fmt="none", capsize=4, color="#222222")
    axes[0].set_ylim(0, 100)
    axes[0].set_ylabel("Reduction of final pixel temporal variance (%)")
    axes[0].set_title("256-frame fixed-camera noise comparison")
    axes[1].axhspan(-1, 1, color="#dcebdc", alpha=.7, label="Declared +/-1% equivalence band")
    axes[1].axhline(0, color="#555555", linewidth=.7)
    axes[1].errorbar(x, energy, yerr=np.stack([energy-low, high-energy]), fmt="o", capsize=4, color="#333333")
    axes[1].set_ylim(-2, 2)
    axes[1].set_ylabel("Mean energy change vs white-noise control (%)")
    axes[1].set_title("2048-frame independent phase: floor still uncertain")
    axes[1].legend(fontsize=8, loc="lower right")
    for ax in axes:
        ax.set_xticks(x, labels, fontsize=8)
        ax.grid(axis="y", alpha=.2)
    fig.suptitle("Blocked importance sampling - Cornell Box, 16px / 8 rays, same frozen source", fontsize=13)
    fig.savefig(root / "blocked-sampling-quality.png", dpi=150)
    plt.close(fig)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    plot(parser.parse_args().capture)
