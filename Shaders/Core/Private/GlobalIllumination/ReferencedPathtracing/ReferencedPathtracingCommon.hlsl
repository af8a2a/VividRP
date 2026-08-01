#ifndef VIVIDRP_REFERENCED_PATH_TRACING_COMMON_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_COMMON_INCLUDED

int _ReferencedLocalLightNeeEnabled;
int _ReferencedShadingPointLightSelectionEnabled;
float _ReferencedGlobalLightProposalProbability;
int _ReferencedLightSpatialIndexEnabled;

// Lower-resolution lighting cubemap shared by BSDF miss, distribution build, and NEE.
TextureCube<float4> _ReferencedEnvironmentTexture;
SamplerState sampler_ReferencedEnvironmentTexture;
// Raw source cubemap retained at asset resolution for primary camera background evaluation.
TextureCube<float4> _ReferencedEnvironmentBackgroundTexture;
SamplerState sampler_ReferencedEnvironmentBackgroundTexture;
float4 _ReferencedEnvironmentTint;
// x: scene-linear intensity multiplier, y: rotation in degrees,
// z: maximum available mip, w: valid HDRI source.
float4 _ReferencedEnvironmentParameters;
int _ReferencedEnvironmentMode;
int _ReferencedEnvironmentLightingEnabled;
int _ReferencedEnvironmentCameraVisible;
int _ReferencedEnvironmentImportanceSamplingEnabled;
int _ReferencedEnvironmentNeeEnabled;
int _ReferencedEnvironmentSamplingMode;
int _ReferencedTransportEstimatorMode;
int _ReferencedTransportDebugMode;
int _ReferencedEnvironmentDebugMode;
float4 _ReferencedCameraClearColor;
int _ReferencedCameraSkyEnabled;

// Resource-independent Phase 2 atmosphere snapshot shared by spherical medium,
// solar-disk, virtual-ground, and visibility transport.
int _ReferencedAtmosphereFlags;
// xyz: planet center in world space, w: bottom radius in meters.
float4 _ReferencedAtmospherePlanetCenterBottomRadius;
// x: top radius in meters, y: Mie anisotropy, z: physical sky intensity.
float4 _ReferencedAtmosphereTopRadiusMieAnisotropy;
float4 _ReferencedAtmosphereGroundAlbedo;
// rgb: sea-level coefficient, w: density scale height in meters.
float4 _ReferencedAtmosphereRayleighScattering;
float4 _ReferencedAtmosphereRayleighExtinction;
float4 _ReferencedAtmosphereMieScattering;
float4 _ReferencedAtmosphereMieExtinction;
float4 _ReferencedAtmosphereOzoneExtinction;
// x: layer start radius, y: layer width, in meters.
float4 _ReferencedAtmosphereOzoneLayer;
// xyz: main directional-light direction, w: angular radius in radians.
float4 _ReferencedAtmosphereSunDirection;
// rgb: physical main-light illuminance, w: shadow strength.
float4 _ReferencedAtmosphereSunIlluminance;
int _ReferencedAtmosphereHasSun;
// x: cloud bottom radius, y: cloud top radius, z: coverage,
// w: full-density extinction in inverse meters.
float4 _ReferencedCloudLayerParameters;
// rgb: scattering albedo, w: Henyey-Greenstein anisotropy.
float4 _ReferencedCloudMaterialParameters;
// x: procedural noise scale in meters, y: stable seed,
// z: multiple-scattering approximation mode, w: approximation strength.
float4 _ReferencedCloudNoiseParameters;

static const int kReferencedEnvironmentModeHdri = 0;
static const int kReferencedEnvironmentModeReferenceAtmosphere = 1;
static const int kReferencedAtmosphereFlagActive = 1 << 0;
static const int kReferencedAtmosphereFlagLightingEnabled = 1 << 1;
static const int kReferencedAtmosphereFlagCameraVisible = 1 << 2;
static const int kReferencedAtmosphereFlagHoldout = 1 << 3;
static const int kReferencedAtmosphereFlagCloudsEnabled = 1 << 4;
static const int kReferencedAtmosphereFlagCloudsCameraVisible = 1 << 5;
static const int kReferencedAtmosphereFlagCloudsHoldout = 1 << 6;
static const int kReferencedAtmosphereFlagGroundCameraVisible = 1 << 7;
static const int kReferencedAtmosphereFlagGroundHoldout = 1 << 8;
static const int kReferencedAtmosphereFlagCameraRelativeRenderingSpace =
    1 << 9;
static const int kReferencedAtmosphereFlagOptimizedTransport = 1 << 10;

static const int kReferencedEnvironmentSamplingBsdfOnly = 0;
static const int kReferencedEnvironmentSamplingImportance = 1;
static const int kReferencedEnvironmentSamplingUniformSphere = 2;
static const int kReferencedTransportEstimatorMis = 0;
static const int kReferencedTransportEstimatorLightOnly = 1;
static const int kReferencedTransportEstimatorBsdfOnly = 2;
static const int kReferencedTransportDebugCombined = 0;
static const int kReferencedTransportDebugNeePdfs = 1;
static const int kReferencedTransportDebugNeeMisWeight = 2;
static const int kReferencedTransportDebugBsdfSegmentPdfs = 3;
static const int kReferencedTransportDebugBsdfSegmentMisWeight = 4;
static const int kReferencedTransportDebugNeeLightIdentity = 5;
static const int kReferencedTransportDebugInvalidSampleMask = 6;
static const int kReferencedTransportDebugLightSpatialIndex = 7;
static const int kReferencedTransportDebugPathSamples = 8;
static const int kReferencedTransportDebugShadingNormal = 9;
static const int kReferencedTransportDebugPhysicalCamera = 10;
static const int kReferencedTransportDebugAtmosphereTransport = 11;
static const int kReferencedTransportDebugThinWalledTransmission = 12;
static const int kReferencedTransportDebugStochasticTransparency = 13;
static const uint kReferencedPathtracingMaximumMaterialMediumDepth = 4u;
static const uint kReferencedPathtracingInvalidMediumInstance = 0xffffffffu;
static const float kReferencedPathtracingVacuumIor = 1.0;
static const int kReferencedEnvironmentDebugCombined = 0;
static const int kReferencedEnvironmentDebugEnvironmentOnly = 1;
static const int kReferencedEnvironmentDebugPrimaryBackgroundOnly = 2;
static const int kReferencedEnvironmentDebugIndirectMissOnly = 3;
static const float kReferencedPathtracingPi = 3.14159265358979323846;

bool VividReferencedPathtracingIsFinite(float value)
{
    return !isnan(value) && !isinf(value);
}

bool VividReferencedPathtracingIsFinite(float2 value)
{
    return !any(isnan(value)) && !any(isinf(value));
}

bool VividReferencedPathtracingIsFinite(float3 value)
{
    return !any(isnan(value)) && !any(isinf(value));
}

uint ReferencedPathtracingHashStochasticTransparency(uint value)
{
    value ^= value >> 16u;
    value *= 0x7feb352du;
    value ^= value >> 15u;
    value *= 0x846ca68bu;
    value ^= value >> 16u;
    return value;
}

float ReferencedPathtracingHashStochasticTransparencyToUnitFloat(uint value)
{
    return (float)(
        ReferencedPathtracingHashStochasticTransparency(value) >> 8u)
        * (1.0 / 16777216.0);
}

float3 ReferencedPathtracingEvaluateMaterialMediumTransmittance(
    float3 extinctionCoefficient,
    float distance)
{
    float3 extinction = max(extinctionCoefficient, 0.0);
    float finiteDistance = min(max(distance, 0.0), 1e30);
    float3 opticalDepth = min(extinction * finiteDistance, 80.0);
    return exp(-opticalDepth);
}

#define REFERENCED_ENVIRONMENT_DISTRIBUTION_VERSION 1
#define REFERENCED_ENVIRONMENT_HEADER_ELEMENT_COUNT 4u
#define REFERENCED_ENVIRONMENT_PDF_NORMALIZATION_OFFSET 0u
#define REFERENCED_ENVIRONMENT_AVERAGE_LUMINANCE_OFFSET 1u
#define REFERENCED_ENVIRONMENT_VALID_OFFSET 2u
#define REFERENCED_ENVIRONMENT_VERSION_OFFSET 3u
#define REFERENCED_ENVIRONMENT_MARGINAL_RESOLUTION 64u
#define REFERENCED_ENVIRONMENT_CONDITIONAL_RESOLUTION 128u
#define REFERENCED_ENVIRONMENT_MARGINAL_OFFSET \
    REFERENCED_ENVIRONMENT_HEADER_ELEMENT_COUNT
#define REFERENCED_ENVIRONMENT_CONDITIONAL_OFFSET \
    (REFERENCED_ENVIRONMENT_MARGINAL_OFFSET \
        + REFERENCED_ENVIRONMENT_MARGINAL_RESOLUTION)
#define REFERENCED_ENVIRONMENT_ELEMENT_COUNT \
    (REFERENCED_ENVIRONMENT_CONDITIONAL_OFFSET \
        + REFERENCED_ENVIRONMENT_CONDITIONAL_RESOLUTION \
        * REFERENCED_ENVIRONMENT_MARGINAL_RESOLUTION)
#define REFERENCED_ENVIRONMENT_MIN_LUMINANCE 1e-12

// A1 appends a density-column LUT after the frozen HDRI CDF layout. Each texel
// stores Rayleigh, Mie, and ozone path lengths in meters; extinction remains a
// runtime coefficient so A2 can reuse the same transport contract.
#define REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_VERSION 1
#define REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_HEADER_ELEMENT_COUNT 4u
#define REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_VALID_OFFSET \
    REFERENCED_ENVIRONMENT_ELEMENT_COUNT
#define REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_VERSION_OFFSET \
    (REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_VALID_OFFSET + 1u)
#define REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_SAMPLE_COUNT_OFFSET \
    (REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_VERSION_OFFSET + 1u)
#define REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_RESERVED_OFFSET \
    (REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_SAMPLE_COUNT_OFFSET + 1u)
#define REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_RADIAL_RESOLUTION 64u
#define REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_ZENITH_RESOLUTION 128u
#define REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_CHANNEL_COUNT 3u
#define REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_DATA_OFFSET \
    (REFERENCED_ENVIRONMENT_ELEMENT_COUNT \
        + REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_HEADER_ELEMENT_COUNT)
#define REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_REFERENCE_SAMPLE_COUNT 256u
#define REFERENCED_ATMOSPHERE_TRANSPORT_REFERENCE_SAMPLE_COUNT 1024u
// Finite local segments must not be evaluated by subtracting two large
// boundary optical depths. Near the ground that cancellation exposes the
// radial LUT cells as horizontal bands. Integrate segments that fit within a
// few minimum density-profile scale heights directly instead.
#define REFERENCED_ATMOSPHERE_LOCAL_SEGMENT_MAX_SAMPLE_COUNT 256u
#define REFERENCED_ATMOSPHERE_LOCAL_SEGMENT_SAMPLES_PER_PROFILE_SCALE 16.0
#define REFERENCED_ATMOSPHERE_LOCAL_SEGMENT_PROFILE_SCALE_COUNT 4.0
#define REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_ELEMENT_COUNT \
    (REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_DATA_OFFSET \
        + REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_RADIAL_RESOLUTION \
        * REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_ZENITH_RESOLUTION \
        * REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_CHANNEL_COUNT)

