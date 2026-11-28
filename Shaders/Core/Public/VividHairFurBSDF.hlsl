#ifndef VIVIDRP_HAIR_FUR_BSDF_INCLUDED
#define VIVIDRP_HAIR_FUR_BSDF_INCLUDED

// =============================================================================
// VividRP Real-Time Far-Field Hair & Fur BCSDF (Stage 3.1)
//
// Combines three physically grounded building blocks:
//   1. UE5 / Karis (2016) Far-Field Marschner BCSDF (R, TT, TRT) with cuticle
//      tilt shifts (+2*alpha, -alpha, -4*alpha) and radial-roughness-aware
//      azimuthal scattering normalized over [-pi, pi].
//   2. Zinke et al. (2008) / UE5 Dual Scattering real-time approximation driven
//      by local shell depth / AO & shadow attenuation (no Deep Shadow Map
//      required; exploits Hybrid Shell depth h directly).
//   3. Simplified Yan et al. (2015/2017) Double-Cylinder Medulla internal
//      scattering for animal fur & plush coats, coupled energy-conservatively
//      with the cortex (1 - medullaWeight) * (TT + TRT) + medullaWeight * M.
//
// Convention:
//   - T (tangentWS): unit 3D hair growth direction (root -> tip).
//   - V (viewDirWS): unit vector from shading point toward camera.
//   - L (lightDirWS): unit vector from shading point toward light.
//   - Evaluated BSDF already includes the cylindrical cosine projection; do NOT
//     multiply by an extra surface dot(N, L) at the call site.
// =============================================================================

#ifndef VIVID_HAIR_PI
    #define VIVID_HAIR_PI 3.14159265359f
#endif
#ifndef VIVID_HAIR_TWO_PI
    #define VIVID_HAIR_TWO_PI 6.28318530718f
#endif
#ifndef VIVID_HAIR_INV_PI
    #define VIVID_HAIR_INV_PI 0.31830988618f
#endif
#ifndef VIVID_HAIR_INV_TWO_PI
    #define VIVID_HAIR_INV_TWO_PI 0.15915494309f
#endif
#ifndef VIVID_HAIR_SQRT_TWO_PI
    #define VIVID_HAIR_SQRT_TWO_PI 2.50662827463f
#endif

#define VIVID_HAIR_DEFAULT_IOR 1.55f
#define VIVID_HAIR_MIN_ROUGHNESS 0.05f
#define VIVID_HAIR_MAX_CUTICLE_RAD 0.20943951f // 12 degrees

struct VividHairFurBSDFInputs
{
    float3 baseColor;          // Linear hair/fur absorption color in [0, 1]
    float3 tangentWS;          // Unit 3D strand growth tangent (root -> tip)
    float3 geomNormalWS;       // Macro surface normal (fallback when V || T)
    float  roughness;          // Longitudinal roughness beta_m in [0.05, 1.0]
    float  radialRoughness;    // Azimuthal roughness beta_n in [0.05, 1.0]
    float  cuticleAngleRad;    // Cuticle tilt alpha in radians (typical ~0.035 rad = 2 deg)
    float  specular;           // Specular reflectance scale (0.5 -> F0 = 0.0465 for keratin IOR 1.55)
    float  scatter;            // Zinke Dual Scattering multiple-scattering weight in [0, 1]
    float  backlit;            // TT transmission weight in [0, 1] (1.0 = physical)
    float  medullaWeight;      // Animal fur medulla internal scattering fraction in [0, 1]
    float  medullaRoughness;   // Medulla internal scattering softness in [0.1, 1.0]
    float  shellDepth;         // Normalized position along fur coat: 0 = deep root, 1 = outer tip
    float  ambientOcclusion;   // Combined macro + root self-occlusion AO in [0, 1]
};

