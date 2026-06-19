Shader "Hidden/VividRP/PreIntegratedFGD_GGXDisneyDiffuse"
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

            #define VIVID_FGD_TEXTURE_RESOLUTION 64

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
                float2 coordLUT = RemapHalfTexelCoordTo01(input.texCoord, VIVID_FGD_TEXTURE_RESOLUTION);
                float NdotV = coordLUT.x * coordLUT.x;
                float perceptualRoughness = coordLUT.y;
                float4 preFGD = IntegrateGGXAndDisneyDiffuseFGD(NdotV, PerceptualRoughnessToRoughness(perceptualRoughness));
                return float4(preFGD.xyz, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
