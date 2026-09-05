#ifndef VIVIDRP_VIRTUAL_SHADOW_MAP_ADDRESSING_INCLUDED
#define VIVIDRP_VIRTUAL_SHADOW_MAP_ADDRESSING_INCLUDED

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
