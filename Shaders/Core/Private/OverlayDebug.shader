Shader "Hidden/VividRP/OverlayDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "OverlayDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl"

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

            #define VIVID_OVERLAY_VISUALIZATION_AUTO 0
            #define VIVID_OVERLAY_VISUALIZATION_COLOR 1
            #define VIVID_OVERLAY_VISUALIZATION_DEPTH 2
            #define VIVID_OVERLAY_VISUALIZATION_MOTION_VECTORS 3
            #define VIVID_OVERLAY_VISUALIZATION_VISIBILITY_BUFFER 4
            #define VIVID_OVERLAY_VISUALIZATION_AUTO_EXPOSURE 5
            #define VIVID_OVERLAY_DEPTHMODE_RAW 0
            #define VIVID_OVERLAY_DEPTHMODE_LINEAR01 1
            #define VIVID_OVERLAY_MOTION_VECTOR_ARROW_SPACING 28.0
            #define VIVID_OVERLAY_MOTION_VECTOR_ARROW_MIN_LENGTH 1.5
            #define VIVID_OVERLAY_MOTION_VECTOR_ARROW_OUTLINE_THICKNESS 1.75
            #define VIVID_OVERLAY_MOTION_VECTOR_ARROW_CORE_THICKNESS 0.85
            #define VIVID_OVERLAY_AUTO_EXPOSURE_HISTOGRAM_BUCKET_COUNT 64
            #define VIVID_OVERLAY_AUTO_EXPOSURE_EPSILON 1e-4

            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);
            TEXTURE2D(_DebugTexture);
            SAMPLER(sampler_DebugTexture);
            TEXTURE2D_ARRAY(_DebugTextureArray);
            SAMPLER(sampler_DebugTextureArray);
            TYPED_TEXTURE2D(float2, _DebugVisibilityTexture);
            StructuredBuffer<uint> _AutoExposureHistogramBuffer;
            StructuredBuffer<float4> _AutoExposureCurrentExposureBuffer;

            float4 _SourceTextureScaleBias;
            float4 _DebugTextureScaleBias;
            float4 _OverlayRect;
            float4 _OverlayScreenSize;
            int _DebugTextureAvailable;
            int _DebugTextureIsArray;
            int _DebugSlice;
            int _VisualizationMode;
            int _DepthMode;
            float _DebugExposure;
            float _DebugOpacity;
            float4 _AutoExposureDebugState;
            float4 _AutoExposureHistogramTransform;
            float4 _AutoExposureRangeParams;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float2 ApplyScaleBias(float2 uv, float4 scaleBias)
            {
                return uv * scaleBias.xy + scaleBias.zw;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 EvaluateDebugColor(float4 sampleColor)
            {
                float exposureMultiplier = exp2(_DebugExposure);

                if (_VisualizationMode == VIVID_OVERLAY_VISUALIZATION_DEPTH)
                {
                    float depthValue = sampleColor.r;
                    if (_DepthMode == VIVID_OVERLAY_DEPTHMODE_LINEAR01)
                        depthValue = Linear01Depth(depthValue, _ZBufferParams);

                    return float4(depthValue.xxx * exposureMultiplier, 1.0);
                }

                if (_VisualizationMode == VIVID_OVERLAY_VISUALIZATION_MOTION_VECTORS)
                {
                    float2 motion = sampleColor.xy;
                    float magnitude = saturate(length(motion) * 8.0);
                    return float4((float3(motion * 0.5 + 0.5, magnitude)) * exposureMultiplier, 1.0);
                }

                return float4(sampleColor.rgb * exposureMultiplier, 1.0);
            }

            float4 SampleDebugTextureRaw(float2 uv)
            {
                float2 debugUv = ApplyScaleBias(uv, _DebugTextureScaleBias);
                return _DebugTextureIsArray != 0
                    ? SAMPLE_TEXTURE2D_ARRAY(_DebugTextureArray, sampler_DebugTextureArray, debugUv, (float)_DebugSlice)
                    : SAMPLE_TEXTURE2D(_DebugTexture, sampler_DebugTexture, debugUv);
            }

            float DistanceToSegment(float2 _point, float2 a, float2 b)
            {
                float2 segment = b - a;
                float segmentLengthSq = max(dot(segment, segment), 1e-5);
                float factor = saturate(dot(_point - a, segment) / segmentLengthSq);
                return length(_point - (a + segment * factor));
            }

            float2 ResolveMotionVectorGridCount(float2 overlayPixelSize)
            {
                float2 minGridCount = float2(1.0, 1.0);
                float2 minOverlayPixelSize = float2(1.0, 1.0);
                float2 arrowSpacing = float2(
                    VIVID_OVERLAY_MOTION_VECTOR_ARROW_SPACING,
                    VIVID_OVERLAY_MOTION_VECTOR_ARROW_SPACING);
                return max(minGridCount, floor(max(overlayPixelSize, minOverlayPixelSize) / arrowSpacing));
            }

            float2 ResolveMotionVectorCellCenterUv(float2 overlayUv, float2 gridCount)
            {
                float2 cellIndex = min(gridCount - 1.0, floor(saturate(overlayUv) * gridCount));
                return (cellIndex + 0.5) / gridCount;
            }

            float3 EvaluateMotionVectorColor(float2 motion)
            {
                float exposureMultiplier = exp2(_DebugExposure);
                float magnitude = saturate(length(motion) * 8.0);
                return float3(motion * 0.5 + 0.5, magnitude) * exposureMultiplier;
            }

            float3 OverlayMotionVectorArrows(float2 overlayUv, float3 baseColor)
            {
                float2 overlayPixelSize = max(_OverlayScreenSize.xy * _OverlayRect.zw, float2(1.0, 1.0));
                float2 gridCount = ResolveMotionVectorGridCount(overlayPixelSize);
                float2 cellCenterUv = ResolveMotionVectorCellCenterUv(overlayUv, gridCount);
                float2 cellSize = overlayPixelSize / gridCount;
                float2 localCellPosition = (overlayUv - cellCenterUv) * overlayPixelSize;
                float2 motionPixels = SampleDebugTextureRaw(cellCenterUv).xy * overlayPixelSize;
                float motionPixelsLength = length(motionPixels);

                if (motionPixelsLength < VIVID_OVERLAY_MOTION_VECTOR_ARROW_MIN_LENGTH)
                    return baseColor;

                float maxArrowLength = max(0.0, min(cellSize.x, cellSize.y) * 0.5 - 2.0);
                float arrowLength = min(motionPixelsLength, maxArrowLength);
                if (arrowLength <= 0.0)
                    return baseColor;

                float2 direction = motionPixels / motionPixelsLength;
                float2 tangent = float2(-direction.y, direction.x);

                float headLength = clamp(arrowLength * 0.35, 4.0, 10.0);
                float headWidth = headLength * 0.5;
                float shaftLength = max(arrowLength - headLength, 2.0);

                float2 tail = -direction * shaftLength * 0.35;
                float2 headBase = tail + direction * shaftLength;
                float2 tip = headBase + direction * headLength;
                float2 headLeft = headBase + tangent * headWidth;
                float2 headRight = headBase - tangent * headWidth;

                float shaftDistance = DistanceToSegment(localCellPosition, tail, headBase);
                float headLeftDistance = DistanceToSegment(localCellPosition, headLeft, tip);
                float headRightDistance = DistanceToSegment(localCellPosition, headRight, tip);
                float arrowDistance = min(shaftDistance, min(headLeftDistance, headRightDistance));

                float outlineCoverage = 1.0 - smoothstep(
                    VIVID_OVERLAY_MOTION_VECTOR_ARROW_OUTLINE_THICKNESS - 1.0,
                    VIVID_OVERLAY_MOTION_VECTOR_ARROW_OUTLINE_THICKNESS + 1.0,
                    arrowDistance);
                float fillCoverage = 1.0 - smoothstep(
                    VIVID_OVERLAY_MOTION_VECTOR_ARROW_CORE_THICKNESS - 1.0,
                    VIVID_OVERLAY_MOTION_VECTOR_ARROW_CORE_THICKNESS + 1.0,
                    arrowDistance);

                float3 outlinedColor = lerp(baseColor, float3(0.0, 0.0, 0.0), outlineCoverage);
                return lerp(outlinedColor, float3(1.0, 1.0, 1.0), fillCoverage);
            }

            float3 HashColor(uint seed)
            {
                float seedValue = (float) (seed + 1u);
                float3 value = float3(seedValue, seedValue + 17.0, seedValue + 37.0);
                value = frac(sin(value * float3(12.9898, 78.233, 39.425)) * 43758.5453);
                return saturate(0.25 + value * 0.75);
            }

            float4 EvaluateVisibilityBufferColor(float2 uv)
            {
                float exposureMultiplier = exp2(_DebugExposure);
                float2 debugUv = ApplyScaleBias(uv, _DebugTextureScaleBias);
                uint2 packedValue = asuint(SAMPLE_TEXTURE2D_LOD(_DebugVisibilityTexture, sampler_PointClamp, debugUv, 0).xy);
                if (!IsPackedVisibilityBufferValueValid(packedValue))
                    return 0.0f.xxxx;

                VividVisibilityBufferValue value = UnpackVisibilityBufferValue(packedValue);
                uint triangleID = value.IndexID / 3u;

                float3 instanceColor = HashColor(value.InstanceID);
                float3 meshletColor = HashColor(value.MeshletID * 31u + 7u);
                float3 triangleColor = HashColor(triangleID * 17u + 13u);
                float3 color = lerp(instanceColor, meshletColor, 0.5);
                color = lerp(color, triangleColor, 0.25);
                return float4(color * exposureMultiplier, 1.0);
            }

            float4 SampleDebugTexture(float2 uv)
            {
                if (_VisualizationMode == VIVID_OVERLAY_VISUALIZATION_VISIBILITY_BUFFER)
                    return EvaluateVisibilityBufferColor(uv);

                float4 sampleColor = SampleDebugTextureRaw(uv);

                if (_VisualizationMode == VIVID_OVERLAY_VISUALIZATION_MOTION_VECTORS)
                    return float4(OverlayMotionVectorArrows(uv, EvaluateMotionVectorColor(sampleColor.xy)), 1.0);

                return EvaluateDebugColor(sampleColor);
            }

            float ResolveAutoExposureHistogramPositionFromLogLuminance(float logLuminance)
            {
                return saturate(logLuminance * _AutoExposureHistogramTransform.x + _AutoExposureHistogramTransform.y);
            }

            float ResolveAutoExposureHistogramPositionFromLuminance(float luminance)
            {
                float resolvedLuminance = max(luminance, max(_AutoExposureHistogramTransform.z, VIVID_OVERLAY_AUTO_EXPOSURE_EPSILON));
                return ResolveAutoExposureHistogramPositionFromLogLuminance(log2(resolvedLuminance));
            }

            float AutoExposureLineMask(float value, float center, float thickness)
            {
                return 1.0 - smoothstep(thickness, thickness + 0.004, abs(value - center));
            }

            float AutoExposureRectMask(float2 uv, float2 rectMin, float2 rectMax)
            {
                return step(rectMin.x, uv.x)
                    * step(rectMin.y, uv.y)
                    * step(uv.x, rectMax.x)
                    * step(uv.y, rectMax.y);
            }

            float ResolveAutoExposureGaugePosition(float exposureScale)
            {
                const float gaugeLogRange = 12.0;
                float logExposureScale = log2(max(exposureScale, VIVID_OVERLAY_AUTO_EXPOSURE_EPSILON));
                return saturate(0.5 + logExposureScale / gaugeLogRange);
            }

            struct AutoExposureDebugSummary
            {
                float histogramSum;
                float histogramMax;
                float lowPercentilePosition;
                float highPercentilePosition;
                float averageSceneLuminancePosition;
                float minClampPosition;
                float maxClampPosition;
                float currentExposureScale;
                float targetExposureScale;
                float averageSceneLuminance;
                float middleGreyCompensation;
            };

            AutoExposureDebugSummary SummarizeAutoExposureDebug()
            {
                AutoExposureDebugSummary summary;
                summary.histogramSum = 0.0;
                summary.histogramMax = 0.0;
                summary.lowPercentilePosition = 0.0;
                summary.highPercentilePosition = 1.0;

                float4 exposureState = _AutoExposureCurrentExposureBuffer[0];
                summary.currentExposureScale = max(exposureState.x, VIVID_OVERLAY_AUTO_EXPOSURE_EPSILON);
                summary.targetExposureScale = max(exposureState.y, VIVID_OVERLAY_AUTO_EXPOSURE_EPSILON);
                summary.averageSceneLuminance = max(exposureState.z, VIVID_OVERLAY_AUTO_EXPOSURE_EPSILON);
                summary.middleGreyCompensation = max(exposureState.w, VIVID_OVERLAY_AUTO_EXPOSURE_EPSILON);

                [unroll]
                for (uint bucketIndex = 0; bucketIndex < VIVID_OVERLAY_AUTO_EXPOSURE_HISTOGRAM_BUCKET_COUNT; ++bucketIndex)
                {
                    float bucketValue = (float)_AutoExposureHistogramBuffer[bucketIndex];
                    summary.histogramSum += bucketValue;
                    summary.histogramMax = max(summary.histogramMax, bucketValue);
                }

                if (summary.histogramSum > VIVID_OVERLAY_AUTO_EXPOSURE_EPSILON)
                {
                    float lowThreshold = summary.histogramSum * saturate(_AutoExposureRangeParams.z);
                    float highThreshold = summary.histogramSum * saturate(_AutoExposureRangeParams.w);
                    float cumulative = 0.0;
                    bool foundLow = false;
                    bool foundHigh = false;

                    [unroll]
                    for (uint bucketIndex = 0; bucketIndex < VIVID_OVERLAY_AUTO_EXPOSURE_HISTOGRAM_BUCKET_COUNT; ++bucketIndex)
                    {
                        cumulative += (float)_AutoExposureHistogramBuffer[bucketIndex];
                        float bucketPosition = (float)bucketIndex / (float)(VIVID_OVERLAY_AUTO_EXPOSURE_HISTOGRAM_BUCKET_COUNT - 1);

                        if (!foundLow && cumulative >= lowThreshold)
                        {
                            summary.lowPercentilePosition = bucketPosition;
                            foundLow = true;
                        }

                        if (!foundHigh && cumulative >= highThreshold)
                        {
                            summary.highPercentilePosition = bucketPosition;
                            foundHigh = true;
                        }
                    }
                }

                summary.averageSceneLuminancePosition = ResolveAutoExposureHistogramPositionFromLuminance(summary.averageSceneLuminance);
                summary.minClampPosition = ResolveAutoExposureHistogramPositionFromLuminance(_AutoExposureRangeParams.x);
                summary.maxClampPosition = ResolveAutoExposureHistogramPositionFromLuminance(_AutoExposureRangeParams.y);
                return summary;
            }

            float ResolveAutoExposureHistogramHeight(float histogramPosition, float histogramMax)
            {
                uint bucketIndex = (uint)min(
                    floor(saturate(histogramPosition) * (float)(VIVID_OVERLAY_AUTO_EXPOSURE_HISTOGRAM_BUCKET_COUNT - 1) + 0.5),
                    (float)(VIVID_OVERLAY_AUTO_EXPOSURE_HISTOGRAM_BUCKET_COUNT - 1));
                float bucketValue = (float)_AutoExposureHistogramBuffer[bucketIndex];
                float normalizedValue = bucketValue / max(histogramMax, 1.0);
                return pow(saturate(normalizedValue), 0.35);
            }

            float3 EvaluateAutoExposureDebugOverlay(float2 overlayUv, float3 sourceColor)
            {
                AutoExposureDebugSummary summary = SummarizeAutoExposureDebug();
                float exposureEnabled = _AutoExposureDebugState.x;
                float histogramMode = _AutoExposureDebugState.y;
                float histogramAvailable = _AutoExposureDebugState.z;
                float hasHistory = _AutoExposureDebugState.w;

                float3 accentColor = histogramAvailable > 0.5
                    ? float3(0.20, 0.90, 0.48)
                    : exposureEnabled > 0.5
                        ? (histogramMode > 0.5 ? float3(0.95, 0.66, 0.18) : float3(0.30, 0.55, 1.00))
                        : float3(0.95, 0.24, 0.30);
                float3 panelColor = lerp(sourceColor, float3(0.04, 0.05, 0.07), 0.9);
                float3 color = panelColor;

                float headerMask = AutoExposureRectMask(overlayUv, float2(0.05, 0.90), float2(0.95, 0.97));
                color = lerp(color, accentColor * 0.65 + float3(0.05, 0.05, 0.06), headerMask * 0.95);

                float2 histogramMin = float2(0.07, 0.22);
                float2 histogramMax = float2(0.93, 0.78);
                float histogramMask = AutoExposureRectMask(overlayUv, histogramMin, histogramMax);
                float2 histogramUv = saturate((overlayUv - histogramMin) / max(histogramMax - histogramMin, float2(1e-5, 1e-5)));

                float clampMin = min(summary.minClampPosition, summary.maxClampPosition);
                float clampMax = max(summary.minClampPosition, summary.maxClampPosition);
                float outsideClamp = histogramMask * ((1.0 - step(clampMin, histogramUv.x)) + step(clampMax + 1e-4, histogramUv.x));
                color = lerp(color, color * 0.6 + float3(0.09, 0.03, 0.05), saturate(outsideClamp) * 0.7);

                float gridMask = histogramMask * max(
                    max(AutoExposureLineMask(histogramUv.x, 0.25, 0.002), AutoExposureLineMask(histogramUv.x, 0.5, 0.002)),
                    max(
                        max(AutoExposureLineMask(histogramUv.x, 0.75, 0.002), AutoExposureLineMask(histogramUv.y, 0.25, 0.002)),
                        max(AutoExposureLineMask(histogramUv.y, 0.5, 0.002), AutoExposureLineMask(histogramUv.y, 0.75, 0.002))));
                color = lerp(color, float3(0.13, 0.14, 0.17), gridMask * 0.6);

                if (histogramAvailable > 0.5 && summary.histogramSum > VIVID_OVERLAY_AUTO_EXPOSURE_EPSILON)
                {
                    float histogramHeight = ResolveAutoExposureHistogramHeight(histogramUv.x, summary.histogramMax);
                    float histogramFill = histogramMask * (1.0 - smoothstep(histogramHeight - 0.012, histogramHeight + 0.012, histogramUv.y));
                    float histogramTop = histogramMask * AutoExposureLineMask(histogramUv.y, histogramHeight, 0.008);
                    color = lerp(color, float3(0.10, 0.18, 0.12), histogramFill * 0.55);
                    color = lerp(color, float3(0.28, 1.00, 0.54), histogramTop * 0.9);
                }
                else
                {
                    float stripe = 0.5 + 0.5 * sin((histogramUv.x + histogramUv.y) * 42.0);
                    float stripeMask = histogramMask * smoothstep(0.45, 0.55, stripe);
                    color = lerp(color, accentColor * 0.35 + float3(0.08, 0.08, 0.10), stripeMask * 0.65);
                }

                float minLine = histogramMask * AutoExposureLineMask(histogramUv.x, clampMin, 0.005);
                float maxLine = histogramMask * AutoExposureLineMask(histogramUv.x, clampMax, 0.005);
                float lowLine = histogramMask * AutoExposureLineMask(histogramUv.x, summary.lowPercentilePosition, 0.004);
                float highLine = histogramMask * AutoExposureLineMask(histogramUv.x, summary.highPercentilePosition, 0.004);
                float averageLine = histogramMask * AutoExposureLineMask(histogramUv.x, summary.averageSceneLuminancePosition, 0.006);
                color = lerp(color, float3(0.75, 0.78, 0.85), minLine * 0.9);
                color = lerp(color, float3(0.95, 0.96, 0.98), maxLine * 0.9);
                color = lerp(color, float3(1.00, 0.82, 0.24), lowLine * 0.9);
                color = lerp(color, float3(1.00, 0.37, 0.27), highLine * 0.95);
                color = lerp(color, float3(0.30, 0.85, 1.00), averageLine * 0.95);

                float2 gaugeMin = float2(0.07, 0.10);
                float2 gaugeMax = float2(0.93, 0.19);
                float gaugeMask = AutoExposureRectMask(overlayUv, gaugeMin, gaugeMax);
                float2 gaugeUv = saturate((overlayUv - gaugeMin) / max(gaugeMax - gaugeMin, float2(1e-5, 1e-5)));
                float currentExposureGauge = ResolveAutoExposureGaugePosition(summary.currentExposureScale);
                float targetExposureGauge = ResolveAutoExposureGaugePosition(summary.targetExposureScale);
                float currentRow = gaugeMask * (1.0 - step(0.45, gaugeUv.y));
                float targetRow = gaugeMask * step(0.55, gaugeUv.y);
                float neutralLine = gaugeMask * AutoExposureLineMask(gaugeUv.x, 0.5, 0.004);
                float currentSpan = currentRow
                    * step(min(0.5, currentExposureGauge), gaugeUv.x)
                    * step(gaugeUv.x, max(0.5, currentExposureGauge));
                float targetSpan = targetRow
                    * step(min(0.5, targetExposureGauge), gaugeUv.x)
                    * step(gaugeUv.x, max(0.5, targetExposureGauge));
                float currentMarker = currentRow * AutoExposureLineMask(gaugeUv.x, currentExposureGauge, 0.01);
                float targetMarker = targetRow * AutoExposureLineMask(gaugeUv.x, targetExposureGauge, 0.01);
                color = lerp(color, float3(0.08, 0.09, 0.11), gaugeMask * 0.9);
                color = lerp(color, float3(0.85, 0.87, 0.90), neutralLine * 0.9);
                color = lerp(color, float3(0.18, 0.82, 1.00), currentSpan * 0.45);
                color = lerp(color, float3(0.20, 0.92, 1.00), currentMarker * 0.95);
                color = lerp(color, float3(1.00, 0.64, 0.22), targetSpan * 0.45);
                color = lerp(color, float3(1.00, 0.78, 0.28), targetMarker * 0.95);

                float2 statusCellSize = float2(0.13, 0.06);
                float2 statusOrigin = float2(0.07, 0.82);
                float4 stateValues = float4(exposureEnabled, histogramMode, histogramAvailable, hasHistory);
                float3 stateColors[4] =
                {
                    float3(0.95, 0.26, 0.32),
                    float3(0.30, 0.55, 1.00),
                    float3(0.18, 0.90, 0.48),
                    float3(0.86, 0.32, 1.00),
                };

                [unroll]
                for (int stateIndex = 0; stateIndex < 4; ++stateIndex)
                {
                    float2 cellMin = statusOrigin + float2((statusCellSize.x + 0.02) * stateIndex, 0.0);
                    float2 cellMax = cellMin + statusCellSize;
                    float cellMask = AutoExposureRectMask(overlayUv, cellMin, cellMax);
                    float stateValue = stateValues[stateIndex];
                    float3 stateColor = lerp(float3(0.16, 0.17, 0.20), stateColors[stateIndex], saturate(stateValue));
                    color = lerp(color, stateColor, cellMask * 0.95);
                }

                float frameMask = max(
                    AutoExposureLineMask(overlayUv.x, 0.05, 0.004) * step(0.05, overlayUv.y) * step(overlayUv.y, 0.97),
                    max(
                        AutoExposureLineMask(overlayUv.x, 0.95, 0.004) * step(0.05, overlayUv.y) * step(overlayUv.y, 0.97),
                        max(
                            AutoExposureLineMask(overlayUv.y, 0.05, 0.004) * step(0.05, overlayUv.x) * step(overlayUv.x, 0.95),
                            AutoExposureLineMask(overlayUv.y, 0.97, 0.004) * step(0.05, overlayUv.x) * step(overlayUv.x, 0.95))));
                color = lerp(color, float3(0.96, 0.97, 0.98), frameMask * 0.9);
                return color;
            }

            bool IsInsideOverlay(float2 uv, float2 overlayMin, float2 overlayMax)
            {
                return all(uv >= overlayMin) && all(uv <= overlayMax);
            }

            bool IsOverlayBorder(float2 uv, float2 overlayMin, float2 overlayMax)
            {
                float2 borderThickness = _OverlayScreenSize.zw * 2.0;
                float2 distanceToMin = uv - overlayMin;
                float2 distanceToMax = overlayMax - uv;

                return any(distanceToMin <= borderThickness)
                    || any(distanceToMax <= borderThickness);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 sourceUv = ApplyScaleBias(input.uv, _SourceTextureScaleBias);
                float4 sourceColor = SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, sourceUv);

                if (_DebugTextureAvailable == 0)
                    return sourceColor;

                float2 overlayMin = _OverlayRect.xy;
                float2 overlayMax = overlayMin + _OverlayRect.zw;
                if (!IsInsideOverlay(input.uv, overlayMin, overlayMax))
                    return sourceColor;

                if (all(_OverlayRect.zw < 0.999) && IsOverlayBorder(input.uv, overlayMin, overlayMax))
                    return float4(1.0, 1.0, 1.0, 1.0);

                float2 overlayUv = saturate((input.uv - overlayMin) / max(_OverlayRect.zw, float2(1e-5, 1e-5)));
                float4 debugColor = _VisualizationMode == VIVID_OVERLAY_VISUALIZATION_AUTO_EXPOSURE
                    ? float4(EvaluateAutoExposureDebugOverlay(overlayUv, sourceColor.rgb), 1.0)
                    : SampleDebugTexture(overlayUv);
                return lerp(sourceColor, debugColor, saturate(_DebugOpacity));
            }
            ENDHLSL
        }
    }
}