// A4 appends a conservative radial cloud-density majorant map. The procedural
// material remains authoritative; this map is acceleration metadata and never
// supplies camera radiance or a raster-cloud history.
#define REFERENCED_CLOUD_ACCELERATION_VERSION 1
#define REFERENCED_CLOUD_ACCELERATION_HEADER_ELEMENT_COUNT 4u
#define REFERENCED_CLOUD_ACCELERATION_VALID_OFFSET \
    REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_ELEMENT_COUNT
#define REFERENCED_CLOUD_ACCELERATION_VERSION_OFFSET \
    (REFERENCED_CLOUD_ACCELERATION_VALID_OFFSET + 1u)
#define REFERENCED_CLOUD_ACCELERATION_RESOLUTION_OFFSET \
    (REFERENCED_CLOUD_ACCELERATION_VERSION_OFFSET + 1u)
#define REFERENCED_CLOUD_ACCELERATION_SHADOW_SAMPLE_COUNT_OFFSET \
    (REFERENCED_CLOUD_ACCELERATION_RESOLUTION_OFFSET + 1u)
#define REFERENCED_CLOUD_ACCELERATION_RADIAL_RESOLUTION 64u
#define REFERENCED_CLOUD_ACCELERATION_DATA_OFFSET \
    (REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_ELEMENT_COUNT \
        + REFERENCED_CLOUD_ACCELERATION_HEADER_ELEMENT_COUNT)
#define REFERENCED_CLOUD_SHADOW_REFERENCE_SAMPLE_COUNT 96u
#define REFERENCED_CLOUD_SHADOW_NUMERICAL_REFERENCE_SAMPLE_COUNT 512u
#define REFERENCED_ATMOSPHERE_MAXIMUM_TRACKING_STEP_COUNT 1024u
#define REFERENCED_CLOUD_MAXIMUM_TRACKING_STEP_COUNT 1024u

#if defined(REFERENCED_ENVIRONMENT_DISTRIBUTION_BUILD)
RWStructuredBuffer<float> _ReferencedEnvironmentImportanceDistribution;
#else
StructuredBuffer<float> _ReferencedEnvironmentImportanceDistribution;
#endif

bool ReferencedPathtracingHasEnvironment()
{
    return _ReferencedEnvironmentMode == kReferencedEnvironmentModeHdri
        && _ReferencedEnvironmentParameters.w > 0.5;
}

bool ReferencedPathtracingHasReferenceAtmosphere()
{
    return _ReferencedEnvironmentMode
            == kReferencedEnvironmentModeReferenceAtmosphere
        && (_ReferencedAtmosphereFlags & kReferencedAtmosphereFlagActive) != 0;
}

bool ReferencedPathtracingUsesCameraRelativeAtmosphere()
{
    return ReferencedPathtracingHasReferenceAtmosphere()
        && (_ReferencedAtmosphereFlags
            & kReferencedAtmosphereFlagCameraRelativeRenderingSpace) != 0;
}

bool ReferencedPathtracingUsesOptimizedAtmosphereTransport()
{
    return ReferencedPathtracingHasReferenceAtmosphere()
        && (_ReferencedAtmosphereFlags
            & kReferencedAtmosphereFlagOptimizedTransport) != 0;
}

bool ReferencedPathtracingHasReferenceClouds()
{
    return ReferencedPathtracingHasReferenceAtmosphere()
        && (_ReferencedAtmosphereFlags
            & kReferencedAtmosphereFlagCloudsEnabled) != 0
        && _ReferencedCloudLayerParameters.y
            > _ReferencedCloudLayerParameters.x
        && _ReferencedCloudLayerParameters.z > 0.0
        && _ReferencedCloudLayerParameters.w > 0.0;
}

float ReferencedPathtracingEvaluateCloudHeightEnvelope(
    float normalizedHeight)
{
    normalizedHeight = saturate(normalizedHeight);
    float lowerRamp = saturate(normalizedHeight / 0.15);
    float upperRamp = saturate((1.0 - normalizedHeight) / 0.2);
    return min(lowerRamp, upperRamp);
}

struct ReferencedPathtracingAtmosphereRayInterval
{
    float entryDistance;
    float exitDistance;
    float groundDistance;
    uint intersectsAtmosphere;
    uint hitsGround;
};

float ReferencedPathtracingDifferenceOfSquares(float a, float b)
{
    return (a - b) * (a + b);
}

bool ReferencedPathtracingIntersectAtmosphereSphere(
    float3 rayOriginPS,
    float3 rayDirectionPS,
    float sphereRadius,
    out float2 intersection)
{
    intersection = -1.0;
    float directionLengthSquared =
        dot(rayDirectionPS, rayDirectionPS);
    float radialDistanceSquared =
        dot(rayOriginPS, rayOriginPS);
    if (directionLengthSquared <= 1e-12
        || radialDistanceSquared <= 1e-12
        || sphereRadius <= 0.0
        || isnan(directionLengthSquared)
        || isinf(directionLengthSquared)
        || isnan(radialDistanceSquared)
        || isinf(radialDistanceSquared))
    {
        return false;
    }

    float3 direction =
        rayDirectionPS * rsqrt(directionLengthSquared);
    float radialDistance = sqrt(radialDistanceSquared);
    float b = dot(rayOriginPS, direction);
    float c = ReferencedPathtracingDifferenceOfSquares(
        radialDistance,
        sphereRadius);
    float discriminant = b * b - c;
    if (discriminant < 0.0)
        return false;

    float rootDiscriminant = sqrt(max(discriminant, 0.0));
    float q = -b
        - (b >= 0.0 ? rootDiscriminant : -rootDiscriminant);
    float first;
    float second;
    if (abs(q) > 1e-8)
    {
        first = q;
        second = c / q;
    }
    else
    {
        first = -b - rootDiscriminant;
        second = -b + rootDiscriminant;
    }

    intersection = first <= second
        ? float2(first, second)
        : float2(second, first);
    return !any(isnan(intersection))
        && !any(isinf(intersection));
}

bool ReferencedPathtracingIntersectAtmospherePlanetSpaceWithGroundPolicy(
    float3 rayOriginPS,
    float3 rayDirectionPS,
    float maximumDistance,
    bool includeVirtualGround,
    out ReferencedPathtracingAtmosphereRayInterval interval)
{
    interval = (ReferencedPathtracingAtmosphereRayInterval)0;
    interval.groundDistance = -1.0;

    float bottomRadius =
        _ReferencedAtmospherePlanetCenterBottomRadius.w;
    float topRadius =
        _ReferencedAtmosphereTopRadiusMieAnisotropy.x;
    float radialDistance = length(rayOriginPS);
    float directionLengthSquared =
        dot(rayDirectionPS, rayDirectionPS);
    if (bottomRadius <= 0.0
        || topRadius <= bottomRadius
        || (includeVirtualGround
            && radialDistance < bottomRadius - 0.01)
        || directionLengthSquared <= 1e-12)
    {
        return false;
    }

    float3 direction =
        rayDirectionPS * rsqrt(directionLengthSquared);
    float2 atmosphereIntersection;
    if (!ReferencedPathtracingIntersectAtmosphereSphere(
            rayOriginPS,
            direction,
            topRadius,
            atmosphereIntersection)
        || atmosphereIntersection.y < 0.0)
    {
        return false;
    }

    float entryDistance = max(atmosphereIntersection.x, 0.0);
    float exitDistance = min(
        atmosphereIntersection.y,
        max(maximumDistance, 0.0));
    if (exitDistance < entryDistance)
        return false;

    float boundaryTolerance = max(bottomRadius * 1e-7, 0.01);
    float radialDirection = dot(rayOriginPS, direction);
    bool startsOnGroundTowardPlanet =
        includeVirtualGround
        && radialDistance <= bottomRadius + boundaryTolerance
        && radialDirection < 0.0;
    float groundDistance =
        startsOnGroundTowardPlanet ? 0.0 : -1.0;

    if (includeVirtualGround && !startsOnGroundTowardPlanet)
    {
        float2 groundIntersection;
        if (ReferencedPathtracingIntersectAtmosphereSphere(
                rayOriginPS,
                direction,
                bottomRadius,
                groundIntersection))
        {
            if (groundIntersection.x >= entryDistance
                && groundIntersection.x >= 0.0)
            {
                groundDistance = groundIntersection.x;
            }
            else if (groundIntersection.y >= entryDistance
                && groundIntersection.y >= 0.0
                && radialDistance > bottomRadius + boundaryTolerance)
            {
                groundDistance = groundIntersection.y;
            }
        }
    }

    bool hitsGround =
        groundDistance >= entryDistance
        && groundDistance <= exitDistance;
    if (hitsGround)
        exitDistance = groundDistance;

    interval.entryDistance = entryDistance;
    interval.exitDistance = exitDistance;
    interval.groundDistance = hitsGround
        ? groundDistance
        : -1.0;
    interval.intersectsAtmosphere = 1u;
    interval.hitsGround = hitsGround ? 1u : 0u;
    return true;
}

bool ReferencedPathtracingIntersectAtmospherePlanetSpace(
    float3 rayOriginPS,
    float3 rayDirectionPS,
    float maximumDistance,
    out ReferencedPathtracingAtmosphereRayInterval interval)
{
    return ReferencedPathtracingIntersectAtmospherePlanetSpaceWithGroundPolicy(
        rayOriginPS,
        rayDirectionPS,
        maximumDistance,
        true,
        interval);
}

bool ReferencedPathtracingIntersectAtmosphereWithGroundPolicy(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    bool includeVirtualGround,
    out ReferencedPathtracingAtmosphereRayInterval interval)
{
    float3 rayOriginPS =
        rayOriginWS
        - _ReferencedAtmospherePlanetCenterBottomRadius.xyz;
    return ReferencedPathtracingIntersectAtmospherePlanetSpaceWithGroundPolicy(
        rayOriginPS,
        rayDirectionWS,
        maximumDistance,
        includeVirtualGround,
        interval);
}

bool ReferencedPathtracingIntersectAtmosphere(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    out ReferencedPathtracingAtmosphereRayInterval interval)
{
    return ReferencedPathtracingIntersectAtmosphereWithGroundPolicy(
        rayOriginWS,
        rayDirectionWS,
        maximumDistance,
        true,
        interval);
}

float ReferencedPathtracingAtmosphereHorizonCosine(
    float radialDistance)
{
    float bottomRadius =
        _ReferencedAtmospherePlanetCenterBottomRadius.w;
    float radiusRatio =
        bottomRadius / max(radialDistance, bottomRadius);
    return -sqrt(saturate(1.0 - radiusRatio * radiusRatio));
}

