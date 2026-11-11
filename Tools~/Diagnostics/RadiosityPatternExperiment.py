"""Separate frozen radiosity sampling error from Gather noise using paired cosine rays."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np
from scipy.ndimage import gaussian_filter

from ProbeAnchorCalibration import LUMA


CONFIGS = ((0, 16, 0), (1, 16, 0), (2, 16, 0), (8, 16, 0),
           (1, 1024, 0), (2, 1024, 0), (8, 1024, 0), (8, 4096, 0), (8, 16, 1))


def arm_name(config):
    order, rays, seed = config
    return f"grid5-directional-hybrid-bounce{order}-rays{rays}-seed{seed}-spatial-diagnostics"


def correlation(a, b):
    return float(np.corrcoef(a.ravel(), b.ravel())[0, 1]) if min(a.std(), b.std()) > 1e-10 else None


def paired_pattern(low, reference, height, width):
    n = min(len(low), len(reference))
    low, reference = low[:n].astype(float), reference[:n].astype(float)
    gain = low.mean() / max(reference.mean(), 1e-10)
    delta = low - reference
    shape_delta = low - gain * reference
    mean_delta = shape_delta.mean(0)
    mc_variance = shape_delta.var(0, ddof=1) / n
    smooth = gaussian_filter(mean_delta.reshape(height, width), 1.0, mode="reflect")
    first, second = shape_delta[:n // 2].mean(0), shape_delta[n // 2:].mean(0)
    raw_rmse = float(np.sqrt(np.mean(delta.mean(0) ** 2)))
    return {"paired_frames": n, "mean_low": float(low.mean()), "mean_reference": float(reference.mean()),
            "mean_energy_error_percent": float((gain - 1) * 100), "raw_mean_map_rmse": raw_rmse,
            "gain_removed_shape_rmse": float(np.sqrt(np.mean(mean_delta ** 2))),
            "paired_gather_mean_se_rms": float(np.sqrt(mc_variance.mean())),
            "debiased_shape_rmse": float(np.sqrt(max(0.0, np.mean(mean_delta ** 2) - mc_variance.mean()))),
            "lowpass_shape_rmse": float(np.sqrt(np.mean(smooth ** 2))),
            "first_second_half_error_correlation": correlation(first, second)}, mean_delta, first, second


def owned_cards(atlas, cards=37):
    image = atlas.reshape(-1, 128, 4)
    return np.stack([image[(i // 16) * 8:(i // 16 + 1) * 8, (i % 16) * 8:(i % 16 + 1) * 8] for i in range(cards)])


def analyze(root, output):
    output.mkdir(parents=True, exist_ok=True)
    with (root / "regions.csv").open(encoding="utf-8-sig", newline="") as stream:
        regions = [{k: v if k == "roi" else int(v) for k, v in row.items()} for row in csv.DictReader(stream)]
    count = sum(r["w"] * r["h"] for r in regions)
    signals, pixels, atlases, row_data = {}, {}, {}, []
    report = {"capture": str(root), "arms": {}, "pairs": {}, "source_pairs": {}}
    reference_geometry = None
    canonical_work = None
    for config in CONFIGS:
        arm = arm_name(config)
        frames = 1024 if config[0] == 8 else 512
        paths = sorted(root.glob(arm + "-*-raw.f16"), key=lambda p: int(p.name.split("-")[-2]))
        if [int(p.name.split("-")[-2]) for p in paths] != list(range(frames)):
            raise ValueError(f"Incomplete capture {arm}: {len(paths)} frames")
        raw = np.stack([np.fromfile(p, "<f2").reshape(count, 4) for p in paths]).astype(float)
        energy = raw[..., :3] * raw[..., 3:4]
        if not np.isfinite(energy).all():
            raise ValueError("Non-finite raw Gather contribution")
        signals[config] = energy @ LUMA
        gi = np.stack([np.fromfile(root / f"{arm}-{i:02}-gi.f16", "<f2").reshape(48, 96, 4)
                       for i in range(frames)]).astype(np.float32)
        pixels[config] = np.einsum("thwc,c->thw", gi[..., :3] * gi[..., 3:4], LUMA).astype(np.float32)
        geometry = np.fromfile(root / f"{arm}-00-geometry.f32", "<f4").reshape(count, 4)
        if reference_geometry is None:
            reference_geometry = geometry
        if (np.max(np.abs(geometry[:, :3] - reference_geometry[:, :3])) > 1e-5
                or not np.array_equal(geometry[:, 3], reference_geometry[:, 3])):
            raise ValueError("Receiver geometry changed")
        arm_report = {"orders": config[0], "radiosity_rays_per_texel": config[1],
                      "radiosity_seed": 912731 if config[2] else 0,
                      "gather_frames": frames, "gather_rays_per_probe": frames * 8, "regions": {}}
        if config[0] > 0:
            first = owned_cards(np.fromfile(root / f"{arm}-00-radiosity.f16", "<f2"))
            last = owned_cards(np.fromfile(root / f"{arm}-{frames - 1:02}-radiosity.f16", "<f2"))
            frozen = np.array_equal(first.view(np.uint16), last.view(np.uint16))
            if not frozen or not np.isfinite(first).all():
                raise ValueError(f"Unstable radiosity atlas {arm}")
            work = np.fromfile(root / f"{arm}-00-work.u32", "<u4")
            pairs = work[int(work[0]):int(work[0]) + 74].reshape(37, 2)
            if canonical_work is None:
                canonical_work = pairs
            if not np.array_equal(pairs, canonical_work):
                raise ValueError("Radiosity card ordering changed")
            atlases[config] = first.astype(float)
            arm_report.update({"owned_atlas_frozen_bitwise": frozen, "covered_card_texels": int(np.count_nonzero(first[..., 3]))})
        for region in regions:
            span = slice(region["offset"], region["offset"] + region["w"] * region["h"])
            y = signals[config][:, span]
            residual = y - y.mean(0)
            result = {"mean_y": float(y.mean()), "mean_rgb": energy[:, span].mean((0, 1)).tolist(),
                      "raw_temporal_rms": float(np.sqrt(np.mean(residual ** 2))),
                      "gather_mean_se_rms": float(np.sqrt(np.mean(y.var(0, ddof=1) / frames)))}
            arm_report["regions"][region["roi"]] = result
            row_data.append({"arm": arm, "roi": region["roi"], "mean_y": result["mean_y"],
                             "raw_temporal_rms": result["raw_temporal_rms"], "gather_mean_se_rms": result["gather_mean_se_rms"]})
        py = pixels[config]
        arm_report["final_ceiling_roi"] = {"mean_y": float(py.mean()),
            "temporal_rms": float(np.sqrt(np.mean((py - py.mean(0)) ** 2)))}
        report["arms"][arm] = arm_report
        np.savez_compressed(output / (arm + ".npz"), raw_mean_rgb=energy.mean(0), raw_y=signals[config],
                            final_mean_y=py.mean(0), geometry=geometry,
                            radiosity=atlases[config] if config in atlases else np.empty(0))

    comparisons = (((1, 16, 0), (1, 1024, 0)), ((2, 16, 0), (2, 1024, 0)),
                   ((8, 16, 0), (8, 4096, 0)), ((8, 1024, 0), (8, 4096, 0)),
                   ((8, 16, 1), (8, 4096, 0)), ((8, 16, 0), (8, 16, 1)))
    maps = {}
    for low, ref in comparisons:
        key = f"{low[0]}x{low[1]}s{low[2]}_vs_{ref[0]}x{ref[1]}s{ref[2]}"
        paired_regions = {}
        for r in regions:
            span = slice(r["offset"], r["offset"] + r["w"] * r["h"])
            result, shape, first, second = paired_pattern(signals[low][:, span], signals[ref][:, span], r["h"], r["w"])
            paired_regions[r["roi"]] = result
            if r["roi"] == "ceiling":
                maps[key] = (shape.reshape(r["h"], r["w"]), first.reshape(r["h"], r["w"]), second.reshape(r["h"], r["w"]))
        pixel_result, _, _, _ = paired_pattern(pixels[low].reshape(len(pixels[low]), -1),
                                              pixels[ref].reshape(len(pixels[ref]), -1), 48, 96)
        report["pairs"][key] = {"regions": paired_regions, "final_ceiling_roi": pixel_result}
        a, b = atlases[low], atlases[ref]
        mask = b[..., 3] > 0
        if not np.array_equal(a[..., 3] > 0, mask):
            raise ValueError("Card coverage changed with radiosity sampling")
        ay, by = a[..., :3] @ LUMA, b[..., :3] @ LUMA
        difference = ay - by
        report["source_pairs"][key] = {"covered_texels": int(mask.sum()),
            "mean_low": float(ay[mask].mean()), "mean_reference": float(by[mask].mean()),
            "card_texel_error_rmse": float(np.sqrt(np.mean(difference[mask] ** 2))),
            "card_texel_error_p95": float(np.percentile(np.abs(difference[mask]), 95)),
            "card_texel_error_correlation": correlation(ay[mask], by[mask])}

    (output / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    with (output / "means.csv").open("w", encoding="utf8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(row_data[0]))
        writer.writeheader()
        writer.writerows(row_data)

    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    ceiling = regions[0]
    h, w, offset = ceiling["h"], ceiling["w"], ceiling["offset"]
    fig, axes = plt.subplots(3, 4, figsize=(16, 9), constrained_layout=True)
    means = [signals[(n, 16, 0)][:, offset:offset + h * w].mean(0).reshape(h, w) for n in (0, 1, 2, 8)]
    for col, (n, value) in enumerate(zip((0, 1, 2, 8), means)):
        im = axes[0, col].imshow(value, origin="lower", aspect="auto", cmap="viridis", vmin=0, vmax=max(a.max() for a in means))
        axes[0, col].set_title(f"Radiosity {n} orders / 16 rays\nMean Y {value.mean():.3f}")
    fig.colorbar(im, ax=list(axes[0]), shrink=.65, label="Gather mean Y")
    keys = ("1x16s0_vs_1x1024s0", "2x16s0_vs_2x1024s0", "8x16s0_vs_8x4096s0", "8x16s1_vs_8x4096s0")
    limit = max(np.abs(maps[k][0]).max() for k in keys)
    for col, key in enumerate(keys):
        im = axes[1, col].imshow(maps[key][0], origin="lower", aspect="auto", cmap="RdBu_r", vmin=-limit, vmax=limit)
        axes[1, col].set_title(key.replace("_vs_", " vs ") + "\nMean gain removed")
    fig.colorbar(im, ax=list(axes[1]), shrink=.65, label="Fixed shape error Y")
    values = (maps[keys[2]][1], maps[keys[2]][2], maps["8x1024s0_vs_8x4096s0"][0], maps["8x16s0_vs_8x16s1"][0])
    limit = max(np.abs(a).max() for a in values)
    for col, (value, title) in enumerate(zip(values, ("8 orders: error first half", "8 orders: error second half", "1024 vs 4096 radiosity rays", "Only radiosity seed changed"))):
        im = axes[2, col].imshow(value, origin="lower", aspect="auto", cmap="RdBu_r", vmin=-limit, vmax=limit)
        axes[2, col].set_title(title)
    fig.colorbar(im, ax=list(axes[2]), shrink=.65, label="Gain-removed mean error Y")
    for ax in axes.flat:
        ax.set_xticks([])
        ax.set_yticks([])
    fig.suptitle("Cornell ceiling: fixed Surface Cache experiment; identical Gather ray sequences\n"
                 "Order changes alter real illumination. Same-order high-sample and seed controls isolate cache sampling error.")
    fig.savefig(output / "ceiling-fixed-patterns.png", dpi=160)
    plt.close(fig)

    fig, axes = plt.subplots(1, 4, figsize=(16, 4), constrained_layout=True)
    images = []
    for rays in (16, 1024, 4096):
        cards = atlases[(8, rays, 0)]
        image = np.full((24, 128), np.nan)
        for card in range(37):
            value = cards[card, ..., :3] @ LUMA
            image[(card // 16) * 8:(card // 16 + 1) * 8, (card % 16) * 8:(card % 16 + 1) * 8] = np.where(cards[card, ..., 3] > 0, value, np.nan)
        images.append(image)
    maximum = max(np.nanmax(i) for i in images)
    for i in range(3):
        im = axes[i].imshow(images[i], cmap="magma", vmin=0, vmax=maximum)
        axes[i].set_title(f"8 orders, {(16, 1024, 4096)[i]} rays/texel")
    fig.colorbar(im, ax=list(axes[:3]), shrink=.7, label="Card E / pi")
    error = images[0] - images[2]
    im = axes[3].imshow(error, cmap="RdBu_r", vmin=-np.nanmax(abs(error)), vmax=np.nanmax(abs(error)))
    axes[3].set_title("16 minus 4096 rays")
    fig.colorbar(im, ax=axes[3], shrink=.7, label="Source error")
    for ax in axes:
        ax.set_xticks([])
        ax.set_yticks([])
    fig.suptitle("Actual frozen radiosity atlases: same 37 cards and coverage; only angular samples change")
    fig.savefig(output / "surface-cache-atlases.png", dpi=160)
    plt.close(fig)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    analyze(args.capture, args.output)
