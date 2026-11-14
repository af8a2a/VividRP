"""Separate frozen emissive Radiosity error from eight-ray Gather variance.

The high-batch arm is the same renderer with an independent cache seed, not a
path-traced physical truth. No fitted gain is applied to stored GI or images.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from scipy.ndimage import gaussian_filter

from ProbeAnchorCalibration import LUMA, normal_from_packed
from TemporalSamplingExperiment import read


def plane_masks(geometry):
    g = geometry[:68, :120]
    n = normal_from_packed(np.maximum(g[..., 3], 0))
    valid = g[..., 3] >= 0
    # Exclude the exterior end caps of the open box and the two interior blocks.
    return {name: valid & (abs(g[..., axis] - location) < .003) & (n[..., axis] * sign > .99)
            & (g[..., 2] > .05) for name, axis, location, sign in [
                ("left", 0, -2, 1), ("right", 0, 2, -1), ("back", 2, 4, -1),
                ("floor", 1, 0, 1), ("ceiling", 1, 3, -1)]}


def compare(low, reference, mask):
    delta = low - reference
    mean = delta.mean(0, dtype=np.float64)
    # Diagnostic shape separation only; report the real, unfitted energy too.
    gain = low.mean() / reference.mean()
    shape = low - gain * reference
    mean_shape = shape.mean(0, dtype=np.float64)
    half0, half1 = shape[:128].mean(0), shape[128:].mean(0)
    block_means = shape.reshape(8, 32, -1).mean(1)
    se_variance = block_means.var(0, ddof=1) / 8
    field = np.zeros(mask.shape); field[mask] = mean_shape
    weights = gaussian_filter(mask.astype(float), 1)
    smooth = gaussian_filter(field, 1) / np.maximum(weights, 1e-8)
    correlation = float(np.corrcoef(half0, half1)[0, 1]) if min(half0.std(), half1.std()) > 1e-10 else None
    return {"low_mean_y": float(low.mean()), "reference_mean_y": float(reference.mean()),
            "mean_energy_change_percent": float(100 * (gain - 1)),
            "unfitted_mean_map_rmse": float(np.sqrt(np.mean(mean ** 2))),
            "unfitted_mean_map_absolute_p99": float(np.quantile(abs(mean), .99)),
            "shape_rmse": float(np.sqrt(np.mean(mean_shape ** 2))),
            "shape_block32_mean_se_rms": float(np.sqrt(se_variance.mean())),
            "debiased_shape_rmse": float(np.sqrt(max(0, np.mean(mean_shape ** 2) - se_variance.mean()))),
            "lowpass_shape_rmse": float(np.sqrt(np.mean(smooth[mask] ** 2))),
            "first_second_half_shape_correlation": correlation}, field


def analyze(root, partial=False):
    manifest_path = root / ("capture-progress.json" if partial else "capture.json")
    m = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    if not partial:
        assert m["complete"] and (m["width"], m["height"], m["spacing"], m["rays"]) == (1920, 1080, 16, 8)
    assert not m["errors"] and m["warmup"] == 512 and m["measuredFrames"] == 256
    frames = list(range(512, 768))
    files = {(r["arm"], r["frame"], r["tag"]): r for r in m["records"] if "tag" in r}
    configs = {"b32-a": (32, 0), "b32-b": (32, 0), "b128": (128, 0), "b512-reference": (512, 912731)}
    arms = [a for a in configs if all((a, f, "raw") in files for f in frames)]
    assert arms
    if not partial:
        assert arms == list(configs)
    report = {"scope": "Pure-emissive Cornell, uniform 16px/8-ray Gather; source cache sample count only",
              "reference_scope": "Independent-seed high-batch same-renderer reference, not physical truth",
              "complete": not partial, "arms": {}, "pairs": {}}
    signals = {}; masks = None; canonical = None
    for arm in arms:
        batches, seed = configs[arm]
        states = [r["state"] for r in m["records"] if r.get("arm") == arm and "state" in r]
        assert len(states) == 256 and all(s["order"] == 8 and s["batch"] == batches and s["samples"] == 64
            and s["seed"] == seed and not s["guide"] and not s["restir"] and s["atrous"] == 4
            and s["sun"] == [0, 0, 0] and s["sky"] == 0 for s in states)
        first = Path(files[arm, 512, "radiosity"]["path"]).read_bytes()
        assert first == Path(files[arm, 767, "radiosity"]["path"]).read_bytes(), "Source moved during measurement"
        geometry = read(files[arm, 512, "geometry"])
        if canonical is None:
            canonical = geometry
            masks = plane_masks(geometry)
        assert all(np.array_equal(read(files[arm, f, "geometry"]), canonical) for f in frames), "Receiver geometry differs"
        raw = np.stack([read(files[arm, f, "raw"])[:68] for f in frames])
        y = (raw[..., :3] * raw[..., 3:4]) @ LUMA
        signals[arm] = {name: y[:, mask] for name, mask in masks.items()}
        report["arms"][arm] = {"source_sha256": hashlib.sha256(first).hexdigest(), "planes": {}}
        for name, mask in masks.items():
            values = signals[arm][name]
            report["arms"][arm]["planes"][name] = {"probes": int(mask.sum()), "mean_y": float(values.mean()),
                "temporal_rms": float(np.sqrt(np.mean((values - values.mean(0)) ** 2))),
                "coverage_mean": float(raw[..., 3][:, mask].mean())}
        if arm != arms[0]:
            assert all(np.array_equal(read(files[arms[0], f, "raw"])[..., 3], read(files[arm, f, "raw"])[..., 3]) for f in frames), "Source sampling changed geometric coverage"
    figures = []
    if "b128" in arms:
        reference = "b512-reference" if "b512-reference" in arms else "b128"
        for arm in [a for a in ["b32-b", "b128"] if a != reference]:
            report["pairs"][arm + "-vs-" + reference] = {}
            for name, mask in masks.items():
                row, field = compare(signals[arm][name], signals[reference][name], mask)
                report["pairs"][arm + "-vs-" + reference][name] = row
                if name in ("left", "right", "back"):
                    figures.append((arm, name, np.where(mask, field, np.nan)))
    if "b32-b" in arms:
        qa = json.loads((root / "qualification.json").read_text())
        assert qa["files"] > 0 and not qa["mismatch"]
        assert report["arms"]["b32-a"]["source_sha256"] == report["arms"]["b32-b"]["source_sha256"]
        report["aa"] = qa
    output = root / ("partial-analysis.json" if partial else "analysis.json")
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    if figures:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        fig, axes = plt.subplots(len(figures)//3, 3, figsize=(11, 3.8*(len(figures)//3)), squeeze=False, layout="constrained")
        limit = max(float(np.nanquantile(abs(f), .99)) for _, _, f in figures)
        for ax, (arm, name, field) in zip(axes.flat, figures):
            shown = ax.imshow(field[::-1], cmap="coolwarm", vmin=-limit, vmax=limit, interpolation="nearest")
            ax.set_title(arm + " " + name); ax.set_xticks([]); ax.set_yticks([])
        fig.colorbar(shown, ax=axes, shrink=.7, label="Gain-separated E/pi Y residual, diagnostic only")
        fig.suptitle("Paired mean residual to " + reference + "; no fitted gain in real energy metrics")
        fig.savefig(root / ("partial-patterns.png" if partial else "patterns.png"), dpi=160); plt.close(fig)
    print(json.dumps({"complete": report["complete"], "arms": arms, "pairs": report["pairs"]}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--partial", action="store_true")
    args = parser.parse_args()
    analyze(args.capture, args.partial)
