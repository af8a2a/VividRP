Shader "Hidden/VividRP/Particles/BillboardUnlit"
{
    Properties
    {
        [HideInInspector] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "VividForward"
            Tags { "LightMode" = "VividForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
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

                    #define VIVID_PARTICLE_BASE_COLOR UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(float4, _BaseColor, float4(1.0, 1.0, 1.0, 1.0))
                #else
                    CBUFFER_START(UnityPerMaterial)
                        float4 _BaseColor;
                    CBUFFER_END

                    #define VIVID_PARTICLE_BASE_COLOR _BaseColor
                #endif

                struct Attributes
                {
                    float3 positionOS : POSITION;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                };

                struct Varyings
                {
                    float4 positionCS : SV_POSITION;
                    float4 color : COLOR0;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                    UNITY_VERTEX_OUTPUT_STEREO
                };

                Varyings Vert(Attributes input)
                {
                    Varyings output = (Varyings)0;
                    UNITY_SETUP_INSTANCE_ID(input);
                    UNITY_TRANSFER_INSTANCE_ID(input, output);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                    float4x4 objectToWorld = GetObjectToWorldMatrix();
                    float3 centerRWS = float3(objectToWorld._m03, objectToWorld._m13, objectToWorld._m23);
                    float sizeX = length(float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20));
                    float sizeY = length(float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21));

                    float4x4 viewToWorld = GetViewToWorldMatrix();
                    float3 viewRight = normalize(float3(viewToWorld._m00, viewToWorld._m10, viewToWorld._m20));
                    float3 viewUp = normalize(float3(viewToWorld._m01, viewToWorld._m11, viewToWorld._m21));
                    float3 positionRWS = centerRWS
                        + viewRight * (input.positionOS.x * sizeX)
                        + viewUp * (input.positionOS.y * sizeY);

                    output.positionCS = TransformWorldToHClip(positionRWS);
                    output.color = VIVID_PARTICLE_BASE_COLOR;
                    return output;
                }

                float4 Frag(Varyings input) : SV_Target
                {
                    UNITY_SETUP_INSTANCE_ID(input);
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                    return float4(VividApplyPreExposure(input.color.rgb), input.color.a);
                }
            ENDHLSL
        }

        Pass
        {
            Name "SRPDefaultUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
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

                    #define VIVID_PARTICLE_BASE_COLOR UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(float4, _BaseColor, float4(1.0, 1.0, 1.0, 1.0))
                #else
                    CBUFFER_START(UnityPerMaterial)
                        float4 _BaseColor;
                    CBUFFER_END

                    #define VIVID_PARTICLE_BASE_COLOR _BaseColor
                #endif

                struct Attributes
                {
                    float3 positionOS : POSITION;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                };

                struct Varyings
                {
                    float4 positionCS : SV_POSITION;
                    float4 color : COLOR0;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                    UNITY_VERTEX_OUTPUT_STEREO
                };

                Varyings Vert(Attributes input)
                {
                    Varyings output = (Varyings)0;
                    UNITY_SETUP_INSTANCE_ID(input);
                    UNITY_TRANSFER_INSTANCE_ID(input, output);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                    float4x4 objectToWorld = GetObjectToWorldMatrix();
                    float3 centerRWS = float3(objectToWorld._m03, objectToWorld._m13, objectToWorld._m23);
                    float sizeX = length(float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20));
                    float sizeY = length(float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21));

                    float4x4 viewToWorld = GetViewToWorldMatrix();
                    float3 viewRight = normalize(float3(viewToWorld._m00, viewToWorld._m10, viewToWorld._m20));
                    float3 viewUp = normalize(float3(viewToWorld._m01, viewToWorld._m11, viewToWorld._m21));
                    float3 positionRWS = centerRWS
                        + viewRight * (input.positionOS.x * sizeX)
                        + viewUp * (input.positionOS.y * sizeY);

                    output.positionCS = TransformWorldToHClip(positionRWS);
                    output.color = VIVID_PARTICLE_BASE_COLOR;
                    return output;
                }

                float4 Frag(Varyings input) : SV_Target
                {
                    UNITY_SETUP_INSTANCE_ID(input);
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                    return float4(VividApplyPreExposure(input.color.rgb), input.color.a);
                }
            ENDHLSL
        }
    }
}
