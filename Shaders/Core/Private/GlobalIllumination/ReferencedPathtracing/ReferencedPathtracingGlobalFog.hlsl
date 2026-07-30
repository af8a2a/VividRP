#ifndef VIVIDRP_REFERENCED_PATH_TRACING_GLOBAL_FOG_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_GLOBAL_FOG_INCLUDED

int _ReferencedGlobalFogEnabled;
// rgb: single-scattering albedo, w: base-layer extinction.
float4 _ReferencedGlobalFogScatteringExtinction;
// x: base height, y: reciprocal scale height, z: camera-centered support
// radius, w: Henyey-Greenstein anisotropy.
float4 _ReferencedGlobalFogHeightAnisotropy;
// x: ambient/environment dimmer, y: directional-lights-only.
float4 _ReferencedGlobalFogLighting;

struct ReferencedPathtracingGlobalFogSample
{
    float distance;
    uint hasEvent;
};

bool ReferencedPathtracingHasGlobalFog()
{
    return _ReferencedGlobalFogEnabled != 0
        && _ReferencedGlobalFogScatteringExtinction.w > 0.0
        && _ReferencedGlobalFogHeightAnisotropy.z > 0.0;
}

float ReferencedPathtracingEvaluateGlobalFogDensityAtHeight(
    float height)
{
    float heightAboveBase = max(
        height - _ReferencedGlobalFogHeightAnisotropy.x,
        0.0);
    return exp(
        -heightAboveBase
        * max(_ReferencedGlobalFogHeightAnisotropy.y, 0.0));
}

bool ReferencedPathtracingIntersectGlobalFogSupport(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float minimumDistance,
    float maximumDistance,
    out float entryDistance,
    out float exitDistance)
{
    entryDistance = 0.0;
    exitDistance = 0.0;
    if (!ReferencedPathtracingHasGlobalFog())
        return false;

    float directionLengthSquared =
        dot(rayDirectionWS, rayDirectionWS);
    if (directionLengthSquared <= 1e-12)
        return false;

    float3 direction =
        rayDirectionWS * rsqrt(directionLengthSquared);
    float3 cameraToOrigin =
        rayOriginWS - _CameraPositionWS.xyz;
    float projectedOrigin = dot(cameraToOrigin, direction);
    float supportRadius =
        _ReferencedGlobalFogHeightAnisotropy.z;
    float discriminant =
        projectedOrigin * projectedOrigin
        - (dot(cameraToOrigin, cameraToOrigin)
            - supportRadius * supportRadius);
    if (discriminant < 0.0)
        return false;

    float root = sqrt(max(discriminant, 0.0));
    float nearDistance = -projectedOrigin - root;
    float farDistance = -projectedOrigin + root;
    entryDistance = max(
        max(nearDistance, minimumDistance),
        0.0);
    exitDistance = min(
        farDistance,
        max(maximumDistance, 0.0));
    return exitDistance > entryDistance;
}

float ReferencedPathtracingIntegrateGlobalFogDensitySingleRegion(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float startDistance,
    float endDistance)
{
    float distance = max(endDistance - startDistance, 0.0);
    if (distance <= 0.0)
        return 0.0;

    float startHeight =
        rayOriginWS.y + rayDirectionWS.y * startDistance;
    float endHeight =
        rayOriginWS.y + rayDirectionWS.y * endDistance;
    float baseHeight =
        _ReferencedGlobalFogHeightAnisotropy.x;
    if (max(startHeight, endHeight) <= baseHeight)
        return distance;

    float reciprocalScaleHeight =
        max(_ReferencedGlobalFogHeightAnisotropy.y, 0.0);
    float verticalRate =
        reciprocalScaleHeight * rayDirectionWS.y;
    float startDensity =
        ReferencedPathtracingEvaluateGlobalFogDensityAtHeight(
            startHeight);
    if (abs(verticalRate) <= 1e-7)
        return startDensity * distance;

    float exponent = verticalRate * distance;
    if (abs(exponent) < 1e-3)
    {
        float exponentialIntegralFactor =
            1.0
            - 0.5 * exponent
            + exponent * exponent * (1.0 / 6.0);
        return max(
            startDensity
            * distance
            * exponentialIntegralFactor,
            0.0);
    }

    // Evaluating the two endpoint densities avoids exp(-exponent)
    // overflow on long downward rays.
    float endDensity =
        ReferencedPathtracingEvaluateGlobalFogDensityAtHeight(
            endHeight);
    return max(
        (startDensity - endDensity) / verticalRate,
        0.0);
}

