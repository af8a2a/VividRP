#ifndef VIVIDRP_VSM_LOCAL_PROJECTION_INCLUDED
#define VIVIDRP_VSM_LOCAL_PROJECTION_INCLUDED

// Phase 3: Unified Local Light (Point + Spot) One-Pass Projection & UE 5.7.4 Verbatim Local SMRT.
// Ported from ReferenceVSM/VirtualShadowMapProjectionSpot.ush & VirtualShadowMapSMRTTemplate.ush.
// Output: 4-bit x 32 local lights/pixel + blue-noise dithering packed into R32G32B32A32_UInt.

RWTexture2D<uint4> _VSMLocalShadowMaskBitsRW;

// x = rayCount, y = samplesPerRay, z = maxSlopeBiasLocal (dimensionless), w = cotMaxRayAngleFromLight
float4 _VSMLocalSMRTParameters;
// x = extrapolateMaxSlope (dimensionless), y = texelDitherScale, z = adaptiveRayCount, w = useSTBN (1/0)
float4 _VSMLocalSMRTSettings;

struct VSMLocalSampleResult
{
    bool bValid;
    float Depth;
    uint MipLevel;
    float MipLevelDim;
    float2 VirtualTexelAddressFloat;
    uint2 VirtualTexelAddress;
    uint2 PhysicalTexelAddress;
};

VSMLocalSampleResult SampleVirtualShadowMapLocal(uint vsmId, float2 shadowUV, uint requestedMipLevel)
{
    VSMLocalSampleResult result = (VSMLocalSampleResult)0;
    if (vsmId >= (uint)_VSMProjectionCount)
        return result;
    if (any(shadowUV < 0.0) || any(shadowUV >= 1.0))
        return result;

    VividVSMProjection proj = _VSMProjections[vsmId];
    uint pageSize = (uint)max(_VSMPrototypePageSize, 1);

    if (VividVSMProjectionIsSinglePage(proj))
    {
        uint pageIndex = proj.addressing.x;
        if (pageIndex >= (uint)_VSMPrototypePageTableEntryCount)
            return result;
        uint entry = _VSMPrototypePageTable[pageIndex];
        if ((entry & VIVID_VSM_PAGE_TABLE_VALID) == 0u)
            return result;

        uint2 physPage = VividVSMPageTableAddress(entry);
        float2 vTexelF = shadowUV * (float)pageSize;
        uint2 vTexel = min((uint2)vTexelF, pageSize - 1u);
        uint2 pTexel = physPage * pageSize + vTexel;
        uint rawDepth = _VSMPhysicalPagePool.Load(int4(pTexel, VIVID_VSM_FINAL_DEPTH_SLICE, 0));

        result.bValid = true;
        result.Depth = asfloat(rawDepth);
        result.MipLevel = 0u;
        result.MipLevelDim = (float)pageSize;
        result.VirtualTexelAddressFloat = vTexelF;
        result.VirtualTexelAddress = vTexel;
        result.PhysicalTexelAddress = pTexel;
        return result;
    }

    uint maxMip = max(proj.addressing.w, 1u) - 1u;
    uint minMip = min(proj.meta.z, maxMip);
    uint queryMip = clamp(requestedMipLevel, minMip, maxMip);
    uint queryPages = VividVSMMipPagesPerAxis(proj.addressing.z, queryMip);
    uint2 queryPage = min((uint2)(shadowUV * (float)queryPages), queryPages - 1u);
    uint pageIndex = VividVSMCalcProjectionPageIndex(proj, queryMip, queryPage);
    if (pageIndex >= (uint)_VSMPrototypePageTableEntryCount)
        return result;

    uint entry = _VSMPrototypePageTable[pageIndex];
    if ((entry & VIVID_VSM_PAGE_TABLE_VALID) == 0u)
        return result;

    uint lodOffset = VividVSMPageTableLOD(entry);
    uint resolvedMip = min(queryMip + lodOffset, maxMip);
    uint resolvedPages = VividVSMMipPagesPerAxis(proj.addressing.z, resolvedMip);
    uint mipRes = resolvedPages * pageSize;

    uint2 physPage = VividVSMPageTableAddress(entry);
    float2 vTexelF = shadowUV * (float)mipRes;
    uint2 vTexel = min((uint2)vTexelF, mipRes - 1u);
    uint2 pTexel = physPage * pageSize + (vTexel % pageSize);
    uint rawDepth = _VSMPhysicalPagePool.Load(int4(pTexel, VIVID_VSM_FINAL_DEPTH_SLICE, 0));

    result.bValid = true;
    result.Depth = asfloat(rawDepth);
    result.MipLevel = resolvedMip;
    result.MipLevelDim = (float)mipRes;
    result.VirtualTexelAddressFloat = vTexelF;
    result.VirtualTexelAddress = vTexel;
    result.PhysicalTexelAddress = pTexel;
    return result;
}