struct VividHairFurBSDFResponse
{
    float3 lobeR;              // Primary achromatic specular reflection (air -> cuticle)
    float3 lobeTT;             // Forward cortex transmission (colored)
    float3 lobeTRT;            // Secondary internal cortex reflection (colored, shifted)
    float3 lobeMedulla;        // Animal fur medulla internal scattering (back + forward)
    float3 lobeDualScatter;    // Local multiple scattering approximation (Zinke Dual Scattering)
};

VividHairFurBSDFInputs VividCreateDefaultHairFurBSDFInputs()
{
    VividHairFurBSDFInputs inputs;
    inputs.baseColor = float3(0.32f, 0.20f, 0.12f);
    inputs.tangentWS = float3(0.0f, 1.0f, 0.0f);
    inputs.geomNormalWS = float3(0.0f, 0.0f, 1.0f);
    inputs.roughness = 0.35f;
    inputs.radialRoughness = 0.45f;
    inputs.cuticleAngleRad = 0.035f; // ~2.0 degrees
    inputs.specular = 0.5f;
    inputs.scatter = 1.0f;
    inputs.backlit = 1.0f;
    inputs.medullaWeight = 0.0f;
    inputs.medullaRoughness = 0.75f;
    inputs.shellDepth = 1.0f;
    inputs.ambientOcclusion = 1.0f;
    return inputs;
}

float3 VividHairSafeNormalize(float3 v, float3 fallback)
{
    float lenSq = dot(v, v);
    return lenSq > 1e-12f ? v * rsqrt(lenSq) : fallback;
}

float VividHairPow2(float x) { return x * x; }
float VividHairPow5(float x) { float x2 = x * x; return x2 * x2 * x; }

// Converts normalized melanin [0, 1] & redness [0, 1] into equivalent linear base color
// consistent with RTXCR_AbsorptionCoefficientFromMelaninNormalized / Chiang (2016).
float3 VividHairColorFromMelanin(float melanin, float melaninRedness, float radialRoughness)
{
    float m = saturate(melanin);
    float r = saturate(melaninRedness);
    float betaN = clamp(radialRoughness, VIVID_HAIR_MIN_ROUGHNESS, 1.0f);

    float melaninQty = -log(max(1.0f - m, 1e-4f));
    float eumelanin = melaninQty * (1.0f - r);
    float pheomelanin = melaninQty * r;

    const float3 eumelaninSigmaA = float3(0.506f, 0.841f, 1.653f);
    const float3 pheomelaninSigmaA = float3(0.343f, 0.733f, 1.924f);
    float3 sigmaA = max(eumelanin * eumelaninSigmaA + pheomelanin * pheomelaninSigmaA, 0.0f.xxx);

    // Exact inverse of Chiang 2016 Eq. 6 (RTXCR_AbsorptionCoefficientFromColor):
    // sqrt(sigma_a) = -ln(C) / tmp(betaN) => C = exp(-sqrt(sigma_a) * tmp(betaN))
    float b2 = betaN * betaN;
    float b3 = b2 * betaN;
    float b4 = b2 * b2;
    float b5 = b4 * betaN;
    float tmp = 5.969f - 0.215f * betaN + 2.532f * b2 - 10.73f * b3 + 5.574f * b4 + 0.245f * b5;
    return saturate(exp(-sqrt(sigmaA) * max(tmp, 1e-3f)));
}

// Keratin dielectric Schlick Fresnel (UE5 HairShadingCommon: F0 = 0.0465 at specular = 0.5 / IOR = 1.55).
float VividHairF0FromSpecular(float specular)
{
    // Maps specular = 0.5 -> 0.0465 (IOR 1.55), specular = 0 -> 0, specular = 1 -> 0.093.
    return saturate(specular) * 0.093f;
}

float VividHairFresnelSchlick(float f0, float cosTheta)
{
    float u = saturate(1.0f - saturate(cosTheta));
    return f0 + (1.0f - f0) * VividHairPow5(u);
}

