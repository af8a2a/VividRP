#ifndef VIVIDRP_HAIR_GEOMETRY_INCLUDED
#define VIVIDRP_HAIR_GEOMETRY_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"

#define ATTRIBUTES_NEED_TANGENT
#define ATTRIBUTES_NEED_TEXCOORD0
#define ATTRIBUTES_NEED_TEXCOORD1
#define ATTRIBUTES_NEED_TEXCOORD2
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Raytracing/RayTracingCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Raytracing/RaytracingIntersection.hlsl"

struct VividHairSurfaceGeometry
{
    float3 positionWS;
    float3 centerlinePositionWS;
    float3 previousPositionWS;
    float3 previousCenterlinePositionWS;
    float3 faceNormalWS;
    float3 radialNormalWS;
    float3 tangentWS;
    float2 strandUV;
    float radius;
    float segmentU;
    float hitDistance;
};

float3 VividHairTransformPositionToWorld(float3 positionOS)
{
    return mul(ObjectToWorld3x4(), float4(positionOS, 1.0));
}

float3 VividHairTransformPreviousPositionToWorld(float3 positionOS)
{
    return TransformPreviousObjectToWorld(positionOS);
}

float3 VividHairTransformNormalToWorld(float3 normalOS)
{
    return normalize(mul(normalOS, (float3x3)WorldToObject3x4()));
}

float3 VividHairTransformDirectionToWorld(float3 directionOS)
{
    return normalize(mul(directionOS, (float3x3)WorldToObject3x4()));
}

bool VividHairIsFiniteScalar(float value)
{
    return value == value && abs(value) < 3.402823466e+38;
}

bool VividHairIsFinite3(float3 value)
{
    return VividHairIsFiniteScalar(value.x)
        && VividHairIsFiniteScalar(value.y)
        && VividHairIsFiniteScalar(value.z);
}

void VividHairBuildPerpendicularFrame(
    float3 tangentOS,
    out float3 firstAxisOS,
    out float3 secondAxisOS)
{
    tangentOS = normalize(tangentOS);
    float3 referenceAxis = abs(dot(tangentOS, float3(0.0, 1.0, 0.0)))
            < 0.999
        ? float3(0.0, 1.0, 0.0)
        : float3(1.0, 0.0, 0.0);
    firstAxisOS = normalize(cross(tangentOS, referenceAxis));
    secondAxisOS = normalize(cross(tangentOS, firstAxisOS));
}

float3 VividHairReconstructPreviousSurfacePositionOS(
    float3 currentPositionOS,
    float3 currentCenterlinePositionOS,
    float3 currentSegmentStartOS,
    float3 currentSegmentEndOS,
    float3 previousSegmentStartOS,
    float3 previousSegmentEndOS,
    float previousRadius,
    float segmentU)
{
    float3 currentTangent = normalize(
        currentSegmentEndOS - currentSegmentStartOS);
    float3 previousSegmentVector =
        previousSegmentEndOS - previousSegmentStartOS;
    float previousSegmentLength = length(previousSegmentVector);
    if (previousSegmentLength <= 1e-7)
        return currentPositionOS;

    float3 previousTangent =
        previousSegmentVector / previousSegmentLength;
    float3 currentFirstAxis;
    float3 currentSecondAxis;
    VividHairBuildPerpendicularFrame(
        currentTangent,
        currentFirstAxis,
        currentSecondAxis);
    float3 previousFirstAxis;
    float3 previousSecondAxis;
    VividHairBuildPerpendicularFrame(
        previousTangent,
        previousFirstAxis,
        previousSecondAxis);

    float3 currentRadialVector =
        currentPositionOS - currentCenterlinePositionOS;
    float radialLength = length(currentRadialVector);
    float3 currentRadialDirection = radialLength > 1e-7
        ? currentRadialVector / radialLength
        : currentFirstAxis;
    float2 radialCoordinates = float2(
        dot(currentRadialDirection, currentFirstAxis),
        dot(currentRadialDirection, currentSecondAxis));
    float radialCoordinateLength = length(radialCoordinates);
    radialCoordinates = radialCoordinateLength > 1e-7
        ? radialCoordinates / radialCoordinateLength
        : float2(1.0, 0.0);

    float3 previousRadialDirection = normalize(
        previousFirstAxis * radialCoordinates.x
        + previousSecondAxis * radialCoordinates.y);
    float3 previousCenterlinePositionOS = lerp(
        previousSegmentStartOS,
        previousSegmentEndOS,
        saturate(segmentU));
    float3 previousPositionOS = previousCenterlinePositionOS
        + previousRadialDirection * max(previousRadius, 1e-7);
    return VividHairIsFinite3(previousPositionOS)
        ? previousPositionOS
        : currentPositionOS;
}

