"""Analyze independent bounce-order/sample-batch GPU captures and lighting steps."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np

from ProbeAnchorCalibration import LUMA
from RadiosityPatternExperiment import owned_cards


def atlas(root, arm, frame):
    return owned_cards(np.fromfile(root / f"{arm}-{frame:04}-atlas.f16", "<f2"))


def compare(value, reference):
    mask = reference[..., 3] > 0
    assert np.array_equal(value[..., 3] > 0, mask), "Coverage changed"
    a, b = value[..., :3].astype(float) @ LUMA, reference[..., :3].astype(float) @ LUMA
    delta = (a - b)[mask]
    return {"rmse_y": float(np.sqrt(np.mean(delta ** 2))), "p95_absolute_y": float(np.percentile(np.abs(delta), 95)),
            "mean_y": float(a[mask].mean()), "reference_mean_y": float(b[mask].mean()),
            "energy_error_percent": float(100 * (a[mask].mean() / b[mask].mean() - 1))}


def analyze(root, output):
    output.mkdir(parents=True, exist_ok=True)
    with (root / "frames.csv").open(encoding="utf-8-sig") as stream:
        rows = list(csv.DictReader(stream))
    for row in rows:
        for key in ("frame", "order", "batch", "bounce", "cursor", "updated", "cards", "publishedBatches", "rays", "seed"):
            row[key] = int(row[key])
        for key in ("lux", "lightR", "lightG", "lightB"):
            row[key] = float(row[key])
        for key in ("published", "accumulate"):
            row[key] = row[key] == "True"
    references = {3600: atlas(root, "reference-bright", 0), 1800: atlas(root, "reference-dim", 0)}
    assert all(np.isfinite(a).all() for a in references.values())
    report = {"arms": {}, "static_checkpoints": {}, "light_steps": {}}
    metrics, plots, gi_rows, gi_curves = [], {}, [], {}
    for arm in dict.fromkeys(r["arm"] for r in rows):
        selected = [r for r in rows if r["arm"] == arm]
        expected_count = {"reference-bright": 1, "reference-dim": 1, "fixed": 128,
                          "progressive-step": 1152, "progressive-seedB": 1024, "partial-step": 96}[arm]
        assert [r["frame"] for r in selected] == list(range(expected_count)), "Incomplete capture"
        last, previous_key = None, None
        atomic_checks, frozen_checks = 0, 0
        per_frame, gi = [], []
        for row in selected:
            frame = row["frame"]
            lighting = np.fromfile(root / f"{arm}-{frame:04}-gi.f16", "<f2").reshape(48, 96, 4).astype(float)
            assert np.isfinite(lighting).all()
            gi.append(float(((lighting[..., :3] * lighting[..., 3:4]) @ LUMA).mean()))
            gi_rows.append({"arm": arm, "frame": frame, "lux": row["lux"], "mean_y": gi[-1]})
            assert row["cards"] == 37 and row["rays"] == (4096 if arm.startswith("reference") else 16)
            assert row["lightR"] == row["lightG"] == row["lightB"] == row["lux"], "Light was not fixed"
            path = root / f"{arm}-{frame:04}-atlas.f16"
            assert path.exists() == row["published"], "Unpublished cache exported as valid"
            if not row["published"]:
                continue
            value = atlas(root, arm, frame)
            assert np.isfinite(value).all()
            key = (row["lux"], row["publishedBatches"])
            if previous_key == key:
                assert np.array_equal(value.view("<u2"), last.view("<u2")), "Partial sweep leaked into published cache"
                atomic_checks += row["accumulate"]
                frozen_checks += not row["accumulate"]
            last, previous_key = value, key
            metric = {"arm": arm, "frame": frame, "lux": row["lux"], "batches": row["publishedBatches"],
                      **compare(value, references[int(row["lux"])])}
            per_frame.append(metric)
            metrics.append(metric)
        report["arms"][arm] = {"frames": len(selected), "atomic_publication_checks": atomic_checks,
                                "frozen_checks": frozen_checks, "last": per_frame[-1]}
        plots[arm] = per_frame
        gi_curves[arm] = np.array(gi)
        if arm.startswith("progressive"):
            static = [r for r in per_frame if r["lux"] == 3600]
            report["static_checkpoints"][arm] = [next(r for r in static if r["batches"] == n)
                                                  for n in (1, 2, 4, 8, 16, 32, 64, 128)]
            # Confirm refinement never advances the published solution beyond eight bounce orders.
            assert all(r["order"] == 8 for r in selected if r["batch"] > 0)
        changed = next((i for i in range(1, len(selected)) if selected[i]["lux"] != selected[i - 1]["lux"]), None)
        if changed is not None:
            now = selected[changed]
            assert now["batch"] == now["bounce"] == now["cursor"] == now["order"] == 0
            assert not now["published"], "Stale lighting remained published after a light change"
            first = next(r for r in selected[changed:] if r["published"])
            consumer = next(r for r in selected[changed:] if r["order"] > 0)
            assert first["publishedBatches"] == 1
            report["light_steps"][arm] = {"changed_frame": changed,
                "first_new_publication_frame": first["frame"], "new_publication_latency_frames": first["frame"] - changed + 1,
                "first_consumer_frame": consumer["frame"], "consumer_latency_frames": consumer["frame"] - changed + 1,
                "first_new_cache": next(r for r in per_frame if r["frame"] == first["frame"]),
                "last_new_cache": per_frame[-1], "stale_cache_hidden_immediately": True}
            first_old_consumer = next(r["frame"] for r in selected[:changed] if r["order"] > 0)
            old_mean = float(np.mean(gi[max(first_old_consumer, changed - 64):changed]))
            late_start = min(len(gi) - 8, max(consumer["frame"] + 8, len(gi) - 64))
            new_mean = float(np.mean(gi[late_start:]))
            post = np.array(gi[changed:])
            settled = next((i + 1 for i in range(len(post) - 7) if np.all(np.abs(post[i:i + 8] / new_mean - 1) < .1)), None)
            report["light_steps"][arm]["gi_ceiling_roi"] = {
                "old_window_mean_y": old_mean, "late_window_mean_y": new_mean,
                "late_window_first_frame": late_start, "late_window_frames": len(gi) - late_start,
                "first_frame_y": float(post[0]), "first_frame_vs_late_percent": float(100 * (post[0] / new_mean - 1)),
                "first32_min_y": float(post[:32].min()), "first32_max_y": float(post[:32].max()),
                "settled_within_10percent_for_8_frames": settled,
                "scope": "Observed final Gather ROI response relative to its late window, not a ground-truth GI error"}
    # Distinct old histories and card schedules must produce the same first fresh-light batch.
    full_step = report["light_steps"]["progressive-step"]["first_new_publication_frame"]
    partial_step = report["light_steps"]["partial-step"]["first_new_publication_frame"]
    a, b = atlas(root, "progressive-step", full_step), atlas(root, "partial-step", partial_step)
    assert np.array_equal(a.view("<u2"), b.view("<u2")), "Card budget changed a fresh light solve"
    report["full_vs_partial_first_light_solve_bitwise_equal"] = True
    controls = root.parent / "controls"
    if controls.exists():
        with (controls / "frames.csv").open(encoding="utf-8-sig") as stream:
            control_rows = list(csv.DictReader(stream))
        capped = [r for r in control_rows if r["arm"] == "cold-dim-cap2" and int(r["frame"]) >= 16]
        assert len(capped) == 16
        assert all(int(r["updated"]) == 0 and int(r["batch"]) == 2 and int(r["order"]) == 8 for r in capped)
        cold = atlas(controls, "cold-dim-cap2", 7)
        assert np.array_equal(cold.view("<u2"), a.view("<u2")), "Light reset differs from a cold first batch"
        cap = atlas(controls, "cold-dim-cap2", 15)
        assert all(np.array_equal(cap.view("<u2"), atlas(controls, "cold-dim-cap2", i).view("<u2")) for i in range(16, 32))
        fixed = atlas(root, "fixed", 127)
        assert all(np.array_equal(fixed.view("<u2"), atlas(controls, "default-restored", i).view("<u2")) for i in range(32))
        allocations = [json.loads(p.read_text()) for p in controls.glob("*-validation-allocation.json")]
        assert len(allocations) == 2 and all(r["bytes"] == 0 and r["iterations"] == 4096 for r in allocations)
        report["controls"] = {"cold_first_batch_matches_light_reset_bitwise": True, "batch_cap_stops_updates": True,
                              "default_restored_bitwise": True, "input_validation_allocations": allocations}
    (output / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf8")
    with (output / "metrics.csv").open("w", newline="", encoding="utf8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(metrics[0]))
        writer.writeheader()
        writer.writerows(metrics)
    with (output / "gi-roi.csv").open("w", newline="", encoding="utf8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(gi_rows[0]))
        writer.writeheader()
        writer.writerows(gi_rows)
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(1, 3, figsize=(17, 4.6), constrained_layout=True)
    for arm, data in report["static_checkpoints"].items():
        axes[0].plot([r["batches"] * 16 for r in data], [r["rmse_y"] for r in data], "o-", label=arm)
    axes[0].axhline(report["arms"]["fixed"]["last"]["rmse_y"], color="gray", linestyle="--", label="Frozen 16 rays")
    axes[0].set(xscale="log", xlabel="Accumulated rays / texel / order", ylabel="Cache texel Y RMSE vs 4096-ray reference",
                title="Fixed 8 bounce orders; new independent sample batches")
    axes[0].legend(fontsize=8)
    for arm, step in report["light_steps"].items():
        data = [r for r in plots[arm] if r["frame"] >= step["changed_frame"]]
        axes[1].plot([r["frame"] - step["changed_frame"] + 1 for r in data], [r["mean_y"] for r in data], label=arm)
    reference_mean = compare(references[1800], references[1800])["mean_y"]
    axes[1].axhline(reference_mean, color="gray", linestyle="--", label="New-light reference")
    axes[1].set(xlabel="Frames since 3600 to 1800 light step", ylabel="Published cache mean Y",
                title="Old cache hidden immediately; complete sweeps published")
    axes[1].legend(fontsize=8)
    for arm, step in report["light_steps"].items():
        post = gi_curves[arm][step["changed_frame"]:]
        axes[2].plot(np.arange(len(post)) + 1, post, label=arm)
        axes[2].axhline(step["gi_ceiling_roi"]["late_window_mean_y"], linestyle=":", alpha=.4)
    axes[2].set(xlabel="Frames since light step", ylabel="Final Gather ceiling ROI mean Y",
                title="Actual Gather: refill energy gap and response")
    axes[2].legend(fontsize=8)
    fig.savefig(output / "convergence-and-light-response.png", dpi=160)
    plt.close(fig)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    analyze(args.capture, args.output)
