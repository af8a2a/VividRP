"""Qualify a source-settled 4096/2304-render native anchor precision/window capture.

The 256 stored samples span 2304 rendered frames, not 256 consecutive frames.
Reference energy uses an independent same-renderer grid, not physical truth.
"""
import argparse
import json
from pathlib import Path

import numpy as np

from EmissiveRadiosityConvergence import plane_masks
from GatherHistoryExperiment import unpack_pixel_history
from LinearGiExport import exr_read
from ScreenProbeAnchorStorageExperiment import difference, read
from TemporalSamplingExperiment import LUMA, metrics, read as read_reference


ARMS = ("half128-a", "half128-b", "float128", "float512")
AGES = dict(zip(ARMS, (128, 128, 128, 512)))
FRAMES = list(range(4096, 6400, 9))


def paired(before, after):
    """Conditional block dispersion, with 576 rendered frames per block."""
    b, a = metrics(before), metrics(after)
    blocks, size = 4, 64
    bm = before.reshape(blocks, size, -1).mean((1, 2))
    am = after.reshape(blocks, size, -1).mean((1, 2))
    bv = ((before-before.mean(0))**2).reshape(blocks, size, -1).mean((1, 2))
    av = ((after-after.mean(0))**2).reshape(blocks, size, -1).mean((1, 2))
    draws = np.random.default_rng(20261114).integers(0, blocks, (4096, blocks))
    return dict(mean_change_percent=100*(a["mean_y"]/b["mean_y"]-1),
                variance_reduction_percent=100*(1-a["temporal_variance"]/b["temporal_variance"]),
                conditional_block_mean_change_95_percent=np.quantile(100*(am[draws].mean(1)/bm[draws].mean(1)-1), [.025, .975]).tolist(),
                conditional_block_variance_reduction_95_percent=np.quantile(100*(1-av[draws].mean(1)/bv[draws].mean(1)), [.025, .975]).tolist(),
                block_rendered_frames=576, blocks=4)


