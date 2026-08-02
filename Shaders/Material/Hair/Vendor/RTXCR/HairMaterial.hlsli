/*
* Copyright (c) 2024-2025, NVIDIA CORPORATION. All rights reserved.
*
* Permission is hereby granted, free of charge, to any person obtaining a
* copy of this software and associated documentation files (the "Software"),
* to deal in the Software without restriction, including without limitation
* the rights to use, copy, modify, merge, publish, distribute, sublicense,
* and/or sell copies of the Software, and to permit persons to whom the
* Software is furnished to do so, subject to the following conditions:
*
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL
* THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
* FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
* DEALINGS IN THE SOFTWARE.
*/

#ifndef _RTXCR_HAIRMATERIAL_HLSLI_
#define _RTXCR_HAIRMATERIAL_HLSLI_

#include "utils/RtxcrMath.hlsli"

#define RTXCR_HairLobeType uint
#define RTXCR_HairLobeType_R             (0)
#define RTXCR_HairLobeType_TT            (1)
#define RTXCR_HairLobeType_TRT           (2)
#define RTXCR_Hair_Max_Scattering_Events (3)

#define RTXCR_HairAbsorptionModel uint
#define RTXCR_HairAbsorptionModel_Color      (0)
#define RTXCR_HairAbsorptionModel_Physics    (1)
#define RTXCR_HairAbsorptionModel_Normalized (2)

/************************************************
    Hair Surface
************************************************/

struct RTXCR_HairInteractionSurface
{
    float3 incidentRayDirection;
    float3 shadingNormal;
    float3 tangent;
};

RTXCR_HairInteractionSurface RTXCR_CreateHairInteractionSurface(
    const float3 incidentRayDirection,
    const float3 normalWorld,
    const float3 tangentWorld)
{
    const float3 biTangentWorld = cross(normalWorld, tangentWorld);
    const float3x3 hairTangentBasis = float3x3(tangentWorld, biTangentWorld, normalWorld); // TBN

    const float3 incidentRayDirectionTangentSpace = mul(hairTangentBasis, incidentRayDirection);
    RTXCR_HairInteractionSurface hairInteractionSurface;
    hairInteractionSurface.incidentRayDirection = incidentRayDirectionTangentSpace;
    hairInteractionSurface.shadingNormal = float3(0.0f, 0.0f, 1.0f);
    hairInteractionSurface.tangent = float3(0.0f, 1.0f, 0.0f);
    return hairInteractionSurface;
}

/************************************************
    Hair Material
************************************************/

struct RTXCR_HairMaterialData
{
    float3 baseColor;
    float  longitudinalRoughness; // beta_m

    float  azimuthalRoughness;    // beta_n
    float  ior;
    float  eta;
    uint   fresnelApproximation;

    uint   absorptionModel;
    float  melanin;
    float  melaninRedness;
    float  cuticleAngleInDegrees; // alpha
};

/************************************************
    Hair Interaction - Chiang BSDF
************************************************/

struct RTXCR_HairMaterialInteraction
{
    float  h;
    float  gammaI;
    float3 absorptionCoefficient;

    float  ior;
    float  eta;
    uint   fresnelApproximation;

    float  logisticDistributionScalar; // s

    float v[RTXCR_Hair_Max_Scattering_Events + 1];

    float sin2kAlpha[RTXCR_Hair_Max_Scattering_Events];
    float cos2kAlpha[RTXCR_Hair_Max_Scattering_Events];
};

// Compute Longitudinal Roughness Variance
void RTXCR_ComputeRoughnessVariance(const float betaM, inout RTXCR_HairMaterialInteraction hairMaterialInteraction)
{
    float tmp = 0.726f * betaM + 0.812f * betaM * betaM + 3.7f * pow(betaM, 20.f);
    hairMaterialInteraction.v[0] = max(tmp * tmp, 1e-7f);
    hairMaterialInteraction.v[1] = 0.25f * hairMaterialInteraction.v[0];
    hairMaterialInteraction.v[2] = 4 * hairMaterialInteraction.v[0];
    [unroll]
    for (uint p = 3; p <= RTXCR_Hair_Max_Scattering_Events; ++p)
    {
        hairMaterialInteraction.v[p] = hairMaterialInteraction.v[2];
    }
}

