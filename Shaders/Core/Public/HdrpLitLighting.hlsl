#ifndef VIVIDRP_HDRP_LIT_LIGHTING_INCLUDED
#define VIVIDRP_HDRP_LIT_LIGHTING_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/PreIntegratedFGD.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AmbientProbe.hlsl"
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
    float3 fresnel90;
    float perceptualRoughness;
    float roughness;
    float coatMask;
    float coatRoughness;
};

float VividClampNdotV(float nDotV)
{
    return max(abs(nDotV), 1e-4);
}

float VividClampRoughnessForAnalyticalLights(float roughness)
{
    return max(roughness, 1.0 / 1024.0);
}

float VividPerceptualRoughnessToRoughness(float perceptualRoughness)
{
    return perceptualRoughness * perceptualRoughness;
}

float VividRoughnessToPerceptualRoughness(float roughness)
{
    return sqrt(max(roughness, 0.0));
}

float VividRoughnessToVariance(float roughness)
{
    roughness = max(roughness, 1e-4);
    return 2.0 / (roughness * roughness) - 2.0;
}

float VividVarianceToRoughness(float variance)
{
    return sqrt(2.0 / max(variance + 2.0, 1e-4));
}

float VividF_Schlick(float f0, float f90, float u)
{
    float x = 1.0 - u;
    float x2 = x * x;
    float x5 = x * x2 * x2;
    return (f90 - f0) * x5 + f0;
}

float3 VividF_Schlick(float3 f0, float3 f90, float u)
{
    float x = 1.0 - u;
    float x2 = x * x;
    float x5 = x * x2 * x2;
    return f0 * (1.0 - x5) + f90 * x5;
}

float VividDisneyDiffuse(float nDotV, float nDotL, float lDotV, float perceptualRoughness)
{
    float fd90 = 0.5 + (perceptualRoughness + perceptualRoughness * lDotV);
    float lightScatter = VividF_Schlick(1.0, fd90, nDotL);
    float viewScatter = VividF_Schlick(1.0, fd90, nDotV);
    return INV_PI * rcp(1.03571) * lightScatter * viewScatter;
}

float VividGetSmithJointGGXPartLambdaV(float nDotV, float roughness)
{
    float roughnessSquared = roughness * roughness;
    return sqrt(max((-nDotV * roughnessSquared + nDotV) * nDotV + roughnessSquared, 0.0));
}

float VividDV_SmithJointGGX(float nDotH, float nDotL, float nDotV, float roughness, float partLambdaV)
{
    float roughnessSquared = roughness * roughness;
    float s = (nDotH * roughnessSquared - nDotH) * nDotH + 1.0;

    float lambdaV = nDotL * partLambdaV;
    float lambdaL = nDotV * sqrt(max((-nDotL * roughnessSquared + nDotL) * nDotL + roughnessSquared, 0.0));

    return INV_PI * 0.5 * roughnessSquared / max(s * s * (lambdaV + lambdaL), 1e-6);
}

float VividD_Charlie(float nDotH, float roughness)
{
    roughness = max(roughness, 1.0 / 1024.0);
    float invRoughness = rcp(roughness);
    float cos2h = nDotH * nDotH;
    float sin2h = saturate(1.0 - cos2h);
    return INV_PI * (2.0 + invRoughness) * pow(sin2h, invRoughness * 0.5) * 0.5;
}

float VividV_Ashikhmin(float nDotL, float nDotV)
{
    return rcp(max(4.0 * (nDotL + nDotV - nDotL * nDotV), 1e-4));
}

float VividFabricLambert(float roughness)
{
    return INV_PI * lerp(1.0, 0.5, saturate(roughness));
}

float VividGetLuminance(float3 color)
{
    return dot(color, float3(0.2126729, 0.7151522, 0.0721750));
}

bool VividHasSkyIBL()
{
    return _VividSkyIBLParams.w > 0.5;
}

