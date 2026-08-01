#ifndef VIVIDRP_HAIR_CHIANG_ADAPTER_INCLUDED
#define VIVIDRP_HAIR_CHIANG_ADAPTER_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Material/Hair/HairInput.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/Hair/HairGeometry.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/Hair/Vendor/RTXCR/HairChiangBSDF.hlsli"

struct VividHairPreparedChiang
{
    RTXCR_HairMaterialInteraction interaction;
    float3x3 worldToLocal;
    float3 viewDirectionLocal;
};

RTXCR_HairMaterialData VividHairCreateChiangMaterialData()
{
    RTXCR_HairMaterialData material;
    material.baseColor = max(VividHairGetBaseColor(), 1e-4);
    material.longitudinalRoughness =
        VividHairGetLongitudinalRoughness();
    material.azimuthalRoughness = VividHairGetAzimuthalRoughness();
    material.ior = VividHairGetIor();
    material.eta = rcp(material.ior);
    material.fresnelApproximation =
        _HairFresnelApproximation > 0.5 ? 1u : 0u;
    material.absorptionModel = (uint)clamp(
        round(_HairAbsorptionModel),
        0.0,
        2.0);
    material.melanin = saturate(_HairMelanin);
    material.melaninRedness = saturate(_HairMelaninRedness);
    material.cuticleAngleInDegrees = clamp(
        _HairCuticleAngleDegrees,
        0.0,
        10.0);
    return material;
}

VividHairPreparedChiang VividHairPrepareChiang(
    VividHairSurfaceGeometry geometry,
    float3 viewDirectionWS)
{
    float3 tangent = normalize(geometry.tangentWS);
    float3 normal = normalize(geometry.radialNormalWS);
    float3 bitangent = normalize(cross(normal, tangent));
    normal = normalize(cross(tangent, bitangent));

    VividHairPreparedChiang prepared;
    prepared.worldToLocal = float3x3(tangent, bitangent, normal);
    prepared.viewDirectionLocal = normalize(
        mul(prepared.worldToLocal, normalize(viewDirectionWS)));

    RTXCR_HairInteractionSurface surface;
    surface.incidentRayDirection = prepared.viewDirectionLocal;
    surface.shadingNormal = float3(0.0, 0.0, 1.0);
    surface.tangent = float3(1.0, 0.0, 0.0);
    prepared.interaction = RTXCR_CreateHairMaterialInteraction(
        VividHairCreateChiangMaterialData(),
        surface);
    return prepared;
}

float VividHairEvaluateChiangPdfLocal(
    RTXCR_HairMaterialInteraction interaction,
    float3 wi,
    float3 wo)
{
    float sinThetaO = clamp(wo.x, -1.0, 1.0);
    float cosThetaO = RTXCR_Sqrt01(1.0 - sinThetaO * sinThetaO);
    float phiO = RTXCR_Atan2safe(wo.z, wo.y);
    float sinThetaI = clamp(wi.x, -1.0, 1.0);
    float cosThetaI = RTXCR_Sqrt01(1.0 - sinThetaI * sinThetaI);
    float phiI = RTXCR_Atan2safe(wi.z, wi.y);
    float dphi = phiI - phiO;

    float apPdf[RTXCR_Hair_Max_Scattering_Events + 1];
    RTXCR_ComputeApPdf(interaction, max(cosThetaO, 1e-6), apPdf);
    float safeCosThetaO = max(cosThetaO, 1e-6);
    float etaPrime = RTXCR_Sqrt0(
        interaction.ior * interaction.ior
        - sinThetaO * sinThetaO) / safeCosThetaO;
    float sinGammaT = interaction.h / max(etaPrime, 1e-6);
    float gammaT = asin(clamp(sinGammaT, -1.0, 1.0));

    float pdf = 0.0;
    [unroll]
    for (uint lobe = 0u;
         lobe < RTXCR_Hair_Max_Scattering_Events;
         ++lobe)
    {
        float sinThetaIp;
        float cosThetaIp;
        if (lobe == 0u)
        {
            sinThetaIp = sinThetaI * interaction.cos2kAlpha[1]
                - cosThetaI * interaction.sin2kAlpha[1];
            cosThetaIp = cosThetaI * interaction.cos2kAlpha[1]
                + sinThetaI * interaction.sin2kAlpha[1];
        }
        else if (lobe == 1u)
        {
            sinThetaIp = sinThetaI * interaction.cos2kAlpha[0]
                + cosThetaI * interaction.sin2kAlpha[0];
            cosThetaIp = cosThetaI * interaction.cos2kAlpha[0]
                - sinThetaI * interaction.sin2kAlpha[0];
        }
        else
        {
            sinThetaIp = sinThetaI * interaction.cos2kAlpha[2]
                + cosThetaI * interaction.sin2kAlpha[2];
            cosThetaIp = cosThetaI * interaction.cos2kAlpha[2]
                - sinThetaI * interaction.sin2kAlpha[2];
        }

        pdf += RTXCR_MP(
                abs(cosThetaIp),
                cosThetaO,
                sinThetaIp,
                sinThetaO,
                interaction.v[lobe])
            * apPdf[lobe]
            * RTXCR_NP(
                dphi,
                lobe,
                interaction.logisticDistributionScalar,
                interaction.gammaI,
                gammaT);
    }

    pdf += RTXCR_MP(
            cosThetaI,
            cosThetaO,
            sinThetaI,
            sinThetaO,
            interaction.v[RTXCR_Hair_Max_Scattering_Events])
        * apPdf[RTXCR_Hair_Max_Scattering_Events]
        * RTXCR_ONE_OVER_TWO_PI;
    return VividHairIsFiniteScalar(pdf) ? max(pdf, 0.0) : 0.0;
}

float3 VividHairEvaluateChiang(
    VividHairPreparedChiang prepared,
    float3 lightDirectionWS,
    out float pdf)
{
    float3 lightDirectionLocal = normalize(
        mul(prepared.worldToLocal, normalize(lightDirectionWS)));
    pdf = VividHairEvaluateChiangPdfLocal(
        prepared.interaction,
        lightDirectionLocal,
        prepared.viewDirectionLocal);
    float3 value = RTXCR_HairChiangBsdfEval(
        prepared.interaction,
        lightDirectionLocal,
        prepared.viewDirectionLocal);
    return VividHairIsFinite3(value) ? max(value, 0.0) : 0.0;
}

bool VividHairSampleChiang(
    VividHairPreparedChiang prepared,
    float4 randomSample,
    out float3 directionWS,
    out float3 value,
    out float pdf,
    out uint lobe)
{
    float2 randomPairs[2];
    randomPairs[0] = saturate(randomSample.xy);
    randomPairs[1] = saturate(randomSample.zw);
    float3 directionLocal;
    bool sampled = RTXCR_SampleChiangBsdf(
        prepared.interaction,
        prepared.viewDirectionLocal,
        randomPairs,
        directionLocal,
        pdf,
        value,
        lobe);
    directionWS = normalize(
        mul(transpose(prepared.worldToLocal), directionLocal));
    bool finite = VividHairIsFinite3(directionWS)
        && VividHairIsFinite3(value)
        && VividHairIsFiniteScalar(pdf);
    if (!sampled || !finite || pdf <= 0.0 || !any(value > 0.0))
    {
        directionWS = 0.0;
        value = 0.0;
        pdf = 0.0;
        lobe = RTXCR_HairLobeType_R;
        return false;
    }

    value = max(value, 0.0);
    return true;
}

#endif
