"""Validate masked GI signals and real temporal constants from an unused probe."""
import argparse
import hashlib
import itertools
import json
from pathlib import Path

import numpy as np


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def masked_hash(payload, height, bins=9):
    copy = bytearray(payload)
    assert len(copy) == 1920 * height * 8
    for plane in range(3 if height == 408 else 1):
        at = (68 + 136 * plane) * 1920 * 8
        copy[at:at + bins * 8] = bytes(bins * 8)
    return hashlib.sha256(copy).hexdigest()


def decode_packet(payload, bins=9):
    half = np.frombuffer(payload, dtype="<u2").reshape(408, 1920, 4)
    chunks = [half[68 + 136 * plane, :bins].astype(np.uint32) for plane in range(3)]
    return decode_chunks(chunks)


def decode_chunks(chunks):
    for plane, chunk in enumerate(chunks):
        assert (chunk >= 0x400).all() and (chunk <= 0x400 + (63 if plane == 2 else 8191)).all()
    return ((chunks[0] - 0x400) | ((chunks[1] - 0x400) << 13) | ((chunks[2] - 0x400) << 26)).ravel().tolist()


def cpu_words(state):
    c = state["constants"]
    floats = c["previousVP"] + c["screenSize"] + c["cameraForward"] + c["historyLightScale"]
    return np.asarray(floats, dtype="<f4").view("<u4").tolist() + [int(state["predicates"]["directionalHistory"]),
            int(c["restirDense"]), int(c["adaptive"]), int(c["flags"])]


def inspect(root, bins=9, schema="screen-probe-unused-bin-witness.v1", compare_cpu=True, magic_offset=32):
    c = load(root / "capture.json")
    assert c["schema"] == schema and c["complete"] and not c["errors"]
    assert c["lastFrame"] == 64 and load(root / "source-verification.json")["unchanged"]
    e = {(r["arm"], r["frame"], r["tag"]): r for r in c["records"] if "tag" in r}
    states = {(r["arm"], r["frame"]): r for r in c["records"] if "constants" in r}
    signals = {(r["arm"], r["frame"], r["tag"]): r["signalSha256"] for r in c["signalRecords"]}
    arms = sorted({k[0] for k in e})
    completed = [r for r in c["records"] if r.get("completed")]
    assert len(arms) == len(completed) == 8 and len(states) == 512 and len(signals) == 2048
    assert all(r["targetCount"] == 64 and r["sourceFrames"] == 264 and r["otherCount"] == 0 for r in completed)
    saved_bytes, packet_rechecks = 0, 0
    witness = {(r["arm"], r["frame"]): r["words"] for r in c["witnesses"]}
    for r in c["savedPayloads"]:
        path = Path(r["path"]).resolve()
        assert path.is_relative_to(root.resolve())
        data = path.read_bytes()
        key = r["arm"], r["frame"], r["tag"]
        assert hashlib.sha256(data).hexdigest() == e[key]["sha256"]
        saved_bytes += len(data)
        if key in signals:
            assert masked_hash(data, e[key]["height"], bins) == signals[key]
        if r["tag"] == "geometry":
            assert np.frombuffer(data, dtype="<f4").reshape(136, 120, 4)[68, 0, 3] < 0
        if r["tag"] == "directional-temporal" and c["expectWitness"]:
            assert decode_packet(data, bins) == witness[r["arm"], r["frame"]]
            packet_rechecks += 1
    assert saved_bytes == c["savedPayloadBytes"] <= c["savedPayloadBudget"]
    assert c["retainedPayloadBytes"] <= c["retainedPayloadBudget"]
    mismatches = []
    for key, actual in witness.items():
        assert actual[magic_offset:magic_offset + 4] == [0x12345678, 0x89ABCDEF, 0x31415926, 0xFACEFEED]
        expected = cpu_words(states[key]) if compare_cpu else []
        bad = [dict(word=i, expected=int(x), actual=int(actual[i])) for i, x in enumerate(expected) if int(x) != actual[i]]
        if bad:
            mismatches.append(dict(arm=key[0], frame=key[1], differences=bad))
    assert len(witness) == (512 if c["expectWitness"] else 0)
    if c["expectWitness"]:
        assert witness.keys() == states.keys() and all(len(words) == bins * 4 for words in witness.values())
    history_errors = []
    for arm in arms:
        for f in range(1, 64):
            for previous, current in (("previous-directional", "directional-temporal"),
                                      ("previous-directional-filtered", "directional-filtered"), ("previous-geometry", "geometry")):
                if e[arm, f, previous]["sha256"] != e[arm, f - 1, current]["sha256"]:
                    history_errors.append([arm, f, previous])
    assert not history_errors
    qualified = []
    for a, b in itertools.combinations(arms, 2):
        for f in range(1, 64):
            output_differs = signals[a, f, "directional-temporal"] != signals[b, f, "directional-temporal"]
            inputs_equal = all(e[a, f, tag]["sha256"] == e[b, f, tag]["sha256"]
                               for tag in ("geometry", "composite", "previous-geometry"))
            inputs_equal &= signals[a, f, "previous-directional"] == signals[b, f, "previous-directional"]
            inputs_equal &= signals[a, f - 1, "directional-temporal"] == signals[b, f - 1, "directional-temporal"]
            inputs_equal &= cpu_words(states[a, f]) == cpu_words(states[b, f])
            if output_differs and inputs_equal:
                constants_equal = witness[a, f][:32] == witness[b, f][:32] if c["expectWitness"] and compare_cpu else None
                qualified.append(dict(a=a, b=b, frame=f, recordedGroupConstantsEqual=constants_equal))
                break
    return c, e, signals, dict(complete=True, witnessCount=len(witness), constantWordsCompared=len(witness) * 32 if compare_cpu else 0,
        constantMismatches=mismatches, rawPacketsIndependentlyDecoded=packet_rechecks,
        historyComparisons=1512, historyErrors=history_errors, savedPayloadBytes=saved_bytes,
        qualifiedPairs=qualified, firstSavedBundle=c["firstBundle"])


