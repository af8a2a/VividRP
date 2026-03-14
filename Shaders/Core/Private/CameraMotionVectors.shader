Shader "Hidden/VividRP/CameraMotionVectors"
{
    Properties
    {
        [NoScaleOffset] _CameraDepthTexture("Camera Depth", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "Camera Motion Vectors"
            ColorMask RG
            Cull Off
            ZWrite On
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/MotionVectorsCommon.hlsl"

            TEXTURE2D_X_FLOAT(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            half4 Frag(Varyings input, out float outputDepth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float depth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, input.uv).x;
                outputDepth = depth;

                float3 positionWS = ComputeWorldSpacePosition(input.uv, depth, UNITY_MATRIX_I_VP);
                float4 positionCSNoJitter = mul(_NonJitteredViewProjMatrix, float4(positionWS, 1.0));
                float4 previousPositionCSNoJitter = mul(_PrevViewProjMatrix, float4(positionWS, 1.0));
                float2 velocity = CalcNdcMotionVectorFromCsPositions(positionCSNoJitter, previousPositionCSNoJitter);

                return float4(velocity, 0.0, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
