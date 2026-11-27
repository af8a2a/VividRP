#ifndef VIVIDRP_FUR_SURFACE_INCLUDED
#define VIVIDRP_FUR_SURFACE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividStandardLitToOpenPBR.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividSlabSurfaceContract.hlsl"

struct VividFurStrandSample
{
    float alpha;
    float2 strandOffsetTS; // Vector from strand center to fragment in cell space (for micro-normal tilt)
    float strandVariation; // Deterministic per-strand [0, 1] variation
    float tipFactor;       // Normalized progress along this specific strand [0, 1]
};

struct VividFurSurfaceParams
{
    float shellHeight;
    float3 rootColor;
    float3 tipColor;
    float colorGradientPower;
    float rootAO;
    float aoPower;
    float rootRoughness;
    float tipRoughness;
    float specularWeight;
    float3 specularTint;
    float anisotropy;
    float fuzzWeight;
    float3 fuzzColor;
    float fuzzRoughness;
    float strandVariation;
    float strandColorVariance;
    float2 strandOffsetTS;
    float strandNormalStrength;
    float3 gravityDirWS;
    float3 combDirWS;
    float combStrength;
    float3 normalWS;
    float3 tangentWS;
    float tangentHandedness;
    float ambientOcclusion;
};

VividFurSurfaceParams VividCreateDefaultFurSurfaceParams()
{
    VividFurSurfaceParams p;
    p.shellHeight = 0.0f;
    p.rootColor = float3(0.14f, 0.09f, 0.06f);
    p.tipColor = float3(0.55f, 0.39f, 0.25f);
    p.colorGradientPower = 1.1f;
    p.rootAO = 0.18f;
    p.aoPower = 1.35f;
    p.rootRoughness = 0.48f;
    p.tipRoughness = 0.34f;
    p.specularWeight = 0.65f;
    p.specularTint = float3(1.0f, 0.94f, 0.88f);
    p.anisotropy = 0.72f;
    p.fuzzWeight = 0.85f;
    p.fuzzColor = float3(0.92f, 0.84f, 0.74f);
    p.fuzzRoughness = 0.48f;
    p.strandVariation = 0.5f;
    p.strandColorVariance = 0.18f;
    p.strandOffsetTS = 0.0f.xx;
    p.strandNormalStrength = 0.22f;
    p.gravityDirWS = float3(0.0f, -1.0f, 0.0f);
    p.combDirWS = float3(0.0f, -1.0f, 0.0f);
    p.combStrength = 0.85f;
    p.normalWS = float3(0.0f, 1.0f, 0.0f);
    p.tangentWS = float3(1.0f, 0.0f, 0.0f);
    p.tangentHandedness = 1.0f;
    p.ambientOcclusion = 1.0f;
    return p;
}

// Fast deterministic 2D->3D hash without trig precision drift across GPUs.
float3 VividFurHash23(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031f, 0.1030f, 0.0973f));
    p3 += dot(p3, p3.yzx + 33.33f);
    return frac((p3.xxy + p3.yzz) * p3.zyx);
}

// Jorge Jimenez's Interleaved Gradient Noise with optional TAA jitter-locked temporal offset.
float VividFurInterleavedGradientNoise(float2 pixelCoord, float2 jitterXY, bool temporalEnabled)
{
    float2 temporalShift = temporalEnabled
        ? (jitterXY * float2(173.0f, 251.0f))
        : 0.0f.xx;
    float2 coord = floor(pixelCoord) + temporalShift;
    return frac(52.9829189f * frac(dot(coord, float2(0.06711056f, 0.00583715f))));
}

