#ifndef VIVIDRP_HDRP_LIT_LIGHTING_INCLUDED
#define VIVIDRP_HDRP_LIT_LIGHTING_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Lighting.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/PunctualLightCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/LTCAreaLight.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/PreIntegratedFGD.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividProbeVolume.hlsl"

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

TEXTURECUBE(_SkyTexture);
SAMPLER(sampler_SkyTexture);
float4 _SkyTextureTint;
float4 _SkyTextureParams;

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

bool HasSkyTexture()
{
    return _SkyTextureParams.w > 0.5;
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

float3 SampleSkyTexture(float3 directionWS, float mipLevel)
{
    if (!HasSkyTexture())
        return float3(0.0, 0.0, 0.0);

    float skyMipLevel = min(mipLevel, max(_SkyTextureParams.z, 0.0));
    float3 rotatedDirectionWS = VividRotateAroundYAxis(directionWS, _SkyTextureParams.y);
    float3 envLighting = float3(0.0, 0.0, 0.0);
    envLighting = SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_SkyTexture, rotatedDirectionWS, skyMipLevel).rgb;
    return envLighting * _SkyTextureTint.rgb * _SkyTextureParams.x;
}

bool VividHasSkyIBL()
{
    return HasSkyTexture();
}

float3 VividSampleSkyIBL(float3 directionWS, float perceptualRoughness)
{
    uint maxMip = (uint)max(_SkyTextureParams.z, 0.0);
    float mipLevel = PerceptualRoughnessToMipmapLevel(saturate(perceptualRoughness), maxMip);
    return SampleSkyTexture(directionWS, mipLevel);
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

    if (HasVividMaterialFeature(surfaceData.materialFeatures, VIVID_MATERIALFEATURE_CLEAR_COAT))
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
    return HasVividMaterialFeature(surfaceData.materialFeatures, VIVID_MATERIALFEATURE_FABRIC)
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

    float3 F = F_Schlick(bsdfData.fresnel0, bsdfData.fresnel90, lDotH);
    float DV = DV_SmithJointGGX(nDotH, clampedNdotL, clampedNdotV, bsdfData.roughness, preLightData.partLambdaV);
    float3 specTerm = F * DV;
    // A note on subsurface scattering: [SSS-NOTE-TRSM]
    // The correct way to handle SSS is to transmit light inside the surface, perform SSS,
    // and then transmit it outside towards the viewer.
    // Transmit(X) = F_Transm_Schlick(F0, F90, NdotX), where F0 = 0, F90 = 1.
    // Therefore, the diffuse BSDF should be decomposed as follows:
    // f_d = A / Pi * F_Transm_Schlick(0, 1, NdotL) * F_Transm_Schlick(0, 1, NdotV) + f_d_reflection,
    // with F_Transm_Schlick(0, 1, NdotV) applied after the SSS pass.
    // The alternative (artistic) formulation of Disney is to set F90 = 0.5:
    // f_d = A / Pi * F_Transm_Schlick(0, 0.5, NdotL) * F_Transm_Schlick(0, 0.5, NdotV) + f_retro_reflection.
    // That way, darkening at grading angles is reduced to 0.5.
    // In practice, applying F_Transm_Schlick(F0, F90, NdotV) after the SSS pass is expensive,
    // as it forces us to read the normal buffer at the end of the SSS pass.
    // Separating f_retro_reflection also has a small cost (mostly due to energy compensation
    // for multi-bounce GGX), and the visual difference is negligible.
    // Therefore, we choose not to separate diffuse lighting into reflected and transmitted.

    // Use abs NdotL to evaluate diffuse term also for transmission
    // TODO: See with Evgenii about the clampedNdotV here. This is what we use before the refactor
    // but now maybe we want to revisit it for transmission
    float diffTerm = DisneyDiffuse(clampedNdotV, abs(nDotL), lDotV, bsdfData.perceptualRoughness);

    if (bsdfData.coatMask > 0.0)
    {
        float coatFresnel = F_Schlick(kVividClearCoatF0, 1.0, lDotH) * bsdfData.coatMask;
        specTerm *= Sq(1.0 - coatFresnel);

        float coatPartLambdaV = GetSmithJointGGXPartLambdaV(clampedNdotV, bsdfData.coatRoughness);
        specTerm += coatFresnel * DV_SmithJointGGX(
            nDotH,
            clampedNdotL,
            clampedNdotV,
            bsdfData.coatRoughness,
            coatPartLambdaV);

        diffTerm *= lerp(1.0, 1.0 - coatFresnel, bsdfData.coatMask);
    }

    cbsdf.diffuse = diffTerm * clampedNdotL;
    cbsdf.specular = specTerm * clampedNdotL;
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

    if (HasVividMaterialFeature(surfaceData.materialFeatures, VIVID_MATERIALFEATURE_FABRIC))
        cbsdf = EvaluateVividFabricBSDF(surfaceData, viewDirectionWS, lightDirectionWS);
    else
        cbsdf = EvaluateVividLitBSDF(surfaceData, bsdfData, preLightData, viewDirectionWS, lightDirectionWS);

    return cbsdf;
}

