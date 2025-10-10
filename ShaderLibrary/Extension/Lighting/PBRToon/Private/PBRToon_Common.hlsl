#ifndef PBRTOON_COMMON_INCLUDED
#define PBRTOON_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/Lighting/Common/LightingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/Filter/PreIntegratedFGD/Shader/PreIntegratedFGD.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/LightGrid/ClusterLight.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/Lighting/Common/AreaLightCommon.hlsl"

#include "PBRToonInput.hlsl"



half3 SampleNormal(float2 uv, TEXTURE2D_PARAM(bumpMap, sampler_bumpMap), half scale = half(1.0))
{
    #ifdef _NORMALMAP
    half4 n = SAMPLE_TEXTURE2D(bumpMap, sampler_bumpMap, uv);
    #if BUMP_SCALE_NOT_SUPPORTED
    return UnpackNormal(n);
    #else
    return UnpackNormalScale(n, scale);
    #endif
    #else
    return half3(0.0h, 0.0h, 1.0h);
    #endif
}

// void InitializeCharacterInputData(Varyings input, half3 normalTS, out InputData inputData)
// {
//     inputData = (InputData)0;
//
//     inputData.positionWS = input.positionWS;
//
//     inputData.positionCS = input.positionCS;
//
//     half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
//     #if defined(_NORMALMAP) || defined(_DETAIL)
//     float sgn = input.tangentWS.w; // should be either +1 or -1
//     float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
//     half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);
//
//     #if defined(_NORMALMAP)
//     inputData.tangentToWorld = tangentToWorld;
//     #endif
//     inputData.normalWS = TransformTangentToWorld(normalTS, tangentToWorld);
//     #else
//     inputData.normalWS = input.normalWS;
//     #endif
//
//     inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
//     inputData.viewDirectionWS = viewDirWS;
//
//     inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
//
//     inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
// }


// void InitializeCharacterBRDFData(SurfaceData surfaceData, out BRDFData brdfData)
// {
//     brdfData = (BRDFData)0;
//     brdfData.albedo = surfaceData.albedo;
//
//     brdfData.diffuse = surfaceData.albedo * (1 - surfaceData.metallic);
//     brdfData.specular = lerp(kDieletricSpec.rgb, surfaceData.albedo, surfaceData.metallic);
//     brdfData.reflectivity = surfaceData.metallic;
//     brdfData.perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(surfaceData.smoothness);
//     brdfData.roughness = max(PerceptualRoughnessToRoughness(brdfData.perceptualRoughness), HALF_MIN_SQRT);
//     brdfData.roughness2 = max(brdfData.roughness * brdfData.roughness, HALF_MIN);
//     brdfData.normalizationTerm = brdfData.roughness * half(4.0) + half(2.0);
//     brdfData.roughness2MinusOne = brdfData.roughness2 - half(1.0);
// }


//--------------------------------------------------------------------------------------------------
// Def
//--------------------------------------------------------------------------------------------------

// keep this file in sync with LitGBufferPass.hlsl
struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    half4 tangentWS : TEXCOORD3; // xyz: tangent, w: sign

    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};


struct ShadingData
{
    float3 normalWS;

    float3 albedo;
    float3 emissive;
    float metallic;
    float occlusion;
    float smoothness;

    float perceptualRoughness;
    float roughness;
    float roughness2;

    float3 diffuseColor;
    float3 fresnel0;
    real fresnel90;

    //GGX
    float partLambdaV;
    float energyCompensation;


    float3 specularFGD; // Store preintegrated BSDF for both specular and diffuse
    float diffuseFGD;

    #ifdef _LIGHT_LAYERS
    uint meshRenderingLayers;
    #endif
};

