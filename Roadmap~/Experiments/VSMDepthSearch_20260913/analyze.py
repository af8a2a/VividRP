"""Recompute timing, correctness, and Nsight comparisons from the saved data."""
import csv
import importlib.util
import json
import statistics
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(name, value):
    (HERE / name).write_text(json.dumps(value, indent=2), encoding="utf-8")


def timing():
    modes = [1, 0, 1, 0, 0, 1, 0, 1]
    runs = []
    for stage, path in enumerate(sorted((HERE / "timing").glob("*.csv"))):
        rows = list(csv.DictReader(path.open(newline="", encoding="utf-8-sig")))
        resolution = int(path.stem.split("_r")[1].split("_")[0])
        settings = read(path.with_name(path.stem + "-settings.json"))
        assert settings["virtualShadowMapResolution"]["m_Value"] == resolution
        assert settings["virtualShadowMapPhysicalPageBudget"]["m_Value"] == 1024
        assert settings["virtualShadowMapSMRTRayCount"]["m_Value"] == 4
        assert not settings["virtualShadowMapSMRTAdaptiveRays"]["m_Value"]
        counters = list(map(int, path.with_name(path.stem + "-counters.txt").read_text().split(",")))
        assert counters == ([266, 266, 0, 0] if resolution == 2048 else [641, 641, 0, 0])
        run = dict(file=path.name, resolution=resolution, linear=modes[stage], counters=counters)
        for marker in ("VSM.ResolveTrace", "VSM.Resolve", "VSM.StaticRaster"):
            selected = [row for row in rows if row["marker"] == marker]
            assert len(selected) == 128
            counts = sorted({int(row["gpu_sample_count"]) for row in selected})
            assert counts == [1]
            values = [float(row["gpu_ns"]) / 1e6 for row in selected]
            run[marker] = dict(median_ms=statistics.median(values), sample_counts=counts,
                               min_ms=min(values), max_ms=max(values))
        runs.append(run)
    assert len(runs) == 8
    restoration = {}
    for stem in ("settings", "camera", "camera-transform"):
        restoration[stem] = read(HERE / f"timing/{stem}-before.json") == read(HERE / f"timing/{stem}-after.json")
    restoration["scenes"] = (HERE / "timing/scenes-before.txt").read_text() == (HERE / "timing/scenes-after.txt").read_text()
    assert all(restoration.values()), restoration
    write("timing-results.json", runs)
    write("timing-restoration.json", restoration)
    return runs


def correctness():
    fixture = read(HERE / "gpu-fixture.json")
    assert fixture["mismatches"] == 0 and fixture["checkedCount"] == 131072
    frames = read(HERE / "same-frame/results.json")
    assert len(frames) == 192
    groups = {}
    for stage in range(12):
        rows = [row for row in frames if row["stage"] == stage]
        assert len(rows) == 16
        groups[str(stage)] = dict(resolution=rows[0]["resolution"], adaptive=rows[0]["adaptive"],
            scenario=rows[0]["scenario"], frames=len(rows), pixels=sum(r["pixels"] for r in rows),
            different=sum(r["different"] for r in rows), max_error_bits=max(r["maxErrorBits"] for r in rows))
    result = dict(frames=len(frames), pixels=sum(r["pixels"] for r in frames),
        different=sum(r["different"] for r in frames), max_error_bits=max(r["maxErrorBits"] for r in frames),
        min_shadowed_pixels=min(r["shadowed"] for r in frames), stages=groups, gpu_fixture=fixture)
    assert result["different"] == result["max_error_bits"] == 0
    assert result["min_shadowed_pixels"] > 0
    write("correctness.json", result)
    return result


def nsight():
    previous = HERE.parent / "VSMNsight_20260913"
    spec = importlib.util.spec_from_file_location("nsight_tables", previous / "analyze.py")
    parser = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(parser)
    parser.SOURCE = ROOT / "Temp~/vsm-depth-search/nsight"
    baseline = read(previous / "results.json")
    optimized = {str(r): parser.run(r) for r in (2048, 4096)}
    write("nsight-results.json", optimized)
    rows = []
    for resolution in (2048, 4096):
        a, b = baseline[f"{resolution}_recheck"], optimized[str(resolution)]
        for name in ("VSM.ResolveTrace", "VSM.Resolve", "VSM.Allocate"):
            before, after = a["timings_ms"][name]["median"], b["timings_ms"][name]["median"]
            rows.append((resolution, name + "_ms", before, after))
        for name in ("l1_hit_pct", "dram_throughput_pct", "l2_hit_pct", "l2_tex_hit_pct",
                     "compute_occupancy_pct", "pred_on_threads_per_inst", "clock_export"):
            rows.append((resolution, name, a["trace_metrics"][name]["median"], b["trace_metrics"][name]["median"]))
        name = "long_scoreboard_pipe_l1tex"
        rows.append((resolution, "long_scoreboard_sample_share_pct",
            a["stall_sample_shares_pct"][name]["median"], b["stall_sample_shares_pct"][name]["median"]))
    with (HERE / "nsight-comparison.csv").open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["resolution", "metric", "baseline_recheck", "ordered_search"])
        writer.writerows(rows)
    print("\n".join(f"{r} {name}: {a:.4f} -> {b:.4f}" for r, name, a, b in rows))


if __name__ == "__main__":
    timing()
    result = correctness()
    print(f"Correctness: {result['frames']} frames, {result['pixels']} pixels, zero differences")
    nsight()