// Compute azimuthally offset h
float RTXCR_CalculateAzimuthallyDistance(const RTXCR_HairInteractionSurface hairInteractionSurface)
{
    // Project wi to the (B, N) plane
    float3 wiProj = normalize(hairInteractionSurface.incidentRayDirection -
        dot(hairInteractionSurface.incidentRayDirection, hairInteractionSurface.tangent) * hairInteractionSurface.tangent);
    // Calculate the vector that perpendicular with projected wi on (B, N) plane
    float3 wiProjPerpendicular = cross(wiProj, hairInteractionSurface.tangent);
    // h = sin(Gamma) = cos(pi/2 - Gamma) = dot(N, Wi_Proj_Prependicular)
    return dot(hairInteractionSurface.shadingNormal, wiProjPerpendicular);
}

// Mapping from color to absorption coefficient.
float3 RTXCR_AbsorptionCoefficientFromColor(const float3 color, const float betaN)
{
    const float tmp = 5.969f - 0.215f * betaN + 2.532f * betaN * betaN - 10.73f * pow(betaN, 3.0f) + 5.574f * pow(betaN, 4.0f) + 0.245f * pow(betaN, 5.0f);
    const float3 sqrtAbsorptionCoefficient = log(max(color, 1e-4f)) / tmp;
    return sqrtAbsorptionCoefficient * sqrtAbsorptionCoefficient;
}

// Mapping from hair melanin to absorption coefficient
float3 RTXCR_ComputeAbsorptionFromMelanin(float eumelanin, float pheomelanin)
{
    return max(eumelanin * float3(0.506f, 0.841f, 1.653f) + pheomelanin * float3(0.343f, 0.733f, 1.924f), float3(0.0f, 0.0f, 0.0f));
}

float3 RTXCR_AbsorptionCoefficientFromMelanin(const float melanin_concentration, const float melanin_redness)
{
    float melanin_concentration_value = melanin_concentration;
    float melanin_gamma = 2.4f;
    float melanin = melanin_concentration_value * melanin_concentration_value * melanin_gamma;
    float eumelanin = melanin * (1.0f - melanin_redness);
    float pheomelanin = melanin * melanin_redness;
    return RTXCR_ComputeAbsorptionFromMelanin(eumelanin, pheomelanin);
}

float3 RTXCR_AbsorptionCoefficientFromMelaninNormalized(const float melanin, const float melaninRedness)
{
    const float melaninQty = -log(max(1.0f - melanin, 0.0001f));
    const float eumelanin = melaninQty * (1.0f - melaninRedness);
    const float pheomelanin = melaninQty * melaninRedness;
    // Adjusted sigma coefficient for range [0, 1]
    const float3 eumelaninSigmaA = float3(0.506f, 0.841f, 1.653f);
    const float3 pheomelaninSigmaA = float3(0.343f, 0.733f, 1.924f);
    return eumelanin.rrr * eumelaninSigmaA + pheomelanin.rrr * pheomelaninSigmaA;
}

float3 RTXCR_ComputeAbsorptionCoefficient(const RTXCR_HairMaterialData hairMaterialData)
{
    switch (hairMaterialData.absorptionModel)
    {
        case RTXCR_HairAbsorptionModel_Color:
            return RTXCR_AbsorptionCoefficientFromColor(hairMaterialData.baseColor, hairMaterialData.azimuthalRoughness);
        case RTXCR_HairAbsorptionModel_Physics:
            return RTXCR_AbsorptionCoefficientFromMelanin(hairMaterialData.melanin, hairMaterialData.melaninRedness);
        case RTXCR_HairAbsorptionModel_Normalized:
            return RTXCR_AbsorptionCoefficientFromMelaninNormalized(hairMaterialData.melanin, hairMaterialData.melaninRedness);
    }
    return float3(0.0f, 0.0f, 0.0f);
}

// Compute azimuthal logistic scale factor
float RTXCR_ComputelogisticDistributionScalar(const float betaN)
{
    return max(RTXCR_PI_OVER_EIGHT * (0.265f * betaN + 1.194f * betaN * betaN + 5.372f * pow(betaN, 22.0f)), 1e-7f);
}

// Compute the scales that caused by the angle between hair cuticle and hair surface
//    /    /    /  <-- Hair Cuticles
//   /    /    /
//  /____/____/____   <-- Hair Surface
//
void RTXCR_ComputeHairCuticleScales(const float cuticleAngleInDegrees, inout RTXCR_HairMaterialInteraction hairMaterialInteraction)
{
    hairMaterialInteraction.sin2kAlpha[0] = sin(cuticleAngleInDegrees / 180.0f * RTXCR_PI);
    hairMaterialInteraction.cos2kAlpha[0] = sqrt(saturate(1.f - hairMaterialInteraction.sin2kAlpha[0] * hairMaterialInteraction.sin2kAlpha[0]));
    [unroll]
    for (uint i = 1; i < 3; i++)
    {
        // sin(2*Theta) = 2 * sin(Theta) * cos(Theta)
        hairMaterialInteraction.sin2kAlpha[i] =
            2 * hairMaterialInteraction.cos2kAlpha[i - 1] * hairMaterialInteraction.sin2kAlpha[i - 1];
        // cos(2*Theta) = (cos(Theta))^2 - (sin(Theta))^2
        hairMaterialInteraction.cos2kAlpha[i] =
            hairMaterialInteraction.cos2kAlpha[i - 1] * hairMaterialInteraction.cos2kAlpha[i - 1] -
            hairMaterialInteraction.sin2kAlpha[i - 1] * hairMaterialInteraction.sin2kAlpha[i - 1];
    }
}

