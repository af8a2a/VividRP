#ifndef VIVIDRP_LIGHTING_INCLUDED
#define VIVIDRP_LIGHTING_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Input.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GeometricTools.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

#define VIVID_PUNCTUAL_LIGHT_TYPE_POINT 0u
#define VIVID_PUNCTUAL_LIGHT_TYPE_SPOT  1u
#define VIVID_PUNCTUAL_LIGHT_TYPE_PROJECTOR_BOX 2u
#define VIVID_AREA_LIGHT_TYPE_TUBE      0u
#define VIVID_AREA_LIGHT_TYPE_RECTANGLE 1u

struct DirectionalLightData
{
    float3 directionWS;
    float shadowStrength;
    float3 color;
    uint renderingLayerMask;
    float volumetricDimmer;
    float volumetricShadowDimmer;
    float volumetricFadeDistance;
    uint affectVolumetric;
};

struct PunctualLightData
{
    float3 positionWS;
    float range;
    float3 color;
    uint lightType;
    float3 directionWS;
    float angleScale;
    float3 rightWS;
    float angleOffset;
    float3 upWS;
    float shapeRadiusSquared;
    float2 projectorSize;
    float rangeAttenuationScale;
    float rangeAttenuationBias;
    float shadowStrength;
    uint renderingLayerMask;
    float volumetricDimmer;
    float volumetricShadowDimmer;
    float volumetricFadeDistance;
    uint affectVolumetric;
    float padding0;
    float padding1;
};

struct AreaLightData
{
    float3 positionWS;
    float rangeAttenuationScale;
    float3 color;
    uint lightType;
    float3 forwardWS;
    float rangeAttenuationBias;
    float3 rightWS;
    float width;
    float3 upWS;
    float height;
    uint renderingLayerMask;
    float range;
    float cosBarnDoorAngle;
    float barnDoorLength;
    float volumetricDimmer;
    float volumetricShadowDimmer;
    float volumetricFadeDistance;
    uint affectVolumetric;
};

struct ReflectionProbeData
{
    float3 positionWS;
    float blendDistance;
    float3 extents;
    uint isBoxProjection;
    float3 rightWS;
    float importance;
    float3 upWS;
    float weight;
    float3 forwardWS;
    float padding0;
    float3 capturePositionWS;
    float padding1;
    float4 hdrData;
    float4 atlasScaleOffset;
    float4 atlasIndexAndSlice;
};

StructuredBuffer<DirectionalLightData> _DirectionalLights;
StructuredBuffer<PunctualLightData> _PunctualLights;
StructuredBuffer<AreaLightData> _AreaLights;
StructuredBuffer<ReflectionProbeData> _ReflectionProbes;
TEXTURE2D_ARRAY(_ReflectionAtlas);
SAMPLER(sampler_ReflectionAtlas);
float4 _ReflectionAtlasCubeData;
uint _ReflectionAtlasMipCount;
uint _ReflectionAtlasSliceCount;
uint _DirectionalLightCount;
uint _ReflectionProbeCount;
int _MainDirectionalLightIndex;

bool HasDirectionalLights()
{
    return _DirectionalLightCount > 0;
}

bool IsDirectionalLightIndexValid(int lightIndex)
{
    return lightIndex >= 0 && lightIndex < (int)_DirectionalLightCount;
}

DirectionalLightData GetDirectionalLight(int lightIndex)
{
    return _DirectionalLights[lightIndex];
}

PunctualLightData GetPunctualLight(int lightIndex)
{
    return _PunctualLights[lightIndex];
}

AreaLightData GetAreaLight(int lightIndex)
{
    return _AreaLights[lightIndex];
}

ReflectionProbeData GetReflectionProbe(int lightIndex)
{
    return _ReflectionProbes[lightIndex];
}

bool IsReflectionProbeAtlasAvailable()
{
    return _ReflectionAtlasMipCount > 0u && _ReflectionAtlasSliceCount > 0u;
}

int GetReflectionProbeAtlasFetchIndex(ReflectionProbeData probe)
{
    return (int)probe.atlasIndexAndSlice.x;
}

int GetReflectionProbeAtlasSlice(ReflectionProbeData probe)
{
    return (int)probe.atlasIndexAndSlice.y;
}

bool IsReflectionProbeAtlasEntryValid(ReflectionProbeData probe)
{
    return IsReflectionProbeAtlasAvailable()
        && GetReflectionProbeAtlasFetchIndex(probe) >= 0
        && GetReflectionProbeAtlasSlice(probe) >= 0
        && probe.atlasScaleOffset.x > 0.0
        && probe.atlasScaleOffset.y > 0.0;
}

float3 WorldToReflectionProbeLocalPosition(ReflectionProbeData probe, float3 positionWS)
{
    float3 offsetWS = positionWS - probe.positionWS;
    return float3(
        dot(offsetWS, probe.rightWS),
        dot(offsetWS, probe.upWS),
        dot(offsetWS, probe.forwardWS));
}

float3 WorldToReflectionProbeLocalDirection(ReflectionProbeData probe, float3 directionWS)
{
    return float3(
        dot(directionWS, probe.rightWS),
        dot(directionWS, probe.upWS),
        dot(directionWS, probe.forwardWS));
}

