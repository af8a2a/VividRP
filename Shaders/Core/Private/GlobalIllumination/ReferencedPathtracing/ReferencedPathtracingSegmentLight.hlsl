#ifndef VIVIDRP_REFERENCED_PATH_TRACING_SEGMENT_LIGHT_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_SEGMENT_LIGHT_INCLUDED

// Analytic light hit reconstructed along a BSDF-sampled ray. These virtual
// emitters do not need to be present in the scene RTAS, but their intersection
// and conditional PDF must exactly match the NEE shape sampler.
struct ReferencedPathtracingSegmentLightHit
{
    float3 radiance;
    float distance;
    float selectionPdf;
    float solidAnglePdf;
    float shadowStrength;
    uint lightIndex;
    uint lightType;
    uint flags;
    uint valid;
};

void ReferencedPathtracingInitializeSegmentLightHit(
    out ReferencedPathtracingSegmentLightHit lightHit)
{
    lightHit = (ReferencedPathtracingSegmentLightHit)0;
    lightHit.lightIndex = 0xffffffffu;
    lightHit.lightType = REFERENCED_LIGHT_TYPE_INVALID;
}

float ReferencedPathtracingEvaluateAreaRangeWindow(
    float distanceSquared,
    float2 rangeAttenuation)
{
    // Matches Core RP SmoothDistanceWindowing without importing the material
    // lighting include graph into the ray-generation shader.
    float scaledDistanceSquared =
        distanceSquared * rangeAttenuation.x;
    float window = saturate(
        rangeAttenuation.y
        - scaledDistanceSquared * scaledDistanceSquared);
    return window * window;
}

bool ReferencedPathtracingResolveRectangleBarnDoor(
    ReferencedPathTracingLightRecord light,
    float3 positionWS,
    out float3 lightCenterWS,
    out float2 lightSize,
    out float3 lightRightWS,
    out float3 lightUpWS,
    out float3 lightForwardWS)
{
    lightCenterWS = light.positionWS;
    lightSize = max(light.areaSize, 0.0);
    lightRightWS = 0.0;
    lightUpWS = 0.0;
    lightForwardWS = 0.0;

    float rightLengthSquared = dot(light.rightWS, light.rightWS);
    float upLengthSquared = dot(light.upWS, light.upWS);
    float forwardLengthSquared = dot(light.forwardWS, light.forwardWS);
    if (light.lightType != REFERENCED_LIGHT_TYPE_RECTANGLE
        || any(lightSize <= 1e-6)
        || rightLengthSquared <= 1e-8
        || upLengthSquared <= 1e-8
        || forwardLengthSquared <= 1e-8)
    {
        return false;
    }

    lightRightWS = light.rightWS * rsqrt(rightLengthSquared);
    lightUpWS = light.upWS * rsqrt(upLengthSquared);
    lightForwardWS = light.forwardWS * rsqrt(forwardLengthSquared);

    // Match the existing Vivid/HDRP barn-door convention.
    float cosBarnDoorAngle = saturate(light.barnDoorCosAngle);
    float barnDoorLength = max(light.barnDoorLength, 0.0);
    if (cosBarnDoorAngle <= 0.017 || barnDoorLength <= 0.05)
        return true;

    float2 halfSize = 0.5 * lightSize;
    float3 lightRelativePosition = positionWS - light.positionWS;
    float3 pointLS = float3(
        dot(lightRelativePosition, lightRightWS),
        dot(lightRelativePosition, lightUpWS),
        dot(lightRelativePosition, lightForwardWS));

    float maxDepth = cosBarnDoorAngle * barnDoorLength;
    float pointDepth = min(pointLS.z, maxDepth);
    float pointDepthRatio = pointDepth / max(maxDepth, 1e-5);
    float sinTheta =
        sqrt(saturate(1.0 - cosBarnDoorAngle * cosBarnDoorAngle));
    float barnDoorProjection =
        sinTheta * barnDoorLength * pointDepthRatio;

    float2 pointSign = sign(pointLS.xy);
    pointLS.xy = pointSign
        * max(abs(pointLS.xy), halfSize + barnDoorProjection.xx);

    float3 closestLightCorner = float3(
        pointSign.x * (halfSize.x + barnDoorProjection),
        pointSign.y * (halfSize.y + barnDoorProjection),
        pointDepth);
    float3 pointProjection = pointLS - closestLightCorner;
    float cosPhi = max(0.0, pointProjection.z);
    float2 tanPhi = cosPhi > 0.001
        ? abs(pointProjection.xy) / cosPhi
        : float2(99999.0, 99999.0);
    float2 projectionDistance = pointDepth * tanPhi;

    float2 horizontalBounds = float2(-halfSize.x, halfSize.x);
    float2 verticalBounds = float2(-halfSize.y, halfSize.y);
    horizontalBounds += (projectionDistance.x - barnDoorProjection)
        * float2(max(0.0, -pointSign.x), -max(0.0, pointSign.x));
    verticalBounds += (projectionDistance.y - barnDoorProjection)
        * float2(max(0.0, -pointSign.y), -max(0.0, pointSign.y));
    horizontalBounds =
        clamp(horizontalBounds, -halfSize.x, halfSize.x);
    verticalBounds =
        clamp(verticalBounds, -halfSize.y, halfSize.y);

    float2 lightCenterOffset = 0.5 * float2(
        horizontalBounds.x + horizontalBounds.y,
        verticalBounds.x + verticalBounds.y);
    lightSize = max(
        float2(
            horizontalBounds.y - horizontalBounds.x,
            verticalBounds.y - verticalBounds.x),
        0.0);
    lightCenterWS += lightRightWS * lightCenterOffset.x
        + lightUpWS * lightCenterOffset.y;
    return all(lightSize > 1e-6);
}

