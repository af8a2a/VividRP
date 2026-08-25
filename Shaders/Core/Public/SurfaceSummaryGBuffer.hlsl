#ifndef VIVIDRP_SURFACE_SUMMARY_GBUFFER_INCLUDED
#define VIVIDRP_SURFACE_SUMMARY_GBUFFER_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

#define VIVID_SURFACE_SUMMARY_GBUFFER_ABI_VERSION 1u
#define VIVID_SURFACE_SUMMARY_GBUFFER_ABI_TAG 3u
#define VIVID_SURFACE_SUMMARY_GBUFFER_ABI_TAG_MASK 0x3u
#define VIVID_DUAL_SLAB_LAYER_SIDECAR_ABI_VERSION 1u
#define VIVID_DUAL_SLAB_LAYER_SIDECAR_MIN_WEIGHT (0.5f / 255.0f)

#define VIVID_DEFERRED_EXPORT_CLASS_EMPTY 0u
#define VIVID_DEFERRED_EXPORT_CLASS_UNLIT 1u
#define VIVID_DEFERRED_EXPORT_CLASS_FAST_SLAB 2u
#define VIVID_DEFERRED_EXPORT_CLASS_GENERAL_SLAB 3u
#define VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB 4u
#define VIVID_DEFERRED_EXPORT_CLASS_SUBSURFACE 5u
#define VIVID_DEFERRED_EXPORT_CLASS_CATCH_ALL 14u
#define VIVID_DEFERRED_EXPORT_CLASS_ERROR 15u

// Compact tile-classification mask. These bits are deliberately independent
// from the encoded DeferredExportClass values above.
#define VIVID_DEFERRED_CLASS_BIT_FAST_SLAB (1u << 0)
#define VIVID_DEFERRED_CLASS_BIT_GENERAL_SLAB (1u << 1)
#define VIVID_DEFERRED_CLASS_BIT_DUAL_SLAB (1u << 2)
#define VIVID_DEFERRED_CLASS_BIT_CATCH_ALL (1u << 3)

#define VIVID_DEFERRED_EXPORT_CLASS_MASK 0x0Fu
#define VIVID_DEFERRED_EXPORT_FLAG_VERTICAL_LAYER (1u << 4)
#define VIVID_DEFERRED_EXPORT_FLAG_HAS_DIFFUSE_IRRADIANCE (1u << 5)
#define VIVID_DEFERRED_EXPORT_FLAG_RECEIVE_SSR (1u << 6)
#define VIVID_DEFERRED_EXPORT_FLAG_RECEIVE_DECALS (1u << 7)
#define VIVID_DEFERRED_EXPORT_HEADER_MASK 0xFFu

// Surface Summary GBuffer ABI:
// RT0 (R8G8B8A8_SRGB)       : DiffuseAlbedo.rgb + DeferredExportHeader.a
// RT1 (A2B10G10R10_UNORM)   : Octahedral Normal.xy + PerceptualRoughness.z + ABI tag.a
// RT2 (R8G8B8A8_UNORM)      : SqrtEncodedSpecularF0.rgb + AmbientOcclusion.a
// RT3 (B10G11R11_UFLOAT)    : Emissive.rgb
// RT4 / DiffuseIrradiance   : DiffuseIrradiance.rgb
// Optional Dual Slab sidecar:
// Logical RT5 / LayerAux0 (R8G8B8A8_SRGB) : TopDiffuseAlbedo.rgb + LayerWeight.a
// Logical RT6 / LayerAux1 (R8G8B8A8_UNORM): SqrtEncodedTopSpecularF0.rgb + TopPerceptualRoughness.a
// The sidecar is emitted by a separate two-MRT draw as SV_Target0/1 so the
// core draw can retain VT feedback UAV slots u5-u7.
// The top slab shares the core surface normal. The vertical-layer operator is
// encoded by VIVID_DEFERRED_EXPORT_FLAG_VERTICAL_LAYER in the core header.

struct VividSurfaceSummaryData
{
    float3 diffuseAlbedo;
    float3 normalWS;
    float perceptualRoughness;
    float3 specularF0;
    float ambientOcclusion;
    uint deferredExportHeader;
    float3 emissive;
    float3 diffuseIrradiance;
};

struct VividSurfaceSummaryGBufferOutput
{
    float4 rt0 : SV_Target0;
    float4 rt1 : SV_Target1;
    float4 rt2 : SV_Target2;
    float4 rt3 : SV_Target3;
    float4 rt4 : SV_Target4;
};

struct VividDualSlabLayerData
{
    float3 diffuseAlbedo;
    float3 specularF0;
    float perceptualRoughness;
    float layerWeight;
};

struct VividDualSlabLayerSidecarOutput
{
    float4 rt0 : SV_Target0;
    float4 rt1 : SV_Target1;
};

bool VividIsDeferredExportClassValid(uint exportClass)
{
    return exportClass <= VIVID_DEFERRED_EXPORT_CLASS_SUBSURFACE
        || exportClass == VIVID_DEFERRED_EXPORT_CLASS_CATCH_ALL
        || exportClass == VIVID_DEFERRED_EXPORT_CLASS_ERROR;
}

