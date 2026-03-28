#ifndef VIVIDRP_GBUFFER_INCLUDED
#define VIVIDRP_GBUFFER_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Input.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

#define VIVID_GBUFFER_MATERIAL_STANDARD  0u
#define VIVID_GBUFFER_MATERIAL_FABRIC    1u
#define VIVID_GBUFFER_MATERIAL_CLEARCOAT 2u
#define VIVID_GBUFFER_MAX_MATERIAL_ID    255u
#define VIVID_GBUFFER_MAX_NRD_MATERIAL_ID 3u

// GBuffer layout:
// RT0 (RGBA8_UNORM)              : BaseColor.rgb + MaterialId.a
// RT1 (A2B10G10R10_UNORM)        : Octahedral Normal.xy + LinearRoughness.b + NRDMaterialId.a
// RT2 (RGBA8_UNORM)              : Metallic.r + AO.g + MaterialData0.b + MaterialData1.a
// RT3 (R11G11B10_UFLOAT)         : Emissive.rgb

struct VividGBufferSurfaceData
{
    float3 baseColor;
    float3 normalWS;
    float linearRoughness;
    float metallic;
    float ambientOcclusion;
    float customData;
    float customData1;
    uint materialId;
    float3 emissive;
};

struct VividGBufferFragmentOutput
{
    float4 rt0 : SV_Target0;
    float4 rt1 : SV_Target1;
    float4 rt2 : SV_Target2;
    float4 rt3 : SV_Target3;
};

float EncodeVividMaterialId(uint materialId)
{
    uint clampedMaterialId = min(materialId, VIVID_GBUFFER_MAX_MATERIAL_ID);
    return clampedMaterialId * (1.0 / 255.0);
}

uint DecodeVividMaterialId(float encodedMaterialId)
{
    return (uint)min(round(saturate(encodedMaterialId) * 255.0), 255.0);
}

float2 EncodeVividNormalOctRaw(float3 normalWS)
{
    return PackNormalOctQuadEncode(normalize(normalWS));
}

float3 DecodeVividNormalOctRaw(float2 encodedNormal)
{
    return normalize(UnpackNormalOctQuadEncode(encodedNormal));
}

float2 EncodeVividNormalOct(float3 normalWS)
{
    return EncodeVividNormalOctRaw(normalWS) * 0.5 + 0.5;
}

float3 DecodeVividNormalOct(float2 encodedNormal)
{
    float2 remappedNormal = saturate(encodedNormal) * 2.0 - 1.0;
    return DecodeVividNormalOctRaw(remappedNormal);
}

float SanitizeLinearRoughness(float linearRoughness)
{
    return saturate(linearRoughness);
}

float SanitizeMetallic(float metallic)
{
    return saturate(metallic);
}

float SanitizeAmbientOcclusion(float ambientOcclusion)
{
    return saturate(ambientOcclusion);
}

float SanitizeCustomData(float customData)
{
    return saturate(customData);
}

float SanitizeCustomData1(float customData1)
{
    return saturate(customData1);
}

float EncodeVividNrdMaterialId(uint materialId)
{
    uint clampedMaterialId = min(materialId, VIVID_GBUFFER_MAX_NRD_MATERIAL_ID);
    return clampedMaterialId * (1.0 / 3.0);
}

uint DecodeVividNrdMaterialId(float encodedMaterialId)
{
    return (uint)min(round(saturate(encodedMaterialId) * 3.0), 3.0);
}

VividGBufferSurfaceData SanitizeVividGBufferSurfaceData(VividGBufferSurfaceData surfaceData)
{
    surfaceData.baseColor = saturate(surfaceData.baseColor);
    surfaceData.normalWS = normalize(surfaceData.normalWS);
    surfaceData.linearRoughness = SanitizeLinearRoughness(surfaceData.linearRoughness);
    surfaceData.metallic = SanitizeMetallic(surfaceData.metallic);
    surfaceData.ambientOcclusion = SanitizeAmbientOcclusion(surfaceData.ambientOcclusion);
    surfaceData.customData = SanitizeCustomData(surfaceData.customData);
    surfaceData.customData1 = SanitizeCustomData1(surfaceData.customData1);
    surfaceData.materialId = min(surfaceData.materialId, VIVID_GBUFFER_MAX_MATERIAL_ID);
    surfaceData.emissive = max(surfaceData.emissive, 0.0);
    return surfaceData;
}

VividGBufferFragmentOutput PackVividGBufferSurfaceData(VividGBufferSurfaceData surfaceData)
{
    surfaceData = SanitizeVividGBufferSurfaceData(surfaceData);

    VividGBufferFragmentOutput output;
    output.rt0 = float4(surfaceData.baseColor, EncodeVividMaterialId(surfaceData.materialId));
    output.rt1 = float4(
        EncodeVividNormalOct(surfaceData.normalWS),
        surfaceData.linearRoughness,
        EncodeVividNrdMaterialId(surfaceData.materialId));
    output.rt2 = float4(
        surfaceData.metallic,
        surfaceData.ambientOcclusion,
        surfaceData.customData,
        surfaceData.customData1);
    output.rt3 = float4(surfaceData.emissive, 0.0);
    return output;
}

VividGBufferSurfaceData UnpackVividGBufferSurfaceData(float4 rt0, float4 rt1, float4 rt2, float4 rt3)
{
    VividGBufferSurfaceData surfaceData;
    surfaceData.baseColor = saturate(rt0.rgb);
    surfaceData.materialId = DecodeVividMaterialId(rt0.a);
    surfaceData.normalWS = DecodeVividNormalOct(rt1.xy);
    surfaceData.linearRoughness = SanitizeLinearRoughness(rt1.z);
    surfaceData.metallic = SanitizeMetallic(rt2.r);
    surfaceData.ambientOcclusion = SanitizeAmbientOcclusion(rt2.g);
    surfaceData.customData = SanitizeCustomData(rt2.b);
    surfaceData.customData1 = SanitizeCustomData1(rt2.a);
    surfaceData.emissive = max(rt3.rgb, 0.0);
    return surfaceData;
}

float GetPerceptualRoughnessFromLinearRoughness(float linearRoughness)
{
    return sqrt(SanitizeLinearRoughness(linearRoughness));
}

#endif
