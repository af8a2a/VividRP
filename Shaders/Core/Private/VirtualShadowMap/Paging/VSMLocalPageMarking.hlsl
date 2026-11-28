#ifndef VIVIDRP_VSM_LOCAL_PAGE_MARKING_INCLUDED
#define VIVIDRP_VSM_LOCAL_PAGE_MARKING_INCLUDED

#include "../../Lighting/ShaderConfig.cs.hlsl"

// Matches Lighting.hlsl::PunctualLightData (128 bytes) without pulling full lighting includes.
struct VSMPunctualLightData
{
    float3 positionWS;
    float range;
    float3 color;
    uint lightType;
    float3 directionWS;
    float angleScale;
    float3 rightWS;
    float angleOffset;
    float3 upWS;
    float shapeRadiusSquared;
    float2 projectorSize;
    float rangeAttenuationScale;
    float rangeAttenuationBias;
    float shadowStrength;
    uint renderingLayerMask;
    float volumetricDimmer;
    float volumetricShadowDimmer;
    float volumetricFadeDistance;
    uint affectVolumetric;
    float padding0;
    float padding1;
};

#define VIVID_VSM_PUNCTUAL_TYPE_POINT 0u
#define VIVID_VSM_PUNCTUAL_TYPE_SPOT 1u

// Matches Lighting.hlsl::AreaLightData (112 bytes) without pulling full lighting includes.
struct VSMAreaLightData
{
    float3 positionWS;
    float rangeAttenuationScale;
    float3 color;
    uint lightType;
    float3 forwardWS;
    float rangeAttenuationBias;
    float3 rightWS;
    float width;
    float3 upWS;
    float height;
    uint renderingLayerMask;
    float range;
    float cosBarnDoorAngle;
    float barnDoorLength;
    float volumetricDimmer;
    float volumetricShadowDimmer;
    float volumetricFadeDistance;
    uint affectVolumetric;
};

#define VIVID_VSM_AREA_TYPE_TUBE 0u
#define VIVID_VSM_AREA_TYPE_RECTANGLE 1u

StructuredBuffer<uint> _VSMLayeredOffset;
StructuredBuffer<uint> _VSMLayeredLightList;
StructuredBuffer<float> _VSMLogBaseBuffer;
StructuredBuffer<VSMPunctualLightData> _VSMPunctualLights;
StructuredBuffer<int2> _VSMPunctualLightVSMInfo; // x = baseVsmId (-1 if none), y = projection flags
StructuredBuffer<VSMAreaLightData> _VSMAreaLights;
StructuredBuffer<int4> _VSMAreaLightVSMInfo; // x = baseVsmId (-1 if none), y = projFlags, z = asint(shadowStrength), w = reserved

RWStructuredBuffer<uint> _VSMPrunedLightOffsetCountRW;
RWStructuredBuffer<uint> _VSMPrunedLightIndicesRW;
StructuredBuffer<uint> _VSMPrunedLightOffsetCount;
StructuredBuffer<uint> _VSMPrunedLightIndices;

int _VSMClusterCount;
int _VSMPunctualLightCount;
int _VSMAreaLightCount;
int _VSMPruneAreaCategoryEnabled;
int _VSMMarkCoarsePagesLocal;
// x = radiusScreen = min(1 / (W * P00), 1 / (H * P11)), y = isPerspective (1/0),
// z = orthoRadiusWorld, w = globalLocalLodBias
float4 _VSMCameraViewParams;
// x = tileSize, y = tileCountX, z = tileCountY, w = sliceCount
float4 _VSMClusterParams;
// x = nearPlane, y = farPlane, z = logBase, w = isLogBaseBufferEnabled
float4 _VSMClusterDepthParams;
float4x4 _VSMCameraViewMatrix;

