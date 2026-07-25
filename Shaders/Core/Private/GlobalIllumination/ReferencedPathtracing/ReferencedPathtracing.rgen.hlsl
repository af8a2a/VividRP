#pragma max_recursion_depth 1

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/NRD/REBLUR/VividReblurSignalEncoding.hlsli"

RaytracingAccelerationStructure _AccelerationStructure;
RWTexture2D<float4> _WorldPositionTexture;
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
float _RayMinDistance;
float _RayMaxDistance;
int _ReferencedMaxBounceCount;
int _ReferencedRussianRouletteStartBounce;
int _ReferencedFrameIndex;
int _ReferencedSeed;
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

uint HashReferencedPathtracingRng(uint value)
{
    value ^= value >> 16u;
    value *= 0x7feb352du;
    value ^= value >> 15u;
    value *= 0x846ca68bu;
    value ^= value >> 16u;
    return value;
}

uint NextReferencedPathtracingRngUint(inout uint rngState)
{
    rngState = rngState * 747796405u + 2891336453u;
    uint word = ((rngState >> ((rngState >> 28u) + 4u)) ^ rngState) * 277803737u;
    return (word >> 22u) ^ word;
}

float NextReferencedPathtracingRngFloat(inout uint rngState)
{
    return (float)(NextReferencedPathtracingRngUint(rngState) >> 8u) * (1.0 / 16777216.0);
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

float TraceReferencedPathtracingVisibility(
    float3 positionWS,
    float3 faceNormalWS,
    float3 lightDirectionWS,
    float maximumDistance)
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

    return visibilityPayload.hit == 0u ? 1.0 : 0.0;
}

