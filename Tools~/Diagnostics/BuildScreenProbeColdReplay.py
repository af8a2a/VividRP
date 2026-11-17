"""Capture complete cold Resolve inputs before investigating a final-history delta."""
import argparse
import json
from pathlib import Path
from BuildScreenProbeCardQuality import build_capture
from BuildScreenProbePixelOracleCapture import targets,snapshot_capture


HELPER='''    int coldOtherCameras=0,coldTargetCameras=0;
    ComputeShader coldDecode=null;RenderTexture coldDecoded=null;
    void ReadCold(ComputePassContext context,Texture texture,string tag) {
        int f=frame,w=texture.width,h=texture.height;string format=texture.graphicsFormat.ToString();pending++;
        var cmd=(CommandBuffer)wrapped.GetValue(context.cmd);
        cmd.RequestAsyncReadback(texture,request=>{
            try{
                if(request.hasError)throw new InvalidOperationException("Cold readback failed: "+tag);
                byte[] bytes=request.GetData<byte>().ToArray();string path=output+"/capture/"+f.ToString("D4")+"-cold-"+tag+".bin";
                payloadBytes+=bytes.Length;if(payloadBytes>payloadBudget)throw new InvalidOperationException("Cold payload budget exceeded");
                if(System.IO.File.Exists(path))throw new InvalidOperationException("Duplicate cold record: "+path);
                System.IO.File.WriteAllBytes(path,bytes);
                records.Add(new{frame=f,tag="cold-"+tag,side="source",width=w,height=h,format,path,saved=true,sha256=Hash(bytes)});
            }catch(Exception e){errors.Add(e.Message);}finally{pending--;}
        });
    }
    void CaptureColdResolve(ComputePassContext context,Texture native) {
        ReadCold(context,native,"resolved");
        foreach(var name in new[]{"m_Geometry","m_AdaptivePlacement","m_HitLighting","m_Probes","m_FilteredProbes","m_PreparedAnchor","m_ReconstructedAnchor","m_CurrentAnchorStatistics",
            "m_PreviousGeometry","m_PreviousProbes","m_PreviousAnchorStatistics","m_CurrentDirectional","m_DirectionalTemporal","m_PreviousPixelGeometry",
            "m_DirectionalGuideTexture","m_PreviousDirectionalFiltered","m_CurrentDirectionalFiltered","m_DirectionalRayTexture","m_ScreenRays",
            "m_PreviousScreenRadiance","m_PreviousScreenGeometry","m_ScreenMotion"})
            if((name!="m_ScreenRays" && name!="m_PreviousScreenRadiance" && name!="m_PreviousScreenGeometry") || (bool)PrivateField("m_HasScreenHistory"))ReadCold(context,QualityResolve(name),name);
        if(frame==0){
            ReadCold(context,QualityResolve("m_GBuffer0"),"gbuffer0");ReadCold(context,QualityResolve("m_GBuffer1"),"gbuffer1");
            var cmd=context.cmd;var raw=(CommandBuffer)wrapped.GetValue(cmd);int kernel=qualityDepthCopy.FindKernel("CopyDepthCS");
            raw.SetComputeTextureParam(qualityDepthCopy,kernel,"_Source",QualityResolve("m_Depth"));raw.SetComputeTextureParam(qualityDepthCopy,kernel,"_Destination",qualityDepth);
            cmd.DispatchCompute(qualityDepthCopy,kernel,240,135,1);ReadCold(context,qualityDepth,"depth");
            raw.SetComputeTextureParam(coldDecode,0,"_Source",QualityResolve("m_GBuffer1"));raw.SetComputeTextureParam(coldDecode,0,"_Destination",coldDecoded);
            cmd.DispatchCompute(coldDecode,0,240,135,1);ReadCold(context,coldDecoded,"gbuffer1-decoded");
        }
        int[] Bits(Matrix4x4 m)=>Enumerable.Range(0,16).Select(i=>BitConverter.SingleToInt32Bits(m[i])).ToArray();
        var state=PrivateField("m_State");
        records.Add(new{frame,coldConstants=true,
            inverseVp=Bits((Matrix4x4)PrivateField("m_InverseViewProjection")),vp=Bits((Matrix4x4)PrivateField("m_ViewProjection")),
            previousVp=Bits((Matrix4x4)state.GetType().GetField("ViewProjection",inst|System.Reflection.BindingFlags.Public).GetValue(state)),
            cameraForward=new[]{((Vector4)PrivateField("m_ProbeCameraForward")).x,((Vector4)PrivateField("m_ProbeCameraForward")).y,((Vector4)PrivateField("m_ProbeCameraForward")).z,((Vector4)PrivateField("m_ProbeCameraForward")).w}.Select(BitConverter.SingleToInt32Bits).ToArray(),
            frameOverride=hooks["EditorGatherFrameOverride"].GetValue(null),
            realtimeOnly=PrivateField("m_RealtimeOnly"),adaptive=PrivateField("m_AdaptiveProbes"),restirDense=PrivateField("m_RestirPixelIntegration"),
            nearOcclusion=Enumerable.Range(0,4).Select(i=>((Vector4)PrivateField("m_NearOcclusion"))[i]).ToArray(),
            history=PrivateField("m_HasHistory"),guideValid=PrivateField("m_DirectionalGuideHistoryValid"),screenHistory=PrivateField("m_HasScreenHistory")});
    }
'''