float2 ReferencedPathtracingMapAtmosphereOpticalDepthLut(
    float radialDistance,
    float cosineZenith)
{
    float bottomRadius =
        _ReferencedAtmospherePlanetCenterBottomRadius.w;
    float topRadius =
        _ReferencedAtmosphereTopRadiusMieAnisotropy.x;
    float atmosphereDepth = max(topRadius - bottomRadius, 1.0);
    float normalizedHeight = saturate(
        (radialDistance - bottomRadius) / atmosphereDepth);
    float v = sqrt(normalizedHeight);

    float horizonCosine =
        ReferencedPathtracingAtmosphereHorizonCosine(
            radialDistance);
    bool aboveHorizon = cosineZenith >= horizonCosine;
    float denominator = aboveHorizon
        ? max(1.0 - horizonCosine, 1e-6)
        : max(1.0 + horizonCosine, 1e-6);
    float horizonDistance = aboveHorizon
        ? max(cosineZenith - horizonCosine, 0.0)
        : max(horizonCosine - cosineZenith, 0.0);
    float mappedCosine = sqrt(saturate(
        horizonDistance / denominator));
    float u = aboveHorizon
        ? 0.5 + 0.5 * mappedCosine
        : 0.5 - 0.5 * mappedCosine;

    float2 inverseResolution = rcp(float2(
        REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_ZENITH_RESOLUTION,
        REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_RADIAL_RESOLUTION));
    float halfTexelU = 0.5 * inverseResolution.x;
    u = aboveHorizon
        ? clamp(u, 0.5 + halfTexelU, 1.0 - halfTexelU)
        : clamp(u, halfTexelU, 0.5 - halfTexelU);
    v = clamp(v, 0.5 * inverseResolution.y, 1.0 - 0.5 * inverseResolution.y);
    return float2(u, v);
}

float2 ReferencedPathtracingUnmapAtmosphereOpticalDepthLut(
    float2 uv)
{
    float bottomRadius =
        _ReferencedAtmospherePlanetCenterBottomRadius.w;
    float topRadius =
        _ReferencedAtmosphereTopRadiusMieAnisotropy.x;
    float atmosphereDepth = max(topRadius - bottomRadius, 1.0);
    float radialDistance =
        bottomRadius + uv.y * uv.y * atmosphereDepth;
    float horizonCosine =
        ReferencedPathtracingAtmosphereHorizonCosine(
            radialDistance);
    float mappedCosine = uv.x * 2.0 - 1.0;
    float hemisphereSign = mappedCosine >= 0.0 ? 1.0 : -1.0;
    float cosineZenith =
        horizonCosine
        + hemisphereSign
        * mappedCosine
        * mappedCosine
        * (1.0 - hemisphereSign * horizonCosine);
    return float2(
        radialDistance,
        clamp(cosineZenith, -1.0, 1.0));
}

float3 ReferencedPathtracingEvaluateAtmosphereDensity(
    float radialDistance)
{
    float bottomRadius =
        _ReferencedAtmospherePlanetCenterBottomRadius.w;
    float height = max(radialDistance - bottomRadius, 0.0);
    float rayleighScaleHeight = max(
        _ReferencedAtmosphereRayleighScattering.w,
        1.0);
    float mieScaleHeight = max(
        _ReferencedAtmosphereMieScattering.w,
        1.0);
    float rayleighDensity =
        exp(-height / rayleighScaleHeight);
    float mieDensity =
        exp(-height / mieScaleHeight);

    float ozoneLayerStart =
        _ReferencedAtmosphereOzoneLayer.x;
    float ozoneLayerWidth = max(
        _ReferencedAtmosphereOzoneLayer.y,
        1.0);
    float ozoneCoordinate =
        (radialDistance - ozoneLayerStart)
        / ozoneLayerWidth;
    float ozoneDensity = saturate(
        1.0 - abs(ozoneCoordinate * 2.0 - 1.0));
    return float3(
        rayleighDensity,
        mieDensity,
        ozoneDensity);
}

float3 ReferencedPathtracingEvaluateAtmosphereExtinction(
    float radialDistance)
{
    float3 density =
        ReferencedPathtracingEvaluateAtmosphereDensity(
            radialDistance);
    return max(
        density.x
            * max(_ReferencedAtmosphereRayleighExtinction.rgb, 0.0)
        + density.y
            * max(_ReferencedAtmosphereMieExtinction.rgb, 0.0)
        + density.z
            * max(_ReferencedAtmosphereOzoneExtinction.rgb, 0.0),
        0.0);
}

float3 ReferencedPathtracingEvaluateAtmosphereScattering(
    float radialDistance)
{
    float3 density =
        ReferencedPathtracingEvaluateAtmosphereDensity(
            radialDistance);
    return max(
        density.x
            * max(_ReferencedAtmosphereRayleighScattering.rgb, 0.0)
        + density.y
            * max(_ReferencedAtmosphereMieScattering.rgb, 0.0),
        0.0);
}

float3 ReferencedPathtracingIntegrateAtmosphereDensityReferenceWithGroundPolicy(
    float3 rayOriginPS,
    float3 rayDirectionPS,
    float maximumDistance,
    uint sampleCount,
    bool includeVirtualGround)
{
    ReferencedPathtracingAtmosphereRayInterval interval;
    if (!ReferencedPathtracingIntersectAtmospherePlanetSpaceWithGroundPolicy(
            rayOriginPS,
            rayDirectionPS,
            maximumDistance,
            includeVirtualGround,
            interval))
    {
        return 0.0;
    }

    float segmentLength =
        max(interval.exitDistance - interval.entryDistance, 0.0);
    if (segmentLength <= 0.0)
        return 0.0;

    float3 direction = normalize(rayDirectionPS);
    sampleCount = clamp(sampleCount, 1u, 4096u);
    float stepLength = segmentLength / sampleCount;
    float3 densityOpticalDepth = 0.0;
    [loop]
    for (uint sampleIndex = 0u;
        sampleIndex < sampleCount;
        ++sampleIndex)
    {
        float sampleDistance =
            interval.entryDistance
            + (sampleIndex + 0.5) * stepLength;
        float radialDistance = length(
            rayOriginPS + direction * sampleDistance);
        densityOpticalDepth +=
            ReferencedPathtracingEvaluateAtmosphereDensity(
                radialDistance);
    }

    return max(densityOpticalDepth * stepLength, 0.0);
}

float3 ReferencedPathtracingIntegrateAtmosphereDensityReference(
    float3 rayOriginPS,
    float3 rayDirectionPS,
    float maximumDistance,
    uint sampleCount)
{
    return ReferencedPathtracingIntegrateAtmosphereDensityReferenceWithGroundPolicy(
        rayOriginPS,
        rayDirectionPS,
        maximumDistance,
        sampleCount,
        true);
}

uint ReferencedPathtracingGetAtmosphereOpticalDepthAddress(
    uint2 coordinate)
{
    uint texelIndex =
        coordinate.y
            * REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_ZENITH_RESOLUTION
        + coordinate.x;
    return REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_DATA_OFFSET
        + texelIndex
            * REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_CHANNEL_COUNT;
}

float3 ReferencedPathtracingReadAtmosphereOpticalDepth(
    uint2 coordinate)
{
    uint address =
        ReferencedPathtracingGetAtmosphereOpticalDepthAddress(
            coordinate);
    return float3(
        _ReferencedEnvironmentImportanceDistribution[address],
        _ReferencedEnvironmentImportanceDistribution[address + 1u],
        _ReferencedEnvironmentImportanceDistribution[address + 2u]);
}

bool ReferencedPathtracingHasAtmosphereOpticalDepthLut()
{
    float valid =
        _ReferencedEnvironmentImportanceDistribution[
            REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_VALID_OFFSET];
    float version =
        _ReferencedEnvironmentImportanceDistribution[
            REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_VERSION_OFFSET];
    return ReferencedPathtracingHasReferenceAtmosphere()
        && valid > 0.5
        && abs(
            version
            - REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_VERSION) < 0.5;
}

float3 ReferencedPathtracingSampleAtmosphereOpticalDepthLut(
    float3 positionPS,
    float3 directionPS)
{
    if (!ReferencedPathtracingHasAtmosphereOpticalDepthLut())
        return 0.0;

    float radialDistance = length(positionPS);
    float directionLengthSquared =
        dot(directionPS, directionPS);
    if (radialDistance <= 0.0
        || directionLengthSquared <= 1e-12)
    {
        return 0.0;
    }

    float3 direction =
        directionPS * rsqrt(directionLengthSquared);
    ReferencedPathtracingAtmosphereRayInterval boundaryInterval;
    if (!ReferencedPathtracingIntersectAtmospherePlanetSpace(
            positionPS,
            direction,
            3.402823466e+38,
            boundaryInterval)
        || boundaryInterval.exitDistance <= 0.01)
    {
        return 0.0;
    }

    float cosineZenith =
        dot(positionPS, direction) / radialDistance;
    float2 uv =
        ReferencedPathtracingMapAtmosphereOpticalDepthLut(
            radialDistance,
            cosineZenith);
    float2 resolution = float2(
        REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_ZENITH_RESOLUTION,
        REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_RADIAL_RESOLUTION);
    float2 texelCoordinate = uv * resolution - 0.5;
    int2 lowerCoordinate = (int2)floor(texelCoordinate);
    float2 interpolation = frac(texelCoordinate);
    uint2 lower = (uint2)clamp(
        lowerCoordinate,
        int2(0, 0),
        (int2)resolution - 1);
    uint2 upper = min(
        lower + 1u,
        (uint2)resolution - 1u);

    float3 lowerRow = lerp(
        ReferencedPathtracingReadAtmosphereOpticalDepth(
            uint2(lower.x, lower.y)),
        ReferencedPathtracingReadAtmosphereOpticalDepth(
            uint2(upper.x, lower.y)),
        interpolation.x);
    float3 upperRow = lerp(
        ReferencedPathtracingReadAtmosphereOpticalDepth(
            uint2(lower.x, upper.y)),
        ReferencedPathtracingReadAtmosphereOpticalDepth(
            uint2(upper.x, upper.y)),
        interpolation.x);
    return max(
        lerp(lowerRow, upperRow, interpolation.y),
        0.0);
}

float3 ReferencedPathtracingEvaluateAtmosphereSegmentOpticalDepthLut(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance)
{
    ReferencedPathtracingAtmosphereRayInterval interval;
    if (!ReferencedPathtracingIntersectAtmosphere(
            rayOriginWS,
            rayDirectionWS,
            maximumDistance,
            interval))
    {
        return 0.0;
    }

    float3 direction = normalize(rayDirectionWS);
    float3 originPS =
        rayOriginWS
        - _ReferencedAtmospherePlanetCenterBottomRadius.xyz;
    float3 startPS =
        originPS + direction * interval.entryDistance;
    float3 endPS =
        originPS + direction * interval.exitDistance;
    float3 startOpticalDepth =
        ReferencedPathtracingSampleAtmosphereOpticalDepthLut(
            startPS,
            direction);
    float3 endOpticalDepth =
        ReferencedPathtracingSampleAtmosphereOpticalDepthLut(
            endPS,
            direction);
    return max(startOpticalDepth - endOpticalDepth, 0.0);
}

float3 ReferencedPathtracingEvaluateAtmosphereSegmentOpticalDepthReferenceWithGroundPolicy(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    uint sampleCount,
    bool includeVirtualGround)
{
    float3 originPS =
        rayOriginWS
        - _ReferencedAtmospherePlanetCenterBottomRadius.xyz;
    return ReferencedPathtracingIntegrateAtmosphereDensityReferenceWithGroundPolicy(
        originPS,
        rayDirectionWS,
        maximumDistance,
        sampleCount,
        includeVirtualGround);
}

