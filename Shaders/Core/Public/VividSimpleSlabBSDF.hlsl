#ifndef VIVIDRP_SIMPLE_SLAB_BSDF_INCLUDED
#define VIVIDRP_SIMPLE_SLAB_BSDF_INCLUDED

#include "VividSimpleSlabContract.hlsl"

// Standalone analytic kernel. It intentionally has no dependency on HDRP's
// BSDF.hlsl or Vivid's legacy HdrpLitLighting.hlsl.
#define VIVID_SIMPLE_SLAB_BSDF_KERNEL_VERSION 1u
#define VIVID_SIMPLE_SLAB_PI 3.14159265358979323846f
#define VIVID_SIMPLE_SLAB_INV_PI (1.0f / VIVID_SIMPLE_SLAB_PI)
#define VIVID_SIMPLE_SLAB_MIN_HALF_VECTOR_LENGTH_SQ 1e-8f
#define VIVID_SIMPLE_SLAB_MIN_VISIBILITY_DENOMINATOR 1e-6f

struct VividSimpleSlabBSDFResponse
{
    // Both lobes are colored directional responses and include NdotL.
    float3 diffuse;
    float3 specular;
};

VividSimpleSlabBSDFResponse VividCreateEmptySimpleSlabBSDFResponse()
{
    VividSimpleSlabBSDFResponse response;
    response.diffuse = 0.0f;
    response.specular = 0.0f;
    return response;
}

float VividSimpleSlabDGGX(float alphaRoughness, float nDotH)
{
    float alpha = max(
        alphaRoughness,
        VIVID_SIMPLE_SLAB_MINIMUM_ALPHA_ROUGHNESS);
    float alphaSquared = alpha * alpha;
    float saturatedNdotH = saturate(nDotH);
    // Avoid cancellation at NdotH = 1 and the minimum alpha.
    float denominator = (1.0f - saturatedNdotH) * (1.0f + saturatedNdotH)
        + saturatedNdotH * saturatedNdotH * alphaSquared;
    return alphaSquared
        / (VIVID_SIMPLE_SLAB_PI * denominator * denominator);
}

// Height-correlated Smith visibility. This returns G2 / (4 * NdotL * NdotV),
// so callers multiply D * V * F to obtain the specular BRDF.
float VividSimpleSlabVSmithGGXCorrelated(
    float alphaRoughness,
    float nDotV,
    float nDotL)
{
    float alpha = max(
        alphaRoughness,
        VIVID_SIMPLE_SLAB_MINIMUM_ALPHA_ROUGHNESS);
    float alphaSquared = alpha * alpha;
    float saturatedNdotV = saturate(nDotV);
    float saturatedNdotL = saturate(nDotL);
    float ggxV = saturatedNdotL * sqrt(
        saturatedNdotV * saturatedNdotV * (1.0f - alphaSquared)
        + alphaSquared);
    float ggxL = saturatedNdotV * sqrt(
        saturatedNdotL * saturatedNdotL * (1.0f - alphaSquared)
        + alphaSquared);
    return 0.5f / max(
        ggxV + ggxL,
        VIVID_SIMPLE_SLAB_MIN_VISIBILITY_DENOMINATOR);
}

VividSimpleSlabBSDFResponse VividEvaluateSimpleSlabAnalyticDirect(
    VividSimpleSlabData slab,
    float3 viewDirectionWS,
    float3 lightDirectionWS)
{
    VividSimpleSlabBSDFResponse response =
        VividCreateEmptySimpleSlabBSDFResponse();
    float nDotV = dot(slab.normalWS, viewDirectionWS);
    float nDotL = dot(slab.normalWS, lightDirectionWS);
    if (nDotV <= 0.0f || nDotL <= 0.0f)
        return response;

    float3 halfVector = viewDirectionWS + lightDirectionWS;
    float halfVectorLengthSquared = dot(halfVector, halfVector);
    if (halfVectorLengthSquared
        <= VIVID_SIMPLE_SLAB_MIN_HALF_VECTOR_LENGTH_SQ)
    {
        return response;
    }

    halfVector *= rsqrt(halfVectorLengthSquared);
    float nDotH = saturate(dot(slab.normalWS, halfVector));
    float vDotH = saturate(dot(viewDirectionWS, halfVector));
    float alphaRoughness = VividSimpleSlabPerceptualRoughnessToAlpha(
        slab.perceptualRoughness);
    float distribution = VividSimpleSlabDGGX(alphaRoughness, nDotH);
    float visibility = VividSimpleSlabVSmithGGXCorrelated(
        alphaRoughness,
        nDotV,
        nDotL);
    float3 fresnel = VividSimpleSlabEvaluateSchlickFresnel(
        slab.specularF0,
        vDotH);

    response.diffuse = saturate(slab.diffuseAlbedo)
        * (VIVID_SIMPLE_SLAB_INV_PI * nDotL);
    response.specular = fresnel
        * (distribution * visibility * nDotL);
    return response;
}

float3 VividCombineSimpleSlabBSDFResponse(
    VividSimpleSlabBSDFResponse response)
{
    return response.diffuse + response.specular;
}

#endif
