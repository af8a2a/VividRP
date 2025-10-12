#ifndef VIVID_PBRTOON_LIT_BASE_PASS_INCLUDED
#define VIVID_PBRTOON_LIT_BASE_PASS_INCLUDED


struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;


    //smooth normal
    float2 octahedronSmoothNormal : TEXCOORD7;
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


//--------------------------------------------------------------------------------------------------
// Def
//--------------------------------------------------------------------------------------------------



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

#include "Evalution/DirectionalLightEvalution.hlsl"
#include "Evalution/PunctualLightEvaluation.hlsl"
#include "Evalution/AreaLightEvaluation.hlsl"

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

// Used in Standard (Physically Based) shader
Varyings ToonLitPassVertex(Attributes input)
{
    Varyings output;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    real sign = input.tangentOS.w * GetOddNegativeScale();
    float3 bitangent = cross(input.normalOS.xyz, input.tangentOS.xyz).xyz * sign;
    float3 normalTS = OctahedronToUnitVector(input.octahedronSmoothNormal.xy * 2.0 - 1.0);
    input.normalOS = mul(normalTS.xyz, float3x3(input.tangentOS.xyz, bitangent.xyz, input.normalOS.xyz)).xyz;

    
    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);


    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

    // already normalized from normal transform to WS.
    output.normalWS = normalInput.normalWS;

    half4 tangentWS = half4(normalInput.tangentWS.xyz, sign);
    output.tangentWS = tangentWS;


    output.positionWS = vertexInput.positionWS;
    output.positionCS = vertexInput.positionCS;



    return output;
}


DirectLighting Lightloop(Varyings input, ShadingData shadingData)
{
    float4 positionSS = input.positionCS;
    uint2 tileIndex = uint2(positionSS.xy) / GetTileSize();

    PositionInputs posInput = GetPositionInput(positionSS.xy, _ScreenSize.zw, positionSS.z, positionSS.w, input.positionWS.xyz, tileIndex);


    DirectLighting lightOutput;

    float3 N = shadingData.normalWS;
    float3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);

    PreLightData preLightData = PreLightData::Init(N, V, shadingData);
    float clampedNdotV = ClampNdotV(preLightData.NdotV);


    float3 specularFGD;
    float diffuseFGD;
    float reflectivity;
    GetPreIntegratedFGDGGXAndDisneyDiffuse(clampedNdotV, shadingData.perceptualRoughness, shadingData.fresnel0,
                                           specularFGD, diffuseFGD, reflectivity);
    shadingData.diffuseFGD = diffuseFGD;
    shadingData.specularFGD = specularFGD;
    // Ref: Practical multiple scattering compensation for microfacet models.
    // We only apply the formulation for metals.
    // For dielectrics, the change of reflectance is negligible.
    // We deem the intensity difference of a couple of percent for high values of roughness
    // to not be worth the cost of another precomputed table.
    // Note: this formulation bakes the BSDF non-symmetric!
    float energyCompensation = 1.0 / reflectivity - 1.0;

    float3 directDiffuse = 0;
    float3 directSpecular = 0;
    float3 indirectDiffuse = 0;
    float3 indirectSpecular = 0;

    // Shading


    DirectLighting dirLight = EvaluateDirectional(preLightData, shadingData, V);

    directDiffuse += dirLight.diffuse;
    directSpecular += dirLight.specular;


    float2 shadowCoord = TransformWorldToScreenShadowCoord(input.positionWS);
    float shadowAttenuation = SampleVividScreenSpaceShadowmap(shadowCoord).x;

    half3 shadowScatter = EvaluateShadowScatter(shadowAttenuation);
    
    directDiffuse *= shadowAttenuation * shadowScatter;
    directSpecular *= shadowAttenuation * shadowScatter;

    DirectLighting punctualLight = EvaluatePunctual(posInput, preLightData, shadingData, V);

    directDiffuse += punctualLight.diffuse;
    directSpecular += punctualLight.specular;

    DirectLighting areaLighting = EvaluateAreaHDRP(posInput, shadingData, V);
    directDiffuse += areaLighting.diffuse;
    directSpecular += areaLighting.specular;


    indirectDiffuse *= shadingData.occlusion;
    indirectSpecular *= shadingData.occlusion;
    lightOutput.diffuse = directDiffuse + indirectDiffuse;
    lightOutput.specular = directSpecular + indirectSpecular;
    lightOutput.specular *= 1.0 + shadingData.fresnel0 * energyCompensation;


    // lightOutput.specular = shadowAttenuation;
    return lightOutput;
}

half3 PackGBufferNormal(half3 normalWS)
{
    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);           // values between [-1, +1], must use fp32 on some platforms.
    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);   // values between [ 0, +1]
    return half3(PackFloat2To888(remappedOctNormalWS));               // values between [ 0, +1]
}


// Used in Standard (Physically Based) shader
half4 ToonLitPassFragment(Varyings input): SV_Target0
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);


    ShadingData shadingData = GetShadingData(input);


    DirectLighting lightOutput = Lightloop(input, shadingData);


    float3 diffuseLighting = lightOutput.diffuse;
    float3 specularLighting = lightOutput.specular;
    
    return float4(diffuseLighting + specularLighting + shadingData.emissive, 1.0);
}


#endif