float3 ReferencedPathtracingEvaluateAtmosphereSegmentOpticalDepthReference(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    uint sampleCount)
{
    return ReferencedPathtracingEvaluateAtmosphereSegmentOpticalDepthReferenceWithGroundPolicy(
        rayOriginWS,
        rayDirectionWS,
        maximumDistance,
        sampleCount,
        true);
}

float3 ReferencedPathtracingAtmosphereTransmittanceFromDensityDepth(
    float3 densityOpticalDepth)
{
    float3 extinctionOpticalDepth =
        densityOpticalDepth.x
            * max(_ReferencedAtmosphereRayleighExtinction.rgb, 0.0)
        + densityOpticalDepth.y
            * max(_ReferencedAtmosphereMieExtinction.rgb, 0.0)
        + densityOpticalDepth.z
            * max(_ReferencedAtmosphereOzoneExtinction.rgb, 0.0);
    return exp(-min(max(extinctionOpticalDepth, 0.0), 80.0));
}

float3 ReferencedPathtracingEvaluateAtmosphereTransmittanceLut(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance)
{
    return ReferencedPathtracingAtmosphereTransmittanceFromDensityDepth(
        ReferencedPathtracingEvaluateAtmosphereSegmentOpticalDepthLut(
            rayOriginWS,
            rayDirectionWS,
            maximumDistance));
}

float3 ReferencedPathtracingEvaluateAtmosphereTransmittanceReferenceWithGroundPolicy(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    uint sampleCount,
    bool includeVirtualGround)
{
    return ReferencedPathtracingAtmosphereTransmittanceFromDensityDepth(
        ReferencedPathtracingEvaluateAtmosphereSegmentOpticalDepthReferenceWithGroundPolicy(
            rayOriginWS,
            rayDirectionWS,
            maximumDistance,
            sampleCount,
            includeVirtualGround));
}

float3 ReferencedPathtracingEvaluateAtmosphereTransmittanceReference(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    uint sampleCount)
{
    return ReferencedPathtracingEvaluateAtmosphereTransmittanceReferenceWithGroundPolicy(
        rayOriginWS,
        rayDirectionWS,
        maximumDistance,
        sampleCount,
        true);
}

bool ReferencedPathtracingResolveAtmosphereLocalSegmentSampleCountWithGroundPolicy(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    bool includeVirtualGround,
    out uint sampleCount)
{
    sampleCount = 1u;
    ReferencedPathtracingAtmosphereRayInterval interval;
    if (!ReferencedPathtracingIntersectAtmosphereWithGroundPolicy(
            rayOriginWS,
            rayDirectionWS,
            maximumDistance,
            includeVirtualGround,
            interval))
    {
        return false;
    }

    float segmentLength =
        max(interval.exitDistance - interval.entryDistance, 0.0);
    if (segmentLength <= 0.0)
        return false;

    float minimumProfileScale = max(
        min(
            min(
                _ReferencedAtmosphereRayleighScattering.w,
                _ReferencedAtmosphereMieScattering.w),
            _ReferencedAtmosphereOzoneLayer.y),
        1.0);
    float targetStepLength = max(
        minimumProfileScale
            / REFERENCED_ATMOSPHERE_LOCAL_SEGMENT_SAMPLES_PER_PROFILE_SCALE,
        1.0);
    float atmosphereDepth = max(
        _ReferencedAtmosphereTopRadiusMieAnisotropy.x
            - _ReferencedAtmospherePlanetCenterBottomRadius.w,
        1.0);
    float desiredThreshold = max(
        minimumProfileScale
            * REFERENCED_ATMOSPHERE_LOCAL_SEGMENT_PROFILE_SCALE_COUNT,
        atmosphereDepth
            / REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_RADIAL_RESOLUTION);
    float maximumResolvedLength =
        targetStepLength
        * REFERENCED_ATMOSPHERE_LOCAL_SEGMENT_MAX_SAMPLE_COUNT;
    float localSegmentThreshold = min(
        desiredThreshold,
        maximumResolvedLength);
    if (segmentLength > localSegmentThreshold)
        return false;

    sampleCount = (uint)clamp(
        ceil(segmentLength / targetStepLength),
        1.0,
        (float)REFERENCED_ATMOSPHERE_LOCAL_SEGMENT_MAX_SAMPLE_COUNT);
    return true;
}

bool ReferencedPathtracingResolveAtmosphereLocalSegmentSampleCount(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    out uint sampleCount)
{
    return ReferencedPathtracingResolveAtmosphereLocalSegmentSampleCountWithGroundPolicy(
        rayOriginWS,
        rayDirectionWS,
        maximumDistance,
        true,
        sampleCount);
}

float3 ReferencedPathtracingEvaluateAtmosphereTransmittanceWithGroundPolicy(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    bool includeVirtualGround)
{
    uint localSegmentSampleCount;
    bool usesLocalSegmentIntegration =
        ReferencedPathtracingResolveAtmosphereLocalSegmentSampleCountWithGroundPolicy(
            rayOriginWS,
            rayDirectionWS,
            maximumDistance,
            includeVirtualGround,
            localSegmentSampleCount);
    if (!ReferencedPathtracingUsesOptimizedAtmosphereTransport())
    {
        return ReferencedPathtracingEvaluateAtmosphereTransmittanceReferenceWithGroundPolicy(
            rayOriginWS,
            rayDirectionWS,
            maximumDistance,
            usesLocalSegmentIntegration
                ? localSegmentSampleCount
                : REFERENCED_ATMOSPHERE_TRANSPORT_REFERENCE_SAMPLE_COUNT,
            includeVirtualGround);
    }

    if (!includeVirtualGround)
    {
        return ReferencedPathtracingEvaluateAtmosphereTransmittanceReferenceWithGroundPolicy(
            rayOriginWS,
            rayDirectionWS,
            maximumDistance,
            usesLocalSegmentIntegration
                ? localSegmentSampleCount
                : REFERENCED_ATMOSPHERE_TRANSPORT_REFERENCE_SAMPLE_COUNT,
            false);
    }

    if (ReferencedPathtracingHasAtmosphereOpticalDepthLut())
    {
        if (usesLocalSegmentIntegration)
        {
            return ReferencedPathtracingEvaluateAtmosphereTransmittanceReference(
                rayOriginWS,
                rayDirectionWS,
                maximumDistance,
                localSegmentSampleCount);
        }

        return ReferencedPathtracingEvaluateAtmosphereTransmittanceLut(
            rayOriginWS,
            rayDirectionWS,
            maximumDistance);
    }

    return ReferencedPathtracingEvaluateAtmosphereTransmittanceReference(
        rayOriginWS,
        rayDirectionWS,
        maximumDistance,
        REFERENCED_ATMOSPHERE_OPTICAL_DEPTH_REFERENCE_SAMPLE_COUNT);
}

float3 ReferencedPathtracingEvaluateAtmosphereTransmittance(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance)
{
    return ReferencedPathtracingEvaluateAtmosphereTransmittanceWithGroundPolicy(
        rayOriginWS,
        rayDirectionWS,
        maximumDistance,
        true);
}

float3 ReferencedPathtracingEvaluateAtmosphereTransmittanceRelativeError(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    uint referenceSampleCount)
{
    float3 lutTransmittance =
        ReferencedPathtracingEvaluateAtmosphereTransmittanceLut(
            rayOriginWS,
            rayDirectionWS,
            maximumDistance);
    float3 referenceTransmittance =
        ReferencedPathtracingEvaluateAtmosphereTransmittanceReference(
            rayOriginWS,
            rayDirectionWS,
            maximumDistance,
            referenceSampleCount);
    return abs(lutTransmittance - referenceTransmittance)
        / max(referenceTransmittance, 1e-4);
}

float ReferencedPathtracingEnvironmentLuminance(float3 radiance)
{
    return max(dot(max(radiance, 0.0), float3(0.2126, 0.7152, 0.0722)), 0.0);
}

float3 ReferencedPathtracingRotateEnvironmentDirection(float3 directionWS)
{
    float rotationRadians = radians(_ReferencedEnvironmentParameters.y);
    float sine;
    float cosine;
    sincos(rotationRadians, sine, cosine);

    return float3(
        cosine * directionWS.x - sine * directionWS.z,
        directionWS.y,
        sine * directionWS.x + cosine * directionWS.z);
}

float3 ReferencedPathtracingSampleEnvironment(float3 directionWS)
{
    float directionLengthSquared = dot(directionWS, directionWS);
    if (!ReferencedPathtracingHasEnvironment()
        || directionLengthSquared <= 1e-8
        || isnan(directionLengthSquared)
        || isinf(directionLengthSquared))
    {
        return 0.0;
    }

    float3 rotatedDirectionWS = ReferencedPathtracingRotateEnvironmentDirection(
        directionWS * rsqrt(directionLengthSquared));
    float3 radiance = _ReferencedEnvironmentTexture.SampleLevel(
        sampler_ReferencedEnvironmentTexture,
        rotatedDirectionWS,
        0.0).rgb;
    radiance *= max(_ReferencedEnvironmentTint.rgb, 0.0)
        * max(_ReferencedEnvironmentParameters.x, 0.0);
    return !any(isnan(radiance)) && !any(isinf(radiance))
        ? max(radiance, 0.0)
        : 0.0;
}

float3 ReferencedPathtracingSampleBackgroundEnvironment(float3 directionWS)
{
    float directionLengthSquared = dot(directionWS, directionWS);
    if (!ReferencedPathtracingHasEnvironment()
        || directionLengthSquared <= 1e-8
        || isnan(directionLengthSquared)
        || isinf(directionLengthSquared))
    {
        return 0.0;
    }

    float3 rotatedDirectionWS = ReferencedPathtracingRotateEnvironmentDirection(
        directionWS * rsqrt(directionLengthSquared));
    float3 radiance = _ReferencedEnvironmentBackgroundTexture.SampleLevel(
        sampler_ReferencedEnvironmentBackgroundTexture,
        rotatedDirectionWS,
        0.0).rgb;
    radiance *= max(_ReferencedEnvironmentTint.rgb, 0.0)
        * max(_ReferencedEnvironmentParameters.x, 0.0);
    return !any(isnan(radiance)) && !any(isinf(radiance))
        ? max(radiance, 0.0)
        : 0.0;
}

float3 ReferencedPathtracingEvaluateLightingEnvironment(float3 directionWS)
{
    return _ReferencedEnvironmentLightingEnabled != 0
        ? ReferencedPathtracingSampleEnvironment(directionWS)
        : 0.0;
}

float2 ReferencedPathtracingMapDirectionToEquiarealUV(float3 directionWS)
{
    float directionLengthSquared = dot(directionWS, directionWS);
    if (directionLengthSquared <= 1e-8
        || isnan(directionLengthSquared)
        || isinf(directionLengthSquared))
    {
        return 0.5;
    }

    float3 direction = directionWS * rsqrt(directionLengthSquared);
    float phi = atan2(-direction.z, -direction.x);
    float u = frac(0.5 - phi * (0.5 / kReferencedPathtracingPi));
    float v = saturate((direction.y + 1.0) * 0.5);
    return float2(u, v);
}

