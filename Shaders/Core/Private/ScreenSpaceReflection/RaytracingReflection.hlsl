#pragma max_recursion_depth 1

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/SurfaceSummaryGBuffer.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/BlueNoise.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

#define VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS 0
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/IndirectDiffuse.hlsl"

#define SSR_MIN_GGX_ROUGHNESS 0.00001f
#define SSR_MAX_GGX_ROUGHNESS 0.99999f

RaytracingAccelerationStructure _AccelerationStructure;
StructuredBuffer<uint> _SSRHybridCandidateBuffer;
RWTexture2D<float4> _SSRTraceTexture;
RWTexture2D<float4> _SSRRayInfoTexture;
Texture2D<float> _DepthTexture;
Texture2D<float4> _GBuffer1;
Texture2D<float4> _PreviousColorPyramidTexture;
TEXTURECUBE(_SkyTexture);
SAMPLER(sampler_SkyTexture);

float4 _SkyTextureTint;
float4 _SkyTextureParams;
float4 _SsrTraceScreenSize;
float _SsrRoughnessFadeEnd;
float _SsrRoughnessFadeRcpLength;
float _SsrRoughnessFadeEndTimesRcpLength;
float _SsrEdgeFadeRcpLength;
float _SsrIntensity;
float _SsrIntensityClamp;
int _SsrReflectsSky;
int _SsrFrameIndex;
float4 _SsrHistoryColorPyramidSize;
float4 _SsrHistoryColorPyramidUvScaleAndLimit;
int _SsrUseHistoryColorPyramid;
int _SsrHistoryColorPyramidMaxMip;
float4 _SsrWorldSpaceCameraPos;
float4x4 _SsrViewProjMatrix;
float4x4 _SsrInvViewProjMatrix;
float4x4 _SsrPrevViewProjMatrix;
float _SsrHybridRayBias;
float _SsrPBRBias;

uint2 UnpackSsrHybridCandidateCoord(uint packedCoord)
{
    uint width = max((uint)_SsrTraceScreenSize.x, 1u);
    return uint2(packedCoord % width, packedCoord / width);
}

bool IsOutsideScreen(uint2 pixelCoord)
{
    return pixelCoord.x >= (uint)_SsrTraceScreenSize.x || pixelCoord.y >= (uint)_SsrTraceScreenSize.y;
}

float4 EmptySsrResult()
{
    return float4(0.0, 0.0, 0.0, 0.0);
}

void StoreEmptySsrTrace(uint2 coordSS)
{
    _SSRTraceTexture[coordSS] = EmptySsrResult();
    _SSRRayInfoTexture[coordSS] = EmptySsrResult();
}

bool IsRawFarDepth(float deviceDepth)
{
    return abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) <= 1e-5f;
}

float3 SanitizeSsrRadiance(float3 color)
{
    if (any(isnan(color)) || any(isinf(color)))
        return 0.0;

    return min(max(color, 0.0), _SsrIntensityClamp);
}

float3 DecodePreExposedHistoryRadiance(float3 color)
{
    if (any(isnan(color)) || any(isinf(color)))
        return 0.0;

    return ClampToFloat16Max(max(color * VividGetOneOverPreExposure(), 0.0));
}

float3 ComputeSsrWorldSpacePosition(float2 screenUV, float deviceDepth)
{
    return ComputeWorldSpacePosition(screenUV, deviceDepth, _SsrInvViewProjMatrix);
}

float3 ComputeSsrNormalizedDeviceCoordinatesWithZ(float3 positionWS)
{
    return ComputeNormalizedDeviceCoordinatesWithZ(positionWS, _SsrViewProjMatrix);
}

float3 LoadHistoryPyramidColor(int2 pixelCoord, int mipLevel)
{
    uint width;
    uint height;
    uint mipCount;
    _PreviousColorPyramidTexture.GetDimensions((uint)mipLevel, width, height, mipCount);

    int2 maxCoord = int2(max((int)width - 1, 0), max((int)height - 1, 0));
    pixelCoord = clamp(pixelCoord, int2(0, 0), maxCoord);
    return DecodePreExposedHistoryRadiance(_PreviousColorPyramidTexture.Load(int3(pixelCoord, mipLevel)).rgb);
}

