#ifndef VIVIDRP_EXPERIMENTAL_CLOSURE_BUFFER_INCLUDED
#define VIVIDRP_EXPERIMENTAL_CLOSURE_BUFFER_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosure.hlsl"

// Stage 2 screen-space ABI. This is deliberately versioned separately from
// the semantic contract so packing can change without redefining Closure.
#define VIVID_EXPERIMENTAL_CLOSURE_BUFFER_VERSION 1u
#define VIVID_EXPERIMENTAL_CLOSURE_BUFFER_ATTACHMENT_COUNT 6u
#define VIVID_EXPERIMENTAL_CLOSURE_BUFFER_BYTES_PER_PIXEL 28u

#define VIVID_EXPERIMENTAL_CLOSURE_HEADER_COMPLEXITY_SHIFT 0u
#define VIVID_EXPERIMENTAL_CLOSURE_HEADER_COMPLEXITY_MASK 3u
#define VIVID_EXPERIMENTAL_CLOSURE_HEADER_FEATURE_SHIFT 2u
#define VIVID_EXPERIMENTAL_CLOSURE_HEADER_FEATURE_MASK 7u
#define VIVID_EXPERIMENTAL_CLOSURE_HEADER_MODEL_SHIFT 5u
#define VIVID_EXPERIMENTAL_CLOSURE_HEADER_MODEL_MASK 3u
#define VIVID_EXPERIMENTAL_CLOSURE_HEADER_VALID_BIT (1u << 7)

// RT0 RGBA8_SRGB            : baseColor.rgb + Closure header.a
// RT1 A2B10G10R10_UNORM     : oct normal.xy + linear roughness.b + coverage.a
// RT2 RGBA8_UNORM           : metallic.r + AO.g + coat weight.b + coat roughness.a
// RT3 RGBA8_UNORM           : encoded IOR.r + transmission.g + subsurface.b + compatibility loss.a
// RT4 B10G11R11_UFLOAT      : emissive.rgb
// RT5 RGBA16_SFLOAT         : baked diffuse lighting.rgb + has baked GI.a
struct VividExperimentalClosureBufferOutput
{
    float4 rt0 : SV_Target0;
    float4 rt1 : SV_Target1;
    float4 rt2 : SV_Target2;
    float4 rt3 : SV_Target3;
    float4 rt4 : SV_Target4;
    float4 rt5 : SV_Target5;
};

uint VividPackExperimentalClosureHeader(
    uint model,
    uint complexity,
    uint featureFlags)
{
    uint header = VIVID_EXPERIMENTAL_CLOSURE_HEADER_VALID_BIT;
    header |=
        (complexity & VIVID_EXPERIMENTAL_CLOSURE_HEADER_COMPLEXITY_MASK)
        << VIVID_EXPERIMENTAL_CLOSURE_HEADER_COMPLEXITY_SHIFT;
    header |=
        (featureFlags & VIVID_EXPERIMENTAL_CLOSURE_HEADER_FEATURE_MASK)
        << VIVID_EXPERIMENTAL_CLOSURE_HEADER_FEATURE_SHIFT;
    header |=
        (model & VIVID_EXPERIMENTAL_CLOSURE_HEADER_MODEL_MASK)
        << VIVID_EXPERIMENTAL_CLOSURE_HEADER_MODEL_SHIFT;
    return header;
}

uint VividDecodeExperimentalClosureHeader(float encodedHeader)
{
    return (uint)round(saturate(encodedHeader) * 255.0);
}

bool VividIsExperimentalClosureHeaderValid(uint header)
{
    return (header & VIVID_EXPERIMENTAL_CLOSURE_HEADER_VALID_BIT) != 0u;
}

uint VividGetExperimentalClosureHeaderComplexity(uint header)
{
    return
        (header >> VIVID_EXPERIMENTAL_CLOSURE_HEADER_COMPLEXITY_SHIFT)
        & VIVID_EXPERIMENTAL_CLOSURE_HEADER_COMPLEXITY_MASK;
}

uint VividGetExperimentalClosureHeaderFeatures(uint header)
{
    return
        (header >> VIVID_EXPERIMENTAL_CLOSURE_HEADER_FEATURE_SHIFT)
        & VIVID_EXPERIMENTAL_CLOSURE_HEADER_FEATURE_MASK;
}

