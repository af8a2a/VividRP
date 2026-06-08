#ifndef VIVIDRP_STANDARD_LIT_META_PASS_INCLUDED
#define VIVIDRP_STANDARD_LIT_META_PASS_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/MetaPass.hlsl"
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
    float _ReceiveSSR;
    float _ReceiveDecals;
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_OpacityMap);
SAMPLER(sampler_OpacityMap);
TEXTURE2D(_MetallicGlossMap);
SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_EmissionMap);
SAMPLER(sampler_EmissionMap);

struct Attributes
{
    float3 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    float2 uv2 : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
#if defined(EDITOR_VISUALIZATION)
    float2 vizUV : TEXCOORD1;
    float4 lightCoord : TEXCOORD2;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

float2 TransformBaseUV(float2 uv)
{
    return uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
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

float SampleMetallic(float2 uv)
{
    float metallic = saturate(_Metallic);
#if defined(_METALLICSPECGLOSSMAP)
    metallic = saturate(SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv).r * _Metallic);
#endif
    return metallic;
}

float3 SampleEmission(float2 uv)
{
#if defined(_EMISSION)
    return max(SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb, 0.0);
#else
    return float3(0.0, 0.0, 0.0);
#endif
}

float3 GetLightTransportDiffuseColor(float3 baseColor, float metallic)
{
    return saturate(baseColor) * (1.0 - saturate(metallic));
}

Varyings Vert(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionCS = UnityMetaVertexPosition(
        input.positionOS,
        input.uv1,
        input.uv2,
        unity_LightmapST,
        unity_DynamicLightmapST);
    output.uv = TransformBaseUV(input.uv);

#if defined(EDITOR_VISUALIZATION)
    UnityEditorVizData(input.positionOS, input.uv, input.uv1, input.uv2, output.vizUV, output.lightCoord);
#endif

    return output;
}

float4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float4 baseSample = SampleBase(input.uv);
    ApplyAlphaClip(baseSample.a);

    UnityMetaInput metaInput;
    metaInput.Albedo =GetLightTransportDiffuseColor(baseSample.rgb, SampleMetallic(input.uv));
    metaInput.Emission = SampleEmission(input.uv);
#if defined(EDITOR_VISUALIZATION)
    metaInput.VizUV = input.vizUV;
    metaInput.LightCoord = input.lightCoord;
#endif

    return UnityMetaFragment(metaInput);
}

#endif
