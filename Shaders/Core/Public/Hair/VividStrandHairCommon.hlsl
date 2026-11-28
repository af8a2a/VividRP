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

// IJGC SIGGRAPH 2025 Biased L1 Spherical Harmonics self-shadowing contract
#define VIVID_HAIR_MAX_SCATTER_EVENTS  5.0f
#define VIVID_HAIR_SH_L0               0.2820947918f // 1 / (2 * sqrt(pi))
#define VIVID_HAIR_SH_L1               0.4886025119f // sqrt(3 / (4 * pi))

// Orthonormal L1 SH basis ordered (Y00, Y1x, Y1y, Y1z) matching HairGroomL1SH in C#.
float4 VividHairEvalL1SHBasis(float3 dir)
{
    return float4(
        VIVID_HAIR_SH_L0,
        VIVID_HAIR_SH_L1 * dir.x,
        VIVID_HAIR_SH_L1 * dir.y,
        VIVID_HAIR_SH_L1 * dir.z);
}

// Reconstructs non-negative expected scattering event count n in [0, VIVID_HAIR_MAX_SCATTER_EVENTS]
// along object-space light direction from Biased L1 SH coefficients.
float VividHairEvaluateScatterEventsL1SH(float4 shCoeffsOS, float3 lightDirOS)
{
    float n = dot(shCoeffsOS, VividHairEvalL1SHBasis(lightDirOS));
    return clamp(n, 0.0f, VIVID_HAIR_MAX_SCATTER_EVENTS);
}

// Reconstructs omnidirectional ambient visibility in [0, 1] from the L0 DC component of Biased L1 SH.
// When unbaked (shCoeffsOS == 0), returns 1.0 (unoccluded).
float VividHairEvaluateAmbientVisibilityL1SH(float4 shCoeffsOS, float extinctionPerEvent)
{
    if (dot(shCoeffsOS, shCoeffsOS) < 1e-8f)
        return 1.0f;

    // Average biased value over sphere is c0 * Y00 in [-MAX, +MAX].
    // Map [-MAX, +MAX] smoothly to effective omnidirectional scatter count in [0, MAX].
    float avgBiased = shCoeffsOS.x * VIVID_HAIR_SH_L0;
    float avgEvents = max(0.0f, (avgBiased + VIVID_HAIR_MAX_SCATTER_EVENTS) * 0.5f);
    return saturate(exp(-extinctionPerEvent * avgEvents));
}

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
