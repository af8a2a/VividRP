Shader "Hidden/VividRP/SliderDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "SliderDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

            TEXTURE2D(_LeftTexture);
            SAMPLER(sampler_LeftTexture);
            TEXTURE2D(_RightTexture);
            SAMPLER(sampler_RightTexture);

            float4 _LeftTextureScaleBias;
            float4 _RightTextureScaleBias;
            float _Split;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float2 ApplyScaleBias(float2 uv, float4 scaleBias)
            {
                return uv * scaleBias.xy + scaleBias.zw;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float split = saturate(_Split);
                float2 leftUv = ApplyScaleBias(input.uv, _LeftTextureScaleBias);
                float2 rightUv = ApplyScaleBias(input.uv, _RightTextureScaleBias);
                float4 leftColor = SAMPLE_TEXTURE2D(_LeftTexture, sampler_LeftTexture, leftUv);
                float4 rightColor = SAMPLE_TEXTURE2D(_RightTexture, sampler_RightTexture, rightUv);
                float leftWeight = step(input.uv.x, split);
                return lerp(rightColor, leftColor, leftWeight);
            }
            ENDHLSL
        }
    }
}
