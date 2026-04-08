/*
* Copyright (c) 2019-2023, NVIDIA CORPORATION.  All rights reserved.
*
* NVIDIA CORPORATION and its licensors retain all intellectual property
* and proprietary rights in and to this software, related documentation
* and any modifications thereto.  Any use, reproduction, disclosure or
* distribution of this software and related documentation without an express
* license agreement from NVIDIA CORPORATION is strictly prohibited.
*/

#ifndef RTXGI_DDGI_IRRADIANCE_HLSL
#define RTXGI_DDGI_IRRADIANCE_HLSL

#include "include/ProbeCommon.hlsl"

struct DDGIVolumeResources
{
    Texture2DArray<float4> probeIrradiance;
    Texture2DArray<float4> probeDistance;
    Texture2DArray<float4> probeData;
    SamplerState bilinearSampler;
};

float3 DDGIGetSurfaceBias(float3 surfaceNormal, float3 cameraDirection, DDGIVolumeDescGPU volume)
{
    return (surfaceNormal * volume.probeNormalBias) + (-cameraDirection * volume.probeViewBias);
}

float DDGIGetVolumeBlendWeight(float3 worldPosition, DDGIVolumeDescGPU volume)
{
    float3 origin = volume.origin + (volume.probeScrollOffsets * volume.probeSpacing);
    float3 extent = (volume.probeSpacing * (volume.probeCounts - 1)) * 0.5f;

    float3 position = (worldPosition - origin);
    position = abs(RTXGIQuaternionRotate(position, RTXGIQuaternionConjugate(volume.rotation)));

    float3 delta = position - extent;
    if (all(delta < 0))
    {
        return 1.f;
    }

    float volumeBlendWeight = 1.f;
    volumeBlendWeight *= (1.f - saturate(delta.x / volume.probeSpacing.x));
    volumeBlendWeight *= (1.f - saturate(delta.y / volume.probeSpacing.y));
    volumeBlendWeight *= (1.f - saturate(delta.z / volume.probeSpacing.z));

    return volumeBlendWeight;
}

float3 DDGIGetVolumeIrradiance(
    float3 worldPosition,
    float3 surfaceBias,
    float3 direction,
    DDGIVolumeDescGPU volume,
    DDGIVolumeResources resources)
{
    float3 irradiance = float3(0.f, 0.f, 0.f);
    float accumulatedWeights = 0.f;

    float3 biasedWorldPosition = (worldPosition + surfaceBias);
    int3 baseProbeCoords = DDGIGetBaseProbeGridCoords(biasedWorldPosition, volume);
    float3 baseProbeWorldPosition = DDGIGetProbeWorldPosition(baseProbeCoords, volume);

    float3 gridSpaceDistance = (biasedWorldPosition - baseProbeWorldPosition);
    if (!IsVolumeMovementScrolling(volume))
    {
        gridSpaceDistance = RTXGIQuaternionRotate(gridSpaceDistance, RTXGIQuaternionConjugate(volume.rotation));
    }

    float3 alpha = clamp((gridSpaceDistance / volume.probeSpacing), float3(0.f, 0.f, 0.f), float3(1.f, 1.f, 1.f));

    for (int probeIndex = 0; probeIndex < 8; probeIndex++)
    {
        int3 adjacentProbeOffset = int3(probeIndex, probeIndex >> 1, probeIndex >> 2) & int3(1, 1, 1);
        int3 adjacentProbeCoords = clamp(
            baseProbeCoords + adjacentProbeOffset,
            int3(0, 0, 0),
            volume.probeCounts - int3(1, 1, 1));

        int adjacentProbeIndex = DDGIGetScrollingProbeIndex(adjacentProbeCoords, volume);
        int probeState = DDGILoadProbeState(adjacentProbeIndex, resources.probeData, volume);
        if (probeState == RTXGI_DDGI_PROBE_STATE_INACTIVE)
        {
            continue;
        }

        float3 adjacentProbeWorldPosition = DDGIGetProbeWorldPosition(adjacentProbeCoords, volume, resources.probeData);
        float3 worldPosToAdjProbe = normalize(adjacentProbeWorldPosition - worldPosition);
        float3 biasedPosToAdjProbe = normalize(adjacentProbeWorldPosition - biasedWorldPosition);
        float biasedPosToAdjProbeDist = length(adjacentProbeWorldPosition - biasedWorldPosition);

        float3 trilinear = max(0.001f, lerp(1.f - alpha, alpha, adjacentProbeOffset));
        float trilinearWeight = (trilinear.x * trilinear.y * trilinear.z);
        float weight = 1.f;

        float wrapShading = (dot(worldPosToAdjProbe, direction) + 1.f) * 0.5f;
        weight *= (wrapShading * wrapShading) + 0.2f;

        float2 octantCoords = DDGIGetOctahedralCoordinates(-biasedPosToAdjProbe);
        float3 probeTextureUV = DDGIGetProbeUV(
            adjacentProbeIndex,
            octantCoords,
            volume.probeNumDistanceInteriorTexels,
            volume);

        float2 filteredDistance = 2.f * resources.probeDistance.SampleLevel(resources.bilinearSampler, probeTextureUV, 0).rg;
        float variance = abs((filteredDistance.x * filteredDistance.x) - filteredDistance.y);

        float chebyshevWeight = 1.f;
        if (biasedPosToAdjProbeDist > filteredDistance.x)
        {
            float v = biasedPosToAdjProbeDist - filteredDistance.x;
            chebyshevWeight = variance / (variance + (v * v));
            chebyshevWeight = max((chebyshevWeight * chebyshevWeight * chebyshevWeight), 0.f);
        }

        weight *= max(0.05f, chebyshevWeight);
        weight = max(0.000001f, weight);

        const float crushThreshold = 0.2f;
        if (weight < crushThreshold)
        {
            weight *= (weight * weight) * (1.f / (crushThreshold * crushThreshold));
        }

        weight *= trilinearWeight;

        octantCoords = DDGIGetOctahedralCoordinates(direction);
        probeTextureUV = DDGIGetProbeUV(
            adjacentProbeIndex,
            octantCoords,
            volume.probeNumIrradianceInteriorTexels,
            volume);

        float3 probeIrradiance = resources.probeIrradiance.SampleLevel(resources.bilinearSampler, probeTextureUV, 0).rgb;
        float3 exponent = volume.probeIrradianceEncodingGamma * 0.5f;
        probeIrradiance = pow(probeIrradiance, exponent);

        irradiance += (weight * probeIrradiance);
        accumulatedWeights += weight;
    }

    if (accumulatedWeights == 0.f)
    {
        return float3(0.f, 0.f, 0.f);
    }

    irradiance *= (1.f / accumulatedWeights);
    irradiance *= irradiance;
    irradiance *= RTXGI_2PI;

    if (volume.probeIrradianceFormat == RTXGI_DDGI_VOLUME_TEXTURE_FORMAT_U32)
    {
        irradiance *= 1.0989f;
    }

    return irradiance;
}

#endif // RTXGI_DDGI_IRRADIANCE_HLSL
