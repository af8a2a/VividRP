#ifndef VIVIDRP_AREA_LIGHT_COMMON_INCLUDED
#define VIVIDRP_AREA_LIGHT_COMMON_INCLUDED

#include "Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"

void ApplyRectangularAreaLightBarnDoor(inout AreaLightData areaLight, float3 positionWS)
{
    if (areaLight.lightType != VIVID_AREA_LIGHT_TYPE_RECTANGLE)
        return;

    if (areaLight.cosBarnDoorAngle <= 0.017f || areaLight.barnDoorLength <= 0.05f)
        return;

    float halfWidth = areaLight.width * 0.5;
    float halfHeight = areaLight.height * 0.5;
    float3 pointToLight = positionWS - areaLight.positionWS;
    float3 pointLS = float3(
        dot(pointToLight, areaLight.rightWS),
        dot(pointToLight, areaLight.upWS),
        dot(pointToLight, areaLight.forwardWS));

    float maxDepth = areaLight.cosBarnDoorAngle * areaLight.barnDoorLength;
    float pointDepth = min(pointLS.z, maxDepth);
    float pointDepthRatio = pointDepth / max(maxDepth, 1e-5);
    float sinTheta = sqrt(saturate(1.0 - areaLight.cosBarnDoorAngle * areaLight.cosBarnDoorAngle));
    float barnDoorProjection = sinTheta * areaLight.barnDoorLength * pointDepthRatio;
    float2 pointSign = sign(pointLS.xy);
    pointLS.xy = pointSign * max(abs(pointLS.xy), float2(halfWidth, halfHeight) + barnDoorProjection.xx);

    float3 closestLightCorner = float3(
        pointSign.x * (halfWidth + barnDoorProjection),
        pointSign.y * (halfHeight + barnDoorProjection),
        pointDepth);
    float3 pointProjection = pointLS - closestLightCorner;
    float cosPhi = max(0.0, pointProjection.z);
    float2 tanPhi = cosPhi > 0.001f
        ? abs(pointProjection.xy) / cosPhi
        : float2(99999.0, 99999.0);
    float2 projectionDistance = pointDepth * tanPhi;

    float2 topRight = float2(-halfWidth, halfWidth);
    float2 bottomLeft = float2(-halfHeight, halfHeight);
    topRight += (projectionDistance.x - barnDoorProjection) * float2(max(0.0, -pointSign.x), -max(0.0, pointSign.x));
    bottomLeft += (projectionDistance.y - barnDoorProjection) * float2(max(0.0, -pointSign.y), -max(0.0, pointSign.y));
    topRight = clamp(topRight, -halfWidth, halfWidth);
    bottomLeft = clamp(bottomLeft, -halfHeight, halfHeight);

    float2 lightCenterOffset = 0.5f * float2(topRight.x + topRight.y, bottomLeft.x + bottomLeft.y);
    areaLight.width = topRight.y - topRight.x;
    areaLight.height = bottomLeft.y - bottomLeft.x;
    areaLight.positionWS += areaLight.rightWS * lightCenterOffset.x + areaLight.upWS * lightCenterOffset.y;
}

float EvaluateAreaLightIntensity(AreaLightData areaLight, float3 positionWS)
{
    float3 unL = areaLight.positionWS - positionWS;
    float halfLength = areaLight.width * 0.5;
    float halfHeight = areaLight.height * 0.5;
    float intensity = PillowWindowing(
        unL,
        areaLight.rightWS,
        areaLight.upWS,
        halfLength,
        halfHeight,
        areaLight.rangeAttenuationScale,
        areaLight.rangeAttenuationBias);

    if (areaLight.lightType == VIVID_AREA_LIGHT_TYPE_RECTANGLE
        && dot(unL, areaLight.forwardWS) >= 0.0)
    {
        return 0.0;
    }

    return intensity;
}

#endif
