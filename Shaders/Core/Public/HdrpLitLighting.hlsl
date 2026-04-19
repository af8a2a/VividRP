#ifndef VIVIDRP_HDRP_LIT_LIGHTING_INCLUDED
#define VIVIDRP_HDRP_LIT_LIGHTING_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/LTCAreaLight.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/PreIntegratedFGD.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/VividProbeVolume.hlsl"

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"


static const float3 kVividDielectricF0 = float3(0.04, 0.04, 0.04);
static const float kVividClearCoatIor = 1.5;
static const float kVividClearCoatIeta = 1.0 / kVividClearCoatIor;
static const float kVividClearCoatF0 = 0.04;
static const float kVividClearCoatRoughness = 0.01;

TEXTURECUBE(_VividSkyIBLCubemap);
SAMPLER(sampler_VividSkyIBLCubemap);
float4 _VividSkyIBLTint;
float4 _VividSkyIBLParams;

struct VividLitBSDFData
{
    float3 diffuseColor;
    float3 fresnel0;
    float fresnel90;
    float perceptualRoughness;
    float roughness;
    float coatMask;
    float coatRoughness;
};

struct VividPreLightData
{
    float NdotV;
    float partLambdaV;
    float3 iblR;
    float iblPerceptualRoughness;
    float3 specularFGD;
    float diffuseFGD;
    float reflectivity;
    float energyCompensation;
    float3x3 orthoBasisViewNormal;
    float3x3 ltcTransformDiffuse;
    float3x3 ltcTransformSpecular;
    float3x3 ltcTransformCoat;
    float coatIblF;
};

struct VividCBSDF
{
    float3 diffuse;
    float3 specular;
};

struct VividDirectLighting
{
    float3 diffuse;
    float3 specular;
};

struct VividIndirectLighting
{
    float3 diffuse;
    float3 specularReflected;
};

struct VividAggregateLighting
{
    VividDirectLighting direct;
    VividIndirectLighting indirect;
};

struct VividLightLoopOutput
{
    float3 diffuseLighting;
    float3 specularLighting;
};


float VividGetLuminance(float3 color)
{
    return Luminance(color);
}

bool VividHasSkyIBL()
{
    return _VividSkyIBLParams.w > 0.5;
}

float3 VividRotateAroundYAxis(float3 directionWS, float rotationDegrees)
{
    float rotationRadians = radians(rotationDegrees);
    float s = 0.0;
    float c = 1.0;
    sincos(rotationRadians, s, c);

    return float3(
        c * directionWS.x - s * directionWS.z,
        directionWS.y,
        s * directionWS.x + c * directionWS.z);
}

float3 VividGetReflectionVector(float3 viewDirectionWS, float3 normalWS)
{
    return reflect(-viewDirectionWS, normalWS);
}

float3 VividSampleSkyIBL(float3 directionWS, float perceptualRoughness)
{
    if (!VividHasSkyIBL())
        return float3(0.0, 0.0, 0.0);

    uint maxMip = (uint)max(_VividSkyIBLParams.z, 0.0);
    float mipLevel = PerceptualRoughnessToMipmapLevel(saturate(perceptualRoughness), maxMip);
    float3 rotatedDirectionWS = VividRotateAroundYAxis(directionWS, _VividSkyIBLParams.y);
    float3 envLighting = float3(0.0, 0.0, 0.0);
    envLighting = SAMPLE_TEXTURECUBE_LOD(_VividSkyIBLCubemap, sampler_VividSkyIBLCubemap, rotatedDirectionWS, mipLevel).rgb;
    return envLighting * _VividSkyIBLTint.rgb * _VividSkyIBLParams.x;
}

VividLitBSDFData BuildVividHDRPLitBSDFData(VividGBufferSurfaceData surfaceData)
{
    VividLitBSDFData bsdfData = (VividLitBSDFData)0;
    bsdfData.diffuseColor = surfaceData.baseColor * (1.0 - surfaceData.metallic);
    bsdfData.fresnel0 = lerp(kVividDielectricF0, surfaceData.baseColor, surfaceData.metallic);
    bsdfData.fresnel90 = 1.0;
    bsdfData.perceptualRoughness = GetPerceptualRoughnessFromLinearRoughness(surfaceData.linearRoughness);
    bsdfData.roughness =  ClampRoughnessForAnalyticalLights(surfaceData.linearRoughness);
    bsdfData.coatMask = 0.0;
    bsdfData.coatRoughness = kVividClearCoatRoughness;

    if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_CLEARCOAT)
    {
        bsdfData.coatMask = saturate(surfaceData.customData);

        if (bsdfData.coatMask > 0.0)
        {
            float ieta = lerp(1.0, kVividClearCoatIeta, bsdfData.coatMask);
            float coatRoughnessScale = ieta * ieta;
            float sigma = RoughnessToVariance(bsdfData.roughness);
            float coatAdjustedRoughness = VarianceToRoughness(sigma * coatRoughnessScale);
            bsdfData.perceptualRoughness = RoughnessToPerceptualRoughness(coatAdjustedRoughness);
            bsdfData.roughness = ClampRoughnessForAnalyticalLights(
                PerceptualRoughnessToRoughness(bsdfData.perceptualRoughness));
        }
    }

    return bsdfData;
}

