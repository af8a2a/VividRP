#pragma max_recursion_depth 1

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCommon.hlsl"

RaytracingAccelerationStructure _AccelerationStructure;
RWTexture2D<float4> _WorldPositionTexture;
RWTexture2D<float4> _ReferencedDiffuseRadianceHitDistance;
RWTexture2D<float4> _ReferencedSpecularRadianceHitDistance;
RWTexture2D<float4> _ReferencedPathTracingDirectLighting;
RWTexture2D<float4> _ReferencedPathTracingEmission;
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
float4 _ReferencedReblurHitDistanceParameters;
int _ReferencedReblurCheckerboardMode;

static const float kReferencedPathtracingShadowMinBias = 0.001;
static const float kReferencedPathtracingShadowMaxDistance = 100000.0;
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

float3 ReferencedPathtracingLinearToYCoCg(float3 color)
{
    float y = dot(color, float3(0.25, 0.5, 0.25));
    float co = dot(color, float3(0.5, 0.0, -0.5));
    float cg = dot(color, float3(-0.25, 0.5, -0.25));
    return float3(y, co, cg);
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
    return float4(ReferencedPathtracingLinearToYCoCg(radiance), normalizedHitDistance);
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

[shader("raygeneration")]
void RayGenReferencedPathtracing()
{
    uint2 launchIndex = DispatchRaysIndex().xy;
    uint2 launchDimensions = DispatchRaysDimensions().xy;
    uint2 pixelCoord = uint2(launchIndex.x, launchDimensions.y - launchIndex.y - 1u);

    uint pixelIndex = pixelCoord.x + pixelCoord.y * launchDimensions.x;
    uint frameHash = HashReferencedPathtracingRng((uint)_ReferencedFrameIndex ^ 0xa511e9b3u);
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
    float3 emissionRadiance = 0.0;
    float3 throughput = 1.0;
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
        // Preserve the existing BSDF dimensions and append one discrete ReGIR dimension plus
        // two continuous area-shape dimensions.
        payload.directLightRandom = float3(
            NextReferencedPathtracingRngFloat(rngState),
            NextReferencedPathtracingRngFloat(rngState),
            NextReferencedPathtracingRngFloat(rngState));
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
            break;

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

        // Directional light RGB is photometric illuminance in lux. The closest-hit OpenPBR
        // response already includes NdotL, so this product is the physical direct-light term.
        float3 mainLightIlluminance = max(_ReferencedMainLightColor.rgb, 0.0);
        if (any(mainLightIlluminance > 0.0)
            && (any(payload.mainLightDiffuseBsdf > 0.0)
                || any(payload.mainLightSpecularBsdf > 0.0)))
        {
            float visibility = TraceReferencedPathtracingVisibility(
                payload.positionWS,
                normalize(payload.faceNormalWS),
                normalize(_ReferencedMainLightDirectionWS.xyz),
                max(_RayMaxDistance, kReferencedPathtracingShadowMaxDistance));
            float3 directDiffuse = throughput
                * payload.mainLightDiffuseBsdf
                * mainLightIlluminance
                * visibility;
            float3 directSpecular = throughput
                * payload.mainLightSpecularBsdf
                * mainLightIlluminance
                * visibility;

            if (bounceIndex == 0u)
            {
                // Primary NEE is deterministic for the current directional-light prototype.
                // Keep it out of REBLUR: filtering it with the indirect signal destroys hard
                // visibility boundaries because a directional shadow has no finite hit distance.
                directLightingRadiance += directDiffuse + directSpecular;
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
    if (!IsFiniteReferencedPathtracingRadiance(emissionRadiance))
        emissionRadiance = 0.0;

    float diffuseNormHitDistance = primaryHit != 0u
        ? GetReferencedPathtracingReblurNormHitDistance(
            diffuseHitDistance,
            primaryViewZ,
            primaryLinearRoughness)
        : 0.0;
    float specularNormHitDistance = primaryHit != 0u
        ? GetReferencedPathtracingReblurNormHitDistance(
            specularHitDistance,
            primaryViewZ,
            primaryLinearRoughness)
        : 0.0;
    float3 radiance =
        diffuseRadiance + specularRadiance + directLightingRadiance + emissionRadiance;

    _WorldPositionTexture[pixelCoord] = float4(radiance, primaryHit != 0u ? 1.0 : 0.0);
    _ReferencedPathTracingDirectLighting[pixelCoord] =
        float4(directLightingRadiance, primaryHit != 0u ? 1.0 : 0.0);
    _ReferencedPathTracingEmission[pixelCoord] = float4(emissionRadiance, primaryHit != 0u ? 1.0 : 0.0);
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
            PackReferencedPathtracingReblurSignal(diffuseRadiance, diffuseNormHitDistance);
    }

    if (writeSpecular)
    {
        _ReferencedSpecularRadianceHitDistance[signalPixelCoord] =
            PackReferencedPathtracingReblurSignal(specularRadiance, specularNormHitDistance);
    }
}

[shader("miss")]
void MissReferencedPathtracing(inout ReferencedPathtracingPayload payload : SV_RayPayload)
{
    InitializeReferencedPathtracingPayload(payload);
}
