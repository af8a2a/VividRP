"""Independent 8192-frame energy confirmation; unchanged 16px / 8-ray renderer.

The white control is a filtered-renderer reference, not physical GI truth.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from LinearGiExport import exr_read
from ProbeAnchorCalibration import LUMA
from TemporalSamplingExperiment import read


def analyze(root, linear):
    manifest = json.loads((root / "capture.json").read_text())
    assert not manifest["errors"]
    assert manifest["experiment"] == "reference-confirm-blocked" and manifest["frameBase"] == 15731
    assert manifest["measuredFrames"] == 8192 and manifest["spacing"] == 16 and manifest["rays"] == 8
    assert not manifest["scene_dirty_before"] and not manifest["scene_dirty_after"]
    frames = list(range(manifest["warmup"], manifest["warmup"] + manifest["measuredFrames"]))
    records = manifest["records"]
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    report = {"scope": "independent 8192-frame phase 15731; same frozen source, not physical truth",
              "equivalence_limit_percent": 1.0, "arms": {}, "pairs": {}}
    values, hashes, matrices = {}, set(), {}
    for mode in ["hybrid", "sdf"]:
        for sampler in ["white", "blocked"]:
            arm = sampler + "-reference-" + mode
            rows = [r for r in records if r["arm"] == arm and "state" in r]
            assert [r["frame"] for r in rows] == frames
            for row in rows:
                s = row["state"]
                assert s["order"] == 8 and s["batch"] == 32 and s["radioSamples"] == 64
                assert s["directionalCache"] and not s["restir"] and s["sun"][:3] == [0, 0, 0] and s["sky"] == 0
                assert s["samplingFlags"] == (0 if sampler == "white" else 8388608)
                assert s["frozenProposal"] == (sampler == "blocked")
                if sampler == "blocked": assert s["proposalHistoryValid"]
            cache = [r for r in records if r["arm"] == arm and r.get("tag") == "radiosity"]
            assert len(cache) == 2
            hashes.update(hashlib.sha256(Path(r["path"]).read_bytes()).hexdigest() for r in cache)
            matrices[arm] = np.asarray([r["vp"] for r in records if r["arm"] == arm and "vp" in r])
            assert matrices[arm].shape == (8192, 16)
            report["arms"][arm] = {}
            for region, oid in [("ceiling", 2), ("floor", 1), ("right", 5)]:
                subset = [r for r in records if r["arm"] == arm and r.get("tag") == "final-" + region]
                assert [r["frame"] for r in subset] == frames
                ys = []
                for r in subset:
                    rgba = read(r)
                    mask = ids[r["y"]:r["y"] + r["height"], r["x"]:r["x"] + r["width"]] == oid
                    ys.append(float(((rgba[..., :3] * np.abs(rgba[..., 3:4])) @ LUMA)[mask].mean()))
                values[arm, region] = np.asarray(ys)
                report["arms"][arm][region] = {"mean_y": float(np.mean(ys)), "frames": len(ys)}
    assert len(hashes) == 1, "Surface Cache changed between arms"
    report["radiosity_sha256"] = next(iter(hashes))
    draws = np.random.default_rng(20261113).integers(0, 64, (4096, 64))
    passed = True
    for mode in ["hybrid", "sdf"]:
        before, after = "white-reference-" + mode, "blocked-reference-" + mode
        assert np.array_equal(matrices[before], matrices[after]), "Camera matrices differ"
        report["pairs"][mode] = {}
        for region in ["ceiling", "floor", "right"]:
            b, a = values[before, region], values[after, region]
            bm, am = b.reshape(64, 128).mean(1), a.reshape(64, 128).mean(1)
            delta = float(100 * (a.mean() / b.mean() - 1))
            interval = np.quantile(100 * (am[draws].mean(1) / bm[draws].mean(1) - 1), [.025, .975])
            ok = abs(delta) <= 1 and np.max(np.abs(interval)) <= 1
            passed &= bool(ok)
            report["pairs"][mode][region] = {"mean_change_percent": delta,
                "block128_conditional_95_percent": interval.tolist(), "equivalence_passed": bool(ok)}
    report["all_regions_passed"] = passed
    (root.parent / "confirmation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"pairs": report["pairs"], "all_regions_passed": passed}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--linear", type=Path, required=True)
    args = parser.parse_args()
    analyze(args.capture, args.linear)
