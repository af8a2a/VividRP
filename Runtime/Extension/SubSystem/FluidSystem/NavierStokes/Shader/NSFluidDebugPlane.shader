Shader "Vivid/Debug/NSFluidDebugPlane"
{
    Properties
    {
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
        }

        Pass
        {

            Tags
            {
                "LightMode" = "UniversalForwardOnly"
            }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            TEXTURE2D(_FluidTex);
            float4x4 _WorldToFluid;

            v2f vert(appdata v)
            {
                v2f o;
                float3 positionWS = TransformObjectToWorld(v.vertex.xyz);
                o.pos = TransformWorldToHClip(positionWS);
                o.uv = mul(_WorldToFluid, float4(positionWS, 1)).xz;
                o.positionWS = positionWS;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 debug = SAMPLE_TEXTURE2D(_FluidTex, sampler_LinearClamp, i.uv);


                return debug;
            }
            ENDHLSL
        }
    }
}