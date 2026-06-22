#ifndef VIVIDRP_VIRTUAL_TEXTURE_INCLUDED
#define VIVIDRP_VIRTUAL_TEXTURE_INCLUDED

#ifndef VIVID_VT_MAX_MIPS
#define VIVID_VT_MAX_MIPS 16
#endif

StructuredBuffer<uint> _VTPageTable;
TEXTURE2D_ARRAY(_VTPhysicalCache);
SAMPLER(sampler_VTPhysicalCache);
float4 _VTLayerFallbacks[4];

#if defined(VIVID_VT_ENABLE_FEEDBACK_RW)
RWStructuredBuffer<uint2> _VTFeedbackRequests : register(u1);
RWStructuredBuffer<uint> _VTFeedbackCounter : register(u2);
#else
StructuredBuffer<uint2> _VTFeedbackRequests;
StructuredBuffer<uint> _VTFeedbackCounter;
#endif

float _VTSpaceParams[20];
float _VTMipOffsets[VIVID_VT_MAX_MIPS];
int _VTDebugMode;
int _VTFeedbackEnabled;
int _VTFeedbackFrameIndex;
int _VTFeedbackSampleRate;
float4 _VTFeedbackViewParams;

#define VT_SPACE_ID               ((int)_VTSpaceParams[0])
#define VT_PAGE_SIZE              ((int)_VTSpaceParams[1])
#define VT_BORDER_SIZE            ((int)_VTSpaceParams[2])
#define VT_PHYSICAL_PAGE_SIZE     ((int)_VTSpaceParams[3])
#define VT_VIRTUAL_PAGE_COUNT_X   ((int)_VTSpaceParams[4])
#define VT_VIRTUAL_PAGE_COUNT_Y   ((int)_VTSpaceParams[5])
#define VT_MIP_COUNT              ((int)_VTSpaceParams[6])
#define VT_CACHE_PAGE_COUNT       ((int)_VTSpaceParams[7])
#define VT_FEEDBACK_CAPACITY      ((int)_VTSpaceParams[8])
#define VT_PAGE_TABLE_ENTRY_COUNT ((int)_VTSpaceParams[9])
#define VT_PHYSICAL_PAGE_WIDTH    ((int)_VTSpaceParams[10])
#define VT_PHYSICAL_PAGE_HEIGHT   ((int)_VTSpaceParams[11])
#define VT_LAYER_COUNT            ((int)_VTSpaceParams[12])
#define VT_BASE_COLOR_LAYER       ((int)_VTSpaceParams[13])
#define VT_NORMAL_LAYER           ((int)_VTSpaceParams[14])
#define VT_MASK_LAYER             ((int)_VTSpaceParams[15])
#define VT_LAYER0_SRGB            ((int)_VTSpaceParams[16])
#define VT_LAYER1_SRGB            ((int)_VTSpaceParams[17])
#define VT_LAYER2_SRGB            ((int)_VTSpaceParams[18])
#define VT_LAYER3_SRGB            ((int)_VTSpaceParams[19])
#define VT_FEEDBACK_REQUEST_COUNTER_INDEX 0
#define VT_FEEDBACK_FALLBACK_SAMPLE_COUNTER_INDEX 1

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

