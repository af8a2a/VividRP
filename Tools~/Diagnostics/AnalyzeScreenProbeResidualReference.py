"""Qualify a compact current-frame reference and locate Cornell probe residuals.

All radiance inputs are GPU readbacks. The reference shares the renderer and
frozen Surface Cache; it cannot measure physical accuracy or source-cache error.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from EmissiveRadiosityConvergence import plane_masks
from ProbeAnchorCalibration import LUMA, evaluate_sh, normal_from_packed
from TemporalSamplingExperiment import read


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def qualify(root, reference_name="reference"):
    root = root.resolve()
    assert reference_name in ("reference", "candidate-reference", "inside-shell-reference")
    reference = root / reference_name
    capture = load(reference / "capture.json")
    protocol = load(reference / "protocol.json")
    assert capture["schema"] == "scalar-anchor-grid-block-reference.v1"
    assert capture["complete"] and not capture["errors"]
    assert (capture["warmup"], capture["measuredFrames"], capture["spacing"], capture["rays"]) == (512, 1024, 16, 8)
    assert capture["blocks"] == 32 and capture["blockFrames"] == 64 and capture["blockCounts"] == [64] * 32
    assert protocol["shape"] == [32, 68, 120, 5]
    for where in (root, root / "isolated", reference):
        assert load(where / "source-verification.json")["unchanged"]
    actual_bytes = 0
    for row in capture["records"]:
        if not row.get("saved") and "aggregate" not in row:
            continue
        path = Path(row["path"]).resolve()
        assert path.is_relative_to(reference) and digest(path) == row["sha256"]
        actual_bytes += path.stat().st_size
    assert actual_bytes == capture["payloadBytes"] <= capture["payloadBudget"] == protocol["payloadBudget"]
    rows = {(r["arm"], r["frame"], r["tag"]): r for r in capture["records"] if "tag" in r}
    assert len(rows) == sum("tag" in r for r in capture["records"])
    states = {(r["arm"], r["frame"]): r for r in capture["records"] if "state" in r}
    assert len(states) == 2048
    vp = next(iter(states.values()))["vp"]
    for arm in ("uniform-a", "uniform-b"):
        for frame in range(512, 1536):
            state = states[arm, frame]["state"]
            assert states[arm, frame]["vp"] == vp
            assert state["order"] == 8 and state["batch"] == 32 and state["samples"] == 64 and state["seed"] == 0
            assert not state["guide"] and not state["restir"] and state["sun"] == [0, 0, 0] and state["sky"] == 0
            assert rows[arm, frame, "raw"]["format"] == "R16G16B16A16_SFloat"
    sums = np.fromfile(reference / "block-sums.f64", "<f8").reshape(protocol["shape"])
    squares = np.fromfile(reference / "block-squares.f64", "<f8").reshape(protocol["shape"])
    assert np.isfinite(sums).all() and np.isfinite(squares).all()
    assert np.all(squares >= 0) and np.all(squares * 64 >= sums * sums - 1e-7)
    verification = []
    for block, arm in ((0, "uniform-a"), (16, "uniform-b")):
        samples = []
        for frame in range(512, 576):
            raw = read(rows[arm, frame, "raw"])[:68].astype(np.float64)
            rgb = raw[..., :3] * raw[..., 3:4]
            samples.append(np.concatenate([rgb, raw[..., 3:4], (rgb @ LUMA)[..., None]], axis=-1))
        samples = np.asarray(samples)
        cpu_sum, cpu_square = samples.sum(axis=0), (samples * samples).sum(axis=0)
        assert np.allclose(cpu_sum, sums[block], rtol=1e-11, atol=1e-10)
        assert np.allclose(cpu_square, squares[block], rtol=1e-11, atol=1e-10)
        verification.append(dict(arm=arm, frames=64, sumMaxError=float(abs(cpu_sum - sums[block]).max()),
                                 squareMaxError=float(abs(cpu_square - squares[block]).max())))
    snapshot = load(root / "isolated/snapshot.json")
    assert snapshot["success"] and not snapshot["errors"]
    snap = {r["tag"]: r for r in snapshot["records"]}
    for row in snap.values():
        assert digest(Path(row["path"])) == row["sha256"]
    for arm in ("uniform-a", "uniform-b"):
        for frame in (512, 1535):
            assert rows[arm, frame, "radiosity"]["sha256"] == snap["radiosity"]["sha256"] == protocol["sourceSha256"]
            assert rows[arm, frame, "geometry"]["sha256"] == snap["geometry"]["sha256"]
    return capture, snap, sums / 64, squares, verification


def analyze(root):
    capture, snap, blocks, squares, verification = qualify(root)
    geometry = read(snap["geometry"])
    masks = plane_masks(geometry)
    target = blocks.mean(axis=0)
    normal = normal_from_packed(np.maximum(geometry[:68, :120, 3], 0)).reshape(1, -1, 3)
    sh = read(snap["sh"])[:68].reshape(68, 7, 120, 4).transpose(1, 0, 2, 3).reshape(1, 7, -1, 4)
    anchor = read(snap["anchor"])[:68, :120].astype(np.float64)
    prepared = read(snap["prepared"])[:68, :120].astype(np.float64)
    reconstructed = read(snap["reconstructed"])[:68, :120].astype(np.float64)
    signals = {"temporalAnchor": prepared[..., :3] * prepared[..., 3:4],
               "halfStepAnchor": reconstructed[..., :3] * reconstructed[..., 3:4],
               "calibratedShAtNormal": evaluate_sh(sh, normal).reshape(68, 120, 3) * anchor[..., 3:4]}
    planes = {}
    for name, mask in masks.items():
        reference_y = target[..., 4][mask]
        standard_error = blocks[..., 4].var(axis=0, ddof=1) / 32
        a, b = blocks[:16].mean(axis=0)[..., 4][mask], blocks[16:].mean(axis=0)[..., 4][mask]
        planes[name] = dict(probes=int(mask.sum()), referenceMeanY=float(reference_y.mean()),
            referenceBlockStandardErrorRms=float(np.sqrt(standard_error[mask].mean())),
            referenceArmMeanChangePercent=float(100 * (b.mean() / a.mean() - 1)), stages={})
        for stage, values in signals.items():
            delta = (values @ LUMA)[mask] - reference_y
            planes[name]["stages"][stage] = dict(meanChangePercent=float(100 * delta.mean() / reference_y.mean()),
                rms=float(np.sqrt(np.mean(delta ** 2))), p99=float(np.quantile(abs(delta), .99)),
                centeredSpatialRms=float(delta.std()),
                referenceVarianceSubtractedRms=float(np.sqrt(max(0, np.mean(delta ** 2) - standard_error[mask].mean()))))
    ordinary = load(root / "snapshot.json")
    ordinary = {r["tag"]: r for r in ordinary["records"]}
    geometry_equal = all(ordinary[tag]["sha256"] == snap[tag]["sha256"] for tag in ("geometry", "pixel-geometry", "albedo"))
    return dict(schema="screen-probe-residual-reference-analysis.v1", qualified=True,
        referenceRaysPerProbe=16384, aggregationVerification=verification,
        referencePayloadBytes=capture["payloadBytes"], ordinaryAndIsolatedGeometryEqual=geometry_equal, planes=planes,
        limitations=["One age512 production snapshot; this is not a temporal quality comparison",
            "Reference shares the frozen Surface Cache and tracing implementation; source-cache error remains unmeasured",
            "Two independent uniform sequences and 32 blocks estimate reference dispersion, not a physical error bound",
            "Probe-normal comparison precedes full-resolution interpolation, temporal accumulation and spatial denoising",
            "Raw interior frames are represented by hashes and sufficient block statistics; only first blocks and endpoints are saved"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    result = analyze(args.root)
    (args.root / "residual-reference-analysis.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
