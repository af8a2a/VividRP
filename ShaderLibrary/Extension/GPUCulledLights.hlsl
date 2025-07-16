#ifndef GPU_CULLED_LIGHTS_INCLUDED
#define GPU_CULLED_LIGHTS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal//Runtime/Extension/ClusterLighting/GPULights.cs.hlsl"
#pragma enable_d3d11_debug_symbols

#ifdef _GPU_LIGHTS_CLUSTER
#define USE_GPULIGHTS_CLUSTER
#endif /* _GPU_LIGHTS_CLUSTER */

//--------------------------------------------------------------------------------------------------
// Helpers
//--------------------------------------------------------------------------------------------------
float GetScaleFromBase(float base)
{
    const float C = (float)(1u << g_iLog2NumClusters);
    const float geomSeries = (1.0 - PositivePow(base, C)) / (1 - base);     // geometric series: sum_k=0^{C-1} base^k
    return geomSeries / (g_fFarPlane - g_fNearPlane);
}

float LogBase(float x, float b)
{
    return log2(x) / log2(b);
}

int SnapToClusterIdxFlex(float z_in, float suggestedBase, bool logBasePerTile)
{
#if USE_LEFT_HAND_CAMERA_SPACE
    float z = z_in;
#else
    float z = -z_in;
#endif

    //float userscale = g_fClustScale;
    //if (logBasePerTile)
    //    userscale = GetScaleFromBase(suggestedBase);

    // using the inverse of the geometric series
    //const float dist = max(0, z - g_fNearPlane);
    //return (int)clamp(log2(dist * userscale * (suggestedBase - 1.0f) + 1) / log2(suggestedBase), 0.0, (float)((1 << g_iLog2NumClusters) - 1));

    const int C = 1u << g_iLog2NumClusters;
    const float rangeFittedDistance = max(0, z - g_fNearPlane) / (g_fFarPlane - g_fNearPlane);
    return (int)clamp( LogBase( lerp(1.0, PositivePow(suggestedBase, (float) C), rangeFittedDistance), suggestedBase), 0.0, (float)(C - 1));
}

int SnapToClusterIdx(float z_in, float suggestedBase)
{
#ifdef ENABLE_DEPTH_TEXTURE_BACKPLANE
    bool logBasePerTile = true;     // resolved compile time
#else
    bool logBasePerTile = false;
#endif

    return SnapToClusterIdxFlex(z_in, suggestedBase, logBasePerTile);
}

float ClusterIdxToZFlex(int k, float suggestedBase, bool logBasePerTile)
{
    float res;

    //float userscale = g_fClustScale;
    //if (logBasePerTile)
    //    userscale = GetScaleFromBase(suggestedBase);

    //float dist = (PositivePow(suggestedBase, (float)k) - 1.0) / (userscale * (suggestedBase - 1.0f));
    //res = dist + g_fNearPlane;

    const float C = (float)(1u << g_iLog2NumClusters);
    float rangeFittedDistance = (PositivePow(suggestedBase, (float)k) - 1.0) / (PositivePow(suggestedBase, C) - 1.0);
    res = lerp(g_fNearPlane, g_fFarPlane, rangeFittedDistance);


#if USE_LEFT_HAND_CAMERA_SPACE
    return res;
#else
    return -res;
#endif
}

float ClusterIdxToZ(int k, float suggestedBase)
{
#ifdef ENABLE_DEPTH_TEXTURE_BACKPLANE
    bool logBasePerTile = true;     // resolved compile time
#else
    bool logBasePerTile = false;
#endif

    return ClusterIdxToZFlex(k, suggestedBase, logBasePerTile);
}

// generate a log-base value such that half of the clusters are consumed from near plane to max. opaque depth of tile.
float SuggestLogBase50(float tileFarPlane)
{
    const float C = (float)(1u << g_iLog2NumClusters);
    float rangeFittedDistance = clamp((tileFarPlane - g_fNearPlane) / (g_fFarPlane - g_fNearPlane), FLT_EPS, 1.0);
    float suggested_base = pow((1.0 + sqrt(max(0.0, 1.0 - 4.0 * rangeFittedDistance * (1.0 - rangeFittedDistance)))) / (2.0 * rangeFittedDistance), 2.0 / C);      //
    return max(g_fClustBase, suggested_base);
}

