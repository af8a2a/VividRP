Shader "Hidden/VividRP/BlitCubeTextureFace"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma editor_sync_compilation
            #pragma prefer_hlslcc gles
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

            TEXTURECUBE(_InputTex);
            SAMPLER(sampler_InputTex);
            float4 _InputTex_HDR;
            float _FaceIndex;
            float _LoD;

            struct Attributes
            {
                uint vertexID : VERTEXID_SEMANTIC;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 texcoord : TEXCOORD0;
            };

            static const float3 k_FaceU[6] =
            {
                float3(0.0, 0.0, -1.0),
                float3(0.0, 0.0, 1.0),
                float3(1.0, 0.0, 0.0),
                float3(1.0, 0.0, 0.0),
                float3(1.0, 0.0, 0.0),
                float3(-1.0, 0.0, 0.0)
            };

            static const float3 k_FaceV[6] =
            {
                float3(0.0, -1.0, 0.0),
                float3(0.0, -1.0, 0.0),
                float3(0.0, 0.0, 1.0),
                float3(0.0, 0.0, -1.0),
                float3(0.0, -1.0, 0.0),
                float3(0.0, -1.0, 0.0)
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);

                float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);
                uv = uv * 2.0 - 1.0;

                int faceIndex = (int)_FaceIndex;
                float3 transformU = k_FaceU[faceIndex];
                float3 transformV = k_FaceV[faceIndex];
                float3 normal = cross(transformV, transformU);
                output.texcoord = normal + uv.x * transformU + uv.y * transformV;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 color = SAMPLE_TEXTURECUBE_LOD(_InputTex, sampler_InputTex, input.texcoord, _LoD);
                color.rgb = DecodeHDREnvironment(color, _InputTex_HDR);
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
