Shader "Hidden/VividRP/StopNaN"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "StopNaN"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"

            FRAMEBUFFER_INPUT_X_FLOAT(0);

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                float4 color = LOAD_FRAMEBUFFER_INPUT_X(0, input.positionCS.xy);

                if (AnyIsNaN(color) || AnyIsInf(color))
                    color = 0.0;

                return color;
            }
            ENDHLSL
        }
    }
}