VividPreLightData GetVividPreLightData(
    float3 viewDirectionWS,
    VividGBufferSurfaceData surfaceData,
    inout VividLitBSDFData bsdfData)
{
    VividPreLightData preLightData = (VividPreLightData)0;
    float clampedNdotV = 0.0;
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);

    preLightData.NdotV = dot(surfaceData.normalWS, normalizedViewDirectionWS);
    preLightData.iblPerceptualRoughness = bsdfData.perceptualRoughness;
    clampedNdotV = saturate( ClampNdotV(preLightData.NdotV));
    preLightData.partLambdaV =  GetSmithJointGGXPartLambdaV(clampedNdotV, bsdfData.roughness);

    GetPreIntegratedFGDGGXAndDisneyDiffuse(
        clampedNdotV,
        preLightData.iblPerceptualRoughness,
        bsdfData.fresnel0,
        bsdfData.fresnel90,
        preLightData.specularFGD,
        preLightData.diffuseFGD,
        preLightData.reflectivity);

    preLightData.energyCompensation = rcp(max(preLightData.reflectivity, 1e-4)) - 1.0;
    preLightData.iblR = VividGetReflectionVector(normalizedViewDirectionWS, surfaceData.normalWS);
    preLightData.orthoBasisViewNormal = GetOrthoBasisViewNormal(
        normalizedViewDirectionWS,
        surfaceData.normalWS,
        preLightData.NdotV);
    preLightData.ltcTransformDiffuse = SampleLtcMatrix(
        bsdfData.perceptualRoughness,
        clampedNdotV,
        VIVID_LTC_LIGHTING_MODEL_DISNEY_DIFFUSE);
    preLightData.ltcTransformSpecular = SampleLtcMatrix(
        bsdfData.perceptualRoughness,
        clampedNdotV,
        VIVID_LTC_LIGHTING_MODEL_GGX);
    preLightData.ltcTransformCoat = 0.0;
    preLightData.coatIblF = 0.0;

    if (bsdfData.coatMask > 0.0)
    {
        preLightData.coatIblF = F_Schlick(kVividClearCoatF0, 1.0, clampedNdotV) * bsdfData.coatMask;
        preLightData.ltcTransformCoat = SampleLtcMatrix(
            RoughnessToPerceptualRoughness(bsdfData.coatRoughness),
            clampedNdotV,
            VIVID_LTC_LIGHTING_MODEL_GGX);
    }

    return preLightData;
}

VividPreLightData InitVividPreLightData(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 viewDirectionWS)
{
    return GetVividPreLightData(viewDirectionWS, surfaceData, bsdfData);
}

float3 ApplyVividSpecularEnergyCompensation(
    float3 specularLighting,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData)
{
    return specularLighting * (1.0 + bsdfData.fresnel0 * preLightData.energyCompensation);
}

float3 FinalizeVividSpecularLighting(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 specularLighting)
{
    return surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC
        ? specularLighting
        : ApplyVividSpecularEnergyCompensation(specularLighting, bsdfData, preLightData);
}

