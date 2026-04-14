#ifndef VIVIDRP_STANDARD_LIT_SHADOW_CASTER_PASS_INCLUDED
#define VIVIDRP_STANDARD_LIT_SHADOW_CASTER_PASS_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Input.hlsl"
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

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vert(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    return output;
}

half4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);

#if defined(_ALPHATEST_ON)
    float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
    #if defined(_OPACITYMAP)
        alpha *= SAMPLE_TEXTURE2D(_OpacityMap, sampler_OpacityMap, input.uv).r;
    #endif
    clip(alpha - _Cutoff);
#endif

    return 0;
}

#endif