struct VTMipRange
{
    float level;
    uint lowerMip;
    uint upperMip;
    float blend;
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

float2 VTComputePageLocalUv(float2 virtualUv, uint mip, uint2 pageCoord)
{
    float2 pageCount = float2(
        VTGetPageCount((uint)VT_VIRTUAL_PAGE_COUNT_X, mip),
        VTGetPageCount((uint)VT_VIRTUAL_PAGE_COUNT_Y, mip));
    float2 pageUv = saturate(virtualUv) * pageCount;
    return saturate(pageUv - float2(pageCoord.x, pageCoord.y));
}

uint VTGetFlatPageIndex(uint2 pageCoord, uint mip)
{
    uint pageCountX = VTGetPageCount((uint)VT_VIRTUAL_PAGE_COUNT_X, mip);
    return (uint)_VTMipOffsets[mip] + pageCoord.y * pageCountX + pageCoord.x;
}

float VTComputeRequestedMipLevel(float2 virtualUv)
{
    float2 virtualTexelCount = float2(
        max((float)(VT_VIRTUAL_PAGE_COUNT_X * VT_PAGE_SIZE), 1.0),
        max((float)(VT_VIRTUAL_PAGE_COUNT_Y * VT_PAGE_SIZE), 1.0));
    float2 dx = ddx(virtualUv * virtualTexelCount);
    float2 dy = ddy(virtualUv * virtualTexelCount);
    float rho = max(dot(dx, dx), dot(dy, dy));
    float requestedMip = 0.5 * log2(max(rho, 1e-8));
    return clamp(requestedMip, 0.0, (float)max(VT_MIP_COUNT - 1, 0));
}

uint VTComputeRequestedMip(float2 virtualUv)
{
    return (uint)floor(VTComputeRequestedMipLevel(virtualUv));
}

VTMipRange VTComputeRequestedMipRange(float2 virtualUv)
{
    float mipLevel = VTComputeRequestedMipLevel(virtualUv);
    uint maxMip = (uint)max(VT_MIP_COUNT - 1, 0);
    uint lowerMip = min((uint)floor(mipLevel), maxMip);
    uint upperMip = min(lowerMip + 1u, maxMip);

    VTMipRange range;
    range.level = mipLevel;
    range.lowerMip = lowerMip;
    range.upperMip = upperMip;
    range.blend = upperMip == lowerMip ? 0.0 : saturate(mipLevel - (float)lowerMip);
    return range;
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

float3 VTComputePhysicalUVWLayer(float2 virtualUv, VTResolvedAddress resolved, uint layerIndex)
{
    if (!resolved.valid)
        return float3(0.0, 0.0, 0.0);

    uint2 resolvedPageCoord = VTGetPageCoord(virtualUv, resolved.resolvedMip);
    float2 localUv = VTComputePageLocalUv(virtualUv, resolved.resolvedMip, resolvedPageCoord);
    // Hardware texture filtering already applies the half-texel center convention.
    // Address page edges on the gutter boundary so bilinear sampling spans baked borders.
    float2 texelCoord = localUv * VT_PAGE_SIZE + VT_BORDER_SIZE;
    float2 physicalUv = texelCoord / max((float)VT_PHYSICAL_PAGE_SIZE, 1.0);
    uint layerCount = max((uint)VT_LAYER_COUNT, 1u);
    uint physicalSlice = resolved.physicalPageId * layerCount + min(layerIndex, layerCount - 1u);
    return float3(physicalUv, (float)physicalSlice);
}

float3 VTComputePhysicalUVW(float2 virtualUv, VTResolvedAddress resolved)
{
    return VTComputePhysicalUVWLayer(virtualUv, resolved, 0u);
}

float4 VTSamplePhysicalCache(float2 virtualUv, VTResolvedAddress resolved)
{
    if (!resolved.valid)
        return float4(1.0, 0.0, 1.0, 1.0);

    float3 uvw = VTComputePhysicalUVW(virtualUv, resolved);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_VTPhysicalCache, sampler_VTPhysicalCache, uvw.xy, uvw.z, 0.0);
}

float4 VTGetLayerFallback(uint layerIndex)
{
    return _VTLayerFallbacks[min(layerIndex, 3u)];
}

int VTGetLayerSRGB(uint layerIndex)
{
    uint clampedLayer = min(layerIndex, 3u);
    if (clampedLayer == 0u)
        return VT_LAYER0_SRGB;
    if (clampedLayer == 1u)
        return VT_LAYER1_SRGB;
    if (clampedLayer == 2u)
        return VT_LAYER2_SRGB;

    return VT_LAYER3_SRGB;
}

float3 VTSRGBToLinear(float3 value)
{
    float3 low = value / 12.92;
    float3 high = pow((value + 0.055) / 1.055, 2.4);
    float3 useLow = step(value, float3(0.04045, 0.04045, 0.04045));
    return lerp(high, low, useLow);
}

float3 VTApplyLayerColorSpace(float3 value, uint layerIndex)
{
    return VTGetLayerSRGB(layerIndex) != 0 ? VTSRGBToLinear(saturate(value)) : value;
}

float4 VTSamplePhysicalCacheLayer(float2 virtualUv, VTResolvedAddress resolved, uint layerIndex)
{
    if (!resolved.valid)
        return VTGetLayerFallback(layerIndex);

    float3 uvw = VTComputePhysicalUVWLayer(virtualUv, resolved, layerIndex);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_VTPhysicalCache, sampler_VTPhysicalCache, uvw.xy, uvw.z, 0.0);
}

bool VTResolvedAddressMatches(VTResolvedAddress left, VTResolvedAddress right)
{
    return left.valid == right.valid
        && left.physicalPageId == right.physicalPageId
        && left.resolvedMip == right.resolvedMip
        && left.resident == right.resident
        && left.fallback == right.fallback
        && left.pendingUpload == right.pendingUpload
        && left.locked == right.locked;
}

float4 VTSamplePhysicalCacheTrilinear(
    float2 virtualUv,
    VTResolvedAddress lowerResolved,
    VTResolvedAddress upperResolved,
    float mipBlend)
{
    if (VTResolvedAddressMatches(lowerResolved, upperResolved))
        return VTSamplePhysicalCache(virtualUv, lowerResolved);

    float4 lowerColor = VTSamplePhysicalCache(virtualUv, lowerResolved);
    float4 upperColor = VTSamplePhysicalCache(virtualUv, upperResolved);

    if (!lowerResolved.valid)
        lowerColor = upperColor;
    if (!upperResolved.valid)
        upperColor = lowerColor;

    return lerp(lowerColor, upperColor, saturate(mipBlend));
}