float3 VividRotateAroundYAxis(float3 directionWS, float rotationDegrees)
{
    float rotationRadians = radians(rotationDegrees);
    float s;
    float c;
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
        return 0.0;

    uint maxMip = (uint)max(_VividSkyIBLParams.z, 0.0);
    float mipLevel = PerceptualRoughnessToMipmapLevel(saturate(perceptualRoughness), maxMip);
    float3 rotatedDirectionWS = VividRotateAroundYAxis(directionWS, _VividSkyIBLParams.y);
    float3 envLighting = SAMPLE_TEXTURECUBE_LOD(_VividSkyIBLCubemap, sampler_VividSkyIBLCubemap, rotatedDirectionWS, mipLevel).rgb;
    return envLighting * _VividSkyIBLTint.rgb * _VividSkyIBLParams.x;
}

void VividGetBSDFAngles(
    float3 normalWS,
    float3 viewDirectionWS,
    float3 lightDirectionWS,
    out float nDotV,
    out float nDotL,
    out float lDotV,
    out float nDotH,
    out float lDotH)
{
    nDotV = dot(normalWS, viewDirectionWS);
    nDotL = dot(normalWS, lightDirectionWS);
    lDotV = dot(lightDirectionWS, viewDirectionWS);

    float invLenLV = rsqrt(max(2.0 * lDotV + 2.0, 1e-6));
    float3 halfVectorWS = (lightDirectionWS + viewDirectionWS) * invLenLV;
    nDotH = saturate(dot(normalWS, halfVectorWS));
    lDotH = saturate(dot(lightDirectionWS, halfVectorWS));
}

VividLitBSDFData BuildVividHdrpLitBSDFData(VividGBufferSurfaceData surfaceData)
{
    VividLitBSDFData bsdfData;
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

float3 EvaluateVividHdrpLitDirectLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 viewDirectionWS,
    float3 lightDirectionWS)
{
    float nDotV;
    float nDotL;
    float lDotV;
    float nDotH;
    float lDotH;
    VividGetBSDFAngles(surfaceData.normalWS, viewDirectionWS, lightDirectionWS, nDotV, nDotL, lDotV, nDotH, lDotH);

    if (nDotL <= 0.0)
        return 0.0;

    float clampedNdotV = VividClampNdotV(nDotV);
    float clampedNdotL = saturate(nDotL);
    float3 fresnel = VividF_Schlick(bsdfData.fresnel0, bsdfData.fresnel90, lDotH);
    float partLambdaV = VividGetSmithJointGGXPartLambdaV(clampedNdotV, bsdfData.roughness);
    float3 specular = fresnel * VividDV_SmithJointGGX(nDotH, clampedNdotL, clampedNdotV, bsdfData.roughness, partLambdaV);
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

float3 EvaluateVividFabricDirectLight(
    VividGBufferSurfaceData surfaceData,
    float3 viewDirectionWS,
    float3 lightDirectionWS)
{
    float nDotV;
    float nDotL;
    float lDotV;
    float nDotH;
    float lDotH;
    VividGetBSDFAngles(surfaceData.normalWS, viewDirectionWS, lightDirectionWS, nDotV, nDotL, lDotV, nDotH, lDotH);

    if (nDotL <= 0.0)
        return 0.0;

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

float3 EvaluateVividHdrpLitIndirectLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 viewDirectionWS)
{
    float nDotV = dot(surfaceData.normalWS, viewDirectionWS);
    float clampedNdotV = saturate(VividClampNdotV(nDotV));
    float3 specularFGD;
    float diffuseFGD;
    float reflectivity;
    GetPreIntegratedFGDGGXAndDisneyDiffuse(
        clampedNdotV,
        bsdfData.perceptualRoughness,
        bsdfData.fresnel0,
        specularFGD,
        diffuseFGD,
        reflectivity);

    float3 diffuseLighting = SampleSH(surfaceData.normalWS) * bsdfData.diffuseColor * diffuseFGD;
    float3 reflectionVectorWS = VividGetReflectionVector(viewDirectionWS, surfaceData.normalWS);
    float3 dominantDirectionWS = GetSpecularDominantDir(
        surfaceData.normalWS,
        reflectionVectorWS,
        bsdfData.perceptualRoughness,
        clampedNdotV);
    float3 specularLighting = VividSampleSkyIBL(dominantDirectionWS, bsdfData.perceptualRoughness) * specularFGD;

    if (bsdfData.coatMask > 0.0)
    {
        float coatIblF = VividF_Schlick(kVividClearCoatF0, 1.0, clampedNdotV) * bsdfData.coatMask;
        float attenuation = Sq(1.0 - coatIblF);
        diffuseLighting *= attenuation;
        specularLighting *= attenuation;

        float coatPerceptualRoughness = VividRoughnessToPerceptualRoughness(bsdfData.coatRoughness);
        float3 coatDominantDirectionWS = GetSpecularDominantDir(
            surfaceData.normalWS,
            reflectionVectorWS,
            coatPerceptualRoughness,
            clampedNdotV);
        specularLighting += VividSampleSkyIBL(coatDominantDirectionWS, coatPerceptualRoughness) * coatIblF;
    }

    return (diffuseLighting + specularLighting) * surfaceData.ambientOcclusion;
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
    float3 specularFGD;
    float diffuseFGD;
    float reflectivity;
    GetPreIntegratedFGDCharlieAndFabricLambert(
        clampedNdotV,
        perceptualRoughness,
        fabricFresnel0,
        specularFGD,
        diffuseFGD,
        reflectivity);

    float3 diffuseLighting = SampleSH(surfaceData.normalWS) * diffuseColor * diffuseFGD;
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
    float3 viewDirectionWS)
{
    return surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC
        ? EvaluateVividFabricIndirectLight(surfaceData, viewDirectionWS)
        : EvaluateVividHdrpLitIndirectLight(surfaceData, bsdfData, viewDirectionWS);
}

float3 EvaluateDirectionalLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 viewDirectionWS,
    DirectionalLightData directionalLight)
{
    float3 lightDirectionWS = SafeNormalize(directionalLight.directionWS);
    float3 lighting = surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC
        ? EvaluateVividFabricDirectLight(surfaceData, viewDirectionWS, lightDirectionWS)
        : EvaluateVividHdrpLitDirectLight(surfaceData, bsdfData, viewDirectionWS, lightDirectionWS);
    return lighting * directionalLight.color;
}

