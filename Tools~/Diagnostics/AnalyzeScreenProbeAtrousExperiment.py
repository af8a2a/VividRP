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


def inspect(root):
    capture = load(root / "capture.json")
    assert capture["schema"] == "screen-probe-atrous-stages.v1"
    assert capture["complete"] and not capture["errors"]
    assert (capture["warmup"], capture["measured"], capture["lastFrame"]) == (1024, 128, 1152)
    assert load(root / "source-verification.json")["unchanged"]
    rows = {(r["arm"], r["frame"], r["tag"]): r for r in capture["records"] if "tag" in r}
    states = {(r["arm"], r["frame"]): r["qualityState"] for r in capture["records"] if "qualityState" in r}
    assert len(rows) == sum("tag" in r for r in capture["records"])
    assert set(states) == {(a, f) for a in ARMS for f in FRAMES}
    completed = [r for r in capture["records"] if r.get("completed")]
    assert len(completed) == 2 and all(r["targetCount"] == 1152 and r["sourceFrames"] == 264
                                     and r["otherCount"] == 0 for r in completed)
    assert all(s["order"] == 8 and s["batch"] == 32 and s["updates"] == 0 and s["anchorAge"] == 512
               and s["flags"] == 0 and s["atrous"] == 4 and not s["moving"]
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
        for tag in STAGES:
            values = stack(tag)
            y = ((values[..., :3] * abs(values[..., 3:4])) @ LUMA)[:, py, px]
            error = y[matched] - target[59, xs]
            stages[tag] = dict(metrics(y), referenceBias=float(error.mean()),
                               referenceRmsError=float(np.sqrt(np.mean(error**2))))
        variance = {}
        for step in range(3):
            values = np.stack([scalar_read(rows[arm, f, f"variance-{step}"]) for f in FRAMES])[:, py, px]
            variance[str(step)] = dict(mean=float(values.mean()),
                                      quantiles=np.quantile(values, [0, .5, .99, 1]).tolist())
        moments = None
        if (arm, 1024, "native-moments") in rows:
            values = stack("native-moments")[:, py, px]
            assert (values[..., 1:] >= 0).all()
            moments = dict(channels=["logMean", "centralLogVariance", "effectiveAge", "coverage"],
                           quantiles=np.quantile(values, [0, .5, 1], axis=(0, 1)).tolist())
        result[arm] = dict(stages=stages, variance=variance, moments=moments,
                           exactReferenceFrames=int(matched.sum()),
                           regions={tag: metrics((stack(tag)[..., :3] * abs(stack(tag)[..., 3:4])) @ LUMA)
                                    for tag in ("final-ceiling", "final-floor", "final-back")})
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


def compare_motion(baseline, candidate):
    assert baseline.parent == candidate.parent
    core = quality_analysis(candidate.parent, candidate.name, baseline)
    captures = {name: quality_inspect(path) for name, path in (("before", baseline), ("after", candidate))}
    inputs = trace_input_qualification(captures["before"], captures["after"])
    histories = {name: motion_history(*capture) for name, capture in captures.items()}
    assert all(h is not None for h in histories.values())
    history_ok = all(h["oldPixelAge"] == 0 and h["acceptedOldPixelHistory"] == 0
                     and h["temporalMinusResolveMax"] == 0 for history in histories.values() for h in history.values())
    moments_ok = all(h.get("nativeMoments", {}).get("oldAge") == 0
                     and h["nativeMoments"]["ageRange"] == [1.0, 1.0]
                     and h["nativeMoments"]["varianceMax"] == 0
                     for h in histories["after"].values())
    return dict(schema="screen-probe-native-moments-motion.v1", baseline=str(baseline), candidate=str(candidate),
                qualified=bool(core["qualityComparisonQualified"] and inputs["qualified"] and history_ok and moments_ok),
                inputQualification=inputs, histories=histories, core=core,
                limitations=["Final-minus-resolve includes spatial filtering; only temporal-minus-resolve isolates pixel history",
                             "Newly exposed pixels are classified by the existing strict geometry thresholds",
                             "Exact input gates remain unchanged; no GPU timing claim"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--reference-root", type=Path)
    parser.add_argument("--matched-reference-subset", action="store_true")
    parser.add_argument("--motion-only", action="store_true")
    args = parser.parse_args()
    if args.motion_only:
        report = compare_motion(args.baseline.resolve(), args.candidate.resolve())
        (args.candidate / "moments-motion-comparison.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(dict(qualified=report["qualified"], inputs=report["inputQualification"],
                             histories=report["histories"], motion=report["core"]["motion"]), indent=2))
        raise SystemExit(0)
    assert args.reference_root, "Static analysis requires the frozen high-sample reference"
    report = compare(args.baseline.resolve(), args.candidate.resolve(), args.reference_root.resolve(), args.matched_reference_subset)
    (args.candidate / "atrous-comparison.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(qualified=report["qualified"], inputDifferences=report["crossConfigurationInputDifferences"],
                         final={a: dict(before=r["before"]["stages"]["final-emitter-gap"],
                                        after=r["after"]["stages"]["final-emitter-gap"]) for a, r in report["arms"].items()}), indent=2))
