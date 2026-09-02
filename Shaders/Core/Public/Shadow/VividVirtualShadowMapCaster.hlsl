#ifndef VIVIDRP_VIRTUAL_SHADOW_MAP_CASTER_INCLUDED
#define VIVIDRP_VIRTUAL_SHADOW_MAP_CASTER_INCLUDED

#if defined(VIVID_VSM_CASTER)
RWTexture2D<uint> _VSMPrototypePhysicalPage : register(u0);
StructuredBuffer<uint> _VSMPrototypePageTable;
int _VSMPrototypePageSize;
int _VSMPrototypeVirtualResolution;
int _VSMPrototypePagesPerAxis;
int _VSMPrototypePhysicalPagesPerRow;

bool VividTryResolveVSMPhysicalTexel(
    float4 positionCS,
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
    const uint2 virtualTexel = min(
        uint2(positionCS.xy),
        virtualResolution - 1u);
    const uint2 virtualPage = virtualTexel / pageSize;
    const uint2 texelInPage = virtualTexel % pageSize;
    const uint pagesPerCascade = pagesPerAxis * pagesPerAxis;
    const uint pageTableIndex =
        (uint)_VividShadowCascadeIndex * pagesPerCascade
        + virtualPage.y * pagesPerAxis
        + virtualPage.x;
    const uint encodedPhysicalPage = _VSMPrototypePageTable[pageTableIndex];
    if (encodedPhysicalPage == 0u)
        return false;

    const uint physicalPageIndex = encodedPhysicalPage - 1u;
    const uint2 physicalPage = uint2(
        physicalPageIndex % physicalPagesPerRow,
        physicalPageIndex / physicalPagesPerRow);
    physicalTexel = physicalPage * pageSize + texelInPage;
    return true;
}

void VividWriteVSMDepth(float4 positionCS)
{
    uint2 physicalTexel;
    if (!VividTryResolveVSMPhysicalTexel(positionCS, physicalTexel))
        return;

    InterlockedMax(
        _VSMPrototypePhysicalPage[physicalTexel],
        asuint(saturate(positionCS.z)));
}
#else
void VividWriteVSMDepth(float4 positionCS)
{
}
#endif

#endif
