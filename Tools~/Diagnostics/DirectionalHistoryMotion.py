"""Compare old/new GI at geometric boundaries; differences are not ground-truth errors."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np
from scipy.ndimage import binary_dilation

from GatherHistoryMotion import world
from ProbeAnchorCalibration import LUMA, normal_from_packed


def analyze(root):
    rows = list(csv.DictReader((root / "frames.csv").open(encoding="utf-8-sig")))
    arms = ["grid5-directional-hybrid-bounce8-spatial-" + mode + "-diagnostics" for mode in ("legacy", "fixed")]
    yy, xx = np.mgrid[480:736, 940:1260]
    pixels = np.stack([xx, yy], -1)
    images, geometries, matrices = [], [], []
    for arm in arms:
        records = sorted((r for r in rows if r["arm"] == arm), key=lambda r: int(r["frame"]))
        assert len(records) == 128
        matrices.append(np.array([np.fromstring(r["vp"], sep=" ").reshape(4, 4) for r in records]))
        geometries.append(np.stack([np.fromfile(root / f"{arm}-{i:02}-edge-geometry.u32", "<u4").reshape(256, 320) for i in range(128)]))
        value = np.stack([np.fromfile(root / f"{arm}-{i:02}-edge-temporal.f16", "<f2").reshape(256, 320, 4) for i in range(128)]).astype(float)
        assert np.isfinite(value).all()
        images.append(value[..., :3] * value[..., 3:4])
    assert np.array_equal(matrices[0], matrices[1])
    assert np.array_equal(geometries[0], geometries[1]), "A/B receiver geometry differs"
    geometry = geometries[0]
    depth = (geometry & 65535).astype("<u2").view("<f2").astype(float)
    normal = normal_from_packed((geometry >> 16).astype(float))
    boundary = np.zeros(depth.shape, bool)
    for i in range(128):
        position = world(pixels, depth[i], np.linalg.inv(matrices[0][i]))
        for axis in (0, 1):
            delta = np.diff(position, axis=axis)
            if axis == 0:
                n, other = normal[i, :-1], normal[i, 1:]
            else:
                n, other = normal[i, :, :-1], normal[i, :, 1:]
            plane = np.maximum(np.abs(np.sum(delta * n, -1)), np.abs(np.sum(delta * other, -1)))
            edge = (plane > .05) | (np.sum(n * other, -1) < .95)
            if axis == 0:
                boundary[i, :-1] |= edge
                boundary[i, 1:] |= edge
            else:
                boundary[i, :, :-1] |= edge
                boundary[i, :, 1:] |= edge
        boundary[i] = binary_dilation(boundary[i], iterations=3)
    old, new = images
    old_y, new_y = old @ LUMA, new @ LUMA
    mask = boundary & (depth > 0) & (old_y > 1) & (new_y > 1)
    assert mask.any()
    # Normalize RGB by its sum to distinguish chroma shifts from energy changes.
    old_chroma = old / np.maximum(old.sum(-1, keepdims=True), 1e-5)
    new_chroma = new / np.maximum(new.sum(-1, keepdims=True), 1e-5)
    chroma = np.linalg.norm(new_chroma - old_chroma, axis=-1)[mask]
    relative_y = ((new_y - old_y) / old_y.clip(1))[mask]
    report = {"scope": "A/B differences at depth/normal boundaries, not ground-truth leakage errors",
              "geometry_and_trajectory_bitwise_equal": True, "boundary_samples": int(mask.sum()),
              "chroma_delta_mean": float(chroma.mean()), "chroma_delta_p99": float(np.percentile(chroma, 99)),
              "relative_y_rms_percent": float(100 * np.sqrt(np.mean(relative_y ** 2))),
              "relative_y_absolute_p99_percent": float(100 * np.percentile(np.abs(relative_y), 99)),
              "aggregate_y_change_percent": float(100 * (new_y[mask].mean() / old_y[mask].mean() - 1))}
    (root / "boundary-summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(2, 3, figsize=(11, 6), constrained_layout=True)
    for col, frame in enumerate((32, 64, 96)):
        for row, value in enumerate(images):
            linear = np.maximum(value[frame][::-1] / 256.0, 0)
            view = (linear / (1 + linear)) ** (1 / 2.2)
            axes[row, col].imshow(view)
            axes[row, col].set_title(("Legacy" if row == 0 else "New") + f" / frame {frame}")
            axes[row, col].axis("off")
    fig.suptitle("GI buffer at panel edges: same trajectory, exposure and display transform")
    fig.savefig(root / "panel-edge-comparison.png", dpi=160)
    plt.close(fig)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    analyze(parser.parse_args().capture)