// UE PruneLightGridCS: per-cluster filter keeping only VSM-enabled local lights,
// ordered with Full VSMs first and Single-Page (Distant) VSMs at the tail.
// Reuses the cluster's original startOffset in-place so no global atomics are needed.
[numthreads(64, 1, 1)]
void VSMPruneLightGridCS(uint3 id : SV_DispatchThreadID)
{
    uint clusterIndex = id.x;
    if (clusterIndex >= (uint)_VSMClusterCount)
        return;

    uint packed = _VSMLayeredOffset[clusterIndex];
    uint startOffset = packed & LIGHT_CLUSTER_PACKING_OFFSET_MASK;
    uint lightCount = (packed >> LIGHT_CLUSTER_PACKING_OFFSET_BITS) & LIGHT_CLUSTER_PACKING_COUNT_MASK;

    uint retained = 0u;
    uint firstWithVSM = lightCount;
    uint lastWithVSM = 0u;

    // Pass 1: retain Full VSM local lights (non-single-page).
    [loop]
    for (uint i = 0u; i < lightCount; i++)
    {
        uint lightIdx = _VSMLayeredLightList[startOffset + i];
        if (lightIdx >= (uint)_VSMPunctualLightCount)
            continue;
        int2 info = _VSMPunctualLightVSMInfo[lightIdx];
        if (info.x >= 0)
        {
            firstWithVSM = min(firstWithVSM, i);
            lastWithVSM = max(lastWithVSM, i);
            if ((info.y & VIVID_VSM_PROJ_FLAG_SINGLE_PAGE) == 0u)
                _VSMPrunedLightIndicesRW[startOffset + retained++] = lightIdx;
        }
    }

    // Pass 2: append Single-Page (Distant) VSM local lights after Full VSMs.
    if (firstWithVSM < lightCount)
    {
        [loop]
        for (uint i = firstWithVSM; i <= lastWithVSM; i++)
        {
            uint lightIdx = _VSMLayeredLightList[startOffset + i];
            if (lightIdx >= (uint)_VSMPunctualLightCount)
                continue;
            int2 info = _VSMPunctualLightVSMInfo[lightIdx];
            if (info.x >= 0 && (info.y & VIVID_VSM_PROJ_FLAG_SINGLE_PAGE) != 0u)
                _VSMPrunedLightIndicesRW[startOffset + retained++] = lightIdx;
        }
    }

    _VSMPrunedLightOffsetCountRW[clusterIndex] =
        (startOffset & LIGHT_CLUSTER_PACKING_OFFSET_MASK)
        | ((retained & LIGHT_CLUSTER_PACKING_COUNT_MASK) << LIGHT_CLUSTER_PACKING_OFFSET_BITS);

    // Optional Category 1 (LIGHTCATEGORY_AREA) pruning at [_VSMClusterCount + clusterIndex].
    if (_VSMPruneAreaCategoryEnabled != 0)
    {
        uint areaCellIdx = (uint)_VSMClusterCount + clusterIndex;
        uint areaPacked = _VSMLayeredOffset[areaCellIdx];
        uint areaStartOffset = areaPacked & LIGHT_CLUSTER_PACKING_OFFSET_MASK;
        uint areaLightCount = (areaPacked >> LIGHT_CLUSTER_PACKING_OFFSET_BITS) & LIGHT_CLUSTER_PACKING_COUNT_MASK;

        uint areaRetained = 0u;
        if (_VSMAreaLightCount > 0 && areaLightCount > 0u)
        {
            uint firstArea = areaLightCount;
            uint lastArea = 0u;
            [loop]
            for (uint i = 0u; i < areaLightCount; i++)
            {
                uint lightIdx = _VSMLayeredLightList[areaStartOffset + i];
                if (lightIdx >= (uint)_VSMAreaLightCount)
                    continue;
                int4 info = _VSMAreaLightVSMInfo[lightIdx];
                if (info.x >= 0)
                {
                    firstArea = min(firstArea, i);
                    lastArea = max(lastArea, i);
                    if (((uint)info.y & VIVID_VSM_PROJ_FLAG_SINGLE_PAGE) == 0u)
                        _VSMPrunedLightIndicesRW[areaStartOffset + areaRetained++] = lightIdx;
                }
            }
            if (firstArea < areaLightCount)
            {
                [loop]
                for (uint i = firstArea; i <= lastArea; i++)
                {
                    uint lightIdx = _VSMLayeredLightList[areaStartOffset + i];
                    if (lightIdx >= (uint)_VSMAreaLightCount)
                        continue;
                    int4 info = _VSMAreaLightVSMInfo[lightIdx];
                    if (info.x >= 0 && ((uint)info.y & VIVID_VSM_PROJ_FLAG_SINGLE_PAGE) != 0u)
                        _VSMPrunedLightIndicesRW[areaStartOffset + areaRetained++] = lightIdx;
                }
            }
        }
        _VSMPrunedLightOffsetCountRW[areaCellIdx] =
            (areaStartOffset & LIGHT_CLUSTER_PACKING_OFFSET_MASK)
            | ((areaRetained & LIGHT_CLUSTER_PACKING_COUNT_MASK) << LIGHT_CLUSTER_PACKING_OFFSET_BITS);
    }
}

