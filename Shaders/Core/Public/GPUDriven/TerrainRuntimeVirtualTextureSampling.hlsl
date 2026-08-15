#ifndef VIVIDRP_TERRAIN_RUNTIME_VIRTUAL_TEXTURE_SAMPLING_INCLUDED
#define VIVIDRP_TERRAIN_RUNTIME_VIRTUAL_TEXTURE_SAMPLING_INCLUDED

struct TerrainRuntimeVirtualTextureRecordGPUData
{
    uint LevelStartIndex;
    uint LevelCount;
    uint Revision;
    uint Padding0;
};

struct TerrainRuntimeVirtualTextureLevelGPUData
{
    uint2 AtlasPageOrigin;
    int2 WindowPageOrigin;
    uint2 TotalPageCount;
    uint2 Padding0;
};

StructuredBuffer<TerrainRuntimeVirtualTextureRecordGPUData> _VividTerrainRVTRecords;
StructuredBuffer<TerrainRuntimeVirtualTextureLevelGPUData> _VividTerrainRVTLevels;
uint _VividTerrainRVTRecordCount;
uint _VividTerrainRVTEnabled;

float3 VividDecodeTerrainRVTNormal(float4 packedNormal)
{
    const float2 normalXY = packedNormal.wy * 2.0 - 1.0;
    return float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
}

bool VividTrySampleTerrainRVTLevel(
    const TerrainRuntimeVirtualTextureLevelGPUData levelData,
    const float2 terrainUv,
    const float2 terrainUvDdx,
    const float2 terrainUvDdy,
    const float4 positionCS,
    out float blendWeight,
    out float3 baseColor,
    out float3 normalTS,
    out float4 mask)
{
    blendWeight = 0.0;
    baseColor = 0.0.xxx;
    normalTS = float3(0.0, 0.0, 1.0);
    mask = 1.0.xxxx;

    const float2 totalPageCount = max((float2)levelData.TotalPageCount, 1.0.xx);
    float2 scaledPage = saturate(terrainUv) * totalPageCount;
    scaledPage = min(scaledPage, totalPageCount - 1e-5);
    const int2 logicalPage = (int2)floor(scaledPage);
    const int2 localPage = logicalPage - levelData.WindowPageOrigin;
    if (any(localPage < 0) || any(localPage >= 8))
        return false;

    const float2 totalTexelCount = totalPageCount * VT_PAGE_SIZE;
    const float texelFootprint = max(
        length(terrainUvDdx * totalTexelCount),
        length(terrainUvDdy * totalTexelCount));
    const float detailWeight = saturate(2.0 - texelFootprint);
    const float2 windowPosition = scaledPage - (float2)levelData.WindowPageOrigin;
    const float edgeDistance = min(
        min(windowPosition.x, windowPosition.y),
        min(8.0 - windowPosition.x, 8.0 - windowPosition.y));
    blendWeight = min(detailWeight, saturate(edgeDistance));
    if (blendWeight <= 0.0)
        return false;

    const uint2 ringPage = (uint2)logicalPage & 7u;
    const uint2 atlasPage = levelData.AtlasPageOrigin + ringPage;
    const float2 virtualUv =
        (float2(atlasPage) + frac(scaledPage)) / float2(VT_VIRTUAL_PAGE_COUNT_X, VT_VIRTUAL_PAGE_COUNT_Y);
    const VTResolvedAddress resolved = VTResolveAddress(virtualUv, 0u);
    VTWriteAccessFeedback(virtualUv, 0u, resolved, positionCS);
    VTWriteResolvedSampleStatus(virtualUv, 0u, resolved, positionCS);
    if (!resolved.resident || !resolved.valid || resolved.resolvedMip != 0u)
        return false;

    float4 packedBaseColor = VTSamplePhysicalCacheLayer(virtualUv, resolved, 0u);
    packedBaseColor.rgb = VTApplyLayerColorSpace(packedBaseColor.rgb, 0u);
    baseColor = packedBaseColor.rgb;
    normalTS = VividDecodeTerrainRVTNormal(
        VTSamplePhysicalCacheLayer(virtualUv, resolved, 1u));
    mask = VTSamplePhysicalCacheLayer(virtualUv, resolved, 2u);
    return true;
}

bool VividResolveTerrainRVT(
    const uint recordIndex,
    const float2 terrainUv,
    const float2 terrainUvDdx,
    const float2 terrainUvDdy,
    const float4 positionCS,
    inout float3 baseColor,
    inout float3 normalTS,
    inout float4 mask)
{
    if (_VividTerrainRVTEnabled == 0u || recordIndex >= _VividTerrainRVTRecordCount)
        return false;

    const TerrainRuntimeVirtualTextureRecordGPUData recordData =
        _VividTerrainRVTRecords[recordIndex];
    bool sampledAnyLevel = false;
    [unroll]
    for (int reverseLevelIndex = 2; reverseLevelIndex >= 0; --reverseLevelIndex)
    {
        if ((uint)reverseLevelIndex >= recordData.LevelCount)
            continue;

        const TerrainRuntimeVirtualTextureLevelGPUData levelData =
            _VividTerrainRVTLevels[recordData.LevelStartIndex + (uint)reverseLevelIndex];
        float levelWeight;
        float3 levelBaseColor;
        float3 levelNormalTS;
        float4 levelMask;
        if (!VividTrySampleTerrainRVTLevel(
                levelData,
                terrainUv,
                terrainUvDdx,
                terrainUvDdy,
                positionCS,
                levelWeight,
                levelBaseColor,
                levelNormalTS,
                levelMask))
        {
            continue;
        }

        baseColor = lerp(baseColor, levelBaseColor, levelWeight);
        normalTS = SafeNormalize(lerp(normalTS, levelNormalTS, levelWeight));
        mask = lerp(mask, levelMask, levelWeight);
        sampledAnyLevel = true;
    }
    return sampledAnyLevel;
}

#endif