RTXCR_HairMaterialInteraction RTXCR_CreateHairMaterialInteraction(
    const RTXCR_HairMaterialData hairMaterialData,
    const RTXCR_HairInteractionSurface hairInteractionSurface)
{
    RTXCR_HairMaterialInteraction hairMaterialInteraction;
    hairMaterialInteraction.h = RTXCR_CalculateAzimuthallyDistance(hairInteractionSurface);
    hairMaterialInteraction.gammaI = asin(clamp(hairMaterialInteraction.h, -1.0f, 1.0f));
    hairMaterialInteraction.absorptionCoefficient = RTXCR_ComputeAbsorptionCoefficient(hairMaterialData);
    hairMaterialInteraction.fresnelApproximation = hairMaterialData.fresnelApproximation;
    hairMaterialInteraction.ior = hairMaterialData.ior;
    hairMaterialInteraction.eta = hairMaterialData.eta;
    hairMaterialInteraction.logisticDistributionScalar = RTXCR_ComputelogisticDistributionScalar(hairMaterialData.azimuthalRoughness);
    // Compute hairMaterialInteraction.v
    RTXCR_ComputeRoughnessVariance(hairMaterialData.longitudinalRoughness, hairMaterialInteraction);
    // Compute Hair Scales
    RTXCR_ComputeHairCuticleScales(hairMaterialData.cuticleAngleInDegrees, hairMaterialInteraction);
    return hairMaterialInteraction;
}

/************************************************
    Hair Interaction - Separate Chiang BSDF
************************************************/

struct RTXCR_HairMaterialSeparateChiangData
{
    RTXCR_HairMaterialData base;

    float longitudinalRoughnessTT;
    float longitudinalRoughnessTRT;
    float azimuthalRoughnessTT;
    float azimuthalRoughnessTRT;
};

struct RTXCR_HairMaterialSeparateChiangInteraction
{
    float h;
    float gammaI;
    float3 absorptionCoefficient;

    float ior;
    float eta;
    uint fresnelApproximation;

    float logisticDistributionScalar[RTXCR_Hair_Max_Scattering_Events + 1]; // s

    float v[RTXCR_Hair_Max_Scattering_Events + 1];

    float sin2kAlpha[RTXCR_Hair_Max_Scattering_Events];
    float cos2kAlpha[RTXCR_Hair_Max_Scattering_Events];
};

float RTXCR_ComputeRoughnessVarianceSeparateChiang(const float betaM)
{
    const float tmp = 0.726f * betaM + 0.812f * betaM * betaM + 3.7f * pow(betaM, 20.f);
    return max(tmp * tmp, 1e-7f);
}

void RTXCR_ComputeHairCuticleScalesSeparateChiang(const float cuticleAngleInDegrees, inout RTXCR_HairMaterialSeparateChiangInteraction hairMaterialSeparateChiangInteraction)
{
    hairMaterialSeparateChiangInteraction.sin2kAlpha[0] = sin(cuticleAngleInDegrees / 180.0f * RTXCR_PI);
    hairMaterialSeparateChiangInteraction.cos2kAlpha[0] = sqrt(saturate(1.f - hairMaterialSeparateChiangInteraction.sin2kAlpha[0] * hairMaterialSeparateChiangInteraction.sin2kAlpha[0]));
    [unroll]
    for (uint i = 1; i < 3; i++)
    {
        // sin(2*Theta) = 2 * sin(Theta) * cos(Theta)
        hairMaterialSeparateChiangInteraction.sin2kAlpha[i] =
            2 * hairMaterialSeparateChiangInteraction.cos2kAlpha[i - 1] * hairMaterialSeparateChiangInteraction.sin2kAlpha[i - 1];
        // cos(2*Theta) = (cos(Theta))^2 - (sin(Theta))^2
        hairMaterialSeparateChiangInteraction.cos2kAlpha[i] =
            hairMaterialSeparateChiangInteraction.cos2kAlpha[i - 1] * hairMaterialSeparateChiangInteraction.cos2kAlpha[i - 1] -
            hairMaterialSeparateChiangInteraction.sin2kAlpha[i - 1] * hairMaterialSeparateChiangInteraction.sin2kAlpha[i - 1];
    }
}

