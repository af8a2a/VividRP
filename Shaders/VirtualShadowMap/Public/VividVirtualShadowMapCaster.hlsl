#ifndef VIVIDRP_VIRTUAL_SHADOW_MAP_CASTER_INCLUDED
#define VIVIDRP_VIRTUAL_SHADOW_MAP_CASTER_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/VirtualShadowMap/Public/VividVirtualShadowMapAddressing.hlsl"

#if defined(VIVID_VSM_CASTER) || defined(VIVID_VSM_PAGE_CASTER)
RWTexture2DArray<uint> _VSMPrototypePhysicalPage : register(u0);
StructuredBuffer<uint> _VSMPrototypePageTable;
StructuredBuffer<uint4> _VSMPrototypePageMetadata;
StructuredBuffer<uint2> _VSMPageReceiverMasks;
int _VSMReceiverMaskEnabled;
int _VSMPrototypePageSize;
int _VSMPrototypeVirtualResolution;
int _VSMPrototypePagesPerAxis;
int _VSMPrototypePhysicalPagesPerRow;
int _VSMPrototypeCasterLayer;
int _VSMProjectionIndex;
float4 _VSMRasterOrigin;

static const uint kVividVSMPageDirty = 1u << 2;
static const uint kVividVSMPageDynamicDirty = 1u << 15;
static const uint kVividVSMPageDeferred = 1u << 17;

bool VividVSMCasterReceiverTexel(uint page, uint2 texel)
{
    // Cached static pages always contain complete geometry.
    return _VSMReceiverMaskEnabled == 0 || _VSMPrototypeCasterLayer == 0
        || VividVSMReceiverMaskTexel(_VSMPageReceiverMasks[page], texel, (uint)_VSMPrototypePageSize);
}

bool VividVSMCasterReceiverSphere(uint page, float4 sphereWS, float4x4 worldToShadow)
{
    if (_VSMReceiverMaskEnabled == 0 || _VSMPrototypeCasterLayer == 0) return true;
    float4 center = mul(worldToShadow, float4(sphereWS.xyz, 1));
    float inverseW = rcp(max(abs(center.w), 1e-6));
    float2 radius = sphereWS.w * float2(length(worldToShadow[0].xyz), length(worldToShadow[1].xyz)) * inverseW;
    uint resolution = (uint)_VSMPrototypeVirtualResolution;
    uint2 low = VividVSMUVToVirtualTexel(center.xy * inverseW - radius, resolution);
    uint2 high = VividVSMUVToVirtualTexel(center.xy * inverseW + radius, resolution);
    uint axis = (uint)_VSMPrototypePagesPerAxis;
    uint2 coord = uint2(page % axis, (page / axis) % axis);
    return VividVSMReceiverMaskOverlapsRect(_VSMPageReceiverMasks[page], coord, low, high,
        (uint)_VSMPrototypePageSize);
}

bool VividTryResolveVSMPhysicalTexel(
    float4 positionCS,
    uint cascadeIndex,
    out uint2 physicalTexel)
{
    physicalTexel = 0u;

    const uint virtualResolution = (uint)max(
        _VSMPrototypeVirtualResolution,
        1);
    const uint pageSize = (uint)max(_VSMPrototypePageSize, 1);
    const uint pagesPerAxis = (uint)max(_VSMPrototypePagesPerAxis, 1);
    const uint physicalPagesPerRow = (uint)max(
        _VSMPrototypePhysicalPagesPerRow,
        1);
    const uint2 virtualTexel = VividVSMRasterPositionToVirtualTexel(
        positionCS.xy,
        virtualResolution);
    const uint2 virtualPage = virtualTexel / pageSize;
    const uint2 texelInPage = virtualTexel % pageSize;
    const uint pagesPerCascade = pagesPerAxis * pagesPerAxis;
    const uint pageTableIndex =
        cascadeIndex * pagesPerCascade
        + virtualPage.y * pagesPerAxis
        + virtualPage.x;
    const uint encodedPhysicalPage = _VSMPrototypePageTable[pageTableIndex];
    if (!VividVSMCasterReceiverTexel(pageTableIndex, texelInPage)) return false;
    if (encodedPhysicalPage == 0u)
        return false;
    if ((_VSMPrototypePageMetadata[pageTableIndex].x & kVividVSMPageDeferred) != 0u)
        return false;
    if ((_VSMPrototypePageMetadata[pageTableIndex].x
            & (_VSMPrototypeCasterLayer == 0 ? kVividVSMPageDirty : kVividVSMPageDynamicDirty)) == 0u)
    {
        return false;
    }

    const uint physicalPageIndex = encodedPhysicalPage - 1u;
    const uint2 physicalPage = uint2(
        physicalPageIndex % physicalPagesPerRow,
        physicalPageIndex / physicalPagesPerRow);
    physicalTexel = physicalPage * pageSize + texelInPage;
    return true;
}

