#ifndef VIVIDRP_STANDARD_LIT_GBUFFER_PASS_INCLUDED
#define VIVIDRP_STANDARD_LIT_GBUFFER_PASS_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/VividProbeVolume.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float4 _BaseMap_ST;
    float4 _EmissionColor;
    float _Cutoff;
    float _Smoothness;
    float _SmoothnessTextureChannel;
    float _Metallic;
    float _BumpScale;
    float _OcclusionStrength;
    float _ClearCoatMask;
    float _ClearCoatSmoothness;
    float _AlphaClip;
    float _WorkflowMode;
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_OpacityMap);
SAMPLER(sampler_OpacityMap);
TEXTURE2D(_MetallicGlossMap);
SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_RoughnessMap);
SAMPLER(sampler_RoughnessMap);
TEXTURE2D(_BumpMap);
SAMPLER(sampler_BumpMap);
TEXTURE2D(_OcclusionMap);
SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_EmissionMap);
SAMPLER(sampler_EmissionMap);

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS : TEXCOORD0;
    float4 tangentWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float2 lightmapUV : TEXCOORD3;
    float3 positionWS : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

float3 UnpackVividNormalScale(float4 packedNormal, float scale)
{
    float3 normalTS;
    normalTS.xy = packedNormal.wy * 2.0 - 1.0;
    normalTS.xy *= scale;
    normalTS.z = sqrt(saturate(1.0 - dot(normalTS.xy, normalTS.xy)));
    return normalTS;
}

Varyings Vert(Attributes input)
{
    Varyings output;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
    output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    output.lightmapUV = TransformVividLightmapUV(input.uv1);
    return output;
}

float4 SampleBase(float2 uv)
{
    float4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
#if defined(_OPACITYMAP)
    baseSample.a *= SAMPLE_TEXTURE2D(_OpacityMap, sampler_OpacityMap, uv).r;
#endif
    return baseSample;
}

void ApplyAlphaClip(float alpha)
{
#if defined(_ALPHATEST_ON)
    clip(alpha - _Cutoff);
#endif
}

float2 SampleMetallicSmoothness(float2 uv, float baseAlpha)
{
    float metallic = saturate(_Metallic);
    float smoothness = saturate(_Smoothness);

#if defined(_METALLICSPECGLOSSMAP)
    float4 metallicGlossSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
    metallic = saturate(metallicGlossSample.r * _Metallic);

#if defined(_ROUGHNESSMAP)
    float roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uv).r;
    smoothness = (1.0 - roughness) * _Smoothness;
#elif defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
    smoothness = baseAlpha * _Smoothness;
#else
    smoothness = metallicGlossSample.a * _Smoothness;
#endif
#elif defined(_ROUGHNESSMAP)
    float roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uv).r;
    smoothness = (1.0 - roughness) * _Smoothness;
#elif defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
    smoothness = baseAlpha * _Smoothness;
#endif

    return float2(metallic, saturate(smoothness));
}

float SampleAmbientOcclusion(float2 uv)
{
#if defined(_OCCLUSIONMAP)
    float occlusion = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g;
    return saturate(lerp(1.0, occlusion, _OcclusionStrength));
#else
    return 1.0;
#endif
}

float3 SampleEmission(float2 uv)
{
#if defined(_EMISSION)
    return max(SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb, 0.0);
#else
    return 0.0;
#endif
}

float3 SampleNormalWS(Varyings input)
{
    float3 normalWS = normalize(input.normalWS);
#if defined(_NORMALMAP)
    float3 tangentWS = normalize(input.tangentWS.xyz);
    float tangentSign = input.tangentWS.w * GetOddNegativeScale();
    float3 bitangentWS = normalize(cross(normalWS, tangentWS) * tangentSign);
    float3 normalTS = UnpackVividNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
    return normalize(normalTS.x * tangentWS + normalTS.y * bitangentWS + normalTS.z * normalWS);
#else
    return normalWS;
#endif
}

float3 SampleStandardLitBakedGI(float2 lightmapUV, float3 normalWS, float3 positionWS)
{
#if defined(LIGHTMAP_ON)
    return SampleVividBakedGI(lightmapUV, normalWS);
#else
    return SampleVividProbeVolume(
        positionWS,
        normalWS,
        GetWorldSpaceNormalizeViewDir(positionWS),
        GetMeshRenderingLayerMask());
#endif
}

float HasStandardLitBakedGI()
{
#if defined(LIGHTMAP_ON)
    return 1.0;
#else
    return VividHasProbeVolumeGI() ? 1.0 : 0.0;
#endif
}

VividGBufferSurfaceData BuildStandardLitSurfaceData(Varyings input)
{
    float4 baseSample = SampleBase(input.uv);
    ApplyAlphaClip(baseSample.a);

    float2 metallicSmoothness = SampleMetallicSmoothness(input.uv, baseSample.a);

    VividGBufferSurfaceData surfaceData;
    surfaceData.baseColor = baseSample.rgb;
    surfaceData.normalWS = SampleNormalWS(input);
    surfaceData.linearRoughness = (1.0 - metallicSmoothness.y) * (1.0 - metallicSmoothness.y);
    surfaceData.metallic = metallicSmoothness.x;
    surfaceData.ambientOcclusion = SampleAmbientOcclusion(input.uv);
    surfaceData.customData1 = 0.0;

#if defined(_CLEARCOAT)
    float clearCoatMask = saturate(_ClearCoatMask);
    surfaceData.customData = clearCoatMask;
    surfaceData.materialId = clearCoatMask > 0.0 ? VIVID_GBUFFER_MATERIAL_CLEARCOAT : VIVID_GBUFFER_MATERIAL_STANDARD;
#else
    surfaceData.customData = 0.0;
    surfaceData.materialId = VIVID_GBUFFER_MATERIAL_STANDARD;
#endif

    surfaceData.emissive = SampleEmission(input.uv);
    surfaceData.bakedGI = SampleStandardLitBakedGI(input.lightmapUV, surfaceData.normalWS, input.positionWS);
    surfaceData.hasBakedGI = HasStandardLitBakedGI();
    return surfaceData;
}

VividGBufferFragmentOutput FragGBuffer(Varyings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    return PackVividGBufferSurfaceData(BuildStandardLitSurfaceData(input));
}

half4 FragPreDepth(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    ApplyAlphaClip(SampleBase(input.uv).a);
    return 0.0;
}

half4 FragDebug(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    VividGBufferSurfaceData surfaceData = BuildStandardLitSurfaceData(input);
    float3 debugColor = surfaceData.baseColor + surfaceData.emissive;
    return half4(debugColor, 1.0);
}

#endif
