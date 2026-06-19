Shader "Hidden/VividRP/PostProcessing/Diffusion"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        HLSLINCLUDE
        #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        TEXTURE2D(_BlitTexture);
        SAMPLER(sampler_BlitTexture);
        TEXTURE2D(_BlurTexture);
        SAMPLER(sampler_BlurTexture);
        float4 _BlitScaleBias;
        float4 _BlitTexture_TexelSize;
        float _Intensity;
        float _Filter;
        float _Multiply;
        float _BlurScale;
        float _BlurIntensity;


        #define DYNAMIC_SCALING_APPLY_SCALEBIAS(uv) DynamicScalingApplyScaleBias(uv, _BlitScaleBias)

        struct Attributes
        {
            uint vertexID : SV_VertexID;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
            output.uv = DYNAMIC_SCALING_APPLY_SCALEBIAS(GetFullScreenTriangleTexCoord(input.vertexID));
            return output;
        }

        float4 FragCopy(Varyings input) : SV_Target
        {
            return SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);
        }

        float4 FragBlurHorizontal(Varyings input) : SV_Target
        {
            float texelSize = _BlitTexture_TexelSize.x * _BlurScale;
            float2 uv = input.uv;

            float4 c0 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv - float2(texelSize * 3.23076923, 0));
            float4 c1 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv - float2(texelSize * 1.38461538, 0));
            float4 c2 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv);
            float4 c3 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv + float2(texelSize * 1.38461538, 0));
            float4 c4 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv + float2(texelSize * 3.23076923, 0));

            return c0 * 0.07027027
                + c1 * 0.31621622
                + c2 * 0.22702703
                + c3 * 0.31621622
                + c4 * 0.07027027;
        }

        float4 FragBlurVertical(Varyings input) : SV_Target
        {
            float texelSize = _BlitTexture_TexelSize.y * _BlurScale;
            float2 uv = input.uv;

            float4 c0 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv - float2(0, texelSize * 3.23076923));
            float4 c1 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv - float2(0, texelSize * 1.38461538));
            float4 c2 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv);
            float4 c3 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv + float2(0, texelSize * 1.38461538));
            float4 c4 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv + float2(0, texelSize * 3.23076923));

            return c0 * 0.07027027
                + c1 * 0.31621622
                + c2 * 0.22702703
                + c3 * 0.31621622
                + c4 * 0.07027027;
        }

        float4 FragMax(Varyings input) : SV_Target
        {
            float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);
            color.rgb *= _Intensity * _BlurIntensity;
            return color;
        }

        float4 FragMultiply(Varyings input) : SV_Target
        {
            float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);
            color.rgb = pow(max(color.rgb, 0.0), _Multiply + 1.0);
            return color;
        }

        float4 FragFilter(Varyings input) : SV_Target
        {
            float4 baseSample = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);
            float3 color = FastTonemap(baseSample.rgb);
            float3 blur = FastTonemap(SAMPLE_TEXTURE2D(_BlurTexture, sampler_BlurTexture, input.uv).rgb);

            color = 1.0 - (1.0 - color) * (1.0 - blur * (_Filter * _BlurIntensity));
            return float4(FastTonemapInvert(color), baseSample.a);
        }
        ENDHLSL

        Pass
        {
            Name "BlurH"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragBlurHorizontal
            ENDHLSL
        }

        Pass
        {
            Name "BlurV"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragBlurVertical
            ENDHLSL
        }

        Pass
        {
            Name "Max"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One
            BlendOp Max

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragMax
            ENDHLSL
        }

        Pass
        {
            Name "Multiply"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragMultiply
            ENDHLSL
        }

        Pass
        {
            Name "Filter"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragFilter
            ENDHLSL
        }

        Pass
        {
            Name "Copy"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragCopy
            ENDHLSL
        }
    }
}
