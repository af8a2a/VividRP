"""Audit short directional-history captures and frozen original-kernel replays."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def analyze(root):
    root = root.resolve()
    capture = load(root / "capture.json")
    assert capture["schema"] == "screen-probe-directional-binding.v1"
    assert capture["complete"] and not capture["errors"]
    assert capture["lastFrame"] == 64 and capture["graphicsApi"] == "Direct3D12"
    assert load(root / "source-verification.json")["unchanged"]
    entries = {(r["arm"], r["frame"], r["tag"]): r for r in capture["records"] if "tag" in r}
    assert len(entries) == sum("tag" in r for r in capture["records"])
    states = {(r["arm"], r["frame"]): r for r in capture["records"] if "bindings" in r}
    completed = [r for r in capture["records"] if r.get("completed")]
    assert len(completed) == 8 and len(states) == 512
    assert all(r["targetCount"] == 64 and r["sourceFrames"] == 264 and r["otherCount"] == 0
               and not r["recorderMode"] and not r["tickMode"] for r in completed)
    arms = [r["arm"] for r in completed]
    history_errors, identity_errors, state_errors, frozen_input_errors = [], [], [], []
    history_pairs = (("previous-directional", "directional-temporal"),
                     ("previous-geometry", "geometry"), ("previous-directional-filtered", "directional-filtered"))
    def same_constants(a, b):
        vectors = ("previousVP", "screenSize", "cameraForward", "historyLightScale")
        return (all(np.array_equal(np.asarray(a[key], dtype="<f4").view("<u4"),
                                   np.asarray(b[key], dtype="<f4").view("<u4")) for key in vectors)
                and all(a[key] == b[key] for key in ("restirDense", "adaptive", "flags")))
    for arm in arms:
        for frame in range(64):
            row = states[arm, frame]
            reference = states[arms[0], frame]
            if not same_constants(row["constants"], reference["constants"]) or row["predicates"] != reference["predicates"]:
                state_errors.append([arm, frame])
            bindings = {v["field"]: v["id"] for v in row["bindings"]}
            for field, owner in (("m_Geometry", "geometryCurrent"), ("m_PreviousGeometry", "geometryPrevious"),
                                 ("m_DirectionalTemporal", "directionalCurrent"), ("m_PreviousDirectional", "directionalPrevious")):
                if bindings[field] != row["historyIds"][owner]:
                    identity_errors.append([arm, frame, field, "owner"])
            for current, previous in (("m_Geometry", "m_PreviousGeometry"), ("m_DirectionalTemporal", "m_PreviousDirectional")):
                if bindings[current] == bindings[previous]:
                    identity_errors.append([arm, frame, current, "alias"])
            if entries[arm, frame, "geometry"]["sha256"] != entries[arms[0], frame, "geometry"]["sha256"]:
                frozen_input_errors.append([arm, frame, "geometry"])
            if frame in (0, 63) and entries[arm, frame, "radiosity"]["sha256"] != entries[arms[0], frame, "radiosity"]["sha256"]:
                frozen_input_errors.append([arm, frame, "radiosity"])
            if frame:
                for previous, current in history_pairs:
                    if entries[arm, frame, previous]["sha256"] != entries[arm, frame - 1, current]["sha256"]:
                        history_errors.append([arm, frame, previous])
                if row["historyIds"]["directionalPrevious"] != states[arm, frame - 1]["historyIds"]["directionalCurrent"]:
                    identity_errors.append([arm, frame, "directional", "swap"])

    saved = {(r["arm"], r["frame"], r["tag"]): r for r in capture["savedPayloads"]}
    saved_bytes = 0
    for key, row in saved.items():
        path = Path(row["path"]).resolve()
        assert path.is_relative_to(root)
        data = path.read_bytes()
        assert hashlib.sha256(data).hexdigest() == entries[key]["sha256"]
        saved_bytes += len(data)
    assert saved_bytes == capture["savedPayloadBytes"] <= capture["savedPayloadBudget"]
    assert capture["retainedPayloadBytes"] <= capture["retainedPayloadBudget"]
    bundle = capture["firstBundle"]
    assert bundle
    a, b, first = bundle["reference"], bundle["repeated"], bundle["firstFrame"]
    bundle_stage_differences = {
        frame: sorted(tag for arm, f, tag in entries if arm == a and f == frame
                      and entries[a, frame, tag]["sha256"] != entries[b, frame, tag]["sha256"])
        for frame in (first - 1, first)
    }
    functional = ("geometry", "raw", "composite", "directional-temporal", "directional-filtered")
    first_differences = {}
    for arm in arms[1:]:
        differing = [[f, tag] for f in range(64) for tag in functional
                     if entries[arms[0], f, tag]["sha256"] != entries[arm, f, tag]["sha256"]]
        first_differences[arm] = differing[:5]
    replay_rows, replay_summaries = {}, {}
    for name in ("replay", "replay-direct"):
        replay = load(root / name / "replay.json")
        assert replay["complete"] and replay["isPaused"] and replay["isPlaying"]
        assert load(root / name / "source-verification.json")["unchanged"]
        rows = [v for v in replay["records"] if "repetition" in v]
        uploads = [v for v in replay["records"] if "uploadedInputHash" in v]
        assert len(rows) == 34 and len(uploads) == 16
        assert all(v["uploadedInputHash"] == entries[v["arm"], v["frame"], v["tag"]]["sha256"] for v in uploads)
        replay_rows[name] = {(v["arm"], v["frame"], v["rejectHistory"], v["repetition"]): v for v in rows}
        groups = sorted({(v["arm"], v["frame"], v["rejectHistory"]) for v in rows})
        replay_summaries[name] = []
        for arm, frame, rejected in groups:
            selected = [v for v in rows if (v["arm"], v["frame"], v["rejectHistory"]) == (arm, frame, rejected)]
            replay_summaries[name].append(dict(arm=arm, frame=frame, rejectHistory=rejected,
                                               count=len(selected), uniqueHashes=len({v["sha256"] for v in selected}),
                                               matchesProduction=sum(v["matchesProduction"] for v in selected)))
        for row in rows:
            if row["path"]:
                payload = Path(row["path"]).read_bytes()
                assert hashlib.sha256(payload).hexdigest() == row["sha256"]
                saved_bytes += len(payload)
    assert saved_bytes <= capture["savedPayloadBudget"]
    clone_direct_differences = [list(key) for key, row in replay_rows["replay"].items()
                                if row["sha256"] != replay_rows["replay-direct"][key]["sha256"]]
    scan = load(root / "replay-ulp-scan" / "replay.json")
    assert scan["complete"] and load(root / "replay-ulp-scan" / "source-verification.json")["unchanged"]
    scan_summary = []
    for frame in (first - 1, first):
        rows = [v for v in scan["records"] if v.get("frame") == frame and "variant" in v]
        assert len(rows) == 61
        scan_summary.append(dict(frame=frame, variants=len(rows), primaryMatches=sum(v["matchesProduction"] for v in rows),
                                 otherMatches=sum(v["matchesOther"] for v in rows)))
    x, y = [np.fromfile(saved[arm, first, "directional-temporal"]["path"], dtype="<f2").astype(float).reshape(408, 1920, 4)
            for arm in (a, b)]
    assert np.isfinite(x).all() and np.isfinite(y).all()
    delta = np.abs(y - x)
    bands = [dict(differentTexels=int(np.count_nonzero(np.any(part != 0, axis=-1))),
                  maximumComponentError=float(part.max())) for part in np.split(delta, 3, axis=0)]
    return dict(schema="screen-probe-directional-binding-analysis.v1", captureComplete=True,
                renderingChange=False, qualityBenefitVerified=False, performanceComparisonValid=False,
                firstBundle=bundle, sourceUnchanged=True, historyComparisons=8 * 63 * 3,
                bundleStageDifferences=bundle_stage_differences,
                historyContentErrors=history_errors, cpuTextureIdentityErrors=identity_errors,
                selectedCpuStateDifferences=state_errors, frozenInputErrors=frozen_input_errors,
                firstFunctionalDifferences=first_differences, replay=replay_summaries,
                cloneDirectComparedPairs=34, cloneDirectDifferences=clone_direct_differences,
                firstProductionDifferenceBands=bands, ulpScan=scan_summary,
                savedPayloadBytesIncludingReplays=saved_bytes, savedPayloadBudget=capture["savedPayloadBudget"],
                limitations=["CPU-selected predicates/constants are reconstructed from pass state, not GPU b0 bytes",
                             "Object identities verify CPU ownership, not physical heap placement or GPU descriptors",
                             "Camera input snapshots occur after the pass; they do not prove contents at the original dispatch",
                             "Frozen frame before first divergence does not exactly match production; replay is not full execution equivalence",
                             "No fence, compiler, or rounding fix is justified by these controls alone"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    result = analyze(args.root)
    (args.root / "analysis.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: result[k] for k in ("firstBundle", "historyComparisons", "historyContentErrors",
                                           "cpuTextureIdentityErrors", "selectedCpuStateDifferences", "frozenInputErrors",
                                           "cloneDirectDifferences", "savedPayloadBytesIncludingReplays")}))
