Shader "VolumetricFog/TextureFog"
{
    Properties
    {
        // [MainColor] _Albedo ("Albdedo", Color) = (1, 1, 1, 1)
        [PerRendererData]_FogTex ("Fog Texture", 3D) = "white" {}
        [PerRendererData]_FogTexTiling ("Fog Texture Tiling", Vector) = (1, 1, 1, 1)
        [PerRendererData]_FogTexScroll ("Fog Texture Scroll", Vector) = (0, 0, 0, 1)
    }
    SubShader
    {
        Pass
        {
            Name "VolumetricFogVoxelize"
            Tags
            {
                "LightMode"="VolumetricFogVoxelize"
            }
            ZTest Always
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #include "ShaderPassVoxelize.hlsl"
            #pragma enable_d3d11_debug_symbols

            #pragma vertex Vert
            #pragma fragment Frag

            sampler3D _FogTex;
            float3 _FogTexTiling;
            float3 _FogTexScroll;

            void VolumetricFogVoxelize(VoxelizeInput input, inout float3 albedo, inout float extinction)
            {
                float4 fog = tex3D(_FogTex, input.positionNVCS.xyz * _FogTexTiling + _FogTexScroll * _Time.x);

                extinction *= fog.a;
                albedo *= fog.rgb;
            }
            ENDHLSL
        }
    }
}