"""Compare anchor age/storage through existing Editor hooks, without asset edits."""
import argparse
import json
from pathlib import Path

from BuildScreenProbeCoordinateQuality import build as quality_build


def build(template, output, age, precision, production=False):
    if age not in (128, 512) or precision not in ("half", "float"):
        raise ValueError("This controlled experiment supports age128/512 and half/float storage")
    source = quality_build(template, output)
    full = precision == "float"
    if age > 128 and not full:
        raise ValueError("Long anchor windows require float storage")
    if production and full != (age > 128):
        raise ValueError("Production selects anchor precision from the configured age")
    fmt = "R32G32B32A32_SFloat" if full else "R16G16B16A16_SFloat"

    def replace(old, new):
        nonlocal source
        if source.count(old) != 1:
            raise ValueError(f"Expected one original fragment: {old[:100]!r}")
        source = source.replace(old, new, 1)

    replace("recorderMode=mode.Item1; tickMode=mode.Item2; age=128;", f"recorderMode=mode.Item1; tickMode=mode.Item2; age={age};")
    replace('hooks["EditorGatherHistoryDiagnostics"].SetValue(null,age>128?32:0);',
            f'hooks["EditorGatherHistoryDiagnostics"].SetValue(null,{32 if full and not production else 0});')
    if production:
        replace('hooks["EditorProbeAnchorMaxAgeOverride"].SetValue(null,age);', 'hooks["EditorProbeAnchorMaxAgeOverride"].SetValue(null,-1);')
        replace('gi.screenProbeAnchorMaxAge.Override(128);', f'gi.screenProbeAnchorMaxAge.Override({age});')
    replace('(age > 128 ? "R32G32B32A32_SFloat" : "R16G16B16A16_SFloat")', '"' + fmt + '"')
    replace('            if(endpoint)ReadRegion(cmd,Resolve("m_RadiosityInput"),"radiosity");', """            if(endpoint) {
                ReadRegion(cmd,Resolve("m_RadiosityInput"),"radiosity");
                ReadRegion(cmd,Resolve("m_CurrentAnchorStatistics"),"anchor-statistics");
                ReadRegion(cmd,Resolve("m_Probes"),"anchor");
            }""")
    replace('bias=Field("m_Bias"),anchorAge=Field("m_ProbeAnchorMaxAge"),atrous=Field("m_AtrousIterations"),', """bias=Field("m_Bias"),anchorAge=Field("m_ProbeAnchorMaxAge"),atrous=Field("m_AtrousIterations"),
                flags=hooks["EditorGatherHistoryDiagnostics"].GetValue(null),
                anchorFormat=Resolve("m_Probes").graphicsFormat.ToString(),
                preparedFormat=Resolve("m_PreparedAnchor").graphicsFormat.ToString(),
                reconstructedFormat=Resolve("m_ReconstructedAnchor").graphicsFormat.ToString(),""")
    replace("savedPayloadBudget,savedPayloadBytes,", f"savedPayloadBudget,savedPayloadBytes,anchorConfiguration=new {{ maxAge={age},fullPrecision={str(full).lower()},settingsPath={str(production).lower()} }},")
    replace('schema="screen-probe-coordinate-quality.v1"', 'schema="screen-probe-anchor-quality.v1"')
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--age", type=int, choices=(128, 512), required=True)
    parser.add_argument("--precision", choices=("half", "float"), required=True)
    parser.add_argument("--production", action="store_true", help="Use the public Volume parameter with diagnostics=0 and no age override")
    args = parser.parse_args()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs").read_text(encoding="utf-8-sig")
    source = build(template, args.output.resolve(), args.age, args.precision, args.production)
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "capture-script.cs").write_text(source, encoding="utf-8")
    (args.output / "protocol.json").write_text(json.dumps(dict(schema="screen-probe-anchor-quality-protocol.v1",
        maxAge=args.age,precision=args.precision,settingsPath=args.production,
        editorDiagnosticFlags=32 if args.precision=="float" and not args.production else 0,
        arms=["static-a","static-b","motion-a","motion-b"],warmup=1024,measured=128,
        spacing=16,rays=8,savedPayloadBudget=192*1024**2,
        note="Same ROI and trajectory as coordinate-quality; actual anchor age/formats recorded every measured frame. No source or asset change."),indent=2)+"\n",encoding="utf-8")
    print(json.dumps(dict(root=str(args.output.resolve()))))
