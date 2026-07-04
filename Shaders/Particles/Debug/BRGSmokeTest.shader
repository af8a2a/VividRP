Shader "Hidden/VividRP/Particles/BRGSmokeTest"
{
    Properties
    {
        [HideInInspector] _BaseColor("Base Color", Color) = (1, 0, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "VividForward"
            Tags { "LightMode" = "VividForward" }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma multi_compile _ DOTS_INSTANCING_ON
                #pragma vertex Vert
                #pragma fragment Frag

                #ifdef DOTS_INSTANCING_ON
                    cbuffer UnityDOTSInstancing_BuiltinPropertyMetadata
                    {
                        uint unity_DOTSInstancingF48_Metadataunity_ObjectToWorld;
                        uint unity_DOTSInstancingF48_Metadataunity_WorldToObject;
                        uint unity_DOTSInstancingF48_Metadataunity_MatrixPreviousM;
                        uint unity_DOTSInstancingF48_Metadataunity_MatrixPreviousMI;
                    }

                    #define unity_WorldTransformParams LoadDOTSInstancedData_WorldTransformParams()
                    #define unity_RenderingLayer LoadDOTSInstancedData_RenderingLayer()
                    #define UNITY_SETUP_DOTS_SH_COEFFS
                    #define UNITY_SETUP_DOTS_RENDER_BOUNDS
                #endif

                #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
                #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"

                #ifdef UNITY_DOTS_INSTANCING_ENABLED
                    UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                        UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
                    UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

                    #define VIVID_BRG_BASE_COLOR UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(float4, _BaseColor, float4(1.0, 0.0, 1.0, 1.0))
                #else
                    CBUFFER_START(UnityPerMaterial)
                        float4 _BaseColor;
                    CBUFFER_END

                    #define VIVID_BRG_BASE_COLOR _BaseColor
                #endif

                struct Attributes
                {
                    float3 positionOS : POSITION;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                };

                struct Varyings
                {
                    float4 positionCS : SV_POSITION;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                    UNITY_VERTEX_OUTPUT_STEREO
                };

                Varyings Vert(Attributes input)
                {
                    Varyings output = (Varyings)0;
                    UNITY_SETUP_INSTANCE_ID(input);
                    UNITY_TRANSFER_INSTANCE_ID(input, output);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                    output.positionCS = TransformObjectToHClip(input.positionOS);
                    return output;
                }

                float4 Frag(Varyings input) : SV_Target
                {
                    UNITY_SETUP_INSTANCE_ID(input);
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                    float4 color = VIVID_BRG_BASE_COLOR;
                    return float4(VividApplyPreExposure(color.rgb), color.a);
                }
            ENDHLSL
        }

        Pass
        {
            Name "SRPDefaultUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma multi_compile _ DOTS_INSTANCING_ON
                #pragma vertex Vert
                #pragma fragment Frag

                #ifdef DOTS_INSTANCING_ON
                    cbuffer UnityDOTSInstancing_BuiltinPropertyMetadata
                    {
                        uint unity_DOTSInstancingF48_Metadataunity_ObjectToWorld;
                        uint unity_DOTSInstancingF48_Metadataunity_WorldToObject;
                        uint unity_DOTSInstancingF48_Metadataunity_MatrixPreviousM;
                        uint unity_DOTSInstancingF48_Metadataunity_MatrixPreviousMI;
                    }

                    #define unity_WorldTransformParams LoadDOTSInstancedData_WorldTransformParams()
                    #define unity_RenderingLayer LoadDOTSInstancedData_RenderingLayer()
                    #define UNITY_SETUP_DOTS_SH_COEFFS
                    #define UNITY_SETUP_DOTS_RENDER_BOUNDS
                #endif

                #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
                #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"

                #ifdef UNITY_DOTS_INSTANCING_ENABLED
                    UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                        UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
                    UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

                    #define VIVID_BRG_BASE_COLOR UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(float4, _BaseColor, float4(1.0, 0.0, 1.0, 1.0))
                #else
                    CBUFFER_START(UnityPerMaterial)
                        float4 _BaseColor;
                    CBUFFER_END

                    #define VIVID_BRG_BASE_COLOR _BaseColor
                #endif

                struct Attributes
                {
                    float3 positionOS : POSITION;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                };

                struct Varyings
                {
                    float4 positionCS : SV_POSITION;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                    UNITY_VERTEX_OUTPUT_STEREO
                };

                Varyings Vert(Attributes input)
                {
                    Varyings output = (Varyings)0;
                    UNITY_SETUP_INSTANCE_ID(input);
                    UNITY_TRANSFER_INSTANCE_ID(input, output);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                    output.positionCS = TransformObjectToHClip(input.positionOS);
                    return output;
                }

                float4 Frag(Varyings input) : SV_Target
                {
                    UNITY_SETUP_INSTANCE_ID(input);
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                    float4 color = VIVID_BRG_BASE_COLOR;
                    return float4(VividApplyPreExposure(color.rgb), color.a);
                }
            ENDHLSL
        }
    }
}
