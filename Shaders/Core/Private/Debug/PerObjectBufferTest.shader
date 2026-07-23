Shader "Hidden/VividRP/Tests/PerObjectBuffer"
{
    Properties
    {
        _PerObjectTestPadding("Per Object Test Padding", Float) = 0
    }

    SubShader
    {
        HLSLINCLUDE
        #pragma target 4.5

        #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
        #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/PerObjectBuffer.hlsl"

        uint GetPerObjectTestLayoutSignature()
        {
            return 0x7047A0E7u;
        }

        float4 _VividPerObjectTestAddressWords;

        CBUFFER_START(UnityPerMaterial)
            float _PerObjectTestPadding;
        CBUFFER_END

        uint GetPerObjectTestUserValue(float objectX)
        {
            if (objectX < -1.0f)
            {
                return (uint)_VividPerObjectTestAddressWords.x
                    | ((uint)_VividPerObjectTestAddressWords.y << 16u);
            }
            if (objectX < 1.0f)
            {
                return (uint)_VividPerObjectTestAddressWords.z
                    | ((uint)_VividPerObjectTestAddressWords.w << 16u);
            }
            return 0u;
        }

        struct Attributes
        {
            float3 positionOS : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float objectX : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        Varyings PerObjectTestVertex(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            Varyings output;
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            const float4 positionWS = mul(UNITY_MATRIX_M, float4(input.positionOS, 1.0f));
            output.positionCS = float4(positionWS.x / 3.0f, positionWS.y, 0.0f, 1.0f);
            output.objectX = positionWS.x;
            return output;
        }

        float4 PerObjectTestFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            const VividPerObjectContext context =
                VividPerObjectCreateContextFromUserValue(
                    GetPerObjectTestUserValue(input.objectX),
                    GetPerObjectTestLayoutSignature());
            const float scalarValue = VividPerObjectLoadFloat(context, 4u, 0.25f);
            const float4 vectorValue = VividPerObjectLoadFloat4(context, 8u, float4(0.5f, 0.0f, 0.0f, 0.0f));
            const float4 colorValue = VividPerObjectLoadFloat4(context, 24u, float4(0.0f, 0.0f, 0.75f, 0.0f));
            const float4x4 matrixValue = VividPerObjectLoadFloat4x4(context, 40u, float4x4(
                1.0f, 0.0f, 0.0f, 0.0f,
                0.0f, 1.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 1.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f));
            return float4(scalarValue, vectorValue.x, colorValue.b, matrixValue[0][0]);
        }

        float4 PerObjectTestDepthFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            const VividPerObjectContext context =
                VividPerObjectCreateContextFromUserValue(
                    GetPerObjectTestUserValue(input.objectX),
                    GetPerObjectTestLayoutSignature());
            const float scalarValue = VividPerObjectLoadFloat(context, 4u, 0.25f);
            return scalarValue.xxxx;
        }

        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma vertex PerObjectTestVertex
            #pragma fragment PerObjectTestFragment
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma vertex PerObjectTestVertex
            #pragma fragment PerObjectTestDepthFragment
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma vertex PerObjectTestVertex
            #pragma fragment PerObjectTestDepthFragment
            ENDHLSL
        }
    }
}
