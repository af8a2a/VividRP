#ifndef VIVIDRP_HDRP_LIT_LIGHTING_INCLUDED
#define VIVIDRP_HDRP_LIT_LIGHTING_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/BakedGI.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/PreIntegratedFGD.hlsl"

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/DDGI.hlsl"
#if defined(VIVIDRP_DDGI_ENABLED)
#endif

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
};

float VividClampNdotV(float nDotV)
{
    return max(abs(nDotV), 1e-4);
}

float VividClampRoughnessForAnalyticalLights(float roughness)
{
    return ClampRoughnessForAnalyticalLights(roughness);
}

float VividPerceptualRoughnessToRoughness(float perceptualRoughness)
{
    return PerceptualRoughnessToRoughness(perceptualRoughness);
}

float VividRoughnessToPerceptualRoughness(float roughness)
{
    return RoughnessToPerceptualRoughness(roughness);
}

float VividRoughnessToVariance(float roughness)
{
    return RoughnessToVariance(roughness);
}

float VividVarianceToRoughness(float variance)
{
    return VarianceToRoughness(variance);
}

float VividF_Schlick(float f0, float f90, float u)
{
    return F_Schlick(f0, f90, u);
}

float3 VividF_Schlick(float3 f0, float f90, float u)
{
    return F_Schlick(f0, f90, u);
}

float VividDisneyDiffuse(float nDotV, float nDotL, float lDotV, float perceptualRoughness)
{
    return DisneyDiffuse(nDotV, nDotL, lDotV, perceptualRoughness);
}

float VividGetSmithJointGGXPartLambdaV(float nDotV, float roughness)
{
    return GetSmithJointGGXPartLambdaV(nDotV, roughness);
}

float VividDV_SmithJointGGX(float nDotH, float nDotL, float nDotV, float roughness, float partLambdaV)
{
    return DV_SmithJointGGX(nDotH, nDotL, nDotV, roughness, partLambdaV);
}

float VividD_Charlie(float nDotH, float roughness)
{
    return D_Charlie(nDotH, roughness);
}

float VividV_Ashikhmin(float nDotL, float nDotV)
{
    return V_Ashikhmin(nDotL, nDotV);
}

float VividFabricLambert(float roughness)
{
    return FabricLambert(saturate(roughness));
}

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

VividLitBSDFData BuildVividHdrpLitBSDFData(VividGBufferSurfaceData surfaceData)
{
    VividLitBSDFData bsdfData = (VividLitBSDFData)0;
    bsdfData.diffuseColor = surfaceData.baseColor * (1.0 - surfaceData.metallic);
    bsdfData.fresnel0 = lerp(kVividDielectricF0, surfaceData.baseColor, surfaceData.metallic);
    bsdfData.fresnel90 = 1.0;
    bsdfData.perceptualRoughness = GetPerceptualRoughnessFromLinearRoughness(surfaceData.linearRoughness);
    bsdfData.roughness = VividClampRoughnessForAnalyticalLights(surfaceData.linearRoughness);
    bsdfData.coatMask = 0.0;
    bsdfData.coatRoughness = kVividClearCoatRoughness;

    if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_CLEARCOAT)
    {
        bsdfData.coatMask = saturate(surfaceData.customData);

        if (bsdfData.coatMask > 0.0)
        {
            float ieta = lerp(1.0, kVividClearCoatIeta, bsdfData.coatMask);
            float coatRoughnessScale = ieta * ieta;
            float sigma = VividRoughnessToVariance(bsdfData.roughness);
            float coatAdjustedRoughness = VividVarianceToRoughness(sigma * coatRoughnessScale);
            bsdfData.perceptualRoughness = VividRoughnessToPerceptualRoughness(coatAdjustedRoughness);
            bsdfData.roughness = VividClampRoughnessForAnalyticalLights(
                VividPerceptualRoughnessToRoughness(bsdfData.perceptualRoughness));
        }
    }

    return bsdfData;
}

