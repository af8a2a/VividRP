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
            #define VIVID_OVERLAY_MOTION_VECTOR_ARROW_SPACING 28.0
            #define VIVID_OVERLAY_MOTION_VECTOR_ARROW_MIN_LENGTH 1.5
            #define VIVID_OVERLAY_MOTION_VECTOR_ARROW_OUTLINE_THICKNESS 1.75
            #define VIVID_OVERLAY_MOTION_VECTOR_ARROW_CORE_THICKNESS 0.85

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

                if (_VisualizationMode == VIVID_OVERLAY_VISUALIZATION_MOTION_VECTORS)
                {
                    float2 motion = sampleColor.xy;
                    float magnitude = saturate(length(motion) * 8.0);
                    return float4((float3(motion * 0.5 + 0.5, magnitude)) * exposureMultiplier, 1.0);
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

            float4 SampleDebugTexture(float2 uv)
            {
                float4 sampleColor = SampleDebugTextureRaw(uv);

                if (_VisualizationMode == VIVID_OVERLAY_VISUALIZATION_MOTION_VECTORS)
                {
                    float2 motionVector;
                    DecodeMotionVector(sampleColor, motionVector);
                    return float4(OverlayMotionVectorArrows(uv, EvaluateMotionVectorColor(motionVector)), 1.0);
                }

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
