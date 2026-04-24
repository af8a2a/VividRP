Shader "VividRP/Material/StandardLit"
{
    Properties
    {
        [Main(SurfaceOptions, _, on, off)] _SurfaceOptions("Surface Options", Float) = 1
        [SubEnum(SurfaceOptions, Opaque, 0, Transparent, 1)] _Surface("Surface Type", Float) = 0.0
        [SubToggle(SurfaceOptions, _)] _AlphaClip("Alpha Clipping", Float) = 0.0
        [Sub(SurfaceOptions)] _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [SubEnum(SurfaceOptions, Back, 2, Front, 1, Off, 0)] _Cull("Cull", Float) = 2.0
        [SubToggle(SurfaceOptions, _)] _ReceiveShadows("Receive Shadows", Float) = 1.0
        [Sub(SurfaceOptions)] _QueueOffset("Queue Offset", Float) = 0.0

        [Main(SurfaceInputs, _, on, off)] _SurfaceInputs("Surface Inputs", Float) = 1
        [SubEnum(SurfaceInputs, Specular, 0, Metallic, 1)] _WorkflowMode("Workflow Mode", Float) = 1.0
        [MainTexture] [Tex(SurfaceInputs, _BaseColor)] _BaseMap("Albedo", 2D) = "white" {}
        [HideInInspector] [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        [Sub(SurfaceInputs)] _OpacityMap("Opacity Map", 2D) = "white" {}
        [Sub(SurfaceInputs)] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        [Sub(SurfaceInputs)] _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        [SubEnum(SurfaceInputs, Metallic Alpha, 0, Albedo Alpha, 1)] _SmoothnessTextureChannel("Smoothness Source", Float) = 0.0
        [Sub(SurfaceInputs)] _MetallicGlossMap("Metallic Map", 2D) = "white" {}
        [Sub(SurfaceInputs)] _RoughnessMap("Roughness Map", 2D) = "white" {}
        [Sub(SurfaceInputs)] _BumpScale("Normal Scale", Float) = 1.0
        [Sub(SurfaceInputs)] [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        [Sub(SurfaceInputs)] _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        [Sub(SurfaceInputs)] _OcclusionMap("Occlusion Map", 2D) = "white" {}
        [Sub(SurfaceInputs)] [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 0)
        [Sub(SurfaceInputs)] _EmissionMap("Emission Map", 2D) = "black" {}
        [Sub(SurfaceInputs)] _ClearCoatMask("Clear Coat Mask", Range(0.0, 1.0)) = 0.0
        [Sub(SurfaceInputs)] _ClearCoatSmoothness("Clear Coat Smoothness", Range(0.0, 1.0)) = 1.0

        [HideInInspector] _Blend("__blend", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0

        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
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
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_Cull]
//            ColorMask 0

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma multi_compile _ LIGHTMAP_ON
                #pragma multi_compile _ DIRLIGHTMAP_COMBINED
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _OPACITYMAP
                #pragma shader_feature_local_fragment _NORMALMAP
                #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
                #pragma shader_feature_local_fragment _ROUGHNESSMAP
                #pragma shader_feature_local_fragment _OCCLUSIONMAP
                #pragma shader_feature_local_fragment _EMISSION
                #pragma shader_feature_local_fragment _CLEARCOAT
                #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
                #pragma vertex Vert
                #pragma fragment FragPreDepth

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/StandardLitGBufferPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull [_Cull]
            ColorMask 0

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _OPACITYMAP
                #pragma vertex Vert
                #pragma fragment Frag
                // #pragma enable_d3d11_debug_symbols

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/StandardLitShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "VividGBuffer"
            Tags { "LightMode" = "VividGBuffer" }

            Blend One Zero
            ZWrite [_ZWrite]
            ZTest Equal
            Cull [_Cull]

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma multi_compile _ LIGHTMAP_ON
                #pragma multi_compile _ DIRLIGHTMAP_COMBINED
                #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _OPACITYMAP
                #pragma shader_feature_local_fragment _NORMALMAP
                #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
                #pragma shader_feature_local_fragment _ROUGHNESSMAP
                #pragma shader_feature_local_fragment _OCCLUSIONMAP
                #pragma shader_feature_local_fragment _EMISSION
                #pragma shader_feature_local_fragment _CLEARCOAT
                #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
                #pragma vertex Vert
                #pragma fragment FragGBuffer

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/StandardLitGBufferPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "SRPDefaultUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend One Zero
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma multi_compile _ LIGHTMAP_ON
                #pragma multi_compile _ DIRLIGHTMAP_COMBINED
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _OPACITYMAP
                #pragma shader_feature_local_fragment _NORMALMAP
                #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
                #pragma shader_feature_local_fragment _ROUGHNESSMAP
                #pragma shader_feature_local_fragment _OCCLUSIONMAP
                #pragma shader_feature_local_fragment _EMISSION
                #pragma shader_feature_local_fragment _CLEARCOAT
                #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
                #pragma vertex Vert
                #pragma fragment FragDebug

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/StandardLitGBufferPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            ColorMask RG
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _OPACITYMAP
                #pragma vertex Vert
                #pragma fragment Frag

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/StandardLitMotionVectorPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "IndirectDXR"
            Tags { "LightMode" = "IndirectDXR" }

            HLSLPROGRAM
                #pragma only_renderers d3d11 xboxseries ps5 switch2
                #pragma raytracing surface_shader
                #pragma multi_compile _ INSTANCING_ON
                #pragma multi_compile _ LIGHTMAP_ON
                #pragma multi_compile _ DIRLIGHTMAP_COMBINED
                #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
                #pragma shader_feature_local_raytracing _ALPHATEST_ON
                #pragma shader_feature_local_raytracing _OPACITYMAP
                #pragma shader_feature_local_raytracing _NORMALMAP
                #pragma shader_feature_local_raytracing _METALLICSPECGLOSSMAP
                #pragma shader_feature_local_raytracing _ROUGHNESSMAP
                #pragma shader_feature_local_raytracing _OCCLUSIONMAP
                #pragma shader_feature_local_raytracing _EMISSION
                #pragma shader_feature_local_raytracing _CLEARCOAT
                #pragma shader_feature_local_raytracing _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

                #define VIVIDRP_INDIRECT_DIFFUSE_CLOSEST_HIT_NAME StandardLitIndirectDiffuseClosestHit
                #define VIVIDRP_INDIRECT_DIFFUSE_ANY_HIT_NAME StandardLitIndirectDiffuseAnyHit
                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/IndirectDiffuse.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "VividRP.Editor.StandardLitShaderGUI"

    FallBack Off
}
