Shader "Hidden/VividRP/VolumetricFogComposite"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "VolumetricFogComposite"
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Volumetric/VBuffer.hlsl"

            Texture2D<float4> _InputColor;
            Texture2D<float> _CameraDepth;
            Texture3D<float4> _VBufferLighting;
            float _VolumetricEnabled;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.positionCS.xy * _ScreenSize.zw;
                float4 color = _InputColor.SampleLevel(sampler_LinearClamp, uv, 0);

                if (_VolumetricEnabled <= 0.5)
                    return color;

                float deviceDepth = _CameraDepth.SampleLevel(sampler_PointClamp, uv, 0);
                float linearDistance = GetVBufferLinearDistanceFromDeviceDepth(uv, deviceDepth);
                float3 vBufferUVW = GetVBufferUVW(uv, linearDistance);
                float4 lighting = _VBufferLighting.SampleLevel(sampler_LinearClamp, vBufferUVW, 0);
                return float4(color.rgb * saturate(lighting.a) + lighting.rgb, color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
