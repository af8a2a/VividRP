"""Compare covered-black thin-shell handling with matched Cornell captures."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeCoordinateQuality import analyze as core_analysis, inspect
from AnalyzeScreenProbeResidualReference import load, qualify
from GatherHistoryExperiment import unpack_pixel_history
from GatherHistoryMotion import world
from ProbeAnchorCalibration import LUMA, normal_from_packed
from TemporalSamplingExperiment import paired, read


def hit_coverage(root, capture_name):
    capture_root = (root / capture_name).resolve()
    capture = load(capture_root / "snapshot.json")
    assert capture["schema"] == "screen-probe-ceiling-hit-diagnostics.v1" and capture["success"] and not capture["errors"]
    assert load(capture_root / "source-verification.json")["unchanged"]
    rows = {(r["frame"], r["tag"]): r for r in capture["records"]}
    assert len(rows) == len(capture["records"]) and {f for f, _ in rows} == set(range(512, 544))
    payload_bytes = 0
    for row in rows.values():
        path = Path(row["path"]).resolve()
        assert path.is_relative_to(capture_root)
        data = path.read_bytes()
        assert hashlib.sha256(data).hexdigest() == row["sha256"]
        payload_bytes += len(data)
    assert payload_bytes == capture["bytes"] <= capture["budget"]
    source_hashes = {r["sha256"] for r in rows.values() if r["tag"] == "radiosity"}
    assert source_hashes == {"40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8"}
    rays = np.stack([read(rows[f, "rays"])[0] for f in range(512, 544)])
    hits = np.stack([read(rows[f, "first-hit"]) for f in range(512, 544)])
    raw = np.stack([read(rows[f, "raw"])[0] for f in range(512, 544)])
    stats = np.stack([read(rows[f, "statistics"])[0] for f in range(512, 544)])
    flags = np.floor(rays[..., 3]).astype(int)
    source = flags & 7
    thin = hits[:, 1, :, 1] == 2
    inside = hits[:, 1, :, 3] < 0
    black = np.all(rays[..., :3] == 0, axis=-1)
    accepted_back = (source == 2) & thin & ((flags & 2048) != 0)
    return dict(rays=int(source.size), probeFrames=int(raw.shape[0] * raw.shape[1]), payloadBytes=payload_bytes,
        thinShellGeometricBackfaces=int(((source == 5) & thin).sum()),
        insideThinShellGeometricBackfaces=int(((source == 5) & thin & inside).sum()),
        acceptedThinShellMaterialBackfaces=int(accepted_back.sum()),
        acceptedInsideThinShellMaterialBackfaces=int((accepted_back & inside).sum()),
        positiveMaterialBackfaceRadiance=int((accepted_back & ~black).sum()),
        zeroCoverageProbeFrames=int((raw[..., 3] == 0).sum()), meanCoverage=float(raw[..., 3].mean()),
        anchorAgeQuantiles=np.quantile(stats[..., 0], [0, .1, .5, .9, 1]).tolist(), sourceSha256=next(iter(source_hashes)))


def motion_history(capture, rows, states, comparison_rows=None, control_moment_layout=None):
    """Isolate pixel temporal inheritance; final-minus-resolve also includes spatial filtering."""
    if not capture.get("motionHistoryRecorded", False):
        return None
    frames = list(range(1024, 1152))
    yy, xx = np.mgrid[414:542, 880:1040]
    pixels = np.stack([xx, yy], -1)
    report = {}
    for arm in ("02-r0-t0", "03-r0-t0"):
        geometry = np.stack([read(rows[arm, f, "geometry-edge"])[..., 0] for f in frames])
        depth = (geometry & 65535).astype("<u2").view("<f2").astype(float)
        normals = normal_from_packed((geometry >> 16).astype(float))
        vp = np.array([states[arm, f]["vp"] for f in frames]).reshape(128, 4, 4)
        inverse = np.linalg.inv(vp)
        exposed = np.zeros(depth.shape, bool)
        temporal = np.stack([read(rows[arm, f, "temporal-edge"]).astype(float) for f in frames])
        current = np.stack([read(rows[arm, f, "resolved-edge"]).astype(float) for f in frames])
        history = np.stack([read(rows[arm, f, "pixel-history-edge"]) for f in frames])
        for i in range(1, 128):
            pos = world(pixels, depth[i], inverse[i])
            clip = np.concatenate([pos, np.ones((*pos.shape[:-1], 1))], -1) @ vp[i - 1].T
            projected = clip[..., :3] / clip[..., 3:4]
            nearest = np.rint(np.stack([(projected[..., 0] + 1) * 960 - .5,
                                       (1 - projected[..., 1]) * 540 - .5], -1)).astype(int)
            valid = ((depth[i] > 0) & (clip[..., 3] > 0) & (nearest[..., 0] >= 880)
                     & (nearest[..., 0] < 1040) & (nearest[..., 1] >= 414) & (nearest[..., 1] < 542))
            ix, iy = np.clip(nearest[..., 0] - 880, 0, 159), np.clip(nearest[..., 1] - 414, 0, 127)
            old_depth = depth[i - 1, iy, ix]
            delta = world(nearest, old_depth, inverse[i - 1]) - world(nearest, projected[..., 2], inverse[i - 1])
            plane = np.maximum(abs(np.sum(delta * normals[i], -1)), abs(np.sum(delta * normals[i - 1, iy, ix], -1)))
            exposed[i] = (valid & (old_depth > projected[..., 2]) & (plane > .075)
                          & (np.linalg.norm(delta, axis=-1) > .10) & (temporal[i, ..., 3] != 0))
        assert exposed.any(), "Trajectory did not exercise disocclusion"
        age, _, status = unpack_pixel_history(history[..., 3].astype("<f2"))
        error = abs(((temporal[..., :3] * abs(temporal[..., 3:4]))
                     - (current[..., :3] * abs(current[..., 3:4]))) @ LUMA)[exposed]
        report[arm] = dict(samples=int(exposed.sum()), frames=int(exposed.any((1, 2)).sum()),
            oldPixelAge=int((age[exposed] > 1).sum()), acceptedOldPixelHistory=int((status[exposed] == 1).sum()),
            temporalMinusResolveP99=float(np.quantile(error, .99)), temporalMinusResolveMax=float(error.max()))
        if (arm, 1024, "final-edge") in rows:
            final = np.stack([read(rows[arm, f, "final-edge"]).astype(float) for f in frames])
            difference = abs(((final[..., :3] * abs(final[..., 3:4]))
                              - (current[..., :3] * abs(current[..., 3:4]))) @ LUMA)[exposed]
            report[arm]["finalMinusResolveP99"] = float(np.quantile(difference, .99))
            report[arm]["finalMinusResolveMax"] = float(difference.max())
            report[arm]["finalExposedRgba16Sha256"] = hashlib.sha256(
                np.ascontiguousarray(final[exposed].astype("<f2")).tobytes()).hexdigest()
            if comparison_rows is not None:
                comparison = np.stack([read(comparison_rows[arm, f, "final-edge"]).astype(float) for f in frames])
                paired_difference = abs(((final[..., :3] * abs(final[..., 3:4]))
                    - (comparison[..., :3] * abs(comparison[..., 3:4]))) @ LUMA)[exposed]
                report[arm]["pairedFinalDifferenceP99"] = float(np.quantile(paired_difference, .99))
                report[arm]["pairedFinalDifferenceMax"] = float(paired_difference.max())
                report[arm]["pairedDifferentRgbaSamples"] = int(np.any(final[exposed] != comparison[exposed], axis=-1).sum())
        if (arm, 1024, "native-moments-edge") in rows:
            moments = np.stack([read(rows[arm, f, "native-moments-edge"]) for f in frames])[exposed]
            encoding = capture.get("nativeMomentEncoding", "legacy-mean")
            assert encoding in ("legacy-mean", "split-mean")
            assert np.isfinite(moments).all() and (moments[:, 1:3] >= 0).all()
            assert encoding == "split-mean" or (moments[:, 3] >= 0).all()
            report[arm]["nativeMoments"] = dict(oldAge=int((moments[:, 2] > 1).sum()),
                ageRange=[float(moments[:, 2].min()), float(moments[:, 2].max())],
                varianceMax=float(moments[:, 1].max()), encoding=encoding)
            if encoding == "split-mean":
                report[arm]["nativeMoments"]["meanResidualMaxAbs"] = float(abs(moments[:, 3]).max())
            else:
                report[arm]["nativeMoments"]["minimumCoverage"] = float(moments[:, 3].min())
        if control_moment_layout == "packed-dual-variance":
            control = np.stack([read(rows[arm, f, "temporal-control-moments-edge"]) for f in frames])[exposed]
            report[arm]["controlSlowMoments"] = dict(ageRange=[float(control[:, 2].min()), float(control[:, 2].max())],
                maximumSlowVariance=float(np.maximum(0, control[:, 3].astype(float) - 2**-14).max()),
                invalidPackedValues=int((control[:, 3] < 2**-14).sum()))
    return report


def trace_input_qualification(before, after, arms=None):
    """TraceProbe writes geometry itself; compare its actual receiver inputs separately."""
    if not all(capture[0].get("traceInputsRecorded", False) for capture in (before, after)):
        return None
    _, old_rows, old_states = before
    _, new_rows, new_states = after
    input_differences, matrix_differences = [], []
    normal_differences = valid_differences = geometry_frames = 0
    position_max = 0.0
    observations = 0
    arms = tuple(arms) if arms is not None else tuple(f"{i:02}-r0-t0" for i in range(4))
    for arm in arms:
        for frame in range(1024, 1152):
            for tag in ("input-gbuffer0", "input-gbuffer1", "input-depth", "input-placement"):
                a, b = old_rows[arm, frame, tag], new_rows[arm, frame, tag]
                observations += 1
                if any(a[k] != b[k] for k in ("sha256", "width", "height", "format")):
                    input_differences.append([arm, frame, tag])
            if old_states[arm, frame]["inverseVp"] != new_states[arm, frame]["inverseVp"]:
                matrix_differences.append([arm, frame])
            a, b = (read(rows[arm, frame, "geometry"]) for rows in (old_rows, new_rows))
            valid = a[..., 3] >= 0
            valid_differences += int(np.count_nonzero(valid != (b[..., 3] >= 0)))
            normal_differences += int(np.count_nonzero(a[..., 3] != b[..., 3]))
            geometry_frames += int(not np.array_equal(a, b))
            if valid.any():
                position_max = max(position_max, float(abs(a[..., :3] - b[..., :3])[valid].max()))
    derived_valid = not (geometry_frames or normal_differences or valid_differences)
    def subset_valid(arms):
        return derived_valid and not any(row[0] in arms for row in input_differences + matrix_differences)
    return dict(qualified=derived_valid and not (input_differences or matrix_differences),
        staticQualified=subset_valid(("00-r0-t0", "01-r0-t0")) if all(a in arms for a in ("00-r0-t0", "01-r0-t0")) else None,
        motionQualified=subset_valid(("02-r0-t0", "03-r0-t0")) if all(a in arms for a in ("02-r0-t0", "03-r0-t0")) else None,
        observedArms=list(arms),
        receiverInputObservations=observations, inputDifferences=input_differences, inverseVpDifferences=matrix_differences,
        derivedGeometryDifferentFrames=geometry_frames, derivedNormalDifferences=normal_differences,
        derivedValidityDifferences=valid_differences, derivedPositionMaxAbsMetres=position_max,
        derivedPositionBoundMetres=0.0,
        scope="Exact receiver SRV/inverse-VP observations and exact derived geometry. No geometric tolerance replaces the legacy output gate.")


def analyze(root, baseline="quality-before", candidate="quality-after", reference="candidate-reference"):
    core = core_analysis(root, candidate, root / baseline)
    captures = {name: inspect(root / (baseline if name == "quality-before" else candidate))
                for name in ("quality-before", "quality-after")}
    arms = ("00-r0-t0", "01-r0-t0")
    frames = range(1024, 1152)
    yy, xx = np.mgrid[936:976, 720:1200]
    pixels = np.stack([xx, yy], axis=-1)
    geometry_differences = []
    signals = {}
    masks = None
    for name, (capture, rows, states) in captures.items():
        assert capture["thinShellReceiverRoi"] == dict(x=720, y=936, width=480, height=40)
        signals[name] = {}
        for arm in arms:
            for frame in frames:
                before = captures["quality-before"][1][arm, frame, "geometry-emitter-gap"]
                current = rows[arm, frame, "geometry-emitter-gap"]
                if before["sha256"] != current["sha256"]:
                    geometry_differences.append([name, arm, frame])
            packed = read(rows[arm, 1024, "geometry-emitter-gap"])[..., 0]
            depth = (packed & 65535).astype("<u2").view("<f2").astype(float)
            normal = normal_from_packed((packed >> 16).astype(float))
            vp = np.asarray(states[arm, 1024]["vp"]).reshape(4, 4)
            position = world(pixels, depth, np.linalg.inv(vp))
            ceiling = (depth > 0) & (normal[..., 1] < -.99) & (abs(position[..., 1] - 3) < .015)
            selected = dict(ceiling=ceiling,
                emitterGap=ceiling & (abs(position[..., 0]) < .85) & (position[..., 2] > 1.1) & (position[..., 2] < 1.6))
            assert all(mask.any() for mask in selected.values())
            if masks is None:
                masks = selected
            else:
                assert all(np.array_equal(masks[k], selected[k]) for k in masks)
            for tag in ("resolved-emitter-gap", "final-emitter-gap"):
                values = np.stack([read(rows[arm, f, tag]).astype(np.float64) for f in frames])
                signals[name][arm, tag] = (values[..., :3] * values[..., 3:4]) @ LUMA
    comparisons = {}
    for mask_name, mask in masks.items():
        comparisons[mask_name] = dict(pixels=int(mask.sum()), stages={})
        for tag in ("resolved-emitter-gap", "final-emitter-gap"):
            comparisons[mask_name]["stages"][tag] = [paired(
                signals["quality-before"][arm, tag][:, mask],
                signals["quality-after"][arm, tag][:, mask]) for arm in arms]

    references = {name: qualify(root, name) for name in ("reference", reference)}
    energy = {}
    before, after = (references[k][2].mean(axis=0) for k in references)
    from EmissiveRadiosityConvergence import plane_masks
    geometry_row = next(r for r in references["reference"][0]["records"] if r.get("tag") == "geometry")
    for plane, mask in plane_masks(read(geometry_row)).items():
        mean_before, mean_after = before[..., 4][mask].mean(), after[..., 4][mask].mean()
        energy[plane] = dict(meanYChangePercent=float(100 * (mean_after / mean_before - 1)),
            meanCoverageBefore=float(before[..., 3][mask].mean()), meanCoverageAfter=float(after[..., 3][mask].mean()))
    witness = trace_input_qualification(captures["quality-before"], captures["quality-after"])
    input_qualified = (witness is not None and witness["qualified"]
        and not core["crossConfigurationTrajectoryDifferences"]
        and not any(row[2] == "pixel" for row in core["crossConfigurationGeometryDifferences"]))
    # An available direct witness is mandatory even if the derived geometry agrees.
    # Identical outputs cannot excuse mismatched shader inputs.
    qualified = core["qualityComparisonQualified"] and (witness is None or input_qualified)
    static_qualified = (witness is None or witness["staticQualified"]) and not any(
        row[0] in ("00-r0-t0", "01-r0-t0") for row in core["crossConfigurationGeometryDifferences"])
    return dict(schema="screen-probe-thin-shell-quality-analysis.v1",
        baselineCapture=str(root / baseline), candidateCapture=str(root / candidate),
        qualified=bool(qualified and not geometry_differences),
        staticQualified=bool(static_qualified and not geometry_differences
            and not any(row[0] in ("00-r0-t0", "01-r0-t0") for row in core["crossConfigurationTrajectoryDifferences"])),
        exactReceiverInputWitness=witness,
        core=core, gapGeometryDifferences=geometry_differences, gap=comparisons, uniformReferenceEnergy=energy,
        candidateMotionHistory=motion_history(*captures["quality-after"]),
        limitations=core["limitations"] + [
            "Gap masks select the ceiling and the narrow emitter neighborhood using actual packed GPU geometry",
            "A mean change at a previously invalid receiver needs the current-frame reference, not an old-image gain correction",
            "Same-renderer reference cannot establish absolute Unreal accuracy; GPU timing is separate"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--baseline", default="quality-before")
    parser.add_argument("--candidate", default="quality-after")
    parser.add_argument("--reference", default="candidate-reference")
    parser.add_argument("--output", default="thin-shell-quality-analysis.json")
    args = parser.parse_args()
    result = analyze(args.root, args.baseline, args.candidate, args.reference)
    (args.root / args.output).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(qualified=result["qualified"], gap=result["gap"], energy=result["uniformReferenceEnergy"], motion=result["core"]["motion"]), indent=2))
