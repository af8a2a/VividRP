#ifndef VIVIDRP_STANDARD_LIT_TO_OPENPBR_INCLUDED
#define VIVIDRP_STANDARD_LIT_TO_OPENPBR_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/OpenPBR/OpenPBR.hlsl"
#if !defined(VIVIDRP_STANDARD_LIT_TO_OPENPBR_CORE_ONLY)
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividOpenPBRV2.hlsl"
#endif

// Single source of truth for StandardLit -> OpenPBR 1.1 parameter conversion.
// Shared between Referenced Path Tracing (StandardLitOpenPBRAdapter.hlsl) and
// Raster / GPUDriven / Substrate Resolve so both pipelines stay numerically locked.
#define VIVID_STANDARD_LIT_TO_OPENPBR_VERSION 1u
#define VIVID_STANDARD_LIT_MIN_ROUGHNESS 0.001f
#define VIVID_STANDARD_LIT_MIN_SPECULAR_IOR 1.0f
#define VIVID_STANDARD_LIT_MAX_SPECULAR_IOR 3.0f

struct VividStandardLitSurfaceParams
{
    float3 baseColor;
    float metallic;
    float smoothness;
    float specularIor;
    float clearCoatMask;
    float clearCoatSmoothness;
    float3 normalWS;
    float3 coatNormalWS;
    uint hasIndependentCoatNormal;
    float3 tangentWS;
    float tangentHandedness;
    float3 emission;
    float geometryOpacity;
    bool geometryThinWalled;
};

VividStandardLitSurfaceParams VividCreateDefaultStandardLitSurfaceParams()
{
    VividStandardLitSurfaceParams p;
    p.baseColor = 0.5f.xxx;
    p.metallic = 0.0f;
    p.smoothness = 0.5f;
    p.specularIor = 1.5f;
    p.clearCoatMask = 0.0f;
    p.clearCoatSmoothness = 0.0f;
    p.normalWS = float3(0.0f, 1.0f, 0.0f);
    p.coatNormalWS = float3(0.0f, 1.0f, 0.0f);
    p.hasIndependentCoatNormal = 0u;
    p.tangentWS = float3(1.0f, 0.0f, 0.0f);
    p.tangentHandedness = 1.0f;
    p.emission = 0.0f.xxx;
    p.geometryOpacity = 1.0f;
    p.geometryThinWalled = false;
    return p;
}

float VividSanitizeStandardLitSpecularIor(float rawIor)
{
    if (isnan(rawIor) || isinf(rawIor))
        rawIor = 1.5f;
    return clamp(
        rawIor,
        VIVID_STANDARD_LIT_MIN_SPECULAR_IOR,
        VIVID_STANDARD_LIT_MAX_SPECULAR_IOR);
}

// Applies the canonical v1 StandardLit -> OpenPBR surface mapping directly onto
// an OpenPBR_ResolvedInputs struct (used by Referenced Path Tracing & ground truth).
void VividStandardLitApplyCoreToOpenPBRResolved(
    VividStandardLitSurfaceParams params,
    inout OpenPBR_ResolvedInputs inputs)
{
    float roughness = saturate(1.0f - saturate(params.smoothness));
    inputs.base_weight = 1.0f;
    inputs.base_color = saturate(params.baseColor);
    inputs.base_metalness = saturate(params.metallic);
    inputs.base_diffuse_roughness = roughness;
    inputs.specular_weight = 1.0f;
    inputs.specular_color = 1.0f.xxx;
    inputs.specular_roughness = max(roughness, VIVID_STANDARD_LIT_MIN_ROUGHNESS);
    inputs.specular_ior = VividSanitizeStandardLitSpecularIor(params.specularIor);
    inputs.geometry_opacity = saturate(params.geometryOpacity);
    inputs.geometry_thin_walled = params.geometryThinWalled;

    float coatWeight = saturate(params.clearCoatMask);
    inputs.coat_weight = coatWeight;
    if (coatWeight > 0.0f)
    {
        inputs.coat_roughness = max(
            saturate(1.0f - saturate(params.clearCoatSmoothness)),
            VIVID_STANDARD_LIT_MIN_ROUGHNESS);
        inputs.coat_color = 1.0f.xxx;
        inputs.coat_ior = 1.5f;
        inputs.coat_darkening = 1.0f;
    }

    float3 cleanEmission = max(params.emission, 0.0f);
    inputs.emission_luminance = any(cleanEmission > 0.0f) ? 1.0f : 0.0f;
    inputs.emission_color = cleanEmission;
}

#if !defined(VIVIDRP_STANDARD_LIT_TO_OPENPBR_CORE_ONLY)
// Converts StandardLit parameters into VividOpenPBRV2Inputs for Substrate Closure
// Buffer packing and Deferred Tier 1/2/3 evaluation.
VividOpenPBRV2Inputs VividStandardLitToOpenPBRV2Inputs(VividStandardLitSurfaceParams params)
{
    VividOpenPBRV2Inputs v2 = VividCreateDefaultOpenPBRV2Inputs();
    float roughness = saturate(1.0f - saturate(params.smoothness));

    v2.core.baseWeight = 1.0f;
    v2.core.baseColor = saturate(params.baseColor);
    v2.core.baseDiffuseRoughness = roughness;
    v2.core.baseMetalness = saturate(params.metallic);
    v2.core.specularWeight = 1.0f;
    v2.core.specularColor = 1.0f.xxx;
    v2.core.specularRoughness = max(roughness, VIVID_STANDARD_LIT_MIN_ROUGHNESS);
    v2.core.specularIor = VividSanitizeStandardLitSpecularIor(params.specularIor);
    v2.core.normalWS = VividSanitizeSurfaceSummaryNormal(params.normalWS);

    float3 cleanEmission = max(params.emission, 0.0f);
    v2.core.emissionLuminance = any(cleanEmission > 0.0f) ? 1.0f : 0.0f;
    v2.core.emissionColor = cleanEmission;

    v2.coatWeight = saturate(params.clearCoatMask);
    v2.coatRoughness = max(
        saturate(1.0f - saturate(params.clearCoatSmoothness)),
        VIVID_STANDARD_LIT_MIN_ROUGHNESS);
    v2.coatColor = 1.0f.xxx;
    v2.coatIor = 1.5f;
    v2.coatDarkening = 1.0f;
    v2.hasIndependentCoatNormal = (v2.coatWeight > 0.0f && params.hasIndependentCoatNormal != 0u) ? 1u : 0u;
    v2.coatNormalWS = v2.hasIndependentCoatNormal != 0u
        ? VividSanitizeSurfaceSummaryNormal(params.coatNormalWS)
        : v2.core.normalWS;

    v2.tangentWS = params.tangentWS;
    v2.tangentHandedness = params.tangentHandedness < 0.0f ? -1.0f : 1.0f;
    return v2;
}
#endif

#endif // VIVIDRP_STANDARD_LIT_TO_OPENPBR_INCLUDED