def analyze(root):
    baseline, _, _, b = inspect(root / "baseline")
    capture, entries, signals, w = inspect(root / "witness")
    assert not baseline["expectWitness"] and capture["expectWitness"]
    bundle = capture["firstBundle"]
    a, other, f = bundle["reference"], bundle["repeated"], bundle["firstFrame"]
    assert any(row["a"] == a and row["b"] == other and row["frame"] == f for row in w["qualifiedPairs"])
    saved = {(r["arm"], r["frame"], r["tag"]): r for r in capture["savedPayloads"]}
    x, y = [np.fromfile(saved[arm, f, "directional-temporal"]["path"], dtype="<f2").astype(float).reshape(408, 1920, 4)
            for arm in (a, other)]
    delta = np.abs(y - x)
    for plane in range(3):
        delta[68 + plane * 136, :9] = 0
    row, col, channel = map(int, np.unravel_index(np.argmax(delta[:136, :, :3]), delta[:136, :, :3].shape))
    geometry = np.fromfile(saved[a, f, "geometry"]["path"], dtype="<f4").reshape(136, 120, 4)
    assert geometry[row, col // 16, 3] >= 0
    restored = load(root / "restored-import.json")["data"]["result"]["result"]
    temporal = next(v for v in restored["variants"] if v["name"] == "ScreenProbeTemporallyAccumulateTraceRadianceCS")
    assert temporal["sha256"] == "1527c6fb3bc97ebe3f2d8813a30cd504f732cb503923ce89e998247a217304f8"
    original = load(root / "original-source.json")
    assert hashlib.sha256(Path(original["path"]).read_bytes()).hexdigest() == original["sha256"]
    return dict(schema="screen-probe-unused-bin-witness-analysis.v1", baseline=b, witness=w,
        witnessRunReproduced=True, actualRecordedConstantsMatchCpu=not w["constantMismatches"],
        maskedTemporalDifferentTexels=int(np.count_nonzero(np.any(delta != 0, axis=-1))),
        maskedTemporalMaximumError=float(delta.max()),
        nextTarget=dict(tile=[col // 16, row], bin=col % 16, channel=channel, geometry=geometry[row, col // 16].tolist()),
        renderingChange=False, qualityBenefitVerified=False, performanceComparisonValid=False, prototypeWithdrawn=True,
        limitations=["Actual constants were recorded by group (0,68), not every thread group",
                     "Four full saved temporal packets were independently decoded; other witness values remain in verified capture metadata",
                     "Input snapshots occur after the pass; same snapshots do not prove same values at the original failing thread",
                     "Instrumented shader changes code and execution timing despite unchanged resource dimensions/count"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    result = analyze(args.root)
    (args.root / "analysis.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: result[k] for k in ("witnessRunReproduced", "actualRecordedConstantsMatchCpu",
                                           "maskedTemporalDifferentTexels", "maskedTemporalMaximumError", "nextTarget")}))