float TraceReferencedPathtracingMainLightVisibility(
    float3 positionWS,
    float3 faceNormalWS,
    float3 lightDirectionWS)
{
    float shadowStrength = saturate(_ReferencedMainLightShadowStrength);
    if (shadowStrength <= 0.0)
        return 1.0;

    float tracedVisibility = TraceReferencedPathtracingVisibility(
        positionWS,
        faceNormalWS,
        lightDirectionWS,
        max(_RayMaxDistance, kReferencedPathtracingShadowMaxDistance));
    return lerp(1.0, tracedVisibility, shadowStrength);
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

    uint pixelIndex = pixelCoord.x + pixelCoord.y * launchDimensions.x;
    uint frameHash = HashReferencedPathtracingRng(
        (uint)_ReferencedFrameIndex
        ^ HashReferencedPathtracingRng((uint)_ReferencedSeed)
        ^ 0xa511e9b3u);
    uint rngState = HashReferencedPathtracingRng(pixelIndex ^ frameHash);

    RayDesc ray;
    ray.Origin = _CameraPositionWS.xyz;
    float2 pixelCenter = (float2)pixelCoord + float2(
        NextReferencedPathtracingRngFloat(rngState),
        NextReferencedPathtracingRngFloat(rngState));
    ray.Direction = GetReferencedPathtracingPrimaryRayDirectionWS(pixelCenter);
    ray.TMin = _RayMinDistance;
    ray.TMax = _RayMaxDistance;

    float3 rayDirectionDx = GetReferencedPathtracingPrimaryRayDirectionWS(pixelCenter + float2(1.0, 0.0));
    float3 rayDirectionDy = GetReferencedPathtracingPrimaryRayDirectionWS(pixelCenter + float2(0.0, 1.0));
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
    float cameraBackgroundAlpha = 0.0;
    float3 throughput = 1.0;
    float previousBsdfPdf = 0.0;
    bool previousBsdfWasDelta = false;
    float rayConeWidth = 0.0;
    uint primaryHit = 0u;
    uint primaryLobeClass = 0u;
    float primaryViewZ = 1.0;
    float primaryLinearRoughness = 1.0;
    float diffuseHitDistance = _RayMaxDistance;
    float specularHitDistance = _RayMaxDistance;
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
        payload.bsdfRandom = float3(
            NextReferencedPathtracingRngFloat(rngState),
            NextReferencedPathtracingRngFloat(rngState),
            NextReferencedPathtracingRngFloat(rngState));
        // Keep proposal dimensions independent: ReGIR consumes one discrete-light dimension
        // plus two area-shape dimensions. Distant-light and environment NEE derive separate
        // streams below so neither shifts the existing BSDF/ReGIR or later-bounce sequence.
        payload.directLightRandom = float3(
            NextReferencedPathtracingRngFloat(rngState),
            NextReferencedPathtracingRngFloat(rngState),
            NextReferencedPathtracingRngFloat(rngState));
        uint mainLightRngState = HashReferencedPathtracingRng(
            rngState ^ (0x243f6a88u + bounceIndex * 0x9e3779b9u));
        payload.mainLightRandom = float2(
            NextReferencedPathtracingRngFloat(mainLightRngState),
            NextReferencedPathtracingRngFloat(mainLightRngState));
        uint environmentRngState = HashReferencedPathtracingRng(
            rngState ^ (0x68bc21ebu + bounceIndex * 0x9e3779b9u));
        payload.environmentRandom = float2(
            NextReferencedPathtracingRngFloat(environmentRngState),
            NextReferencedPathtracingRngFloat(environmentRngState));
        payload.rayConeWidth = rayConeWidth;
        payload.rayConeSpreadAngle = rayConeSpreadAngle;
        TraceRay(
            _AccelerationStructure,
            RAY_FLAG_NONE,
            0xFF,
            0,
            1,
            0,
            ray,
            payload);

        if (payload.hit == 0u)
        {
            if (bounceIndex == 0u)
            {
                float4 cameraBackground =
                    ReferencedPathtracingEvaluateCameraBackground(ray.Direction);
                cameraBackgroundRadiance = cameraBackground.rgb;
                cameraBackgroundAlpha = cameraBackground.a;

                bool usesEnvironmentBackground =
                    _ReferencedCameraSkyEnabled != 0
                    && _ReferencedEnvironmentCameraVisible != 0
                    && ReferencedPathtracingHasEnvironment();
                if (usesEnvironmentBackground)
                    primaryEnvironmentBackgroundRadiance = cameraBackground.rgb;
            }
            else
            {
                float3 environmentRadiance =
                    ReferencedPathtracingEvaluateLightingEnvironment(ray.Direction);
                float environmentLightPdf =
                    ReferencedPathtracingEvaluateEnvironmentLightPdf(ray.Direction);
                float bsdfEstimatorWeight =
                    ReferencedPathtracingGetEnvironmentBsdfEstimatorWeight(
                        previousBsdfPdf,
                        environmentLightPdf,
                        previousBsdfWasDelta);
                float3 environmentContribution =
                    throughput * environmentRadiance * bsdfEstimatorWeight;
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
                    diffuseHitDistance = payload.hitDistance;
                else if (primaryLobeClass == 2u)
                    specularHitDistance = payload.hitDistance;
            }
        }

        // Directional light RGB is photometric illuminance in lux. For a uniform solid-angle
        // proposal, distant radiance divided by the light PDF integrates back to illuminance.
        float3 mainLightIlluminance = max(_ReferencedMainLightColor.rgb, 0.0);
        float mainLightDirectionLengthSquared = dot(
            payload.mainLightDirectionWS,
            payload.mainLightDirectionWS);
        if (any(mainLightIlluminance > 0.0)
            && mainLightDirectionLengthSquared > 1e-8
            && (any(payload.mainLightDiffuseBsdf > 0.0)
                || any(payload.mainLightSpecularBsdf > 0.0)))
        {
            float3 mainLightDirectionWS = payload.mainLightDirectionWS
                * rsqrt(mainLightDirectionLengthSquared);
            float lightEstimatorWeight =
                ReferencedPathtracingGetMainLightEstimatorWeight(
                    payload.mainLightLightPdf,
                    payload.mainLightBsdfPdf,
                    payload.mainLightIsDelta);
            float visibility = TraceReferencedPathtracingMainLightVisibility(
                payload.positionWS,
                normalize(payload.faceNormalWS),
                mainLightDirectionWS);
            float3 directDiffuse = throughput
                * payload.mainLightDiffuseBsdf
                * mainLightIlluminance
                * lightEstimatorWeight
                * visibility;
            float3 directSpecular = throughput
                * payload.mainLightSpecularBsdf
                * mainLightIlluminance
                * lightEstimatorWeight
                * visibility;
            bool includeInPrimaryDenoiserSignal =
                payload.mainLightIsDelta == 0u;
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

        // Evaluate the same finite sun disk with the path's BSDF proposal. Doing this at the
        // current vertex, rather than only on a later miss, preserves artistic shadowStrength
        // semantics and uses the exact same visibility interval as light-sampled NEE.
        if (any(mainLightIlluminance > 0.0)
            && payload.nextPdf > 0.0
            && any(payload.nextThroughputWeight > 0.0))
        {
            float mainLightPdfForBsdfSample;
            if (ReferencedPathtracingEvaluateMainDirectionalLightPdf(
                    payload.nextDirectionWS,
                    mainLightPdfForBsdfSample))
            {
                float bsdfEstimatorWeight =
                    ReferencedPathtracingGetMainBsdfEstimatorWeight(
                        payload.nextPdf,
                        mainLightPdfForBsdfSample,
                        payload.nextLobeIsDelta);
                float visibility =
                    TraceReferencedPathtracingMainLightVisibility(
                        payload.positionWS,
                        normalize(payload.faceNormalWS),
                        normalize(payload.nextDirectionWS));
                // Uniform distant radiance is illuminance divided by disk solid angle.
                float3 mainLightRadiance =
                    mainLightIlluminance * mainLightPdfForBsdfSample;
                float3 bsdfSampledDirect = throughput
                    * payload.nextThroughputWeight
                    * mainLightRadiance
                    * bsdfEstimatorWeight
                    * visibility;
                if (IsFiniteReferencedPathtracingRadiance(
                        bsdfSampledDirect))
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
            }
        }

        if (payload.reGIRLocalDistance > 0.0
            && (any(payload.reGIRLocalDiffuseRadiance > 0.0)
                || any(payload.reGIRLocalSpecularRadiance > 0.0)))
        {
            float3 localLightDirectionWS = normalize(payload.reGIRLocalDirectionWS);
            float visibility = TraceReferencedPathtracingVisibility(
                payload.positionWS,
                normalize(payload.faceNormalWS),
                localLightDirectionWS,
                payload.reGIRLocalDistance);
            float3 directDiffuse = throughput
                * payload.reGIRLocalDiffuseRadiance
                * visibility;
            float3 directSpecular = throughput
                * payload.reGIRLocalSpecularRadiance
                * visibility;

            if (bounceIndex == 0u)
            {
                // ReGIR selects one corrected local-light estimator per pixel and frame. Keep its
                // primary diffuse/specular components in the REBLUR signals instead of the
                // deterministic direct-light AOV used by the main directional light.
                diffuseRadiance += directDiffuse;
                specularRadiance += directSpecular;

                if (any(directDiffuse > 0.0))
                {
                    diffuseRayDirectionWS = localLightDirectionWS;
                    diffuseHitDistance = payload.reGIRLocalDistance;
                }

                if (any(directSpecular > 0.0))
                {
                    specularRayDirectionWS = localLightDirectionWS;
                    specularHitDistance = payload.reGIRLocalDistance;
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

        if (payload.environmentLightPdf > 0.0
            && (any(payload.environmentDirectDiffuseRadiance > 0.0)
                || any(payload.environmentDirectSpecularRadiance > 0.0)))
        {
            float lightEstimatorWeight =
                ReferencedPathtracingGetEnvironmentLightEstimatorWeight(
                    payload.environmentLightPdf,
                    payload.environmentBsdfPdf);
            float3 environmentDirectionWS =
                normalize(payload.environmentDirectionWS);
            float visibility = TraceReferencedPathtracingVisibility(
                payload.positionWS,
                normalize(payload.faceNormalWS),
                environmentDirectionWS,
                kReferencedPathtracingInfiniteDistance);
            float3 directDiffuse = throughput
                * payload.environmentDirectDiffuseRadiance
                * lightEstimatorWeight
                * visibility;
            float3 directSpecular = throughput
                * payload.environmentDirectSpecularRadiance
                * lightEstimatorWeight
                * visibility;

            if (IsFiniteReferencedPathtracingRadiance(directDiffuse)
                && IsFiniteReferencedPathtracingRadiance(directSpecular))
            {
                environmentNeeRadiance += directDiffuse + directSpecular;

                if (bounceIndex == 0u)
                {
                    // Direct environment AOVs describe the primary surface only.
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
        throughput *= payload.nextThroughputWeight;
        if (!IsFiniteReferencedPathtracingRadiance(throughput)
            || MaxReferencedPathtracingComponent(throughput) <= 0.0)
        {
            break;
        }

        if ((int)(bounceIndex + 1u) >= _ReferencedRussianRouletteStartBounce)
        {
            float survivalProbability = clamp(
                MaxReferencedPathtracingComponent(throughput),
                0.05,
                0.95);
            if (NextReferencedPathtracingRngFloat(rngState) >= survivalProbability)
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

    if (!IsFiniteReferencedPathtracingRadiance(diffuseRadiance))
        diffuseRadiance = 0.0;
    if (!IsFiniteReferencedPathtracingRadiance(specularRadiance))
        specularRadiance = 0.0;
    if (!IsFiniteReferencedPathtracingRadiance(directLightingRadiance))
        directLightingRadiance = 0.0;
    if (!IsFiniteReferencedPathtracingRadiance(
            primaryDenoiserMainLightDiffuseRadiance))
    {
        primaryDenoiserMainLightDiffuseRadiance = 0.0;
    }
    if (!IsFiniteReferencedPathtracingRadiance(
            primaryDenoiserMainLightSpecularRadiance))
    {
        primaryDenoiserMainLightSpecularRadiance = 0.0;
    }
    if (!IsFiniteReferencedPathtracingRadiance(emissionRadiance))
        emissionRadiance = 0.0;
    if (!IsFiniteReferencedPathtracingRadiance(environmentNeeRadiance))
        environmentNeeRadiance = 0.0;
    if (!IsFiniteReferencedPathtracingRadiance(environmentDirectDiffuseRadiance))
        environmentDirectDiffuseRadiance = 0.0;
    if (!IsFiniteReferencedPathtracingRadiance(environmentDirectSpecularRadiance))
        environmentDirectSpecularRadiance = 0.0;

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
    float3 radiance = surfaceRadiance + cameraBackgroundRadiance;
    if (_ReferencedEnvironmentDebugMode == kReferencedEnvironmentDebugEnvironmentOnly)
    {
        radiance =
            primaryEnvironmentBackgroundRadiance
            + indirectEnvironmentRadiance
            + environmentNeeRadiance;
    }
    else if (_ReferencedEnvironmentDebugMode
        == kReferencedEnvironmentDebugPrimaryBackgroundOnly)
    {
        radiance = cameraBackgroundRadiance;
    }
    else if (_ReferencedEnvironmentDebugMode
        == kReferencedEnvironmentDebugIndirectMissOnly)
    {
        radiance = indirectEnvironmentRadiance;
    }

    float outputAlpha = primaryHit != 0u ? 1.0 : cameraBackgroundAlpha;
    _WorldPositionTexture[pixelCoord] = float4(radiance, outputAlpha);
    _ReferencedPathTracingDirectLighting[pixelCoord] =
        float4(directLightingRadiance, primaryHit != 0u ? 1.0 : 0.0);
    _ReferencedPathTracingEmission[pixelCoord] = float4(emissionRadiance, primaryHit != 0u ? 1.0 : 0.0);
    _ReferencedPathTracingEnvironmentDirectDiffuse[pixelCoord] = float4(
        environmentDirectDiffuseRadiance,
        primaryHit != 0u ? 1.0 : 0.0);
    _ReferencedPathTracingEnvironmentDirectSpecular[pixelCoord] = float4(
        environmentDirectSpecularRadiance,
        primaryHit != 0u ? 1.0 : 0.0);
    _ReferencedDiffuseRayDirectionHitDistance[pixelCoord] = float4(
        diffuseRayDirectionWS,
        primaryHit != 0u ? diffuseHitDistance : 0.0);
    _ReferencedSpecularRayDirectionHitDistance[pixelCoord] = float4(
        specularRayDirectionWS,
        primaryHit != 0u ? specularHitDistance : 0.0);

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
    InitializeReferencedPathtracingPayload(payload);
}
