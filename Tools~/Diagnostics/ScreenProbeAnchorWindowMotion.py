"""Qualify long-anchor motion and reveal errors against protected matched-pose resolves."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from scipy.ndimage import binary_dilation

from GatherHistoryMotion import world
from ProbeAnchorCalibration import normal_from_packed
from ScalarAnchorAgeExperiment import motion
from ScalarAnchorPoseReferenceExperiment import error, mean_reference, recent_exposure
from TemporalSamplingExperiment import LUMA, read


def metric(values):
    values = np.asarray(values, np.float64)
    assert values.size and np.isfinite(values).all()
    return dict(samples=int(values.size), rmse=float(np.sqrt(np.mean(values**2))),
        absolute_p99=float(np.quantile(abs(values), .99)), absolute_max=float(abs(values).max()),
        signed_mean=float(values.mean()))


def board_edges(packed, matrix):
    """Geometry-only two-pixel band around front-facing panel depth discontinuities."""
    yy, xx = np.mgrid[350:606, 800:1120]
    depth = (packed & 65535).astype("<u2").view("<f2").astype(float)
    normal = normal_from_packed((packed >> 16).astype(float))
    position = world(np.stack([xx, yy], -1), depth, np.linalg.inv(matrix))
    valid = depth > 0
    edges = np.zeros(depth.shape, bool)
    for axis in (0, 1):
        a = (slice(1, None), slice(None)) if axis == 0 else (slice(None), slice(1, None))
        b = (slice(None, -1), slice(None)) if axis == 0 else (slice(None), slice(None, -1))
        delta = position[a]-position[b]
        plane = np.maximum(abs(np.sum(delta*normal[a], -1)), abs(np.sum(delta*normal[b], -1)))
        broken = valid[a] & valid[b] & (plane > .075) & (np.linalg.norm(delta, axis=-1) > .10)
        edges[a] |= broken; edges[b] |= broken
    return binary_dilation(edges, iterations=2) & valid & (normal[..., 2] < -.95)


def chroma_delta(value, target):
    delta = np.asarray(value, float)-target
    return np.stack([delta[..., 0]-delta[..., 1], delta[..., 2]-delta[..., 1]], -1)


def analyze(root, reference, static):
    m = json.loads((root / "capture.json").read_text())
    assert m["schema"] == "screen-probe-anchor-window-motion.v1" and m["complete"] and not m["errors"]
    assert m["motion"] and m["recenteredBeforeReset"]
    assert (m["spacing"], m["rays"], m["warmup"], m["measuredFrames"], m["sampleStride"]) == (16, 8, 4096, 256, 1)
    source = json.loads((root / "source-verification.json").read_text())
    assert source["unchanged"] and source["files"] > 2000 and source["inputs"] > 4096 and source["scripts"] == 1
    qa = json.loads((root / "qualification.json").read_text())
    assert qa["files"] > 0 and not qa["mismatch"]
    rows = {(r["arm"], r["frame"], r["tag"]): r for r in m["records"] if "tag" in r}
    assert len(rows) == sum("tag" in r for r in m["records"])
    arms = ("half128-a", "half128-b", "float512")
    frames = range(4096, 4352)
    matrices = {(r["arm"], r["frame"]): np.asarray(r["vp"]).reshape(4, 4) for r in m["records"] if "vp" in r}
    canonical = "8d1b4e596e3ef067de430082d0d25763b9df83c25b57422acbdd0e33c73d6e62"
    for row in rows.values():
        if row["saved"]:
            assert hashlib.sha256(Path(row["path"]).read_bytes()).hexdigest() == row["sha256"]
    for arm in arms:
        cpu = [r for r in m["cpuFrames"] if r["arm"] == arm]
        assert [r["frame"] for r in cpu] == list(range(4352))
        assert all(r["hasHistory"] == (r["frame"] != 0) and r["order"] == 8 and r["batch"] == 32
            and r["updateCards"] == 0 and r["age"] == (512 if arm == "float512" else 128)
            and r["fullPrecision"] == (arm == "float512") and not r["isFirstFrame"] and not r["resetPostProcessingHistory"]
            and r["configuredFrame"] == 1733+r["frame"] and r["versions"] == m["cpuFrames"][0]["versions"] for r in cpu)
        states = [r["state"] for r in m["records"] if r.get("arm") == arm and "state" in r]
        assert len(states) == 258
        fmt = "R32G32B32A32_SFloat" if arm == "float512" else "R16G16B16A16_SFloat"
        assert all(s["preFilter"] and s["atrous"] == 4 and not s["restir"] and s["guide"] and s["hierarchicalGuide"]
            and s["sun"] == [0, 0, 0] and s["sky"] == 0 and s["nearOcclusion"][1:3] == [0, 0]
            and all(s[k] == fmt for k in ("anchorFormat", "previousFormat", "preparedFormat", "reconstructedFormat")) for s in states)
        for f in (0, 4096, 4351):
            assert rows[arm, f, "radiosity"]["sha256"] == canonical
        for f in frames:
            assert np.array_equal(matrices[arm, f], matrices["half128-a", f]), "Trajectory differs"
            for tag in ("geometry", "raw", "pixel-geometry-motion"):
                assert rows[arm, f, tag]["sha256"] == rows["half128-a", f, tag]["sha256"], "Motion input differs: " + tag
    # The baseline must begin at the same receiver field as the stationary study.
    prior = json.loads((static / "capture.json").read_text())
    prior_rows = {(r["arm"], r["frame"], r["tag"]): r for r in prior["records"] if "tag" in r}
    for f in (0, 4095, 4096):
        for tag in ("geometry", "raw"):
            assert rows["half128-a", f, tag]["sha256"] == prior_rows["half128-a", f, tag]["sha256"]
    variants = {v["name"]: v for v in m["variants"]}
    previous = {v["name"]: v for v in prior["variants"]}
    assert variants.keys() == previous.keys()
    for name, v in variants.items():
        assert hashlib.sha256(Path(v["path"]).read_bytes()).hexdigest() == v["sha256"] == previous[name]["sha256"]

    report = dict(schema="screen-probe-anchor-window-motion-analysis.v1", qualifiedInputs=True,
        scope="Two fixed camera cycles, consumed E/pi. Same-renderer matched-pose current-only Resolve reference; not physical truth",
        sourceSha256=canonical, aa=qa, motion={}, matchedPoses={}, referenceUncertainty="16 conditional reference blocks; fixed candidate frames")
    cycles = []
    for arm in arms[1:]:
        report["motion"][arm], values = motion(arm, rows, matrices, first_frame=4096)
        cycles.append(values)
    before, after = [np.mean(c*c, axis=(1, 2)).reshape(10, 8).mean(1) for c in cycles]
    draws = np.random.default_rng(20261114).integers(0, 10, (4096, 10))
    report["cycleComparison"] = dict(rmsChangePercent=float(100*(np.sqrt(after.mean()/before.mean())-1)),
        conditionalBlock8Ci95=np.quantile(100*(np.sqrt(after[draws].mean(1)/before[draws].mean(1))-1), [.025, .975]).tolist())

    ref = json.loads((reference / "capture.json").read_text())
    assert ref["complete"] and not ref["errors"] and ref["schema"] == "scalar-anchor-pose-reference-combined.v1"
    rf = {(r["arm"], r["frame"], r["tag"]): r for r in ref["records"] if "tag" in r}
    rm = {(r["arm"], r["frame"]): np.asarray(r["vp"]).reshape(4, 4) for r in ref["records"] if "vp" in r}
    pooled = {a: [] for a in arms[1:]}
    pooled_values = {a: [] for a in pooled}; blocks_y = []
    edge_errors = {a: [] for a in pooled}; edge_rgb = {a: [] for a in pooled}; previews = {}
    for pose in (24, 48, 80, 104):
        name = "pose%03d" % pose
        assert all(np.array_equal(rm[name, f], matrices["half128-b", 4096+pose]) for f in range(512, 1536)), "Reference pose differs"
        states = [r["state"] for r in ref["records"] if r.get("arm") == name and "state" in r]
        assert len(states) == 1024 and all(s["order"] == 8 and s["batch"] == 32 and s["samples"] == 64 and not s["guide"]
            and s["seed"] == 0 and not s["restir"] and s["sun"] == [0, 0, 0] and s["sky"] == 0 for s in states)
        for f in (512, 1535):
            assert hashlib.sha256(Path(rf[name, f, "radiosity"]["path"]).read_bytes()).hexdigest() == canonical
            assert np.array_equal(read(rf[name, f, "geometry"]), read(rows["half128-b", 4096+pose, "geometry"]))
            stats = read(rf[name, f, "statistics"])
            assert np.all(stats[..., 0][stats[..., 0] > 0] == 1), "Reference retained history"
        target, variance, blocks = mean_reference(rf, name, "resolved-motion")
        recent = recent_exposure(rows, matrices, pose, arm="half128-b", first_frame=4096)
        assert recent.sum() > 0
        packed = read(rows["half128-b", 4096+pose, "pixel-geometry-motion"])[..., 0]
        edge = board_edges(packed, matrices["half128-b", 4096+pose])
        assert edge.sum() > 8
        report["matchedPoses"][str(pose)] = dict(recentPixels=int(recent.sum()), boardEdgePixels=int(edge.sum()), arms={})
        blocks_y.append((blocks @ LUMA)[:, recent])
        for arm in pooled:
            value = read(rows[arm, 4096+pose, "final-motion"])
            value = value[..., :3] * abs(value[..., 3:4])
            report["matchedPoses"][str(pose)]["arms"][arm] = error(value, target, variance, recent)
            pooled[arm].extend(((value-target) @ LUMA)[recent])
            pooled_values[arm].append((value @ LUMA)[recent])
            edge_errors[arm].append(np.linalg.norm(chroma_delta(value, target)[edge], axis=-1)/np.sqrt(2))
            edge_rgb[arm].append((value-target)[edge])
            if pose == 48:
                previews[arm] = np.ma.array(np.linalg.norm(chroma_delta(value, target), axis=-1)/np.sqrt(2), mask=~edge)
    report["pooledRecentExposure"] = {a: metric(v) for a, v in pooled.items()}
    report["boardEdgeChroma"] = {a: dict(metric(np.concatenate(v)),
        signedMeanRgbError=np.concatenate(edge_rgb[a]).mean(0, dtype=float).tolist()) for a, v in edge_errors.items()}
    cb, cc = [report["boardEdgeChroma"][a] for a in arms[1:]]
    report["boardEdgeChromaComparison"] = {k+"ChangePercent": 100*(cc[k]/cb[k]-1) for k in ("rmse", "absolute_p99", "absolute_max")}
    report["boardEdgeChromaScope"] = "Geometry-only two-pixel band; RMS of R-G and B-G error against matched-pose reference. Diagnostic without a predeclared chroma promotion threshold."
    weights = np.random.default_rng(20261114).multinomial(16, [1/16]*16, 4096)/16
    targets = weights @ np.concatenate(blocks_y, axis=1)
    errors = [np.concatenate(v)[None, :]-targets for v in pooled_values.values()]
    rr = [np.sqrt(np.mean(v*v, axis=1)) for v in errors]
    rp = [np.quantile(abs(v), .99, axis=1) for v in errors]
    report["referenceConditionalComparison"] = dict(rmsChangeCi95=np.quantile(100*(rr[1]/rr[0]-1), [.025, .975]).tolist(),
        p99ChangeCi95=np.quantile(100*(rp[1]/rp[0]-1), [.025, .975]).tolist())
    gates = json.loads((root / "decision-gates.json").read_text(encoding="utf-8-sig"))
    b, c = [report["motion"][a] for a in arms[1:]]
    pb, pc = [report["pooledRecentExposure"][a] for a in arms[1:]]
    tests = [dict(name="Cycle RMS increase percent", value=report["cycleComparison"]["rmsChangePercent"], limit=gates["maximumCycleRmsIncreasePercent"]),
        dict(name="Cycle P99 increase percent", value=100*(c["cycle_abs_p99"]/b["cycle_abs_p99"]-1), limit=gates["maximumCycleP99IncreasePercent"])]
    for k, limit in (("rmse", "maximumRecentExposureReferenceRmsIncreasePercent"),
        ("absolute_p99", "maximumRecentExposureReferenceP99IncreasePercent"), ("absolute_max", "maximumRecentExposureReferenceMaxIncreasePercent")):
        tests.append(dict(name="Recent exposure " + k + " increase percent", value=100*(pc[k]/pb[k]-1), limit=gates[limit]))
    for k, limit in (("old_pixel_age", "maximumExtraOldPixelHistorySamples"), ("old_pixel_accepted", "maximumExtraOldPixelHistorySamples"),
        ("old_probe_age", "maximumExtraOldProbeHistorySamples")):
        tests.append(dict(name=k + " additional samples", value=c[k]-b[k], limit=gates[limit]))
    for t in tests:
        t["passed"] = bool(np.isfinite(t["value"]) and t["value"] <= t["limit"])
    report["decision"] = dict(motionGatePassed=all(t["passed"] for t in tests), checks=tests,
        nextStep="Only a passing motion gate permits relight and cost validation; no automatic default promotion")
    (root / "analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(1, 2, figsize=(11, 4), layout="constrained")
    vmax = float(np.quantile(previews["half128-b"].compressed(), .99))
    for ax, arm, title in zip(axes, arms[1:], ("F16 / 128", "F32 / 512")):
        chart = ax.imshow(previews[arm][::-1], vmin=0, vmax=vmax, cmap="magma")
        ax.set_title(title); ax.set_xticks([]); ax.set_yticks([])
    fig.colorbar(chart, ax=axes, label="GI chroma error: R-G / B-G RMS (shared scale)")
    fig.suptitle("Cornell motion pose 48: panel-edge GI chroma error vs current-only reference\nGI signal only; not a SceneColor screenshot")
    fig.savefig(root / "board-edge-chroma.png", dpi=150); plt.close(fig)
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--reference", type=Path, default=Path("E:/VividRP_GI_Evidence/ScalarAnchorPoseReferenceCombined-20261114-072002-644163"))
    parser.add_argument("--static", type=Path, default=Path("E:/VividRP_GI_Evidence/ScreenProbeAnchorWindowRevisit-20261114-183607-958"))
    args = parser.parse_args()
    result = analyze(args.root, args.reference, args.static)
    print(json.dumps({k: result[k] for k in ("qualifiedInputs", "cycleComparison", "pooledRecentExposure", "decision")}, indent=2))
