#ifndef VIVIDRP_LIGHTING_INCLUDED
#define VIVIDRP_LIGHTING_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Input.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Lighting/ShaderConfig.cs.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Lighting/ShaderVariablesGlobalLightLoop.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Lighting/LightLoop.cs.hlsl"

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
StructuredBuffer<uint> g_vLayeredLightList;
StructuredBuffer<uint> g_LayeredOffset;
StructuredBuffer<float> g_logBaseBuffer;
uint _DirectionalLightCount;
uint _PunctualLightCount;
int _MainDirectionalLightIndex;
int _ClusterTileSize;
int _ClusterSliceCount;
int _ClusterTileCountX;
int _ClusterTileCountY;
float _ClusterNearClip;
float _ClusterFarClip;
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

uint GetClusterLightIndex(uint lightStart, uint lightOffset)
{
    return g_vLayeredLightList[lightStart + lightOffset];
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

float GetClusterViewDepth(float2 uv, float deviceDepth)
{
    float3 positionWS = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
    return GetClusterViewDepthWS(positionWS);
}

uint GetClusterTileCountX()
{
    return max(_NumTileClusteredX, max((uint)_ClusterTileCountX, 1u));
}

uint GetClusterTileCountY()
{
    return max(_NumTileClusteredY, max((uint)_ClusterTileCountY, 1u));
}

uint GetClusterSliceCountInternal()
{
    return g_iLog2NumClusters > 0 ? (1u << g_iLog2NumClusters) : max((uint)_ClusterSliceCount, 1u);
}

uint2 GetClusterTileCoord(uint2 pixelCoord)
{
    uint tileSize = max((uint)_ClusterTileSize, 1u);
    uint tileCountX = GetClusterTileCountX();
    uint tileCountY = GetClusterTileCountY();

    return uint2(
        min(pixelCoord.x / tileSize, tileCountX - 1u),
        min(pixelCoord.y / tileSize, tileCountY - 1u));
}

uint GetLogBaseBufferIndex(uint2 tileCoord)
{
    return tileCoord.y * GetClusterTileCountX() + tileCoord.x;
}

float GetClusterLogBase(uint2 tileCoord)
{
    float logBase = max(g_fClustBase, 1.0001);

    if (g_isLogBaseBufferEnabled != 0u)
        logBase = max(g_logBaseBuffer[GetLogBaseBufferIndex(tileCoord)], logBase);

    return logBase;
}

uint GetClusterSliceIndex(uint2 pixelCoord, float viewDepth)
{
    uint sliceCount = GetClusterSliceCountInternal();
    if (sliceCount <= 1u)
        return 0u;

    float nearPlane = max(g_fNearPlane, 0.0001);
    float farPlane = max(g_fFarPlane, nearPlane + 0.0001);
    float depth = clamp(viewDepth, nearPlane, farPlane);
    float logBase = GetClusterLogBase(GetClusterTileCoord(pixelCoord));
    float sliceCountF = (float)sliceCount;
    float rangeFittedDistance = saturate((depth - nearPlane) / (farPlane - nearPlane));
    float basePow = pow(logBase, sliceCountF);
    float slice = log2(lerp(1.0, basePow, rangeFittedDistance)) / log2(logBase);
    return min((uint)max((int)slice, 0), sliceCount - 1u);
}

uint GetClusterSliceIndex(float viewDepth)
{
    uint sliceCount = GetClusterSliceCountInternal();
    if (sliceCount <= 1u)
        return 0u;

    float nearPlane = max(g_fNearPlane, 0.0001);
    float farPlane = max(g_fFarPlane, nearPlane + 0.0001);
    float depth = clamp(viewDepth, nearPlane, farPlane);
    float logBase = max(g_fClustBase, 1.0001);
    float sliceCountF = (float)sliceCount;
    float rangeFittedDistance = saturate((depth - nearPlane) / (farPlane - nearPlane));
    float basePow = pow(logBase, sliceCountF);
    float slice = log2(lerp(1.0, basePow, rangeFittedDistance)) / log2(logBase);
    return min((uint)max((int)slice, 0), sliceCount - 1u);
}

uint GetLayeredOffsetBufferIndex(uint lightCategory, uint2 tileCoord, uint clusterIndex)
{
    uint numTilesX = GetClusterTileCountX();
    uint numTilesY = GetClusterTileCountY();
    uint numClusters = GetClusterSliceCountInternal();
    return ((lightCategory * numClusters + clusterIndex) * numTilesY + tileCoord.y) * numTilesX + tileCoord.x;
}

uint2 UnpackClusterLightRange(uint packedOffset)
{
    return uint2(
        packedOffset & LIGHT_CLUSTER_PACKING_OFFSET_MASK,
        (packedOffset >> LIGHT_CLUSTER_PACKING_OFFSET_BITS) & LIGHT_CLUSTER_PACKING_COUNT_MASK);
}

uint GetClusterIndex(uint2 pixelCoord, float viewDepth)
{
    uint2 tileCoord = GetClusterTileCoord(pixelCoord);
    uint tileCountX = GetClusterTileCountX();
    uint tileCountY = GetClusterTileCountY();
    uint sliceIndex = GetClusterSliceIndex(pixelCoord, viewDepth);

    return tileCoord.x + tileCoord.y * tileCountX + sliceIndex * tileCountX * tileCountY;
}

uint GetClusterIndex(uint2 pixelCoord, float3 positionWS)
{
    return GetClusterIndex(pixelCoord, GetClusterViewDepthWS(positionWS));
}

uint2 GetClusterLightRange(uint2 pixelCoord, float viewDepth)
{
    uint2 tileCoord = GetClusterTileCoord(pixelCoord);
    uint sliceIndex = GetClusterSliceIndex(pixelCoord, viewDepth);
    uint packedOffset = g_LayeredOffset[GetLayeredOffsetBufferIndex(LIGHTCATEGORY_PUNCTUAL, tileCoord, sliceIndex)];
    return UnpackClusterLightRange(packedOffset);
}

uint2 GetClusterLightRange(uint2 pixelCoord, float3 positionWS)
{
    return GetClusterLightRange(pixelCoord, GetClusterViewDepthWS(positionWS));
}

#endif
