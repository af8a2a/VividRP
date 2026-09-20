Shader "Hidden/VividRP/FinalBlit"
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
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ HDR_ENCODING

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #if defined(HDR_ENCODING)
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ACES.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/HDROutput.hlsl"
            #endif

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            float4 _BlitScaleBias;
            float4 _HDROutputParams;

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
                float2 screenUV   : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);

                output.positionCS = pos;
                output.uv = DYNAMIC_SCALING_APPLY_SCALEBIAS(uv);
                output.screenUV = uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);
                #if defined(HDR_ENCODING)
                color.rgb = OETF(color.rgb, _HDROutputParams.y);
                #endif
                return color;
            }
            ENDHLSL
        }
    }
}
