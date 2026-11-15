"""Audit four-frame PIX controls and the withdrawn read-only geometry trial."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

import numpy as np


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def inspect(root, frames, count):
    capture = load(root / "capture.json")
    assert capture["complete"] and not capture["errors"] and capture["lastFrame"] == frames
    assert load(root / "source-verification.json")["unchanged"]
    rows = [r for r in capture["records"] if "tag" in r]
    entries = {(r["arm"], r["frame"], r["tag"]): r for r in rows}
    assert len(entries) == len(rows)
    completed = [r for r in capture["records"] if r.get("completed")]
    assert len(completed) == count
    assert all(r["targetCount"] == frames and r["sourceFrames"] == 264 and r["otherCount"] == 0 for r in completed)
    arms = [r["arm"] for r in completed]
    selected = ("geometry", "raw", "composite", "directional-temporal", "directional-filtered")
    differences = {arm: [[f, tag] for f in range(frames) for tag in selected
                         if entries[arms[0], f, tag]["sha256"] != entries[arm, f, tag]["sha256"]]
                   for arm in arms[1:]}
    links = (("previous-directional", "directional-temporal"), ("previous-geometry", "geometry"),
             ("previous-directional-filtered", "directional-filtered"))
    history_errors = [[arm, f, previous] for arm in arms for f in range(1, frames) for previous, current in links
                      if entries[arm, f, previous]["sha256"] != entries[arm, f - 1, current]["sha256"]]
    saved = {(r["arm"], r["frame"], r["tag"]): r for r in capture["savedPayloads"]}
    byte_count = 0
    for key, row in saved.items():
        path = Path(row["path"]).resolve()
        assert path.is_relative_to(root.resolve())
        payload = path.read_bytes()
        assert hashlib.sha256(payload).hexdigest() == entries[key]["sha256"]
        byte_count += len(payload)
    assert byte_count == capture["savedPayloadBytes"] <= capture["savedPayloadBudget"]
    assert not history_errors
    summary = dict(complete=True, arms=count, frames=frames, firstBundle=capture["firstBundle"],
                   firstFunctionalDifferences={arm: rows[:5] for arm, rows in differences.items()},
                   historyComparisons=count * (frames - 1) * 3, historyErrors=history_errors,
                   payloadCount=len(saved), payloadBytes=byte_count)
    return capture, entries, summary


def audit_pix(root, natural_root):
    capture, entries, summary = inspect(root, 4, 2)
    assert capture["schema"] == "screen-probe-four-frame-pix.v1"
    terminals = [r for r in capture["pixRecords"] if r.get("stage") == "terminal"]
    assert len(terminals) == 2
    canonical = []
    for row in terminals:
        state, validation, frames = row["state"], row["state"]["validation"], row["cameraFrames"]
        assert state["state"] == "ready" and state["requestedCameraFrames"] == state["submittedCameraFrames"] == 4
        assert validation["success"] and validation["markersOrdered"] and validation["validatedQueueScopes"] == 4
        assert len(frames) == 4 and all(y == x + 1 for x, y in zip(frames, frames[1:]))
        path = Path(state["capturePath"])
        canonical.append(dict(arm=row["arm"], session=state["sessionId"], path=str(path), bytes=path.stat().st_size,
                              sha256=hashlib.sha256(path.read_bytes()).hexdigest(), actualCameraFrames=frames,
                              physicalQueues=validation["queueCount"], gpuWork=validation["gpuWorkEventCount"]))
    assert all(not v for v in summary["firstFunctionalDifferences"].values())
    natural = load(natural_root / "capture.json")
    previous = {(r["arm"], r["frame"], r["tag"]): r for r in natural["records"] if "tag" in r}
    versus_natural = {}
    for frame in range(4):
        tags = sorted(tag for arm, f, tag in entries if arm == "00-r0-t0" and f == frame)
        versus_natural[frame] = [tag for tag in tags if ("00-r0-t0", frame, tag) in previous
                                and entries["00-r0-t0", frame, tag]["sha256"] != previous["00-r0-t0", frame, tag]["sha256"]]
    replay = load(natural_root / "replay/replay.json")
    frozen = next(r for r in replay["records"] if r.get("arm") == "00-r0-t0" and r.get("frame") == 1
                  and r.get("repetition") == 0 and not r["rejectHistory"])
    resources, event = load(root / "temporal-resources.json"), load(root / "temporal-barrier-event.json")
    for query in (resources, event):
        assert query["schemaVersion"] == 3 and query["success"] and query["captureHash"] == canonical[0]["sha256"]
    geometry = next(v for v in resources["data"]["views"]["items"]
                    if v.get("resource", {}).get("name", "").startswith("SdfDiffuseGeometryHistory")
                    and "mipSlice" in v["description"])
    resource_id = geometry["resource"]["resourceId"]
    xml = event["data"]["apiCallData"]
    assert not event["data"]["apiCallDataTruncated"]
    assert f"obj#{resource_id}" in [node.text for node in ET.fromstring(xml).findall(".//UAV/pResource")]
    assert event["queueId"] == resources["queueId"] and event["eventIndex"] + 1 == resources["eventIndex"]
    return dict(schema="screen-probe-four-frame-pix-analysis.v1", capture=summary, canonical=canonical,
                attributionGatePassed=False, reason="Captured functional signals agree; natural failure not reproduced",
                versusNaturalFirstArm=versus_natural,
                capturedFrame1MatchesFrozenNaturalReplay=entries["00-r0-t0", 1, "directional-temporal"]["sha256"] == frozen["sha256"],
                normalBinding=dict(queueId=resources["queueId"], event=resources["eventIndex"],
                                   geometryResource=resource_id, geometryView=geometry, barrierBefore=True),
                limitations=["Static bindings and a barrier from a normal capture are not fault-dispatch evidence",
                             "Capture boundaries alter scheduling; no original failing b0 was obtained"],
                renderingChange=False, qualityBenefitVerified=False, performanceComparisonValid=False)


def audit_srv(root):
    phases, captures = {}, {}
    for name in ("baseline", "candidate", "restored"):
        capture, entries, summary = inspect(root / name, 64, 8)
        phases[name], captures[name] = summary, (capture, entries)
    capture, entries = captures["candidate"]
    bundle = capture["firstBundle"]
    saved = {(r["arm"], r["frame"], r["tag"]): r for r in capture["savedPayloads"]}
    a, b, f = bundle["reference"], bundle["repeated"], bundle["firstFrame"]
    x, y = [np.fromfile(saved[arm, f, "geometry"]["path"], dtype="<f4").reshape(136, 120, 4) for arm in (a, b)]
    geometry_delta = float(np.max(np.abs(y[..., :3] - x[..., :3])))
    assert geometry_delta > 0
    restored = load(root / "restored-import.json")["data"]["result"]["result"]
    variants = {r["name"]: r for r in restored["variants"]}
    temporal = variants["ScreenProbeTemporallyAccumulateTraceRadianceCS"]
    assert temporal["sha256"] == "1527c6fb3bc97ebe3f2d8813a30cd504f732cb503923ce89e998247a217304f8"
    for row in load(root / "original-source.json"):
        assert hashlib.sha256(Path(row["path"]).read_bytes()).hexdigest() == row["sha256"]
    loaded_source = load(root / "loaded-runtime-source-verification.json")
    assert loaded_source["loadedFileMatches"] and loaded_source["pdbMatches"] and loaded_source["sourceMatches"]
    return dict(schema="screen-probe-geometry-srv-analysis.v1", phases=phases,
                candidateGeometryXYZMaximumDifference=geometry_delta,
                candidateInputGatePassed=False, candidateBenefitVerified=False, withdrawn=True,
                reason="Candidate receiver geometry differs before temporal; original variability persists after withdrawal",
                restoredTemporal=temporal, loadedRuntimeSourceVerified=True,
                renderingChange=False, performanceComparisonValid=False)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--natural-reference", type=Path, required=True)
    parser.add_argument("--geometry-srv", type=Path, required=True)
    args = parser.parse_args()
    pix = audit_pix(args.root, args.natural_reference)
    srv = audit_srv(args.geometry_srv)
    (args.root / "analysis.json").write_text(json.dumps(pix, indent=2) + "\n", encoding="utf-8")
    (args.geometry_srv / "analysis.json").write_text(json.dumps(srv, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(pixAttributionGatePassed=pix["attributionGatePassed"], pixPayloadBytes=pix["capture"]["payloadBytes"],
                          srvInputGatePassed=srv["candidateInputGatePassed"], srvWithdrawn=srv["withdrawn"],
                          srvGeometryError=srv["candidateGeometryXYZMaximumDifference"])))
