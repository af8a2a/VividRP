#ifndef VIVIDRP_PUNCTUAL_LIGHT_COMMON_INCLUDED
#define VIVIDRP_PUNCTUAL_LIGHT_COMMON_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"

void ModifyVividPunctualLightDistancesForFillLighting(inout float4 distances, float lightSqRadius)
{
    distances.z = rsqrt(distances.y + max(lightSqRadius, 0.0));
}

bool IsVividPunctualProjectorBox(PunctualLightData punctualLight)
{
    return punctualLight.lightType == VIVID_PUNCTUAL_LIGHT_TYPE_PROJECTOR_BOX;
}

float VividPunctualLightProjectorBoxBoundsAttenuation(PunctualLightData punctualLight, float3 lightToSample)
{
    if (!IsVividPunctualProjectorBox(punctualLight))
        return 1.0;

    float3x3 lightToWorld = float3x3(punctualLight.rightWS, punctualLight.upWS, punctualLight.directionWS);
    float3 positionLS = mul(lightToSample, transpose(lightToWorld));
    float z = positionLS.z;
    float range = punctualLight.range;
    float boundsDistance = max(
        max(abs(positionLS.x), abs(positionLS.y)),
        abs(z - 0.5 * range) - 0.5 * range + 1.0);

    return boundsDistance <= 1.0 ? 1.0 : 0.0;
}

void GetVividPunctualLightVectors(
    float3 positionWS,
    PunctualLightData punctualLight,
    out float3 lightDirectionWS,
    out float4 distances)
{
    float3 lightToSample = positionWS - punctualLight.positionWS;
    distances.w = dot(lightToSample, punctualLight.directionWS);

    if (IsVividPunctualProjectorBox(punctualLight))
    {
        float distance = distances.w;
        float distanceSquared = distance * distance;

        lightDirectionWS = -punctualLight.directionWS;
        distances.xyz = float3(distance, distanceSquared, 1.0);
        return;
    }

    float3 unL = -lightToSample;
    float distanceSquared = dot(unL, unL);
    float inverseDistance = rsqrt(max(distanceSquared, 1e-12));
    float distance = distanceSquared * inverseDistance;

    lightDirectionWS = unL * inverseDistance;
    distances.xyz = float3(distance, distanceSquared, inverseDistance);

    ModifyVividPunctualLightDistancesForFillLighting(distances, punctualLight.shapeRadiusSquared);
}

float VividPunctualLightAttenuationWithDistanceModification(PunctualLightData punctualLight, float3 lightToSample, float4 distances)
{
    float distanceSquared = distances.y;
    float inverseDistance = distances.z;
    float spotCosine = distances.w * rsqrt(max(distanceSquared, 1e-12));

    float attenuation = min(inverseDistance, 1.0 / PUNCTUAL_LIGHT_THRESHOLD);
    attenuation *= DistanceWindowing(distanceSquared, punctualLight.rangeAttenuationScale, punctualLight.rangeAttenuationBias);
    attenuation *= AngleAttenuation(spotCosine, punctualLight.angleScale, punctualLight.angleOffset);

    return attenuation * attenuation
        * VividPunctualLightProjectorBoxBoundsAttenuation(punctualLight, lightToSample);
}

float VividPunctualLightAttenuationWithDistanceModification(PunctualLightData punctualLight, float4 distances)
{
    return VividPunctualLightAttenuationWithDistanceModification(punctualLight, float3(0.0, 0.0, 0.0), distances);
}

#endif
