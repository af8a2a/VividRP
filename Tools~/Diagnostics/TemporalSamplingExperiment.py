"""Compare native 16px / 8-ray white and temporally stratified GPU captures.

Scene-linear consumed E/pi; regional temporal noise, not a physical GI reference.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import unpack_pixel_history
from GatherHistoryMotion import world
from LinearGiExport import exr_read
from ProbeAnchorCalibration import LUMA, evaluate_sh, normal_from_packed


def read(record):
    dtype = {"R16G16B16A16_SFloat": "<f2", "R32G32B32A32_SFloat": "<f4", "R32_UInt": "<u4"}[record["format"]]
    channels = 1 if record["format"] == "R32_UInt" else 4
    result = np.fromfile(record["path"], dtype).reshape(record["height"], record["width"], channels)
    assert np.isfinite(result).all(), record["path"]
    return result if channels == 1 else result.astype(np.float32)


def metrics(y):
    y = np.asarray(y, np.float64)
    residual = y - y.mean(0)
    variance = np.mean(residual ** 2)
    return {"mean_y": float(y.mean()), "temporal_rms": float(np.sqrt(variance)),
            "temporal_variance": float(variance),
            "relative_rms_percent": float(100 * np.sqrt(variance) / max(abs(y.mean()), 1e-10))}


def paired(before, after):
    b, a = metrics(before), metrics(after)
    # Conditional block resampling describes this capture's dispersion only.
    blocks = len(before) // 32
    bm = before.reshape(blocks, 32, -1).mean((1, 2))
    am = after.reshape(blocks, 32, -1).mean((1, 2))
    bv = ((before-before.mean(0))**2).reshape(blocks, 32, -1).mean((1, 2))
    av = ((after-after.mean(0))**2).reshape(blocks, 32, -1).mean((1, 2))
    draws = np.random.default_rng(20261113).integers(0, blocks, (4096, blocks))
    mean_change = 100 * (am[draws].mean(1) / bm[draws].mean(1) - 1)
    reduction = 100 * (1 - av[draws].mean(1) / bv[draws].mean(1))
    return {"mean_change_percent": 100 * (a["mean_y"] / b["mean_y"] - 1),
            "variance_reduction_percent": 100 * (1 - a["temporal_variance"] / b["temporal_variance"]),
            "relative_variance_reduction_percent": 100 * (1 - (a["relative_rms_percent"] / b["relative_rms_percent"]) ** 2),
            "block_bootstrap_mean_change_95_percent": np.quantile(mean_change, [.025, .975]).tolist(),
            "block_bootstrap_variance_reduction_95_percent": np.quantile(reduction, [.025, .975]).tolist()}


def analyze(root, linear):
    manifest = json.loads((root / "capture.json").read_text())
    assert not manifest["errors"] and manifest["spacing"] == 16 and manifest["rays"] == 8
    records = manifest["records"]
    files = {(r["arm"], r["frame"], r["tag"]): r for r in records if "tag" in r}
    frames = list(range(manifest["warmup"], manifest["warmup"] + manifest["measuredFrames"]))
    states = [r for r in records if "state" in r]
    arms = list(dict.fromkeys(r["arm"] for r in states))
    assert len(states) == len(arms) * len(frames)
    for record in states:
        s = record["state"]
        assert s["directionalCache"] and not s["restir"] and s["order"] == 8 and s["batch"] == 32, s
        assert s["radioSamples"] == 64 and s["sun"][:3] == [0, 0, 0] and s["sky"] == 0
    get = lambda a, f, tag: read(files[a, f, tag])
    stack = lambda a, tag: np.stack([get(a, f, tag) for f in frames])
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    report = {"scope": "256 consecutive frames, fixed source; temporal noise and pixel history inheritance, not physical truth",
              "contract": "linear consumed E/pi, no exposure or gain fitting", "arms": {}, "pairs": {}}
    all_y = {}
    matrices = {(r["arm"], r["frame"]): np.asarray(r["vp"]).reshape(4, 4) for r in records if "vp" in r}
    source_hashes = {}
    for arm in arms:
        first = Path(files[arm, frames[0], "radiosity"]["path"]).read_bytes()
        last = Path(files[arm, frames[-1], "radiosity"]["path"]).read_bytes()
        assert first == last, "Radiosity changed during " + arm
        source_hashes[arm] = hashlib.sha256(first).hexdigest()
        report["arms"][arm] = {"radiosity_sha256": source_hashes[arm], "pixels": {}, "probes": {}}
        if "motion" in arm:
            continue
        for region, object_id in [("ceiling", 2), ("floor", 1), ("right", 5)]:
            meta = files[arm, frames[0], "final-" + region]
            x, y, w, h = (meta[k] for k in ("x", "y", "width", "height"))
            mask = ids[y:y+h, x:x+w] == object_id
            assert mask.any()
            for stage in ["resolved", "pixel-temporal", "final"]:
                rgba = stack(arm, stage + "-" + region)
                y_values = ((rgba[..., :3] * np.abs(rgba[..., 3:4])) @ LUMA)[:, mask]
                all_y[arm, stage, region] = y_values
                report["arms"][arm]["pixels"][stage + "-" + region] = metrics(y_values)
        geometry = stack(arm, "probe-geometry").reshape(len(frames), -1, 4)
        normal = normal_from_packed(np.maximum(0, geometry[..., 3]))
        valid = ((geometry[..., 3] >= 0) & (np.abs(geometry[..., 1] - 3) < .002) & (normal[..., 1] < -.99)).all(0)
        sh = stack(arm, "sh").reshape(len(frames), 7, 22, 120, 4).transpose(0, 2, 1, 3, 4)
        sh = sh[:, :, :, 48:72].reshape(len(frames), 22, -1, 4)
        for stage, values in [("raw", sh[:, 21]), ("anchor", stack(arm, "anchor").reshape(len(frames), -1, 4))]:
            y_values = (values[..., :3] * values[..., 3:4]) @ LUMA
            all_y[arm, stage, "probe"] = y_values[:, valid]
            report["arms"][arm]["probes"][stage] = metrics(y_values[:, valid])
        for stage, offset in [("pre_sh", 7), ("post_sh", 0)]:
            block = sh[:, offset:offset+7]
            y_values = (evaluate_sh(block, normal) * block[:, 6, :, 3:4]) @ LUMA
            all_y[arm, stage, "probe"] = y_values[:, valid]
            report["arms"][arm]["probes"][stage] = metrics(y_values[:, valid])
    assert len(set(source_hashes.values())) == 1, "Source differs between arms"
    for mode in ["hybrid", "sdf"]:
        b, a = "white-" + mode, "sobol-" + mode
        assert all(np.array_equal(matrices[b, f], matrices[a, f]) for f in frames)
        report["pairs"][mode] = {stage + "-" + region: paired(y, all_y[a, stage, region])
                                  for (arm, stage, region), y in all_y.items() if arm == b}
    report["motion"] = {}
    motion_geometry = []
    for arm in ["white-motion", "sobol-motion"]:
        vp = np.stack([matrices[arm, f] for f in frames])
        inverse = np.linalg.inv(vp)
        geometry = stack(arm, "geometry-motion")[..., 0]
        motion_geometry.append(geometry)
        depth = (geometry & 65535).astype("<u2").view("<f2").astype(float)
        normals = normal_from_packed((geometry >> 16).astype(float))
        meta = files[arm, frames[0], "geometry-motion"]
        x0, y0, w, h = (meta[k] for k in ("x", "y", "width", "height"))
        yy, xx = np.mgrid[y0:y0+h, x0:x0+w]
        pixels = np.stack([xx, yy], -1)
        raw = stack(arm, "resolved-motion")
        temporal = stack(arm, "pixel-temporal-motion")
        history = stack(arm, "history-motion")
        age, _, status = unpack_pixel_history(history[..., 3].astype("<f2"))
        exposed = np.zeros(depth.shape, bool)
        for i in range(1, len(frames)):
            position = world(pixels, depth[i], inverse[i])
            clip = np.concatenate([position, np.ones((*position.shape[:-1], 1))], -1) @ vp[i-1].T
            projected = clip[..., :3] / clip[..., 3:4]
            nearest = np.rint(np.stack([(projected[..., 0]+1)*960-.5, (1-projected[..., 1])*540-.5], -1)).astype(int)
            valid = ((depth[i] > 0) & (clip[..., 3] > 0) & (nearest[..., 0] >= x0) & (nearest[..., 0] < x0+w)
                     & (nearest[..., 1] >= y0) & (nearest[..., 1] < y0+h))
            ix, iy = np.clip(nearest[..., 0]-x0, 0, w-1), np.clip(nearest[..., 1]-y0, 0, h-1)
            old_depth = depth[i-1, iy, ix]
            delta = world(nearest, old_depth, inverse[i-1]) - world(nearest, projected[..., 2], inverse[i-1])
            plane = np.maximum(np.abs(np.sum(delta * normals[i-1, iy, ix], -1)), np.abs(np.sum(delta * normals[i], -1)))
            exposed[i] = valid & (old_depth > projected[..., 2]) & (plane > .075) & (np.linalg.norm(delta, axis=-1) > .10) & (temporal[i, ..., 3] != 0)
        raw_y = (raw[..., :3] * np.abs(raw[..., 3:4])) @ LUMA
        temporal_y = (temporal[..., :3] * np.abs(temporal[..., 3:4])) @ LUMA
        error = np.abs(temporal_y - raw_y)[exposed]
        assert error.size, "Motion did not exercise disocclusion"
        assert np.array_equal(vp[:128], vp[128:]), "Repeated trajectory poses differ"
        assert np.array_equal(geometry[:128], geometry[128:]), "Repeated trajectory geometry differs"
        final = stack(arm, "final-motion")
        final_y = (final[..., :3] * np.abs(final[..., 3:4])) @ LUMA
        cycle_delta = final_y[176:] - final_y[48:128]
        report["motion"][arm] = {"newly_exposed_samples": int(exposed.sum()),
                                  "age_over_one_percent": float(100*np.mean(age[exposed] > 1)),
                                  "accepted_history_percent": float(100*np.mean(status[exposed] == 1)),
                                  "temporal_minus_current_p99": float(np.quantile(error, .99)),
                                  "temporal_minus_current_max": float(error.max()),
                                  "matched_pose_cycle_delta_rms": float(np.sqrt(np.mean(cycle_delta**2))),
                                  "matched_pose_cycle_delta_absolute_p99": float(np.quantile(np.abs(cycle_delta), .99)),
                                  "matched_pose_cycle_delta_mean": float(cycle_delta.mean())}
    assert np.array_equal(*motion_geometry), "A/B motion geometry differs"
    assert all(np.array_equal(matrices["white-motion", f], matrices["sobol-motion", f]) for f in frames)
    (root.parent / "analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report["pairs"], indent=2))
    print(json.dumps(report["motion"], indent=2))


def analyze_references(root, linear):
    """Long-sequence energy equivalence to the unchanged filtered pipeline.

    This is an independent frame sequence, not an independent physical renderer.
    """
    short = json.loads((root / "analysis.json").read_text())
    canonical = next(iter(short["arms"].values()))["radiosity_sha256"]
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    report = {"scope": "2048-frame regional means; same frozen Surface Cache, independent frame phase; not physical truth",
              "diagnostic_equivalence_limit_percent": 1.0, "arms": {}, "pairs": {}}
    arrays = {}
    for folder in ["reference", "reference-sdf", "reference-sobol"]:
        manifest = json.loads((root / folder / "capture.json").read_text())
        assert not manifest["errors"] and manifest["measuredFrames"] == 2048
        assert manifest["spacing"] == 16 and manifest["rays"] == 8
        assert not manifest["scene_dirty_before"] and not manifest["scene_dirty_after"]
        for r in (r for r in manifest["records"] if "state" in r):
            s = r["state"]
            assert s["order"] == 8 and s["batch"] == 32 and s["radioSamples"] == 64 and s["directionalCache"] and not s["restir"]
        arms = list(dict.fromkeys(r["arm"] for r in manifest["records"]))
        for arm in arms:
            name = folder + "/" + arm
            cache = [r for r in manifest["records"] if r["arm"] == arm and r.get("tag") == "radiosity"]
            assert len(cache) == 2
            assert all(hashlib.sha256(Path(r["path"]).read_bytes()).hexdigest() == canonical for r in cache)
            report["arms"][name] = {}
            for region, object_id in [("ceiling", 2), ("floor", 1), ("right", 5)]:
                records = [r for r in manifest["records"] if r["arm"] == arm and r.get("tag") == "final-" + region]
                assert [r["frame"] for r in records] == list(range(512, 2560))
                means = []
                for r in records:
                    rgba = read(r)
                    mask = ids[r["y"]:r["y"]+r["height"], r["x"]:r["x"]+r["width"]] == object_id
                    means.append(float(((rgba[..., :3] * np.abs(rgba[..., 3:4])) @ LUMA)[mask].mean()))
                arrays[name, region] = np.asarray(means)
                report["arms"][name][region] = {"mean_y": float(np.mean(means)), "frames": len(means)}
    draws = np.random.default_rng(20261113).integers(0, 16, (4096, 16))
    all_passed = True
    for mode, before in [("hybrid", "reference/white-reference"), ("sdf", "reference-sdf/white-reference")]:
        after = "reference-sobol/sobol-reference-" + mode
        report["pairs"][mode] = {}
        for region in ["ceiling", "floor", "right"]:
            b, a = arrays[before, region], arrays[after, region]
            bm, am = b.reshape(16, 128).mean(1), a.reshape(16, 128).mean(1)
            change = 100 * (a.mean() / b.mean() - 1)
            interval = np.quantile(100 * (am[draws].mean(1) / bm[draws].mean(1) - 1), [.025, .975])
            # Require the conditional interval, as well as the observed mean, to fit the declared limit.
            passed = bool(np.max(np.abs(interval)) <= 1 and abs(change) <= 1)
            all_passed &= passed
            report["pairs"][mode][region] = {"mean_change_percent": float(change),
                "block128_conditional_95_percent": interval.tolist(), "equivalence_passed": passed}
    report["all_regions_passed"] = all_passed
    (root / "energy-reference.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report["pairs"], indent=2))
    print("All long-sequence energy intervals within 1%:", all_passed)


def analyze_uniform(root, linear):
    manifest = json.loads((root / "uniform-isolation/capture.json").read_text())
    assert not manifest["errors"] and manifest["measuredFrames"] == 1024
    assert manifest["spacing"] == 16 and manifest["rays"] == 8
    files = {(r["arm"], r["frame"], r["tag"]): r for r in manifest["records"] if "tag" in r}
    for r in (r for r in manifest["records"] if "state" in r):
        s = r["state"]
        assert not s["guide"] and not s["hierarchical"] and s["order"] == 8 and s["batch"] == 32
    frames = list(range(512, 1536))
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    canonical = next(iter(json.loads((root / "analysis.json").read_text())["arms"].values()))["radiosity_sha256"]
    values, geometries = {}, {}
    report = {"scope": "1024 frames, uniform cosine strata, SDF Only, identical frozen source", "arms": {}, "pairs": {}}
    for arm in ["white-uniform-sdf", "sobol-uniform-sdf"]:
        stack = lambda tag: np.stack([read(files[arm, f, tag]) for f in frames])
        cache = [r for r in manifest["records"] if r["arm"] == arm and r.get("tag") == "radiosity"]
        assert len(cache) == 2 and all(hashlib.sha256(Path(r["path"]).read_bytes()).hexdigest() == canonical for r in cache)
        report["arms"][arm] = {}
        for region, object_id in [("ceiling", 2), ("floor", 1), ("right", 5)]:
            suffix = "" if region == "ceiling" else "-" + region
            geometry = stack("probe-geometry" + suffix)
            geometries[arm, region] = geometry
            normal = normal_from_packed(np.maximum(geometry[..., 3], 0))
            valid = geometry[..., 3] >= 0
            if region == "ceiling": valid &= (np.abs(geometry[..., 1]-3) < .002) & (normal[..., 1] < -.99)
            elif region == "floor": valid &= (np.abs(geometry[..., 1]) < .002) & (normal[..., 1] > .99)
            else: valid &= (np.abs(geometry[..., 0]-2) < .002) & (normal[..., 0] < -.99)
            valid = valid.all(0)
            assert valid.any(), region
            rgba = stack("hit-lighting" + suffix)
            raw = ((rgba[..., :3] * rgba[..., 3:4]) @ LUMA)[:, valid]
            values[arm, "raw-" + region] = raw
            report["arms"][arm]["raw-" + region] = metrics(raw)
            meta = files[arm, frames[0], "final-" + region]
            mask = ids[meta["y"]:meta["y"]+meta["height"], meta["x"]:meta["x"]+meta["width"]] == object_id
            for stage in ["resolved", "pixel-temporal", "final"]:
                rgba = stack(stage + "-" + region)
                y = ((rgba[..., :3] * np.abs(rgba[..., 3:4])) @ LUMA)[:, mask]
                values[arm, stage + "-" + region] = y
                report["arms"][arm][stage + "-" + region] = metrics(y)
    for region in ["ceiling", "floor", "right"]:
        assert np.array_equal(geometries["white-uniform-sdf", region], geometries["sobol-uniform-sdf", region])
        for stage in ["raw", "resolved", "pixel-temporal", "final"]:
            key = stage + "-" + region
            report["pairs"][key] = paired(values["white-uniform-sdf", key], values["sobol-uniform-sdf", key])
    (root / "uniform-isolation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report["pairs"], indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--linear", type=Path, default=Path("E:/VividRP_Reborn/Logs/LinearGI-20261113/linear"))
    parser.add_argument("--reference-only", action="store_true")
    parser.add_argument("--uniform-only", action="store_true")
    args = parser.parse_args()
    if args.uniform_only:
        analyze_uniform(args.capture, args.linear)
    elif args.reference_only:
        analyze_references(args.capture, args.linear)
    else:
        analyze(args.capture, args.linear)
