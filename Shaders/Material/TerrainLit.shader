Shader "VividRP/Terrain/TerrainLit"
{
    Properties
    {
        [ToggleUI] _EnableHeightBlend("Enable Height-based Blend", Float) = 0.0
        _HeightTransition("Height Transition", Range(0.0, 1.0)) = 0.0
        [ToggleUI] _EnableInstancedPerPixelNormal("Enable Per-pixel Normal", Float) = 1.0
        [ToggleUI] _ReceivesSSR("Receive SSR", Float) = 1.0
        [ToggleUI] _SupportDecals("Receive Decals", Float) = 1.0

        [HideInInspector] _TerrainHolesTexture("Holes Map", 2D) = "white" {}
        [HideInInspector] _EmissionColor("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _MainTex("Albedo", 2D) = "white" {}
        [HideInInspector] _Color("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _AlphaClip("Alpha Clipping", Float) = 0.0
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Cull("__cull", Float) = 2.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "SplatCount" = "8"
            "MaskMapR" = "Metallic"
            "MaskMapG" = "AO"
            "MaskMapB" = "Height"
            "MaskMapA" = "Smoothness"
            "DiffuseA" = "Smoothness (becomes Density when Mask map is assigned)"
            "DiffuseA_MaskMapUsed" = "Density"
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

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/TerrainLitPass.hlsl"
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
                #pragma use_dxc
                #pragma multi_compile_instancing
                #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap renderinglayer
                #pragma shader_feature_local _ALPHATEST_ON
                #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
                #pragma vertex Vert
                #pragma fragment FragShadow

                #define VIVID_TERRAIN_PASS_SHADOW 1
                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/TerrainLitPass.hlsl"
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
                #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
                #pragma shader_feature_local _ALPHATEST_ON
                #pragma shader_feature_local _TERRAIN_8_LAYERS
                #pragma shader_feature_local _NORMALMAP
                #pragma shader_feature_local _MASKMAP
                #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
                #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
                #pragma vertex Vert
                #pragma fragment FragGBuffer

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/TerrainLitPass.hlsl"
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
                #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
                #pragma shader_feature_local _ALPHATEST_ON
                #pragma shader_feature_local _TERRAIN_8_LAYERS
                #pragma shader_feature_local _NORMALMAP
                #pragma shader_feature_local _MASKMAP
                #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
                #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
                #pragma vertex Vert
                #pragma fragment FragGBuffer

                #define VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER 1
                #include_with_pragmas "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/Bindless.hlsl"
                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/TerrainLitPass.hlsl"
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
                #pragma shader_feature_local _TERRAIN_8_LAYERS
                #pragma shader_feature_local _NORMALMAP
                #pragma shader_feature_local _MASKMAP
                #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
                #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
                #pragma vertex Vert
                #pragma fragment FragMeta

                #define VIVID_TERRAIN_PASS_META 1
                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/TerrainLitPass.hlsl"
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
                #pragma use_dxc
                #pragma multi_compile_instancing
                #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap renderinglayer
                #pragma shader_feature_local _ALPHATEST_ON
                #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
                #pragma vertex Vert
                #pragma fragment FragMotionVectors

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/TerrainLitPass.hlsl"
            ENDHLSL
        }

        UsePass "Hidden/Nature/Terrain/Utilities/PICKING"
    }

    Dependency "BaseMapShader" = "Hidden/VividRP/TerrainLit_Basemap"
    Dependency "BaseMapGenShader" = "Hidden/VividRP/TerrainLit_BasemapGen"
    CustomEditor "VividRP.Editor.TerrainLitShaderGUI"
    FallBack Off
}
