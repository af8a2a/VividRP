#ifndef VIVIDRP_VIRTUAL_SHADOW_MAP_ADDRESSING_INCLUDED
#define VIVIDRP_VIRTUAL_SHADOW_MAP_ADDRESSING_INCLUDED

// One depth per texel. Slice 0 is final depth; slice 1 is the static cache.
#define VIVID_VSM_FINAL_DEPTH_SLICE 0u
#define VIVID_VSM_STATIC_DEPTH_SLICE 1u
#if defined(VIVID_VSM_LEGACY_DEPTH_TESTS)
#define VIVID_VSM_DEPTH_LAYER_COUNT 16
#else
#define VIVID_VSM_DEPTH_LAYER_COUNT 1
#endif
#define VIVID_VSM_RASTER_MAX_LEVELS 24
#define VIVID_VSM_RASTER_PAGE_HEADER_SIZE (1 + 2 * VIVID_VSM_RASTER_MAX_LEVELS)

// Unified Directional + Point + Spot VSM addressing and handle constants.
// Low ID range [0, VIVID_VSM_MAX_SINGLE_PAGE_SHADOW_MAPS) is reserved for single-page (distant) lights.
#define VIVID_VSM_MAX_SINGLE_PAGE_SHADOW_MAPS 512u
#define VIVID_VSM_INVALID_ID 0xFFFFFFFFu

#define VIVID_VSM_LIGHT_TYPE_DIRECTIONAL 0u
#define VIVID_VSM_LIGHT_TYPE_POINT 1u
#define VIVID_VSM_LIGHT_TYPE_SPOT 2u
#define VIVID_VSM_LIGHT_TYPE_RECT 3u
#define VIVID_VSM_LIGHT_TYPE_TUBE 4u

#define VIVID_VSM_PROJ_FLAG_SINGLE_PAGE (1u << 0)
#define VIVID_VSM_PROJ_FLAG_UNREFERENCED (1u << 1)
#define VIVID_VSM_PROJ_FLAG_USE_RECEIVER_MASK (1u << 2)
#define VIVID_VSM_PROJ_FLAG_IS_COARSE_CLIP_LEVEL (1u << 3)
#define VIVID_VSM_PROJ_FLAG_UNCACHED (1u << 4)
#define VIVID_VSM_PROJ_FLAG_SKIP_DYNAMIC_INVALID (1u << 5)

#define VIVID_VSM_NEXT_MAP_FLAG_VALID (1u << 0)
#define VIVID_VSM_NEXT_MAP_FLAG_INVALIDATE_ALL (1u << 1)

#define VIVID_VSM_OWNER_FLAG_VALID (1u << 0)
#define VIVID_VSM_OWNER_FLAG_SINGLE_PAGE (1u << 1)
#define VIVID_VSM_OWNER_FLAG_UNREFERENCED (1u << 2)
#define VIVID_VSM_OWNER_FLAG_DETAIL_GEOMETRY (1u << 3)
#define VIVID_VSM_OWNER_FLAG_REMAPPED (1u << 4)

// One-Pass Projection packed shadow mask: 4-bit x 32 local lights per pixel in uint4.
#define VIVID_VSM_MAX_PACKED_LOCAL_LIGHTS 32u

struct VividVSMHandle
{
    uint id;
    bool isSinglePage;
};

VividVSMHandle VividVSMMakeHandle(uint vsmId)
{
    VividVSMHandle handle;
    handle.id = vsmId;
    handle.isSinglePage = vsmId < VIVID_VSM_MAX_SINGLE_PAGE_SHADOW_MAPS;
    return handle;
}

VividVSMHandle VividVSMMakeDirectionalHandle(uint vsmId)
{
    VividVSMHandle handle;
    handle.id = vsmId;
    handle.isSinglePage = false;
    return handle;
}

VividVSMHandle VividVSMMakeInvalidHandle()
{
    VividVSMHandle handle;
    handle.id = VIVID_VSM_INVALID_ID;
    handle.isSinglePage = false;
    return handle;
}

bool VividVSMHandleIsValid(VividVSMHandle handle)
{
    return handle.id != VIVID_VSM_INVALID_ID;
}

VividVSMHandle VividVSMHandleOffset(VividVSMHandle handle, int offset)
{
    VividVSMHandle result;
    result.id = (uint)((int)handle.id + offset);
    result.isSinglePage = handle.isSinglePage;
    return result;
}