float3 SampleHistoryPyramidBilinear(float2 screenUV, int mipLevel)
{
    uint width;
    uint height;
    uint mipCount;
    _PreviousColorPyramidTexture.GetDimensions((uint)mipLevel, width, height, mipCount);

    float2 mipSize = float2((float)max(width, 1u), (float)max(height, 1u));
    float2 pixelCoord = screenUV * mipSize - 0.5;
    int2 baseCoord = (int2)floor(pixelCoord);
    float2 factor = frac(pixelCoord);

    float3 c00 = LoadHistoryPyramidColor(baseCoord, mipLevel);
    float3 c10 = LoadHistoryPyramidColor(baseCoord + int2(1, 0), mipLevel);
    float3 c01 = LoadHistoryPyramidColor(baseCoord + int2(0, 1), mipLevel);
    float3 c11 = LoadHistoryPyramidColor(baseCoord + int2(1, 1), mipLevel);

    float3 cx0 = lerp(c00, c10, factor.x);
    float3 cx1 = lerp(c01, c11, factor.x);
    return lerp(cx0, cx1, factor.y);
}

int GetSsrHistoryColorPyramidMaxMip()
{
    uint width;
    uint height;
    uint mipCount;
    _PreviousColorPyramidTexture.GetDimensions((uint)0, width, height, mipCount);

    return min(max((int)mipCount - 1, 0), _SsrHistoryColorPyramidMaxMip);
}

float GetSsrHistoryColorPyramidMipLevel(float perceptualRoughness)
{
    return saturate(perceptualRoughness) * (float)GetSsrHistoryColorPyramidMaxMip();
}

float4 GetSsrHistoryColorPyramidUvScaleAndLimit()
{
    float4 uvScaleAndLimit = _SsrHistoryColorPyramidUvScaleAndLimit;
    if (all(uvScaleAndLimit.xy > 0.0) && all(uvScaleAndLimit.zw > 0.0))
    {
        uvScaleAndLimit.zw = min(uvScaleAndLimit.zw, uvScaleAndLimit.xy);
        return uvScaleAndLimit;
    }

    float2 uvScale = float2(1.0, 1.0);
    float2 uvLimit = uvScale - 0.5 * _SsrHistoryColorPyramidSize.zw;
    return float4(uvScale, saturate(uvLimit));
}

bool TryComputeSsrHistoryColorPyramidUV(float2 historyScreenUV, float mipLevel, out float2 historyPyramidUV)
{
    historyPyramidUV = 0.0;

    if (_SsrUseHistoryColorPyramid == 0 || any(_SsrHistoryColorPyramidSize.xy <= 0.0))
        return false;

    if (any(isnan(historyScreenUV)) || any(isinf(historyScreenUV)))
        return false;

    float4 uvScaleAndLimit = GetSsrHistoryColorPyramidUvScaleAndLimit();
    historyPyramidUV = historyScreenUV * uvScaleAndLimit.xy;

    float2 diffLimit = uvScaleAndLimit.xy - uvScaleAndLimit.zw;
    float2 diffLimitMipAdjusted = diffLimit * exp2(1.5 + ceil(abs(mipLevel)));
    float2 limit = uvScaleAndLimit.xy - diffLimitMipAdjusted;
    return all(historyPyramidUV >= 0.0) && all(historyPyramidUV <= limit);
}

bool TrySampleReflectionColor(float2 screenUV, float perceptualRoughness, out float3 color)
{
    color = 0.0;

    if (_SsrUseHistoryColorPyramid == 0)
        return false;

    int maxMip = GetSsrHistoryColorPyramidMaxMip();
    float lod = saturate(perceptualRoughness) * (float)maxMip;
    float2 pyramidUV;
    if (!TryComputeSsrHistoryColorPyramidUV(screenUV, lod, pyramidUV))
        return false;

    int mip0 = (int)floor(lod);
    int mip1 = min(mip0 + 1, maxMip);
    float factor = frac(lod);

    float3 color0 = SampleHistoryPyramidBilinear(pyramidUV, mip0);
    float3 color1 = SampleHistoryPyramidBilinear(pyramidUV, mip1);
    color = lerp(color0, color1, factor);
    return true;
}

