#ifndef VIVIDRP_FABRIC_SURFACE_INCLUDED
#define VIVIDRP_FABRIC_SURFACE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividStandardLitToOpenPBR.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividSlabSurfaceContract.hlsl"

// Self-contained Reoriented Normal Mapping (RNM) blend matching CoreRP BlendNormalRNM.
float3 VividBlendNormalRNM(float3 n1, float3 n2)
{
    float3 t = n1 + float3(0.0f, 0.0f, 1.0f);
    float3 u = n2 * float3(-1.0f, -1.0f, 1.0f);
    return SafeNormalize(t * dot(t, u) - u * t.z);
}

// HDRP-compatible Fabric material types mapped onto OpenPBR V2 (Substrate SingleComplex):
//   0 = CottonWool (Cotton, Denim, Wool, Linen, Velvet):
//       Rough EON diffuse + OpenPBR Disney LTC Fuzz/Sheen lobe with suppressed plastic specular.
//   1 = Silk (Silk, Shot Silk, Satin, Nylon):
//       Smooth filament yarns with tinted dielectric specular (Fresnel IOR boost),
//       anisotropic GGX highlight, and optional subtle grazing silk sheen.
#define VIVID_FABRIC_TYPE_COTTON_WOOL 0u
#define VIVID_FABRIC_TYPE_SILK 1u

struct VividFabricSurfaceParams
{
    uint fabricType;
    float3 baseColor;
    float3 sheenColor;
    float smoothness;
    float diffuseRoughness;
    float fuzzWeight;
    float fuzzRoughness;
    float anisotropy;          // [-1, 1], negative rotates tangent 90 deg (HDRP convention)
    float anisotropyRotation;  // radians
    float silkSpecularBoost;   // [0, 1], scales Silk dielectric F0 / IOR
    float ambientOcclusion;
    float3 normalWS;
    float3 tangentWS;
    float tangentHandedness;
    float3 emission;
};

VividFabricSurfaceParams VividCreateDefaultFabricSurfaceParams()
{
    VividFabricSurfaceParams p;
    p.fabricType = VIVID_FABRIC_TYPE_COTTON_WOOL;
    p.baseColor = float3(0.5f, 0.5f, 0.5f);
    p.sheenColor = float3(0.35f, 0.35f, 0.35f);
    p.smoothness = 0.25f;
    p.diffuseRoughness = 0.85f;
    p.fuzzWeight = 0.65f;
    p.fuzzRoughness = 0.65f;
    p.anisotropy = 0.0f;
    p.anisotropyRotation = 0.0f;
    p.silkSpecularBoost = 0.65f;
    p.ambientOcclusion = 1.0f;
    p.normalWS = float3(0.0f, 1.0f, 0.0f);
    p.tangentWS = float3(1.0f, 0.0f, 0.0f);
    p.tangentHandedness = 1.0f;
    p.emission = 0.0f.xxx;
    return p;
}

// HDRP ThreadMap channel packing:
//   R   = Ambient Occlusion
//   G,A = Tangent-space Normal XY
//   B   = Smoothness modulation centered at 0.5
void VividApplyFabricThreadMap(
    float4 threadSample,
    float threadAOStrength,
    float threadNormalStrength,
    float threadSmoothnessScale,
    inout float3 normalTS,
    inout float smoothness,
    inout float ambientOcclusion)
{
    float2 threadNxy = (threadSample.ga * 2.0f - 1.0f) * saturate(threadNormalStrength);
    float3 threadNormalTS = SafeNormalize(float3(threadNxy, sqrt(saturate(1.0f - dot(threadNxy, threadNxy)))));
    normalTS = VividBlendNormalRNM(normalTS, threadNormalTS);

    float smoothnessDelta = (threadSample.b * 2.0f - 1.0f) * threadSmoothnessScale;
    smoothness = saturate(smoothness + smoothnessDelta);

    ambientOcclusion *= lerp(1.0f, saturate(threadSample.r), saturate(threadAOStrength));
}

