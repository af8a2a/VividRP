"""Compare spatial SH anchor reconstruction with independent raw-ray energy references."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np
from ProbeAnchorCalibration import LUMA, evaluate_sh, load_arm, metrics, normal_from_packed, paired_variance_ratio


def energy_interval(trial, reference):
    # Bootstrap complete 32-frame blocks, including reference uncertainty.
    rng = np.random.default_rng(20261111)
    trial_blocks = trial.reshape(8, 32, -1).mean((1, 2))
    reference_blocks = reference.reshape(64, 32, -1).mean((1, 2))
    t = trial_blocks[rng.integers(0, 8, (4096, 8))].mean(1)
    r = reference_blocks[rng.integers(0, 64, (4096, 64))].mean(1)
    return np.percentile((t / r - 1) * 100, [2.5, 97.5]).tolist()


def analyze(root, output):
    with (root / "regions.csv").open(encoding="utf-8-sig", newline="") as stream:
        regions = [{k: v if k == "roi" else int(v) for k, v in row.items()} for row in csv.DictReader(stream)]
    count = sum(r["w"] * r["h"] for r in regions)
    output.mkdir(parents=True, exist_ok=True)
    reference_parts, reference_geometry = [], []
    for sequence in ("referenceA", "referenceB"):
        prefix = f"grid5-directional-hybrid-bounce8-{sequence}-diagnostics"
        paths = sorted(root.glob(prefix + "-*-reference.f16"), key=lambda p: int(p.name.split("-")[-2]))
        if [int(p.name.split("-")[-2]) for p in paths] != list(range(1024)):
            raise ValueError(f"Incomplete independent reference {sequence}")
        samples = np.stack([np.fromfile(p, "<f2").reshape(count, 4) for p in paths]).astype(float)
        if not np.isfinite(samples).all():
            raise ValueError("Non-finite reference")
        # Uniform cosine draws have unit inverse relative PDF. Unresolved rays contribute zero.
        reference_parts.append(samples[..., :3] * samples[..., 3:4])
        reference_geometry.append(np.fromfile(root / f"{prefix}-00-geometry.f32", "<f4").reshape(count, 4))
    reference_rgb = np.concatenate(reference_parts)
    reference_y = reference_rgb @ LUMA
    if not np.array_equal(reference_geometry[0], reference_geometry[1]):
        raise ValueError("Reference receiver geometry changed")
    np.savez_compressed(output / "high-sample-reference.npz", mean_rgb=reference_rgb.mean(0),
                        independent_mean_rgb=np.stack([r.mean(0) for r in reference_parts]),
                        geometry=reference_geometry[0], raw_energy_rgb=reference_rgb)
    report = {"capture": str(root), "reference_rays_per_probe": 16384, "reference_regions": {}, "arms": {}}
    rows, plots = [], {}
    for r in regions:
        span = slice(r["offset"], r["offset"] + r["w"] * r["h"])
        value = reference_y[:, span]
        block_means = value.reshape(64, 32, -1).mean((1, 2))
        reference_mean = float(value.mean())
        means = [float((part[:, span] @ LUMA).mean()) for part in reference_parts]
        report["reference_regions"][r["roi"]] = {"mean_y": reference_mean, "independent_means": means,
            "half_difference_percent": (means[1] / means[0] - 1) * 100,
            "mean_95_percent_half_width": float(1.96 * block_means.std(ddof=1) / np.sqrt(64)),
            "mean_rgb": reference_rgb[:, span].mean((0, 1)).tolist()}
    for guided in (False, True):
        for spatial in (False, True):
            arm = "grid5-directional-" + ("guided-" if guided else "") + "hybrid-bounce8-"
            arm += "spatial-diagnostics" if spatial else "baseline-diagnostics"
            _, packed, anchor_half, geometry = load_arm(root, arm, count)
            geometry_delta = float(np.max(np.abs(geometry[..., :3] - reference_geometry[0][None, :, :3])))
            same_normals = np.array_equal(geometry[..., 3], np.broadcast_to(reference_geometry[0][:, 3], geometry.shape[:2]))
            if geometry_delta > 1e-5 or not same_normals:
                raise ValueError(f"{arm}: receivers differ from reference")
            normal = normal_from_packed(geometry[..., 3])
            rgb = evaluate_sh(packed[:, :7].astype(float), normal)
            temporal_rgb = anchor_half[..., :3].astype(float)
            coverage = anchor_half[..., 3].astype(float)
            energy_rgb = rgb * coverage[..., None]
            temporal_energy_rgb = temporal_rgb * coverage[..., None]
            energy_y, temporal_y = energy_rgb @ LUMA, temporal_energy_rgb @ LUMA
            np.savez_compressed(output / (arm + ".npz"), calibrated_rgb=rgb, temporal_anchor=anchor_half,
                                covered_energy_rgb=energy_rgb, geometry=geometry, post_sh=packed[:, :7])
            arm_report = {"guided": guided, "spatial_anchor": spatial,
                          "max_receiver_position_delta_m": geometry_delta, "identical_packed_normals": same_normals, "regions": {}}
            for r in regions:
                span = slice(r["offset"], r["offset"] + r["w"] * r["h"])
                y, old, ref = energy_y[:, span], temporal_y[:, span], reference_y[:, span]
                reference_mean = ref.mean()
                mean_error = y.mean(0) - ref.mean(0)
                ref_rgb = reference_rgb[:, span].mean((0, 1))
                result = metrics(y, r["h"], r["w"])
                result.update({"reference_mean_y": float(reference_mean),
                    "reference_energy_error_percent": float((y.mean() / reference_mean - 1) * 100),
                    "reference_energy_error_95_percent": energy_interval(y, ref),
                    "reference_mean_rgb_error_percent": ((energy_rgb[:, span].mean((0, 1)) / ref_rgb - 1) * 100).tolist(),
                    "per_probe_mean_error_rmse": float(np.sqrt(np.mean(mean_error ** 2))),
                    "per_probe_mean_error_p95": float(np.percentile(np.abs(mean_error), 95)),
                    "current_temporal_anchor": metrics(old, r["h"], r["w"]),
                    "paired_variance_ratio": paired_variance_ratio(old, y)})
                result["energy_gate_2_percent"] = abs(result["reference_energy_error_percent"]) <= 2
                arm_report["regions"][r["roi"]] = result
                rows.append({"arm": arm, "roi": r["roi"], **{k: v for k, v in result.items() if isinstance(v, (float, bool))}})
                if guided and spatial:
                    plots[r["roi"]] = (r, old, y, ref.mean(0), mean_error)
            gi = np.stack([np.fromfile(root / f"{arm}-{i:02}-gi.f16", "<f2").reshape(48, 96, 4)
                           for i in range(256)]).astype(float)
            # The exported full-resolution diffuse output stores conditional RGB and separate coverage.
            full_y = (gi[..., :3] * gi[..., 3:4]) @ LUMA
            arm_report["final_ceiling_roi"] = metrics(full_y.reshape(256, -1), 48, 96)
            report["arms"][arm] = arm_report
    for guided in (False, True):
        pair = [v for v in report["arms"].values() if v["guided"] == guided]
        baseline = next(v for v in pair if not v["spatial_anchor"])
        spatial = next(v for v in pair if v["spatial_anchor"])
        for roi in ("ceiling", "backwall", "floor"):
            b, s = baseline["regions"][roi], spatial["regions"][roi]
            s["independent_baseline_mean_change_percent"] = (s["mean_y"] / b["mean_y"] - 1) * 100
            s["independent_baseline_variance_ratio"] = s["temporal_variance"] / b["temporal_variance"]
        b, s = baseline["final_ceiling_roi"], spatial["final_ceiling_roi"]
        s["baseline_mean_change_percent"] = (s["mean_y"] / b["mean_y"] - 1) * 100
        s["baseline_variance_ratio"] = s["temporal_variance"] / b["temporal_variance"]
    (output / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    with (output / "metrics.csv").open("w", encoding="utf8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(3, 4, figsize=(14, 9), constrained_layout=True)
    for row, (name, (r, old, y, ref, error)) in enumerate(plots.items()):
        h, w = r["h"], r["w"]
        std0, std1 = old.std(0).reshape(h, w), y.std(0).reshape(h, w)
        axes[row, 0].imshow(ref.reshape(h, w), origin="lower", aspect="auto", cmap="viridis")
        maximum = float(max(std0.max(), std1.max()))
        axes[row, 1].imshow(std0, vmin=0, vmax=maximum, origin="lower", aspect="auto", cmap="magma")
        noise = axes[row, 2].imshow(std1, vmin=0, vmax=maximum, origin="lower", aspect="auto", cmap="magma")
        bias = axes[row, 3].imshow(error.reshape(h, w), origin="lower", aspect="auto", cmap="RdBu_r",
                                 vmin=-max(abs(error)), vmax=max(abs(error)))
        fig.colorbar(noise, ax=list(axes[row, 1:3]), shrink=.65, label="Temporal stddev")
        fig.colorbar(bias, ax=axes[row, 3], shrink=.65, label="Mean error Y")
        axes[row, 0].set_ylabel(name)
        for col in range(4):
            axes[row, col].set_xticks([])
            axes[row, col].set_yticks([])
        axes[row, 0].set_title("16,384-ray reference mean")
        axes[row, 1].set_title(f"Temporal anchor RMS {np.sqrt(np.mean(std0 ** 2)):.3f}")
        axes[row, 2].set_title(f"Spatial anchor RMS {np.sqrt(np.mean(std1 ** 2)):.3f}")
        axes[row, 3].set_title("Spatial mean minus reference")
    fig.suptitle("Conservative anchor reconstruction: guided 8-ray gather, 256 measured frames\n"
                 "Center/spatial noise maps use identical ray inputs; reference uses independent uniform cosine samples")
    fig.savefig(output / "plane-noise-reference.png", dpi=160)
    plt.close(fig)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    analyze(args.capture, args.output)
