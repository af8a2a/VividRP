#ifndef VIVIDRP_EXPERIMENTAL_STANDARD_LIT_OPENPBR_ADAPTER_INCLUDED
#define VIVIDRP_EXPERIMENTAL_STANDARD_LIT_OPENPBR_ADAPTER_INCLUDED

// Preserve the mature OpenPBR sampling implementation, then wrap its resolved
// surface in the same StandardSurface -> Closure conversion used by raster.
#define VividReferencedPathtracingResolveStandardLitOpenPBR \
    VividReferencedPathtracingResolveLegacyStandardLitOpenPBR
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/StandardLitOpenPBRAdapter.hlsl"
#undef VividReferencedPathtracingResolveStandardLitOpenPBR

#include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosure.hlsl"

VividExperimentalClosureMaterial VividCompileExperimentalOpenPBRMaterial(
    VividReferencedPathtracingMaterial material)
{
    OpenPBR_ResolvedInputs inputs = material.openPbrInputs;

    VividExperimentalStandardSurfaceParameters parameters;
    parameters.baseColor = inputs.base_color;
    parameters.normalWS = material.shadingNormalWS;
    parameters.perceptualRoughness = inputs.specular_roughness;
    parameters.metallic = inputs.base_metalness;
    parameters.ambientOcclusion = 1.0;
    parameters.coverage = inputs.geometry_opacity;
    parameters.specularIor = inputs.specular_ior;
    parameters.clearCoatWeight = inputs.coat_weight;
    parameters.clearCoatPerceptualRoughness = inputs.coat_roughness;
    parameters.transmissionWeight = material.effectiveTransmissionWeight;
    parameters.subsurfaceWeight = material.effectiveSubsurfaceWeight;
    parameters.emissive = material.emission;
    parameters.materialFeatures = VIVID_MATERIALFEATURE_LIT;
    if (inputs.coat_weight > 0.0)
        parameters.materialFeatures |= VIVID_MATERIALFEATURE_CLEAR_COAT;
    parameters.builtinData = InitVividBuiltinData();

    VividExperimentalStandardSurface surface =
        VividResolveExperimentalStandardSurface(parameters);
    return VividCompileExperimentalStandardSurface(surface);
}

void VividApplyExperimentalClosureToOpenPBRMaterial(
    VividExperimentalClosureMaterial closureMaterial,
    inout VividReferencedPathtracingMaterial material)
{
    float perceptualRoughness = sqrt(
        max(closureMaterial.slab.linearRoughness, 0.0));
    float clearCoatPerceptualRoughness = sqrt(
        max(closureMaterial.slab.clearCoatLinearRoughness, 0.0));

    material.openPbrInputs.base_color = closureMaterial.summary.baseColor;
    material.openPbrInputs.base_metalness = closureMaterial.summary.metallic;
    material.openPbrInputs.base_diffuse_roughness = perceptualRoughness;
    material.openPbrInputs.specular_roughness = perceptualRoughness;
    material.openPbrInputs.geometry_opacity = closureMaterial.slab.coverage;
    material.openPbrInputs.coat_weight = closureMaterial.slab.clearCoatWeight;
    material.openPbrInputs.coat_roughness = clearCoatPerceptualRoughness;
    material.emission = closureMaterial.emissive;
    material.openPbrInputs.emission_color = closureMaterial.emissive;
    material.openPbrInputs.emission_luminance =
        any(closureMaterial.emissive > 0.0) ? 1.0 : 0.0;
}

VividReferencedPathtracingMaterial VividReferencedPathtracingResolveStandardLitOpenPBR(
#if defined(VIVIDRP_REFERENCED_PATH_TRACING_USE_RTXTF)
    inout STF_SamplerState rtxtfSamplerState,
#endif
    VividIndirectDiffuseHitGeometry geometry,
    float textureBaseLambda,
    float baseTextureLod,
    float normalTextureLod,
    float3 viewDirectionWS)
{
    VividReferencedPathtracingMaterial material =
        VividReferencedPathtracingResolveLegacyStandardLitOpenPBR(
#if defined(VIVIDRP_REFERENCED_PATH_TRACING_USE_RTXTF)
            rtxtfSamplerState,
#endif
            geometry,
            textureBaseLambda,
            baseTextureLod,
            normalTextureLod,
            viewDirectionWS);

    VividExperimentalClosureMaterial closureMaterial =
        VividCompileExperimentalOpenPBRMaterial(material);
    VividApplyExperimentalClosureToOpenPBRMaterial(
        closureMaterial,
        material);
    return material;
}

#endif
