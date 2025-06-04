#ifndef PER_OBJECT_SHADOWS_INCLUDED
#define PER_OBJECT_SHADOWS_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingDisk.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension//Shadow/ShadowsPCSS.hlsl"
float4 _PerObjectShadowmapTexture_TexelSize;
TEXTURE2D_SHADOW(_PerObjectShadowmapTexture);
TEXTURE2D_X(_PerObjectScreenSpaceShadowmapTexture);

// x: softShadowQuality
// y: shadowStrength
// z: downSampleScale screenspacePerObjectShadowmap 0:none 1:2x 2:4x
// w: penumbra
float4 _PerObjectShadowParams;

float4 TransformWorldToPerObjectShadowCoord(float3 positionWS, float4x4 worldToShadowMatrix)
{
    float4 shadowCoord = mul(worldToShadowMatrix, float4(positionWS, 1.0));
    return float4(shadowCoord.xyz, 0);
}

float GetPerObjectShadowPenumbra()
{
    return _PerObjectShadowParams.w;
}

real SamplePerObjectShadowmapFiltered(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord, float4 shadowMapTexelSize,
                                      float softShadowQuality)
{
    real attenuation = real(1.0);

    if (softShadowQuality == SOFT_SHADOW_QUALITY_LOW)
    {
        real fetchesWeights[4];
        real2 fetchesUV[4];
        SampleShadow_ComputeSamples_Tent_3x3(shadowMapTexelSize, shadowCoord.xy, fetchesWeights, fetchesUV);
        attenuation = fetchesWeights[0] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[0].xy, shadowCoord.z))
            + fetchesWeights[1] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[1].xy, shadowCoord.z))
            + fetchesWeights[2] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[2].xy, shadowCoord.z))
            + fetchesWeights[3] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[3].xy, shadowCoord.z));
    }
    else if (softShadowQuality == SOFT_SHADOW_QUALITY_MEDIUM)
    {
        real fetchesWeights[9];
        real2 fetchesUV[9];
        SampleShadow_ComputeSamples_Tent_5x5(shadowMapTexelSize, shadowCoord.xy, fetchesWeights, fetchesUV);

        attenuation = fetchesWeights[0] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[0].xy, shadowCoord.z))
            + fetchesWeights[1] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[1].xy, shadowCoord.z))
            + fetchesWeights[2] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[2].xy, shadowCoord.z))
            + fetchesWeights[3] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[3].xy, shadowCoord.z))
            + fetchesWeights[4] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[4].xy, shadowCoord.z))
            + fetchesWeights[5] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[5].xy, shadowCoord.z))
            + fetchesWeights[6] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[6].xy, shadowCoord.z))
            + fetchesWeights[7] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[7].xy, shadowCoord.z))
            + fetchesWeights[8] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[8].xy, shadowCoord.z));
    }
    else // SOFT_SHADOW_QUALITY_HIGH
    {
        real fetchesWeights[16];
        real2 fetchesUV[16];
        SampleShadow_ComputeSamples_Tent_7x7(shadowMapTexelSize, shadowCoord.xy, fetchesWeights, fetchesUV);

        attenuation = fetchesWeights[0] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[0].xy, shadowCoord.z))
            + fetchesWeights[1] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[1].xy, shadowCoord.z))
            + fetchesWeights[2] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[2].xy, shadowCoord.z))
            + fetchesWeights[3] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[3].xy, shadowCoord.z))
            + fetchesWeights[4] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[4].xy, shadowCoord.z))
            + fetchesWeights[5] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[5].xy, shadowCoord.z))
            + fetchesWeights[6] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[6].xy, shadowCoord.z))
            + fetchesWeights[7] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[7].xy, shadowCoord.z))
            + fetchesWeights[8] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[8].xy, shadowCoord.z))
            + fetchesWeights[9] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[9].xy, shadowCoord.z))
            + fetchesWeights[10] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[10].xy, shadowCoord.z))
            + fetchesWeights[11] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[11].xy, shadowCoord.z))
            + fetchesWeights[12] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[12].xy, shadowCoord.z))
            + fetchesWeights[13] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[13].xy, shadowCoord.z))
            + fetchesWeights[14] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[14].xy, shadowCoord.z))
            + fetchesWeights[15] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[15].xy, shadowCoord.z));
    }

    return attenuation;
}

