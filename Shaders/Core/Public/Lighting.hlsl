#ifndef VIVIDRP_LIGHTING_INCLUDED
#define VIVIDRP_LIGHTING_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Input.hlsl"

#define VIVID_PUNCTUAL_LIGHT_TYPE_POINT 0u
#define VIVID_PUNCTUAL_LIGHT_TYPE_SPOT  1u
#define VIVID_AREA_LIGHT_TYPE_TUBE      0u
#define VIVID_AREA_LIGHT_TYPE_RECTANGLE 1u

struct DirectionalLightData
{
    float3 directionWS;
    float shadowStrength;
    float3 color;
    uint renderingLayerMask;
};

struct PunctualLightData
{
    float3 positionWS;
    float range;
    float3 color;
    uint lightType;
    float3 directionWS;
    float angleScale;
    float angleOffset;
    float inverseRangeSquared;
    float shadowStrength;
    uint renderingLayerMask;
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
    float3 padding;
};

StructuredBuffer<DirectionalLightData> _DirectionalLights;
StructuredBuffer<PunctualLightData> _PunctualLights;
StructuredBuffer<AreaLightData> _AreaLights;
uint _DirectionalLightCount;
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

DirectionalLightData GetDirectionalLightDefault()
{
    DirectionalLightData light;
    light.directionWS = float3(0.0, 1.0, 0.0);
    light.shadowStrength = 0.0;
    light.color = 0.0;
    light.renderingLayerMask = 0u;
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
