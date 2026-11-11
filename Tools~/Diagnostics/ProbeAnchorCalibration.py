"""Analyze the optional Screen Probe anchor/SH GPU capture (numpy/scipy/matplotlib)."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np
from scipy.ndimage import gaussian_filter


STAGES = ("raw_anchor", "temporal_anchor", "unfiltered_sh", "pre_calibration_sh", "post_calibration_sh")
LUMA = np.array([0.2126, 0.7152, 0.0722])


def normal_from_packed(packed):
    octahedral = packed.astype(np.uint32)
    xy = np.stack([octahedral & 255, octahedral >> 8], -1) / 255.0 * 2.0 - 1.0
    normal = np.concatenate([xy, 1.0 - np.abs(xy).sum(-1, keepdims=True)], -1)
    fold = np.maximum(-normal[..., 2:3], 0.0)
    normal[..., :2] += np.where(normal[..., :2] >= 0.0, -fold, fold)
    return normal / np.linalg.norm(normal, axis=-1, keepdims=True)


def evaluate_sh(sh, normal):
    # The exact Cartesian packing in SdfScreenProbeIrradiance.hlsl, before max(0, E).
    n4 = np.concatenate([normal, np.ones((*normal.shape[:-1], 1))], -1)
    products = normal[..., [0, 1, 2, 2]] * normal[..., [1, 2, 2, 0]]
    return (np.einsum("tcpk,tpk->tpc", sh[:, :3], n4)
            + np.einsum("tcpk,tpk->tpc", sh[:, 3:6], products)
            + sh[:, 6, :, :3] * (normal[..., 0] ** 2 - normal[..., 1] ** 2)[..., None])


def load_arm(root, arm, point_count):
    paths = sorted(root.glob(arm + "-*-sh.f16"), key=lambda p: int(p.name.split("-")[-2]))
    indices = [int(p.name.split("-")[-2]) for p in paths]
    if indices != list(range(256)):
        raise ValueError(f"{arm}: expected all 256 consecutive measured frames, got {indices}")
    planes = 22 if arm.endswith("diagnostics") else 7
    sh = np.stack([np.fromfile(p, "<f2").reshape(planes, point_count, 4) for p in paths])
    anchor = np.stack([np.fromfile(str(p).replace("-sh.f16", "-anchor.f16"), "<f2")
                       .reshape(point_count, 4) for p in paths])
    geometry = np.stack([np.fromfile(str(p).replace("-sh.f16", "-geometry.f32"), "<f4")
                         .reshape(point_count, 4) for p in paths])
    if not all(np.isfinite(a).all() for a in (sh, anchor, geometry)):
        raise ValueError(f"{arm}: non-finite GPU data")
    return paths, sh, anchor, geometry


def metrics(y, height, width):
    residual = y - y.mean(0, keepdims=True)
    smooth = gaussian_filter(residual.reshape(len(y), height, width), (0, 1, 1), mode="reflect")
    variance = float(np.mean(residual ** 2))
    mean = float(y.mean())
    return {"mean_y": mean, "temporal_variance": variance, "temporal_rms": variance ** 0.5,
            "relative_rms_percent": variance ** 0.5 / max(abs(mean), 1e-10) * 100,
            "plane_residual_variance": float(residual.var(axis=1).mean()),
            "lowpass_rms": float(np.sqrt(np.mean(smooth ** 2))),
            "negative_fraction": float(np.mean(y < 0))}


def paired_variance_ratio(before, after):
    # Paired block bootstrap; fixed per-probe means and blocks of 32 frames preserve short history correlation.
    def blocks(y):
        residual = y - y.mean(0, keepdims=True)
        return np.mean(residual.reshape(8, 32, -1) ** 2, axis=(1, 2))
    b, a = blocks(before), blocks(after)
    samples = np.random.default_rng(20261111).integers(0, 8, (4096, 8))
    ratios = a[samples].mean(1) / np.maximum(b[samples].mean(1), 1e-20)
    return {"ratio": float(a.mean() / max(b.mean(), 1e-20)),
            "paired_block_bootstrap_95_percent": np.percentile(ratios, [2.5, 97.5]).tolist()}


def analyze(root, output, same_input=None):
    with (root / "regions.csv").open(newline="", encoding="utf-8-sig") as stream:
        regions = [{k: v if k == "roi" else int(v) for k, v in row.items()} for row in csv.DictReader(stream)]
    count = sum(r["w"] * r["h"] for r in regions)
    arms = ("grid5-directional-hybrid-bounce8-control", "grid5-directional-hybrid-bounce8-diagnostics",
            "grid5-directional-guided-hybrid-bounce8-diagnostics")
    loaded = {arm: load_arm(root, arm, count) for arm in arms}
    control, diagnostic = loaded[arms[0]], loaded[arms[1]]
    equivalence = {
        "post_sh_bitwise_equal": bool(np.array_equal(control[1].view(np.uint16), diagnostic[1][:, :7].view(np.uint16))),
        "anchor_bitwise_equal": bool(np.array_equal(control[2].view(np.uint16), diagnostic[2].view(np.uint16))),
        "geometry_bitwise_equal": bool(np.array_equal(control[3].view(np.uint32), diagnostic[3].view(np.uint32))),
        "gi_roi_bitwise_equal": all((root / f"{arms[0]}-{i:02}-gi.f16").read_bytes()
                                   == (root / f"{arms[1]}-{i:02}-gi.f16").read_bytes() for i in (0, 127, 255))}
    report = {"capture": str(root), "frames": 256, "independent_rerun_equivalence": equivalence, "arms": {}}
    if same_input is not None:
        same_input_report = {}
        for tag in ("sh", "anchor"):
            different = 0
            maximum = 0.0
            for i in range(32):
                original = np.fromfile(same_input / f"{arms[1]}-{i:02}-{tag}.f16", "<f2")
                control_values = np.fromfile(same_input / f"{arms[1]}-{i:02}-same-input-control-{tag}.f16", "<f2")
                original = original[:control_values.size]
                different += int(np.count_nonzero(original.view(np.uint16) != control_values.view(np.uint16)))
                maximum = max(maximum, float(np.max(np.abs(original.astype(float) - control_values))))
            same_input_report[tag] = {"frames": 32, "bitwise_equal": different == 0,
                                      "different_elements": different, "max_abs": maximum}
        report["same_input_equivalence"] = same_input_report
    output.mkdir(parents=True, exist_ok=True)
    rows, samples, plot_data = [], [], {}
    for arm in arms[1:]:
        _, packed, anchor_half, geometry = loaded[arm]
        sh, anchor = packed.astype(np.float64), anchor_half.astype(np.float64)
        normal = normal_from_packed(geometry[..., 3])
        pre, post, unfiltered = evaluate_sh(sh[:, 7:14], normal), evaluate_sh(sh[:, :7], normal), evaluate_sh(sh[:, 14:21], normal)
        unchanged = np.ones((7, 4), dtype=bool)
        unchanged[:3, 3] = False
        if not np.array_equal(packed[:, :7, :, :].transpose(0, 2, 1, 3)[..., unchanged],
                              packed[:, 7:14, :, :].transpose(0, 2, 1, 3)[..., unchanged]):
            raise ValueError("Calibration unexpectedly changed a non-constant coefficient")
        values = (sh[:, 21, :, :3], anchor[..., :3], unfiltered, pre, post)
        np.savez_compressed(output / ("guide-on-probes.npz" if "guided" in arm else "guide-off-probes.npz"),
                            raw_anchor=packed[:, 21], temporal_anchor=anchor_half,
                            unfiltered_sh=packed[:, 14:21], pre_calibration_sh=packed[:, 7:14],
                            post_calibration_sh=packed[:, :7], geometry=geometry,
                            probe_normal=normal, stage_normal_rgb=np.stack(values))
        arm_report = {"normal_anchor_max_abs_rgb": float(np.max(np.abs(post - anchor[..., :3]))),
                      "only_constant_coefficients_changed": True, "regions": {}}
        for r in regions:
            start, end = r["offset"], r["offset"] + r["w"] * r["h"]
            span = slice(start, end)
            g = geometry[:, span]
            norm = normal[:, span]
            expected = {"ceiling": (1, -1), "floor": (1, 1), "backwall": (2, -1)}[r["roi"]]
            if not np.all(g[..., 3] >= 0) or not np.all(norm[..., expected[0]] * expected[1] > .95):
                raise ValueError(f"{r['roi']}: region does not match its intended plane; check GPU readback Y orientation")
            if np.max(np.abs(g - g[0:1])) > 1e-4:
                raise ValueError(f"{r['roi']}: receiver geometry moved")
            common_coverage = anchor[:, span, 3]
            conditional = [v[:, span] @ LUMA for v in values]
            covered = [v * common_coverage for v in conditional]
            region_report = {"points": end - start, "common_coverage_mean": float(common_coverage.mean()),
                             "common_coverage_min": float(common_coverage.min()),
                             "raw_coverage_mean": float(sh[:, 21, span, 3].mean()), "stages": {}}
            for label, y, e in zip(STAGES, conditional, covered):
                pair = {"conditional": metrics(y, r["h"], r["w"]), "common_coverage": metrics(e, r["h"], r["w"])}
                region_report["stages"][label] = pair
                for weighting, result in pair.items():
                    rows.append({"arm": arm, "roi": r["roi"], "stage": label, "weighting": weighting, **result})
            region_report["spatial_filter_variance_ratio"] = paired_variance_ratio(covered[2], covered[3])
            region_report["calibration_variance_ratio"] = paired_variance_ratio(covered[3], covered[4])
            region_report["post_anchor_rms_difference"] = float(np.sqrt(np.mean((covered[4] - covered[1]) ** 2)))
            correction = covered[4] - covered[3]
            pre_residual = covered[3] - covered[3].mean(0)
            correction_residual = correction - correction.mean(0)
            region_report["correction_variance"] = float(np.mean(correction_residual ** 2))
            region_report["twice_pre_correction_covariance"] = float(2 * np.mean(pre_residual * correction_residual))
            arm_report["regions"][r["roi"]] = region_report
            if r["roi"] == "ceiling":
                plot_data[arm] = (r, covered)
            for t in range(256):
                for j in range(end - start):
                    point = start + j
                    sample = {"arm": arm, "roi": r["roi"], "frame": t,
                              "probe_x": r["x"] + j % r["w"], "probe_y": r["y"] + j // r["w"],
                              "temporal_coverage": common_coverage[t, j], "raw_coverage": sh[t, 21, point, 3]}
                    sample.update({label + "_y": y[t, j] for label, y in zip(STAGES, conditional)})
                    samples.append(sample)
        report["arms"][arm] = arm_report
    (output / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    for name, data in (("metrics.csv", rows), ("probe_samples.csv", samples)):
        with (output / name).open("w", newline="", encoding="utf8") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(data[0]))
            writer.writeheader()
            writer.writerows(data)
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    labels = ("Raw anchor", "Temporal anchor", "SH before spatial filter", "SH before calibration", "SH after calibration")
    fig, axes = plt.subplots(4, 5, figsize=(16, 8), constrained_layout=True)
    for row, (arm, (r, values)) in enumerate(plot_data.items()):
        noise = [np.std(y, axis=0).reshape(r["h"], r["w"]) for y in values]
        maximum = max(float(n.max()) for n in noise)
        residuals = [(y[-1] - y.mean(0)).reshape(r["h"], r["w"]) for y in values]
        limit = max(float(np.abs(n).max()) for n in residuals)
        for col in range(5):
            noise_image = axes[2 * row, col].imshow(noise[col], vmin=0, vmax=maximum, cmap="magma", origin="lower", aspect="auto")
            residual_image = axes[2 * row + 1, col].imshow(residuals[col], vmin=-limit, vmax=limit, cmap="RdBu_r", origin="lower", aspect="auto")
            for ax in axes[2 * row:2 * row + 2, col]:
                ax.set_xticks([])
                ax.set_yticks([])
            axes[2 * row, col].set_title(labels[col])
            axes[2 * row, col].set_xlabel(f"Temporal RMS {np.sqrt(np.mean(noise[col] ** 2)):.3f}")
        guide = "Guide on" if "guided" in arm else "Guide off"
        axes[2 * row, 0].set_ylabel(guide + "\nTemporal stddev")
        axes[2 * row + 1, 0].set_ylabel(guide + "\nFrame 255 residual")
        fig.colorbar(noise_image, ax=list(axes[2 * row]), shrink=.75, pad=.01, label="Y stddev")
        fig.colorbar(residual_image, ax=list(axes[2 * row + 1]), shrink=.75, pad=.01, label="Y residual")
    fig.suptitle("Ceiling: identical receivers, 16 px grid / 8 rays, 256 frames; common temporal coverage\n"
                 "Color limits shared across all five stages within each row; maps contain indirect GI only")
    fig.savefig(output / "ceiling-stages.png", dpi=160)
    plt.close(fig)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--same-input", type=Path, help="32-frame same-dispatch-input diagnostic toggle capture")
    args = parser.parse_args()
    analyze(args.capture, args.output, args.same_input)
