"""Build two static replicas exposing anchor, SH and full-resolution GI stages."""
import argparse
import json
from pathlib import Path

from BuildScreenProbeThinShellQuality import build as quality_build


def build(template, output):
    source = quality_build(template, output, trace_inputs=True)

    def replace(old, new):
        nonlocal source
        if source.count(old) != 1:
            raise ValueError(f"Expected one source fragment: {old[:100]!r}")
        source = source.replace(old, new, 1)

    replace("Enumerable.Repeat((false,false),4)", "Enumerable.Repeat((false,false),2)")
    replace('            ReadRegion(cmd,Resolve("m_HitLighting"),"raw",save:false);', '''            ReadRegion(cmd,Resolve("m_HitLighting"),"raw");
            ReadRegion(cmd,Resolve("m_Probes"),"anchor-center",0,0,120,68);
            ReadRegion(cmd,Resolve("m_PreparedAnchor"),"prepared-center",0,0,120,68);
            ReadRegion(cmd,Resolve("m_ReconstructedAnchor"),"reconstructed-center",0,0,120,68);
            ReadRegion(cmd,Resolve("m_CurrentAnchorStatistics"),"statistics-center",0,0,120,68);
            ReadRegion(cmd,Resolve("m_FilteredProbes"),"sh-center",0,0,840,68);''')
    replace("frame<warmup || index!=0 ||", "frame<warmup ||")
    replace('        else ReadRegion(cmd,texture,"resolved-emitter-gap",720,936,480,40);', '''        else if(index==0)ReadRegion(cmd,texture,"resolved-emitter-gap",720,936,480,40);
        else if(index==1)ReadRegion(cmd,texture,"temporal-emitter-gap",720,936,480,40);''')
    replace('schema="screen-probe-anchor-quality.v1"', 'schema="screen-probe-anchor-stage-experiment.v1"')
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs").read_text(encoding="utf-8-sig")
    source = build(template, args.output.resolve())
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "capture-script.cs").write_text(source, encoding="utf-8")
    (args.output / "protocol.json").write_text(json.dumps(dict(
        schema="screen-probe-anchor-stage-protocol.v1", warmup=1024, measured=128,
        arms=["00-r0-t0", "01-r0-t0"], spacing=16, rays=8, anchorMaxAge=512,
        payloadBudget=512*1024**2, sourceSha256="40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8",
        scope="Frozen-source static replicas; base probe grid and Resolve/temporal/final ROI; no performance claim"), indent=2)+"\n", encoding="utf-8")
    print(json.dumps(dict(root=str(args.output.resolve()))))