float4 VTSamplePhysicalCacheTrilinearLayer(
    float2 virtualUv,
    VTResolvedAddress lowerResolved,
    VTResolvedAddress upperResolved,
    float mipBlend,
    uint layerIndex)
{
    if (VTResolvedAddressMatches(lowerResolved, upperResolved))
        return VTSamplePhysicalCacheLayer(virtualUv, lowerResolved, layerIndex);

    float4 lowerColor = VTSamplePhysicalCacheLayer(virtualUv, lowerResolved, layerIndex);
    float4 upperColor = VTSamplePhysicalCacheLayer(virtualUv, upperResolved, layerIndex);

    if (!lowerResolved.valid)
        lowerColor = upperColor;
    if (!upperResolved.valid)
        upperColor = lowerColor;

    return lerp(lowerColor, upperColor, saturate(mipBlend));
}

uint VTResolveLayerIndex(int configuredLayerIndex, uint fallbackLayerIndex)
{
    if (configuredLayerIndex < 0)
        return fallbackLayerIndex;

    uint layerIndex = (uint)configuredLayerIndex;
    return min(layerIndex, max((uint)VT_LAYER_COUNT, 1u) - 1u);
}

float4 VTSampleBaseColor(
    float2 virtualUv,
    VTResolvedAddress lowerResolved,
    VTResolvedAddress upperResolved,
    float mipBlend)
{
    uint layerIndex = VTResolveLayerIndex(VT_BASE_COLOR_LAYER, 0u);
    float4 color = VTSamplePhysicalCacheTrilinearLayer(
        virtualUv,
        lowerResolved,
        upperResolved,
        mipBlend,
        layerIndex);
    color.rgb = VTApplyLayerColorSpace(color.rgb, layerIndex);
    return color;
}

float3 VTSampleNormal(
    float2 virtualUv,
    VTResolvedAddress lowerResolved,
    VTResolvedAddress upperResolved,
    float mipBlend)
{
    if (VT_NORMAL_LAYER < 0)
        return float3(0.0, 0.0, 1.0);

    uint layerIndex = VTResolveLayerIndex(VT_NORMAL_LAYER, 0u);
    float3 encodedNormal = VTSamplePhysicalCacheTrilinearLayer(
        virtualUv,
        lowerResolved,
        upperResolved,
        mipBlend,
        layerIndex).xyz;
    return normalize(encodedNormal * 2.0 - 1.0);
}

float4 VTSampleMask(
    float2 virtualUv,
    VTResolvedAddress lowerResolved,
    VTResolvedAddress upperResolved,
    float mipBlend)
{
    if (VT_MASK_LAYER < 0)
        return float4(1.0, 1.0, 1.0, 1.0);

    uint layerIndex = VTResolveLayerIndex(VT_MASK_LAYER, 0u);
    return VTSamplePhysicalCacheTrilinearLayer(virtualUv, lowerResolved, upperResolved, mipBlend, layerIndex);
}

uint2 VTEncodeFeedbackKey(uint2 pageCoord, uint mip)
{
    uint low = (uint)(VT_SPACE_ID & 0xFFFF) | ((pageCoord.x & 0xFFFFu) << 16u);
    uint high = ((pageCoord.x >> 16u) & 0xFu) | ((pageCoord.y & 0xFFFFFu) << 4u) | ((mip & 0xFFu) << 24u);
    return uint2(low, high);
}

uint VTFeedbackHash(uint2 virtualTexel, uint mip)
{
    uint hash = virtualTexel.x * 73856093u;
    hash ^= virtualTexel.y * 19349663u;
    hash ^= mip * 83492791u;
    hash ^= (uint)max(_VTFeedbackFrameIndex, 0) * 2654435761u;
    return hash;
}

bool VTShouldWriteFeedback(float2 virtualUv, uint requestedMip)
{
    uint sampleRate = (uint)max(_VTFeedbackSampleRate, 1);
    if (sampleRate <= 1u)
        return true;

    uint2 virtualTexelCount = uint2(
        max(VT_VIRTUAL_PAGE_COUNT_X * VT_PAGE_SIZE, 1),
        max(VT_VIRTUAL_PAGE_COUNT_Y * VT_PAGE_SIZE, 1));
    float2 virtualTexelCountFloat = float2(
        (float)virtualTexelCount.x,
        (float)virtualTexelCount.y);
    uint2 virtualTexel = min(
        (uint2)(saturate(virtualUv) * virtualTexelCountFloat),
        virtualTexelCount - 1u);
    return (VTFeedbackHash(virtualTexel, requestedMip) % sampleRate) == 0u;
}

