"""Owned quality experiment: symmetric, conservative consumed-energy wavelets.

Native Editor hooks overwrite each wavelet result before its next consumer. The
original dispatch still executes, so this experiment cannot qualify GPU cost.
"""
from pathlib import Path

from ScreenProbeEnergyMoments import wavelet_source


FILTER = r'''
Texture2D<float4> _SdfFluxGuides;
RWTexture2D<float4> _SdfFluxGuidesOutput;

[numthreads(8,8,1)]
void PrepareScreenProbeFluxCS(uint3 id:SV_DispatchThreadID)
{
    uint2 pixel=id.xy;
    int2 size=int2(_SdfDiffuseScreenSize.xy);
    if(any(int2(pixel)>=size))return;
    _SdfFluxGuidesOutput[pixel]=0;
    int2 centerPixel=int2(pixel);
    float4 center=LoadSdfAtrousLighting(centerPixel);
    SdfAtrousGuide guide=LoadSdfAtrousGuide(centerPixel,size);
    if(abs(center.a)<=0.0 || !guide.valid)return;
__PREPARE_VARIANCE__
    bool allowed=true;
    if(_SdfAtrousStep>=4u)
    {
        float depthTolerance=lerp(0.006*abs(guide.depth),0.0002,unity_OrthoParams.w)+1e-6;
        [loop] for(int y=-2;y<=2;++y)
        [loop] for(int x=-2;x<=2;++x)
        {
            int2 offset=int2(x,y)*int(_SdfAtrousStep);
            int2 tapPixel=centerPixel+offset;
            if(any(tapPixel<0) || any(tapPixel>=size))continue;
            SdfAtrousGuide tap=LoadSdfAtrousGuide(tapPixel,size);
            float planeError=abs(tap.depth-guide.depth-dot(gradient,float2(offset)))/depthTolerance;
            if(SdfAtrousNormalAgreement(guide,tap)<=0.8 || planeError>=3.0)allowed=false;
        }
    }
    _SdfFluxGuidesOutput[pixel]=float4(variance,allowed?1.0:0.0,centerLuma,lumaFloor);
}

void FilterScreenProbeFluxPixel(uint2 pixel)
{
    int2 size=int2(_SdfDiffuseScreenSize.xy);
    if(any(int2(pixel)>=size))return;
    int2 centerPixel=int2(pixel);
    float4 center=LoadSdfAtrousLighting(centerPixel);
    float4 summary=_SdfFluxGuides.Load(int3(centerPixel,0));
    float filteredVariance=summary.x;
    if(summary.y>0.0)
    {
        SdfAtrousGuide guide=LoadSdfAtrousGuide(centerPixel,size);
        float3 centerPosition=ComputeWorldSpacePosition((float2(centerPixel)+0.5)/float2(size),guide.depth,_SdfDiffuseInverseVP);
        float centerEye=LinearEyeDepth(guide.depth,_ZBufferParams);
        float3 centerEnergy=center.rgb*abs(center.a);
        float3 flux=0.0;
        float coefficientSum=0.0;
        float varianceNumerator=0.0;
        const float kernel[5]={1.0,4.0,6.0,4.0,1.0};
        [loop] for(int y=-2;y<=2;++y)
        [loop] for(int x=-2;x<=2;++x)
        {
            if(x==0 && y==0)continue;
            int2 tapPixel=centerPixel+int2(x,y)*int(_SdfAtrousStep);
            if(any(tapPixel<0) || any(tapPixel>=size))continue;
            float4 tapSummary=_SdfFluxGuides.Load(int3(tapPixel,0));
            if(tapSummary.y<=0.0)continue;
            SdfAtrousGuide tap=LoadSdfAtrousGuide(tapPixel,size);
            float normalAgreement=SdfAtrousNormalAgreement(guide,tap);
            if(normalAgreement<=0.8)continue;
            float4 lighting=LoadSdfAtrousLighting(tapPixel);
            if(abs(lighting.a)<=0.0 || (lighting.a<0.0)!=(center.a<0.0))continue;
            float3 tapPosition=ComputeWorldSpacePosition((float2(tapPixel)+0.5)/float2(size),tap.depth,_SdfDiffuseInverseVP);
            float3 delta=tapPosition-centerPosition;
            // Both endpoints use the same plane distance, uncertainty, coverage
            // confidence and binomial coefficient. No row-wise renormalization.
            float tolerance=clamp(0.006*min(centerEye,LinearEyeDepth(tap.depth,_ZBufferParams)),0.02,0.10);
            float planeError=max(abs(dot(delta,guide.normal)),abs(dot(delta,tap.normal)))/tolerance;
            if(planeError>=3.0)continue;
            float3 materialDelta=tap.material.rgb-guide.material.rgb;
            float lumaScale=rcp(max(summary.w,tapSummary.w)+2.5*sqrt(max(summary.x,tapSummary.x)));
            float coverage=min(abs(center.a),abs(lighting.a))/max(max(abs(center.a),abs(lighting.a)),1e-4);
            float coefficient=(kernel[x+2]*kernel[y+2]/256.0)*coverage
                *PositivePow(normalAgreement,16.0)
                *exp2(-planeError*planeError-16.0*dot(materialDelta,materialDelta)-lumaScale*abs(summary.z-tapSummary.z));
            flux+=coefficient*(lighting.rgb*abs(lighting.a)-centerEnergy);
            coefficientSum+=coefficient;
            varianceNumerator+=coefficient*coefficient*tapSummary.x;
        }
        center.rgb=min(max(centerEnergy+flux,0.0)/abs(center.a),65000.0);
        filteredVariance=max(0.25*summary.x,(1.0-coefficientSum)*(1.0-coefficientSum)*summary.x+varianceNumerator);
    }
#if defined(VIVID_SDF_ATROUS_FINAL)
    center.a=abs(center.a);
#else
    _SdfAtrousVarianceOutput[pixel]=filteredVariance;
#endif
    // Explicit RNE makes the permitted mass error measurable from half ULPs.
    _SdfDiffuseOutput[pixel]=SdfRoundToNearestHalf(center);
}

[numthreads(8,8,1)]
#if defined(VIVID_SDF_ATROUS_FINAL)
void FilterScreenProbeFluxFinalCS(uint3 id:SV_DispatchThreadID)
#else
void FilterScreenProbeFluxCS(uint3 id:SV_DispatchThreadID)
#endif
{
    FilterScreenProbeFluxPixel(id.xy);
}
'''


