#ifndef VIVIDRP_CLUSTERED_LIGHTING_INCLUDED
#define VIVIDRP_CLUSTERED_LIGHTING_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Input.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Lighting/ShaderConfig.cs.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Lighting/ShaderVariablesGlobalLightLoop.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Lighting/LightLoop.cs.hlsl"

StructuredBuffer<uint> g_vLayeredLightList;
StructuredBuffer<uint> g_LayeredOffset;
StructuredBuffer<float> g_logBaseBuffer;
int _ClusterTileSize;
int _ClusterSliceCount;
int _ClusterTileCountX;
int _ClusterTileCountY;
float _ClusterNearClip;
float _ClusterFarClip;
int _ClusterIsOrthographic;

struct VividClusteredLightCell
{
    uint offset;
    uint count;
};

struct VividClusteredLighting
{
    static float GetViewDepthWS(float3 positionWS)
    {
        float3 positionVS = float3(0.0, 0.0, 0.0);
        positionVS = TransformWorldToView(positionWS);
        return max(-positionVS.z, max(_ClusterNearClip, 0.0001));
    }

    static float GetViewDepth(float2 uv, float deviceDepth)
    {
        float3 positionWS = float3(0.0, 0.0, 0.0);
        positionWS = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
        return GetViewDepthWS(positionWS);
    }

    static uint GetTileCountX()
    {
        return max(_NumTileClusteredX, max((uint)_ClusterTileCountX, 1u));
    }

    static uint GetTileCountY()
    {
        return max(_NumTileClusteredY, max((uint)_ClusterTileCountY, 1u));
    }

    static uint GetSliceCount()
    {
        return g_iLog2NumClusters > 0 ? (1u << g_iLog2NumClusters) : max((uint)_ClusterSliceCount, 1u);
    }

    static uint2 GetTileCoord(uint2 pixelCoord)
    {
        uint tileSize = max((uint)_ClusterTileSize, 1u);
        uint tileCountX = GetTileCountX();
        uint tileCountY = GetTileCountY();

        return uint2(
            min(pixelCoord.x / tileSize, tileCountX - 1u),
            min(pixelCoord.y / tileSize, tileCountY - 1u));
    }

    static uint GetLogBaseBufferIndex(uint2 tileCoord)
    {
        return tileCoord.y * GetTileCountX() + tileCoord.x;
    }

    static float GetLogBase(uint2 tileCoord)
    {
        float logBase = max(g_fClustBase, 1.0001);

        if (g_isLogBaseBufferEnabled != 0u)
            logBase = max(g_logBaseBuffer[GetLogBaseBufferIndex(tileCoord)], logBase);

        return logBase;
    }

    static uint GetSliceIndex(uint2 pixelCoord, float viewDepth)
    {
        uint sliceCount = GetSliceCount();
        if (sliceCount <= 1u)
            return 0u;

        float nearPlane = max(g_fNearPlane, 0.0001);
        float farPlane = max(g_fFarPlane, nearPlane + 0.0001);
        float depth = clamp(viewDepth, nearPlane, farPlane);
        float logBase = GetLogBase(GetTileCoord(pixelCoord));
        float sliceCountF = (float)sliceCount;
        float rangeFittedDistance = saturate((depth - nearPlane) / (farPlane - nearPlane));
        float basePow = pow(logBase, sliceCountF);
        float slice = 0.0;
        slice = log2(lerp(1.0, basePow, rangeFittedDistance)) / log2(logBase);
        uint sliceIndex = min((uint)max((int)slice, 0), sliceCount - 1u);
        return sliceIndex;
    }

    static uint GetSliceIndex(float viewDepth)
    {
        uint sliceCount = GetSliceCount();
        if (sliceCount <= 1u)
            return 0u;

        float nearPlane = max(g_fNearPlane, 0.0001);
        float farPlane = max(g_fFarPlane, nearPlane + 0.0001);
        float depth = clamp(viewDepth, nearPlane, farPlane);
        float logBase = max(g_fClustBase, 1.0001);
        float sliceCountF = (float)sliceCount;
        float rangeFittedDistance = saturate((depth - nearPlane) / (farPlane - nearPlane));
        float basePow = pow(logBase, sliceCountF);
        float slice = 0.0;
        slice = log2(lerp(1.0, basePow, rangeFittedDistance)) / log2(logBase);
        uint sliceIndex = min((uint)max((int)slice, 0), sliceCount - 1u);
        return sliceIndex;
    }

    static uint GetLayeredOffsetBufferIndex(uint lightCategory, uint2 tileCoord, uint sliceIndex)
    {
        uint numTilesX = GetTileCountX();
        uint numTilesY = GetTileCountY();
        uint numClusters = GetSliceCount();
        return ((lightCategory * numClusters + sliceIndex) * numTilesY + tileCoord.y) * numTilesX + tileCoord.x;
    }

    static VividClusteredLightCell UnpackLightCell(uint packedOffset)
    {
        VividClusteredLightCell lightCell = (VividClusteredLightCell)0;
        lightCell.offset = packedOffset & LIGHT_CLUSTER_PACKING_OFFSET_MASK;
        lightCell.count = (packedOffset >> LIGHT_CLUSTER_PACKING_OFFSET_BITS) & LIGHT_CLUSTER_PACKING_COUNT_MASK;
        return lightCell;
    }

