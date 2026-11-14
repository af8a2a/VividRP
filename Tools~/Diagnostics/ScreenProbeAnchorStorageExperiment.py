"""Qualify native anchor storage and compare real probe / final-pixel signals."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from EmissiveRadiosityConvergence import plane_masks
from GatherHistoryExperiment import unpack_pixel_history
from LinearGiExport import exr_read
from ProbeAnchorCalibration import evaluate_sh, normal_from_packed
from TemporalSamplingExperiment import LUMA, metrics, paired


ARMS = ("half128-a", "half128-b", "covered128", "float128")
FLAGS = dict(zip(ARMS, (0, 0, 268435456, 32)))


def read(row):
    payload = Path(row["path"]).read_bytes()
    assert hashlib.sha256(payload).hexdigest() == row["sha256"], row["path"]
    dtype = "<f4" if row["format"] == "R32G32B32A32_SFloat" else "<f2"
    result = np.frombuffer(payload, dtype).reshape(row["height"], row["width"], 4).astype(np.float32)
    assert np.isfinite(result).all(), row["path"]
    return result


def difference(values, reference):
    delta = np.asarray(values, float) - np.asarray(reference, float)
    return dict(rms=float(np.sqrt(np.mean(delta * delta))),
                mean_field_rms=float(np.sqrt(np.mean(delta.mean(0) ** 2))),
                mean_change_percent=float(100 * (values.mean() / reference.mean() - 1)))


def analyze(root, linear, reference_root):
    if (root / "raw-retention.json").exists():
        raise ValueError("Interior readbacks were archived; retain the saved analysis or capture a new run for full reanalysis.")
    manifest = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    assert manifest["schema"] == "screen-probe-anchor-storage.v1"
    assert manifest["complete"] and not manifest["errors"]
    assert (manifest["spacing"], manifest["rays"], manifest["warmup"], manifest["measuredFrames"]) == (16, 8, 1024, 256)
    qualification = json.loads((root / "qualification.json").read_text(encoding="utf-8"))
    assert qualification["files"] > 0 and not qualification["mismatch"]
    frames = list(range(1024, 1280))
    rows = {(r["arm"], r["frame"], r["tag"]): r for r in manifest["records"] if "tag" in r}
    geometry = read(rows["half128-b", 1024, "geometry"])
    masks = plane_masks(geometry)
    source_hash = rows["half128-b", 1024, "radiosity"]["sha256"]
    geometry_hash = rows["half128-b", 1024, "geometry"]["sha256"]
    vp = next(r["vp"] for r in manifest["records"] if r.get("arm") == "half128-b" and "vp" in r)
    for arm in ARMS:
        states = [r for r in manifest["records"] if r.get("arm") == arm and "state" in r]
        assert [r["frame"] for r in states] == [0, 1023, *frames]
        for row in states:
            s = row["state"]
            assert row["vp"] == vp
            assert s["order"] == 8 and s["batch"] == 32 and s["samples"] == 64 and s["seed"] == 0
            assert s["anchorMaxAge"] == 128 and s["preFilter"] and s["atrous"] == 4 and not s["restir"]
            assert s["guide"] and s["hierarchicalGuide"] and s["sun"] == [0, 0, 0] and s["sky"] == 0
            assert s["nearOcclusion"][1:3] == [0, 0]
            assert s["requestedFlags"] == FLAGS[arm] and s["covered"] == (arm == "covered128")
            assert s["hasHistory"] == (row["frame"] != 0)
            expected_format = "R32G32B32A32_SFloat" if arm == "float128" else "R16G16B16A16_SFloat"
            assert all(s[k] == expected_format for k in ("anchorFormat", "previousFormat", "preparedFormat", "reconstructedFormat"))
        cpu = [r for r in manifest["cpuFrames"] if r["arm"] == arm]
        assert [r["frame"] for r in cpu] == list(range(1280))
        assert all(r["hasHistory"] == (r["frame"] != 0) and r["order"] == 8 and r["batch"] == 32
                   and r["updateCards"] == 0 and r["covered"] == (arm == "covered128") for r in cpu)
        for frame in (0, 1024, 1279):
            assert rows[arm, frame, "radiosity"]["sha256"] == source_hash
            read(rows[arm, frame, "radiosity"])
        for frame in frames:
            assert rows[arm, frame, "geometry"]["sha256"] == geometry_hash
            assert rows[arm, frame, "raw"]["sha256"] == rows["half128-b", frame, "raw"]["sha256"]
    for frame in frames:
        assert rows["half128-b", frame, "statistics"]["sha256"] == rows["covered128", frame, "statistics"]["sha256"]

    reference = json.loads((reference_root / "capture.json").read_text(encoding="utf-8"))
    assert reference["complete"] and not reference["errors"]
    rf = {(r["arm"], r["frame"], r["tag"]): r for r in reference["records"] if "tag" in r}
    # Older independent references predate the per-file hash. Keep their reader separate.
    from TemporalSamplingExperiment import read as read_reference
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
                  frames=256, anchorAge=128, gridReferenceRaysPerProbe=16384,
                  probePlanes={}, pixels={}, historySemantics={}, historyStatus={})
    stack = lambda arm, tag: np.stack([read(rows[arm, f, tag]) for f in frames])
    probe_signals = {}
    for arm in ARMS[1:]:
        history = stack(arm, "anchor-history")[:, :68, :120]
        anchor = stack(arm, "anchor")[:, :68, :120]
        e = history[..., :3] if arm == "covered128" else history[..., :3] * history[..., 3:4]
        if arm == "covered128":
            base = stack("half128-b", "anchor-history")[:, :68, :120]
            assert np.array_equal(base[..., 3], history[..., 3]), "Coverage estimator changed"
            conditional_energy = anchor[..., :3] * anchor[..., 3:4]
            scale = np.maximum(1, np.maximum(abs(e), abs(conditional_energy)))
            relative = abs(e - conditional_energy) / scale
            different = np.mean(np.any(history[..., :3] != anchor[..., :3], -1))
            assert np.quantile(relative, .999) < .002 and different > .25, "Actual covered store not observed"
            report["historySemantics"] = dict(p999ConditionalReconstructionError=float(np.quantile(relative, .999)),
                                              differentFromConditionalFraction=float(different), coverageByteExact=True,
                                              anchorStatisticsByteExact=True)
        sh = stack(arm, "sh")[:, :68].reshape(256, 68, 7, 120, 4).transpose(0, 2, 1, 3, 4).reshape(256, 7, -1, 4)
        normal = np.broadcast_to(normal_from_packed(np.maximum(geometry[:68, :120, 3], 0)).reshape(1, -1, 3), (256, 68*120, 3))
        sh_energy = evaluate_sh(sh, normal) * sh[:, 6, :, 3:4]
        probe_signals[arm] = {"history": e @ LUMA, "prepared": (anchor[..., :3] * anchor[..., 3:4]) @ LUMA,
                              "sh": (sh_energy @ LUMA).reshape(256, 68, 120)}
    for name, mask in masks.items():
        report["probePlanes"][name] = {}
        for stage in ("history", "prepared", "sh"):
            signals = {arm: values[stage][:, mask] for arm, values in probe_signals.items()}
            report["probePlanes"][name][stage] = {arm: dict(metrics(v), fromFp32=difference(v, signals["float128"]),
                fromIndependentReference=difference(v, np.broadcast_to(reference_y[mask], v.shape))) for arm, v in signals.items()}

    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    for name, label in (("left", 4), ("right", 5), ("back", 3), ("floor", 1), ("ceiling", 2)):
        r = rows["half128-b", 1024, "final-" + name]
        x, y, w, h = [r[k] for k in ("x", "y", "width", "height")]
        mask = ids[y:y+h, x:x+w] == label
        assert mask.sum() > 8
        report["pixels"][name] = {}
        report["historyStatus"][name] = {}
        for arm in ARMS[1:]:
            encoded = stack(arm, "history-" + name)[..., 3].astype("<f2")
            age, coverage, status = unpack_pixel_history(encoded)
            report["historyStatus"][name][arm] = dict(ageQuantiles=np.quantile(age[:, mask], [0, .5, 1]).tolist(),
                statusCounts={str(k): int(np.sum(status[:, mask] == k)) for k in np.unique(status[:, mask])})
        for stage in ("resolved", "temporal", "final"):
            signals = {}
            for arm in ARMS[1:]:
                v = stack(arm, stage + "-" + name)
                signals[arm] = ((v[..., :3] * abs(v[..., 3:4])) @ LUMA)[:, mask]
            report["pixels"][name][stage] = {arm: dict(metrics(v), fromFp32=difference(v, signals["float128"])) for arm, v in signals.items()}
            report["pixels"][name][stage]["coveredVsBaseline"] = paired(signals["half128-b"], signals["covered128"])
            report["pixels"][name][stage]["floatVsBaseline"] = paired(signals["half128-b"], signals["float128"])
    (root / "analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--linear", type=Path, default=Path("E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113/linear"))
    parser.add_argument("--reference", type=Path, default=Path("E:/VividRP_GI_Evidence/ScalarAnchorGridReference-20261114-082442-577"))
    args = parser.parse_args()
    result = analyze(args.root, args.linear, args.reference)
    print(json.dumps({"qualifiedInputs": result["qualifiedInputs"], "historySemantics": result["historySemantics"],
        "ceiling": result["pixels"]["ceiling"]["final"], "floor": result["pixels"]["floor"]["final"]}, indent=2))