// UE GetTangentBasis(TangentZ) verbatim from MonteCarlo.ush / CapsuleLight.
float3x3 VSMLocalGetTangentBasis(float3 tangentZ)
{
    const float signVal = tangentZ.z >= 0.0 ? 1.0 : -1.0;
    const float a = -rcp(signVal + tangentZ.z);
    const float b = tangentZ.x * tangentZ.y * a;
    float3 tangentX = float3(1.0 + signVal * a * tangentZ.x * tangentZ.x, signVal * b, -signVal * tangentZ.x);
    float3 tangentY = float3(b, signVal + a * tangentZ.y * tangentZ.y, -tangentZ.y);
    return float3x3(tangentX, tangentY, tangentZ);
}

// Analytic tangent-plane depth slope d(z)/d(u,v) in shadow UV space.
// Mathematically identical to UE ComputeDepthSlopeLocalUV using TranslatedWorldToShadowUVNormalMatrix
// without adding a 64-byte matrix to the 256-byte VividVSMProjection ABI.
float2 ComputeDepthSlopeLocalUV(uint vsmId, float3 positionWS, float3 worldNormal)
{
    VividVSMProjection proj = _VSMProjections[vsmId];
    float4 c = mul(proj.worldToShadow, float4(positionWS, 1.0));
    if (abs(c.w) <= 1e-8)
        return float2(0.0, 0.0);

    float invW = rcp(c.w);
    float3 uvz = c.xyz * invW;
    float3x3 basis = VSMLocalGetTangentBasis(worldNormal);

    float4 dc1 = mul(proj.worldToShadow, float4(basis[0], 0.0));
    float4 dc2 = mul(proj.worldToShadow, float4(basis[1], 0.0));
    float3 duvz1 = (dc1.xyz - uvz * dc1.w) * invW;
    float3 duvz2 = (dc2.xyz - uvz * dc2.w) * invW;

    float det = duvz1.x * duvz2.y - duvz1.y * duvz2.x;
    if (abs(det) <= 1e-20)
        return float2(0.0, 0.0);

    float invDet = rcp(det);
    return float2(
        (duvz1.z * duvz2.y - duvz1.y * duvz2.z) * invDet,
        (duvz1.x * duvz2.z - duvz1.z * duvz2.x) * invDet);
}

// UE ComputeOptimalSlopeBiasLocal verbatim (VirtualShadowMapProjectionSpot.ush:29-49).
float ComputeOptimalSlopeBiasLocal(uint vsmId, float3 positionWS, float2 depthSlopeUV, uint minMipLevel)
{
    VividVSMProjection proj = _VSMProjections[vsmId];
    float4 shadowUVz = mul(proj.worldToShadow, float4(positionWS, 1.0));
    if (shadowUVz.w <= 1e-8)
        return 0.0;
    shadowUVz.xyz /= shadowUVz.w;

    VSMLocalSampleResult page = SampleVirtualShadowMapLocal(vsmId, shadowUVz.xy, max(minMipLevel, proj.meta.z));
    if (!page.bValid || page.MipLevelDim <= 0.0)
        return 0.0;

    float2 texelCenter = float2(page.VirtualTexelAddress) + 0.5;
    float2 texelCenterOffset = texelCenter - page.VirtualTexelAddressFloat;
    float2 texelCenterOffsetUV = texelCenterOffset / page.MipLevelDim;
    return 2.0 * max(0.0, dot(depthSlopeUV, texelCenterOffsetUV));
}

