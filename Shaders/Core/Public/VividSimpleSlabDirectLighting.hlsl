#ifndef VIVIDRP_SIMPLE_SLAB_DIRECT_LIGHTING_INCLUDED
#define VIVIDRP_SIMPLE_SLAB_DIRECT_LIGHTING_INCLUDED

#include "VividSlabLut.hlsl"
#include "PunctualLightCommon.hlsl"

#define VIVID_SIMPLE_SLAB_DIRECT_LIGHTING_VERSION 2u
#define VIVID_SIMPLE_SLAB_MIN_DIRECTION_LENGTH_SQ 1e-12f
#define VIVID_SIMPLE_SLAB_MIN_PUNCTUAL_DISTANCE_SQ 1e-6f

struct VividSimpleSlabDirectLighting
{
    float3 diffuse;
    float3 specular;
};

VividSimpleSlabDirectLighting VividCreateEmptySimpleSlabDirectLighting()
{
    VividSimpleSlabDirectLighting lighting;
    lighting.diffuse = 0.0f;
    lighting.specular = 0.0f;
    return lighting;
}

float3 VividSimpleSlabNormalizeDirection(float3 direction)
{
    float lengthSquared = dot(direction, direction);
    return lengthSquared > VIVID_SIMPLE_SLAB_MIN_DIRECTION_LENGTH_SQ
        ? direction * rsqrt(lengthSquared)
        : 0.0f;
}

VividSimpleSlabDirectLighting VividApplySimpleSlabLightColor(
    VividSimpleSlabBSDFResponse response,
    float3 lightColor)
{
    VividSimpleSlabDirectLighting lighting;
    lighting.diffuse = response.diffuse * lightColor;
    lighting.specular = response.specular * lightColor;
    return lighting;
}

VividSimpleSlabDirectLighting VividEvaluateSimpleSlabDirectionalLight(
    VividSimpleSlabData slab,
    VividSimpleSlabEnergy viewEnergy,
    float3 viewDirectionWS,
    DirectionalLightData directionalLight,
    float shadowAttenuation)
{
    float3 lightDirectionWS = VividSimpleSlabNormalizeDirection(
        directionalLight.directionWS);
    VividSimpleSlabBSDFResponse response =
        VividEvaluateSimpleSlabEnergyDirect(
            slab,
            viewDirectionWS,
            lightDirectionWS,
            viewEnergy,
            VividSampleSlabLut(dot(slab.normalWS, lightDirectionWS), slab.perceptualRoughness).xy);
    float3 lightColor = max(directionalLight.color, 0.0f)
        * saturate(shadowAttenuation);
    return VividApplySimpleSlabLightColor(response, lightColor);
}

VividSimpleSlabDirectLighting VividEvaluateSimpleSlabPunctualLight(
    VividSimpleSlabData slab,
    VividSimpleSlabEnergy viewEnergy,
    float3 positionWS,
    float3 viewDirectionWS,
    PunctualLightData punctualLight)
{
    VividSimpleSlabDirectLighting lighting =
        VividCreateEmptySimpleSlabDirectLighting();
    float3 lightDirectionWS;
    float4 distances;
    GetVividPunctualLightVectors(
        positionWS,
        punctualLight,
        lightDirectionWS,
        distances);
    if (distances.y <= VIVID_SIMPLE_SLAB_MIN_PUNCTUAL_DISTANCE_SQ)
        return lighting;
    if (dot(slab.normalWS, lightDirectionWS) <= 0.0f)
        return lighting;

    float attenuation = VividPunctualLightAttenuationWithDistanceModification(
        punctualLight,
        positionWS - punctualLight.positionWS,
        distances);
    if (attenuation <= 0.0f)
        return lighting;

    VividSimpleSlabBSDFResponse response =
        VividEvaluateSimpleSlabEnergyDirect(
            slab,
            viewDirectionWS,
            lightDirectionWS,
            viewEnergy,
            VividSampleSlabLut(dot(slab.normalWS, lightDirectionWS), slab.perceptualRoughness).xy);
    return VividApplySimpleSlabLightColor(
        response,
        max(punctualLight.color, 0.0f) * attenuation);
}

VividSimpleSlabDirectLighting VividScaleSimpleSlabDirectLighting(
    VividSimpleSlabDirectLighting lighting,
    float3 weight)
{
    lighting.diffuse *= weight;
    lighting.specular *= weight;
    return lighting;
}

void VividAccumulateSimpleSlabDirectLighting(
    VividSimpleSlabDirectLighting lighting,
    inout VividSimpleSlabDirectLighting aggregateLighting)
{
    aggregateLighting.diffuse += lighting.diffuse;
    aggregateLighting.specular += lighting.specular;
}

#endif