VividCBSDF EvaluateVividLitBSDF(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS,
    float3 lightDirectionWS)
{
    VividCBSDF cbsdf = (VividCBSDF)0;
    float nDotL = dot(surfaceData.normalWS, lightDirectionWS);
    if (nDotL <= 0.0)
        return cbsdf;

    float clampedNdotV = ClampNdotV(preLightData.NdotV);
    float clampedNdotL = saturate(nDotL);
    float lDotV = 0.0;
    float nDotH = 0.0;
    float lDotH = 0.0;
    float invLenLV = 0.0;
    GetBSDFAngle(viewDirectionWS, lightDirectionWS, nDotL, preLightData.NdotV, lDotV, nDotH, lDotH, invLenLV);

    float3 fresnel = F_Schlick(bsdfData.fresnel0, bsdfData.fresnel90, lDotH);
    float3 specular = fresnel * DV_SmithJointGGX(
        nDotH,
        clampedNdotL,
        clampedNdotV,
        bsdfData.roughness,
        preLightData.partLambdaV);
    float diffuse = DisneyDiffuse(clampedNdotV, clampedNdotL, lDotV, bsdfData.perceptualRoughness);

    if (bsdfData.coatMask > 0.0)
    {
        float coatFresnel = F_Schlick(kVividClearCoatF0, 1.0, lDotH) * bsdfData.coatMask;
        specular *= Sq(1.0 - coatFresnel);

        float coatPartLambdaV = GetSmithJointGGXPartLambdaV(clampedNdotV, bsdfData.coatRoughness);
        specular += coatFresnel * DV_SmithJointGGX(
            nDotH,
            clampedNdotL,
            clampedNdotV,
            bsdfData.coatRoughness,
            coatPartLambdaV);

        diffuse *= lerp(1.0, 1.0 - coatFresnel, bsdfData.coatMask);
    }

    cbsdf.diffuse = diffuse * clampedNdotL;
    cbsdf.specular = specular * clampedNdotL;
    return cbsdf;
}

float3 EvaluateVividLitDirectLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS,
    float3 lightDirectionWS)
{
    VividCBSDF cbsdf = EvaluateVividLitBSDF(surfaceData, bsdfData, preLightData, viewDirectionWS, lightDirectionWS);
    return bsdfData.diffuseColor * cbsdf.diffuse
        + ApplyVividSpecularEnergyCompensation(cbsdf.specular, bsdfData, preLightData);
}

float3 EvaluateVividLitDirectLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 viewDirectionWS,
    float3 lightDirectionWS)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividPreLightData preLightData = GetVividPreLightData(normalizedViewDirectionWS, surfaceData, bsdfData);
    return EvaluateVividLitDirectLight(surfaceData, bsdfData, preLightData, normalizedViewDirectionWS, lightDirectionWS);
}

VividCBSDF EvaluateVividFabricBSDF(
    VividGBufferSurfaceData surfaceData,
    float3 viewDirectionWS,
    float3 lightDirectionWS)
{
    VividCBSDF cbsdf = (VividCBSDF)0;
    float nDotV = 0.0;
    float nDotL = 0.0;
    float lDotV = 0.0;
    float nDotH = 0.0;
    float lDotH = 0.0;
    float invLenLV = 0.0;
    nDotV = dot(surfaceData.normalWS, viewDirectionWS);
    nDotL = dot(surfaceData.normalWS, lightDirectionWS);
    GetBSDFAngle(viewDirectionWS, lightDirectionWS, nDotL, nDotV, lDotV, nDotH, lDotH, invLenLV);

    if (nDotL <= 0.0)
        return cbsdf;

    float clampedNdotV = ClampNdotV(nDotV);
    float clampedNdotL = saturate(nDotL);
    float roughness =  ClampRoughnessForAnalyticalLights(surfaceData.linearRoughness);
    float fuzzAmount = saturate(surfaceData.customData);
    float3 diffuseColor = surfaceData.baseColor * (1.0 - surfaceData.metallic);
    float3 baseSpecular = lerp(kVividDielectricF0, surfaceData.baseColor, surfaceData.metallic);
    float luminance = VividGetLuminance(surfaceData.baseColor);
    float3 sheenTint = lerp(luminance.xxx, surfaceData.baseColor, 0.35);
    float3 fabricFresnel0 = lerp(baseSpecular, sheenTint, fuzzAmount);
    float3 specular = fabricFresnel0 *  D_Charlie(nDotH, roughness) *  V_Ashikhmin(clampedNdotL, clampedNdotV) * fuzzAmount;
    float diffuse =  FabricLambert(roughness);
    cbsdf.diffuse = diffuse * clampedNdotL;
    cbsdf.specular = specular * clampedNdotL;
    return cbsdf;
}

VividCBSDF EvaluateVividFabricDirectLight(
    VividGBufferSurfaceData surfaceData,
    float3 viewDirectionWS,
    float3 lightDirectionWS)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividCBSDF cbsdf = EvaluateVividFabricBSDF(surfaceData, normalizedViewDirectionWS, lightDirectionWS);
    float3 diffuseColor = surfaceData.baseColor * (1.0 - surfaceData.metallic);
    cbsdf.diffuse = diffuseColor * cbsdf.diffuse + cbsdf.specular;
    
    return cbsdf;

}

