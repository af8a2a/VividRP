#ifndef VIVIDRP_FUR_SURFACE_INCLUDED
#define VIVIDRP_FUR_SURFACE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividStandardLitToOpenPBR.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividSubstrateBuffer.hlsl"
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
    float gravityStrength;
    float3 combDirWS;
    float combStrength;
    float3 normalWS;
    float3 tangentWS;
    float tangentHandedness;
    float ambientOcclusion;
    // Stage 3.3 Marschner Hair/Fur BCSDF parameters
    float hairRadialRoughness;
    float hairCuticleAngleDeg;
    float hairScatter;
    float hairBacklit;
    float furMedullaWeight;
    float furMedullaRoughness;
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
    p.gravityStrength = 0.45f;
    p.combDirWS = float3(0.0f, -1.0f, 0.0f);
    p.combStrength = 0.85f;
    p.normalWS = float3(0.0f, 1.0f, 0.0f);
    p.tangentWS = float3(1.0f, 0.0f, 0.0f);
    p.tangentHandedness = 1.0f;
    p.ambientOcclusion = 1.0f;
    p.hairRadialRoughness = 0.45f;
    p.hairCuticleAngleDeg = 2.5f;
    p.hairScatter = 0.85f;
    p.hairBacklit = 0.85f;
    p.furMedullaWeight = 0.55f;
    p.furMedullaRoughness = 0.65f;
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

struct VividFurHybridHit
{
    bool hit;
    float hitShellHeight;
    VividFurStrandSample strand;
};

// Maps a world-space tangent displacement into UV space using the screen-space 2x2 Jacobian
// of (positionWS, uv0), working cleanly across arbitrary mesh scales, UV tilings, and shell bends.
float2 VividFurProjectWorldDeltaToUV(
    float3 deltaTangentWS,
    float3 dPosDx,
    float3 dPosDy,
    float2 dUvDx,
    float2 dUvDy)
{
    // Normalize pixel footprint vectors to O(1) so det is completely scale-invariant
    // (at ~1m camera distance |dPosDx|^2 ~ 1e-6, so unnormalized det ~ 1e-12!).
    float lenSqMax = max(dot(dPosDx, dPosDx), dot(dPosDy, dPosDy));
    if (lenSqMax <= 1e-12f)
        return 0.0f.xx;

    float invScale = rsqrt(lenSqMax);
    float3 pX = dPosDx * invScale;
    float3 pY = dPosDy * invScale;
    float3 deltaScaled = deltaTangentWS * invScale;

    float a = dot(pX, pX);
    float b = dot(pX, pY);
    float c = dot(pY, pY);
    float det = a * c - b * b;
    if (det <= 1e-6f)
        return 0.0f.xx;

    float ux = dot(deltaScaled, pX);
    float uy = dot(deltaScaled, pY);
    float invDet = rcp(det);
    float sx = (c * ux - b * uy) * invDet;
    float sy = (a * uy - b * ux) * invDet;
    return sx * dUvDx + sy * dUvDy;
}

