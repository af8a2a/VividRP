#ifndef VIVIDRP_EXPERIMENTAL_CLOSURE_INCLUDED
#define VIVIDRP_EXPERIMENTAL_CLOSURE_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GBuffer.hlsl"

// Versioned high-level semantic contract. Screen-space packing remains a
// separate ABI so material operators can evolve independently from storage.
#define VIVID_EXPERIMENTAL_CLOSURE_SEMANTIC_VERSION 2u
#define VIVID_EXPERIMENTAL_CLOSURE_MAX_COUNT 2u

#define VIVID_EXPERIMENTAL_CLOSURE_MODEL_SLAB 0u

#define VIVID_EXPERIMENTAL_CLOSURE_COMPLEXITY_FAST 0u
#define VIVID_EXPERIMENTAL_CLOSURE_COMPLEXITY_SINGLE 1u
#define VIVID_EXPERIMENTAL_CLOSURE_COMPLEXITY_COMPLEX 2u

#define VIVID_EXPERIMENTAL_CLOSURE_OPERATOR_HORIZONTAL_MIX 0u
#define VIVID_EXPERIMENTAL_CLOSURE_OPERATOR_VERTICAL_LAYER 1u

#define VIVID_EXPERIMENTAL_CLOSURE_FEATURE_COAT (1u << 0)
#define VIVID_EXPERIMENTAL_CLOSURE_FEATURE_TRANSMISSION (1u << 1)
#define VIVID_EXPERIMENTAL_CLOSURE_FEATURE_SUBSURFACE (1u << 2)

#define VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_SPECULAR_IOR (1u << 0)
#define VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_COAT_ROUGHNESS (1u << 1)
#define VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_TRANSMISSION (1u << 2)
#define VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_SUBSURFACE (1u << 3)
#define VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_MULTI_LAYER (1u << 4)

#define VIVID_EXPERIMENTAL_CLOSURE_LAYER_WEIGHT_EPSILON (0.5 / 255.0)

static const float kVividExperimentalLegacyDielectricIor = 1.5;
static const float kVividExperimentalLegacyCoatLinearRoughness = 0.01;

// Stage-independent sampled inputs. Raster, ray tracing and eventually the
// VBuffer material evaluator all resolve through this structure.
struct VividExperimentalStandardSurfaceParameters
{
    float3 baseColor;
    float3 normalWS;
    float perceptualRoughness;
    float metallic;
    float ambientOcclusion;
    float coverage;
    float specularIor;
    float clearCoatWeight;
    float clearCoatPerceptualRoughness;
    float transmissionWeight;
    float subsurfaceWeight;
    float3 emissive;
    uint materialFeatures;
    VividBuiltinData builtinData;
};

struct VividExperimentalStandardSurface
{
    float3 baseColor;
    float3 normalWS;
    float linearRoughness;
    float metallic;
    float ambientOcclusion;
    float coverage;
    float specularIor;
    float clearCoatWeight;
    float clearCoatLinearRoughness;
    float transmissionWeight;
    float subsurfaceWeight;
    float3 emissive;
    uint materialFeatures;
    VividBuiltinData builtinData;
};

struct VividExperimentalSlabClosure
{
    uint model;
    uint featureFlags;
    float3 diffuseAlbedo;
    float3 specularF0;
    float3 specularF90;
    float specularIor;
    float3 normalWS;
    float linearRoughness;
    float coverage;
    float clearCoatWeight;
    float clearCoatLinearRoughness;
    float transmissionWeight;
    float subsurfaceWeight;
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
    uint compatibilityLossFlags;
};

struct VividExperimentalClosureMaterial
{
    uint closureCount;
    uint complexity;
    VividExperimentalSlabClosure slab;
    VividExperimentalSlabClosure topSlab;
    uint layerOperator;
    float layerWeight;
    VividExperimentalMaterialSummary summary;
    VividExperimentalMaterialSummary topSummary;
    float3 emissive;
    VividBuiltinData builtinData;
};

