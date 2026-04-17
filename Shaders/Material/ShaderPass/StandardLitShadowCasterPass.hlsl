#ifndef VIVIDRP_STANDARD_LIT_SHADOW_CASTER_PASS_INCLUDED
#define VIVIDRP_STANDARD_LIT_SHADOW_CASTER_PASS_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Input.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

float4 _ShadowBias;
float3 _LightDirection;
float3 _LightPosition;

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
    float3 normalOS : NORMAL;
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

float3 ApplyVividShadowBias(float3 positionWS, float3 lightDirectionWS)
{
    return positionWS + lightDirectionWS * _ShadowBias.x;
}

float4 ApplyVividShadowClamping(float4 positionCS)
{
#if UNITY_REVERSED_Z
    float clampedZ = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
#else
    float clampedZ = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
#endif

    positionCS.z = lerp(positionCS.z, clampedZ, round(_ShadowBias.z) == 1.0 ? 1.0 : 0.0);
    return positionCS;
}

Varyings Vert(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    output.positionCS = TransformWorldToHClip(ApplyVividShadowBias(positionWS, lightDirectionWS));
    output.positionCS = ApplyVividShadowClamping(output.positionCS);
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