// Hybrid Shells (compute-safe core): marches along the view ray through a sparse volumetric slab
// [hTop - shellStep, hTop] using explicit screen-space position/UV derivatives so both Raster
// fragment passes and GPUDriven Compute Resolve execute the exact same algorithm.
VividFurHybridHit VividEvaluateFurHybridSlabWithDerivatives(
    float3 positionWS,
    float3 positionWSDdx,
    float3 positionWSDdy,
    float3 geomNormalWS,
    float3 cameraPosWS,
    float2 uv0,
    float2 uv0Ddx,
    float2 uv0Ddy,
    float2 uvTiling,
    float density,
    float shellHeightTop,
    float shellStep,
    float furLength,
    float3 gravityWS,
    float gravityStrength,
    float strandThickness,
    float strandTaper,
    float lengthVariance,
    float clumpStrength,
    float densityMask,
    float alphaCutoff,
    float ditherStrength,
    float ign,
    int stepCount,
    float maxParallaxScale)
{
    VividFurHybridHit hitResult;
    hitResult.hit = false;
    hitResult.hitShellHeight = saturate(shellHeightTop);
    hitResult.strand.alpha = 0.0f;
    hitResult.strand.strandOffsetTS = 0.0f.xx;
    hitResult.strand.strandVariation = 0.5f;
    hitResult.strand.tipFactor = 1.0f;

    float hTop = saturate(shellHeightTop);
    if (hTop <= 1e-4f)
    {
        hitResult.hit = true;
        hitResult.hitShellHeight = 0.0f;
        hitResult.strand = VividEvaluateFurProceduralStrands(
            uv0, uvTiling, density, 0.0f, strandThickness, strandTaper, lengthVariance, clumpStrength, densityMask);
        return hitResult;
    }

    float slabDeltaH = min(hTop, max(shellStep, 1e-4f));
    float slabThicknessWS = slabDeltaH * max(furLength, 1e-4f);

    float3 nWS = SafeNormalize(geomNormalWS);
    float3 rayDirWS = SafeNormalize(positionWS - cameraPosWS);
    float cosTheta = max(dot(-nWS, rayDirWS), rcp(max(maxParallaxScale, 1.0f)));
    float slabRayDist = slabThicknessWS / cosTheta;

    float3 rayDeltaWS = rayDirWS * slabRayDist;
    float3 tangentRayDeltaWS = rayDeltaWS - nWS * dot(rayDeltaWS, nWS);

    // Unwind quadratic gravity bend across the slab so raymarched strands curve identically to dense shells.
    float3 bendDeltaWS = gravityWS * (2.0f * hTop * slabDeltaH * max(furLength, 0.0f) * max(gravityStrength, 0.0f));
    float3 tangentBendDeltaWS = bendDeltaWS - nWS * dot(bendDeltaWS, nWS);

    float2 uvDeltaPerSlab = VividFurProjectWorldDeltaToUV(
        tangentRayDeltaWS + tangentBendDeltaWS,
        positionWSDdx,
        positionWSDdy,
        uv0Ddx,
        uv0Ddy);

    // Clamp extreme grazing UV jumps at silhouette discontinuities.
    float maxUvJump = 0.35f;
    float uvJumpLenSq = dot(uvDeltaPerSlab, uvDeltaPerSlab);
    if (uvJumpLenSq > maxUvJump * maxUvJump)
        uvDeltaPerSlab *= maxUvJump * rsqrt(uvJumpLenSq);

    int steps = clamp(stepCount, 1, 8);
    float invSteps = rcp((float)steps);
    float ignAlpha = frac(ign + 0.6180339887f);
    float ditherCutoff = lerp(0.04f, 0.96f, ignAlpha);
    float threshold = lerp(saturate(alphaCutoff), ditherCutoff, saturate(ditherStrength));

    [loop]
    for (int k = 0; k < steps; ++k)
    {
        float s = ((float)k + ign) * invSteps;
        float hSample = max(hTop - slabDeltaH * s, 0.0f);
        float2 uvSample = uv0 + uvDeltaPerSlab * s;

        VividFurStrandSample sampleVal = VividEvaluateFurProceduralStrands(
            uvSample,
            uvTiling,
            density,
            hSample,
            strandThickness,
            strandTaper,
            lengthVariance,
            clumpStrength,
            densityMask);

        if (sampleVal.alpha >= threshold)
        {
            hitResult.hit = true;
            hitResult.hitShellHeight = hSample;
            hitResult.strand = sampleVal;
            return hitResult;
        }
    }

    return hitResult;
}

#if !defined(VIVID_SLAB_TARGET_GPUDRIVEN_COMPUTE)
// Fragment-shader convenience overload using hardware ddx/ddy on positionWS.
VividFurHybridHit VividEvaluateFurHybridSlab(
    float3 positionWS,
    float3 geomNormalWS,
    float3 cameraPosWS,
    float2 uv0,
    float2 uv0Ddx,
    float2 uv0Ddy,
    float2 uvTiling,
    float density,
    float shellHeightTop,
    float shellStep,
    float furLength,
    float3 gravityWS,
    float gravityStrength,
    float strandThickness,
    float strandTaper,
    float lengthVariance,
    float clumpStrength,
    float densityMask,
    float alphaCutoff,
    float ditherStrength,
    float ign,
    int stepCount,
    float maxParallaxScale)
{
    return VividEvaluateFurHybridSlabWithDerivatives(
        positionWS,
        ddx(positionWS),
        ddy(positionWS),
        geomNormalWS,
        cameraPosWS,
        uv0,
        uv0Ddx,
        uv0Ddy,
        uvTiling,
        density,
        shellHeightTop,
        shellStep,
        furLength,
        gravityWS,
        gravityStrength,
        strandThickness,
        strandTaper,
        lengthVariance,
        clumpStrength,
        densityMask,
        alphaCutoff,
        ditherStrength,
        ign,
        stepCount,
        maxParallaxScale);
}
#endif

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

