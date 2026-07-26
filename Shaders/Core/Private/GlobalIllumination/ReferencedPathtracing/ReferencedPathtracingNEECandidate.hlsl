#ifndef VIVIDRP_REFERENCED_PATH_TRACING_NEE_CANDIDATE_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_NEE_CANDIDATE_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingSegmentLight.hlsl"

// RTXPT-style canonical light sample. incidentRadianceOverPdf already contains
// both the discrete light-selection and conditional shape/direction PDFs.
struct ReferencedPathtracingNEECandidate
{
    float3 incidentRadianceOverPdf;
    float3 directionWS;
    float distance;
    float selectionPdf;
    float solidAnglePdf;
    float shadowStrength;
    uint lightIndex;
    uint lightType;
    uint flags;
    uint valid;
};

void ReferencedPathtracingInitializeNEECandidate(
    out ReferencedPathtracingNEECandidate candidate)
{
    candidate = (ReferencedPathtracingNEECandidate)0;
    candidate.lightIndex = 0xffffffffu;
    candidate.lightType = REFERENCED_LIGHT_TYPE_INVALID;
}

bool ReferencedPathtracingSampleDirectionalCandidate(
    ReferencedPathTracingLightRecord light,
    float2 shapeSample,
    inout ReferencedPathtracingNEECandidate candidate)
{
    float directionLengthSquared = dot(light.forwardWS, light.forwardWS);
    if (directionLengthSquared <= 1e-8)
        return false;

    float3 centerDirectionWS =
        -light.forwardWS * rsqrt(directionLengthSquared);
    uint isDelta;
    ReferencedPathtracingSampleDirectionalLight(
        centerDirectionWS,
        light.angularDiameter,
        shapeSample,
        candidate.directionWS,
        candidate.solidAnglePdf,
        isDelta);
    candidate.distance = 3.402823466e+38;
    candidate.flags = light.flags;
    if (isDelta != 0u)
        candidate.flags |= REFERENCED_LIGHT_FLAG_SINGULAR;

    // Directional RGB is illuminance. A finite disk has constant radiance
    // illuminance / solidAngle, so Li / (pSelect * pDirection) = E / pSelect.
    candidate.incidentRadianceOverPdf =
        max(light.radiometricColor, 0.0) / candidate.selectionPdf;
    return any(candidate.incidentRadianceOverPdf > 0.0);
}

bool ReferencedPathtracingSamplePunctualCandidate(
    float3 positionWS,
    ReferencedPathTracingLightRecord light,
    inout ReferencedPathtracingNEECandidate candidate)
{
    float3 surfaceToLight = light.positionWS - positionWS;
    float distanceSquared = dot(surfaceToLight, surfaceToLight);
    if (distanceSquared <= 1e-8)
        return false;

    float inverseGeometricDistance = rsqrt(distanceSquared);
    candidate.distance =
        distanceSquared * inverseGeometricDistance;
    candidate.directionWS =
        surfaceToLight * inverseGeometricDistance;

    float inverseModifiedDistance = rsqrt(
        distanceSquared
        + max(light.shapeRadius * light.shapeRadius, 0.0));
    float attenuation =
        min(inverseModifiedDistance, 1.0 / PUNCTUAL_LIGHT_THRESHOLD);
    attenuation *= DistanceWindowing(
        distanceSquared,
        light.rangeAttenuation.x,
        light.rangeAttenuation.y);

    if (light.lightType == REFERENCED_LIGHT_TYPE_SPOT)
    {
        float directionLengthSquared =
            dot(light.forwardWS, light.forwardWS);
        if (directionLengthSquared <= 1e-8)
            return false;

        float3 spotDirectionWS =
            light.forwardWS * rsqrt(directionLengthSquared);
        attenuation *= AngleAttenuation(
            dot(spotDirectionWS, -candidate.directionWS),
            light.spotAngleParameters.x,
            light.spotAngleParameters.y);
    }

    attenuation *= attenuation;
    candidate.solidAnglePdf = 0.0;
    candidate.flags = light.flags | REFERENCED_LIGHT_FLAG_SINGULAR;
    candidate.incidentRadianceOverPdf =
        max(light.radiometricColor, 0.0)
        * attenuation
        / candidate.selectionPdf;
    return attenuation > 0.0
        && any(candidate.incidentRadianceOverPdf > 0.0);
}

