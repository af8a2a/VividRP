"""Relight response against same-frame constant-source controls; no fitted output gain."""
import argparse
import json
from pathlib import Path

import numpy as np

from LinearGiExport import exr_read
from TemporalSamplingExperiment import LUMA, read


def analyze(root, linear, partial=False):
    m = json.loads((root / ("capture-progress.json" if partial else "capture.json")).read_text())
    assert not m["errors"] and m["warmup"] == 512 and m["measuredFrames"] == 384
    if not partial:
        assert m["complete"] and m["schema"] == "scalar-anchor-relight.v1" and m["changeAt"] == 64
        assert (m["width"], m["height"], m["spacing"], m["rays"]) == (1920, 1080, 16, 8)
    files = {(r["arm"], r["frame"], r["tag"]): r for r in m["records"] if "tag" in r}
    states = [r for r in m["records"] if "state" in r]
    arms = [a for a in dict.fromkeys(r["arm"] for r in states) if all((a, f, "raw") in files for f in range(512, 896))]
    report = {"scope": "Actual pure-emissive Cornell, scalar age 32/128; same-frame physical source scaling",
              "limits": "Final filters can be nonlinear; response is reported by stage and does not imply independent physical truth",
              "complete": not partial, "arms": {}, "pairs": {}}
    labels = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    for arm in arms:
        age = 128 if arm.startswith("age128") else 32
        step = arm.split("-")[1]; level = {"constant": 1, "up": 4, "down": .25, "off": 0}[step]
        rows = [r for r in states if r["arm"] == arm]
        assert [r["frame"] for r in rows] == list(range(512, 896))
        for r in rows:
            s = r["state"]; target = level if r["frame"] >= 576 else 1
            assert s["anchorMaxAge"] == age and s["samples"] == 64 and s["seed"] == 0 and s["guide"] and not s["restir"]
            assert s["atrous"] == 4 and s["sun"] == [0, 0, 0] and s["sky"] == 0 and s["requestedScale"] == target
            actual = np.asarray(s["emission"]); base = np.asarray(rows[0]["state"]["emission"])
            assert np.allclose(base, [200, 200, 200], atol=.002, rtol=0)
            assert np.allclose(actual, base * target, atol=.002, rtol=0), "Physical input did not match requested emission"
        if step == "constant":
            assert all(r["state"]["order"] == 8 and r["state"]["batch"] == 32 for r in rows)
            assert Path(files[arm, 512, "radiosity"]["path"]).read_bytes() == Path(files[arm, 895, "radiosity"]["path"]).read_bytes()
            continue
        control = f"age{age}-constant"; assert control in arms
        assert all(np.array_equal(read(files[control, f, "raw"]), read(files[arm, f, "raw"])) for f in range(512, 576)), "Pre-step raw input differs"
        before = read(files[control, 895, "radiosity"])[..., :3]
        after = read(files[arm, 895, "radiosity"])[..., :3]
        delta = after - before * level
        source = {"normalized_rms_percent": float(100*np.sqrt(np.mean(delta**2)) / max(np.sqrt(np.mean((before*level)**2)), 1e-8)),
                  "max_absolute_delta": float(abs(delta).max()),
                  "mean_rgb_relative_percent": (100*delta.mean((0, 1)) / np.maximum(abs((before*level).mean((0, 1))), 1e-8)).tolist()}
        report["arms"][arm] = {"pre_step_raw_exact": True, "source_scaling": source,
                               "regions": {}, "final_cache_state": rows[-1]["state"]}
        for region, label in [("left", 4), ("right", 5), ("back", 3), ("floor", 1), ("ceiling", 2)]:
            meta = files[control, 512, "final-"+region]; x, y, w, h = (meta[k] for k in ["x", "y", "width", "height"])
            mask = labels[y:y+h, x:x+w] == label; assert mask.sum() > 8
            report["arms"][arm]["regions"][region] = {}
            for stage in ["resolved", "temporal", "final"]:
                errors = []; rms = []; maxima = []
                for f in range(576, 896):
                    c = read(files[control, f, stage+"-"+region]); v = read(files[arm, f, stage+"-"+region])
                    c = (c[..., :3]*abs(c[..., 3:4]))@LUMA; v = (v[..., :3]*abs(v[..., 3:4]))@LUMA
                    target = c*level
                    denominator = float(c[mask].mean()) * (level if level else 1)
                    delta = v[mask] - target[mask]
                    errors.append(float(100*delta.mean()/denominator))
                    rms.append(float(100*np.sqrt(np.mean(delta**2))/denominator)); maxima.append(float(abs(v[mask]).max()))
                errors = np.asarray(errors)
                report["arms"][arm]["regions"][region][stage] = {
                    "first_frame_error_percent": float(errors[0]),
                    "first_8_frames_max_abs_mean_error_percent": float(abs(errors[:8]).max()),
                    "first_32_frames_mean_abs_error_percent": float(abs(errors[:32]).mean()),
                    "positive_overshoot_percent": float(max(0, errors[:32].max())),
                    "negative_undershoot_percent": float(min(0, errors[:32].min())),
                    "tail_64_mean_error_percent": float(errors[-64:].mean()),
                    "first_frame_normalized_rms_percent": rms[0],
                    "off_max_absolute_y": max(maxima) if level == 0 else None,
                    "error_percent_series": errors.tolist()}
    for change in ["up", "down", "off"]:
        a, b = "age32-"+change, "age128-"+change
        if a in report["arms"] and b in report["arms"]:
            report["pairs"][change] = {}
            for region in report["arms"][a]["regions"]:
                va, vb = [report["arms"][k]["regions"][region]["final"] for k in [a, b]]
                report["pairs"][change][region] = {"first_frame_error_percent": [va["first_frame_error_percent"], vb["first_frame_error_percent"]],
                    "first_32_mean_abs_error_percent": [va["first_32_frames_mean_abs_error_percent"], vb["first_32_frames_mean_abs_error_percent"]],
                    "tail_error_percent": [va["tail_64_mean_error_percent"], vb["tail_64_mean_error_percent"]],
                    "off_max_y": [va["off_max_absolute_y"], vb["off_max_absolute_y"]]}
    (root / ("partial-analysis.json" if partial else "analysis.json")).write_text(json.dumps(report, indent=2))
    print(json.dumps({"arms": arms, "pairs": report["pairs"]}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path); parser.add_argument("--linear", type=Path, required=True); parser.add_argument("--partial", action="store_true")
    args = parser.parse_args(); analyze(args.capture, args.linear, args.partial)
