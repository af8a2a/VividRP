#ifndef VIVIDRP_SIMPLE_SLAB_ENERGY_INCLUDED
#define VIVIDRP_SIMPLE_SLAB_ENERGY_INCLUDED

#include "VividSimpleSlabBSDF.hlsl"

#define VIVID_SIMPLE_SLAB_ENERGY_VERSION 1u
#define VIVID_SLAB_LUT_VERSION 1u
#define VIVID_SLAB_LUT_RESOLUTION 64
#define VIVID_SLAB_LUT_SAMPLE_COUNT 4096u

// LUT: RG = (integral GGX * SchlickWeight, unit-Fresnel single-scatter loss).
// BA = cosine-weighted hemispherical averages of RG. Store loss, not 1 - loss,
// to preserve near-mirror energy in half precision. No material colors baked in.
float2 VividSlabNormalizeAlbedoBasis(float2 basis)
{
    basis = saturate(basis);
    return basis / max(1.0f, basis.x + basis.y);
}

struct VividSimpleSlabEnergy
{
    float singleScatterLoss;
    float3 singleScatterSpecularAlbedo;
    float3 multipleScatterSpecularAlbedo;
    float3 specularAlbedo;
    float3 multipleScatterColor;
    float inverseAverageLoss;
    float3 diffuseTransmission;
    float3 inverseAverageTransmission;
};

VividSimpleSlabEnergy VividPrepareSimpleSlabEnergy(float3 specularF0, float4 lut)
{
    float2 directional = VividSlabNormalizeAlbedoBasis(lut.xy);
    float2 average = VividSlabNormalizeAlbedoBasis(lut.zw);
    float3 f0 = saturate(specularF0);
    float f90 = VividSimpleSlabDeriveAchromaticF90(f0);
    float averageAlbedo = 1.0f - average.y;
    float3 averageFresnel = f0 + (f90 - f0) / 21.0f;
    VividSimpleSlabEnergy energy;
    energy.singleScatterLoss = directional.y;
    // Kulla-Conty geometric series for the additional, reciprocal MS lobe.
    energy.multipleScatterColor = averageFresnel * averageFresnel * averageAlbedo
        / max(1.0f - averageFresnel * average.y, 1e-6f);
    energy.inverseAverageLoss = rcp(max(average.y, 1e-6f));
    energy.singleScatterSpecularAlbedo = saturate(
        f0 * (1.0f - directional.y - directional.x) + f90 * directional.x);
    energy.multipleScatterSpecularAlbedo = min(
        energy.multipleScatterColor * energy.singleScatterLoss,
        1.0f - energy.singleScatterSpecularAlbedo);
    energy.specularAlbedo = energy.singleScatterSpecularAlbedo + energy.multipleScatterSpecularAlbedo;
    float3 averageSpecular = saturate(f0 * (averageAlbedo - average.x) + f90 * average.x
        + energy.multipleScatterColor * average.y);
    energy.diffuseTransmission = 1.0f - energy.specularAlbedo;
    energy.inverseAverageTransmission = rcp(max(1.0f - averageSpecular, 1e-6f));
    return energy;
}

VividSimpleSlabBSDFResponse VividEvaluateSimpleSlabEnergyDirect(
    VividSimpleSlabData slab, float3 viewDirectionWS, float3 lightDirectionWS,
    VividSimpleSlabEnergy viewEnergy, float2 lightBasis)
{
    VividSimpleSlabBSDFResponse response =
        VividEvaluateSimpleSlabAnalyticDirect(slab, viewDirectionWS, lightDirectionWS);
    float nDotL = dot(slab.normalWS, lightDirectionWS);
    if (dot(slab.normalWS, viewDirectionWS) <= 0.0f || nDotL <= 0.0f)
        return response;

    lightBasis = VividSlabNormalizeAlbedoBasis(lightBasis);
    float lightLoss = lightBasis.y;
    float3 lightSpecular = saturate(saturate(slab.specularF0) * (1.0f - lightBasis.y - lightBasis.x)
        + VividSimpleSlabDeriveAchromaticF90(slab.specularF0) * lightBasis.x
        + viewEnergy.multipleScatterColor * lightLoss);
    response.specular += viewEnergy.multipleScatterColor
        * (viewEnergy.singleScatterLoss * lightLoss
            * viewEnergy.inverseAverageLoss * VIVID_SIMPLE_SLAB_INV_PI * nDotL);
    // Reciprocal interface transmission. Its integral is albedo * (1 - E_spec(V)).
    response.diffuse *= viewEnergy.diffuseTransmission * (1.0f - lightSpecular)
        * viewEnergy.inverseAverageTransmission;
    return response;
}

#endif