// UE ComputeShadowResolutionScale adapted to VividRP metres (0.1 cm -> 0.001 m).
float ComputeShadowResolutionScaleLocal(VividVSMProjection proj, float viewDepth)
{
    float screenPixelRadiusWorld = _VSMCameraViewParams.y > 0.5
        ? viewDepth * _VSMCameraViewParams.x
        : _VSMCameraViewParams.z;
    return max(0.001, screenPixelRadiusWorld * exp2(proj.parameters.z + _VSMCameraViewParams.w));
}

// UE VirtualShadowMapGetClampedRayDistanceScaleLocal verbatim (VirtualShadowMapProjectionSpot.ush:278-287).
float VirtualShadowMapGetClampedRayDistanceScaleLocal(float cosTheta)
{
    float sinTheta = sqrt(max(0.0, 1.0 - cosTheta * cosTheta));
    return 0.75 * saturate(1.5 / (cosTheta + _VSMLocalSMRTParameters.w * sinTheta));
}

// UE EvolveSobolSeed verbatim from RandomSequence.ush.
uint VSMLocalEvolveSobolSeed(uint seed)
{
    // PCG / LCG step matching UE's 32-bit Sobol seed evolution for step jitter decorrelation across rays.
    return seed * 1664525u + 1013904223u;
}

float4 VSMLocalGetRandomSample(uint2 pixelPos, uint frameIndex, uint rayIndex, uint maxRayCount)
{
    if (_VSMLocalSMRTSettings.w > 0.5)
        return VSMSMRTRandomSample(pixelPos, frameIndex, rayIndex, maxRayCount);

    uint seed = GetVSMStochasticSeed(pixelPos, frameIndex) ^ (rayIndex * 0x9e3779b9u);
    uint h0 = VSMStochasticHash(seed);
    uint h1 = VSMStochasticHash(h0 + 0x68bc21ebu);
    uint h2 = VSMStochasticHash(h1 + 0x9e3779b9u);
    uint h3 = VSMStochasticHash(h2 + 0x68bc21ebu);
    return float4(h0 >> 8, h1 >> 8, h2 >> 8, h3 >> 8) * (1.0 / 16777216.0);
}

float VSMLocalGetScalarNoise(uint2 pixelPos, uint frameIndex)
{
    if (_VSMLocalSMRTSettings.w > 0.5)
        return _VSMSTBNScalar.Load(int3(VSMSMRTNoiseAddress(pixelPos, frameIndex), 0));

    uint h = GetVSMStochasticSeed(pixelPos, frameIndex);
    return (float)(h >> 8) * (1.0 / 16777216.0);
}

// ============================================================================
// UE VirtualShadowMapProjectionSpot.ush: Single-Face & Dual-Cube-Face SMRT
// ============================================================================

struct VSMLocalSMRTSingleRayState
{
    uint VSMId;
    float3 RayStartUVz;
    float3 RayStepUVz;
    float ExtrapolateSlope;
    uint MinMipLevel;
    uint MipLevel;
    uint2 VirtualTexelAddress;
    uint2 PhysicalTexelAddress;
};

VSMLocalSMRTSingleRayState VSMLocalSMRTSingleRayInitialize(
    uint vsmId,
    float3 rayStartWS,
    float3 rayEndWS,
    float extrapolateSlope,
    float depthBias,
    uint mipLevel,
    bool bClampUVs)
{
    VividVSMProjection proj = _VSMProjections[vsmId];

    float4 rayStartUVz = mul(proj.worldToShadow, float4(rayStartWS, 1.0));
    float4 rayEndUVz   = mul(proj.worldToShadow, float4(rayEndWS, 1.0));

    rayStartUVz.xyz /= max(rayStartUVz.w, 1e-8);
    rayEndUVz.xyz   /= max(rayEndUVz.w, 1e-8);
    float3 rayStepUVz = rayEndUVz.xyz - rayStartUVz.xyz;

    // Offsets can move it slightly off the edge of spotlight projection. Clamp to edge.
    // Do NOT clamp for dual-face cubemap rays!
    if (bClampUVs)
    {
        rayStartUVz.xy = saturate(rayStartUVz.xy);
    }
    rayStartUVz.z += depthBias;

    VSMLocalSMRTSingleRayState result;
    result.VSMId = vsmId;
    result.RayStartUVz = rayStartUVz.xyz;
    result.RayStepUVz = rayStepUVz.xyz;
    result.ExtrapolateSlope = abs(extrapolateSlope * rayStepUVz.z);
    result.MinMipLevel = max(mipLevel, proj.meta.z);
    result.MipLevel = 0u;
    result.VirtualTexelAddress = uint2(0xFFFFFFFFu, 0xFFFFFFFFu);
    result.PhysicalTexelAddress = uint2(0xFFFFFFFFu, 0xFFFFFFFFu);
    return result;
}

