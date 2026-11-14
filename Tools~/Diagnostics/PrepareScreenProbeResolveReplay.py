"""Prepare immutable real GPU SH fields to isolate interpolation variance."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path

import numpy as np

from TemporalSamplingExperiment import read


def prepare(source, parent):
    path = source / "capture.json"
    capture = json.loads(path.read_text(encoding="utf-8"))
    assert capture["complete"] and not capture["errors"] and capture["schema"] == "scalar-anchor-prefilter.v1"
    files = {(r["arm"], r["frame"], r["tag"]): r for r in capture["records"] if "tag" in r}
    first = capture["warmup"]
    frames = range(first, first + capture["measuredFrames"])
    arm = "candidate"
    geometry = Path(files[arm, first, "geometry"]["path"]).read_bytes()
    assert all(Path(files[arm, f, "geometry"]["path"]).read_bytes() == geometry for f in frames)
    sh = np.stack([read(files[arm, f, "sh"]) for f in frames])
    assert sh.shape == (256, 136, 840, 4)
    root = parent / ("ScreenProbeResolveReplay-" + datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S-%f"))
    root.mkdir(exist_ok=False)
    for name, data in [("mean-sh", sh.mean(0, dtype=np.float64)), ("last-sh", sh[-1])]:
        (root / (name + ".bin")).write_bytes(data.astype("<f2").tobytes())
    (root / "expected-geometry.bin").write_bytes(geometry)
    (root / "expected-radiosity.bin").write_bytes(Path(files[arm, first, "radiosity"]["path"]).read_bytes())
    result = dict(schema="screen-probe-resolve-replay-input.v1", source=str(source),
                  sourceManifestSha256=hashlib.sha256(path.read_bytes()).hexdigest(), width=840, height=136,
                  spacing=16, rays=8, averagedFrames=256,
                  scope="Frozen actual SH fields only; neither a physical reference nor a renderer quality benchmark")
    (root / "input.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(root)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("parent", type=Path)
    args = parser.parse_args()
    prepare(args.capture, args.parent)
