Shader "TAA"
{
    Properties
    {
        _MainTex("Source", 2D) = "white" {}
    }

    HLSLINCLUDE
    #pragma target 3.5
    #pragma multi_compile _LOW_TAA _MIDDLE_TAA _HIGH_TAA
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    float3 _TAA_Params; // xy = offset, z = feedback
    TEXTURE2D_X(_MotionTexture);
    TEXTURE2D_X(_TAA_Pretexture);

    TEXTURE2D_X_FLOAT(_CameraDepthTexture);
    float4x4 _PrevViewProjM_TAA;
    float4x4 _I_P_Current_jittered;


    float2 historyPostion(float2 un_jitted_uv)
    {
        float depth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, un_jitted_uv).r;
        float2 motion = SAMPLE_TEXTURE2D(_MotionTexture, sampler_PointClamp, un_jitted_uv);

        #if UNITY_REVERSED_Z
        depth = 1.0 - depth;
        #endif
        depth = 2.0 * depth - 1.0;
        #if UNITY_UV_STARTS_AT_TOP
        un_jitted_uv.y = 1.0f - un_jitted_uv.y;
        #endif
        float3 viewPos = ComputeViewSpacePosition(un_jitted_uv, depth, _I_P_Current_jittered);
        float4 worldPos = float4(mul(unity_CameraToWorld, float4(viewPos, 1.0)).xyz, 1.0);
        float3 historyNDC = ComputeNormalizedDeviceCoordinatesWithZ(float3(un_jitted_uv - motion, depth));
        return historyNDC;
    }

    float4 clip_aabb(float3 aabb_min, float3 aabb_max, float4 avg, float4 input_texel)
    {
        float3 p_clip = 0.5 * (aabb_max + aabb_min);
        float3 e_clip = 0.5 * (aabb_max - aabb_min) + FLT_EPS;
        float4 v_clip = input_texel - float4(p_clip, avg.w);
        float3 v_unit = v_clip.xyz / e_clip;
        float3 a_unit = abs(v_unit);
        float ma_unit = max(a_unit.x, max(a_unit.y, a_unit.z));

        if (ma_unit > 1.0)
            return float4(p_clip, avg.w) + v_clip / ma_unit;
        else
            return input_texel;
    }

    void minmax(in float2 uv, out float4 color_min, out float4 color_max, out float4 color_avg)
    {
        float2 du = float2(_BlitTexture_TexelSize.x, 0.0);
        float2 dv = float2(0.0, _BlitTexture_TexelSize.y);
        #if defined(_HIGH_TAA)
            float4 ctl = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv - dv - du);
            float4 ctc = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv - dv);
            float4 ctr = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv - dv + du);
            float4 cml = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv - du);
            float4 cmc = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv);
            float4 cmr = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv + du);
            float4 cbl = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv + dv - du);
            float4 cbc = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv + dv);
            float4 cbr = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv + dv + du);

            color_min = min(ctl, min(ctc, min(ctr, min(cml, min(cmc, min(cmr, min(cbl, min(cbc, cbr))))))));
            color_max = max(ctl, max(ctc, max(ctr, max(cml, max(cmc, max(cmr, max(cbl, max(cbc, cbr))))))));

            color_avg = (ctl + ctc + ctr + cml + cmc + cmr + cbl + cbc + cbr) / 9.0;
        #elif defined(_MIDDLE_TAA)
            float2 ss_offset01 =  float2(-_BlitTexture_TexelSize.x, _BlitTexture_TexelSize.y);
            float2 ss_offset11 =  float2(_BlitTexture_TexelSize.x, _BlitTexture_TexelSize.y);
            float4 c00 = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv - ss_offset11);
            float4 c10 = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv - ss_offset01);
            float4 c01 = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv + ss_offset01);
            float4 c11 = SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, uv + ss_offset11);

            color_min = min(c00, min(c10, min(c01, c11)));
            color_max = max(c00, max(c10, max(c01, c11)));
            color_avg = (c00 + c10 + c01 + c11) / 4.0;
        #elif defined(_LOW_TAA)
        float2 ss_offset11 = float2(_BlitTexture_TexelSize.x, _BlitTexture_TexelSize.y);
        float4 c00 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - ss_offset11);
        float4 c11 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + ss_offset11);
        color_min = min(c00, c11);
        color_max = max(c00, c11);
        color_avg = (c00 + c11) / 2.0;
        #endif
    }

    float4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv_jitted = UnityStereoTransformScreenSpaceTex(input.texcoord);
        float2 un_jitted_uv = uv_jitted - _TAA_Params.xy;
        float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, un_jitted_uv);
        float4 color_min, color_max, color_avg;
        minmax(un_jitted_uv, color_min, color_max, color_avg);
        float2 previousTC = historyPostion(un_jitted_uv);
        float4 prev_color = SAMPLE_TEXTURE2D_X(_TAA_Pretexture, sampler_LinearClamp, previousTC);
        prev_color = clip_aabb(color_min, color_max, color_avg, prev_color);
        float4 result_color = lerp(color, prev_color, _TAA_Params.z);
        return result_color;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "TAA"
            HLSLPROGRAM
            #pragma multi_compile _LOW_TAA _MIDDLE_TAA _HIGH_TAA

            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}