// Unity left-handed Y-up cube face selection: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z.
// dirFromLight = positionWS - lightPositionWS.
uint VividVSMGetCubeFace(float3 dirFromLight)
{
    float3 a = abs(dirFromLight);
    if (a.x >= a.y && a.x >= a.z) return dirFromLight.x > 0.0 ? 0u : 1u;
    if (a.y > a.z) return dirFromLight.y > 0.0 ? 2u : 3u;
    return dirFromLight.z > 0.0 ? 4u : 5u;
}

// Unified mip-chain virtual page addressing (power-of-two pagesPerAxis).
uint VividVSMMipPagesPerAxis(uint pagesPerAxis, uint mipLevel)
{
    return max(pagesPerAxis >> mipLevel, 1u);
}

uint VividVSMMipTexelsPerAxis(uint pagesPerAxis, uint mipLevel, uint pageSize)
{
    return VividVSMMipPagesPerAxis(pagesPerAxis, mipLevel) * pageSize;
}

uint VividVSMMipEntryOffset(uint pagesPerAxis, uint mipLevel)
{
    uint mipAxis = pagesPerAxis >> mipLevel;
    return (4u * (pagesPerAxis * pagesPerAxis - mipAxis * mipAxis)) / 3u;
}

uint VividVSMTotalMipChainEntries(uint pagesPerAxis)
{
    return (4u * pagesPerAxis * pagesPerAxis - 1u) / 3u;
}

uint VividVSMCalcPageIndex(uint baseOffset, uint pagesPerAxis, bool isSinglePage, uint mipLevel, uint2 pageXY)
{
    if (isSinglePage) return baseOffset;
    uint mipAxis = pagesPerAxis >> mipLevel;
    return baseOffset + VividVSMMipEntryOffset(pagesPerAxis, mipLevel) + pageXY.y * mipAxis + pageXY.x;
}

// Structured physical slot descriptor (64-bit uint2) paired with uint PhysicalPageOwners[slot].
// word0: [0..15]=vsmId, [16..19]=mipLevel, [20..27]=flags, [28..31]=reserved
// word1: [0..15]=pageX, [16..31]=pageY
uint2 VividVSMEncodePhysicalOwnerDesc(uint vsmId, uint mipLevel, uint2 pageXY, uint flags)
{
    uint word0 = (vsmId & 0xFFFFu) | ((mipLevel & 0xFu) << 16u) | ((flags & 0xFFu) << 20u);
    uint word1 = (pageXY.x & 0xFFFFu) | ((pageXY.y & 0xFFFFu) << 16u);
    return uint2(word0, word1);
}

void VividVSMDecodePhysicalOwnerDesc(uint2 desc, out uint vsmId, out uint mipLevel, out uint2 pageXY, out uint flags)
{
    vsmId = desc.x & 0xFFFFu;
    mipLevel = (desc.x >> 16u) & 0xFu;
    flags = (desc.x >> 20u) & 0xFFu;
    pageXY = uint2(desc.y & 0xFFFFu, (desc.y >> 16u) & 0xFFFFu);
}

// 4-bit x 32 lights/pixel shadow mask packing with blue-noise dither (matches UE One-Pass Projection).
void VividVSMPackShadowMask4Bit(inout uint4 maskBits, float shadowFactor, uint lightSlot, float blueNoise)
{
    if (lightSlot >= VIVID_VSM_MAX_PACKED_LOCAL_LIGHTS) return;
    float dithered = saturate(shadowFactor + (blueNoise - 0.5) * (1.0 / 15.0));
    uint quantized = min((uint)floor(dithered * 15.0 + 0.5), 15u);
    uint wordIndex = lightSlot >> 3u;
    uint bitOffset = (lightSlot & 7u) * 4u;
    uint clearMask = ~(0xFu << bitOffset);
    uint shifted = quantized << bitOffset;
    if (wordIndex == 0u) maskBits.x = (maskBits.x & clearMask) | shifted;
    else if (wordIndex == 1u) maskBits.y = (maskBits.y & clearMask) | shifted;
    else if (wordIndex == 2u) maskBits.z = (maskBits.z & clearMask) | shifted;
    else maskBits.w = (maskBits.w & clearMask) | shifted;
}