float3 SampleReflectionColor(float2 screenUV, float perceptualRoughness)
{
    float3 color;
    if (TrySampleReflectionColor(screenUV, perceptualRoughness, color))
        return color;

    return 0.0;
}

float EdgeOfScreenFade(float2 screenUV)
{
    float2 edgeDistance = min(screenUV, 1.0 - screenUV);
    float fade = saturate(min(edgeDistance.x, edgeDistance.y) * _SsrEdgeFadeRcpLength);
    return fade * fade * (3.0 - 2.0 * fade);
}

bool TryComputeHistoryPyramidUVFromWorldPosition(
    float3 hitPositionWS,
    out float2 historyScreenUV,
    out float historyReliability,
    out float hitDeviceDepth)
{
    historyScreenUV = 0.0;
    historyReliability = 0.0;
    hitDeviceDepth = 0.0;

    if (_SsrUseHistoryColorPyramid == 0)
        return false;

    float3 currentNDC = ComputeSsrNormalizedDeviceCoordinatesWithZ(hitPositionWS);
    hitDeviceDepth = currentNDC.z;
    if (any(currentNDC.xy < 0.0) || any(currentNDC.xy > 1.0) || currentNDC.z < 0.0 || currentNDC.z > 1.0)
        return false;

    float3 previousNDC = ComputeNormalizedDeviceCoordinatesWithZ(hitPositionWS, _SsrPrevViewProjMatrix);
    historyScreenUV = previousNDC.xy;
    bool insideHistoryDepth = previousNDC.z >= 0.0 && previousNDC.z <= 1.0;
    if (any(historyScreenUV < 0.0) || any(historyScreenUV > 1.0))
        return false;

    historyReliability = EdgeOfScreenFade(historyScreenUV);
    historyScreenUV = saturate(historyScreenUV);
    return insideHistoryDepth;
}

float4 BuildSsrRayInfo(float hitDistance, float historyReliability, float deviceDepth, float contribution)
{
    if (hitDistance <= 0.0001 || contribution <= 0.0001)
        return EmptySsrResult();

    if (IsRawFarDepth(deviceDepth))
        return EmptySsrResult();

    float hitEyeDepth = LinearEyeDepth(deviceDepth, _ZBufferParams);
    if (isnan(hitEyeDepth) || isinf(hitEyeDepth) || hitEyeDepth <= 0.0)
        return EmptySsrResult();

    return float4(min(hitDistance, 65504.0), saturate(historyReliability), hitEyeDepth, saturate(contribution));
}

float PerceptualRoughnessFade(float perceptualRoughness)
{
    float t = saturate(_SsrRoughnessFadeEndTimesRcpLength - perceptualRoughness * _SsrRoughnessFadeRcpLength);
    return t * t * (3.0 - 2.0 * t);
}

float3 LoadNormalWS(uint2 coordSS)
{
    return VividDecodeSurfaceSummaryNormal(
        _GBuffer1.Load(int3(coordSS, 0)).xy);
}

float LoadPerceptualRoughness(uint2 coordSS)
{
    return saturate(_GBuffer1.Load(int3(coordSS, 0)).z);
}

bool HasSsrSkyTexture()
{
    return _SkyTextureParams.w > 0.5;
}

float3 RotateSsrSkyDirectionAroundYAxis(float3 directionWS, float rotationDegrees)
{
    float rotationRadians = radians(rotationDegrees);
    float s = 0.0;
    float c = 1.0;
    sincos(rotationRadians, s, c);

    return float3(
        c * directionWS.x - s * directionWS.z,
        directionWS.y,
        s * directionWS.x + c * directionWS.z);
}

