//------------------------------------------------------------------------------
// RayTracingGBufferOutput.hlsl - DLSS-RR Native GBuffer Output Format
//------------------------------------------------------------------------------
// Provides helper functions and structures for outputting GBuffer data
// directly in DLSS-RR native format from raytracing passes.
//------------------------------------------------------------------------------

#ifndef RAYTRACING_GBUFFER_OUTPUT_INCLUDED
#define RAYTRACING_GBUFFER_OUTPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"

//------------------------------------------------------------------------------
// DLSS-RR GBuffer Output Structure
//------------------------------------------------------------------------------

struct RayTracingGBufferOutput
{
    float4 diffuseAlbedo;      // RGB = diffuse albedo (albedo * (1-metallic)), A = unused
    float4 specularAlbedo;     // RGB = specular albedo (EnvBRDFApprox2), A = unused
    float4 normalRoughness;    // RGB = world normal [-1,1], A = sqrt(alphaRoughness)
    float depth;               // Linear depth or hardware depth
};

//------------------------------------------------------------------------------
// EnvBRDFApprox2 - Ray Tracing Gems Chapter 32
//------------------------------------------------------------------------------
// Approximation of the environment BRDF integral for specular.
// This computes F0 * scale + bias where scale and bias are approximated.
//
// @param specularColor The F0 specular color (0.04 for dielectrics, albedo for metals)
// @param alphaRoughness Roughness squared (perceptualRoughness^2)
// @param NoV Dot product of normal and view direction
// @return Specular albedo suitable for DLSS-RR
//------------------------------------------------------------------------------
float3 EnvBRDFApprox2(float3 specularColor, float alphaRoughness, float NoV)
{
    // Approximation coefficients from Ray Tracing Gems
    const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);

    float4 r = alphaRoughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
    float2 AB = float2(-1.04, 1.04) * a004 + r.zw;

    return specularColor * AB.x + AB.y;
}

//------------------------------------------------------------------------------
// DLSS-RR Roughness Conversion
//------------------------------------------------------------------------------
// DLSS-RR expects roughness in the format: sqrt(alphaRoughness)
// where alphaRoughness = perceptualRoughness^2
//
// @param perceptualSmoothness Unity's smoothness value [0,1]
// @return Roughness in DLSS-RR format
//------------------------------------------------------------------------------
float ComputeDLSSRRRoughness(float perceptualSmoothness)
{
    float perceptualRoughness = 1.0 - perceptualSmoothness;
    float alphaRoughness = perceptualRoughness * perceptualRoughness;
    return sqrt(alphaRoughness); // DLSS-RR expects sqrt(alphaRoughness)
}

//------------------------------------------------------------------------------
// Compute DLSS-RR Albedos
//------------------------------------------------------------------------------
// Computes both diffuse and specular albedo in the format expected by DLSS-RR.
//
// @param albedo Surface base color
// @param metallic Metallic value [0,1]
// @param roughness DLSS-RR roughness (sqrt(alphaRoughness))
// @param NoV Dot product of normal and view direction
// @param diffuseAlbedo Output diffuse albedo
// @param specularAlbedo Output specular albedo
//------------------------------------------------------------------------------
void ComputeDLSSRRAlbedos(
    float3 albedo,
    float metallic,
    float roughness,
    float NoV,
    out float3 diffuseAlbedo,
    out float3 specularAlbedo)
{
    // Diffuse: non-metallic contribution only
    diffuseAlbedo = albedo * (1.0 - metallic);

    // F0 for metallic workflow
    // Dielectrics: 0.04 (4% reflectance)
    // Metals: use albedo as F0
    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);

    // Convert DLSS-RR roughness back to alpha roughness for EnvBRDF
    float alphaRoughness = roughness * roughness;

    // Specular albedo using EnvBRDFApprox2
    specularAlbedo = EnvBRDFApprox2(F0, alphaRoughness, NoV);
}

//------------------------------------------------------------------------------
// All-in-One GBuffer Output Helper
//------------------------------------------------------------------------------
// Convenience function to compute all DLSS-RR GBuffer outputs at once.
//
// @param albedo Surface base color
// @param metallic Metallic value [0,1]
// @param smoothness Smoothness value [0,1]
// @param normalWS World-space normal (should be normalized)
// @param NoV Dot product of normal and view direction
// @return Complete GBuffer output structure
//------------------------------------------------------------------------------
RayTracingGBufferOutput ComputeDLSSRRGBuffer(
    float3 albedo,
    float metallic,
    float smoothness,
    float3 normalWS,
    float NoV)
{
    RayTracingGBufferOutput output;

    // Compute roughness in DLSS-RR format
    float roughness = ComputeDLSSRRRoughness(smoothness);

    // Compute albedos
    float3 diffuseAlbedo, specularAlbedo;
    ComputeDLSSRRAlbedos(albedo, metallic, roughness, NoV, diffuseAlbedo, specularAlbedo);

    // Fill output structure
    output.diffuseAlbedo = float4(diffuseAlbedo, 1.0);
    output.specularAlbedo = float4(specularAlbedo, 1.0);
    output.normalRoughness = float4(normalWS, roughness);
    output.depth = 0.0; // Caller should set this

    return output;
}

#endif // RAYTRACING_GBUFFER_OUTPUT_INCLUDED
