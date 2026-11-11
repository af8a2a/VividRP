"""Analyze native Gather history/reconstruction stages with a frozen radiosity source."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np
from scipy.ndimage import gaussian_filter

from ProbeAnchorCalibration import LUMA, evaluate_sh, normal_from_packed


def stats(y, shape):
    residual = y - y.mean(0, keepdims=True)
    variance = np.mean(residual ** 2)
    smooth = gaussian_filter(residual.reshape(len(y), *shape), (0, 8 if shape[0] == 48 else 1, 8 if shape[0] == 48 else 1), mode="reflect")
    result = {"mean_y": float(y.mean()), "temporal_variance": float(variance), "temporal_rms": float(np.sqrt(variance)),
              "relative_rms_percent": float(100 * np.sqrt(variance) / y.mean()),
              "lowpass_rms": float(np.sqrt(np.mean(smooth ** 2)))}
    for lag in (1, 8, 32):
        result[f"autocorrelation_lag_{lag}"] = float(np.mean(residual[lag:] * residual[:-lag]) / max(variance, 1e-20))
    return result


def unpack_pixel_history(alpha):
    bits = alpha.copy().view(np.uint16).astype(np.int32) - 0x400
    valid = alpha > 0
    age = np.where(valid, 1 + ((bits >> 8) & 31), 0)
    coverage = np.where(valid, (bits & 255) / 255.0, 0)
    status = np.where(valid, (bits >> 13) & 3, 0)
    return age, coverage, status


def source_cards(path):
    image = np.fromfile(path, "<f2").reshape(-1, 128, 4)
    return np.stack([image[(i // 16) * 8:(i // 16 + 1) * 8, (i % 16) * 8:(i % 16 + 1) * 8] for i in range(37)])


def analyze(root, output, reference):
    output.mkdir(parents=True, exist_ok=True)
    paths = list(reference.glob("grid5-directional-hybrid-bounce8-rays4096-seed0-spatial-diagnostics-*-raw.f16"))
    ref = np.stack([np.fromfile(p, "<f2").reshape(463, 4)[:168] for p in paths]).astype(float)
    reference_mean = float(((ref[..., :3] * ref[..., 3:4]) @ LUMA).mean())
    report = {"reference_probe_mean": reference_mean, "arms": {}}
    rows, curves = [], {}
    canonical_source = source_cards(reference / "grid5-directional-hybrid-bounce8-rays4096-seed0-spatial-diagnostics-00-radiosity.f16")
    for directory in ("ablations", "rounding", "nearest", "final"):
        capture = root / directory
        if not capture.exists():
            continue
        arms = sorted({p.name.rsplit("-", 2)[0] for p in capture.glob("*-final.f16")})
        for arm in arms:
            files = sorted(capture.glob(arm + "-*-final.f16"), key=lambda p: int(p.name.split("-")[-2]))
            if len(files) != 512:
                continue
            key = directory + "/" + arm.split("-spatial-", 1)[1].removesuffix("-diagnostics")
            sh = np.stack([np.fromfile(str(p).replace("-final.f16", "-sh.f16"), "<f2").reshape(22, 168, 4) for p in files]).astype(float)
            full = (capture / f"{arm}-00-anchor.f32").exists()
            ext, dtype = ("f32", "<f4") if full else ("f16", "<f2")
            anchor = np.stack([np.fromfile(str(p).replace("-final.f16", "-anchor." + ext), dtype).reshape(168, 4) for p in files]).astype(float)
            geometry = np.fromfile(capture / f"{arm}-00-geometry.f32", "<f4").reshape(168, 4)
            normal = np.broadcast_to(normal_from_packed(geometry[:, 3]), (512, 168, 3))
            raw = (sh[:, 21, :, :3] * sh[:, 21, :, 3:4]) @ LUMA
            temporal_anchor = (anchor[..., :3] * anchor[..., 3:4]) @ LUMA
            reconstructed = (evaluate_sh(sh[:, :7], normal) * anchor[..., 3:4]) @ LUMA
            stages = {"raw_anchor": stats(raw, (7, 24)), "temporal_anchor": stats(temporal_anchor, (7, 24)),
                      "calibrated_sh": stats(reconstructed, (7, 24))}
            energies = {}
            for stage in ("resolve", "temporal", "fine", "final"):
                value = np.stack([np.fromfile(str(p).replace("-final.f16", f"-{stage}.f16"), "<f2").reshape(48, 96, 4) for p in files]).astype(float)
                energies[stage] = (value[..., :3] * value[..., 3:4]) @ LUMA
                stages[stage] = stats(energies[stage].reshape(512, -1), (48, 96))
            history = np.stack([np.fromfile(str(p).replace("-final.f16", "-history.f16"), "<f2").reshape(48, 96, 4) for p in files])
            age, coverage, status = unpack_pixel_history(history[..., 3])
            max_age = 32 if "age32" in arm else 16
            info = {"frames": 512, "pixel_history_cap": max_age, "fp32_anchor": full, "stages": stages,
                    "probe_energy_error_percent": (stages["calibrated_sh"]["mean_y"] / reference_mean - 1) * 100,
                    "pixel_history": {"age_mean": float(age.mean()), "age_p5_p50_p95": np.percentile(age, [5, 50, 95]).tolist(),
                                      "at_cap_percent": float(np.mean(age == max_age) * 100),
                                      "accepted_percent": float(np.mean(status == 1) * 100),
                                      "limited_percent": float(np.mean(status == 2) * 100),
                                      "no_history_percent": float(np.mean(status == 0) * 100)}}
            # Estimate the effective pixel history response under static zero-motion reprojection.
            old_energy = (history[:-1, ..., :3].astype(float) * coverage[:-1, ..., None]) @ LUMA
            x, y = energies["resolve"][1:] - old_energy, energies["temporal"][1:] - old_energy
            info["estimated_pixel_current_weight"] = float(np.sum(x * y) / max(np.sum(x * x), 1e-20))
            source0 = source_cards(capture / f"{arm}-00-radiosity.f16")
            source1 = source_cards(capture / f"{arm}-511-radiosity.f16")
            if not np.array_equal(source0.view(np.uint16), source1.view(np.uint16)):
                raise ValueError("Radiosity changed during measurement")
            if not np.array_equal(source0.view(np.uint16), canonical_source.view(np.uint16)):
                raise ValueError("Radiosity differs from the high-sample reference source")
            info["frozen_identical_radiosity"] = True
            info["matches_high_sample_reference_source"] = True
            directional_files = list(capture.glob(arm + "-*-directional.f16"))
            if directional_files:
                light = np.stack([np.fromfile(p, "<f2").reshape(16, 168, 4) for p in directional_files]).astype(float)
                meta = np.stack([np.fromfile(str(p).replace("-directional.f16", "-directional-meta.f16"), "<f2").reshape(16, 168, 4) for p in directional_files]).astype(float)
                valid = light[..., 3] > 0
                info["directional_history"] = {"valid_fraction": float(valid.mean()),
                    "sample_age_mean": float(np.floor(meta[..., 1])[valid].mean()),
                    "age_p5_p50_p95": np.percentile(np.floor(meta[..., 1])[valid], [5, 50, 95]).tolist()}
            report["arms"][key] = info
            for stage, metric in stages.items():
                rows.append({"arm": key, "stage": stage, **metric})
            curves[key] = {"final_mean": energies["final"].mean((1, 2)), "temporal_mean": energies["temporal"].mean((1, 2)),
                           "probe_mean": reconstructed.mean(1)}
    with (output / "metrics.csv").open("w", encoding="utf8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    (output / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(1, 2, figsize=(14, 5), constrained_layout=True)
    labels = ("resolve", "temporal", "fine", "final")
    plotted = {"ablations/baseline", "ablations/age32", "ablations/noclip", "ablations/align-anchor97",
               "nearest/nearest-align-anchor97", "nearest/fp32-nearest-align-anchor97", "final/legacy", "final/fixed"}
    for key, info in report["arms"].items():
        if key not in plotted:
            continue
        axes[0].plot(labels, [info["stages"][s]["temporal_rms"] for s in labels], marker="o", label=key)
        axes[1].scatter(info["probe_energy_error_percent"], info["stages"]["final"]["temporal_rms"], label=key)
    axes[0].set_ylabel("Temporal RMS of covered GI Y")
    axes[0].set_title("Ceiling: full-resolution stages")
    axes[1].axvspan(-2, 2, color="green", alpha=.08)
    axes[1].set_xlabel("Probe mean energy error vs raw-ray reference (%)")
    axes[1].set_ylabel("Final pixel temporal RMS")
    axes[1].set_title("Noise / energy: lower RMS alone is insufficient")
    axes[0].legend(fontsize=6)
    fig.suptitle("Frozen 8-order / 4096-ray radiosity; 16 px probes and 8 rays/probe; 512 measured frames")
    fig.savefig(output / "ceiling-history-stages.png", dpi=160)
    plt.close(fig)
    np.savez_compressed(output / "curves.npz", **{k.replace("/", "_") + "_" + n: v for k, c in curves.items() for n, v in c.items()})
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--reference", type=Path, required=True)
    args = parser.parse_args()
    analyze(args.root, args.output, args.reference)