VSMSMRTSample VSMSMRTFindSample(inout VSMLocalSMRTSingleRayState rayState, float sampleTime)
{
    float3 uvz = rayState.RayStartUVz + rayState.RayStepUVz * sampleTime;

    VSMSMRTSample sample = (VSMSMRTSample)0;
    sample.bValid = false;
    sample.ReferenceDepth = uvz.z;
    sample.ExtrapolateSlope = rayState.ExtrapolateSlope;

    if (all(uvz.xy == saturate(uvz.xy)))
    {
        VSMLocalSampleResult smSample = SampleVirtualShadowMapLocal(rayState.VSMId, uvz.xy, rayState.MinMipLevel);
        if (smSample.bValid)
        {
            sample.bValid = true;
            sample.SampleDepth = smSample.Depth;
            rayState.MipLevel = smSample.MipLevel;
            rayState.PhysicalTexelAddress = smSample.PhysicalTexelAddress;
            rayState.VirtualTexelAddress = smSample.VirtualTexelAddress;
        }
    }

    return sample;
}

// Instantiate VSMSMRTRayCast for VSMLocalSMRTSingleRayState (UE VirtualShadowMapSMRTTemplate.ush).
#define VIVID_SMRT_TEMPLATE_RAY_STRUCT VSMLocalSMRTSingleRayState
#include "../SMRT/VSMSMRTTraceTemplate.hlsl"
#undef VIVID_SMRT_TEMPLATE_RAY_STRUCT

struct VSMLocalSMRTTwoCubeFaceRayState
{
    VSMLocalSMRTSingleRayState Face0;
    VSMLocalSMRTSingleRayState Face1;
    bool bSampleInFace1;
};

VSMLocalSMRTTwoCubeFaceRayState VSMLocalSMRTTwoCubeFaceRayInitialize(
    uint vsmId0,
    uint vsmId1,
    float3 rayStartWS,
    float3 rayEndWS,
    float extrapolateSlope,
    float depthBias,
    uint mipLevel)
{
    VSMLocalSMRTTwoCubeFaceRayState result;
    result.Face0 = VSMLocalSMRTSingleRayInitialize(vsmId0, rayStartWS, rayEndWS, extrapolateSlope, depthBias, mipLevel, false);
    result.Face1 = VSMLocalSMRTSingleRayInitialize(vsmId1, rayStartWS, rayEndWS, extrapolateSlope, depthBias, mipLevel, false);
    result.bSampleInFace1 = true;
    return result;
}

VSMSMRTSample VSMSMRTFindSample(inout VSMLocalSMRTTwoCubeFaceRayState rayState, float sampleTime)
{
    // Traces from END to START, so start in Face1 and switch to Face0 on UV exit.
    VSMSMRTSample sample = (VSMSMRTSample)0;
    if (rayState.bSampleInFace1)
    {
        sample = VSMSMRTFindSample(rayState.Face1, sampleTime);
        if (!sample.bValid)
        {
            sample = VSMSMRTFindSample(rayState.Face0, sampleTime);
            sample.bResetExtrapolation = true;
            rayState.bSampleInFace1 = false;
        }
    }
    else
    {
        sample = VSMSMRTFindSample(rayState.Face0, sampleTime);
    }
    return sample;
}

