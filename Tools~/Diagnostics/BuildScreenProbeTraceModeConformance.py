"""Capture the original tracing program in explicit SDF-only and Hybrid modes."""
import argparse
import hashlib
import json
from pathlib import Path


def build(output):
    source = Path(__file__).with_name("ScreenProbeGeometryReferenceBase.cs.txt").read_text(encoding="utf-8-sig")

    def replace(old, new):
        nonlocal source
        assert source.count(old) == 1, old[:100]
        source = source.replace(old, new, 1)

    replace("__OUTPUT__", output.as_posix())
    replace("// No assets saved. Two 1024-frame sequences; excludes temporal scalar accumulation.",
            "// No assets saved. Two 64-frame original-program mode controls; not a converged reference.")
    replace("warmup = 512, count = 1024", "warmup = 512, count = 64")
    replace("blockFrames=64,blocks=32", "blockFrames=64,blocks=2")
    replace("48L*1024*1024", "24L*1024*1024")
    replace('currentArm=="uniform-b"?16:0', 'currentArm=="hybrid"?1:0')
    replace('bool save=tag!="raw" || f<warmup+64 || f==warmup+count-1;',
            'bool save=!tag.StartsWith("input-",StringComparison.Ordinal) && (tag=="raw" || f==warmup || f==warmup+count-1);')
    replace('                        if(baseGeometrySha256!="__BASE_GEOMETRY_SHA__")throw new InvalidOperationException("Reference base geometry differs from its exact input.");', '')
    replace('        Read(Resolve("m_HitLighting"), "raw");', '        Read(Resolve("m_HitLighting"), "raw");Read(geometry,"geometry");')
    replace('            Read(geometry, "geometry"); Read(Resolve("m_RadiosityInput"), "radiosity"); Read(Resolve("m_CurrentAnchorStatistics"), "statistics");',
            '            Read(Resolve("m_RadiosityInput"), "radiosity");Read(Resolve("m_GBuffer0"),"input-gbuffer0");Read(Resolve("m_GBuffer1"),"input-gbuffer1");')
    replace('guide = Field("m_DirectionalGuide"), restir', 'traceMode = Field("m_TraceMode").ToString(), useSdf = Field("m_UseSdf"), guide = Field("m_DirectionalGuide"), restir')
    replace('        await Run("uniform-a", 7919); await Run("uniform-b", 25387);',
            '        gi.traceMode.Override(SdfDiffuseTraceMode.SdfOnly);await Run("sdf-only",7919);\n        gi.traceMode.Override(SdfDiffuseTraceMode.Hybrid);await Run("hybrid",7919);')
    replace('schema = "scalar-anchor-grid-block-reference.v1",', 'schema = "screen-probe-native-trace-mode-conformance.v1",')
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    output = args.output.resolve()
    source = build(output)
    output.mkdir(parents=True, exist_ok=False)
    path = output / "capture-script.cs"
    path.write_text(source, encoding="utf-8")
    protocol = dict(schema="screen-probe-native-trace-mode-conformance-protocol.v1",
        spacing=16, rays=8, warmup=512, measuredFrames=64, frameBase=7919,
        arms=["sdf-only", "hybrid"], originalTraceShader=True,
        payloadBudget=24*1024**2, scriptSha256=hashlib.sha256(path.read_bytes()).hexdigest(),
        limitations=["Short mode/source diagnostic, not a converged energy reference or performance comparison"])
    (output / "protocol.json").write_text(json.dumps(protocol, indent=2)+"\n", encoding="utf-8")
    print(json.dumps(dict(root=str(output))))
