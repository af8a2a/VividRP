"""Capture the native GI chain with controlled black-environment classification."""
import argparse
import hashlib
import json
from pathlib import Path
from BuildSdfBlackEnvironmentCandidate import build_source, install_script


def build(output,corrected,guided,sequence=1733,measured=128):
    assert measured in (128,1024)
    warmup=4096 if measured==1024 else 1024
    folder=Path(__file__).resolve().parent
    source=(folder/'ScreenProbeGeometryReferenceBase.cs.txt').read_text(encoding='utf-8-sig')
    helper=(folder/'ScreenProbeBlackEnvironmentQuality.cs.txt').read_text(encoding='utf-8-sig')

    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100]
        source=source.replace(old,new,1)

    replace('__OUTPUT__',output.as_posix());replace('warmup = 512, count = 1024',f'warmup = {warmup}, count = {measured}')
    replace('48L*1024*1024','192L*1024*1024');replace('blockFrames=64,blocks=32',f'blockFrames=64,blocks={measured//64}')
    replace('(currentArm=="uniform-b"?16:0)+(f-warmup)/blockFrames','(f-warmup)/blockFrames')
    replace('bool save=tag!="raw" || f<warmup+64 || f==warmup+count-1;',
            'bool save=count<=128 || (tag=="raw"?f<warmup+64 || f==warmup+count-1:f==warmup || f==warmup+count-1);')
    replace('                        if(baseGeometrySha256!="__BASE_GEOMETRY_SHA__")throw new InvalidOperationException("Reference base geometry differs from its exact input.");','')
    replace('"EditorTraceCapture", "EditorLinearCapture", "EditorGatherFrameOverride",','"EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorGatherFrameOverride",')
    replace('    string arm = "";','    string arm = "";\n'+helper)
    replace('phase = 1; frame = 0;','phase = 1; frame = -1;')
    replace('        Read(Resolve("m_HitLighting"), "raw");','''        Read(Resolve("m_HitLighting"), "raw");Read(geometry,"geometry");
        ReadQuality(context,Resolve("m_Probes"),"anchor","probe");
        ReadQuality(context,Resolve("m_PreparedAnchor"),"prepared","probe");
        ReadQuality(context,Resolve("m_ReconstructedAnchor"),"reconstructed","probe");
        ReadQuality(context,Resolve("m_FilteredProbes"),"sh","sh");
        ReadQuality(context,Resolve("m_DirectionalTemporal"),"directional","directional");
        ReadQuality(context,Resolve("m_DirectionalTemporal"),"directional-meta","directional-meta");
        ReadQuality(context,Resolve("m_Output"),"final-points","pixel-points");
        ReadQuality(context,Resolve("m_Output"),"final","pixel");''')
    replace('            Read(geometry, "geometry"); Read(Resolve("m_RadiosityInput"), "radiosity");','            Read(Resolve("m_RadiosityInput"), "radiosity");')
    replace('        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather); gi.screenProbeSamplingGuide.Override(false);',
            '        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather);gi.traceMode.Override(SdfDiffuseTraceMode.Hybrid);gi.screenProbeSamplingGuide.Override('+str(guided).lower()+');SetupQuality();SetupExperimentalTracing(qualityPass);')
    replace('hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 32768);','hooks["EditorGatherHistoryDiagnostics"].SetValue(null, '+str(33554432 if corrected else 0)+');')
    replace('hooks["EditorTraceCapture"].SetValue(null, trace); hooks["EditorLinearCapture"].SetValue(null, linear);',
            'hooks["EditorTraceCapture"].SetValue(null, trace);hooks["EditorLinearCapture"].SetValue(null, linear);hooks["EditorGatherStageCapture"].SetValue(null,qualityStage);')
    replace('        await Run("uniform-a", 7919); await Run("uniform-b", 25387);',f'        await Run("capture",{sequence});')
    replace('        complete = true; return', '        PublishQualityStatistics();\n        complete = true; return')
    replace('AsyncGPUReadback.WaitAllRequests(); volume.profile = previousProfile;','''AsyncGPUReadback.WaitAllRequests();volume.profile = previousProfile;
            CleanupExperimentalTracing();
            if(qualityDepth!=null){qualityDepth.Release();UnityEngine.Object.DestroyImmediate(qualityDepth);}
            if(qualityDepthCopy!=null)UnityEngine.Object.DestroyImmediate(qualityDepthCopy);
            if(qualityPass!=null) {
                var owner=type.GetField("m_Cameras",inst).GetValue(qualityPass);owner.GetType().GetMethod("CleanupCamera").Invoke(owner,new object[]{camera});
                var histories=type.Assembly.GetType("VividRP.Runtime.CameraHistorySystem").GetField("s_Histories",stat).GetValue(null);
                histories.GetType().GetMethod("CleanupCamera").Invoke(histories,new object[]{camera});
            }''')
    replace('schema = "scalar-anchor-grid-block-reference.v1",','schema = "screen-probe-black-environment-quality.v1",experimentalTracePrograms,')
    replace('            guide = Field("m_DirectionalGuide"), restir','            flags=hooks["EditorGatherHistoryDiagnostics"].GetValue(null),traceMode=Field("m_TraceMode").ToString(),pixelMaxAge=Field("m_PixelHistoryMaxAge"),anchorMaxAge=Field("m_ProbeAnchorMaxAge"),atrous=Field("m_AtrousIterations"),activeSkyType=context.Get<VividSkyData>().activeSkyType.ToString(),inverseVp=Enumerable.Range(0,16).Select(i=>((Matrix4x4)Field("m_InverseViewProjection"))[i/4,i%4]).ToArray(),guide = Field("m_DirectionalGuide"), restir')
    return install_script(source)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('output',type=Path)
    parser.add_argument('--corrected',action='store_true');parser.add_argument('--guided',action='store_true');parser.add_argument('--sequence',type=int,default=1733)
    parser.add_argument('--measured',type=int,choices=(128,1024),default=128)
    args=parser.parse_args();output=args.output.resolve();source=build(output,args.corrected,args.guided,args.sequence,args.measured)
    output.mkdir(parents=True,exist_ok=False);(output/'capture-script.cs').write_text(source,encoding='utf-8')
    (output/'candidate-tracing.compute.txt').write_text(build_source(),encoding='utf-8')
    (output/'depth-copy.compute.txt').write_text('''#pragma kernel CopyDepthCS
Texture2D<float> _Source;RWTexture2D<float> _Destination;
[numthreads(8,8,1)]void CopyDepthCS(uint3 id:SV_DispatchThreadID){if(any(id.xy>=uint2(1920,1080)))return;_Destination[id.xy]=_Source.Load(int3(id.xy,0));}
''',encoding='utf-8')
    protocol=dict(schema='screen-probe-black-environment-quality-protocol.v1',corrected=args.corrected,guided=args.guided,sequence=args.sequence,
        warmup=4096 if args.measured==1024 else 1024,measuredFrames=args.measured,spacing=16,rays=8,payloadBudget=192*1024**2,
        finalStatistics=True,statisticsScope='Exact per-pixel consumed-Y sum/square, frame-delta-square and64frame block sums; long runs retain first64frame raw and endpoints',
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in output.iterdir()},
        gates=dict(inputGeometryExact=True,affectedVarianceReductionPercent=5,otherVarianceIncreasePercent=2,meanChangeAbsPercent=1,frameDifferenceRmsIncreasePercent=2),
        note='Native chain, one arm. Uniform-direction controls first; actual depth/GBuffer/geometry/VP must match before comparison. Full guide quality and motion are later gates. No default installation.')
    (output/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n',encoding='utf-8');print(json.dumps(dict(root=str(output))))
