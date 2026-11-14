"""Assemble completed, source-matched denoise pairs without modifying raw captures."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def merge(base, hybrid):
    base, hybrid = base.resolve(), hybrid.resolve()
    manifests = [read_json(root / "capture/capture.json") for root in [base, hybrid]]
    a, b = manifests
    assert all(m["complete"] and not m["errors"] for m in manifests)
    assert b["freshCameraState"] and b["requestedPaths"] == "hybrid-static"
    for field in ["width", "height", "spacing", "rays", "warmup", "measuredFrames", "captureStride",
                  "frameBase", "camera", "control", "candidate", "pre_exposure", "quantity", "row_origin"]:
        assert a[field] == b[field], field
    assert a["spacing"] == 16 and a["rays"] == 8 and a["control"] == "white" and a["candidate"] == "atrous3"
    fingerprints = [{v["name"]: v["sha256"] for v in m["compiledVariants"]} for m in manifests]
    for manifest in manifests:
        for variant in manifest["compiledVariants"]:
            assert hashlib.sha256(Path(variant["path"]).read_bytes()).hexdigest() == variant["sha256"]
    assert fingerprints[0] == fingerprints[1]
    assert fingerprints[0]["FilterSdfDiffuseAtrousFinal"] == "31fbe5302c2a893310ee72f0bcd4f53ad81578da0ad5b154326f4f829b3d3148"
    snapshots = [read_json(root / "source-before-capture.json") for root in [base, hybrid]]
    hashes = [{f["path"]: f["sha256"] for f in s["files"]} for s in snapshots]
    assert hashes[0] == hashes[1], "Renderer/fixture source differs across runs"
    for root in [base, hybrid]:
        verification = read_json(root / "source-verification.json")
        assert verification["unchanged"] and not verification["changedSources"]
    qualifications = [read_json(root / "capture/qualification.json") for root in [base, hybrid]]
    assert all(q["exact"] and q["filesChecked"] > 0 and not q["mismatched"] for q in qualifications)

    records = []
    for root, manifest in zip([base, hybrid], manifests):
        for record in manifest["records"]:
            is_hybrid = record["arm"].endswith("-hybrid") or record["arm"].startswith("qualification-hybrid-")
            if (root == hybrid) != is_hybrid:
                continue
            records.append(dict(record, sourceCapture=str(root / "capture/capture.json")))
    states = [r for r in records if "state" in r]
    arms = sorted(set(r["arm"] for r in states))
    required = {"white-hybrid", "atrous3-hybrid", "white-sdf", "atrous3-sdf", "white-motion", "atrous3-motion"}
    assert required <= set(arms)
    frames = list(range(a["warmup"], a["warmup"] + a["measuredFrames"]))
    for arm in arms:
        assert [r["frame"] for r in states if r["arm"] == arm] == frames
    keys = [(r["arm"], r["frame"], r["tag"]) for r in records if "tag" in r]
    assert len(keys) == len(set(keys)), "Duplicate payload"
    assert all(Path(r["path"]).is_file() for r in records if "path" in r)

    stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%d-%H%M%S-%f")
    output = base.parent / ("AtrousGuardedCombined-" + stamp)
    (output / "capture").mkdir(parents=True, exist_ok=False)
    manifest = dict(a, records=records, assembled=True, requestedPaths="full",
                    captureSources=[str(root / "capture/capture.json") for root in [base, hybrid]],
                    excludedPairs=[str(base / "capture") + "/white-hybrid,atrous3-hybrid"],
                    pairResetPolicies={"hybrid": "fresh per-camera CPU and GPU history",
                                       "sdf": "64-frame same-mode warmup", "motion": "64-frame same-mode warmup"})
    def write(path, value):
        path.write_text(json.dumps(value, indent=2), encoding="utf-8")
    write(output / "capture/capture.json", manifest)
    write(output / "capture/qualification.json", {
        "exact": True, "filesChecked": sum(q["filesChecked"] for q in qualifications), "mismatched": [],
        "scope": "Independent A/A qualifications in each source run",
        "sourceQualifications": [str(root / "capture/qualification.json") for root in [base, hybrid]]})
    write(output / "source-before-capture.json", snapshots[0])
    write(output / "source-verification.json", {
        "unchanged": True, "changedSources": [], "checkedFiles": len(hashes[0]),
        "scope": "Equal source snapshots and unchanged-source checks in both completed source runs",
        "sourceVerifications": [str(root / "source-verification.json") for root in [base, hybrid]]})
    print(json.dumps({"output": str(output), "arms": arms, "note": "Run the full input, quality and edge analyzers before promotion."}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("base", type=Path)
    parser.add_argument("hybrid", type=Path)
    args = parser.parse_args()
    merge(args.base, args.hybrid)
