"""Verify the sparse 1728-frame GI repeatability gate without timing claims."""
import argparse
import hashlib
import json
from pathlib import Path

from AnalyzeScreenProbeUnusedBinWitness import load


def analyze(root):
    root = root.resolve()
    c = load(root / "capture.json")
    assert c["schema"] == "screen-probe-long-repeatability.v1" and c["complete"] and not c["errors"]
    assert c["lastFrame"] == 1728 and load(root / "source-verification.json")["unchanged"]
    completed = [r for r in c["records"] if r.get("completed")]
    assert len(completed) == 2 and all(r["targetCount"] == 1728 and r["sourceFrames"] == 264
        and r["otherCount"] == 0 and not r["recorderMode"] and not r["tickMode"] for r in completed)
    arms = sorted(r["arm"] for r in completed)
    e = {(r["arm"], r["frame"], r["tag"]): r for r in c["records"] if "tag" in r}
    assert len(e) == sum("tag" in r for r in c["records"])
    states = {(r["arm"], r["frame"]): r["cpu"] for r in c["records"] if "cpu" in r}
    assert len(states) == 3456
    frames = list(range(64)) + [1023, 1727]
    functional = ("geometry", "raw", "composite", "directional-temporal", "directional-filtered")
    differences, other_differences, cpu_differences, history_errors = [], [], [], []
    for frame in range(1728):
        x, y = [states[a, frame] for a in arms]
        assert x["frameOverride"] == y["frameOverride"] == 1733 + frame
        for key in ("hasHistory", "hasScreenHistory", "directionalGuide", "hierarchicalGuide", "farUpdate", "jitter", "previousJitter"):
            if x[key] != y[key]:
                cpu_differences.append([frame, key])
    for frame in frames:
        for tag in functional:
            if e[arms[0], frame, tag]["sha256"] != e[arms[1], frame, tag]["sha256"]:
                differences.append([frame, tag])
        for (arm, f, tag), row in e.items():
            if arm != arms[0] or f != frame or tag in functional or (frame == 0 and tag.startswith("previous-")):
                continue
            if row["sha256"] != e[arms[1], f, tag]["sha256"]:
                other_differences.append([frame, tag])
        if 0 < frame < 64:
            for arm in arms:
                for prev, current in (("previous-geometry", "geometry"), ("previous-directional", "directional-temporal"),
                                      ("previous-directional-filtered", "directional-filtered")):
                    if e[arm, frame, prev]["sha256"] != e[arm, frame - 1, current]["sha256"]:
                        history_errors.append([arm, frame, prev])
    saved_bytes = 0
    for row in e.values():
        if not row["saved"]:
            continue
        path = Path(row["path"]).resolve()
        assert path.is_relative_to(root)
        raw = path.read_bytes()
        assert hashlib.sha256(raw).hexdigest() == row["sha256"]
        saved_bytes += len(raw)
    assert saved_bytes == c["savedPayloadBytes"] <= c["savedPayloadBudget"]
    return dict(schema="screen-probe-long-repeatability-analysis.v1", sourceUnchanged=True,
        framesPerArm=1728, sampledFrames=frames, functionalComparisons=len(frames) * len(functional),
        functionalDifferences=differences, otherDifferences=other_differences, cpuDifferences=cpu_differences,
        historyErrors=history_errors, savedPayloadBytes=saved_bytes,
        passed=not (differences or other_differences or cpu_differences or history_errors),
        finalQualityVerified=False, performanceComparisonValid=False,
        limitation="Frames 64..1022 and 1024..1726 have no texture observation; checks do not establish exact onset or final image quality")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    result = analyze(args.root)
    (args.root / "long-analysis.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: result[k] for k in ("passed", "functionalDifferences", "otherDifferences", "cpuDifferences", "savedPayloadBytes")}))
