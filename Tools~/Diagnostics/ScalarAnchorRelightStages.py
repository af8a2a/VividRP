"""Locate the observed uniform-relight difference before full-resolution Resolve."""
import argparse
import json
from pathlib import Path

import numpy as np

from EmissiveRadiosityConvergence import plane_masks
from TemporalSamplingExperiment import LUMA, read


def analyze(root):
    capture = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    assert capture["complete"] and not capture["errors"]
    files = {(r["arm"], r["frame"], r["tag"]): r for r in capture["records"] if "tag" in r}
    geometry = read(files["age128-constant", 512, "geometry"])
    masks = plane_masks(geometry)
    report = {"scope": "Paired uniform-source relight: consumed raw and temporal scalar anchor, not a physical truth", "arms": {}}
    for age in [32, 128]:
        control = "age%d-constant" % age
        for change, scale in [("up", 4), ("down", .25)]:
            arm = "age%d-%s" % (age, change)
            result = {name: {} for name in masks}
            for tag in ["raw", "anchor"]:
                baseline = []; candidate = []
                for frame in range(576, 896):
                    for which, values in [(control, baseline), (arm, candidate)]:
                        value = read(files[which, frame, tag])[:68, :120]
                        values.append((value[..., :3] * value[..., 3:4]) @ LUMA)
                baseline, candidate = np.asarray(baseline), np.asarray(candidate)
                for name, mask in masks.items():
                    b, v = baseline[:, mask] * scale, candidate[:, mask]
                    errors = 100*(v.mean(1)-b.mean(1))/b.mean(1)
                    result[name][tag] = {"first_mean_error_percent": float(errors[0]),
                        "tail_64_mean_error_percent": float(errors[-64:].mean()),
                        "tail_64_mean_normalized_rms_percent": float(100*np.sqrt(np.mean((v[-64:]-b[-64:])**2))/b[-64:].mean()),
                        "per_frame_mean_error_percent": errors.tolist()}
            report["arms"][arm] = result
    (root / "pre-resolve-relight-analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({a: {n: {s: v["tail_64_mean_error_percent"] for s, v in r.items()} for n, r in planes.items()}
                      for a, planes in report["arms"].items()}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    analyze(parser.parse_args().capture)