// Clears local/unified page request flags and receiver masks without touching directional page pressure.
[numthreads(64, 1, 1)]
void VSMClearLocalPageRequests(uint3 id : SV_DispatchThreadID)
{
    uint pageIndex = id.x;
    if (pageIndex >= (uint)_VSMPrototypePageTableEntryCount)
        return;
    _VSMPageRequestFlags[pageIndex] = 0u;
    if (_VSMReceiverMaskEnabled != 0)
        _VSMPageReceiverMasks[pageIndex] = 0u;
}

void WriteVSMDirectPageRequest(uint pageIndex, uint requestFlags, uint2 receiverMask)
{
    if (pageIndex >= (uint)_VSMPrototypePageTableEntryCount)
        return;
    InterlockedOr(_VSMPageRequestFlags[pageIndex], requestFlags);
    if (_VSMReceiverMaskEnabled != 0)
    {
        if (receiverMask.x != 0u) InterlockedOr(_VSMPageReceiverMasks[pageIndex].x, receiverMask.x);
        if (receiverMask.y != 0u) InterlockedOr(_VSMPageReceiverMasks[pageIndex].y, receiverMask.y);
    }
}

// UE MarkCoarsePages generalized to Directional Clipmaps + Point/Spot Full & Single-Page VSMs.
// Dispatch: (DivRoundUp(_VSMProjectionCount, 64), 1, 1).
[numthreads(64, 1, 1)]
void VSMMarkCoarsePagesUnified(uint3 id : SV_DispatchThreadID)
{
    uint vsmId = id.x;
    if (vsmId >= (uint)_VSMProjectionCount || _VSMPrototypeRequestEnabled == 0)
        return;

    VividVSMProjection proj = _VSMProjections[vsmId];
    if ((proj.meta.y & VIVID_VSM_PROJ_FLAG_UNREFERENCED) != 0u)
        return;

    uint flags = kVSMPageRequested | kVSMPageCoarseRequested;
    if (VividVSMProjectionIsDirectional(proj))
    {
        if ((proj.meta.y & VIVID_VSM_PROJ_FLAG_IS_COARSE_CLIP_LEVEL) == 0u)
            return;
        uint axis = proj.addressing.z;
        float2 uv = mul(proj.worldToShadow, float4(proj.selectionSphere.xyz, 1.0)).xy;
        float2 center = uv * (float)axis - 0.5;
        int2 low = max((int2)floor(center), 0);
        int2 high = min((int2)ceil(center), (int)axis - 1);
        for (int y = low.y; y <= high.y; y++)
        for (int x = low.x; x <= high.x; x++)
        {
            uint pageIndex = VividVSMCalcProjectionPageIndex(proj, 0u, uint2((uint)x, (uint)y));
            WriteVSMDirectPageRequest(pageIndex, flags, 0xFFFFFFFFu);
        }
    }
    else if (VividVSMProjectionIsSinglePage(proj) || _VSMMarkCoarsePagesLocal != 0)
    {
        uint coarseMip = VividVSMProjectionIsSinglePage(proj) ? 0u : max(proj.addressing.w, 1u) - 1u;
        uint pageIndex = VividVSMCalcProjectionPageIndex(proj, coarseMip, uint2(0u, 0u));
        WriteVSMDirectPageRequest(pageIndex, flags, 0xFFFFFFFFu);
    }
}

