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
            Blend One Zero

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/MotionVectorsCommon.hlsl"

            #define VIVID_OVERLAY_VISUALIZATION_AUTO 0
            #define VIVID_OVERLAY_VISUALIZATION_COLOR 1
            #define VIVID_OVERLAY_VISUALIZATION_DEPTH 2
            #define VIVID_OVERLAY_VISUALIZATION_MOTION_VECTORS 3
            #define VIVID_OVERLAY_DEPTHMODE_RAW 0
            #define VIVID_OVERLAY_DEPTHMODE_LINEAR01 1
            #define VIVID_OVERLAY_CHANNEL_RGB 0
            #define VIVID_OVERLAY_CHANNEL_RED 1
            #define VIVID_OVERLAY_CHANNEL_GREEN 2
            #define VIVID_OVERLAY_CHANNEL_BLUE 3
            #define VIVID_OVERLAY_CHANNEL_ALPHA 4
            #define VIVID_OVERLAY_MOTION_VECTOR_GRID 64.0
            #define VIVID_OVERLAY_MOTION_VECTOR_MIN_PIXELS 0.0
            #define VIVID_OVERLAY_MOTION_VECTOR_BACKGROUND_MIN_INTENSITY 0.03
            #define VIVID_OVERLAY_MOTION_VECTOR_BACKGROUND_MAX_INTENSITY 0.50
            #define VIVID_OVERLAY_MOTION_VECTOR_MAX_SPEED (60.0 / 0.15)
            #define VIVID_OVERLAY_MOTION_VECTOR_ARROW_LINE_WIDTH 2.0
            #define VIVID_OVERLAY_MOTION_VECTOR_ARROW_ANTIALIAS 1.0

            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);
            TEXTURE2D(_DebugTexture);
            SAMPLER(sampler_DebugTexture);
            TEXTURE2D_ARRAY(_DebugTextureArray);
            SAMPLER(sampler_DebugTextureArray);

            float4 _SourceTextureScaleBias;
            float4 _DebugTextureScaleBias;
            float4 _OverlayRect;
            float4 _OverlayScreenSize;
            int _DebugTextureAvailable;
            int _DebugTextureIsArray;
            int _DebugSlice;
            int _VisualizationMode;
            int _DepthMode;
            int _DebugChannelMode;
            float _DebugExposure;
            float _DebugOpacity;

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

            float4 ApplyDebugChannelMode(float4 sampleColor, float exposureMultiplier)
            {
                if (_DebugChannelMode == VIVID_OVERLAY_CHANNEL_RED)
                    return float4(sampleColor.rrr * exposureMultiplier, 1.0);

                if (_DebugChannelMode == VIVID_OVERLAY_CHANNEL_GREEN)
                    return float4(sampleColor.ggg * exposureMultiplier, 1.0);

                if (_DebugChannelMode == VIVID_OVERLAY_CHANNEL_BLUE)
                    return float4(sampleColor.bbb * exposureMultiplier, 1.0);

                if (_DebugChannelMode == VIVID_OVERLAY_CHANNEL_ALPHA)
                    return float4(sampleColor.aaa * exposureMultiplier, 1.0);

                return float4(sampleColor.rgb * exposureMultiplier, 1.0);
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

                return ApplyDebugChannelMode(sampleColor, exposureMultiplier);
            }

            float4 SampleDebugTextureRaw(float2 uv)
            {
                float2 debugUv = ApplyScaleBias(uv, _DebugTextureScaleBias);
                return _DebugTextureIsArray != 0
                    ? SAMPLE_TEXTURE2D_ARRAY(_DebugTextureArray, sampler_DebugTextureArray, debugUv, (float)_DebugSlice)
                    : SAMPLE_TEXTURE2D(_DebugTexture, sampler_DebugTexture, debugUv);
            }

            float4 SampleDebugTextureRawPoint(float2 uv)
            {
                float2 debugUv = ApplyScaleBias(uv, _DebugTextureScaleBias);
                return _DebugTextureIsArray != 0
                    ? SAMPLE_TEXTURE2D_ARRAY(_DebugTextureArray, sampler_PointClamp, debugUv, (float)_DebugSlice)
                    : SAMPLE_TEXTURE2D(_DebugTexture, sampler_PointClamp, debugUv);
            }

            float2 SampleMotionVectors(float2 overlayUv)
            {
                float2 motionVectorNdc;
                DecodeMotionVector(SampleDebugTextureRawPoint(saturate(overlayUv)), motionVectorNdc);
                return motionVectorNdc;
            }

            float DistanceToLine(float2 p, float2 start, float2 end)
            {
                float2 center = (start + end) * 0.5;
                float2 segment = end - start;
                float segmentLength = max(length(segment), 1e-5);
                float2 direction = segment / segmentLength;
                float2 relativePoint = p - center;
                return dot(relativePoint, float2(direction.y, -direction.x));
            }

            float DistanceToSegment(float2 p, float2 start, float2 end)
            {
                float2 center = (start + end) * 0.5;
                float2 segment = end - start;
                float segmentLength = max(length(segment), 1e-5);
                float2 direction = segment / segmentLength;
                float2 relativePoint = p - center;
                float distanceToLine = abs(dot(relativePoint, float2(direction.y, -direction.x)));
                float distancePastSegment = abs(dot(relativePoint, direction)) - 0.5 * segmentLength;
                return max(distanceToLine, distancePastSegment);
            }

            float DrawMotionVectorArrow(
                float2 texcoord,
                float body,
                float head,
                float height,
                float lineWidth,
                float antialias)
            {
                float2 start = -float2(body * 0.5, 0.0);
                float2 end = float2(body * 0.5, 0.0);

                float headLeft = DistanceToLine(texcoord, end, end - head * float2(1.0, -height));
                float headRight = DistanceToLine(texcoord, end - head * float2(1.0, height), end);
                float headBase = texcoord.x - end.x + head;
                float shaft = DistanceToSegment(texcoord, start, end - float2(lineWidth, 0.0));
                float distance = min(max(max(headLeft, headRight), -headBase), shaft);
                return distance / max(antialias, 1e-5);
            }

            float2 ResolveMotionVectorGridSize(float2 overlayPixelSize)
            {
                float aspect = overlayPixelSize.y / max(overlayPixelSize.x, 1.0);
                return float2(
                    VIVID_OVERLAY_MOTION_VECTOR_GRID,
                    max(1.0, floor(VIVID_OVERLAY_MOTION_VECTOR_GRID * aspect)));
            }

            float3 EvaluateMotionVectorBackgroundColor(float2 motionVector, float2 debugPixelSize)
            {
                if (length(motionVector * debugPixelSize) < VIVID_OVERLAY_MOTION_VECTOR_MIN_PIXELS)
                    return float3(0.0, 0.0, 0.0);

                float phi = atan2(motionVector.x, motionVector.y);
                float hue = (phi / PI + 1.0) * 0.5;
                float red = abs(hue * 6.0 - 3.0) - 1.0;
                float green = 2.0 - abs(hue * 6.0 - 2.0);
                float blue = 2.0 - abs(hue * 6.0 - 4.0);

                float absoluteLength = saturate(length(motionVector.xy) * VIVID_OVERLAY_MOTION_VECTOR_MAX_SPEED);
                float3 color = float3(red, green, blue) * lerp(
                    VIVID_OVERLAY_MOTION_VECTOR_BACKGROUND_MIN_INTENSITY,
                    VIVID_OVERLAY_MOTION_VECTOR_BACKGROUND_MAX_INTENSITY,
                    absoluteLength);
                color = saturate(color);

                if (!any(motionVector))
                    color = float3(0.0, 0.0, 0.0);

                return color;
            }

            float EvaluateMotionVectorArrow(float2 overlayUv, float2 overlayPixelSize)
            {
                float2 gridSize = ResolveMotionVectorGridSize(overlayPixelSize);
                float2 cellSize = overlayPixelSize / gridSize;
                float2 positionSS = saturate(overlayUv) * overlayPixelSize;
                float2 cellCenter = (floor(positionSS / cellSize) + 0.5) * cellSize;
                float2 cellCenterUv = saturate(cellCenter / overlayPixelSize);
                positionSS -= cellCenter;

                float2 arrowMotionVector = 0.0;
                UNITY_UNROLL
                for (int y = -1; y <= 1; ++y)
                {
                    UNITY_UNROLL
                    for (int x = -1; x <= 1; ++x)
                    {
                        float2 sampleUv = saturate(cellCenterUv + float2(x, y) * _OverlayScreenSize.zw);
                        arrowMotionVector += SampleMotionVectors(sampleUv);
                    }
                }
                arrowMotionVector /= 9.0;
                arrowMotionVector.y *= -1.0;

                if (!any(arrowMotionVector))
                    return 0.0;

                arrowMotionVector = normalize(arrowMotionVector);
                float2x2 rotation = float2x2(
                    arrowMotionVector.x,
                    -arrowMotionVector.y,
                    arrowMotionVector.y,
                    arrowMotionVector.x);
                positionSS = mul(rotation, positionSS);

                float body = min(cellSize.x, cellSize.y) / sqrt(2.0);
                float distance = DrawMotionVectorArrow(
                    positionSS,
                    body,
                    0.25 * body,
                    0.5,
                    VIVID_OVERLAY_MOTION_VECTOR_ARROW_LINE_WIDTH,
                    VIVID_OVERLAY_MOTION_VECTOR_ARROW_ANTIALIAS);
                return 1.0 - saturate(distance);
            }

            float4 EvaluateMotionVectorDebug(float2 overlayUv)
            {
                float2 overlayPixelSize = max(_OverlayScreenSize.xy * _OverlayRect.zw, float2(1.0, 1.0));
                float2 motionVector = SampleMotionVectors(overlayUv);
                float2 debugPixelSize = max(_OverlayScreenSize.xy, float2(1.0, 1.0));
                float3 color = EvaluateMotionVectorBackgroundColor(motionVector, debugPixelSize);
                float arrow = EvaluateMotionVectorArrow(overlayUv, overlayPixelSize);
                color += float3(arrow, arrow, arrow);
                return float4(color * exp2(_DebugExposure), 1.0);
            }

            float4 SampleDebugTexture(float2 uv)
            {
                if (_VisualizationMode == VIVID_OVERLAY_VISUALIZATION_MOTION_VECTORS)
                    return EvaluateMotionVectorDebug(uv);

                float4 sampleColor = SampleDebugTextureRaw(uv);
                return EvaluateDebugColor(sampleColor);
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
                float4 debugColor = SampleDebugTexture(overlayUv);
                return lerp(sourceColor, debugColor, saturate(_DebugOpacity));
            }
            ENDHLSL
        }
    }
}