float VividExperimentalSanitizeIor(float specularIor)
{
    if (isnan(specularIor) || isinf(specularIor))
        return kVividExperimentalLegacyDielectricIor;

    return clamp(specularIor, 1.0, 3.0);
}

float VividExperimentalIorToF0(float specularIor)
{
    float ior = VividExperimentalSanitizeIor(specularIor);
    float ratio = (ior - 1.0) / (ior + 1.0);
    return ratio * ratio;
}

VividExperimentalStandardSurface VividResolveExperimentalStandardSurface(
    VividExperimentalStandardSurfaceParameters parameters)
{
    VividExperimentalStandardSurface surface;
    surface.baseColor = max(parameters.baseColor, 0.0);
    surface.normalWS = normalize(parameters.normalWS);
    float perceptualRoughness = saturate(parameters.perceptualRoughness);
    surface.linearRoughness = perceptualRoughness * perceptualRoughness;
    surface.metallic = saturate(parameters.metallic);
    surface.ambientOcclusion = saturate(parameters.ambientOcclusion);
    surface.coverage = saturate(parameters.coverage);
    surface.specularIor = VividExperimentalSanitizeIor(parameters.specularIor);
    surface.clearCoatWeight = saturate(parameters.clearCoatWeight);
    float clearCoatPerceptualRoughness =
        saturate(parameters.clearCoatPerceptualRoughness);
    surface.clearCoatLinearRoughness = max(
        clearCoatPerceptualRoughness * clearCoatPerceptualRoughness,
        0.0001);
    surface.transmissionWeight = saturate(parameters.transmissionWeight);
    surface.subsurfaceWeight = saturate(parameters.subsurfaceWeight);
    surface.emissive = max(parameters.emissive, 0.0);
    surface.materialFeatures = parameters.materialFeatures;
    surface.builtinData = parameters.builtinData;
    return surface;
}

uint VividGetExperimentalLegacyCompatibilityLoss(
    VividExperimentalStandardSurface surface)
{
    uint flags = 0u;
    if (abs(surface.specularIor - kVividExperimentalLegacyDielectricIor) > 0.0001)
        flags |= VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_SPECULAR_IOR;

    if (surface.clearCoatWeight > 0.0
        && abs(
            surface.clearCoatLinearRoughness
            - kVividExperimentalLegacyCoatLinearRoughness) > 0.0001)
    {
        flags |= VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_COAT_ROUGHNESS;
    }

    if (surface.transmissionWeight > 0.0)
        flags |= VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_TRANSMISSION;

    if (surface.subsurfaceWeight > 0.0)
        flags |= VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_SUBSURFACE;

    return flags;
}

uint VividClassifyExperimentalClosure(
    uint closureCount,
    uint featureFlags)
{
    if (closureCount > 1u)
        return VIVID_EXPERIMENTAL_CLOSURE_COMPLEXITY_COMPLEX;

    if (featureFlags == 0u)
        return VIVID_EXPERIMENTAL_CLOSURE_COMPLEXITY_FAST;

    return VIVID_EXPERIMENTAL_CLOSURE_COMPLEXITY_SINGLE;
}

