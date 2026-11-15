"""Fail closed before interpreting an anchor GPU timing capture as an A/B."""
import argparse
import hashlib
import json
from pathlib import Path


MARKERS = (
    "Diffuse GI Total", "Diffuse GI Gather and SDF Fallback", "Diffuse GI Screen Trace",
    "Screen Probe Convert to Irradiance", "Screen Probe Anchor Prefilter",
    "Screen Probe Resolve", "Screen Probe Pixel Temporal",
)
STAGES = ("gbuffer0", "gbuffer1", "depth-copy", "motion", "previous-screen-geometry",
          "hzb-0", "hzb-1", "hzb-2", "hzb-3", "hzb-4",
          "geometry", "proposal", "screen-rays", "first-hit", "ray-diagnostics", "rays",
          "raw", "composite", "directional-temporal", "directional-filtered")


def qualify(capture, protocol, source):
    reasons = []
    if not capture.get("complete") or capture.get("errors"):
        reasons.append("Capture did not complete without errors.")
    if not source.get("unchanged"):
        reasons.append("Source/fixture/script verification is missing or failed.")
    if not capture.get("isPlaying") or capture.get("graphicsApi") != "Direct3D12":
        reasons.append("The qualified PlayMode/D3D12 target is missing.")
    sequence = protocol["preflight"] + protocol["measuredSequence"]
    arms = [f"{i:02d}-{'float512' if age > 128 else 'half128'}" for i, age in enumerate(sequence)]
    if sequence[:2] != [128, 128] or sequence[2:] != [128, 512, 512, 128] * 2:
        reasons.append("Protocol does not contain A/A followed by two ABBA blocks.")
    entries = {}
    completed = {}
    for row in capture.get("records", []):
        if row.get("tag"):
            key = row["arm"], row["frame"], row["tag"]
            if key in entries:
                reasons.append("Duplicate texture observation: " + str(key))
            entries[key] = row
        if row.get("completed"):
            completed[row["arm"]] = row
    for arm in arms:
        row = completed.get(arm, {})
        if row.get("targetCount") != capture["lastFrame"] or row.get("otherCount") != 0:
            reasons.append("Missing complete isolated arm: " + arm)

    def equal(a, b):
        return all(a.get(k) == b.get(k) and a.get(k) is not None
                   for k in ("sha256", "width", "height", "format"))

    mismatches = []
    for (arm, frame, tag), row in entries.items():
        # The reset frame's previous textures can contain intentionally invalid
        # storage. Their bytes are observable, but they are not valid history.
        if arm == arms[1] and not (frame == 0 and tag.startswith("previous-")):
            baseline = entries.get((arms[0], frame, tag))
            if baseline is not None and not equal(baseline, row):
                mismatches.append(dict(frame=frame, tag=tag, baseline=baseline, repeated=row))
    order = {name: i for i, name in enumerate(STAGES)}
    mismatches.sort(key=lambda r: (r["frame"], order.get(r["tag"], len(order)), r["tag"]))
    if mismatches:
        reasons.append("Preflight A/A differs; timing cannot be attributed to the anchor candidate.")
    dense_tags = [tag for tag in STAGES if (arms[0], 0, tag) in entries]
    if not dense_tags:
        reasons.append("Dense preflight observations are missing.")
    for arm in arms[:2]:
        for frame in range(1, capture["warmup"] - 1):
            for tag in dense_tags:
                if (arm, frame, tag) not in entries:
                    reasons.append(f"Incomplete dense preflight: {arm}/{frame}-{tag}")
                    break
            else:
                continue
            break
    for arm in arms[1:]:
        for frame in (capture["warmup"] - 1, capture["lastFrame"] - 1):
            for tag in ("geometry", "raw", "radiosity"):
                a, b = entries.get((arms[0], frame, tag)), entries.get((arm, frame, tag))
                if a is None or b is None or not equal(a, b):
                    reasons.append(f"Missing/different input checkpoint: {arm}/{frame}-{tag}")

    formats = {}
    for row in capture.get("records", []):
        if row.get("resource"):
            formats[row["arm"], row["resource"]] = row["format"]
    for arm, age in zip(arms, sequence):
        expected = "R32G32B32A32_SFloat" if age > 128 else "R16G16B16A16_SFloat"
        for name in ("m_Probes", "m_PreviousProbes", "m_PreparedAnchor", "m_ReconstructedAnchor"):
            if formats.get((arm, name)) != expected:
                reasons.append("Missing/wrong anchor texture format: " + arm + "/" + name)

    timings = {(r["arm"], r["marker"]): r for r in capture.get("timings", [])}
    counts = []
    for arm in arms:
        for marker in MARKERS:
            row = timings.get((arm, marker), {})
            samples = row.get("samples", [])
            first = protocol["censorFirstGpuSamples"]
            last = protocol["censorLastGpuSamples"]
            retained = samples[first:len(samples) - last] if len(samples) > first + last else []
            valid = row.get("valid") and not row.get("wrappedAround") and row.get("count") == len(samples)
            valid = valid and len(retained) >= protocol["minimumRetainedGpuSamples"]
            valid = valid and all(s["nanoseconds"] > 0 and s["count"] == 1 for s in samples)
            counts.append(dict(arm=arm, marker=marker, count=len(samples), retained=len(retained), valid=bool(valid)))
            if not valid:
                reasons.append("Missing/invalid GPU samples: " + arm + "/" + marker)
    return dict(schema="screen-probe-timing-qualification.v1", qualified=not reasons,
                reasons=reasons, firstDifference=mismatches[0] if mismatches else None,
                comparedMismatchCount=len(mismatches), gpuSampleCounts=counts,
                performanceConclusionAvailable=not reasons)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run", type=Path)
    args = parser.parse_args()
    def read(name):
        return json.loads((args.run / name).read_text(encoding="utf-8-sig"))
    result = qualify(read("capture.json"), read("protocol.json"), read("source-verification.json"))
    # Pin the actual analysis inputs; do not silently replace an earlier verdict.
    result["inputs"] = {name: hashlib.sha256((args.run / name).read_bytes()).hexdigest()
                        for name in ("capture.json", "protocol.json", "source-verification.json")}
    target = args.run / "timing-qualification.json"
    with target.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2)
    first = result["firstDifference"]
    print(json.dumps(dict(qualified=result["qualified"], reasons=len(result["reasons"]),
                         firstDifference={k: first[k] for k in ("frame", "tag")} if first else None,
                         report=str(target))))


if __name__ == "__main__":
    main()