VividCBSDF EvaluateBSDF(
    float3 viewDirectionWS,
    float3 lightDirectionWS,
    VividPreLightData preLightData,
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData)
{
    VividCBSDF cbsdf = (VividCBSDF)0;

    if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC)
        cbsdf = EvaluateVividFabricBSDF(surfaceData, viewDirectionWS, lightDirectionWS);
    else
        cbsdf = EvaluateVividLitBSDF(surfaceData, bsdfData, preLightData, viewDirectionWS, lightDirectionWS);

    return cbsdf;
}

float3 EvaluateVividBakedDiffuseLighting(VividGBufferSurfaceData surfaceData)
{
    return surfaceData.hasBakedGI > 0.5
        ? surfaceData.bakedGI
        : VividSampleAmbientProbe(surfaceData.normalWS);
}

float3 EvaluateVividBakedDiffuseLighting(
    VividGBufferSurfaceData surfaceData,
    float3 positionWS,
    float3 viewDirectionWS)
{
    if (surfaceData.hasBakedGI <= 0.5 && VividHasProbeVolumeGI())
        return SampleVividProbeVolume(positionWS, surfaceData.normalWS, viewDirectionWS, 0xFFFFFFFFu);

    return EvaluateVividBakedDiffuseLighting(surfaceData);
}

VividIndirectLighting EvaluateVividLitIndirectBSDF(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS)
{
    VividIndirectLighting lighting = (VividIndirectLighting)0;
    float clampedNdotV = saturate( ClampNdotV(preLightData.NdotV));
    float3 dominantDirectionWS = GetSpecularDominantDir(
        surfaceData.normalWS,
        preLightData.iblR,
        preLightData.iblPerceptualRoughness,
        clampedNdotV);
    lighting.diffuse = EvaluateVividBakedDiffuseLighting(surfaceData) * preLightData.diffuseFGD;
    lighting.specularReflected = VividSampleSkyIBL(dominantDirectionWS, preLightData.iblPerceptualRoughness) * preLightData.specularFGD;

    if (bsdfData.coatMask > 0.0)
    {
        float coatIblF =  F_Schlick(kVividClearCoatF0, 1.0, clampedNdotV) * bsdfData.coatMask;
        float attenuation = Sq(1.0 - coatIblF);
        lighting.diffuse *= attenuation;
        lighting.specularReflected *= attenuation;

        float coatPerceptualRoughness =  RoughnessToPerceptualRoughness(bsdfData.coatRoughness);
        float3 coatDominantDirectionWS = GetSpecularDominantDir(
            surfaceData.normalWS,
            preLightData.iblR,
            coatPerceptualRoughness,
            clampedNdotV);
        lighting.specularReflected += VividSampleSkyIBL(coatDominantDirectionWS, coatPerceptualRoughness) * coatIblF;
    }

    lighting.diffuse *= surfaceData.ambientOcclusion;
    lighting.specularReflected *= surfaceData.ambientOcclusion;
    return lighting;
}

VividIndirectLighting EvaluateVividLitIndirectBSDF(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS,
    float3 positionWS)
{
    VividIndirectLighting lighting = (VividIndirectLighting)0;
    float clampedNdotV = saturate( ClampNdotV(preLightData.NdotV));
    float3 dominantDirectionWS = GetSpecularDominantDir(
        surfaceData.normalWS,
        preLightData.iblR,
        preLightData.iblPerceptualRoughness,
        clampedNdotV);
    lighting.diffuse = EvaluateVividBakedDiffuseLighting(surfaceData, positionWS, viewDirectionWS) * preLightData.diffuseFGD;
    lighting.specularReflected = VividSampleSkyIBL(dominantDirectionWS, preLightData.iblPerceptualRoughness) * preLightData.specularFGD;

    if (bsdfData.coatMask > 0.0)
    {
        float coatIblF =  F_Schlick(kVividClearCoatF0, 1.0, clampedNdotV) * bsdfData.coatMask;
        float attenuation = Sq(1.0 - coatIblF);
        lighting.diffuse *= attenuation;
        lighting.specularReflected *= attenuation;

        float coatPerceptualRoughness =  RoughnessToPerceptualRoughness(bsdfData.coatRoughness);
        float3 coatDominantDirectionWS = GetSpecularDominantDir(
            surfaceData.normalWS,
            preLightData.iblR,
            coatPerceptualRoughness,
            clampedNdotV);
        lighting.specularReflected += VividSampleSkyIBL(coatDominantDirectionWS, coatPerceptualRoughness) * coatIblF;
    }

    lighting.diffuse *= surfaceData.ambientOcclusion;
    lighting.specularReflected *= surfaceData.ambientOcclusion;
    return lighting;
}

float3 EvaluateVividHdrpLitIndirectLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 viewDirectionWS)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividPreLightData preLightData = GetVividPreLightData(normalizedViewDirectionWS, surfaceData, bsdfData);
    VividIndirectLighting lighting = EvaluateVividLitIndirectBSDF(surfaceData, bsdfData, preLightData, normalizedViewDirectionWS);
    return bsdfData.diffuseColor * lighting.diffuse
        + ApplyVividSpecularEnergyCompensation(lighting.specularReflected, bsdfData, preLightData);
}

float3 EvaluateVividHDRPLitIndirectLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS)
{
    VividIndirectLighting lighting = EvaluateVividLitIndirectBSDF(surfaceData, bsdfData, preLightData, viewDirectionWS);
    return bsdfData.diffuseColor * lighting.diffuse
        + ApplyVividSpecularEnergyCompensation(lighting.specularReflected, bsdfData, preLightData);
}

VividIndirectLighting EvaluateVividFabricIndirectBSDF(
    VividGBufferSurfaceData surfaceData,
    float3 viewDirectionWS)
{
    VividIndirectLighting lighting = (VividIndirectLighting)0;

    float nDotV = dot(surfaceData.normalWS, viewDirectionWS);
    float clampedNdotV = saturate( ClampNdotV(nDotV));
    float roughness =  ClampRoughnessForAnalyticalLights(surfaceData.linearRoughness);
    float perceptualRoughness =  RoughnessToPerceptualRoughness(roughness);
    float fuzzAmount = saturate(surfaceData.customData);
    float3 baseSpecular = lerp(kVividDielectricF0, surfaceData.baseColor, surfaceData.metallic);
    float luminance = VividGetLuminance(surfaceData.baseColor);
    float3 sheenTint = lerp(luminance.xxx, surfaceData.baseColor, 0.35);
    float3 fabricFresnel0 = lerp(baseSpecular, sheenTint, fuzzAmount);
    float3 specularFGD = 0.0;
    float diffuseFGD = 0.0;
    float reflectivity = 0.0;
    GetPreIntegratedFGDCharlieAndFabricLambert(
        clampedNdotV,
        perceptualRoughness,
        fabricFresnel0,
        specularFGD,
        diffuseFGD,
        reflectivity);

    lighting.diffuse = EvaluateVividBakedDiffuseLighting(surfaceData) * diffuseFGD;
    float3 reflectionVectorWS = VividGetReflectionVector(viewDirectionWS, surfaceData.normalWS);
    float3 dominantDirectionWS = GetSpecularDominantDir(
        surfaceData.normalWS,
        reflectionVectorWS,
        perceptualRoughness,
        clampedNdotV);
    lighting.specularReflected = VividSampleSkyIBL(dominantDirectionWS, perceptualRoughness) * specularFGD * fuzzAmount;
    lighting.diffuse *= surfaceData.ambientOcclusion;
    lighting.specularReflected *= surfaceData.ambientOcclusion;
    return lighting;
}

VividIndirectLighting EvaluateVividFabricIndirectBSDF(
    VividGBufferSurfaceData surfaceData,
    float3 viewDirectionWS,
    float3 positionWS)
{
    VividIndirectLighting lighting = (VividIndirectLighting)0;

    float nDotV = dot(surfaceData.normalWS, viewDirectionWS);
    float clampedNdotV = saturate( ClampNdotV(nDotV));
    float roughness =  ClampRoughnessForAnalyticalLights(surfaceData.linearRoughness);
    float perceptualRoughness =  RoughnessToPerceptualRoughness(roughness);
    float fuzzAmount = saturate(surfaceData.customData);
    float3 baseSpecular = lerp(kVividDielectricF0, surfaceData.baseColor, surfaceData.metallic);
    float luminance = VividGetLuminance(surfaceData.baseColor);
    float3 sheenTint = lerp(luminance.xxx, surfaceData.baseColor, 0.35);
    float3 fabricFresnel0 = lerp(baseSpecular, sheenTint, fuzzAmount);
    float3 specularFGD = 0.0;
    float diffuseFGD = 0.0;
    float reflectivity = 0.0;
    GetPreIntegratedFGDCharlieAndFabricLambert(
        clampedNdotV,
        perceptualRoughness,
        fabricFresnel0,
        specularFGD,
        diffuseFGD,
        reflectivity);

    lighting.diffuse = EvaluateVividBakedDiffuseLighting(surfaceData, positionWS, viewDirectionWS) * diffuseFGD;
    float3 reflectionVectorWS = VividGetReflectionVector(viewDirectionWS, surfaceData.normalWS);
    float3 dominantDirectionWS = GetSpecularDominantDir(
        surfaceData.normalWS,
        reflectionVectorWS,
        perceptualRoughness,
        clampedNdotV);
    lighting.specularReflected = VividSampleSkyIBL(dominantDirectionWS, perceptualRoughness) * specularFGD * fuzzAmount;
    lighting.diffuse *= surfaceData.ambientOcclusion;
    lighting.specularReflected *= surfaceData.ambientOcclusion;
    return lighting;
}

