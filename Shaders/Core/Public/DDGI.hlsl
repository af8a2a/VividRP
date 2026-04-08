#ifndef VIVIDRP_DDGI_INCLUDED
#define VIVIDRP_DDGI_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/GlobalIllumination/DDGI/Internal/DDGIShaderConfig.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/GlobalIllumination/DDGI/Internal/Irradiance.hlsl"

CBUFFER_START(ShaderVariablesDDGI)
    float4 _DDGIWorldAabbMin_BlendDistance;
    float4 _DDGIWorldAabbMax_Enabled;
    float4 _DDGIVolumeOrigin_ProbeNormalBias;
    float4 _DDGIVolumeRotation;
    float4 _DDGIProbeSpacing_ProbeViewBias;
    float4 _DDGIProbeCounts_IrradianceInteriorTexels;
    float4 _DDGIDistanceInteriorTexels_IrradianceGamma_IrradianceFormat;
CBUFFER_END

Texture2DArray<float4> _DDGIProbeIrradiance;
Texture2DArray<float4> _DDGIProbeDistance;
Texture2DArray<float4> _DDGIProbeData;

bool VividDDGIIsEnabled()
{
    return _DDGIWorldAabbMax_Enabled.w > 0.5f;
}

DDGIVolumeDescGPU VividCreateDDGIVolumeDesc()
{
    DDGIVolumeDescGPU volume = (DDGIVolumeDescGPU)0;
    volume.origin = _DDGIVolumeOrigin_ProbeNormalBias.xyz;
    volume.rotation = _DDGIVolumeRotation;
    volume.probeRayRotation = float4(0.0f, 0.0f, 0.0f, 1.0f);
    volume.movementType = RTXGI_DDGI_VOLUME_MOVEMENT_TYPE_DEFAULT;
    volume.probeSpacing = max(_DDGIProbeSpacing_ProbeViewBias.xyz, float3(0.01f, 0.01f, 0.01f));
    volume.probeCounts = int3(max(_DDGIProbeCounts_IrradianceInteriorTexels.xyz, 1.0f));
    volume.probeNumRays = RTXGI_DDGI_BLEND_RAYS_PER_PROBE;
    volume.probeNumIrradianceInteriorTexels = max((int)_DDGIProbeCounts_IrradianceInteriorTexels.w, 1);
    volume.probeNumDistanceInteriorTexels = max((int)_DDGIDistanceInteriorTexels_IrradianceGamma_IrradianceFormat.x, 1);
    volume.probeNormalBias = _DDGIVolumeOrigin_ProbeNormalBias.w;
    volume.probeViewBias = _DDGIProbeSpacing_ProbeViewBias.w;
    volume.probeDistanceExponent = 50.0f;
    volume.probeIrradianceEncodingGamma = _DDGIDistanceInteriorTexels_IrradianceGamma_IrradianceFormat.y;
    volume.probeIrradianceFormat = (uint)_DDGIDistanceInteriorTexels_IrradianceGamma_IrradianceFormat.z;
    volume.probeRayDataFormat = RTXGI_DDGI_VOLUME_TEXTURE_FORMAT_F32x2;
    volume.probeRelocationEnabled = false;
    volume.probeClassificationEnabled = false;
    volume.probeVariabilityEnabled = false;
    return volume;
}

float VividDDGIGetBlendWeight(float3 worldPosition)
{
    if (!VividDDGIIsEnabled())
    {
        return 0.0f;
    }

    float3 aabbMin = _DDGIWorldAabbMin_BlendDistance.xyz;
    float3 aabbMax = _DDGIWorldAabbMax_Enabled.xyz;
    if (any(worldPosition < aabbMin) || any(worldPosition > aabbMax))
    {
        return 0.0f;
    }

    float blendDistance = _DDGIWorldAabbMin_BlendDistance.w;
    if (blendDistance <= 0.0f)
    {
        return 1.0f;
    }

    float3 center = (aabbMin + aabbMax) * 0.5f;
    float3 halfExtents = max((aabbMax - aabbMin) * 0.5f, 0.0f);
    float3 localPosition = abs(worldPosition - center);
    float3 distanceToFaces = halfExtents - localPosition;
    float distanceToBoundary = min(distanceToFaces.x, min(distanceToFaces.y, distanceToFaces.z));
    return saturate(distanceToBoundary / blendDistance);
}

float3 VividDDGIGetIrradiance(float3 worldPosition, float3 surfaceNormal, float3 viewDirectionWS)
{
    if (!VividDDGIIsEnabled())
    {
        return 0.0f;
    }

    DDGIVolumeDescGPU volume = VividCreateDDGIVolumeDesc();
    DDGIVolumeResources resources;
    resources.probeIrradiance = _DDGIProbeIrradiance;
    resources.probeDistance = _DDGIProbeDistance;
    resources.probeData = _DDGIProbeData;
    resources.bilinearSampler = sampler_LinearClamp;

    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    float3 normalizedSurfaceNormal = SafeNormalize(surfaceNormal);
    float3 surfaceBias = DDGIGetSurfaceBias(normalizedSurfaceNormal, normalizedViewDirectionWS, volume);
    return DDGIGetVolumeIrradiance(
        worldPosition,
        surfaceBias,
        normalizedSurfaceNormal,
        volume,
        resources);
}

#endif
