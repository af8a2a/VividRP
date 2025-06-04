#ifndef SHADOWS_PCSS_INCLUDED
#define SHADOWS_PCSS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingDisk.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"


// Limitation:
// Note that in cascade shadows, all occluders behind the near plane will get clamped to the near plane
// This will lead to the closest blocker sometimes being reported as much closer to the receiver than it really is
#if UNITY_REVERSED_Z
#define Z_OFFSET_DIRECTION 1
#else
#define Z_OFFSET_DIRECTION (-1)
#endif


// PreFilter finds the border of shadows. 
float PreFilterSearch(float sampleCount, float filterSize, float3 shadowCoord, float cascadeIndex, float2 random)
{
    float numBlockers = 0.0;

    float radial2ShadowmapDepth = _PerCascadePCSSData[cascadeIndex].x;
    float texelSizeWS = _PerCascadePCSSData[cascadeIndex].y;
    float farToNear = _PerCascadePCSSData[cascadeIndex].z;
    float blockerInvTangent = _PerCascadePCSSData[cascadeIndex].w;

    float2 minCoord = _DirLightShadowUVMinMax.xy;
    float2 maxCoord = _DirLightShadowUVMinMax.zw;
    float sampleCountInverse = rcp((float)sampleCount);
    float sampleCountBias = 0.5 * sampleCountInverse;

    // kernel my be too large, and there can be no valid depth compare result.
    // we must calculate it again in later PCSS.
    float coordOutOfBoundCount = 0;

    for (int i = 1; i < sampleCount && i < DISK_SAMPLE_COUNT; i++)
    {
        float sampleRadius = sqrt((float)i * sampleCountInverse + sampleCountBias);
        float2 offset = fibonacciSpiralDirection[i] * sampleRadius;
        offset = float2(offset.x * random.y + offset.y * random.x,
                        offset.x * -random.x + offset.y * random.y);
        offset *= filterSize;
        offset *= _MainLightShadowmapSize.x; // coord to uv


        float2 sampleCoord = shadowCoord.xy + offset;

        float radialOffset = filterSize * sampleRadius * texelSizeWS;
        float zoffset = radialOffset / farToNear * blockerInvTangent;

        float depthLS = shadowCoord.z + (Z_OFFSET_DIRECTION) * zoffset;

        float shadowMapDepth = SAMPLE_TEXTURE2D_LOD(_MainLightShadowmapTexture, sampler_PointClamp, sampleCoord, 0).x;

        bool isOutOfCoord = any(sampleCoord < minCoord) || any(sampleCoord > maxCoord);
        if (!isOutOfCoord && COMPARE_DEVICE_DEPTH_CLOSER(shadowMapDepth, depthLS))
        {
            numBlockers += 1.0;
        }

        if (isOutOfCoord)
        {
            coordOutOfBoundCount++;
        }
    }

    // Out of bound, we must calculate it again in later PCSS.
    if (coordOutOfBoundCount > 0)
    {
        numBlockers = 1.0;
    }

    // We must cover zero offset.

    float shadowMapDepth = SAMPLE_TEXTURE2D_LOD(_MainLightShadowmapTexture, sampler_PointClamp, shadowCoord.xy, 0).x;
    if (!(any(shadowCoord.xy < minCoord) || any(shadowCoord.xy > maxCoord)) &&
        COMPARE_DEVICE_DEPTH_CLOSER(shadowMapDepth, shadowCoord.z))
    {
        numBlockers += 1.0;
    }

    return numBlockers;
}


float2 CustomComputeFibonacciSpiralDiskSample(const in int sampleIndex, const in float sampleCountInverse, const in float sampleBias, out float sampleRadius)
{
    sampleRadius = sqrt((float)sampleIndex * sampleCountInverse + sampleBias);
    float2 sampleDirection = fibonacciSpiralDirection[sampleIndex];
    return sampleDirection * sampleRadius;
}


// Return x:avgerage blocker depth, y:num blockers
float2 BlockerSearch(float sampleCount, float filterSize, float3 shadowCoord, float2 random, float cascadeIndex)
{
    float avgBlockerDepth = 0.0;
    float depthSum = 0.0;
    float numBlockers = 0.0;

    float radial2ShadowmapDepth = _PerCascadePCSSData[cascadeIndex].x;
    float texelSizeWS = _PerCascadePCSSData[cascadeIndex].y;
    float farToNear = _PerCascadePCSSData[cascadeIndex].z;
    float blockerInvTangent = _PerCascadePCSSData[cascadeIndex].w;

    float2 minCoord = _DirLightShadowUVMinMax.xy;
    float2 maxCoord = _DirLightShadowUVMinMax.zw;
    float sampleCountInverse = rcp((float)sampleCount);
    float sampleCountBias = 0.5 * sampleCountInverse;

    for (int i = 0; i < sampleCount && i < DISK_SAMPLE_COUNT; i++)
    {
        float sampleDistNorm;
        float2 offset = 0.0;
        offset = ComputeFibonacciSpiralDiskSampleUniform_Directional(i, sampleCountInverse, sampleCountBias, sampleDistNorm);
        offset = float2(offset.x * random.y + offset.y * random.x,
                        offset.x * -random.x + offset.y * random.y);
        offset *= filterSize;
        offset *= _MainLightShadowmapSize.x; // coord to uv

        float2 sampleCoord = shadowCoord.xy + offset;

        float radialOffset = filterSize * sampleDistNorm * texelSizeWS;
        float zoffset = radialOffset / farToNear * blockerInvTangent;

        float depthLS = shadowCoord.z + (Z_OFFSET_DIRECTION) * zoffset;

        float shadowMapDepth = SAMPLE_TEXTURE2D_LOD(_MainLightShadowmapTexture, sampler_PointClamp, sampleCoord, 0).x;
        if (!(any(sampleCoord < minCoord) || any(sampleCoord > maxCoord)) &&
            COMPARE_DEVICE_DEPTH_CLOSER(shadowMapDepth, depthLS))
        {
            depthSum += shadowMapDepth;
            numBlockers += 1.0;
        }
    }

    if (numBlockers > 0.0)
    {
        avgBlockerDepth = depthSum / numBlockers;
    }

    return float2(avgBlockerDepth, numBlockers);
}


