"""Analyze Nsight Graphics 2026.3 multi-frame tab-separated .xls exports.

No Excel dependency: repeated metric headers are the captured frame columns.
The exporter also repeats flattened event rows; the first occurrence holds the
full frame vector. Later rows must not be counted as independent samples.
"""
import csv
import json
import statistics
from pathlib import Path

HERE = Path(__file__).resolve().parent
SOURCE = HERE.parents[2] / "Temp~" / "vsm-nsight-20260913"

KEYS = {
    "dram_throughput_pct": "dram__sectors.avg.pct_of_peak_sustained_elapsed",
    "l2_throughput_pct": "GPUTrace.lts__throughput.avg.pct_of_peak_sustained_elapsed",
    "l1tex_throughput_pct": "GPUTrace.l1tex__throughput.avg.pct_of_peak_sustained_elapsed",
    "sm_throughput_pct": "GPUTrace.sm__throughput.avg.pct_of_peak_sustained_elapsed",
    "l1_hit_pct": "Top_Level_Triage.l1tex__t_sector_hit_rate.pct",
    "l1_texture_load_hit_pct": "Top_Level_Triage.l1tex__t_sector_pipe_tex_mem_texture_op_ld_hit_rate.pct",
    "l2_hit_pct": "lts__average_t_sector_hit_rate_realtime.pct",
    "l2_tex_hit_pct": "lts__average_t_sector_hit_rate_srcunit_tex_realtime.pct",
    "compute_occupancy_pct": "tpc__warps_active_shader_cs_queue_sync_realtime.avg.pct_of_peak_sustained_elapsed",
    "pred_on_threads_per_inst": "Top_Level_Triage.sm__average_thread_inst_executed_pred_on_per_inst_executed_realtime.ratio",
    "threads_per_inst": "smsp__average_thread_inst_executed_per_inst_executed.ratio",
    "clock_export": "gpc__cycles_elapsed.avg.per_second",
    "dram_traffic_export": "dram__sectors.sum",
    "l1_traffic_export": "l1tex__t_sectors.sum",
}

def table(path):
    with path.open(encoding="utf-8-sig", newline="") as f:
        return list(csv.reader(f, delimiter="\t"))

def stat(values):
    return dict(values=values, mean=statistics.mean(values),
                median=statistics.median(values), min=min(values), max=max(values))

def run(resolution, label=None):
    folder = SOURCE / (label or f"r{resolution}")
    events = table(folder / "BASE/D3DPERF_EVENTS.xls")
    frames = len(events[0]) - 1
    assert frames == 4, frames
    timings = {}
    for row in events[1:]:
        name = row[0].strip()
        if name.startswith("VSM.") and name not in timings:
            timings[name] = stat([float(x) for x in row[1:]])
    regimes = table(folder / "BASE/GPUTRACE_REGIMES.xls")
    header = regimes[0]
    trace = next(row for row in regimes[1:] if row[0].endswith("/VSM.ResolveTrace"))
    metrics = {}
    for key, column in KEYS.items():
        i = header.index(column)
        assert header[i:i + frames] == [column] * frames, column
        metrics[key] = stat([float(x) for x in trace[i:i + frames]])
    raw_metrics = {}
    for i, column in enumerate(header[1:], 1):
        if column not in raw_metrics:
            raw_metrics[column] = [float(x) for x in trace[i:i + frames]]
    stalls = {}
    for i, column in enumerate(header):
        if column.startswith("GPUTrace.PCSampler.tpc__warps_issue_stalled_") and column.endswith(".avg.per_cycle_elapsed"):
            if column in stalls:
                continue
            stalls[column] = [float(x) for x in trace[i:i + frames]]
    total = [sum(values[f] for values in stalls.values()) for f in range(frames)]
    stall_shares = {
        key.split("stalled_")[1].split(".avg")[0]: stat([100 * value / total[f] for f, value in enumerate(values)])
        for key, values in stalls.items()
    }
    ready = json.loads((folder / "ready.json").read_text())
    settings = json.loads((folder / "settings-effective.json").read_text())
    assert ready["resolution"] == resolution and ready["capacity"] == 1024
    assert ready["active"] and ready["frames"] >= 256
    assert ready["counters"][2:] == [0, 0]
    assert not settings["virtualShadowMapSMRTAdaptiveRays"]["m_Value"]
    assert settings["virtualShadowMapSMRTRayCount"]["m_Value"] == 4
    log = (folder / "ngfx.log").read_text(encoding="utf-8-sig")
    for failure in ("ran out of resource", "sampling data was dropped", "timed out"):
        assert failure not in log.lower(), failure
    assert "Succeeded to export data" in log
    return dict(ready=ready, frames=frames, timings_ms=timings, trace_metrics=metrics, raw_trace_metrics=raw_metrics,
                stall_sample_shares_pct=stall_shares)

if __name__ == "__main__":
    results = {str(r): run(r) for r in (2048, 4096)}
    for resolution in (2048, 4096):
        label = f"r{resolution}_recheck"
        if (SOURCE / label / "BASE/GPUTRACE_REGIMES.xls").exists():
            results[f"{resolution}_recheck"] = run(resolution, label)
    (HERE / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
    a, b = results["2048"], results["4096"]
    rows = []
    for key in ["VSM.Resolve", "VSM.ResolveTrace", "VSM.Allocate", "VSM.FilterHorizontal", "VSM.FilterTemporalVertical"]:
        rows.append((key + " (ms)", a["timings_ms"][key]["median"], b["timings_ms"][key]["median"]))
    for key in KEYS:
        rows.append((key, a["trace_metrics"][key]["median"], b["trace_metrics"][key]["median"]))
    for key in ["long_scoreboard_pipe_l1tex", "wait", "short_scoreboard", "branch_resolving", "not_selected", "selected"]:
        rows.append(("stall_share_" + key, a["stall_sample_shares_pct"][key]["median"], b["stall_sample_shares_pct"][key]["median"]))
    with (HERE / "comparison.csv").open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["metric", "2048_median", "4096_median", "ratio"])
        for key, av, bv in rows:
            writer.writerow([key, av, bv, bv / av if av else None])
    print("\n".join(f"{key}: {av:.4f} -> {bv:.4f} ({bv / av:.3f}x)" for key, av, bv in rows if av))