float PerObjectShadowmapPCF(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord, float shadowMapTileInvSize, float2 minCoord,
                            float2 maxCoord, float sampleCount, float filterSize, float2 random,
                            float texelSizeWS, float farToNear, float blockerInvTangent)
{
    float numBlockers = 0.0;
    float totalSamples = 0.0;
    float sampleCountInverse = rcp((float)sampleCount);
    float sampleCountBias = 0.5 * sampleCountInverse;

    // Limitation:
    // Note that in cascade shadows, all occluders behind the near plane will get clamped to the near plane
    // This will lead to the closest blocker sometimes being reported as much closer to the receiver than it really is
    #if UNITY_REVERSED_Z
    #define Z_OFFSET_DIRECTION 1
    #else
    #define Z_OFFSET_DIRECTION (-1)
    #endif

    UNITY_LOOP
    for (int i = 0; i < sampleCount && i < DISK_SAMPLE_COUNT; i++)
    {
        float sampleDistNorm;
        float2 offset = 0.0;
        offset = ComputeFibonacciSpiralDiskSampleUniform_Directional(i, sampleCountInverse, sampleCountBias, sampleDistNorm);
        offset = float2(offset.x * random.y + offset.y * random.x,
                        offset.x * -random.x + offset.y * random.y);
        offset *= filterSize;
        offset *= shadowMapTileInvSize; // coord to uv

        float2 sampleCoord = shadowCoord.xy + offset;

        float radialOffset = filterSize * sampleDistNorm * texelSizeWS;
        float zoffset = radialOffset / farToNear * blockerInvTangent;

        float depthLS = shadowCoord.z;

        if (!(any(sampleCoord < minCoord) || any(sampleCoord > maxCoord)))
        {
            float shadowSample = SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(sampleCoord, depthLS));
            numBlockers += shadowSample;
            totalSamples++;
        }
    }

    return totalSamples > 0 ? numBlockers / totalSamples : 1.0;
}

real SamplePerObjectShadowmap(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord, float4 perObjectShadowParams)
{
    real attenuation;
    real shadowStrength = perObjectShadowParams.y;
    float softShadowQuality = perObjectShadowParams.x;
    #if (_SHADOWS_SOFT)
    if(softShadowQuality > SOFT_SHADOW_QUALITY_OFF)
    {
        attenuation = SamplePerObjectShadowmapFiltered(TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowCoord, _PerObjectShadowmapTexture_TexelSize, softShadowQuality);
    }
    else
    #endif
    {
        // 1-tap hardware comparison
        attenuation = real(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz));
    }

    attenuation = LerpWhiteTo(attenuation, shadowStrength);

    return attenuation;
}

// PerObjectShadowParams
// x: SoftShadowQuality
// y: shadowStrength
// TODO: set object independent shadowStrength
float4 GetPerObjectShadowParams()
{
    float4 params = _PerObjectShadowParams;
    return _PerObjectShadowParams;
}


float PerObjectRealtimeShadow(float4 shadowCoord)
{
    float4 perObjectShadowParams = GetPerObjectShadowParams();
    return SamplePerObjectShadowmap(TEXTURE2D_ARGS(_PerObjectShadowmapTexture, sampler_LinearClampCompare), shadowCoord, perObjectShadowParams);
}

float SamplePerObjectScreenSpaceShadowmap(float2 screenUV)
{
    float attenuation = SAMPLE_TEXTURE2D(_PerObjectScreenSpaceShadowmapTexture, sampler_PointClamp, screenUV).x;

    return attenuation;
}

float LoadPerObjectScreenSpaceShadowmap(uint2 coordSS)
{
    uint2 scaledCoordSS = coordSS >> (int)_PerObjectShadowParams.z;
    float attenuation = LOAD_TEXTURE2D(_PerObjectScreenSpaceShadowmapTexture, scaledCoordSS).x;

    return attenuation;
}

#endif /* PER_OBJECT_SHADOWS_INCLUDED */
