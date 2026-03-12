Shader "Hidden/VividRP PostProcessing/Editor/CurveBackground"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Hue"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragHue

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            float _DisabledState;

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
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 FragHue(Varyings input) : SV_Target
            {
                float3 rgb = HsvToRgb(float3(input.uv.x, 1.0, 1.0));
                return float4(rgb * _DisabledState, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Grayscale"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragGrayscale

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            float _DisabledState;

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
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 FragGrayscale(Varyings input) : SV_Target
            {
                float value = input.uv.x * _DisabledState;
                return float4(value, value, value, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
