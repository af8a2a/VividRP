"""Combine qualified static and recentered-motion pairs without rewriting payloads."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def merge(static, motion):
    base, moved = [load(p / "capture.json") for p in [static, motion]]
    assert base["complete"] and moved["complete"] and not base["errors"] and not moved["errors"]
    assert base["schema"] == "scalar-anchor-prefilter.v1" and base["geometryGuard"]
    assert moved["schema"] == "scalar-anchor-prefilter-motion.v1" and moved["geometryGuard"] and moved["recenteredBeforeReset"]
    for key in ["width", "height", "spacing", "rays", "warmup", "measuredFrames"]:
        assert base[key] == moved[key]
    assert (base["warmup"], base["measuredFrames"], base["spacing"], base["rays"]) == (1024, 256, 16, 8)
    assert {v["name"]: v["sha256"] for v in base["variants"]} == {v["name"]: v["sha256"] for v in moved["variants"]}
    excluded = load(static / "invalid-input-analysis.json")["motion_qualification"]
    assert not excluded["exact"] and not excluded["motion_quality_accepted"]
    snapshots = [load(p / "source-before-capture.json") for p in [static, motion]]
    physics = [{v["path"]: v["sha256"] for v in snap["files"] if v["path"].startswith(("Runtime/", "Shaders/", "Editor/", "Samples/"))} for snap in snapshots]
    assert len(physics[0]) > 2700 and physics[0] == physics[1], "Production source or fixture differs"
    qa = []
    for root in [static, motion]:
        verified = load(root / "source-verification.json")
        assert verified["unchanged"] and not verified["changedSources"]
        qualification = load(root / "qualification.json")
        assert qualification["files"] > 0 and not qualification["mismatch"]
        qa.append(qualification)
    records = [v for v in base["records"] if v["arm"] in ["baseline-a", "baseline-b", "candidate"]]
    records += [v for v in moved["records"] if v["arm"] in ["baseline-motion", "candidate-motion"]]
    for arm in ["baseline-a", "baseline-b", "candidate", "baseline-motion", "candidate-motion"]:
        states = [v for v in records if v["arm"] == arm and "state" in v]
        assert [v["frame"] for v in states] == list(range(1024, 1280))
    payloads = [v for v in records if "tag" in v]
    assert all(Path(v["path"]).is_file() for v in payloads)
    output = static.parent / ("ScalarAnchorPreFilterCombined-" + datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S-%f"))
    output.mkdir(exist_ok=False)
    provenance = [dict(root=str(root), manifest=str(root/"capture.json"),
        sha256=hashlib.sha256((root/"capture.json").read_bytes()).hexdigest(), accepted=role)
        for root, role in [(static, "static pairs only"), (motion, "qualified recentered motion pairs")]]
    combined = dict(base, records=records, isComposite=True, sourceRuns=provenance,
        matchedProductionFiles=len(physics[0]), motionQualification=qa[1],
        excludedMotion=dict(root=str(static), qualification=excluded))
    (output/"capture.json").write_text(json.dumps(combined, indent=2), encoding="utf-8")
    (output/"qualification.json").write_text(json.dumps(qa[0], indent=2), encoding="utf-8")
    entries = [dict(path=key, sha256=value) for key, value in sorted(physics[0].items())]
    (output/"source-before-capture.json").write_text(json.dumps(dict(files=entries, sourceRuns=provenance), indent=2), encoding="utf-8")
    (output/"source-verification.json").write_text(json.dumps(dict(checkedFiles=len(entries), unchanged=True, changedSources=[],
        scope="Matching verified production/fixture snapshots of both source runs"), indent=2), encoding="utf-8")
    print(json.dumps(dict(output=str(output), matchedProductionFiles=len(entries), motionQualification=qa[1])))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("static", type=Path); parser.add_argument("motion", type=Path)
    args = parser.parse_args(); merge(args.static, args.motion)