bool ReferencedPathtracingEvaluateDirectionalSegmentLight(
    float3 rayDirectionWS,
    ReferencedPathTracingLightRecord light,
    inout ReferencedPathtracingSegmentLightHit lightHit)
{
    float forwardLengthSquared = dot(light.forwardWS, light.forwardWS);
    if (forwardLengthSquared <= 1e-8)
        return false;

    float conditionalPdf;
    if (!ReferencedPathtracingEvaluateDirectionalLightPdf(
            -light.forwardWS * rsqrt(forwardLengthSquared),
            light.angularDiameter,
            rayDirectionWS,
            conditionalPdf))
    {
        return false;
    }

    lightHit.distance = 3.402823466e+38;
    lightHit.solidAnglePdf = conditionalPdf;
    // Directional RGB is illuminance. A finite disk has radiance
    // illuminance / solidAngle = illuminance * conditionalPdf.
    lightHit.radiance =
        max(light.radiometricColor, 0.0) * conditionalPdf;
    return conditionalPdf > 0.0 && any(lightHit.radiance > 0.0);
}

bool ReferencedPathtracingFinalizeAreaSegmentLight(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    ReferencedPathTracingLightRecord light,
    float3 lightCenterWS,
    float3 lightRightWS,
    float3 lightUpWS,
    float3 lightForwardWS,
    float2 halfExtents,
    float sampleArea,
    bool isDisc,
    inout ReferencedPathtracingSegmentLightHit lightHit)
{
    float lightFacingCosine =
        -dot(rayDirectionWS, lightForwardWS);
    if (lightFacingCosine <= 1e-6 || sampleArea <= 1e-8)
        return false;

    float planeDenominator =
        dot(rayDirectionWS, lightForwardWS);
    float distance = dot(
        lightCenterWS - rayOriginWS,
        lightForwardWS) / planeDenominator;
    if (distance <= 1e-5
        || isnan(distance)
        || isinf(distance))
    {
        return false;
    }

    float3 hitOffsetWS =
        rayOriginWS + distance * rayDirectionWS - lightCenterWS;
    float2 hitOffset = float2(
        dot(hitOffsetWS, lightRightWS),
        dot(hitOffsetWS, lightUpWS));
    bool insideShape = isDisc
        ? dot(hitOffset, hitOffset)
            <= halfExtents.x * halfExtents.x
        : all(abs(hitOffset) <= halfExtents);
    if (!insideShape)
        return false;

    float distanceSquared = distance * distance;
    float rangeWindow =
        ReferencedPathtracingEvaluateAreaRangeWindow(
            distanceSquared,
            light.rangeAttenuation);
    if (rangeWindow <= 0.0)
        return false;

    lightHit.distance = distance;
    lightHit.solidAnglePdf =
        distanceSquared / (lightFacingCosine * sampleArea);
    lightHit.radiance =
        max(light.radiometricColor, 0.0) * rangeWindow;
    return lightHit.solidAnglePdf > 0.0
        && any(lightHit.radiance > 0.0);
}