float3 EvaluateVividBakedDiffuseLighting(VividGBufferSurfaceData surfaceData)
{
    return surfaceData.builtinData.hasBakedGI > 0.5
        ? surfaceData.builtinData.bakeDiffuseLighting
        : VividSampleAmbientProbe(surfaceData.normalWS);
}

float3 EvaluateVividBakedDiffuseLighting(
    VividGBufferSurfaceData surfaceData,
    float3 positionWS,
    float3 viewDirectionWS)
{
    if (surfaceData.builtinData.hasBakedGI <= 0.5 && VividHasProbeVolumeGI())
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

    if (HasVividMaterialFeature(surfaceData.materialFeatures, VIVID_MATERIALFEATURE_FABRIC))
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

    if (HasVividMaterialFeature(surfaceData.materialFeatures, VIVID_MATERIALFEATURE_FABRIC))
        lighting = EvaluateVividFabricIndirectBSDF(surfaceData, normalizedViewDirectionWS, positionWS);
    else
        lighting = EvaluateVividLitIndirectBSDF(surfaceData, bsdfData, preLightData, normalizedViewDirectionWS, positionWS);

    return lighting;
}

float GetVividFabricIblPerceptualRoughness(VividGBufferSurfaceData surfaceData)
{
    float roughness = ClampRoughnessForAnalyticalLights(surfaceData.linearRoughness);
    return RoughnessToPerceptualRoughness(roughness);
}

float3 GetVividFabricReflectionProbeSpecularFactor(
    VividGBufferSurfaceData surfaceData,
    float3 viewDirectionWS)
{
    float nDotV = dot(surfaceData.normalWS, SafeNormalize(viewDirectionWS));
    float clampedNdotV = saturate(ClampNdotV(nDotV));
    float perceptualRoughness = GetVividFabricIblPerceptualRoughness(surfaceData);
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

    return specularFGD * fuzzAmount;
}

void GetVividReflectionProbeSampleInputs(
    VividGBufferSurfaceData surfaceData,
    VividPreLightData preLightData,
    float3 viewDirectionWS,
    out float3 directionWS,
    out float perceptualRoughness)
{
    float clampedNdotV = saturate(ClampNdotV(preLightData.NdotV));

    if (HasVividMaterialFeature(surfaceData.materialFeatures, VIVID_MATERIALFEATURE_FABRIC))
    {
        perceptualRoughness = GetVividFabricIblPerceptualRoughness(surfaceData);
        float3 reflectionVectorWS = VividGetReflectionVector(SafeNormalize(viewDirectionWS), surfaceData.normalWS);
        directionWS = GetSpecularDominantDir(
            surfaceData.normalWS,
            reflectionVectorWS,
            perceptualRoughness,
            clampedNdotV);
        return;
    }

    perceptualRoughness = preLightData.iblPerceptualRoughness;
    directionWS = GetSpecularDominantDir(
        surfaceData.normalWS,
        preLightData.iblR,
        perceptualRoughness,
        clampedNdotV);
}

bool NeedsVividClearCoatReflectionProbeSample(VividLitBSDFData bsdfData)
{
    return bsdfData.coatMask > 0.0;
}

void GetVividClearCoatReflectionProbeSampleInputs(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    out float3 directionWS,
    out float perceptualRoughness)
{
    float clampedNdotV = saturate(ClampNdotV(preLightData.NdotV));
    perceptualRoughness = RoughnessToPerceptualRoughness(bsdfData.coatRoughness);
    directionWS = GetSpecularDominantDir(
        surfaceData.normalWS,
        preLightData.iblR,
        perceptualRoughness,
        clampedNdotV);
}

float3 GetVividReflectionProbeSpecularFactor(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS)
{
    if (HasVividMaterialFeature(surfaceData.materialFeatures, VIVID_MATERIALFEATURE_FABRIC))
        return GetVividFabricReflectionProbeSpecularFactor(surfaceData, viewDirectionWS);

    float3 specularFactor = preLightData.specularFGD;
    if (NeedsVividClearCoatReflectionProbeSample(bsdfData))
        specularFactor *= Sq(1.0 - preLightData.coatIblF);

    return specularFactor;
}

