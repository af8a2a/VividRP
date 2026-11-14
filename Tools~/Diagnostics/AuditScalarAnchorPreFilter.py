"""Check Cornell promotion evidence without changing assets or hiding quality limits."""
import argparse
import hashlib
import json
from pathlib import Path


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def audit(root, relight):
    capture, analysis, pose = [load(root / n) for n in ["capture.json", "analysis.json", "pose-reference-analysis.json"]]
    light_capture, light = [load(relight / n) for n in ["capture.json", "analysis.json"]]
    assert capture["complete"] and not capture["errors"] and capture["isComposite"]
    assert analysis["complete"] and analysis["static_upstream_exact"] and analysis["motion_upstream_exact"]
    assert analysis["only_constant_sh_coefficients_changed"] and analysis["guard"]["protected_sh_exact"]
    assert not capture["motionQualification"]["mismatch"] and capture["motionQualification"]["files"] > 0
    assert not analysis["aa"]["mismatch"] and analysis["aa"]["files"] > 0
    assert light_capture["complete"] and not light_capture["errors"] and light["complete"]
    assert light_capture["schema"] == "scalar-anchor-prefilter-relight.v1"
    assert set(light["arms"]) == {"age128-" + c for c in ["up", "down", "off"]}
    assert "sourcePreparation" in light_capture, "GPU source preparation was not qualified"
    source_hashes = []
    for step in ["constant", "up", "down", "off"]:
        barriers = [r for r in light_capture["records"] if r.get("sourceBarrier") and r["arm"] == "age128-" + step]
        assert barriers and barriers[-1]["stableFrames"] >= 8 and abs(barriers[-1]["peak"] - 200) < .01
        source_hashes.append(barriers[-1]["sha256"])
    assert len(set(source_hashes)) == 1, "GPU direct-source initial states differ"
    static = {}
    for region, row in analysis["static"].items():
        final = row["final"]
        assert abs(final["mean_change_percent"]) < 1
        assert max(map(abs, final["mean_rgb_change_percent"])) < 1
        assert row["resolved"]["coverage"]["exact"] and final["coverage"]["positive_mask_exact"]
        if region != "floor":
            assert final["block_bootstrap_variance_reduction_95_percent"][0] > 0
        else:
            # This is a tradeoff, not a claim that every temporal metric improved.
            assert final["variance_reduction_percent"] >= -.1
        grid = analysis["grid"][region]["reference"]
        assert grid["candidate"]["per_frame_rmse"] <= grid["baseline-b"]["per_frame_rmse"]
        assert abs(grid["candidate"]["mean_error_percent"]) < 1
        static[region] = dict(varianceReductionPercent=final["variance_reduction_percent"],
                              meanChangePercent=final["mean_change_percent"])
    for contact in analysis["contacts"].values():
        for row in contact["objects"].values():
            assert abs(row["mean_change_percent"]) < 1
            assert max(map(abs, row["mean_rgb_change_percent"])) < 1
    before, after = [analysis["motion"][a] for a in ["baseline-motion", "candidate-motion"]]
    assert after["cycle_rms"] < before["cycle_rms"] and after["cycle_abs_p99"] <= before["cycle_abs_p99"]
    assert analysis["motion_comparison"]["conditional_block8_ci95"][1] < 0
    for row in [before, after]:
        assert row["newly_exposed_pixels"] > 0 and row["newly_exposed_probes"] > 0
        assert row["old_pixel_age"] == row["old_pixel_accepted"] == row["old_probe_age"] == 0
        assert row["temporal_current_max"] == 0
    exposure_before, exposure_after = [pose["pooled_recent_exposure"][a] for a in ["baseline-motion", "candidate-motion"]]
    assert exposure_before["pixels"] == exposure_after["pixels"] > 0
    for key in ["rmse", "absolute_p99", "absolute_max"]:
        assert exposure_after[key] <= exposure_before[key]
    relight_summary = {}
    for arm, row in light["arms"].items():
        assert row["pre_step_raw_exact"] and row["source_scaling"]["normalized_rms_percent"] < .001
        relight_summary[arm] = {}
        for region, stages in row["regions"].items():
            final = stages["final"]
            if arm.endswith("-off"):
                assert all(v["off_max_absolute_y"] == 0 for v in stages.values())
            else:
                assert abs(final["first_frame_error_percent"]) < .1
            relight_summary[arm][region] = {k: final[k] for k in [
                "first_frame_error_percent", "first_32_frames_mean_abs_error_percent",
                "tail_64_mean_error_percent", "off_max_absolute_y"]}
    for run in [root, relight]:
        verified = load(run / "source-verification.json")
        assert verified["unchanged"] and not verified["changedSources"]
    limits = ["Cornell only; no Sponza or Unreal quality acceptance",
              "Floor temporal variance increases slightly; independent grid reference error decreases",
              "Pixel Resolve reference retains the existing roughly 1.2% back-ROI energy deficit",
              "Uniform emission response is tested; arbitrary local lighting changes are not",
              "Finite conditional confidence intervals; no physical truth or noise-free claim",
              "GPU cost and whole-frame allocations have not been measured"]
    manifests = [{"path": str(p), "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
                 for p in [root / "capture.json", root / "analysis.json", root / "pose-reference-analysis.json",
                           relight / "capture.json", relight / "analysis.json"]]
    result = dict(ready=True, scope="Enable the tested prefilter only in the emissive Cornell fixture",
                  static=static, motion=analysis["motion_comparison"], recentlyExposed=pose["pooled_recent_exposure"],
                  relight=relight_summary, manifests=manifests, limits=limits)
    (root / "quality-audit.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("relight", type=Path)
    args = parser.parse_args()
    audit(args.capture, args.relight)