def generate(output,before,frames=4,sequence=1733,probe_frame=None,prefix=False,witness=False,coordinate_precise=False,anchor_precise=False,resolve_precise=False,pixel_precise=False,filter_precise=False,prefix_frames=3,dense=False,fingerprints=False,receiver_precise=False):
    assert not anchor_precise or coordinate_precise
    assert not filter_precise or coordinate_precise
    source,shaders=build_capture(output,before,sequence)
    if receiver_precise:
        import re
        import posixpath
        common=(Path(__file__).resolve().parents[2]/'Shaders/Core/Private/GlobalIllumination/SdfScreenProbeCommon.hlsl').read_text()
        start=common.index('bool LoadReceiver(')
        end=common.rfind('\n',start,common.index('float PackProbeNormal(',start))
        block=common[start:end]
        old='    position = ComputeWorldSpacePosition(uv, depth, _SdfDiffuseInverseVP);'
        assert block.count(old)==1
        if 'VIVID_SDF_PRECISE_RECEIVER' not in block:
            block=block.replace('    float2 uv =','    precise float2 uv =',1).replace(old,'''    precise float4 clipPosition = float4(uv * 2.0 - 1.0, depth, 1.0);
#if UNITY_UV_STARTS_AT_TOP
    clipPosition.y = -clipPosition.y;
#endif
    precise float4 homogeneousPosition = mul(_SdfDiffuseInverseVP, clipPosition);
    precise float3 receiverPosition = homogeneousPosition.xyz / homogeneousPosition.w;
    position = receiverPosition;''',1)
        common=common[:start]+block+common[end:]
        common=re.sub(r'#include "([^"]+)"',lambda m:'#include "'+(m[1] if m[1].startswith('Packages/') else 'Packages/com.vivid.render-pipelines/'+posixpath.normpath('Shaders/Core/Private/GlobalIllumination/'+m[1]))+'"',common)
        marker='#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfScreenProbeCommon.hlsl"'
        assert shaders['candidate-tracing.compute.txt'].count(marker)==1
        shaders['candidate-tracing.compute.txt']=shaders['candidate-tracing.compute.txt'].replace(marker,'#ifndef VIVID_SDF_PRECISE_RECEIVER\n#define VIVID_SDF_PRECISE_RECEIVER 1\n#endif\n'+common,1)
    source=source.replace('    void Begin(ScriptableRenderContext context, Camera current) {','''    void Begin(ScriptableRenderContext context, Camera current) {
        if(active){if(current==camera)coldTargetCameras++;else{coldOtherCameras++;if(coldOtherCameras<=8)errors.Add("Unexpected capture camera: "+current.name+" / "+current.cameraType);}}
''',1)
    source=source.replace('epochReset,epochInvalidated, complete,','epochReset,epochInvalidated,coldOtherCameras,coldTargetCameras, complete,',1)
    for shader_name in (['quadrature-gather.compute.txt'] if resolve_precise else [])+(['temporal.compute.txt'] if pixel_precise else []):
        import re
        text=shaders[shader_name]
        # Diagnostic precision ablation; no ray counts or reconstruction weights change.
        text=re.sub(r'(?m)^(\s+)(float[234]?)(\s+\w)',r'\1precise \2\3',text)
        text=re.sub(r'(?m)^(float[234]?)(\s+\w+\()',r'precise \1\2',text)
        shaders[shader_name]=text
    substitutions={
        'const int warmup = 1024, count = 128;':f'const int warmup = 0, count = {frames};',
        'blockFrames=64,blocks=2':f'blockFrames={frames},blocks=1',
        'blockCounts.Any(n=>n!=64)':f'blockCounts.Any(n=>n!={frames})',
        '(count/64)*pixels':'(count/blockFrames)*pixels',
        '(index/64)*pixels':'(index/blockFrames)*pixels',
        'componentAaPairs!=count+4':'componentAaPairs!=count',
        'privateAaPairs!=(count+4)*25':'privateAaPairs!=count*25',
        '    void SetupQuality() {':HELPER+'''\n    void SetupQuality() {
        coldDecode=PrivateShader("cold-decode.compute.txt",new[]{"CopyCS"});
        coldDecoded=new RenderTexture(new RenderTextureDescriptor(1920,1080){graphicsFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat,enableRandomWrite=true});
        if(!coldDecoded.Create())throw new Exception("Cold decode allocation failed");''',
        '            if(qualityDepthCopy!=null)UnityEngine.Object.DestroyImmediate(qualityDepthCopy);':
        '            if(coldDecode!=null)UnityEngine.Object.DestroyImmediate(coldDecode);if(coldDecoded!=null)UnityEngine.Object.DestroyImmediate(coldDecoded);\n            if(qualityDepthCopy!=null)UnityEngine.Object.DestroyImmediate(qualityDepthCopy);',
        'if(index==0){ReplayComponents(context,texture);':'if(index==0){CaptureColdResolve(context,texture);ReplayComponents(context,texture);',
    }
    for old,new in substitutions.items():
        if probe_frame is not None and old in ('const int warmup = 1024, count = 128;','blockFrames=64,blocks=2','blockCounts.Any(n=>n!=64)',
            '(count/64)*pixels','(index/64)*pixels','componentAaPairs!=count+4','privateAaPairs!=(count+4)*25'):
            continue
        if probe_frame is not None and old=='if(index==0){ReplayComponents(context,texture);':
            new=f'if(index==0){{if(frame=={probe_frame})CaptureColdResolve(context,texture);ReplayComponents(context,texture);'
        assert source.count(old)==1,old
        source=source.replace(old,new,1)
    if probe_frame is not None:
        source=source.replace('if(frame==0){\n            ReadCold',f'if(frame=={probe_frame}){{\n            ReadCold',1)
    if prefix:
        assert probe_frame is not None and 3<=prefix_frames<=1024
        aa_frames=3+max(0,prefix_frames-1023)
        replacements={
            '++frame >= warmup + count':f'++frame >= {prefix_frames}',
            '        if(blockCounts.Any(n=>n!=64))throw new InvalidOperationException("Incomplete aggregate blocks.");':'',
            '        WriteDoubles("block-sums.f64",sums);WriteDoubles("block-squares.f64",squares);':'',
            'componentAaPairs!=count+4':f'componentAaPairs!={aa_frames}',
            'privateAaPairs!=(count+4)*25':f'privateAaPairs!={aa_frames*25}',
            '        PublishQualityStatistics();':'',
            'schema = "screen-probe-black-environment-quality.v1"':'schema = "screen-probe-prefix-inputs.v1"',
            'complete, warmup, measuredFrames = count':f'complete, warmup=0, measuredFrames=0, renderedFrames={prefix_frames}',
        }
        for old,new in replacements.items():
            assert source.count(old)==1,old
            source=source.replace(old,new,1)
    if dense:
        assert prefix and 0<=probe_frame<prefix_frames
        source=source.replace(f'if(frame=={probe_frame})CaptureColdResolve(context,texture);','CaptureColdResolve(context,texture);',1)
        old='            if((name!="m_ScreenRays" && name!="m_PreviousScreenRadiance" && name!="m_PreviousScreenGeometry") || (bool)PrivateField("m_HasScreenHistory"))ReadCold(context,QualityResolve(name),name);'
        source=source.replace(old,'            if((frame>=2 || !name.StartsWith("m_Previous",StringComparison.Ordinal)) && ((name!="m_ScreenRays" && name!="m_PreviousScreenRadiance" && name!="m_PreviousScreenGeometry") || (bool)PrivateField("m_HasScreenHistory")))ReadCold(context,QualityResolve(name),name);',1)
        source=source.replace('        CaptureEpochFirstFrame(context);','        CaptureEpochFirstFrame(context);ReadCold(context,QualityResolve("m_Output"),"final");',1)
        source=source.replace('                payloadBytes+=bytes.Length;if(payloadBytes>payloadBudget)',f'                bool save=f=={probe_frame};if(save)payloadBytes+=bytes.Length;if(payloadBytes>payloadBudget)',1)
        source=source.replace('                System.IO.File.WriteAllBytes(path,bytes);','                if(save)System.IO.File.WriteAllBytes(path,bytes);else path=null;',1)
        source=source.replace('format,path,saved=true,sha256=Hash(bytes)','format,path,saved=save,sha256=Hash(bytes)',1)
    if witness or coordinate_precise:
        text=(Path(__file__).resolve().parents[2]/'Shaders/Core/Private/GlobalIllumination/SdfScreenProbeFiltering.compute').read_text()
        import re
        text=re.sub(r'#include "([^"/]+)"',r'#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/\1"',text)
        marker='    _SdfPreparedProbeAnchorOutput[id.xy] = RoundSdfAnchorHistory(anchor);'
        assert text.count(marker)==1
        witness_code='''
    // These twelve adaptive slots are required to be invalid in this fixture.
    if(id.y==68u && id.x<12u && geometry.w<0.0) {
        float4 words[12]={_SdfDiffusePreviousVP[0],_SdfDiffusePreviousVP[1],_SdfDiffusePreviousVP[2],_SdfDiffusePreviousVP[3],
            _SdfScreenPreviousInverseVP[0],_SdfScreenPreviousInverseVP[1],_SdfScreenPreviousInverseVP[2],_SdfScreenPreviousInverseVP[3],
            _SdfDiffuseScreenSize,_SdfProbeCameraForward,_SdfHistoryLightScale,
            float4(_SdfDiffuseHasHistory,_SdfAnchorStatisticsHistoryValid,_SdfProbeAnchorMaxAge,_SdfGatherHistoryDiagnostics)};
        _SdfPreparedProbeAnchorOutput[id.xy]=words[id.x];
    }
'''
        if witness:text=text.replace(marker,marker+witness_code,1)
        if coordinate_precise:
            import posixpath
            common=(Path(__file__).resolve().parents[2]/'Shaders/Core/Private/GlobalIllumination/SdfScreenProbeCommon.hlsl').read_text()
            start=common.index('float2 ProbeHistoryGridPosition(')
            end=common.rfind('\n',start,common.index('float4 SdfRoundToNearestHalf',start))
            block=common[start:end]
            block=block.replace('    float2 ','    precise float2 ')
            common=common[:start]+block+common[end:]
            old='    float2 uv = clip.xy / clip.w * 0.5 + 0.5;'
            if old in common:
                assert common.count(old)==1;common=common.replace(old,old.replace('float2','precise float2'),1)
            else:
                assert common.count(old.replace('float2','SDF_FILTER_PRECISE float2'))==1
            common=re.sub(r'#include "([^"]+)"',lambda m:'#include "'+(m[1] if m[1].startswith('Packages/') else 'Packages/com.vivid.render-pipelines/'+posixpath.normpath('Shaders/Core/Private/GlobalIllumination/'+m[1]))+'"',common)
            pos=text.index('#include ');text=text[:pos]+common+'\n'+text[pos:]
            text=text.replace('        float4 clip = mul(_SdfDiffusePreviousVP,','        precise float4 clip = mul(_SdfDiffusePreviousVP,').replace('    float4 clip = mul(_SdfDiffusePreviousVP,','    precise float4 clip = mul(_SdfDiffusePreviousVP,')
            if anchor_precise:
                marker=('precise ' if 'precise float4 AccumulateDirectionalProbeAnchor(' in text else '')+'float4 AccumulateDirectionalProbeAnchor('
                start=text.index(marker)
                end=text.index('\nprecise float AnchorConnectionWeight(' if '\nprecise float AnchorConnectionWeight(' in text else '\nfloat AnchorConnectionWeight(',start)
                block=text[start:end]
                block=re.sub(r'(?m)^(\s*)(float[234]?)(\s+\w)',r'\1precise \2\3',block)
                text=text[:start]+block+text[end:]
        if filter_precise:
            text=re.sub(r'(?m)^(\s+)(float[234]?)(\s+\w)',r'\1precise \2\3',text)
            text=re.sub(r'(?m)^(float[234]?)(\s+\w+\()',r'precise \1\2',text)
        shaders['cold-filter.compute.txt']=text
        helper='''    ComputeShader coldFilter=null,coldOriginalFilter=null;
    void SetupColdFilter(){coldOriginalFilter=(ComputeShader)type.GetField("m_FilterCompute",inst).GetValue(privatePass);
        coldFilter=PrivateShader("cold-filter.compute.txt",new[]{"ScreenProbePrepareAnchorCS","ScreenProbePrepareAnchorNoPixelHistoryCS"});type.GetField("m_FilterCompute",inst).SetValue(privatePass,coldFilter);}
    void CleanupColdFilter(){if(coldOriginalFilter!=null)type.GetField("m_FilterCompute",inst).SetValue(privatePass,coldOriginalFilter);if(coldFilter!=null)UnityEngine.Object.DestroyImmediate(coldFilter);}
'''
        source=source.replace('    ComputeShader coldDecode=null;',helper+'    ComputeShader coldDecode=null;',1)
        source=source.replace('SetupExperimentalTracing(privatePass);','SetupExperimentalTracing(privatePass);SetupColdFilter();',1)
        source=source.replace('CleanupExperimentalTracing();','CleanupExperimentalTracing();CleanupColdFilter();',1)
    if pixel_precise:
        helper='''    ComputeShader coldOriginalPixel=null;
    void SetupColdPixel(){coldOriginalPixel=(ComputeShader)type.GetField("m_GatherTemporalCompute",inst).GetValue(privatePass);type.GetField("m_GatherTemporalCompute",inst).SetValue(privatePass,privateTemporal);}
    void CleanupColdPixel(){if(coldOriginalPixel!=null)type.GetField("m_GatherTemporalCompute",inst).SetValue(privatePass,coldOriginalPixel);}
'''
        source=source.replace('    ComputeShader coldDecode=null;',helper+'    ComputeShader coldDecode=null;',1)
        source=source.replace('SetupExperimentalTracing(privatePass);','SetupExperimentalTracing(privatePass);SetupColdPixel();',1)
        source=source.replace('CleanupPrivateReplay();','CleanupColdPixel();CleanupPrivateReplay();',1)
    shaders['cold-decode.compute.txt']='''#pragma kernel CopyCS
Texture2D<float4> _Source;RWTexture2D<float4> _Destination;
[numthreads(8,8,1)]void CopyCS(uint3 id:SV_DispatchThreadID){if(id.x<1920&&id.y<1080)_Destination[id.xy]=_Source.Load(int3(id.xy,0));}
'''
    if fingerprints:
        assert not dense, 'GPU ledger is a separate observation protocol'
        from BuildScreenProbeGpuFingerprint import install
        source,shaders=install(source,shaders,prefix_frames if prefix else frames if probe_frame is None else 1152)
    output.mkdir(parents=True,exist_ok=False)
    (output/'capture-script.cs').write_text(source)
    for name,body in shaders.items():(output/name).write_text(body)
    (output/'targets.json').write_text(json.dumps(targets(),indent=2)+'\n')
    (output/'protocol.json').write_text(json.dumps(dict(schema='screen-probe-cold-replay.v1',frames=prefix_frames if prefix else frames if probe_frame is None else 1152,probeFrame=probe_frame,prefixOnly=prefix,dense=dense,fingerprints=fingerprints,witness=witness,coordinatePrecise=coordinate_precise,anchorPrecise=anchor_precise,resolvePrecise=resolve_precise,pixelPrecise=pixel_precise,filterPrecise=filter_precise,receiverPrecise=receiver_precise,cardCoverageCandidate=before is None,sequence=sequence,
        spacing=16,rays=8,sourceOrders=8,sourceSamples=64,sourceBatches=32,flags=0,payloadBudget=384*1024**2,
        note='Original card query, cold epoch after frozen source; full Resolve inputs and CPU matrix words. Diagnostic only.'),indent=2)+'\n')
    snapshot_capture(output,shaders)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output',type=Path)
    role=parser.add_mutually_exclusive_group(required=True)
    role.add_argument('--before',type=Path)
    role.add_argument('--card-candidate',action='store_true')
    parser.add_argument('--frames',type=int,choices=(4,8),default=4)
    parser.add_argument('--probe-frame',type=int)
    parser.add_argument('--prefix-frames',type=int,default=3)
    flags=('prefix','dense','fingerprints','witness','coordinate_precise','anchor_precise',
           'resolve_precise','pixel_precise','filter_precise','receiver_precise')
    for flag in flags:
        parser.add_argument('--'+flag.replace('_','-'),action='store_true')
    args=parser.parse_args()
    generate(args.output.resolve(),args.before,args.frames,probe_frame=args.probe_frame,
             prefix_frames=args.prefix_frames,**{flag:getattr(args,flag) for flag in flags})
