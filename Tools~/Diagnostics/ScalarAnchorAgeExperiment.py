"""Native scalar anchor convergence; fixed source, matched trajectory, no gain fitting."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from EmissiveRadiosityConvergence import plane_masks
from GatherHistoryExperiment import unpack_pixel_history
from GatherHistoryMotion import world
from LinearGiExport import exr_read
from ProbeAnchorCalibration import LUMA, normal_from_packed
from TemporalSamplingExperiment import read, metrics, paired


def motion(arm, files, matrices, first_frame=512):
    frames = range(first_frame, first_frame+256)
    vp = np.stack([matrices[arm, f] for f in frames]); inverse = np.linalg.inv(vp)
    assert np.array_equal(vp[:128], vp[128:])
    geometry = np.stack([read(files[arm, f, "pixel-geometry-motion"])[..., 0] for f in frames])
    assert np.array_equal(geometry[:128], geometry[128:])
    depth = (geometry & 65535).astype("<u2").view("<f2").astype(float)
    normals = normal_from_packed((geometry >> 16).astype(float))
    meta = files[arm, first_frame, "pixel-geometry-motion"]
    x, y, w, h = (meta[k] for k in ("x", "y", "width", "height"))
    yy, xx = np.mgrid[y:y+h, x:x+w]; pixels = np.stack([xx, yy], -1)
    exposure_errors = []; probe_ages = []; probe_status = []
    old_age = old_accepted = exposed_count = 0; final_y = []
    for index, frame in enumerate(frames):
        current = read(files[arm, frame, "resolved-motion"])
        temporal = read(files[arm, frame, "temporal-motion"])
        history = read(files[arm, frame, "history-motion"])
        final = read(files[arm, frame, "final-motion"])
        final_y.append((final[..., :3] * abs(final[..., 3:4])) @ LUMA)
        if index == 0:
            continue
        position = world(pixels, depth[index], inverse[index])
        clip = np.concatenate([position, np.ones((*position.shape[:-1], 1))], -1) @ vp[index-1].T
        projected = clip[..., :3] / clip[..., 3:4]
        nearest = np.rint(np.stack([(projected[..., 0]+1)*960-.5, (1-projected[..., 1])*540-.5], -1)).astype(int)
        valid = ((depth[index] > 0) & (clip[..., 3] > 0) & (nearest[..., 0] >= x) & (nearest[..., 0] < x+w)
                 & (nearest[..., 1] >= y) & (nearest[..., 1] < y+h))
        ix, iy = np.clip(nearest[..., 0]-x, 0, w-1), np.clip(nearest[..., 1]-y, 0, h-1)
        old_depth = depth[index-1, iy, ix]
        delta = world(nearest, old_depth, inverse[index-1]) - world(nearest, projected[..., 2], inverse[index-1])
        plane = np.maximum(abs(np.sum(delta * normals[index-1, iy, ix], -1)), abs(np.sum(delta * normals[index], -1)))
        exposed = valid & (old_depth > projected[..., 2]) & (plane > .075) & (np.linalg.norm(delta, axis=-1) > .10) & (temporal[..., 3] != 0)
        age, _, status = unpack_pixel_history(history[..., 3].astype("<f2"))
        exposed_count += int(exposed.sum()); old_age += int((age[exposed] > 1).sum()); old_accepted += int((status[exposed] == 1).sum())
        current_y = (current[..., :3] * abs(current[..., 3:4])) @ LUMA
        temporal_y = (temporal[..., :3] * abs(temporal[..., 3:4])) @ LUMA
        exposure_errors.extend(abs(temporal_y - current_y)[exposed].tolist())

        # The same nearest-depth diagnostic at the actual base/adaptive probe point.
        g = read(files[arm, frame, "geometry"]); s = read(files[arm, frame, "statistics"])
        p = np.concatenate([g[..., :3], np.ones((*g.shape[:-1], 1))], -1) @ vp[index-1].T
        q = p[..., :3] / np.maximum(p[..., 3:4], 1e-8)
        near = np.rint(np.stack([(q[..., 0]+1)*960-.5, (1-q[..., 1])*540-.5], -1)).astype(int)
        inside = ((g[..., 3] >= 0) & (s[..., 0] > 0) & (p[..., 3] > 0) & (near[..., 0] >= x) & (near[..., 0] < x+w)
                  & (near[..., 1] >= y) & (near[..., 1] < y+h))
        px, py = np.clip(near[..., 0]-x, 0, w-1), np.clip(near[..., 1]-y, 0, h-1)
        d = depth[index-1, py, px]
        difference = world(near, d, inverse[index-1]) - world(near, q[..., 2], inverse[index-1])
        n = normal_from_packed(np.maximum(g[..., 3], 0))
        rejected = inside & (d > q[..., 2]) & (abs(np.sum(difference*n, -1)) > .075) & (np.linalg.norm(difference, axis=-1) > .10)
        probe_ages.extend(s[..., 0][rejected].tolist()); probe_status.extend(s[..., 3][rejected].tolist())
    assert exposed_count > 0 and exposure_errors and probe_ages
    values = np.stack(final_y)
    cycle = values[176:] - values[48:128]
    return {"newly_exposed_pixels": exposed_count, "old_pixel_age": old_age, "old_pixel_accepted": old_accepted,
            "temporal_current_p99": float(np.quantile(exposure_errors, .99)), "temporal_current_max": float(max(exposure_errors)),
            "newly_exposed_probes": len(probe_ages), "old_probe_age": int(np.count_nonzero(np.asarray(probe_ages) > 1)),
            "probe_rejection_status_count": int(np.count_nonzero(np.asarray(probe_status) == 3)),
            "cycle_rms": float(np.sqrt(np.mean(cycle**2))), "cycle_abs_p99": float(np.quantile(abs(cycle), .99)),
            "geometry_sha256": hashlib.sha256(geometry).hexdigest()}, cycle


def analyze(root, linear, partial=False):
    m = json.loads((root / ("capture-progress.json" if partial else "capture.json")).read_text())
    if not partial:
        assert m["complete"] and m["schema"] == "scalar-anchor-age.v1"
        assert (m["width"], m["height"], m["spacing"], m["rays"]) == (1920, 1080, 16, 8)
    assert not m["errors"] and m["warmup"] == 512 and m["measuredFrames"] == 256
    frames = range(512, 768)
    files = {(r["arm"], r["frame"], r["tag"]): r for r in m["records"] if "tag" in r}
    states = [r for r in m["records"] if "state" in r]
    arms = list(dict.fromkeys(r["arm"] for r in states))
    arms = [a for a in arms if all((a, f, "raw") in files for f in frames)]
    expected = ["age32-a", "age32-b", "age128", "age32-motion", "age128-motion"]
    assert arms == expected[:len(arms)]
    if not partial:
        assert arms == expected
    report = {"scope": "Fixed Cornell source; consumed linear E/pi without gain fitting",
              "complete": not partial, "arms": {}, "static": {}, "motion": {}}
    matrices = {(r["arm"], r["frame"]): np.asarray(r["vp"]).reshape(4, 4) for r in m["records"] if "vp" in r}
    source_hashes = []; geometry0 = None; masks = None
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    stack = lambda arm, tag: np.stack([read(files[arm, f, tag]) for f in frames])
    for arm in arms:
        rows = [r["state"] for r in states if r["arm"] == arm]
        expected_age = 128 if arm.startswith("age128") else 32
        assert len(rows) == 256 and all(s["order"] == 8 and s["batch"] == 32 and s["samples"] == 64 and s["seed"] == 0
            and s["anchorMaxAge"] == expected_age and s["guide"] and not s["restir"] and s["atrous"] == 4
            and s["sun"] == [0, 0, 0] and s["sky"] == 0 for s in rows)
        source = Path(files[arm, 512, "radiosity"]["path"]).read_bytes()
        assert source == Path(files[arm, 767, "radiosity"]["path"]).read_bytes()
        source_hashes.append(hashlib.sha256(source).hexdigest())
        report["arms"][arm] = {"source_sha256": source_hashes[-1], "planes": {}}
        if "motion" in arm:
            continue
        geometry = read(files[arm, 512, "geometry"])
        if geometry0 is None:
            geometry0 = geometry; masks = plane_masks(geometry)
        assert all(np.array_equal(read(files[arm, f, "geometry"]), geometry0) for f in frames)
        anchors = stack(arm, "anchor")[:, :68, :120]; stats = stack(arm, "statistics")[:, :68, :120]
        ay = (anchors[..., :3] * anchors[..., 3:4]) @ LUMA
        for name, mask in masks.items():
            report["arms"][arm]["planes"][name] = dict(metrics(ay[:, mask]), age_quantiles=np.quantile(stats[..., 0][:, mask], [0, .1, .5, .9, 1]).tolist())
    assert len(set(source_hashes)) == 1, "Source differs between arms"
    if "age32-b" in arms:
        qa = json.loads((root / "qualification.json").read_text()); assert qa["files"] > 0 and not qa["mismatch"]
        report["aa"] = qa
    if "age128" in arms:
        a, b = "age32-b", "age128"
        assert all(np.array_equal(read(files[a, f, "raw"]), read(files[b, f, "raw"])) for f in frames), "Scalar change altered raw tracing"
        for region, label in [("left", 4), ("right", 5), ("back", 3), ("floor", 1), ("ceiling", 2)]:
            meta = files[a, 512, "final-" + region]; x, y, w, h = (meta[k] for k in ("x", "y", "width", "height"))
            mask = ids[y:y+h, x:x+w] == label; assert mask.sum() > 8
            report["static"][region] = {}
            for stage in ["resolved", "temporal", "final"]:
                values = [stack(arm, stage + "-" + region) for arm in [a, b]]
                rgb = [v[..., :3] * abs(v[..., 3:4]) for v in values]
                row = paired(*[(v @ LUMA)[:, mask] for v in rgb])
                means = [v[:, mask].mean((0, 1), dtype=float) for v in rgb]
                row["mean_rgb_change_percent"] = (100*(means[1]-means[0])/np.maximum(abs(means[0]), 1e-8)).tolist()
                row["before"] = metrics((rgb[0]@LUMA)[:, mask]); row["after"] = metrics((rgb[1]@LUMA)[:, mask])
                report["static"][region][stage] = row
        report["static_raw_exact"] = True
    if "age128-motion" in arms:
        a, b = "age32-motion", "age128-motion"
        assert all(np.array_equal(matrices[a, f], matrices[b, f]) and np.array_equal(read(files[a, f, "geometry"]), read(files[b, f, "geometry"]))
            and np.array_equal(read(files[a, f, "raw"]), read(files[b, f, "raw"])) for f in frames), "Motion geometry/raw differs"
        cycles = []
        for arm in [a, b]:
            report["motion"][arm], cycle = motion(arm, files, matrices); cycles.append(cycle)
        assert report["motion"][a]["geometry_sha256"] == report["motion"][b]["geometry_sha256"]
        before, after = [np.mean(c**2, axis=(1, 2)).reshape(10, 8).mean(1) for c in cycles]
        draws = np.random.default_rng(20261114).integers(0, 10, (4096, 10))
        report["motion_comparison"] = {"cycle_rms_change_percent": float(100*(np.sqrt(after.mean()/before.mean())-1)),
            "conditional_block8_ci95": np.quantile(100*(np.sqrt(after[draws].mean(1)/before[draws].mean(1))-1), [.025, .975]).tolist(),
            "scope": "Matched-pose consistency, not independent motion truth; pose references required before promotion"}
        report["motion_raw_exact"] = True
    (root / ("partial-analysis.json" if partial else "analysis.json")).write_text(json.dumps(report, indent=2))
    print(json.dumps({"arms": arms, "static": {k: v["final"] for k, v in report["static"].items()},
        "motion": report["motion"], "motion_comparison": report.get("motion_comparison")}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path); parser.add_argument("--linear", type=Path, required=True)
    parser.add_argument("--partial", action="store_true")
    args = parser.parse_args(); analyze(args.capture, args.linear, args.partial)
