#pragma max_recursion_depth 1

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingLightList.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingSegmentLight.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingSampling.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingAtmosphere.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCloud.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/NRD/REBLUR/VividReblurSignalEncoding.hlsli"

#if defined(VIVID_REFERENCE_PT_SER)
// This slot must match ReferencedPathTracingPass.ShaderExecutionReorderingUavSlot.
// u31 stays clear of the pass's twelve automatically allocated output UAVs.
#define NV_SHADER_EXTN_SLOT u31
#define NV_HITOBJECT_USE_MACRO_API
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/NVAPI/nvHLSLExtns.h"
#endif

RaytracingAccelerationStructure _AccelerationStructure;
RWTexture2D<float4> _ReferencedPathTracingRadiance;
RWTexture2D<float4> _ReferencedPathTracingAlbedo;
RWTexture2D<float4> _ReferencedPathTracingNormal;
RWTexture2D<float4> _ReferencedPathTracingDebugTexture;
RWTexture2D<float4> _ReferencedDiffuseRadianceHitDistance;
RWTexture2D<float4> _ReferencedSpecularRadianceHitDistance;
RWTexture2D<float4> _ReferencedPathTracingDirectLighting;
RWTexture2D<float4> _ReferencedPathTracingEmission;
RWTexture2D<float4> _ReferencedPathTracingEnvironmentDirectDiffuse;
RWTexture2D<float4> _ReferencedPathTracingEnvironmentDirectSpecular;
RWTexture2D<float4> _ReferencedDiffuseRayDirectionHitDistance;
RWTexture2D<float4> _ReferencedSpecularRayDirectionHitDistance;

float4 _CameraPositionWS;
float4x4 _PixelCoordToViewDirWS;
float4x4 _ReferencedWorldToView;
float4 _ReferencedCameraRightWS;
float4 _ReferencedCameraUpWS;
float4 _ReferencedCameraForwardWS;
// xy: anamorphic lens radii in world units, z: focus-plane distance,
// w: thin-lens transport enabled.
float4 _ReferencedPhysicalCameraParameters;
float _RayMinDistance;
float _RayMaxDistance;
int _ReferencedMaxBounceCount;
int _ReferencedRussianRouletteStartBounce;
int _ReferencedFrameIndex;
int _ReferencedSeed;
int _ReferencedPathSamplingMode;
float4 _ReferencedReblurHitDistanceParameters;
int _ReferencedReblurCheckerboardMode;

static const float kReferencedPathtracingShadowMinBias = 0.001;
static const float kReferencedPathtracingShadowMaxDistance = 100000.0;
// DXR requires a finite float, so FLT_MAX is the infinite-light visibility endpoint.
static const float kReferencedPathtracingInfiniteDistance = 3.402823466e+38;
static const uint kReferencedPathtracingMaxSupportedBounceCount = 8u;

float3 GetReferencedPathtracingPrimaryRayDirectionWS(float2 pixelCoord)
{
    float4 viewDirectionWS = mul(float4(pixelCoord, 1.0, 1.0), _PixelCoordToViewDirWS);
    return -normalize(viewDirectionWS.xyz);
}

void GetReferencedPathtracingPhysicalCameraRay(
    float2 pixelCoord,
    float2 lensDiskSample,
    out float3 rayOrigin,
    out float3 rayDirection)
{
    rayOrigin = _CameraPositionWS.xyz;
    rayDirection =
        GetReferencedPathtracingPrimaryRayDirectionWS(pixelCoord);
    if (_ReferencedPhysicalCameraParameters.w < 0.5)
        return;

    float3 cameraForward = normalize(_ReferencedCameraForwardWS.xyz);
    float focusProjection = dot(rayDirection, cameraForward);
    if (focusProjection <= 1e-6)
        return;

    float focusRayDistance =
        _ReferencedPhysicalCameraParameters.z / focusProjection;
    float3 focusPoint =
        _CameraPositionWS.xyz + rayDirection * focusRayDistance;
    float3 lensOffset =
        normalize(_ReferencedCameraRightWS.xyz)
            * lensDiskSample.x
            * _ReferencedPhysicalCameraParameters.x
        + normalize(_ReferencedCameraUpWS.xyz)
            * lensDiskSample.y
            * _ReferencedPhysicalCameraParameters.y;
    rayOrigin += lensOffset;
    rayDirection = normalize(focusPoint - rayOrigin);
}

bool IsFiniteReferencedPathtracingRadiance(float3 value)
{
    return !any(isnan(value)) && !any(isinf(value));
}

float MaxReferencedPathtracingComponent(float3 value)
{
    return max(value.x, max(value.y, value.z));
}

float GetReferencedPathtracingReblurNormHitDistance(
    float hitDistance,
    float viewZ,
    float linearRoughness)
{
    float4 parameters = _ReferencedReblurHitDistanceParameters;
    float normalization = (parameters.x + abs(viewZ) * parameters.y)
        * lerp(
            1.0,
            parameters.z,
            saturate(exp2(parameters.w * linearRoughness * linearRoughness)));
    return saturate(hitDistance / max(normalization, 1e-6));
}

float4 PackReferencedPathtracingReblurSignal(float3 radiance, float normalizedHitDistance)
{
    radiance = IsFiniteReferencedPathtracingRadiance(radiance)
        ? clamp(radiance, 0.0, 65504.0)
        : 0.0;
    normalizedHitDistance = !isnan(normalizedHitDistance) && !isinf(normalizedHitDistance)
        ? saturate(normalizedHitDistance)
        : 0.0;
    return float4(
        VividReblurEncodeRadiance(radiance),
        normalizedHitDistance);
}

static const float kReferencedPathtracingDlssInfiniteHitDistance = 65504.0;

float3 ResolveReferencedPathtracingDlssRayDirection(
    float3 directionWS,
    float3 fallbackDirectionWS)
{
    float directionLengthSquared = dot(directionWS, directionWS);
    if (IsFiniteReferencedPathtracingRadiance(directionWS)
        && directionLengthSquared > 1e-12)
    {
        return directionWS * rsqrt(directionLengthSquared);
    }

    float fallbackLengthSquared =
        dot(fallbackDirectionWS, fallbackDirectionWS);
    if (IsFiniteReferencedPathtracingRadiance(fallbackDirectionWS)
        && fallbackLengthSquared > 1e-12)
    {
        return fallbackDirectionWS * rsqrt(fallbackLengthSquared);
    }

    return float3(0.0, 0.0, 1.0);
}

float4 PackReferencedPathtracingDlssRayDirectionHitDistance(
    float3 directionWS,
    float hitDistance,
    float3 fallbackDirectionWS,
    bool hasPrimarySurface,
    bool hasFiniteHitDistance)
{
    // DLSS-RR consumes a dense world-space guide. Background pixels retain a
    // zero direction, while an unsampled lobe or a secondary miss uses the
    // largest representable FP16 value instead of camera far clip or zero.
    if (!hasPrimarySurface)
    {
        return float4(
            0.0,
            0.0,
            0.0,
            kReferencedPathtracingDlssInfiniteHitDistance);
    }

    float3 resolvedDirectionWS =
        ResolveReferencedPathtracingDlssRayDirection(
            directionWS,
            fallbackDirectionWS);
    bool hitDistanceIsFinite =
        !isnan(hitDistance)
        && !isinf(hitDistance)
        && hitDistance >= 0.0;
    float resolvedHitDistance =
        hasFiniteHitDistance && hitDistanceIsFinite
            ? min(
                hitDistance,
                kReferencedPathtracingDlssInfiniteHitDistance)
            : kReferencedPathtracingDlssInfiniteHitDistance;
    return float4(resolvedDirectionWS, resolvedHitDistance);
}

float GetReferencedPathtracingDenoiserLuminance(float3 radiance)
{
    return dot(max(radiance, 0.0), float3(0.2126, 0.7152, 0.0722));
}

float CombineReferencedPathtracingDenoiserHitDistance(
    float3 currentRadiance,
    float currentHitDistance,
    float3 addedRadiance,
    float addedHitDistance)
{
    // Match RTXPT stable-plane accumulation: when multiple estimators share one REBLUR
    // signal, hitT represents their radiance-weighted source distance.
    float addedLuminance =
        GetReferencedPathtracingDenoiserLuminance(addedRadiance);
    if (addedLuminance < 1e-5)
        return currentHitDistance;

    float currentLuminance =
        GetReferencedPathtracingDenoiserLuminance(currentRadiance);
    float addedWeight =
        addedLuminance / max(currentLuminance + addedLuminance, 1e-6);
    return lerp(
        abs(currentHitDistance),
        max(addedHitDistance, 0.0),
        saturate(addedWeight));
}

float3 OffsetReferencedPathtracingRayOrigin(
    float3 positionWS,
    float3 faceNormalWS,
    float3 rayDirectionWS,
    out float rayBias)
{
    float positionScale = max(max(abs(positionWS.x), abs(positionWS.y)), abs(positionWS.z));
    rayBias = max(kReferencedPathtracingShadowMinBias, positionScale * 0.00001);
    float offsetSign = dot(faceNormalWS, rayDirectionWS) >= 0.0 ? 1.0 : -1.0;
    return positionWS + faceNormalWS * (rayBias * offsetSign);
}