VividPreLightData InitVividPreLightData(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 viewDirectionWS)
{
    VividPreLightData preLightData = (VividPreLightData)0;
    float clampedNdotV = 0.0;

    preLightData.NdotV = dot(surfaceData.normalWS, viewDirectionWS);
    preLightData.iblPerceptualRoughness = bsdfData.perceptualRoughness;
    clampedNdotV = saturate(VividClampNdotV(preLightData.NdotV));
    preLightData.partLambdaV = VividGetSmithJointGGXPartLambdaV(clampedNdotV, bsdfData.roughness);

    GetPreIntegratedFGDGGXAndDisneyDiffuse(
        clampedNdotV,
        preLightData.iblPerceptualRoughness,
        bsdfData.fresnel0,
        bsdfData.fresnel90,
        preLightData.specularFGD,
        preLightData.diffuseFGD,
        preLightData.reflectivity);

    preLightData.energyCompensation = rcp(max(preLightData.reflectivity, 1e-4)) - 1.0;
    preLightData.iblR = VividGetReflectionVector(viewDirectionWS, surfaceData.normalWS);
    return preLightData;
}

float3 EvaluateVividLitDirectLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS,
    float3 lightDirectionWS)
{
    float nDotL = dot(surfaceData.normalWS, lightDirectionWS);
    if (nDotL <= 0.0)
        return float3(0.0, 0.0, 0.0);

    float clampedNdotV = VividClampNdotV(preLightData.NdotV);
    float clampedNdotL = saturate(nDotL);
    float lDotV = 0.0;
    float nDotH = 0.0;
    float lDotH = 0.0;
    float invLenLV = 0.0;
    GetBSDFAngle(viewDirectionWS, lightDirectionWS, nDotL, preLightData.NdotV, lDotV, nDotH, lDotH, invLenLV);

    float3 fresnel = VividF_Schlick(bsdfData.fresnel0, bsdfData.fresnel90, lDotH);
    float3 specular = fresnel * VividDV_SmithJointGGX(
        nDotH,
        clampedNdotL,
        clampedNdotV,
        bsdfData.roughness,
        preLightData.partLambdaV);
    float diffuse = VividDisneyDiffuse(clampedNdotV, clampedNdotL, lDotV, bsdfData.perceptualRoughness);

    if (bsdfData.coatMask > 0.0)
    {
        float coatFresnel = VividF_Schlick(kVividClearCoatF0, 1.0, lDotH) * bsdfData.coatMask;
        specular *= Sq(1.0 - coatFresnel);

        float coatPartLambdaV = VividGetSmithJointGGXPartLambdaV(clampedNdotV, bsdfData.coatRoughness);
        specular += coatFresnel * VividDV_SmithJointGGX(
            nDotH,
            clampedNdotL,
            clampedNdotV,
            bsdfData.coatRoughness,
            coatPartLambdaV);

        diffuse *= lerp(1.0, 1.0 - coatFresnel, bsdfData.coatMask);
    }

    return (bsdfData.diffuseColor * diffuse + specular) * clampedNdotL;
}

float3 EvaluateVividLitDirectLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 viewDirectionWS,
    float3 lightDirectionWS)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividPreLightData preLightData = InitVividPreLightData(surfaceData, bsdfData, normalizedViewDirectionWS);
    return EvaluateVividLitDirectLight(surfaceData, bsdfData, preLightData, normalizedViewDirectionWS, lightDirectionWS);
}

float3 EvaluateVividFabricDirectLight(
    VividGBufferSurfaceData surfaceData,
    float3 viewDirectionWS,
    float3 lightDirectionWS)
{
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
        return float3(0.0, 0.0, 0.0);

    float clampedNdotV = VividClampNdotV(nDotV);
    float clampedNdotL = saturate(nDotL);
    float roughness = VividClampRoughnessForAnalyticalLights(surfaceData.linearRoughness);
    float fuzzAmount = saturate(surfaceData.customData);
    float3 diffuseColor = surfaceData.baseColor * (1.0 - surfaceData.metallic);
    float3 baseSpecular = lerp(kVividDielectricF0, surfaceData.baseColor, surfaceData.metallic);
    float luminance = VividGetLuminance(surfaceData.baseColor);
    float3 sheenTint = lerp(luminance.xxx, surfaceData.baseColor, 0.35);
    float3 fabricFresnel0 = lerp(baseSpecular, sheenTint, fuzzAmount);
    float3 specular = fabricFresnel0 * VividD_Charlie(nDotH, roughness) * VividV_Ashikhmin(clampedNdotL, clampedNdotV) * fuzzAmount;
    float diffuse = VividFabricLambert(roughness);
    return (diffuseColor * diffuse + specular) * clampedNdotL;
}

float3 EvaluateVividBakedDiffuseLighting(VividGBufferSurfaceData surfaceData)
{
    return surfaceData.hasBakedGI > 0.5
        ? surfaceData.bakedGI
        : VividSampleAmbientProbe(surfaceData.normalWS);
}

