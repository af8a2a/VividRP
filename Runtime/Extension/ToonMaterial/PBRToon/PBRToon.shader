Shader "Universal Render Pipeline/PBR Toon"
{
    Properties
    {


        [Main(PBRParameterSettingGroup)] _PBRGroup ("PBR Parameter", Float) = 0.0
        [ShowIf(_PBRGroup, Equal, 1)][Sub(PBRParameterSettingGroup)] _Cutoff("Cutoff", Range(0, 1)) = 1

        [ShowIf(_PBRGroup, Equal, 1)][Tex(PBRParameterSettingGroup,_BaseColor)] _BaseMap ("Albedo Map", 2D) = "white" { }
        [HideInInspector] _BaseColor (" ", Color) = (1, 1, 1, 1)
        [ShowIf(_PBRGroup, Equal, 1)][TexKW(PBRParameterSettingGroup,_NORMALMAP,_NormalScale)] _NormalMap ("Normal Map", 2D) = "bump" { }
        [HideInInspector] _NormalScale("Normal Scale", Range(0, 5)) = 1

        [ShowIf(_PBRGroup, Equal, 1)][Tex(PBRParameterSettingGroup,_EmissionColor)] _PBRMap ("PBR Map", 2D) = "white" { }//Metallic & Emission & Occlusion & Roughness
        [HideInInspector][HDR] _EmissionColor ("Emission Color", Color) = (1, 1, 1, 1)



        [ShowIf(_PBRGroup, Equal, 1)][MinMaxSlider(PBRParameterSettingGroup,_MetallicStart, _MetallicEnd)] _MetallicSlider ("Metallic Remap Slider (0 - 1)", Range(0.0, 1.0)) = 1.0
        [HideInInspector]_MetallicStart ("Start", Range(0.0, 1)) = 0.0
        [HideInInspector]_MetallicEnd ("End", Range(0.0, 1.0)) =1.0

        [ShowIf(_PBRGroup, Equal, 1)][MinMaxSlider(PBRParameterSettingGroup,_RoughnessStart, _RoughnessEnd)] _RoughnessSlider ("Roughness Remap Slider (0 - 1)", Range(0.0, 1.0)) = 1.0
        [HideInInspector]_RoughnessStart ("Start", Range(0.0, 1)) = 0.0
        [HideInInspector]_RoughnessEnd ("End", Range(0.0, 1.0)) = 1.0

        [ShowIf(_PBRGroup, Equal, 1)] [MinMaxSlider(PBRParameterSettingGroup,_OcclusionRange, _OcclusionEnd)] _OcclusionSlider ("Occlusion Remap Slider (0 - 1)", Range(0.0, 1.0)) = 1.0
        [HideInInspector]_OcclusionRange ("Start", Range(0.0, 1)) = 0.0
        [HideInInspector]_OcclusionEnd ("End", Range(0.0, 1.0)) = 1.0


    }
    HLSLINCLUDE
    #include "HLSL/PBRToonInput.hlsl"
    ENDHLSL


    SubShader
    {
        // Universal Pipeline tag is required. If Universal render pipeline is not set in the graphics settings
        // this Subshader will fail. One can add a subshader below or fallback to Standard built-in to make this
        // material work with both Universal Render Pipeline and Builtin Unity Pipeline

        // ------------------------------------------------------------------
        //  Forward pass. Shades all light in a single pass. GI + emission + Fog
        Pass
        {
            Tags
            {
                "RenderType" = "Opaque"
                "RenderPipeline" = "UniversalPipeline"
                "UniversalMaterialType" = "Lit"
                "IgnoreProjector" = "True"
                "LightMode" = "CharacterForward"

            }


            HLSLPROGRAM
            #pragma target 4.5
            // -------------------------------------
            // Shader Stages
            #pragma vertex ToonLitPassVertex
            #pragma fragment ToonLitPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _NORMALMAP


            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS


            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "HLSL/PBRLighting.hlsl"
            ENDHLSL

        }


        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5

            // -------------------------------------
            // Shader Stages
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _ALPHATEST_ON

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Universal Pipeline keywords

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            // This is used during shadow map generation to differentiate between directional and punctual light shadows, as they use different formulas to apply Normal Bias
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // -------------------------------------
            // Includes
            #include "HLSL/PBRLightingShadowCaster.hlsl"
            ENDHLSL
        }


        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _ALPHATEST_ON

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "HLSL/PBRLightingDepthOnly.hlsl"
            ENDHLSL
        }


        // This pass is used when drawing to a _CameraNormalsTexture texture
        Pass
        {
            Name "DepthNormals"
            Tags
            {
                "LightMode" = "DepthNormals"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On


            HLSLPROGRAM
            #pragma target 4.5


            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ALPHATEST_ON

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            // -------------------------------------
            // Universal Pipeline keywords
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "HLSL/PBRLightingDepthNormal.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags
            {
                "LightMode" = "MotionVectors"
            }
            ColorMask RG

            HLSLPROGRAM
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY

            #include_with_pragmas "HLSL/PBRLightingMotionVector.hlsl"
            ENDHLSL
        }
    }


    SubShader
    {

        Pass
        {
            Name "VisibilityDXR"
            Tags
            {
                "LightMode" = "VisibilityDXR"
            }

            HLSLPROGRAM
            // -------------------------------------
            // Shader Stages
            #pragma only_renderers d3d11 xboxseries ps5
            #pragma raytracing surface_shader


            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile  _  _GBUFFER_NORMALS_OCT


            
            #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/RayTracing/Shaders/ShaderVariablesRaytracing.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/RayTracing/Shaders/RaytracingIntersection.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/RayTracing/Shaders/RaytracingFragInputs.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/RayTracing/Shaders/RayTracingCommon.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/RayTracing/Shaders/RayTracingShaderPassVisibility.hlsl"
            ENDHLSL
        }
    }


    CustomEditor "LWGUI.LWGUI"
}