Shader "VividRP/Material/SkyAmbientVisualization"
{
    Properties
    {
        [Main(Display, _, on, off)] _Display("Display", Float) = 1
        [MainColor] [Sub(Display)] _Tint("Tint", Color) = (1, 1, 1, 1)
        [Sub(Display)] _Intensity("Intensity", Range(0.0, 8.0)) = 1.0
        [Sub(Display)] _DisplayGamma("Display Gamma", Range(0.25, 4.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "VividRenderPipeline"
        }

        HLSLINCLUDE
        #pragma target 4.5
        #pragma multi_compile_instancing

        #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
        #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/BakedGI.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            float _Intensity;
            float _DisplayGamma;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;

            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float3 normalWS = SafeNormalize(input.normalWS);
            float3 ambientLighting = max(VividSampleAmbientProbe(normalWS), 0.0);
            ambientLighting *= _Tint.rgb * _Intensity;
            ambientLighting = pow(max(ambientLighting, 0.0), rcp(max(_DisplayGamma, 1e-4)));
            return half4(ambientLighting, 1.0);
        }
        ENDHLSL

        Pass
        {
            Name "VividForward"
            Tags
            {
                "LightMode" = "VividForward"
            }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "SRPDefaultUnlit"
            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    CustomEditor "LWGUI.LWGUI"

    FallBack Off
}