uint VividVSMComputeClusterIndex(uint2 pixelCoord, float viewDepth)
{
    uint tileSize = max((uint)_VSMClusterParams.x, 1u);
    uint tileCountX = max((uint)_VSMClusterParams.y, 1u);
    uint tileCountY = max((uint)_VSMClusterParams.z, 1u);
    uint sliceCount = max((uint)_VSMClusterParams.w, 1u);
    uint2 tileCoord = uint2(
        min(pixelCoord.x / tileSize, tileCountX - 1u),
        min(pixelCoord.y / tileSize, tileCountY - 1u));

    uint sliceIndex = 0u;
    if (sliceCount > 1u)
    {
        float nearPlane = max(_VSMClusterDepthParams.x, 0.0001);
        float farPlane = max(_VSMClusterDepthParams.y, nearPlane + 0.0001);
        float depth = clamp(viewDepth, nearPlane, farPlane);
        float logBase = max(_VSMClusterDepthParams.z, 1.0001);
        if (_VSMClusterDepthParams.w > 0.5)
            logBase = max(_VSMLogBaseBuffer[tileCoord.y * tileCountX + tileCoord.x], logBase);
        float sliceCountF = (float)sliceCount;
        float t = saturate((depth - nearPlane) / (farPlane - nearPlane));
        float slice = log2(lerp(1.0, pow(logBase, sliceCountF), t)) / log2(logBase);
        sliceIndex = min((uint)max((int)slice, 0), sliceCount - 1u);
    }
    return (sliceIndex * tileCountY + tileCoord.y) * tileCountX + tileCoord.x;
}

bool VividVSMIsBackfaceToLocalLight(float3 toLight, float distSq, float invDist, float3 normalWS, float sourceRadiusSq)
{
    float sinAlpha = sqrt(saturate(sourceRadiusSq / (distSq + 1e-4)));
    return dot(normalWS, toLight * invDist) < -sinAlpha;
}

// Verbatim mirror of VividAreaLightCommon.hlsl::ApplyRectangularAreaLightBarnDoor.
void VSMApplyRectangularAreaLightBarnDoor(inout VSMAreaLightData areaLight, float3 positionWS)
{
    if (areaLight.lightType != VIVID_VSM_AREA_TYPE_RECTANGLE)
        return;
    if (areaLight.cosBarnDoorAngle <= 0.017 || areaLight.barnDoorLength <= 0.05)
        return;

    float halfWidth = areaLight.width * 0.5;
    float halfHeight = areaLight.height * 0.5;
    float3 pointToLight = positionWS - areaLight.positionWS;
    float3 pointLS = float3(
        dot(pointToLight, areaLight.rightWS),
        dot(pointToLight, areaLight.upWS),
        dot(pointToLight, areaLight.forwardWS));

    float maxDepth = areaLight.cosBarnDoorAngle * areaLight.barnDoorLength;
    float pointDepth = min(pointLS.z, maxDepth);
    float pointDepthRatio = pointDepth / max(maxDepth, 1e-5);
    float sinTheta = sqrt(saturate(1.0 - areaLight.cosBarnDoorAngle * areaLight.cosBarnDoorAngle));
    float barnDoorProjection = sinTheta * areaLight.barnDoorLength * pointDepthRatio;
    float2 pointSign = sign(pointLS.xy);
    pointLS.xy = pointSign * max(abs(pointLS.xy), float2(halfWidth, halfHeight) + barnDoorProjection.xx);

    float3 closestLightCorner = float3(
        pointSign.x * (halfWidth + barnDoorProjection),
        pointSign.y * (halfHeight + barnDoorProjection),
        pointDepth);
    float3 pointProjection = pointLS - closestLightCorner;
    float cosPhi = max(0.0, pointProjection.z);
    float2 tanPhi = cosPhi > 0.001
        ? abs(pointProjection.xy) / cosPhi
        : float2(99999.0, 99999.0);
    float2 projectionDistance = pointDepth * tanPhi;

    float2 topRight = float2(-halfWidth, halfWidth);
    float2 bottomLeft = float2(-halfHeight, halfHeight);
    topRight += (projectionDistance.x - barnDoorProjection) * float2(max(0.0, -pointSign.x), -max(0.0, pointSign.x));
    bottomLeft += (projectionDistance.y - barnDoorProjection) * float2(max(0.0, -pointSign.y), -max(0.0, pointSign.y));
    topRight = clamp(topRight, -halfWidth, halfWidth);
    bottomLeft = clamp(bottomLeft, -halfHeight, halfHeight);

    float2 lightCenterOffset = 0.5 * float2(topRight.x + topRight.y, bottomLeft.x + bottomLeft.y);
    areaLight.width = topRight.y - topRight.x;
    areaLight.height = bottomLeft.y - bottomLeft.x;
    areaLight.positionWS += areaLight.rightWS * lightCenterOffset.x + areaLight.upWS * lightCenterOffset.y;
}

