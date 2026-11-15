"""Locate frozen-source Cornell error before and after scalar/SH reconstruction."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeResidualReference import load, qualify
from EmissiveRadiosityConvergence import plane_masks
from ProbeAnchorCalibration import LUMA, evaluate_sh, normal_from_packed
from TemporalSamplingExperiment import metrics, paired, read


def inspect(root):
    capture = load(root / "capture.json")
    protocol = load(root / "protocol.json")
    assert capture["schema"] == "screen-probe-anchor-stage-experiment.v1"
    assert capture["complete"] and not capture["errors"]
    assert (capture["warmup"], capture["measured"], capture["lastFrame"]) == (1024, 128, 1152)
    assert load(root / "source-verification.json")["unchanged"]
    rows = {(r["arm"], r["frame"], r["tag"]): r for r in capture["records"] if "tag" in r}
    states = {(r["arm"], r["frame"]): r["qualityState"] for r in capture["records"] if "qualityState" in r}
    assert len(rows) == sum("tag" in r for r in capture["records"])
    assert len(states) == 256
    completed = [r for r in capture["records"] if r.get("completed")]
    assert len(completed) == 2 and all(r["targetCount"] == 1152 and r["otherCount"] == 0 for r in completed)
    assert all(s["order"] == 8 and s["batch"] == 32 and s["updates"] == 0 and s["anchorAge"] == 512
               and s["flags"] == 0 and s["atrous"] == 4 and not s["moving"] for s in states.values())
    source_hashes = {r["sha256"] for r in rows.values() if r["tag"] == "radiosity"}
    assert source_hashes == {protocol["sourceSha256"]}
    total = 0
    for row in rows.values():
        if not row["saved"]:
            continue
        path = Path(row["path"]).resolve()
        assert path.is_relative_to(root.resolve())
        data = path.read_bytes()
        assert hashlib.sha256(data).hexdigest() == row["sha256"]
        total += len(data)
    assert total == capture["savedPayloadBytes"] <= capture["savedPayloadBudget"] == protocol["payloadBudget"]
    inputs_differ = []
    for frame in range(1024, 1152):
        for tag in ("input-gbuffer0", "input-gbuffer1", "input-depth", "input-placement", "geometry", "geometry-emitter-gap"):
            a, b = (rows[arm, frame, tag] for arm in protocol["arms"])
            if any(a[k] != b[k] for k in ("sha256", "width", "height", "format")):
                inputs_differ.append([frame, tag])
        for key in ("vp", "inverseVp", "pose"):
            if states["00-r0-t0", frame][key] != states["01-r0-t0", frame][key]:
                inputs_differ.append([frame, key])
    return capture, protocol, rows, states, inputs_differ


def analyze(root, reference_root, matched_reference_subset=False):
    capture, protocol, rows, states, inputs_differ = inspect(root)
    _, reference_snapshot, blocks, _, _ = qualify(reference_root, "inside-shell-reference")
    target = blocks.mean(0)[..., 4]
    reference_geometry = read(reference_snapshot["geometry"])[:68, :120]
    plane = plane_masks(read(reference_snapshot["geometry"]))
    xs = np.arange(46, 74)
    px, py = xs * 16 + 8 - 720, 59 * 16 + 8 - 936
    arm_results = {}
    reference_geometry_differences = []
    for arm in protocol["arms"]:
        stack = lambda tag: np.stack([read(rows[arm, frame, tag]).astype(float) for frame in range(1024, 1152)])
        geometry = stack("geometry")[:, :68, :120]
        same_reference = np.all(geometry[:, 59, xs] == reference_geometry[59, xs], axis=(1, 2))
        if not same_reference.all():
            reference_geometry_differences.extend([arm, int(frame)] for frame in np.arange(1024, 1152)[~same_reference])
            assert matched_reference_subset, "Reference point geometry differs"
        assert same_reference.any(), "No exact reference frames"
        normal = normal_from_packed(np.maximum(geometry[..., 3], 0)).reshape(128, -1, 3)
        stages = {}
        for tag, label in (("raw", "raw"), ("prepared-center", "temporalAnchor"), ("reconstructed-center", "halfStepAnchor")):
            values = stack(tag)[:, :68, :120]
            stages[label] = (values[..., :3] * values[..., 3:4]) @ LUMA
        sh = stack("sh-center").reshape(128, 68, 7, 120, 4).transpose(0, 2, 1, 3, 4).reshape(128, 7, -1, 4)
        anchor = stack("anchor-center")
        stages["shAtNormal"] = (np.maximum(evaluate_sh(sh, normal).reshape(128, 68, 120, 3), 0)
                                    * anchor[..., 3:4]) @ LUMA
        center = {}
        for name, values in stages.items():
            selected = values[:, 59, xs]
            error = selected[same_reference] - target[59, xs]
            center[name] = dict(metrics(selected), referenceBias=float(error.mean()),
                                referenceRmsError=float(np.sqrt(np.mean(error**2))))
        for tag, label in (("resolved-emitter-gap", "resolve"), ("temporal-emitter-gap", "pixelTemporal"), ("final-emitter-gap", "final")):
            values = stack(tag)
            selected = ((values[..., :3] * abs(values[..., 3:4])) @ LUMA)[:, py, px]
            error = selected[same_reference] - target[59, xs]
            center[label] = dict(metrics(selected), referenceBias=float(error.mean()),
                                referenceRmsError=float(np.sqrt(np.mean(error**2))))
        arm_results[arm] = dict(points=center, planes={}, exactReferenceFrames=int(same_reference.sum()))
        for name, mask in plane.items():
            arm_results[arm]["planes"][name] = {label: dict(metrics(values[:, mask]),
                referenceBias=float((values[:, mask] - target[mask]).mean())) for label, values in stages.items()}
    return dict(schema="screen-probe-anchor-stage-analysis.v1", qualified=not (inputs_differ or reference_geometry_differences),
        inputDifferences=inputs_differ, payloadBytes=capture["savedPayloadBytes"],
        referenceGeometryDifferences=reference_geometry_differences,
        referenceMeanY=float(target[59, xs].mean()), referencePoints=28, arms=arm_results,
        limitations=["Static source-matched stage diagnosis; no performance or motion result",
                     "Reference shares the frozen Surface Cache; it is not absolute Unreal truth",
                     "When requested, reference error uses only exactly matching frames; any missing frame keeps whole-capture qualification false",
                     "Pixel-center comparison uses exactly matched probe receiver geometry and unmodulated linear GI"])


def compare(baseline, candidate, reference_root):
    reports = [analyze(root, reference_root) for root in (baseline, candidate)]
    captures = [inspect(root) for root in (baseline, candidate)]
    differences = []
    for arm in captures[0][1]["arms"]:
        for frame in range(1024, 1152):
            for tag in ("input-gbuffer0", "input-gbuffer1", "input-depth", "input-placement", "geometry", "geometry-emitter-gap"):
                a, b = (capture[2][arm, frame, tag] for capture in captures)
                if any(a[k] != b[k] for k in ("sha256", "width", "height", "format")):
                    differences.append([arm, frame, tag])
            for key in ("vp", "inverseVp", "pose"):
                if captures[0][3][arm, frame][key] != captures[1][3][arm, frame][key]:
                    differences.append([arm, frame, key])
    xs = np.arange(46, 74)
    points, planes = {}, {}
    for arm in captures[0][1]["arms"]:
        values = []
        for capture in captures:
            data = np.stack([read(capture[2][arm, f, "final-emitter-gap"]).astype(float) for f in range(1024, 1152)])
            values.append(((data[..., :3] * data[..., 3:4]) @ LUMA)[:, 16, xs * 16 + 8 - 720])
        before, after = (report["arms"][arm] for report in reports)
        points[arm] = dict(paired(*values), before=before["points"]["final"], after=after["points"]["final"])
        planes[arm] = {}
        for name in before["planes"]:
            planes[arm][name] = {}
            for stage in ("temporalAnchor", "halfStepAnchor", "shAtNormal"):
                a, b = before["planes"][name][stage], after["planes"][name][stage]
                planes[arm][name][stage] = dict(meanChangePercent=100 * (b["mean_y"] / a["mean_y"] - 1),
                    beforeReferenceBias=a["referenceBias"], afterReferenceBias=b["referenceBias"])
    return dict(schema="screen-probe-anchor-stage-comparison.v1", baseline=str(baseline), candidate=str(candidate),
        qualified=all(report["qualified"] for report in reports) and not differences,
        inputDifferences=differences, finalPoints=points, planes=planes,
        limitations=["Static stage and receiver-point quality only; full-motion qualification and performance are separate",
                     "Reference shares the source cache; no gain fitting or absolute Unreal accuracy claim"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--reference-root", type=Path, required=True)
    parser.add_argument("--baseline", type=Path, help="Also compare all actual inputs with an existing static stage capture")
    parser.add_argument("--matched-reference-subset", action="store_true", help="Diagnose only exact reference frames; unmatched captures remain unqualified")
    args = parser.parse_args()
    result = analyze(args.capture.resolve(), args.reference_root.resolve(), args.matched_reference_subset)
    (args.capture / "stage-analysis.json").write_text(json.dumps(result, indent=2)+"\n", encoding="utf-8")
    if args.baseline:
        comparison = compare(args.baseline.resolve(), args.capture.resolve(), args.reference_root.resolve())
        (args.capture / "stage-comparison.json").write_text(json.dumps(comparison, indent=2)+"\n", encoding="utf-8")
    print(json.dumps(dict(qualified=result["qualified"], inputs=result["inputDifferences"],
                         reference=result["referenceMeanY"], stages={k: v["points"] for k, v in result["arms"].items()}), indent=2))
