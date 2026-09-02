Shader "VividRP/Material/StandardLayeredLit"
{
    Properties
    {
        [Main(SurfaceOptions, _, on, off)] _SurfaceOptions("Surface Options", Float) = 1
        [SubEnum(SurfaceOptions, Opaque, 0, Transparent, 1)] _Surface("Surface Type", Float) = 0.0
        [SubToggle(SurfaceOptions, _)] _AlphaClip("Alpha Clipping", Float) = 0.0
        [Sub(SurfaceOptions)] _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [SubEnum(SurfaceOptions, Back, 2, Front, 1, Off, 0)] _Cull("Cull", Float) = 2.0
        [SubToggle(SurfaceOptions, _)] _ReceiveShadows("Receive Shadows", Float) = 1.0
        [SubToggle(SurfaceOptions, _)] _ReceiveSSR("Receive SSR", Float) = 1.0
        [SubToggle(SurfaceOptions, _)] _ReceiveDecals("Receive Decals", Float) = 1.0
        [Sub(SurfaceOptions)] _QueueOffset("Queue Offset", Float) = 0.0

        [Main(SurfaceInputs, _, on, off)] _SurfaceInputs("Surface Inputs", Float) = 1
        [SubEnum(SurfaceInputs, Specular, 0, Metallic, 1)] _WorkflowMode("Workflow Mode", Float) = 1.0
        [AdvancedHeaderProperty] [MainTexture] [Tex(SurfaceInputs, _BaseColor)] _BaseMap("Albedo", 2D) = "white" {}
        [Advanced] [HideInInspector] [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        [Advanced] [TilingOffset(SurfaceInputs)] _BaseMap_ST("UV Tiling and Offset", Vector) = (1, 1, 0, 0)
        [Tex(SurfaceInputs)] _OpacityMap("Opacity Map", 2D) = "white" {}
        [Tex(SurfaceInputs, _Metallic)] _MetallicGlossMap("Metallic Map", 2D) = "white" {}
        [HideInInspector] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        [Sub(SurfaceInputs)] _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        [SubEnum(SurfaceInputs, Metallic Alpha, 0, Albedo Alpha, 1)] _SmoothnessTextureChannel("Smoothness Source", Float) = 0.0
        [Tex(SurfaceInputs)] _RoughnessMap("Roughness Map", 2D) = "white" {}
        [Tex(SurfaceInputs, _BumpScale)] [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _BumpScale("Normal Scale", Float) = 1.0
        [Tex(SurfaceInputs, _OcclusionStrength)] _OcclusionMap("Occlusion Map", 2D) = "white" {}
        [HideInInspector] _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        [Tex(SurfaceInputs, _EmissionColor)] _EmissionMap("Emission Map", 2D) = "black" {}
        [HideInInspector] [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 0)
        [Sub(SurfaceInputs)] _ClearCoatMask("Clear Coat Mask", Range(0.0, 1.0)) = 0.0
        [Sub(SurfaceInputs)] _ClearCoatSmoothness("Clear Coat Smoothness", Range(0.0, 1.0)) = 1.0

        [Main(VirtualTextureInputs, _, on, off)] _VirtualTextureInputs("Virtual Texture", Float) = 1
        [SubToggle(VirtualTextureInputs, _VIRTUAL_TEXTURE_BASE_COLOR)] _UseVirtualTextureBaseColor("Use SVT Base Color", Float) = 1.0

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
            ZWrite On
            ZTest LEqual
            Cull [_Cull]
            ColorMask 0

            HLSLPROGRAM
                #pragma use_dxc
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _OPACITYMAP
                #pragma vertex Vert
                #pragma fragment FragPreDepth

                #define VIVIDRP_SHADERPASS_DEPTH_ONLY 1
                #define VIVIDRP_ATTRIBUTES_NEED_TEXCOORD0 1
                #define VIVIDRP_VARYINGS_NEED_TEXCOORD0 1
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitDepthOnlyPass.hlsl"
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
                #pragma use_dxc
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma multi_compile_fragment _ VIVID_VSM_CASTER
                #pragma require randomwrite : VIVID_VSM_CASTER
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _OPACITYMAP
                #pragma vertex Vert
                #pragma fragment Frag

                #define VIVIDRP_SHADERPASS_SHADOW_CASTER 1
                #define VIVIDRP_ATTRIBUTES_NEED_TEXCOORD0 1
                #define VIVIDRP_VARYINGS_NEED_TEXCOORD0 1
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "VividVTGBuffer"
            Tags { "LightMode" = "VividVTGBuffer" }

            Blend One Zero
            ZWrite Off
            ZTest Equal
            Cull [_Cull]
            ColorMask 0 0
            ColorMask 0 1
            ColorMask 0 2
            ColorMask 0 3
            ColorMask 0 4

            HLSLPROGRAM
                #pragma target 5.0
                #pragma require randomwrite
                #pragma use_dxc
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma multi_compile _ LIGHTMAP_ON
                #pragma multi_compile _ DIRLIGHTMAP_COMBINED
                #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
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
                #pragma shader_feature_local_fragment _VIRTUAL_TEXTURE_BASE_COLOR
                #pragma vertex Vert
                #pragma fragment FragGBuffer

                #define VIVIDRP_SHADERPASS_GBUFFER 1
                #define VIVIDRP_ATTRIBUTES_NEED_NORMAL 1
                #define VIVIDRP_ATTRIBUTES_NEED_TANGENT 1
                #define VIVIDRP_ATTRIBUTES_NEED_TEXCOORD0 1
                #define VIVIDRP_ATTRIBUTES_NEED_TEXCOORD1 1
                #define VIVIDRP_VARYINGS_NEED_POSITION_WS 1
                #define VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD 1
                #define VIVIDRP_VARYINGS_NEED_TEXCOORD0 1
                #define VIVIDRP_VARYINGS_NEED_TEXCOORD1 1
                #define VIVIDRP_STANDARD_LIT_VIRTUAL_TEXTURE 1
                #define VIVID_VT_ENABLE_FEEDBACK_RW 1
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitGBufferPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "VividVTGBufferGPUDrivenDecal"
            Tags { "LightMode" = "VividVTGBufferGPUDrivenDecal" }

            Blend One Zero
            ZWrite Off
            ZTest Equal
            Cull [_Cull]
            ColorMask 0 0
            ColorMask 0 1
            ColorMask 0 2
            ColorMask 0 3
            ColorMask 0 4

            HLSLPROGRAM
                #pragma target 5.0
                #pragma require randomwrite
                #pragma use_dxc
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma multi_compile _ LIGHTMAP_ON
                #pragma multi_compile _ DIRLIGHTMAP_COMBINED
                #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
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
                #pragma shader_feature_local_fragment _VIRTUAL_TEXTURE_BASE_COLOR
                #pragma vertex Vert
                #pragma fragment FragGBuffer

                #define VIVIDRP_SHADERPASS_GBUFFER 1
                #define VIVIDRP_ATTRIBUTES_NEED_NORMAL 1
                #define VIVIDRP_ATTRIBUTES_NEED_TANGENT 1
                #define VIVIDRP_ATTRIBUTES_NEED_TEXCOORD0 1
                #define VIVIDRP_ATTRIBUTES_NEED_TEXCOORD1 1
                #define VIVIDRP_VARYINGS_NEED_POSITION_WS 1
                #define VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD 1
                #define VIVIDRP_VARYINGS_NEED_TEXCOORD0 1
                #define VIVIDRP_VARYINGS_NEED_TEXCOORD1 1
                #define VIVIDRP_STANDARD_LIT_VIRTUAL_TEXTURE 1
                #define VIVID_VT_ENABLE_FEEDBACK_RW 1
                #define VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER 1
                #include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/Bindless.hlsl"
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitGBufferPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma shader_feature EDITOR_VISUALIZATION
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _OPACITYMAP
                #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
                #pragma shader_feature_local_fragment _EMISSION
                #pragma vertex Vert
                #pragma fragment Frag

                #define VIVIDRP_SHADERPASS_META 1
                #define VIVIDRP_ATTRIBUTES_NEED_TEXCOORD0 1
                #define VIVIDRP_ATTRIBUTES_NEED_TEXCOORD1 1
                #define VIVIDRP_ATTRIBUTES_NEED_TEXCOORD2 1
                #define VIVIDRP_VARYINGS_NEED_TEXCOORD0 1
                #define VIVIDRP_VARYINGS_NEED_META_EDITOR_VIS 1
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitMetaPass.hlsl"
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

                #define VIVIDRP_SHADERPASS_DEBUG 1
                #define VIVIDRP_ATTRIBUTES_NEED_NORMAL 1
                #define VIVIDRP_ATTRIBUTES_NEED_TANGENT 1
                #define VIVIDRP_ATTRIBUTES_NEED_TEXCOORD0 1
                #define VIVIDRP_ATTRIBUTES_NEED_TEXCOORD1 1
                #define VIVIDRP_VARYINGS_NEED_POSITION_WS 1
                #define VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD 1
                #define VIVIDRP_VARYINGS_NEED_TEXCOORD0 1
                #define VIVIDRP_VARYINGS_NEED_TEXCOORD1 1
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitGBufferPass.hlsl"
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
            Stencil
            {
                WriteMask 32
                Ref 32
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _OPACITYMAP
                #pragma vertex Vert
                #pragma fragment Frag

                #define VIVIDRP_SHADERPASS_MOTION_VECTORS 1
                #define VIVIDRP_ATTRIBUTES_NEED_TEXCOORD0 1
                #define VIVIDRP_ATTRIBUTES_NEED_PREVIOUS_POSITION 1
                #define VIVIDRP_VARYINGS_NEED_TEXCOORD0 1
                #define VIVIDRP_VARYINGS_NEED_MOTION_POSITIONS 1
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitMotionVectorPass.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "VividRP.Editor.StandardLayeredLitShaderGUI"

    FallBack Off
}
