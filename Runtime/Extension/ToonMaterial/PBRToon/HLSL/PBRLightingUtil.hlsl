
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/Filter/PreIntegratedFGD/Shader/PreIntegratedFGD.hlsl"

#ifndef kDieletricSpec
#define kDieletricSpec half4(0.04, 0.04, 0.04, 1.0 - 0.04) // standard dielectric reflectivity coef at incident angle (= 4%)
#endif

#define TRANSMISSION_WRAP_ANGLE (PI/12)
#define TRANSMISSION_WRAP_LIGHT cos(PI/2 - TRANSMISSION_WRAP_ANGLE)

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

void InitializeCharacterInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

    inputData.positionWS = input.positionWS;

    inputData.positionCS = input.positionCS;

    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    #if defined(_NORMALMAP) || defined(_DETAIL)
    float sgn = input.tangentWS.w;      // should be either +1 or -1
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);

    #if defined(_NORMALMAP)
    inputData.tangentToWorld = tangentToWorld;
    #endif
    inputData.normalWS = TransformTangentToWorld(normalTS, tangentToWorld);
    #else
    inputData.normalWS = input.normalWS;
    #endif

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    inputData.viewDirectionWS = viewDirWS;

    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);

    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
}

inline void InitializeCharacterSurfaceData(float2 uv, out SurfaceData surfaceData)
{
    surfaceData = (SurfaceData)0;

    half4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_TrilinearClamp, uv);
    surfaceData.alpha = AlphaDiscard(albedoAlpha.a * _BaseColor.a, _Cutoff);
    surfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;

    half4 meor = SAMPLE_TEXTURE2D_X(_PBRMap, sampler_TrilinearClamp, uv);


    surfaceData.smoothness = 1 - saturate(mad(meor.a, _RoughnessStart, _RoughnessEnd));
    surfaceData.metallic = saturate(mad(meor.r, _MetallicStart, _MetallicEnd));
    surfaceData.occlusion = saturate(mad(meor.b, _OcclusionStart, _OcclusionEnd));
    surfaceData.emission = meor.g * _EmissionColor * surfaceData.albedo;


    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D_X(_NormalMap, sampler_TrilinearClamp, uv), _NormalScale);

    surfaceData.normalTS = normalize(normalTS);
}


void InitializeCharacterBRDFData(SurfaceData surfaceData, out BRDFData brdfData)
{
    brdfData = (BRDFData)0;
    brdfData.albedo = surfaceData.albedo;

    brdfData.diffuse = surfaceData.albedo * (1 - surfaceData.metallic);
    brdfData.specular = lerp(kDieletricSpec.rgb, surfaceData.albedo, surfaceData.metallic);
    brdfData.reflectivity = surfaceData.metallic;
    brdfData.perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(surfaceData.smoothness);
    brdfData.roughness = max(PerceptualRoughnessToRoughness(brdfData.perceptualRoughness), HALF_MIN_SQRT);
    brdfData.roughness2 = max(brdfData.roughness * brdfData.roughness, HALF_MIN);
    brdfData.normalizationTerm = brdfData.roughness * half(4.0) + half(2.0);
    brdfData.roughness2MinusOne = brdfData.roughness2 - half(1.0);
}