bool ReferencedPathtracingEvaluateRectangleSegmentLight(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    ReferencedPathTracingLightRecord light,
    inout ReferencedPathtracingSegmentLightHit lightHit)
{
    float3 lightCenterWS;
    float2 lightSize;
    float3 lightRightWS;
    float3 lightUpWS;
    float3 lightForwardWS;
    if (!ReferencedPathtracingResolveRectangleBarnDoor(
            light,
            rayOriginWS,
            lightCenterWS,
            lightSize,
            lightRightWS,
            lightUpWS,
            lightForwardWS))
    {
        return false;
    }

    return ReferencedPathtracingFinalizeAreaSegmentLight(
        rayOriginWS,
        rayDirectionWS,
        light,
        lightCenterWS,
        lightRightWS,
        lightUpWS,
        lightForwardWS,
        0.5 * lightSize,
        lightSize.x * lightSize.y,
        false,
        lightHit);
}

bool ReferencedPathtracingEvaluateDiscSegmentLight(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    ReferencedPathTracingLightRecord light,
    inout ReferencedPathtracingSegmentLightHit lightHit)
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

    return ReferencedPathtracingFinalizeAreaSegmentLight(
        rayOriginWS,
        rayDirectionWS,
        light,
        light.positionWS,
        light.rightWS * rsqrt(rightLengthSquared),
        light.upWS * rsqrt(upLengthSquared),
        light.forwardWS * rsqrt(forwardLengthSquared),
        float2(radius, radius),
        kReferencedPathtracingPi * radius * radius,
        true,
        lightHit);
}

bool ReferencedPathtracingEvaluateSegmentLight(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    uint lightIndex,
    ReferencedPathTracingLightRecord light,
    out ReferencedPathtracingSegmentLightHit lightHit)
{
    ReferencedPathtracingInitializeSegmentLightHit(lightHit);
    if ((light.flags & REFERENCED_LIGHT_FLAG_BSDF_REACHABLE) == 0u)
        return false;

    float directionLengthSquared =
        dot(rayDirectionWS, rayDirectionWS);
    if (directionLengthSquared <= 1e-8
        || isnan(directionLengthSquared)
        || isinf(directionLengthSquared))
    {
        return false;
    }

    float3 directionWS =
        rayDirectionWS * rsqrt(directionLengthSquared);
    lightHit.lightIndex = lightIndex;
    lightHit.lightType = light.lightType;
    lightHit.flags = light.flags;
    lightHit.selectionPdf =
        ReferencedPathtracingGetUnifiedReferenceLightSelectionPdf(light);
    lightHit.shadowStrength =
        (light.flags & REFERENCED_LIGHT_FLAG_CASTS_SHADOWS) != 0u
            ? saturate(light.shadowStrength)
            : 0.0;

    bool evaluated = false;
    if (light.lightType == REFERENCED_LIGHT_TYPE_DIRECTIONAL)
    {
        evaluated =
            ReferencedPathtracingEvaluateDirectionalSegmentLight(
                directionWS,
                light,
                lightHit);
    }
    else if (light.lightType == REFERENCED_LIGHT_TYPE_RECTANGLE)
    {
        evaluated =
            ReferencedPathtracingEvaluateRectangleSegmentLight(
                rayOriginWS,
                directionWS,
                light,
                lightHit);
    }
    else if (light.lightType == REFERENCED_LIGHT_TYPE_DISC)
    {
        evaluated =
            ReferencedPathtracingEvaluateDiscSegmentLight(
                rayOriginWS,
                directionWS,
                light,
                lightHit);
    }

    lightHit.valid = evaluated
        && !any(isnan(lightHit.radiance))
        && !any(isinf(lightHit.radiance))
        && !isnan(lightHit.distance)
        && !isinf(lightHit.distance)
        && !isnan(lightHit.selectionPdf)
        && !isinf(lightHit.selectionPdf)
        && !isnan(lightHit.solidAnglePdf)
        && !isinf(lightHit.solidAnglePdf)
            ? 1u
            : 0u;
    return lightHit.valid != 0u;
}

#endif
