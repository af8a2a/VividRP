"""Build two 1728-frame controls with dense reads only in the first 64 frames."""
import argparse
import json
from pathlib import Path

from BuildScreenProbeRecorderCadenceExperiment import build as cadence_build


def build(template, output):
    source = cadence_build(template, output)

    def replace(old, new):
        nonlocal source
        if source.count(old) != 1:
            raise ValueError(f"Expected one original fragment: {old[:100]!r}")
        source = source.replace(old, new, 1)

    replace("// Original-shader 2x2 recorder/cadence diagnostic. No performance comparison.",
            "// Two-arm long GI repeatability control. No GPU performance comparison.")
    replace("if (frame<warmup || frame==lastFrame-1)", "if (frame<64 || frame==warmup-1 || frame==lastFrame-1)")
    replace("if (frame<=16 || frame==warmup-1 || frame==lastFrame-1)", "if (true)")
    replace("        foreach(var mode in new[] { (false,false),(true,false),(true,true),(false,true),\n                (false,true),(true,true),(true,false),(false,false) })",
            "        foreach(var mode in Enumerable.Repeat((false,false),2))")
    replace('schema="screen-probe-recorder-cadence.v1"', 'schema="screen-probe-long-repeatability.v1"')
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs").read_text(encoding="utf-8-sig")
    source = build(template, args.output.resolve())
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "capture-script.cs").write_text(source, encoding="utf-8")
    (args.output / "protocol.json").write_text(json.dumps(dict(schema="screen-probe-long-repeatability-protocol.v1",
        arms=2, frames=1728, diagnosticFlags=0, sourceWarmup=264, readFrames=list(range(64))+[1023,1727],
        recorders=False, tickReadbacks=False, savedPayloadBudget=192*1024**2,
        gate="All matching-frame functional hashes and CPU workload must agree; sparse observations do not locate the true onset"),indent=2)+"\n",encoding="utf-8")
    print(json.dumps(dict(root=str(args.output.resolve()))))
