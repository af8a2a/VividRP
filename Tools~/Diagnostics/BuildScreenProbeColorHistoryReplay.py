"""Replay full private pixel histories on shared native per-frame GI inputs."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeSameFrameAtrous import shader_source
from BuildScreenProbeVarianceHistory import temporal_source


def build(output, method, sequence, measured, feedback_witness=False):
    assert method in ("none","wavelet","conservative-moments","stable-unclamped")
    assert measured in (64,128,256)
    folder=Path(__file__).resolve().parent;workspace=folder.parents[1]
    source=(folder/"ScreenProbeGeometryReferenceBase.cs.txt").read_text(encoding="utf-8-sig")
    helper=(folder/"ScreenProbeColorHistoryReplay.cs.txt").read_text(encoding="utf-8-sig")
    helper=helper.replace("__METHOD__",method)
    helper=helper.replace("__FEEDBACK_WITNESS__",str(feedback_witness).lower())

    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100]
        source=source.replace(old,new,1)

    replace("__OUTPUT__",output.as_posix())
    replace("warmup = 512, count = 1024",f"warmup = 1024, count = {measured}")
    replace("blockFrames=64,blocks=32",f"blockFrames=64,blocks={measured//64}")
    replace("48L*1024*1024","256L*1024*1024")
    replace('(currentArm=="uniform-b"?16:0)+(f-warmup)/blockFrames','(f-warmup)/blockFrames')
    replace('bool save=tag!="raw" || f<warmup+64 || f==warmup+count-1;',
            'bool save=tag=="raw"?f<warmup+64 || f==warmup+count-1:f==warmup || f==warmup+count-1;')
    replace('                        if(baseGeometrySha256!="__BASE_GEOMETRY_SHA__")throw new InvalidOperationException("Reference base geometry differs from its exact input.");','')
    replace('"EditorTraceCapture", "EditorLinearCapture", "EditorGatherFrameOverride",',
            '"EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorAtrousDispatchCapture", "EditorGatherFrameOverride",')
    replace('    string arm = "";','    string arm = "";\n'+helper)
    replace('phase = 1; frame = 0;', 'phase = 1; frame = -1;')
    replace('        else if (!frozen) {','        else if (!frozen) {\n            if(privateInitialized)throw new InvalidOperationException("Source invalidated during private recurrence.");')
    replace('        if(blockCounts.Any(n=>n!=64))', '        if(privatePendingPairs.Count!=0)throw new InvalidOperationException("Unpaired private AA readbacks.");\n        if(blockCounts.Any(n=>n!=64))')
    replace('        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather); gi.screenProbeSamplingGuide.Override(false);',
            '        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather);gi.traceMode.Override(SdfDiffuseTraceMode.Hybrid);SetupPrivateReplay();')
    replace('hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 32768);','hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 0);')
    replace('hooks["EditorTraceCapture"].SetValue(null, trace); hooks["EditorLinearCapture"].SetValue(null, linear);',
            'hooks["EditorTraceCapture"].SetValue(null, trace);hooks["EditorLinearCapture"].SetValue(null, linear);\n        hooks["EditorGatherStageCapture"].SetValue(null,privateTemporalHook);hooks["EditorAtrousDispatchCapture"].SetValue(null,privateWaveletHook);')
    replace('        await Run("uniform-a", 7919); await Run("uniform-b", 25387);',f'        await Run("shared",{sequence});')
    replace('AsyncGPUReadback.WaitAllRequests(); volume.profile = previousProfile;',
            'AsyncGPUReadback.WaitAllRequests();CleanupPrivateReplay();volume.profile = previousProfile;')
    replace('schema = "scalar-anchor-grid-block-reference.v1",',
            'schema = "screen-probe-private-color-history-replay.v1", privatePrograms, method=privateMethod, aaPairs=privateAaPairs,')
    replace('            guide = Field("m_DirectionalGuide"), restir',
            '            postNearOcclusion=((Vector4)Field("m_NearOcclusion")).y>0f && ((Vector4)Field("m_NearOcclusion")).z==0f, traceMode=Field("m_TraceMode").ToString(), atrousIterations=Field("m_AtrousIterations"), anchorMaxAge=Field("m_ProbeAnchorMaxAge"), pixelMaxAge=Field("m_PixelHistoryMaxAge"), guide = Field("m_DirectionalGuide"), restir')
    replace('quantity = "Current-frame consumed E/pi grid; independent uniform sequences; same-renderer target"',
            'quantity = "Shared native Resolve; independent private color/moment/geometry histories; pre-short-range-occlusion wavelet output"')
    replace('// Independent uniform current-frame scalar grid, same frozen Cornell source.','// Private color-history recurrence replay on shared native GI inputs.')
    replace('// No assets saved. Two 1024-frame sequences; excludes temporal scalar accumulation.','// No renderer history writes. Native/private baseline must be byte-exact before candidate comparison.')
    shaders={"temporal.compute.txt":temporal_source((workspace/"Shaders/Core/Private/GlobalIllumination/SdfScreenProbeGatherTemporal.compute").read_text(encoding="utf-8-sig")),
        "wavelet.compute.txt":shader_source((workspace/"Shaders/Core/Private/GlobalIllumination/SdfDiffuseAtrous.hlsl").read_text(encoding="utf-8-sig"))}
    if method=="stable-unclamped":
        marker='    return (_SdfGatherHistoryDiagnostics & 1048576u) != 0u\n        ? history : lerp(history, bounded, lerp(0.10, 1.0, fastUpdate));'
        assert shaders["temporal.compute.txt"].count(marker)==1
        shaders["temporal-candidate.compute.txt"]=shaders["temporal.compute.txt"].replace(marker,'    if (fastUpdate == 0.0)\n        return history;\n'+marker,1)
    if method=="wavelet":
        from BuildScreenProbeHistoryFeedback import build as feedback_build
        _,shaders["feedback.compute.txt"]=feedback_build(output,sequence,"wavelet")
    elif method=="conservative-moments":
        shaders["feedback.compute.txt"]="#define VIVID_HISTORY_FEEDBACK_MOMENTS 1\n"+(folder/"ScreenProbeConservativeHistoryFeedback.compute.txt").read_text(encoding="utf-8-sig")
    return source,shaders


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("output",type=Path)
    parser.add_argument("--method",choices=("none","wavelet","conservative-moments","stable-unclamped"),required=True)
    parser.add_argument("--sequence",type=int,default=1733);parser.add_argument("--measured",type=int,choices=(64,128,256),default=128)
    parser.add_argument("--feedback-witness",action="store_true")
    args=parser.parse_args();output=args.output.resolve();source,shaders=build(output,args.method,args.sequence,args.measured,args.feedback_witness)
    output.mkdir(parents=True,exist_ok=False)
    (output/"capture-script.cs").write_text(source,encoding="utf-8")
    for name,body in shaders.items():(output/name).write_text(body,encoding="utf-8")
    protocol=dict(schema="screen-probe-private-color-history-replay-protocol.v1",method=args.method,sequence=args.sequence,
        warmup=1024,measuredFrames=args.measured,spacing=16,rays=8,payloadBudget=256*1024**2,
        metadataWitnesses=True,
        feedbackWitness=args.feedback_witness,
        feedbackPublicationWitnesses=True,publicationMomentChannels="variance-age-coverage",
        spatialFeedback=args.method in ("wavelet","conservative-moments"),
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in output.iterdir()},
        gates=dict(nativeBaselineByteExact=True,ceilingVarianceReductionPercent=5,meanChangeAbsPercent=1,
            independentSequenceRequired=True,motionRequired=True,energyReferenceRequired=True),
        note="All paths share native raw Resolve, motion and camera inputs. Each private path owns color, moments and geometry ping-pong histories. No renderer history writes. Full-frame AA at measured frames and key warmup frames; ROI AA every replay frame.")
    (output/"protocol.json").write_text(json.dumps(protocol,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(dict(root=str(output))))
