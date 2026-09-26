#ifndef VIVIDRP_VIRTUAL_SHADOW_MAP_ADDRESSING_INCLUDED
#define VIVIDRP_VIRTUAL_SHADOW_MAP_ADDRESSING_INCLUDED

// Independent nearest surfaces; never fill the empty space between layers.
#define VIVID_VSM_DEPTH_LAYER_COUNT 16
#define VIVID_VSM_RASTER_MAX_LEVELS 16
#define VIVID_VSM_RASTER_PAGE_HEADER_SIZE (1 + 2 * VIVID_VSM_RASTER_MAX_LEVELS)

// 8x8 receiver cells per page, packed as two row-major 32-bit masks. Endpoints
// are inclusive texels; mask quantization expands coverage, never contracts it.
uint2 VividVSMReceiverMaskRect(uint2 low, uint2 high, uint pageSize)
{
    uint2 first = min(low * 8u / pageSize, 7u);
    uint2 last = min(high * 8u / pageSize, 7u);
    uint row = ((1u << (last.x - first.x + 1u)) - 1u) << first.x;
    uint2 mask = 0u;
    // Avoid dynamically indexing an l-value vector: legacy HLSL compilation
    // otherwise forces callers' traversal loops to unroll as well.
    [loop]
    for (uint y = first.y; y <= last.y; y++)
    {
        uint bits = row << ((y & 3u) * 8u);
        if (y < 4u) mask.x |= bits;
        else mask.y |= bits;
    }
    return mask;
}

bool VividVSMReceiverMaskContains(uint2 coverage, uint2 demand)
{
    return all((coverage & demand) == demand);
}

bool VividVSMReceiverMaskTexel(uint2 mask, uint2 texel, uint pageSize)
{
    uint2 cell = min(texel * 8u / pageSize, 7u);
    return (mask[cell.y >> 2u] & (1u << ((cell.y & 3u) * 8u + cell.x))) != 0u;
}

bool VividVSMReceiverMaskOverlapsRect(uint2 mask, uint2 page, uint2 low, uint2 high, uint pageSize)
{
    uint2 origin = page * pageSize;
    if (any(high < origin) || any(low >= origin + pageSize)) return false;
    uint2 rect = VividVSMReceiverMaskRect(max(low, origin) - origin,
        min(high, origin + pageSize - 1u) - origin, pageSize);
    return any((mask & rect) != 0u);
}

// Spatial hierarchy within each clipmap, not requests for another clipmap.
// Pad arbitrary page axes to a power of two; padded leaves stay empty.
uint VividVSMHierarchyAxis(uint axis) { return axis <= 1u ? 1u : 1u << ((uint)firstbithigh(axis - 1u) + 1u); }
uint VividVSMHierarchyNodesPerLevel(uint axis)
{
    axis = VividVSMHierarchyAxis(axis);
    return (4u * axis * axis - 1u) / 3u;
}
uint VividVSMHierarchyAddress(uint level, uint2 coord, uint mip, uint axis)
{
    axis = VividVSMHierarchyAxis(axis);
    uint mipAxis = axis >> mip;
    uint offset = 4u * (axis * axis - mipAxis * mipAxis) / 3u;
    return level * VividVSMHierarchyNodesPerLevel(axis) + offset + coord.y * mipAxis + coord.x;
}

// Inclusive, ordered page rectangle. Match UE MipLevelForRect(rect, 2):
// choose the finest mip that fits in 2x2 nodes after grid alignment.
uint VividVSMHierarchyMipForRect(uint2 lowPage, uint2 highPage)
{
    uint2 extent = highPage - lowPage;
    uint mip = (uint)max((int)firstbithigh(max(extent.x, extent.y)), 0);
    if (any((highPage >> mip) - (lowPage >> mip) > 1u)) mip++;
    return mip;
}

// OR each 2x2 group of cells into a 4x4 quadrant of the parent 8x8 mask.
uint2 VividVSMReduceReceiverMask(uint2 mask, uint2 quadrant)
{
    uint2 result = 0u;
    [unroll] for (uint y = 0u; y < 4u; y++)
    {
        uint rows = mask[y >> 1u] >> ((y & 1u) * 16u);
        uint bits = (rows | (rows >> 8u)) & 0xffu;
        bits = (bits | (bits >> 1u)) & 0x55u;
        bits = (bits | (bits >> 1u)) & 0x33u;
        bits = (bits | (bits >> 2u)) & 0x0fu;
        uint row = quadrant.y * 4u + y;
        result[row >> 2u] |= bits << ((row & 3u) * 8u + quadrant.x * 4u);
    }
    return result;
}

// Callers provide a positive resolution. SV_Position is in non-negative
// raster coordinates; truncation selects its pixel, including pixel centers.
uint2 VividVSMRasterPositionToVirtualTexel(
    float2 positionSS,
    uint virtualResolution)
{
    return min((uint2)positionSS, virtualResolution - 1u);
}

uint2 VividVSMUVToVirtualTexel(float2 shadowUV, uint virtualResolution)
{
    // Match the raster viewport, not an interpolation between texel indices.
    // Clamp UV == 1 to the last texel. Using this for both endpoints of a
    // bounds range conservatively includes the page at an exact upper edge.
    return VividVSMRasterPositionToVirtualTexel(
        saturate(shadowUV) * (float)virtualResolution,
        virtualResolution);
}

// Sampling uses signed virtual offsets and a half-open map domain. Do not clamp
// a tap to a page edge or add offsets to a resolved physical-atlas coordinate:
// neighboring virtual pages can occupy unrelated physical slots.
bool VividVSMTryOffsetVirtualTexel(float2 shadowUV, int2 offset, uint resolution,
    out int2 virtualTexel)
{
    virtualTexel = 0;
    float2 texel = floor(shadowUV * (float)resolution) + (float2)offset;
    if (!all(texel >= 0.0) || !all(texel < (float)resolution))
        return false;
    virtualTexel = (int2)texel;
    return true;
}

#endif