def analyze(root, linear, reference_root):
    manifest = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    assert manifest["schema"] == "screen-probe-anchor-window.v1"
    assert manifest["complete"] and not manifest["errors"]
    assert (manifest["spacing"], manifest["rays"], manifest["warmup"], manifest["measuredFrames"], manifest["sampleStride"]) == (16, 8, 4096, 2304, 9)
    source = json.loads((root / "source-verification.json").read_text(encoding="utf-8"))
    assert source["unchanged"] and source["files"] > 0 and source["inputs"] == 2
    qualification = json.loads((root / "qualification.json").read_text(encoding="utf-8"))
    assert qualification["files"] > 0 and not qualification["mismatch"]
    rows = {(r["arm"], r["frame"], r["tag"]): r for r in manifest["records"] if "tag" in r}
    assert len(rows) == sum("tag" in r for r in manifest["records"]), "Duplicate GPU checkpoint"
    geometry = read(rows["half128-b", 4096, "geometry"])
    masks = plane_masks(geometry)
    source_hash = rows["half128-b", 4096, "radiosity"]["sha256"]
    geometry_hash = rows["half128-b", 4096, "geometry"]["sha256"]
    canonical = Path(manifest["sourceReference"]) / "expected-radiosity.bin"
    import hashlib
    assert hashlib.sha256(canonical.read_bytes()).hexdigest() == source_hash
    vp = next(r["vp"] for r in manifest["records"] if r.get("arm") == "half128-b" and "vp" in r)
    for arm in ARMS:
        states = [r for r in manifest["records"] if r.get("arm") == arm and "state" in r]
        assert [r["frame"] for r in states] == [0, 4095, *FRAMES, 6399]
        for row in states:
            s = row["state"]
            assert row["vp"] == vp
            assert s["order"] == 8 and s["batch"] == 32 and s["samples"] == 64 and s["seed"] == 0
            assert s["anchorMaxAge"] == s["requestedAge"] == AGES[arm]
            assert s["preFilter"] and s["atrous"] == 4 and not s["restir"]
            assert s["guide"] and s["hierarchicalGuide"] and s["sun"] == [0, 0, 0] and s["sky"] == 0
            assert s["nearOcclusion"][1:3] == [0, 0] and s["traceMode"] == "Hybrid"
            assert s["fullPrecision"] == arm.startswith("float")
            assert s["hasHistory"] == (row["frame"] != 0) and s["epoch"] == (row["frame"] == 0)
            fmt = "R32G32B32A32_SFloat" if arm.startswith("float") else "R16G16B16A16_SFloat"
            assert all(s[k] == fmt for k in ("anchorFormat", "previousFormat", "preparedFormat", "reconstructedFormat"))
        cpu = [r for r in manifest["cpuFrames"] if r["arm"] == arm]
        assert [r["frame"] for r in cpu] == list(range(6400))
        assert all(r["hasHistory"] == (r["frame"] != 0) and r["order"] == 8 and r["batch"] == 32
                   and r["updateCards"] == 0 and r["age"] == AGES[arm]
                   and r["fullPrecision"] == arm.startswith("float") for r in cpu)
        for frame in (0, 4096, 6399):
            assert rows[arm, frame, "radiosity"]["sha256"] == source_hash
            read(rows[arm, frame, "radiosity"])
        for frame in (0, 4095, *FRAMES, 6399):
            assert rows[arm, frame, "geometry"]["sha256"] == geometry_hash
            assert rows[arm, frame, "raw"]["sha256"] == rows["half128-b", frame, "raw"]["sha256"]

    reference = json.loads((reference_root / "capture.json").read_text(encoding="utf-8"))
    assert reference["complete"] and not reference["errors"]
    rf = {(r["arm"], r["frame"], r["tag"]): r for r in reference["records"] if "tag" in r}
    mean = np.zeros((68, 120, 3), np.float64)
    for arm in ("uniform-a", "uniform-b"):
        assert np.array_equal(read_reference(rf[arm, 512, "geometry"]), geometry)
        assert hashlib.sha256(Path(rf[arm, 512, "radiosity"]["path"]).read_bytes()).hexdigest() == source_hash
        for frame in range(512, 1536):
            value = read_reference(rf[arm, frame, "raw"])[:68, :120]
            mean += value[..., :3] * value[..., 3:4]
    reference_y = (mean / 2048) @ LUMA
    report = dict(scope="Actual native Gather, consumed E/pi; independent grid is the same renderer, not physical truth",
                  qualifiedInputs=True, baseline=qualification, sourceSha256=source_hash,
                  warmupRenderedFrames=4096, observedRenderedFrames=2304, storedSamples=256, sampleStride=9,
                  uncertainty="Four conditional 576-render-frame blocks; not independent runs or a bound on long-term drift",
                  gridReferenceRaysPerProbe=16384, probePlanes={}, pixels={}, historyStatus={})
    stack = lambda arm, tag: np.stack([read(rows[arm, f, tag]) for f in FRAMES])
    signals = {}
    ages = {}
    for arm in ARMS[1:]:
        value = stack(arm, "anchor")[:, :68, :120]
        signals[arm] = (value[..., :3] * value[..., 3:4]) @ LUMA
        ages[arm] = stack(arm, "statistics")[:, :68, :120, 0]
    for name, mask in masks.items():
        report["probePlanes"][name] = {}
        for arm in ARMS[1:]:
            v, age = signals[arm][:, mask], ages[arm][:, mask]
            report["probePlanes"][name][arm] = dict(metrics(v),
                fromIndependentReference=difference(v, np.broadcast_to(reference_y[mask], v.shape)),
                ageQuantiles=np.quantile(age, [0, .5, 1]).tolist(),
                fractionAtRequestedAge=float(np.mean(age >= .95*AGES[arm])))
        report["probePlanes"][name]["float512VsBaseline"] = paired(signals["half128-b"][:, mask], signals["float512"][:, mask])

    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    for name, label in (("left", 4), ("right", 5), ("back", 3), ("floor", 1), ("ceiling", 2)):
        r = rows["half128-b", 4096, "final-" + name]
        x, y, w, h = [r[k] for k in ("x", "y", "width", "height")]
        mask = ids[y:y+h, x:x+w] == label
        assert mask.sum() > 8
        report["pixels"][name], report["historyStatus"][name] = {}, {}
        for arm in ARMS[1:]:
            encoded = stack(arm, "history-" + name)[..., 3].astype("<f2")
            age, coverage, status = unpack_pixel_history(encoded)
            report["historyStatus"][name][arm] = dict(ageQuantiles=np.quantile(age[:, mask], [0, .5, 1]).tolist(),
                statusCounts={str(k): int(np.sum(status[:, mask] == k)) for k in np.unique(status[:, mask])})
        for stage in ("resolved", "temporal", "final"):
            rgb, ys = {}, {}
            for arm in ARMS[1:]:
                v = stack(arm, stage + "-" + name)
                rgb[arm] = (v[..., :3] * abs(v[..., 3:4]))[:, mask]
                ys[arm] = rgb[arm] @ LUMA
            result = {}
            for arm, v in ys.items():
                residual = v - v.mean(0)
                quarter = v.reshape(4, 64, -1).mean((1, 2))
                result[arm] = dict(metrics(v), meanRgb=rgb[arm].mean((0, 1)).tolist(),
                    quarterMeanY=quarter.tolist(), quarterMeanRangePercent=float(100*np.ptp(quarter)/v.mean()),
                    lag9FrameCorrelation=float(np.sum(residual[1:]*residual[:-1])/np.sqrt(np.sum(residual[1:]**2)*np.sum(residual[:-1]**2))))
            for arm in ARMS[2:]:
                result[arm+"VsBaseline"] = dict(paired(ys["half128-b"], ys[arm]),
                    meanRgbChangePercent=(100*(rgb[arm].mean((0, 1))/rgb["half128-b"].mean((0, 1))-1)).tolist())
            result["float512VsFloat128"] = paired(ys["float128"], ys["float512"])
            report["pixels"][name][stage] = result
    (root / "analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--linear", type=Path, default=Path("E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113/linear"))
    parser.add_argument("--reference", type=Path, default=Path("E:/VividRP_GI_Evidence/ScalarAnchorGridReference-20261114-082442-577"))
    args = parser.parse_args()
    result = analyze(args.root, args.linear, args.reference)
    print(json.dumps(dict(qualifiedInputs=result["qualifiedInputs"],
        ceiling=result["pixels"]["ceiling"]["final"], floor=result["pixels"]["floor"]["final"]), indent=2))
