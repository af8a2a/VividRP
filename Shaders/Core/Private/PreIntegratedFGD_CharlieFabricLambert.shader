Shader "Hidden/VividRP/PreIntegratedFGD_CharlieFabricLambert"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            ZTest Always
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #define PREFER_HALF 0

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"

            float4 IntegrateCharlieAndFabricLambertFGD(float3 V, float3 N, float roughness, uint sampleCount = 4096)
            {
                float NdotV = ClampNdotV(dot(N, V));
                float4 acc = float4(0.0, 0.0, 0.0, 0.0);
                float3x3 localToWorld = GetLocalFrame(N);
                float rcpSampleCount = rcp(sampleCount);

                for (uint i = 0; i < sampleCount; ++i)
                {
                    float3 localL = SampleConeStrata(i, rcpSampleCount, 0.0);
                    float NdotL = localL.z;
                    float3 L = mul(localL, localToWorld);

                    float3 H = normalize(V + L);
                    float NdotH = dot(N, H);
                    float weight = D_Charlie(NdotH, roughness) * V_Charlie(NdotL, NdotV, roughness) * NdotL;
                    float VdotH = dot(V, H);
                    acc.x += weight * pow(1.0 - VdotH, 5.0);
                    acc.y += weight;

                    float weightOverPdf;
                    float2 u = Hammersley2d(i, sampleCount);
                    ImportanceSampleLambert(u, localToWorld, L, NdotL, weightOverPdf);
                    float fabricLambert = FabricLambertNoPI(roughness);
                    acc.z += fabricLambert * weightOverPdf;
                }

                acc /= sampleCount;
                return acc;
            }

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texCoord : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.texCoord = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float NdotV = input.texCoord.x;
                float perceptualRoughness = input.texCoord.y;
                float3 V = float3(sqrt(1.0 - NdotV * NdotV), 0.0, NdotV);
                float3 N = float3(0.0, 0.0, 1.0);
                float4 preFGD = IntegrateCharlieAndFabricLambertFGD(V, N, PerceptualRoughnessToRoughness(perceptualRoughness));
                return float4(preFGD.xyz, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