// Instantiate VSMSMRTRayCast for VSMLocalSMRTTwoCubeFaceRayState (UE VirtualShadowMapSMRTTemplate.ush).
#define VIVID_SMRT_TEMPLATE_RAY_STRUCT VSMLocalSMRTTwoCubeFaceRayState
#include "../SMRT/VSMSMRTTraceTemplate.hlsl"
#undef VIVID_SMRT_TEMPLATE_RAY_STRUCT

VSMSMRTResult VSMShadowRayCastSpotLight(
    uint vsmId,
    float3 rayStartWS,
    float3 rayEndWS,
    float depthBias,
    uint mipLevel,
    int numSteps,
    float stepOffset,
    float extrapolateSlope,
    inout VSMLocalSMRTSingleRayState outRayState)
{
    outRayState = VSMLocalSMRTSingleRayInitialize(vsmId, rayStartWS, rayEndWS, extrapolateSlope, depthBias, mipLevel, true);
    return VSMSMRTRayCast(outRayState, numSteps, stepOffset);
}

VSMSMRTResult VSMShadowRayCastPointLight(
    uint baseVsmId,
    float3 lightPositionWS,
    float3 rayStartWS,
    float3 rayEndWS,
    float depthBias,
    uint mipLevel,
    int numSteps,
    float stepOffset,
    float extrapolateSlope,
    inout VSMLocalSMRTSingleRayState outRayState)
{
    uint startFaceId = baseVsmId + VividVSMGetCubeFace(rayStartWS - lightPositionWS);
    uint endFaceId   = baseVsmId + VividVSMGetCubeFace(rayEndWS - lightPositionWS);

    VSMSMRTResult result;
#if defined(PLATFORM_SUPPORTS_WAVE_INTRINSICS)
    bool anyCrossFace = WaveActiveAnyTrue(startFaceId != endFaceId);
#else
    bool anyCrossFace = (startFaceId != endFaceId);
#endif
    if (anyCrossFace)
    {
        VSMLocalSMRTTwoCubeFaceRayState rayState = VSMLocalSMRTTwoCubeFaceRayInitialize(
            startFaceId, endFaceId, rayStartWS, rayEndWS, extrapolateSlope, depthBias, mipLevel);
        result = VSMSMRTRayCast(rayState, numSteps, stepOffset);
        if (rayState.bSampleInFace1)
            outRayState = rayState.Face1;
        else
            outRayState = rayState.Face0;
    }
    else
    {
        // Fast path: ray stays on a single cube map face (effectively a spot light).
        outRayState = VSMLocalSMRTSingleRayInitialize(endFaceId, rayStartWS, rayEndWS, extrapolateSlope, depthBias, mipLevel, true);
        result = VSMSMRTRayCast(outRayState, numSteps, stepOffset);
    }
    return result;
}

// Single-sample hard local VSM test (used for Single-Page lights or when SMRT is disabled / point source).
float SampleLocalLightHardVSM(
    uint vsmId,
    float3 positionWS,
    float3 worldNormal,
    float viewDepth,
    uint mipLevel)
{
    VividVSMProjection proj = _VSMProjections[vsmId];
    float4 shadowUVz = mul(proj.worldToShadow, float4(positionWS, 1.0));
    if (shadowUVz.w <= 1e-8)
        return 1.0;
    shadowUVz.xyz /= shadowUVz.w;
    if (any(shadowUVz.xy < 0.0) || any(shadowUVz.xy >= 1.0) || shadowUVz.z <= 0.0 || shadowUVz.z >= 1.0)
        return 1.0;

    VSMLocalSampleResult smSample = SampleVirtualShadowMapLocal(vsmId, shadowUVz.xy, mipLevel);
    if (!smSample.bValid)
        return 1.0;

    float3 toLight = proj.selectionSphere.xyz - positionWS;
    float distSq = max(dot(toLight, toLight), 1e-8);
    float shadowResolutionScale = ComputeShadowResolutionScaleLocal(proj, viewDepth);
    float shadowDepthFunctionScale = abs(proj.projectionScales.w / distSq);
    float maxDepthBias = _VSMLocalSMRTParameters.z * shadowResolutionScale * shadowDepthFunctionScale;

    float depthBias = 0.0;
    if (maxDepthBias > 0.0 && smSample.MipLevelDim > 0.0)
    {
        float2 depthSlopeUV = ComputeDepthSlopeLocalUV(vsmId, positionWS, worldNormal);
        float2 texelCenterOffsetUV = (float2(smSample.VirtualTexelAddress) + 0.5 - smSample.VirtualTexelAddressFloat) / smSample.MipLevelDim;
        depthBias = min(maxDepthBias, 2.0 * max(0.0, dot(depthSlopeUV, texelCenterOffsetUV)));
    }

    // Reverse-Z: larger depth is closer to light.
    return (smSample.Depth > shadowUVz.z + depthBias) ? 0.0 : 1.0;
}

