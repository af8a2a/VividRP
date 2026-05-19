Shader "Hidden/VividRP/Sky/GGXConvolve"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Cull Off
            ZTest Always
            ZWrite Off
            Blend Off

            HLSLPROGRAM
            #pragma editor_sync_compilation
            #pragma target 4.5

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"

            SAMPLER(s_trilinear_clamp_sampler);

            TEXTURECUBE(_MainTex);
            TEXTURE2D(_GgxIblSamples);

            float _Level;
            float _InvOmegaP;
            float4x4 _PixelCoordToViewDirWS;

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

            float4 Frag(Varyings input) : SV_Target
            {
                float3 viewDirWS = normalize(mul(float3(input.positionCS.xy, 1.0), (float3x3)_PixelCoordToViewDirWS));
                float3 N = -viewDirWS;
                float3 V = N;

                float perceptualRoughness = MipmapLevelToPerceptualRoughness(_Level);
                float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
                uint sampleCount = GetIBLRuntimeFilterSampleCount((uint)_Level);

                return IntegrateLD(
                    TEXTURECUBE_ARGS(_MainTex, s_trilinear_clamp_sampler),
                    _GgxIblSamples,
                    V,
                    N,
                    roughness,
                    _Level - 1.0,
                    _InvOmegaP,
                    sampleCount,
                    true,
                    true);
            }
            ENDHLSL
        }

        Pass
        {
            Cull Off
            ZTest Always
            ZWrite Off
            Blend Off

            HLSLPROGRAM
            #pragma editor_sync_compilation
            #pragma target 4.5

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"

            SAMPLER(s_trilinear_clamp_sampler);

            TEXTURECUBE(_MainTex);
            TEXTURE2D(_GgxIblSamples);

            float _Level;
            float _InvOmegaP;
            float4x4 _PixelCoordToViewDirWS;

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

            float4 Frag(Varyings input) : SV_Target
            {
                float3 viewDirWS = normalize(mul(float3(input.positionCS.xy, 1.0), (float3x3)_PixelCoordToViewDirWS));
                float3 directionWS = -viewDirWS;
                return SAMPLE_TEXTURECUBE_LOD(_MainTex, s_trilinear_clamp_sampler, directionWS, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