float PCSSFilter(float sampleCount, float filterSize, float3 shadowCoord, float2 random, float cascadeIndex, float maxPCSSoffset)
{
    float numBlockers = 0.0;
    float totalSamples = 0.0;

    float radial2ShadowmapDepth = _PerCascadePCSSData[cascadeIndex].x;
    float texelSizeWS = _PerCascadePCSSData[cascadeIndex].y;
    float farToNear = _PerCascadePCSSData[cascadeIndex].z;
    float blockerInvTangent = _PerCascadePCSSData[cascadeIndex].w;

    float2 minCoord = _DirLightShadowUVMinMax.xy;
    float2 maxCoord = _DirLightShadowUVMinMax.zw;
    float sampleCountInverse = rcp((float)sampleCount);
    float sampleCountBias = 0.5 * sampleCountInverse;

    for (int i = 0; i < sampleCount && i < DISK_SAMPLE_COUNT; i++)
    {
        float sampleDistNorm;
        float2 offset = 0.0;
        offset = CustomComputeFibonacciSpiralDiskSample(i, sampleCountInverse, sampleCountBias, sampleDistNorm);
        offset = float2(offset.x * random.y + offset.y * random.x,
                        offset.x * -random.x + offset.y * random.y);
        offset *= filterSize;
        offset *= _MainLightShadowmapSize.x; // coord to uv

        float2 sampleCoord = shadowCoord.xy + offset;

        float radialOffset = filterSize * sampleDistNorm * texelSizeWS;
        float zoffset = radialOffset / farToNear * blockerInvTangent;

        float depthLS = shadowCoord.z + (Z_OFFSET_DIRECTION) * min(zoffset, maxPCSSoffset);

        if (!(any(sampleCoord < minCoord) || any(sampleCoord > maxCoord)))
        {
            float shadowSample = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3(sampleCoord, depthLS)).x;
            numBlockers += shadowSample;
            totalSamples++;
        }
    }

    return totalSamples > 0 ? numBlockers / totalSamples : 1.0;
}


// World space filter size.
#define FILTER_SIZE_PREFILTER           (0.3)
#define FILTER_SIZE_BLOCKER             (0.2)
#define PREFILTER_SAMPLE_COUNT          (32)
#define BLOCKER_SAMPLE_COUNT            (64)
#define PCSS_SAMPLE_COUNT               (32)
#define DIR_LIGHT_PENUMBRA_WIDTH        _DirLightShadowPenumbraParams.x



float MainLightRealtimeShadow_PCSS(float3 positionWS, float4 shadowCoord, float2 screenUV)
{
    float cascadeIndex = ComputeCascadeIndex(positionWS);
    
    float radial2ShadowmapDepth = _PerCascadePCSSData[cascadeIndex].x;
    float texelSizeWS           = _PerCascadePCSSData[cascadeIndex].y;
    float farToNear             = _PerCascadePCSSData[cascadeIndex].z;
    float blockerInvTangent     = _PerCascadePCSSData[cascadeIndex].w;
    
    // Sample Noise: Use Jitter Instead
    float sampleJitterAngle = InterleavedGradientNoise(screenUV * _ScreenSize.xy, _Time.y) * TWO_PI;
    float2 noiseJitter = float2(sin(sampleJitterAngle), cos(sampleJitterAngle));
    
    // Blocker Search
    float filterSize = FILTER_SIZE_BLOCKER / texelSizeWS;
    filterSize = max(filterSize, 1.0);
    float2 avgDepthAndCount = BlockerSearch(BLOCKER_SAMPLE_COUNT, filterSize, shadowCoord.xyz, noiseJitter, cascadeIndex);
    if (avgDepthAndCount.y == 0) return 1.0;
    
    // Penumbra Estimation
    float blockerDistance = abs(avgDepthAndCount.x - shadowCoord.z);
    blockerDistance *= farToNear;
    blockerDistance = min(blockerDistance, 10.0);
    
    float pcssFilterSize = DIR_LIGHT_PENUMBRA_WIDTH * blockerDistance * 0.01 / texelSizeWS;
    pcssFilterSize = max(pcssFilterSize, 0.01);
    
    float maxPCSSoffset = blockerDistance / farToNear * 0.25;
    float attenuation = PCSSFilter(PCSS_SAMPLE_COUNT, pcssFilterSize, shadowCoord.xyz, noiseJitter, cascadeIndex, maxPCSSoffset);
    
    if (!GetShadowScatterEnable())
        attenuation = LerpWhiteTo(attenuation, GetMainLightShadowParams().x);
    return attenuation;
}




#endif /* SHADOWS_PCSS_INCLUDED */
