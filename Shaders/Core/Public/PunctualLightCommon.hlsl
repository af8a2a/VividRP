#ifndef VIVIDRP_PUNCTUAL_LIGHT_COMMON_INCLUDED
#define VIVIDRP_PUNCTUAL_LIGHT_COMMON_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"

void ModifyVividPunctualLightDistancesForFillLighting(inout float4 distances, float lightSqRadius)
{
    distances.z = rsqrt(distances.y + max(lightSqRadius, 0.0));
}

void GetVividPunctualLightVectors(
    float3 positionWS,
    PunctualLightData punctualLight,
    out float3 lightDirectionWS,
    out float4 distances)
{
    float3 lightToSample = positionWS - punctualLight.positionWS;
    distances.w = dot(lightToSample, punctualLight.directionWS);

    float3 unL = -lightToSample;
    float distanceSquared = dot(unL, unL);
    float inverseDistance = rsqrt(max(distanceSquared, 1e-12));
    float distance = distanceSquared * inverseDistance;

    lightDirectionWS = unL * inverseDistance;
    distances.xyz = float3(distance, distanceSquared, inverseDistance);

    ModifyVividPunctualLightDistancesForFillLighting(distances, punctualLight.shapeRadiusSquared);
}

float VividPunctualLightAttenuationWithDistanceModification(PunctualLightData punctualLight, float4 distances)
{
    float distanceSquared = distances.y;
    float inverseDistance = distances.z;
    float spotCosine = distances.w * rsqrt(max(distanceSquared, 1e-12));

    float attenuation = min(inverseDistance, 1.0 / PUNCTUAL_LIGHT_THRESHOLD);
    attenuation *= DistanceWindowing(
        distanceSquared,
        punctualLight.rangeAttenuationScale,
        punctualLight.rangeAttenuationBias);
    attenuation *= AngleAttenuation(
        spotCosine,
        punctualLight.angleScale,
        punctualLight.angleOffset);

    return attenuation * attenuation;
}

#endif
