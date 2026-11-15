"""Qualify static wavelet/moment readbacks against the frozen Cornell reference."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeResidualReference import load, qualify
from AnalyzeScreenProbeCoordinateQuality import analyze as quality_analysis, inspect as quality_inspect
from AnalyzeScreenProbeThinShellQuality import motion_history, trace_input_qualification
from ProbeAnchorCalibration import LUMA
from TemporalSamplingExperiment import metrics, paired, read


ARMS = ("00-r0-t0", "01-r0-t0")
FRAMES = range(1024, 1152)
INPUTS = ("input-gbuffer0", "input-gbuffer1", "input-depth", "input-placement",
          "geometry", "geometry-emitter-gap")
STAGES = ("resolved-emitter-gap", "temporal-emitter-gap", "wavelet-0", "wavelet-1",
          "wavelet-2", "final-emitter-gap")
SOURCE_SHA256 = "40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8"


def scalar_read(row):
    assert row["format"] == "R16_SFloat"
    values = np.fromfile(row["path"], "<f2").reshape(row["height"], row["width"])
    assert np.isfinite(values).all() and (values >= 0).all()
    return values.astype(float)


def inspect(root, motion_only=False):
    capture = load(root / "capture.json")
    schemas = ("screen-probe-native-moments-motion-only.v1", "screen-probe-atrous-same-frame-motion.v1") if motion_only else ("screen-probe-atrous-stages.v1", "screen-probe-atrous-same-frame.v1")
    assert capture["schema"] in schemas
    assert capture["complete"] and not capture["errors"]
    assert (capture["warmup"], capture["measured"], capture["lastFrame"]) == (1024, 128, 1152)
    assert load(root / "source-verification.json")["unchanged"]
    rows = {(r["arm"], r["frame"], r["tag"]): r for r in capture["records"] if "tag" in r}
    states = {(r["arm"], r["frame"]): r["qualityState"] for r in capture["records"] if "qualityState" in r}
    assert len(rows) == sum("tag" in r for r in capture["records"])
    arms = ("02-r0-t0", "03-r0-t0") if motion_only else ARMS
    assert set(states) == {(a, f) for a in arms for f in FRAMES}
    completed = [r for r in capture["records"] if r.get("completed")]
    assert len(completed) == 2 and all(r["targetCount"] == 1152 and r["sourceFrames"] == 264
                                     and r["otherCount"] == 0 for r in completed)
    assert all(s["order"] == 8 and s["batch"] == 32 and s["updates"] == 0 and s["anchorAge"] == 512
               and s["flags"] == 0 and s["atrous"] == 4 and s["moving"] == motion_only
               and abs(s["bias"] - .04) < 1e-6 for s in states.values())
    assert {r["sha256"] for r in rows.values() if r["tag"] == "radiosity"} == {SOURCE_SHA256}
    payload = 0
    for row in rows.values():
        if row["saved"]:
            path = Path(row["path"]).resolve()
            assert path.is_relative_to(root.resolve())
            data = path.read_bytes()
            assert hashlib.sha256(data).hexdigest() == row["sha256"]
            payload += len(data)
    assert payload == capture["savedPayloadBytes"] <= capture["savedPayloadBudget"] == 512 * 1024**2
    return capture, rows, states


def input_differences(a, b, same_run=False):
    """Require exact observed receiver inputs and derived geometry, without tolerances."""
    differences = []
    for arm in (ARMS[:1] if same_run else ARMS):
        other_arm = ARMS[1] if same_run else arm
        for frame in FRAMES:
            for tag in INPUTS:
                old, new = a[1][arm, frame, tag], b[1][other_arm, frame, tag]
                if any(old[k] != new[k] for k in ("sha256", "width", "height", "format")):
                    differences.append([arm, frame, tag])
            for key in ("vp", "inverseVp", "pose"):
                if a[2][arm, frame][key] != b[2][other_arm, frame][key]:
                    differences.append([arm, frame, key])
    return differences


def analyze(root, reference_geometry, target, matched_reference_subset=False):
    capture, rows, states = data = inspect(root)
    protocol = load(root / "protocol.json")
    encoding = capture.get("nativeMomentEncoding", "legacy-mean")
    assert encoding in ("legacy-mean", "split-mean")
    stage_tags = tuple(tag for tag in STAGES if (not tag.startswith("wavelet-") or protocol.get("waveletStagesSaved", True))
                       and (tag != "wavelet-2" or protocol.get("wavelet2Saved", True)))
    aa = input_differences(data, data, same_run=True)
    xs = np.arange(46, 74)
    px, py = xs * 16 + 8 - 720, 16
    result, reference_differences = {}, []
    for arm in ARMS:
        stack = lambda tag: np.stack([read(rows[arm, f, tag]).astype(float) for f in FRAMES])
        geometry = stack("geometry")[:, 59, xs]
        matched = np.all(geometry == reference_geometry[59, xs], axis=(1, 2))
        reference_differences.extend([arm, int(f)] for f in np.array(list(FRAMES))[~matched])
        assert matched.any(), "No exactly matched reference points"
        assert matched_reference_subset or matched.all(), "Reference geometry differs; keep the failure or explicitly diagnose a subset"
        stages = {}
        for tag in stage_tags:
            values = stack(tag)
            y = ((values[..., :3] * abs(values[..., 3:4])) @ LUMA)[:, py, px]
            error = y[matched] - target[59, xs]
            stages[tag] = dict(metrics(y), referenceBias=float(error.mean()),
                               referenceRmsError=float(np.sqrt(np.mean(error**2))))
        variance = {}
        for step in (range(3) if protocol.get("varianceStagesSaved", True) else ()):
            values = np.stack([scalar_read(rows[arm, f, f"variance-{step}"]) for f in FRAMES])[:, py, px]
            variance[str(step)] = dict(mean=float(values.mean()),
                                      quantiles=np.quantile(values, [0, .5, .99, 1]).tolist())
        moments = None
        if (arm, 1024, "native-moments") in rows:
            values = stack("native-moments")[:, py, px]
            assert (values[..., 1:3] >= 0).all()
            assert encoding == "split-mean" or (values[..., 3] >= 0).all()
            channels = ["logMeanHigh", "centralLogVariance", "effectiveAge", "logMeanResidual"] if encoding == "split-mean" else ["logMean", "centralLogVariance", "effectiveAge", "coverage"]
            moments = dict(encoding=encoding, channels=channels,
                           quantiles=np.quantile(values, [0, .5, 1], axis=(0, 1)).tolist())
        regions, region_inputs = {}, {}
        for tag in ("final-ceiling", "final-floor", "final-back"):
            final = stack(tag)
            regions[tag] = metrics((final[..., :3] * abs(final[..., 3:4])) @ LUMA)
            if protocol.get("planeGuidance", False):
                label = tag.removeprefix("final-")
                for frame in FRAMES:
                    current, final_row = rows[arm, frame, "temporal-" + label], rows[arm, frame, tag]
                    assert all(current[k] == final_row[k] for k in ("x", "y", "width", "height"))
                temporal, moment = stack("temporal-" + label), stack("native-moments-" + label)
                conditional_y = temporal[..., :3] @ LUMA
                temporal_metrics = metrics(conditional_y * abs(temporal[..., 3]))
                region_inputs[tag] = dict(temporal=temporal_metrics,
                    currentLog=metrics(np.log2(1 + conditional_y)),
                    momentLogMean=metrics(moment[..., 0] + (moment[..., 3] if encoding == "split-mean" else 0)),
                    momentMeanUnchangedFraction=float(np.mean(np.diff(moment[..., 0] + (moment[..., 3] if encoding == "split-mean" else 0), axis=0) == 0)),
                    momentAgeQuantiles=np.quantile(moment[..., 2], [0, .5, 1]).tolist(),
                    momentVarianceQuantiles=np.quantile(moment[..., 1], [0, .5, 1]).tolist(),
                    finalToTemporalVarianceRatio=regions[tag]["temporal_variance"] / max(temporal_metrics["temporal_variance"], 1e-20),
                    meanChangeFromTemporalPercent=100 * (regions[tag]["mean_y"] / temporal_metrics["mean_y"] - 1))
        result[arm] = dict(stages=stages, variance=variance, moments=moments,
                           exactReferenceFrames=int(matched.sum()), regions=regions, regionInputs=region_inputs)
    return dict(schema="screen-probe-atrous-analysis.v1", qualified=not (aa or reference_differences),
                withinConfigurationInputDifferences=aa, referenceGeometryDifferences=reference_differences,
                payloadBytes=capture["savedPayloadBytes"], sourceSha256=SOURCE_SHA256,
                referenceMeanY=float(target[59, xs].mean()), referencePoints=28, arms=result), data


def compare(baseline, candidate, reference_root, matched_reference_subset=False):
    _, snapshot, blocks, _, _ = qualify(reference_root, "inside-shell-reference")
    reference_geometry = read(snapshot["geometry"])[:68, :120]
    target = blocks.mean(0)[..., 4]
    reports = [analyze(root, reference_geometry, target, matched_reference_subset) for root in (baseline, candidate)]
    differences = input_differences(reports[0][1], reports[1][1])
    arms = {}
    for arm in ARMS:
        values = []
        for _, (_, rows, _) in reports:
            rgba = np.stack([read(rows[arm, f, "final-emitter-gap"]).astype(float) for f in FRAMES])
            values.append(((rgba[..., :3] * abs(rgba[..., 3:4])) @ LUMA)[:, 16, np.arange(46, 74) * 16 + 8 - 720])
        before, after = (r[0]["arms"][arm] for r in reports)
        arms[arm] = dict(finalPoints=paired(*values), before=before, after=after)
    return dict(schema="screen-probe-atrous-comparison.v1", baseline=str(baseline), candidate=str(candidate),
                qualified=not differences and all(r[0]["qualified"] for r in reports),
                crossConfigurationInputDifferences=differences,
                baselineQualification=reports[0][0], candidateQualification=reports[1][0], arms=arms,
                limitations=["Exact static receiver-input/reference qualification only; no tolerance, gain fitting or hidden frame exclusion",
                             "Optional subset errors remain diagnostic and never qualify unmatched captures",
                             "The high-sample reference shares frozen Surface Cache data; this is not absolute Unreal truth",
                             "Variance records are in log luminance, not linear radiance variance; correlated observations are not divided by age",
                             "No GPU performance or motion acceptance follows from this static experiment"])


def compare_motion(baseline, candidate, compact=False):
    assert baseline.parent == candidate.parent
    core = None if compact else quality_analysis(candidate.parent, candidate.name, baseline)
    captures = {name: inspect(path, motion_only=True) if compact else quality_inspect(path)
                for name, path in (("before", baseline), ("after", candidate))}
    inputs = trace_input_qualification(captures["before"], captures["after"], arms=("02-r0-t0", "03-r0-t0") if compact else None)
    histories = {name: motion_history(*capture) for name, capture in captures.items()}
    assert all(h is not None for h in histories.values())
    history_ok = all(h["oldPixelAge"] == 0 and h["acceptedOldPixelHistory"] == 0
                     and h["temporalMinusResolveMax"] == 0 for history in histories.values() for h in history.values())
    moments_ok = all(h.get("nativeMoments", {}).get("oldAge") == 0
                     and h["nativeMoments"]["ageRange"] == [1.0, 1.0]
                     and h["nativeMoments"]["varianceMax"] == 0
                     for h in histories["after"].values())
    return dict(schema="screen-probe-native-moments-motion.v1", baseline=str(baseline), candidate=str(candidate),
                qualified=bool((compact or core["qualityComparisonQualified"]) and inputs["qualified"] and history_ok and moments_ok),
                inputQualification=inputs, histories=histories, core=core,
                limitations=["Final-minus-resolve includes spatial filtering; only temporal-minus-resolve isolates pixel history",
                             "Newly exposed pixels are classified by the existing strict geometry thresholds",
                             "Exact input gates remain unchanged; no GPU timing claim"])


def verify_replay_sources(root, capture, protocol):
    for label, expected in protocol["referenceSourceSha256"].items():
        assert hashlib.sha256((root / f"reference-{label}.compute.txt").read_bytes()).hexdigest() == expected
    programs = capture["replayPrograms"]
    assert len(programs) == 4 and {(p["reference"], p["name"]) for p in programs} == {
        (label, name) for label in ("aa", "control") for name in ("FilterSdfDiffuseAtrous", "FilterSdfDiffuseAtrousFinal")}


def replay_hash_differences(rows, arms, labels):
    differences = []
    for arm in arms:
        for label in labels:
            for frame in FRAMES:
                for actual, replay in (("temporal-", "replay-input-"), ("final-", "replay-aa-")):
                    a, b = rows[arm, frame, actual + label], rows[arm, frame, replay + label]
                    if any(a[k] != b[k] for k in ("sha256", "x", "y", "width", "height", "format")):
                        differences.append([arm, frame, actual + label])
    return differences


def qualify_temporal_replay(root, capture, rows, protocol, arms, labels):
    if not protocol.get("temporalMomentReplay", False):
        return None
    for label, expected in protocol["temporalSourceSha256"].items():
        assert hashlib.sha256((root / f"temporal-{label}.compute.txt").read_bytes()).hexdigest() == expected
    programs = capture["temporalReplayPrograms"]
    packed = protocol.get("controlMomentLayout") == "packed-dual-variance"
    dual = protocol.get("controlMomentLayout") in ("dual-variance", "packed-dual-variance")
    expected_programs = {
        (label, "ScreenProbeTemporalReprojectionWithMomentsCS") for label in ("aa", "control")}
    if dual:
        expected_programs.add(("initialize", "InitializeDualVarianceHistoryCS"))
    if packed:
        expected_programs.add(("slow", "ScreenProbeUpdateSlowVarianceCS"))
    assert len(programs) == len(expected_programs) and {(p["reference"], p["name"]) for p in programs} == expected_programs
    differences, results = [], {}
    frames = (0, 1, *FRAMES)
    for arm in arms:
        results[arm] = {}
        for label in labels:
            initial = rows[arm, 0, "temporal-initial-actual-" + label]
            copied = rows[arm, 0, "temporal-initial-copy-" + label]
            if dual:
                old, new = read(initial), read(copied)
                expected_slow = np.where((old[..., 2] >= 1) & (old[..., 3] > 0), old[..., 1].astype(float) + 2**-14, 0).astype("<f2") if packed else old[..., 1]
                initial_matches = np.array_equal(old[..., :3], new[..., :3]) and np.array_equal(expected_slow, new[..., 3])
            else:
                initial_matches = all(initial[k] == copied[k] for k in ("sha256", "x", "y", "width", "height", "format"))
            if not initial_matches:
                differences.append([arm, 0, "initial-copy", label])
            for frame in frames:
                for reference in ("aa", "control"):
                    for kind in ("lighting", "history", "geometry", "moments"):
                        if reference == "control" and kind == "moments":
                            continue  # Check the layout's invariant channels below.
                        actual = rows[arm, frame, f"temporal-actual-{kind}-{label}"]
                        replay = rows[arm, frame, f"temporal-{reference}-{kind}-{label}"]
                        if any(actual[k] != replay[k] for k in ("sha256", "x", "y", "width", "height", "format")):
                            differences.append([arm, frame, reference, kind, label])
            tag = "native-moments" if label == "emitter-gap" else "native-moments-" + label
            actual = np.stack([read(rows[arm, f, "temporal-actual-moments-" + label if f < 2 else tag]).astype(float) for f in frames])
            control = np.stack([read(rows[arm, f, "temporal-control-moments-" + label]).astype(float) for f in frames])
            assert np.isfinite(control).all() and (control[..., 1:] >= 0).all()
            invariant_channels = (0, 1, 2) if dual else (0, 2, 3)
            unchanged = int(np.count_nonzero(actual[..., invariant_channels] != control[..., invariant_channels]))
            variance_channel = 3 if dual else 1
            control_variance = np.maximum(0, control[2:, ..., variance_channel] - (2**-14 if packed else 0))
            results[arm][label] = dict(invariantChannelDifferentValues=unchanged, invariantChannels=list(invariant_channels),
                actualMeanVariance=float(actual[2:, ..., 1].mean()), controlMeanVariance=float(control_variance.mean()),
                actualVarianceTemporalRms=float(np.sqrt(np.mean(np.var(actual[2:, ..., 1], axis=0)))),
                controlVarianceTemporalRms=float(np.sqrt(np.mean(np.var(control_variance, axis=0)))))
    return dict(schema="screen-probe-temporal-moments-replay.v1",
        qualified=not differences and not any(v["invariantChannelDifferentValues"] for a in results.values() for v in a.values()),
        hashDifferences=differences, arms=results,
        aaHashObservations=len(arms) * len(labels) * len(frames) * 4,
        initialHistoryCopyHashObservations=len(arms) * len(labels),
        controlColorHistoryGeometryHashObservations=len(arms) * len(labels) * len(frames) * 3,
        limitations=["Control moments evolve independently, but receiver geometry and pixel color histories remain shared",
                     "AA covers four output roles in recorded ROIs and frames; not unsaved full-screen pixels",
                     "A passing provenance gate does not establish a quality gain or dynamic lighting acceptance"])


def compare_same_frame(root, reference_root, matched_reference_subset=False):
    _, snapshot, blocks, _, _ = qualify(reference_root, "inside-shell-reference")
    geometry = read(snapshot["geometry"])[:68, :120]
    target = blocks.mean(0)[..., 4]
    report, (capture, rows, _) = analyze(root, geometry, target, matched_reference_subset)
    assert capture["schema"] == "screen-probe-atrous-same-frame.v1"
    protocol = load(root / "protocol.json")
    verify_replay_sources(root, capture, protocol)
    results = {}
    labels = ("emitter-gap", "ceiling", "floor", "back")
    differences = replay_hash_differences(rows, ARMS, labels)
    temporal = qualify_temporal_replay(root, capture, rows, protocol, ARMS, labels)
    for arm in ARMS:
        results[arm] = {}
        for label in labels:
            stack = lambda prefix: np.stack([read(rows[arm, f, prefix + label]).astype(float) for f in FRAMES])
            current, control = stack("final-"), stack("control-")
            coverage_differences = int(np.count_nonzero(current[..., 3] != control[..., 3]))
            current_y = (current[..., :3] * abs(current[..., 3:4])) @ LUMA
            control_y = (control[..., :3] * abs(control[..., 3:4])) @ LUMA
            result = dict(paired(control_y, current_y), current=metrics(current_y), control=metrics(control_y),
                          coverageDifferences=coverage_differences)
            if label == "emitter-gap":
                px = np.arange(46, 74) * 16 + 8 - 720
                result["points"] = {}
                for name, y in (("current", current_y), ("control", control_y)):
                    points = y[:, 16, px]
                    error = points - target[59, np.arange(46, 74)]
                    result["points"][name] = dict(metrics(points), referenceBias=float(error.mean()),
                        referenceRmsError=float(np.sqrt(np.mean(error**2))))
            results[arm][label] = result
    frozen_moments = protocol.get("controlMomentsFrozen", False)
    frozen_differences = []
    if frozen_moments:
        if not protocol.get("controlMomentHashesRecorded", False):
            frozen_differences.append("Frozen control moments have no runtime hash observations")
        else:
            for arm in ARMS:
                for label in labels:
                    actual_tag = "native-moments" if label == "emitter-gap" else "native-moments-" + label
                    first = rows[arm, 1024, actual_tag]
                    for frame in FRAMES:
                        frozen = rows[arm, frame, "control-moments-" + label]
                        if any(first[k] != frozen[k] for k in ("sha256", "x", "y", "width", "height", "format")):
                            frozen_differences.append([arm, frame, label])
    return dict(schema="screen-probe-atrous-same-frame-analysis.v1", capture=str(root),
        qualified=report["qualified"] and not differences and not frozen_differences
            and (temporal is None or temporal["qualified"]) and not any(v["coverageDifferences"] for a in results.values() for v in a.values()),
        replayDifferences=differences, inputHashObservations=4 * 256, aaHashObservations=4 * 256,
        controlMomentsFrozen=frozen_moments, frozenMomentDifferences=frozen_differences,
        controlIterations=protocol.get("controlIterations", 4), temporalReplay=temporal, sourceAndReference=report, arms=results,
        limitations=["AA is byte-exact in four recorded ROIs, not a claim about unsaved full-screen pixels",
                     "Both filters consume one copied pixel-temporal input and the same receiver textures",
                     "The control evolves independent temporal moments" if temporal is not None else
                         "The control uses first-measured-frame frozen moments" if frozen_moments else "Both filters consume the actual current moment textures",
                     "The control isolates wavelet behavior; changes upstream of the copied input require separate validation",
                     "Replay adds dispatches/readbacks and is not a GPU performance experiment",
                     "The frozen-source high-sample reference is not absolute Unreal truth"])


def compare_same_frame_motion(root):
    capture, rows, states = inspect(root, motion_only=True)
    assert capture["schema"] == "screen-probe-atrous-same-frame-motion.v1"
    protocol = load(root / "protocol.json")
    arms = ("02-r0-t0", "03-r0-t0")
    assert protocol["motionHistory"] and protocol["arms"] == list(arms)
    verify_replay_sources(root, capture, protocol)
    differences = replay_hash_differences(rows, arms, ("edge",))
    temporal = qualify_temporal_replay(root, capture, rows, protocol, arms, ("edge",))
    control_rows = dict(rows)
    for arm in arms:
        for frame in FRAMES:
            control_rows[arm, frame, "final-edge"] = rows[arm, frame, "control-edge"]
    histories = dict(current=motion_history(capture, rows, states, comparison_rows=control_rows,
                                          control_moment_layout=protocol.get("controlMomentLayout")),
                     control=motion_history(capture, control_rows, states,
                                            control_moment_layout=protocol.get("controlMomentLayout")))
    results = {}
    for arm in arms:
        current = np.stack([read(rows[arm, f, "final-edge"]).astype(float) for f in FRAMES])
        control = np.stack([read(rows[arm, f, "control-edge"]).astype(float) for f in FRAMES])
        current_y = (current[..., :3] * abs(current[..., 3:4])) @ LUMA
        control_y = (control[..., :3] * abs(control[..., 3:4])) @ LUMA
        delta = abs(current_y - control_y)
        results[arm] = dict(coverageDifferences=int(np.count_nonzero(current[..., 3] != control[..., 3])),
            currentMeanY=float(current_y.mean()), controlMeanY=float(control_y.mean()),
            meanChangePercent=float(100 * (current_y.mean() / control_y.mean() - 1)),
            pairedOutputDifferenceP99=float(np.quantile(delta, .99)), pairedOutputDifferenceMax=float(delta.max()))
    history_ok = all(h["oldPixelAge"] == 0 and h["acceptedOldPixelHistory"] == 0
                     and h["temporalMinusResolveP99"] == 0 and h["temporalMinusResolveMax"] == 0
                     and h["nativeMoments"]["oldAge"] == 0 and h["nativeMoments"]["varianceMax"] == 0
                     for group in histories.values() for h in group.values())
    if protocol.get("controlMomentLayout") == "packed-dual-variance":
        history_ok = history_ok and all(h["controlSlowMoments"]["ageRange"] == [1, 1]
            and h["controlSlowMoments"]["maximumSlowVariance"] == 0
            and h["controlSlowMoments"]["invalidPackedValues"] == 0 for h in histories["control"].values())
    return dict(schema="screen-probe-atrous-same-frame-motion-analysis.v1", capture=str(root),
        qualified=not differences and history_ok and (temporal is None or temporal["qualified"])
            and not any(v["coverageDifferences"] for v in results.values()),
        replayDifferences=differences, inputHashObservations=256, aaHashObservations=256,
        pixelHistoryRejectedAtDisocclusion=history_ok,
        exposedOutputExactlyEqual={arm: histories["current"][arm]["finalExposedRgba16Sha256"]
            == histories["control"][arm]["finalExposedRgba16Sha256"] for arm in arms},
        histories=histories, arms=results, temporalReplay=temporal,
        limitations=["Same-frame copied input and A/A qualification cover the moving-edge ROI only",
                     "The control evolves independent moments but shares actual pixel/probe color history" if temporal is not None else
                         "The control changes only spatial filtering; its own pixel/probe history is not evolved independently",
                     "Final-minus-resolve includes spatial reconstruction and is not error relative to physical ground truth",
                     "Replays invalidate GPU performance timing; no cross-run GBuffer matching claim"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--reference-root", type=Path)
    parser.add_argument("--matched-reference-subset", action="store_true")
    parser.add_argument("--motion-only", action="store_true")
    parser.add_argument("--same-frame", action="store_true")
    parser.add_argument("--compact-motion", action="store_true", help="Analyze two moving arms without duplicated static captures")
    args = parser.parse_args()
    if args.same_frame:
        if args.motion_only:
            report = compare_same_frame_motion(args.candidate.resolve())
        else:
            assert args.reference_root, "Static replay requires the high-sample reference"
            report = compare_same_frame(args.candidate.resolve(), args.reference_root.resolve(), args.matched_reference_subset)
        (args.candidate / "same-frame-comparison.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(dict(qualified=report["qualified"], replayDifferences=report["replayDifferences"],
                             arms=report["arms"]), indent=2))
        raise SystemExit(0)
    assert args.baseline, "Independent-run comparison requires a baseline"
    if args.motion_only:
        report = compare_motion(args.baseline.resolve(), args.candidate.resolve(), args.compact_motion)
        (args.candidate / "moments-motion-comparison.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(dict(qualified=report["qualified"], inputs=report["inputQualification"],
                             histories=report["histories"], motion=report["core"]["motion"] if report["core"] else None), indent=2))
        raise SystemExit(0)
    assert args.reference_root, "Static analysis requires the frozen high-sample reference"
    report = compare(args.baseline.resolve(), args.candidate.resolve(), args.reference_root.resolve(), args.matched_reference_subset)
    (args.candidate / "atrous-comparison.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(qualified=report["qualified"], inputDifferences=report["crossConfigurationInputDifferences"],
                         final={a: dict(before=r["before"]["stages"]["final-emitter-gap"],
                                        after=r["after"]["stages"]["final-emitter-gap"]) for a, r in report["arms"].items()}), indent=2))
