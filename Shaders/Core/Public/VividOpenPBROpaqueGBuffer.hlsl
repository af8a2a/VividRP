#ifndef VIVIDRP_OPENPBR_OPAQUE_GBUFFER_INCLUDED
#define VIVIDRP_OPENPBR_OPAQUE_GBUFFER_INCLUDED

#include "SurfaceSummaryGBuffer.hlsl"
#include "VividOpenPBROpaqueContract.hlsl"

// Native Deferred storage v1. The core summary remains usable by normal,
// roughness and albedo consumers. It is NEVER inverted to reconstruct a BSDF.
// LayerAux0 (sRGB8): baseColor.rgb, baseWeight.
// LayerAux1 (UNorm8): specularColor.rgb, specularWeight.
// RT4 (R11G11B10): diffuseRoughness, metalness, absolute IOR.
// RT1 retains normal/roughness; RT3 retains emissionLuminance * emissionColor.
// Class 6 owns RT4, so HAS_DIFFUSE_IRRADIANCE must stay clear. Native GI is
// evaluated in Deferred. Zero base/specular weight is valid, not an empty slot.
#define VIVID_OPENPBR_OPAQUE_GBUFFER_VERSION 1u

bool VividCanStoreOpenPBROpaqueGBuffer(VividOpenPBROpaqueInputs inputs)
{
    // Reject finite input emission that would overflow the existing HDR target.
    return VividValidateOpenPBROpaqueInputs(inputs, 0u) == 0u
        && all(inputs.emissionLuminance * inputs.emissionColor <= float3(65024.0, 65024.0, 64512.0));
}

VividSurfaceSummaryData VividBuildOpenPBROpaqueSummary(VividOpenPBROpaqueInputs inputs)
{
    VividSurfaceSummaryData summary = (VividSurfaceSummaryData)0;
    summary.normalWS = inputs.normalWS;
    summary.perceptualRoughness = inputs.specularRoughness;
    summary.diffuseAlbedo = inputs.baseWeight * inputs.baseColor * (1.0 - inputs.baseMetalness);
    float f0 = (inputs.specularIor - 1.0) / (inputs.specularIor + 1.0);
    summary.specularF0 = lerp(f0 * f0 * inputs.specularWeight * inputs.specularColor,
        inputs.baseWeight * inputs.baseColor, inputs.baseMetalness);
    summary.ambientOcclusion = 1.0;
    // Native v1 has no decal or SSR export policy. Do not silently opt it in.
    summary.deferredExportHeader = VIVID_DEFERRED_EXPORT_CLASS_OPENPBR_OPAQUE;
    summary.emissive = inputs.emissionLuminance * inputs.emissionColor;
    return summary;
}

VividSurfaceSummaryGBufferOutput VividPackOpenPBROpaqueGBuffer(VividOpenPBROpaqueInputs inputs)
{
    VividSurfaceSummaryGBufferOutput output = VividPackSurfaceSummaryGBuffer(
        VividBuildOpenPBROpaqueSummary(inputs));
    output.rt4 = float4(inputs.baseDiffuseRoughness, inputs.baseMetalness, inputs.specularIor, 0.0);
    return output;
}

VividDualSlabLayerSidecarOutput VividPackOpenPBROpaqueSidecar(VividOpenPBROpaqueInputs inputs)
{
    VividDualSlabLayerSidecarOutput output;
    output.rt0 = float4(inputs.baseColor, inputs.baseWeight);
    output.rt1 = float4(inputs.specularColor, inputs.specularWeight);
    return output;
}

bool VividTryUnpackOpenPBROpaqueGBuffer(VividSurfaceSummaryData summary,
    float4 nativeParameters, float4 aux0, float4 aux1, out VividOpenPBROpaqueInputs inputs)
{
    inputs = (VividOpenPBROpaqueInputs)0;
    inputs.baseColor = aux0.rgb;
    inputs.baseWeight = aux0.a;
    inputs.specularColor = aux1.rgb;
    inputs.specularWeight = aux1.a;
    inputs.baseDiffuseRoughness = nativeParameters.x;
    inputs.baseMetalness = nativeParameters.y;
    inputs.specularIor = nativeParameters.z;
    inputs.specularRoughness = summary.perceptualRoughness;
    inputs.normalWS = summary.normalWS;
    // Emission is already integrated into RT3 and composed once by the host.
    inputs.emissionLuminance = 0.0;
    inputs.emissionColor = 1.0;
    return summary.deferredExportHeader == VIVID_DEFERRED_EXPORT_CLASS_OPENPBR_OPAQUE
        && VividValidateOpenPBROpaqueInputs(inputs, 0u) == 0u;
}

#endif
