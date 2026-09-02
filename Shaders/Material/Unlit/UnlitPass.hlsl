#ifndef VIVIDRP_UNLIT_PASS_INCLUDED
#define VIVIDRP_UNLIT_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Shadow/VividVirtualShadowMapCaster.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

#if defined(VIVIDRP_UNLIT_MOTION_VECTOR_PASS)
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/MotionVectorsCommon.hlsl"
#endif

#if defined(VIVIDRP_UNLIT_META_PASS)
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/MetaPass.hlsl"
#endif

#if defined(VIVIDRP_UNLIT_SHADOW_CASTER_PASS)
float4 _ShadowBias;
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _UnlitColor;
    float4 _UnlitColorMap_ST;
    float4 _EmissiveColor;
    float4 _EmissiveColorMap_ST;
    float _AlphaCutoff;
    float _AlphaRemapMin;
    float _AlphaRemapMax;
    float _EmissiveExposureWeight;
    float _AlbedoAffectEmissive;
    float _SurfaceType;
    float _BlendMode;
CBUFFER_END

TEXTURE2D(_UnlitColorMap);
SAMPLER(sampler_UnlitColorMap);
TEXTURE2D(_EmissiveColorMap);
SAMPLER(sampler_EmissiveColorMap);

float2 TransformUnlitUV(float2 uv)
{
    return uv * _UnlitColorMap_ST.xy + _UnlitColorMap_ST.zw;
}

float4 SampleUnlitColor(float2 uv)
{
    float4 colorSample = SAMPLE_TEXTURE2D(_UnlitColorMap, sampler_UnlitColorMap, uv);
    float alpha = lerp(_AlphaRemapMin, _AlphaRemapMax, colorSample.a) * _UnlitColor.a;
    return float4(colorSample.rgb * _UnlitColor.rgb, alpha);
}

float3 SampleUnlitEmission(float2 uv, float3 baseColor)
{
    float3 emissive = _EmissiveColor.rgb;

#if defined(_EMISSIVE_COLOR_MAP)
    float2 emissiveUV = uv * _EmissiveColorMap_ST.xy + _EmissiveColorMap_ST.zw;
    emissive *= SAMPLE_TEXTURE2D(_EmissiveColorMap, sampler_EmissiveColorMap, emissiveUV).rgb;
#endif

    emissive *= lerp(1.0.xxx, saturate(baseColor), saturate(_AlbedoAffectEmissive));
    return max(emissive, 0.0.xxx);
}

float3 ApplyUnlitEmissionExposure(float3 emissive)
{
    float3 emissiveRcpExposure = emissive * VividGetOneOverPreExposure();
    return lerp(emissiveRcpExposure, emissive, saturate(_EmissiveExposureWeight));
}

void ApplyUnlitAlphaClip(float alpha)
{
#if defined(_ALPHATEST_ON)
    clip(alpha - _AlphaCutoff);
#endif
}

#if defined(VIVIDRP_UNLIT_MOTION_VECTOR_PASS)

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

Varyings Vert(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M, input.positionOS));
    output.uv = TransformUnlitUV(input.uv);

    float4 previousPositionOS = unity_MotionVectorsParams.x == 1.0
        ? float4(input.positionOld, 1.0)
        : input.positionOS;

    output.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, mul(UNITY_PREV_MATRIX_M, previousPositionOS));
    return output;
}

float4 FragMotionVectors(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float4 colorSample = SampleUnlitColor(input.uv);
    ApplyUnlitAlphaClip(colorSample.a);
    return EncodeMotionVectorFromCsPositions(input.positionCSNoJitter, input.previousPositionCSNoJitter);
}

#else

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
#if defined(VIVIDRP_UNLIT_META_PASS) && defined(EDITOR_VISUALIZATION)
    float2 vizUV : TEXCOORD1;
    float4 lightCoord : TEXCOORD2;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

#if defined(VIVIDRP_UNLIT_SHADOW_CASTER_PASS)
float4 ApplyVividUnlitShadowClamping(float4 positionCS)
{
#if UNITY_REVERSED_Z
    float clampedZ = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
#else
    float clampedZ = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
#endif

    positionCS.z = lerp(positionCS.z, clampedZ, round(_ShadowBias.z) == 1.0 ? 1.0 : 0.0);
    return positionCS;
}
#endif

Varyings Vert(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

#if defined(VIVIDRP_UNLIT_META_PASS)
    output.positionCS = UnityMetaVertexPosition(
        input.positionOS,
        input.uv1,
        input.uv2,
        unity_LightmapST,
        unity_DynamicLightmapST);
#if defined(EDITOR_VISUALIZATION)
    UnityEditorVizData(input.positionOS, input.uv, input.uv1, input.uv2, output.vizUV, output.lightCoord);
#endif
#else
    output.positionCS = TransformObjectToHClip(input.positionOS);
#if defined(VIVIDRP_UNLIT_SHADOW_CASTER_PASS)
    output.positionCS = ApplyVividUnlitShadowClamping(output.positionCS);
#endif
#endif

    output.uv = TransformUnlitUV(input.uv);
    return output;
}

float4 FragPreDepth(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    ApplyUnlitAlphaClip(SampleUnlitColor(input.uv).a);
    return 0.0;
}

#if defined(VIVID_VSM_CASTER)
void FragShadow(Varyings input)
#else
float4 FragShadow(Varyings input) : SV_Target
#endif
{
    UNITY_SETUP_INSTANCE_ID(input);

    ApplyUnlitAlphaClip(SampleUnlitColor(input.uv).a);
    VividWriteVSMDepth(input.positionCS);
#if !defined(VIVID_VSM_CASTER)
    return 0.0;
#endif
}

float4 FragForward(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float4 colorSample = SampleUnlitColor(input.uv);
    ApplyUnlitAlphaClip(colorSample.a);

    float3 emissive = ApplyUnlitEmissionExposure(SampleUnlitEmission(input.uv, colorSample.rgb));
    return float4(VividApplyPreExposure(colorSample.rgb + emissive), colorSample.a);
}

#if defined(VIVIDRP_UNLIT_META_PASS)
float4 FragMeta(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float4 colorSample = SampleUnlitColor(input.uv);
    ApplyUnlitAlphaClip(colorSample.a);

    UnityMetaInput metaInput;
    metaInput.Albedo = 0.0.xxx;
    metaInput.Emission = SampleUnlitEmission(input.uv, colorSample.rgb);
#if defined(EDITOR_VISUALIZATION)
    metaInput.VizUV = input.vizUV;
    metaInput.LightCoord = input.lightCoord;
#endif

    return UnityMetaFragment(metaInput);
}
#endif

#endif

#endif