VividExperimentalClosureMaterial VividCompileExperimentalStandardSurface(
    VividExperimentalStandardSurface surface)
{
    VividExperimentalClosureMaterial material;
    material.topSlab = (VividExperimentalSlabClosure)0;
    material.topSummary = (VividExperimentalMaterialSummary)0;
    material.layerOperator =
        VIVID_EXPERIMENTAL_CLOSURE_OPERATOR_HORIZONTAL_MIX;
    material.layerWeight = 0.0;

    float metallic = saturate(surface.metallic);
    float dielectricF0 = VividExperimentalIorToF0(surface.specularIor);
    material.closureCount = 1u;
    material.slab.model = VIVID_EXPERIMENTAL_CLOSURE_MODEL_SLAB;
    material.slab.featureFlags = 0u;
    material.slab.diffuseAlbedo =
        saturate(surface.baseColor)
        * (1.0 - metallic)
        * (1.0 - surface.transmissionWeight);
    material.slab.specularF0 = lerp(
        dielectricF0.xxx,
        saturate(surface.baseColor),
        metallic);
    material.slab.specularF90 = 1.0;
    material.slab.specularIor = surface.specularIor;
    material.slab.normalWS = normalize(surface.normalWS);
    material.slab.linearRoughness = saturate(surface.linearRoughness);
    material.slab.coverage = saturate(surface.coverage);
    material.slab.clearCoatWeight = saturate(surface.clearCoatWeight);
    material.slab.clearCoatLinearRoughness =
        saturate(surface.clearCoatLinearRoughness);
    material.slab.transmissionWeight = saturate(surface.transmissionWeight);
    material.slab.subsurfaceWeight = saturate(surface.subsurfaceWeight);

    if (material.slab.clearCoatWeight > 0.0)
        material.slab.featureFlags |= VIVID_EXPERIMENTAL_CLOSURE_FEATURE_COAT;
    if (material.slab.transmissionWeight > 0.0)
        material.slab.featureFlags |= VIVID_EXPERIMENTAL_CLOSURE_FEATURE_TRANSMISSION;
    if (material.slab.subsurfaceWeight > 0.0)
        material.slab.featureFlags |= VIVID_EXPERIMENTAL_CLOSURE_FEATURE_SUBSURFACE;

    material.complexity = VividClassifyExperimentalClosure(
        material.closureCount,
        material.slab.featureFlags);

    material.summary.baseColor = max(surface.baseColor, 0.0);
    material.summary.metallic = metallic;
    material.summary.ambientOcclusion = saturate(surface.ambientOcclusion);
    material.summary.materialFeatures = surface.materialFeatures;
    material.summary.compatibilityLossFlags =
        VividGetExperimentalLegacyCompatibilityLoss(surface);
    material.emissive = max(surface.emissive, 0.0);
    material.builtinData = surface.builtinData;
    return material;
}

VividExperimentalClosureMaterial VividCompileExperimentalLayeredSurface(
    VividExperimentalStandardSurface baseSurface,
    VividExperimentalStandardSurface topSurface,
    uint layerOperator,
    float layerWeight)
{
    VividExperimentalClosureMaterial material =
        VividCompileExperimentalStandardSurface(baseSurface);
    float safeLayerWeight = saturate(layerWeight);
    if (safeLayerWeight <=
        VIVID_EXPERIMENTAL_CLOSURE_LAYER_WEIGHT_EPSILON)
    {
        return material;
    }

    VividExperimentalClosureMaterial topMaterial =
        VividCompileExperimentalStandardSurface(topSurface);
    material.topSlab = topMaterial.slab;
    material.topSummary = topMaterial.summary;

    // ABI v2 keeps the second Slab intentionally compact: it shares the base
    // normal and does not yet carry nested coat/transmission/SSS parameters.
    material.topSlab.normalWS = material.slab.normalWS;
    material.topSlab.coverage = 1.0;
    material.topSlab.clearCoatWeight = 0.0;
    material.topSlab.clearCoatLinearRoughness = 0.0001;
    material.topSlab.transmissionWeight = 0.0;
    material.topSlab.subsurfaceWeight = 0.0;
    material.topSlab.featureFlags = 0u;

    material.closureCount = 2u;
    material.layerOperator = layerOperator ==
        VIVID_EXPERIMENTAL_CLOSURE_OPERATOR_VERTICAL_LAYER
            ? VIVID_EXPERIMENTAL_CLOSURE_OPERATOR_VERTICAL_LAYER
            : VIVID_EXPERIMENTAL_CLOSURE_OPERATOR_HORIZONTAL_MIX;
    material.layerWeight = safeLayerWeight;
    material.complexity = VividClassifyExperimentalClosure(
        material.closureCount,
        material.slab.featureFlags | material.topSlab.featureFlags);
    material.summary.compatibilityLossFlags |=
        VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_MULTI_LAYER;
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