// Normalized Gaussian longitudinal distribution G(B, x) on sin(theta) domain:
// Integral over x in [-inf, +inf] is 1.0; truncated smoothly over [-2, 2] so
// integral over sin(theta_i) in [-1, 1] stays <= 1.0 (energy-conserving).
float VividHairGaussianM(float B, float x)
{
    float safeB = max(B, 0.01f);
    return exp(-0.5f * (x * x) / (safeB * safeB)) / (VIVID_HAIR_SQRT_TWO_PI * safeB);
}

// Compute UE5/Kajiya virtual cylinder normal V_perp perpendicular to strand tangent T.
// Used for Lumen Diffuse GI, Reflection Probe sampling, and local multiple-scatter wrap.
float3 VividComputeHairCylinderNormal(float3 tangentWS, float3 viewDirWS, float3 fallbackNormalWS)
{
    float3 T = VividHairSafeNormalize(tangentWS, float3(0.0f, 1.0f, 0.0f));
    float3 V = VividHairSafeNormalize(viewDirWS, float3(0.0f, 0.0f, 1.0f));
    float3 vPerp = V - T * dot(V, T);
    float lenSq = dot(vPerp, vPerp);
    if (lenSq > 1e-8f)
        return vPerp * rsqrt(lenSq);
    float3 fb = fallbackNormalWS - T * dot(fallbackNormalWS, T);
    return VividHairSafeNormalize(fb, VividHairSafeNormalize(fallbackNormalWS, float3(0.0f, 0.0f, 1.0f)));
}

// Normalized Henyey-Greenstein phase function integrated onto the azimuthal circle [-pi, pi]
// (2D circular Poisson kernel: (1 - g^2) / (2*pi * (1 + g^2 - 2*g*cosPhi))).
// Exact analytical integral over phi in [-pi, pi] is identically 1.0 for every |g| < 1!
float VividHairAzimuthalPoisson(float cosPhi, float g)
{
    float gc = clamp(g, -0.96f, 0.96f);
    float g2 = gc * gc;
    float denom = max(1.0f + g2 - 2.0f * gc * clamp(cosPhi, -1.0f, 1.0f), 1e-4f);
    return VIVID_HAIR_INV_TWO_PI * (1.0f - g2) / denom;
}