float3 EvaluateVividIndirectDiffuseLighting(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData)
{
    float3 diffuseLighting = EvaluateVividBakedDiffuseLighting(surfaceData) * bsdfData.diffuseColor * preLightData.diffuseFGD;
    if (bsdfData.coatMask > 0.0)
    {
        float clampedNdotV = saturate(VividClampNdotV(preLightData.NdotV));
        float coatIblF = VividF_Schlick(kVividClearCoatF0, 1.0, clampedNdotV) * bsdfData.coatMask;
        diffuseLighting *= Sq(1.0 - coatIblF);
    }

    return diffuseLighting * surfaceData.ambientOcclusion;
}

float3 ApplyVividDDGIIndirectDiffuse(
    float3 indirectLighting,
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS,
    float3 positionWS)
{
#if defined(VIVIDRP_DDGI_ENABLED)
    if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC)
    {
        return indirectLighting;
    }

    float blendWeight = VividDDGIGetBlendWeight(positionWS);
    if (blendWeight <= 0.0f)
    {
        return indirectLighting;
    }

    float3 ddgiIrradiance = VividDDGIGetIrradiance(positionWS, surfaceData.normalWS, viewDirectionWS);
    float3 ddgiDiffuseLighting = ddgiIrradiance * bsdfData.diffuseColor * preLightData.diffuseFGD;
    if (bsdfData.coatMask > 0.0)
    {
        float clampedNdotV = saturate(VividClampNdotV(preLightData.NdotV));
        float coatIblF = VividF_Schlick(kVividClearCoatF0, 1.0, clampedNdotV) * bsdfData.coatMask;
        ddgiDiffuseLighting *= Sq(1.0 - coatIblF);
    }

    ddgiDiffuseLighting *= surfaceData.ambientOcclusion;
    float3 bakedDiffuseLighting = EvaluateVividIndirectDiffuseLighting(surfaceData, bsdfData, preLightData);
    return indirectLighting + ((ddgiDiffuseLighting - bakedDiffuseLighting) * blendWeight);
#else
    return indirectLighting;
#endif
}

float3 EvaluateVividHdrpLitIndirectLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS)
{
    float clampedNdotV = saturate(VividClampNdotV(preLightData.NdotV));
    float3 diffuseLighting = EvaluateVividBakedDiffuseLighting(surfaceData) * bsdfData.diffuseColor * preLightData.diffuseFGD;
    float3 dominantDirectionWS = GetSpecularDominantDir(
        surfaceData.normalWS,
        preLightData.iblR,
        preLightData.iblPerceptualRoughness,
        clampedNdotV);
    float3 specularLighting = VividSampleSkyIBL(dominantDirectionWS, preLightData.iblPerceptualRoughness) * preLightData.specularFGD;

    if (bsdfData.coatMask > 0.0)
    {
        float coatIblF = VividF_Schlick(kVividClearCoatF0, 1.0, clampedNdotV) * bsdfData.coatMask;
        float attenuation = Sq(1.0 - coatIblF);
        diffuseLighting *= attenuation;
        specularLighting *= attenuation;

        float coatPerceptualRoughness = VividRoughnessToPerceptualRoughness(bsdfData.coatRoughness);
        float3 coatDominantDirectionWS = GetSpecularDominantDir(
            surfaceData.normalWS,
            preLightData.iblR,
            coatPerceptualRoughness,
            clampedNdotV);
        specularLighting += VividSampleSkyIBL(coatDominantDirectionWS, coatPerceptualRoughness) * coatIblF;
    }

    return (diffuseLighting + specularLighting) * surfaceData.ambientOcclusion;
}

float3 EvaluateVividHdrpLitIndirectLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 viewDirectionWS)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividPreLightData preLightData = InitVividPreLightData(surfaceData, bsdfData, normalizedViewDirectionWS);
    return EvaluateVividHdrpLitIndirectLight(surfaceData, bsdfData, preLightData, normalizedViewDirectionWS);
}