uint VividGetDeferredExportClass(uint header)
{
    return header & VIVID_DEFERRED_EXPORT_CLASS_MASK;
}

bool VividHasDeferredExportFlag(uint header, uint flag)
{
    return (header & flag) == flag;
}

uint VividSanitizeDeferredExportHeader(uint header)
{
    header &= VIVID_DEFERRED_EXPORT_HEADER_MASK;
    uint exportClass = VividGetDeferredExportClass(header);

    if (!VividIsDeferredExportClassValid(exportClass))
        return VIVID_DEFERRED_EXPORT_CLASS_ERROR;

    if (exportClass == VIVID_DEFERRED_EXPORT_CLASS_EMPTY)
        return 0u;

    if (exportClass != VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB)
        header &= ~VIVID_DEFERRED_EXPORT_FLAG_VERTICAL_LAYER;

    return header;
}

uint VividBuildDeferredExportHeader(
    uint exportClass,
    bool verticalLayer,
    bool hasDiffuseIrradiance,
    bool receiveSSR,
    bool receiveDecals)
{
    if (!VividIsDeferredExportClassValid(exportClass))
        return VIVID_DEFERRED_EXPORT_CLASS_ERROR;

    uint header = exportClass;
    header |= verticalLayer
        ? VIVID_DEFERRED_EXPORT_FLAG_VERTICAL_LAYER
        : 0u;
    header |= hasDiffuseIrradiance
        ? VIVID_DEFERRED_EXPORT_FLAG_HAS_DIFFUSE_IRRADIANCE
        : 0u;
    header |= receiveSSR
        ? VIVID_DEFERRED_EXPORT_FLAG_RECEIVE_SSR
        : 0u;
    header |= receiveDecals
        ? VIVID_DEFERRED_EXPORT_FLAG_RECEIVE_DECALS
        : 0u;
    return VividSanitizeDeferredExportHeader(header);
}

float VividEncodeDeferredExportHeader(uint header)
{
    return VividSanitizeDeferredExportHeader(header) * (1.0 / 255.0);
}

uint VividDecodeDeferredExportHeader(float encodedHeader)
{
    uint header = (uint)min(round(saturate(encodedHeader) * 255.0), 255.0);
    return VividSanitizeDeferredExportHeader(header);
}

float VividEncodeSurfaceSummaryGBufferABITag()
{
    return (VIVID_SURFACE_SUMMARY_GBUFFER_ABI_TAG
        & VIVID_SURFACE_SUMMARY_GBUFFER_ABI_TAG_MASK) * (1.0 / 3.0);
}

uint VividDecodeSurfaceSummaryGBufferABITag(float encodedTag)
{
    return (uint)min(round(saturate(encodedTag) * 3.0), 3.0);
}

bool VividIsSurfaceSummaryGBufferABIValid(float encodedTag)
{
    return VividDecodeSurfaceSummaryGBufferABITag(encodedTag)
        == VIVID_SURFACE_SUMMARY_GBUFFER_ABI_TAG;
}

float3 VividSanitizeSurfaceSummaryNormal(float3 normalWS)
{
    float lengthSquared = dot(normalWS, normalWS);
    return lengthSquared > 1e-8
        ? normalWS * rsqrt(lengthSquared)
        : float3(0.0, 0.0, 1.0);
}

float2 VividEncodeSurfaceSummaryNormal(float3 normalWS)
{
    normalWS = VividSanitizeSurfaceSummaryNormal(normalWS);
    return PackNormalOctQuadEncode(normalWS) * 0.5 + 0.5;
}

float3 VividDecodeSurfaceSummaryNormal(float2 encodedNormal)
{
    float2 octahedralNormal = saturate(encodedNormal) * 2.0 - 1.0;
    return VividSanitizeSurfaceSummaryNormal(
        UnpackNormalOctQuadEncode(octahedralNormal));
}

float3 VividEncodeSurfaceSummarySpecularF0(float3 specularF0)
{
    return sqrt(saturate(specularF0));
}

float3 VividDecodeSurfaceSummarySpecularF0(float3 encodedSpecularF0)
{
    encodedSpecularF0 = saturate(encodedSpecularF0);
    return encodedSpecularF0 * encodedSpecularF0;
}

VividDualSlabLayerData VividSanitizeDualSlabLayerData(
    VividDualSlabLayerData layerData)
{
    layerData.diffuseAlbedo = saturate(layerData.diffuseAlbedo);
    layerData.specularF0 = saturate(layerData.specularF0);
    layerData.perceptualRoughness = saturate(layerData.perceptualRoughness);
    layerData.layerWeight = saturate(layerData.layerWeight);
    return layerData;
}

float4 VividPackDualSlabLayerAux0(VividDualSlabLayerData layerData)
{
    layerData = VividSanitizeDualSlabLayerData(layerData);
    return float4(layerData.diffuseAlbedo, layerData.layerWeight);
}

