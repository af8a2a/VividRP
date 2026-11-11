"""Measure directional-history maturity, uncalibrated SH variance and energy on GPU captures."""

import argparse
import csv
import itertools
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import source_cards, stats
from ProbeAnchorCalibration import LUMA, evaluate_sh, normal_from_packed


def directional_age(value, packed):
    if not packed:
        age = np.floor(value)
        return age, np.rint((value - age) * 32)
    bits = value.astype("<f2").view("<u2").astype(np.int32) - 0x400
    return (bits & 511) / 32.0, (bits >> 9) & 15


def load(root, arm, suffix, shape, dtype="<f2", indices=range(512)):
    extension = "f16" if dtype == "<f2" else "f32"
    value = np.stack([np.fromfile(root / f"{arm}-{i:02}-{suffix}.{extension}", dtype).reshape(shape) for i in indices])
    assert np.isfinite(value).all(), f"Non-finite {arm}/{suffix}"
    return value.astype(float)


def analyze(root, output):
    output.mkdir(parents=True, exist_ok=True)
    report, metric_rows, variance_signals = {"arms": {}}, [], {}
    sphere = np.array([d for d in itertools.product((-1, 0, 1), repeat=3) if d != (0, 0, 0)], float)
    sphere /= np.linalg.norm(sphere, axis=-1, keepdims=True)
    canonical_source = None
    for directory in ("initial", "plane", "identity-tier", "fractional", "final"):
        capture = root / directory
        if not capture.exists():
            continue
        arms = sorted({p.name.rsplit("-", 2)[0] for p in capture.glob("*-sh.f16")})
        for arm in arms:
            if len(list(capture.glob(arm + "-*-sh.f16"))) != 512:
                continue
            key = directory + "/" + arm.split("-spatial-")[1].removesuffix("-diagnostics")
            sh = load(capture, arm, "sh", (22, 168, 4))
            anchor = load(capture, arm, "anchor", (168, 4))
            geometry = np.fromfile(capture / f"{arm}-00-geometry.f32", "<f4").reshape(168, 4)
            normal = np.broadcast_to(normal_from_packed(geometry[:, 3]), (512, 168, 3))
            stages = {}
            signals = {}
            for name, start in (("uncalibrated_unfiltered", 14), ("uncalibrated_filtered", 7), ("calibrated", 0)):
                coefficients = sh[:, start:start + 7]
                # Use the same scalar trace coverage for all angular reconstructions.
                # Separately report directional validity so filled support cannot hide energy loss.
                y = (evaluate_sh(coefficients, normal) * anchor[..., 3:4]) @ LUMA
                angular = np.stack([(evaluate_sh(coefficients, np.broadcast_to(d, normal.shape)) * anchor[..., 3:4]) @ LUMA
                                    for d in sphere], -1)
                stages[name] = stats(y, (7, 24))
                signals[name] = np.mean((y - y.mean(0)) ** 2, axis=1)
                stages[name]["angular_temporal_variance_26_directions"] = float(np.mean((angular - angular.mean(0)) ** 2))
            pixel = load(capture, arm, "final", (48, 96, 4))
            stages["final_pixel"] = stats(((pixel[..., :3] * pixel[..., 3:4]) @ LUMA).reshape(512, -1), (48, 96))
            indices = sorted(int(p.name.split("-")[-2]) for p in capture.glob(arm + "-*-directional.f16"))
            lighting = load(capture, arm, "directional", (16, 168, 4), indices=indices)
            meta = load(capture, arm, "directional-meta", (16, 168, 4), indices=indices)
            composite = load(capture, arm, "composite", (16, 168, 4), indices=indices)
            composite_meta = load(capture, arm, "composite-meta", (16, 168, 4), indices=indices)
            valid = lighting[..., 3] > 0
            age, stale = directional_age(meta[..., 1], directory in ("fractional", "final"))
            assert np.all((age[valid] >= 1) & (age[valid] <= 8))
            drawn = composite_meta[..., 1] > 0
            explicit_failure = drawn & (composite[..., 3] <= 0)
            assert not np.any(valid & explicit_failure), "An explicit unresolved ray inherited history"
            detail = {"valid_fraction": float(valid.mean()), "age_mean": float(age[valid].mean()),
                      "age_p5_p50_p95": np.percentile(age[valid], [5, 50, 95]).tolist(),
                      "drawn_age_mean": float(age[valid & drawn].mean()),
                      "undrawn_age_mean": float(age[valid & ~drawn].mean()),
                      "stale_mean": float(stale[valid].mean()),
                      "explicit_unresolved_samples": int(explicit_failure.sum()), "explicit_unresolved_inheritance": 0}
            for label, mask in (("near", (meta[..., 0] > 0) & (meta[..., 0] <= 5)),
                                ("far", meta[..., 0] > 5), ("sky", meta[..., 0] < 0)):
                selected = mask & valid
                detail[label] = {"fraction_of_valid": float(selected.sum() / max(valid.sum(), 1)),
                                 "age_mean": float(age[selected].mean()) if selected.any() else None}
            plane_path = capture / f"{arm}-{indices[0]:02}-directional-plane.f16"
            if plane_path.exists():
                plane = load(capture, arm, "directional-plane", (16, 168, 4), indices=indices)
                detail["valid_plane_fraction"] = float((plane[..., 3] > 0)[valid].mean())
            first = source_cards(capture / f"{arm}-00-radiosity.f16")
            last = source_cards(capture / f"{arm}-511-radiosity.f16")
            assert np.array_equal(first.view("<u2"), last.view("<u2"))
            if canonical_source is None:
                canonical_source = first
            assert np.array_equal(first.view("<u2"), canonical_source.view("<u2")), "Radiosity differs between arms"
            info = {"frames": 512, "directional_frames": len(indices), "directional": detail, "stages": stages,
                    "frozen_identical_radiosity": True}
            report["arms"][key] = info
            variance_signals[key] = signals
            for name, result in stages.items():
                metric_rows.append({"arm": key, "stage": name, "mean_y": result["mean_y"],
                                    "temporal_variance": result["temporal_variance"], "temporal_rms": result["temporal_rms"]})
    for cohort in ("initial", "plane", "identity-tier", "fractional", "final"):
        baseline = report["arms"].get(cohort + "/legacy")
        if baseline is None:
            continue
        for key, info in report["arms"].items():
            if not key.startswith(cohort + "/"):
                continue
            info["vs_legacy"] = {}
            for name, value in info["stages"].items():
                control = baseline["stages"][name]
                info["vs_legacy"][name] = {"variance_reduction_percent": 100 * (1 - value["temporal_variance"] / control["temporal_variance"]),
                                         "equal_mean_variance_reduction_percent": 100 * (1 - value["temporal_variance"] * (control["mean_y"] / value["mean_y"]) ** 2 / control["temporal_variance"]),
                                         "mean_change_percent": 100 * (value["mean_y"] / control["mean_y"] - 1)}
                if name in variance_signals[key]:
                    a = variance_signals[key][name].reshape(16, 32).mean(1)
                    b = variance_signals[cohort + "/legacy"][name].reshape(16, 32).mean(1)
                    indices = np.random.default_rng(101).integers(0, 16, (2000, 16))
                    ratios = 100 * (1 - a[indices].mean(1) / b[indices].mean(1))
                    info["vs_legacy"][name]["paired_32frame_block_ci95_percent"] = np.percentile(ratios, [2.5, 97.5]).tolist()
    (output / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    with (output / "metrics.csv").open("w", newline="", encoding="utf8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(metric_rows[0]))
        writer.writeheader()
        writer.writerows(metric_rows)
    if "final/borrow" in report["arms"]:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        fig, axes = plt.subplots(1, 3, figsize=(16, 4.5), constrained_layout=True)
        modes, colors = ("legacy", "decay", "borrow"), ("#777777", "#E69F00", "#0072B2")
        values = [report["arms"]["final/" + name] for name in modes]
        axes[0].axhspan(4, 6, color="green", alpha=.10, label="Requested range")
        axes[0].bar(modes, [v["directional"]["age_mean"] for v in values], color=colors)
        axes[0].set(ylabel="Mean effective sample age", title="Static ceiling: 16 px, 8 rays/probe", ylim=(0, 8))
        axes[0].legend(fontsize=8)
        labels = ("unfiltered SH", "filtered SH", "calibrated SH", "final pixel")
        stages = ("uncalibrated_unfiltered", "uncalibrated_filtered", "calibrated", "final_pixel")
        for mode, value, color in zip(modes, values, colors):
            axes[1].plot(labels, [100 * value["stages"][s]["temporal_variance"] / values[0]["stages"][s]["temporal_variance"] for s in stages], "o-", label=mode, color=color)
            axes[2].plot(labels, [value["stages"][s]["temporal_rms"] for s in stages], "o-", label=mode, color=color)
        axes[1].set(ylabel="Temporal variance (% of legacy)", title="Directional filtering improves before calibration")
        axes[2].set(ylabel="Covered GI Y temporal RMS", title="Scalar anchor remains the final noise limit")
        for ax in axes[1:]:
            ax.tick_params(axis="x", labelrotation=15)
            ax.legend(fontsize=8)
        fig.savefig(output / "directional-history-quality.png", dpi=160)
        plt.close(fig)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    analyze(args.root, args.output)