// Evaluates Karis (2016) Far-Field Marschner single-scattering lobes (R, TT, TRT) +
// energy-conserving Yan (2015/2017) Medulla internal scattering + Zinke (2008) Dual Scattering.
//
// Parameters:
//   inputs    - Sanitized hair/fur material and local geometry parameters.
//   V         - Unit view direction (shading point -> camera).
//   L         - Unit light direction (shading point -> light).
//   shadow    - Direct shadow visibility in [0, 1] (used for Dual Scattering penetration).
//   outDirectShadowScale - Optional transmitted direct shadow factor for dual-scattered penetration.
VividHairFurBSDFResponse VividEvaluateHairFurDirect(
    VividHairFurBSDFInputs inputs,
    float3 V,
    float3 L,
    float shadow)
{
    VividHairFurBSDFResponse resp = (VividHairFurBSDFResponse)0;

    float3 T = VividHairSafeNormalize(inputs.tangentWS, float3(0.0f, 1.0f, 0.0f));
    float3 viewDir = VividHairSafeNormalize(V, float3(0.0f, 0.0f, 1.0f));
    float3 lightDir = VividHairSafeNormalize(L, float3(0.0f, 0.0f, 1.0f));

    float3 baseColor = saturate(inputs.baseColor);
    float3 safeBaseColor = max(baseColor, 1e-4f.xxx);
    float betaM = clamp(inputs.roughness, VIVID_HAIR_MIN_ROUGHNESS, 1.0f);
    float betaN = clamp(inputs.radialRoughness, VIVID_HAIR_MIN_ROUGHNESS, 1.0f);
    float alpha = clamp(inputs.cuticleAngleRad, 0.0f, VIVID_HAIR_MAX_CUTICLE_RAD);
    float f0 = VividHairF0FromSpecular(inputs.specular);
    float scatterWeight = saturate(inputs.scatter);
    float backlitWeight = saturate(inputs.backlit);
    float medullaWeight = saturate(inputs.medullaWeight);
    float medullaRough = clamp(inputs.medullaRoughness, 0.1f, 1.0f);
    float shellDepth = saturate(inputs.shellDepth);
    float ao = saturate(inputs.ambientOcclusion);

    // -------------------------------------------------------------------------
    // 1. Cylindrical Coordinate Angles (Marschner 2003 / Karis 2016)
    // -------------------------------------------------------------------------
    float sinThetaL = clamp(dot(T, lightDir), -1.0f, 1.0f);
    float sinThetaV = clamp(dot(T, viewDir), -1.0f, 1.0f);
    float cosThetaL = sqrt(saturate(1.0f - sinThetaL * sinThetaL));
    float cosThetaV = sqrt(saturate(1.0f - sinThetaV * sinThetaV));

    float thetaL = asin(sinThetaL);
    float thetaV = asin(sinThetaV);
    float cosThetaD = max(cos(0.5f * abs(thetaV - thetaL)), 0.05f);

    // Project V and L onto normal plane perpendicular to T for azimuthal angle phi.
    float3 Lp = lightDir - sinThetaL * T;
    float3 Vp = viewDir - sinThetaV * T;
    float LpVpLenSq = dot(Lp, Lp) * dot(Vp, Vp);
    float cosPhi = LpVpLenSq > 1e-10f
        ? clamp(dot(Lp, Vp) * rsqrt(LpVpLenSq), -1.0f, 1.0f)
        : 1.0f;
    float cosHalfPhi = sqrt(saturate(0.5f + 0.5f * cosPhi));

    // Karis 2016 modified IOR approximation for keratin n = 1.55:
    // n' = sqrt(n^2 - sin^2(theta_d)) / cos(theta_d) ≈ 1.19 / cosThetaD + 0.36 * cosThetaD
    float nPrime = 1.19f / cosThetaD + 0.36f * cosThetaD;
    float invNPrime = rcp(max(nPrime, 1.0f));

    // Longitudinal lobe standard deviations (Karis 2016 / Chiang 2016 variance ratios 1 : 1/4 : 4):
    // Mapped smoothly so betaM in [0.05, 1.0] produces visually linear highlight broadening.
    float B_R = clamp(VividHairPow2(betaM), 0.015f, 1.0f);
    float B_TT = max(0.5f * B_R, 0.01f);
    float B_TRT = min(2.0f * B_R, 1.0f);

    // Cuticle tilt shifts in sin(theta) domain (aligned with RTXCR Chiang sign convention):
    // With T pointing root -> tip and outward-tilted cuticles (+alpha):
    //   R   peaks at theta_i + theta_o = +2*alpha
    //   TT  peaks at theta_i + theta_o = -alpha
    //   TRT peaks at theta_i + theta_o = -4*alpha
    float shiftR = 2.0f * sin(alpha);
    float shiftTT = -sin(alpha);
    float shiftTRT = -4.0f * sin(alpha);

    float sinThetaSum = sinThetaL + sinThetaV;

    // -------------------------------------------------------------------------
    // 2. R Lobe: Primary Achromatic Specular Reflection (Air -> Cuticle)
    //    Integral over phi of 0.25 * cos(phi/2) is identically 1.0.
    // -------------------------------------------------------------------------
    {
        float Mp = VividHairGaussianM(B_R, sinThetaSum - shiftR);
        float Np = 0.25f * cosHalfPhi;
        float F_R = VividHairFresnelSchlick(f0, cosThetaD * cosHalfPhi);
        resp.lobeR = (Mp * Np * F_R).xxx;
    }

    // Cortex transmittance fraction left after medulla interception (Yan 2015/2017 coupling),
    // and energy-conserving Zinke Dual Scattering split between single-bounce transmitted rays
    // and locally multi-scattered fiber-volume radiance.
    float msShare = scatterWeight * lerp(0.16f, 0.34f, 1.0f - shellDepth);
    float singleInternalScale = 1.0f - msShare;
    float cortexFraction = (1.0f - medullaWeight) * singleInternalScale;

    // -------------------------------------------------------------------------
    // 3. TT Lobe: Forward Cortex Transmission (Colored, Strong Backlit Glow)
    // -------------------------------------------------------------------------
    if (cortexFraction > 1e-4f && backlitWeight > 1e-4f)
    {
        float Mp = VividHairGaussianM(B_TT, sinThetaSum - shiftTT);

        // Karis 2016 effective impact parameter for TT:
        float hTT = clamp(cosHalfPhi * (1.0f + invNPrime * (0.6f - 0.8f * cosPhi)), 0.0f, 0.999f);
        float cosGammaI_TT = sqrt(saturate(1.0f - hTT * hTT));
        float fTT = VividHairFresnelSchlick(f0, cosThetaD * cosGammaI_TT);
        float F_TT = VividHairPow2(1.0f - fTT);

        // Path-length-dependent Beer-Lambert absorption through cortex cylinder:
        float sinGammaT = clamp(hTT * invNPrime, 0.0f, 0.999f);
        float cosGammaT = sqrt(saturate(1.0f - sinGammaT * sinGammaT));
        float3 T_TT = pow(safeBaseColor, (0.5f * cosGammaT / cosThetaD).xxx);

        // Karis azimuthal distribution exp(-3.65*cosPhi - 3.98) (unit integral ~0.98)
        // smoothly broadened via exact-unit-integral Poisson kernel as radialRoughness rises.
        float D_TT_karis = exp(-3.65f * cosPhi - 3.98f);
        float gTT = lerp(-0.82f, -0.18f, betaN);
        float D_TT_broad = VividHairAzimuthalPoisson(cosPhi, gTT);
        float Np = lerp(D_TT_karis, D_TT_broad, saturate((betaN - 0.25f) / 0.75f));

        resp.lobeTT = (cortexFraction * backlitWeight) * (Mp * Np * F_TT) * T_TT;
    }

    // -------------------------------------------------------------------------
    // 4. TRT Lobe: Secondary Internal Cortex Reflection (Colored & Shifted)
    // -------------------------------------------------------------------------
    if (cortexFraction > 1e-4f)
    {
        float Mp = VividHairGaussianM(B_TRT, sinThetaSum - shiftTRT);

        // Karis 2016 effective impact parameter h_TRT = sqrt(3)/2 -> cosGammaI = 0.5:
        float fTRT = VividHairFresnelSchlick(f0, cosThetaD * 0.5f);
        float F_TRT = VividHairPow2(1.0f - fTRT) * fTRT;
        float3 T_TRT = pow(safeBaseColor, (0.8f / cosThetaD).xxx);

        // Karis azimuthal glint exp(17*cosPhi - 16.78) (integral ~0.76) blended smoothly
        // with unit-integral forward-reflected Poisson kernel as radialRoughness rises.
        float D_TRT_karis = exp(17.0f * cosPhi - 16.78f);
        float gTRT = lerp(0.75f, 0.12f, betaN);
        float D_TRT_broad = VividHairAzimuthalPoisson(cosPhi, gTRT);
        float Np = lerp(D_TRT_karis, D_TRT_broad, saturate((betaN - 0.20f) / 0.80f));

        resp.lobeTRT = cortexFraction * (Mp * Np * F_TRT) * T_TRT;
    }

    // -------------------------------------------------------------------------
    // 5. Medulla Internal Body Scattering (Simplified Yan 2015/2017 Animal Fur)
    //    Receives the (medullaWeight * (1 - F)) energy intercepted by the turbid
    //    central medulla inside animal fur shafts. Splits into:
    //      - TRTs: soft opaque back/side body scattering (velvety plush look)
    //      - TTs:  broad forward-transmitted glow (softer than sharp cortex TT)
    // -------------------------------------------------------------------------
    if (medullaWeight > 1e-4f)
    {
        // Average air-cuticle transmission into the fiber (complementary to R).
        float fAvg = VividHairFresnelSchlick(f0, cosThetaD * 0.70710678f);
        float transmitIn = 1.0f - fAvg;

        // Yan (2015/2017) turbid medulla volume scattering loses incoming longitudinal direction memory:
        // its longitudinal profile is broad/isotropic (normalized to <= 1 over sinTheta in [-1, 1])
        // with only subtle specular memory at low medullaRoughness, avoiding artificial specular-band pileup.
        float M_Iso = (2.0f / VIVID_HAIR_PI) * cosThetaL; // integral_{-1}^{1} (2/pi)*sqrt(1-x^2) dx = 1.0
        float M_Broad = VividHairGaussianM(lerp(0.60f, 1.15f, medullaRough), sinThetaSum);
        float M_Medulla = lerp(M_Iso, M_Broad, 0.20f * (1.0f - medullaRough));

        // Azimuthal split: broad back/side velvety diffuse (g > 0) + soft forward scatter (g < 0).
        float N_TRTs = VividHairAzimuthalPoisson(cosPhi, lerp(0.28f, 0.06f, medullaRough));
        float N_TTs = VividHairAzimuthalPoisson(cosPhi, lerp(-0.45f, -0.12f, medullaRough));

        // Medulla body albedo retains warm plush luminance across the full lit fur coat.
        float3 medullaAlbedo = lerp(baseColor, sqrt(safeBaseColor) * baseColor, 0.15f) * lerp(0.84f, 0.96f, medullaRough);
        float backShare = lerp(0.68f, 0.80f, medullaRough);
        float fwdShare = (1.0f - backShare) * backlitWeight;

        // Normalized spherical energy: integral over S^2 of (M_Medulla * N_poisson) <= 1.0.
        resp.lobeMedulla = (medullaWeight * singleInternalScale * transmitIn) * medullaAlbedo
            * (M_Medulla * (backShare * N_TRTs + fwdShare * N_TTs));
    }

    // -------------------------------------------------------------------------
    // 6. Zinke (2008) / UE5 Dual Scattering Real-Time Approximation
    //    Compensates multi-bounce scattering inside a dense fur/hair volume:
    //      - Uses Hybrid Shell depth (1 - shellDepth) & AO to estimate local
    //        internal bounce density without requiring a Deep Shadow Map.
    //      - Shares internal transmitted energy (msShare vs 1 - msShare) with
    //        TT/TRT/Medulla so White Furnace spherical reflectance stays <= 1.05.
    // -------------------------------------------------------------------------
    if (msShare > 1e-4f)
    {
        float3 fakeNormal = VividComputeHairCylinderNormal(T, viewDir, inputs.geomNormalWS);
        float NdotLWrap = saturate(dot(fakeNormal, lightDir) * 0.5f + 0.5f);
        float kajiyaDiffuse = cosThetaL; // 1 - |T.L| cylinder irradiance projection

        // Average multi-bounce fiber albedo (Zinke a_b / Karis multi-scatter color):
        // Higher-order bounces preserve warm saturation while lifting luminance on light/mid fur
        // without darkening brown coats (<= 1.0 for white furnace).
        float3 avgFiberAlbedo = lerp(baseColor, sqrt(safeBaseColor), 0.25f) * (baseColor / max(max(baseColor.r, max(baseColor.g, baseColor.b)), 0.15f));
        avgFiberAlbedo = min(avgFiberAlbedo, sqrt(safeBaseColor));
        float fAvg = VividHairFresnelSchlick(f0, cosThetaD * 0.70710678f);

        // Direct shadow penetration through outer fur tips (global forward multiple scattering):
        // Outer tips (high ao, high shellDepth) retain warm forward penetration even in partial shadow,
        // whereas deep roots (low ao) stay occluded.
        float shadowSat = saturate(shadow);
        float3 globalTrans = pow(safeBaseColor, (0.5f + 1.5f * (1.0f - shellDepth)).xxx);
        float3 penetratedShadow = lerp(globalTrans * (0.28f * ao * ao), 1.0f.xxx, shadowSat);

        // Normalized cylindrical local scatter phase: integral over S^2 of cosThetaL = pi^2,
        // and average of (0.5 + 0.5 * NdotLWrap) over azimuth is 0.75 -> dividing by (0.75 * pi^2)
        // yields unit spherical integral 1.0.
        float normalizedCylScatter = (kajiyaDiffuse * (0.5f + 0.5f * NdotLWrap)) / (0.75f * VIVID_HAIR_PI * VIVID_HAIR_PI);

        resp.lobeDualScatter = (msShare * (1.0f - fAvg)) * avgFiberAlbedo * normalizedCylScatter * penetratedShadow;
    }

    return resp;
}

