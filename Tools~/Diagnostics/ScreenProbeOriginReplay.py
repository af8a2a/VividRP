"""Replay native bound Hybrid tracing before testing a smaller emission origin."""
from pathlib import Path

TRACE_KERNELS=('TraceSdfDiffuseProbes','TraceSdfDiffuseCachedProbes','TraceSdfDiffuseCachedSunProbes',
    'TraceSdfReceiverProbes','TraceSdfReceiverCachedProbes','TraceSdfReceiverCachedSunProbes',
    'TraceDiffuseScreenRays','TraceReceiverDiffuseScreenRays','GatherScreenDiffuseProbes')
REPLAY_KERNELS=('CaptureOriginReceiversCS','ClearOrigin4CS','ClearOrigin1CS','ClearOriginCountersCS','InspectOriginCS','InitializeOriginDirectionsCS','CompareOriginIncompleteCS')


ADAPTIVE_ORIGIN='''uint _SdfReceiverOriginAdaptive;
float3 SdfProbeEmissionOrigin(uint2 tile,uint2 screenSize,float3 position,float3 normal)
{
    float bias=min(_SdfDiffuseRayBias,_SdfOriginLimit);
    if(_SdfReceiverOriginAdaptive==0u)return position+normal*bias;
#if defined(VIVID_SDF_RECEIVER)
    uint2 pixel;
    if(LoadProbeReceiverPixel(tile,screenSize,pixel))
    {
        uint index=_SdfReceiverVisibility.Load(int3(pixel,0)).x-VIVID_VISIBILITY_BUFFER_INSTANCE_ID_BIAS;
        if(index<_SdfReceiverCount)
        {
            uint4 receiver=_SdfReceiverEntities[index];
            VividMeshSdfInstance instance=_MeshSdfInstances[receiver.z];
            VividMeshSdfField field=_MeshSdfFields[instance.fieldIndex];
            if(all(receiver.xy==instance.entityId) && (field.signMode==1u || field.signMode==2u))
            {
                float3 localNormal=float3(dot(instance.worldToLocal0.xyz,normal),dot(instance.worldToLocal1.xyz,normal),dot(instance.worldToLocal2.xyz,normal));
                float shell=field.signMode==2u?0.5*field.shellThickness:0.0;
                // Stay beyond the existing0.075-voxel hit band; do not skip any
                // scene geometry or enlarge the configured maximum origin bias.
                float safe=(shell+0.125*field.originAndVoxelSize.w)/max(length(localNormal),1e-6);
                bias=min(_SdfDiffuseRayBias,max(bias,safe));
                float3 candidate=position+normal*bias;
                float4 p=float4(candidate,1.0);
                float3 grid=float3(dot(instance.worldToLocal0,p),dot(instance.worldToLocal1,p),dot(instance.worldToLocal2,p))-field.originAndVoxelSize.xyz;
                float3 extent=float3(field.dimensions-1u)*field.originAndVoxelSize.w;
                if(any(grid<0.0) || any(grid>extent) || VividMeshSdfSample(field,grid)>0.075*field.originAndVoxelSize.w)
                    return candidate;
            }
        }
    }
#endif
    return position+normal*_SdfDiffuseRayBias;
}
'''


