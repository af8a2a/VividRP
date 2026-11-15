"""Audit the uninstrumented eight-arm, 64-frame GI repeatability gate."""
import argparse
import json
from pathlib import Path

from AnalyzeScreenProbeUnusedBinWitness import cpu_words, inspect


def analyze(root):
    capture, entries, _, verified = inspect(root)
    assert not capture["expectWitness"] and not capture["witnesses"]
    states = {(r["arm"], r["frame"]): r for r in capture["records"] if "constants" in r}
    arms = sorted({k[0] for k in states})
    functional = ("geometry", "raw", "composite", "directional-temporal", "directional-filtered")
    differences, other_differences, cpu_differences, identity_errors = [], [], [], []
    for arm in arms:
        for frame in range(64):
            state, reference = states[arm, frame], states[arms[0], frame]
            assert state["constants"]["flags"] == 0
            if cpu_words(state) != cpu_words(reference) or state["predicates"] != reference["predicates"]:
                cpu_differences.append([arm, frame])
            bindings = {r["field"]: r["id"] for r in state["bindings"]}
            for current, previous, owner in (("m_Geometry", "m_PreviousGeometry", "geometry"),
                                             ("m_DirectionalTemporal", "m_PreviousDirectional", "directional")):
                if (bindings[current] == bindings[previous] or bindings[current] != state["historyIds"][owner + "Current"]
                        or bindings[previous] != state["historyIds"][owner + "Previous"]):
                    identity_errors.append([arm, frame, owner])
            if arm == arms[0]:
                continue
            for tag in functional:
                if entries[arm, frame, tag]["sha256"] != entries[arms[0], frame, tag]["sha256"]:
                    differences.append([arm, frame, tag])
            for tag in ("proposal", "rays", "screen-rays"):
                if entries[arm, frame, tag]["sha256"] != entries[arms[0], frame, tag]["sha256"]:
                    other_differences.append([arm, frame, tag])
    return dict(schema="screen-probe-short-repeatability-analysis.v1", sourceUnchanged=True,
        shortFunctionalComparisons=7 * 64 * len(functional), historyComparisons=verified["historyComparisons"],
        functionalDifferences=differences, additionalRayDifferences=other_differences,
        cpuDifferences=cpu_differences, cpuIdentityErrors=identity_errors,
        qualifiedTemporalDivergences=verified["qualifiedPairs"], firstBundle=capture["firstBundle"],
        savedPayloadBytes=verified["savedPayloadBytes"],
        passed=not (differences or cpu_differences or identity_errors),
        finalQualityVerified=False, performanceComparisonValid=False,
        limitation="Short static repeatability is a prerequisite, not final image quality or long/moving stability proof")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    result = analyze(args.root)
    (args.root / "short-analysis.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(passed=result["passed"], firstBundle=result["firstBundle"],
                         functionalDifferences=len(result["functionalDifferences"]),
                         firstDifferences=result["functionalDifferences"][:8],
                         cpuDifferences=len(result["cpuDifferences"]), additionalRayDifferences=len(result["additionalRayDifferences"]))))