float3 TraceReferencedPathtracingVisibility(
    float3 positionWS,
    float3 faceNormalWS,
    float3 lightDirectionWS,
    float maximumDistance,
    uint stochasticAlphaSeed)
{
    RayDesc shadowRay;
    float shadowBias;
    shadowRay.Origin = OffsetReferencedPathtracingRayOrigin(
        positionWS,
        faceNormalWS,
        lightDirectionWS,
        shadowBias);
    shadowRay.Direction = lightDirectionWS;
    shadowRay.TMin = shadowBias;
    shadowRay.TMax = max(maximumDistance - shadowBias, shadowBias);
    if (shadowRay.TMax <= shadowRay.TMin)
        return 1.0;

    ReferencedPathtracingPayload visibilityPayload;
    InitializeReferencedPathtracingPayload(visibilityPayload);
    visibilityPayload.stochasticAlphaSeed = stochasticAlphaSeed;
    // With the closest-hit shader skipped, this value survives any hit; the miss shader clears it.
    visibilityPayload.hit = 1u;
    TraceRay(
        _AccelerationStructure,
        RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH
            | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER,
        0xFF,
        0,
        1,
        0,
        shadowRay,
        visibilityPayload);

    return visibilityPayload.hit == 0u
        ? visibilityPayload.stochasticTransparencyWeight
        : 0.0;
}

float3 TraceReferencedPathtracingCandidateVisibility(
    float3 positionWS,
    float3 faceNormalWS,
    float3 lightDirectionWS,
    float lightDistance,
    bool includeVirtualGround,
    float shadowStrength,
    uint stochasticAlphaSeed)
{
    shadowStrength = saturate(shadowStrength);
    float3 tracedVisibility = shadowStrength > 0.0
        ? TraceReferencedPathtracingVisibility(
            positionWS,
            faceNormalWS,
            lightDirectionWS,
            lightDistance,
            stochasticAlphaSeed)
        : 1.0;
    float geometryVisibility =
        lerp(1.0, tracedVisibility, shadowStrength);

    float shadowBias;
    float3 shadowOriginWS =
        OffsetReferencedPathtracingRayOrigin(
            positionWS,
            faceNormalWS,
            lightDirectionWS,
            shadowBias);
    float3 atmosphereTransmittance = 1.0;
    float3 cloudTransmittance = 1.0;
    if (ReferencedPathtracingHasReferenceAtmosphere())
    {
        ReferencedPathtracingAtmosphereRayInterval shadowInterval;
        if (ReferencedPathtracingIntersectAtmosphereWithGroundPolicy(
                shadowOriginWS,
                lightDirectionWS,
                max(
                    lightDistance - shadowBias,
                    shadowBias),
                includeVirtualGround,
                shadowInterval)
            && shadowInterval.hitsGround != 0u)
        {
            return 0.0;
        }

        atmosphereTransmittance =
            ReferencedPathtracingEvaluateAtmosphereTransmittanceWithGroundPolicy(
                shadowOriginWS,
                lightDirectionWS,
                max(
                    lightDistance - shadowBias,
                    shadowBias),
                includeVirtualGround);
        cloudTransmittance =
            ReferencedPathtracingEvaluateCloudTransmittance(
                shadowOriginWS,
                lightDirectionWS,
                max(
                    lightDistance - shadowBias,
                    shadowBias));
    }
    return atmosphereTransmittance
        * cloudTransmittance
        * geometryVisibility;
}

void TraceReferencedPathtracingSurface(
    RayDesc ray,
    inout ReferencedPathtracingPayload payload)
{
#if defined(VIVID_REFERENCE_PT_SER)
    NvHitObject hitObject;
    NvTraceRayHitObject(
        _AccelerationStructure,
        RAY_FLAG_NONE,
        0xFF,
        0,
        1,
        0,
        ray,
        payload,
        hitObject);
    NvReorderThread(hitObject);
    NvInvokeHitObject(_AccelerationStructure, hitObject, payload);
#else
    TraceRay(
        _AccelerationStructure,
        RAY_FLAG_NONE,
        0xFF,
        0,
        1,
        0,
        ray,
        payload);
#endif
}

float GetReferencedPathtracingNEELightEstimatorWeight(
    uint lightFlags,
    float selectionPdf,
    float solidAnglePdf,
    float bsdfPdf)
{
    float lightPdf = selectionPdf * solidAnglePdf;
    return ReferencedPathtracingGetLightEstimatorWeight(
        (lightFlags & REFERENCED_LIGHT_FLAG_BSDF_REACHABLE) != 0u,
        (lightFlags & REFERENCED_LIGHT_FLAG_SINGULAR) != 0u,
        lightPdf,
        bsdfPdf);
}

void AccumulateReferencedPathtracingMainLightRadiance(
    float3 contribution,
    uint contributionLobeClass,
    uint bounceIndex,
    uint primaryLobeClass,
    bool includeInPrimaryDenoiserSignal,
    inout float3 directLightingRadiance,
    inout float3 diffuseRadiance,
    inout float3 specularRadiance,
    inout float3 primaryDenoiserDiffuseRadiance,
    inout float3 primaryDenoiserSpecularRadiance)
{
    if (bounceIndex == 0u)
    {
        // Preserve the raw FP32 direct-light AOV for canonical accumulation/capture.
        directLightingRadiance += contribution;

        // A finite sun is stochastic. Copy it into the preview-only REBLUR signals,
        // while keeping delta directional lighting out of spatial filtering.
        if (includeInPrimaryDenoiserSignal)
        {
            if (contributionLobeClass == 1u)
                primaryDenoiserDiffuseRadiance += contribution;
            else if (contributionLobeClass == 2u)
                primaryDenoiserSpecularRadiance += contribution;
        }
    }
    else if (primaryLobeClass == 1u)
    {
        diffuseRadiance += contribution;
    }
    else if (primaryLobeClass == 2u)
    {
        specularRadiance += contribution;
    }
}

