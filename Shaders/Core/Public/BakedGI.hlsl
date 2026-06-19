#ifndef VIVIDRP_BAKED_GI_INCLUDED
#define VIVIDRP_BAKED_GI_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/BuiltinData.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

StructuredBuffer<float4> _VividAmbientProbeData;

float3 VividSampleAmbientProbe(float3 normalWS)
{
    float3 normalizedNormalWS = SafeNormalize(normalWS);
    return SampleSH9(_VividAmbientProbeData, normalizedNormalWS);
}

float2 TransformVividLightmapUV(float2 lightmapUV)
{
    return lightmapUV * unity_LightmapST.xy + unity_LightmapST.zw;
}

float3 SampleVividBakedGI(float2 lightmapUV, float3 normalWS)
{
#if defined(LIGHTMAP_ON) && defined(DIRLIGHTMAP_COMBINED)
    const half4 transformCoords = half4(1.0, 1.0, 0.0, 0.0);
    return SampleDirectionalLightmap(
        TEXTURE2D_LIGHTMAP_ARGS(unity_Lightmap, samplerunity_Lightmap),
        TEXTURE2D_LIGHTMAP_ARGS(unity_LightmapInd, samplerunity_Lightmap),
        lightmapUV,
        transformCoords,
        SafeNormalize(normalWS),
        true);
#elif defined(LIGHTMAP_ON)
    const half4 transformCoords = half4(1.0, 1.0, 0.0, 0.0);
    return SampleSingleLightmap(
        TEXTURE2D_LIGHTMAP_ARGS(unity_Lightmap, samplerunity_Lightmap),
        lightmapUV,
        transformCoords,
        true);
#else
    return 0.0;
#endif
}

float HasVividBakedGI()
{
#if defined(LIGHTMAP_ON)
    return 1.0;
#else
    return 0.0;
#endif
}

float VividBuiltinDataIsLightmap()
{
#if defined(LIGHTMAP_ON)
    return 1.0;
#else
    return 0.0;
#endif
}

float4 SampleVividShadowMask(float2 lightmapUV, float3 positionWS)
{
#if defined(SHADOWS_SHADOWMASK)
#if defined(LIGHTMAP_ON)
    return SAMPLE_TEXTURE2D(unity_ShadowMask, samplerunity_ShadowMask, lightmapUV);
#elif defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
    return 1.0;
#else
    return unity_ProbesOcclusion;
#endif
#else
    return 1.0;
#endif
}

VividBuiltinData BuildVividBuiltinData(
    float3 bakeDiffuseLighting,
    float hasBakedGI,
    float2 lightmapUV,
    float3 positionWS)
{
    return CreateVividBuiltinData(
        bakeDiffuseLighting,
        hasBakedGI,
        VividBuiltinDataIsLightmap(),
        SampleVividShadowMask(lightmapUV, positionWS));
}

#endif