ShadingData GetShadingData(Varyings input)
{
    float2 uv = input.uv;
    ShadingData shadingData;
    ZERO_INITIALIZE(ShadingData, shadingData);


    half4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);


    AlphaDiscard(albedoAlpha.a * _BaseColor.a, _Cutoff);
    shadingData.albedo = albedoAlpha.rgb * _BaseColor.rgb;

    half4 meor = SAMPLE_TEXTURE2D_X(_PBRMap, sampler_PBRMap, uv);


    shadingData.smoothness = 1 - saturate(mad(meor.a, _RoughnessStart, _RoughnessEnd));
    shadingData.metallic = saturate(mad(meor.r, _MetallicStart, _MetallicEnd));
    shadingData.occlusion = saturate(mad(meor.b, _OcclusionStart, _OcclusionEnd));
    shadingData.emissive = meor.g * _EmissionColor * shadingData.albedo;


    half3 normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_NormalMap, sampler_NormalMap), _NormalScale);


    #if defined(_NORMALMAP) || defined(_DETAIL)
    float sgn = input.tangentWS.w; // should be either +1 or -1
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    shadingData.normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz));
    #else
    shadingData.normalWS = input.normalWS;
    #endif

    shadingData.normalWS = NormalizeNormalPerPixel(shadingData.normalWS);


    shadingData.perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(shadingData.smoothness);
    shadingData.roughness = PerceptualRoughnessToRoughness(shadingData.perceptualRoughness);
    // We need to max this with Angular Diameter, which result in minRoughness.
    shadingData.roughness2 = max(shadingData.roughness * shadingData.roughness, FLT_MIN);

    shadingData.diffuseColor = ComputeDiffuseColor(shadingData.albedo, shadingData.metallic);
    shadingData.fresnel0 = ComputeFresnel0(shadingData.albedo, shadingData.metallic, DEFAULT_SPECULAR_VALUE);
    shadingData.fresnel90 = ComputeF90(shadingData.fresnel0);

    // #ifdef _LIGHT_LAYERS
    // float4 renderingLayers = LOAD_TEXTURE2D_X(MERGE_NAME(_, GBUFFER_LIGHT_LAYERS), posInput.positionSS);
    // shadingData.meshRenderingLayers = DecodeMeshRenderingLayer(renderingLayers.r);
    // #endif

    return shadingData;
}


struct PreLightData
{
    float NdotV; // Could be negative due to normal mapping, use ClampNdotV()

    // GGX
    float partLambdaV;
    float energyCompensation;


    // IBL
    float3 iblR; // Reflected specular direction, used for IBL in EvaluateBSDF_Env()
    float iblPerceptualRoughness;

    float3 specularFGD; // Store preintegrated BSDF for both specular and diffuse
    float diffuseFGD;


    // Area lights
    // TODO: 'orthoBasisViewNormal' is just a rotation around the normal and should thus be just 1x VGPR.
    float3x3 orthoBasisViewNormal; // Right-handed view-dependent orthogonal basis around the normal (6x VGPRs)
    // Warning: these matrices are transposed! They are designed to transform row vectors via mul(V, M).
    float3x3 ltcTransformDiffuse; // Inverse transformation for Lambertian or Disney Diffuse        (4x VGPRs)
    float3x3 ltcTransformSpecular[2]; // Inverse transformation for GGX - 2 specular lobes              (4x VGPRs * 2)
    #if MATERIALFEATUREFLAGS_SSS_DUAL_LOBE
    float ltcLobeMix; // We store it only for area lights to save the vgpr otherwise    (1x VGPR)
    #endif


    static PreLightData Init(float3 N, float3 V, ShadingData shadingData)
    {
        PreLightData preLightData;
        preLightData.NdotV = dot(N, V);
        preLightData.iblPerceptualRoughness = shadingData.perceptualRoughness;
        float clampedNdotV = ClampNdotV(preLightData.NdotV);

        preLightData.partLambdaV = GetSmithJointGGXPartLambdaV(clampedNdotV, shadingData.roughness);

        float specularReflectivity;
        GetPreIntegratedFGDGGXAndDisneyDiffuse(clampedNdotV, preLightData.iblPerceptualRoughness, shadingData.fresnel0, shadingData.fresnel90,
                                               preLightData.specularFGD, preLightData.diffuseFGD, specularReflectivity);

        preLightData.energyCompensation = 1.0 / specularReflectivity - 1.0;

        float3 iblN;
        preLightData.iblR = reflect(-V, iblN);
        preLightData.orthoBasisViewNormal = GetOrthoBasisViewNormal(V, N, preLightData.NdotV);
        #ifdef USE_DIFFUSE_LAMBERT_BRDF
        preLightData.ltcTransformDiffuse = k_identity3x3;

        if (HasFlag(bsdfData.materialFeatures, MATERIALFEATUREFLAGS_SSS_DIFFUSE_POWER))
            ModifyLambertLTCTransformForDiffusePower(preLightData.ltcTransformDiffuse, GetDiffusePower(bsdfData.diffusionProfileIndex));
        #else
        preLightData.ltcTransformDiffuse = SampleLtcMatrix(shadingData.perceptualRoughness, clampedNdotV, LTCLIGHTINGMODEL_DISNEY_DIFFUSE);

        // if (HasFlag(bsdfData.materialFeatures, MATERIALFEATUREFLAGS_SSS_DIFFUSE_POWER))
        //     ModifyDisneyLTCTransformForDiffusePower(preLightData.ltcTransformDiffuse, GetDiffusePower(bsdfData.diffusionProfileIndex), bsdfData.perceptualRoughness, clampedNdotV);
        #endif
        float perceptualRoughnessA = shadingData.perceptualRoughness;
        preLightData.ltcTransformSpecular[0] = SampleLtcMatrix(perceptualRoughnessA, clampedNdotV, LTCLIGHTINGMODEL_GGX);

        return preLightData;
    }
};
#endif