float3 EvaluateVividFabricIndirectLight(
    VividGBufferSurfaceData surfaceData,
    float3 viewDirectionWS)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividIndirectLighting lighting = EvaluateVividFabricIndirectBSDF(surfaceData, normalizedViewDirectionWS);
    float3 diffuseColor = surfaceData.baseColor * (1.0 - surfaceData.metallic);
    return diffuseColor * lighting.diffuse + lighting.specularReflected;
}

VividIndirectLighting EvaluateBSDF_Env(
    float3 viewDirectionWS,
    VividPreLightData preLightData,
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividIndirectLighting lighting = (VividIndirectLighting)0;

    if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC)
        lighting = EvaluateVividFabricIndirectBSDF(surfaceData, normalizedViewDirectionWS);
    else
        lighting = EvaluateVividLitIndirectBSDF(surfaceData, bsdfData, preLightData, normalizedViewDirectionWS);

    return lighting;
}

VividIndirectLighting EvaluateBSDF_Env(
    float3 positionWS,
    float3 viewDirectionWS,
    VividPreLightData preLightData,
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividIndirectLighting lighting = (VividIndirectLighting)0;

    if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC)
        lighting = EvaluateVividFabricIndirectBSDF(surfaceData, normalizedViewDirectionWS, positionWS);
    else
        lighting = EvaluateVividLitIndirectBSDF(surfaceData, bsdfData, preLightData, normalizedViewDirectionWS, positionWS);

    return lighting;
}

float3 EvaluateIndirectLighting(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS)
{
    VividIndirectLighting lighting = EvaluateBSDF_Env(viewDirectionWS, preLightData, surfaceData, bsdfData);
    return bsdfData.diffuseColor * lighting.diffuse
        + FinalizeVividSpecularLighting(surfaceData, bsdfData, preLightData, lighting.specularReflected);
}

float3 EvaluateIndirectLighting(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 viewDirectionWS)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividPreLightData preLightData = GetVividPreLightData(normalizedViewDirectionWS, surfaceData, bsdfData);
    return EvaluateIndirectLighting(surfaceData, bsdfData, preLightData, normalizedViewDirectionWS);
}

VividDirectLighting EvaluateBSDF_Directional(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS,
    DirectionalLightData directionalLight,
    float shadowAttenuation)
{
    float3 lightDirectionWS = SafeNormalize(directionalLight.directionWS);
    VividCBSDF cbsdf = EvaluateBSDF(viewDirectionWS, lightDirectionWS, preLightData, surfaceData, bsdfData);
    VividDirectLighting lighting = (VividDirectLighting)0;
    float3 lightColor = directionalLight.color * shadowAttenuation;
    lighting.diffuse = cbsdf.diffuse * lightColor;
    lighting.specular = cbsdf.specular * lightColor;
    return lighting;
}

VividDirectLighting EvaluateBSDF_Directional(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS,
    DirectionalLightData directionalLight)
{
    return EvaluateBSDF_Directional(
        surfaceData,
        bsdfData,
        preLightData,
        viewDirectionWS,
        directionalLight,
        1.0);
}

float3 EvaluateDirectional(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS,
    DirectionalLightData directionalLight)
{
    VividDirectLighting lighting = EvaluateBSDF_Directional(surfaceData, bsdfData, preLightData, viewDirectionWS, directionalLight);
    return bsdfData.diffuseColor * lighting.diffuse
        + FinalizeVividSpecularLighting(surfaceData, bsdfData, preLightData, lighting.specular);
}

float3 EvaluateDirectionalLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS,
    DirectionalLightData directionalLight)
{
    return EvaluateDirectional(surfaceData, bsdfData, preLightData, viewDirectionWS, directionalLight);
}

float3 EvaluateDirectionalLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 viewDirectionWS,
    DirectionalLightData directionalLight)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividPreLightData preLightData = GetVividPreLightData(normalizedViewDirectionWS, surfaceData, bsdfData);
    return EvaluateDirectional(surfaceData, bsdfData, preLightData, normalizedViewDirectionWS, directionalLight);
}

