"""Archive official Nsight exports and verify matching capture conditions."""
import hashlib
import csv
import json
import zipfile
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SOURCE = ROOT / "Temp~/vsm-depth-search/nsight"
BASELINE = ROOT / "Temp~/vsm-nsight-20260913"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


checks, manifest = {}, []
with zipfile.ZipFile(HERE / "nsight-exports.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for resolution in (2048, 4096):
        label = f"r{resolution}"
        folder = SOURCE / label
        baseline = BASELINE / f"{label}_recheck"
        ready = read(folder / "ready.json")
        capture = next(folder.glob("*.ngfx-gputrace"))
        captured_at = datetime.strptime(capture.stem.removeprefix("Unity_"), "%Y_%m_%d_%H_%M_%S").astimezone(timezone.utc)
        ready_at = datetime.fromisoformat(ready["utc"].removesuffix("Z")).replace(tzinfo=timezone.utc)
        checks[label + "_warm_before_capture"] = ready_at < captured_at
        for filename in ("settings-effective.json", "camera-transform.json", "camera.json", "camera-additional.json", "light.json"):
            checks[label + "_baseline_" + filename] = read(folder / filename) == read(baseline / filename)
        def config(path):
            with path.open(encoding="utf-8-sig", newline="") as f:
                return {row[0]: row[1:] for row in csv.reader(f, delimiter="\t") if row}
        actual = config(folder / "BASE/REPRO_INFO.xls")
        original = config(baseline / "BASE/REPRO_INFO.xls")
        keys = ("Driver Version", "Allocated Event Buffer Memory (kB)",
                "Allocated Hardware Event Buffer Memory (kB)", "GPU Clocks",
                "Warp State Sampling Interval", "Metric Set", "Multi-Pass Metrics")
        checks[label + "_capture_controls_match"] = all(actual[k] == original[k] for k in keys)
        manifest.append(dict(run=label, capture=str(capture), bytes=capture.stat().st_size,
            sha256=hashlib.file_digest(capture.open("rb"), "sha256").hexdigest(),
            warm_seconds_before_capture=(captured_at - ready_at).total_seconds()))
        for file in folder.rglob("*"):
            if file.is_file() and (file.suffix in (".xls", ".json") or file.name in
                    ("ngfx.log", "launch-failed.log", "launch-timeout.log", "ReportGeneratorTags.txt")):
                archive.write(file, label + "/" + file.relative_to(folder).as_posix())
checks["all_passed"] = all(checks.values())
(HERE / "nsight-manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
(HERE / "nsight-verification.json").write_text(json.dumps(checks, indent=2), encoding="utf-8")
sources = ["Shaders/Core/Private/VSMSMRT.hlsl", "Shaders/Core/Private/CSMShadowResolve.compute",
    "Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.cs",
    "Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"]
(HERE / "source-sha256.json").write_text(json.dumps({name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()
    for name in sources}, indent=2), encoding="utf-8")
print(json.dumps(checks, indent=2))
assert checks["all_passed"], checks
