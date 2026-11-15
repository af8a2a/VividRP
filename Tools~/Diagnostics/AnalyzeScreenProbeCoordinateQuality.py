"""Compare consumed linear GI, temporal noise and a fixed motion trajectory."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeUnusedBinWitness import load
from GatherHistoryMotion import world
from ProbeAnchorCalibration import LUMA, normal_from_packed
from TemporalSamplingExperiment import metrics, paired, read


def inspect(root):
    root = root.resolve()
    c = load(root / "capture.json")
    assert c["schema"] in ("screen-probe-coordinate-quality.v1", "screen-probe-anchor-quality.v1") and c["complete"] and not c["errors"]
    assert c["warmup"] == 1024 and c["measured"] == 128 and c["lastFrame"] == 1152
    assert load(root / "source-verification.json")["unchanged"]
    completed = [r for r in c["records"] if r.get("completed")]
    assert len(completed) == 4 and all(r["sourceFrames"] == 264 and r["targetCount"] == 1152 and r["otherCount"] == 0 for r in completed)
    e = {(r["arm"], r["frame"], r["tag"]): r for r in c["records"] if "tag" in r}
    assert len(e) == sum("tag" in r for r in c["records"])
    states = {(r["arm"], r["frame"]): r["qualityState"] for r in c["records"] if "qualityState" in r}
    assert len(states) == 512
    anchor = c.get("anchorConfiguration", dict(maxAge=128, fullPrecision=False))
    assert anchor["maxAge"] in (128, 512)
    assert all(s["order"] == 8 and s["batch"] == 32 and s["updates"] == 0 and s["anchorAge"] == anchor["maxAge"]
        and s["atrous"] == 4 and abs(s["bias"] - .04) < 1e-6 for s in states.values())
    if c["schema"] == "screen-probe-anchor-quality.v1":
        expected_format = "R32G32B32A32_SFloat" if anchor["fullPrecision"] else "R16G16B16A16_SFloat"
        expected_flags = 32 if anchor["fullPrecision"] and not anchor.get("settingsPath", False) else 0
        assert all(s["flags"] == expected_flags
            and all(s[key] == expected_format for key in ("anchorFormat", "preparedFormat", "reconstructedFormat")) for s in states.values())
    saved_bytes = 0
    for row in e.values():
        if not row["saved"]:
            continue
        p = Path(row["path"]).resolve()
        assert p.is_relative_to(root)
        raw = p.read_bytes()
        assert hashlib.sha256(raw).hexdigest() == row["sha256"]
        saved_bytes += len(raw)
    assert saved_bytes == c["savedPayloadBytes"] <= c["savedPayloadBudget"]
    assert len({r["sha256"] for r in e.values() if r["tag"] == "radiosity"}) == 1
    return c, e, states


def analyze(root, candidate, baseline=None):
    configs = {"quality-original": inspect(baseline if baseline is not None else root / "quality-original"),
               candidate: inspect(root / candidate)}
    frames = list(range(1024, 1152))
    arms = [f"{i:02}-r0-t0" for i in range(4)]
    series, report = {}, {}
    sources = []
    for name, (c, e, states) in configs.items():
        sources.extend(r["sha256"] for r in e.values() if r["tag"] == "radiosity")
        def stack(arm, tag):
            values = np.stack([read(e[arm, f, tag]) for f in frames])
            return values[..., 0] if tag == "geometry-edge" else values[..., :3] * values[..., 3:4]
        pixels = {(arm, tag): stack(arm, tag) for arm in arms for tag in
                  (("final-ceiling", "final-floor", "final-back") if arm in arms[:2]
                   else ("final-edge", "resolved-edge", "geometry-edge"))}
        series[name] = pixels
        aa = {}
        for a, b in ((arms[0], arms[1]), (arms[2], arms[3])):
            tags = ("geometry", "raw") + (("final-ceiling", "final-floor", "final-back") if a == arms[0]
                                             else ("resolved-edge", "final-edge", "geometry-edge"))
            aa[a] = {tag: sum(e[a, f, tag]["sha256"] != e[b, f, tag]["sha256"] for f in frames) for tag in tags}
        report[name] = dict(savedPayloadBytes=c["savedPayloadBytes"], withinConfigurationDifferentFrames=aa,
            static={tag: [metrics(pixels[a, tag] @ LUMA) for a in arms[:2]] for tag in ("final-ceiling", "final-floor", "final-back")})
    assert len(set(sources)) == 1, "Source differs between configurations"
    base_e, base_states = configs["quality-original"][1:]
    candidate_e, candidate_states = configs[candidate][1:]
    geometry_differences, trajectory_differences = [], []
    for arm in arms:
        for f in frames:
            if base_e[arm, f, "geometry"]["sha256"] != candidate_e[arm, f, "geometry"]["sha256"]:
                geometry_differences.append([arm, f, "probe"])
            for key in ("vp", "pose"):
                if base_states[arm, f][key] != candidate_states[arm, f][key]:
                    trajectory_differences.append([arm, f, key])
            if arm in arms[2:] and base_e[arm, f, "geometry-edge"]["sha256"] != candidate_e[arm, f, "geometry-edge"]["sha256"]:
                geometry_differences.append([arm, f, "pixel"])
    static = {tag: [paired(series["quality-original"][a, tag] @ LUMA, series[candidate][a, tag] @ LUMA)
                    for a in arms[:2]] for tag in ("final-ceiling", "final-floor", "final-back")}
    yy, xx = np.mgrid[414:542, 880:1040]
    pixels = np.stack([xx, yy], -1)
    geometry = series["quality-original"][arms[2], "geometry-edge"]
    depth = (geometry & 65535).astype("<u2").view("<f2").astype(float)
    normals = normal_from_packed((geometry >> 16).astype(float))
    vp = np.array([base_states[arms[2], f]["vp"] for f in frames]).reshape(128, 4, 4)
    inverse = np.linalg.inv(vp)
    exposed = np.zeros(depth.shape, bool)
    for i in range(1, 128):
        pos = world(pixels, depth[i], inverse[i])
        clip = np.concatenate([pos, np.ones((*pos.shape[:-1], 1))], -1) @ vp[i - 1].T
        p = clip[..., :3] / clip[..., 3:4]
        nearest = np.rint(np.stack([(p[..., 0] + 1) * 960 - .5, (1 - p[..., 1]) * 540 - .5], -1)).astype(int)
        valid = ((depth[i] > 0) & (clip[..., 3] > 0) & (nearest[..., 0] >= 880) & (nearest[..., 0] < 1040)
                 & (nearest[..., 1] >= 414) & (nearest[..., 1] < 542))
        ix, iy = np.clip(nearest[..., 0] - 880, 0, 159), np.clip(nearest[..., 1] - 414, 0, 127)
        old_depth = depth[i - 1, iy, ix]
        delta = world(nearest, old_depth, inverse[i - 1]) - world(nearest, p[..., 2], inverse[i - 1])
        plane = np.maximum(abs(np.sum(delta * normals[i], -1)), abs(np.sum(delta * normals[i - 1, iy, ix], -1)))
        exposed[i] = valid & (old_depth > p[..., 2]) & (plane > .075) & (np.linalg.norm(delta, axis=-1) > .10)
    motion = {}
    for name, values in series.items():
        motion[name] = []
        for a in arms[2:]:
            error = abs((values[a, "final-edge"] - values[a, "resolved-edge"]) @ LUMA)[exposed]
            assert error.size
            motion[name].append(dict(samples=int(error.size), frames=int(exposed.any((1, 2)).sum()),
                finalMinusResolveP99=float(np.percentile(error, 99)), finalMinusResolveMax=float(error.max())))
    return dict(schema="screen-probe-coordinate-quality-analysis.v1", configurations=report,
        crossConfigurationGeometryDifferences=geometry_differences, crossConfigurationTrajectoryDifferences=trajectory_differences,
        sourceIdentical=True, staticComparisons=static, motion=motion,
        qualityComparisonQualified=not (geometry_differences or trajectory_differences),
        qualityBenefitVerified=False, performanceComparisonValid=False,
        limitations=["128 measured frames per replicate; block bootstrap is conditional on these samples",
                     "Consumed same-renderer linear GI, not a high-sample or Unreal physical reference",
                     "Final-minus-resolve at disocclusions includes spatial filtering; it is not isolated temporal ghosting",
                     "Qualification covers matched source, geometry and trajectory; it does not accept a quality improvement",
                     "Within-configuration pixel differences are retained as measured dispersion, not discarded"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--candidate", default="quality-precise")
    parser.add_argument("--baseline", type=Path, help="Reuse an existing protected baseline without copying its payloads")
    args = parser.parse_args()
    result = analyze(args.root, args.candidate, args.baseline)
    (args.root / (args.candidate + "-analysis.json")).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(qualified=result["qualityComparisonQualified"], static=result["staticComparisons"], motion=result["motion"])))