VividIndirectLighting ApplyVividReflectionProbeSpecularLighting(
    VividIndirectLighting lighting,
    float3 weightedProbeRadiance,
    float reflectionProbeWeight,
    float3 weightedCoatProbeRadiance,
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 viewDirectionWS)
{
    float hierarchyWeight = saturate(reflectionProbeWeight);
    float ambientOcclusion = surfaceData.ambientOcclusion;

    lighting.specularReflected *= 1.0 - hierarchyWeight;
    lighting.specularReflected += weightedProbeRadiance
        * GetVividReflectionProbeSpecularFactor(surfaceData, bsdfData, preLightData, viewDirectionWS)
        * ambientOcclusion;

    if (!HasVividMaterialFeature(surfaceData.materialFeatures, VIVID_MATERIALFEATURE_FABRIC)
        && NeedsVividClearCoatReflectionProbeSample(bsdfData))
    {
        lighting.specularReflected += weightedCoatProbeRadiance * preLightData.coatIblF * ambientOcclusion;
    }

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

VividDirectLighting EvaluateBSDF_Punctual(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    VividPreLightData preLightData,
    float3 positionWS,
    float3 viewDirectionWS,
    PunctualLightData punctualLight)
{
    VividDirectLighting lighting = (VividDirectLighting)0;
    float3 lightDirectionWS = 0.0;
    float4 distances = 0.0;
    GetVividPunctualLightVectors(positionWS, punctualLight, lightDirectionWS, distances);

    if (distances.y <= 1e-6)
        return lighting;

    float nDotL = saturate(dot(surfaceData.normalWS, lightDirectionWS));

    if (nDotL <= 0.0)
        return lighting;

    float attenuation = VividPunctualLightAttenuationWithDistanceModification(
        punctualLight,
        positionWS - punctualLight.positionWS,
        distances);

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

void ApplyRectangularAreaLightBarnDoor(inout AreaLightData areaLight, float3 positionWS)
{
    if (areaLight.lightType != VIVID_AREA_LIGHT_TYPE_RECTANGLE)
        return;

    if (areaLight.cosBarnDoorAngle <= 0.017f || areaLight.barnDoorLength <= 0.05f)
        return;

    float halfWidth = areaLight.width * 0.5;
    float halfHeight = areaLight.height * 0.5;
    float3 pointToLight = positionWS - areaLight.positionWS;
    float3 pointLS = float3(
        dot(pointToLight, areaLight.rightWS),
        dot(pointToLight, areaLight.upWS),
        dot(pointToLight, areaLight.forwardWS));

    float maxDepth = areaLight.cosBarnDoorAngle * areaLight.barnDoorLength;
    float pointDepth = min(pointLS.z, maxDepth);
    float pointDepthRatio = pointDepth / max(maxDepth, 1e-5);
    float sinTheta = sqrt(saturate(1.0 - areaLight.cosBarnDoorAngle * areaLight.cosBarnDoorAngle));
    float barnDoorProjection = sinTheta * areaLight.barnDoorLength * pointDepthRatio;
    float2 pointSign = sign(pointLS.xy);
    pointLS.xy = pointSign * max(abs(pointLS.xy), float2(halfWidth, halfHeight) + barnDoorProjection.xx);

    float3 closestLightCorner = float3(
        pointSign.x * (halfWidth + barnDoorProjection),
        pointSign.y * (halfHeight + barnDoorProjection),
        pointDepth);
    float3 pointProjection = pointLS - closestLightCorner;
    float cosPhi = max(0.0, pointProjection.z);
    float2 tanPhi = cosPhi > 0.001f
        ? abs(pointProjection.xy) / cosPhi
        : float2(99999.0, 99999.0);
    float2 projectionDistance = pointDepth * tanPhi;

    float2 topRight = float2(-halfWidth, halfWidth);
    float2 bottomLeft = float2(-halfHeight, halfHeight);
    topRight += (projectionDistance.x - barnDoorProjection) * float2(max(0.0, -pointSign.x), -max(0.0, pointSign.x));
    bottomLeft += (projectionDistance.y - barnDoorProjection) * float2(max(0.0, -pointSign.y), -max(0.0, pointSign.y));
    topRight = clamp(topRight, -halfWidth, halfWidth);
    bottomLeft = clamp(bottomLeft, -halfHeight, halfHeight);

    float2 lightCenterOffset = 0.5f * float2(topRight.x + topRight.y, bottomLeft.x + bottomLeft.y);
    areaLight.width = topRight.y - topRight.x;
    areaLight.height = bottomLeft.y - bottomLeft.x;
    areaLight.positionWS += areaLight.rightWS * lightCenterOffset.x + areaLight.upWS * lightCenterOffset.y;
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
    ApplyRectangularAreaLightBarnDoor(areaLight, positionWS);
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

    if (HasVividMaterialFeature(surfaceData.materialFeatures, VIVID_MATERIALFEATURE_FABRIC))
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
