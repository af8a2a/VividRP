#ifndef VIVIDRP_BAKED_GI_INCLUDED
#define VIVIDRP_BAKED_GI_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
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

#endif