bool ReferencedPathtracingFinalizeAreaCandidate(
    float3 positionWS,
    ReferencedPathTracingLightRecord light,
    float3 lightSamplePositionWS,
    float3 lightForwardWS,
    float sampleArea,
    inout ReferencedPathtracingNEECandidate candidate)
{
    float3 surfaceToLight = lightSamplePositionWS - positionWS;
    float distanceSquared = dot(surfaceToLight, surfaceToLight);
    if (distanceSquared <= 1e-8 || sampleArea <= 1e-8)
        return false;

    float inverseDistance = rsqrt(distanceSquared);
    candidate.distance = distanceSquared * inverseDistance;
    candidate.directionWS = surfaceToLight * inverseDistance;
    float lightFacingCosine =
        saturate(dot(-candidate.directionWS, lightForwardWS));
    if (lightFacingCosine <= 0.0)
        return false;

    candidate.solidAnglePdf =
        distanceSquared / (lightFacingCosine * sampleArea);
    float rangeWindow = SmoothDistanceWindowing(
        distanceSquared,
        light.rangeAttenuation.x,
        light.rangeAttenuation.y);
    float fullLightPdf =
        candidate.selectionPdf * candidate.solidAnglePdf;
    candidate.flags = light.flags;
    candidate.incidentRadianceOverPdf =
        max(light.radiometricColor, 0.0)
        * rangeWindow
        / fullLightPdf;
    return rangeWindow > 0.0
        && fullLightPdf > 0.0
        && any(candidate.incidentRadianceOverPdf > 0.0);
}

bool ReferencedPathtracingSampleRectangleCandidate(
    float3 positionWS,
    ReferencedPathTracingLightRecord light,
    float2 shapeSample,
    inout ReferencedPathtracingNEECandidate candidate)
{
    float3 lightCenterWS;
    float2 lightSize;
    float3 lightRightWS;
    float3 lightUpWS;
    float3 lightForwardWS;
    if (!ReferencedPathtracingResolveRectangleBarnDoor(
            light,
            positionWS,
            lightCenterWS,
            lightSize,
            lightRightWS,
            lightUpWS,
            lightForwardWS))
    {
        return false;
    }

    float2 centeredSample = saturate(shapeSample) - 0.5;
    float3 lightSamplePositionWS = lightCenterWS
        + centeredSample.x * lightSize.x * lightRightWS
        + centeredSample.y * lightSize.y * lightUpWS;
    return ReferencedPathtracingFinalizeAreaCandidate(
        positionWS,
        light,
        lightSamplePositionWS,
        lightForwardWS,
        lightSize.x * lightSize.y,
        candidate);
}

bool ReferencedPathtracingSampleDiscCandidate(
    float3 positionWS,
    ReferencedPathTracingLightRecord light,
    float2 shapeSample,
    inout ReferencedPathtracingNEECandidate candidate)
{
    float rightLengthSquared = dot(light.rightWS, light.rightWS);
    float upLengthSquared = dot(light.upWS, light.upWS);
    float forwardLengthSquared = dot(light.forwardWS, light.forwardWS);
    float radius = max(light.shapeRadius, 0.0);
    if (rightLengthSquared <= 1e-8
        || upLengthSquared <= 1e-8
        || forwardLengthSquared <= 1e-8
        || radius <= 1e-6)
    {
        return false;
    }

    float radialDistance = radius * sqrt(saturate(shapeSample.x));
    float phi =
        2.0 * kReferencedPathtracingPi * saturate(shapeSample.y);
    float sinePhi;
    float cosinePhi;
    sincos(phi, sinePhi, cosinePhi);
    float3 lightRightWS =
        light.rightWS * rsqrt(rightLengthSquared);
    float3 lightUpWS =
        light.upWS * rsqrt(upLengthSquared);
    float3 lightForwardWS =
        light.forwardWS * rsqrt(forwardLengthSquared);
    float3 lightSamplePositionWS = light.positionWS
        + radialDistance
            * (cosinePhi * lightRightWS + sinePhi * lightUpWS);
    return ReferencedPathtracingFinalizeAreaCandidate(
        positionWS,
        light,
        lightSamplePositionWS,
        lightForwardWS,
        kReferencedPathtracingPi * radius * radius,
        candidate);
}

bool ReferencedPathtracingSampleTubeCandidate(
    float3 positionWS,
    ReferencedPathTracingLightRecord light,
    float shapeSample,
    inout ReferencedPathtracingNEECandidate candidate)
{
    float length = max(light.areaSize.x, 0.0);
    float rightLengthSquared = dot(light.rightWS, light.rightWS);
    if (length <= 1e-6 || rightLengthSquared <= 1e-8)
        return false;

    float3 rightWS = light.rightWS * rsqrt(rightLengthSquared);
    float3 lightSamplePositionWS = light.positionWS
        + (saturate(shapeSample) - 0.5) * length * rightWS;
    float3 surfaceToLight = lightSamplePositionWS - positionWS;
    float distanceSquared = dot(surfaceToLight, surfaceToLight);
    if (distanceSquared <= 1e-8)
        return false;

    float inverseDistance = rsqrt(distanceSquared);
    candidate.distance = distanceSquared * inverseDistance;
    candidate.directionWS = surfaceToLight * inverseDistance;
    float axialCosine = abs(dot(candidate.directionWS, rightWS));
    float radialCosine =
        sqrt(saturate(1.0 - axialCosine * axialCosine));
    if (radialCosine <= 0.0)
        return false;

    float shapeJacobian =
        2.0 * radialCosine * length / distanceSquared;
    float rangeWindow = SmoothDistanceWindowing(
        distanceSquared,
        light.rangeAttenuation.x,
        light.rangeAttenuation.y);
    candidate.solidAnglePdf = 0.0;
    candidate.flags = light.flags | REFERENCED_LIGHT_FLAG_SINGULAR;
    candidate.incidentRadianceOverPdf =
        max(light.radiometricColor, 0.0)
        * rangeWindow
        * shapeJacobian
        / candidate.selectionPdf;
    return rangeWindow > 0.0
        && shapeJacobian > 0.0
        && any(candidate.incidentRadianceOverPdf > 0.0);
}

