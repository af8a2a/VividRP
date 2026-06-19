Shader "VividRP/Tools/ColorChecker"
{
    Properties
    {
        [HideInInspector] _CheckerTexture("Checker Texture", 2D) = "gray" {}
        [HideInInspector] _rawTexture("Raw Texture", 2D) = "black" {}
        [HideInInspector] _Compare_to_Unlit("Compare To Unlit", Float) = 0
        [HideInInspector] _NumberOfFields("Number Of Fields", Float) = 24
        [HideInInspector] _FieldsPerRow("Fields Per Row", Float) = 6
        [HideInInspector] _gridThickness("Grid Thickness", Float) = 0.05
        [HideInInspector] _SquareSize("Square Size", Float) = 0.1
        [HideInInspector] _Add_Gradient("Add Gradient", Float) = 0
        [HideInInspector] _Gradient_Color_A("Gradient Color A", Color) = (0.075, 0.078, 0.086, 1)
        [HideInInspector] _Gradient_Color_B("Gradient Color B", Color) = (0.914, 0.914, 0.890, 1)
        [HideInInspector] _gradient_power("Gradient Power", Float) = 2.2
        [HideInInspector] _sphereMode("Sphere Mode", Float) = 0
        [HideInInspector] _material_mode("Material Mode", Float) = 0
        [HideInInspector] _texture_mode("Texture Mode", Float) = 0
        [HideInInspector] _reflection_mode("Reflection Mode", Float) = 0
        [HideInInspector] _rawTextureAvailable("Raw Texture Available", Float) = 0
        [HideInInspector] _rawTexturePreExposure("Raw Texture Pre Exposure", Float) = 1
        [HideInInspector] _textureSlice("Texture Slice", Float) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "VividPreDepth"
            Tags { "LightMode" = "VividPreDepth" }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma vertex Vert
                #pragma fragment FragPreDepth

                #define VIVID_COLOR_CHECKER_PRE_DEPTH 1
                #include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/Tools/ColorCheckerPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "VividGBuffer"
            Tags { "LightMode" = "VividGBuffer" }

            Blend One Zero
            ZWrite On
            ZTest Equal
            Cull Back

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma multi_compile _ LIGHTMAP_ON
                #pragma multi_compile _ DIRLIGHTMAP_COMBINED
                #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
                #pragma vertex Vert
                #pragma fragment FragGBuffer

                #include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/Tools/ColorCheckerPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "SRPDefaultUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma multi_compile _ LIGHTMAP_ON
                #pragma multi_compile _ DIRLIGHTMAP_COMBINED
                #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
                #pragma vertex Vert
                #pragma fragment FragDebug

                #include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/Tools/ColorCheckerPass.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
