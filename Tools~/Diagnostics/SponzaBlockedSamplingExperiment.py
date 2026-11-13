"""Same-workload Sponza sampling comparison; no physical lighting reference."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import unpack_pixel_history
from GatherHistoryMotion import world
from ProbeAnchorCalibration import LUMA, normal_from_packed
from TemporalSamplingExperiment import metrics, paired, read, verify_blocked_proposals


def digest(record):
    return hashlib.sha256(Path(record["path"]).read_bytes()).hexdigest()


def unpack_geometry(data):
    packed = data[..., 0]
    depth = (packed & 65535).astype("<u2").view("<f2").astype(float)
    normal = normal_from_packed((packed >> 16).astype(float))
    return depth, normal


def edge_mask(depth, normal):
    edge = np.zeros(depth.shape, bool)
    for axis in (0, 1):
        a, b = (slice(None, -1), slice(1, None))
        pair = (a, slice(None)) if axis == 0 else (slice(None), a)
        other = (b, slice(None)) if axis == 0 else (slice(None), b)
        changed = ((np.sum(normal[pair] * normal[other], -1) < .85)
                   | (np.abs(depth[pair]-depth[other]) > .02 * np.maximum(depth[pair], depth[other])))
        edge[pair] |= changed
        edge[other] |= changed
    for _ in range(3):
        expanded = edge.copy()
        expanded[1:] |= edge[:-1]; expanded[:-1] |= edge[1:]
        expanded[:, 1:] |= edge[:, :-1]; expanded[:, :-1] |= edge[:, 1:]
        edge = expanded
    return edge & (depth > 0)


def analyze(root):
    manifest = json.loads((root / "capture.json").read_text())
    assert manifest["complete"] and not manifest["errors"]
    assert manifest["spacing"] == 16 and manifest["rays"] == 8
    assert manifest["scene_dirty_before"] == manifest["scene_dirty_after"]
    assert manifest["original_lights_dirty"] == manifest["lights_dirty_after"]
    frames = list(range(manifest["warmup"], manifest["warmup"]+manifest["measuredFrames"]))
    assert len(frames) == 256
    records = manifest["records"]
    candidate = manifest.get("candidate", "blocked")
    assert candidate in ("blocked", "planar")
    files = {(r["arm"], r["frame"], r["tag"]): r for r in records if "tag" in r}
    matrices = {(r["arm"], r["frame"]): np.asarray(r["vp"]).reshape(4, 4) for r in records if "vp" in r}
    arms = ["white-hybrid", candidate+"-hybrid", "white-motion", candidate+"-motion"]
    hashes = []
    report = {"scope": "256-frame current renderer A/B, fixed camera plus two repeated 128-frame cycles; not GI ground truth",
              "candidate": candidate, "arms": {}, "static_pairs": {}, "motion": {}}
    for arm in arms:
        states = [r["state"] for r in records if r["arm"] == arm and "state" in r]
        assert len(states) == len(frames)
        for s in states:
            assert s["order"] == 2 and s["batch"] == 32 and s["radioSamples"] == 64
            assert s["directionalCache"] and not s["restir"] and s["guide"] and s["hierarchical"]
            assert s["spatialKernel"] == "Adaptive5x5" and s["spatialAnchor"]
            expected = (8388608 if candidate == "blocked" else 16777216) if arm.startswith(candidate) else 0
            assert s["samplingFlags"] == expected
            assert s["frozenProposal"] == arm.startswith("blocked")
            if candidate == "planar":
                assert s["anchorKernel"] == manifest["anchorKernels"]["planar" if arm.startswith(candidate) else "control"]
        source = [r for r in records if r["arm"] == arm and r.get("tag") == "radiosity"]
        assert len(source) == 2
        hashes.extend(digest(r) for r in source)
    assert len(set(hashes)) == 1, "Radiosity changed between arms or during a capture"
    report["radiosity_sha256"] = hashes[0]
    if candidate == "blocked":
        report["proposals"] = verify_blocked_proposals(manifest, files, frames, arms)
    for mode in ("hybrid", "motion"):
        assert all(np.array_equal(matrices["white-"+mode, f], matrices[candidate+"-"+mode, f]) for f in frames)
    static_y = {}
    for region in ("left", "right", "center"):
        baseline_geometry = None
        for arm in arms[:2]:
            geometries = [files[arm, f, "geometry-"+region] for f in frames]
            assert len({digest(r) for r in geometries}) == 1, "Static geometry changed"
            geometry = read(geometries[0])
            if baseline_geometry is None:
                baseline_geometry = geometry
            assert np.array_equal(geometry, baseline_geometry), "A/B static geometry differs"
            depth, normal = unpack_geometry(geometry)
            valid = depth > 0
            edge = edge_mask(depth, normal)
            report["arms"].setdefault(arm, {})[region] = {"valid_pixels": int(valid.sum()), "edge_pixels": int(edge.sum())}
            for stage in ("resolved", "pixel-temporal", "final"):
                values = []
                rgb_sum = np.zeros(3)
                for f in frames:
                    rgba = read(files[arm, f, stage+"-"+region])
                    rgb = rgba[..., :3] * np.abs(rgba[..., 3:4])
                    values.append(rgb @ LUMA)
                    rgb_sum += rgb[valid].mean(0)
                y = np.asarray(values)
                static_y[arm, stage, region] = y[:, valid]
                report["arms"][arm][region][stage] = metrics(y[:, valid])
                report["arms"][arm][region][stage]["mean_rgb"] = (rgb_sum/len(frames)).tolist()
                if stage == "final":
                    static_y[arm, "edge-final", region] = y[:, edge] if edge.any() else None
        for stage in ("resolved", "pixel-temporal", "final", "edge-final"):
            a, b = (static_y[arm, stage, region] for arm in arms[:2])
            report["static_pairs"][stage+"-"+region] = paired(a, b) if a is not None else None
    mismatch_counts = []
    for f in frames:
        a, b = (read(files[arm, f, "geometry-motion"])[..., 0] for arm in arms[2:])
        assert np.array_equal(a & 65535, b & 65535), "A/B motion depth differs"
        mismatch_counts.append(int((a != b).sum()))
    report["motion_geometry"] = {"identical_depth": True,
        "normal_encoding_mismatch_pixels_total": sum(mismatch_counts),
        "normal_encoding_mismatch_pixels_per_frame_max": max(mismatch_counts),
        "cycle_comparison": "Only exactly matching geometry pixels common to both arms and both cycles; no normal tolerance"}
    if candidate == "planar" or manifest.get("inspectAnchors", False):
        report["anchors"] = {}
        for arm in arms:
            ages, rejections = [], 0
            for f in frames:
                statistics = read(files[arm, f, "anchor-statistics"])
                anchor = read(files[arm, f, "temporal-anchor"])
                raw = read(files[arm, f, "raw-anchor"])
                geometry = read(files[arm, f, "probe-geometry"])
                valid = (geometry[..., 3] >= 0) & (anchor[..., 3] > 0)
                ages.append(statistics[..., 0][valid])
                rejected = valid & (statistics[..., 3] == 3)
                rejections += int(rejected.sum())
                assert np.all(statistics[..., 0][rejected] == 1) and np.all(statistics[..., 2][rejected] == 0)
                ulp = np.nextafter(raw.astype("<f2"), np.float16(np.inf)).astype(float)-raw
                assert np.all(np.abs(anchor-raw)[rejected] <= 1.01*ulp[rejected]+1e-6), "A newly visible anchor retained old lighting"
            age = np.concatenate(ages)
            report["anchors"][arm] = {"effective_age_mean": float(age.mean()), "effective_age_p95": float(np.quantile(age, .95)),
                "age_over_eight_percent": float(100*np.mean(age > 8)), "newly_visible_anchor_samples": rejections,
                "newly_visible_age_one_and_current_sample": True}
    for arm in arms[2:]:
        previous = None
        exposed_count = inherited = accepted = 0
        errors = []
        cycle_errors = []
        cycle_sums, cycle_counts = [], []
        excluded_cycle_pixels = 0
        first_cycle = {}
        for i, f in enumerate(frames):
            g = files[arm, f, "geometry-motion"]
            depth, normal = unpack_geometry(read(g))
            vp = matrices[arm, f]
            inverse = np.linalg.inv(vp)
            raw, temporal, final = (read(files[arm, f, stage+"-motion"]) for stage in ("resolved", "pixel-temporal", "final"))
            final_y = (final[..., :3] * np.abs(final[..., 3:4])) @ LUMA
            if 48 <= i < 128:
                first_cycle[i] = final_y
            elif i >= 176:
                assert np.array_equal(vp, matrices[arm, f-128])
                four = [read(files[a, frame, "geometry-motion"])[..., 0]
                        for a in arms[2:] for frame in (f, f-128)]
                assert all(np.array_equal(four[0] & 65535, g & 65535) for g in four[1:]), "Repeated motion depth differs"
                exact = np.logical_and.reduce([four[0] == g for g in four[1:]])
                excluded_cycle_pixels += int((~exact).sum())
                cycle_delta = (final_y-first_cycle[i-128])[exact]
                cycle_errors.append(cycle_delta)
                cycle_sums.append(float(np.sum(cycle_delta.astype(float)**2)))
                cycle_counts.append(int(cycle_delta.size))
            if previous is not None:
                yy, xx = np.mgrid[g["y"]:g["y"]+g["height"], g["x"]:g["x"]+g["width"]]
                pixels = np.stack([xx, yy], -1)
                old_depth, old_normal, old_vp, old_inverse = previous
                with np.errstate(divide="ignore", invalid="ignore"):
                    position = world(pixels, depth, inverse)
                    clip = np.concatenate([position, np.ones((*position.shape[:-1], 1))], -1) @ old_vp.T
                    projected = clip[..., :3] / clip[..., 3:4]
                    projected_xy = np.stack([(projected[..., 0]+1)*960-.5, (1-projected[..., 1])*540-.5], -1)
                    nearest = np.rint(np.nan_to_num(projected_xy)).astype(int)
                    valid = ((depth > 0) & (clip[..., 3] > 0) & np.isfinite(projected).all(-1)
                             & (nearest[..., 0] >= g["x"]) & (nearest[..., 0] < g["x"]+g["width"])
                             & (nearest[..., 1] >= g["y"]) & (nearest[..., 1] < g["y"]+g["height"]))
                    ix = np.clip(nearest[..., 0]-g["x"], 0, g["width"]-1)
                    iy = np.clip(nearest[..., 1]-g["y"], 0, g["height"]-1)
                    od = old_depth[iy, ix]
                    delta = world(nearest, od, old_inverse)-world(nearest, projected[..., 2], old_inverse)
                    plane = np.maximum(np.abs(np.sum(delta*old_normal[iy, ix], -1)), np.abs(np.sum(delta*normal, -1)))
                    exposed = valid & (od > projected[..., 2]) & (plane > .075) & (np.linalg.norm(delta, axis=-1) > .10) & (temporal[..., 3] != 0)
                if exposed.any():
                    age, _, status = unpack_pixel_history(read(files[arm, f, "history-motion"])[..., 3].astype("<f2"))
                    exposed_count += int(exposed.sum())
                    inherited += int((age[exposed] > 1).sum())
                    accepted += int((status[exposed] == 1).sum())
                    errors.append(np.abs(((temporal[..., :3]*np.abs(temporal[..., 3:4]))-(raw[..., :3]*np.abs(raw[..., 3:4]))) @ LUMA)[exposed])
            previous = depth, normal, vp, inverse
        cycle = np.concatenate(cycle_errors)
        error = np.concatenate(errors) if errors else np.empty(0)
        report["motion"][arm] = {"newly_exposed_samples": exposed_count,
            "cycle_compared_pixels": int(cycle.size), "cycle_excluded_geometry_pixels": excluded_cycle_pixels,
            "age_over_one_percent": 100*inherited/exposed_count if exposed_count else None,
            "accepted_history_percent": 100*accepted/exposed_count if exposed_count else None,
            "temporal_minus_current_p99": float(np.quantile(error, .99)) if len(error) else None,
            "temporal_minus_current_max": float(error.max()) if len(error) else None,
            "matched_pose_cycle_delta_rms": float(np.sqrt(np.mean(cycle**2))),
            "matched_pose_cycle_delta_absolute_p99": float(np.quantile(np.abs(cycle), .99)),
            "cycle_sum_squared_error_per_frame": cycle_sums, "cycle_sample_count_per_frame": cycle_counts}
    b, a = (report["motion"][arm] for arm in arms[2:])
    assert a["cycle_sample_count_per_frame"] == b["cycle_sample_count_per_frame"]
    draw = np.random.default_rng(20261114).integers(0, 10, (4096, 10))
    def resample_rms(arm):
        sums = np.asarray(arm["cycle_sum_squared_error_per_frame"]).reshape(10, 8).sum(1)
        counts = np.asarray(arm["cycle_sample_count_per_frame"]).reshape(10, 8).sum(1)
        return np.sqrt(sums[draw].sum(1)/counts[draw].sum(1))
    report["motion_comparison"] = {
        "rms_change_percent": 100*(a["matched_pose_cycle_delta_rms"]/b["matched_pose_cycle_delta_rms"]-1),
        "block8_conditional_rms_change_95_percent": np.quantile(100*(resample_rms(a)/resample_rms(b)-1), [.025, .975]).tolist(),
        "absolute_p99_change_percent": 100*(a["matched_pose_cycle_delta_absolute_p99"]/b["matched_pose_cycle_delta_absolute_p99"]-1),
        "scope": "Paired block-8 dispersion of this 80-pose repeated trajectory; not an independent dynamic GI reference"}
    (root.parent/"analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    plot(report, root.parent)
    print(json.dumps({"static_pairs": report["static_pairs"], "motion": report["motion"]}, indent=2))


def plot(report, root):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    labels = ["Left", "Right", "Center", "Left edge", "Right edge"]
    keys = ["final-left", "final-right", "final-center", "edge-final-left", "edge-final-right"]
    noise = [report["static_pairs"][k]["variance_reduction_percent"] for k in keys]
    energy = [report["static_pairs"][k]["mean_change_percent"] for k in keys]
    fig, axes = plt.subplots(1, 3, figsize=(13, 4), layout="constrained")
    axes[0].bar(labels, noise, color=["#3e877a" if v >= 0 else "#bb7752" for v in noise])
    axes[0].axhline(0, color="gray", linewidth=.7)
    axes[0].set_ylim(min(0, min(noise)-5) if min(noise) < 0 else 0, 100)
    axes[0].set_ylabel("Final temporal variance reduction (%)")
    axes[0].set_title("Fixed camera, 256 frames")
    axes[1].bar(labels, energy, color="#547ca7")
    axes[1].axhline(0, color="gray", linewidth=.7)
    axes[1].set_ylabel("Short-sequence mean change (%)")
    axes[1].set_title("Not a long-sequence energy reference")
    candidate = report.get("candidate", "blocked")
    motion = [report["motion"][a]["matched_pose_cycle_delta_rms"] for a in ("white-motion", candidate+"-motion")]
    bars = axes[2].bar(["White", candidate.title()], motion, color=["#547ca7", "#bb7752"])
    axes[2].bar_label(bars, fmt="%.3f", padding=3, fontsize=9)
    axes[2].set_ylabel("Matched-pose difference RMS, linear E/pi")
    axes[2].set_title("Matched-pose motion comparison")
    for ax in axes:
        ax.grid(axis="y", alpha=.2); ax.tick_params(axis="x", labelsize=8)
    fig.suptitle("Sponza - " + candidate + ", 16 px / 8 rays, identical converged Surface Cache")
    fig.savefig(root/("sponza-"+candidate+"-quality.png"), dpi=150)
    plt.close(fig)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    analyze(parser.parse_args().capture)
