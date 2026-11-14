"""Inspect actual Cornell Mesh SDF origins; not a rendered-noise acceptance test."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


LUMA = np.array([.2126, .7152, .0722])
CANONICAL_SOURCE = "8d1b4e596e3ef067de430082d0d25763b9df83c25b57422acbdd0e33c73d6e62"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def analyze(root):
    m = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    assert m["schema"] == "sdf-ray-origin.v1" and m["complete"] and not m["errors"]
    assert (m["spacing"], m["gatherRays"], m["rays"], m["sourceOrder"], m["sourceBatches"]) == (16, 8, 1024, 8, 32)
    assert m["sourceHash"] == digest(root / "source.bin") == CANONICAL_SOURCE
    source = json.loads((root / "source-verification.json").read_text(encoding="utf-8"))
    assert source["unchanged"] and source["checkedFiles"] > 0
    assert {v["name"] for v in m["variants"]} == {"InspectSdfCardOrigins", "TraceSdfOriginPoints"}
    for v in m["variants"]:
        path = root / ("compiled-" + v["name"] + ".bin")
        assert path.stat().st_size == v["bytes"] and digest(path) == v["sha256"]
    cards = np.fromfile(root / "cards.f32", dtype="<f4").reshape(m["cards"] * 64, 5, 4)
    rays = np.fromfile(root / "rays.f32", dtype="<f4").reshape(m["pointCount"], m["rays"], 4, 4)
    metrics = np.fromfile(root / "point-metrics.f32", dtype="<f4").reshape(m["pointCount"], 4)
    assert len(m["points"]) == m["pointCount"] and np.isfinite(rays).all() and np.isfinite(metrics).all()
    assert len({p["name"] for p in m["points"]}) == m["pointCount"]
    points = {}
    for p, r, metric in zip(m["points"], rays, metrics):
        flags = r[:, 2, 3].astype(int)
        assert np.all((flags & ~31) == 0) and np.all(r[:, 0, :3] >= 0)
        hit = r[:, 0, 3].astype(int)-1
        black_back = ((flags & 1) != 0) & ((flags & 8) != 0) & ((flags & 16) == 0)
        assert np.all(r[black_back, 0, :3] == 0), "A culled material hit returned light"
        ids, counts = np.unique(hit, return_counts=True)
        mean = r[:, 0, :3].astype(float).mean(0)
        points[p["name"]] = dict(position=p["position"], normal=p["normal"], bias=p["bias"],
            meanRgb=mean.tolist(), meanY=float(mean @ LUMA),
            hitCounts={str(i): int(n) for i, n in zip(ids, counts)},
            incompleteRays=int(np.sum((flags & 2) != 0)), receiverAdvancedRays=int(np.sum((flags & 4) != 0)),
            blackMaterialBackHits=int(black_back.sum()),
            emitterSdfDistance=float(metric[0]) if metric[1] == 1 else None,
            originInsideEmitterGrid=bool(metric[1]),
            originInsideEmitterShell=bool(metric[1] == 1 and metric[0] < 0),
            emitterShellThickness=float(metric[2]), emitterSignMode=int(metric[3]))
    selected = np.flatnonzero((cards[:, 0, 3] == m["ceiling"]+1) & (cards[:, 1, 3] == 1)
        & (cards[:, 3, 1] < -.99) & (abs(cards[:, 0, 0]) < .8)
        & (cards[:, 0, 2] > 1.25) & (cards[:, 0, 2] < 2.45))
    assert len(selected) > 0 and np.isfinite(cards[selected]).all()
    comparisons = []
    for index in selected:
        aname, bname = f"card-{index}-actual", f"card-{index}-5mm"
        ai, bi = [next(i for i, p in enumerate(m["points"]) if p["name"] == name) for name in (aname, bname)]
        np.testing.assert_array_equal(rays[ai, :, 1, :3], rays[bi, :, 1, :3])
        np.testing.assert_array_equal(np.array(m["points"][ai]["position"], dtype=np.float32), cards[index, 1, :3])
        comparisons.append(dict(cell=int(index), meshPosition=cards[index, 0, :3].tolist(),
            projectedPosition=cards[index, 1, :3].tolist(), actualOrigin=cards[index, 4, :3].tolist(),
            actual=points[aname], shorter=points[bname],
            pairedMeanDeltaY=points[bname]["meanY"]-points[aname]["meanY"]))
    report = dict(schema="sdf-ray-origin-analysis.v1", complete=True, sourceUnchanged=True,
        sourceSha256=CANONICAL_SOURCE, diagnosticRaysPerPoint=m["rays"],
        actualGatherGrid=16, actualGatherRays=8, inspectedCardCells=int(len(selected)),
        points=points, cards=comparisons,
        scope="Production Mesh SDF/Surface Cache helpers with frozen 8/64/32 input; not the Hybrid screen tracer or final rendered chain",
        limits="One 1024-ray stratified diagnostic per point. No final-pixel noise, motion, GPU timing or engine-equivalence acceptance. Smaller bias remains diagnostic only.")
    (root / "analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    report = analyze(parser.parse_args().root)
    print(json.dumps(dict(sourceUnchanged=report["sourceUnchanged"], cardCells=report["inspectedCardCells"],
        gap4cm=report["points"]["gap-4cm"],
        recoveredCells=sum(c["pairedMeanDeltaY"] > 0 for c in report["cards"])), indent=2))
