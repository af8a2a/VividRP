#include "Packages/com.unity.render-pipelines.universal//Runtime/Extension/ClusterLighting/GPULights.cs.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/LightingData.hlsl"
// #pragma enable_d3d11_debug_symbols


StructuredBuffer<uint>  g_vBigTileLightList;

// The "big tile" list contains the number of objects contained within the tile followed by the
// list of object indices. Note that while objects are already sorted by type, we don't know the
// number of each type of objects (e.g. lights), so we should remember to break out of the loop.
// Each light indices are encoded into 16 bits.
void GetBigTileLightCountAndStart(uint tileIndex, out uint lightCount, out uint start)
{
    start = tileIndex;
    lightCount = g_vBigTileLightList[MAX_NR_BIG_TILE_LIGHTS_PLUS_ONE * tileIndex / 2] & 0xFFFF;
    // On Metal for unknow reasons it seems we have bad value sometimes causing freeze / crash. This min here prevent it.
    lightCount = min(lightCount, MAX_NR_BIG_TILE_LIGHTS_PLUS_ONE);
}

uint FetchIndex(uint lightStart, uint lightOffset)
{
    const uint lightOffsetPlusOne = lightOffset + 1; // Add +1 as first slot is reserved to store number of light
    // Light index are store on 16bit
    return (g_vBigTileLightList[MAX_NR_BIG_TILE_LIGHTS_PLUS_ONE * lightStart / 2 + (lightOffsetPlusOne >> 1)] >> ((lightOffsetPlusOne & 1) * 16)) & 0xffff;
}