float EvaluatePunctualLightDistanceAttenuation(PunctualLightData punctualLight, float distanceSquared)
{
    float distanceAttenuation = rcp(max(distanceSquared, 1e-6));
    float rangeAttenuation = saturate(1.0 - distanceSquared * punctualLight.inverseRangeSquared);
    return distanceAttenuation * rangeAttenuation * rangeAttenuation;
}

float EvaluatePunctualLightSpotAttenuation(PunctualLightData punctualLight, float3 lightDirectionWS)
{
    float attenuation = 1.0;

    if (punctualLight.lightType == VIVID_PUNCTUAL_LIGHT_TYPE_SPOT)
    {
        float spotCosine = saturate(dot(punctualLight.directionWS, -lightDirectionWS));
        attenuation = saturate(spotCosine * punctualLight.angleScale + punctualLight.angleOffset);
        attenuation *= attenuation;
    }

    return attenuation;
}

VividDirectLighting EvaluateBSDF_Punctual(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 positionWS,
    float3 viewDirectionWS,
    PunctualLightData punctualLight)
{
    VividDirectLighting lighting = (VividDirectLighting)0;
    float3 lightVectorWS = punctualLight.positionWS - positionWS;
    float distanceSquared = dot(lightVectorWS, lightVectorWS);

    if (distanceSquared <= 1e-6)
        return lighting;

    float inverseDistance = rsqrt(distanceSquared);
    float3 lightDirectionWS = lightVectorWS * inverseDistance;
    float nDotL = saturate(dot(surfaceData.normalWS, lightDirectionWS));

    if (nDotL <= 0.0)
        return lighting;

    float attenuation = EvaluatePunctualLightDistanceAttenuation(punctualLight, distanceSquared)
        * EvaluatePunctualLightSpotAttenuation(punctualLight, lightDirectionWS);

    if (attenuation <= 0.0)
        return lighting;

    VividCBSDF cbsdf = EvaluateBSDF(viewDirectionWS, lightDirectionWS, preLightData, surfaceData, bsdfData);
    float3 lightColor = punctualLight.color * attenuation;
    lighting.diffuse = cbsdf.diffuse * lightColor;
    lighting.specular = cbsdf.specular * lightColor;
    return lighting;
}

float3 EvaluatePunctualLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 positionWS,
    float3 viewDirectionWS,
    PunctualLightData punctualLight)
{
    VividDirectLighting lighting = EvaluateBSDF_Punctual(surfaceData, bsdfData, preLightData, positionWS, viewDirectionWS, punctualLight);
    return bsdfData.diffuseColor * lighting.diffuse
        + FinalizeVividSpecularLighting(surfaceData, bsdfData, preLightData, lighting.specular);
}

float3 EvaluatePunctualLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 positionWS,
    float3 viewDirectionWS,
    PunctualLightData punctualLight)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividPreLightData preLightData = GetVividPreLightData(normalizedViewDirectionWS, surfaceData, bsdfData);
    return EvaluatePunctualLight(surfaceData, bsdfData, preLightData, positionWS, normalizedViewDirectionWS, punctualLight);
}

float EvaluateAreaLightIntensity(AreaLightData areaLight, float3 positionWS)
{
    float3 unL = areaLight.positionWS - positionWS;
    float halfLength = areaLight.width * 0.5;
    float halfHeight = areaLight.height * 0.5;
    float intensity = PillowWindowing(
        unL,
        areaLight.rightWS,
        areaLight.upWS,
        halfLength,
        halfHeight,
        areaLight.rangeAttenuationScale,
        areaLight.rangeAttenuationBias);

    if (areaLight.lightType == VIVID_AREA_LIGHT_TYPE_RECTANGLE
        && dot(unL, areaLight.forwardWS) >= 0.0)
    {
        return 0.0;
    }

    return intensity;
}