float3 ReferencedPathtracingMapEquiarealUVToDirection(float2 uv)
{
    uv = saturate(uv);
    float phi = 2.0 * kReferencedPathtracingPi * (1.0 - uv.x);
    float cosineTheta = clamp(2.0 * uv.y - 1.0, -1.0, 1.0);
    float sineTheta = sqrt(saturate(1.0 - cosineTheta * cosineTheta));
    float sinePhi;
    float cosinePhi;
    sincos(phi, sinePhi, cosinePhi);
    return float3(cosinePhi * sineTheta, cosineTheta, sinePhi * sineTheta);
}

float ReferencedPathtracingReadEnvironmentCDF(
    uint bufferOffset,
    uint index)
{
    return _ReferencedEnvironmentImportanceDistribution[bufferOffset + index];
}

float ReferencedPathtracingSampleEnvironmentCDF(
    uint bufferOffset,
    uint elementCount,
    float randomValue)
{
    float sampleValue = min(saturate(randomValue), 0.99999994);
    uint lowerIndex = 0u;
    uint upperIndex = elementCount;
    while (lowerIndex + 1u < upperIndex)
    {
        uint middleIndex = (lowerIndex + upperIndex) >> 1u;
        float middleCDF = ReferencedPathtracingReadEnvironmentCDF(
            bufferOffset,
            middleIndex);
        if (middleCDF <= sampleValue)
            lowerIndex = middleIndex;
        else
            upperIndex = middleIndex;
    }

    float lowerCDF = ReferencedPathtracingReadEnvironmentCDF(
        bufferOffset,
        lowerIndex);
    float upperCDF =
        lowerIndex + 1u < elementCount
            ? ReferencedPathtracingReadEnvironmentCDF(
                bufferOffset,
                lowerIndex + 1u)
            : 1.0;
    float interval = max(upperCDF - lowerCDF, 1e-8);
    float fraction = saturate((sampleValue - lowerCDF) / interval);
    return (lowerIndex + fraction) / elementCount;
}

bool ReferencedPathtracingHasEnvironmentDistributionEnergy()
{
    float valid = _ReferencedEnvironmentImportanceDistribution[
        REFERENCED_ENVIRONMENT_VALID_OFFSET];
    float version = _ReferencedEnvironmentImportanceDistribution[
        REFERENCED_ENVIRONMENT_VERSION_OFFSET];
    float normalization = _ReferencedEnvironmentImportanceDistribution[
        REFERENCED_ENVIRONMENT_PDF_NORMALIZATION_OFFSET];
    return valid > 0.5
        && abs(version - REFERENCED_ENVIRONMENT_DISTRIBUTION_VERSION) < 0.5
        && normalization > 0.0
        && !isnan(normalization)
        && !isinf(normalization);
}

float ReferencedPathtracingEvaluateEnvironmentPdf(float3 directionWS)
{
    if (_ReferencedEnvironmentLightingEnabled == 0
        || !ReferencedPathtracingHasEnvironment()
        || !ReferencedPathtracingHasEnvironmentDistributionEnergy())
    {
        return 0.0;
    }

    if (_ReferencedEnvironmentSamplingMode
        == kReferencedEnvironmentSamplingUniformSphere)
    {
        return 0.25 / kReferencedPathtracingPi;
    }

    if (_ReferencedEnvironmentImportanceSamplingEnabled == 0
        || _ReferencedEnvironmentSamplingMode
            != kReferencedEnvironmentSamplingImportance)
    {
        return 0.0;
    }

    float normalization = _ReferencedEnvironmentImportanceDistribution[
        REFERENCED_ENVIRONMENT_PDF_NORMALIZATION_OFFSET];
    float3 radiance =
        ReferencedPathtracingEvaluateLightingEnvironment(directionWS);
    float pdf =
        ReferencedPathtracingEnvironmentLuminance(radiance) * normalization;
    return !isnan(pdf) && !isinf(pdf) ? max(pdf, 0.0) : 0.0;
}

bool ReferencedPathtracingSampleEnvironment(
    float2 randomValue,
    out float3 directionWS,
    out float3 radiance,
    out float pdf)
{
    directionWS = 0.0;
    radiance = 0.0;
    pdf = 0.0;

    if (_ReferencedEnvironmentLightingEnabled == 0
        || !ReferencedPathtracingHasEnvironment()
        || !ReferencedPathtracingHasEnvironmentDistributionEnergy())
    {
        return false;
    }

    float2 uv;
    if (_ReferencedEnvironmentSamplingMode
        == kReferencedEnvironmentSamplingUniformSphere)
    {
        uv = min(saturate(randomValue), 0.99999994);
    }
    else
    {
        if (_ReferencedEnvironmentImportanceSamplingEnabled == 0
            || _ReferencedEnvironmentSamplingMode
                != kReferencedEnvironmentSamplingImportance)
        {
            return false;
        }

        float v = ReferencedPathtracingSampleEnvironmentCDF(
            REFERENCED_ENVIRONMENT_MARGINAL_OFFSET,
            REFERENCED_ENVIRONMENT_MARGINAL_RESOLUTION,
            randomValue.x);
        uint rowIndex = min(
            (uint)(v * REFERENCED_ENVIRONMENT_MARGINAL_RESOLUTION),
            REFERENCED_ENVIRONMENT_MARGINAL_RESOLUTION - 1u);
        float u = ReferencedPathtracingSampleEnvironmentCDF(
            REFERENCED_ENVIRONMENT_CONDITIONAL_OFFSET
                + rowIndex * REFERENCED_ENVIRONMENT_CONDITIONAL_RESOLUTION,
            REFERENCED_ENVIRONMENT_CONDITIONAL_RESOLUTION,
            randomValue.y);
        uv = float2(u, v);
    }

    directionWS = ReferencedPathtracingMapEquiarealUVToDirection(uv);
    radiance = ReferencedPathtracingEvaluateLightingEnvironment(directionWS);
    pdf = ReferencedPathtracingEvaluateEnvironmentPdf(directionWS);
    return pdf > 0.0;
}

float ReferencedPathtracingPowerHeuristic(float pdfA, float pdfB)
{
    pdfA = !isnan(pdfA) && !isinf(pdfA) ? max(pdfA, 0.0) : 0.0;
    pdfB = !isnan(pdfB) && !isinf(pdfB) ? max(pdfB, 0.0) : 0.0;
    float maximumPdf = max(pdfA, pdfB);
    if (maximumPdf <= 0.0)
        return 0.0;

    // Normalize before squaring so a very sharp glossy PDF cannot overflow.
    float normalizedA = pdfA / maximumPdf;
    float normalizedB = pdfB / maximumPdf;
    float squaredA = normalizedA * normalizedA;
    float squaredB = normalizedB * normalizedB;
    return squaredA / max(squaredA + squaredB, 1e-20);
}

float ReferencedPathtracingOneMinusCosFromSinSquared(float sinThetaSquared)
{
    sinThetaSquared = saturate(sinThetaSquared);
    return sinThetaSquared < 0.01
        ? sinThetaSquared * (0.5 + 0.125 * sinThetaSquared)
        : 1.0 - sqrt(max(1.0 - sinThetaSquared, 0.0));
}

void ReferencedPathtracingBuildDirectionalBasis(
    float3 directionWS,
    out float3 basisX,
    out float3 basisY)
{
    float signZ = directionWS.z >= 0.0 ? 1.0 : -1.0;
    float a = -rcp(signZ + directionWS.z);
    float b = directionWS.x * directionWS.y * a;
    basisX = float3(
        1.0 + signZ * directionWS.x * directionWS.x * a,
        signZ * b,
        -signZ * directionWS.x);
    basisY = float3(
        b,
        signZ + directionWS.y * directionWS.y * a,
        -directionWS.y);
}

bool ReferencedPathtracingGetDirectionalLightSolidAnglePdf(
    float angularDiameter,
    out float cosThetaMax,
    out float lightPdf)
{
    cosThetaMax = 1.0;
    lightPdf = 0.0;

    float halfAngularDiameter = 0.5
        * clamp(
            angularDiameter,
            0.0,
            0.5 * kReferencedPathtracingPi);
    float sinThetaMax = sin(halfAngularDiameter);
    float sinThetaMaxSquared = sinThetaMax * sinThetaMax;
    if (sinThetaMaxSquared <= 1e-12)
        return false;

    float oneMinusCosThetaMax =
        ReferencedPathtracingOneMinusCosFromSinSquared(
            sinThetaMaxSquared);
    float solidAngle =
        2.0 * kReferencedPathtracingPi * oneMinusCosThetaMax;
    if (solidAngle <= 0.0 || isnan(solidAngle) || isinf(solidAngle))
        return false;

    cosThetaMax = 1.0 - oneMinusCosThetaMax;
    lightPdf = rcp(solidAngle);
    return !isnan(lightPdf) && !isinf(lightPdf);
}

void ReferencedPathtracingSampleDirectionalLight(
    float3 centerDirectionWS,
    float angularDiameter,
    float2 randomSample,
    out float3 sampledDirectionWS,
    out float lightPdf,
    out uint isDelta)
{
    sampledDirectionWS = centerDirectionWS;
    lightPdf = 0.0;
    isDelta = 1u;

    float cosThetaMax;
    if (!ReferencedPathtracingGetDirectionalLightSolidAnglePdf(
            angularDiameter,
            cosThetaMax,
            lightPdf))
    {
        return;
    }

    float cosTheta = lerp(
        1.0,
        cosThetaMax,
        saturate(randomSample.y));
    float sinTheta = sqrt(max(1.0 - cosTheta * cosTheta, 0.0));
    float phi =
        2.0 * kReferencedPathtracingPi * saturate(randomSample.x);
    float sinPhi;
    float cosPhi;
    sincos(phi, sinPhi, cosPhi);

    float3 basisX;
    float3 basisY;
    ReferencedPathtracingBuildDirectionalBasis(
        centerDirectionWS,
        basisX,
        basisY);
    sampledDirectionWS = normalize(
        basisX * (sinTheta * cosPhi)
        + basisY * (sinTheta * sinPhi)
        + centerDirectionWS * cosTheta);
    isDelta = 0u;
}

bool ReferencedPathtracingEvaluateDirectionalLightPdf(
    float3 centerDirectionWS,
    float angularDiameter,
    float3 directionWS,
    out float lightPdf)
{
    float cosThetaMax;
    if (!ReferencedPathtracingGetDirectionalLightSolidAnglePdf(
            angularDiameter,
            cosThetaMax,
            lightPdf))
    {
        return false;
    }

    float directionLengthSquared = dot(directionWS, directionWS);
    float centerLengthSquared = dot(
        centerDirectionWS,
        centerDirectionWS);
    if (directionLengthSquared <= 1e-8
        || centerLengthSquared <= 1e-8
        || isnan(directionLengthSquared)
        || isinf(directionLengthSquared)
        || isnan(centerLengthSquared)
        || isinf(centerLengthSquared))
    {
        lightPdf = 0.0;
        return false;
    }

    float3 direction = directionWS * rsqrt(directionLengthSquared);
    float3 centerDirection = centerDirectionWS
        * rsqrt(centerLengthSquared);
    return dot(direction, centerDirection) >= cosThetaMax;
}

bool ReferencedPathtracingIsLightNeeEligible(bool bsdfReachable)
{
    return _ReferencedTransportEstimatorMode
            != kReferencedTransportEstimatorBsdfOnly
        || !bsdfReachable;
}

