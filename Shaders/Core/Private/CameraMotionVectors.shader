Shader "Hidden/VividRP/CameraMotionVectors"
{
    Properties
    {
        [NoScaleOffset] _CameraDepthTexture("Camera Depth", 2D) = "white" {}
        [HideInInspector] _StencilRef("_StencilRef", Int) = 32
        [HideInInspector] _StencilMask("_StencilMask", Int) = 32
    }

    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "Camera Motion Vectors"
            ColorMask RG
            Cull Off
            ZWrite Off
            ZTest Less
            Stencil
            {
                WriteMask [_StencilMask]
                ReadMask [_StencilMask]
                Ref [_StencilRef]
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"

            TEXTURE2D_X_FLOAT(_CameraDepthTexture);

            #define VIVIDRP_CAMERA_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD (0.01 * _ScreenSize.zw)

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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 depthUV = ClampAndScaleUV(input.uv, _ScreenSize.zw, 0.5);
                float depth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, depthUV, 0).x;

                float3 positionWS = ComputeWorldSpacePosition(input.uv, depth, UNITY_MATRIX_I_VP);
                float4 positionCSNoJitter = mul(_NonJitteredViewProjMatrix, float4(positionWS, 1.0));
                float4 previousPositionCSNoJitter = mul(_PrevViewProjMatrix, float4(positionWS, 1.0));

                float2 positionNDC = positionCSNoJitter.xy * rcp(positionCSNoJitter.w);
                float2 previousPositionNDC = previousPositionCSNoJitter.xy * rcp(previousPositionCSNoJitter.w);
                float2 motionVector = positionNDC - previousPositionNDC;

                motionVector.x = abs(motionVector.x) < VIVIDRP_CAMERA_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD.x ? 0.0 : motionVector.x;
                motionVector.y = abs(motionVector.y) < VIVIDRP_CAMERA_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD.y ? 0.0 : motionVector.y;
                motionVector = clamp(
                    motionVector,
                    -1.0 + VIVIDRP_CAMERA_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD,
                    1.0 - VIVIDRP_CAMERA_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD);

#if UNITY_UV_STARTS_AT_TOP
                motionVector.y = -motionVector.y;
#endif

                return float4(motionVector * 0.5, 0.0, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