float3 VividCombineHairFurBSDFResponse(VividHairFurBSDFResponse resp)
{
    return max(resp.lobeR + resp.lobeTT + resp.lobeTRT + resp.lobeMedulla + resp.lobeDualScatter, 0.0f.xxx);
}

// Convenience helper for deferred direct lighting:
// Single-scatter lobes (R, TT, TRT, Medulla) use direct `shadow`, while Dual Scattering
// retains warm penetrated multiple-scattering radiance in soft/partial shadow regions.
float3 VividEvaluateHairFurDirectRadiance(
    VividHairFurBSDFInputs inputs,
    float3 V,
    float3 L,
    float3 unshadowedLightRadiance,
    float shadow)
{
    VividHairFurBSDFResponse resp = VividEvaluateHairFurDirect(inputs, V, L, shadow);
    float3 singleScatter = resp.lobeR + resp.lobeTT + resp.lobeTRT + resp.lobeMedulla;
    float3 radiance = max(unshadowedLightRadiance, 0.0f.xxx);
    // Note: resp.lobeDualScatter already incorporates `penetratedShadow` for `shadow`.
    return radiance * (singleScatter * saturate(shadow) + resp.lobeDualScatter);
}

// Evaluates indirect IBL / Lumen Diffuse GI + Reflection contribution for a hair/fur pixel
// using cylinder virtual normal V_perp and energy-conserving specular/diffuse albedos.
float3 VividEvaluateHairFurIndirect(
    VividHairFurBSDFInputs inputs,
    float3 V,
    float3 diffuseIrradiance,
    float3 specularRadiance)
{
    float3 T = VividHairSafeNormalize(inputs.tangentWS, float3(0.0f, 1.0f, 0.0f));
    float3 viewDir = VividHairSafeNormalize(V, float3(0.0f, 0.0f, 1.0f));
    float sinThetaV = clamp(dot(T, viewDir), -1.0f, 1.0f);
    float cosThetaV = sqrt(saturate(1.0f - sinThetaV * sinThetaV));

    float f0 = VividHairF0FromSpecular(inputs.specular);
    float F_env = VividHairFresnelSchlick(f0, cosThetaV * 0.70710678f);
    float3 baseColor = saturate(inputs.baseColor);
    float ao = saturate(inputs.ambientOcclusion);

    // Multi-scatter boosted diffuse albedo for hair volume under omnidirectional environment light.
    float3 envDiffuseAlbedo = lerp(
        baseColor,
        sqrt(max(baseColor, 1e-4f.xxx)) * baseColor,
        0.5f * saturate(inputs.scatter));
    // Medulla increases matte diffuse body reflectance slightly while reducing glassy transmission.
    envDiffuseAlbedo = lerp(envDiffuseAlbedo, baseColor * 0.85f, saturate(inputs.medullaWeight) * 0.5f);

    float3 indirectDiffuse = max(diffuseIrradiance, 0.0f.xxx) * envDiffuseAlbedo * (1.0f - F_env) * ao;
    float3 indirectSpec = max(specularRadiance, 0.0f.xxx) * F_env * lerp(0.35f, 1.0f, ao);
    return indirectDiffuse + indirectSpec;
}

#endif // VIVIDRP_HAIR_FUR_BSDF_INCLUDED