float ReferencedPathtracingGetLightEstimatorWeight(
    bool bsdfReachable,
    bool singular,
    float lightPdf,
    float bsdfPdf)
{
    if (singular || !bsdfReachable)
    {
        return 1.0;
    }

    if (_ReferencedTransportEstimatorMode
        == kReferencedTransportEstimatorBsdfOnly)
    {
        return 0.0;
    }

    if (_ReferencedTransportEstimatorMode
        == kReferencedTransportEstimatorLightOnly)
    {
        return 1.0;
    }

    return ReferencedPathtracingPowerHeuristic(lightPdf, bsdfPdf);
}

float ReferencedPathtracingGetBsdfEstimatorWeight(
    float bsdfPdf,
    float lightPdf,
    bool sampledDeltaEvent)
{
    // Delta BSDF directions cannot be generated by a continuous light proposal.
    if (sampledDeltaEvent)
        return 1.0;

    // BSDF sampling is the mandatory fallback when NEE has no support.
    if (lightPdf <= 0.0 || isnan(lightPdf) || isinf(lightPdf))
    {
        return 1.0;
    }

    if (_ReferencedTransportEstimatorMode
        == kReferencedTransportEstimatorBsdfOnly)
    {
        return 1.0;
    }

    if (_ReferencedTransportEstimatorMode
        == kReferencedTransportEstimatorLightOnly)
    {
        return 0.0;
    }

    return ReferencedPathtracingPowerHeuristic(bsdfPdf, lightPdf);
}

float4 ReferencedPathtracingEvaluateCameraBackground(float3 directionWS)
{
    if (_ReferencedCameraSkyEnabled != 0
        && ReferencedPathtracingHasReferenceAtmosphere()
        && (_ReferencedAtmosphereFlags
            & kReferencedAtmosphereFlagCameraVisible) != 0)
    {
        float atmosphereAlpha =
            (_ReferencedAtmosphereFlags
                & kReferencedAtmosphereFlagHoldout) != 0
                ? 0.0
                : 1.0;
        // Reference Atmosphere has no emissive skydome. A3 evaluates the finite
        // solar disk separately; outer space itself remains black.
        return float4(0.0, 0.0, 0.0, atmosphereAlpha);
    }

    if (_ReferencedCameraSkyEnabled != 0
        && _ReferencedEnvironmentCameraVisible != 0
        && ReferencedPathtracingHasEnvironment())
    {
        return float4(
            ReferencedPathtracingSampleBackgroundEnvironment(directionWS),
            1.0);
    }

    return float4(
        max(_ReferencedCameraClearColor.rgb, 0.0),
        saturate(_ReferencedCameraClearColor.a));
}

#define REFERENCED_PATHTRACING_PAYLOAD_UINT4_COUNT 10
#define REFERENCED_PATHTRACING_PAYLOAD_DWORD_COUNT 40u

// Raygen input layout. Closest-hit unpacks these values before overwriting the
// same storage with the surface result.
#define REFERENCED_PAYLOAD_INPUT_PATH_THROUGHPUT 0u
#define REFERENCED_PAYLOAD_INPUT_BSDF_RANDOM 3u
#define REFERENCED_PAYLOAD_INPUT_DIRECT_LIGHT_RANDOM 6u
#define REFERENCED_PAYLOAD_INPUT_STOCHASTIC_ALPHA_SEED 9u
#define REFERENCED_PAYLOAD_INPUT_RAY_CONE_WIDTH 10u
#define REFERENCED_PAYLOAD_INPUT_RAY_CONE_SPREAD_ANGLE 11u
#define REFERENCED_PAYLOAD_INPUT_ACTIVE_MEDIUM_IOR 12u
#define REFERENCED_PAYLOAD_INPUT_PARENT_MEDIUM_IOR 13u
#define REFERENCED_PAYLOAD_INPUT_ACTIVE_MEDIUM_EXTINCTION 14u
#define REFERENCED_PAYLOAD_INPUT_ACTIVE_MEDIUM_INSTANCE_INDEX 17u
#define REFERENCED_PAYLOAD_INPUT_RTXTF_RANDOM 18u

// Closest-hit result layout. Raygen reconstructs positionWS from its RayDesc
// and hit distance. Unit directions use octahedral UNORM16x2 encoding. Material
// medium extinction uses FP16x3 and scattering uses UNORM8x3 + SNORM8 so the
// complete result remains exactly 40 DWORDs (160 bytes).
#define REFERENCED_PAYLOAD_RESULT_RAY_CONE_WIDTH 0u
#define REFERENCED_PAYLOAD_RESULT_FACE_NORMAL_WS_PACKED 1u
#define REFERENCED_PAYLOAD_RESULT_EMISSION 2u
#define REFERENCED_PAYLOAD_RESULT_NEE_DIFFUSE_RADIANCE 5u
#define REFERENCED_PAYLOAD_RESULT_NEE_SPECULAR_RADIANCE 8u
#define REFERENCED_PAYLOAD_RESULT_NEE_DIRECTION_WS_PACKED 11u
#define REFERENCED_PAYLOAD_RESULT_NEE_DISTANCE 12u
#define REFERENCED_PAYLOAD_RESULT_NEE_SELECTION_PDF 13u
#define REFERENCED_PAYLOAD_RESULT_NEE_SOLID_ANGLE_PDF 14u
#define REFERENCED_PAYLOAD_RESULT_NEE_BSDF_PDF 15u
#define REFERENCED_PAYLOAD_RESULT_NEE_SHADOW_STRENGTH 16u
#define REFERENCED_PAYLOAD_RESULT_NEE_LIGHT_INDEX 17u
#define REFERENCED_PAYLOAD_RESULT_FLAGS 18u
#define REFERENCED_PAYLOAD_RESULT_NEXT_DIRECTION_WS_PACKED 19u
#define REFERENCED_PAYLOAD_RESULT_NEXT_THROUGHPUT_WEIGHT 20u
#define REFERENCED_PAYLOAD_RESULT_NEXT_PDF 23u
#define REFERENCED_PAYLOAD_RESULT_LINEAR_ROUGHNESS 24u
#define REFERENCED_PAYLOAD_RESULT_HIT_DISTANCE 25u
#define REFERENCED_PAYLOAD_RESULT_DENOISING_ALBEDO 26u
#define REFERENCED_PAYLOAD_RESULT_DENOISING_NORMAL_WS_PACKED 29u
#define REFERENCED_PAYLOAD_RESULT_SHADING_NORMAL_DIAGNOSTICS 30u
#define REFERENCED_PAYLOAD_RESULT_THIN_WALLED_TRANSMISSION_WEIGHT 33u
#define REFERENCED_PAYLOAD_RESULT_STOCHASTIC_TRANSPARENCY_OPACITY 34u
#define REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_IOR 35u
#define REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_EXTINCTION 36u
#define REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_SCATTERING 38u
#define REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_INSTANCE_INDEX 39u

#define REFERENCED_PAYLOAD_FLAG_HIT (1u << 0u)
#define REFERENCED_PAYLOAD_FLAG_NEE_VALID (1u << 1u)
#define REFERENCED_PAYLOAD_FLAG_NEXT_LOBE_IS_DELTA (1u << 2u)
#define REFERENCED_PAYLOAD_FLAG_NEXT_LOBE_IS_TRANSMISSION (1u << 3u)
#define REFERENCED_PAYLOAD_FLAG_NEXT_LOBE_CLASS_SHIFT 4u
#define REFERENCED_PAYLOAD_FLAG_NEXT_LOBE_CLASS_MASK (3u << REFERENCED_PAYLOAD_FLAG_NEXT_LOBE_CLASS_SHIFT)
#define REFERENCED_PAYLOAD_FLAG_MEDIUM_TRANSITION_SHIFT 6u
#define REFERENCED_PAYLOAD_FLAG_MEDIUM_TRANSITION_MASK (3u << REFERENCED_PAYLOAD_FLAG_MEDIUM_TRANSITION_SHIFT)
#define REFERENCED_PAYLOAD_FLAG_STOCHASTIC_TRANSPARENCY_SEEN (1u << 8u)
#define REFERENCED_PAYLOAD_FLAG_NEE_LIGHT_TYPE_SHIFT 9u
#define REFERENCED_PAYLOAD_FLAG_NEE_LIGHT_TYPE_MASK (15u << REFERENCED_PAYLOAD_FLAG_NEE_LIGHT_TYPE_SHIFT)
#define REFERENCED_PAYLOAD_FLAG_NEE_FLAGS_SHIFT 13u
#define REFERENCED_PAYLOAD_FLAG_NEE_FLAGS_MASK (255u << REFERENCED_PAYLOAD_FLAG_NEE_FLAGS_SHIFT)

struct ReferencedPathtracingPayload
{
    uint4 packed[REFERENCED_PATHTRACING_PAYLOAD_UINT4_COUNT];
};

// Opaque visibility traversal never invokes material hit shaders. It therefore
// needs only a committed-hit sentinel and a matching dedicated miss shader.
struct ReferencedPathtracingVisibilityPayload
{
    uint hit;
};

struct ReferencedPathtracingPayloadInput
{
    float3 pathThroughput;
    float3 bsdfRandom;
    float3 directLightRandom;
    uint stochasticAlphaSeed;
    float3 rtxtfRandom;
    float rayConeWidth;
    float rayConeSpreadAngle;
    // RTXPT-style compact view of the active nested dielectric stack. The
    // complete four-entry stack stays in raygen; closest-hit only needs the
    // current and parent media to prepare the interface BSDF.
    float activeMediumIor;
    float parentMediumIor;
    float3 activeMediumExtinction;
    uint activeMediumInstanceIndex;
};

struct ReferencedPathtracingSurfaceResult
{
    float rayConeWidth;
    float3 positionWS;
    float3 faceNormalWS;
    float3 emission;
    float3 neeDiffuseRadiance;
    float3 neeSpecularRadiance;
    float3 neeDirectionWS;
    float neeDistance;
    // Keep discrete and conditional PDFs separate for future proposal mixtures.
    float neeSelectionPdf;
    float neeSolidAnglePdf;
    float neeBsdfPdf;
    float neeShadowStrength;
    uint neeLightIndex;
    uint neeLightType;
    uint neeFlags;
    uint neeValid;
    float3 nextDirectionWS;
    float3 nextThroughputWeight;
    float nextPdf;
    float linearRoughness;
    float hitDistance;
    // Primary-surface OIDN features. Albedo is diffuse reflectance and the
    // normal is the same view-consistent shading normal used by the BSDF.
    float3 denoisingAlbedo;
    float3 denoisingNormalWS;
    // R/G: unadjusted/consistent shading-normal agreement with the geometric
    // normal. B: minimum diffuse shadow-terminator factor for this vertex.
    float3 shadingNormalDiagnostics;
    // Effective dielectric transmission fraction after metallic suppression.
    float thinWalledTransmissionWeight;
    // RGB: most recent scalar geometry opacity. A: whether a transparent
    // candidate was encountered. The payload stores this as one float and one bit.
    float4 stochasticTransparencyDiagnostics;
    uint nextLobeClass;
    uint nextLobeIsDelta;
    uint nextLobeIsTransmission;
    // A non-zero transition is applied only after a valid solid-transmission
    // sample: +1 enters this medium, -1 exits the current stack entry.
    int mediumTransition;
    float nextMediumIor;
    float3 nextMediumExtinction;
    uint nextMediumScattering;
    uint nextMediumInstanceIndex;
    uint hit;
};