bool VTShouldWriteFeedback(float4 svPosition, float2 virtualUv, uint requestedMip)
{
    if (_VTFeedbackViewParams.w == 0.0)
        return VTShouldWriteFeedback(virtualUv, requestedMip);

    uint tileMask = (uint)max(_VTFeedbackViewParams.x, 0.0);
    uint tileShift = (uint)max(_VTFeedbackViewParams.y, 0.0);
    if (tileMask == 0u)
        return true;

    uint2 pixelTilePos = (uint2)svPosition.xy & tileMask;
    uint pixelTileIndex = (pixelTilePos.y << tileShift) + pixelTilePos.x;
    return pixelTileIndex == (uint)max(_VTFeedbackViewParams.z, 0.0);
}

void VTWriteFeedbackRequest(float2 virtualUv, uint clampedMip)
{
#if defined(VIVID_VT_ENABLE_FEEDBACK_RW)
    uint2 pageCoord = VTGetPageCoord(virtualUv, clampedMip);
    uint requestIndex = 0u;
    InterlockedAdd(_VTFeedbackCounter[VT_FEEDBACK_REQUEST_COUNTER_INDEX], 1u, requestIndex);
    if (requestIndex < (uint)VT_FEEDBACK_CAPACITY)
        _VTFeedbackRequests[requestIndex] = VTEncodeFeedbackKey(pageCoord, clampedMip);
#endif
}

void VTWriteFeedback(float2 virtualUv, uint requestedMip)
{
#if defined(VIVID_VT_ENABLE_FEEDBACK_RW)
    if (_VTFeedbackEnabled == 0)
        return;

    uint clampedMip = min(requestedMip, (uint)max(VT_MIP_COUNT - 1, 0));
    if (!VTShouldWriteFeedback(virtualUv, clampedMip))
        return;

    VTWriteFeedbackRequest(virtualUv, clampedMip);
#endif
}

void VTWriteFeedback(float2 virtualUv, uint requestedMip, float4 svPosition)
{
#if defined(VIVID_VT_ENABLE_FEEDBACK_RW)
    if (_VTFeedbackEnabled == 0)
        return;

    uint clampedMip = min(requestedMip, (uint)max(VT_MIP_COUNT - 1, 0));
    if (!VTShouldWriteFeedback(svPosition, virtualUv, clampedMip))
        return;

    VTWriteFeedbackRequest(virtualUv, clampedMip);
#endif
}

void VTWriteFallbackSample(VTResolvedAddress resolved)
{
#if defined(VIVID_VT_ENABLE_FEEDBACK_RW)
    if (_VTFeedbackEnabled == 0 || !resolved.fallback)
        return;

    uint previousFallbackSampleCount = 0u;
    InterlockedAdd(
        _VTFeedbackCounter[VT_FEEDBACK_FALLBACK_SAMPLE_COUNTER_INDEX],
        1u,
        previousFallbackSampleCount);
#endif
}

void VTWriteFallbackSampleWeighted(uint sampleRate)
{
#if defined(VIVID_VT_ENABLE_FEEDBACK_RW)
    uint previousFallbackSampleCount = 0u;
    InterlockedAdd(
        _VTFeedbackCounter[VT_FEEDBACK_FALLBACK_SAMPLE_COUNTER_INDEX],
        sampleRate,
        previousFallbackSampleCount);
#endif
}

void VTWriteFallbackSample(float2 virtualUv, uint requestedMip, VTResolvedAddress resolved)
{
#if defined(VIVID_VT_ENABLE_FEEDBACK_RW)
    if (_VTFeedbackEnabled == 0 || !resolved.fallback)
        return;

    uint clampedMip = min(requestedMip, (uint)max(VT_MIP_COUNT - 1, 0));
    uint sampleRate = (uint)max(_VTFeedbackSampleRate, 1);
    if (!VTShouldWriteFeedback(virtualUv, clampedMip))
        return;

    VTWriteFallbackSampleWeighted(sampleRate);
#endif
}

void VTWriteFallbackSample(float2 virtualUv, uint requestedMip, VTResolvedAddress resolved, float4 svPosition)
{
#if defined(VIVID_VT_ENABLE_FEEDBACK_RW)
    if (_VTFeedbackEnabled == 0 || !resolved.fallback)
        return;

    uint clampedMip = min(requestedMip, (uint)max(VT_MIP_COUNT - 1, 0));
    uint sampleRate = (uint)max(_VTFeedbackSampleRate, 1);
    if (!VTShouldWriteFeedback(svPosition, virtualUv, clampedMip))
        return;

    VTWriteFallbackSampleWeighted(sampleRate);
#endif
}

#endif