// Analytic 3D strand growth tangent T(h) pointing root -> tip along combed fur flow:
// Groomed animal fur lies predominantly along the surface-projected comb/gravity flow
// with a progressive root-to-tip loft along shadingNormalWS (roots emerge slightly steeper
// from the skin while outer tips lie smoothly along gravity/combing for natural Marschner bands).
float3 VividComputeFurStrandTangent3D(
    float3 shadingNormalWS,
    float3 gravityDirWS,
    float gravityStrength,
    float3 combDirWS,
    float combStrength,
    float shellHeight,
    float3 fallbackTangentWS)
{
    float3 nWS = VividSanitizeSurfaceSummaryNormal(shadingNormalWS);
    float h = saturate(shellHeight);
    float comb = saturate(combStrength);

    float3 flowDirWS = lerp(gravityDirWS, combDirWS, comb);
    float3 hairFlowTangent = flowDirWS - nWS * dot(flowDirWS, nWS);
    float flowLenSq = dot(hairFlowTangent, hairFlowTangent);
    float3 unitFlowTangent = flowLenSq > 1e-6f
        ? hairFlowTangent * rsqrt(flowLenSq)
        : fallbackTangentWS;

    // Combine surface-combed tangent with physical loft away from skin (higher at roots, sleeker at tips).
    float droop = saturate(max(gravityStrength, 0.0f) * 0.5f + comb * 0.5f);
    float loftWeight = lerp(0.50f, 0.14f, h) * lerp(1.0f, 0.35f, droop);
    float3 t3D = unitFlowTangent + nWS * loftWeight;
    float tLenSq = dot(t3D, t3D);
    return tLenSq > 1e-6f ? t3D * rsqrt(tLenSq) : unitFlowTangent;
}

// Converts Fur shell parameters into Stage 3.2 Substrate Hair/Fur Closure inputs
// (UE5 Karis Far-Field Marschner + Zinke Dual Scattering + Yan Medulla).
VividSubstrateHairInputs VividFurParamsToSubstrateHairInputs(
    VividFurSurfaceParams p,
    out float3 outSummaryNormalWS)
{
    VividSubstrateHairInputs hair = VividCreateDefaultSubstrateHairInputs();

    float h = saturate(p.shellHeight);
    float3 geomNormal = VividSanitizeSurfaceSummaryNormal(p.normalWS);

    float3 baseTangent = p.tangentWS - geomNormal * dot(p.tangentWS, geomNormal);
    float baseTangentLenSq = dot(baseTangent, baseTangent);
    float3 unitTangent = baseTangentLenSq > 1e-8f
        ? baseTangent * rsqrt(baseTangentLenSq)
        : float3(1.0f, 0.0f, 0.0f);
    float handedness = p.tangentHandedness < 0.0f ? -1.0f : 1.0f;
    float3 unitBitangent = cross(geomNormal, unitTangent) * handedness;

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

    // Root-to-tip albedo gradient + per-strand brightness variation (matches OpenPBR path).
    float gradT = pow(h, max(p.colorGradientPower, 0.1f));
    float strandMul = lerp(1.0f - saturate(p.strandColorVariance), 1.0f + saturate(p.strandColorVariance), p.strandVariation);
    float3 furAlbedo = saturate(lerp(saturate(p.rootColor), saturate(p.tipColor), gradT) * strandMul);

    // Volumetric root self-occlusion AO.
    float shellAO = lerp(saturate(p.rootAO), 1.0f, pow(h, max(p.aoPower, 0.1f)));
    float finalAO = saturate(p.ambientOcclusion * shellAO);

    hair.baseColor = furAlbedo;
    hair.tangentWS = VividComputeFurStrandTangent3D(
        shadingNormal,
        p.gravityDirWS,
        p.gravityStrength,
        p.combDirWS,
        p.combStrength,
        h,
        unitTangent);
    hair.roughness = clamp(lerp(p.rootRoughness, p.tipRoughness, h), 0.05f, 1.0f);
    hair.radialRoughness = clamp(p.hairRadialRoughness, 0.05f, 1.0f);
    hair.cuticleAngleRad = clamp(p.hairCuticleAngleDeg, 0.0f, 12.0f) * (3.14159265359f / 180.0f);
    hair.specular = saturate(p.specularWeight);
    hair.scatter = saturate(p.hairScatter);
    hair.backlit = saturate(p.hairBacklit);
    hair.medullaWeight = saturate(p.furMedullaWeight);
    hair.medullaRoughness = clamp(p.furMedullaRoughness, 0.10f, 1.0f);
    hair.shellDepth = h;
    hair.ambientOcclusion = finalAO;

    outSummaryNormalWS = shadingNormal;
    return hair;
}

#endif // VIVIDRP_FUR_SURFACE_INCLUDED
