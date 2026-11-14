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


def probe_hash(value):
    value = np.asarray(value, np.uint32)
    value = (value ^ (value >> 16)) * np.uint32(0x7feb352d)
    value = (value ^ (value >> 15)) * np.uint32(0x846ca68b)
    return value ^ (value >> 16)


def verify_blocked_proposals(manifest, files, frames, arms, allow_fixed_cosine_resets=False, require_frozen=True):
    report = {}
    for arm in (a for a in arms if a.startswith(("blocked-", "fine-", "stable-", "atrous3-")) or (not require_frozen and a.startswith("white-"))):
        previous = None
        fixed = changed = octahedral = fine_count = descriptors = resets = 0
        maximum_mass = 0.0
        crossed_faces = 0
        topology_updates = 0
        previous_topology = None
        for frame in frames:
            record = files[arm, frame, "proposal"]
            data = read(record).reshape(record["height"], record["width"] // 8, 8, 4)
            code, subdivision = data[..., 0].astype(int), data[..., 3].astype(int)
            oct_ray = subdivision <= -3
            oct_probe = oct_ray.all(-1)
            assert np.all(oct_probe | ~oct_ray.any(-1)), "Mixed proposal charts"
            fine_ray = subdivision <= -5
            fine_probe = fine_ray.all(-1)
            assert np.all(fine_probe | ~fine_ray.any(-1)), "Mixed octahedral chart resolutions"
            assert np.all(data[..., :2] == np.floor(data[..., :2])), "Non-integer proposal payload"
            extent = data[..., 1].astype(int)
            fine_w, fine_h = (extent & 7) + 1, (extent >> 3) + 1
            fine_x, fine_y = code & 7, code >> 3
            assert np.all(~fine_ray | ((fine_x >= 0) & (fine_y >= 0) & (fine_x + fine_w <= 8) & (fine_y + fine_h <= 8))), "Fine rectangle escaped chart"
            fine_count += int(fine_probe.sum())
            octahedral += int(oct_probe.sum())
            descriptors += int(oct_probe.size)
            area = (1 << ((code >> 4) & 1)) * (1 << ((code >> 5) & 1))
            mass = np.where(oct_ray, .5 * area, np.where(subdivision < 0, .5, np.where(subdivision > 0, 2., 1.)))
            mass = np.where(fine_ray, fine_w * fine_h / 8., mass)
            maximum_mass = max(maximum_mass, float(mass.max()))
            crossed_faces += int((fine_ray & (((fine_x // 4) != ((fine_x + fine_w - 1) // 4))
                                                        | ((fine_y // 4) != ((fine_y + fine_h - 1) // 4)))).sum())
            assert np.all(mass.sum(-1) == 8), "Inverse PDF mass drift"
            occupancy = np.zeros((*oct_probe.shape, 16), int)
            for ray in range(8):
                c, s = code[..., ray], subdivision[..., ray]
                for cell in range(16):
                    covered = ((cell % 4 >= (c & 3)) & (cell % 4 < (c & 3) + (1 << ((c >> 4) & 1)))
                               & (cell // 4 >= ((c >> 2) & 3)) & (cell // 4 < ((c >> 2) & 3) + (1 << ((c >> 5) & 1))))
                    legacy = np.where(s < 0, (cell == 2*c + (-s-1)),
                                      (cell//2 == c) | ((s > 0) & (cell//2 == s-1)))
                    occupancy[..., cell] += np.where(oct_probe, covered, legacy)
            assert np.all(occupancy[~fine_probe] == 1), "Proposal overlap or lost hemisphere support"
            fine_occupancy = np.zeros((*fine_probe.shape, 64), int)
            for ray in range(8):
                for cell in range(64):
                    fine_occupancy[..., cell] += ((cell % 8 >= fine_x[..., ray]) & (cell % 8 < fine_x[..., ray] + fine_w[..., ray])
                                                 & (cell // 8 >= fine_y[..., ray]) & (cell // 8 < fine_y[..., ray] + fine_h[..., ray]))
            assert np.all(fine_occupancy[fine_probe] == 1), "Fine proposal overlap or lost hemisphere support"
            # History coordinates and screen/world source tags do not change
            # ray directions. Compare actual strata in an 8x8 common chart.
            coarse_w, coarse_h = 1 << ((code >> 4) & 1), 1 << ((code >> 5) & 1)
            topology = np.stack([np.where(fine_ray, fine_x, 2 * (code & 3)),
                                 np.where(fine_ray, fine_y, 2 * ((code >> 2) & 3)),
                                 np.where(fine_ray, fine_w, 2 * coarse_w),
                                 np.where(fine_ray, fine_h, 2 * coarse_h),
                                 np.where(oct_ray, -3, subdivision)], axis=-1)
            topology[..., 0] = np.where(oct_ray, topology[..., 0], code)
            topology[..., 1:4] = np.where(oct_ray[..., None], topology[..., 1:4], 0)
            if previous_topology is not None:
                topology_updates += int(np.any(topology != previous_topology, axis=(-1, -2)).sum())
            previous_topology = topology
            yy, xx = np.mgrid[record["y"]:record["y"]+record["height"],
                              record["x"]//8:(record["x"]+record["width"])//8]
            phase = probe_hash(xx + yy * 65537) & 31
            # Older manifests used this fixed phase without recording it explicitly.
            base = manifest.get("frameBase", 1733)
            boundary = ((base + frame + phase) & 31) == 0
            if previous is not None and require_frozen:
                modified = np.any(data != previous, axis=(-1, -2))
                # Lighting changes can invalidate directional guide history. The
                # shader then uses a fixed cosine partition until the boundary;
                # an adaptive within-block rebuild remains an error.
                cosine = np.zeros((8, 4), dtype=data.dtype)
                cosine[:, 0] = np.arange(8)
                reset = ~boundary & modified & np.all(data == cosine, axis=(-1, -2))
                if not allow_fixed_cosine_resets:
                    assert not np.any(reset), "Unexpected proposal reset in a static experiment"
                assert not np.any(~boundary & modified & ~reset), "Adaptive proposal changed within a QMC block"
                resets += int(reset.sum())
                fixed += int((~boundary & ~reset).sum())
                changed += int((modified & boundary).sum())
            previous = data
        report[arm] = {"fixed_probe_frames_checked": fixed, "boundary_updates": changed,
                       "frozen_proposals_checked": require_frozen,
                       "fixed_cosine_reset_probe_frames": resets,
                       "octahedral_probe_frames": octahedral, "probe_frames": descriptors,
                       "fine_probe_frames": fine_count,
                       "maximum_inverse_pdf": maximum_mass, "fine_rectangles_crossing_faces": crossed_faces,
                       "sampling_topology_updates": topology_updates,
                       "full_support_and_mass": True}
    assert report, "Blocked proposal data was not captured"
    return report


def analyze(root, linear, candidate="sobol"):
    manifest = json.loads((root / "capture.json").read_text())
    assert manifest.get("complete", True), "Incomplete capture"
    control = manifest.get("control", "white")
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
        if candidate == "blocked":
            expected = record["arm"].startswith("blocked-")
            assert s["frozenProposal"] == expected and s["samplingFlags"] == (8388608 if expected else 0)
            if expected: assert s["proposalHistoryValid"]
        elif candidate == "fine":
            expected = record["arm"].startswith("fine-")
            assert s["frozenProposal"] and s["proposalHistoryValid"] and s["guide"] and s["hierarchical"]
            assert s["fineGuide"] == expected and s["samplingFlags"] == (41943040 if expected else 8388608)
        elif candidate == "stable":
            expected = record["arm"].startswith("stable-")
            assert s["frozenProposal"] and s["proposalHistoryValid"] and s["guide"] and s["hierarchical"]
            assert s["stableGuide"] == expected and s["samplingFlags"] == (75497472 if expected else 8388608)
        elif candidate == "atrous3":
            expected = record["arm"].startswith("atrous3-")
            frozen = control != "white"
            assert s["frozenProposal"] == frozen and (not frozen or s["proposalHistoryValid"])
            assert s["samplingFlags"] == (8388608 if frozen else 0)
            assert s["atrousIterations"] == (3 if expected else 2)
    get = lambda a, f, tag: read(files[a, f, tag])
    stack = lambda a, tag: np.stack([get(a, f, tag) for f in frames])
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    report = {"scope": "256 consecutive frames, fixed source; temporal noise and pixel history inheritance, not physical truth",
              "contract": "linear consumed E/pi, no exposure or gain fitting", "arms": {}, "pairs": {}}
    all_y = {}
    all_rgb = {}
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
                all_rgb[arm, stage, region] = (rgba[..., :3] * np.abs(rgba[..., 3:4]))[:, mask].mean((0, 1), dtype=np.float64)
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
        b, a = control + "-" + mode, candidate + "-" + mode
        assert all(np.array_equal(matrices[b, f], matrices[a, f]) for f in frames)
        assert all(np.array_equal(get(b, f, "probe-geometry"), get(a, f, "probe-geometry")) for f in frames), "Static A/B probe geometry differs"
        if candidate == "atrous3":
            tags = ["proposal", "hit-lighting", "anchor", "sh"] + [stage + "-" + region
                    for stage in ["resolved", "pixel-temporal"] for region in ["ceiling", "floor", "right"]]
            assert all(np.array_equal(get(b, f, tag), get(a, f, tag)) for f in frames for tag in tags), "Denoise-only comparison changed upstream lighting"
        report["pairs"][mode] = {stage + "-" + region: paired(y, all_y[a, stage, region])
                                  for (arm, stage, region), y in all_y.items() if arm == b}
        for region in ["ceiling", "floor", "right"]:
            before_rgb, after_rgb = all_rgb[b, "final", region], all_rgb[a, "final", region]
            report["pairs"][mode]["final-" + region].update({"mean_rgb_before": before_rgb.tolist(), "mean_rgb_after": after_rgb.tolist(),
                "mean_rgb_change_percent": (100 * (after_rgb - before_rgb) / np.maximum(abs(before_rgb), 1e-8)).tolist()})
    report["motion"] = {}
    report["motion_scope"] = "Matched-pose cycle noise plus nearest-depth exposure diagnostic; not native four-tap rejection replay or dynamic ground truth"
    motion_geometry = []
    cycle_errors = {}
    motion_upstream = {}
    for arm in [control + "-motion", candidate + "-motion"]:
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
        if candidate == "atrous3":
            motion_upstream[arm] = {tag: hashlib.sha256(value).hexdigest()
                                    for tag, value in [("resolved", raw), ("pixel-temporal", temporal), ("history", history)]}
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
        cycle_errors[arm] = cycle_delta
        report["motion"][arm] = {"newly_exposed_samples": int(exposed.sum()),
                                  "age_over_one_percent": float(100*np.mean(age[exposed] > 1)),
                                  "accepted_history_percent": float(100*np.mean(status[exposed] == 1)),
                                  "temporal_minus_current_p99": float(np.quantile(error, .99)),
                                  "temporal_minus_current_max": float(error.max()),
                                  "matched_pose_cycle_delta_rms": float(np.sqrt(np.mean(cycle_delta**2))),
                                  "matched_pose_cycle_delta_absolute_p99": float(np.quantile(np.abs(cycle_delta), .99)),
                                  "matched_pose_cycle_delta_mean": float(cycle_delta.mean())}
    assert np.array_equal(*motion_geometry), "A/B motion geometry differs"
    assert all(np.array_equal(matrices[control + "-motion", f], matrices[candidate + "-motion", f]) for f in frames)
    if candidate == "atrous3":
        assert motion_upstream[control + "-motion"] == motion_upstream[candidate + "-motion"], "Denoise-only comparison changed motion history"
        report["denoise_upstream_equivalence"] = {"static_and_motion_exact": True,
            "scope": "Captured static interiors and 320x256 motion ROI", "motion_hashes": motion_upstream}
    # Compare the same 80 poses; eight-pose blocks retain short-range correlation.
    before = cycle_errors[control + "-motion"]
    after = cycle_errors[candidate + "-motion"]
    b = np.mean(before**2, axis=(1, 2)).reshape(10, 8).mean(1)
    a = np.mean(after**2, axis=(1, 2)).reshape(10, 8).mean(1)
    draws = np.random.default_rng(20261114).integers(0, 10, (4096, 10))
    report["motion_comparison"] = {"cycle_rms_change_percent": float(100 * (np.sqrt(a.mean()/b.mean()) - 1)),
        "paired_block8_conditional_95_percent": np.quantile(100 * (np.sqrt(a[draws].mean(1)/b[draws].mean(1)) - 1), [.025, .975]).tolist()}
    if candidate in ("blocked", "fine", "stable", "atrous3"):
        report["proposals"] = verify_blocked_proposals(manifest, files, frames, arms, require_frozen=not(candidate == "atrous3" and control == "white"))
    (root.parent / "analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    plot_quality(root.parent, report, control, candidate)
    print(json.dumps(report["pairs"], indent=2))
    print(json.dumps(report["motion"], indent=2))


def plot_quality(root, report, control, candidate, filename="sampling-quality.png"):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    rows = [(mode + " " + region, report["pairs"][mode]["final-" + region])
            for mode in report["pairs"] for region in ["ceiling", "floor", "right"]]
    fig, axes = plt.subplots(1, 3, figsize=(14, 4), layout="constrained")
    for axis, key, interval, title in [
        (axes[0], "variance_reduction_percent", "block_bootstrap_variance_reduction_95_percent", "Final variance reduction (%)"),
        (axes[1], "mean_change_percent", "block_bootstrap_mean_change_95_percent", "Short-sequence mean change (%)")]:
        for y, (label, row) in enumerate(rows):
            value = row[key]
            axis.hlines(y, *row[interval], color="#777777", linewidth=2)
            axis.scatter(value, y, color=("#238b45" if value >= 0 else "#c33f35") if key.startswith("variance") else "#2c7fb8")
        axis.set_yticks(range(len(rows)), [label for label, _ in rows]); axis.invert_yaxis()
        axis.axvline(0, color="black", linewidth=.8); axis.grid(axis="x", alpha=.2); axis.set_title(title)
    if "motion_comparison" in report:
        motion = report["motion_comparison"]
        axes[2].hlines(0, *motion["paired_block8_conditional_95_percent"], color="#777777", linewidth=2)
        axes[2].scatter(motion["cycle_rms_change_percent"], 0, color="#2c7fb8")
        axes[2].axvline(0, color="black", linewidth=.8); axes[2].set_yticks([0], ["80 matched poses"])
        axes[2].set_title("Motion cycle RMS change (%)\nnegative is lower noise"); axes[2].grid(axis="x", alpha=.2)
    else:
        message = "Static-only view\nMotion is reported separately" if report.get("capture_complete") else "Rejected at static quality gate\nNo completed motion comparison"
        axes[2].axis("off"); axes[2].text(.5, .5, message, ha="center", va="center")
    fig.suptitle(f"{control} vs {candidate}: 16 px / 8 rays, fixed source; conditional 95% intervals")
    fig.savefig(root / filename, dpi=160); plt.close(fig)


def analyze_static_prefix(root, linear, candidate):
    """Analyze only fully completed static pairs after an intentional early rejection."""
    manifest = json.loads((root / "capture.json").read_text())
    assert not manifest["errors"] and manifest["spacing"] == 16 and manifest["rays"] == 8
    control = manifest.get("control", "white")
    frames = list(range(manifest["warmup"], manifest["warmup"] + manifest["measuredFrames"]))
    assert len(frames) == 256, "Static spectral bands require a 256-frame capture"
    files = {(r["arm"], r["frame"], r["tag"]): r for r in manifest["records"] if "tag" in r}
    matrices = {(r["arm"], r["frame"]): r["vp"] for r in manifest["records"] if "vp" in r}
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    report = {"scope": "Completed static pairs only; no motion or physical-reference claim",
              "capture_complete": manifest.get("complete", True), "pairs": {}, "sources": {}, "spectra": {}}
    completed_arms = []
    for mode in ["hybrid", "sdf"]:
        before, after = control + "-" + mode, candidate + "-" + mode
        if not all((arm, frame, "final-ceiling") in files for arm in [before, after] for frame in frames):
            continue
        for arm in [before, after]:
            completed_arms.append(arm)
            states = [r["state"] for r in manifest["records"] if r.get("arm") == arm and "state" in r]
            assert len(states) == len(frames)
            assert all(s["order"] == 8 and s["batch"] == 32 and s["radioSamples"] == 64 and s["directionalCache"] and not s["restir"] for s in states)
            assert all(s["sun"][:3] == [0, 0, 0] and s["sky"] == 0 for s in states)
            if candidate == "fine":
                expected = arm.startswith("fine-")
                assert all(s["frozenProposal"] and s["proposalHistoryValid"] and s["fineGuide"] == expected
                           and s["samplingFlags"] == (41943040 if expected else 8388608) for s in states)
            elif candidate == "stable":
                expected = arm.startswith("stable-")
                assert all(s["frozenProposal"] and s["proposalHistoryValid"] and s["stableGuide"] == expected
                           and s["samplingFlags"] == (75497472 if expected else 8388608) for s in states)
            elif candidate == "atrous3":
                expected = arm.startswith("atrous3-")
                frozen = control != "white"
                assert all(s["frozenProposal"] == frozen and (not frozen or s["proposalHistoryValid"])
                           and s["samplingFlags"] == (8388608 if frozen else 0)
                           and s["atrousIterations"] == (3 if expected else 2) for s in states)
            first = Path(files[arm, frames[0], "radiosity"]["path"]).read_bytes()
            assert first == Path(files[arm, frames[-1], "radiosity"]["path"]).read_bytes()
            report["sources"][arm] = hashlib.sha256(first).hexdigest()
        assert all(matrices[before, f] == matrices[after, f] for f in frames)
        assert all(np.array_equal(read(files[before, f, "probe-geometry"]), read(files[after, f, "probe-geometry"])) for f in frames)
        report["pairs"][mode] = {}
        report["spectra"][mode] = {}
        for region, object_id in [("ceiling", 2), ("floor", 1), ("right", 5)]:
            meta = files[before, frames[0], "final-" + region]
            mask = ids[meta["y"]:meta["y"]+meta["height"], meta["x"]:meta["x"]+meta["width"]] == object_id
            assert mask.any()
            for stage in ["resolved", "pixel-temporal", "final"]:
                values = []
                rgb_means = []
                for arm in [before, after]:
                    rgba = np.stack([read(files[arm, f, stage + "-" + region]) for f in frames])
                    values.append(((rgba[..., :3] * np.abs(rgba[..., 3:4])) @ LUMA)[:, mask])
                    rgb_means.append((rgba[..., :3] * np.abs(rgba[..., 3:4]))[:, mask].mean((0, 1), dtype=np.float64))
                report["pairs"][mode][stage + "-" + region] = paired(*values)
                if stage == "final":
                    before_rgb, after_rgb = rgb_means
                    report["pairs"][mode][stage + "-" + region].update({"mean_rgb_before": before_rgb.tolist(), "mean_rgb_after": after_rgb.tolist(),
                        "mean_rgb_change_percent": (100 * (after_rgb - before_rgb) / np.maximum(abs(before_rgb), 1e-8)).tolist()})
                bands = []
                for y in values:
                    signal = y.astype(np.float64) - y.mean(0, dtype=np.float64)
                    power = np.mean(np.abs(np.fft.rfft(signal, axis=0))**2, axis=1) / len(frames)**2
                    power[1:-1] *= 2
                    # 256-frame captures: >=32, 8..32, and <8 frame periods.
                    bands.append({"period_at_least_32_frames": float(power[1:9].sum()),
                                  "period_8_to_32_frames": float(power[9:33].sum()),
                                  "period_below_8_frames": float(power[33:].sum()),
                                  "total_variance": float(power[1:].sum())})
                    assert np.isclose(power[1:].sum(), np.var(y, axis=0, dtype=np.float64).mean()), "Spectral variance normalization mismatch"
                report["spectra"][mode][stage + "-" + region] = dict(zip([control, candidate], bands))
    assert report["pairs"], "No completed static pair"
    assert len(set(report["sources"].values())) == 1, "A/B source differs"
    if candidate in ("fine", "blocked", "stable", "atrous3"):
        report["proposals"] = verify_blocked_proposals(manifest, files, frames, completed_arms, require_frozen=not(candidate == "atrous3" and control == "white"))
    (root.parent / "static-analysis.json").write_text(json.dumps(report, indent=2))
    plot_quality(root.parent, report, control, candidate, "static-sampling-quality.png")
    print(json.dumps({mode: {key: row for key, row in rows.items() if key.startswith("final-")} for mode, rows in report["pairs"].items()}, indent=2))


def analyze_denoise_edges(root, linear):
    from scipy.ndimage import minimum_filter, maximum_filter
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    manifest = json.loads((root / "capture.json").read_text())
    assert manifest["complete"] and not manifest["errors"] and manifest["candidate"] == "atrous3"
    assert manifest["spacing"] == 16 and manifest["rays"] == 8 and manifest["measuredFrames"] == 256
    frames = range(manifest["warmup"], manifest["warmup"] + manifest["measuredFrames"])
    files = {(r["arm"], r["frame"], r["tag"]): r for r in manifest["records"] if "tag" in r}
    control = manifest["control"]
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    edge = minimum_filter(ids, 3) != maximum_filter(ids, 3)
    edge = maximum_filter(edge, 9)
    report = {"scope": "Contact and wall-corner patches; signal change relative to two-pass mean, not physical ground truth",
              "display_scale": 20, "pairs": {}}
    images = []
    for mode in ["hybrid", "sdf"]:
        before, after = control + "-" + mode, "atrous3-" + mode
        report["pairs"][mode] = {}
        for region in ["short-contact", "tall-contact", "wall-corner"]:
            meta = files[before, frames.start, "final-" + region]
            y, x, h, w = (meta[k] for k in ["y", "x", "height", "width"])
            labels, boundary = ids[y:y+h, x:x+w], edge[y:y+h, x:x+w]
            for f in frames:
                for stage in ["resolved", "pixel-temporal"]:
                    assert np.array_equal(read(files[before, f, stage + "-" + region]), read(files[after, f, stage + "-" + region])), "Edge input differs"
            a = np.stack([read(files[before, f, "final-" + region]) for f in frames])
            b = np.stack([read(files[after, f, "final-" + region]) for f in frames])
            assert np.array_equal(a[..., 3], b[..., 3]), "Denoising changed edge coverage"
            a, b = a[..., :3] * abs(a[..., 3:4]), b[..., :3] * abs(b[..., 3:4])
            ay, by = a @ LUMA, b @ LUMA
            am, bm = a.mean(0, dtype=np.float64), b.mean(0, dtype=np.float64)
            delta = (bm - am) @ LUMA
            row = {"coverage_exact": True, "upstream_exact": True, "objects": {}}
            for object_id in np.unique(labels):
                mask = labels == object_id
                if object_id <= 0 or mask.sum() < 8:
                    continue
                mean_a, mean_b = am[mask].mean(0), bm[mask].mean(0)
                values = paired(ay[:, mask], by[:, mask])
                values.update({"pixels": int(mask.sum()), "mean_rgb_before": mean_a.tolist(), "mean_rgb_after": mean_b.tolist(),
                    "mean_rgb_change_percent": (100 * (mean_b - mean_a) / np.maximum(abs(mean_a), 1e-8)).tolist(),
                    "pixel_mean_delta_abs_p99": float(np.quantile(abs(delta[mask]), .99))})
                selected = mask & boundary
                if selected.any():
                    values["geometry_edge_mean_delta_y"] = float(delta[selected].mean())
                    values["geometry_edge_abs_delta_p99"] = float(np.quantile(abs(delta[selected]), .99))
                row["objects"][str(int(object_id))] = values
            report["pairs"][mode][region] = row
            if mode == "hybrid": images.append((region, am, bm, delta))
    fig, axes = plt.subplots(3, 3, figsize=(9, 8), layout="constrained")
    bound = max(float(np.quantile(abs(row[3]), .995)) for row in images)
    for index, (region, am, bm, delta) in enumerate(images):
        axes[index, 0].imshow(np.clip(am[::-1] / 20, 0, 1)**(1/2.2), interpolation="nearest")
        axes[index, 1].imshow(np.clip(bm[::-1] / 20, 0, 1)**(1/2.2), interpolation="nearest")
        shown = axes[index, 2].imshow(delta[::-1], cmap="coolwarm", vmin=-bound, vmax=bound, interpolation="nearest")
        for axis in axes[index]: axis.set_xticks([]); axis.set_yticks([])
        axes[index, 0].set_ylabel(region)
    for axis, title in zip(axes[0], ["2-pass mean", "3-pass mean", "Signed delta E/pi luminance"]): axis.set_title(title)
    fig.colorbar(shown, ax=axes[:, 2], shrink=.8)
    fig.suptitle("Hybrid contact patches: shared display scale 20, no brightness fitting")
    fig.savefig(root.parent / "denoise-edges.png", dpi=160); plt.close(fig)
    (root.parent / "denoise-edges.json").write_text(json.dumps(report, indent=2))
    print(json.dumps({"coverage_and_upstream_exact": True, "modes": list(report["pairs"])}, indent=2))


def analyze_references(root, linear, candidate="sobol", control_root=None):
    """Long-sequence energy equivalence to the unchanged filtered pipeline.

    This is an independent frame sequence, not an independent physical renderer.
    """
    short = json.loads((root / "analysis.json").read_text())
    canonical = next(iter(short["arms"].values()))["radiosity_sha256"]
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    report = {"scope": "2048-frame regional means; same frozen Surface Cache, independent frame phase; not physical truth",
              "diagnostic_equivalence_limit_percent": 1.0, "arms": {}, "pairs": {}}
    arrays = {}
    control_root = control_root or root
    folders = [("reference", control_root / "reference"), ("reference-sdf", control_root / "reference-sdf"),
               ("reference-" + candidate, root / ("reference-" + candidate))]
    for folder, directory in folders:
        manifest = json.loads((directory / "capture.json").read_text())
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
        after = "reference-" + candidate + "/" + candidate + "-reference-" + mode
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
    parser.add_argument("--static-only", action="store_true", help="Completed static pairs from an intentionally rejected, incomplete capture")
    parser.add_argument("--edges-only", action="store_true", help="Denoise-only contact patches with unchanged upstream and coverage")
    parser.add_argument("--candidate", choices=["sobol", "blocked", "fine", "stable", "atrous3"], default="sobol")
    parser.add_argument("--control-root", type=Path, help="Frozen-source white reference root from a previous capture")
    args = parser.parse_args()
    if args.edges_only:
        analyze_denoise_edges(args.capture, args.linear)
    elif args.static_only:
        analyze_static_prefix(args.capture, args.linear, args.candidate)
    elif args.uniform_only:
        analyze_uniform(args.capture, args.linear)
    elif args.reference_only:
        analyze_references(args.capture, args.linear, args.candidate, args.control_root)
    else:
        analyze(args.capture, args.linear, args.candidate)