float ReferencedPathtracingIntegrateGlobalFogDensity(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float startDistance,
    float endDistance)
{
    if (endDistance <= startDistance)
        return 0.0;

    float verticalDirection = rayDirectionWS.y;
    if (abs(verticalDirection) <= 1e-7)
    {
        return ReferencedPathtracingIntegrateGlobalFogDensitySingleRegion(
            rayOriginWS,
            rayDirectionWS,
            startDistance,
            endDistance);
    }

    float crossingDistance =
        (_ReferencedGlobalFogHeightAnisotropy.x
            - rayOriginWS.y)
        / verticalDirection;
    if (crossingDistance > startDistance
        && crossingDistance < endDistance)
    {
        return
            ReferencedPathtracingIntegrateGlobalFogDensitySingleRegion(
                rayOriginWS,
                rayDirectionWS,
                startDistance,
                crossingDistance)
            + ReferencedPathtracingIntegrateGlobalFogDensitySingleRegion(
                rayOriginWS,
                rayDirectionWS,
                crossingDistance,
                endDistance);
    }

    return ReferencedPathtracingIntegrateGlobalFogDensitySingleRegion(
        rayOriginWS,
        rayDirectionWS,
        startDistance,
        endDistance);
}

float ReferencedPathtracingInvertGlobalFogDensitySingleRegion(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float startDistance,
    float endDistance,
    float targetOpticalLength)
{
    float regionDistance = max(
        endDistance - startDistance,
        0.0);
    if (regionDistance <= 0.0
        || targetOpticalLength <= 0.0)
    {
        return startDistance;
    }

    float startHeight =
        rayOriginWS.y + rayDirectionWS.y * startDistance;
    float endHeight =
        rayOriginWS.y + rayDirectionWS.y * endDistance;
    float baseHeight =
        _ReferencedGlobalFogHeightAnisotropy.x;
    if (max(startHeight, endHeight) <= baseHeight)
    {
        return min(
            startDistance + targetOpticalLength,
            endDistance);
    }

    float reciprocalScaleHeight =
        max(_ReferencedGlobalFogHeightAnisotropy.y, 0.0);
    float verticalRate =
        reciprocalScaleHeight * rayDirectionWS.y;
    float startDensity =
        ReferencedPathtracingEvaluateGlobalFogDensityAtHeight(
            startHeight);
    if (abs(verticalRate) <= 1e-7)
    {
        return min(
            startDistance
                + targetOpticalLength
                    / max(startDensity, 1e-20),
            endDistance);
    }

    float logarithmArgument =
        1.0
        - targetOpticalLength
            * verticalRate
            / max(startDensity, 1e-20);
    float localDistance =
        -log(max(logarithmArgument, 1e-20))
        / verticalRate;
    return startDistance
        + clamp(localDistance, 0.0, regionDistance);
}

float ReferencedPathtracingInvertGlobalFogDensity(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float startDistance,
    float endDistance,
    float targetOpticalLength)
{
    float verticalDirection = rayDirectionWS.y;
    if (abs(verticalDirection) > 1e-7)
    {
        float crossingDistance =
            (_ReferencedGlobalFogHeightAnisotropy.x
                - rayOriginWS.y)
            / verticalDirection;
        if (crossingDistance > startDistance
            && crossingDistance < endDistance)
        {
            float firstRegionOpticalLength =
                ReferencedPathtracingIntegrateGlobalFogDensitySingleRegion(
                    rayOriginWS,
                    rayDirectionWS,
                    startDistance,
                    crossingDistance);
            if (targetOpticalLength
                <= firstRegionOpticalLength)
            {
                return ReferencedPathtracingInvertGlobalFogDensitySingleRegion(
                    rayOriginWS,
                    rayDirectionWS,
                    startDistance,
                    crossingDistance,
                    targetOpticalLength);
            }

            return ReferencedPathtracingInvertGlobalFogDensitySingleRegion(
                rayOriginWS,
                rayDirectionWS,
                crossingDistance,
                endDistance,
                targetOpticalLength
                    - firstRegionOpticalLength);
        }
    }

    return ReferencedPathtracingInvertGlobalFogDensitySingleRegion(
        rayOriginWS,
        rayDirectionWS,
        startDistance,
        endDistance,
        targetOpticalLength);
}

bool ReferencedPathtracingSampleGlobalFog(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float minimumDistance,
    float maximumDistance,
    float randomValue,
    out ReferencedPathtracingGlobalFogSample fogSample)
{
    fogSample =
        (ReferencedPathtracingGlobalFogSample)0;
    float directionLengthSquared =
        dot(rayDirectionWS, rayDirectionWS);
    if (directionLengthSquared <= 1e-12)
        return false;

    float3 direction =
        rayDirectionWS * rsqrt(directionLengthSquared);
    float entryDistance;
    float exitDistance;
    if (!ReferencedPathtracingIntersectGlobalFogSupport(
            rayOriginWS,
            direction,
            minimumDistance,
            maximumDistance,
            entryDistance,
            exitDistance))
    {
        return false;
    }

    float extinction =
        max(_ReferencedGlobalFogScatteringExtinction.w, 0.0);
    float segmentOpticalLength =
        ReferencedPathtracingIntegrateGlobalFogDensity(
            rayOriginWS,
            direction,
            entryDistance,
            exitDistance);
    float segmentOpticalDepth =
        extinction * segmentOpticalLength;
    float sampledOpticalDepth =
        -log(max(
            1.0 - min(saturate(randomValue), 0.99999994),
            1e-7));
    if (sampledOpticalDepth >= segmentOpticalDepth)
        return true;

    fogSample.distance =
        ReferencedPathtracingInvertGlobalFogDensity(
            rayOriginWS,
            direction,
            entryDistance,
            exitDistance,
            sampledOpticalDepth / max(extinction, 1e-20));
    fogSample.hasEvent = 1u;
    return true;
}