// Shell extrusion + quadratic gravity droop + optional sinusoidal wind sway.
// Takes explicit timeSeconds so MotionVectors can evaluate both current (_TimeParameters.x)
// and previous (_LastTimeParameters.x) frame offsets identically.
float3 VividEvaluateFurVertexOffset(
    float3 positionWS,
    float3 normalWS,
    float shellHeight,
    float furLength,
    float3 gravityWS,
    float gravityStrength,
    float3 windDirWS,
    float windStrength,
    float windFrequency,
    float timeSeconds)
{
    float h = saturate(shellHeight);
    if (h <= 1e-5f || furLength <= 1e-5f)
        return 0.0f.xxx;

    float h2 = h * h;
    float3 nWS = SafeNormalize(normalWS);
    float3 offset = nWS * (h * furLength);

    // Quadratic bend keeps hair roots perpendicular to the skin while tips droop smoothly.
    offset += gravityWS * (h2 * furLength * gravityStrength);

    if (windStrength > 1e-5f)
    {
        float3 wDir = dot(windDirWS, windDirWS) > 1e-6f ? normalize(windDirWS) : float3(1.0f, 0.0f, 0.0f);
        float phase = dot(positionWS, wDir * 2.4f + float3(0.7f, 1.3f, 0.9f)) + timeSeconds * max(windFrequency, 0.1f);
        float gust = sin(phase) + 0.45f * sin(phase * 2.13f + 1.37f);
        offset += wDir * (gust * windStrength * furLength * h2);
    }

    return offset;
}

// Procedural tapered multi-strand shell coverage over a jittered 2D UV strand lattice.
// Produces organic clump/strand separation without requiring a texture asset.
VividFurStrandSample VividEvaluateFurProceduralStrands(
    float2 uv0,
    float2 uvTiling,
    float density,
    float shellHeight,
    float strandThickness,
    float strandTaper,
    float lengthVariance,
    float clumpStrength,
    float densityMask)
{
    VividFurStrandSample result;
    result.alpha = 1.0f;
    result.strandOffsetTS = 0.0f.xx;
    result.strandVariation = 0.5f;
    result.tipFactor = saturate(shellHeight);

    float h = saturate(shellHeight);
    float2 scaledUv = uv0 * uvTiling * max(density, 4.0f);

    // Optional macro clumping pulls nearby strands gently toward a shared clump center as h increases.
    if (clumpStrength > 1e-4f && h > 1e-4f)
    {
        float2 clumpGrid = scaledUv * 0.25f;
        float2 clumpCell = floor(clumpGrid);
        float2 clumpLocal = frac(clumpGrid);
        float3 clumpRand = VividFurHash23(clumpCell + 19.37f);
        float2 clumpCenter = 0.25f + 0.50f * clumpRand.xy;
        float2 toClump = (clumpCenter - clumpLocal) * 4.0f;
        scaledUv += toClump * (saturate(clumpStrength) * 0.45f * h);
    }

    float2 baseCell = floor(scaledUv);
    float2 localUv = frac(scaledUv);

    float3 hVal = VividFurHash23(baseCell);
    float2 strandCenter = 0.22f + 0.56f * hVal.xy;
    float2 delta = localUv - strandCenter;
    float dist = length(delta);

    float maxLen = lerp(1.0f - saturate(lengthVariance), 1.0f, hVal.z) * saturate(densityMask);
    result.strandVariation = hVal.z;
    result.strandOffsetTS = delta * 2.0f;

    // Base skin shell (h == 0) is never clipped so no holes ever expose the mesh interior.
    if (h <= 1e-4f)
    {
        result.alpha = 1.0f;
        result.tipFactor = 0.0f;
        return result;
    }

    if (maxLen <= 1e-3f || h >= maxLen)
    {
        result.alpha = 0.0f;
        result.tipFactor = 1.0f;
        return result;
    }

    float tipFrac = saturate(h / maxLen);
    result.tipFactor = tipFrac;

    // Taper strand radius smoothly toward the tip.
    float radiusAtH = clamp(strandThickness, 0.08f, 0.65f)
        * pow(saturate(1.0f - tipFrac), clamp(strandTaper, 0.25f, 3.0f));
    float edgeSoftness = max(radiusAtH * 0.45f, 0.025f);
    result.alpha = saturate((radiusAtH - dist) / edgeSoftness);
    return result;
}

