#ifndef VIVIDRP_EXPERIMENTAL_CLOSURE_INCLUDED
#define VIVIDRP_EXPERIMENTAL_CLOSURE_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GBuffer.hlsl"

// Phase 0 contract. This is deliberately not a packed storage ABI yet: it
// separates authoring, closure evaluation, and legacy export while the
// experimental path is validated against the current deferred renderer.
#define VIVID_EXPERIMENTAL_CLOSURE_FEATURE_COAT (1u << 0)

struct VividExperimentalStandardSurface
{
    float3 baseColor;
    float3 normalWS;
    float linearRoughness;
    float metallic;
    float ambientOcclusion;
    float coverage;
    float clearCoatWeight;
    float clearCoatLinearRoughness;
    float3 emissive;
    uint materialFeatures;
    VividBuiltinData builtinData;
};

struct VividExperimentalSlabClosure
{
    float3 diffuseAlbedo;
    float3 specularF0;
    float3 specularF90;
    float3 normalWS;
    float linearRoughness;
    float coverage;
    float clearCoatWeight;
    float clearCoatLinearRoughness;
    uint featureFlags;
};

// Data required by screen-space systems that cannot consume the closure yet.
// Keeping this explicit prevents the legacy GBuffer layout from becoming the
// long-term closure representation.
struct VividExperimentalMaterialSummary
{
    float3 baseColor;
    float metallic;
    float ambientOcclusion;
    uint materialFeatures;
};

struct VividExperimentalClosureMaterial
{
    VividExperimentalSlabClosure slab;
    VividExperimentalMaterialSummary summary;
    float3 emissive;
    VividBuiltinData builtinData;
};

VividExperimentalClosureMaterial VividCompileExperimentalStandardSurface(
    VividExperimentalStandardSurface surface)
{
    VividExperimentalClosureMaterial material;

    float metallic = saturate(surface.metallic);
    material.slab.diffuseAlbedo = saturate(surface.baseColor) * (1.0 - metallic);
    material.slab.specularF0 = lerp(float3(0.04, 0.04, 0.04), saturate(surface.baseColor), metallic);
    material.slab.specularF90 = 1.0;
    material.slab.normalWS = normalize(surface.normalWS);
    material.slab.linearRoughness = saturate(surface.linearRoughness);
    material.slab.coverage = saturate(surface.coverage);
    material.slab.clearCoatWeight = saturate(surface.clearCoatWeight);
    material.slab.clearCoatLinearRoughness = saturate(surface.clearCoatLinearRoughness);
    material.slab.featureFlags = material.slab.clearCoatWeight > 0.0
        ? VIVID_EXPERIMENTAL_CLOSURE_FEATURE_COAT
        : 0u;

    material.summary.baseColor = max(surface.baseColor, 0.0);
    material.summary.metallic = metallic;
    material.summary.ambientOcclusion = saturate(surface.ambientOcclusion);
    material.summary.materialFeatures = surface.materialFeatures;
    material.emissive = max(surface.emissive, 0.0);
    material.builtinData = surface.builtinData;
    return material;
}

VividGBufferSurfaceData VividExportExperimentalClosureToLegacyGBuffer(
    VividExperimentalClosureMaterial material)
{
    VividGBufferSurfaceData surfaceData;
    surfaceData.baseColor = material.summary.baseColor;
    surfaceData.normalWS = material.slab.normalWS;
    surfaceData.linearRoughness = material.slab.linearRoughness;
    surfaceData.metallic = material.summary.metallic;
    surfaceData.ambientOcclusion = material.summary.ambientOcclusion;
    surfaceData.customData = material.slab.clearCoatWeight;
    surfaceData.customData1 = 0.0;
    surfaceData.materialFeatures = material.summary.materialFeatures;
    surfaceData.emissive = material.emissive;
    surfaceData.builtinData = material.builtinData;
    return surfaceData;
}

#endif
