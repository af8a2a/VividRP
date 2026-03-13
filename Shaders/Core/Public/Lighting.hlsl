#ifndef VIVIDRP_LIGHTING_INCLUDED
#define VIVIDRP_LIGHTING_INCLUDED

struct DirectionalLightData
{
    float3 directionWS;
    float shadowStrength;
    float3 color;
    uint renderingLayerMask;
};

StructuredBuffer<DirectionalLightData> _DirectionalLights;
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
