Shader "Hidden/VividRP/DepthOfField"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "DepthOfField"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _DOF_HQ_FILTERING

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            TEXTURE2D(_VividLinearDepthTexture);
            SAMPLER(sampler_VividLinearDepthTexture);

            float4 _BlitScaleBias;
            float4 _VividDoFSourceSize;   // xy: size, zw: inverse size
            float4 _VividDoFFocusParams;  // x: enabled, y: focus mode, z: focus distance, w: physical max CoC
            float4 _VividDoFManualParams; // x: near start, y: near end, z: far start, w: far end
            float4 _VividDoFBlurParams;   // x: near blur limit, y: far blur limit, z: near samples, w: far samples
            float4 _VividDoFExtraParams;  // x: adaptive sampling weight, y: limit manual near blur, z: hq filtering

            #define DYNAMIC_SCALING_APPLY_SCALEBIAS(uv) DynamicScalingApplyScaleBias(uv, _BlitScaleBias)

            static const int kMaxDoFSamples = 16;
            static const float2 kPoissonDisk[kMaxDoFSamples] =
            {
                float2(-0.94201624, -0.39906216),
                float2( 0.94558609, -0.76890725),
                float2(-0.09418410, -0.92938870),
                float2( 0.34495938,  0.29387760),
                float2(-0.91588581,  0.45771432),
                float2(-0.81544232, -0.87912464),
                float2(-0.38277543,  0.27676845),
                float2( 0.97484398,  0.75648379),
                float2( 0.44323325, -0.97511554),
                float2( 0.53742981, -0.47373420),
                float2(-0.26496911, -0.41893023),
                float2( 0.79197514,  0.19090188),
                float2(-0.24188840,  0.99706507),
                float2(-0.81409955,  0.91437590),
                float2( 0.19984126,  0.78641367),
                float2( 0.14383161, -0.14100790)
            };

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
                output.uv = DYNAMIC_SCALING_APPLY_SCALEBIAS(GetFullScreenTriangleTexCoord(input.vertexID));
                return output;
            }

            float2 ClampUv(float2 uv)
            {
                return ClampUVForBilinear(uv, _VividDoFSourceSize.zw);
            }

            float4 SampleSource(float2 uv)
            {
                uv = ClampUv(uv);

            #if defined(_DOF_HQ_FILTERING)
                float2 maxCoord = _RTHandleScale.xy - _VividDoFSourceSize.zw;
                return SampleTexture2DBicubic(
                    TEXTURE2D_ARGS(_BlitTexture, sampler_BlitTexture),
                    uv,
                    _VividDoFSourceSize,
                    maxCoord,
                    0.0);
            #else
                return SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv);
            #endif
            }

            float SampleLinearDepth(float2 uv)
            {
                uv = ClampUv(uv);
                return SAMPLE_TEXTURE2D(_VividLinearDepthTexture, sampler_VividLinearDepthTexture, uv).x;
            }

            float ComputeManualNearBlend(float linearDepth)
            {
                if (_VividDoFExtraParams.y < 0.5)
                    return 1.0;

                float nearEnd = max(_VividDoFManualParams.y, 1e-4);
                return 1.0 - saturate(linearDepth / (nearEnd * nearEnd));
            }

            float ComputeSignedCoC(float linearDepth)
            {
                float nearLimit = _VividDoFBlurParams.x;
                float farLimit = _VividDoFBlurParams.y;

                if (_VividDoFFocusParams.y < 1.5)
                {
                    float focusDistance = _VividDoFFocusParams.z;
                    float physicalMaxCoC = _VividDoFFocusParams.w;
                    float coc = (1.0 - focusDistance / max(linearDepth, 1e-4)) * physicalMaxCoC;
                    return clamp(coc, -nearLimit, farLimit);
                }

                float nearStart = _VividDoFManualParams.x;
                float nearEnd = _VividDoFManualParams.y;
                float farStart = _VividDoFManualParams.z;
                float farEnd = _VividDoFManualParams.w;

                float nearRange = nearStart - nearEnd;
                float farRange = farEnd - farStart;
                float nearBlend = ComputeManualNearBlend(linearDepth);

                float nearCoC = abs(nearRange) > 1e-4
                    ? saturate((linearDepth - nearEnd) / nearRange)
                    : step(linearDepth, nearEnd);
                nearCoC *= nearBlend;

                float farCoC = abs(farRange) > 1e-4
                    ? saturate((linearDepth - farStart) / farRange)
                    : step(farStart, linearDepth);

                return farCoC * farLimit - nearCoC * nearLimit;
            }

            int ResolveSampleCount(float baseCount, float radius, float limit)
            {
                float adaptiveWeight = max(_VividDoFExtraParams.x, 0.5);
                float normalizedRadius = saturate((radius / max(limit, 1e-4)) * adaptiveWeight);
                return clamp((int)round(lerp(1.0, baseCount, normalizedRadius)), 1, kMaxDoFSamples);
            }

            float ComputeCoverage(float sampleRadius, float distanceInPixels)
            {
                float weight = saturate(1.0 - distanceInPixels / max(sampleRadius, 1e-4));
                return weight * weight;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 sourceColor = SampleSource(input.uv);
                if (_VividDoFFocusParams.x < 0.5)
                    return sourceColor;

                float centerDepth = SampleLinearDepth(input.uv);
                if (centerDepth <= 0.0)
                    return sourceColor;

                float centerCoC = ComputeSignedCoC(centerDepth);
                float centerNearRadius = max(-centerCoC, 0.0);
                float centerFarRadius = max(centerCoC, 0.0);
                float nearLimit = max(_VividDoFBlurParams.x, 1e-4);
                float farLimit = max(_VividDoFBlurParams.y, 1e-4);
                float maxLimit = max(nearLimit, farLimit);

                int nearSampleCount = ResolveSampleCount(_VividDoFBlurParams.z, centerNearRadius, nearLimit);
                int farSampleCount = ResolveSampleCount(_VividDoFBlurParams.w, centerFarRadius, farLimit);

                float3 nearAcc = 0.0;
                float3 farAcc = 0.0;
                float nearWeight = 0.0;
                float farWeight = 0.0;
                float nearCoverage = 0.0;

                if (centerNearRadius > 1e-4)
                {
                    nearAcc += sourceColor.rgb;
                    nearWeight += 1.0;
                    nearCoverage = saturate(centerNearRadius / nearLimit);
                }

                if (centerFarRadius > 1e-4)
                {
                    farAcc += sourceColor.rgb;
                    farWeight += 1.0;
                }

                [unroll]
                for (int i = 0; i < kMaxDoFSamples; i++)
                {
                    if (i >= nearSampleCount && i >= farSampleCount)
                        break;

                    float2 tap = kPoissonDisk[i];
                    float2 sampleUv = input.uv + tap * maxLimit * _VividDoFSourceSize.zw;
                    float sampleDepth = SampleLinearDepth(sampleUv);
                    if (sampleDepth <= 0.0)
                        continue;

                    float sampleCoC = ComputeSignedCoC(sampleDepth);
                    float sampleNearRadius = max(-sampleCoC, 0.0);
                    float sampleFarRadius = max(sampleCoC, 0.0);
                    float distanceInPixels = length(tap) * maxLimit;

                    if (i < farSampleCount && sampleFarRadius > 1e-4)
                    {
                        float weight = ComputeCoverage(sampleFarRadius, distanceInPixels);
                        if (weight > 0.0)
                        {
                            farAcc += SampleSource(sampleUv).rgb * weight;
                            farWeight += weight;
                        }
                    }

                    if (i < nearSampleCount && sampleNearRadius > 1e-4)
                    {
                        float weight = ComputeCoverage(sampleNearRadius, distanceInPixels);
                        if (weight > 0.0)
                        {
                            nearAcc += SampleSource(sampleUv).rgb * weight;
                            nearWeight += weight;
                            nearCoverage += weight;
                        }
                    }
                }

                float3 result = sourceColor.rgb;

                if (farWeight > 1e-4 && centerFarRadius > 1e-4)
                {
                    float farBlend = saturate(centerFarRadius / farLimit);
                    float3 farColor = farAcc / farWeight;
                    result = lerp(result, farColor, farBlend);
                }

                if (nearWeight > 1e-4)
                {
                    float nearBlendFromCenter = saturate(centerNearRadius / nearLimit);
                    float nearBlendFromCoverage = saturate(nearCoverage / max((float)nearSampleCount, 1.0));
                    float nearAlpha = smoothstep(0.0, 1.0, max(nearBlendFromCenter, nearBlendFromCoverage));
                    float3 nearColor = nearAcc / nearWeight;
                    result = lerp(result, nearColor, nearAlpha);
                }

                return float4(result, sourceColor.a);
            }
            ENDHLSL
        }
    }
}
