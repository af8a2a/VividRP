"""Compare scalar-history candidates to independent, matched-pose Resolve means."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from GatherHistoryMotion import world
from LinearGiExport import exr_read
from ProbeAnchorCalibration import LUMA, normal_from_packed
from TemporalSamplingExperiment import read


def mean_reference(files, arm, tag):
    blocks = []
    for first in range(512, 1536, 64):
        total = None
        for frame in range(first, first + 64):
            v = read(files[arm, frame, tag])
            value = v[..., :3] * abs(v[..., 3:4])
            if total is None:
                total = np.zeros(value.shape, np.float64)
            total += value
        blocks.append(total / 64)
    blocks = np.stack(blocks)
    mean = blocks.mean(0)
    variance = (blocks @ LUMA).var(0, ddof=1) / len(blocks)
    return mean, variance, blocks


def error(value, reference, reference_variance, mask):
    delta = (value - reference) @ LUMA
    selected = delta[mask]
    mse = np.mean(selected**2)
    return {"pixels": int(mask.sum()), "rmse": float(np.sqrt(mse)),
            "absolute_p99": float(np.quantile(abs(selected), .99)), "absolute_max": float(abs(selected).max()),
            "signed_mean": float(selected.mean()),
            "reference_mean_se_rms": float(np.sqrt(reference_variance[mask].mean())),
            "reference_variance_subtracted_rmse": float(np.sqrt(max(0, mse - reference_variance[mask].mean())))}


def recent_exposure(files, matrices, pose, arm="age32-motion", first_frame=512):
    """Current and three preceding newly visible strips, reprojected to this pose."""
    x, y, w, h = 800, 350, 320, 256
    yy, xx = np.mgrid[y:y+h, x:x+w]; pixels = np.stack([xx, yy], -1)
    geometry = {}; depth = {}; normals = {}; vp = {}; inverse = {}
    for point in range(pose - 4, pose + 1):
        frame = first_frame + point
        geometry[point] = read(files[arm, frame, "pixel-geometry-motion"])[..., 0]
        depth[point] = (geometry[point] & 65535).astype("<u2").view("<f2").astype(float)
        normals[point] = normal_from_packed((geometry[point] >> 16).astype(float))
        vp[point] = matrices[arm, frame]; inverse[point] = np.linalg.inv(vp[point])
    selected = np.zeros((h, w), bool)
    for point in range(pose - 3, pose + 1):
        p = world(pixels, depth[point], inverse[point])
        clip = np.concatenate([p, np.ones((*p.shape[:-1], 1))], -1) @ vp[point-1].T
        q = clip[..., :3] / clip[..., 3:4]
        near = np.rint(np.stack([(q[..., 0]+1)*960-.5, (1-q[..., 1])*540-.5], -1)).astype(int)
        inside = ((depth[point] > 0) & (clip[..., 3] > 0) & (near[..., 0] >= x) & (near[..., 0] < x+w)
                  & (near[..., 1] >= y) & (near[..., 1] < y+h))
        ix, iy = np.clip(near[..., 0]-x, 0, w-1), np.clip(near[..., 1]-y, 0, h-1)
        old = depth[point-1][iy, ix]
        difference = world(near, old, inverse[point-1]) - world(near, q[..., 2], inverse[point-1])
        plane = np.maximum(abs(np.sum(difference*normals[point], -1)), abs(np.sum(difference*normals[point-1][iy, ix], -1)))
        fresh = inside & (old > q[..., 2]) & (plane > .075) & (np.linalg.norm(difference, axis=-1) > .10)
        if point == pose:
            selected |= fresh
            continue
        points = p[fresh]
        projected = np.concatenate([points, np.ones((len(points), 1))], -1) @ vp[pose].T
        ndc = projected[:, :3] / projected[:, 3:4]
        dest = np.rint(np.stack([(ndc[:, 0]+1)*960-.5, (1-ndc[:, 1])*540-.5], -1)).astype(int)
        valid = ((projected[:, 3] > 0) & (dest[:, 0] >= x) & (dest[:, 0] < x+w) & (dest[:, 1] >= y) & (dest[:, 1] < y+h))
        dest = dest[valid]; points = points[valid]; old_normals = normals[point][fresh][valid]
        if not len(points):
            continue
        ax, ay = dest[:, 0]-x, dest[:, 1]-y
        actual = world(dest, depth[pose][ay, ax], inverse[pose])
        delta = actual - points
        same = ((depth[pose][ay, ax] > 0) & (abs(np.sum(delta*old_normals, -1)) < .075)
                & (np.linalg.norm(delta, axis=-1) < .10) & (np.sum(old_normals*normals[pose][ay, ax], -1) > .92))
        selected[ay[same], ax[same]] = True
    return selected


def analyze(main, reference, linear, partial=False):
    m = json.loads((main / "capture.json").read_text())
    r = json.loads((reference / ("capture-progress.json" if partial else "capture.json")).read_text())
    assert m["complete"] and not m["errors"] and not r["errors"]
    if not partial:
        assert r["complete"] and r["schema"] in ("scalar-anchor-pose-reference.v1", "scalar-anchor-pose-reference-combined.v1")
        if r["schema"] == "scalar-anchor-pose-reference-combined.v1":
            assert r["isComposite"] and r["matched_production_files"] > 2000 and len(r["source_runs"]) == 2
    mf = {(v["arm"], v["frame"], v["tag"]): v for v in m["records"] if "tag" in v}
    rf = {(v["arm"], v["frame"], v["tag"]): v for v in r["records"] if "tag" in v}
    matrices = {(v["arm"], v["frame"]): np.asarray(v["vp"]).reshape(4, 4) for v in m["records"] if "vp" in v}
    rm = {(v["arm"], v["frame"]): np.asarray(v["vp"]).reshape(4, 4) for v in r["records"] if "vp" in v}
    report = {"scope": "Independent uniform current-scalar Resolve, 1024 frames / 8192 rays per probe; same frozen cache",
              "limits": "Same-renderer Monte Carlo target before pixel denoising, not independent physical truth", "static": {}, "poses": {}}
    baseline = Path(mf["age32-b", 512, "radiosity"]["path"]).read_bytes()
    poses = [p for p in [0, 24, 48, 80, 104] if all((f"pose{p:03}", f, "resolved-motion") in rf for f in range(512, 1536))]
    if not partial:
        assert poses == [0, 24, 48, 80, 104]
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    pooled = {a: [] for a in ["age32-motion", "age128-motion"]}
    pooled_blocks = []; pooled_values = {a: [] for a in pooled}
    for pose in poses:
        arm = f"pose{pose:03}"; main_arm = "age32-b" if pose == 0 else "age32-motion"
        assert all(np.array_equal(rm[arm, f], matrices[main_arm, 512+pose]) for f in range(512, 1536)), "Reference pose differs"
        source = Path(rf[arm, 512, "radiosity"]["path"]).read_bytes()
        assert source == baseline and source == Path(rf[arm, 1535, "radiosity"]["path"]).read_bytes(), "Reference source differs"
        assert all(np.array_equal(read(rf[arm, f, "geometry"]), read(mf[main_arm, 512+pose, "geometry"])) for f in [512, 1535]), "Reference probe receivers differ"
        states = [v["state"] for v in r["records"] if v.get("arm") == arm and "state" in v]
        assert len(states) == 1024 and all(v["order"] == 8 and v["batch"] == 32 and v["samples"] == 64 and not v["guide"]
            and v["seed"] == 0 and not v["restir"] and v["sun"] == [0, 0, 0] and v["sky"] == 0 for v in states)
        for f in [512, 1535]:
            stats = read(rf[arm, f, "statistics"])
            assert np.all(stats[..., 0][stats[..., 0] > 0] == 1), "Reference retained scalar history"
        mean, variance, blocks = mean_reference(rf, arm, "resolved-motion")
        if pose == 0:
            for region, label in [("left", 4), ("right", 5), ("back", 3), ("floor", 1), ("ceiling", 2)]:
                target, sem_variance, block = mean_reference(rf, arm, "resolved-" + region)
                meta = mf["age32-b", 512, "final-" + region]; x, y, w, h = (meta[k] for k in ("x", "y", "width", "height"))
                mask = ids[y:y+h, x:x+w] == label
                report["static"][region] = {}
                for candidate in ["age32-b", "age128"]:
                    report["static"][region][candidate] = {}
                    for stage in ["resolved", "temporal", "final"]:
                        averages = []
                        for f in range(512, 768):
                            value = read(mf[candidate, f, stage+"-"+region]); averages.append(value[..., :3]*abs(value[..., 3:4]))
                        values = np.stack(averages); average = values.mean(0, dtype=float)
                        row = error(average, target, sem_variance, mask)
                        target_rgb = target[mask].mean(0); candidate_rgb = average[mask].mean(0)
                        row.update(mean_energy_error_percent=float(100*((candidate_rgb@LUMA)/(target_rgb@LUMA)-1)),
                            mean_rgb_error_percent=(100*(candidate_rgb-target_rgb)/np.maximum(abs(target_rgb), 1e-8)).tolist(),
                            per_frame_rmse=float(np.sqrt(np.mean((((values-target)@LUMA)[:, mask])**2))))
                        report["static"][region][candidate][stage] = row
            continue
        recent = recent_exposure(mf, matrices, pose)
        assert recent.sum() > 0, "No recent exposure at reference pose"
        packed = read(mf["age32-motion", 512+pose, "pixel-geometry-motion"])[..., 0]
        visible = (packed & 65535) != 0
        report["poses"][str(pose)] = {"recently_exposed_pixels": int(recent.sum()), "candidates": {}}
        pooled_blocks.append((blocks @ LUMA)[:, recent])
        for candidate in ["age32-motion", "age128-motion"]:
            report["poses"][str(pose)]["candidates"][candidate] = {}
            for stage in ["resolved", "temporal", "final"]:
                value = read(mf[candidate, 512+pose, stage+"-motion"]); value = value[..., :3]*abs(value[..., 3:4])
                report["poses"][str(pose)]["candidates"][candidate][stage] = {
                    "whole_roi": error(value, mean, variance, visible), "recent_exposure": error(value, mean, variance, recent)}
                if stage == "final":
                    pooled[candidate].extend(((value-mean)@LUMA)[recent].tolist())
                    pooled_values[candidate].append((value @ LUMA)[recent])
    report["pooled_recent_exposure"] = {a: {"pixels": len(v), "rmse": float(np.sqrt(np.mean(np.asarray(v)**2))),
        "absolute_p99": float(np.quantile(abs(np.asarray(v)), .99)), "absolute_max": float(max(abs(np.asarray(v)))),
        "signed_mean": float(np.mean(v))} for a, v in pooled.items() if v}
    if pooled_blocks:
        # Resample common 64-frame blocks jointly across pixels and poses. This
        # retains spatial correlation and shared frame-sequence correlation.
        block_y = np.concatenate(pooled_blocks, axis=1)
        weights = np.random.default_rng(20261114).multinomial(16, [1/16]*16, 4096) / 16
        reference_means = weights @ block_y
        errors = [np.concatenate(pooled_values[a])[None, :] - reference_means for a in pooled_values]
        rms = [np.sqrt(np.mean(e**2, axis=1)) for e in errors]
        tails = [np.quantile(abs(e), .99, axis=1) for e in errors]
        report["reference_uncertainty_comparison"] = {
            "rms_change_percent_conditional_ci95": np.quantile(100*(rms[1]/rms[0]-1), [.025, .975]).tolist(),
            "p99_change_percent_conditional_ci95": np.quantile(100*(tails[1]/tails[0]-1), [.025, .975]).tolist(),
            "scope": "Reference block sampling uncertainty only; fixed captured candidate frames, same-renderer target"}
    (reference / ("partial-reference-analysis.json" if partial else "reference-analysis.json")).write_text(json.dumps(report, indent=2))
    print(json.dumps({"completed_poses": poses, "static_energy": {region: {a: row["final"]["mean_energy_error_percent"] for a, row in arms.items()}
        for region, arms in report["static"].items()}, "pooled_recent_exposure": report["pooled_recent_exposure"]}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("main", type=Path); parser.add_argument("reference", type=Path); parser.add_argument("--linear", type=Path, required=True)
    parser.add_argument("--partial", action="store_true"); args = parser.parse_args()
    analyze(args.main, args.reference, args.linear, args.partial)
