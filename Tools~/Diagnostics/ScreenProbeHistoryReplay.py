"""Replay the production four-tap screen-probe history reader from GPU inputs."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import unpack_pixel_history
from GatherHistoryMotion import world
from ProbeAnchorCalibration import LUMA, normal_from_packed
from TemporalSamplingExperiment import read


def replay(data, geometry, lighting, metadata, previous_vp, light_scale):
    position = data[..., 3, :3].astype(float)
    normal = data[..., 4, :3].astype(float)
    previous_pixel = data[..., 5, :2].astype(float)
    expected_depth = data[..., 5, 2].astype(float)
    inverse = np.linalg.inv(previous_vp)
    clip = np.concatenate([position, np.ones((*position.shape[:-1], 1))], -1) @ previous_vp.T
    uv = (previous_pixel+.5)/np.array([1920, 1080])
    eligible = ((data[..., 2, 1] > .5) & (data[..., 5, 3] == 0) & (clip[..., 3] > 0)
                & (clip[..., 2] > 0) & (clip[..., 2] <= clip[..., 3]) & (uv >= 0).all(-1) & (uv < 1).all(-1))
    expected = world(previous_pixel, expected_depth, inverse)
    lower = np.floor(previous_pixel).astype(int)
    fraction = previous_pixel-lower
    shape = data.shape[:2]
    footprint = np.zeros(shape)
    foreground = np.zeros(shape)
    geometry_weight = np.zeros(shape)
    covered_weight = np.zeros(shape)
    age = np.zeros(shape)
    covered = np.zeros((*shape, 3))
    complete = np.ones(shape, bool)
    for x, y in ((0, 0), (1, 0), (0, 1), (1, 1)):
        tap = lower+np.array([x, y])
        weight = (fraction[..., 0] if x else 1-fraction[..., 0])*(fraction[..., 1] if y else 1-fraction[..., 1])
        on_screen = (tap >= 0).all(-1) & (tap < [1920, 1080]).all(-1) & (weight > 0)
        inside = ((tap[..., 0] >= metadata["x"]) & (tap[..., 0] < metadata["x"]+metadata["width"])
                  & (tap[..., 1] >= metadata["y"]) & (tap[..., 1] < metadata["y"]+metadata["height"]))
        complete &= ~on_screen | inside
        ix = np.clip(tap[..., 0]-metadata["x"], 0, metadata["width"]-1)
        iy = np.clip(tap[..., 1]-metadata["y"], 0, metadata["height"]-1)
        packed = geometry[iy, ix, 0]
        depth = (packed & 65535).astype("<u2").view("<f2").astype(float)
        previous_normal = normal_from_packed((packed >> 16).astype(float))
        sample_position = world(tap, depth, inverse)
        expected_tap = world(tap, expected_depth, inverse)
        usable = eligible & on_screen & inside & (depth > 0)
        depth_error = np.linalg.norm(sample_position-expected_tap, axis=-1)
        plane_error = np.abs(np.sum((sample_position-expected)*normal, -1))
        depth_mismatch = depth_error > data[..., 2, 2]
        plane_mismatch = plane_error > data[..., 2, 3]
        foreground += np.where(usable & (depth > expected_depth) & depth_mismatch & plane_mismatch, weight, 0)
        accepted = usable & ~depth_mismatch & ~plane_mismatch & (np.sum(normal*previous_normal, -1) >= .92)
        geometry_weight += np.where(accepted, weight, 0)
        sample = lighting[iy, ix].astype(float)
        sample_age, sample_coverage, _ = unpack_pixel_history(sample[..., 3].astype("<f2"))
        valid_lighting = accepted & (sample[..., 3] > 0)
        contribution = np.where(valid_lighting, weight*sample_coverage, 0)
        covered += np.where(valid_lighting[..., None], weight[..., None]*sample[..., :3]*light_scale, 0)
        covered_weight += contribution
        age += contribution*sample_age
        footprint += np.where(valid_lighting, weight, 0)
    valid = eligible & complete & (footprint >= .35) & (foreground < .25)
    covered /= np.maximum(geometry_weight[..., None], 1e-10)
    coverage = covered_weight/np.maximum(geometry_weight, 1e-10)
    age /= np.maximum(covered_weight, 1e-5)
    return dict(valid=valid, complete=complete, footprint=footprint, foreground=foreground,
                covered=covered, coverage=coverage, age=age)


def analyze(root):
    manifest = json.loads((root/"capture.json").read_text())
    assert manifest["complete"] and not manifest["errors"] and manifest["inspection"]["enabled"]
    assert manifest["rays"] == 8 and manifest["spacing"] == 16
    assert manifest["scene_dirty_before"] == manifest["scene_dirty_after"]
    assert manifest["original_lights_dirty"] == manifest["lights_dirty_after"]
    info = manifest["inspection"]
    frames = list(range(manifest["warmup"], manifest["warmup"]+manifest["measuredFrames"], manifest["captureStride"]))
    records = manifest["records"]
    files = {(r["arm"], r["frame"], r["tag"]): r for r in records if "tag" in r}
    states = {(r["arm"], r["frame"]): r for r in records if "previousVP" in r}
    for r in (r for r in records if "state" in r):
        s = r["state"]
        assert s["order"] == 2 and s["batch"] == 32 and s["radioSamples"] == 64
        assert s["directionalCache"] and not s["restir"] and s["rays"] == 8 and s["spacing"] == 16
    sources = [hashlib.sha256(Path(r["path"]).read_bytes()).hexdigest() for r in records if r.get("tag") == "radiosity"]
    assert len(sources) == 4 and len(set(sources)) == 1
    report = {"scope": "Actual production history reader versus independent CPU replay; no physical GI reference",
              "radiosity_sha256": sources[0], "arms": {}}
    for arm in ("white-motion", "blocked-motion"):
        totals = dict(samples=0, incomplete_halo_samples=0, decision_mismatches=0, native_valid=0,
                      age_over_one_without_native_history=0, native_rejected_current_samples=0,
                      small_foreground_retained=0, native_foreground_rejected=0)
        errors = {k: [] for k in ("footprint", "foreground", "covered", "coverage", "age")}
        ages, statuses, geometric_motion, sample_rows = [], [], [], []
        for f in frames:
            record = files[arm, f, "inspection"]
            data = read(record).reshape(info["height"], info["width"], info["slots"], 4)
            previous_geometry = files[arm, f, "previous-geometry"]
            s = states[arm, f]
            assert s["hasHistory"] and np.allclose(s["historyLightScale"], [1, 1, 1])
            predicted = replay(data, read(previous_geometry), read(files[arm, f, "previous-history"]),
                               previous_geometry, np.asarray(s["previousVP"]).reshape(4, 4), np.asarray(s["historyLightScale"]))
            valid = data[..., 0, 0] > .5
            complete = predicted["complete"]
            current_history = read(files[arm, f, "history-motion"])[::info["pixelStride"], ::info["pixelStride"]]
            age, _, status = unpack_pixel_history(current_history[..., 3].astype("<f2"))
            raw = read(files[arm, f, "resolved-motion"])[::info["pixelStride"], ::info["pixelStride"]]
            temporal = read(files[arm, f, "pixel-temporal-motion"])[::info["pixelStride"], ::info["pixelStride"]]
            current = (raw[..., 3] > 0) & (data[..., 2, 1] > .5)
            totals["samples"] += int(complete.sum())
            totals["incomplete_halo_samples"] += int((~complete).sum())
            totals["decision_mismatches"] += int(((valid != predicted["valid"]) & complete).sum())
            totals["native_valid"] += int((valid & complete).sum())
            totals["age_over_one_without_native_history"] += int((current & ~valid & (age > 1)).sum())
            totals["native_rejected_current_samples"] += int((current & ~valid).sum())
            totals["small_foreground_retained"] += int((current & valid & (data[..., 0, 2] > 0)).sum())
            totals["native_foreground_rejected"] += int((current & (data[..., 0, 2] >= .25)).sum())
            actual = dict(footprint=data[..., 0, 1], foreground=data[..., 0, 2], covered=data[..., 1, :3],
                          coverage=data[..., 1, 3], age=data[..., 2, 0])
            for key in errors:
                mask = valid & predicted["valid"] & complete if key in ("covered", "coverage", "age") else complete
                error = np.abs(actual[key]-predicted[key])[mask]
                errors[key].append(error.ravel())
            ages.append(age[current]); statuses.append(status[current]); geometric_motion.append(data[..., 0, 3][current])
            delta = np.abs(((temporal[..., :3]*np.abs(temporal[..., 3:4]))-(raw[..., :3]*np.abs(raw[..., 3:4]))) @ LUMA)
            rejected = current & ~valid
            sample_rows.append({"frame": f, "native_valid_percent": float(100*np.mean(valid[current])),
                "current_age_mean": float(age[current].mean()),
                "rejected_temporal_minus_resolve_max": float(delta[rejected].max()) if rejected.any() else 0})
        ages, statuses, geometric_motion = map(np.concatenate, (ages, statuses, geometric_motion))
        report["arms"][arm] = {**totals, "current_age_mean": float(ages.mean()),
            "accepted_status_percent": float(100*np.mean(statuses == 1)),
            "object_motion_pixels_p99": float(np.quantile(geometric_motion, .99)),
            "replay_error": {k: {"p99": float(np.quantile(np.concatenate(v), .99)),
                                  "max": float(np.concatenate(v).max())} for k, v in errors.items()},
            "frames": sample_rows}
    report["production_reader_verified"] = all(
        r["incomplete_halo_samples"] == 0 and r["decision_mismatches"] == 0
        and r["age_over_one_without_native_history"] == 0
        and max(f["rejected_temporal_minus_resolve_max"] for f in r["frames"]) == 0
        and all(r["replay_error"][k]["max"] < limit for k, limit in
                [("footprint", 1e-5), ("foreground", 1e-5), ("coverage", 1e-5), ("age", 3e-5), ("covered", .005)])
        for r in report["arms"].values())
    (root.parent/"history-replay.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    assert report["production_reader_verified"], "Inspect the retained replay mismatches before changing history rules"
    print(json.dumps({a: {k: v for k, v in r.items() if k != "frames"} for a, r in report["arms"].items()}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    analyze(parser.parse_args().capture)
