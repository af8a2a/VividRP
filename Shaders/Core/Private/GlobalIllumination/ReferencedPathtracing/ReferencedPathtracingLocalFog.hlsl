#ifndef VIVIDRP_REFERENCED_PATH_TRACING_LOCAL_FOG_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_LOCAL_FOG_INCLUDED

SamplerState sampler_LinearRepeat;
Texture3D<float4> _ReferencedLocalFogMask0;
Texture3D<float4> _ReferencedLocalFogMask1;
Texture3D<float4> _ReferencedLocalFogMask2;
Texture3D<float4> _ReferencedLocalFogMask3;
Texture3D<float4> _ReferencedLocalFogMask4;
Texture3D<float4> _ReferencedLocalFogMask5;
Texture3D<float4> _ReferencedLocalFogMask6;
Texture3D<float4> _ReferencedLocalFogMask7;
Texture3D<float4> _ReferencedLocalFogMask8;
Texture3D<float4> _ReferencedLocalFogMask9;
Texture3D<float4> _ReferencedLocalFogMask10;
Texture3D<float4> _ReferencedLocalFogMask11;
Texture3D<float4> _ReferencedLocalFogMask12;
Texture3D<float4> _ReferencedLocalFogMask13;
Texture3D<float4> _ReferencedLocalFogMask14;
Texture3D<float4> _ReferencedLocalFogMask15;

static const uint kReferencedPathtracingLocalFogMaximumTrackingStepCount =
    128u;
static const uint kReferencedPathtracingLocalFogShadowIntegrationStepCount =
    8u;
static const uint kReferencedPathtracingInvalidLocalFogMaskIndex =
    0xffffffffu;

struct ReferencedPathtracingLocalFogRecord
{
    float4 worldToLocalRow0;
    float4 worldToLocalRow1;
    float4 worldToLocalRow2;
    float4 scatteringExtinction;
    float4 positiveFade;
    float4 negativeFade;
    float4 distanceFade;
    float4 parameters;
    float4 textureScaleOffset0;
    float4 textureScaleOffset1;
};

int _ReferencedLocalFogCount;
StructuredBuffer<ReferencedPathtracingLocalFogRecord>
    _ReferencedLocalFogList;

struct ReferencedPathtracingLocalFogSample
{
    float distance;
    float3 scatteringAlbedo;
    float anisotropy;
    uint recordIndex;
    uint hasEvent;
    uint trackingOverflow;
    uint trackingStepCount;
};

struct ReferencedPathtracingLocalFogPoint
{
    float densityFactor;
    float3 scatteringAlbedo;
};

float3 ReferencedPathtracingTransformLocalFogPosition(
    ReferencedPathtracingLocalFogRecord record,
    float3 positionWS)
{
    float4 homogeneousPosition = float4(positionWS, 1.0);
    return float3(
        dot(record.worldToLocalRow0, homogeneousPosition),
        dot(record.worldToLocalRow1, homogeneousPosition),
        dot(record.worldToLocalRow2, homogeneousPosition));
}

float3 ReferencedPathtracingTransformLocalFogDirection(
    ReferencedPathtracingLocalFogRecord record,
    float3 directionWS)
{
    float4 homogeneousDirection = float4(directionWS, 0.0);
    return float3(
        dot(record.worldToLocalRow0, homogeneousDirection),
        dot(record.worldToLocalRow1, homogeneousDirection),
        dot(record.worldToLocalRow2, homogeneousDirection));
}

bool ReferencedPathtracingIntersectLocalFogSlab(
    float origin,
    float direction,
    inout float entryDistance,
    inout float exitDistance)
{
    if (abs(direction) <= 1e-8)
        return origin >= -0.5 && origin <= 0.5;

    float firstDistance = (-0.5 - origin) / direction;
    float secondDistance = (0.5 - origin) / direction;
    float nearDistance = min(firstDistance, secondDistance);
    float farDistance = max(firstDistance, secondDistance);
    entryDistance = max(entryDistance, nearDistance);
    exitDistance = min(exitDistance, farDistance);
    return exitDistance > entryDistance;
}

