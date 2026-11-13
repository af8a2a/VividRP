"""Measure native Screen Probe temporal energy on identical frozen-source inputs."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import unpack_pixel_history
from GatherHistoryMotion import world
from LinearGiExport import exr_read
from ProbeAnchorCalibration import LUMA, normal_from_packed
from TemporalSamplingExperiment import metrics, read


def motion_metrics(arm, frames, files, matrices):
    metadata = files[arm, frames[0], "geometry-motion"]
    x0, y0, w, h = (metadata[k] for k in ["x", "y", "width", "height"])
    yy, xx = np.mgrid[y0:y0+h, x0:x0+w]
    pixels = np.stack([xx, yy], -1)
    errors, ages = [], []
    cycle_square = cycle_abs = cycle_sum = 0.0
    cycle_count = 0
    previous = None
    def get(frame, tag): return read(files[arm, frame, tag + "-motion"])
    for i, frame in enumerate(frames):
        vp = matrices[arm, frame]
        inverse = np.linalg.inv(vp)
        geometry = get(frame, "geometry")[..., 0]
        depth = (geometry & 65535).astype("<u2").view("<f2").astype(float)
        normal = normal_from_packed((geometry >> 16).astype(float))
        if previous is not None:
            old_frame, old_vp, old_inverse, old_depth, old_normal = previous
            position = world(pixels, depth, inverse)
            clip = np.concatenate([position, np.ones((*position.shape[:-1], 1))], -1) @ old_vp.T
            projected = clip[..., :3] / clip[..., 3:4]
            nearest = np.rint(np.stack([(projected[..., 0]+1)*960-.5, (1-projected[..., 1])*540-.5], -1)).astype(int)
            inside = ((clip[..., 3] > 0) & (nearest[..., 0] >= x0) & (nearest[..., 0] < x0+w)
                      & (nearest[..., 1] >= y0) & (nearest[..., 1] < y0+h))
            ix, iy = np.clip(nearest[..., 0]-x0, 0, w-1), np.clip(nearest[..., 1]-y0, 0, h-1)
            delta = world(nearest, old_depth[iy, ix], old_inverse) - world(nearest, projected[..., 2], old_inverse)
            plane = np.maximum(np.abs(np.sum(delta*old_normal[iy, ix], -1)), np.abs(np.sum(delta*normal, -1)))
            temporal, raw = get(frame, "pixel-temporal"), get(frame, "resolved")
            exposed = ((depth > 0) & inside & (old_depth[iy, ix] > projected[..., 2]) & (plane > .075)
                       & (np.linalg.norm(delta, axis=-1) > .10) & (temporal[..., 3] > 0))
            if exposed.any():
                difference = (temporal[..., :3]*temporal[..., 3:4] - raw[..., :3]*raw[..., 3:4]) @ LUMA
                age, _, _ = unpack_pixel_history(get(frame, "history")[..., 3].astype("<f2"))
                errors.extend(difference[exposed].tolist())
                ages.extend(age[exposed].tolist())
        if i >= 176:
            old = frames[i-128]
            assert np.array_equal(vp, matrices[arm, old]), "Repeated pose differs"
            current, prior = get(frame, "final"), get(old, "final")
            delta = ((current[..., :3]*current[..., 3:4]) - (prior[..., :3]*prior[..., 3:4])) @ LUMA
            cycle_square += float(np.sum(delta*delta)); cycle_sum += float(delta.sum())
            cycle_abs = max(cycle_abs, float(np.abs(delta).max())); cycle_count += delta.size
        previous = (frame, vp, inverse, depth, normal)
    assert errors and cycle_count
    error = np.abs(errors)
    return {"exposed_samples": len(errors), "exposed_age_over_one_percent": float(100*np.mean(np.asarray(ages)>1)),
            "temporal_minus_current_p99": float(np.quantile(error, .99)), "temporal_minus_current_max": float(error.max()),
            "matched_pose_cycle_rms": float(np.sqrt(cycle_square/cycle_count)),
            "matched_pose_cycle_mean": cycle_sum/cycle_count, "matched_pose_cycle_max": cycle_abs}


def analyze(root, linear, control=None):
    manifest = json.loads((root / "capture.json").read_text())
    assert not manifest["errors"] and manifest["spacing"] == 16 and manifest["rays"] == 8
    assert not manifest["scene_dirty_before"] and not manifest["scene_dirty_after"]
    if control is not None:
        repeated = json.loads((control / "capture.json").read_text())
        assert not repeated["errors"] and repeated["measuredFrames"] == manifest["measuredFrames"]
        assert repeated["camera"] == manifest["camera"] and repeated["spacing"] == 16 and repeated["rays"] == 8
        assert not repeated["scene_dirty_before"] and not repeated["scene_dirty_after"]
        manifest["records"] = [r for r in manifest["records"] if r["arm"] != "legacy"] + repeated["records"]
    files = {(r["arm"], r["frame"], r["tag"]): r for r in manifest["records"] if "tag" in r}
    states = [r for r in manifest["records"] if "state" in r]
    arms = list(dict.fromkeys(r["arm"] for r in states))
    frames = list(range(manifest["warmup"], manifest["warmup"] + manifest["measuredFrames"]))
    assert len(states) == len(arms) * len(frames)
    for r in states:
        s = r["state"]
        assert s["order"] == 8 and s["batch"] == 32 and not s["restir"] and s["directionalCache"]
        assert s["radioSamples"] == 64
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    report = {"scope": "scene-linear consumed E/pi; identical Resolve and frozen source; no independent physical reference",
              "arms": {}, "source_hashes": {}, "identical_resolve": True, "repeated_control": str(control) if control else None}
    source = None
    baseline_resolve = {}
    matrices = {(r["arm"], r["frame"]): np.asarray(r["vp"]).reshape(4, 4) for r in manifest["records"] if "vp" in r}
    for arm in arms:
        report["arms"][arm] = {}
        cache = [files[arm, f, "radiosity"] for f in [frames[0], frames[-1]]]
        hashes = [hashlib.sha256(Path(r["path"]).read_bytes()).hexdigest() for r in cache]
        assert hashes[0] == hashes[1]
        source = hashes[0] if source is None else source
        assert hashes[0] == source
        report["source_hashes"][arm] = hashes[0]
        if "motion" in arm:
            report["arms"][arm] = motion_metrics(arm, frames, files, matrices)
            continue
        if "light" in arm:
            continue
        for region, object_id in [("ceiling", 2), ("floor", 1), ("right", 5)]:
            first = files[arm, frames[0], "resolved-" + region]
            x, y, w, h = (first[k] for k in ["x", "y", "width", "height"])
            mask = ids[y:y+h, x:x+w] == object_id
            values = {}
            coverages = {}
            for stage in ["resolved", "pixel-temporal", "final"]:
                paths = [files[arm, f, stage + "-" + region] for f in frames]
                if stage == "resolved":
                    current_hashes = [hashlib.sha256(Path(r["path"]).read_bytes()).hexdigest() for r in paths]
                    group = ("guided" if "guided" in arm else "uniform", region)
                    if group not in baseline_resolve:
                        baseline_resolve[group] = current_hashes
                    assert current_hashes == baseline_resolve[group], "Resolve differs in " + arm + "/" + region
                rgba = np.stack([read(r) for r in paths])[:, mask]
                values[stage] = (rgba[..., :3] * rgba[..., 3:4]) @ LUMA
                coverages[stage] = rgba[..., 3]
            stages = {stage: metrics(value) for stage, value in values.items()}
            current_mean = stages["resolved"]["mean_y"]
            for stage in ["pixel-temporal", "final"]:
                stages[stage]["bias_from_resolve_percent"] = 100 * (stages[stage]["mean_y"] / current_mean - 1)
                stages[stage]["coverage_bias_percent"] = float(100 * (coverages[stage].mean() / coverages["resolved"].mean() - 1))
            history = np.stack([read(files[arm, f, "history-" + region]) for f in frames])[:, mask]
            age, _, _ = unpack_pixel_history(history[..., 3].astype("<f2"))
            report["arms"][arm][region] = {"stages": stages, "age_mean": float(age.mean())}
    motion_arms = [arm for arm in arms if "motion" in arm]
    if motion_arms:
        first = motion_arms[0]
        for arm in motion_arms[1:]:
            assert all(np.array_equal(matrices[first, f], matrices[arm, f]) for f in frames)
            assert all(Path(files[first, f, "geometry-motion"]["path"]).read_bytes()
                       == Path(files[arm, f, "geometry-motion"]["path"]).read_bytes() for f in frames)
    (root.parent / (root.name + "-analysis.json")).write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({arm: regions if "motion" in arm else {region: {"energy_bias_percent": data["stages"]["pixel-temporal"]["bias_from_resolve_percent"],
                "final_rms": data["stages"]["final"]["temporal_rms"], "age": data["age_mean"]}
                for region, data in regions.items()} for arm, regions in report["arms"].items()}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--linear", type=Path, default=Path("E:/VividRP_Reborn/Logs/LinearGI-20261113/linear"))
    parser.add_argument("--control", type=Path)
    args = parser.parse_args()
    analyze(args.capture, args.linear, args.control)
