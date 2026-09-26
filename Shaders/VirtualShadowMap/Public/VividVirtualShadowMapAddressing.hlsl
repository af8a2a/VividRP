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
    for (uint y = first.y; y <= last.y; y++) mask[y >> 2u] |= row << ((y & 3u) * 8u);
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
