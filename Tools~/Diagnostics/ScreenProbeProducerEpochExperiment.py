"""Locate the first native producer prefix divergence, without quality claims."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

import numpy as np

from EmissiveRadiosityConvergence import plane_masks
from ScreenProbeAnchorStorageExperiment import read
from TemporalSamplingExperiment import LUMA


PRIMARY = ("geometry", "proposal", "raw")
SOURCES = ("geometry", "radiosity", "screen-source", "screen-geometry", "screen-motion")


def analyze(root):
    m = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    assert m["schema"] == "screen-probe-producer-epoch.v1" and m["complete"] and not m["errors"]
    dense = m.get("densePrimary", True)
    count = 4608 if dense else 6400
    expected_gpu = list(range(count)) if dense else [0, 4095, *range(4096, 6400, 9), 6399]
    assert (m["spacing"], m["rays"], m["measuredFrames"]) == (16, 8, count)
    source = json.loads((root / "source-verification.json").read_text(encoding="utf-8"))
    assert source["unchanged"] and source["files"] > 0 and source["inputs"] == 2
    rows = {(r["arm"], r["frame"], r["tag"]): r for r in m["records"] if "tag" in r}
    assert len(rows) == sum("tag" in r for r in m["records"])
    a, b = "half128-a", "half128-b"
    cpus = {arm: {r["frame"]: r for r in m["cpuFrames"] if r["arm"] == arm} for arm in (a, b)}
    assert len(cpus[a]) == count and 0 < len(cpus[b]) <= count
    assert sum(map(len, cpus.values())) == len(m["cpuFrames"])
    states = {(r["arm"], r["frame"]): r for r in m["records"] if "state" in r}
    for arm, cpu in cpus.items():
        assert sorted(cpu) == list(range(len(cpu))), "CPU frames have gaps"
        gpu_frames = [f for f in expected_gpu if f < len(cpu)]
        assert sorted(f for rarm, f in states if rarm == arm) == gpu_frames
        for frame in gpu_frames:
            s = states[arm, frame]["state"]
            assert s["anchorMaxAge"] == 128 and s["preFilter"] and s["atrous"] == 4 and not s["restir"]
            assert s["guide"] and s["hierarchicalGuide"] and s["traceMode"] == "Hybrid"
            assert s["sun"] == [0, 0, 0] and s["sky"] == 0 and s["nearOcclusion"][1:3] == [0, 0]
            assert all(s[k] == "R16G16B16A16_SFloat" for k in ("anchorFormat", "previousFormat", "preparedFormat", "reconstructedFormat"))
        for tag in PRIMARY:
            assert sorted(f for rarm, f, rtag in rows if rarm == arm and rtag == tag) == gpu_frames
    paired = [(frame, tag) for arm, frame, tag in rows if arm == b and (a, frame, tag) in rows]
    mismatch = [(f, t) for f, t in paired if any(rows[a, f, t][k] != rows[b, f, t][k]
                for k in ("width", "height", "format", "sha256"))]
    first = {tag: min((f for f, t in mismatch if t == tag), default=None) for tag in {t for f, t in paired}}
    first_primary = min((f for t, f in first.items() if t in PRIMARY and f is not None), default=None)
    assert first_primary == (m["firstPrimaryMismatchFrame"] if m["firstPrimaryMismatchFrame"] >= 0 else None)
    events, cpu_differences = [], []
    for arm, cpu in cpus.items():
        for frame, row in cpu.items():
            s = row["current"]
            if s["m_HasHistory"] != (frame != 0) or s["m_RadiosityPublishedOrder"] != 8 or s["m_RadiosityBatch"] != 32 or s["m_RadiosityUpdateCards"] != 0:
                events.append(dict(arm=arm, frame=frame, reason="History/source state"))
            if row["isFirstFrame"] or row["resetPostProcessingHistory"] or row["configuredFrame"] != 1733+frame:
                events.append(dict(arm=arm, frame=frame, reason="Temporal/sampling state"))
    for frame in cpus[b]:
        x, y = cpus[a][frame], cpus[b][frame]
        if (b, frame) in states and states[a, frame]["vp"] != states[b, frame]["vp"]:
            cpu_differences.append(dict(frame=frame, group="camera", field="vp"))
        for group in ("current", "previous", "scene"):
            for key in sorted(set(x[group]) | set(y[group])):
                if group == "previous" and key == "Frame":
                    continue  # Source preparation render counts can differ; shader frame checked above.
                if x[group].get(key) != y[group].get(key):
                    cpu_differences.append(dict(frame=frame, group=group, field=key, a=x[group].get(key), b=y[group].get(key)))
    source_checks = {}
    for tag in SOURCES:
        values = [r for (_, _, t), r in rows.items() if t == tag]
        source_checks[tag] = dict(reads=len(values), uniqueHashes=len({r["sha256"] for r in values}))
    canonical = Path(m["sourceReference"]) / "expected-radiosity.bin"
    canonical_hash = hashlib.sha256(canonical.read_bytes()).hexdigest()
    assert all(r["sha256"] == canonical_hash for (_, _, tag), r in rows.items() if tag == "radiosity")
    report = dict(schema="screen-probe-producer-epoch-analysis.v1", complete=True,
                  scope="Producer prefix A/A; no noise, energy or performance candidate",
                  cpuFrames={arm: len(cpu) for arm, cpu in cpus.items()}, pairedGpuCheckpoints=len(paired),
                  primaryPairs=sum(f < len(cpus[b]) for f in expected_gpu)*len(PRIMARY),
                  primaryCoverageEveryFrame=dense, firstPrimaryMismatchFrame=first_primary,
                  firstMismatchByTag=first, mismatchCountsByTag=dict(Counter(t for f, t in mismatch)),
                  cpuEvents=events, cpuDifferenceCount=len(cpu_differences), firstCpuDifferences=cpu_differences[:32],
                  sourceChecks=source_checks, sourceSha256=canonical_hash,
                  unpairedContextReads=sum(arm == b and (a, f, t) not in rows for arm, f, t in rows),
                  sourceUnchanged=True,
                  qualifiedFullPrefix=first_primary is None and not mismatch and len(cpus[b]) == count
                    and not events and not cpu_differences
                    and all(s["reads"] >= 2 and s["uniqueHashes"] == 1 for s in source_checks.values()),
                  limits="Primary hashes cover every render only in dense mode. Sparse checkpoints cannot locate an intervening divergence. Passing either capture does not resolve an earlier failure or prove no observer effect. Additional B-only context is not paired.")
    if first_primary is not None:
        report["firstMismatchCpuContext"] = {arm: [cpu[f] for f in (first_primary-1, first_primary) if f in cpu]
                                               for arm, cpu in cpus.items()}
    raw_frame = first.get("raw")
    if raw_frame is not None:
        x, y = rows[a, raw_frame, "raw"], rows[b, raw_frame, "raw"]
        assert x["saved"] and y["saved"], "First raw divergence payload missing"
        va, vb = read(x), read(y)
        geometry_row = rows[a, raw_frame, "geometry"]
        if not geometry_row["saved"]:
            assert geometry_row["sha256"] == rows[a, 0, "geometry"]["sha256"]
            geometry_row = rows[a, 0, "geometry"]
        geometry = read(geometry_row)
        valid = geometry[..., 3] >= 0
        delta = vb-va
        report["firstRawDifference"] = dict(frame=raw_frame, validProbes=int(valid.sum()),
            differentValidProbes=int(np.sum(np.any(delta != 0, -1) & valid)),
            componentRms=float(np.sqrt(np.mean(delta[valid].astype(float)**2))),
            maximumComponent=float(abs(delta[valid]).max()), planes={})
        for name, mask in plane_masks(geometry).items():
            ay = ((va[..., :3]*va[..., 3:4])@LUMA)[:68, :120][mask]
            by = ((vb[..., :3]*vb[..., 3:4])@LUMA)[:68, :120][mask]
            report["firstRawDifference"]["planes"][name] = dict(differentProbes=int(np.sum(ay != by)),
                consumedYRms=float(np.sqrt(np.mean((by-ay)**2))), maximumConsumedY=float(abs(by-ay).max()))
    (root / "analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    r = analyze(parser.parse_args().root)
    print(json.dumps({k: r[k] for k in ("qualifiedFullPrefix", "cpuFrames", "primaryPairs", "firstPrimaryMismatchFrame", "firstMismatchByTag", "cpuDifferenceCount", "cpuEvents", "firstRawDifference") if k in r}, indent=2))
