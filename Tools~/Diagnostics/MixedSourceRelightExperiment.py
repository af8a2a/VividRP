"""Compare sky/emissive and independently changing mixed-source relighting."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import source_cards
from ProbeAnchorCalibration import LUMA


def analyze_controls(capture, output):
    rows = list(csv.DictReader((capture / "frames.csv").open(encoding="utf-8-sig")))
    controls = {}
    for arm in dict.fromkeys(row["arm"] for row in rows):
        if arm in ("previous-emission-up", "new-emission-up"):
            continue
        records = [row for row in rows if row["arm"] == arm]
        event = records[16]
        scales = [int(row["frame"]) for row in records if row["rescale"] == "True"]
        bursts = [(int(row["frame"]), int(row["sweeps"])) for row in records if int(row["sweeps"]) > 1]
        valid = "albedo" not in arm
        if "geometry" in arm:
            assert not scales and not bursts and records[0]["transportVersion"] != event["transportVersion"]
        elif "limited" in arm:
            assert not scales and bursts == [(16, 2), (17, 2), (18, 2), (19, 2)]
        elif "rotate" in arm:
            assert not scales and bursts == [(16, 8)]
        elif valid:
            assert scales == [16] and not bursts and event["history"] == "True"
        controls[arm] = {"valid_fixture": valid, "scale_frames": scales, "burst_frames": bursts,
            "transport_before": records[0]["transportVersion"], "transport_after": event["transportVersion"]}
        if not valid:
            controls[arm]["exclusion"] = "Proxy BaseColor did not change source Material Albedo read by SDF; excluded from GPU material-guard evidence."
    assert not (capture / "errors.txt").read_text()
    (output / "control-summary.json").write_text(json.dumps(controls, indent=2), encoding="utf8")
    signals, sources = {}, {}
    for mode in ("previous", "new"):
        images = np.stack([np.fromfile(capture / f"{mode}-emission-up-{i:04}-gi.f16", "<f2").reshape(48, 96, 4)
                           for i in range(1024)]).astype(float)
        assert np.isfinite(images).all()
        signals[mode] = ((images[..., :3] * images[..., 3:4]) @ LUMA).mean((1, 2))
        sources[mode] = source_cards(capture / f"{mode}-emission-up-1023-atlas.f16")
    assert np.array_equal(sources["previous"].view("<u2"), sources["new"].view("<u2"))
    result = {"frames_after_step": 960, "source_bitwise_equal": True, "windows": {}}
    for start, end in ((192, 256), (256, 512), (512, 768), (768, 1024), (960, 1024)):
        old, new = signals["previous"][start:end], signals["new"][start:end]
        result["windows"][f"{start}:{end}"] = {"old_y": float(old.mean()), "new_y": float(new.mean()),
            "mean_difference_percent": float(100 * (new.mean() / old.mean() - 1)),
            "max_frame_mean_difference_percent": float(100 * np.abs(new - old).max() / old.mean())}
    (output / "convergence-summary.json").write_text(json.dumps(result, indent=2), encoding="utf8")


def analyze(capture, output):
    output.mkdir(parents=True, exist_ok=True)
    rows = list(csv.DictReader((capture / "frames.csv").open(encoding="utf-8-sig")))
    report = {"scope": "Cornell fixed camera; 16 px / 8 Gather rays. Final GI ceiling ROI, not path tracing truth.",
              "cases": {}}
    curves = {}
    families = ("sky", "emission", "mixed-sun", "mixed-sky", "mixed-emission", "fixed-mixed-emission")
    for family in families:
        for change in ("up", "down", "off"):
            name = family + "-" + change
            arms, images, sources = {}, {}, {}
            for mode in ("previous", "new"):
                arm = mode + "-" + name
                records = sorted((r for r in rows if r["arm"] == arm), key=lambda r: int(r["frame"]))
                assert [int(r["frame"]) for r in records] == list(range(256)), arm
                assert all(r["cards"] == "37" for r in records), arm
                has_sky = family != "emission"
                assert all((float(r["skyTrace"]) > 0) == has_sky for r in records), arm
                if has_sky:
                    assert all(r["skyBoundValid"] == "True" and float(r["skyBound"]) == 120 for r in records), arm
                assert len({r["transportVersion"] for r in records}) == 1, "Transport changed during " + arm
                values = {}
                for stage in ("resolve", "temporal", "gi"):
                    a = np.stack([np.fromfile(capture / f"{arm}-{i:04}-{stage}.f16", "<f2").reshape(48, 96, 4)
                                  for i in range(256)]).astype(float)
                    assert np.isfinite(a).all(), arm
                    if mode == "new" and family in ("sky", "emission") and change == "off":
                        assert np.count_nonzero(a[64:, ..., :3]) == 0, "Old lighting returned after off: " + arm
                    values[stage] = ((a[..., :3] * a[..., 3:4]) @ LUMA).mean((1, 2))
                source_before = source_cards(capture / f"{arm}-0063-atlas.f16")
                source_first = source_cards(capture / f"{arm}-0064-atlas.f16")
                source_final = source_cards(capture / f"{arm}-0255-atlas.f16")
                assert np.isfinite(source_final).all()
                scale_frames = [int(r["frame"]) for r in records if r["rescale"] == "True"]
                burst_frames = [int(r["frame"]) for r in records if int(r["sweeps"]) > 1]
                if mode == "previous":
                    assert not scale_frames and not burst_frames, arm
                elif family in ("sky", "emission"):
                    assert scale_frames == [64] and not burst_frames, arm
                    scale = np.array([float(records[64]["scale" + c]) for c in "RGB"], dtype=np.float32)
                    expected = (source_before[..., :3].astype(np.float32) * scale).astype("<f2")
                    assert np.array_equal(source_first[..., :3].view("<u2"), expected.view("<u2")), arm
                    assert records[64]["history"] == "True", arm
                else:
                    assert not scale_frames and burst_frames == [64], arm
                    assert int(records[64]["sweeps"]) == 8 and records[64]["publishedOrder"] == "8", arm
                    assert records[64]["history"] == "False", "Mixed source history must reject"
                images[mode] = values
                sources[mode] = source_final
                curves[(family, change, mode)] = values["gi"]
                arms[mode] = {"rescale_frames": scale_frames, "burst_frames": burst_frames,
                    "first_history": records[64]["history"],
                    "stage_first_y": {stage: float(v[64]) for stage, v in values.items()},
                    "source_first_y": float((source_first[..., :3].astype(float) @ LUMA).mean()),
                    "source_final_y": float((source_final[..., :3].astype(float) @ LUMA).mean()),
                    "pre_mean_y": float(values["gi"][:64].mean()),
                    "tail_mean_y": float(values["gi"][-64:].mean())}
            assert np.array_equal(sources["previous"].view("<u2"), sources["new"].view("<u2")), "Final source differs: " + name
            target = arms["previous"]["tail_mean_y"]
            pure_off = family in ("sky", "emission") and change == "off"
            for mode in ("previous", "new"):
                y = images[mode]["gi"]
                if pure_off:
                    delta = 100 * y[64:] / arms[mode]["pre_mean_y"]
                    arms[mode].update({"first_residual_percent_of_pre": float(delta[0]),
                        "tail_residual_percent_of_pre": float(delta[-64:].mean()),
                        "below_1percent_for_8frames_starts": next((i for i in range(len(delta) - 7)
                            if np.all(delta[i:i + 8] <= 1)), None)})
                else:
                    delta = 100 * (y[64:] / target - 1)
                    arms[mode].update({"first_error_percent": float(delta[0]),
                        "first32_min_max_percent": [float(delta[:32].min()), float(delta[:32].max())],
                        "first32_mae_percent": float(np.abs(delta[:32]).mean()),
                        "within_5percent_for_8frames_starts": next((i for i in range(len(delta) - 7)
                            if np.all(np.abs(delta[i:i + 8]) <= 5)), None)})
                    if change == "off":
                        span = arms[mode]["pre_mean_y"] - target
                        arms[mode]["first_removed_source_residual_percent"] = float(100 * (y[64] - target) / span)
            arms["final_source_bitwise_equal"] = True
            arms["tail_mean_change_percent"] = 100 * (arms["new"]["tail_mean_y"] / target - 1) if not pure_off else None
            arms["common_target_y"] = 0.0 if pure_off else target
            report["cases"][name] = arms
    assert not (capture / "errors.txt").read_text()
    (output / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(len(families), 3, figsize=(17, 3 * len(families)), constrained_layout=True)
    for row, family in enumerate(families):
        for col, change in enumerate(("up", "down", "off")):
            for mode, color in (("previous", "#777777"), ("new", "#0072B2")):
                axes[row, col].plot(np.arange(256) - 64, curves[(family, change, mode)], label=mode, color=color)
            axes[row, col].set(title=family + " / " + change, xlabel="Frames after step", ylabel="Linear covered Y")
            axes[row, col].legend()
    fig.savefig(output / "mixed-source-response.png", dpi=140)
    plt.close(fig)
    for name, case in report["cases"].items():
        key = "first_residual_percent_of_pre" if "first_residual_percent_of_pre" in case["new"] else "first_error_percent"
        print(name, "first", round(case["previous"][key], 3), "->", round(case["new"][key], 3),
              "tail delta", case["tail_mean_change_percent"], "source exact")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--controls", type=Path)
    args = parser.parse_args()
    analyze(args.capture, args.output)
    if args.controls:
        analyze_controls(args.controls, args.output)
