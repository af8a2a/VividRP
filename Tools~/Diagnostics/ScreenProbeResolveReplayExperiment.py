"""Measure interpolation noise while replaying production kernels with frozen SH."""
import argparse
import json
from pathlib import Path

import numpy as np

from LinearGiExport import exr_read
from TemporalSamplingExperiment import LUMA, metrics, read


def analyze(root, linear):
    capture = json.loads((root / "capture.json").read_text(encoding="utf-8"))
    assert capture["complete"] and not capture["errors"] and capture["schema"] == "screen-probe-resolve-replay.v1"
    assert (capture["measuredFrames"], capture["spacing"], capture["rays"]) == (64, 16, 8)
    files = {(r["frame"], r["tag"]): r for r in capture["records"] if "tag" in r}
    ids = exr_read(linear / "vivid-ev4/object_id.exr")[::-1]
    report = dict(scope="Frozen actual SH, production Resolve only; no temporal/spatial denoising or quality/performance acceptance",
                  replay_exact=True, regions={})
    for region, label in [("left", 4), ("right", 5), ("back", 3), ("floor", 1), ("ceiling", 2)]:
        meta = files[0, "original-" + region]
        x, y, w, h = [meta[k] for k in ["x", "y", "width", "height"]]
        mask = ids[y:y+h, x:x+w] == label
        assert mask.sum() > 8
        values = {arm: np.stack([read(files[f, arm + "-" + region]) for f in range(64)])
                  for arm in ["original", *capture["arms"]]}
        assert np.array_equal(values["original"], values["dynamic-jitter"]), "Replay differs from production Resolve: " + region
        y_values = {arm: (v[..., :3] * abs(v[..., 3:4])) @ LUMA for arm, v in values.items()}
        rows = {arm: metrics(v[:, mask]) for arm, v in y_values.items()}
        dynamic, frozen = [y_values[a][:, mask].astype(float) for a in ["dynamic-jitter", "mean-jitter"]]
        residual = dynamic - frozen
        dc, fc, rc = [v-v.mean(0) for v in [dynamic, frozen, residual]]
        variance, frozen_variance, residual_variance = [float(np.mean(v*v)) for v in [dc, fc, rc]]
        covariance = float(np.mean(fc*rc))
        assert np.isclose(variance, frozen_variance+residual_variance+2*covariance, rtol=1e-9, atol=1e-12)
        for name in ["mean-fixed", "last-fixed"]:
            rows[name]["all_frames_exact"] = bool(np.all(values[name] == values[name][:1]))
        fixed = y_values["mean-fixed"][0, mask].astype(float)
        jitter_average = frozen.mean(0)
        rows["jitter_only_decomposition"] = dict(dynamic_variance=variance, frozen_mean_variance=frozen_variance,
            residual_variance=residual_variance, covariance_twice=2*covariance,
            frozen_to_dynamic_variance_percent=100*frozen_variance/variance,
            fixed_minus_jitter_mean_percent=float(100*(fixed-jitter_average).mean()/jitter_average.mean()),
            fixed_minus_jitter_spatial_rmse=float(np.sqrt(np.mean((fixed-jitter_average)**2))))
        if "mean-blue" in y_values:
            filtered = {}
            for arm in ["mean-jitter", "mean-blue"]:
                samples = y_values[arm][:, mask].astype(float)
                sums = np.concatenate([np.zeros_like(samples[:1]), np.cumsum(samples, axis=0)], axis=0)
                filtered[arm] = metrics((sums[32:]-sums[:-32])/32)
            rows["frozen_stbn_comparison"] = dict(
                instantaneous_variance_change_percent=100*(rows["mean-blue"]["temporal_variance"] / rows["mean-jitter"]["temporal_variance"]-1),
                rolling32_variance_change_percent=100*(filtered["mean-blue"]["temporal_variance"] / filtered["mean-jitter"]["temporal_variance"]-1),
                mean_change_percent=100*(rows["mean-blue"]["mean_y"] / rows["mean-jitter"]["mean_y"]-1),
                rolling32=filtered, scope="Frozen input and diagnostic box average only; not the production temporal filter")
        report["regions"][region] = rows
    (root / "analysis.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({r: v.get("frozen_stbn_comparison", v["jitter_only_decomposition"]) | {"mean_fixed_exact": v["mean-fixed"]["all_frames_exact"]}
                      for r, v in report["regions"].items()}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--linear", type=Path, required=True)
    args = parser.parse_args()
    analyze(args.capture, args.linear)