uint LoadReferencedPathtracingPayloadUint(
    ReferencedPathtracingPayload payload,
    uint dwordOffset)
{
    return payload.packed[dwordOffset >> 2u][dwordOffset & 3u];
}

void StoreReferencedPathtracingPayloadUint(
    inout ReferencedPathtracingPayload payload,
    uint dwordOffset,
    uint value)
{
    payload.packed[dwordOffset >> 2u][dwordOffset & 3u] = value;
}

float LoadReferencedPathtracingPayloadFloat(
    ReferencedPathtracingPayload payload,
    uint dwordOffset)
{
    return asfloat(LoadReferencedPathtracingPayloadUint(payload, dwordOffset));
}

void StoreReferencedPathtracingPayloadFloat(
    inout ReferencedPathtracingPayload payload,
    uint dwordOffset,
    float value)
{
    StoreReferencedPathtracingPayloadUint(payload, dwordOffset, asuint(value));
}

float3 LoadReferencedPathtracingPayloadFloat3(
    ReferencedPathtracingPayload payload,
    uint dwordOffset)
{
    return float3(
        LoadReferencedPathtracingPayloadFloat(payload, dwordOffset),
        LoadReferencedPathtracingPayloadFloat(payload, dwordOffset + 1u),
        LoadReferencedPathtracingPayloadFloat(payload, dwordOffset + 2u));
}

void StoreReferencedPathtracingPayloadFloat3(
    inout ReferencedPathtracingPayload payload,
    uint dwordOffset,
    float3 value)
{
    StoreReferencedPathtracingPayloadFloat(payload, dwordOffset, value.x);
    StoreReferencedPathtracingPayloadFloat(payload, dwordOffset + 1u, value.y);
    StoreReferencedPathtracingPayloadFloat(payload, dwordOffset + 2u, value.z);
}

uint2 PackReferencedPathtracingMaterialMediumExtinction(
    float3 extinctionCoefficient)
{
    float3 extinction = clamp(
        max(extinctionCoefficient, 0.0),
        0.0,
        65504.0);
    return uint2(
        (f32tof16(extinction.x) & 0xffffu)
            | (f32tof16(extinction.y) << 16u),
        f32tof16(extinction.z) & 0xffffu);
}

float3 UnpackReferencedPathtracingMaterialMediumExtinction(
    uint2 packedExtinction)
{
    return float3(
        f16tof32(packedExtinction.x & 0xffffu),
        f16tof32(packedExtinction.x >> 16u),
        f16tof32(packedExtinction.y & 0xffffu));
}

uint PackReferencedPathtracingMaterialMediumScattering(
    float3 scatteringAlbedo,
    float anisotropy)
{
    uint3 albedo = (uint3)round(saturate(scatteringAlbedo) * 255.0);
    int signedAnisotropy = (int)round(
        clamp(anisotropy, -1.0, 1.0) * 127.0);
    return albedo.x
        | (albedo.y << 8u)
        | (albedo.z << 16u)
        | ((uint)(signedAnisotropy & 0xff) << 24u);
}

float4 UnpackReferencedPathtracingMaterialMediumScattering(
    uint packedScattering)
{
    float3 scatteringAlbedo = float3(
        packedScattering & 0xffu,
        (packedScattering >> 8u) & 0xffu,
        (packedScattering >> 16u) & 0xffu)
        * (1.0 / 255.0);
    int signedAnisotropy = (int)packedScattering >> 24;
    return float4(
        scatteringAlbedo,
        clamp((float)signedAnisotropy / 127.0, -1.0, 1.0));
}

uint PackReferencedPathtracingUnitVector(float3 value)
{
    float inverseL1Norm = rcp(max(
        dot(abs(value), float3(1.0, 1.0, 1.0)),
        0.0000001));
    float3 octahedral = value * inverseL1Norm;
    if (octahedral.z < 0.0)
    {
        float2 signNotZero = float2(
            octahedral.x >= 0.0 ? 1.0 : -1.0,
            octahedral.y >= 0.0 ? 1.0 : -1.0);
        octahedral.xy =
            (1.0 - abs(octahedral.yx)) * signNotZero;
    }

    uint2 quantized = (uint2)round(
        saturate(octahedral.xy * 0.5 + 0.5) * 65535.0);
    return quantized.x | (quantized.y << 16u);
}

float3 UnpackReferencedPathtracingUnitVector(uint packedValue)
{
    float2 octahedral =
        float2(packedValue & 0xffffu, packedValue >> 16u)
        * (2.0 / 65535.0)
        - 1.0;
    float3 value = float3(
        octahedral,
        1.0 - abs(octahedral.x) - abs(octahedral.y));
    float fold = saturate(-value.z);
    value.xy += float2(
        value.x >= 0.0 ? -fold : fold,
        value.y >= 0.0 ? -fold : fold);
    return normalize(value);
}

void InitializeReferencedPathtracingPayload(out ReferencedPathtracingPayload payload)
{
    [unroll]
    for (uint packedIndex = 0u;
        packedIndex < REFERENCED_PATHTRACING_PAYLOAD_UINT4_COUNT;
        ++packedIndex)
    {
        payload.packed[packedIndex] = 0u;
    }
}

void InitializeReferencedPathtracingPayloadInput(
    out ReferencedPathtracingPayloadInput input)
{
    input.pathThroughput = 1.0;
    input.bsdfRandom = 0.0;
    input.directLightRandom = 0.0;
    input.stochasticAlphaSeed = 0u;
    input.rtxtfRandom = 0.0;
    input.rayConeWidth = 0.0;
    input.rayConeSpreadAngle = 0.0;
    input.activeMediumIor = 1.0;
    input.parentMediumIor = 1.0;
    input.activeMediumExtinction = 0.0;
    input.activeMediumInstanceIndex =
        kReferencedPathtracingInvalidMediumInstance;
}

void InitializeReferencedPathtracingSurfaceResult(
    out ReferencedPathtracingSurfaceResult result)
{
    result.rayConeWidth = 0.0;
    result.positionWS = 0.0;
    result.faceNormalWS = 0.0;
    result.emission = 0.0;
    result.neeDiffuseRadiance = 0.0;
    result.neeSpecularRadiance = 0.0;
    result.neeDirectionWS = 0.0;
    result.neeDistance = 0.0;
    result.neeSelectionPdf = 0.0;
    result.neeSolidAnglePdf = 0.0;
    result.neeBsdfPdf = 0.0;
    result.neeShadowStrength = 0.0;
    result.neeLightIndex = 0xffffffffu;
    result.neeLightType = 0u;
    result.neeFlags = 0u;
    result.neeValid = 0u;
    result.nextDirectionWS = 0.0;
    result.nextThroughputWeight = 0.0;
    result.nextPdf = 0.0;
    result.linearRoughness = 1.0;
    result.hitDistance = 0.0;
    result.denoisingAlbedo = 0.0;
    result.denoisingNormalWS = 0.0;
    result.shadingNormalDiagnostics = 0.0;
    result.thinWalledTransmissionWeight = 0.0;
    result.stochasticTransparencyDiagnostics = 0.0;
    result.nextLobeClass = 0u;
    result.nextLobeIsDelta = 0u;
    result.nextLobeIsTransmission = 0u;
    result.mediumTransition = 0;
    result.nextMediumIor = 1.0;
    result.nextMediumExtinction = 0.0;
    result.nextMediumScattering = 0u;
    result.nextMediumInstanceIndex =
        kReferencedPathtracingInvalidMediumInstance;
    result.hit = 0u;
}

void PackReferencedPathtracingPayloadInput(
    ReferencedPathtracingPayloadInput input,
    out ReferencedPathtracingPayload payload)
{
    InitializeReferencedPathtracingPayload(payload);
    StoreReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_INPUT_PATH_THROUGHPUT,
        input.pathThroughput);
    StoreReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_INPUT_BSDF_RANDOM,
        input.bsdfRandom);
    StoreReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_INPUT_DIRECT_LIGHT_RANDOM,
        input.directLightRandom);
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_INPUT_STOCHASTIC_ALPHA_SEED,
        input.stochasticAlphaSeed);
    StoreReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_INPUT_RTXTF_RANDOM,
        input.rtxtfRandom);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_INPUT_RAY_CONE_WIDTH,
        input.rayConeWidth);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_INPUT_RAY_CONE_SPREAD_ANGLE,
        input.rayConeSpreadAngle);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_INPUT_ACTIVE_MEDIUM_IOR,
        input.activeMediumIor);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_INPUT_PARENT_MEDIUM_IOR,
        input.parentMediumIor);
    StoreReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_INPUT_ACTIVE_MEDIUM_EXTINCTION,
        input.activeMediumExtinction);
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_INPUT_ACTIVE_MEDIUM_INSTANCE_INDEX,
        input.activeMediumInstanceIndex);
}

void UnpackReferencedPathtracingPayloadInput(
    ReferencedPathtracingPayload payload,
    out ReferencedPathtracingPayloadInput input)
{
    input.pathThroughput = LoadReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_INPUT_PATH_THROUGHPUT);
    input.bsdfRandom = LoadReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_INPUT_BSDF_RANDOM);
    input.directLightRandom = LoadReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_INPUT_DIRECT_LIGHT_RANDOM);
    input.stochasticAlphaSeed = LoadReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_INPUT_STOCHASTIC_ALPHA_SEED);
    input.rtxtfRandom = LoadReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_INPUT_RTXTF_RANDOM);
    input.rayConeWidth = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_INPUT_RAY_CONE_WIDTH);
    input.rayConeSpreadAngle = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_INPUT_RAY_CONE_SPREAD_ANGLE);
    input.activeMediumIor = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_INPUT_ACTIVE_MEDIUM_IOR);
    input.parentMediumIor = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_INPUT_PARENT_MEDIUM_IOR);
    input.activeMediumExtinction = LoadReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_INPUT_ACTIVE_MEDIUM_EXTINCTION);
    input.activeMediumInstanceIndex = LoadReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_INPUT_ACTIVE_MEDIUM_INSTANCE_INDEX);
}

uint PackReferencedPathtracingSurfaceResultFlags(
    ReferencedPathtracingSurfaceResult result)
{
    uint flags = result.hit != 0u ? REFERENCED_PAYLOAD_FLAG_HIT : 0u;
    flags |= result.neeValid != 0u ? REFERENCED_PAYLOAD_FLAG_NEE_VALID : 0u;
    flags |= result.nextLobeIsDelta != 0u
        ? REFERENCED_PAYLOAD_FLAG_NEXT_LOBE_IS_DELTA
        : 0u;
    flags |= result.nextLobeIsTransmission != 0u
        ? REFERENCED_PAYLOAD_FLAG_NEXT_LOBE_IS_TRANSMISSION
        : 0u;
    flags |= (result.nextLobeClass & 3u)
        << REFERENCED_PAYLOAD_FLAG_NEXT_LOBE_CLASS_SHIFT;
    uint mediumTransition = result.mediumTransition < 0
        ? 0u
        : (result.mediumTransition > 0 ? 2u : 1u);
    flags |= mediumTransition
        << REFERENCED_PAYLOAD_FLAG_MEDIUM_TRANSITION_SHIFT;
    flags |= result.stochasticTransparencyDiagnostics.a > 0.0
        ? REFERENCED_PAYLOAD_FLAG_STOCHASTIC_TRANSPARENCY_SEEN
        : 0u;
    flags |= (result.neeLightType & 15u)
        << REFERENCED_PAYLOAD_FLAG_NEE_LIGHT_TYPE_SHIFT;
    flags |= (result.neeFlags & 255u)
        << REFERENCED_PAYLOAD_FLAG_NEE_FLAGS_SHIFT;
    return flags;
}

