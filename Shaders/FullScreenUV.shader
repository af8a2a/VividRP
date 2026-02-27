Shader "Hidden/VividRP/FullScreenUV"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "FullScreenUV"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

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
                Varyings o;
                // Full-screen triangle from vertex ID (0,1,2)
                o.uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                o.positionCS = float4(o.uv * 2.0 - 1.0, 0.0, 1.0);
                // Flip Y for Direct3D-like platforms
                o.positionCS.y *= -1.0;
                return o;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return float4(input.uv, 0.0, 1.0);
            }
            ENDHLSL
        }
    }
}