[shader("raygeneration")]
void RayGenReferencedPathtracing()
{
    uint2 launchIndex = DispatchRaysIndex().xy;
    uint2 launchDimensions = DispatchRaysDimensions().xy;
    uint2 pixelCoord = uint2(launchIndex.x, launchDimensions.y - launchIndex.y - 1u);

    uint sampleIndex = (uint)_ReferencedFrameIndex;
    uint sampleSeed = (uint)_ReferencedSeed;
    uint pathSamplingMode = (uint)max(_ReferencedPathSamplingMode, 0);
    float2 filmSample = float2(
        ReferencedPathtracingGetPathSample(
            pixelCoord,
            sampleIndex,
            kReferencedPathtracingFilmDimension,
            sampleSeed,
            pathSamplingMode),
        ReferencedPathtracingGetPathSample(
            pixelCoord,
            sampleIndex,
            kReferencedPathtracingFilmDimension + 1u,
            sampleSeed,
            pathSamplingMode));
    float2 lensUniformSample = float2(
        ReferencedPathtracingGetPathSample(
            pixelCoord,
            sampleIndex,
            kReferencedPathtracingLensDimension,
            sampleSeed,
            pathSamplingMode),
        ReferencedPathtracingGetPathSample(
            pixelCoord,
            sampleIndex,
            kReferencedPathtracingLensDimension + 1u,
            sampleSeed,
            pathSamplingMode));
    float2 lensDiskSample =
        ReferencedPathtracingSampleConcentricDisk(lensUniformSample);

    RayDesc ray;
    float2 pixelCenter = (float2)pixelCoord + filmSample;
    GetReferencedPathtracingPhysicalCameraRay(
        pixelCenter,
        lensDiskSample,
        ray.Origin,
        ray.Direction);
    float3 primaryCameraRayDirectionWS = ray.Direction;
    float cameraForwardProjection = max(
        dot(
            ray.Direction,
            normalize(_ReferencedCameraForwardWS.xyz)),
        1e-6);
    ray.TMin = _ReferencedPhysicalCameraParameters.w > 0.5
        ? _RayMinDistance / cameraForwardProjection
        : _RayMinDistance;
    ray.TMax = _ReferencedPhysicalCameraParameters.w > 0.5
        ? _RayMaxDistance / cameraForwardProjection
        : _RayMaxDistance;

    float3 rayOriginDx;
    float3 rayDirectionDx;
    GetReferencedPathtracingPhysicalCameraRay(
        pixelCenter + float2(1.0, 0.0),
        lensDiskSample,
        rayOriginDx,
        rayDirectionDx);
    float3 rayOriginDy;
    float3 rayDirectionDy;
    GetReferencedPathtracingPhysicalCameraRay(
        pixelCenter + float2(0.0, 1.0),
        lensDiskSample,
        rayOriginDy,
        rayDirectionDy);
    float rayConeSpreadAngle = max(
        length(rayDirectionDx - ray.Direction),
        length(rayDirectionDy - ray.Direction));

    float3 diffuseRadiance = 0.0;
    float3 specularRadiance = 0.0;
    float3 directLightingRadiance = 0.0;
    float3 primaryDenoiserMainLightDiffuseRadiance = 0.0;
    float3 primaryDenoiserMainLightSpecularRadiance = 0.0;
    float3 emissionRadiance = 0.0;
    float3 cameraBackgroundRadiance = 0.0;
    float3 primaryEnvironmentBackgroundRadiance = 0.0;
    float3 indirectEnvironmentRadiance = 0.0;
    float3 environmentNeeRadiance = 0.0;
    float3 environmentDirectDiffuseRadiance = 0.0;
    float3 environmentDirectSpecularRadiance = 0.0;
    float4 neeTransportDiagnostic = 0.0;
    float4 segmentTransportDiagnostic = 0.0;
    float3 neeLightIdentityDiagnostic = 0.0;
    float3 lightSpatialIndexDiagnostic = 0.0;
    float3 shadingNormalDiagnostic = 0.0;
    float3 atmosphereTransportDiagnostic = 0.0;
    float3 thinWalledTransmissionDiagnostic = 0.0;
    float3 stochasticTransparencyDiagnostic = 0.0;
    float3 primaryDenoisingAlbedo = 0.0;
    float3 primaryDenoisingNormalWS = 0.0;
    float3 pathSampleDiagnostic = float3(
        filmSample,
        ReferencedPathtracingGetPathSample(
            pixelCoord,
            sampleIndex,
            ReferencedPathtracingGetBounceSampleDimension(
                0u,
                kReferencedPathtracingRussianRouletteDimensionOffset),
            sampleSeed,
            pathSamplingMode));
    float3 physicalCameraDiagnostic = float3(
        lensDiskSample * 0.5 + 0.5,
        _ReferencedPhysicalCameraParameters.w > 0.5 ? 1.0 : 0.0);
    float3 invalidSampleMask = 0.0;
    bool hasNeeTransportDiagnostic = false;
    bool neeTransportContributionValid = false;
    bool hasSegmentTransportDiagnostic = false;
    float cameraBackgroundAlpha = 0.0;
    float3 throughput = 1.0;
    float previousBsdfPdf = 0.0;
    bool previousBsdfWasDelta = false;
    bool previousReferenceSunReachable = false;
    ReferencedPathtracingLightSelectionContext
        previousLightSelectionContext =
            (ReferencedPathtracingLightSelectionContext)0;
    float rayConeWidth = 0.0;
    uint primaryHit = 0u;
    uint primaryAtmosphereHit = 0u;
    uint primaryLobeClass = 0u;
    float primaryViewZ = 1.0;
    float primaryLinearRoughness = 1.0;
    float diffuseHitDistance = _RayMaxDistance;
    float specularHitDistance = _RayMaxDistance;
    // RR requires a literal primary-surface ray distance, while REBLUR hitT
    // may be radiance-weighted when several estimators share one signal.
    float diffuseDlssHitDistance = 0.0;
    float specularDlssHitDistance = 0.0;
    bool diffuseHitDistanceValid = false;
    bool specularHitDistanceValid = false;
    float3 diffuseRayDirectionWS = 0.0;
    float3 specularRayDirectionWS = 0.0;
    uint maxBounceCount = min(
        (uint)max(_ReferencedMaxBounceCount, 1),
        kReferencedPathtracingMaxSupportedBounceCount);

    for (uint bounceIndex = 0u; bounceIndex < maxBounceCount; ++bounceIndex)
    {
        ReferencedPathtracingPayload payload;
        InitializeReferencedPathtracingPayload(payload);
        payload.pathThroughput = throughput;
        uint bounceSampleDimension =
            ReferencedPathtracingGetBounceSampleDimension(
                bounceIndex,
                0u);
        payload.bsdfRandom =
            ReferencedPathtracingGetPathSample3D(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingBsdfDimensionOffset,
                sampleSeed,
                pathSamplingMode);
        // A single dimension selects the source and the remaining pair samples its
        // conditional shape or direction.
        payload.directLightRandom =
            ReferencedPathtracingGetPathSample3D(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingNeeDimensionOffset,
                sampleSeed,
                pathSamplingMode);
        float stochasticAlphaSample =
            ReferencedPathtracingGetPathSample(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingStochasticAlphaDimensionOffset,
                sampleSeed,
                pathSamplingMode);
        payload.stochasticAlphaSeed =
            ReferencedPathtracingHashStochasticTransparency(
                asuint(stochasticAlphaSample)
                ^ ReferencedPathtracingHashStochasticTransparency(
                    pixelCoord.x + 0x9e3779b9u)
                ^ ReferencedPathtracingHashStochasticTransparency(
                    pixelCoord.y + 0x85ebca6bu)
                ^ ReferencedPathtracingHashStochasticTransparency(
                    sampleIndex + bounceSampleDimension));
        payload.rayConeWidth = rayConeWidth;
        payload.rayConeSpreadAngle = rayConeSpreadAngle;
        // SER is useful around the material-heavy closest-hit path. Shadow rays
        // skip closest-hit shading and retain the lower-overhead standard TraceRay.
        TraceReferencedPathtracingSurface(ray, payload);
        throughput *= payload.stochasticTransparencyWeight;
        if (!IsFiniteReferencedPathtracingRadiance(throughput)
            || MaxReferencedPathtracingComponent(throughput) <= 0.0)
        {
            if (!IsFiniteReferencedPathtracingRadiance(throughput))
                invalidSampleMask.z = 1.0;
            break;
        }
        if (bounceIndex == 0u
            && payload.stochasticTransparencyDiagnostics.a > 0.0)
        {
            stochasticTransparencyDiagnostic =
                payload.stochasticTransparencyDiagnostics.rgb;
        }

        float4 volumeRandom = float4(
            ReferencedPathtracingGetPathSample(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingVolumeDimensionOffset,
                sampleSeed,
                pathSamplingMode),
            ReferencedPathtracingGetPathSample(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingVolumeDimensionOffset
                    + 1u,
                sampleSeed,
                pathSamplingMode),
            ReferencedPathtracingGetPathSample(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingVolumeDimensionOffset
                    + 2u,
                sampleSeed,
                pathSamplingMode),
            ReferencedPathtracingGetPathSample(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingVolumeDimensionOffset
                    + 3u,
                sampleSeed,
                pathSamplingMode));
        float2 atmosphereSunRandom = float2(
            ReferencedPathtracingGetPathSample(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingAtmosphereSunDimensionOffset,
                sampleSeed,
                pathSamplingMode),
            ReferencedPathtracingGetPathSample(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingAtmosphereSunDimensionOffset
                    + 1u,
                sampleSeed,
                pathSamplingMode));
        float4 cloudRandom = float4(
            ReferencedPathtracingGetPathSample(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingCloudDimensionOffset,
                sampleSeed,
                pathSamplingMode),
            ReferencedPathtracingGetPathSample(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingCloudDimensionOffset
                    + 1u,
                sampleSeed,
                pathSamplingMode),
            ReferencedPathtracingGetPathSample(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingCloudDimensionOffset
                    + 2u,
                sampleSeed,
                pathSamplingMode),
            ReferencedPathtracingGetPathSample(
                pixelCoord,
                sampleIndex,
                bounceSampleDimension
                    + kReferencedPathtracingCloudDimensionOffset
                    + 3u,
                sampleSeed,
                pathSamplingMode));
        float segmentStartDistance = max(ray.TMin, 0.0);
        float3 atmosphereRayOriginWS =
            ray.Origin
            + normalize(ray.Direction) * segmentStartDistance;
        float atmosphereMaximumDistance = payload.hit != 0u
            ? max(payload.hitDistance - segmentStartDistance, 0.0)
            : kReferencedPathtracingInfiniteDistance;
        bool includeVirtualGround =
            !ReferencedPathtracingUsesCameraRelativeAtmosphere()
            || payload.hit == 0u;
        ReferencedPathtracingAtmosphereMediumSample
            atmosphereMediumSample;
        bool intersectsAtmosphere =
            ReferencedPathtracingSampleAtmosphereMedium(
                atmosphereRayOriginWS,
                ray.Direction,
                atmosphereMaximumDistance,
                includeVirtualGround,
                volumeRandom,
                atmosphereMediumSample);
        float cloudMaximumDistance =
            intersectsAtmosphere
                && atmosphereMediumSample.hitsGround != 0u
            ? min(
                atmosphereMaximumDistance,
                atmosphereMediumSample.boundaryDistance)
            : atmosphereMaximumDistance;
        ReferencedPathtracingCloudMediumSample cloudMediumSample;
        bool intersectsCloud =
            ReferencedPathtracingSampleCloudMedium(
                atmosphereRayOriginWS,
                ray.Direction,
                cloudMaximumDistance,
                cloudRandom,
                cloudMediumSample);
        if (intersectsAtmosphere)
        {
            atmosphereTransportDiagnostic.x = max(
                atmosphereTransportDiagnostic.x,
                (float)atmosphereMediumSample.trackingStepCount
                    / REFERENCED_ATMOSPHERE_MAXIMUM_TRACKING_STEP_COUNT);
        }
        if (intersectsCloud)
        {
            atmosphereTransportDiagnostic.y = max(
                atmosphereTransportDiagnostic.y,
                (float)cloudMediumSample.trackingStepCount
                    / REFERENCED_CLOUD_MAXIMUM_TRACKING_STEP_COUNT);
        }
        if ((intersectsAtmosphere
                && atmosphereMediumSample.eventType
                    == REFERENCED_ATMOSPHERE_MEDIUM_EVENT_TRACKING_OVERFLOW)
            || (intersectsCloud
                && cloudMediumSample.eventType
                    == REFERENCED_CLOUD_EVENT_TRACKING_OVERFLOW))
        {
            atmosphereTransportDiagnostic.z = 1.0;
            invalidSampleMask.z = 1.0;
            break;
        }

        bool atmosphereHasMediumEvent =
            intersectsAtmosphere
            && atmosphereMediumSample.eventType
                != REFERENCED_ATMOSPHERE_MEDIUM_EVENT_NONE;
        bool cloudHasMediumEvent =
            intersectsCloud
            && cloudMediumSample.eventType
                != REFERENCED_CLOUD_EVENT_NONE;
        bool cloudEventFirst =
            cloudHasMediumEvent
            && (!atmosphereHasMediumEvent
                || cloudMediumSample.distance
                    < atmosphereMediumSample.distance);
        if (cloudEventFirst)
        {
            if (intersectsAtmosphere)
            {
                float3 atmosphereToCloudTransmittance =
                    ReferencedPathtracingEvaluateAtmosphereTransmittanceWithGroundPolicy(
                        atmosphereRayOriginWS,
                        ray.Direction,
                        cloudMediumSample.distance,
                        includeVirtualGround);
                throughput *=
                    ReferencedPathtracingGetAtmosphereTransmittanceRatio(
                        atmosphereToCloudTransmittance,
                        atmosphereMediumSample.heroChannel);
                if (!IsFiniteReferencedPathtracingRadiance(throughput)
                    || MaxReferencedPathtracingComponent(throughput)
                        <= 0.0)
                {
                    invalidSampleMask.z = 1.0;
                    break;
                }
            }

            bool primaryCloudVisible =
                (_ReferencedAtmosphereFlags
                    & kReferencedAtmosphereFlagCloudsCameraVisible) != 0;
            bool primaryCloudHoldout =
                (_ReferencedAtmosphereFlags
                    & kReferencedAtmosphereFlagCloudsHoldout) != 0;
            if (cloudMediumSample.eventType
                == REFERENCED_CLOUD_EVENT_ABSORB)
            {
                if (bounceIndex == 0u)
                {
                    cameraBackgroundAlpha =
                        primaryCloudVisible && !primaryCloudHoldout
                            ? 1.0
                            : 0.0;
                }
                break;
            }

            bool cloudScatterVisible =
                bounceIndex > 0u || primaryCloudVisible;
            if (!cloudScatterVisible)
                break;

            if (bounceIndex == 0u)
            {
                primaryHit = 1u;
                primaryAtmosphereHit = 1u;
                primaryLobeClass = 1u;
                primaryViewZ = abs(mul(
                    _ReferencedWorldToView,
                    float4(cloudMediumSample.positionWS, 1.0)).z);
                primaryLinearRoughness = 1.0;
                primaryDenoisingAlbedo =
                    saturate(_ReferencedCloudMaterialParameters.rgb);
                primaryDenoisingNormalWS =
                    -normalize(ray.Direction);
                diffuseHitDistance =
                    cloudMediumSample.distance
                    + segmentStartDistance;
                cameraBackgroundAlpha =
                    primaryCloudHoldout ? 0.0 : 1.0;
            }

            ReferencedPathtracingAtmosphereSunSample cloudSunSample;
            if (ReferencedPathtracingSampleAtmosphereSun(
                    atmosphereSunRandom,
                    cloudSunSample))
            {
                float cloudPhasePdf =
                    ReferencedPathtracingEvaluateCloudPhasePdf(
                        ray.Direction,
                        cloudSunSample.directionWS);
                float cloudSunEstimatorWeight =
                    ReferencedPathtracingGetLightEstimatorWeight(
                        cloudSunSample.isDelta == 0u,
                        cloudSunSample.isDelta != 0u,
                        cloudSunSample.solidAnglePdf,
                        cloudPhasePdf);
                float3 cloudDirectWeight =
                    ReferencedPathtracingEvaluateCloudDirectWeight(
                        cloudMediumSample,
                        ray.Direction,
                        cloudSunSample.directionWS);
                float3 cloudSunVisibility =
                    TraceReferencedPathtracingCandidateVisibility(
                        cloudMediumSample.positionWS,
                        cloudSunSample.directionWS,
                        cloudSunSample.directionWS,
                        kReferencedPathtracingInfiniteDistance,
                        true,
                        _ReferencedAtmosphereSunIlluminance.w,
                        ReferencedPathtracingHashStochasticTransparency(
                            payload.stochasticAlphaSeed
                            ^ 0x243f6a88u));
                float3 cloudDirectRadiance =
                    throughput
                    * cloudDirectWeight
                    * max(
                        _ReferencedAtmosphereSunIlluminance.rgb,
                        0.0)
                    * cloudSunEstimatorWeight
                    * cloudSunVisibility
                    * ReferencedPathtracingEvaluateCloudMultipleScatteringWeight(
                        cloudMediumSample);
                if (IsFiniteReferencedPathtracingRadiance(
                        cloudDirectRadiance)
                    && !any(cloudDirectRadiance < -1e-6))
                {
                    AccumulateReferencedPathtracingMainLightRadiance(
                        cloudDirectRadiance,
                        1u,
                        bounceIndex,
                        primaryLobeClass,
                        cloudSunSample.isDelta == 0u,
                        directLightingRadiance,
                        diffuseRadiance,
                        specularRadiance,
                        primaryDenoiserMainLightDiffuseRadiance,
                        primaryDenoiserMainLightSpecularRadiance);
                }
                else
                {
                    invalidSampleMask.z = 1.0;
                }
            }

            if (bounceIndex + 1u >= maxBounceCount)
                break;

            float3 cloudNextDirectionWS;
            float3 cloudThroughputWeight;
            float cloudPhasePdf;
            if (!ReferencedPathtracingSampleCloudPhase(
                    cloudMediumSample,
                    ray.Direction,
                    cloudRandom.zw,
                    cloudNextDirectionWS,
                    cloudThroughputWeight,
                    cloudPhasePdf))
            {
                break;
            }

            throughput *= cloudThroughputWeight;
            if (!IsFiniteReferencedPathtracingRadiance(throughput)
                || MaxReferencedPathtracingComponent(throughput) <= 0.0)
            {
                invalidSampleMask.z = 1.0;
                break;
            }

            if ((int)(bounceIndex + 1u)
                >= _ReferencedRussianRouletteStartBounce)
            {
                float survivalProbability = clamp(
                    MaxReferencedPathtracingComponent(throughput),
                    0.05,
                    0.95);
                float russianRouletteSample =
                    ReferencedPathtracingGetPathSample(
                        pixelCoord,
                        sampleIndex,
                        ReferencedPathtracingGetBounceSampleDimension(
                            bounceIndex,
                            kReferencedPathtracingRussianRouletteDimensionOffset),
                        sampleSeed,
                        pathSamplingMode);
                if (russianRouletteSample >= survivalProbability)
                    break;
                throughput /= survivalProbability;
            }

            if (bounceIndex == 0u)
                diffuseRayDirectionWS =
                    normalize(cloudNextDirectionWS);
            previousBsdfPdf = cloudPhasePdf;
            previousBsdfWasDelta = false;
            previousReferenceSunReachable = true;
            previousLightSelectionContext =
                (ReferencedPathtracingLightSelectionContext)0;
            rayConeWidth = max(
                rayConeWidth
                + cloudMediumSample.distance * rayConeSpreadAngle,
                0.0);
            ray.Direction = normalize(cloudNextDirectionWS);
            ray.Origin =
                cloudMediumSample.positionWS
                + ray.Direction
                    * kReferencedPathtracingShadowMinBias;
            ray.TMin = kReferencedPathtracingShadowMinBias;
            ray.TMax = _RayMaxDistance;
            continue;
        }

        if (intersectsAtmosphere)
        {
            bool primaryAtmosphereVisible =
                (_ReferencedAtmosphereFlags
                    & kReferencedAtmosphereFlagCameraVisible) != 0;
            bool primaryAtmosphereHoldout =
                (_ReferencedAtmosphereFlags
                    & kReferencedAtmosphereFlagHoldout) != 0;
            if (atmosphereMediumSample.eventType
                == REFERENCED_ATMOSPHERE_MEDIUM_EVENT_ABSORB)
            {
                if (bounceIndex == 0u)
                {
                    cameraBackgroundAlpha =
                        primaryAtmosphereVisible
                        && !primaryAtmosphereHoldout
                            ? 1.0
                            : 0.0;
                }
                break;
            }

            if (atmosphereMediumSample.eventType
                == REFERENCED_ATMOSPHERE_MEDIUM_EVENT_SCATTER)
            {
                bool scatterVisible =
                    bounceIndex > 0u
                    || primaryAtmosphereVisible;
                if (!scatterVisible)
                    break;

                if (bounceIndex == 0u)
                {
                    primaryHit = 1u;
                    primaryAtmosphereHit = 1u;
                    primaryLobeClass = 1u;
                    primaryViewZ = abs(mul(
                        _ReferencedWorldToView,
                        float4(
                            atmosphereMediumSample.positionWS,
                            1.0)).z);
                    primaryLinearRoughness = 1.0;
                    float radialDistance = length(
                        atmosphereMediumSample.positionWS
                        - _ReferencedAtmospherePlanetCenterBottomRadius.xyz);
                    float3 localExtinction =
                        ReferencedPathtracingEvaluateAtmosphereExtinction(
                            radialDistance);
                    primaryDenoisingAlbedo = saturate(
                        (atmosphereMediumSample.rayleighScattering
                            + atmosphereMediumSample.mieScattering)
                        / max(localExtinction, 1e-20));
                    primaryDenoisingNormalWS =
                        -normalize(ray.Direction);
                    diffuseHitDistance =
                        atmosphereMediumSample.distance
                        + segmentStartDistance;
                    cameraBackgroundAlpha =
                        primaryAtmosphereHoldout ? 0.0 : 1.0;
                }

                ReferencedPathtracingAtmosphereSunSample sunSample;
                if (ReferencedPathtracingSampleAtmosphereSun(
                        atmosphereSunRandom,
                        sunSample))
                {
                    float atmospherePhasePdf =
                        ReferencedPathtracingEvaluateAtmospherePhasePdf(
                            atmosphereMediumSample,
                            ray.Direction,
                            sunSample.directionWS);
                    float sunEstimatorWeight =
                        ReferencedPathtracingGetLightEstimatorWeight(
                            sunSample.isDelta == 0u,
                            sunSample.isDelta != 0u,
                            sunSample.solidAnglePdf,
                            atmospherePhasePdf);
                    float3 atmosphereDirectWeight =
                        ReferencedPathtracingEvaluateAtmosphereDirectWeight(
                            atmosphereMediumSample,
                            ray.Direction,
                            sunSample.directionWS);
                    float3 sunVisibility =
                        TraceReferencedPathtracingCandidateVisibility(
                            atmosphereMediumSample.positionWS,
                            sunSample.directionWS,
                            sunSample.directionWS,
                            kReferencedPathtracingInfiniteDistance,
                            true,
                            _ReferencedAtmosphereSunIlluminance.w,
                            ReferencedPathtracingHashStochasticTransparency(
                                payload.stochasticAlphaSeed
                                ^ 0x13198a2eu));
                    float3 atmosphereDirectRadiance =
                        throughput
                        * atmosphereDirectWeight
                        * max(
                            _ReferencedAtmosphereSunIlluminance.rgb,
                            0.0)
                        * sunEstimatorWeight
                        * sunVisibility;
                    if (IsFiniteReferencedPathtracingRadiance(
                            atmosphereDirectRadiance)
                        && !any(atmosphereDirectRadiance < -1e-6))
                    {
                        AccumulateReferencedPathtracingMainLightRadiance(
                            atmosphereDirectRadiance,
                            1u,
                            bounceIndex,
                            primaryLobeClass,
                            sunSample.isDelta == 0u,
                            directLightingRadiance,
                            diffuseRadiance,
                            specularRadiance,
                            primaryDenoiserMainLightDiffuseRadiance,
                            primaryDenoiserMainLightSpecularRadiance);
                    }
                    else
                    {
                        invalidSampleMask.z = 1.0;
                    }
                }

                if (bounceIndex + 1u >= maxBounceCount)
                    break;

                float3 atmosphereNextDirectionWS;
                float3 atmosphereThroughputWeight;
                float atmospherePhasePdf;
                if (!ReferencedPathtracingSampleAtmospherePhase(
                        atmosphereMediumSample,
                        ray.Direction,
                        volumeRandom.zw,
                        atmosphereNextDirectionWS,
                        atmosphereThroughputWeight,
                        atmospherePhasePdf))
                {
                    break;
                }

                throughput *= atmosphereThroughputWeight;
                if (!IsFiniteReferencedPathtracingRadiance(throughput)
                    || MaxReferencedPathtracingComponent(throughput)
                        <= 0.0)
                {
                    invalidSampleMask.z = 1.0;
                    break;
                }

                if ((int)(bounceIndex + 1u)
                    >= _ReferencedRussianRouletteStartBounce)
                {
                    float survivalProbability = clamp(
                        MaxReferencedPathtracingComponent(throughput),
                        0.05,
                        0.95);
                    float russianRouletteSample =
                        ReferencedPathtracingGetPathSample(
                            pixelCoord,
                            sampleIndex,
                            ReferencedPathtracingGetBounceSampleDimension(
                                bounceIndex,
                                kReferencedPathtracingRussianRouletteDimensionOffset),
                            sampleSeed,
                            pathSamplingMode);
                    if (russianRouletteSample >= survivalProbability)
                        break;
                    throughput /= survivalProbability;
                }

                if (bounceIndex == 0u)
                    diffuseRayDirectionWS =
                        normalize(atmosphereNextDirectionWS);
                previousBsdfPdf = atmospherePhasePdf;
                previousBsdfWasDelta = false;
                previousReferenceSunReachable = true;
                previousLightSelectionContext =
                    (ReferencedPathtracingLightSelectionContext)0;
                rayConeWidth = max(
                    rayConeWidth
                    + atmosphereMediumSample.distance
                        * rayConeSpreadAngle,
                    0.0);
                ray.Direction =
                    normalize(atmosphereNextDirectionWS);
                ray.Origin =
                    atmosphereMediumSample.positionWS
                    + ray.Direction
                        * kReferencedPathtracingShadowMinBias;
                ray.TMin = kReferencedPathtracingShadowMinBias;
                ray.TMax = _RayMaxDistance;
                continue;
            }

            throughput *=
                atmosphereMediumSample.transmittanceRatio;
            if (!IsFiniteReferencedPathtracingRadiance(throughput)
                || MaxReferencedPathtracingComponent(throughput)
                    <= 0.0)
            {
                invalidSampleMask.z = 1.0;
                break;
            }

            if (atmosphereMediumSample.hitsGround != 0u)
            {
                bool groundVisible = true;
                bool groundHoldout = false;
                if (bounceIndex == 0u)
                {
                    groundVisible =
                        (_ReferencedAtmosphereFlags
                            & kReferencedAtmosphereFlagGroundCameraVisible)
                            != 0;
                    groundHoldout =
                        (_ReferencedAtmosphereFlags
                            & kReferencedAtmosphereFlagGroundHoldout)
                            != 0;
                    cameraBackgroundAlpha =
                        groundVisible && !groundHoldout
                            ? 1.0
                            : 0.0;
                    if (!groundVisible)
                        break;
                }

                float3 groundRayDirectionWS =
                    normalize(ray.Direction);
                float3 groundPositionWS =
                    atmosphereRayOriginWS
                    + groundRayDirectionWS
                        * atmosphereMediumSample.boundaryDistance;
                float3 groundNormalWS = normalize(
                    groundPositionWS
                    - _ReferencedAtmospherePlanetCenterBottomRadius.xyz);
                float groundHitDistance =
                    segmentStartDistance
                    + atmosphereMediumSample.boundaryDistance;
                if (bounceIndex == 0u)
                {
                    primaryHit = 1u;
                    primaryAtmosphereHit = 1u;
                    primaryLobeClass = 1u;
                    primaryViewZ = abs(mul(
                        _ReferencedWorldToView,
                        float4(groundPositionWS, 1.0)).z);
                    primaryLinearRoughness = 1.0;
                    primaryDenoisingAlbedo =
                        saturate(_ReferencedAtmosphereGroundAlbedo.rgb);
                    primaryDenoisingNormalWS = groundNormalWS;
                    diffuseHitDistance = groundHitDistance;
                }

                ReferencedPathtracingAtmosphereSunSample groundSunSample;
                if (ReferencedPathtracingSampleAtmosphereSun(
                        atmosphereSunRandom,
                        groundSunSample))
                {
                    float3 groundDirectWeight =
                        ReferencedPathtracingEvaluateAtmosphereGroundDirectWeight(
                            groundNormalWS,
                            groundSunSample.directionWS);
                    float groundBsdfPdf =
                        max(
                            dot(
                                groundNormalWS,
                                groundSunSample.directionWS),
                            0.0)
                        / kReferencedPathtracingPi;
                    float groundSunEstimatorWeight =
                        ReferencedPathtracingGetLightEstimatorWeight(
                            groundSunSample.isDelta == 0u,
                            groundSunSample.isDelta != 0u,
                            groundSunSample.solidAnglePdf,
                            groundBsdfPdf);
                    float3 groundSunVisibility =
                        TraceReferencedPathtracingCandidateVisibility(
                            groundPositionWS,
                            groundNormalWS,
                            groundSunSample.directionWS,
                            kReferencedPathtracingInfiniteDistance,
                            true,
                            _ReferencedAtmosphereSunIlluminance.w,
                            ReferencedPathtracingHashStochasticTransparency(
                                payload.stochasticAlphaSeed
                                ^ 0xa4093822u));
                    float3 groundDirectRadiance =
                        throughput
                        * groundDirectWeight
                        * max(
                            _ReferencedAtmosphereSunIlluminance.rgb,
                            0.0)
                        * groundSunEstimatorWeight
                        * groundSunVisibility;
                    if (IsFiniteReferencedPathtracingRadiance(
                            groundDirectRadiance)
                        && !any(groundDirectRadiance < -1e-6))
                    {
                        AccumulateReferencedPathtracingMainLightRadiance(
                            groundDirectRadiance,
                            1u,
                            bounceIndex,
                            primaryLobeClass,
                            groundSunSample.isDelta == 0u,
                            directLightingRadiance,
                            diffuseRadiance,
                            specularRadiance,
                            primaryDenoiserMainLightDiffuseRadiance,
                            primaryDenoiserMainLightSpecularRadiance);
                    }
                    else
                    {
                        invalidSampleMask.z = 1.0;
                    }
                }

                if (bounceIndex + 1u >= maxBounceCount)
                    break;

                float3 groundNextDirectionWS;
                float3 groundThroughputWeight;
                float groundPdf;
                if (!ReferencedPathtracingSampleAtmosphereGround(
                        groundNormalWS,
                        volumeRandom.zw,
                        groundNextDirectionWS,
                        groundThroughputWeight,
                        groundPdf))
                {
                    break;
                }

                throughput *= groundThroughputWeight;
                if (!IsFiniteReferencedPathtracingRadiance(throughput)
                    || MaxReferencedPathtracingComponent(throughput)
                        <= 0.0)
                {
                    invalidSampleMask.z = 1.0;
                    break;
                }

                if ((int)(bounceIndex + 1u)
                    >= _ReferencedRussianRouletteStartBounce)
                {
                    float survivalProbability = clamp(
                        MaxReferencedPathtracingComponent(throughput),
                        0.05,
                        0.95);
                    float russianRouletteSample =
                        ReferencedPathtracingGetPathSample(
                            pixelCoord,
                            sampleIndex,
                            ReferencedPathtracingGetBounceSampleDimension(
                                bounceIndex,
                                kReferencedPathtracingRussianRouletteDimensionOffset),
                            sampleSeed,
                            pathSamplingMode);
                    if (russianRouletteSample >= survivalProbability)
                        break;
                    throughput /= survivalProbability;
                }

                if (bounceIndex == 0u)
                    diffuseRayDirectionWS =
                        normalize(groundNextDirectionWS);
                previousBsdfPdf = groundPdf;
                previousBsdfWasDelta = false;
                previousReferenceSunReachable = true;
                previousLightSelectionContext =
                    (ReferencedPathtracingLightSelectionContext)0;
                rayConeWidth = max(
                    rayConeWidth
                    + atmosphereMediumSample.boundaryDistance
                        * rayConeSpreadAngle,
                    0.0);
                float groundRayBias;
                ray.Origin = OffsetReferencedPathtracingRayOrigin(
                    groundPositionWS,
                    groundNormalWS,
                    groundNextDirectionWS,
                    groundRayBias);
                ray.Direction = normalize(groundNextDirectionWS);
                ray.TMin = groundRayBias;
                ray.TMax = _RayMaxDistance;
                continue;
            }
        }

        if (payload.hit == 0u)
        {
            float3 atmosphereSunDiskRadiance;
            float atmosphereSunDiskPdf;
            bool hitsAtmosphereSunDisk =
                ReferencedPathtracingEvaluateAtmosphereSunDiskRadiance(
                    ray.Direction,
                    atmosphereSunDiskRadiance,
                    atmosphereSunDiskPdf);
            if (bounceIndex == 0u)
            {
                float4 cameraBackground =
                    ReferencedPathtracingEvaluateCameraBackground(ray.Direction);
                cameraBackgroundRadiance =
                    throughput * cameraBackground.rgb;
                cameraBackgroundAlpha = cameraBackground.a;

                bool usesReferenceAtmosphereBackground =
                    _ReferencedCameraSkyEnabled != 0
                    && ReferencedPathtracingHasReferenceAtmosphere()
                    && (_ReferencedAtmosphereFlags
                        & kReferencedAtmosphereFlagCameraVisible) != 0;
                if (usesReferenceAtmosphereBackground
                    && hitsAtmosphereSunDisk)
                {
                    cameraBackgroundRadiance +=
                        throughput * atmosphereSunDiskRadiance;
                }

                bool usesEnvironmentBackground =
                    _ReferencedCameraSkyEnabled != 0
                    && _ReferencedEnvironmentCameraVisible != 0
                    && ReferencedPathtracingHasEnvironment();
                if (usesEnvironmentBackground
                    || usesReferenceAtmosphereBackground)
                {
                    primaryEnvironmentBackgroundRadiance =
                        cameraBackgroundRadiance;
                }
            }
            else
            {
                float3 environmentContribution = 0.0;
                if (previousReferenceSunReachable
                    && hitsAtmosphereSunDisk)
                {
                    float sunBsdfEstimatorWeight =
                        ReferencedPathtracingGetBsdfEstimatorWeight(
                            previousBsdfPdf,
                            atmosphereSunDiskPdf,
                            previousBsdfWasDelta);
                    environmentContribution =
                        throughput
                        * atmosphereSunDiskRadiance
                        * sunBsdfEstimatorWeight;
                }
                else
                {
                    float3 environmentRadiance =
                        ReferencedPathtracingEvaluateLightingEnvironment(
                            ray.Direction);
                    float environmentLightPdf =
                        ReferencedPathtracingEvaluateUnifiedEnvironmentLightPdf(
                            previousLightSelectionContext,
                            ray.Direction);
                    float bsdfEstimatorWeight =
                        ReferencedPathtracingGetBsdfEstimatorWeight(
                            previousBsdfPdf,
                            environmentLightPdf,
                            previousBsdfWasDelta);
                    environmentContribution =
                        throughput
                        * environmentRadiance
                        * bsdfEstimatorWeight;
                }

                if (IsFiniteReferencedPathtracingRadiance(environmentContribution))
                {
                    if (primaryLobeClass == 1u)
                    {
                        diffuseRadiance += environmentContribution;
                        indirectEnvironmentRadiance += environmentContribution;
                    }
                    else if (primaryLobeClass == 2u)
                    {
                        specularRadiance += environmentContribution;
                        indirectEnvironmentRadiance += environmentContribution;
                    }
                }
                else
                {
                    invalidSampleMask.z = 1.0;
                }
            }

            break;
        }

        if (bounceIndex == 0u)
        {
            primaryHit = 1u;
            primaryViewZ = abs(mul(
                _ReferencedWorldToView,
                float4(payload.positionWS, 1.0)).z);
            primaryLinearRoughness = saturate(payload.linearRoughness);
            shadingNormalDiagnostic = payload.shadingNormalDiagnostics;
            thinWalledTransmissionDiagnostic = float3(
                payload.thinWalledTransmissionWeight,
                payload.nextLobeIsTransmission != 0u ? 1.0 : 0.0,
                payload.neeSelectionPdf > 0.0
                    && dot(payload.neeDirectionWS, payload.faceNormalWS) < 0.0
                        ? 1.0
                        : 0.0);
            primaryDenoisingAlbedo = payload.denoisingAlbedo;
            primaryDenoisingNormalWS = payload.denoisingNormalWS;
            emissionRadiance += throughput * payload.emission;
        }
        else
        {
            float3 bouncedEmission = throughput * payload.emission;
            if (primaryLobeClass == 1u)
                diffuseRadiance += bouncedEmission;
            else if (primaryLobeClass == 2u)
                specularRadiance += bouncedEmission;

            if (bounceIndex == 1u)
            {
                if (primaryLobeClass == 1u)
                {
                    diffuseHitDistance = payload.hitDistance;
                    diffuseDlssHitDistance = payload.hitDistance;
                    diffuseHitDistanceValid = true;
                }
                else if (primaryLobeClass == 2u)
                {
                    specularHitDistance = payload.hitDistance;
                    specularDlssHitDistance = payload.hitDistance;
                    specularHitDistanceValid = true;
                }
            }
        }

        if (bounceIndex == 0u
            && !hasNeeTransportDiagnostic
            && payload.neeSelectionPdf > 0.0)
        {
            float diagnosticLightEstimatorWeight =
                GetReferencedPathtracingNEELightEstimatorWeight(
                    payload.neeFlags,
                    payload.neeSelectionPdf,
                    payload.neeSolidAnglePdf,
                    payload.neeBsdfPdf);
            neeTransportDiagnostic = float4(
                payload.neeSelectionPdf,
                payload.neeSolidAnglePdf,
                payload.neeBsdfPdf,
                diagnosticLightEstimatorWeight);
            neeLightIdentityDiagnostic = float3(
                (float)(payload.neeLightIndex + 1u),
                (float)payload.neeLightType,
                (float)payload.neeFlags);
            hasNeeTransportDiagnostic = true;
            neeTransportContributionValid = payload.neeValid != 0u;
        }

        if (payload.neeValid != 0u
            && (any(payload.neeDiffuseRadiance > 0.0)
                || any(payload.neeSpecularRadiance > 0.0)))
        {
            float lightEstimatorWeight =
                GetReferencedPathtracingNEELightEstimatorWeight(
                    payload.neeFlags,
                    payload.neeSelectionPdf,
                    payload.neeSolidAnglePdf,
                    payload.neeBsdfPdf);
            float3 neeDirectionWS = normalize(payload.neeDirectionWS);
            float3 visibility = lightEstimatorWeight > 0.0
                ? TraceReferencedPathtracingCandidateVisibility(
                    payload.positionWS,
                    normalize(payload.faceNormalWS),
                    neeDirectionWS,
                    payload.neeDistance,
                    !ReferencedPathtracingUsesCameraRelativeAtmosphere(),
                    payload.neeShadowStrength,
                    ReferencedPathtracingHashStochasticTransparency(
                        payload.stochasticAlphaSeed
                        ^ ReferencedPathtracingHashStochasticTransparency(
                            payload.neeLightIndex + 0x299f31d0u)))
                : 0.0;
            float3 directDiffuse = throughput
                * payload.neeDiffuseRadiance
                * lightEstimatorWeight
                * visibility;
            float3 directSpecular = throughput
                * payload.neeSpecularRadiance
                * lightEstimatorWeight
                * visibility;

            bool finiteContributions =
                IsFiniteReferencedPathtracingRadiance(directDiffuse)
                && IsFiniteReferencedPathtracingRadiance(directSpecular);
            if (!finiteContributions
                || any(directDiffuse < -1e-6)
                || any(directSpecular < -1e-6))
            {
                invalidSampleMask.x = 1.0;
            }
            if (finiteContributions
                && payload.neeLightType
                    == REFERENCED_LIGHT_TYPE_DIRECTIONAL)
            {
                bool includeInPrimaryDenoiserSignal =
                    (payload.neeFlags
                        & REFERENCED_LIGHT_FLAG_SINGULAR) == 0u;
                AccumulateReferencedPathtracingMainLightRadiance(
                    directDiffuse,
                    1u,
                    bounceIndex,
                    primaryLobeClass,
                    includeInPrimaryDenoiserSignal,
                    directLightingRadiance,
                    diffuseRadiance,
                    specularRadiance,
                    primaryDenoiserMainLightDiffuseRadiance,
                    primaryDenoiserMainLightSpecularRadiance);
                AccumulateReferencedPathtracingMainLightRadiance(
                    directSpecular,
                    2u,
                    bounceIndex,
                    primaryLobeClass,
                    includeInPrimaryDenoiserSignal,
                    directLightingRadiance,
                    diffuseRadiance,
                    specularRadiance,
                    primaryDenoiserMainLightDiffuseRadiance,
                    primaryDenoiserMainLightSpecularRadiance);
            }
            else if (finiteContributions
                && payload.neeLightType
                    == REFERENCED_LIGHT_TYPE_ENVIRONMENT)
            {
                environmentNeeRadiance += directDiffuse + directSpecular;
                if (bounceIndex == 0u)
                {
                    environmentDirectDiffuseRadiance += directDiffuse;
                    environmentDirectSpecularRadiance += directSpecular;
                    diffuseRadiance += directDiffuse;
                    specularRadiance += directSpecular;
                }
                else if (primaryLobeClass == 1u)
                {
                    diffuseRadiance += directDiffuse + directSpecular;
                }
                else if (primaryLobeClass == 2u)
                {
                    specularRadiance += directDiffuse + directSpecular;
                }
            }
            else if (finiteContributions)
            {
                if (bounceIndex == 0u)
                {
                    diffuseRadiance += directDiffuse;
                    specularRadiance += directSpecular;

                    if (any(directDiffuse > 0.0))
                    {
                        diffuseRayDirectionWS = neeDirectionWS;
                        diffuseHitDistance = payload.neeDistance;
                        diffuseDlssHitDistance = payload.neeDistance;
                        diffuseHitDistanceValid = true;
                    }

                    if (any(directSpecular > 0.0))
                    {
                        specularRayDirectionWS = neeDirectionWS;
                        specularHitDistance = payload.neeDistance;
                        specularDlssHitDistance = payload.neeDistance;
                        specularHitDistanceValid = true;
                    }
                }
                else if (primaryLobeClass == 1u)
                {
                    diffuseRadiance += directDiffuse + directSpecular;
                }
                else if (primaryLobeClass == 2u)
                {
                    specularRadiance += directDiffuse + directSpecular;
                }
            }
        }

        // HDRP/Unreal-style BSDF-segment light evaluation. Analytic emitters are
        // intersected along the sampled direction even though their display meshes are
        // not present in the RTAS. Punctual and tube lights remain zero-measure events.
        if (payload.nextPdf > 0.0
            && any(payload.nextThroughputWeight > 0.0))
        {
            ReferencedPathtracingLightSelectionContext selectionContext =
                ReferencedPathtracingCreateLightSelectionContext(
                    payload.positionWS,
                    payload.faceNormalWS,
                    payload.thinWalledTransmissionWeight > 0.0);
            previousLightSelectionContext = selectionContext;
            uint contextLightCount =
                ReferencedPathtracingGetContextLightCount(
                    selectionContext);
            if (bounceIndex == 0u)
            {
                lightSpatialIndexDiagnostic = float3(
                    (float)contextLightCount,
                    selectionContext.spatialAxis != 0xffffffffu
                        ? (float)(selectionContext.spatialAxis + 1u)
                        : 0.0,
                    (float)selectionContext.spatialFlags);
            }
            for (uint contextLightIndex = 0u;
                 contextLightIndex < contextLightCount;
                 ++contextLightIndex)
            {
                uint lightIndex =
                    ReferencedPathtracingGetContextLightIndex(
                        selectionContext,
                        contextLightIndex);
                ReferencedPathTracingLightRecord light =
                    ReferencedPathtracingLoadReferenceLight(lightIndex);
                float lightSelectionPdf =
                    ReferencedPathtracingGetUnifiedReferenceLightSelectionPdf(
                        selectionContext,
                        lightIndex,
                        light);
                ReferencedPathtracingSegmentLightHit segmentLightHit;
                if (!ReferencedPathtracingEvaluateSegmentLight(
                        payload.positionWS,
                        payload.nextDirectionWS,
                        lightIndex,
                        lightSelectionPdf,
                        light,
                        segmentLightHit))
                {
                    continue;
                }

                float fullLightPdf =
                    segmentLightHit.selectionPdf
                    * segmentLightHit.solidAnglePdf;
                float bsdfEstimatorWeight =
                    ReferencedPathtracingGetBsdfEstimatorWeight(
                        payload.nextPdf,
                        fullLightPdf,
                        payload.nextLobeIsDelta != 0u);
                if (bounceIndex == 0u
                    && !hasSegmentTransportDiagnostic)
                {
                    segmentTransportDiagnostic = float4(
                        segmentLightHit.selectionPdf,
                        segmentLightHit.solidAnglePdf,
                        payload.nextPdf,
                        bsdfEstimatorWeight);
                    hasSegmentTransportDiagnostic = true;
                }
                if (bsdfEstimatorWeight <= 0.0)
                    continue;

                float3 visibility =
                    TraceReferencedPathtracingCandidateVisibility(
                        payload.positionWS,
                        normalize(payload.faceNormalWS),
                        normalize(payload.nextDirectionWS),
                        segmentLightHit.distance,
                        !ReferencedPathtracingUsesCameraRelativeAtmosphere(),
                        segmentLightHit.shadowStrength,
                        ReferencedPathtracingHashStochasticTransparency(
                            payload.stochasticAlphaSeed
                            ^ ReferencedPathtracingHashStochasticTransparency(
                                lightIndex + 0x082efa98u)));
                float3 bsdfSampledDirect =
                    throughput
                    * payload.nextThroughputWeight
                    * segmentLightHit.radiance
                    * bsdfEstimatorWeight
                    * visibility;
                if (!IsFiniteReferencedPathtracingRadiance(
                        bsdfSampledDirect))
                {
                    invalidSampleMask.y = 1.0;
                    continue;
                }
                if (any(bsdfSampledDirect < -1e-6))
                    invalidSampleMask.y = 1.0;

                if (segmentLightHit.lightType
                    == REFERENCED_LIGHT_TYPE_DIRECTIONAL)
                {
                    AccumulateReferencedPathtracingMainLightRadiance(
                        bsdfSampledDirect,
                        payload.nextLobeClass,
                        bounceIndex,
                        primaryLobeClass,
                        true,
                        directLightingRadiance,
                        diffuseRadiance,
                        specularRadiance,
                        primaryDenoiserMainLightDiffuseRadiance,
                        primaryDenoiserMainLightSpecularRadiance);
                }
                else if (bounceIndex == 0u
                    && payload.nextLobeClass == 1u)
                {
                    diffuseHitDistance =
                        CombineReferencedPathtracingDenoiserHitDistance(
                            diffuseRadiance,
                            diffuseHitDistance,
                            bsdfSampledDirect,
                            segmentLightHit.distance);
                    diffuseRadiance += bsdfSampledDirect;
                    diffuseRayDirectionWS =
                        normalize(payload.nextDirectionWS);
                    diffuseDlssHitDistance = segmentLightHit.distance;
                    diffuseHitDistanceValid = true;
                }
                else if (bounceIndex == 0u
                    && payload.nextLobeClass == 2u)
                {
                    specularHitDistance =
                        CombineReferencedPathtracingDenoiserHitDistance(
                            specularRadiance,
                            specularHitDistance,
                            bsdfSampledDirect,
                            segmentLightHit.distance);
                    specularRadiance += bsdfSampledDirect;
                    specularRayDirectionWS =
                        normalize(payload.nextDirectionWS);
                    specularDlssHitDistance = segmentLightHit.distance;
                    specularHitDistanceValid = true;
                }
                else if (primaryLobeClass == 1u)
                {
                    diffuseRadiance += bsdfSampledDirect;
                }
                else if (primaryLobeClass == 2u)
                {
                    specularRadiance += bsdfSampledDirect;
                }
            }
        }

        if (payload.nextPdf <= 0.0)
            break;

        if (bounceIndex == 0u)
        {
            primaryLobeClass = payload.nextLobeClass;
            if (primaryLobeClass == 1u)
                diffuseRayDirectionWS = normalize(payload.nextDirectionWS);
            else if (primaryLobeClass == 2u)
                specularRayDirectionWS = normalize(payload.nextDirectionWS);
        }

        previousBsdfPdf = payload.nextPdf;
        previousBsdfWasDelta = payload.nextLobeIsDelta != 0u;
        previousReferenceSunReachable = false;
        throughput *= payload.nextThroughputWeight;
        if (!IsFiniteReferencedPathtracingRadiance(throughput)
            || MaxReferencedPathtracingComponent(throughput) <= 0.0)
        {
            if (!IsFiniteReferencedPathtracingRadiance(throughput))
                invalidSampleMask.z = 1.0;
            break;
        }

        if ((int)(bounceIndex + 1u) >= _ReferencedRussianRouletteStartBounce)
        {
            float survivalProbability = clamp(
                MaxReferencedPathtracingComponent(throughput),
                0.05,
                0.95);
            float russianRouletteSample =
                ReferencedPathtracingGetPathSample(
                    pixelCoord,
                    sampleIndex,
                    ReferencedPathtracingGetBounceSampleDimension(
                        bounceIndex,
                        kReferencedPathtracingRussianRouletteDimensionOffset),
                    sampleSeed,
                    pathSamplingMode);
            if (russianRouletteSample >= survivalProbability)
                break;
            throughput /= survivalProbability;
        }

        rayConeWidth = payload.rayConeWidth;
        ray.Direction = normalize(payload.nextDirectionWS);
        float nextRayBias;
        ray.Origin = OffsetReferencedPathtracingRayOrigin(
            payload.positionWS,
            normalize(payload.faceNormalWS),
            ray.Direction,
            nextRayBias);
        ray.TMin = nextRayBias;
        ray.TMax = _RayMaxDistance;
    }

    if (any(diffuseRadiance < -1e-6)
        || any(specularRadiance < -1e-6)
        || any(directLightingRadiance < -1e-6)
        || any(emissionRadiance < -1e-6))
    {
        invalidSampleMask.z = 1.0;
    }

    if (!IsFiniteReferencedPathtracingRadiance(diffuseRadiance))
    {
        invalidSampleMask.z = 1.0;
        diffuseRadiance = 0.0;
    }
    if (!IsFiniteReferencedPathtracingRadiance(specularRadiance))
    {
        invalidSampleMask.z = 1.0;
        specularRadiance = 0.0;
    }
    if (!IsFiniteReferencedPathtracingRadiance(directLightingRadiance))
    {
        invalidSampleMask.z = 1.0;
        directLightingRadiance = 0.0;
    }
    if (!IsFiniteReferencedPathtracingRadiance(
            primaryDenoiserMainLightDiffuseRadiance))
    {
        invalidSampleMask.z = 1.0;
        primaryDenoiserMainLightDiffuseRadiance = 0.0;
    }
    if (!IsFiniteReferencedPathtracingRadiance(
            primaryDenoiserMainLightSpecularRadiance))
    {
        invalidSampleMask.z = 1.0;
        primaryDenoiserMainLightSpecularRadiance = 0.0;
    }
    if (!IsFiniteReferencedPathtracingRadiance(emissionRadiance))
    {
        invalidSampleMask.z = 1.0;
        emissionRadiance = 0.0;
    }
    if (!IsFiniteReferencedPathtracingRadiance(environmentNeeRadiance))
    {
        invalidSampleMask.z = 1.0;
        environmentNeeRadiance = 0.0;
    }
    if (!IsFiniteReferencedPathtracingRadiance(environmentDirectDiffuseRadiance))
    {
        invalidSampleMask.z = 1.0;
        environmentDirectDiffuseRadiance = 0.0;
    }
    if (!IsFiniteReferencedPathtracingRadiance(environmentDirectSpecularRadiance))
    {
        invalidSampleMask.z = 1.0;
        environmentDirectSpecularRadiance = 0.0;
    }

    // A sampled directional emitter is represented at the visibility endpoint. Combining
    // that source distance with the existing indirect sample prevents direct sunlight from
    // inheriting an unrelated first-bounce hitT in REBLUR.
    float diffuseSignalHitDistance =
        CombineReferencedPathtracingDenoiserHitDistance(
            diffuseRadiance,
            diffuseHitDistance,
            primaryDenoiserMainLightDiffuseRadiance,
            kReferencedPathtracingShadowMaxDistance);
    float specularSignalHitDistance =
        CombineReferencedPathtracingDenoiserHitDistance(
            specularRadiance,
            specularHitDistance,
            primaryDenoiserMainLightSpecularRadiance,
            kReferencedPathtracingShadowMaxDistance);
    float diffuseNormHitDistance = primaryHit != 0u
        ? GetReferencedPathtracingReblurNormHitDistance(
            diffuseSignalHitDistance,
            primaryViewZ,
            primaryLinearRoughness)
        : 0.0;
    float specularNormHitDistance = primaryHit != 0u
        ? GetReferencedPathtracingReblurNormHitDistance(
            specularSignalHitDistance,
            primaryViewZ,
            primaryLinearRoughness)
        : 0.0;
    float3 surfaceRadiance =
        diffuseRadiance + specularRadiance + directLightingRadiance + emissionRadiance;
    float3 physicalRadiance =
        surfaceRadiance + cameraBackgroundRadiance;
    float3 debugRadiance = physicalRadiance;
    if (_ReferencedEnvironmentDebugMode == kReferencedEnvironmentDebugEnvironmentOnly)
    {
        debugRadiance =
            primaryEnvironmentBackgroundRadiance
            + indirectEnvironmentRadiance
            + environmentNeeRadiance;
    }
    else if (_ReferencedEnvironmentDebugMode
        == kReferencedEnvironmentDebugPrimaryBackgroundOnly)
    {
        debugRadiance = cameraBackgroundRadiance;
    }
    else if (_ReferencedEnvironmentDebugMode
        == kReferencedEnvironmentDebugIndirectMissOnly)
    {
        debugRadiance = indirectEnvironmentRadiance;
    }

    if (_ReferencedTransportDebugMode == kReferencedTransportDebugNeePdfs)
    {
        debugRadiance = neeTransportDiagnostic.xyz;
    }
    else if (_ReferencedTransportDebugMode
        == kReferencedTransportDebugNeeMisWeight)
    {
        debugRadiance = float3(
            neeTransportDiagnostic.w,
            neeTransportContributionValid ? 1.0 : 0.0,
            0.0);
    }
    else if (_ReferencedTransportDebugMode
        == kReferencedTransportDebugBsdfSegmentPdfs)
    {
        debugRadiance = segmentTransportDiagnostic.xyz;
    }
    else if (_ReferencedTransportDebugMode
        == kReferencedTransportDebugBsdfSegmentMisWeight)
    {
        debugRadiance = float3(
            segmentTransportDiagnostic.w,
            hasSegmentTransportDiagnostic ? 1.0 : 0.0,
            0.0);
    }
    else if (_ReferencedTransportDebugMode
        == kReferencedTransportDebugNeeLightIdentity)
    {
        debugRadiance = neeLightIdentityDiagnostic;
    }
    else if (_ReferencedTransportDebugMode
        == kReferencedTransportDebugInvalidSampleMask)
    {
        debugRadiance = invalidSampleMask;
    }
    else if (_ReferencedTransportDebugMode
        == kReferencedTransportDebugLightSpatialIndex)
    {
        // R: traversal candidate count, G: selected axis + 1,
        // B: context flags (indexed/fallback/outside/overflow).
        debugRadiance = lightSpatialIndexDiagnostic;
    }
    else if (_ReferencedTransportDebugMode
        == kReferencedTransportDebugPathSamples)
    {
        // R/G: film dimensions 0/1. B: bounce-zero RR dimension.
        debugRadiance = pathSampleDiagnostic;
    }
    else if (_ReferencedTransportDebugMode
        == kReferencedTransportDebugShadingNormal)
    {
        // R/G: original/consistent Ns dot Ng. B: diffuse terminator factor.
        debugRadiance = shadingNormalDiagnostic;
    }
    else if (_ReferencedTransportDebugMode
        == kReferencedTransportDebugPhysicalCamera)
    {
        // R/G: concentric aperture sample mapped to [0, 1]. B: DOF enabled.
        debugRadiance = physicalCameraDiagnostic;
    }
    else if (_ReferencedTransportDebugMode
        == kReferencedTransportDebugAtmosphereTransport)
    {
        // R/G: maximum atmosphere/cloud tracking budget fraction.
        // B: tracking overflow occurred.
        debugRadiance = atmosphereTransportDiagnostic;
    }
    else if (_ReferencedTransportDebugMode
        == kReferencedTransportDebugThinWalledTransmission)
    {
        // R: effective transmission weight. G: sampled transmission event.
        // B: selected NEE candidate lies in the opposite hemisphere.
        debugRadiance = thinWalledTransmissionDiagnostic;
    }
    else if (_ReferencedTransportDebugMode
        == kReferencedTransportDebugStochasticTransparency)
    {
        // RGB: most recent OpenPBR colored opacity along primary visibility.
        debugRadiance = stochasticTransparencyDiagnostic;
    }

    float physicalOutputAlpha =
        primaryAtmosphereHit != 0u
            ? cameraBackgroundAlpha
            : (primaryHit != 0u ? 1.0 : cameraBackgroundAlpha);
    float debugOutputAlpha =
        _ReferencedTransportDebugMode != kReferencedTransportDebugCombined
            || _ReferencedEnvironmentDebugMode
                != kReferencedEnvironmentDebugCombined
            ? 1.0
            : physicalOutputAlpha;
    _ReferencedPathTracingRadiance[pixelCoord] =
        float4(physicalRadiance, physicalOutputAlpha);
    _ReferencedPathTracingAlbedo[pixelCoord] = float4(
        primaryDenoisingAlbedo,
        primaryHit != 0u ? 1.0 : 0.0);
    _ReferencedPathTracingNormal[pixelCoord] = float4(
        primaryDenoisingNormalWS,
        primaryHit != 0u ? 1.0 : 0.0);
    _ReferencedPathTracingDebugTexture[pixelCoord] =
        float4(debugRadiance, debugOutputAlpha);
    _ReferencedPathTracingDirectLighting[pixelCoord] =
        float4(directLightingRadiance, primaryHit != 0u ? 1.0 : 0.0);
    _ReferencedPathTracingEmission[pixelCoord] = float4(emissionRadiance, primaryHit != 0u ? 1.0 : 0.0);
    _ReferencedPathTracingEnvironmentDirectDiffuse[pixelCoord] = float4(
        environmentDirectDiffuseRadiance,
        primaryHit != 0u ? 1.0 : 0.0);
    _ReferencedPathTracingEnvironmentDirectSpecular[pixelCoord] = float4(
        environmentDirectSpecularRadiance,
        primaryHit != 0u ? 1.0 : 0.0);
    float3 diffuseFallbackDirectionWS =
        ResolveReferencedPathtracingDlssRayDirection(
            primaryDenoisingNormalWS,
            -primaryCameraRayDirectionWS);
    float3 specularFallbackDirectionWS = reflect(
        primaryCameraRayDirectionWS,
        diffuseFallbackDirectionWS);
    _ReferencedDiffuseRayDirectionHitDistance[pixelCoord] =
        PackReferencedPathtracingDlssRayDirectionHitDistance(
            diffuseRayDirectionWS,
            diffuseDlssHitDistance,
            diffuseFallbackDirectionWS,
            primaryHit != 0u,
            diffuseHitDistanceValid);
    _ReferencedSpecularRayDirectionHitDistance[pixelCoord] =
        PackReferencedPathtracingDlssRayDirectionHitDistance(
            specularRayDirectionWS,
            specularDlssHitDistance,
            specularFallbackDirectionWS,
            primaryHit != 0u,
            specularHitDistanceValid);

    uint2 signalPixelCoord = pixelCoord;
    bool writeDiffuse = true;
    bool writeSpecular = true;
    if (_ReferencedReblurCheckerboardMode != 0)
    {
        signalPixelCoord.x >>= 1u;
        uint checkerboard =
            (pixelCoord.x ^ pixelCoord.y ^ (uint)_ReferencedFrameIndex) & 1u;
        uint diffuseCheckerboard = _ReferencedReblurCheckerboardMode == 1 ? 0u : 1u;
        writeDiffuse = checkerboard == diffuseCheckerboard;
        writeSpecular = !writeDiffuse;
    }

    if (writeDiffuse)
    {
        _ReferencedDiffuseRadianceHitDistance[signalPixelCoord] =
            PackReferencedPathtracingReblurSignal(
                diffuseRadiance
                    + primaryDenoiserMainLightDiffuseRadiance,
                diffuseNormHitDistance);
    }

    if (writeSpecular)
    {
        _ReferencedSpecularRadianceHitDistance[signalPixelCoord] =
            PackReferencedPathtracingReblurSignal(
                specularRadiance
                    + primaryDenoiserMainLightSpecularRadiance,
                specularNormHitDistance);
    }
}

[shader("miss")]
void MissReferencedPathtracing(inout ReferencedPathtracingPayload payload : SV_RayPayload)
{
    uint stochasticAlphaSeed = payload.stochasticAlphaSeed;
    float3 stochasticTransparencyWeight =
        payload.stochasticTransparencyWeight;
    float4 stochasticTransparencyDiagnostics =
        payload.stochasticTransparencyDiagnostics;
    InitializeReferencedPathtracingPayload(payload);
    payload.stochasticAlphaSeed = stochasticAlphaSeed;
    payload.stochasticTransparencyWeight =
        stochasticTransparencyWeight;
    payload.stochasticTransparencyDiagnostics =
        stochasticTransparencyDiagnostics;
}