bool VividHairAcceptConeRoot(
    float candidateT,
    float referenceT,
    float axisOrigin,
    float axisDirection,
    float segmentLength,
    inout float selectedT,
    inout float selectedError)
{
    float axisDistance = axisOrigin + candidateT * axisDirection;
    bool valid = VividHairIsFiniteScalar(candidateT)
        && candidateT >= 0.0
        && axisDistance >= -1e-5
        && axisDistance <= segmentLength + 1e-5;
    float error = abs(candidateT - referenceT);
    if (valid && error < selectedError)
    {
        selectedT = candidateT;
        selectedError = error;
        return true;
    }

    return false;
}

bool VividHairIntersectTaperedSegmentBody(
    float3 rayOriginOS,
    float3 rayDirectionOS,
    float referenceT,
    float3 segmentStartOS,
    float3 segmentEndOS,
    float radius0,
    float radius1,
    out float hitT,
    out float segmentU,
    out float3 centerlinePositionOS,
    out float3 radialNormalOS)
{
    float3 segmentVector = segmentEndOS - segmentStartOS;
    float segmentLength = length(segmentVector);
    if (segmentLength <= 1e-7)
    {
        hitT = referenceT;
        segmentU = 0.0;
        centerlinePositionOS = segmentStartOS;
        radialNormalOS = -normalize(rayDirectionOS);
        return false;
    }

    float3 axis = segmentVector / segmentLength;
    float radiusSlope = (radius1 - radius0) / segmentLength;
    float3 relativeOrigin = rayOriginOS - segmentStartOS;
    float axisOrigin = dot(relativeOrigin, axis);
    float axisDirection = dot(rayDirectionOS, axis);
    float3 perpendicularOrigin = relativeOrigin - axisOrigin * axis;
    float3 perpendicularDirection =
        rayDirectionOS - axisDirection * axis;
    float radiusAtRayOrigin = radius0 + radiusSlope * axisOrigin;
    float radiusDirection = radiusSlope * axisDirection;

    float quadraticA = dot(
        perpendicularDirection,
        perpendicularDirection) - radiusDirection * radiusDirection;
    float quadraticB = 2.0 * (
        dot(perpendicularOrigin, perpendicularDirection)
        - radiusAtRayOrigin * radiusDirection);
    float quadraticC = dot(perpendicularOrigin, perpendicularOrigin)
        - radiusAtRayOrigin * radiusAtRayOrigin;

    hitT = referenceT;
    float selectedError = 3.402823466e+38;
    bool foundRoot = false;
    if (abs(quadraticA) > 1e-10)
    {
        float discriminant = quadraticB * quadraticB
            - 4.0 * quadraticA * quadraticC;
        if (discriminant >= 0.0)
        {
            float rootDiscriminant = sqrt(max(discriminant, 0.0));
            float inverseDenominator = 0.5 / quadraticA;
            float firstRoot = (-quadraticB - rootDiscriminant)
                * inverseDenominator;
            float secondRoot = (-quadraticB + rootDiscriminant)
                * inverseDenominator;
            foundRoot |= VividHairAcceptConeRoot(
                firstRoot,
                referenceT,
                axisOrigin,
                axisDirection,
                segmentLength,
                hitT,
                selectedError);
            foundRoot |= VividHairAcceptConeRoot(
                secondRoot,
                referenceT,
                axisOrigin,
                axisDirection,
                segmentLength,
                hitT,
                selectedError);
        }
    }
    else if (abs(quadraticB) > 1e-10)
    {
        foundRoot = VividHairAcceptConeRoot(
            -quadraticC / quadraticB,
            referenceT,
            axisOrigin,
            axisDirection,
            segmentLength,
            hitT,
            selectedError);
    }

    float axisDistance = clamp(
        axisOrigin + hitT * axisDirection,
        0.0,
        segmentLength);
    segmentU = saturate(axisDistance / segmentLength);
    centerlinePositionOS = segmentStartOS + axis * axisDistance;
    float3 hitPositionOS = rayOriginOS + hitT * rayDirectionOS;
    float3 radialVector = hitPositionOS - centerlinePositionOS;
    float radialLength = length(radialVector);
    float3 radialDirection = radialLength > 1e-7
        ? radialVector / radialLength
        : normalize(-perpendicularDirection);
    radialNormalOS = normalize(radialDirection - radiusSlope * axis);

    if (!VividHairIsFinite3(radialNormalOS))
    {
        radialNormalOS = normalize(-rayDirectionOS);
        foundRoot = false;
    }

    return foundRoot;
}