// UE reversed-depth visibility: a single atomic max, no hidden-layer insertion.
void VividInsertVSMDepth(uint2 texel, uint depth)
{
    uint slice = _VSMPrototypeCasterLayer == 0
        ? VIVID_VSM_STATIC_DEPTH_SLICE : VIVID_VSM_FINAL_DEPTH_SLICE;
    InterlockedMax(_VSMPrototypePhysicalPage[uint3(texel, slice)], depth);
}

void VividWriteVSMDepth(float4 positionCS, uint cascadeIndex)
{
    uint2 physicalTexel;
    if (!VividTryResolveVSMPhysicalTexel(
            positionCS,
            cascadeIndex,
            physicalTexel))
        return;

    VividInsertVSMDepth(physicalTexel, asuint(saturate(positionCS.z)));
}

void VividWriteVSMDepth(float4 positionCS)
{
    // SV_Position is tile-local on the compatibility path, not virtual-map-local.
    positionCS.xy += _VSMRasterOrigin.xy;
    if (any(positionCS.xy >= (float)_VSMPrototypeVirtualResolution))
        return;
    VividWriteVSMDepth(positionCS, (uint)_VSMProjectionIndex);
}
// Resolve a page-local texel using the request identity, independent of the DSV.
bool VividTryResolveVSMPagePhysicalTexel(
    float4 positionCS, uint virtualPageIndex, out uint2 physicalTexel)
{
    physicalTexel = 0u;
    if (!VividVSMCasterReceiverTexel(virtualPageIndex, (uint2)positionCS.xy)) return false;
    uint encodedPage = _VSMPrototypePageTable[virtualPageIndex];
    if (encodedPage == 0u)
        return false;
    if ((_VSMPrototypePageMetadata[virtualPageIndex].x & kVividVSMPageDeferred) != 0u)
        return false;
    if ((_VSMPrototypePageMetadata[virtualPageIndex].x
            & (_VSMPrototypeCasterLayer == 0 ? kVividVSMPageDirty : kVividVSMPageDynamicDirty)) == 0u)
        return false;
    uint slot = encodedPage - 1u;
    uint rowSize = (uint)_VSMPrototypePhysicalPagesPerRow;
    physicalTexel = uint2(slot % rowSize, slot / rowSize) * (uint)_VSMPrototypePageSize
        + (uint2)positionCS.xy;
    return true;
}

// A window can contain unmapped, clean, deferred or masked holes. Resolve each
// fragment independently, after derivatives but before coverage and UAV writes.
bool VividTryResolveVSMWindowPhysicalTexel(float4 positionCS, uint originPage,
    uint2 extent, out uint2 physicalTexel)
{
    physicalTexel = 0u;
    uint pageSize = (uint)_VSMPrototypePageSize;
    if (any(positionCS.xy < 0.0) || any(positionCS.xy >= (float2)(extent * pageSize))) return false;
    uint2 localTexel = (uint2)positionCS.xy;
    uint2 pageOffset = localTexel / pageSize;
    uint axis = (uint)_VSMPrototypePagesPerAxis;
    uint2 origin = uint2(originPage % axis, (originPage / axis) % axis);
    if (any(origin + pageOffset >= axis)) return false;
    uint page = originPage + pageOffset.y * axis + pageOffset.x;
    if ((_VSMPrototypePageMetadata[page].x & (1u << 1u)) == 0u) return false;
    positionCS.xy = (float2)(localTexel % pageSize);
    return VividTryResolveVSMPagePhysicalTexel(positionCS, page, physicalTexel);
}

void VividWriteVSMPageDepth(float4 positionCS, uint virtualPageIndex)
{
    uint2 physicalTexel;
    if (VividTryResolveVSMPagePhysicalTexel(positionCS, virtualPageIndex, physicalTexel))
        VividInsertVSMDepth(physicalTexel, asuint(saturate(positionCS.z)));
}
#else
void VividWriteVSMDepth(float4 positionCS, uint cascadeIndex)
{
}

void VividWriteVSMDepth(float4 positionCS)
{
}
#endif

#endif