// Exact analytical conservative backface test over all 4 rectangle corners (or 2 tube endpoints).
bool VividVSMIsBackfaceToAreaLight(VSMAreaLightData light, float3 positionWS, float3 normalWS)
{
    float centerDot = dot(normalWS, light.positionWS - positionWS);
    float halfW = 0.5 * max(light.width, 0.0);
    float halfH = light.lightType == VIVID_VSM_AREA_TYPE_TUBE ? 0.0 : 0.5 * max(light.height, 0.0);
    float maxCornerDot = centerDot + halfW * abs(dot(normalWS, light.rightWS)) + halfH * abs(dot(normalWS, light.upWS));
    return maxCornerDot <= 0.0;
}

// UE VirtualShadowMapCalcPixelFootprintLocal + GetMipLevelLocal.
uint VividVSMSelectLocalMipLevel(VividVSMProjection proj, float3 positionWS, float viewDepth, out float4 shadowUVz)
{
    shadowUVz = mul(proj.worldToShadow, float4(positionWS, 1.0));
    if (shadowUVz.w <= 1e-6)
        return proj.addressing.w;

    float radiusWorld = _VSMCameraViewParams.y > 0.5
        ? viewDepth * _VSMCameraViewParams.x
        : _VSMCameraViewParams.z;
    float virtualRes0 = (float)(proj.addressing.z * (uint)_VSMPrototypePageSize);
    float footprintTexels = abs(radiusWorld * proj.projectionScales.x / shadowUVz.w) * (2.0 * virtualRes0);
#if defined(VIVID_VSM_PAGE_PRESSURE)
    float pressureBias = _VSMLocalPagePressureEnabled != 0 ? asfloat(_VSMPagePressure[0].x) : 0.0;
#else
    float pressureBias = 0.0;
#endif
    float lod = log2(max(footprintTexels, 1.0)) + proj.parameters.z + _VSMCameraViewParams.w + pressureBias;
    int minMip = (int)proj.meta.z;
    int maxMip = max((int)proj.addressing.w - 1, minMip);
    return (uint)clamp((int)floor(lod), minMip, maxMip);
}