VividHairSurfaceGeometry VividHairBuildDotsSurfaceGeometry(
    AttributeData attributeData)
{
    uint3 indices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float3 barycentrics = float3(
        1.0 - attributeData.barycentrics.x - attributeData.barycentrics.y,
        attributeData.barycentrics.x,
        attributeData.barycentrics.y);

    float3 positionsOS[3];
    float3 offsetNormalsOS[3];
    float4 tangentsOS[3];
    float4 strandUVs[3];
    float4 radiusAndEndpoints[3];
    float4 previousCenterlineAndRadius[3];
    bool hasPreviousCenterline =
        UnityRayTracingHasVertexAttribute(kVertexAttributeTexCoord2);

    [unroll]
    for (uint vertex = 0u; vertex < 3u; ++vertex)
    {
        positionsOS[vertex] = UnityRayTracingFetchVertexAttribute3(
            indices[vertex],
            kVertexAttributePosition);
        offsetNormalsOS[vertex] = UnityRayTracingFetchVertexAttribute3(
            indices[vertex],
            kVertexAttributeNormal);
        tangentsOS[vertex] = UnityRayTracingFetchVertexAttribute4(
            indices[vertex],
            kVertexAttributeTangent);
        strandUVs[vertex] = UnityRayTracingFetchVertexAttribute4(
            indices[vertex],
            kVertexAttributeTexCoord0);
        radiusAndEndpoints[vertex] = UnityRayTracingFetchVertexAttribute4(
            indices[vertex],
            UnityRayTracingHasVertexAttribute(kVertexAttributeTexCoord1)
                ? kVertexAttributeTexCoord1
                : kVertexAttributeTexCoord0);
        previousCenterlineAndRadius[vertex] = hasPreviousCenterline
            ? UnityRayTracingFetchVertexAttribute4(
                indices[vertex],
                kVertexAttributeTexCoord2)
            : 0.0;
    }

    float radius0 = max(radiusAndEndpoints[0].x, 1e-7);
    float radius1 = max(radiusAndEndpoints[2].x, 1e-7);
    float3 segmentStartOS = positionsOS[0]
        - offsetNormalsOS[0] * radius0;
    float3 segmentEndOS = positionsOS[2]
        - offsetNormalsOS[2] * radius1;
    float3 previousSegmentStartOS = hasPreviousCenterline
        ? previousCenterlineAndRadius[0].xyz
        : segmentStartOS;
    float3 previousSegmentEndOS = hasPreviousCenterline
        ? previousCenterlineAndRadius[2].xyz
        : segmentEndOS;
    float previousRadius0 = hasPreviousCenterline
        ? max(previousCenterlineAndRadius[0].w, 1e-7)
        : radius0;
    float previousRadius1 = hasPreviousCenterline
        ? max(previousCenterlineAndRadius[2].w, 1e-7)
        : radius1;

    float correctedT;
    float segmentU;
    float3 centerlinePositionOS;
    float3 radialNormalOS;
    VividHairIntersectTaperedSegmentBody(
        ObjectRayOrigin(),
        ObjectRayDirection(),
        RayTCurrent(),
        segmentStartOS,
        segmentEndOS,
        radius0,
        radius1,
        correctedT,
        segmentU,
        centerlinePositionOS,
        radialNormalOS);

    float3 correctedPositionOS = ObjectRayOrigin()
        + correctedT * ObjectRayDirection();
    float previousRadius = lerp(
        previousRadius0,
        previousRadius1,
        segmentU);
    float3 previousCenterlinePositionOS = lerp(
        previousSegmentStartOS,
        previousSegmentEndOS,
        segmentU);
    float3 previousPositionOS =
        VividHairReconstructPreviousSurfacePositionOS(
            correctedPositionOS,
            centerlinePositionOS,
            segmentStartOS,
            segmentEndOS,
            previousSegmentStartOS,
            previousSegmentEndOS,
            previousRadius,
            segmentU);
    float3 positionWS = VividHairTransformPositionToWorld(
        correctedPositionOS);
    float3 centerlinePositionWS = VividHairTransformPositionToWorld(
        centerlinePositionOS);
    float3 radialNormalWS = VividHairTransformNormalToWorld(radialNormalOS);
    float3 tangentOS = normalize(segmentEndOS - segmentStartOS);
    float3 tangentWS = VividHairTransformDirectionToWorld(tangentOS);

    if (dot(radialNormalWS, -WorldRayDirection()) < 0.0)
        radialNormalWS = -radialNormalWS;

    VividHairSurfaceGeometry geometry;
    geometry.positionWS = positionWS;
    geometry.centerlinePositionWS = centerlinePositionWS;
    geometry.previousPositionWS =
        VividHairTransformPreviousPositionToWorld(previousPositionOS);
    geometry.previousCenterlinePositionWS =
        VividHairTransformPreviousPositionToWorld(
            previousCenterlinePositionOS);
    geometry.faceNormalWS = radialNormalWS;
    geometry.radialNormalWS = radialNormalWS;
    geometry.tangentWS = tangentWS;
    geometry.strandUV =
        strandUVs[0].xy * barycentrics.x
        + strandUVs[1].xy * barycentrics.y
        + strandUVs[2].xy * barycentrics.z;
    geometry.radius = max(length(positionWS - centerlinePositionWS), 1e-7);
    geometry.segmentU = segmentU;
    geometry.hitDistance = correctedT;
    return geometry;
}

#endif
