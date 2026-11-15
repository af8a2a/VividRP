"""Build a uniform reference at exact captured receiver-position bit patterns."""
import argparse
import hashlib
import json
from pathlib import Path
import re


def tracing_source(text):
    names = re.findall(r"^#pragma kernel (\w+)", text, re.MULTILINE)
    marker = "uint _SdfScreenPreviousRadianceValid;"
    assert text.count(marker) == 1
    text = text.replace(marker, "Texture2D<float4> _SdfReferenceGeometry;\nuint _SdfUseReferenceGeometry;\n" + marker, 1)
    marker = "    float3x3 basis = DiffuseProbeFrame(normal);"
    assert text.count(marker) == 1
    text = text.replace(marker, "    if (_SdfUseReferenceGeometry != 0u)position = _SdfReferenceGeometry.Load(int3(tile,0)).xyz;\n" + marker, 1)
    # Keep every original kernel index intact for native dispatch bindings.
    text = re.sub(r'#include "([^"/]+)"',
        lambda m: '#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/' + m[1] + '"', text)
    text += '''
#pragma kernel CaptureReferenceNormalsCS
RWTexture2D<float4> _SdfReferenceNormalOutput;
[numthreads(8,8,1)]
void CaptureReferenceNormalsCS(uint3 id:SV_DispatchThreadID) {
    uint2 size=uint2(_SdfDiffuseScreenSize.xy);
    if(any(id.xy>=ProbeGridSize(size)))return;
    float3 position,normal;
    bool valid=LoadReceiver(ProbeReceiverPixel(id.xy,size),position,normal);
    _SdfReferenceNormalOutput[id.xy]=valid?float4(normal,PackProbeNormal(normal)):float4(0,0,0,-1);
}
'''
    return text, names