void VividVSMMarkLocalPage(VividVSMProjection proj, uint mipLevel, float4 shadowUVz, uint groupIndex)
{
    if (shadowUVz.w <= 1e-6 || mipLevel >= proj.addressing.w)
        return;
    float3 uvw = shadowUVz.xyz / shadowUVz.w;
    if (any(uvw.xy < 0.0) || any(uvw.xy >= 1.0) || uvw.z < 0.0 || uvw.z > 1.0)
        return;

    uint mipPages = VividVSMMipPagesPerAxis(proj.addressing.z, mipLevel);
    uint pageSize = (uint)_VSMPrototypePageSize;
    uint mipRes = mipPages * pageSize;
    float2 pagePos = uvw.xy * (float)mipPages;
    int2 lastPage = (int2)(mipPages - 1u);
    uint2 page = min((uint2)pagePos, (uint2)lastPage);

    uint flags = kVSMPageRequested | kVSMPagePrimaryRequested;
    uint2 mask = 0u;
    if ((proj.meta.y & VIVID_VSM_PROJ_FLAG_USE_RECEIVER_MASK) != 0u)
    {
        uint2 texel = min((uint2)(uvw.xy * (float)mipRes), mipRes - 1u) % pageSize;
        uint2 cell = min(texel * 8u / pageSize, 7u);
        uint bit = 1u << ((cell.y & 3u) * 8u + cell.x);
        mask = cell.y < 4u ? uint2(bit, 0u) : uint2(0u, bit);
    }

    WriteVSMDirectPageRequest(VividVSMCalcProjectionPageIndex(proj, mipLevel, page), flags, mask);

    // UE PageDilationBorderSizeLocal (0.05 page diagonal dither).
    float2 offset = 0.05 * float2((groupIndex & 1u) != 0u ? 1.0 : -1.0, (groupIndex & 2u) != 0u ? 1.0 : -1.0);
    uint2 positive = (uint2)clamp((int2)(pagePos + offset), 0, lastPage);
    uint2 negative = (uint2)clamp((int2)(pagePos - offset), 0, lastPage);
    if (any(positive != page))
        WriteVSMDirectPageRequest(VividVSMCalcProjectionPageIndex(proj, mipLevel, positive), flags, 0u);
    if (any(negative != page) && any(negative != positive))
        WriteVSMDirectPageRequest(VividVSMCalcProjectionPageIndex(proj, mipLevel, negative), flags, 0u);
}

