"""Compare progressive radiosity publication, energy and budgeted light-step response."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import source_cards
from ProbeAnchorCalibration import LUMA


def source_y(path):
    return source_cards(path).astype(float)[..., :3] @ LUMA


def analyze(capture, output):
    output.mkdir(parents=True, exist_ok=True)
    records = list(csv.DictReader((capture / "frames.csv").open(encoding="utf-8-sig")))
    reference = {key: source_y(capture / f"legacy-reference-{key}-0000-atlas.f16") for key in ("bright", "dim")}
    report, curves = {"scope": "Cornell 16 px / 8 Gather rays, 64 radiosity rays per batch, 16 sample batches, 8 bounce orders. "
                               "Source reference is the same SDF/card solver at 4096 rays, not physical ground truth.",
                      "arms": {}}, {}
    completed_sources = {}
    for arm in ("legacy-full-step", "new-full-step", "legacy-partial-step", "new-partial-step", "legacy-full-up", "new-full-up"):
        rows = sorted((r for r in records if r["arm"] == arm), key=lambda r: int(r["frame"]))
        count = 320 if "partial" in arm else 256
        assert [int(r["frame"]) for r in rows] == list(range(count))
        assert all(r["rays"] == "64" and r["cards"] == "37" and r["accumulate"] == "True" for r in rows)
        y, source_errors, complete = [], [], {}
        first_fresh, first_target = None, None
        old = source_cards(capture / f"{arm}-0063-atlas.f16")
        for i, row in enumerate(rows):
            lighting = np.fromfile(capture / f"{arm}-{i:04}-gi.f16", "<f2").reshape(48, 96, 4).astype(float)
            assert np.isfinite(lighting).all()
            y.append(float(((lighting[..., :3] * lighting[..., 3:4]) @ LUMA).mean()))
            path = capture / f"{arm}-{i:04}-atlas.f16"
            if not path.exists():
                source_errors.append(None)
                continue
            source = source_cards(path)
            assert np.isfinite(source).all()
            bright = i >= 64 if arm.endswith("-up") else i < 64
            target = reference["bright" if bright else "dim"]
            value = source.astype(float)[..., :3] @ LUMA
            source_errors.append({"rmse_y": float(np.sqrt(np.mean((value - target) ** 2))),
                                  "mean_y_error_percent": float(100 * (value.mean() / target.mean() - 1))})
            if i >= 64 and first_fresh is None and not np.array_equal(source.view("<u2"), old.view("<u2")):
                first_fresh = i - 64
            done = (int(row["updated"]) > 0 and int(row["cursor"]) + int(row["updated"]) == 37
                    and int(row["bounce"]) == 7)
            if i >= 64 and done:
                batch = int(row["batch"]) + 1
                complete[batch] = source
                if first_target is None:
                    first_target = i - 64
        y = np.array(y)
        tail = float(y[-64:].mean())
        delta = 100 * (y[64:] / tail - 1)
        settling = next((i for i in range(len(delta) - 7) if np.all(np.abs(delta[i:i + 8]) <= 10)), None)
        budget = np.array([int(r["updated"]) for r in rows])
        report["arms"][arm] = {"frames": count, "first_fresh_card_frame_after_step": first_fresh,
            "first_complete_target_frame_after_step": first_target, "completed_batches_after_step": len(complete),
            "gi": {"pre_mean_y": float(y[:64].mean()), "tail_mean_y": tail,
                   "first_frame_y": float(y[64]), "first_frame_change_from_tail_percent": float(delta[0]),
                   "first32_min_change_from_tail_percent": float(delta[:32].min()),
                   "first32_max_change_from_tail_percent": float(delta[:32].max()),
                   "first32_mean_absolute_change_percent": float(np.abs(delta[:32]).mean()),
                   "first_8frame_within_10percent_starts_after_step": settling},
            "initial_post_step_budget": budget[64:80].tolist(), "final_source_reference_error": source_errors[-1],
            "published_order_zero_after_step_frames": sum(int(r["publishedOrder"]) == 0 for r in rows[64:])
                if arm.startswith("new") else None,
            "cleared_after_step_frames": sum(r["clear"] == "True" for r in rows[64:])}
        curves[arm] = y, source_errors
        completed_sources[arm] = complete
    for kind in ("full-step", "partial-step", "full-up"):
        old, new = (completed_sources[f"{mode}-{kind}"] for mode in ("legacy", "new"))
        shared = sorted(old.keys() & new.keys())
        assert shared
        for batch in shared:
            assert np.array_equal(old[batch].view("<u2"), new[batch].view("<u2")), f"{kind}: batch {batch} energy differs"
        info = report["arms"][f"new-{kind}"]
        info["matched_batch_source_bitwise_equal"] = shared
        info["tail_gi_mean_change_vs_legacy_percent"] = 100 * (
            info["gi"]["tail_mean_y"] / report["arms"][f"legacy-{kind}"]["gi"]["tail_mean_y"] - 1)
    off_rows = [r for r in records if r["arm"] == "new-off"]
    off_y, off_source = [], []
    for row in off_rows:
        i = int(row["frame"])
        value = np.fromfile(capture / f"new-off-{i:04}-gi.f16", "<f2").reshape(48, 96, 4).astype(float)
        off_y.append(float(((value[..., :3] * value[..., 3:4]) @ LUMA).mean()))
        off_source.append(float(source_y(capture / f"new-off-{i:04}-atlas.f16").mean()))
    off_y, off_source = np.array(off_y), np.array(off_source)
    before = float(off_y[:64].mean())
    threshold = next((i for i in range(len(off_y) - 64 - 7)
                      if np.all(off_y[64 + i:64 + i + 8] <= .05 * before)), None)
    report["off"] = {"frames": len(off_rows), "pre_mean_y": before, "first_frame_y": float(off_y[64]),
        "first_8frame_below_5percent_starts_after_step": threshold, "tail_mean_y": float(off_y[-32:].mean()),
        "source_first_zero_frame_after_step": next((i for i, v in enumerate(off_source[64:]) if v == 0), None),
        "final_source_mean_y": float(off_source[-1]),
        "cleared_after_step_frames": sum(r["clear"] == "True" for r in off_rows[64:])}
    (output / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(2, 3, figsize=(18, 8), constrained_layout=True)
    for col, kind in enumerate(("full-step", "partial-step", "full-up")):
        for mode, color in (("legacy", "#777777"), ("new", "#0072B2")):
            arm = f"{mode}-{kind}"
            value, errors = curves[arm]
            axes[0, col].plot(np.arange(len(value)) - 64, value, color=color, label=mode)
            axes[1, col].plot(np.arange(len(errors)) - 64,
                              [e["rmse_y"] if e else np.nan for e in errors], color=color, label=mode)
        axes[0, col].axvline(0, color="black", linestyle="--", linewidth=.7)
        axes[0, col].set(title=kind + (": 1800 to 3600 lux" if kind.endswith("-up") else ": 3600 to 1800 lux"),
                         xlabel="Frames after step", ylabel="Final ceiling GI Y")
        axes[1, col].set(xlabel="Frames after step", ylabel="Surface Cache Y RMSE vs 4096-ray reference")
        for row in range(2):
            axes[row, col].legend()
    fig.savefig(output / "radiosity-progressive-response.png", dpi=160)
    plt.close(fig)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    analyze(args.capture, args.output)