float3 ReferencedPathtracingEvaluateGlobalFogTransmittance(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance)
{
    float directionLengthSquared =
        dot(rayDirectionWS, rayDirectionWS);
    if (directionLengthSquared <= 1e-12)
        return 1.0;

    float3 direction =
        rayDirectionWS * rsqrt(directionLengthSquared);
    float entryDistance;
    float exitDistance;
    if (!ReferencedPathtracingIntersectGlobalFogSupport(
            rayOriginWS,
            direction,
            0.0,
            maximumDistance,
            entryDistance,
            exitDistance))
    {
        return 1.0;
    }

    float opticalLength =
        ReferencedPathtracingIntegrateGlobalFogDensity(
            rayOriginWS,
            direction,
            entryDistance,
            exitDistance);
    float opticalDepth =
        max(_ReferencedGlobalFogScatteringExtinction.w, 0.0)
        * opticalLength;
    return exp(-min(max(opticalDepth, 0.0), 80.0)).xxx;
}

float ReferencedPathtracingEvaluateHenyeyGreensteinPhase(
    float anisotropy,
    float cosineTheta)
{
    anisotropy = clamp(anisotropy, -0.95, 0.95);
    float anisotropySquared =
        anisotropy * anisotropy;
    float denominator = max(
        1.0
            + anisotropySquared
            - 2.0 * anisotropy
                * clamp(cosineTheta, -1.0, 1.0),
        1e-6);
    return (1.0 - anisotropySquared)
        / (4.0
            * kReferencedPathtracingPi
            * denominator
            * sqrt(denominator));
}

float ReferencedPathtracingEvaluateGlobalFogPhase(
    float cosineTheta)
{
    return ReferencedPathtracingEvaluateHenyeyGreensteinPhase(
        _ReferencedGlobalFogHeightAnisotropy.w,
        cosineTheta);
}

float ReferencedPathtracingEvaluateGlobalFogPhasePdf(
    float3 currentDirectionWS,
    float3 sampledDirectionWS)
{
    return ReferencedPathtracingEvaluateGlobalFogPhase(
        dot(
            normalize(currentDirectionWS),
            normalize(sampledDirectionWS)));
}

bool ReferencedPathtracingSampleHenyeyGreensteinPhase(
    float3 currentDirectionWS,
    float anisotropy,
    float2 randomValue,
    out float3 sampledDirectionWS,
    out float phasePdf)
{
    sampledDirectionWS = 0.0;
    phasePdf = 0.0;
    anisotropy = clamp(anisotropy, -0.95, 0.95);
    float cosineTheta;
    if (abs(anisotropy) < 1e-3)
    {
        cosineTheta =
            1.0 - 2.0 * saturate(randomValue.x);
    }
    else
    {
        float numerator =
            1.0 - anisotropy * anisotropy;
        float denominator = max(
            1.0
                - anisotropy
                + 2.0 * anisotropy
                    * saturate(randomValue.x),
            1e-6);
        float ratio = numerator / denominator;
        cosineTheta = clamp(
            (1.0
                + anisotropy * anisotropy
                - ratio * ratio)
                / (2.0 * anisotropy),
            -1.0,
            1.0);
    }

    float sineTheta = sqrt(
        saturate(1.0 - cosineTheta * cosineTheta));
    float azimuth =
        2.0
        * kReferencedPathtracingPi
        * saturate(randomValue.y);
    float sineAzimuth;
    float cosineAzimuth;
    sincos(
        azimuth,
        sineAzimuth,
        cosineAzimuth);
    float3 forward =
        normalize(currentDirectionWS);
    float3 basisX;
    float3 basisY;
    ReferencedPathtracingBuildDirectionalBasis(
        forward,
        basisX,
        basisY);
    sampledDirectionWS = normalize(
        basisX
            * (sineTheta * cosineAzimuth)
        + basisY
            * (sineTheta * sineAzimuth)
        + forward * cosineTheta);
    phasePdf =
        ReferencedPathtracingEvaluateHenyeyGreensteinPhase(
            anisotropy,
            cosineTheta);
    return phasePdf > 0.0
        && !isnan(phasePdf)
        && !isinf(phasePdf);
}

bool ReferencedPathtracingSampleGlobalFogPhase(
    float3 currentDirectionWS,
    float2 randomValue,
    out float3 sampledDirectionWS,
    out float phasePdf)
{
    return ReferencedPathtracingSampleHenyeyGreensteinPhase(
        currentDirectionWS,
        _ReferencedGlobalFogHeightAnisotropy.w,
        randomValue,
        sampledDirectionWS,
        phasePdf);
}

#endif