def tracing_source(adaptive=False,steps=192,bound_legacy=False):
    assert steps in (192,384,2048)
    assert not bound_legacy or (adaptive and steps==2048)
    folder=Path(__file__).parents[2]/'Shaders/Core/Private/GlobalIllumination'
    source=(folder/'SdfScreenProbeTracing.compute').read_text(encoding='utf-8-sig')
    before='float3 origin = position + normal * _SdfDiffuseRayBias;'
    assert source.count(before)==2
    source=source.replace(before,'float3 origin = position + normal * min(_SdfDiffuseRayBias, _SdfOriginLimit);')
    source=source.replace('uint _SdfScreenPreviousRadianceValid;','uint _SdfScreenPreviousRadianceValid;\nfloat _SdfOriginLimit;',1)
    for line in source.splitlines():
        if line.startswith('#include "'):
            name=line.split('"')[1]
            assert '/' not in name
            source=source.replace(line,'#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/'+name+'"',1)
    if adaptive:
        before='float3 origin = position + normal * min(_SdfDiffuseRayBias, _SdfOriginLimit);'
        assert source.count(before)==2
        source=source.replace(before,'float3 origin = SdfProbeEmissionOrigin(tile, screenSize, position, normal);')
        source=source.replace('float _SdfOriginLimit;','float _SdfOriginLimit;\n'+ADAPTIVE_ORIGIN,1)
    if steps!=192:
        assert adaptive
        body=(folder.parents[1]/'Public/GPUDriven/MeshSdfTrace.hlsl').read_text(encoding='utf-8-sig')
        limit='384' if steps==384 else 'uint(min(2048.0,max(192.0,ceil(length(float3(field.dimensions-1u))/(0.075*0.7))+1.0)))'
        expression=limit if bound_legacy else '(_SdfOriginLimit < 0.04 ? '+limit+' : 192)'
        for before in ('samples++ >= 192','step < 192'):
            assert body.count(before)==1
            body=body.replace(before,before.replace('192',expression),1)
        source=source.replace('float _SdfOriginLimit;','',1)
        index=source.index('#include ')
        source=source[:index]+'float _SdfOriginLimit;\n'+body+'\n'+source[index:]
    return source


def install(source,shaders,limit,adaptive=False,steps=192,bound_legacy=False):
    assert limit in (.04,.01)
    helper=Path(__file__).with_name('ScreenProbeOriginReplay.cs.txt').read_text(encoding='utf-8-sig').replace('__ORIGIN_LIMIT__',f'{limit:.6f}f').replace('__ORIGIN_ADAPTIVE__',str(adaptive).lower())
    split=helper.index('    void SetupOriginReplay(')
    replacements={
        '    string privateMethod=':helper[:split]+'\n    string privateMethod=',
        '    object PrivateField(string name)':helper[split:]+'\n    object PrivateField(string name)',
        'SetupOraclePixels();SetupEnergyMoments();':'SetupOraclePixels();SetupEnergyMoments();SetupOriginReplay();',
        '        if (active && current == camera) hooks["EditorGatherFrameOverride"].SetValue(null, frameBase + (phase == 1 ? frame : 0));':
            '        if(active && current==camera)BindOriginScreenBuffers();\n'
            '        if (active && current == camera) hooks["EditorGatherFrameOverride"].SetValue(null, frameBase + (phase == 1 ? frame : 0));',
        'if(index==0){ReplayComponents(context,texture);':'if(index==0){ReplayOriginTrace(context);ReplayComponents(context,texture);',
        'privatePrograms,oraclePrograms,privateAaPairs,':'privatePrograms,oraclePrograms,originReplayPrograms,originAaPairs,originInitialized,originNativeLimit,privateAaPairs,',
        '384L*1024*1024':'512L*1024*1024',
        'CleanupEnergyMoments();oraclePixelMap?.Dispose();':'originReceivers?.Dispose();originCounters?.Dispose();CleanupEnergyMoments();oraclePixelMap?.Dispose();',
        '        PublishQualityStatistics();':'        if(originPendingPairs.Count!=0)throw new InvalidOperationException("Unpaired origin replay readbacks.");\n        PublishQualityStatistics();',
        'if(!EditorUtility.IsPersistent((ComputeShader)type.GetField("m_TraceCompute",inst).GetValue(qualityPass)))throw new InvalidOperationException("Original persistent trace required.");':
            'if(!EditorUtility.IsPersistent(experimentalOriginalTrace) || !object.ReferenceEquals(type.GetField("m_TraceCompute",inst).GetValue(qualityPass),experimentalTrace))throw new InvalidOperationException("Origin tracing owner or persistent provenance changed.");',
    }
    for before,after in replacements.items():
        assert source.count(before)==1,before[:100]
        source=source.replace(before,after,1)
    shaders['candidate-tracing.compute.txt']=tracing_source(adaptive,steps,bound_legacy)
    shaders['origin-replay.compute.txt']=Path(__file__).with_name('ScreenProbeOriginReplay.compute.txt').read_text(encoding='utf-8-sig')
    return source,shaders