// UE TraceLocalLight verbatim (VirtualShadowMapProjectionSpot.ush:289-489).
float TraceLocalLightVSM(
    uint baseVsmId,
    bool isPointLight,
    bool isSinglePage,
    float3 lightPositionWS,
    float sourceRadius,
    uint2 pixelPos,
    float viewDepth,
    float3 positionWS,
    float rayStartOffset,
    float noise,
    float3 worldNormal)
{
    const float3 toLight = lightPositionWS - positionWS;
    const float distSq = max(dot(toLight, toLight), 1e-8);
    const float distToLight = sqrt(distSq);
    const float3 coneAxis = toLight / distToLight;
    const float coneSin = saturate(sourceRadius / distToLight);

    uint offsetVsmId = baseVsmId;
    if (isPointLight)
        offsetVsmId += VividVSMGetCubeFace(-toLight);
    if (offsetVsmId >= (uint)_VSMProjectionCount)
        return 1.0;

    VividVSMProjection proj = _VSMProjections[offsetVsmId];
    float4 receiverShadowUVz;
    uint mipLevel = isSinglePage ? 0u : VividVSMSelectLocalMipLevel(proj, positionWS, viewDepth, receiverShadowUVz);

    const uint maxRayCount = (uint)clamp((int)_VSMLocalSMRTParameters.x, 1, 16);
    uint samplesPerRay = (uint)clamp((int)_VSMLocalSMRTParameters.y, 0, 16);
    if (coneSin <= 1e-5)
        samplesPerRay = 0u;

    // Single-Page distant lights or hard-shadow settings use single-tap O(1) lookup.
    if (isSinglePage || (maxRayCount <= 1u && samplesPerRay == 0u))
        return SampleLocalLightHardVSM(offsetVsmId, positionWS, worldNormal, viewDepth, mipLevel);

    const float2 depthSlopeUV = ComputeDepthSlopeLocalUV(offsetVsmId, positionWS, worldNormal);
    const float shadowResolutionScale = ComputeShadowResolutionScaleLocal(proj, viewDepth);
    // Rough scaling from world units -> post projection depth (radially symmetric across cube faces).
    const float shadowDepthFunctionScale = abs(proj.projectionScales.w / distSq);

    const float projDitherScale = asfloat(proj.extended.x);
    const float ditherScale = (_VSMLocalSMRTSettings.y * projDitherScale) * shadowResolutionScale;
    const float maxDitherSlope = _VSMLocalSMRTParameters.z * ditherScale;
    const float maxDepthBias = _VSMLocalSMRTParameters.z * shadowResolutionScale * shadowDepthFunctionScale;

    const float3x3 coneTangentToWorldMatrix = VSMLocalGetTangentBasis(coneAxis);
    const float3 normalPlaneTangent = mul(coneTangentToWorldMatrix, worldNormal);
    const float denomZ = abs(normalPlaneTangent.z) > 1e-6 ? normalPlaneTangent.z : (normalPlaneTangent.z < 0.0 ? -1e-6 : 1e-6);
    const float2 depthSlopeTangent = -normalPlaneTangent.xy / denomZ;

    const uint adaptiveRayCount = (uint)max((int)_VSMLocalSMRTSettings.z, 0);
    uint i = 0u;
    uint rayMissCount = 0u;
    float stepOffset = noise;

    [loop]
    for (; i < maxRayCount; i++)
    {
        float4 randSample = VSMLocalGetRandomSample(pixelPos, (uint)_CSMFrameIndex, i, maxRayCount);

        float2 lightUV = VSMSMRTConcentricDisk(randSample.xy);
        lightUV *= coneSin;
        float sinTheta2 = dot(lightUV, lightUV);
        float cosTheta = sqrt(max(0.0, 1.0 - sinTheta2));
        float3 dir = mul(float3(lightUV, cosTheta), coneTangentToWorldMatrix);

        float3 rayStartWithDither = positionWS;
        if (ditherScale > 0.0)
        {
            float2 randomOffset = (randSample.zw - 0.5) * ditherScale;
            float offsetZ = min(maxDitherSlope, 2.0 * max(0.0, dot(depthSlopeTangent, randomOffset)));
            float3 offset = mul(float3(randomOffset, offsetZ), coneTangentToWorldMatrix);
            rayStartWithDither += offset;
        }

        float clampedEnd = distToLight * VirtualShadowMapGetClampedRayDistanceScaleLocal(cosTheta);
        float start = min(rayStartOffset, max(clampedEnd - 1e-6, 0.0));

        float3 rayStart = rayStartWithDither + dir * start;
        float3 rayEnd = rayStartWithDither + dir * clampedEnd;

        float depthBias = 0.0;
        if (maxDepthBias > 0.0)
        {
            depthBias = min(maxDepthBias, ComputeOptimalSlopeBiasLocal(offsetVsmId, rayStart, depthSlopeUV, mipLevel));
        }

        VSMSMRTResult smrtResult;
        VSMLocalSMRTSingleRayState rayState;
        if (!isPointLight)
        {
            smrtResult = VSMShadowRayCastSpotLight(
                baseVsmId,
                rayStart,
                rayEnd,
                depthBias,
                mipLevel,
                (int)samplesPerRay,
                stepOffset,
                _VSMLocalSMRTSettings.x,
                rayState);
        }
        else
        {
            smrtResult = VSMShadowRayCastPointLight(
                baseVsmId,
                lightPositionWS,
                rayStart,
                rayEnd,
                depthBias,
                mipLevel,
                (int)samplesPerRay,
                stepOffset,
                _VSMLocalSMRTSettings.x,
                rayState);
        }

        if (!smrtResult.bValidHit)
            ++rayMissCount;

#if defined(PLATFORM_SUPPORTS_WAVE_INTRINSICS)
        if (maxRayCount > 1u && adaptiveRayCount > 0u)
        {
            if (i == 0u)
            {
                if (WaveActiveAllTrue(!smrtResult.bValidHit))
                    break;
            }
            else if (i >= adaptiveRayCount)
            {
                if (WaveActiveAllTrue(rayMissCount == 0u))
                    break;
            }
        }
#endif

        uint seed = asuint(stepOffset);
        stepOffset = (float)(VSMLocalEvolveSobolSeed(seed) >> 8) * 5.96046447754e-08;
    }

    uint rayCount = min(i + 1u, maxRayCount);
    return (float)rayMissCount / (float)rayCount;
}

