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
            ColorMask RGBA
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
            #pragma multi_compile_local_fragment _ VIVID_MESHLET_MOTION
            #pragma target 4.5 VIVID_MESHLET_MOTION

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/MotionVectorEncoding.hlsl"

            #if defined(VIVID_MESHLET_MOTION)
            #include "Packages/com.vivid.render-pipelines/Runtime/SubSystem/PrimitiveScene/VividPrimitiveSceneTypes.cs.hlsl"
            Texture2D<uint2> _MotionVisibility;
            StructuredBuffer<VividLegacyInstanceMappingData> _VividLegacyInstanceMappingData;
            StructuredBuffer<VividPrimitiveData> _VividPrimitiveData;
            StructuredBuffer<VividPrimitiveTransformData> _VividPrimitiveTransformData;
            StructuredBuffer<VividPrimitivePreviousTransformData> _VividPrimitivePreviousTransformData;
            uint _VividPrimitiveCount, _VividLegacyInstanceMappingCount;
            #endif
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

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 depthUV = ClampAndScaleUV(input.uv, _ScreenSize.zw, 0.5);
                float depth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, depthUV, 0).x;

                float3 positionWS = ComputeWorldSpacePosition(input.uv, depth, UNITY_MATRIX_I_VP);
                float4 positionCSNoJitter = mul(_NonJitteredViewProjMatrix, float4(positionWS, 1.0));
                float4 previousPositionCSNoJitter = mul(_PrevViewProjMatrix, float4(positionWS, 1.0));

                uint objectVelocity = 0u;
                #if defined(VIVID_MESHLET_MOTION)
                uint instanceIndex = _MotionVisibility.Load(int3(uint2(input.positionCS.xy), 0)).x - 1u;
                if (instanceIndex < _VividLegacyInstanceMappingCount)
                {
                    VividLegacyInstanceMappingData mapping = _VividLegacyInstanceMappingData[instanceIndex];
                    if ((mapping.Flags & 1u) != 0u && mapping.PrimitiveIndex < _VividPrimitiveCount)
                    {
                        VividPrimitiveData primitive = _VividPrimitiveData[mapping.PrimitiveIndex];
                        if (primitive.Generation == mapping.PrimitiveGeneration)
                        {
                            if ((primitive.Flags & VIVIDPRIMITIVEFLAGS_FORCE_NO_MOTION) != 0u)
                                return 0.0;
                            if ((primitive.Flags & VIVIDPRIMITIVEFLAGS_OBJECT_MOTION) != 0u)
                            {
                                float4 local = mul(_VividPrimitiveTransformData[primitive.TransformIndex].WorldToObjectMatrix, float4(positionWS, 1));
                                float4 previousWS = mul(_VividPrimitivePreviousTransformData[primitive.TransformIndex].PreviousObjectToWorldMatrix, local);
                                if (any(abs(previousWS.xyz - positionWS) > 1e-5))
                                {
                                    previousPositionCSNoJitter = mul(_PrevViewProjMatrix, previousWS);
                                    objectVelocity = 1u;
                                }
                            }
                        }
                    }
                }
                #endif
                if (positionCSNoJitter.w <= 0.0 || previousPositionCSNoJitter.w <= 0.0)
                    return 0.0;

                float2 positionNDC = positionCSNoJitter.xy * rcp(positionCSNoJitter.w);
                float2 previousPositionNDC = previousPositionCSNoJitter.xy * rcp(previousPositionCSNoJitter.w);
                float2 motionVector = positionNDC - previousPositionNDC;

                motionVector.x = abs(motionVector.x) < VIVIDRP_CAMERA_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD.x ? 0.0 : motionVector.x;
                motionVector.y = abs(motionVector.y) < VIVIDRP_CAMERA_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD.y ? 0.0 : motionVector.y;
                float maxClipMotion = objectVelocity != 0u ? 2.0 : 1.0;
                motionVector = clamp(
                    motionVector,
                    -maxClipMotion + VIVIDRP_CAMERA_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD,
                    maxClipMotion - VIVIDRP_CAMERA_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD);

#if UNITY_UV_STARTS_AT_TOP
                motionVector.y = -motionVector.y;
#endif

                float deltaZ = positionCSNoJitter.z / positionCSNoJitter.w - previousPositionCSNoJitter.z / previousPositionCSNoJitter.w;
                return VividEncodeVelocity(float3(motionVector, deltaZ), objectVelocity);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Decode Motion UV"
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DecodeUV
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #define USE_FULL_PRECISION_BLIT_TEXTURE
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/MotionVectorEncoding.hlsl"

            float2 DecodeUV(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return VividDecodeMotionUV(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