bool ReferencedPathtracingSampleEnvironmentCandidate(
    float2 shapeSample,
    inout ReferencedPathtracingNEECandidate candidate)
{
    float3 environmentRadiance;
    if (!ReferencedPathtracingSampleEnvironment(
            shapeSample,
            candidate.directionWS,
            environmentRadiance,
            candidate.solidAnglePdf))
    {
        return false;
    }

    float fullLightPdf =
        candidate.selectionPdf * candidate.solidAnglePdf;
    candidate.distance = 3.402823466e+38;
    candidate.shadowStrength = 1.0;
    candidate.flags = REFERENCED_LIGHT_FLAG_INFINITE
        | REFERENCED_LIGHT_FLAG_BSDF_REACHABLE
        | REFERENCED_LIGHT_FLAG_CASTS_SHADOWS;
    candidate.incidentRadianceOverPdf =
        max(environmentRadiance, 0.0) / fullLightPdf;
    return fullLightPdf > 0.0
        && any(candidate.incidentRadianceOverPdf > 0.0);
}

bool ReferencedPathtracingSampleUnifiedNEECandidate(
    float3 positionWS,
    float3 randomSample,
    out ReferencedPathtracingNEECandidate candidate)
{
    ReferencedPathtracingInitializeNEECandidate(candidate);
    if (!ReferencedPathtracingSampleUnifiedLightSource(
            randomSample.x,
            candidate.lightType,
            candidate.lightIndex,
            candidate.selectionPdf))
    {
        return false;
    }

    bool sampled = false;
    if (candidate.lightType == REFERENCED_LIGHT_TYPE_ENVIRONMENT)
    {
        candidate.shadowStrength = 1.0;
        candidate.flags = REFERENCED_LIGHT_FLAG_INFINITE
            | REFERENCED_LIGHT_FLAG_BSDF_REACHABLE
            | REFERENCED_LIGHT_FLAG_CASTS_SHADOWS;
        sampled = ReferencedPathtracingSampleEnvironmentCandidate(
            randomSample.yz,
            candidate);
    }
    else
    {
        ReferencedPathTracingLightRecord light =
            ReferencedPathtracingLoadReferenceLight(candidate.lightIndex);
        candidate.flags = light.flags;
        candidate.shadowStrength =
            (light.flags & REFERENCED_LIGHT_FLAG_CASTS_SHADOWS) != 0u
                ? saturate(light.shadowStrength)
                : 0.0;

        if (light.lightType == REFERENCED_LIGHT_TYPE_DIRECTIONAL)
        {
            sampled = ReferencedPathtracingSampleDirectionalCandidate(
                light,
                randomSample.yz,
                candidate);
        }
        else if (light.lightType == REFERENCED_LIGHT_TYPE_POINT
            || light.lightType == REFERENCED_LIGHT_TYPE_SPOT)
        {
            sampled = _ReferencedLocalLightNeeEnabled != 0
                && ReferencedPathtracingSamplePunctualCandidate(
                    positionWS,
                    light,
                    candidate);
        }
        else if (light.lightType == REFERENCED_LIGHT_TYPE_RECTANGLE)
        {
            sampled = _ReferencedLocalLightNeeEnabled != 0
                && ReferencedPathtracingSampleRectangleCandidate(
                    positionWS,
                    light,
                    randomSample.yz,
                    candidate);
        }
        else if (light.lightType == REFERENCED_LIGHT_TYPE_DISC)
        {
            sampled = _ReferencedLocalLightNeeEnabled != 0
                && ReferencedPathtracingSampleDiscCandidate(
                    positionWS,
                    light,
                    randomSample.yz,
                    candidate);
        }
        else if (light.lightType == REFERENCED_LIGHT_TYPE_TUBE)
        {
            sampled = _ReferencedLocalLightNeeEnabled != 0
                && ReferencedPathtracingSampleTubeCandidate(
                    positionWS,
                    light,
                    randomSample.y,
                    candidate);
        }
    }

    candidate.valid = sampled
        && VividReferencedPathtracingIsFinite(
            candidate.incidentRadianceOverPdf)
        && VividReferencedPathtracingIsFinite(candidate.directionWS)
        && VividReferencedPathtracingIsFinite(candidate.distance)
        && VividReferencedPathtracingIsFinite(candidate.selectionPdf)
        && VividReferencedPathtracingIsFinite(candidate.solidAnglePdf)
            ? 1u
            : 0u;
    return candidate.valid != 0u;
}

#endif