// ============================================================================
// Phase 3 Kernel: One-Pass Local Light Shadow Projection
// Packs up to 32 local lights/pixel (4-bit + blue-noise dither) into uint4.
// ============================================================================
[numthreads(8, 8, 1)]
void VSMResolveLocalShadows(uint3 id : SV_DispatchThreadID)
{
    uint2 pixelPos = VSMMortonPixel(id.xy);
    if (pixelPos.x >= (uint)_CSMOutputWidth || pixelPos.y >= (uint)_CSMOutputHeight)
        return;

    // Default all 32 4-bit slots to 15 (1.0 = unshadowed).
    uint4 maskBits = uint4(0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu);

    float depth = _DepthTexture.Load(int3(pixelPos, 0));
    if (IsSkyPixel(depth) || _VSMProjectionCount <= 0 || _VSMClusterCount <= 0)
    {
        _VSMLocalShadowMaskBitsRW[pixelPos] = maskBits;
        return;
    }

    float3 positionWS = ReconstructWorldPosition(pixelPos, depth);
    float3 normalWS = DecodeVividNormalOct(_GBuffer1.Load(int3(pixelPos, 0)).xy);
    float normLenSq = dot(normalWS, normalWS);
    normalWS = normLenSq > 1e-8 ? normalWS * rsqrt(normLenSq) : float3(0.0, 1.0, 0.0);

    float3 positionVS = mul(_VSMCameraViewMatrix, float4(positionWS, 1.0)).xyz;
    float viewDepth = max(-positionVS.z, max(_VSMClusterDepthParams.x, 0.0001));
    uint clusterIndex = VividVSMComputeClusterIndex(pixelPos, viewDepth);
    if (clusterIndex >= (uint)_VSMClusterCount)
    {
        _VSMLocalShadowMaskBitsRW[pixelPos] = maskBits;
        return;
    }

    // Apply UE receiver normal bias once in world space (consistent with directional resolve).
    float3 biasedPosWS = positionWS;
    if (_VSMReceiverOffsetParameters.x > 0.0)
    {
        float eyeDepth = dot(_VSMReceiverViewForward, float4(positionWS, 1.0));
        float normalBiasLen = VSMReceiverNormalBiasLength(positionWS - _WorldSpaceCameraPos, max(eyeDepth, viewDepth));
        biasedPosWS += normalWS * normalBiasLen;
    }

    uint packed = _VSMPrunedLightOffsetCount[clusterIndex];
    uint startOffset = packed & LIGHT_CLUSTER_PACKING_OFFSET_MASK;
    uint lightCount = min(
        (packed >> LIGHT_CLUSTER_PACKING_OFFSET_BITS) & LIGHT_CLUSTER_PACKING_COUNT_MASK,
        VIVID_VSM_MAX_PACKED_LOCAL_LIGHTS);

    float ditherNoise = VSMLocalGetScalarNoise(pixelPos, (uint)_CSMFrameIndex);

    [loop]
    for (uint slot = 0u; slot < lightCount; slot++)
    {
        uint lightIdx = _VSMPrunedLightIndices[startOffset + slot];
        if (lightIdx >= (uint)_VSMPunctualLightCount)
            continue;

        int2 vsmInfo = _VSMPunctualLightVSMInfo[lightIdx];
        if (vsmInfo.x < 0)
            continue;

        VSMPunctualLightData light = _VSMPunctualLights[lightIdx];
        bool isPointLight = light.lightType == VIVID_VSM_PUNCTUAL_TYPE_POINT;
        if (!isPointLight && light.lightType != VIVID_VSM_PUNCTUAL_TYPE_SPOT)
            continue;

        float3 toLight = light.positionWS - biasedPosWS;
        float distSq = dot(toLight, toLight);
        if (distSq > light.range * light.range || distSq <= 1e-8)
            continue;

        float invDist = rsqrt(distSq);
        if (!isPointLight)
        {
            float spotCos = dot(-toLight * invDist, light.directionWS);
            if (spotCos * light.angleScale + light.angleOffset <= 0.0)
                continue;
        }

        // Backfacing surfaces relative to the local light disc are fully shadowed (0.0).
        if (VividVSMIsBackfaceToLocalLight(toLight, distSq, invDist, normalWS, light.shapeRadiusSquared))
        {
            VividVSMPackShadowMask4Bit(maskBits, 0.0, slot, ditherNoise);
            continue;
        }

        bool isSinglePage = ((uint)vsmInfo.y & VIVID_VSM_PROJ_FLAG_SINGLE_PAGE) != 0u;
        float sourceRadius = sqrt(max(light.shapeRadiusSquared, 0.0));
        // Decorrelate per-light dither slightly while preserving blue-noise spectrum.
        float lightNoise = frac(ditherNoise + (float)slot * 0.61803398875);

        float shadowFactor = TraceLocalLightVSM(
            (uint)vsmInfo.x,
            isPointLight,
            isSinglePage,
            light.positionWS,
            sourceRadius,
            pixelPos,
            viewDepth,
            biasedPosWS,
            0.0,
            lightNoise,
            normalWS);

        VividVSMPackShadowMask4Bit(maskBits, shadowFactor, slot, lightNoise);
    }

    _VSMLocalShadowMaskBitsRW[pixelPos] = maskBits;
}

#endif
