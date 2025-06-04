Shader "LPM"
{
    HLSLINCLUDE
    #pragma exclude_renderers gles
    #define SHADER_TARGET 50
    float _SoftGap;
    // Range of 0 to a little over zero, controls how much feather region in out-of-gamut mapping, 0=clip.
    float _HdrMax; // Maximum input value.
    float _Exposure; // Number of stops between 'hdrMax' and 18% mid-level on input.
    float _Contrast; // Input range {0.0 (no extra contrast) to 1.0 (maximum contrast)}.
    float _ShoulderContrast; // Shoulder shaping, 1.0 = no change (fast path).
    float3 _Saturation; // A per channel adjustment, use <0 decrease, 0=no change, >0 increase.
    float3 _Crosstalk; // One channel must be 1.0, the rest can be <= 1.0 but not zero.
    float _LPMIntensity;
    float _LPMExposure;
    float2 _DisplayMinMaxLuminance;
    #include "LPMCommon.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"


    half4 Frag_LPM(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
        half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        half3 color_raw = color.rgb;
        color.rgb = Apply_LPM(color);
        color.rgb = lerp(color_raw, color, _LPMIntensity);
        color.rgb *= _Exposure;
        return color;
    }
    ENDHLSL




    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100


        Pass
        {
            Name "Luma Preserving Mapping"

            HLSLPROGRAM
            #pragma multi_compile  SDR DISPLAYMODE_HDR10_SCRGB DISPLAYMODE_HDR10_2084

            #pragma vertex Vert
            #pragma fragment Frag_LPM
            ENDHLSL
        }

    }
}