bool ReferencedPathtracingIntersectLocalFog(
    ReferencedPathtracingLocalFogRecord record,
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float minimumDistance,
    float maximumDistance,
    out float entryDistance,
    out float exitDistance)
{
    float3 origin =
        ReferencedPathtracingTransformLocalFogPosition(
            record,
            rayOriginWS);
    float3 direction =
        ReferencedPathtracingTransformLocalFogDirection(
            record,
            rayDirectionWS);
    entryDistance = max(minimumDistance, 0.0);
    exitDistance = max(maximumDistance, entryDistance);
    return
        ReferencedPathtracingIntersectLocalFogSlab(
            origin.x,
            direction.x,
            entryDistance,
            exitDistance)
        && ReferencedPathtracingIntersectLocalFogSlab(
            origin.y,
            direction.y,
            entryDistance,
            exitDistance)
        && ReferencedPathtracingIntersectLocalFogSlab(
            origin.z,
            direction.z,
            entryDistance,
            exitDistance)
        && exitDistance > entryDistance;
}

float4 ReferencedPathtracingSampleLocalFogMaskTexture(
    uint maskTextureIndex,
    float3 maskCoord)
{
    switch (maskTextureIndex)
    {
        case 0u:
            return _ReferencedLocalFogMask0.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 1u:
            return _ReferencedLocalFogMask1.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 2u:
            return _ReferencedLocalFogMask2.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 3u:
            return _ReferencedLocalFogMask3.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 4u:
            return _ReferencedLocalFogMask4.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 5u:
            return _ReferencedLocalFogMask5.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 6u:
            return _ReferencedLocalFogMask6.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 7u:
            return _ReferencedLocalFogMask7.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 8u:
            return _ReferencedLocalFogMask8.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 9u:
            return _ReferencedLocalFogMask9.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 10u:
            return _ReferencedLocalFogMask10.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 11u:
            return _ReferencedLocalFogMask11.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 12u:
            return _ReferencedLocalFogMask12.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 13u:
            return _ReferencedLocalFogMask13.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 14u:
            return _ReferencedLocalFogMask14.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        case 15u:
            return _ReferencedLocalFogMask15.SampleLevel(
                sampler_LinearRepeat, maskCoord, 0.0);
        default:
            return 1.0;
    }
}

float4 ReferencedPathtracingSampleLocalFogMask(
    ReferencedPathtracingLocalFogRecord record,
    float3 coord)
{
    uint maskTextureIndex =
        record.parameters.w < 0.0
            ? kReferencedPathtracingInvalidLocalFogMaskIndex
            : (uint)round(record.parameters.w);
    if (maskTextureIndex
        == kReferencedPathtracingInvalidLocalFogMaskIndex)
    {
        return 1.0;
    }

    float3 maskCoord =
        coord * max(record.textureScaleOffset0.xyz, 1e-4)
        + record.textureScaleOffset1.xyz;
    float4 maskValue =
        saturate(
            ReferencedPathtracingSampleLocalFogMaskTexture(
                maskTextureIndex,
                maskCoord));
    if (record.textureScaleOffset0.w > 0.5)
        maskValue.rgb = 1.0;

    return maskValue;
}

