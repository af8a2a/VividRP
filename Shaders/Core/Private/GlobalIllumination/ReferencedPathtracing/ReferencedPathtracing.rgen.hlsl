#pragma max_recursion_depth 1

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCommon.hlsl"

RaytracingAccelerationStructure _AccelerationStructure;
RWTexture2D<float4> _WorldPositionTexture;

float4 _CameraPositionWS;
float4x4 _PixelCoordToViewDirWS;
float _RayMinDistance;
float _RayMaxDistance;
int _ReferencedMaxBounceCount;
int _ReferencedRussianRouletteStartBounce;
int _ReferencedFrameIndex;

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

float TraceReferencedPathtracingMainLightVisibility(
    float3 positionWS,
    float3 faceNormalWS,
    float3 lightDirectionWS)
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
    shadowRay.TMax = max(_RayMaxDistance, kReferencedPathtracingShadowMaxDistance);

    ReferencedPathtracingPayload visibilityPayload;
    InitializeReferencedPathtracingPayload(visibilityPayload);
    // With the closest-hit shader skipped, this value survives any hit; the miss shader clears it.
    visibilityPayload.hit = 1u;
    TraceRay(
        _AccelerationStructure,
        RAY_FLAG_FORCE_OPAQUE
            | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH
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

    float3 radiance = 0.0;
    float3 throughput = 1.0;
    float rayConeWidth = 0.0;
    uint primaryHit = 0u;
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
            primaryHit = 1u;

        radiance += throughput * payload.emission;

        float3 mainLightColor = max(_ReferencedMainLightColor.rgb, 0.0);
        if (any(mainLightColor > 0.0) && any(payload.mainLightBsdf > 0.0))
        {
            float visibility = TraceReferencedPathtracingMainLightVisibility(
                payload.positionWS,
                normalize(payload.faceNormalWS),
                normalize(_ReferencedMainLightDirectionWS.xyz));
            radiance += throughput * payload.mainLightBsdf * mainLightColor * visibility;
        }

        if (payload.nextPdf <= 0.0)
            break;

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

    if (!IsFiniteReferencedPathtracingRadiance(radiance))
        radiance = 0.0;
    _WorldPositionTexture[pixelCoord] = float4(radiance, primaryHit != 0u ? 1.0 : 0.0);
}

[shader("miss")]
void MissReferencedPathtracing(inout ReferencedPathtracingPayload payload : SV_RayPayload)
{
    InitializeReferencedPathtracingPayload(payload);
}
