"""Test stratified Resolve through independent pixel histories and native wavelets."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeQuadrature import build as quadrature_build, shader_source
from BuildScreenProbeSameFrameAtrous import shader_source as wavelet_source
from BuildScreenProbeVarianceHistory import temporal_source


def build(output, sequence=1733, motion=False):
    assert 0 <= sequence < 2**31
    folder = Path(__file__).resolve().parent
    workspace = folder.parents[1]
    source = quadrature_build(output)
    helper = (folder / 'ScreenProbeColorHistoryReplay.cs.txt').read_text(encoding='utf-8-sig')
    helper = helper.replace('__METHOD__', 'none').replace('__FEEDBACK_WITNESS__', 'false')
    marker = '        if(texture==null)throw new InvalidOperationException("Missing private replay resource: "+role);'
    assert helper.count(marker) == 1
    helper = helper.replace(marker, '        if(frame< warmup && frame>2 && frame!=warmup-1)return;\n' + marker, 1)
    marker = 'raw.SetComputeTextureParam(shader,kernel,"_SdfDiffuseCurrentResolved",PrivateResolve("m_RawResolved"));'
    assert helper.count(marker) == 1
    helper = helper.replace(marker, 'raw.SetComputeTextureParam(shader,kernel,"_SdfDiffuseCurrentResolved",path==0?PrivateResolve("m_RawResolved"):quadratureNormalized);', 1)

    def replace(old, new):
        nonlocal source
        assert source.count(old) == 1, old[:100]
        source = source.replace(old, new, 1)

    replace('"EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorGatherFrameOverride",',
            '"EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorAtrousDispatchCapture", "EditorGatherFrameOverride",')
    replace('192L*1024*1024', '320L*1024*1024')
    normalization = '''    ComputeShader quadratureNormalize=null;
    RenderTexture quadratureNormalized=null;
    void SetupQuadratureNormalization() {
        quadratureNormalize=PrivateShader("normalize-alpha.compute.txt",new[]{"NormalizeQuadratureCoverageCS"});
        quadratureNormalized=PrivateTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
    }
    void NormalizeQuadrature(ComputePassContext context) {
        if(componentPre==null)throw new InvalidOperationException("Quadrature Resolve did not execute.");
        int kernel=quadratureNormalize.FindKernel("NormalizeQuadratureCoverageCS");
        var cmd=context.cmd;var raw=(CommandBuffer)wrapped.GetValue(cmd);
        raw.SetComputeTextureParam(quadratureNormalize,kernel,"_SignedInput",componentPre);
        raw.SetComputeTextureParam(quadratureNormalize,kernel,"_PositiveOutput",quadratureNormalized);
        cmd.DispatchCompute(quadratureNormalize,kernel,240,135,1);
    }
'''
    replace('    void SetupQuality() {', normalization + helper + '\n    void SetupQuality() {')
    replace('SetupQuality();SetupComponents();SetupQuadrature();',
            'SetupQuality();SetupComponents();SetupQuadrature();SetupPrivateReplay();SetupQuadratureNormalization();')
    replace('            CleanupQuadrature();CleanupComponents();',
            '            CleanupPrivateReplay();CleanupQuadrature();CleanupComponents();')
    replace('        int f=frame;pending++;',
            '        int f=frame;if(f<warmup && f>2 && f!=warmup-1)return;pending++;')
    replace('else ReadQuality(context,mode.output,"quadrature","pixel");',
            'else if(frame>=warmup)ReadQuality(context,mode.output,"quadrature","pixel");')
    replace('if(!active || phase!=1 || frame<warmup || context.Get<VividCameraData>().camera!=camera)return;',
            '''if(!active || phase!=1 || context.Get<VividCameraData>().camera!=camera)return;
        if(index==0){ReplayComponents(context,texture);NormalizeQuadrature(context);}
        if(index==1)ReplayPrivateTemporal(context);
        if(frame<warmup)return;''')
    replace('if(index==0){ReadQuality(context,texture,"resolved","pixel");ReplayComponents(context,texture);}',
            'if(index==0)ReadQuality(context,texture,"resolved","pixel");')
    replace('hooks["EditorGatherStageCapture"].SetValue(null,qualityStage);',
            'hooks["EditorGatherStageCapture"].SetValue(null,qualityStage);hooks["EditorAtrousDispatchCapture"].SetValue(null,privateWaveletHook);')
    replace('componentAaPairs!=count', 'componentAaPairs!=count+4')
    replace('        PublishQualityStatistics();',
            '''        if(privatePendingPairs.Count!=0 || privateAaPairs!=(count+4)*25)throw new InvalidOperationException("Incomplete private history AA.");
        PublishQualityStatistics();''')
    replace('componentAaPairs,quadraturePrograms,',
            'componentAaPairs,quadraturePrograms,privatePrograms,privateAaPairs,privateInitialized,')
    replace('await Run("capture",1733);', f'await Run("capture",{sequence});')
    if motion:
        replace('320L*1024*1024', '384L*1024*1024')
        replace('    var volume = roots.SelectMany',
                '    var originalPose=(camera.transform.position,camera.transform.rotation);\n    var volume = roots.SelectMany')
        replace('var privateRois=new[]{("ceiling",912,970,96,48),("floor",912,130,96,64),("back",1100,600,96,64),("edge",880,414,160,128)};',
                'var privateRois=new[]{("edge",880,414,160,128)};')
        replace('privateAaPairs!=(count+4)*25', 'privateAaPairs!=(count+4)*10')
        marker='        if (active && current == camera) hooks["EditorGatherFrameOverride"].SetValue(null, frameBase + (phase == 1 ? frame : 0));'
        replace(marker,marker+'''
        if(active && current==camera) {
            float t=phase==1 && frame>=warmup?Mathf.Sin(2f*Mathf.PI*(frame-warmup)/128f):0f;
            camera.transform.SetPositionAndRotation(originalPose.Item1+originalPose.Item2*new Vector3(.28f*t,0,.06f*t),
                originalPose.Item2*Quaternion.Euler(0,-7f*t,0));
        }''')
        replace('        foreach(var item in new[]{("temporal",PrivateResolve', '''        if(frame>=warmup) {
            ReadPrivate(context,PrivateResolve("m_RawResolved"),"resolved","source",false,true);
            ReadPrivate(context,quadratureNormalized,"resolved","candidate",false,true);
        }
        foreach(var item in new[]{("temporal",PrivateResolve''')
        replace('path==0,frame==0 || frame==warmup || frame==warmup+count-1);',
                'path==0,frame==0 || (frame>=warmup && item.Item1!="moments"));')
        replace('            CleanupPrivateReplay();CleanupQuadrature();CleanupComponents();',
                '''            camera.transform.SetPositionAndRotation(originalPose.Item1,originalPose.Item2);
            CleanupPrivateReplay();CleanupQuadrature();CleanupComponents();''')
    shaders = {
        'quadrature-gather.compute.txt': shader_source(),
        'copy-irradiance.compute.txt': (folder/'SdfCopyIrradiancePlanes.compute.txt').read_text(encoding='utf-8-sig'),
        'temporal.compute.txt': temporal_source((workspace/'Shaders/Core/Private/GlobalIllumination/SdfScreenProbeGatherTemporal.compute').read_text(encoding='utf-8-sig')),
        'wavelet.compute.txt': wavelet_source((workspace/'Shaders/Core/Private/GlobalIllumination/SdfDiffuseAtrous.hlsl').read_text(encoding='utf-8-sig')),
        'normalize-alpha.compute.txt': '''#pragma kernel NormalizeQuadratureCoverageCS
Texture2D<float4> _SignedInput;RWTexture2D<float4> _PositiveOutput;
[numthreads(8,8,1)]void NormalizeQuadratureCoverageCS(uint3 id:SV_DispatchThreadID){
if(any(id.xy>=uint2(1920,1080)))return;
float4 value=_SignedInput.Load(int3(id.xy,0));_PositiveOutput[id.xy]=float4(value.rgb,abs(value.a));}
''',
        'depth-copy.compute.txt': '''#pragma kernel CopyDepthCS
Texture2D<float> _Source;RWTexture2D<float> _Destination;
[numthreads(8,8,1)]void CopyDepthCS(uint3 id:SV_DispatchThreadID){if(any(id.xy>=uint2(1920,1080)))return;_Destination[id.xy]=_Source.Load(int3(id.xy,0));}
'''}
    return source, shaders


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    parser.add_argument('--sequence', type=int, default=1733)
    parser.add_argument('--motion', action='store_true')
    args = parser.parse_args()
    output = args.output.resolve()
    source, shaders = build(output,args.sequence,args.motion)
    output.mkdir(parents=True, exist_ok=False)
    (output/'capture-script.cs').write_text(source, encoding='utf-8')
    for name, body in shaders.items():
        (output/name).write_text(body, encoding='utf-8')
    protocol = dict(schema='screen-probe-quadrature-history-protocol.v1', corrected=False, guided=True,
        sequence=args.sequence, warmup=1024, measuredFrames=128, spacing=16, rays=8, payloadBudget=(384 if args.motion else 320)*1024**2,
        historyEpoch=True, finalStatistics=True, quadrature=True, independentPixelHistories=True,
        motion=args.motion, regions=['edge'] if args.motion else ['ceiling','floor','back','edge'],
        aaFrames=[0,1,2,1023,*range(1024,1152)],
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in output.iterdir()},
        gates=dict(nativeCopiedResolveByteExact=True, nativePrivateHistoryByteExact=True,
            meanChangeAbsPercent=1, ceilingVarianceReductionPercent=5, otherVarianceIncreasePercent=2,
            frameDifferenceRmsIncreasePercent=2,
            independentSequenceRequired=True, motionRequired=True, energyReferenceRequired=True, gpuRequired=True),
        note='Native baseline uses prototype Gather with flag off; shared native probe/transport input. Private candidate normalizes eligibility alpha before independent temporal histories and four original wavelet passes. Full-image and ROI AA on all measured frames plus four key warmup frames. No renderer history writes or production promotion.')
    if args.motion:
        protocol.update(trajectory=dict(period=128,lateral=.28,forward=.06,yawDegrees=-7),motionRegion=[880,414,160,128])
        protocol['gates'].update(disocclusionP99IncreasePercent=5,disocclusionMaxIncreasePercent=5,newlyExposedOldAgeFractionIncrease=.01)
    (output/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n', encoding='utf-8')
    print(json.dumps(dict(root=str(output))))