// Procedural high-frequency plain/twill woven thread detail fallback when no _ThreadMap texture
// is assigned. Derivative-filtered via ctx.uv0Ddx/uv0Ddy so both Raster and GPUDriven Compute
// stay moire-free at grazing angles and far distances.
void VividApplyProceduralFabricWeave(
    const VividSlabEvalContext ctx,
    uint fabricType,
    float weaveScale,
    float weaveStrength,
    inout float3 normalTS,
    inout float3 baseColor,
    inout float smoothness,
    inout float ambientOcclusion)
{
    float strength = saturate(weaveStrength);
    if (strength <= 1e-4f)
        return;

    float2 uvScale = float2(2.0f, 1.0f) * max(weaveScale, 4.0f);
    float2 grid = ctx.uv0 * uvScale;
    int2 cell = (int2)floor(grid);
    float2 localUv = frac(grid);

    float filterWidth = max(length(ctx.uv0Ddx * uvScale), length(ctx.uv0Ddy * uvScale));
    float aaAtten = 1.0f - smoothstep(0.30f, 1.25f, filterWidth);
    float effStrength = strength * aaAtten;
    if (effStrength <= 1e-4f)
        return;

    // CottonWool uses tighter plain/twill yarn bumps; Silk uses sleeker satin-twill fine threads.
    int period = (fabricType == VIVID_FABRIC_TYPE_SILK) ? 4 : 2;
    int phase = ((cell.x + cell.y) % period + period) % period;
    bool isWarp = (fabricType == VIVID_FABRIC_TYPE_SILK) ? (phase < 3) : (phase == 0);

    float along = isWarp ? localUv.x : localUv.y;
    float across = isWarp ? localUv.y : localUv.x;

    float sinAlong = sin(along * PI);
    float cosAlong = cos(along * PI);
    float sinAcross = sin(across * PI);
    float cosAcross = cos(across * PI);

    float yarnCrest = saturate(sinAlong * sinAcross);
    float slopeAlong = 0.55f * cosAlong * sinAcross;
    float slopeAcross = 0.85f * sinAlong * cosAcross;
    float2 slopeTS = isWarp
        ? float2(-slopeAlong, -slopeAcross)
        : float2(-slopeAcross, -slopeAlong);

    float3 weaveNormalTS = SafeNormalize(float3(slopeTS * effStrength, 1.0f));
    normalTS = VividBlendNormalRNM(normalTS, weaveNormalTS);

    // Subtle micro-cavity darkening and smoothness variation between yarn crests and valleys.
    float aoFactor = lerp(1.0f, lerp(0.76f, 1.0f, yarnCrest), effStrength);
    ambientOcclusion *= aoFactor;
    baseColor = saturate(baseColor * lerp(1.0f, lerp(0.88f, 1.04f, yarnCrest), effStrength));
    smoothness = saturate(smoothness + lerp(-0.06f, 0.04f, yarnCrest) * effStrength);
}