// Converts Fur shell parameters into OpenPBR V2 single-closure Substrate inputs
// (EON diffuse + Keratin anisotropic specular + OpenPBR Disney LTC Fuzz rim).
VividOpenPBRV2Inputs VividFurParamsToOpenPBRV2Inputs(VividFurSurfaceParams p, out float outAO)
{
    VividOpenPBRV2Inputs v2 = VividCreateDefaultOpenPBRV2Inputs();

    float h = saturate(p.shellHeight);
    float3 geomNormal = VividSanitizeSurfaceSummaryNormal(p.normalWS);

    float3 baseTangent = p.tangentWS - geomNormal * dot(p.tangentWS, geomNormal);
    float baseTangentLenSq = dot(baseTangent, baseTangent);
    float3 unitTangent = baseTangentLenSq > 1e-8f
        ? baseTangent * rsqrt(baseTangentLenSq)
        : float3(1.0f, 0.0f, 0.0f);
    float handedness = p.tangentHandedness < 0.0f ? -1.0f : 1.0f;
    float3 unitBitangent = cross(geomNormal, unitTangent) * handedness;

    // Perturb shading normal gently around each strand cylinder for micro-clump shading relief.
    float3 shadingNormal = geomNormal;
    if (p.strandNormalStrength > 1e-4f && h > 1e-4f)
    {
        float2 tilt = clamp(p.strandOffsetTS, -1.0f.xx, 1.0f.xx) * saturate(p.strandNormalStrength) * sqrt(h);
        float3 nTS = SafeNormalize(float3(tilt, 1.0f));
        shadingNormal = SafeNormalize(
            nTS.x * unitTangent
            + nTS.y * unitBitangent
            + nTS.z * geomNormal);
    }

    // Align hair anisotropic tangent with gravity/comb flow projected onto the shading tangent plane.
    float3 flowDirWS = lerp(p.gravityDirWS, p.combDirWS, saturate(p.combStrength));
    float3 hairFlowTangent = flowDirWS - shadingNormal * dot(flowDirWS, shadingNormal);
    float flowLenSq = dot(hairFlowTangent, hairFlowTangent);
    float3 finalTangent = flowLenSq > 1e-6f
        ? hairFlowTangent * rsqrt(flowLenSq)
        : unitTangent;

    // Root-to-tip albedo gradient + subtle per-strand brightness variation.
    float gradT = pow(h, max(p.colorGradientPower, 0.1f));
    float strandMul = lerp(1.0f - saturate(p.strandColorVariance), 1.0f + saturate(p.strandColorVariance), p.strandVariation);
    float3 furAlbedo = saturate(lerp(saturate(p.rootColor), saturate(p.tipColor), gradT) * strandMul);

    // Root self-shadowing (depth AO inside the fur coat) immediately gives strong volumetric depth.
    float shellAO = lerp(saturate(p.rootAO), 1.0f, pow(h, max(p.aoPower, 0.1f)));
    outAO = saturate(p.ambientOcclusion * shellAO);

    v2.core.baseWeight = 1.0f;
    v2.core.baseColor = furAlbedo;
    v2.core.baseDiffuseRoughness = 0.85f;
    v2.core.baseMetalness = 0.0f;
    v2.core.specularWeight = saturate(p.specularWeight);
    v2.core.specularColor = saturate(p.specularTint);
    v2.core.specularRoughness = clamp(lerp(p.rootRoughness, p.tipRoughness, h), 0.15f, 0.95f);
    v2.core.specularIor = 1.55f; // Physical keratin hair/fur IOR
    v2.core.normalWS = shadingNormal;
    v2.core.emissionLuminance = 0.0f;
    v2.core.emissionColor = 0.0f.xxx;

    // Anisotropic highlight band across hair strands (OpenPBR stretches along bitangent, perpendicular to tangent).
    v2.specularAnisotropy = saturate(p.anisotropy) * lerp(0.35f, 1.0f, h);
    v2.specularAnisotropyRotation = 0.0f;
    v2.tangentWS = finalTangent;
    v2.tangentHandedness = handedness;

    // OpenPBR Disney LTC Fuzz / Sheen builds a lush backlit fur halo increasing toward the outer tips.
    v2.fuzzWeight = saturate(p.fuzzWeight) * lerp(0.35f, 1.0f, h);
    v2.fuzzColor = saturate(p.fuzzColor);
    v2.fuzzRoughness = clamp(p.fuzzRoughness, 0.12f, 1.0f);

    v2.coatWeight = 0.0f;
    v2.coatNormalWS = shadingNormal;
    v2.hasIndependentCoatNormal = 0u;
    return v2;
}

#endif // VIVIDRP_FUR_SURFACE_INCLUDED
