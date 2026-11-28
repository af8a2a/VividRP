#ifndef VIVIDRP_STRAND_HAIR_COMMON_HLSL
#define VIVIDRP_STRAND_HAIR_COMMON_HLSL

#if !defined(VIVID_STANDALONE_DXC)
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#endif
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Hair/VividHairGroomTypes.hlsl"

#define VIVID_HAIR_ATLAS_TILE_SIZE     64u
#define VIVID_HAIR_ATLAS_TILE_PIXELS   4096u // 64 * 64
#define VIVID_HAIR_ATLAS_LAYER_COUNT   4u    // 3 ordered depth/segId layers + 1 additive coverage
#define VIVID_HAIR_ATLAS_TILE_STRIDE   16384u // 4096 * 4

// Exact 1D box-filter overlap coverage for a hair cylinder of screen-space
// radius rPx (diameter 2*rPx) at perpendicular pixel-center distance distPx.
// Works seamlessly for both sub-pixel strands (2*rPx < 1) and thick close-up strands.
float VividHairComputeAnalyticCoverage(float rPx, float distPx)
{
    return saturate(min(2.0f * rPx, 0.5f + rPx - distPx));
}

// Converts clip-space position (before /w) to screen pixel coordinates (0..width, 0..height)
// matching Unity's render-target Y orientation (UNITY_UV_STARTS_AT_TOP).
float2 VividHairClipToScreenPixel(float4 clipPos, float2 screenSize)
{
    float invW = rcp(max(abs(clipPos.w), 1e-6f));
    float2 ndc = clipPos.xy * invW;
    float2 uv = ndc * 0.5f + 0.5f;
#if UNITY_UV_STARTS_AT_TOP
    uv.y = 1.0f - uv.y;
#endif
    return uv * screenSize;
}

float VividHairClipToDeviceZ(float4 clipPos)
{
    return clipPos.z * rcp(max(abs(clipPos.w), 1e-6f));
}

// Returns true if hairDeviceZ is in front of (or equal to) opaque sceneDeviceZ.
bool VividHairDepthPassesOpaque(float hairDeviceZ, float sceneDeviceZ)
{
#if UNITY_REVERSED_Z
    return hairDeviceZ >= sceneDeviceZ;
#else
    return hairDeviceZ <= sceneDeviceZ;
#endif
}

// Resolves the linear index in _HairVisAtlas for an atlas-space pixel within a model's tile grid.
uint VividHairComputeAtlasPixelBase(
    uint2 atlasPixel,
    uint2 tileOrigin,
    uint2 tileGridSize,
    uint firstAtlasTile)
{
    uint2 localTile = (atlasPixel / VIVID_HAIR_ATLAS_TILE_SIZE) - tileOrigin;
    uint2 inTile = atlasPixel & (VIVID_HAIR_ATLAS_TILE_SIZE - 1u);
    uint tileIndex = firstAtlasTile + localTile.y * tileGridSize.x + localTile.x;
    return tileIndex * VIVID_HAIR_ATLAS_TILE_STRIDE
           + inTile.y * VIVID_HAIR_ATLAS_TILE_SIZE
           + inTile.x;
}

#endif // VIVIDRP_STRAND_HAIR_COMMON_HLSL
