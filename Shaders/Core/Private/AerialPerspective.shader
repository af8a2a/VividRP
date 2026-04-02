Shader "Hidden/VividRP/AerialPerspective"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "AerialPerspective"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            TEXTURE2D_X(_InputColor);
            SAMPLER(sampler_InputColor);
            TEXTURE2D_X_FLOAT(_DepthTexture);
            SAMPLER(sampler_DepthTexture);
            TEXTURE2D(_TransmittanceLUT);
            SAMPLER(sampler_TransmittanceLUT);
            TEXTURE2D(_MultiScatteringLUT);
            SAMPLER(sampler_MultiScatteringLUT);

            float4 _SkyCameraPositionPS;
            float4 _SkySunDirection;
            float4 _SkySunColor;
            float4 _SkyPlanetParams;
            float4 _SkyFogParams;
            static const float MAX_SKY_RADIANCE = 60000.0f;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float2 EncodeTransmittanceUv(float height, float mu, float planetRadius, float atmosphereRadius)
            {
                float atmosphereHeight = max(atmosphereRadius - planetRadius, 1.0f);
                return float2(saturate(mu * 0.5f + 0.5f), saturate(height / atmosphereHeight));
            }

            float3 SampleTransmittanceLut(float height, float mu, float planetRadius, float atmosphereRadius)
            {
                float2 uv = EncodeTransmittanceUv(height, mu, planetRadius, atmosphereRadius);
                return SAMPLE_TEXTURE2D(_TransmittanceLUT, sampler_TransmittanceLUT, uv).rgb;
            }

            float2 EncodeMultiScatteringUv(float height, float sunCos, float planetRadius, float atmosphereRadius)
            {
                float atmosphereHeight = max(atmosphereRadius - planetRadius, 1.0f);
                return float2(saturate(sunCos * 0.5f + 0.5f), saturate(height / atmosphereHeight));
            }

            float3 SampleMultiScatteringLut(float height, float sunCos, float planetRadius, float atmosphereRadius)
            {
                float2 uv = EncodeMultiScatteringUv(height, sunCos, planetRadius, atmosphereRadius);
                return SAMPLE_TEXTURE2D(_MultiScatteringLUT, sampler_MultiScatteringLUT, uv).rgb;
            }

            float3 SanitizeSkyRadiance(float3 color)
            {
                if (any(isnan(color)) || any(isinf(color)))
                    return 0.0f;

                return clamp(max(color, 0.0f), 0.0f, MAX_SKY_RADIANCE);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 source = SAMPLE_TEXTURE2D_X(_InputColor, sampler_InputColor, input.uv);

                if (_SkyFogParams.x <= 0.5f)
                    return source;

                float deviceDepth = SAMPLE_TEXTURE2D_X(_DepthTexture, sampler_DepthTexture, input.uv).r;
                if (abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) < 1e-5f)
                    return source;

                float3 positionWS = ComputeWorldSpacePosition(input.uv, deviceDepth, UNITY_MATRIX_I_VP);
                float3 viewVectorWS = positionWS - _WorldSpaceCameraPos;
                float distanceWS = min(length(viewVectorWS), _SkyFogParams.w);
                if (distanceWS <= 1e-5f)
                    return source;

                float3 viewDirWS = viewVectorWS / distanceWS;
                float planetRadius = max(_SkyPlanetParams.x, 1000.0f);
                float atmosphereRadius = max(_SkyPlanetParams.y, planetRadius + 1.0f);
                float cameraHeight = max(length(_SkyCameraPositionPS.xyz) - planetRadius, 0.0f);
                float mu = dot(normalize(_SkyCameraPositionPS.xyz), viewDirWS);
                float3 lutTransmittance = SampleTransmittanceLut(cameraHeight, mu, planetRadius, atmosphereRadius);
                float fogDistanceFactor = 1.0f - exp(-_SkyFogParams.z * distanceWS * 0.001f);
                float heightAttenuation = exp(-max(positionWS.y - _SkyFogParams.y, 0.0f) * 0.002f);
                float fogFactor = saturate(fogDistanceFactor * heightAttenuation);
                float3 sceneTransmittance = lerp(float3(1.0f, 1.0f, 1.0f), lutTransmittance, fogFactor);

                float sunCos = dot(normalize(_SkyCameraPositionPS.xyz), normalize(_SkySunDirection.xyz));
                float3 fogColor = (SampleMultiScatteringLut(cameraHeight, sunCos, planetRadius, atmosphereRadius) + _SkySunColor.rgb * 0.01f)
                    * max(_SkyPlanetParams.z, 0.0f);
                fogColor = VividApplyPreExposure(SanitizeSkyRadiance(fogColor));
                float3 shadedColor = source.rgb * sceneTransmittance + fogColor * (1.0f - sceneTransmittance);
                return float4(shadedColor, source.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
