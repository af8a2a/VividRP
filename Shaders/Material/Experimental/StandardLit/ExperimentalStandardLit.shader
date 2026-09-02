Shader "VividRP/Experimental/Material/StandardLit"
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
        [Tex(SurfaceInputs)] _RMOMap("RMO Map (R: Roughness, G: Metallic, B: AO)", 2D) = "white" {}
        [Sub(SurfaceInputs)] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        [Sub(SurfaceInputs)] _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        [SubEnum(SurfaceInputs, Metallic Alpha, 0, Albedo Alpha, 1)] _SmoothnessTextureChannel("Smoothness Source", Float) = 0.0
        [MinMaxSlider(SurfaceInputs, _MetallicRemapMin, _MetallicRemapMax)] _MetallicRemap("Metallic Remap", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _MetallicRemapMin("Metallic Remap Min", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _MetallicRemapMax("Metallic Remap Max", Range(0.0, 1.0)) = 1.0
        [MinMaxSlider(SurfaceInputs, _SmoothnessRemapMin, _SmoothnessRemapMax)] _SmoothnessRemap("Smoothness Remap", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _SmoothnessRemapMin("Smoothness Remap Min", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _SmoothnessRemapMax("Smoothness Remap Max", Range(0.0, 1.0)) = 1.0
        [Tex(SurfaceInputs, _BumpScale)] [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _BumpScale("Normal Scale", Float) = 1.0
        [HideInInspector] _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        [MinMaxSlider(SurfaceInputs, _AORemapMin, _AORemapMax)] _AORemap("AO Remap", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _AORemapMin("AO Remap Min", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _AORemapMax("AO Remap Max", Range(0.0, 1.0)) = 1.0
        [Tex(SurfaceInputs, _EmissionColor)] _EmissionMap("Emission Map", 2D) = "black" {}
        [HideInInspector] [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 0)
        [Sub(SurfaceInputs)] _ClearCoatMask("Clear Coat Mask", Range(0.0, 1.0)) = 0.0
        [Sub(SurfaceInputs)] _ClearCoatSmoothness("Clear Coat Smoothness", Range(0.0, 1.0)) = 1.0

        [Main(ExperimentalClosureInputs, _, on, off)] _ExperimentalClosureInputs("Experimental Closure Inputs", Float) = 1
        [Sub(ExperimentalClosureInputs)] _SpecularIOR("Specular IOR", Range(1.0, 3.0)) = 1.5
        [Sub(ExperimentalClosureInputs)] _TransmissionWeight("Transmission Weight", Range(0.0, 1.0)) = 0.0
        [Sub(ExperimentalClosureInputs)] _SubsurfaceWeight("Subsurface Weight", Range(0.0, 1.0)) = 0.0

        // Detailed fields are retained for the shared StandardLit sampling code.
        [HideInInspector] _ThinWalledTransmission("Thin-Walled Transmission", Float) = 0.0
        [HideInInspector] _TransmissionMap("Transmission Map", 2D) = "white" {}
        [HideInInspector] _TransmissionColor("Transmission Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _TransmissionDepth("Transmission Depth", Float) = 0.0
        [HideInInspector] _TransmissionScatter("Transmission Scatter", Color) = (0, 0, 0, 0)
        [HideInInspector] _TransmissionScatterAnisotropy("Transmission Scatter Anisotropy", Range(-0.95, 0.95)) = 0.0
        [HideInInspector] _SubsurfaceColor("Subsurface Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _SubsurfaceRadius("Subsurface Radius", Float) = 0.01
        [HideInInspector] _SubsurfaceRadiusScale("Subsurface Radius Scale", Color) = (1, 0.5, 0.25, 1)
        [HideInInspector] _SubsurfaceScatterAnisotropy("Subsurface Scatter Anisotropy", Range(-0.95, 0.95)) = 0.0
        [HideInInspector] _SubsurfaceTransmissionWeight("Subsurface Transmission Weight", Range(0.0, 1.0)) = 0.0

        [HideInInspector] _VividExperimentalClosureVersion("Experimental Closure Version", Float) = 2.0
        [HideInInspector] _Blend("__blend", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _MetallicGlossMap("Legacy Metallic Map", 2D) = "white" {}
        [HideInInspector] _RoughnessMap("Legacy Roughness Map", 2D) = "white" {}
        [HideInInspector] _OcclusionMap("Legacy Occlusion Map", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "VividRenderPipeline"
            "VividMaterialSystem" = "ExperimentalClosure"
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
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma shader_feature EDITOR_VISUALIZATION
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _OPACITYMAP
                #pragma shader_feature_local_fragment _RMOMAP
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
                #pragma use_dxc
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

        Pass
        {
            Name "IndirectDXR"
            Tags { "LightMode" = "IndirectDXR" }

            HLSLPROGRAM
                #pragma only_renderers d3d11 xboxseries ps5 switch2
                #pragma raytracing surface_shader
                #pragma multi_compile _ INSTANCING_ON
                #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
                #pragma shader_feature_local_raytracing _ALPHATEST_ON
                #pragma shader_feature_local_raytracing _OPACITYMAP
                #pragma shader_feature_local_raytracing _NORMALMAP
                #pragma shader_feature_local_raytracing _RMOMAP
                #pragma shader_feature_local_raytracing _METALLICSPECGLOSSMAP
                #pragma shader_feature_local_raytracing _ROUGHNESSMAP
                #pragma shader_feature_local_raytracing _OCCLUSIONMAP
                #pragma shader_feature_local_raytracing _EMISSION
                #pragma shader_feature_local_raytracing _CLEARCOAT
                #pragma shader_feature_local_raytracing _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

                #define VIVIDRP_INDIRECT_DIFFUSE_CLOSEST_HIT_NAME ExperimentalStandardLitIndirectDiffuseClosestHit
                #define VIVIDRP_INDIRECT_DIFFUSE_ANY_HIT_NAME ExperimentalStandardLitIndirectDiffuseAnyHit
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/StandardLit/ExperimentalIndirectDiffuse.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ReferencedPathtracingDXR"
            Tags { "LightMode" = "ReferencedPathtracingDXR" }

            HLSLPROGRAM
                #pragma only_renderers d3d11 xboxseries ps5 switch2
                #pragma raytracing surface_shader
                #pragma multi_compile _ INSTANCING_ON
                #pragma shader_feature_local_raytracing _ALPHATEST_ON
                #pragma shader_feature_local_raytracing _OPACITYMAP
                #pragma shader_feature_local_raytracing _TRANSMISSIONMAP
                #pragma shader_feature_local_raytracing _SURFACE_TYPE_TRANSPARENT
                #pragma shader_feature_local_raytracing _NORMALMAP
                #pragma shader_feature_local_raytracing _RMOMAP
                #pragma shader_feature_local_raytracing _METALLICSPECGLOSSMAP
                #pragma shader_feature_local_raytracing _ROUGHNESSMAP
                #pragma shader_feature_local_raytracing _EMISSION
                #pragma shader_feature_local_raytracing _CLEARCOAT
                #pragma shader_feature_local_raytracing _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

                #include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/StandardLit/ExperimentalReferencedPathtracingPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "RaytracingGBufferDXR"
            Tags { "LightMode" = "RaytracingGBufferDXR" }

            HLSLPROGRAM
                #pragma only_renderers d3d11 xboxseries ps5 switch2
                #pragma raytracing surface_shader
                #pragma multi_compile _ INSTANCING_ON
                #pragma shader_feature_local_raytracing _ALPHATEST_ON
                #pragma shader_feature_local_raytracing _OPACITYMAP
                #pragma shader_feature_local_raytracing _TRANSMISSIONMAP
                #pragma shader_feature_local_raytracing _NORMALMAP
                #pragma shader_feature_local_raytracing _RMOMAP
                #pragma shader_feature_local_raytracing _METALLICSPECGLOSSMAP
                #pragma shader_feature_local_raytracing _ROUGHNESSMAP
                #pragma shader_feature_local_raytracing _EMISSION
                #pragma shader_feature_local_raytracing _CLEARCOAT
                #pragma shader_feature_local_raytracing _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

                #include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/StandardLit/ExperimentalRaytracingGBufferPass.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "VividRP.Editor.ExperimentalStandardLitShaderGUI"

    FallBack Off
}