uint VividGetExperimentalClosureHeaderModel(uint header)
{
    return
        (header >> VIVID_EXPERIMENTAL_CLOSURE_HEADER_MODEL_SHIFT)
        & VIVID_EXPERIMENTAL_CLOSURE_HEADER_MODEL_MASK;
}

float VividEncodeExperimentalClosureIor(float specularIor)
{
    return (VividExperimentalSanitizeIor(specularIor) - 1.0) * 0.5;
}

float VividDecodeExperimentalClosureIor(float encodedIor)
{
    return lerp(1.0, 3.0, saturate(encodedIor));
}

VividExperimentalClosureBufferOutput VividPackExperimentalClosureBuffer(
    VividExperimentalStandardSurface surface,
    VividExperimentalClosureMaterial material)
{
    uint header = VividPackExperimentalClosureHeader(
        material.slab.model,
        material.complexity,
        material.slab.featureFlags);

    VividExperimentalClosureBufferOutput output;
    output.rt0 = float4(
        saturate(material.summary.baseColor),
        header * (1.0 / 255.0));
    output.rt1 = float4(
        EncodeVividNormalOct(material.slab.normalWS),
        saturate(material.slab.linearRoughness),
        saturate(material.slab.coverage));
    output.rt2 = float4(
        saturate(material.summary.metallic),
        saturate(material.summary.ambientOcclusion),
        saturate(material.slab.clearCoatWeight),
        saturate(material.slab.clearCoatLinearRoughness));
    output.rt3 = float4(
        VividEncodeExperimentalClosureIor(surface.specularIor),
        saturate(material.slab.transmissionWeight),
        saturate(material.slab.subsurfaceWeight),
        (material.summary.compatibilityLossFlags & 15u) * (1.0 / 15.0));
    output.rt4 = float4(max(material.emissive, 0.0), 0.0);
    output.rt5 = float4(
        max(material.builtinData.bakeDiffuseLighting, 0.0),
        saturate(material.builtinData.hasBakedGI));
    return output;
}

VividExperimentalClosureMaterial VividUnpackExperimentalClosureBuffer(
    float4 rt0,
    float4 rt1,
    float4 rt2,
    float4 rt3,
    float4 rt4,
    float4 rt5)
{
    uint header = VividDecodeExperimentalClosureHeader(rt0.a);
    float3 baseColor = saturate(rt0.rgb);
    float metallic = saturate(rt2.r);
    float specularIor = VividDecodeExperimentalClosureIor(rt3.r);
    float transmissionWeight = saturate(rt3.g);

    VividExperimentalClosureMaterial material;
    material.closureCount = VividIsExperimentalClosureHeaderValid(header) ? 1u : 0u;
    material.complexity = VividGetExperimentalClosureHeaderComplexity(header);
    material.slab.model = VividGetExperimentalClosureHeaderModel(header);
    material.slab.featureFlags = VividGetExperimentalClosureHeaderFeatures(header);
    material.slab.diffuseAlbedo =
        baseColor * (1.0 - metallic) * (1.0 - transmissionWeight);
    float dielectricF0 = VividExperimentalIorToF0(specularIor);
    material.slab.specularF0 = lerp(dielectricF0.xxx, baseColor, metallic);
    material.slab.specularF90 = 1.0;
    material.slab.normalWS = DecodeVividNormalOct(rt1.xy);
    material.slab.linearRoughness = saturate(rt1.z);
    material.slab.coverage = saturate(rt1.a);
    material.slab.clearCoatWeight = saturate(rt2.b);
    material.slab.clearCoatLinearRoughness = saturate(rt2.a);
    material.slab.transmissionWeight = transmissionWeight;
    material.slab.subsurfaceWeight = saturate(rt3.b);
    material.summary.baseColor = baseColor;
    material.summary.metallic = metallic;
    material.summary.ambientOcclusion = saturate(rt2.g);
    material.summary.materialFeatures = VIVID_MATERIALFEATURE_LIT;
    material.summary.compatibilityLossFlags =
        (uint)round(saturate(rt3.a) * 15.0);
    material.emissive = max(rt4.rgb, 0.0);
    material.builtinData = InitVividBuiltinData();
    material.builtinData.bakeDiffuseLighting = max(rt5.rgb, 0.0);
    material.builtinData.hasBakedGI = saturate(rt5.a);
    return material;
}

#endif