float3 SampleSsrSkyFallback(float3 directionWS, float perceptualRoughness)
{
    if (_SsrReflectsSky == 0 || !HasSsrSkyTexture())
        return 0.0;

    float skyMipLevel = saturate(perceptualRoughness) * max(_SkyTextureParams.z, 0.0);
    float3 rotatedDirectionWS = RotateSsrSkyDirectionAroundYAxis(directionWS, _SkyTextureParams.y);
    float3 skyRadiance = max(
        SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_SkyTexture, rotatedDirectionWS, skyMipLevel).rgb
            * _SkyTextureTint.rgb
            * _SkyTextureParams.x,
        0.0);
    return SanitizeSsrRadiance(skyRadiance);
}

float4 BuildSsrSkyFallback(
    float3 reflectionDirWS,
    float perceptualRoughness,
    float roughnessFade)
{
    float3 reflectedColor = SampleSsrSkyFallback(reflectionDirWS, perceptualRoughness);
    if (all(reflectedColor <= 0.0))
        return float4(0.0, 0.0, 0.0, 0.0);

    float contribution = saturate(roughnessFade * _SsrIntensity);
    return float4(reflectedColor, contribution);
}

float3 ClampSsrRayTracedRadiance(float3 radiance)
{
    if (any(isnan(radiance)) || any(isinf(radiance)))
        return 0.0;

    return SanitizeSsrRadiance(radiance);
}

float2 GetSsrBlueNoiseSample(uint2 coordSS)
{
    return float2(
        GetBNDSequenceSample1SPPTemporal(coordSS, (uint)_SsrFrameIndex, 0),
        GetBNDSequenceSample1SPPTemporal(coordSS, (uint)_SsrFrameIndex, 1));
}

float GetSsrSampleWeight(float3 viewDirTS, float3 reflectionDirTS, float roughness)
{
    float lambdaVPlusOne = Lambda_GGX(roughness, viewDirTS) + 1.0;
    float lambdaL = Lambda_GGX(roughness, reflectionDirTS);
    return lambdaVPlusOne / (lambdaVPlusOne + lambdaL);
}

bool SampleSsrGGXVNDF(
    float roughness,
    float3x3 localToWorld,
    float3 viewDirWS,
    float2 inputSample,
    out float3 reflectionDirWS,
    out float sampleWeight)
{
    sampleWeight = 0.0;

    roughness = clamp(roughness, SSR_MIN_GGX_ROUGHNESS, SSR_MAX_GGX_ROUGHNESS);

    float VdotH;
    float3 localV;
    float3 localH;
    SampleGGXVisibleNormal(inputSample.xy, viewDirWS, localToWorld, roughness, localV, localH, VdotH);

    float3 localReflectionDir = 2.0 * VdotH * localH - localV;
    reflectionDirWS = mul(localReflectionDir, localToWorld);

    if (localReflectionDir.z < 0.001)
        return false;

    sampleWeight = GetSsrSampleWeight(localV, localReflectionDir, roughness);
    if (sampleWeight < 0.001)
        return false;

    return true;
}

float2 ApplySsrPBRBias(float2 sample, float roughness)
{
    roughness = max(roughness, SSR_MIN_GGX_ROUGHNESS);
    float coefBias = saturate(_SsrPBRBias) * rcp(roughness);
    sample.x = lerp(sample.x, 0.0, saturate(roughness * coefBias));
    return sample;
}

float3 SampleSsrReflectionDir(
    float3 normalWS,
    float3 viewDirWS,
    float perceptualRoughness,
    float2 blueNoiseSample,
    out float sampleWeight)
{
    float roughness = clamp(PerceptualRoughnessToRoughness(perceptualRoughness), SSR_MIN_GGX_ROUGHNESS, SSR_MAX_GGX_ROUGHNESS);
    blueNoiseSample = ApplySsrPBRBias(blueNoiseSample, roughness);
    float3x3 localToWorld = GetLocalFrame(normalWS);
    float3 reflectionDirWS = reflect(-viewDirWS, normalWS);

    if (!SampleSsrGGXVNDF(roughness, localToWorld, viewDirWS, blueNoiseSample, reflectionDirWS, sampleWeight))
        sampleWeight = 0.0;

    return normalize(reflectionDirWS);
}

