Shader "Hidden/VividRP/PhysicallyBasedSky"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "PhysicallyBasedSky"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            static const int VIEW_SAMPLE_COUNT = 12;
            static const int LIGHT_SAMPLE_COUNT = 6;
            static const float BLOCKED_OPTICAL_DEPTH = 100000.0f;

            float4x4 _PixelCoordToViewDirWS;
            float4 _SkyCameraPositionPS;
            float4 _SkySunDirection;
            float4 _SkySunColor;
            float4 _SkyPlanetParams;
            float4 _SkyAirScattering;
            float4 _SkyAirExtinction;
            float4 _SkyAerosolScattering;
            float4 _SkyAerosolExtinction;
            float4 _SkyOzoneExtinction;
            float4 _SkyOzoneParams;
            float4 _SkyGroundTint;

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

            float3 GetSkyViewDirWS(float2 positionCS)
            {
                float4 viewDirWS = mul(float4(positionCS.xy, 1.0f, 1.0f), _PixelCoordToViewDirWS);
                return normalize(viewDirWS.xyz);
            }

            bool IntersectAtmosphere(float3 origin, float3 direction, float atmosphereRadius, out float entryDistance, out float exitDistance)
            {
                float b = dot(origin, direction);
                float c = dot(origin, origin) - atmosphereRadius * atmosphereRadius;
                float discriminant = b * b - c;

                if (discriminant < 0.0f)
                {
                    entryDistance = 0.0f;
                    exitDistance = 0.0f;
                    return false;
                }

                float sqrtDiscriminant = sqrt(discriminant);
                entryDistance = -b - sqrtDiscriminant;
                exitDistance = -b + sqrtDiscriminant;
                return exitDistance > 0.0f;
            }

            bool IntersectGround(float3 origin, float3 direction, float planetRadius, out float distance)
            {
                float b = dot(origin, direction);
                float c = dot(origin, origin) - planetRadius * planetRadius;
                float discriminant = b * b - c;

                if (discriminant < 0.0f)
                {
                    distance = 0.0f;
                    return false;
                }

                distance = -b - sqrt(discriminant);
                return distance > 0.0f;
            }

            float EvaluateOzoneDensity(float height, float minimumAltitude, float layerWidth)
            {
                if (layerWidth <= 0.0f)
                    return 0.0f;

                float normalizedHeight = (height - minimumAltitude) / layerWidth;
                return saturate(1.0f - abs(normalizedHeight * 2.0f - 1.0f));
            }

            float3 EvaluateTransmittance(
                float3 airExtinction,
                float3 aerosolExtinction,
                float opticalDepthAir,
                float opticalDepthAerosol,
                float3 opticalDepthOzone)
            {
                return exp(-(airExtinction * opticalDepthAir + aerosolExtinction * opticalDepthAerosol + opticalDepthOzone));
            }

            void ComputeOpticalDepthToSun(
                float3 samplePosition,
                float3 sunDirection,
                float planetRadius,
                float atmosphereRadius,
                out float opticalDepthAir,
                out float opticalDepthAerosol,
                out float3 opticalDepthOzone)
            {
                opticalDepthAir = 0.0f;
                opticalDepthAerosol = 0.0f;
                opticalDepthOzone = float3(0.0f, 0.0f, 0.0f);

                float atmosphereEntry;
                float atmosphereExit;
                if (!IntersectAtmosphere(samplePosition, sunDirection, atmosphereRadius, atmosphereEntry, atmosphereExit))
                    return;

                float groundHit;
                if (IntersectGround(samplePosition, sunDirection, planetRadius, groundHit) && groundHit > 0.0f && groundHit < atmosphereExit)
                {
                    opticalDepthAir = BLOCKED_OPTICAL_DEPTH;
                    opticalDepthAerosol = BLOCKED_OPTICAL_DEPTH;
                    opticalDepthOzone = float3(BLOCKED_OPTICAL_DEPTH, BLOCKED_OPTICAL_DEPTH, BLOCKED_OPTICAL_DEPTH);
                    return;
                }

                float stepLength = atmosphereExit / LIGHT_SAMPLE_COUNT;
                if (stepLength <= 0.0f)
                    return;

                float airScaleHeight = max(_SkyOzoneParams.y, 1.0f);
                float aerosolScaleHeight = max(_SkyOzoneParams.w, 1.0f);
                float ozoneMinimumAltitude = _SkyOzoneExtinction.w;
                float ozoneLayerWidth = _SkyOzoneParams.x;

                [loop]
                for (int sampleIndex = 0; sampleIndex < LIGHT_SAMPLE_COUNT; sampleIndex++)
                {
                    float sampleDistance = (sampleIndex + 0.5f) * stepLength;
                    float3 lightSamplePosition = samplePosition + sunDirection * sampleDistance;
                    float height = max(length(lightSamplePosition) - planetRadius, 0.0f);
                    float localAirDensity = exp(-height / airScaleHeight);
                    float localAerosolDensity = exp(-height / aerosolScaleHeight);
                    float localOzoneDensity = EvaluateOzoneDensity(height, ozoneMinimumAltitude, ozoneLayerWidth);

                    opticalDepthAir += localAirDensity * stepLength;
                    opticalDepthAerosol += localAerosolDensity * stepLength;
                    opticalDepthOzone += _SkyOzoneExtinction.rgb * (localOzoneDensity * stepLength);
                }
            }

            float3 ComputeSunDiskTransmittance(float3 cameraPosition, float3 sunDirection, float planetRadius, float atmosphereRadius)
            {
                float opticalDepthAir;
                float opticalDepthAerosol;
                float3 opticalDepthOzone;
                ComputeOpticalDepthToSun(
                    cameraPosition,
                    sunDirection,
                    planetRadius,
                    atmosphereRadius,
                    opticalDepthAir,
                    opticalDepthAerosol,
                    opticalDepthOzone);
                return EvaluateTransmittance(
                    _SkyAirExtinction.rgb,
                    _SkyAerosolExtinction.rgb,
                    opticalDepthAir,
                    opticalDepthAerosol,
                    opticalDepthOzone);
            }

            float3 EvaluateSky(float3 directionWS)
            {
                float3 normalizedDirection = normalize(directionWS);
                float3 cameraPosition = _SkyCameraPositionPS.xyz;
                float3 sunDirection = normalize(_SkySunDirection.xyz);
                float3 sunColor = _SkySunColor.rgb;
                float planetRadius = max(_SkyPlanetParams.x, 1000.0f);
                float atmosphereRadius = max(_SkyPlanetParams.y, planetRadius + 1.0f);
                float airScaleHeight = max(_SkyOzoneParams.y, 1.0f);
                float aerosolScaleHeight = max(_SkyOzoneParams.w, 1.0f);
                float ozoneMinimumAltitude = _SkyOzoneExtinction.w;
                float ozoneLayerWidth = _SkyOzoneParams.x;
                float g = clamp(_SkyAerosolExtinction.w, -0.95f, 0.95f);

                float atmosphereEntry;
                float atmosphereExit;
                if (!IntersectAtmosphere(cameraPosition, normalizedDirection, atmosphereRadius, atmosphereEntry, atmosphereExit))
                    return 0.0f;

                float rayLength = atmosphereExit;
                float groundHit;
                if (IntersectGround(cameraPosition, normalizedDirection, planetRadius, groundHit) && groundHit > 0.0f)
                    rayLength = min(rayLength, groundHit);

                float stepLength = rayLength / VIEW_SAMPLE_COUNT;
                if (stepLength <= 0.0f)
                    return 0.0f;

                float opticalDepthAir = 0.0f;
                float opticalDepthAerosol = 0.0f;
                float3 opticalDepthOzone = 0.0f;
                float mu = clamp(dot(normalizedDirection, sunDirection), -1.0f, 1.0f);
                float phaseRayleigh = 3.0f / (16.0f * PI) * (1.0f + mu * mu);
                float phaseMieNumerator = 3.0f / (8.0f * PI) * (1.0f - g * g) * (1.0f + mu * mu);
                float phaseMieDenominator = (2.0f + g * g) * pow(max(1.0f + g * g - 2.0f * g * mu, 1e-3f), 1.5f);
                float phaseMie = phaseMieNumerator / max(phaseMieDenominator, 1e-3f);
                float3 inscattered = 0.0f;

                [loop]
                for (int sampleIndex = 0; sampleIndex < VIEW_SAMPLE_COUNT; sampleIndex++)
                {
                    float sampleDistance = (sampleIndex + 0.5f) * stepLength;
                    float3 samplePosition = cameraPosition + normalizedDirection * sampleDistance;
                    float height = max(length(samplePosition) - planetRadius, 0.0f);
                    float localAirDensity = exp(-height / airScaleHeight);
                    float localAerosolDensity = exp(-height / aerosolScaleHeight);
                    float localOzoneDensity = EvaluateOzoneDensity(height, ozoneMinimumAltitude, ozoneLayerWidth);

                    opticalDepthAir += localAirDensity * stepLength;
                    opticalDepthAerosol += localAerosolDensity * stepLength;
                    opticalDepthOzone += _SkyOzoneExtinction.rgb * (localOzoneDensity * stepLength);

                    float sunOpticalDepthAir;
                    float sunOpticalDepthAerosol;
                    float3 sunOpticalDepthOzone;
                    ComputeOpticalDepthToSun(
                        samplePosition,
                        sunDirection,
                        planetRadius,
                        atmosphereRadius,
                        sunOpticalDepthAir,
                        sunOpticalDepthAerosol,
                        sunOpticalDepthOzone);

                    float3 viewTransmittance = EvaluateTransmittance(
                        _SkyAirExtinction.rgb,
                        _SkyAerosolExtinction.rgb,
                        opticalDepthAir,
                        opticalDepthAerosol,
                        opticalDepthOzone);
                    float3 sunTransmittance = EvaluateTransmittance(
                        _SkyAirExtinction.rgb,
                        _SkyAerosolExtinction.rgb,
                        sunOpticalDepthAir,
                        sunOpticalDepthAerosol,
                        sunOpticalDepthOzone);
                    float3 scattering =
                        _SkyAirScattering.rgb * (localAirDensity * phaseRayleigh)
                        + _SkyAerosolScattering.rgb * (localAerosolDensity * phaseMie);
                    float3 attenuation = viewTransmittance * sunTransmittance;
                    inscattered += attenuation * scattering * stepLength;
                }

                float3 skyColor = inscattered * sunColor;

                if (rayLength < atmosphereExit)
                {
                    float3 groundTransmittance = EvaluateTransmittance(
                        _SkyAirExtinction.rgb,
                        _SkyAerosolExtinction.rgb,
                        opticalDepthAir,
                        opticalDepthAerosol,
                        opticalDepthOzone);
                    float groundLightingFactor = max(0.15f, sunDirection.y * 0.5f + 0.5f);
                    float3 groundLighting = _SkyGroundTint.rgb * groundLightingFactor;
                    skyColor += groundLighting * groundTransmittance;
                }

                if (_SkyPlanetParams.w > 0.5f)
                {
                    float sunAngularRadius = _SkyOzoneParams.z;
                    float sunCosThreshold = cos(sunAngularRadius);
                    float sunDot = clamp(dot(normalizedDirection, sunDirection), -1.0f, 1.0f);
                    float sunEdge = saturate((sunDot - (sunCosThreshold - 0.0025f)) / 0.0025f);

                    if (sunEdge > 0.0f)
                    {
                        float3 sunTransmittance = ComputeSunDiskTransmittance(cameraPosition, sunDirection, planetRadius, atmosphereRadius);
                        skyColor += sunColor * sunTransmittance * smoothstep(0.0f, 1.0f, sunEdge) * 2.0f;
                    }
                }

                return max(skyColor * max(_SkyPlanetParams.z, 0.0f), 0.0f);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 viewDirWS = -GetSkyViewDirWS(input.positionCS.xy);
                return float4(EvaluateSky(viewDirWS), 1.0f);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
