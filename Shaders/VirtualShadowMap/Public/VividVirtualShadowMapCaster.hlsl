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

// Atomic insertion preserves the nearest distinct depths regardless of draw
// order. A displaced surface continues into the next layer; equal values must
// stop here so repeated triangles cannot consume the hidden-surface budget.
void VividInsertVSMDepth(uint2 texel, uint depth)
{
    for (uint layer = 0; layer < VIVID_VSM_DEPTH_LAYER_COUNT && depth != 0u; layer++)
    {
        uint previous;
        InterlockedMax(_VSMPrototypePhysicalPage[uint3(texel, layer)], depth, previous);
        if (previous == depth) break;
        depth = min(previous, depth);
    }
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
// Meshlet page draws target a physical-page-sized DSV layer. Use the request's
// virtual page identity, never reinterpret the local SV_Position as a virtual UV.
void VividWriteVSMPageDepth(float4 positionCS, uint virtualPageIndex)
{
    if (!VividVSMCasterReceiverTexel(virtualPageIndex, (uint2)positionCS.xy)) return;
    uint encodedPage = _VSMPrototypePageTable[virtualPageIndex];
    if (encodedPage == 0u)
        return;
    if ((_VSMPrototypePageMetadata[virtualPageIndex].x & kVividVSMPageDeferred) != 0u)
        return;
    if ((_VSMPrototypePageMetadata[virtualPageIndex].x
            & (_VSMPrototypeCasterLayer == 0 ? kVividVSMPageDirty : kVividVSMPageDynamicDirty)) == 0u)
        return;
    uint slot = encodedPage - 1u;
    uint rowSize = (uint)_VSMPrototypePhysicalPagesPerRow;
    uint2 texel = uint2(slot % rowSize, slot / rowSize) * (uint)_VSMPrototypePageSize
        + (uint2)positionCS.xy;
    VividInsertVSMDepth(texel, asuint(saturate(positionCS.z)));
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
