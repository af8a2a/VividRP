"""Locate the first differing producer in a complete, feature-off Cornell A/A run."""
import argparse
from collections import Counter
import json
from pathlib import Path


def analyze(root):
    capture = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    assert capture["schema"] == "screen-probe-producer-history.v1"
    assert capture["complete"] and not capture["errors"]
    assert (capture["spacing"], capture["rays"]) == (16, 8)
    records = capture["records"]
    arms = ["producer-a", "producer-b"]
    images = [{(r["rendered"], r["tag"]): r for r in records
               if r.get("arm") == arm and "sha256" in r} for arm in arms]
    assert images[0].keys() == images[1].keys(), "A/A readback schedules differ"
    states = [{r["rendered"]: r for r in records if r.get("arm") == arm and "state" in r} for arm in arms]
    assert states[0].keys() == states[1].keys(), "A/A render schedules differ"
    for rows in states:
        assert all(not r["state"]["preFilter"] and not r["state"]["restir"] for r in rows.values())
    state_differences = [dict(rendered=i, a=states[0][i], b=states[1][i]) for i in states[0]
                         if any(states[0][i][k] != states[1][i][k] for k in ("frame", "phase", "state"))]
    different = [dict(rendered=i, tag=tag, frame=images[0][i, tag]["frame"],
                      phase=images[0][i, tag]["phase"], a=images[0][i, tag], b=images[1][i, tag])
                 for i, tag in sorted(images[0]) if images[0][i, tag]["sha256"] != images[1][i, tag]["sha256"]]
    # Invalid receiver slots may be uninitialized. A whole-texture mismatch
    # alone cannot distinguish unused slots from valid rays; retain it as
    # unqualified unless both payloads and matching geometry were archived.
    meaningful = [r for r in different if r["tag"] != "ray-data"]
    first_by_tag = {}
    for row in different:
        first_by_tag.setdefault(row["tag"], row)
    result = dict(complete=True, qualityBenchmark=False, prefilterEnabled=False,
                  comparedReadbacks=len(images[0]), comparedRenderedFrames=len(states[0]),
                  bitIdenticalReadbacks=len(images[0]) - len(different),
                  differencesByTag=dict(Counter(r["tag"] for r in different)),
                  recordedProducerStagesExact=not meaningful and not state_differences,
                  rayPayloadQualified=not any(r["tag"] == "ray-data" for r in different),
                  firstMeaningfulDivergence=meaningful[0] if meaningful else None,
                  firstDivergenceByTag=first_by_tag, stateDifferences=state_differences,
                  cacheReady=[r for r in records if "cacheReadyAtRenderedFrame" in r],
                  note="Hash conformance locates divergence; it is not an image quality or performance result. "
                       "Differing ray payloads need both archived arms and geometry masking; "
                       "do not infer that their differences are confined to unused slots.")
    (root / "producer-analysis.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({k: v for k, v in result.items() if k not in ("firstDivergenceByTag", "stateDifferences")}, indent=2))
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    analyze(parser.parse_args().root)
