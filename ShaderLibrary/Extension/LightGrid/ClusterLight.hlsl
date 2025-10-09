#ifndef GPU_CULLED_LIGHTS_INCLUDED
#define GPU_CULLED_LIGHTS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/LightCullingSystem/GPULights.cs.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/LightCullingSystem/Shader/GPULightsCullingUtils.hlsl"
#include "Private/LightingData.hlsl"
// #pragma enable_d3d11_debug_symbols


//--------------------------------------------------------------------------------------------------
// Coarse
//--------------------------------------------------------------------------------------------------

uint GetBigTileSize()
{
    return TILE_SIZE_BIG_TILE;
}


//--------------------------------------------------------------------------------------------------
// Cluster
//--------------------------------------------------------------------------------------------------

// Buffers
StructuredBuffer<uint> g_vLayeredOffsetsBuffer;
StructuredBuffer<float> g_logBaseBuffer;
StructuredBuffer<uint> g_vLightListCluster;

// Cluster Lighting

uint GetTileSize()
{
    return TILE_SIZE_CLUSTERED;
}

uint GetLightClusterIndex(uint2 tileIndex, float linearDepth)
{
    float logBase = g_fClustBase;
    if (g_isLogBaseBufferEnabled)
    {
        const uint logBaseIndex = GenerateLogBaseBufferIndex(tileIndex, _NumTileClusteredX, _NumTileClusteredY,
                                                             unity_StereoEyeIndex);
        logBase = g_logBaseBuffer[logBaseIndex];
    }

    return SnapToClusterIdxFlex(linearDepth, logBase, g_isLogBaseBufferEnabled != 0);
}

void UnpackClusterLayeredOffset(uint packedValue, out uint offset, out uint count)
{
    offset = packedValue & LIGHT_CLUSTER_PACKING_OFFSET_MASK;
    count = packedValue >> LIGHT_CLUSTER_PACKING_OFFSET_BITS;
}

void GetCountAndStartCluster(uint2 tileIndex, uint clusterIndex, uint lightCategory, out uint start,
                             out uint lightCount)
{
    int nrClusters = (1 << g_iLog2NumClusters);

    const int idx = GenerateLayeredOffsetBufferIndex(lightCategory, tileIndex, clusterIndex, _NumTileClusteredX,
                                                     _NumTileClusteredY, nrClusters, unity_StereoEyeIndex);

    uint dataPair = g_vLayeredOffsetsBuffer[idx];
    UnpackClusterLayeredOffset(dataPair, start, lightCount);
}

void GetCountAndStartCluster(PositionInputs posInput, uint lightCategory, out uint start, out uint lightCount)
{
    uint2 tileIndex = (float2)posInput.positionSS / GetTileSize();
    uint clusterIndex = GetLightClusterIndex(tileIndex, posInput.linearDepth);

    GetCountAndStartCluster(tileIndex, clusterIndex, lightCategory, start, lightCount);
}

void GetCountAndStart(PositionInputs posInput, uint lightCategory, out uint start, out uint lightCount)
{
    GetCountAndStartCluster(posInput, lightCategory, start, lightCount);
}

uint FetchIndex(uint lightStart, uint lightOffset)
{
    return g_vLightListCluster[lightStart + lightOffset];
}


GPULightData FetchLight(uint lightStart, uint lightOffset)
{
    int index = FetchIndex(lightStart, lightOffset);
    return g_GPULightDatas[index];
}


struct ClusteredLightingGridCell
{
    uint Start;
    uint Count;

    GPULightData LoadLight(int index)
    {
        return FetchLight(Start, index);
    }
};


struct ClusteredLighting
{
    static uint GetLightClusterIndex(uint2 tileIndex, float linearDepth)
    {
        float logBase = g_fClustBase;
        if (g_isLogBaseBufferEnabled)
        {
            const uint logBaseIndex = GenerateLogBaseBufferIndex(tileIndex, _NumTileClusteredX, _NumTileClusteredY,
                                                                 unity_StereoEyeIndex);
            logBase = g_logBaseBuffer[logBaseIndex];
        }

        return SnapToClusterIdxFlex(linearDepth, logBase, g_isLogBaseBufferEnabled != 0);
    }

    static uint GetTileSize()
    {
        return TILE_SIZE_CLUSTERED;
    }

    static void UnpackClusterLayeredOffset(uint packedValue, out uint offset, out uint count)
    {
        offset = packedValue & LIGHT_CLUSTER_PACKING_OFFSET_MASK;
        count = packedValue >> LIGHT_CLUSTER_PACKING_OFFSET_BITS;
    }

    static ClusteredLightingGridCell LoadPunctualLightCell(const PositionInputs posInput)
    {
        ClusteredLightingGridCell LightingCell;
        ZERO_INITIALIZE(ClusteredLightingGridCell, LightingCell);


        uint lightCategory = LIGHTCATEGORY_PUNCTUAL;
        GetCountAndStart(posInput, lightCategory, LightingCell.Start, LightingCell.Count);

        return LightingCell;
    }


    static ClusteredLightingGridCell LoadAreaLightCell(const PositionInputs posInput)
    {
        ClusteredLightingGridCell LightingCell;
        ZERO_INITIALIZE(ClusteredLightingGridCell, LightingCell);


        uint lightCategory = LIGHTCATEGORY_AREA;
        GetCountAndStart(posInput, lightCategory, LightingCell.Start, LightingCell.Count);

        return LightingCell;
    }
};


#endif /* GPU_CULLED_LIGHTS_INCLUDED */