RTXCR_HairMaterialSeparateChiangInteraction RTXCR_CreateHairMaterialSeparateChiangInteraction(
    const RTXCR_HairMaterialSeparateChiangData hairMaterialSeparateChiangData,
    const RTXCR_HairInteractionSurface hairInteractionSurface)
{
    RTXCR_HairMaterialSeparateChiangInteraction hairMaterialSeparateChiangInteraction;
    hairMaterialSeparateChiangInteraction.h = RTXCR_CalculateAzimuthallyDistance(hairInteractionSurface);
    hairMaterialSeparateChiangInteraction.gammaI = asin(clamp(hairMaterialSeparateChiangInteraction.h, -1.0f, 1.0f));
    hairMaterialSeparateChiangInteraction.absorptionCoefficient = RTXCR_ComputeAbsorptionCoefficient(hairMaterialSeparateChiangData.base);
    hairMaterialSeparateChiangInteraction.fresnelApproximation = hairMaterialSeparateChiangData.base.fresnelApproximation;
    hairMaterialSeparateChiangInteraction.ior = hairMaterialSeparateChiangData.base.ior;
    hairMaterialSeparateChiangInteraction.eta = hairMaterialSeparateChiangData.base.eta;
    hairMaterialSeparateChiangInteraction.logisticDistributionScalar[0] = RTXCR_ComputelogisticDistributionScalar(hairMaterialSeparateChiangData.base.azimuthalRoughness);
    hairMaterialSeparateChiangInteraction.logisticDistributionScalar[1] = RTXCR_ComputelogisticDistributionScalar(hairMaterialSeparateChiangData.azimuthalRoughnessTT);
    hairMaterialSeparateChiangInteraction.logisticDistributionScalar[2] = RTXCR_ComputelogisticDistributionScalar(hairMaterialSeparateChiangData.azimuthalRoughnessTRT);
    hairMaterialSeparateChiangInteraction.logisticDistributionScalar[3] = hairMaterialSeparateChiangInteraction.logisticDistributionScalar[2];
    // Compute hairMaterialInteraction.v
    hairMaterialSeparateChiangInteraction.v[0] = RTXCR_ComputeRoughnessVarianceSeparateChiang(hairMaterialSeparateChiangData.base.longitudinalRoughness);
    hairMaterialSeparateChiangInteraction.v[1] = RTXCR_ComputeRoughnessVarianceSeparateChiang(hairMaterialSeparateChiangData.longitudinalRoughnessTT);
    hairMaterialSeparateChiangInteraction.v[2] = RTXCR_ComputeRoughnessVarianceSeparateChiang(hairMaterialSeparateChiangData.longitudinalRoughnessTRT);
    hairMaterialSeparateChiangInteraction.v[3] = hairMaterialSeparateChiangInteraction.v[2];
    // Compute Hair Scales
    RTXCR_ComputeHairCuticleScalesSeparateChiang(hairMaterialSeparateChiangData.base.cuticleAngleInDegrees, hairMaterialSeparateChiangInteraction);
    return hairMaterialSeparateChiangInteraction;
}

/************************************************
    Hair Interaction - Farfield BSDF
************************************************/

struct RTXCR_HairMaterialInteractionBcsdf
{
    float3 diffuseReflectionTint;
    float diffuseReflectionWeight;

    float roughness;
    float3 absorptionCoefficient;

    float ior;
    float cuticleAngle;
};

RTXCR_HairMaterialInteractionBcsdf RTXCR_CreateHairMaterialInteractionBcsdf(
    const RTXCR_HairMaterialData hairMaterialData,
    const float3                 diffuseRefelctionTint,
    const float                  diffuseReflectionWeight,
    const float                  roughness)
{
    RTXCR_HairMaterialInteractionBcsdf hairMaterialInteractionBcsdf;
    hairMaterialInteractionBcsdf.diffuseReflectionTint = diffuseRefelctionTint;
    hairMaterialInteractionBcsdf.diffuseReflectionWeight = diffuseReflectionWeight;
    hairMaterialInteractionBcsdf.roughness = roughness;
    hairMaterialInteractionBcsdf.absorptionCoefficient = RTXCR_ComputeAbsorptionCoefficient(hairMaterialData);
    hairMaterialInteractionBcsdf.ior = hairMaterialData.ior;
    hairMaterialInteractionBcsdf.cuticleAngle = radians(hairMaterialData.cuticleAngleInDegrees);
    return hairMaterialInteractionBcsdf;
}

#endif