// Converts HDRP-style Fabric parameters into OpenPBR V2 single-closure Substrate inputs.
VividOpenPBRV2Inputs VividFabricParamsToOpenPBRV2Inputs(VividFabricSurfaceParams p)
{
    VividOpenPBRV2Inputs v2 = VividCreateDefaultOpenPBRV2Inputs();

    float3 unitNormal = VividSanitizeSurfaceSummaryNormal(p.normalWS);
    float3 tangent = p.tangentWS - unitNormal * dot(p.tangentWS, unitNormal);
    float tangentLenSq = dot(tangent, tangent);
    float3 unitTangent = tangentLenSq > 1e-8f ? tangent * rsqrt(tangentLenSq) : float3(1.0f, 0.0f, 0.0f);
    float handedness = p.tangentHandedness < 0.0f ? -1.0f : 1.0f;

    float3 baseColor = saturate(p.baseColor);
    float3 sheenColor = saturate(p.sheenColor);
    float smoothness = saturate(p.smoothness);

    v2.core.baseWeight = 1.0f;
    v2.core.baseColor = baseColor;
    v2.core.baseMetalness = 0.0f;
    v2.core.normalWS = unitNormal;
    v2.coatNormalWS = unitNormal;
    v2.hasIndependentCoatNormal = 0u;
    v2.coatWeight = 0.0f;
    v2.tangentWS = unitTangent;
    v2.tangentHandedness = handedness;

    float3 emission = max(p.emission, 0.0f.xxx);
    float emissionMax = max(emission.r, max(emission.g, emission.b));
    if (emissionMax > 0.0f)
    {
        v2.core.emissionLuminance = emissionMax;
        v2.core.emissionColor = saturate(emission / emissionMax);
    }
    else
    {
        v2.core.emissionLuminance = 0.0f;
        v2.core.emissionColor = 0.0f.xxx;
    }

    if (p.fabricType == VIVID_FABRIC_TYPE_SILK)
    {
        // Silk / Shot Silk / Satin / Nylon:
        // Smooth filament yarns with colored dielectric specular, anisotropic highlight,
        // and optional tinted grazing micro-sheen (for two-tone Shot Silk iridescence).
        float sheenMax = max(sheenColor.r, max(sheenColor.g, sheenColor.b));
        float3 normalizedSpecTint = sheenMax > 1e-4f
            ? saturate(sheenColor / sheenMax)
            : 1.0f.xxx;

        // Map HDRP Silk _SpecularColor intensity + _SilkSpecularBoost to physical dielectric IOR [1.35, 2.5].
        float targetF0 = clamp(lerp(0.04f, 0.18f, saturate(p.silkSpecularBoost)) * max(sheenMax, 0.25f), 0.02f, 0.20f);
        float sqrtF0 = sqrt(targetF0);
        float silkIor = clamp((1.0f + sqrtF0) / max(1.0f - sqrtF0, 0.15f), 1.35f, 2.5f);

        v2.core.baseDiffuseRoughness = clamp(p.diffuseRoughness * 0.5f, 0.0f, 0.6f);
        v2.core.specularWeight = 1.0f;
        v2.core.specularColor = normalizedSpecTint;
        v2.core.specularRoughness = clamp(1.0f - smoothness, 0.12f, 0.90f);
        v2.core.specularIor = silkIor;

        // HDRP anisotropy is [-1, 1]; negative swaps tangent/bitangent (90 deg rotation).
        float anisoClamped = clamp(p.anisotropy, -1.0f, 1.0f);
        v2.specularAnisotropy = abs(anisoClamped);
        v2.specularAnisotropyRotation = (anisoClamped < 0.0f ? (PI * 0.5f) : 0.0f) + p.anisotropyRotation;

        // Optional grazing silk micro-sheen enhances two-tone Shot Silk color shift at rim angles.
        v2.fuzzWeight = saturate(p.fuzzWeight);
        v2.fuzzColor = sheenColor;
        v2.fuzzRoughness = clamp(p.fuzzRoughness, 0.15f, 1.0f);
    }
    else
    {
        // Cotton / Wool / Denim / Linen / Velvet:
        // Rough matte EON diffuse + OpenPBR Disney LTC Fuzz/Sheen lobe.
        // Suppresses plastic mirror-like dielectric specular as fuzz coverage increases.
        float fuzzW = saturate(p.fuzzWeight);
        v2.core.baseDiffuseRoughness = clamp(p.diffuseRoughness, 0.35f, 1.0f);
        v2.core.specularWeight = lerp(0.18f, 0.0f, fuzzW);
        v2.core.specularColor = 1.0f.xxx;
        v2.core.specularRoughness = clamp(1.0f - smoothness * 0.65f, 0.45f, 1.0f);
        v2.core.specularIor = 1.45f;

        v2.specularAnisotropy = 0.0f;
        v2.specularAnisotropyRotation = 0.0f;

        v2.fuzzWeight = fuzzW;
        v2.fuzzColor = sheenColor;
        v2.fuzzRoughness = clamp(p.fuzzRoughness, 0.10f, 1.0f);
    }

    return v2;
}

#endif // VIVIDRP_FABRIC_SURFACE_INCLUDED
