"""Evaluate scalar-anchor convergence against frozen radiosity and independent raw-ray energy."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import source_cards, stats
from ProbeAnchorCalibration import LUMA, evaluate_sh, normal_from_packed


def read(root, arm, name, shape):
    result = np.stack([np.fromfile(root / f"{arm}-{i:02}-{name}.f16", "<f2").reshape(shape) for i in range(512)]).astype(float)
    assert np.isfinite(result).all(), name
    return result


def analyze(capture, reference, output):
    output.mkdir(parents=True, exist_ok=True)
    ref_files = list(reference.glob("grid5-directional-hybrid-bounce8-rays4096-seed0-spatial-diagnostics-*-raw.f16"))
    assert len(ref_files) == 1024
    ref = np.stack([np.fromfile(p, "<f2").reshape(463, 4)[:168] for p in ref_files]).astype(float)
    reference_y = float(((ref[..., :3] * ref[..., 3:4]) @ LUMA).mean())
    source = source_cards(reference / "grid5-directional-hybrid-bounce8-rays4096-seed0-spatial-diagnostics-00-radiosity.f16")
    report, signals, rows = {"reference_probe_mean_y": reference_y, "arms": {}}, {}, []
    for mode in ("legacy", "novis", "adaptive"):
        arm = "grid5-directional-hybrid-bounce8-spatial-" + mode + "-diagnostics"
        if not (capture / f"{arm}-511-final.f16").exists():
            continue
        for frame in (0, 511):
            value = source_cards(capture / f"{arm}-{frame:02}-radiosity.f16")
            assert np.array_equal(value.view("<u2"), source.view("<u2")), "Radiosity differs from reference"
        sh = read(capture, arm, "sh", (22, 168, 4))
        anchor = read(capture, arm, "anchor", (168, 4))
        state = read(capture, arm, "stats", (168, 4))
        geometry = np.fromfile(capture / f"{arm}-00-geometry.f32", "<f4").reshape(168, 4)
        normal = np.broadcast_to(normal_from_packed(geometry[:, 3]), (512, 168, 3))
        stages = {"raw_anchor": (sh[:, 21, :, :3] * sh[:, 21, :, 3:4]) @ LUMA,
                  "temporal_anchor": (anchor[..., :3] * anchor[..., 3:4]) @ LUMA,
                  "calibrated_sh": (evaluate_sh(sh[:, :7], normal) * anchor[..., 3:4]) @ LUMA}
        metrics = {name: stats(y, (7, 24)) for name, y in stages.items()}
        for stage in ("resolve", "temporal", "fine", "final"):
            value = read(capture, arm, stage, (48, 96, 4))
            stages[stage] = ((value[..., :3] * value[..., 3:4]) @ LUMA).reshape(512, -1)
            metrics[stage] = stats(stages[stage], (48, 96))
        report["arms"][mode] = {"stages": metrics, "frozen_source_matches_reference": True,
            "probe_mean_error_percent": 100 * (metrics["calibrated_sh"]["mean_y"] / reference_y - 1),
            "statistics": {"age_mean": float(state[..., 0].mean()), "age_p5_p50_p95": np.percentile(state[..., 0], [5, 50, 95]).tolist(),
                           "rejected_percent": float(100 * np.mean(state[..., 3] == 3)),
                           "mean_blend": float(state[..., 2].mean())}}
        signals[mode] = stages
        for stage, value in metrics.items():
            rows.append({"arm": mode, "stage": stage, **value})
    base = report["arms"]["legacy"]
    indices = np.random.default_rng(117).integers(0, 8, (2000, 8))
    for mode, info in report["arms"].items():
        info["vs_legacy"] = {}
        for stage, value in info["stages"].items():
            old = base["stages"][stage]
            a, b = signals[mode][stage], signals["legacy"][stage]
            av = np.mean((a - a.mean(0)) ** 2, 1).reshape(8, 64).mean(1)
            bv = np.mean((b - b.mean(0)) ** 2, 1).reshape(8, 64).mean(1)
            reduction = 100 * (1 - av[indices].mean(1) / bv[indices].mean(1))
            delta = (a.mean(1) - b.mean(1)).reshape(8, 64).mean(1)
            mean_change = 100 * delta[indices].mean(1) / b.mean()
            info["vs_legacy"][stage] = {"variance_reduction_percent": 100 * (1 - value["temporal_variance"] / old["temporal_variance"]),
                "rms_reduction_percent": 100 * (1 - value["temporal_rms"] / old["temporal_rms"]),
                "mean_change_percent": 100 * (value["mean_y"] / old["mean_y"] - 1),
                "paired_64frame_variance_ci95_percent": np.percentile(reduction, [2.5, 97.5]).tolist(),
                "paired_64frame_mean_ci95_percent": np.percentile(mean_change, [2.5, 97.5]).tolist()}
    (output / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    with (output / "metrics.csv").open("w", newline="", encoding="utf8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(1, 3, figsize=(16, 4.5), constrained_layout=True)
    labels = ("raw anchor", "temporal anchor", "calibrated SH", "resolve", "pixel temporal", "final")
    keys = ("raw_anchor", "temporal_anchor", "calibrated_sh", "resolve", "temporal", "final")
    for mode, color in (("legacy", "#777777"), ("novis", "#E69F00"), ("adaptive", "#0072B2")):
        if mode not in report["arms"]:
            continue
        metrics = report["arms"][mode]["stages"]
        axes[0].plot(labels, [metrics[k]["temporal_rms"] for k in keys], "o-", label=mode, color=color)
        axes[1].plot(signals[mode]["final"].mean(1), label=mode, color=color)
        axes[2].scatter(report["arms"][mode]["probe_mean_error_percent"], metrics["final"]["temporal_rms"], label=mode, color=color)
    axes[0].set(ylabel="Temporal RMS of covered GI Y", title="Static ceiling: 16 px / 8 rays")
    axes[0].tick_params(axis="x", labelrotation=25)
    axes[1].set(xlabel="Measured frame", ylabel="Final GI ROI mean Y", title="Energy and low-frequency noise")
    axes[2].axvspan(-1, 1, color="green", alpha=.08)
    axes[2].set(xlabel="Probe energy error vs raw-ray reference (%)", ylabel="Final pixel temporal RMS", title="Noise reduction with stable energy")
    for ax in axes:
        ax.legend(fontsize=8)
    fig.savefig(output / "scalar-anchor-convergence.png", dpi=160)
    plt.close(fig)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("reference", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    analyze(args.capture, args.reference, args.output)
