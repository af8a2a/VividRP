Shader "Hidden/VividRP/Blit"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "Blit"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            float4 _BlitScaleBias;
            float2 DynamicScalingApplyScaleBias(float2 xy, float4 dynamicScalingScaleBias)
            {
                return dynamicScalingScaleBias.zw + xy * dynamicScalingScaleBias.xy;
            }

            float2 DynamicScalingRemoveScaleBias(float2 xy, float4 dynamicScalingScaleBias)
            {
                return (xy - dynamicScalingScaleBias.zw) / dynamicScalingScaleBias.xy;
            }

            #define DYNAMIC_SCALING_APPLY_SCALEBIAS(uv)  DynamicScalingApplyScaleBias(uv, _BlitScaleBias)
            #define DYNAMIC_SCALING_REMOVE_SCALEBIAS(uv) DynamicScalingRemoveScaleBias(uv, _BlitScaleBias)

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 uv  = GetFullScreenTriangleTexCoord(input.vertexID);

                output.positionCS = pos;
                output.uv   = DYNAMIC_SCALING_APPLY_SCALEBIAS(uv);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);
            }
            ENDHLSL
        }
    }
}
