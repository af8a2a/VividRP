"""Verify all original witness packets and compare the actual target invocation."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeUnusedBinWitness import decode_chunks, inspect, load


def analyze(root):
    capture = load(root / "witness/capture.json")
    taps = capture["schema"] == "screen-probe-tap-witness.v1"
    c, entries, signals, summary = inspect(root / "witness", 16,
        "screen-probe-tap-witness.v1" if taps else "screen-probe-target-witness.v1",
        compare_cpu=not taps, magic_offset=60 if taps else 32)
    archive = c["rawWitnessArchive"]
    path = Path(archive["path"]).resolve()
    assert path.is_relative_to(root.resolve()) and archive["count"] == 512 and archive["packetBytes"] == 384
    raw = path.read_bytes()
    assert len(raw) == 512 * 384 and hashlib.sha256(raw).hexdigest() == archive["sha256"]
    encoded = np.frombuffer(raw, dtype="<u2").reshape(512, 3, 16, 4)
    witnesses = {}
    for index, row in enumerate(c["witnesses"]):
        decoded = decode_chunks([encoded[index, plane].astype(np.uint32) for plane in range(3)])
        assert decoded == row["words"]
        witnesses[row["arm"], row["frame"]] = np.array(decoded, dtype="<u4").reshape(16, 4)
    diagnostics = []
    for pair in summary["qualifiedPairs"]:
        x, y = [witnesses[pair[arm], pair["frame"]] for arm in ("a", "b")]
        slots = range(16) if taps else range(9, 16)
        rows = [dict(slot=i, equal=bool(np.array_equal(x[i], y[i])), referenceWords=x[i].tolist(), repeatedWords=y[i].tolist(),
                     reference=x[i].view("<f4").tolist(), repeated=y[i].view("<f4").tolist()) for i in slots]
        diagnostics.append(dict(**pair, slots=rows))
    saved = {(r["arm"], r["frame"], r["tag"]): r for r in c["savedPayloads"]}
    bundle = c["firstBundle"]
    a, b, f = bundle["reference"], bundle["repeated"], bundle["firstFrame"]
    load_checks = []
    if taps:
        states = {(r["arm"], r["frame"]): r for r in c["records"] if "constants" in r}
        for arm in (a, b):
            geometry = np.fromfile(saved[arm, f, "previous-geometry"]["path"], dtype="<f4").reshape(136, 120, 4)
            lighting = np.fromfile(saved[arm, f, "previous-directional"]["path"], dtype="<f2").astype("<f4").reshape(408, 1920, 4)
            words = witnesses[arm, f]
            scale = np.array(states[arm, f]["constants"]["historyLightScale"][:3], dtype="<f4")
            for tap in range(4):
                address = words[3 * tap + 2, 2:4]
                if not address.any():  # No address was recorded before a rejected tap.
                    continue
                col, row = map(int, address)
                assert 0 <= col < 1920 and 0 <= row < 136
                assert np.array_equal(words[3 * tap], geometry[row, col // 16].view("<u4"))
                expected = lighting[row, col].copy()
                expected[:3] *= scale
                assert np.array_equal(words[3 * tap + 1], expected.view("<u4"))
                load_checks.append(dict(arm=arm, tap=tap, address=[col, row], geometryMatchesSnapshot=True, lightingMatchesSnapshot=True))
    x, y = [np.fromfile(saved[arm, f, "directional-temporal"]["path"], dtype="<f2").astype(float).reshape(408, 1920, 4)
            for arm in (a, b)]
    delta = np.abs(y - x)
    for plane in range(3):
        delta[68 + plane * 136, :16] = 0
    restored = load(root / "restored-import.json")["data"]["result"]["result"]
    temporal = next(v for v in restored["variants"] if v["name"] == "ScreenProbeTemporallyAccumulateTraceRadianceCS")
    assert temporal["sha256"] == "1527c6fb3bc97ebe3f2d8813a30cd504f732cb503923ce89e998247a217304f8"
    original = load(root / "original-source.json")
    assert hashlib.sha256(Path(original["path"]).read_bytes()).hexdigest() == original["sha256"]
    return dict(schema="screen-probe-target-witness-analysis.v1", taps=taps, summary=summary,
        rawWitnessPacketsVerified=512, rawWitnessArchiveBytes=len(raw), diagnostics=diagnostics,
        actualTapLoadChecks=load_checks,
        savedBundleDifferentTexels=int(np.count_nonzero(np.any(delta != 0, axis=-1))),
        savedBundleMaximumError=float(delta.max()), savedBundleTargetReference=x[47, 74 * 16 + 10].tolist(),
        savedBundleTargetRepeated=y[47, 74 * 16 + 10].tolist(),
        renderingChange=False, qualityBenefitVerified=False, performanceComparisonValid=False, prototypeWithdrawn=True,
        limitations=["One target invocation is recorded; instrumented code and timing differ from production",
                     "Saved full inputs cover the first bundle; other qualified pairs retain exact target packets and whole-resource hashes",
                     "Matched post-pass input hashes alone do not prove all actual original-dispatch loads were equal"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    result = analyze(args.root)
    (args.root / "analysis.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(rawWitnessPacketsVerified=result["rawWitnessPacketsVerified"],
        qualifiedPairs=len(result["summary"]["qualifiedPairs"]), firstSavedBundle=result["summary"]["firstSavedBundle"],
        savedBundleDifferentTexels=result["savedBundleDifferentTexels"], savedBundleMaximumError=result["savedBundleMaximumError"])))