float4 VividPackDualSlabLayerAux1(VividDualSlabLayerData layerData)
{
    layerData = VividSanitizeDualSlabLayerData(layerData);
    return float4(
        VividEncodeSurfaceSummarySpecularF0(layerData.specularF0),
        layerData.perceptualRoughness);
}

VividDualSlabLayerData VividUnpackDualSlabLayerSidecar(
    float4 layerAux0,
    float4 layerAux1)
{
    VividDualSlabLayerData layerData;
    layerData.diffuseAlbedo = saturate(layerAux0.rgb);
    layerData.layerWeight = saturate(layerAux0.a);
    layerData.specularF0 = VividDecodeSurfaceSummarySpecularF0(
        layerAux1.rgb);
    layerData.perceptualRoughness = saturate(layerAux1.a);
    return layerData;
}

bool VividIsDualSlabLayerSidecarValid(float4 layerAux0)
{
    // ABI v1 reserves quantized alpha zero as the missing/invalid sentinel.
    // Resolve exports Dual only above half an R8_UNorm LSB.
    return layerAux0.a >= VIVID_DUAL_SLAB_LAYER_SIDECAR_MIN_WEIGHT;
}

float4 VividPackSurfaceSummaryDiffuseIrradiance(float3 diffuseIrradiance)
{
    return float4(max(diffuseIrradiance, 0.0), 0.0);
}

float3 VividUnpackSurfaceSummaryDiffuseIrradiance(float4 packedIrradiance)
{
    return max(packedIrradiance.rgb, 0.0);
}

VividSurfaceSummaryData VividSanitizeSurfaceSummaryData(
    VividSurfaceSummaryData surfaceData)
{
    surfaceData.diffuseAlbedo = saturate(surfaceData.diffuseAlbedo);
    surfaceData.normalWS = VividSanitizeSurfaceSummaryNormal(surfaceData.normalWS);
    surfaceData.perceptualRoughness = saturate(surfaceData.perceptualRoughness);
    surfaceData.specularF0 = saturate(surfaceData.specularF0);
    surfaceData.ambientOcclusion = saturate(surfaceData.ambientOcclusion);
    surfaceData.deferredExportHeader = VividSanitizeDeferredExportHeader(
        surfaceData.deferredExportHeader);
    surfaceData.emissive = max(surfaceData.emissive, 0.0);
    surfaceData.diffuseIrradiance = max(surfaceData.diffuseIrradiance, 0.0);
    return surfaceData;
}

VividSurfaceSummaryGBufferOutput VividPackSurfaceSummaryGBuffer(
    VividSurfaceSummaryData surfaceData)
{
    surfaceData = VividSanitizeSurfaceSummaryData(surfaceData);

    VividSurfaceSummaryGBufferOutput output;
    output.rt0 = float4(
        surfaceData.diffuseAlbedo,
        VividEncodeDeferredExportHeader(surfaceData.deferredExportHeader));
    output.rt1 = float4(
        VividEncodeSurfaceSummaryNormal(surfaceData.normalWS),
        surfaceData.perceptualRoughness,
        VividEncodeSurfaceSummaryGBufferABITag());
    output.rt2 = float4(
        VividEncodeSurfaceSummarySpecularF0(surfaceData.specularF0),
        surfaceData.ambientOcclusion);
    output.rt3 = float4(surfaceData.emissive, 0.0);
    output.rt4 = VividPackSurfaceSummaryDiffuseIrradiance(
        surfaceData.diffuseIrradiance);
    return output;
}

VividDualSlabLayerSidecarOutput VividPackDualSlabLayerSidecar(
    VividDualSlabLayerData layerData)
{
    VividDualSlabLayerSidecarOutput output;
    output.rt0 = VividPackDualSlabLayerAux0(layerData);
    output.rt1 = VividPackDualSlabLayerAux1(layerData);
    return output;
}

VividSurfaceSummaryData VividUnpackSurfaceSummaryGBuffer(
    float4 rt0,
    float4 rt1,
    float4 rt2,
    float4 rt3,
    float4 rt4)
{
    bool isValidABI = VividIsSurfaceSummaryGBufferABIValid(rt1.a);
    VividSurfaceSummaryData surfaceData;
    surfaceData.diffuseAlbedo = saturate(rt0.rgb);
    surfaceData.deferredExportHeader = isValidABI
        ? VividDecodeDeferredExportHeader(rt0.a)
        : VIVID_DEFERRED_EXPORT_CLASS_ERROR;
    surfaceData.normalWS = VividDecodeSurfaceSummaryNormal(rt1.xy);
    surfaceData.perceptualRoughness = saturate(rt1.z);
    surfaceData.specularF0 = VividDecodeSurfaceSummarySpecularF0(rt2.rgb);
    surfaceData.ambientOcclusion = saturate(rt2.a);
    surfaceData.emissive = isValidABI
        ? max(rt3.rgb, 0.0)
        : float3(1.0, 0.0, 1.0);
    surfaceData.diffuseIrradiance =
        VividUnpackSurfaceSummaryDiffuseIrradiance(rt4);
    return surfaceData;
}

#endif