float VividVSMUnpackShadowMask4Bit(uint4 maskBits, uint lightSlot)
{
    if (lightSlot >= VIVID_VSM_MAX_PACKED_LOCAL_LIGHTS) return 1.0;
    uint wordIndex = lightSlot >> 3u;
    uint bitOffset = (lightSlot & 7u) * 4u;
    uint word = wordIndex == 0u ? maskBits.x : (wordIndex == 1u ? maskBits.y : (wordIndex == 2u ? maskBits.z : maskBits.w));
    return (float)((word >> bitOffset) & 0xFu) * (1.0 / 15.0);
}

// Bounded HW raster windows. Keep RasterWindowPages in the runtime in sync.
#define VIVID_VSM_RASTER_WINDOW_PAGES 4u
#define VIVID_VSM_PAGE_WINDOW_FLAG 0x80000000u

// Front records keep z = virtual origin. w is either the single-page level or
// this tagged level/extent; back records retain the legacy min/max page ABI.
uint VividVSMEncodePageWindow(uint level, uint2 extent)
{
    return VIVID_VSM_PAGE_WINDOW_FLAG | level | (extent.x << 8u) | (extent.y << 12u);
}
uint2 VividVSMDecodePageWindow(uint tag)
{
    return (tag & VIVID_VSM_PAGE_WINDOW_FLAG) != 0u
        ? uint2((tag >> 8u) & 15u, (tag >> 12u) & 15u) : uint2(1u, 1u);
}

// Unified (VSMId, Mip) active-view window tag: [0..15]=activeViewIndex, [16..19]=extent.x, [20..23]=extent.y.
uint VividVSMEncodeUnifiedPageWindow(uint activeViewIndex, uint2 extent)
{
    return VIVID_VSM_PAGE_WINDOW_FLAG
        | (activeViewIndex & 0xFFFFu)
        | ((extent.x & 15u) << 16u)
        | ((extent.y & 15u) << 20u);
}

uint VividVSMDecodeUnifiedActiveView(uint tag)
{
    return tag & 0xFFFFu;
}

uint2 VividVSMDecodeUnifiedWindowExtent(uint tag)
{
    return (tag & VIVID_VSM_PAGE_WINDOW_FLAG) != 0u
        ? uint2((tag >> 16u) & 15u, (tag >> 20u) & 15u) : uint2(1u, 1u);
}

// UE VirtualShadowMapPageAccessCommon.ush page-table ABI. Cache metadata and
// physical owners retain their own slot+1 sentinel; these entries never do.
#define VIVID_VSM_PAGE_TABLE_VALID 0x80000000u
#define VIVID_VSM_PAGE_TABLE_RENDER 0x40000000u
#define VIVID_VSM_PAGE_TABLE_LOD_MASK 0x03f00000u
uint2 VividVSMPageTableAddress(uint entry) { return uint2(entry & 1023u, (entry >> 10u) & 1023u); }
uint VividVSMPageTableLOD(uint entry) { return (entry >> 20u) & 63u; }
bool VividVSMPageTableIsNative(uint entry)
{
    return (entry & (VIVID_VSM_PAGE_TABLE_VALID | VIVID_VSM_PAGE_TABLE_LOD_MASK)) == VIVID_VSM_PAGE_TABLE_VALID;
}
bool VividVSMPageTableIsRenderable(uint entry) { return (entry & VIVID_VSM_PAGE_TABLE_RENDER) != 0u; }
uint VividVSMPageTableSlot(uint entry, uint rowSize)
{
    uint2 address = VividVSMPageTableAddress(entry);
    return address.y * rowSize + address.x;
}
uint VividVSMEncodePageTable(uint2 address, uint lodOffset)
{
    return VIVID_VSM_PAGE_TABLE_VALID | (lodOffset << 20u) | (address.y << 10u) | address.x;
}
uint VividVSMEncodeNativePage(uint slot, uint rowSize, bool renderable)
{
    return VividVSMEncodePageTable(uint2(slot % rowSize, slot / rowSize), 0u)
        | (renderable ? VIVID_VSM_PAGE_TABLE_RENDER : 0u);
}

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
    // Keep both words scalar; dynamic vector indexing creates private arrays in DXC.
    uint word = cell.y < 4u ? mask.x : mask.y;
    return (word & (1u << ((cell.y & 3u) * 8u + cell.x))) != 0u;
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
