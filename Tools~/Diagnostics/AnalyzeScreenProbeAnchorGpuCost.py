"""Summarize qualified GPU-only samples from two anchor ABBA blocks."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from ScreenProbeTimingQualification import MARKERS


def analyze(root):
    def load(name):
        return json.loads((root / name).read_text(encoding="utf-8-sig"))
    qualification = load("timing-qualification.json")
    for name, expected in qualification["inputs"].items():
        assert hashlib.sha256((root / name).read_bytes()).hexdigest() == expected
    if not qualification["qualified"]:
        return dict(schema="screen-probe-anchor-gpu-cost.v1", available=False, reasons=qualification["reasons"])
    capture, protocol = load("capture.json"), load("protocol.json")
    ages = protocol["preflight"] + protocol["measuredSequence"]
    arms = [f"{i:02}-{'float512' if age > 128 else 'half128'}" for i, age in enumerate(ages)]
    rows = {(r["arm"], r["marker"]): r for r in capture["timings"]}
    summaries = {}
    for marker in MARKERS:
        values, per_arm = {}, []
        for index in range(2, 10):
            row = rows[arms[index], marker]
            samples = row["samples"]
            first, last = protocol["censorFirstGpuSamples"], protocol["censorLastGpuSamples"]
            kept = samples[first:len(samples) - last]
            assert len(kept) >= protocol["minimumRetainedGpuSamples"] and all(s["count"] == 1 and s["nanoseconds"] > 0 for s in kept)
            ms = np.array([s["nanoseconds"] for s in kept], dtype=np.float64) / 1e6
            values[index] = float(ms.mean())
            per_arm.append(dict(arm=arms[index], age=ages[index], retained=len(ms), meanMs=values[index],
                medianMs=float(np.median(ms)), p05Ms=float(np.quantile(ms, .05)), p95Ms=float(np.quantile(ms, .95))))
        blocks = []
        for first in (2, 6):
            a = .5 * (values[first] + values[first + 3])
            b = .5 * (values[first + 1] + values[first + 2])
            blocks.append(dict(baselineMeanMs=a, candidateMeanMs=b, deltaMs=b - a, deltaPercent=100 * (b / a - 1)))
        a = np.mean([r["baselineMeanMs"] for r in blocks])
        b = np.mean([r["candidateMeanMs"] for r in blocks])
        summaries[marker] = dict(baselineMeanMs=float(a), candidateMeanMs=float(b), deltaMs=float(b - a),
            deltaPercent=float(100 * (b / a - 1)), blocks=blocks, arms=per_arm)
    return dict(schema="screen-probe-anchor-gpu-cost.v1", available=True, markers=summaries,
        limitations=["Only two ABBA blocks; per-frame samples are not independent replications",
                     "Warm-up/preflight excluded; exactly the declared first/last sample censorship, no outlier deletion",
                     "Controlled Unity Editor D3D12 workload, not Player whole-frame performance or GPU-resident memory"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    result = analyze(args.root)
    (args.root / "gpu-cost.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(available=result["available"], total=result.get("markers", {}).get("Diffuse GI Total"))))
