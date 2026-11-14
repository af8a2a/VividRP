"""Report source-settled producer A/A failures without calling them quality gains."""
import argparse
from collections import Counter
import json
from pathlib import Path


SOURCE_TAGS = ("geometry", "radiosity", "screen-source", "screen-geometry", "screen-motion")
ORDER = ("geometry", "screen-source", "screen-geometry", "screen-motion", "radiosity",
         "previous-directional-filtered", "proposal", "ray-data", "raw", "composite",
         "directional-temporal", "directional-filtered", "anchor", "statistics", "sh")


def analyze(root):
    manifest = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    if manifest["schema"] != "screen-probe-epoch-control.v1":
        raise ValueError("Expected an epoch control capture")
    if manifest["spacing"] != 16 or manifest["rays"] != 8:
        raise ValueError("Expected the fixed 16px / 8-ray workload")
    reads = [r for r in manifest["records"] if "sha256" in r]
    arms = {}
    for arm in ("half128-a", "half128-b"):
        rows = [r for r in reads if r["arm"] == arm]
        arms[arm] = {(r["frame"], r["tag"]): r for r in rows}
        if len(arms[arm]) != len(rows):
            raise ValueError("Duplicate checkpoint in " + arm)
    a, b = arms.values()
    missing = sorted(set(a) ^ set(b))
    mismatches = []
    for frame, tag in sorted(set(a) & set(b)):
        x, y = a[frame, tag], b[frame, tag]
        if any(x[k] != y[k] for k in ("width", "height", "format", "sha256")):
            mismatches.append(dict(frame=frame, tag=tag))
    first = min((r["frame"] for r in mismatches), default=None)
    first_tags = sorted((r["tag"] for r in mismatches if r["frame"] == first),
                        key=lambda t: ORDER.index(t) if t in ORDER else len(ORDER))
    source = {}
    for tag in SOURCE_TAGS:
        rows = [r for r in reads if r["tag"] == tag]
        source[tag] = dict(reads=len(rows), uniqueHashes=len({r["sha256"] for r in rows}))

    cpu = manifest.get("cpuFrames", [])
    cpu_arms = {arm: {r["frame"]: r for r in cpu if r["arm"] == arm} for arm in arms}
    for arm, rows in cpu_arms.items():
        if len(rows) != sum(r["arm"] == arm for r in cpu):
            raise ValueError("Duplicate CPU frame in " + arm)
    cpu_events, cpu_differences = [], []
    for row in cpu:
        state = row["current"]
        reasons = []
        if row["frame"] > 0 and not state["m_HasHistory"]:
            reasons.append("Gather rejected history")
        if row["isFirstFrame"] or row["resetPostProcessingHistory"]:
            reasons.append("Temporal requested reset")
        if state["m_RadiosityPublishedOrder"] != 8 or state["m_RadiosityBatch"] != 32 or state["m_RadiosityUpdateCards"] != 0:
            reasons.append("Source preparing")
        if row["configuredFrame"] != 1733 + row["frame"]:
            reasons.append("Sampling frame differs")
        if reasons:
            cpu_events.append(dict(arm=row["arm"], frame=row["frame"], reasons=reasons))
    ca, cb = cpu_arms.values()
    for frame in sorted(set(ca) & set(cb)):
        x, y = ca[frame], cb[frame]
        for group in ("current", "previous", "scene"):
            for key in sorted(set(x[group]) | set(y[group])):
                # Preparation render counts may differ before the independent epoch.
                # The actual shader sampling frame is checked separately above.
                if group == "previous" and key == "Frame":
                    continue
                if x[group].get(key) != y[group].get(key):
                    cpu_differences.append(dict(frame=frame, group=group, field=key,
                                                a=x[group].get(key), b=y[group].get(key)))
    expected = set(range(manifest["measuredFrames"]))
    frame_coverage = {arm: dict(count=len(rows), missing=len(expected - set(rows)),
                                extra=len(set(rows) - expected)) for arm, rows in cpu_arms.items()}
    first_context = {arm: [rows[f] for f in (first - 1, first) if f in rows]
                     for arm, rows in cpu_arms.items()} if first is not None else {}
    return dict(schema="screen-probe-epoch-control-analysis.v1", complete=manifest["complete"],
                gpuCheckpointPairs=len(set(a) & set(b)), missingCheckpoints=missing,
                mismatches=len(mismatches), mismatchCountsByTag=dict(Counter(r["tag"] for r in mismatches)),
                firstMismatchFrame=first, firstMismatchTags=first_tags, sourceChecks=source,
                cpuFrameCoverage=frame_coverage, cpuEvents=cpu_events, cpuDifferenceCount=len(cpu_differences),
                firstCpuDifferences=cpu_differences[:32], firstMismatchCpuContext=first_context,
                captureErrors=manifest["errors"],
                qualified=bool(a) and manifest["complete"] and not missing and not mismatches and not manifest["errors"]
                and not cpu_events and not cpu_differences
                and (not cpu or all(not v["missing"] and not v["extra"] for v in frame_coverage.values()))
                and all(v["reads"] >= 2 and v["uniqueHashes"] == 1 for v in source.values()),
                limits="Producer A/A diagnostic, not a quality or performance comparison. Sparse GPU checkpoints cannot exclude intervening changes. CPU frame logs are absent in older captures. Ray-only mismatches require a valid-receiver mask check.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    report = analyze(args.root)
    (args.root / "analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({k: report[k] for k in ("complete", "gpuCheckpointPairs", "mismatches",
                     "firstMismatchFrame", "firstMismatchTags", "cpuFrameCoverage", "cpuEvents",
                     "cpuDifferenceCount", "firstCpuDifferences", "qualified")}, indent=2))


if __name__ == "__main__":
    main()
