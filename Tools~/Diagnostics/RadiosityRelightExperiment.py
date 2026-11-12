"""Measure sun-only cached relighting without changing Gather grid/rays or final solver energy."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import source_cards
from ProbeAnchorCalibration import LUMA


def analyze_guards(capture, output):
    rows = list(csv.DictReader((capture / "frames.csv").open(encoding="utf-8-sig")))
    report = {}
    for name in dict.fromkeys(row["arm"] for row in rows):
        records = [row for row in rows if row["arm"] == name]
        steps = [int(row["frame"]) for row in records if row["rescale"] == "True"]
        assert steps == ([32] if name in ("eligible", "eligible-colour") else []), (name, steps)
        assert all(row["emissive"] == str(name == "emissive") for row in records), name
        event = records[32] if name != "unconverged" else records[1]
        if name == "geometry":
            assert records[0]["revision"] != event["revision"]
        if name == "direction":
            assert any(records[0]["dir" + channel] != event["dir" + channel] for channel in "XYZ")
        if name == "shadow":
            assert float(event["lightShadow"]) == .5
        if steps:
            before = source_cards(capture / f"{name}-0031-atlas.f16")
            first = source_cards(capture / f"{name}-0032-atlas.f16")
            scale = np.array([float(event["scale" + channel]) for channel in "RGB"])
            predicted = (before[..., :3].astype(float) * scale).astype("<f2")
            assert np.array_equal(first[..., :3].view("<u2"), predicted.view("<u2"))
        report[name] = {"rescale_frames": steps, "emissive": event["emissive"], "history": event["history"],
                        "scale_rgb": [float(event["scale" + channel]) for channel in "RGB"]}
    assert not (capture / "errors.txt").read_text()
    (output / "guard-summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")


def analyze_convergence(capture, output):
    signals, sources = {}, {}
    for mode in ("previous", "new"):
        images = np.stack([np.fromfile(capture / f"{mode}-up-{i:04}-gi.f16", "<f2").reshape(48, 96, 4)
                           for i in range(1024)]).astype(float)
        assert np.isfinite(images).all()
        signals[mode] = ((images[..., :3] * images[..., 3:4]) @ LUMA).mean((1, 2))
        sources[mode] = source_cards(capture / f"{mode}-up-1023-atlas.f16")
    assert np.array_equal(sources["previous"].view("<u2"), sources["new"].view("<u2"))
    report = {"frames_after_step": 960, "final_source_bitwise_equal": True, "windows": {}}
    for start, end in ((192, 256), (256, 512), (512, 768), (768, 1024), (960, 1024)):
        a, b = signals["previous"][start:end], signals["new"][start:end]
        report["windows"][f"{start}:{end}"] = {"previous_mean_y": float(a.mean()), "new_mean_y": float(b.mean()),
            "mean_change_percent": float(100 * (b.mean() / a.mean() - 1)),
            "frame_mean_difference_rmse_percent": float(100 * np.sqrt(np.mean((b - a) ** 2)) / a.mean()),
            "max_frame_mean_difference_percent": float(100 * np.abs(b - a).max() / a.mean())}
    target = signals["previous"][-256:].mean()
    report["first_frame_vs_long_tail_percent"] = {mode: float(100 * (values[64] / target - 1))
                                                 for mode, values in signals.items()}
    (output / "convergence-summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")


def analyze(capture, output):
    output.mkdir(parents=True, exist_ok=True)
    rows = list(csv.DictReader((capture / "frames.csv").open(encoding="utf-8-sig")))
    references = {name: source_cards(capture / f"previous-reference-{name}-0000-atlas.f16").astype(float)
                  for name in ("bright", "dim")}
    report = {"scope": "Cornell fixed camera, 16 px / 8 Gather rays. Current-only Monte Carlo SDF/card references; "
                       "not independent path tracing truth.", "cases": {}}
    curves = {}
    for case, factor, pre, post in (("up", 2, "dim", "bright"), ("down", .5, "bright", "dim"),
                                    ("off", 0, "bright", None), ("fixed-up", 2, "dim", "bright")):
        arms, signals, sources = {}, {}, {}
        modes = ("previous", "source-only", "new") if case == "up" else ("previous", "new")
        for mode in modes:
            arm = mode + "-" + case
            records = sorted((r for r in rows if r["arm"] == arm), key=lambda r: int(r["frame"]))
            assert [int(r["frame"]) for r in records] == list(range(256))
            assert all(r["cards"] == "37" and r["progressive"] == "True" and r["emissive"] == "False"
                       and float(r["skyTrace"]) == 0 for r in records)
            values = {}
            for stage in ("resolve", "temporal", "gi"):
                images = np.stack([np.fromfile(capture / f"{arm}-{i:04}-{stage}.f16", "<f2").reshape(48, 96, 4)
                                   for i in range(256)]).astype(float)
                assert np.isfinite(images).all()
                if case == "off" and mode == "new":
                    assert np.count_nonzero(images[64:, ..., :3]) == 0, "Stale relit pixel history after light off"
                values[stage] = ((images[..., :3] * images[..., 3:4]) @ LUMA).mean((1, 2))
            source_before = source_cards(capture / f"{arm}-0063-atlas.f16")
            source_first = source_cards(capture / f"{arm}-0064-atlas.f16")
            source_final = source_cards(capture / f"{arm}-0255-atlas.f16")
            if case == "off" and mode == "new":
                assert all(np.count_nonzero(source_cards(capture / f"{arm}-{i:04}-atlas.f16")[..., :3]) == 0
                           for i in range(64, 256)), "Stale published source after light off"
            assert np.isfinite(source_first).all() and np.isfinite(source_final).all()
            rescale = [int(r["frame"]) for r in records if r["rescale"] == "True"]
            if mode != "previous":
                assert rescale == [64]
                assert all(float(records[64]["scale" + channel]) == factor for channel in "RGB")
                predicted = (source_before[..., :3].astype(float) * factor).astype("<f2")
                assert np.array_equal(source_first[..., :3].view("<u2"), predicted.view("<u2")), "Wrong first-frame cache scaling"
                assert np.array_equal(source_first[..., 3].copy().view("<u2"), source_before[..., 3].copy().view("<u2"))
            else:
                assert not rescale
            expected = references[post][..., :3] @ LUMA if post else np.zeros(source_final.shape[:-1])
            y = source_final.astype(float)[..., :3] @ LUMA
            source_first_y = source_first.astype(float)[..., :3] @ LUMA
            source_expected_first = (source_before.astype(float)[..., :3] * factor) @ LUMA
            arms[mode] = {"rescale_frames": rescale, "source_first_mean_y": float(source_first_y.mean()),
                "source_first_scaled_expectation_y": float(source_expected_first.mean()),
                "source_first_expectation_error_y": float(np.sqrt(np.mean((source_first_y - source_expected_first) ** 2))),
                "source_final_mean_y": float(y.mean()), "source_reference_rmse_y": float(np.sqrt(np.mean((y - expected) ** 2))),
                "source_reference_mean_error_percent": float(100 * (y.mean() / expected.mean() - 1)) if post else None,
                "stage_first_y": {s: float(v[64]) for s, v in values.items()},
                "stage_tail_mean_y": {s: float(v[-64:].mean()) for s, v in values.items()},
                "cleared_after_step_frames": sum(r["clear"] == "True" for r in records[64:])}
            signals[mode] = values
            sources[mode] = source_final
        for mode in modes:
            assert np.array_equal(sources["previous"].view("<u2"), sources[mode].view("<u2")), "Final energy changed"
        common_target = float(signals["previous"]["gi"][-64:].mean())
        for mode, info in arms.items():
            y = signals[mode]["gi"]
            pre_y = float(y[:64].mean())
            info["pre_mean_y"] = pre_y
            info["tail_mean_y"] = float(y[-64:].mean())
            if post:
                delta = 100 * (y[64:] / common_target - 1)
                info.update({"first_frame_error_percent": float(delta[0]),
                    "first32_error_min_max_percent": [float(delta[:32].min()), float(delta[:32].max())],
                    "first32_mean_absolute_error_percent": float(np.abs(delta[:32]).mean()),
                    "first_8frame_within_5percent_starts": next((i for i in range(len(delta) - 7)
                                                               if np.all(np.abs(delta[i:i + 8]) <= 5)), None)})
            else:
                normalized = y[64:] / pre_y * 100
                info.update({"first_frame_percent_of_pre": float(normalized[0]),
                    "first_8frame_below_5percent_starts": next((i for i in range(len(normalized) - 7)
                                                               if np.all(normalized[i:i + 8] <= 5)), None),
                    "tail_percent_of_pre": float(y[-64:].mean() / pre_y * 100)})
            curves[(case, mode)] = signals[mode]
            info["history_accepted_on_step"] = next(r["history"] == "True" for r in rows
                                                    if r["arm"] == mode + "-" + case and r["frame"] == "64")
        arms["final_source_bitwise_equal"] = True
        arms["common_target_y"] = common_target
        if post:
            arms["tail_mean_change_percent"] = 100 * (arms["new"]["tail_mean_y"] / arms["previous"]["tail_mean_y"] - 1)
        report["cases"][case] = arms
    (output / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(2, 3, figsize=(17, 8), constrained_layout=True)
    for col, case in enumerate(("up", "down", "off")):
        for mode, color in (("previous", "#777777"), ("new", "#0072B2")):
            values = curves[(case, mode)]
            x = np.arange(256) - 64
            axes[0, col].plot(x, values["gi"], label=mode, color=color)
            axes[1, col].plot(x[64:96], values["gi"][64:96], label=mode, color=color)
        axes[0, col].set(title=case + ": final ceiling GI", xlabel="Frames after step", ylabel="Linear covered Y")
        axes[1, col].set(title="First 32 frames", xlabel="Frames after step", ylabel="Linear covered Y")
        for row in range(2):
            axes[row, col].legend()
    fig.savefig(output / "radiosity-relight-response.png", dpi=160)
    plt.close(fig)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--guards", type=Path)
    parser.add_argument("--convergence", type=Path)
    args = parser.parse_args()
    analyze(args.capture, args.output)
    if args.guards:
        analyze_guards(args.guards, args.output)
    if args.convergence:
        analyze_convergence(args.convergence, args.output)