ReferencedPathtracingLocalFogPoint
ReferencedPathtracingEvaluateLocalFogPoint(
    ReferencedPathtracingLocalFogRecord record,
    float3 positionWS)
{
    ReferencedPathtracingLocalFogPoint evaluatedPoint;
    evaluatedPoint.densityFactor = 0.0;
    evaluatedPoint.scatteringAlbedo = 0.0;

    float3 localPosition =
        ReferencedPathtracingTransformLocalFogPosition(
            record,
            positionWS);
    float3 coord = localPosition + 0.5;
    if (any(coord < 0.0) || any(coord > 1.0))
        return evaluatedPoint;

    float3 positiveFade = saturate(
        record.positiveFade.xyz
        - coord * record.positiveFade.xyz);
    float3 negativeFade = saturate(
        coord * record.negativeFade.xyz);
    float fadeFactor =
        positiveFade.x * positiveFade.y * positiveFade.z
        * negativeFade.x * negativeFade.y * negativeFade.z;
    if (record.textureScaleOffset1.w > 0.5)
        fadeFactor = pow(saturate(fadeFactor), 2.2);
    if (record.parameters.z > 0.5)
        fadeFactor = 1.0 - fadeFactor;

    float distanceFade = saturate(
        record.distanceFade.y
        - distance(positionWS, _CameraPositionWS.xyz)
            * record.distanceFade.x);
    float4 maskValue =
        ReferencedPathtracingSampleLocalFogMask(
            record,
            coord);
    evaluatedPoint.densityFactor =
        saturate(fadeFactor * distanceFade * maskValue.a);
    float extinction =
        max(record.scatteringExtinction.w, 0.0);
    evaluatedPoint.scatteringAlbedo =
        saturate(
            record.scatteringExtinction.rgb
            / max(extinction, 1e-20)
            * maskValue.rgb);
    return evaluatedPoint;
}

float ReferencedPathtracingEvaluateLocalFogDensityFactor(
    ReferencedPathtracingLocalFogRecord record,
    float3 positionWS)
{
    return ReferencedPathtracingEvaluateLocalFogPoint(
        record,
        positionWS).densityFactor;
}

float ReferencedPathtracingGetLocalFogTrackingRandom(
    float2 randomSeed,
    uint recordIndex,
    uint trackingStep,
    uint streamIndex)
{
    uint seed =
        asuint(randomSeed.x)
        ^ ReferencedPathtracingHash(
            asuint(randomSeed.y) + 0x9e3779b9u)
        ^ ReferencedPathtracingHash(
            recordIndex * 0x85ebca6bu
            + trackingStep * 0xc2b2ae35u
            + streamIndex * 0x27d4eb2du);
    return min(
        max(
            ReferencedPathtracingHashToUnitFloat(seed),
            1e-7),
        0.99999994);
}

bool ReferencedPathtracingSampleLocalFog(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float minimumDistance,
    float maximumDistance,
    float2 randomSeed,
    out ReferencedPathtracingLocalFogSample fogSample)
{
    fogSample = (ReferencedPathtracingLocalFogSample)0;
    fogSample.distance = maximumDistance;
    float directionLengthSquared =
        dot(rayDirectionWS, rayDirectionWS);
    if (_ReferencedLocalFogCount <= 0
        || directionLengthSquared <= 1e-12
        || maximumDistance <= minimumDistance)
    {
        return false;
    }

    float3 direction =
        rayDirectionWS * rsqrt(directionLengthSquared);
    bool intersectsAny = false;
    [loop]
    for (int recordIndex = 0;
        recordIndex < _ReferencedLocalFogCount;
        recordIndex++)
    {
        ReferencedPathtracingLocalFogRecord record =
            _ReferencedLocalFogList[recordIndex];
        float extinction =
            max(record.scatteringExtinction.w, 0.0);
        if (extinction <= 0.0)
            continue;

        float entryDistance;
        float exitDistance;
        float recordMaximumDistance =
            fogSample.hasEvent != 0u
                ? min(maximumDistance, fogSample.distance)
                : maximumDistance;
        if (!ReferencedPathtracingIntersectLocalFog(
                record,
                rayOriginWS,
                direction,
                minimumDistance,
                recordMaximumDistance,
                entryDistance,
                exitDistance))
        {
            continue;
        }

        intersectsAny = true;
        float trackingDistance = entryDistance;
        bool completedTracking = false;
        [loop]
        for (uint trackingStep = 0u;
            trackingStep
                < kReferencedPathtracingLocalFogMaximumTrackingStepCount;
            trackingStep++)
        {
            float distanceRandom =
                ReferencedPathtracingGetLocalFogTrackingRandom(
                    randomSeed,
                    (uint)recordIndex,
                    trackingStep,
                    0u);
            trackingDistance +=
                -log(1.0 - distanceRandom) / extinction;
            fogSample.trackingStepCount++;
            if (trackingDistance >= exitDistance)
            {
                completedTracking = true;
                break;
            }

            ReferencedPathtracingLocalFogPoint evaluatedPoint =
                ReferencedPathtracingEvaluateLocalFogPoint(
                    record,
                    rayOriginWS + direction * trackingDistance);
            float acceptanceRandom =
                ReferencedPathtracingGetLocalFogTrackingRandom(
                    randomSeed,
                    (uint)recordIndex,
                    trackingStep,
                    1u);
            if (acceptanceRandom
                >= evaluatedPoint.densityFactor)
                continue;

            completedTracking = true;
            if (fogSample.hasEvent == 0u
                || trackingDistance < fogSample.distance)
            {
                fogSample.distance = trackingDistance;
                fogSample.recordIndex = (uint)recordIndex;
                fogSample.hasEvent = 1u;
                fogSample.scatteringAlbedo =
                    evaluatedPoint.scatteringAlbedo;
                fogSample.anisotropy = clamp(
                    record.parameters.x,
                    -0.95,
                    0.95);
            }
            break;
        }

        if (!completedTracking
            && trackingDistance < exitDistance)
        {
            fogSample.trackingOverflow = 1u;
        }
    }

    return intersectsAny;
}

