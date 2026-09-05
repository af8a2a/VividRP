#ifndef VIVIDRP_VIRTUAL_SHADOW_MAP_CASTER_INCLUDED
#define VIVIDRP_VIRTUAL_SHADOW_MAP_CASTER_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Shadow/VividVirtualShadowMapAddressing.hlsl"

#if defined(VIVID_VSM_CASTER) || defined(VIVID_VSM_PAGE_CASTER)
RWTexture2D<uint> _VSMPrototypePhysicalPage : register(u0);
StructuredBuffer<uint> _VSMPrototypePageTable;
StructuredBuffer<uint4> _VSMPrototypePageMetadata;
int _VSMPrototypePageSize;
int _VSMPrototypeVirtualResolution;
int _VSMPrototypePagesPerAxis;
int _VSMPrototypePhysicalPagesPerRow;
int _VSMPrototypeCasterLayer;
int _VSMProjectionIndex;
float4 _VSMRasterOrigin;

static const uint kVividVSMPageDirty = 1u << 2;

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
    if (encodedPhysicalPage == 0u)
        return false;
    if (_VSMPrototypeCasterLayer == 0
        && (_VSMPrototypePageMetadata[pageTableIndex].x
            & kVividVSMPageDirty) == 0u)
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

void VividWriteVSMDepth(float4 positionCS, uint cascadeIndex)
{
    uint2 physicalTexel;
    if (!VividTryResolveVSMPhysicalTexel(
            positionCS,
            cascadeIndex,
            physicalTexel))
        return;

    InterlockedMax(
        _VSMPrototypePhysicalPage[physicalTexel],
        asuint(saturate(positionCS.z)));
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
    uint encodedPage = _VSMPrototypePageTable[virtualPageIndex];
    if (encodedPage == 0u)
        return;
    if (_VSMPrototypeCasterLayer == 0
        && (_VSMPrototypePageMetadata[virtualPageIndex].x & kVividVSMPageDirty) == 0u)
        return;
    uint slot = encodedPage - 1u;
    uint rowSize = (uint)_VSMPrototypePhysicalPagesPerRow;
    uint2 texel = uint2(slot % rowSize, slot / rowSize) * (uint)_VSMPrototypePageSize
        + (uint2)positionCS.xy;
    InterlockedMax(_VSMPrototypePhysicalPage[texel], asuint(saturate(positionCS.z)));
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