float EvaluatePunctualLightDistanceAttenuation(PunctualLightData punctualLight, float distanceSquared)
{
    float attenuation = saturate(1.0 - distanceSquared * punctualLight.inverseRangeSquared);
    return attenuation * attenuation;
}

float EvaluatePunctualLightSpotAttenuation(PunctualLightData punctualLight, float3 lightDirectionWS)
{
    if (punctualLight.lightType != VIVID_PUNCTUAL_LIGHT_TYPE_SPOT)
        return 1.0;

    float spotCosine = saturate(dot(punctualLight.directionWS, -lightDirectionWS));
    float attenuation = saturate(spotCosine * punctualLight.angleScale + punctualLight.angleOffset);
    return attenuation * attenuation;
}

float3 EvaluatePunctualLight(
    VividGBufferSurfaceData surfaceData,
    VividLitBSDFData bsdfData,
    float3 positionWS,
    float3 viewDirectionWS,
    PunctualLightData punctualLight)
{
    float3 lightVectorWS = punctualLight.positionWS - positionWS;
    float distanceSquared = dot(lightVectorWS, lightVectorWS);

    if (distanceSquared <= 1e-6)
        return 0.0;

    float inverseDistance = rsqrt(distanceSquared);
    float3 lightDirectionWS = lightVectorWS * inverseDistance;
    float nDotL = saturate(dot(surfaceData.normalWS, lightDirectionWS));

    if (nDotL <= 0.0)
        return 0.0;

    float attenuation = EvaluatePunctualLightDistanceAttenuation(punctualLight, distanceSquared)
        * EvaluatePunctualLightSpotAttenuation(punctualLight, lightDirectionWS);

    if (attenuation <= 0.0)
        return 0.0;

    float3 lighting = surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC
        ? EvaluateVividFabricDirectLight(surfaceData, viewDirectionWS, lightDirectionWS)
        : EvaluateVividHdrpLitDirectLight(surfaceData, bsdfData, viewDirectionWS, lightDirectionWS);
    return lighting * punctualLight.color * attenuation;
}

#endif
