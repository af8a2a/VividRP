"""Qualify original-shader recorder/cadence controls without reporting A/B timing."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from ScreenProbeTimingQualification import STAGES


def analyze(root: Path) -> dict:
    root = root.resolve()
    capture = json.loads((root / "capture.json").read_text(encoding="utf-8-sig"))
    protocol = json.loads((root / "protocol.json").read_text(encoding="utf-8-sig"))
    source = json.loads((root / "source-verification.json").read_text(encoding="utf-8-sig"))
    reasons, observations, summaries, cpu = [], {}, {}, {}
    if capture.get("schema") != "screen-probe-recorder-cadence.v1":
        raise ValueError("Unexpected capture schema")
    if not capture.get("complete") or capture.get("errors"):
        reasons.append("Capture incomplete or reported errors")
    if not source.get("unchanged"):
        reasons.append("Source verification failed")
    if capture.get("graphicsApi") != "Direct3D12" or not capture.get("isPlaying"):
        reasons.append("Expected PlayMode / D3D12")
    for key in ("warmup", "lastFrame"):
        if capture.get(key) != protocol[key]:
            reasons.append("Capture/protocol mismatch: " + key)
    arms = [f"{i:02d}-r{int(v['recorder'])}-t{int(v['tick'])}"
            for i, v in enumerate(protocol["arms"])]
    for row in capture["records"]:
        if "tag" in row:
            key = row["arm"], row["frame"], row["tag"]
            if key in observations:
                reasons.append("Duplicate observation: " + str(key))
            observations[key] = row
        if row.get("completed"):
            summaries[row["arm"]] = row
        if "cpu" in row:
            cpu[row["arm"], row["frame"]] = row["cpu"]

    warmup, final = protocol["warmup"], protocol["lastFrame"] - 1
    # The executed harness reads these only when their diagnostic switches are on.
    # Require complete coverage if either occurs anywhere; total absence is reported.
    optional_counts = {tag: sum(key[2] == tag for key in observations)
                       for tag in ("first-hit", "ray-diagnostics")}
    required_stages = [tag for tag in STAGES if tag not in optional_counts or optional_counts[tag] != 0]
    expected = {(frame, tag) for frame in [*range(warmup), final] for tag in required_stages}
    expected |= {(frame, tag) for frame in [*range(17), warmup - 1, final]
                 for tag in ("previous-directional", "previous-geometry",
                             "previous-directional-filtered", "previous-screen-radiance")}
    expected |= {(frame, "radiosity") for frame in (0, warmup - 1, final)}
    for arm, mode in zip(arms, protocol["arms"]):
        summary = summaries.get(arm, {})
        if (summary.get("targetCount") != final + 1 or summary.get("otherCount") != 0
                or summary.get("sourceFrames") != 264 or summary.get("age") != 128
                or summary.get("recorderMode") != mode["recorder"]
                or summary.get("tickMode") != mode["tick"]):
            reasons.append("Invalid isolated arm: " + arm)
        expected_ticks = protocol["tick"]["expectedRequests"] if mode["tick"] else 0
        if (summary.get("tickIssued") != expected_ticks
                or summary.get("tickCompleted") != expected_ticks or summary.get("tickPending") != 0):
            reasons.append("Invalid pulse completion: " + arm)
        missing = sorted((frame, tag) for frame, tag in expected if (arm, frame, tag) not in observations)
        if missing:
            reasons.append(f"Missing observations in {arm}: {missing[:8]}")
        if sum(a == arm for a, _ in cpu) != final + 1:
            reasons.append("Missing CPU frame records: " + arm)
        recorded = [r for r in capture["timings"] if r["arm"] == arm]
        if len(recorded) != (7 if mode["recorder"] else 0):
            reasons.append("Wrong marker recorder count: " + arm)
        if any(not r.get("valid") or r.get("wrappedAround") or not r.get("count") for r in recorded):
            reasons.append("Invalid marker recorder: " + arm)

    def equal(a, b):
        return a is not None and b is not None and all(a[k] == b[k] for k in ("sha256", "width", "height", "format"))

    def compare(a, b, keys):
        stage_order = {tag: i for i, tag in enumerate(STAGES)}
        ordered = sorted(keys, key=lambda item: (item[0], stage_order.get(item[1], len(STAGES)), item[1]))
        return [{"frame": f, "tag": tag} for f, tag in ordered
                if not equal(observations.get((a, f, tag)), observations.get((b, f, tag)))]

    prefix = {(f, tag) for f, tag in expected if f < warmup
              and not (f == 0 and tag.startswith("previous-"))}
    prefix_differences = {arm: compare(arms[0], arm, prefix) for arm in arms[1:]}
    functional_prefix = {(f, tag) for f, tag in prefix
                         if tag in ("geometry", "raw", "composite", "directional-temporal", "directional-filtered")}
    functional_differences = {arm: compare(arms[0], arm, functional_prefix) for arm in arms[1:]}
    if any(prefix_differences.values()):
        reasons.append("Common dense pre-window prefix differs; no causal factor attribution")
    source_geometry = {(f, tag) for f in (0, warmup - 1, final) for tag in ("geometry", "radiosity")}
    fixed_input_differences = {arm: compare(arms[0], arm, source_geometry) for arm in arms[1:]}
    if any(fixed_input_differences.values()):
        reasons.append("Frozen source/geometry differs")
    cpu_keys = ("frameOverride", "stateFrame", "hasHistory", "hasScreenHistory", "directionalGuide",
                "hierarchicalGuide", "farUpdate", "jitter", "previousJitter")
    cpu_differences = {}
    for arm in arms[1:]:
        differences = []
        for f in range(final + 1):
            a, b = cpu.get((arms[0], f), {}), cpu.get((arm, f), {})
            keys = [key for key in cpu_keys if key not in a or key not in b or a[key] != b[key]]
            if keys:
                differences.append({"frame": f, "keys": keys})
        cpu_differences[arm] = differences
    if any(cpu_differences.values()):
        reasons.append("Controlled CPU epoch state differs")

    saved_bytes, saved_count = 0, 0
    for row in observations.values():
        if not row.get("saved"):
            continue
        path = Path(row["path"]).resolve()
        if not path.is_relative_to(root) or not path.is_file():
            raise ValueError("Saved payload missing or outside run: " + str(path))
        payload = path.read_bytes()
        saved_count += 1
        saved_bytes += len(payload)
        if hashlib.sha256(payload).hexdigest() != row["sha256"]:
            reasons.append("Saved payload hash mismatch: " + str(path))
    if saved_bytes != capture["savedPayloadBytes"] or saved_bytes > protocol["savedPayloadBudget"]:
        reasons.append("Saved payload accounting mismatch")

    def raw_difference(a, b):
        x, y = [observations.get((arm, final, "raw")) for arm in (a, b)]
        if not x or not y or not x.get("saved") or not y.get("saved"):
            return None
        if any(r["format"] != "R16G16B16A16_SFloat" for r in (x, y)):
            return {"unsupportedFormats": [x["format"], y["format"]]}
        arrays = [np.fromfile(r["path"], dtype="<f2").astype(np.float64).reshape(r["height"], r["width"], 4)
                  for r in (x, y)]
        left, right = arrays
        if left.shape != right.shape or not np.isfinite(left).all() or not np.isfinite(right).all():
            reasons.append("Invalid final raw payload: " + a + "/" + b)
            return None
        delta = right - left
        luminance = np.array([0.2126, 0.7152, 0.0722])
        means = [float(np.mean(p[..., :3] @ luminance)) for p in arrays]
        return {"a": a, "b": b, "texels": left.shape[0] * left.shape[1],
                "differentTexels": int(np.count_nonzero(np.any(delta != 0, axis=-1))),
                "maxComponentError": float(np.max(np.abs(delta))),
                "rgbRmse": float(np.sqrt(np.mean(delta[..., :3] ** 2))),
                "wholeRawArrayMeanLuminance": means,
                "note": "Whole raw probe array, no pixel/ceiling ROI; not a final-image quality metric"}

    finals = {(f, tag) for f, tag in expected if f == final}
    pairs = [(arms[0], arms[7]), (arms[1], arms[6]), (arms[2], arms[5]), (arms[3], arms[4])]
    return {
        "schema": "screen-probe-recorder-cadence-analysis.v1",
        "observationGatePassed": not reasons, "gateReasons": reasons,
        "performanceComparisonValid": False, "renderingChange": False,
        "completedArms": summaries, "observedTextureCount": len(observations),
        "commonPrefixPairs": len(prefix) * (len(arms) - 1),
        "prefixDifferences": prefix_differences,
        "prefixDifferenceCounts": {arm: len(rows) for arm, rows in prefix_differences.items()},
        "firstPrefixDifferences": {arm: rows[:8] for arm, rows in prefix_differences.items()},
        "firstFunctionalDifferences": {arm: rows[:8] for arm, rows in functional_differences.items()},
        "optionalDiagnosticObservationCounts": optional_counts,
        "fixedInputDifferences": fixed_input_differences, "cpuDifferences": cpu_differences,
        "finalDifferencesFromFirstArm": {arm: compare(arms[0], arm, finals) for arm in arms[1:]},
        "replicates": [{"a": a, "b": b, "finalDifferences": compare(a, b, finals),
                        "raw": raw_difference(a, b)} for a, b in pairs],
        "rawComparedToFirstArm": [raw_difference(arms[0], arm) for arm in arms[1:]],
        "savedPayloadCount": saved_count, "savedPayloadBytes": saved_bytes,
        "savedPayloadBudget": protocol["savedPayloadBudget"],
        "limitations": ["No observations inside epochs 1024..1726 except optional 1x1 geometry pulses",
                        "Full ray-atlas hashes can contain unused reset storage; no masked first-frame ray payload comparison is available",
                        "GPU profiler enabled in all arms; only marker recorders are a factor",
                        "Readbacks change resource transitions and scheduling; a stable control is not a production fix"]
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    result = analyze(args.root)
    (args.root / "analysis.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: result[k] for k in ("observationGatePassed", "gateReasons", "observedTextureCount",
                                           "commonPrefixPairs", "savedPayloadCount", "savedPayloadBytes")}))
