"""Preserve reproducible exports and hash the large, ignored GPU Trace files."""
import hashlib
import json
import zipfile
from datetime import datetime, timezone
from pathlib import Path
import analyze

root = analyze.HERE
manifest = []
checks = {}
baseline = root.parent / "VSMResolutionScaling_20260913/raw/camera-transform-before.json"
with zipfile.ZipFile(root / "nsight-exports.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for label in ("r2048", "r4096", "r2048_recheck", "r4096_recheck"):
        folder = analyze.SOURCE / label
        ready = json.loads((folder / "ready.json").read_text())
        capture = next(folder.glob("*.ngfx-gputrace"))
        # Capture filename is local time; the Unity readiness stamp is UTC.
        captured_at = datetime.strptime(capture.stem.removeprefix("Unity_"), "%Y_%m_%d_%H_%M_%S").astimezone(timezone.utc)
        ready_at = datetime.fromisoformat(ready["utc"].removesuffix("Z")).replace(tzinfo=timezone.utc)
        checks[label + "_warm_before_capture"] = ready_at < captured_at
        checks[label + "_camera_matches_baseline"] = json.loads((folder / "camera-transform.json").read_text()) == json.loads(baseline.read_text())
        item = dict(run=label, capture=str(capture), bytes=capture.stat().st_size,
                    sha256=hashlib.file_digest(capture.open("rb"), "sha256").hexdigest(),
                    warm_seconds_before_capture=(captured_at - ready_at).total_seconds())
        manifest.append(item)
        for file in folder.rglob("*"):
            if file.is_file() and (file.suffix in (".xls", ".json") or file.name in ("ngfx.log", "ReportGeneratorTags.txt")):
                # Raw editor logs contain unrelated licensing output. Do not bundle.
                archive.write(file, label + "/" + str(file.relative_to(folder)).replace("\\", "/"))
    a = json.loads((analyze.SOURCE / "r2048/settings-effective.json").read_text())
    for label in ("r4096", "r2048_recheck", "r4096_recheck"):
        b = json.loads((analyze.SOURCE / label / "settings-effective.json").read_text())
        av = {k: v for k, v in a.items() if k != "virtualShadowMapResolution"}
        bv = {k: v for k, v in b.items() if k != "virtualShadowMapResolution"}
        checks[label + "_only_resolution_differs"] = av == bv
        for filename in ("camera.json", "camera-additional.json", "light.json"):
            checks[label + "_" + filename + "_matches"] = json.loads((analyze.SOURCE / "r2048" / filename).read_text()) == json.loads((analyze.SOURCE / label / filename).read_text())
checks["all_passed"] = all(checks.values())
(root / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
(root / "verification.json").write_text(json.dumps(checks, indent=2), encoding="utf-8")
assert checks["all_passed"], checks
print(json.dumps(dict(checks=checks, manifest=manifest), indent=2))
