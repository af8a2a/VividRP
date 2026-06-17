Shader "Hidden/VividRP/TerrainLit_BasemapGen"
{
    Properties
    {
        [HideInInspector] _DstBlend("DstBlend", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
            "SplatCount" = "8"
        }

        Pass
        {
            Tags
            {
                "Name" = "_MainTex"
                "Format" = "ARGB32"
                "Size" = "1"
            }

            ZTest Always
            ZWrite Off
            Cull Off
            Blend One [_DstBlend]

            HLSLPROGRAM
                #pragma target 4.5
                #pragma use_dxc
                #pragma shader_feature_local _TERRAIN_8_LAYERS
                #pragma shader_feature_local _NORMALMAP
                #pragma shader_feature_local _MASKMAP
                #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
                #pragma vertex Vert
                #pragma fragment FragMainTex

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/TerrainLit/TerrainLitBasemapGenPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Tags
            {
                "Name" = "_MetallicTex"
                "Format" = "RG16"
                "Size" = "1/4"
            }

            ZTest Always
            ZWrite Off
            Cull Off
            Blend One [_DstBlend]

            HLSLPROGRAM
                #pragma target 4.5
                #pragma use_dxc
                #pragma shader_feature_local _TERRAIN_8_LAYERS
                #pragma shader_feature_local _NORMALMAP
                #pragma shader_feature_local _MASKMAP
                #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
                #pragma vertex Vert
                #pragma fragment FragMetallicTex

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/TerrainLit/TerrainLitBasemapGenPass.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