    static uint FlattenClusterIndex(uint2 tileCoord, uint sliceIndex)
    {
        uint tileCountX = GetTileCountX();
        uint tileCountY = GetTileCountY();
        return tileCoord.x + tileCoord.y * tileCountX + sliceIndex * tileCountX * tileCountY;
    }

    static VividClusteredLightCell LoadPunctualLightCell(uint2 tileCoord, uint sliceIndex)
    {
        uint packedOffset = g_LayeredOffset[GetLayeredOffsetBufferIndex(LIGHTCATEGORY_PUNCTUAL, tileCoord, sliceIndex)];
        return UnpackLightCell(packedOffset);
    }

    static VividClusteredLightCell LoadPunctualLightCell(uint2 pixelCoord, float viewDepth)
    {
        uint2 tileCoord = GetTileCoord(pixelCoord);
        uint sliceIndex = GetSliceIndex(pixelCoord, viewDepth);
        return LoadPunctualLightCell(tileCoord, sliceIndex);
    }

    static VividClusteredLightCell LoadPunctualLightCell(uint2 pixelCoord, float3 positionWS)
    {
        return LoadPunctualLightCell(pixelCoord, GetViewDepthWS(positionWS));
    }

    static VividClusteredLightCell LoadAreaLightCell(uint2 tileCoord, uint sliceIndex)
    {
        uint packedOffset = g_LayeredOffset[GetLayeredOffsetBufferIndex(LIGHTCATEGORY_AREA, tileCoord, sliceIndex)];
        return UnpackLightCell(packedOffset);
    }

    static VividClusteredLightCell LoadAreaLightCell(uint2 pixelCoord, float viewDepth)
    {
        uint2 tileCoord = GetTileCoord(pixelCoord);
        uint sliceIndex = GetSliceIndex(pixelCoord, viewDepth);
        return LoadAreaLightCell(tileCoord, sliceIndex);
    }

    static VividClusteredLightCell LoadAreaLightCell(uint2 pixelCoord, float3 positionWS)
    {
        return LoadAreaLightCell(pixelCoord, GetViewDepthWS(positionWS));
    }

    static uint LoadLightIndex(VividClusteredLightCell lightCell, uint localIndex)
    {
        return g_vLayeredLightList[lightCell.offset + localIndex];
    }
};

float GetClusterViewDepthWS(float3 positionWS)
{
    return VividClusteredLighting::GetViewDepthWS(positionWS);
}

float GetClusterViewDepth(float2 uv, float deviceDepth)
{
    return VividClusteredLighting::GetViewDepth(uv, deviceDepth);
}

uint GetClusterTileCountX()
{
    return VividClusteredLighting::GetTileCountX();
}

uint GetClusterTileCountY()
{
    return VividClusteredLighting::GetTileCountY();
}

uint GetClusterSliceCountInternal()
{
    return VividClusteredLighting::GetSliceCount();
}

uint2 GetClusterTileCoord(uint2 pixelCoord)
{
    return VividClusteredLighting::GetTileCoord(pixelCoord);
}

uint GetLogBaseBufferIndex(uint2 tileCoord)
{
    return VividClusteredLighting::GetLogBaseBufferIndex(tileCoord);
}

float GetClusterLogBase(uint2 tileCoord)
{
    return VividClusteredLighting::GetLogBase(tileCoord);
}

uint GetClusterSliceIndex(uint2 pixelCoord, float viewDepth)
{
    return VividClusteredLighting::GetSliceIndex(pixelCoord, viewDepth);
}

uint GetClusterSliceIndex(float viewDepth)
{
    return VividClusteredLighting::GetSliceIndex(viewDepth);
}

uint GetLayeredOffsetBufferIndex(uint lightCategory, uint2 tileCoord, uint clusterIndex)
{
    return VividClusteredLighting::GetLayeredOffsetBufferIndex(lightCategory, tileCoord, clusterIndex);
}

uint2 UnpackClusterLightRange(uint packedOffset)
{
    VividClusteredLightCell lightCell = VividClusteredLighting::UnpackLightCell(packedOffset);
    return uint2(lightCell.offset, lightCell.count);
}

uint GetClusterLightIndex(uint lightStart, uint lightOffset)
{
    return g_vLayeredLightList[lightStart + lightOffset];
}

uint GetClusterIndex(uint2 pixelCoord, float viewDepth)
{
    uint2 tileCoord = VividClusteredLighting::GetTileCoord(pixelCoord);
    uint sliceIndex = VividClusteredLighting::GetSliceIndex(pixelCoord, viewDepth);
    return VividClusteredLighting::FlattenClusterIndex(tileCoord, sliceIndex);
}

uint GetClusterIndex(uint2 pixelCoord, float3 positionWS)
{
    return GetClusterIndex(pixelCoord, VividClusteredLighting::GetViewDepthWS(positionWS));
}

uint2 GetClusterLightRange(uint2 pixelCoord, float viewDepth)
{
    VividClusteredLightCell lightCell = VividClusteredLighting::LoadPunctualLightCell(pixelCoord, viewDepth);
    return uint2(lightCell.offset, lightCell.count);
}

uint2 GetClusterLightRange(uint2 pixelCoord, float3 positionWS)
{
    VividClusteredLightCell lightCell = VividClusteredLighting::LoadPunctualLightCell(pixelCoord, positionWS);
    return uint2(lightCell.offset, lightCell.count);
}

#endif
