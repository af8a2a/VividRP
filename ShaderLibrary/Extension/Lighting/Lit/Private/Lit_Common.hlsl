#ifndef LIT_COMMON_INCLUDED
#define LIT_COMMON_INCLUDED


#define DEFERRED_LIGHTING_TILE_SIZE (16)
#define DEFERRED_LIGHTING_GROUP_SIZE (DEFERRED_LIGHTING_TILE_SIZE / 2)
#define DEFERRED_LIGHTING_THREADS   (64)
#define HasShadingModel(stencilVal) ((stencilVal >> SHADINGMODELS_USER_MASK_BITS) > 0)
#define StencilToShadingModel(stencilVal) (stencilVal & SHADINGMODELS_MODELS_MASK)
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/Lighting/Common/LightingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/Lighting/Common/AreaLightCommon.hlsl"


//--------------------------------------------------------------------------------------------------
// Def
//--------------------------------------------------------------------------------------------------
// Shading data decode from gbuffer

struct ShadingData
{
    float3 normalWS;

    float3 albedo;
    float metallic;
    float occlusion;
    float smoothness;
    uint materialFlags;

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

    float specularOcclusion;

    #ifdef _LIGHT_LAYERS
    uint meshRenderingLayers;
    #endif
};

ShadingData DecodeShadingDataFromGBuffer(PositionInputs posInput)
{
    ShadingData shadingData;
    ZERO_INITIALIZE(ShadingData, shadingData);

    float4 gbuffer0 = LOAD_TEXTURE2D_X(_GBuffer0, posInput.positionSS);
    float4 gbuffer1 = LOAD_TEXTURE2D_X(_GBuffer1, posInput.positionSS);
    float4 gbuffer2 = LOAD_TEXTURE2D_X(_GBuffer2, posInput.positionSS);

    // Unpack GBuffer informations. Init datas.
    // See UnityGBuffer for more information.
    // GBuffer0: diffuse           diffuse         diffuse         materialFlags   (sRGB rendertarget)
    // GBuffer1: metallic/specular specular        specular        occlusion
    // GBuffer2: encoded-normal    encoded-normal  encoded-normal  smoothness
    shadingData.normalWS = normalize(UnpackNormal(gbuffer2.xyz));

    shadingData.albedo = gbuffer0.rgb;
    shadingData.metallic = MetallicFromReflectivity(gbuffer1.r); // TODO: handle with Specular Metallic and setup.
    shadingData.occlusion = gbuffer1.a;
    shadingData.smoothness = gbuffer2.a;
    shadingData.materialFlags = UnpackMaterialFlags(gbuffer0.a);

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

    shadingData.specularOcclusion = 1.0;
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
