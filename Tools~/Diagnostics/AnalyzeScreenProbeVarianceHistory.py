"""Assess coarse-only variance changes with an exact reference-output bound.

This does not reclassify a failed external-reference geometry gate. Identical
final RGBA at every tested reference point proves unchanged energy and error to
any reference at those points, even where that external geometry did not match.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeAtrousExperiment import ARMS, FRAMES, compare_same_frame, inspect
from TemporalSamplingExperiment import read


def reference_output_identity(rows):
    arms = {}
    for arm in ARMS:
        actual_hash, control_hash = hashlib.sha256(), hashlib.sha256()
        count = 0
        for frame in FRAMES:
            a, b = rows[arm, frame, "final-emitter-gap"], rows[arm, frame, "control-emitter-gap"]
            assert all(a[k] == b[k] for k in ("x", "y", "width", "height", "format"))
            x = np.arange(46, 74) * 16 + 8 - a["x"]
            y = 59 * 16 + 8 - a["y"]
            actual = np.ascontiguousarray(read(a).astype("<f2")[y, x]).view("<u2")
            control = np.ascontiguousarray(read(b).astype("<f2")[y, x]).view("<u2")
            count += int(np.count_nonzero(actual != control))
            actual_hash.update(actual.tobytes())
            control_hash.update(control.tobytes())
        arms[arm] = dict(differentHalfWords=count, actualSha256=actual_hash.hexdigest(), controlSha256=control_hash.hexdigest())
    return dict(exact=all(v["differentHalfWords"] == 0 for v in arms.values()), arms=arms,
        points=28, frames=256, rgbaSamples=28 * 256,
        implication="Identical outputs preserve mean energy and error to any same reference at these points",
        limitations=["This proves non-regression at recorded reference points, not absolute correctness or full-image accuracy"])


def assess(root, reference_root):
    protocol = json.loads((root / "protocol.json").read_text(encoding="utf-8"))
    assert protocol["controlMomentLayout"] == "packed-dual-variance", "This gate is specific to the predeclared coarse-only packed-variance design"
    report = compare_same_frame(root, reference_root, matched_reference_subset=True)
    capture, rows, _ = inspect(root)
    identity = reference_output_identity(rows)
    gates, regions = [], {}
    for arm, values in report["arms"].items():
        regions[arm] = {}
        for label, value in values.items():
            gain = 100 * (1 - value["control"]["temporal_variance"] / value["current"]["temporal_variance"])
            energy = 100 * (value["control"]["mean_y"] / value["current"]["mean_y"] - 1)
            bounds = [100 * (1 - 1 / (1 - x / 100)) for x in value["block_bootstrap_variance_reduction_95_percent"][::-1]]
            good = abs(energy) < .3 and value["coverageDifferences"] == 0
            good = good and (gain >= 5 and bounds[0] > 0 if label == "ceiling" else gain >= -2)
            gates.append(good)
            regions[arm][label] = dict(varianceReductionPercent=gain, meanEnergyChangePercent=energy,
                conditionalBlockBootstrap95Percent=bounds, passed=bool(good))
    temporal = report["temporalReplay"]
    source = report["sourceAndReference"]
    replay_ok = not report["replayDifferences"] and temporal["qualified"] and not source["withinConfigurationInputDifferences"]
    return dict(schema="screen-probe-coarse-variance-assessment.v1", capture=str(root),
        candidateQualityQualified=bool(replay_ok and identity["exact"] and all(gates)),
        referenceOutputIdentity=identity, temporalReplay=temporal, regions=regions,
        legacyReferenceQualified=report["qualified"], legacyReferenceGeometryDifferences=source["referenceGeometryDifferences"],
        actualDispatchPrograms=capture["temporalReplayPrograms"], sourceAndReplayReport=report,
        limitations=["The external-reference geometry gate keeps its original failed status when geometry differs",
                     "Adoption uses the separately predeclared exact-output error bound, not a relaxed position tolerance",
                     "Static ROI quality does not establish moving, relighting, Sponza or GPU-cost acceptance"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--reference-root", type=Path, required=True)
    args = parser.parse_args()
    report = assess(args.root.resolve(), args.reference_root.resolve())
    (args.root / "coarse-variance-assessment.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: report[k] for k in ("candidateQualityQualified", "legacyReferenceQualified", "referenceOutputIdentity", "regions")}, indent=2))
