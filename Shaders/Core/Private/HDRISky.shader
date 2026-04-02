Shader "Hidden/VividRP/HDRISky"
{
    Properties
    {
        [NoScaleOffset] _DepthTexture("Depth", 2D) = "white" {}
        [NoScaleOffset] _SkyCubemap("Sky Cubemap", Cube) = "" {}
        [HDR] _SkyTint("Sky Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _SkyParam("Sky Param", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "HDRISky"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            TEXTURE2D_X_FLOAT(_DepthTexture);
            SAMPLER(sampler_DepthTexture);
            TEXTURECUBE(_SkyCubemap);
            SAMPLER(sampler_SkyCubemap);
            float4x4 _PixelCoordToViewDirWS;
            float4 _SkyTint;
            float4 _SkyParam;

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

                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);

                return output;
            }

            float3 RotateAroundYAxis(float3 directionWS, float rotationDegrees)
            {
                float rotationRadians = radians(rotationDegrees);
                float s;
                float c;
                sincos(rotationRadians, s, c);

                return float3(
                    c * directionWS.x - s * directionWS.z,
                    directionWS.y,
                    s * directionWS.x + c * directionWS.z);
            }

            // Generates a world-space view direction for sky and atmospheric effects
            float3 GetSkyViewDirWS(float2 positionCS)
            {
                float4 viewDirWS = mul(float4(positionCS.xy, 1.0f, 1.0f), _PixelCoordToViewDirWS);
                return normalize(viewDirWS.xyz);
            }


            float4 Frag(Varyings input) : SV_Target
            {
                // float deviceDepth = SAMPLE_TEXTURE2D_X(_DepthTexture, sampler_DepthTexture, input.uv).r;
                //
                // if (!deviceDepth == UNITY_RAW_FAR_CLIP_VALUE)
                //     return 0;

                float3 viewDirWS = GetSkyViewDirWS(input.positionCS.xy);

                // Reverse it to point into the scene
                float3 dir = RotateAroundYAxis(-viewDirWS, _SkyParam.z);

                float3 skyColor = SAMPLE_TEXTURECUBE(_SkyCubemap, sampler_SkyCubemap, dir).rgb;
                skyColor = VividApplyPreExposure(skyColor * _SkyTint.rgb * exp2(_SkyParam.x) * _SkyParam.y);
                return float4(skyColor, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