float3 ReflectionProbeLocalToWorldDirection(ReflectionProbeData probe, float3 directionLS)
{
    return directionLS.x * probe.rightWS
        + directionLS.y * probe.upWS
        + directionLS.z * probe.forwardWS;
}

float EvaluateReflectionProbeInfluenceWeight(ReflectionProbeData probe, float3 positionWS, float3 normalWS)
{
    float3 positionLS = WorldToReflectionProbeLocalPosition(probe, positionWS);
    float3 distanceToFace = probe.extents - abs(positionLS);
    float insideWeight = all(distanceToFace >= 0.0) ? 1.0 : 0.0;
    float fadeDistance = max(probe.blendDistance, 0.0001);
    float faceWeight = saturate(min(min(distanceToFace.x, distanceToFace.y), distanceToFace.z) / fadeDistance);

    return Smoothstep01(faceWeight) * insideWeight * saturate(probe.weight);
}

float3 ProjectReflectionProbeDirection(ReflectionProbeData probe, float3 positionWS, float3 reflectionDirectionWS)
{
    float3 directionWS = SafeNormalize(reflectionDirectionWS);
    if (probe.isBoxProjection == 0u)
        return directionWS;

    float3 positionLS = WorldToReflectionProbeLocalPosition(probe, positionWS);
    float3 directionLS = SafeNormalize(WorldToReflectionProbeLocalDirection(probe, directionWS));
    float projectionDistance = IntersectRayAABBSimple(positionLS, directionLS, -probe.extents, probe.extents);

    if (projectionDistance <= 0.0 || IsNaN(projectionDistance))
        return directionWS;

    float3 hitLS = positionLS + projectionDistance * directionLS;
    float3 hitWS = probe.positionWS + ReflectionProbeLocalToWorldDirection(probe, hitLS);
    return hitWS - probe.capturePositionWS;
}

float GetReflectionProbeAtlasMipLevel(float perceptualRoughness)
{
    uint maxMipLevel = _ReflectionAtlasMipCount > 0u ? _ReflectionAtlasMipCount - 1u : 0u;
    return clamp(
        PerceptualRoughnessToMipmapLevel(saturate(perceptualRoughness), maxMipLevel),
        0.0,
        (float)maxMipLevel);
}

float2 GetReflectionProbeAtlasCoords(ReflectionProbeData probe, float3 directionWS, float mipLevel)
{
    float2 uv = saturate(PackNormalOctQuadEncode(SafeNormalize(directionWS)) * 0.5 + 0.5);
    float2 padding = _ReflectionAtlasCubeData.xy;
    padding *= exp2(max(mipLevel - _ReflectionAtlasCubeData.z, 0.0));

    float2 size = max(probe.atlasScaleOffset.xy - padding, float2(0.0, 0.0));
    float2 offset = probe.atlasScaleOffset.zw + 0.5 * padding;
    return saturate(uv * size + offset);
}

float4 SampleReflectionProbeAtlas(ReflectionProbeData probe, float3 directionWS, float perceptualRoughness)
{
    if (!IsReflectionProbeAtlasEntryValid(probe))
        return float4(0.0, 0.0, 0.0, 0.0);

    float mipLevel = GetReflectionProbeAtlasMipLevel(perceptualRoughness);
    float2 atlasUV = GetReflectionProbeAtlasCoords(probe, directionWS, mipLevel);
    int sliceIndex = clamp(GetReflectionProbeAtlasSlice(probe), 0, (int)_ReflectionAtlasSliceCount - 1);
    float4 color = SAMPLE_TEXTURE2D_ARRAY_LOD(_ReflectionAtlas, sampler_ReflectionAtlas, atlasUV, sliceIndex, mipLevel);
    color.rgb = ClampToFloat16Max(max(color.rgb, 0.0));
    color.a = 1.0;
    return color;
}

bool TryEvaluateReflectionProbeData(
    ReflectionProbeData probe,
    float3 positionWS,
    float3 normalWS,
    float3 reflectionDirectionWS,
    float perceptualRoughness,
    out float3 radiance,
    out float weight)
{
    radiance = 0.0;
    weight = 0.0;

    if (!IsReflectionProbeAtlasEntryValid(probe))
        return false;

    weight = EvaluateReflectionProbeInfluenceWeight(probe, positionWS, normalWS);
    if (weight <= 0.0)
        return false;

    float3 atlasDirectionWS = ProjectReflectionProbeDirection(probe, positionWS, reflectionDirectionWS);
    float4 sample = SampleReflectionProbeAtlas(probe, atlasDirectionWS, perceptualRoughness);
    radiance = sample.rgb;
    return true;
}

DirectionalLightData GetDirectionalLightDefault()
{
    DirectionalLightData light;
    light.directionWS = float3(0.0, 1.0, 0.0);
    light.shadowStrength = 0.0;
    light.color = 0.0;
    light.renderingLayerMask = 0u;
    light.volumetricDimmer = 0.0;
    light.volumetricShadowDimmer = 0.0;
    light.volumetricFadeDistance = 0.0;
    light.affectVolumetric = 0u;
    return light;
}

bool TryGetMainDirectionalLight(out DirectionalLightData light)
{
    if (IsDirectionalLightIndexValid(_MainDirectionalLightIndex))
    {
        light = GetDirectionalLight(_MainDirectionalLightIndex);
        return true;
    }

    light = GetDirectionalLightDefault();
    return false;
}

#endif
