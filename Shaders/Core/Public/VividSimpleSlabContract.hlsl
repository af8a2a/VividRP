#ifndef VIVIDRP_SIMPLE_SLAB_CONTRACT_INCLUDED
#define VIVIDRP_SIMPLE_SLAB_CONTRACT_INCLUDED

// Phase 8.0 freezes this contract without switching the production deferred
// evaluator. C# SimpleSlabContract is the source of truth for these constants.
#define VIVID_SIMPLE_SLAB_CONTRACT_VERSION 1u
#define VIVID_SIMPLE_SLAB_FINGERPRINT_VERSION 1u
#define VIVID_SIMPLE_SLAB_FINGERPRINT_LO 0xB6B790D8u
#define VIVID_SIMPLE_SLAB_FINGERPRINT_HI 0x26E2E47Bu
#define VIVID_SIMPLE_SLAB_SURFACE_SUMMARY_ABI_VERSION 1u
#define VIVID_SIMPLE_SLAB_FIELD_COUNT 4u

#define VIVID_SIMPLE_SLAB_FIELD_DIFFUSE_ALBEDO 0u
#define VIVID_SIMPLE_SLAB_FIELD_SPECULAR_F0 1u
#define VIVID_SIMPLE_SLAB_FIELD_PERCEPTUAL_ROUGHNESS 2u
#define VIVID_SIMPLE_SLAB_FIELD_NORMAL_WS 3u

#define VIVID_SIMPLE_SLAB_DIFFUSE_MODEL_LAMBERT 1u
#define VIVID_SIMPLE_SLAB_SPECULAR_MODEL_ISOTROPIC_GGX_SMITH_CORRELATED 1u
#define VIVID_SIMPLE_SLAB_FRESNEL_MODEL_SCHLICK_DERIVED_ACHROMATIC_F90 1u
#define VIVID_SIMPLE_SLAB_ENERGY_MODEL_DIRECTIONAL_ALBEDO_MULTIPLE_SCATTERING 1u

#define VIVID_SIMPLE_SLAB_MINIMUM_ALPHA_ROUGHNESS 0.002f
#define VIVID_SIMPLE_SLAB_DERIVED_F90_FADE_THRESHOLD 0.02f
#define VIVID_SIMPLE_SLAB_DERIVED_F90_SCALE 50.0f

#define VIVID_SIMPLE_SLAB_DIRECT_RESPONSE_INCLUDES_NDOTL 1u
#define VIVID_SIMPLE_SLAB_AO_AFFECTS_DIRECT_LIGHTING 0u
#define VIVID_SIMPLE_SLAB_AO_AFFECTS_INDIRECT_LIGHTING 1u
#define VIVID_SIMPLE_SLAB_EMISSION_ADDED_AFTER_LIGHTING 1u

struct VividSimpleSlabData
{
    float3 diffuseAlbedo;
    float3 specularF0;
    float perceptualRoughness;
    float3 normalWS;
};

float VividSimpleSlabPerceptualRoughnessToAlpha(
    float perceptualRoughness)
{
    float saturatedRoughness = saturate(perceptualRoughness);
    return max(
        saturatedRoughness * saturatedRoughness,
        VIVID_SIMPLE_SLAB_MINIMUM_ALPHA_ROUGHNESS);
}

float VividSimpleSlabDeriveAchromaticF90(float3 specularF0)
{
    float averageF0 = dot(saturate(specularF0), (1.0f / 3.0f).xxx);
    return saturate(averageF0 * VIVID_SIMPLE_SLAB_DERIVED_F90_SCALE);
}

float3 VividSimpleSlabEvaluateSchlickFresnel(
    float3 specularF0,
    float cosine)
{
    float3 saturatedF0 = saturate(specularF0);
    float f90 = VividSimpleSlabDeriveAchromaticF90(saturatedF0);
    float oneMinusCosine = 1.0f - saturate(cosine);
    float schlickWeight = oneMinusCosine * oneMinusCosine;
    schlickWeight *= schlickWeight * oneMinusCosine;
    return lerp(saturatedF0, f90.xxx, schlickWeight);
}

#endif