float3 EvaluateVividFabricIndirectLight(
    VividGBufferSurfaceData surfaceData,
    float3 viewDirectionWS)
{
    float nDotV = dot(surfaceData.normalWS, viewDirectionWS);
    float clampedNdotV = saturate(VividClampNdotV(nDotV));
    float roughness = VividClampRoughnessForAnalyticalLights(surfaceData.linearRoughness);
    float perceptualRoughness = VividRoughnessToPerceptualRoughness(roughness);
    float fuzzAmount = saturate(surfaceData.customData);
    float3 diffuseColor = surfaceData.baseColor * (1.0 - surfaceData.metallic);
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

    float3 diffuseLighting = EvaluateVividBakedDiffuseLighting(surfaceData) * diffuseColor * diffuseFGD;
    float3 reflectionVectorWS = VividGetReflectionVector(viewDirectionWS, surfaceData.normalWS);
    float3 dominantDirectionWS = GetSpecularDominantDir(
        surfaceData.normalWS,
        reflectionVectorWS,
        perceptualRoughness,
        clampedNdotV);
    float3 specularLighting = VividSampleSkyIBL(dominantDirectionWS, perceptualRoughness) * specularFGD * fuzzAmount;
    return (diffuseLighting + specularLighting) * surfaceData.ambientOcclusion;
}

float3 EvaluateIndirectLighting(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS)
{
    return surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC
        ? EvaluateVividFabricIndirectLight(surfaceData, viewDirectionWS)
        : EvaluateVividHdrpLitIndirectLight(surfaceData, bsdfData, preLightData, viewDirectionWS);
}

float3 EvaluateIndirectLighting(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 viewDirectionWS)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividPreLightData preLightData = InitVividPreLightData(surfaceData, bsdfData, normalizedViewDirectionWS);
    return EvaluateIndirectLighting(surfaceData, bsdfData, preLightData, normalizedViewDirectionWS);
}

float3 EvaluateDirectional(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS,
    DirectionalLightData directionalLight)
{
    float3 lightDirectionWS = SafeNormalize(directionalLight.directionWS);
    float3 lighting = surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC
        ? EvaluateVividFabricDirectLight(surfaceData, viewDirectionWS, lightDirectionWS)
        : EvaluateVividLitDirectLight(surfaceData, bsdfData, preLightData, viewDirectionWS, lightDirectionWS);
    return lighting * directionalLight.color;
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
    VividPreLightData preLightData = InitVividPreLightData(surfaceData, bsdfData, normalizedViewDirectionWS);
    return EvaluateDirectional(surfaceData, bsdfData, preLightData, normalizedViewDirectionWS, directionalLight);
}

float EvaluatePunctualLightDistanceAttenuation(PunctualLightData punctualLight, float distanceSquared)
{
    float attenuation = saturate(1.0 - distanceSquared * punctualLight.inverseRangeSquared);
    return attenuation * attenuation;
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

float3 EvaluatePunctualLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 positionWS,
    float3 viewDirectionWS,
    PunctualLightData punctualLight)
{
    float3 lightVectorWS = punctualLight.positionWS - positionWS;
    float distanceSquared = dot(lightVectorWS, lightVectorWS);

    if (distanceSquared <= 1e-6)
        return float3(0.0, 0.0, 0.0);

    float inverseDistance = rsqrt(distanceSquared);
    float3 lightDirectionWS = lightVectorWS * inverseDistance;
    float nDotL = saturate(dot(surfaceData.normalWS, lightDirectionWS));

    if (nDotL <= 0.0)
        return float3(0.0, 0.0, 0.0);

    float attenuation = EvaluatePunctualLightDistanceAttenuation(punctualLight, distanceSquared)
        * EvaluatePunctualLightSpotAttenuation(punctualLight, lightDirectionWS);

    if (attenuation <= 0.0)
        return float3(0.0, 0.0, 0.0);

    float3 lighting = float3(0.0, 0.0, 0.0);
    lighting = surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC
        ? EvaluateVividFabricDirectLight(surfaceData, viewDirectionWS, lightDirectionWS)
        : EvaluateVividLitDirectLight(surfaceData, bsdfData, preLightData, viewDirectionWS, lightDirectionWS);
    return lighting * punctualLight.color * attenuation;
}

float3 EvaluatePunctualLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 positionWS,
    float3 viewDirectionWS,
    PunctualLightData punctualLight)
{
    float3 normalizedViewDirectionWS = SafeNormalize(viewDirectionWS);
    VividPreLightData preLightData = InitVividPreLightData(surfaceData, bsdfData, normalizedViewDirectionWS);
    return EvaluatePunctualLight(surfaceData, bsdfData, preLightData, positionWS, normalizedViewDirectionWS, punctualLight);
}

#endif