#if defined(VIVID_VSM_MARK_RECEIVERS)
// Marks Full-VSM Point & Spot local lights from the pruned clustered light grid (2x2 pixel stride).
[numthreads(8, 8, 1)]
void VSMMarkLocalReceiverPages(uint3 id : SV_DispatchThreadID, uint groupIndex : SV_GroupIndex)
{
    if (_VSMPrototypeRequestEnabled == 0 || _VSMProjectionCount <= 0 || _VSMClusterCount <= 0)
        return;

    uint2 pixel = id.xy * 2u;
    if (pixel.x >= (uint)_CSMOutputWidth || pixel.y >= (uint)_CSMOutputHeight)
        return;

    float depth = _DepthTexture.Load(int3(pixel, 0));
    if (IsSkyPixel(depth))
        return;

    float3 positionWS = ReconstructWorldPosition(pixel, depth);
    float3 normalWS = _VSMCullBackfacingPixels != 0u
        ? DecodeVividNormalOct(_GBuffer1.Load(int3(pixel, 0)).xy)
        : float3(0.0, 1.0, 0.0);

    float3 positionVS = mul(_VSMCameraViewMatrix, float4(positionWS, 1.0)).xyz;
    float viewDepth = max(-positionVS.z, max(_VSMClusterDepthParams.x, 0.0001));
    uint clusterIndex = VividVSMComputeClusterIndex(pixel, viewDepth);
    if (clusterIndex >= (uint)_VSMClusterCount)
        return;

    uint packed = _VSMPrunedLightOffsetCount[clusterIndex];
    uint startOffset = packed & LIGHT_CLUSTER_PACKING_OFFSET_MASK;
    uint lightCount = (packed >> LIGHT_CLUSTER_PACKING_OFFSET_BITS) & LIGHT_CLUSTER_PACKING_COUNT_MASK;

    [loop]
    for (uint i = 0u; i < lightCount; i++)
    {
        uint lightIdx = _VSMPrunedLightIndices[startOffset + i];
        if (lightIdx >= (uint)_VSMPunctualLightCount)
            continue;

        int2 vsmInfo = _VSMPunctualLightVSMInfo[lightIdx];
        if (vsmInfo.x < 0)
            continue;

        // PruneLightGridCS sorted Single-Page lights to the tail; stop immediately on first hit.
        if ((vsmInfo.y & VIVID_VSM_PROJ_FLAG_SINGLE_PAGE) != 0u)
            break;

        VSMPunctualLightData light = _VSMPunctualLights[lightIdx];
        float3 toLight = light.positionWS - positionWS;
        float distSq = dot(toLight, toLight);
        if (distSq > light.range * light.range)
            continue;

        float invDist = rsqrt(max(distSq, 1e-12));
        if (light.lightType == VIVID_VSM_PUNCTUAL_TYPE_SPOT)
        {
            float spotCos = dot(-toLight * invDist, light.directionWS);
            if (spotCos * light.angleScale + light.angleOffset <= 0.0)
                continue;
        }
        else if (light.lightType != VIVID_VSM_PUNCTUAL_TYPE_POINT)
        {
            continue;
        }

        if (_VSMCullBackfacingPixels != 0u
            && VividVSMIsBackfaceToLocalLight(toLight, distSq, invDist, normalWS, light.shapeRadiusSquared))
            continue;

        uint vsmId = (uint)vsmInfo.x;
        if (light.lightType == VIVID_VSM_PUNCTUAL_TYPE_POINT)
            vsmId += VividVSMGetCubeFace(-toLight);
        if (vsmId >= (uint)_VSMProjectionCount)
            continue;

        VividVSMProjection proj = _VSMProjections[vsmId];
        float4 shadowUVz;
        uint mipLevel = VividVSMSelectLocalMipLevel(proj, positionWS, viewDepth, shadowUVz);
        VividVSMMarkLocalPage(proj, mipLevel, shadowUVz, groupIndex);
    }

    // Mark Full-VSM LTC AreaLights (Rectangle / Tube, 6-face cubemap) from pruned Category 1 list.
    if (_VSMAreaLightCount > 0)
    {
        uint areaPacked = _VSMPrunedLightOffsetCount[(uint)_VSMClusterCount + clusterIndex];
        uint areaStartOffset = areaPacked & LIGHT_CLUSTER_PACKING_OFFSET_MASK;
        uint areaLightCount = (areaPacked >> LIGHT_CLUSTER_PACKING_OFFSET_BITS) & LIGHT_CLUSTER_PACKING_COUNT_MASK;

        [loop]
        for (uint j = 0u; j < areaLightCount; j++)
        {
            uint areaIdx = _VSMPrunedLightIndices[areaStartOffset + j];
            if (areaIdx >= (uint)_VSMAreaLightCount)
                continue;

            int4 vsmInfo = _VSMAreaLightVSMInfo[areaIdx];
            if (vsmInfo.x < 0)
                continue;

            // PruneLightGridCS sorted Single-Page AreaLights to the tail; stop immediately on first hit.
            if (((uint)vsmInfo.y & VIVID_VSM_PROJ_FLAG_SINGLE_PAGE) != 0u)
                break;

            VSMAreaLightData area = _VSMAreaLights[areaIdx];
            float3 origCenterWS = area.positionWS;
            float3 fromCenter = positionWS - origCenterWS;
            bool isRect = area.lightType == VIVID_VSM_AREA_TYPE_RECTANGLE;
            if (!isRect && area.lightType != VIVID_VSM_AREA_TYPE_TUBE)
                continue;

            // Rectangle only illuminates its front hemisphere (+forwardWS).
            if (isRect && dot(fromCenter, area.forwardWS) <= 0.0)
                continue;

            // Check effective outer range before BarnDoor clip.
            float halfExtent = isRect
                ? 0.5 * sqrt(area.width * area.width + area.height * area.height)
                : 0.5 * area.width;
            float effRange = area.range + halfExtent;
            if (dot(fromCenter, fromCenter) > effRange * effRange)
                continue;

            VSMAreaLightData clippedArea = area;
            if (isRect)
            {
                VSMApplyRectangularAreaLightBarnDoor(clippedArea, positionWS);
                if (clippedArea.width <= 1e-4 || clippedArea.height <= 1e-4)
                    continue;
            }

            if (_VSMCullBackfacingPixels != 0u
                && VividVSMIsBackfaceToAreaLight(clippedArea, positionWS, normalWS))
                continue;

            uint vsmId = (uint)vsmInfo.x + VividVSMGetCubeFace(fromCenter);
            if (vsmId >= (uint)_VSMProjectionCount)
                continue;

            VividVSMProjection proj = _VSMProjections[vsmId];
            float4 shadowUVz;
            uint mipLevel = VividVSMSelectLocalMipLevel(proj, positionWS, viewDepth, shadowUVz);
            VividVSMMarkLocalPage(proj, mipLevel, shadowUVz, groupIndex);
        }
    }
}
#endif

#endif
