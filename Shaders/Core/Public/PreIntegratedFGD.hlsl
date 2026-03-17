#ifndef VIVIDRP_PREINTEGRATED_FGD_INCLUDED
#define VIVIDRP_PREINTEGRATED_FGD_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

#define VIVID_FGD_TEXTURE_RESOLUTION 64

TEXTURE2D(_PreIntegratedFGD_GGXDisneyDiffuse);

void GetPreIntegratedFGDGGXAndDisneyDiffuse(
    float NdotV,
    float perceptualRoughness,
    float3 fresnel0,
    float F90,
    out float3 specularFGD,
    out float diffuseFGD,
    out float reflectivity)
{
    float2 coordLUT = Remap01ToHalfTexelCoord(
        float2(sqrt(saturate(NdotV)), saturate(perceptualRoughness)),
        VIVID_FGD_TEXTURE_RESOLUTION);

    float3 preFGD = SAMPLE_TEXTURE2D_LOD(_PreIntegratedFGD_GGXDisneyDiffuse, sampler_LinearClamp, coordLUT, 0).xyz;

    specularFGD = (F90 - fresnel0) * preFGD.xxx + fresnel0 * preFGD.yyy;
    diffuseFGD = preFGD.z + 0.5;
    reflectivity = preFGD.y;
}

void GetPreIntegratedFGDGGXAndDisneyDiffuse(
    float NdotV,
    float perceptualRoughness,
    float3 fresnel0,
    out float3 specularFGD,
    out float diffuseFGD,
    out float reflectivity)
{
    GetPreIntegratedFGDGGXAndDisneyDiffuse(
        NdotV,
        perceptualRoughness,
        fresnel0,
        1.0,
        specularFGD,
        diffuseFGD,
        reflectivity);
}

void GetPreIntegratedFGDGGXAndLambert(
    float NdotV,
    float perceptualRoughness,
    float3 fresnel0,
    out float3 specularFGD,
    out float diffuseFGD,
    out float reflectivity)
{
    GetPreIntegratedFGDGGXAndDisneyDiffuse(
        NdotV,
        perceptualRoughness,
        fresnel0,
        specularFGD,
        diffuseFGD,
        reflectivity);
    diffuseFGD = 1.0;
}

TEXTURE2D(_PreIntegratedFGD_CharlieAndFabric);

void GetPreIntegratedFGDCharlieAndFabricLambert(
    float NdotV,
    float perceptualRoughness,
    float3 fresnel0,
    out float3 specularFGD,
    out float diffuseFGD,
    out float reflectivity)
{
    float3 preFGD = SAMPLE_TEXTURE2D_LOD(
        _PreIntegratedFGD_CharlieAndFabric,
        sampler_LinearClamp,
        saturate(float2(NdotV, perceptualRoughness)),
        0).xyz;

    specularFGD = lerp(preFGD.xxx, preFGD.yyy, fresnel0) * 2.0 * PI;
    diffuseFGD = preFGD.z;
    reflectivity = preFGD.y;
}

#endif
