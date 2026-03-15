#ifndef VIVIDRP_LIGHTING_INCLUDED
#define VIVIDRP_LIGHTING_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Input.hlsl"

#define VIVID_PUNCTUAL_LIGHT_TYPE_POINT 0u
#define VIVID_PUNCTUAL_LIGHT_TYPE_SPOT  1u

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

StructuredBuffer<DirectionalLightData> _DirectionalLights;
StructuredBuffer<PunctualLightData> _PunctualLights;
StructuredBuffer<uint2> _ClusterLightGrid;
StructuredBuffer<uint> _ClusterLightIndices;
uint _DirectionalLightCount;
uint _PunctualLightCount;
int _MainDirectionalLightIndex;
int _ClusterTileSize;
int _ClusterSliceCount;
int _ClusterTileCountX;
int _ClusterTileCountY;
float _ClusterNearClip;
float _ClusterFarClip;
float _ClusterLogDepthScale;
float _ClusterLinearDepthScale;
float _ClusterTanHalfFovX;
float _ClusterTanHalfFovY;
float _ClusterOrthoHalfWidth;
float _ClusterOrthoHalfHeight;
int _ClusterIsOrthographic;

bool HasDirectionalLights()
{
    return _DirectionalLightCount > 0;
}

bool HasPunctualLights()
{
    return _PunctualLightCount > 0;
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

float GetClusterViewDepthWS(float3 positionWS)
{
    float3 positionVS = TransformWorldToView(positionWS);
    return max(-positionVS.z, max(_ClusterNearClip, 0.0001));
}

uint GetClusterSliceIndex(float viewDepth)
{
    uint sliceCount = max((uint)_ClusterSliceCount, 1u);
    if (sliceCount == 1u)
        return 0u;

    if (_ClusterIsOrthographic != 0)
    {
        float linearSlice = (viewDepth - _ClusterNearClip) * _ClusterLinearDepthScale;
        return min((uint)max((int)linearSlice, 0), sliceCount - 1u);
    }

    float logSlice = log2(max(viewDepth, _ClusterNearClip) / _ClusterNearClip) * _ClusterLogDepthScale;
    return min((uint)max((int)logSlice, 0), sliceCount - 1u);
}

uint2 GetClusterTileCoord(uint2 pixelCoord)
{
    uint tileSize = max((uint)_ClusterTileSize, 1u);
    uint tileCountX = max((uint)_ClusterTileCountX, 1u);
    uint tileCountY = max((uint)_ClusterTileCountY, 1u);

    return uint2(
        min(pixelCoord.x / tileSize, tileCountX - 1u),
        min(pixelCoord.y / tileSize, tileCountY - 1u));
}

uint GetClusterIndex(uint2 pixelCoord, float3 positionWS)
{
    uint2 tileCoord = GetClusterTileCoord(pixelCoord);
    uint tileCountX = max((uint)_ClusterTileCountX, 1u);
    uint tileCountY = max((uint)_ClusterTileCountY, 1u);
    uint sliceIndex = GetClusterSliceIndex(GetClusterViewDepthWS(positionWS));

    return tileCoord.x + tileCoord.y * tileCountX + sliceIndex * tileCountX * tileCountY;
}

uint2 GetClusterLightRange(uint2 pixelCoord, float3 positionWS)
{
    return _ClusterLightGrid[GetClusterIndex(pixelCoord, positionWS)];
}

#endif
