"""Compare common producer checkpoints with dense versus absent warm-up readbacks."""
import argparse
from collections import Counter
import json
from pathlib import Path

import numpy as np


def payload(record):
    dtype = {"R16G16B16A16_SFloat": "<u2", "R32G32B32A32_SFloat": "<u4"}[record["format"]]
    path = Path(record["path"])
    assert record["saved"] and path.is_file()
    return np.fromfile(path, dtype).reshape(record["height"], record["width"], 4)


def analyze(root):
    capture = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    assert capture["schema"] == "screen-probe-readback-schedule.v1"
    assert capture["complete"] and not capture["errors"]
    assert (capture["spacing"], capture["rays"], capture["measuredFrames"]) == (16, 8, 256)
    records = capture["records"]
    arms = ["producer-a", "producer-b"]
    images = [{(r["rendered"], r["tag"]): r for r in records
               if r.get("arm") == arm and "sha256" in r} for arm in arms]
    common = sorted(images[0].keys() & images[1].keys())
    states = [{r["rendered"]: r for r in records if r.get("arm") == arm and "state" in r} for arm in arms]
    assert states[0].keys() == states[1].keys()
    assert all(not r["state"]["preFilter"] and not r["state"]["restir"] for s in states for r in s.values())
    state_differences = [dict(rendered=i, a=states[0][i], b=states[1][i]) for i in states[0]
                         if any(states[0][i][k] != states[1][i][k] for k in ("frame", "phase", "state"))]
    first = capture["warmup"]
    for arm in images:
        frames = [r for r in arm.values() if r["tag"] == "raw" and r["frame"] >= first]
        assert sorted(r["frame"] for r in frames) == list(range(first, first + 256))
    different = [dict(rendered=i, tag=tag, frame=images[0][i, tag]["frame"],
                      a=images[0][i, tag], b=images[1][i, tag])
                 for i, tag in common if images[0][i, tag]["sha256"] != images[1][i, tag]["sha256"]]
    ray_details = []
    for row in different:
        if row["tag"] != "ray-data":
            continue
        i = row["rendered"]
        ga, gb = [images[j][i, "geometry"] for j in range(2)]
        a, b = payload(row["a"]), payload(row["b"])
        g = payload(ga).view("<f4" if ga["format"] == "R32G32B32A32_SFloat" else "<f2")
        assert ga["sha256"] == gb["sha256"] and np.array_equal(payload(ga), payload(gb))
        mask = g[..., 3] >= 0
        h, w = mask.shape
        assert a.shape == b.shape == (3*h, 8*w, 4)
        d = (a != b).reshape(3, h, w, 8, 4).any((-1, -2))
        ray_details.append(dict(rendered=i, frame=row["frame"],
                                validDifferences=int(d[:, mask].sum()), invalidDifferences=int(d[:, ~mask].sum())))
    meaningful = [r for r in different if r["tag"] != "ray-data"]
    result = dict(complete=True, qualityBenchmark=False, comparedReadbacks=len(common),
                  comparedRenderedFrames=len(states[0]), differencesByTag=dict(Counter(r["tag"] for r in different)),
                  recordedProducerStagesExact=not meaningful and not state_differences,
                  validRayPayloadExact=all(r["validDifferences"] == 0 for r in ray_details), rayDifferences=ray_details,
                  firstDivergence=meaningful[0] if meaningful else None, stateDifferences=state_differences,
                  firstDivergenceByTag={tag: next(r for r in different if r["tag"] == tag) for tag in {r["tag"] for r in different}},
                  note="Warm-up readback schedules intentionally differ. Only common checkpoints are compared; "
                       "no final post-Atrous pixels were captured. This is not a candidate quality benchmark.")
    (root / "schedule-analysis.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({k: v for k, v in result.items() if k not in ("firstDivergenceByTag", "stateDifferences")}, indent=2))
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    analyze(parser.parse_args().root)