[shader("raygeneration")]
void RayGenScreenSpaceReflectionsHybridTrace()
{
    uint candidateIndex = DispatchRaysIndex().x;
    uint2 coordSS = UnpackSsrHybridCandidateCoord(_SSRHybridCandidateBuffer[candidateIndex]);
    if (IsOutsideScreen(coordSS))
        return;

    float deviceDepth = _DepthTexture.Load(int3(coordSS, 0));
    if (IsRawFarDepth(deviceDepth))
    {
        StoreEmptySsrTrace(coordSS);
        return;
    }

    float perceptualRoughness = LoadPerceptualRoughness(coordSS);
    if (perceptualRoughness >= _SsrRoughnessFadeEnd)
    {
        StoreEmptySsrTrace(coordSS);
        return;
    }

    float roughnessFade = PerceptualRoughnessFade(perceptualRoughness);
    if (roughnessFade <= 0.0001)
    {
        StoreEmptySsrTrace(coordSS);
        return;
    }

    float2 screenUV = ((float2)coordSS + 0.5) * _SsrTraceScreenSize.zw;
    float3 positionWS = ComputeSsrWorldSpacePosition(screenUV, deviceDepth);
    float3 normalWS = LoadNormalWS(coordSS);
    float3 viewDirWS = normalize(_SsrWorldSpaceCameraPos.xyz - positionWS);

    if (any(isnan(positionWS)) || any(isinf(positionWS)) || any(isnan(normalWS)) || any(isinf(normalWS)))
    {
        StoreEmptySsrTrace(coordSS);
        return;
    }

    float sampleWeight;
    float2 blueNoiseSample = GetSsrBlueNoiseSample(coordSS);
    float3 reflectionDirWS = SampleSsrReflectionDir(normalWS, viewDirWS, perceptualRoughness, blueNoiseSample, sampleWeight);
    if (sampleWeight <= 0.0 || dot(normalWS, viewDirWS) <= 0.0)
    {
        StoreEmptySsrTrace(coordSS);
        return;
    }

    float rayBias = max(_SsrHybridRayBias, 0.0001);
    RayDesc rayDescriptor;
    rayDescriptor.Origin = positionWS + normalWS * rayBias;
    rayDescriptor.Direction = normalize(reflectionDirWS);
    rayDescriptor.TMin = rayBias;
    rayDescriptor.TMax = 100000.0;

    VividIndirectDiffusePayload payload;
    VividIndirectDiffuseInitializeVisibilityPayload(payload);
    TraceRay(_AccelerationStructure, RAY_FLAG_CULL_BACK_FACING_TRIANGLES, 0xFF, 0, 1, 0, rayDescriptor, payload);

    if (payload.hit == 0u || payload.signedHitDistance <= 0.0001)
    {
        float4 skyReflection = BuildSsrSkyFallback(reflectionDirWS, perceptualRoughness, roughnessFade);
        _SSRTraceTexture[coordSS] = skyReflection;
        _SSRRayInfoTexture[coordSS] = BuildSsrRayInfo(5000.0, 1.0, deviceDepth, skyReflection.a);
        return;
    }

    float hitDistance = payload.signedHitDistance;
    float3 hitPositionWS = positionWS + reflectionDirWS * hitDistance;
    float2 historyScreenUV;
    float historyReliability;
    float hitDeviceDepth;
    if (!TryComputeHistoryPyramidUVFromWorldPosition(hitPositionWS, historyScreenUV, historyReliability, hitDeviceDepth))
    {
        StoreEmptySsrTrace(coordSS);
        return;
    }

    float3 historyColor;
    if (!TrySampleReflectionColor(historyScreenUV, perceptualRoughness, historyColor))
    {
        StoreEmptySsrTrace(coordSS);
        return;
    }

    historyColor = SanitizeSsrRadiance(historyColor);
    float contribution = saturate(roughnessFade * _SsrIntensity * historyReliability);

    _SSRTraceTexture[coordSS] = float4(historyColor, contribution);
    _SSRRayInfoTexture[coordSS] = BuildSsrRayInfo(hitDistance, historyReliability, hitDeviceDepth, contribution);
}