def shader_source():
    body=wavelet_source((Path(__file__).parents[2]/'Shaders/Core/Private/GlobalIllumination/SdfDiffuseAtrous.hlsl').read_text(encoding='utf-8-sig'))
    helpers=body[:body.index('void FilterSdfDiffuseAtrousPixel(')]
    start=body.index('        float centerLuma =')
    end=body.index('        float lumaScale =',start)
    prepare=body[start:end]
    prefix='''#pragma kernel PrepareScreenProbeFluxCS VIVID_SDF_ATROUS
#pragma kernel FilterScreenProbeFluxCS VIVID_SDF_ATROUS
#pragma kernel FilterScreenProbeFluxFinalCS VIVID_SDF_ATROUS VIVID_SDF_ATROUS_FINAL
'''
    for name in ('SdfScreenProbeCommon.hlsl','SdfScreenProbeTracingCommon.hlsl','SdfShortRangeAOCommon.hlsl','SdfDiffuseReSTIR.hlsl'):
        prefix+='#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/'+name+'"\n'
    return prefix+helpers+FILTER.replace('__PREPARE_VARIANCE__',prepare)+'\n#endif\n'


def install(source,shaders):
    shaders['flux-wavelet.compute.txt']=shader_source()
    helper=Path(__file__).with_name('ScreenProbeConservativeFlux.cs.txt').read_text(encoding='utf-8-sig')
    split=helper.index('    void SetupFlux(')
    replacements={
        '    string privateMethod=':helper[:split]+'\n    string privateMethod=',
        '    object PrivateField(string name)':helper[split:]+'\n    object PrivateField(string name)',
        'SetupOraclePixels();SetupEnergyMoments();':'SetupOraclePixels();SetupEnergyMoments();SetupFlux();',
        'privatePrograms,oraclePrograms,privateAaPairs,':'privatePrograms,oraclePrograms,fluxPrograms,fluxMass,privateAaPairs,',
        'if(!active || phase!=1 || index!=3 || context.Get<VividCameraData>().camera!=camera)return;':
            'if(!active || phase!=1 || context.Get<VividCameraData>().camera!=camera)return;\n'
            '        RecordFlux(context,index,input,result,variance,varianceOutput,PrivateResolve("m_CurrentScreenProbeMoments"),true);\n'
            '        if(index!=3)return;',
        'cmd.DispatchCompute(waveletShader,kernel,240,135,1);input=privateWaveColors[slot];variance=privateWaveVariance[slot];':
            'if(path==1)RecordFlux(context,iteration,input,privateWaveColors[slot],variance,last?null:privateWaveVariance[slot],privateMoments[1,frame&1],false);\n'
            '            else cmd.DispatchCompute(waveletShader,kernel,240,135,1);\n'
            '            input=privateWaveColors[slot];variance=privateWaveVariance[slot];',
    }
    for before,after in replacements.items():
        assert source.count(before)==1,before[:100]
        source=source.replace(before,after,1)
    return source,shaders
