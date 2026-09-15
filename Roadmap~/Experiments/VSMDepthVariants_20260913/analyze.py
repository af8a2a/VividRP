"""Aggregate window medians without treating repeated exports as new samples."""
import collections
import csv
import json
import statistics
import struct
from pathlib import Path

HERE = Path(__file__).resolve().parent
MARKERS = ("VSM.ResolveTrace", "VSM.Resolve", "VSM.ClearPhysicalPages", "VSM.PageOccupancy",
           "VSM.StaticRaster", "VSM.DynamicRaster", "VSM.Allocate", "VSM.MarkReceiverPages")


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def analyze(folder):
    assert (folder / "status.txt").read_text().strip() == "complete", folder
    windows, groups = [], collections.defaultdict(list)
    for path in sorted(folder.glob("*.csv")):
        rows = list(csv.DictReader(path.open(encoding="utf-8-sig", newline="")))
        fields = path.stem.split("_")
        record = dict(file=path.name, resolution=int(fields[1][1:]), layout=int(fields[3][1:]), mode=int(fields[4][1:]), timings_ms={})
        settings = load(path.with_name(path.stem + "-settings.json"))
        assert settings["virtualShadowMapResolution"]["m_Value"] == record["resolution"]
        assert settings["virtualShadowMapPhysicalPageBudget"]["m_Value"] == 1024
        assert settings["virtualShadowMapSMRTRayCount"]["m_Value"] == 4
        assert not settings["virtualShadowMapSMRTAdaptiveRays"]["m_Value"]
        record["counters"] = list(map(int, path.with_name(path.stem + "-counters.txt").read_text().split(",")))
        assert record["counters"][3] == 0, record
        if folder.name != "timing-moving-light":
            assert record["counters"][2] == 0, record
        for marker in MARKERS:
            selected = [r for r in rows if r["marker"] == marker]
            assert len(selected) == 128
            counts = sorted({int(r["gpu_sample_count"]) for r in selected})
            assert counts == [1], (path, marker, counts)
            values = [float(r["gpu_ns"]) / 1e6 for r in selected]
            record["timings_ms"][marker] = dict(median=statistics.median(values), min=min(values), max=max(values))
        flags = [r[0] for r in struct.iter_unpack("<4I", path.with_name(path.stem + "-metadata.bin").read_bytes()) if r[0] & 2]
        if record["mode"] & 1:
            record["page_layer_counts"] = dict(static=dict(sorted(collections.Counter((f >> 16) & 31 for f in flags).items())),
                dynamic=dict(sorted(collections.Counter((f >> 21) & 31 for f in flags).items())))
        windows.append(record)
        groups[path.stem[3:]].append(record)
    summary = {key: {marker: [r["timings_ms"][marker]["median"] for r in records] for marker in MARKERS} for key, records in groups.items()}
    restoration = {stem: load(folder / (stem + "-before.json")) == load(folder / (stem + "-after.json")) for stem in ("settings", "camera", "camera-transform")}
    restoration["scenes"] = (folder / "scenes-before.txt").read_text() == (folder / "scenes-after.txt").read_text()
    assert all(restoration.values()), (folder, restoration)
    return dict(windows=windows, groups=summary, restoration=restoration)


results = {}
for folder in sorted(HERE.iterdir()):
    if folder.is_dir() and (folder.name.startswith("timing") or folder.name.startswith("specialized")) and (folder / "status.txt").exists():
        results[folder.name] = analyze(folder)
(HERE / "timing-results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
with (HERE / "comparison.csv").open("w", newline="", encoding="utf-8") as f:
    writer = csv.writer(f)
    writer.writerow(["run", "configuration", "marker", "window_medians_ms", "median_of_window_medians_ms"])
    for name, result in results.items():
        for group, markers in result["groups"].items():
            for marker, values in markers.items():
                writer.writerow([name, group, marker, json.dumps(values), statistics.median(values)])
fixture = load(HERE / "gpu-fixture.json")
assert fixture["checkedCount"] == 1048576 and fixture["mismatches"] == 0
frames = load(HERE / "same-frame/results.json")
assert len(frames) == 192 and all(r["different"] == r["maxErrorBits"] == 0 and r["shadowed"] > 0 for r in frames)
native = {name: load(HERE / name / "results.json") for name in ("native-layout", "native-layout-moving")}
assert all(r["staticDifferences"] == r["dynamicDifferences"] == r["ownerDifferences"] == 0 for records in native.values() for r in records)
correctness = dict(gpu_fixture=fixture, resolve_frames=len(frames), resolve_pixels=sum(r["pixels"] for r in frames),
    resolve_differences=0, native=native)
(HERE / "correctness.json").write_text(json.dumps(correctness, indent=2), encoding="utf-8")
print(json.dumps({name: len(result["windows"]) for name, result in results.items()}))
