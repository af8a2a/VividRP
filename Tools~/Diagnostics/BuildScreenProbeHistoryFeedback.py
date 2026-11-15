"""Pilot spatial color-history feedback without editing production shaders."""
import argparse
import hashlib
import json
from pathlib import Path
import re


def build(output, sequence, method="wavelet"):
    folder=Path(__file__).resolve().parent
    source=(folder/"ScreenProbeGeometryReferenceBase.cs.txt").read_text(encoding="utf-8-sig")
    helper=(folder/"ScreenProbeHistoryFeedback.cs.txt").read_text(encoding="utf-8-sig")
    assert method in ("wavelet","conservative","conservative-moments")
    helper=helper.replace("__CONSERVATIVE__","false" if method=="wavelet" else "true")
    helper=helper.replace("__ALIGN_MOMENTS__","true" if method=="conservative-moments" else "false")

    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100]
        source=source.replace(old,new,1)

    replace("__OUTPUT__",output.as_posix())
    replace("// Independent uniform current-frame scalar grid, same frozen Cornell source.",
            "// Native color-history feedback pilot with a frozen Cornell source.")
    replace("// No assets saved. Two 1024-frame sequences; excludes temporal scalar accumulation.",
            "// No assets saved. Two baselines and one early-wavelet history-feedback pilot.")
    replace("warmup = 512, count = 1024","warmup = 1024, count = 256")
    replace("blockFrames=64,blocks=32","blockFrames=64,blocks=12")
    replace("48L*1024*1024","384L*1024*1024")
    replace('currentArm=="uniform-b"?16:0','currentArm=="baseline-b"?4:currentArm=="feedback"?8:0')
    replace('bool save=tag!="raw" || f<warmup+64 || f==warmup+count-1;',
            'bool save=tag=="raw"?f<warmup+64 || f==warmup+count-1:f==warmup || f==warmup+count-1;')
    replace('                        if(baseGeometrySha256!="__BASE_GEOMETRY_SHA__")throw new InvalidOperationException("Reference base geometry differs from its exact input.");','')
    replace('"EditorTraceCapture", "EditorLinearCapture", "EditorGatherFrameOverride",',
            '"EditorTraceCapture", "EditorLinearCapture", "EditorAtrousDispatchCapture", "EditorGatherFrameOverride",')
    replace('    string arm = "";','    string arm = "";\n'+helper)
    replace('        Read(Resolve("m_HitLighting"), "raw");', '''        Read(Resolve("m_HitLighting"), "raw");Read(geometry,"geometry");
        ReadFeedbackCrop(context,lighting,"final-ceiling",912,970,96,48);
        ReadFeedbackCrop(context,lighting,"final-floor",912,130,96,64);
        ReadFeedbackCrop(context,lighting,"final-back",1100,600,96,64);
        ReadFeedbackCrop(context,lighting,"final-edge",880,414,160,128);''')
    replace('            Read(geometry, "geometry"); Read(Resolve("m_RadiosityInput"), "radiosity"); Read(Resolve("m_CurrentAnchorStatistics"), "statistics");',
            '            Read(Resolve("m_RadiosityInput"), "radiosity");Read(Resolve("m_CurrentAnchorStatistics"), "statistics");')
    replace('            guide = Field("m_DirectionalGuide"), restir',
            '            feedback = arm=="feedback", traceMode = Field("m_TraceMode").ToString(), atrousIterations = Field("m_AtrousIterations"), anchorMaxAge = Field("m_ProbeAnchorMaxAge"), pixelMaxAge = Field("m_PixelHistoryMaxAge"), guide = Field("m_DirectionalGuide"), restir')
    replace('        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather); gi.screenProbeSamplingGuide.Override(false);',
            '        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather);gi.traceMode.Override(SdfDiffuseTraceMode.Hybrid);SetupFeedback();')
    replace('hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 32768);','hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 0);')
    replace('hooks["EditorTraceCapture"].SetValue(null, trace); hooks["EditorLinearCapture"].SetValue(null, linear);',
            'hooks["EditorTraceCapture"].SetValue(null, trace); hooks["EditorLinearCapture"].SetValue(null, linear);hooks["EditorAtrousDispatchCapture"].SetValue(null,feedbackHook);')
    replace('        await Run("uniform-a", 7919); await Run("uniform-b", 25387);',
            f'        await Run("baseline-a",{sequence});await Run("baseline-b",{sequence});await Run("feedback",{sequence});')
    replace('AsyncGPUReadback.WaitAllRequests(); volume.profile = previousProfile;',
            'AsyncGPUReadback.WaitAllRequests();CleanupFeedback();volume.profile = previousProfile;')
    replace('schema = "scalar-anchor-grid-block-reference.v1",',
            'schema = "screen-probe-color-history-feedback-pilot.v1", feedbackPrograms,')
    replace('quantity = "Current-frame consumed E/pi grid; independent uniform sequences; same-renderer target"',
            'quantity = "Raw probe diagnostics and final ROIs; early-wavelet color-history feedback pilot"')
    workspace=folder.parents[1]
    shader=(workspace/"Shaders/Core/Private/GlobalIllumination/SdfScreenProbeGatherTemporal.compute").read_text(encoding="utf-8-sig")
    shader=re.sub(r'#include "([^"/]+)"',lambda m:'#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/'+m[1]+'"',shader)
    shader+='''
#pragma kernel WriteScreenProbeHistoryFeedbackCS VIVID_SDF_SCREEN_PROBE_TEMPORAL
Texture2D<float4> _SdfFeedbackLighting;
RWTexture2D<float4> _SdfFeedbackPixelHistory;
[numthreads(8,8,1)]
void WriteScreenProbeHistoryFeedbackCS(uint3 id:SV_DispatchThreadID) {
    uint width,height;_SdfFeedbackPixelHistory.GetDimensions(width,height);
    if(any(id.xy>=uint2(width,height)))return;
    float4 history=_SdfFeedbackPixelHistory[id.xy];
    float4 lighting=_SdfFeedbackLighting.Load(int3(id.xy,0));
    if(history.a>0.0 && lighting.a>0.0)
        history.rgb=EncodeScreenProbeCoveredHistory(lighting.rgb,lighting.a);
    _SdfFeedbackPixelHistory[id.xy]=history;
}
'''
    if method.startswith("conservative"):shader=(folder/"ScreenProbeConservativeHistoryFeedback.compute.txt").read_text(encoding="utf-8-sig")
    if method=="conservative-moments":shader="#define VIVID_HISTORY_FEEDBACK_MOMENTS 1\n"+shader
    return source,shader


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("output",type=Path)
    parser.add_argument("--sequence",type=int,default=1733)
    parser.add_argument("--method",choices=("wavelet","conservative","conservative-moments"),required=True)
    args=parser.parse_args();output=args.output.resolve()
    source,shader=build(output,args.sequence,args.method);output.mkdir(parents=True,exist_ok=False)
    (output/"capture-script.cs").write_text(source,encoding="utf-8")
    (output/"feedback.compute.txt").write_text(shader,encoding="utf-8")
    protocol=dict(schema="screen-probe-color-history-feedback-pilot-protocol.v1",spacing=16,rays=8,warmup=1024,measuredFrames=256,
        sequence=args.sequence,method=args.method,validHistoryPredicate=True,arms=["baseline-a","baseline-b","feedback"],feedbackIteration=1,payloadBudget=384*1024**2,
        shaderSha256=hashlib.sha256((output/"feedback.compute.txt").read_bytes()).hexdigest(),
        scriptSha256=hashlib.sha256((output/"capture-script.cs").read_bytes()).hexdigest(),
        gates=dict(ceilingVarianceReductionPercent=5,meanChangeAbsPercent=1,requiredVsBothBaselines=True,
            independentSequenceRequired=True,motionAndEdgeRequired=True,energyReferenceRequired=True,globalFeedbackEnergyAbsPercent=.01),
        limitations=["Pilot only; existing joint Hybrid energy reference remains unqualified",
            "Preserves packed coverage/age/status and pixel geometry; changes color-history RGB and optionally the matching moment mean",
            "No production or performance promotion without further validation"])
    (output/"protocol.json").write_text(json.dumps(protocol,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(dict(root=str(output))))
