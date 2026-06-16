#ifndef VIVIDRP_OBJECT_MOTION_VECTORS_INCLUDED
#define VIVIDRP_OBJECT_MOTION_VECTORS_INCLUDED

#pragma target 3.5
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_instancing

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/MotionVectorsCommon.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float4 _BaseMap_ST;
    float _Cutoff;
    float _AlphaClip;
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_OpacityMap);
SAMPLER(sampler_OpacityMap);

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    float3 positionOld : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float4 positionCSNoJitter : TEXCOORD0;
    float4 previousPositionCSNoJitter : TEXCOORD1;
    float2 uv : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

float SampleMotionVectorAlpha(float2 uv)
{
    float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
    alpha *= SAMPLE_TEXTURE2D(_OpacityMap, sampler_OpacityMap, uv).r;
    return alpha;
}

Varyings Vert(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M, input.positionOS));
    output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;

    float4 previousPositionOS = unity_MotionVectorsParams.x == 1.0
        ? float4(input.positionOld, 1.0)
        : input.positionOS;

    output.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, mul(UNITY_PREV_MATRIX_M, previousPositionOS));
    return output;
}

float4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    if (_AlphaClip > 0.5)
        clip(SampleMotionVectorAlpha(input.uv) - _Cutoff);

    return EncodeMotionVectorFromCsPositions(input.positionCSNoJitter, input.previousPositionCSNoJitter);
}

#endif
