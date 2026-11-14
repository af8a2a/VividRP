"""Qualify completed reference arms; preserve the failed attempt and payload provenance."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def merge(first, supplement):
    failed = load(first / "capture.json"); accepted = load(first / "capture-progress.json")
    last = load(supplement / "capture.json")
    assert not failed["complete"] and failed["errors"] and not accepted["errors"]
    assert accepted["completedArm"] == "pose080" and not accepted["complete"]
    assert failed["records"][:len(accepted["records"])] == accepted["records"]
    assert not any("state" in r and r["arm"] == "pose104" for r in failed["records"])
    assert all(e == "Render Graph Execution error" or "Source cache changed while measuring" in e for e in failed["errors"])
    assert last["complete"] and not last["errors"] and last["requestedPoses"] == [104] and last["readinessVersion"] == 2
    assert (failed["warmup"], failed["measuredFrames"]) == (last["warmup"], last["measuredFrames"]) == (512, 1024)
    before = [load(p / "source-before-capture.json") for p in [first, supplement]]
    for p in [first, supplement]:
        verified = load(p / "source-verification.json")
        assert verified["unchanged"] and not verified["changedSources"]
    physics = [{r["path"]: r["sha256"] for r in s["files"] if r["path"].startswith(("Runtime/", "Shaders/", "Editor/", "Samples/"))} for s in before]
    assert len(physics[0]) > 2000 and physics[0] == physics[1], "Production source or fixture differs"
    records = accepted["records"] + last["records"]
    expected = ["pose000", "pose024", "pose048", "pose080", "pose104"]
    for arm in expected:
        states = [r for r in records if r.get("arm") == arm and "state" in r]
        payloads = [r for r in records if r.get("arm") == arm and r.get("tag") == "resolved-motion"]
        assert [r["frame"] for r in states] == [r["frame"] for r in payloads] == list(range(512, 1536))
        assert all(Path(r["path"]).is_file() for r in payloads)
    output = first.parent / ("ScalarAnchorPoseReferenceCombined-" + datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%d-%H%M%S-%f"))
    output.mkdir(exist_ok=False)
    provenance = []
    for p, manifest, arms in [(first, first / "capture-progress.json", expected[:4]), (supplement, supplement / "capture.json", expected[4:])]:
        provenance.append(dict(root=str(p), manifest=str(manifest), sha256=hashlib.sha256(manifest.read_bytes()).hexdigest(),
                               original_run_complete=load(p/"capture.json")["complete"], accepted_arms=arms))
    combined = dict(last, schema="scalar-anchor-pose-reference-combined.v1", complete=True, isComposite=True,
        requestedPoses=[0, 24, 48, 80, 104], records=records, errors=[], source_runs=provenance,
        matched_production_files=len(physics[0]), excluded_attempt=dict(manifest=str(first/"capture.json"),
        sha256=hashlib.sha256((first/"capture.json").read_bytes()).hexdigest(), arm="pose104", errors=failed["errors"]))
    (output / "capture.json").write_text(json.dumps(combined, indent=2), encoding="utf-8")
    print(json.dumps(dict(output=str(output), accepted_arms=expected, matched_production_files=len(physics[0]))))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("first", type=Path); parser.add_argument("supplement", type=Path)
    args = parser.parse_args(); merge(args.first, args.supplement)
