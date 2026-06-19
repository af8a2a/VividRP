Shader "Hidden/VividRP/PostProcessing/LensFlareDataDriven"
{
    HLSLINCLUDE
        #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
        #pragma target 5.0
        #pragma multi_compile_fragment _ FLARE_INVERSE_SDF
        #pragma multi_compile _ FLARE_HAS_OCCLUSION

        #define DISABLE_TEXTURE2D_X_ARRAY
        #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"

        TEXTURE2D_X_FLOAT(_CameraDepthTexture);

        float4 GetScaledScreenParams()
        {
            return _ScaledScreenParams;
        }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "LensFlareAdditive"
            Tags { "LightMode" = "Forward" "RenderQueue" = "Transparent" }

            Blend One One
            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #define FLARE_ADDITIVE_BLEND
                #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LensFlareScreen"
            Tags { "LightMode" = "Forward" "RenderQueue" = "Transparent" }

            Blend One OneMinusSrcColor
            BlendOp Max
            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #define FLARE_SCREEN_BLEND
                #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LensFlarePremultiply"
            Tags { "LightMode" = "Forward" "RenderQueue" = "Transparent" }

            Blend One OneMinusSrcAlpha
            ColorMask RGB
            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #define FLARE_PREMULTIPLIED_BLEND
                #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LensFlareLerp"
            Tags { "LightMode" = "Forward" "RenderQueue" = "Transparent" }

            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #define FLARE_LERP_BLEND
                #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LensFlareOcclusion"

            Blend Off
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
                #pragma vertex vertOcclusion
                #pragma fragment fragOcclusion
                #define FLARE_COMPUTE_OCCLUSION
                #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareCommon.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
}