// generate a log-base value such that (approximately) a quarter of the clusters are consumed from near plane to max. opaque depth of tile.
float SuggestLogBase25(float tileFarPlane)
{
    const float C = (float)(1u << g_iLog2NumClusters);
    float rangeFittedDistance = clamp((tileFarPlane - g_fNearPlane) / (g_fFarPlane - g_fNearPlane), FLT_EPS, 1.0);
    float suggested_base = pow((1 / 2.3) * max(0.0, (0.8 / rangeFittedDistance) - 1), 4.0 / (C * 2));     // approximate inverse of d*x^4 + (-x) + (1-d) = 0       - d is normalized distance
    return max(g_fClustBase, suggested_base);
}

uint GenerateLogBaseBufferIndex(uint2 tileIndex, uint numTilesX, uint numTilesY, uint eyeIndex)
{
    uint eyeOffset = eyeIndex * numTilesX * numTilesY;
    return (eyeOffset + (tileIndex.y * numTilesX) + tileIndex.x);
}

uint GenerateLayeredOffsetBufferIndex(uint lightCategory, uint2 tileIndex, uint clusterIndex, uint numTilesX, uint numTilesY, int numClusters, uint eyeIndex)
{
    // Each eye is split into category, cluster, x, y

    uint eyeOffset = eyeIndex * LIGHTCATEGORY_COUNT * numClusters * numTilesX * numTilesY;
    int lightOffset = ((lightCategory * numClusters + clusterIndex) * numTilesY + tileIndex.y) * numTilesX + tileIndex.x;

    return (eyeOffset + lightOffset);
}


#if defined(USE_GPULIGHTS_COARSE)
//--------------------------------------------------------------------------------------------------
// Coarse
//--------------------------------------------------------------------------------------------------

uint GetTileSize()
{
    return TILE_SIZE_BIG_TILE;
}


#elif defined(USE_GPULIGHTS_CLUSTER)

//--------------------------------------------------------------------------------------------------
// Cluster
//--------------------------------------------------------------------------------------------------

// Buffers
StructuredBuffer<uint>  g_vLayeredOffsetsBuffer;
StructuredBuffer<float> g_logBaseBuffer;
StructuredBuffer<uint>  g_vLightListCluster;

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
        const uint logBaseIndex = GenerateLogBaseBufferIndex(tileIndex, _NumTileClusteredX, _NumTileClusteredY, unity_StereoEyeIndex);
        logBase = g_logBaseBuffer[logBaseIndex];
    }

    return SnapToClusterIdxFlex(linearDepth, logBase, g_isLogBaseBufferEnabled != 0);
}

void UnpackClusterLayeredOffset(uint packedValue, out uint offset, out uint count)
{
    offset = packedValue & LIGHT_CLUSTER_PACKING_OFFSET_MASK;
    count = packedValue >> LIGHT_CLUSTER_PACKING_OFFSET_BITS;
}

void GetCountAndStartCluster(uint2 tileIndex, uint clusterIndex, uint lightCategory, out uint start, out uint lightCount)
{
    int nrClusters = (1 << g_iLog2NumClusters);

    const int idx = GenerateLayeredOffsetBufferIndex(lightCategory, tileIndex, clusterIndex, _NumTileClusteredX, _NumTileClusteredY, nrClusters, unity_StereoEyeIndex);

    uint dataPair = g_vLayeredOffsetsBuffer[idx];
    UnpackClusterLayeredOffset(dataPair, start, lightCount);
}

void GetCountAndStartCluster(PositionInputs posInput, uint lightCategory, out uint start, out uint lightCount)
{
    uint2 tileIndex    = (float2)posInput.positionSS / GetTileSize();
    uint  clusterIndex = GetLightClusterIndex(tileIndex, posInput.linearDepth);

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

#else /* USE_GPULIGHTS_CLUSTER */
// No function implementation
void GetCountAndStart(PositionInputs posInput, uint lightCategory, out uint start, out uint lightCount)
{
    start = 0;
    lightCount = 0;
}

uint FetchIndex(uint lightStart, uint lightOffset)
{
    // No implementation
    return 0;
}

#endif /* USE_GPULIGHTS_CLUSTER */

//--------------------------------------------------------------------------------------------------
// Global fetch data
//--------------------------------------------------------------------------------------------------

StructuredBuffer<GPULightData> g_GPULightDatas;


GPULightData FetchLight(uint lightStart, uint lightOffset)
{
    int index = FetchIndex(lightStart, lightOffset);
    return g_GPULightDatas[index];
}

GPULightData FetchLight(uint index)
{
    return g_GPULightDatas[index];
}

//--------------------------------------------------------------------------------------------------
// Directional light data
//--------------------------------------------------------------------------------------------------
StructuredBuffer<DirectionalLightData> g_DirectionalLightDatas;



#endif /* GPU_CULLED_LIGHTS_INCLUDED */