[shader("raygeneration")]
void RayGenIntegration()
{

    uint3 launchIndex = DispatchRaysIndex();
    uint3 launchDim = DispatchRaysDimensions();
    uint2 coordSS = uint2(launchIndex.x, launchDim.y - launchIndex.y - 1u);
    if (IsOutsideScreen(coordSS))
        return;

    float deviceDepth = _DepthTexture.Load(int3(coordSS, 0));
    if (IsRawFarDepth(deviceDepth))
    {
        StoreEmptySsrTrace(coordSS);
        return;
    }

    float perceptualRoughness = LoadPerceptualRoughness(coordSS);
    if (perceptualRoughness >= _SsrRoughnessFadeEnd)
    {
        StoreEmptySsrTrace(coordSS);
        return;
    }

    float roughnessFade = PerceptualRoughnessFade(perceptualRoughness);
    if (roughnessFade <= 0.0001)
    {
        StoreEmptySsrTrace(coordSS);
        return;
    }

    float2 screenUV = ((float2)coordSS + 0.5) * _SsrTraceScreenSize.zw;
    float3 positionWS = ComputeSsrWorldSpacePosition(screenUV, deviceDepth);
    float3 normalWS = LoadNormalWS(coordSS);
    float3 viewDirWS = normalize(_SsrWorldSpaceCameraPos.xyz - positionWS);

    if (any(isnan(positionWS)) || any(isinf(positionWS)) || any(isnan(normalWS)) || any(isinf(normalWS)))
    {
        StoreEmptySsrTrace(coordSS);
        return;
    }

    float sampleWeight;
    float2 blueNoiseSample = GetSsrBlueNoiseSample(coordSS);
    float3 reflectionDirWS = SampleSsrReflectionDir(normalWS, viewDirWS, perceptualRoughness, blueNoiseSample, sampleWeight);
    if (sampleWeight <= 0.0 || dot(normalWS, viewDirWS) <= 0.0)
    {
        StoreEmptySsrTrace(coordSS);
        return;
    }

    float rayBias = max(_SsrHybridRayBias, 0.0001);
    RayDesc rayDescriptor;
    rayDescriptor.Origin = positionWS + normalWS * rayBias;
    rayDescriptor.Direction = normalize(reflectionDirWS);
    rayDescriptor.TMin = rayBias;
    rayDescriptor.TMax = 100000.0;

    VividIndirectDiffusePayload payload;
    VividIndirectDiffuseInitializeRadiancePayload(payload);
    TraceRay(_AccelerationStructure, RAY_FLAG_CULL_BACK_FACING_TRIANGLES, 0xFF, 0, 1, 0, rayDescriptor, payload);

    if (payload.hit == 0u || payload.signedHitDistance <= 0.0001)
    {
        float4 skyReflection = BuildSsrSkyFallback(reflectionDirWS, perceptualRoughness, roughnessFade);
        _SSRTraceTexture[coordSS] = skyReflection;
        _SSRRayInfoTexture[coordSS] = BuildSsrRayInfo(5000.0, 1.0, deviceDepth, skyReflection.a);
        return;
    }

    float hitDistance = payload.signedHitDistance;
    float3 radiance = payload.lightingRadiance.rgb + payload.emissionRadiance.rgb + payload.mainDirectionalRadiance.rgb;
    // radiance = ClampSsrRayTracedRadiance(radiance);

    float contribution = saturate(roughnessFade * _SsrIntensity * sampleWeight);
    _SSRTraceTexture[coordSS] = float4(radiance, contribution);
    _SSRRayInfoTexture[coordSS] = BuildSsrRayInfo(hitDistance, 1.0, deviceDepth, contribution);
}

[shader("miss")]
void MissScreenSpaceReflectionsHybrid(inout VividIndirectDiffusePayload payload : SV_RayPayload)
{
    payload.hit = 0u;
    payload.signedHitDistance = -1.0;
}