def build(output, geometry, trace_mode):
    assert trace_mode in ("SdfOnly", "Hybrid")
    workspace = Path(__file__).resolve().parents[2]
    source = Path(__file__).with_name("ScreenProbeGeometryReferenceBase.cs.txt").read_text(encoding="utf-8-sig")
    helper = Path(__file__).with_name("ScreenProbeGeometryReferenceReplay.cs.txt").read_text(encoding="utf-8-sig")
    shader, names = tracing_source((workspace / "Shaders/Core/Private/GlobalIllumination/SdfScreenProbeTracing.compute").read_text(encoding="utf-8-sig"))
    data = geometry.read_bytes()
    assert len(data) == 120 * 136 * 16
    base_sha = hashlib.sha256(data[:120 * 68 * 16]).hexdigest()
    helper = helper.replace("__KERNEL_NAMES__", "new[]{" + ",".join(json.dumps(n) for n in names + ["CaptureReferenceNormalsCS"]) + "}")
    helper = helper.replace("__ORIGINAL_KERNEL_NAMES__", "new[]{" + ",".join(json.dumps(n) for n in names) + "}")
    helper = helper.replace("__GEOMETRY_INPUT_SHA__", hashlib.sha256(data).hexdigest())
    helper = helper.replace("__TRACE_MODE__", trace_mode)

    def replace(old, new):
        nonlocal source
        assert source.count(old) == 1, old[:100]
        source = source.replace(old, new, 1)

    replace("__OUTPUT__", output.as_posix())
    replace("__BASE_GEOMETRY_SHA__", base_sha)
    replace('    string arm = "";', '    string arm = "";\n' + helper)
    replace('            bool save=tag!="raw" || f<warmup+64 || f==warmup+count-1;',
            '            bool save=tag=="raw"?f<warmup+64 || f==warmup+count-1:(tag=="geometry" || tag=="normal")?f==warmup || f==warmup+count-1:!tag.StartsWith("input-",StringComparison.Ordinal);')
    replace('        Read(Resolve("m_HitLighting"), "raw");', '''        Read(Resolve("m_HitLighting"), "raw");
        Read(geometry,"geometry");
        if(frame<warmup+64 || frame==warmup+count-1)ReplayReferenceAa(context,Resolve);
        int normalKernel=referenceShader.FindKernel("CaptureReferenceNormalsCS");
        type.GetMethod("BindReceiver",inst).Invoke(referencePass,new object[]{context.cmd,referenceShader,normalKernel});
        type.GetMethod("BindProbeFrame",inst).Invoke(referencePass,new object[]{context.cmd,referenceShader});
        cmd.SetComputeTextureParam(referenceShader,normalKernel,"_SdfReferenceNormalOutput",referenceNormalOutput);
        context.cmd.DispatchCompute(referenceShader,normalKernel,15,17,1);
        Read(referenceNormalOutput,"normal");''')
    replace('            Read(geometry, "geometry"); Read(Resolve("m_RadiosityInput"), "radiosity");',
            '            Read(Resolve("m_GBuffer0"),"input-gbuffer0");Read(Resolve("m_GBuffer1"),"input-gbuffer1");Read(Resolve("m_RadiosityInput"), "radiosity");')
    replace('                    if(tag=="geometry") {', '                    if(tag=="geometry" || tag=="normal") {')
    replace('                        if(baseGeometrySha256!="' + base_sha + '")', '                        if(tag=="normal") { if(referenceNormalHash==null)referenceNormalHash=baseGeometrySha256;else if(referenceNormalHash!=baseGeometrySha256)throw new InvalidOperationException("Reference normal input changed."); }\n                        if(tag=="geometry" && baseGeometrySha256!="' + base_sha + '")')
    replace('        hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 32768);',
            '        gi.traceMode.Override(SdfDiffuseTraceMode.' + trace_mode + ');SetupReference();\n        hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 32768);')
    replace('            guide = Field("m_DirectionalGuide"), restir',
            '            traceMode = Field("m_TraceMode").ToString(), guide = Field("m_DirectionalGuide"), restir')
    replace('        if(blockCounts.Any(n=>n!=64))', '        if(aaPairs.Count!=0)throw new InvalidOperationException("Unpaired reference AA readbacks.");\n        if(blockCounts.Any(n=>n!=64))')
    replace('AsyncGPUReadback.WaitAllRequests(); volume.profile = previousProfile;',
            'AsyncGPUReadback.WaitAllRequests(); CleanupReference(); volume.profile = previousProfile;')
    replace('schema = "scalar-anchor-grid-block-reference.v1",', 'schema = "screen-probe-exact-geometry-reference.v1", referencePrograms,referenceNormalHash,')
    return source, shader, base_sha


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--geometry", type=Path, required=True)
    parser.add_argument("--trace-mode", choices=("SdfOnly", "Hybrid"), required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    source, shader, base_sha = build(output, args.geometry, args.trace_mode)
    output.mkdir(parents=True, exist_ok=False)
    (output / "geometry-input.bin").write_bytes(args.geometry.read_bytes())
    (output / "reference-tracing.compute.txt").write_text(shader, encoding="utf-8")
    (output / "capture-script.cs").write_text(source, encoding="utf-8")
    protocol = dict(schema="screen-probe-exact-geometry-reference-protocol.v1", spacing=16, rays=8,
        warmup=512, measuredFramesPerArm=1024, frameBases=[7919,25387], blocks=32, blockFrames=64,
        baseGeometrySha256=base_sha, geometryInputSha256=hashlib.sha256((output / "geometry-input.bin").read_bytes()).hexdigest(),
        tracingSourceSha256=hashlib.sha256((output / "reference-tracing.compute.txt").read_bytes()).hexdigest(),
        aaScope=dict(probeRow=59,firstProbe=46,probes=28,measuredFramesPerArm=65,required="Exact native/forced receiver geometry and raw RGBA16 output"),
        payloadBudget=48*1024**2, shape=[32,68,120,5], traceMode=args.trace_mode,
        limitations=["Same renderer and frozen source, not absolute physical or Unreal truth","Fixed-position replay requires AA qualification; does not repair native geometry variation","Raw current-frame probe reference, before full-resolution reconstruction",
            "Only TraceProbe receiver positions are forced; Hybrid screen tracing uses native geometry and history inputs"])
    (output / "protocol.json").write_text(json.dumps(protocol,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(dict(root=str(output))))
