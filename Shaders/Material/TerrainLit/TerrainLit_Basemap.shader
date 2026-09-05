Shader "Hidden/VividRP/TerrainLit_Basemap"
{
    Properties
    {
        _MainTex("Albedo", 2D) = "white" {}
        _MetallicTex("Metallic (R), AO (G)", 2D) = "white" {}
        [HideInInspector] _TerrainHolesTexture("Holes Map", 2D) = "white" {}
        [HideInInspector] _EmissionColor("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Color("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _AlphaClip("Alpha Clipping", Float) = 0.0
        [HideInInspector] _Cull("__cull", Float) = 2.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _ReceivesSSR("Receive SSR", Float) = 1.0
        [HideInInspector] _SupportDecals("Receive Decals", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "TerrainCompatible" = "True"
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
                #pragma target 4.5
                #pragma use_dxc
                #pragma multi_compile_instancing
                #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap renderinglayer
                #pragma shader_feature_local _ALPHATEST_ON
                #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
                #pragma vertex Vert
                #pragma fragment FragPreDepth

                #define VIVID_TERRAIN_BASEMAP 1
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/TerrainLit/TerrainLitPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" "VividVSMCaster" = "2" }

            ZWrite On
            ZTest LEqual
            Cull [_Cull]
            ColorMask 0

            HLSLPROGRAM
                #pragma target 4.5
                #pragma use_dxc
                #pragma multi_compile_instancing
                #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap renderinglayer
                #pragma multi_compile_fragment _ VIVID_VSM_CASTER
                #pragma require randomwrite : VIVID_VSM_CASTER
                #pragma shader_feature_local _ALPHATEST_ON
                #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
                #pragma vertex Vert
                #pragma fragment FragShadow

                #define VIVIDRP_SHADERPASS_SHADOW_CASTER 1
                #define VIVID_TERRAIN_BASEMAP 1
                #define VIVID_TERRAIN_PASS_SHADOW 1
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/TerrainLit/TerrainLitPass.hlsl"
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
                #pragma use_dxc
                #pragma multi_compile_instancing
                #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap renderinglayer
                #pragma multi_compile _ LIGHTMAP_ON
                #pragma multi_compile _ DIRLIGHTMAP_COMBINED
                #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
                #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
                #pragma shader_feature_local _ALPHATEST_ON
                #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
                #pragma vertex Vert
                #pragma fragment FragGBuffer

                #define VIVID_TERRAIN_BASEMAP 1
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/TerrainLit/TerrainLitPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "VividGBufferGPUDrivenDecal"
            Tags { "LightMode" = "VividGBufferGPUDrivenDecal" }

            Blend One Zero
            ZWrite [_ZWrite]
            ZTest Equal
            Cull [_Cull]

            HLSLPROGRAM
                #pragma target 4.5
                #pragma use_dxc
                #pragma multi_compile_instancing
                #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap renderinglayer
                #pragma multi_compile _ LIGHTMAP_ON
                #pragma multi_compile _ DIRLIGHTMAP_COMBINED
                #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
                #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
                #pragma shader_feature_local _ALPHATEST_ON
                #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
                #pragma vertex Vert
                #pragma fragment FragGBuffer

                #define VIVID_TERRAIN_BASEMAP 1
                #define VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER 1
                #include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/Bindless.hlsl"
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/TerrainLit/TerrainLitPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap renderinglayer
                #pragma shader_feature EDITOR_VISUALIZATION
                #pragma shader_feature_local _ALPHATEST_ON
                #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
                #pragma vertex Vert
                #pragma fragment FragMeta

                #define VIVID_TERRAIN_BASEMAP 1
                #define VIVID_TERRAIN_PASS_META 1
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/TerrainLit/TerrainLitPass.hlsl"
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
                #pragma use_dxc
                #pragma multi_compile_instancing
                #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap renderinglayer
                #pragma shader_feature_local _ALPHATEST_ON
                #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
                #pragma vertex Vert
                #pragma fragment FragMotionVectors

                #define VIVID_TERRAIN_BASEMAP 1
                #include "Packages/com.vivid.render-pipelines/Shaders/Material/TerrainLit/TerrainLitPass.hlsl"
            ENDHLSL
        }

        UsePass "Hidden/Nature/Terrain/Utilities/PICKING"
    }

    FallBack Off
}