float3 ReferencedPathtracingEvaluateLocalFogTransmittance(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance)
{
    float directionLengthSquared =
        dot(rayDirectionWS, rayDirectionWS);
    if (_ReferencedLocalFogCount <= 0
        || directionLengthSquared <= 1e-12
        || maximumDistance <= 0.0)
    {
        return 1.0;
    }

    float3 direction =
        rayDirectionWS * rsqrt(directionLengthSquared);
    float opticalDepth = 0.0;
    [loop]
    for (int recordIndex = 0;
        recordIndex < _ReferencedLocalFogCount;
        recordIndex++)
    {
        ReferencedPathtracingLocalFogRecord record =
            _ReferencedLocalFogList[recordIndex];
        float extinction =
            max(record.scatteringExtinction.w, 0.0);
        if (extinction <= 0.0)
            continue;

        float entryDistance;
        float exitDistance;
        if (!ReferencedPathtracingIntersectLocalFog(
                record,
                rayOriginWS,
                direction,
                0.0,
                maximumDistance,
                entryDistance,
                exitDistance))
        {
            continue;
        }

        float segmentLength =
            exitDistance - entryDistance;
        float densitySum = 0.0;
        [unroll]
        for (uint sampleIndex = 0u;
            sampleIndex
                < kReferencedPathtracingLocalFogShadowIntegrationStepCount;
            sampleIndex++)
        {
            float sampleDistance =
                entryDistance
                + segmentLength
                    * ((sampleIndex + 0.5)
                        / kReferencedPathtracingLocalFogShadowIntegrationStepCount);
            densitySum +=
                ReferencedPathtracingEvaluateLocalFogDensityFactor(
                    record,
                    rayOriginWS + direction * sampleDistance);
        }

        opticalDepth +=
            extinction
            * segmentLength
            * densitySum
            / kReferencedPathtracingLocalFogShadowIntegrationStepCount;
        if (opticalDepth >= 80.0)
            return 0.0;
    }

    return exp(-min(max(opticalDepth, 0.0), 80.0)).xxx;
}

float ReferencedPathtracingEvaluateLocalFogPhasePdf(
    float anisotropy,
    float3 currentDirectionWS,
    float3 sampledDirectionWS)
{
    return ReferencedPathtracingEvaluateHenyeyGreensteinPhase(
        anisotropy,
        dot(
            normalize(currentDirectionWS),
            normalize(sampledDirectionWS)));
}

bool ReferencedPathtracingSampleLocalFogPhase(
    float anisotropy,
    float3 currentDirectionWS,
    float2 randomValue,
    out float3 sampledDirectionWS,
    out float phasePdf)
{
    return ReferencedPathtracingSampleHenyeyGreensteinPhase(
        currentDirectionWS,
        anisotropy,
        randomValue,
        sampledDirectionWS,
        phasePdf);
}

#endif
