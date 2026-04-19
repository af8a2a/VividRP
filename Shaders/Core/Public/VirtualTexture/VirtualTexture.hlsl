#ifndef VIVIDRP_VIRTUAL_TEXTURE_INCLUDED
#define VIVIDRP_VIRTUAL_TEXTURE_INCLUDED

#ifndef VIVID_VT_MAX_MIPS
#define VIVID_VT_MAX_MIPS 16
#endif

StructuredBuffer<uint> _VTPageTable;
TEXTURE2D_ARRAY(_VTPhysicalCache);
SAMPLER(sampler_VTPhysicalCache);

#if defined(VIVID_VT_ENABLE_FEEDBACK_RW)
RWStructuredBuffer<uint2> _VTFeedbackRequests;
RWStructuredBuffer<uint> _VTFeedbackCounter;
#else
StructuredBuffer<uint2> _VTFeedbackRequests;
StructuredBuffer<uint> _VTFeedbackCounter;
#endif

int _VTSpaceParams[12];
int _VTMipOffsets[VIVID_VT_MAX_MIPS];

#define VT_SPACE_ID               _VTSpaceParams[0]
#define VT_PAGE_SIZE              _VTSpaceParams[1]
#define VT_BORDER_SIZE            _VTSpaceParams[2]
#define VT_PHYSICAL_PAGE_SIZE     _VTSpaceParams[3]
#define VT_VIRTUAL_PAGE_COUNT_X   _VTSpaceParams[4]
#define VT_VIRTUAL_PAGE_COUNT_Y   _VTSpaceParams[5]
#define VT_MIP_COUNT              _VTSpaceParams[6]
#define VT_CACHE_PAGE_COUNT       _VTSpaceParams[7]
#define VT_FEEDBACK_CAPACITY      _VTSpaceParams[8]
#define VT_PAGE_TABLE_ENTRY_COUNT _VTSpaceParams[9]

struct VTResolvedAddress
{
    uint physicalPageId;
    uint resolvedMip;
    bool resident;
    bool fallback;
    bool pendingUpload;
    bool locked;
    bool valid;
};

uint VTGetPageCount(uint baseCount, uint mip)
{
    return max(1u, baseCount >> mip);
}

uint2 VTGetPageCoord(float2 virtualUv, uint mip)
{
    uint pageCountX = VTGetPageCount((uint)VT_VIRTUAL_PAGE_COUNT_X, mip);
    uint pageCountY = VTGetPageCount((uint)VT_VIRTUAL_PAGE_COUNT_Y, mip);
    float2 clampedUv = saturate(virtualUv);
    uint2 pageCoord;
    pageCoord.x = min((uint)(clampedUv.x * pageCountX), pageCountX - 1u);
    pageCoord.y = min((uint)(clampedUv.y * pageCountY), pageCountY - 1u);
    return pageCoord;
}

uint VTGetFlatPageIndex(uint2 pageCoord, uint mip)
{
    uint pageCountX = VTGetPageCount((uint)VT_VIRTUAL_PAGE_COUNT_X, mip);
    return (uint)_VTMipOffsets[mip] + pageCoord.y * pageCountX + pageCoord.x;
}

VTResolvedAddress VTResolveAddress(float2 virtualUv, uint requestedMip)
{
    uint clampedMip = min(requestedMip, (uint)max(VT_MIP_COUNT - 1, 0));
    uint2 pageCoord = VTGetPageCoord(virtualUv, clampedMip);
    uint flatIndex = VTGetFlatPageIndex(pageCoord, clampedMip);
    uint packedEntry = _VTPageTable[flatIndex];

    VTResolvedAddress resolved;
    resolved.physicalPageId = packedEntry & 0xFFFFFu;
    resolved.resolvedMip = (packedEntry >> 20u) & 0x3Fu;
    resolved.resident = (packedEntry & (1u << 26u)) != 0u;
    resolved.fallback = (packedEntry & (1u << 27u)) != 0u;
    resolved.pendingUpload = (packedEntry & (1u << 28u)) != 0u;
    resolved.locked = (packedEntry & (1u << 29u)) != 0u;
    resolved.valid = resolved.resident || resolved.fallback;
    return resolved;
}

float3 VTComputePhysicalUVW(float2 virtualUv, VTResolvedAddress resolved)
{
    if (!resolved.valid)
        return float3(0.0, 0.0, 0.0);

    uint2 resolvedPageCoord = VTGetPageCoord(virtualUv, resolved.resolvedMip);
    float2 resolvedPageCount = float2(
        VTGetPageCount((uint)VT_VIRTUAL_PAGE_COUNT_X, resolved.resolvedMip),
        VTGetPageCount((uint)VT_VIRTUAL_PAGE_COUNT_Y, resolved.resolvedMip));
    float2 localUv = frac(saturate(virtualUv) * resolvedPageCount);
    float2 texelCoord = localUv * VT_PAGE_SIZE + VT_BORDER_SIZE + 0.5;
    float2 physicalUv = texelCoord / max((float)VT_PHYSICAL_PAGE_SIZE, 1.0);
    return float3(physicalUv, (float)resolved.physicalPageId);
}

uint2 VTEncodeFeedbackKey(uint2 pageCoord, uint mip)
{
    uint low = (uint)(VT_SPACE_ID & 0xFFFF) | ((pageCoord.x & 0xFFFFu) << 16u);
    uint high = ((pageCoord.x >> 16u) & 0xFu) | ((pageCoord.y & 0xFFFFFu) << 4u) | ((mip & 0xFFu) << 24u);
    return uint2(low, high);
}

void VTWriteFeedback(float2 virtualUv, uint requestedMip)
{
#if defined(VIVID_VT_ENABLE_FEEDBACK_RW)
    uint clampedMip = min(requestedMip, (uint)max(VT_MIP_COUNT - 1, 0));
    uint2 pageCoord = VTGetPageCoord(virtualUv, clampedMip);
    uint requestIndex = 0u;
    InterlockedAdd(_VTFeedbackCounter[0], 1u, requestIndex);
    if (requestIndex < (uint)VT_FEEDBACK_CAPACITY)
        _VTFeedbackRequests[requestIndex] = VTEncodeFeedbackKey(pageCoord, clampedMip);
#endif
}

#endif