VividDirectLighting EvaluateBSDF_Area(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 positionWS,
    float3 viewDirectionWS,
    AreaLightData areaLight)
{
    VividDirectLighting lighting = (VividDirectLighting)0;
    float intensity = EvaluateAreaLightIntensity(areaLight, positionWS);

    if (intensity <= 0.0)
        return lighting;

    bool isRectLight = areaLight.lightType == VIVID_AREA_LIGHT_TYPE_RECTANGLE;
    float halfLength = areaLight.width * 0.5;
    float halfHeight = areaLight.height * 0.5;
    float3 unL = areaLight.positionWS - positionWS;
    float3 center = mul(preLightData.orthoBasisViewNormal, unL);
    float3 right = mul(preLightData.orthoBasisViewNormal, areaLight.rightWS);
    float3 up = mul(preLightData.orthoBasisViewNormal, areaLight.upWS);

    if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC)
    {
        float clampedNdotV = saturate(ClampNdotV(preLightData.NdotV));
        float roughness = ClampRoughnessForAnalyticalLights(surfaceData.linearRoughness);
        float perceptualRoughness = RoughnessToPerceptualRoughness(roughness);
        float fuzzAmount = saturate(surfaceData.customData);
        float3 baseSpecular = lerp(kVividDielectricF0, surfaceData.baseColor, surfaceData.metallic);
        float luminance = VividGetLuminance(surfaceData.baseColor);
        float3 sheenTint = lerp(luminance.xxx, surfaceData.baseColor, 0.35);
        float3 fabricFresnel0 = lerp(baseSpecular, sheenTint, fuzzAmount);
        float3 specularFGD = 0.0;
        float diffuseFGD = 0.0;
        float reflectivity = 0.0;

        GetPreIntegratedFGDCharlieAndFabricLambert(
            clampedNdotV,
            perceptualRoughness,
            fabricFresnel0,
            specularFGD,
            diffuseFGD,
            reflectivity);

        float4 ltcValue = EvaluateLTC_Area(
            isRectLight,
            center,
            right,
            up,
            halfLength,
            halfHeight,
            transpose(SampleLtcMatrix(
                perceptualRoughness,
                clampedNdotV,
                VIVID_LTC_LIGHTING_MODEL_CHARLIE)));

        ltcValue.a *= intensity;
        lighting.diffuse = ltcValue.rgb * ltcValue.a * areaLight.color * diffuseFGD;
        lighting.specular = ltcValue.rgb * ltcValue.a * areaLight.color * (specularFGD * fuzzAmount);
        return lighting;
    }

    float4 diffuseLtcValue = EvaluateLTC_Area(
        isRectLight,
        center,
        right,
        up,
        halfLength,
        halfHeight,
        transpose(preLightData.ltcTransformDiffuse));
    diffuseLtcValue.a *= intensity;
    lighting.diffuse = diffuseLtcValue.rgb * diffuseLtcValue.a * areaLight.color * preLightData.diffuseFGD;

    float4 specularLtcValue = EvaluateLTC_Area(
        isRectLight,
        center,
        right,
        up,
        halfLength,
        halfHeight,
        transpose(preLightData.ltcTransformSpecular));
    specularLtcValue.a *= intensity;
    lighting.specular = specularLtcValue.rgb * specularLtcValue.a * areaLight.color * preLightData.specularFGD;

    if (bsdfData.coatMask > 0.0)
    {
        float4 coatLtcValue = EvaluateLTC_Area(
            isRectLight,
            center,
            right,
            up,
            halfLength,
            halfHeight,
            transpose(preLightData.ltcTransformCoat));
        coatLtcValue.a *= intensity;
        lighting.diffuse *= 1.0 - preLightData.coatIblF;
        lighting.specular = lerp(
            lighting.specular,
            coatLtcValue.rgb * coatLtcValue.a * areaLight.color,
            preLightData.coatIblF);
    }

    return lighting;
}

void AccumulateDirectLighting(
    VividDirectLighting lighting,
    inout VividAggregateLighting aggregateLighting)
{
    aggregateLighting.direct.diffuse += lighting.diffuse;
    aggregateLighting.direct.specular += lighting.specular;
}

void AccumulateIndirectLighting(
    VividIndirectLighting lighting,
    inout VividAggregateLighting aggregateLighting)
{
    aggregateLighting.indirect.diffuse += lighting.diffuse;
    aggregateLighting.indirect.specularReflected += lighting.specularReflected;
}

void PostEvaluateBSDF(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    VividAggregateLighting lighting,
    out VividLightLoopOutput lightLoopOutput)
{
    lightLoopOutput = (VividLightLoopOutput)0;
    lightLoopOutput.diffuseLighting =
        bsdfData.diffuseColor * (lighting.direct.diffuse + lighting.indirect.diffuse)
        + surfaceData.emissive;
    lightLoopOutput.specularLighting = FinalizeVividSpecularLighting(
        surfaceData,
        bsdfData,
        preLightData,
        lighting.direct.specular + lighting.indirect.specularReflected);
}

float3 CombineVividLightLoopOutput(VividLightLoopOutput lightLoopOutput)
{
    return lightLoopOutput.diffuseLighting + lightLoopOutput.specularLighting;
}

#endif