void UnpackReferencedPathtracingSurfaceResultFlags(
    uint flags,
    inout ReferencedPathtracingSurfaceResult result)
{
    result.hit = (flags & REFERENCED_PAYLOAD_FLAG_HIT) != 0u ? 1u : 0u;
    result.neeValid =
        (flags & REFERENCED_PAYLOAD_FLAG_NEE_VALID) != 0u ? 1u : 0u;
    result.nextLobeIsDelta =
        (flags & REFERENCED_PAYLOAD_FLAG_NEXT_LOBE_IS_DELTA) != 0u
            ? 1u
            : 0u;
    result.nextLobeIsTransmission =
        (flags & REFERENCED_PAYLOAD_FLAG_NEXT_LOBE_IS_TRANSMISSION) != 0u
            ? 1u
            : 0u;
    result.nextLobeClass =
        (flags & REFERENCED_PAYLOAD_FLAG_NEXT_LOBE_CLASS_MASK)
        >> REFERENCED_PAYLOAD_FLAG_NEXT_LOBE_CLASS_SHIFT;
    uint mediumTransition =
        (flags & REFERENCED_PAYLOAD_FLAG_MEDIUM_TRANSITION_MASK)
        >> REFERENCED_PAYLOAD_FLAG_MEDIUM_TRANSITION_SHIFT;
    result.mediumTransition = mediumTransition == 0u
        ? -1
        : (mediumTransition == 2u ? 1 : 0);
    result.stochasticTransparencyDiagnostics.a =
        (flags & REFERENCED_PAYLOAD_FLAG_STOCHASTIC_TRANSPARENCY_SEEN) != 0u
            ? 1.0
            : 0.0;
    result.neeLightType =
        (flags & REFERENCED_PAYLOAD_FLAG_NEE_LIGHT_TYPE_MASK)
        >> REFERENCED_PAYLOAD_FLAG_NEE_LIGHT_TYPE_SHIFT;
    result.neeFlags =
        (flags & REFERENCED_PAYLOAD_FLAG_NEE_FLAGS_MASK)
        >> REFERENCED_PAYLOAD_FLAG_NEE_FLAGS_SHIFT;
}

float4 LoadReferencedPathtracingStochasticTransparencyDiagnostics(
    ReferencedPathtracingPayload payload)
{
    uint flags = LoadReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_FLAGS);
    float opacity = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_STOCHASTIC_TRANSPARENCY_OPACITY);
    float candidateSeen =
        (flags & REFERENCED_PAYLOAD_FLAG_STOCHASTIC_TRANSPARENCY_SEEN) != 0u
            ? 1.0
            : 0.0;
    return float4(opacity.xxx, candidateSeen);
}

void RecordReferencedPathtracingStochasticTransparency(
    inout ReferencedPathtracingPayload payload,
    float opacity)
{
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_STOCHASTIC_TRANSPARENCY_OPACITY,
        opacity);
    uint flags = LoadReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_FLAGS);
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_FLAGS,
        flags | REFERENCED_PAYLOAD_FLAG_STOCHASTIC_TRANSPARENCY_SEEN);
}

uint LoadReferencedPathtracingStochasticAlphaSeed(
    ReferencedPathtracingPayload payload)
{
    return LoadReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_INPUT_STOCHASTIC_ALPHA_SEED);
}

uint LoadReferencedPathtracingActiveMediumInstanceIndex(
    ReferencedPathtracingPayload payload)
{
    return LoadReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_INPUT_ACTIVE_MEDIUM_INSTANCE_INDEX);
}

void StoreReferencedPathtracingPayloadHit(
    inout ReferencedPathtracingPayload payload,
    uint hit)
{
    uint flags = LoadReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_FLAGS);
    flags = hit != 0u
        ? flags | REFERENCED_PAYLOAD_FLAG_HIT
        : flags & ~REFERENCED_PAYLOAD_FLAG_HIT;
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_FLAGS,
        flags);
}

uint LoadReferencedPathtracingPayloadHit(
    ReferencedPathtracingPayload payload)
{
    uint flags = LoadReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_FLAGS);
    return (flags & REFERENCED_PAYLOAD_FLAG_HIT) != 0u ? 1u : 0u;
}

void PackReferencedPathtracingSurfaceResult(
    ReferencedPathtracingSurfaceResult result,
    out ReferencedPathtracingPayload payload)
{
    InitializeReferencedPathtracingPayload(payload);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_RAY_CONE_WIDTH,
        result.rayConeWidth);
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_FACE_NORMAL_WS_PACKED,
        PackReferencedPathtracingUnitVector(result.faceNormalWS));
    StoreReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_RESULT_EMISSION,
        result.emission);
    StoreReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_DIFFUSE_RADIANCE,
        result.neeDiffuseRadiance);
    StoreReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_SPECULAR_RADIANCE,
        result.neeSpecularRadiance);
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_DIRECTION_WS_PACKED,
        PackReferencedPathtracingUnitVector(result.neeDirectionWS));
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_DISTANCE,
        result.neeDistance);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_SELECTION_PDF,
        result.neeSelectionPdf);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_SOLID_ANGLE_PDF,
        result.neeSolidAnglePdf);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_BSDF_PDF,
        result.neeBsdfPdf);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_SHADOW_STRENGTH,
        result.neeShadowStrength);
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_LIGHT_INDEX,
        result.neeLightIndex);
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_FLAGS,
        PackReferencedPathtracingSurfaceResultFlags(result));
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEXT_DIRECTION_WS_PACKED,
        PackReferencedPathtracingUnitVector(result.nextDirectionWS));
    StoreReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEXT_THROUGHPUT_WEIGHT,
        result.nextThroughputWeight);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEXT_PDF,
        result.nextPdf);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_LINEAR_ROUGHNESS,
        result.linearRoughness);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_HIT_DISTANCE,
        result.hitDistance);
    StoreReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_RESULT_DENOISING_ALBEDO,
        result.denoisingAlbedo);
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_DENOISING_NORMAL_WS_PACKED,
        PackReferencedPathtracingUnitVector(result.denoisingNormalWS));
    StoreReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_RESULT_SHADING_NORMAL_DIAGNOSTICS,
        result.shadingNormalDiagnostics);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_THIN_WALLED_TRANSMISSION_WEIGHT,
        result.thinWalledTransmissionWeight);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_STOCHASTIC_TRANSPARENCY_OPACITY,
        result.stochasticTransparencyDiagnostics.x);
    StoreReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_IOR,
        result.nextMediumIor);
    uint2 packedMediumExtinction =
        PackReferencedPathtracingMaterialMediumExtinction(
            result.nextMediumExtinction);
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_EXTINCTION,
        packedMediumExtinction.x);
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_EXTINCTION + 1u,
        packedMediumExtinction.y);
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_SCATTERING,
        result.nextMediumScattering);
    StoreReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_INSTANCE_INDEX,
        result.nextMediumInstanceIndex);
}

void UnpackReferencedPathtracingSurfaceResult(
    ReferencedPathtracingPayload payload,
    out ReferencedPathtracingSurfaceResult result)
{
    InitializeReferencedPathtracingSurfaceResult(result);
    result.rayConeWidth = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_RAY_CONE_WIDTH);
    result.faceNormalWS = UnpackReferencedPathtracingUnitVector(
        LoadReferencedPathtracingPayloadUint(
            payload,
            REFERENCED_PAYLOAD_RESULT_FACE_NORMAL_WS_PACKED));
    result.emission = LoadReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_RESULT_EMISSION);
    result.neeDiffuseRadiance = LoadReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_DIFFUSE_RADIANCE);
    result.neeSpecularRadiance = LoadReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_SPECULAR_RADIANCE);
    result.neeDirectionWS = UnpackReferencedPathtracingUnitVector(
        LoadReferencedPathtracingPayloadUint(
            payload,
            REFERENCED_PAYLOAD_RESULT_NEE_DIRECTION_WS_PACKED));
    result.neeDistance = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_DISTANCE);
    result.neeSelectionPdf = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_SELECTION_PDF);
    result.neeSolidAnglePdf = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_SOLID_ANGLE_PDF);
    result.neeBsdfPdf = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_BSDF_PDF);
    result.neeShadowStrength = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_SHADOW_STRENGTH);
    result.neeLightIndex = LoadReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEE_LIGHT_INDEX);
    result.nextDirectionWS = UnpackReferencedPathtracingUnitVector(
        LoadReferencedPathtracingPayloadUint(
            payload,
            REFERENCED_PAYLOAD_RESULT_NEXT_DIRECTION_WS_PACKED));
    result.nextThroughputWeight = LoadReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEXT_THROUGHPUT_WEIGHT);
    result.nextPdf = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEXT_PDF);
    result.linearRoughness = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_LINEAR_ROUGHNESS);
    result.hitDistance = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_HIT_DISTANCE);
    result.denoisingAlbedo = LoadReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_RESULT_DENOISING_ALBEDO);
    result.denoisingNormalWS = UnpackReferencedPathtracingUnitVector(
        LoadReferencedPathtracingPayloadUint(
            payload,
            REFERENCED_PAYLOAD_RESULT_DENOISING_NORMAL_WS_PACKED));
    result.shadingNormalDiagnostics = LoadReferencedPathtracingPayloadFloat3(
        payload,
        REFERENCED_PAYLOAD_RESULT_SHADING_NORMAL_DIAGNOSTICS);
    result.thinWalledTransmissionWeight = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_THIN_WALLED_TRANSMISSION_WEIGHT);
    float stochasticTransparencyOpacity =
        LoadReferencedPathtracingPayloadFloat(
            payload,
            REFERENCED_PAYLOAD_RESULT_STOCHASTIC_TRANSPARENCY_OPACITY);
    result.stochasticTransparencyDiagnostics.rgb =
        stochasticTransparencyOpacity;
    result.nextMediumIor = LoadReferencedPathtracingPayloadFloat(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_IOR);
    result.nextMediumExtinction =
        UnpackReferencedPathtracingMaterialMediumExtinction(
            uint2(
                LoadReferencedPathtracingPayloadUint(
                    payload,
                    REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_EXTINCTION),
                LoadReferencedPathtracingPayloadUint(
                    payload,
                    REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_EXTINCTION
                        + 1u)));
    result.nextMediumScattering =
        LoadReferencedPathtracingPayloadUint(
            payload,
            REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_SCATTERING);
    result.nextMediumInstanceIndex = LoadReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_INSTANCE_INDEX);
    uint flags = LoadReferencedPathtracingPayloadUint(
        payload,
        REFERENCED_PAYLOAD_RESULT_FLAGS);
    UnpackReferencedPathtracingSurfaceResultFlags(flags, result);
}

#endif
