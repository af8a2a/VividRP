Shader "Hidden/VividRP/PostProcessing/LensFlareScreenSpace"
{
    HLSLINCLUDE
        #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
        #pragma target 5.0

        #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
        #define URP_LENS_FLARE_SCREEN_SPACE
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "LensFlareScreenSpac Prefilter"
            Tags { "LightMode" = "Forward" "RenderQueue" = "Transparent" }

            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment FragmentPrefilter
                #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareScreenSpaceCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LensFlareScreenSpace Downsample"
            Tags { "LightMode" = "Forward" "RenderQueue" = "Transparent" }

            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment FragmentDownsample
                #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareScreenSpaceCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LensFlareScreenSpace Upsample"
            Tags { "LightMode" = "Forward" "RenderQueue" = "Transparent" }

            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment FragmentUpsample
                #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareScreenSpaceCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LensFlareScreenSpace Composition"
            Tags { "LightMode" = "Forward" "RenderQueue" = "Transparent" }

            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment FragmentComposition
                #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareScreenSpaceCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LensFlareScreenSpace Write to BloomTexture"
            Tags { "LightMode" = "Forward" "RenderQueue" = "Transparent" }

            Blend One One
            BlendOp Add
            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment FragmentWrite
                #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareScreenSpaceCommon.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
}
