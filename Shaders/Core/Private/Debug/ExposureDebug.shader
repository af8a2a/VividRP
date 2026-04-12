Shader "Hidden/VividRP/Debug/Exposure"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        HLSLINCLUDE
        #pragma target 4.5
        #pragma vertex Vert

        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ACES.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
        #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl"

        #define VIVID_EXPOSURE_EPSILON 1e-4
        #define VIVID_EXPOSURE_HISTOGRAM_BINS 64
        #define VIVID_SMALL_FONT_WIDTH 5
        #define VIVID_SMALL_FONT_HEIGHT 7
        #define VIVID_SMALL_FONT_SPACING 6

        #define VIVID_TONEMAP_NONE 0
        #define VIVID_TONEMAP_NEUTRAL 1
        #define VIVID_TONEMAP_ACES_APPROX 2
        #define VIVID_TONEMAP_ACES_FULL 3
        #define VIVID_TONEMAP_GRAN_TURISMO 4
        #define VIVID_TONEMAP_AGX 5
        #define VIVID_TONEMAP_KHRONOS_PBR 6
        #define VIVID_TONEMAP_CUSTOM 7
        #define VIVID_TONEMAP_EXTERNAL 8

        #define VIVID_METERING_AVERAGE 0
        #define VIVID_METERING_SPOT 1
        #define VIVID_METERING_CENTER_WEIGHTED 2
        #define VIVID_METERING_MASK_WEIGHTED 3

        TEXTURE2D(_SourceTexture);
        SAMPLER(sampler_SourceTexture);
        TEXTURE2D(_AutoExposureMeterMask);
        SAMPLER(sampler_AutoExposureMeterMask);
        TEXTURE3D(_LogLut3D);
        SAMPLER(sampler_LogLut3D);

        StructuredBuffer<uint> _AutoExposureHistogramBuffer;
        StructuredBuffer<float4> _AutoExposureCurrentExposureBuffer;

        float4 _SourceTextureScaleBias;
        float4 _ExposureDebugState;
        float4 _ExposureDebugViewParams;
        float4 _ExposureDebugRangeParams;
        float4 _ExposureDebugHistogramTransform;
        float4 _ExposureDebugMeteringParams;
        float4 _MousePixelCoord;
        float4 _LogLut3D_Params;
        float4 _CustomToneCurve;
        float4 _ToeSegmentA;
        float4 _ToeSegmentB;
        float4 _MidSegmentA;
        float4 _MidSegmentB;
        float4 _ShoSegmentA;
        float4 _ShoSegmentB;
        int _DebugTonemapMode;

        struct Attributes
        {
            uint vertexID : SV_VertexID;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        struct ExposureDebugSummary
        {
            float histogramMax;
            float histogramSum;
            float lowPercentileBin;
            float highPercentileBin;
            float averageSceneLuminance;
            float averageSceneEV100;
            float currentExposureScale;
            float targetExposureScale;
            float effectiveExposureScale;
            float currentExposureEV100;
            float targetExposureEV100;
            float exposureCompensationStops;
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

        float4 SampleSource(float2 uv)
        {
            return SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, ApplyScaleBias(uv, _SourceTextureScaleBias));
        }

        float3 SampleSourceLinear(float2 uv)
        {
            return max(SampleSource(uv).rgb * VividGetOneOverPreExposure(), 0.0);
        }

        float ResolveAverageSceneEV100FromLuminance(float luminance)
        {
            return log2(max(luminance, VIVID_EXPOSURE_EPSILON) / 0.18);
        }

        float ResolveExposureEV100FromScale(float exposureScale)
        {
            return log2(rcp(max(exposureScale, VIVID_EXPOSURE_EPSILON)));
        }

        float ResolveHistogramBasePositionFromEV100(float ev100)
        {
            return saturate((ev100 - _ExposureDebugHistogramTransform.x) * _ExposureDebugHistogramTransform.z);
        }

        float2 GetHistogramLabelRange(float currentExposureEV100)
        {
            if (_ExposureDebugViewParams.y > 0.5)
            {
                float halfRange = min(0.5 * (_ExposureDebugRangeParams.y - _ExposureDebugRangeParams.x), 10.0);
                return float2(currentExposureEV100 - halfRange, currentExposureEV100 + halfRange);
            }

            return _ExposureDebugRangeParams.xy;
        }

        float EvToUVLocation(float ev100, float currentExposureEV100)
        {
            float2 labelRange = GetHistogramLabelRange(currentExposureEV100);
            return saturate((ev100 - labelRange.x) / max(labelRange.y - labelRange.x, VIVID_EXPOSURE_EPSILON));
        }

        float3 ToHeat(float value)
        {
            float3 ramp = value * 2.1 - float3(1.8, 1.14, 0.3);
            return saturate(1.0 - ramp * ramp);
        }

        float Checker(float2 pixelCoord)
        {
            return fmod(floor(pixelCoord.x) + floor(pixelCoord.y), 2.0);
        }

        float LineMask(float value, float center, float halfWidth)
        {
            return 1.0 - smoothstep(halfWidth, halfWidth + 0.0025, abs(value - center));
        }

        bool SampleMiniGlyphRows(
            int2 localCoord,
            uint row0,
            uint row1,
            uint row2,
            uint row3,
            uint row4,
            uint row5,
            uint row6)
        {
            if (localCoord.x < 0
                || localCoord.y < 0
                || localCoord.x >= VIVID_SMALL_FONT_WIDTH
                || localCoord.y >= VIVID_SMALL_FONT_HEIGHT)
            {
                return false;
            }

            uint bits = 0u;
            switch (localCoord.y)
            {
                case 0: bits = row0; break;
                case 1: bits = row1; break;
                case 2: bits = row2; break;
                case 3: bits = row3; break;
                case 4: bits = row4; break;
                case 5: bits = row5; break;
                default: bits = row6; break;
            }

            return ((bits >> (VIVID_SMALL_FONT_WIDTH - 1 - localCoord.x)) & 1u) != 0u;
        }

        bool SampleMiniGlyph(int2 localCoord, uint ascii)
        {
            switch (ascii)
            {
                case ' ' : return false;
                case '.' : return SampleMiniGlyphRows(localCoord, 0, 0, 0, 0, 0, 6, 6);
                case ':' : return SampleMiniGlyphRows(localCoord, 0, 4, 4, 0, 4, 4, 0);
                case '-' : return SampleMiniGlyphRows(localCoord, 0, 0, 0, 31, 0, 0, 0);
                case '0' : return SampleMiniGlyphRows(localCoord, 14, 17, 19, 21, 25, 17, 14);
                case '1' : return SampleMiniGlyphRows(localCoord, 4, 12, 4, 4, 4, 4, 14);
                case '2' : return SampleMiniGlyphRows(localCoord, 14, 17, 1, 2, 4, 8, 31);
                case '3' : return SampleMiniGlyphRows(localCoord, 30, 1, 1, 14, 1, 1, 30);
                case '4' : return SampleMiniGlyphRows(localCoord, 2, 6, 10, 18, 31, 2, 2);
                case '5' : return SampleMiniGlyphRows(localCoord, 31, 16, 30, 1, 1, 17, 14);
                case '6' : return SampleMiniGlyphRows(localCoord, 6, 8, 16, 30, 17, 17, 14);
                case '7' : return SampleMiniGlyphRows(localCoord, 31, 1, 2, 4, 8, 8, 8);
                case '8' : return SampleMiniGlyphRows(localCoord, 14, 17, 17, 14, 17, 17, 14);
                case '9' : return SampleMiniGlyphRows(localCoord, 14, 17, 17, 15, 1, 2, 28);
                case 'A' : return SampleMiniGlyphRows(localCoord, 14, 17, 17, 31, 17, 17, 17);
                case 'C' : return SampleMiniGlyphRows(localCoord, 14, 17, 16, 16, 16, 17, 14);
                case 'E' : return SampleMiniGlyphRows(localCoord, 31, 16, 16, 30, 16, 16, 31);
                case 'N' : return SampleMiniGlyphRows(localCoord, 17, 25, 21, 19, 17, 17, 17);
                case 'T' : return SampleMiniGlyphRows(localCoord, 31, 4, 4, 4, 4, 4, 4);
                case 'V' : return SampleMiniGlyphRows(localCoord, 17, 17, 17, 17, 17, 10, 4);
                case 'X' : return SampleMiniGlyphRows(localCoord, 17, 17, 10, 4, 10, 17, 17);
                case 'a' : return SampleMiniGlyphRows(localCoord, 0, 0, 14, 1, 15, 17, 15);
                case 'c' : return SampleMiniGlyphRows(localCoord, 0, 0, 14, 16, 16, 17, 14);
                case 'e' : return SampleMiniGlyphRows(localCoord, 0, 0, 14, 17, 31, 16, 14);
                case 'g' : return SampleMiniGlyphRows(localCoord, 0, 0, 15, 17, 15, 1, 14);
                case 'h' : return SampleMiniGlyphRows(localCoord, 16, 16, 22, 25, 17, 17, 17);
                case 'i' : return SampleMiniGlyphRows(localCoord, 4, 0, 12, 4, 4, 4, 14);
                case 'm' : return SampleMiniGlyphRows(localCoord, 0, 0, 26, 21, 21, 21, 21);
                case 'n' : return SampleMiniGlyphRows(localCoord, 0, 0, 22, 25, 17, 17, 17);
                case 'o' : return SampleMiniGlyphRows(localCoord, 0, 0, 14, 17, 17, 17, 14);
                case 'p' : return SampleMiniGlyphRows(localCoord, 0, 0, 30, 17, 30, 16, 16);
                case 'r' : return SampleMiniGlyphRows(localCoord, 0, 0, 22, 25, 16, 16, 16);
                case 's' : return SampleMiniGlyphRows(localCoord, 0, 0, 15, 16, 14, 1, 30);
                case 't' : return SampleMiniGlyphRows(localCoord, 4, 4, 31, 4, 4, 5, 2);
                case 'u' : return SampleMiniGlyphRows(localCoord, 0, 0, 17, 17, 17, 19, 13);
                case 'x' : return SampleMiniGlyphRows(localCoord, 0, 0, 17, 10, 4, 10, 17);
                default: return false;
            }
        }

        void DrawMiniCharacter(uint ascii, float3 fontColor, uint2 currentPixelCoord, inout uint2 cursor, inout float3 color)
        {
            int2 localCoord = int2(currentPixelCoord) - int2(cursor);
            if (SampleMiniGlyph(localCoord, ascii))
                color = fontColor;

            cursor.x += VIVID_SMALL_FONT_SPACING;
        }

        uint Pow10(uint digitCount)
        {
            switch (digitCount)
            {
                case 0u: return 1u;
                case 1u: return 10u;
                case 2u: return 100u;
                case 3u: return 1000u;
                case 4u: return 10000u;
                case 5u: return 100000u;
                default: return 1000000u;
            }
        }

        void DrawMiniUnsignedInteger(uint value, float3 fontColor, uint2 currentPixelCoord, inout uint2 cursor, inout float3 color)
        {
            uint divisor = 1u;
            while (value / divisor >= 10u && divisor < 100000000u)
                divisor *= 10u;

            [loop]
            while (divisor > 0u)
            {
                uint digit = (value / divisor) % 10u;
                DrawMiniCharacter('0' + digit, fontColor, currentPixelCoord, cursor, color);
                divisor /= 10u;
            }
        }

        void DrawMiniFixedDigits(uint value, uint digitCount, float3 fontColor, uint2 currentPixelCoord, inout uint2 cursor, inout float3 color)
        {
            uint divisor = Pow10(max(digitCount, 1u) - 1u);

            [loop]
            for (uint index = 0u; index < max(digitCount, 1u); ++index)
            {
                uint digit = divisor > 0u ? (value / divisor) % 10u : 0u;
                DrawMiniCharacter('0' + digit, fontColor, currentPixelCoord, cursor, color);
                divisor = max(divisor / 10u, 0u);
            }
        }

        void DrawMiniFloatExplicitPrecision(float value, float3 fontColor, uint2 currentPixelCoord, uint digitCount, inout uint2 cursor, inout float3 color)
        {
            if (IsNaN(value))
            {
                DrawMiniCharacter('N', fontColor, currentPixelCoord, cursor, color);
                DrawMiniCharacter('a', fontColor, currentPixelCoord, cursor, color);
                DrawMiniCharacter('N', fontColor, currentPixelCoord, cursor, color);
                return;
            }

            float absValue = abs(value);
            if (value < 0.0)
                DrawMiniCharacter('-', fontColor, currentPixelCoord, cursor, color);

            DrawMiniUnsignedInteger((uint)absValue, fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('.', fontColor, currentPixelCoord, cursor, color);

            uint multiplier = Pow10(digitCount);
            uint fracValue = (uint)(frac(absValue) * multiplier);
            DrawMiniFixedDigits(fracValue, digitCount, fontColor, currentPixelCoord, cursor, color);
        }

        void DrawLiteralCurrentExposure(uint2 currentPixelCoord, uint2 position, float3 fontColor, inout float3 color)
        {
            uint2 cursor = position;
            DrawMiniCharacter('C', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('u', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('r', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('r', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('e', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('n', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('t', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter(' ', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('E', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('x', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('p', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('o', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('s', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('u', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('r', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('e', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter(':', fontColor, currentPixelCoord, cursor, color);
        }

        void DrawLiteralTargetExposure(uint2 currentPixelCoord, uint2 position, float3 fontColor, inout float3 color)
        {
            uint2 cursor = position;
            DrawMiniCharacter('T', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('a', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('r', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('g', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('e', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('t', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter(' ', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('E', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('x', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('p', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('o', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('s', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('u', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('r', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('e', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter(':', fontColor, currentPixelCoord, cursor, color);
        }

        void DrawLiteralExposureCompensation(uint2 currentPixelCoord, uint2 position, float3 fontColor, inout float3 color)
        {
            uint2 cursor = position;
            DrawMiniCharacter('E', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('x', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('p', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('o', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('s', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('u', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('r', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('e', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter(' ', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('C', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('o', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('m', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('p', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('e', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('n', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('s', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('a', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('t', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('i', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('o', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter('n', fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacter(':', fontColor, currentPixelCoord, cursor, color);
        }

        float ResolveMeteringWeight(float2 uv)
        {
            int meteringMode = (int)round(_ExposureDebugMeteringParams.y);
            float2 sourceSize = _ScreenSize.xy;
            float2 pixel = uv * sourceSize;

            if (meteringMode == VIVID_METERING_MASK_WEIGHTED)
                return saturate(SAMPLE_TEXTURE2D(_AutoExposureMeterMask, sampler_AutoExposureMeterMask, uv).r);

            if (meteringMode == VIVID_METERING_SPOT)
            {
                float screenDiagonal = 0.5 * (sourceSize.x + sourceSize.y);
                float radius = 0.075 * screenDiagonal;
                float2 center = sourceSize * 0.5;
                float distanceFromRadius = length(center - pixel) - radius;
                return 1.0 - saturate(distanceFromRadius);
            }

            if (meteringMode == VIVID_METERING_CENTER_WEIGHTED)
            {
                float screenDiagonal = 0.5 * (sourceSize.x + sourceSize.y);
                float2 center = sourceSize * 0.5;
                return 1.0 - saturate(pow(length(center - pixel) / max(screenDiagonal, 1.0), 1.0));
            }

            return 1.0;
        }

        ExposureDebugSummary SummarizeExposureDebug()
        {
            ExposureDebugSummary summary;
            float4 exposureState = _AutoExposureCurrentExposureBuffer[0];
            float debugExposure = _ExposureDebugViewParams.x;
            summary.histogramMax = 0.0;
            summary.histogramSum = 0.0;
            summary.lowPercentileBin = 0.0;
            summary.highPercentileBin = (float)(VIVID_EXPOSURE_HISTOGRAM_BINS - 1);
            summary.currentExposureScale = max(exposureState.x, VIVID_EXPOSURE_EPSILON);
            summary.targetExposureScale = max(exposureState.y, VIVID_EXPOSURE_EPSILON);
            summary.averageSceneLuminance = max(exposureState.z, VIVID_EXPOSURE_EPSILON);
            summary.averageSceneEV100 = ResolveAverageSceneEV100FromLuminance(summary.averageSceneLuminance);
            summary.effectiveExposureScale = max(summary.currentExposureScale * exp2(debugExposure), VIVID_EXPOSURE_EPSILON);
            summary.currentExposureEV100 = ResolveExposureEV100FromScale(summary.currentExposureScale) - debugExposure;
            summary.targetExposureEV100 = ResolveExposureEV100FromScale(summary.targetExposureScale) - debugExposure;
            summary.exposureCompensationStops = log2(max(exposureState.w, VIVID_EXPOSURE_EPSILON)) + debugExposure;

            [unroll]
            for (uint bucketIndex = 0u; bucketIndex < VIVID_EXPOSURE_HISTOGRAM_BINS; ++bucketIndex)
            {
                float histogramValue = (float)_AutoExposureHistogramBuffer[bucketIndex];
                summary.histogramMax = max(summary.histogramMax, histogramValue);
                summary.histogramSum += histogramValue;
            }

            if (_ExposureDebugState.y > 0.5 && summary.histogramSum > VIVID_EXPOSURE_EPSILON)
            {
                float cumulative = 0.0;
                float lowThreshold = summary.histogramSum * saturate(_ExposureDebugRangeParams.z);
                float highThreshold = summary.histogramSum * saturate(_ExposureDebugRangeParams.w);
                bool foundLow = false;
                bool foundHigh = false;

                [unroll]
                for (uint bucketIndex = 0u; bucketIndex < VIVID_EXPOSURE_HISTOGRAM_BINS; ++bucketIndex)
                {
                    cumulative += (float)_AutoExposureHistogramBuffer[bucketIndex];
                    if (!foundLow && cumulative >= lowThreshold)
                    {
                        summary.lowPercentileBin = bucketIndex;
                        foundLow = true;
                    }

                    if (!foundHigh && cumulative >= highThreshold)
                    {
                        summary.highPercentileBin = bucketIndex;
                        foundHigh = true;
                    }
                }
            }

            return summary;
        }

        float ComputePixelPercentile(float2 uv, float histogramSum, out float minPercentileBin, out float maxPercentileBin)
        {
            minPercentileBin = -1.0;
            maxPercentileBin = -1.0;
            if (_ExposureDebugState.y <= 0.5 || histogramSum <= VIVID_EXPOSURE_EPSILON)
                return 0.5;

            float sumBelow = 0.0;
            float sumForLow = 0.0;
            float sumForHigh = 0.0;
            float ev100 = ResolveAverageSceneEV100FromLuminance(Luminance(SampleSourceLinear(uv)));

            [unroll]
            for (int binIndex = 0; binIndex < VIVID_EXPOSURE_HISTOGRAM_BINS; ++binIndex)
            {
                float histogramValue = (float)_AutoExposureHistogramBuffer[binIndex];
                float evAtBin = lerp(
                    _ExposureDebugHistogramTransform.x,
                    _ExposureDebugHistogramTransform.y,
                    binIndex / (float)(VIVID_EXPOSURE_HISTOGRAM_BINS - 1));

                if (ev100 >= evAtBin)
                    sumBelow += histogramValue;

                if (minPercentileBin < 0.0)
                {
                    sumForLow += histogramValue;
                    if (sumForLow / histogramSum >= _ExposureDebugRangeParams.z)
                        minPercentileBin = binIndex;
                }

                if (maxPercentileBin < 0.0)
                {
                    sumForHigh += histogramValue;
                    if (sumForHigh / histogramSum > _ExposureDebugRangeParams.w)
                        maxPercentileBin = binIndex;
                }
            }

            return sumBelow / histogramSum;
        }

        float3 TonemapDebugLuminance(float luminance)
        {
            float3 colorLinear = luminance.xxx;

            if (_DebugTonemapMode == VIVID_TONEMAP_NEUTRAL)
                return NeutralTonemap(colorLinear);

            if (_DebugTonemapMode == VIVID_TONEMAP_ACES_APPROX || _DebugTonemapMode == VIVID_TONEMAP_ACES_FULL)
            {
                float3 aces = ACEScg_to_ACES(colorLinear);
                return AcesTonemap(aces);
            }

            if (_DebugTonemapMode == VIVID_TONEMAP_CUSTOM)
            {
                return CustomTonemap(
                    colorLinear,
                    _CustomToneCurve.xyz,
                    _ToeSegmentA,
                    _ToeSegmentB.xy,
                    _MidSegmentA,
                    _MidSegmentB.xy,
                    _ShoSegmentA,
                    _ShoSegmentB.xy);
            }

            if (_DebugTonemapMode == VIVID_TONEMAP_EXTERNAL && _LogLut3D_Params.y > 0.5)
            {
                float3 colorLutSpace = saturate(LinearToLogC(colorLinear));
                float3 colorLut = ApplyLut3D(TEXTURE3D_ARGS(_LogLut3D, sampler_LogLut3D), colorLutSpace, _LogLut3D_Params.xy);
                return lerp(colorLinear, colorLut, _LogLut3D_Params.z);
            }

            return colorLinear;
        }

        float GetTonemappedValueAtLocation(float uvX, float currentExposureEV100, float effectiveExposureScale)
        {
            float2 labelRange = GetHistogramLabelRange(currentExposureEV100);
            float exposureEV100 = lerp(labelRange.x, labelRange.y, uvX);
            float luminanceFromExposure = effectiveExposureScale * exp2(exposureEV100);
            return saturate(Luminance(TonemapDebugLuminance(luminanceFromExposure)));
        }

        void DrawHeatSideBar(
            float2 uv,
            float2 startSidebar,
            float2 endSidebar,
            float evValueRange,
            float3 indicatorColor,
            float2 sidebarSize,
            float extremeMargin,
            inout float3 sidebarColor)
        {
            float2 extremesSize = float2(extremeMargin, 0.0);
            float2 borderSize = 2.0 * _ScreenSize.zw;

            if (all(uv > startSidebar) && all(uv < endSidebar))
            {
                float inRange = (uv.x - startSidebar.x) / max(endSidebar.x - startSidebar.x, VIVID_EXPOSURE_EPSILON);
                float clampedRange = saturate(evValueRange);
                int distanceInPixels = abs(clampedRange - inRange) * sidebarSize.x * _ScreenSize.x;
                if (distanceInPixels < 3)
                {
                    sidebarColor = indicatorColor;
                }
                else if (distanceInPixels < 4)
                {
                    sidebarColor = 0.0;
                }
                else
                {
                    sidebarColor = ToHeat(inRange);
                }
            }
            else if (all(uv > startSidebar - extremesSize) && all(uv < endSidebar))
            {
                sidebarColor = 0.0;
            }
            else if (all(uv > startSidebar) && all(uv < endSidebar + extremesSize))
            {
                sidebarColor = 1.0;
            }
            else if (all(uv > startSidebar - (extremesSize + borderSize)) && all(uv < endSidebar + (extremesSize + borderSize)))
            {
                sidebarColor = 0.0;
            }
        }

        uint EVToHistogramBin(float ev100)
        {
            return (uint)clamp(
                floor(ResolveHistogramBasePositionFromEV100(ev100) * (VIVID_EXPOSURE_HISTOGRAM_BINS - 1) + 0.5),
                0.0,
                (float)(VIVID_EXPOSURE_HISTOGRAM_BINS - 1));
        }

        float GetHistogramInfo(
            float coordOnX,
            float maxHistogramValue,
            float labelBarHeight,
            float frameHeight,
            float currentExposureEV100,
            out uint binIndex,
            out bool isEdgeOfBin)
        {
            float barSize = _ScreenSize.x / VIVID_EXPOSURE_HISTOGRAM_BINS;
            float locationWithinBin = 0.0;

            if (_ExposureDebugViewParams.y > 0.5)
            {
                int centerBin = (int)EVToHistogramBin(currentExposureEV100);
                int midXPoint = (int)(_ScreenSize.x * 0.5);
                int halfBarSize = (int)(barSize * 0.5);
                int lowerMidPoint = midXPoint - halfBarSize;
                int higherMidPoint = midXPoint + halfBarSize;

                if (coordOnX < lowerMidPoint)
                {
                    float distanceFromCenter = lowerMidPoint - coordOnX;
                    float deltaBinFloat = distanceFromCenter / max(barSize, 1.0);
                    int deltaInBins = (int)ceil(deltaBinFloat);
                    locationWithinBin = frac(deltaBinFloat) * barSize;
                    binIndex = (uint)clamp(centerBin - deltaInBins, 0, VIVID_EXPOSURE_HISTOGRAM_BINS - 1);
                }
                else if (coordOnX > higherMidPoint)
                {
                    float distanceFromCenter = coordOnX - higherMidPoint;
                    float deltaBinFloat = distanceFromCenter / max(barSize, 1.0);
                    int deltaInBins = (int)ceil(deltaBinFloat);
                    locationWithinBin = frac(deltaBinFloat) * barSize;
                    binIndex = (uint)clamp(centerBin + deltaInBins, 0, VIVID_EXPOSURE_HISTOGRAM_BINS - 1);
                }
                else
                {
                    binIndex = (uint)centerBin;
                    locationWithinBin = higherMidPoint - coordOnX;
                }
            }
            else
            {
                float bin = coordOnX / max(barSize, 1.0);
                locationWithinBin = barSize * frac(bin);
                binIndex = (uint)clamp(floor(bin), 0.0, (float)(VIVID_EXPOSURE_HISTOGRAM_BINS - 1));
            }

            isEdgeOfBin = locationWithinBin < 1.0 || locationWithinBin > (barSize - 1.0);

            float histogramValue = _ExposureDebugState.y > 0.5 ? (float)_AutoExposureHistogramBuffer[binIndex] : 0.0;
            histogramValue /= max(maxHistogramValue, VIVID_EXPOSURE_EPSILON);
            histogramValue *= 0.95 * (frameHeight - labelBarHeight);
            histogramValue += labelBarHeight;
            return histogramValue;
        }

        void GetHistogramLabel(float labelCount, float labelIndex, float currentExposureEV100, out uint2 labelLocation, out float labelValue)
        {
            int minLabelLocationX = (int)(VIVID_SMALL_FONT_SPACING * 0.25);
            int maxLabelLocationX = (int)_ScreenSize.x - (VIVID_SMALL_FONT_SPACING * 6);
            float2 labelRange = GetHistogramLabelRange(currentExposureEV100);
            float t = rcp(labelCount) * (labelIndex - 0.25);
            labelLocation = uint2((uint)lerp(minLabelLocationX, maxLabelLocationX, t), 0u);
            labelValue = lerp(labelRange.x, labelRange.y, t);
        }

        void DrawTriangleIndicator(float2 pixelCoord, float labelBarHeight, float uvXLocation, float widthNdc, float3 color, inout float3 outputColor)
        {
            float arrowStart = labelBarHeight * 0.4;
            float heightInIndicator = saturate((pixelCoord.y - arrowStart) / max(labelBarHeight - arrowStart, VIVID_EXPOSURE_EPSILON));
            float indicatorWidth = 1.0 - heightInIndicator;
            float minScreenPos = (uvXLocation - widthNdc * indicatorWidth * 0.5) * _ScreenSize.x;
            float maxScreenPos = (uvXLocation + widthNdc * indicatorWidth * 0.5) * _ScreenSize.x;

            if (pixelCoord.x > minScreenPos && pixelCoord.x < maxScreenPos && pixelCoord.y >= arrowStart)
            {
                outputColor = color;
            }
            else if (pixelCoord.x > (minScreenPos - 2.0)
                && pixelCoord.x < (maxScreenPos + 2.0)
                && pixelCoord.y > (arrowStart - 2.0))
            {
                outputColor = 0.0;
            }
        }

        bool DrawEmptyFrame(float2 uv, float3 frameColor, float frameAlpha, float frameHeight, float labelBarHeight, inout float3 outputColor)
        {
            float2 borderSize = 2.0 * _ScreenSize.zw;
            if (uv.y > frameHeight)
                return false;

            if (uv.x < borderSize.x || uv.x > (1.0 - borderSize.x))
            {
                outputColor = 0.0;
                return false;
            }

            if (uv.y > frameHeight - borderSize.y)
            {
                outputColor = 0.0;
                return false;
            }

            outputColor = lerp(outputColor, frameColor, frameAlpha);
            if (uv.y < labelBarHeight)
                outputColor *= 0.075;

            return true;
        }

        void DrawHistogramFrame(
            float2 uv,
            uint2 pixelCoord,
            float frameHeight,
            float3 backgroundColor,
            float backgroundAlpha,
            ExposureDebugSummary summary,
            inout float3 outputColor)
        {
            float labelBarHeight = (VIVID_SMALL_FONT_HEIGHT + 4.0) * _ScreenSize.w;

            if (!DrawEmptyFrame(uv, backgroundColor, backgroundAlpha, frameHeight, labelBarHeight, outputColor))
                return;

            bool isEdgeOfBin = false;
            uint binIndex = 0u;
            float histogramValue = GetHistogramInfo(pixelCoord.x, summary.histogramMax, labelBarHeight, frameHeight, summary.currentExposureEV100, binIndex, isEdgeOfBin);

            if (uv.y < histogramValue && uv.y > labelBarHeight)
            {
                isEdgeOfBin = isEdgeOfBin || (uv.y > histogramValue - _ScreenSize.w);
                if (binIndex < (uint)max(summary.lowPercentileBin, 0.0))
                {
                    outputColor = float3(0.0, 0.0, 1.0);
                }
                else if (binIndex >= (uint)max(summary.highPercentileBin, 0.0))
                {
                    outputColor = float3(1.0, 0.0, 0.0);
                }
                else
                {
                    outputColor = 1.0;
                }

                if (isEdgeOfBin)
                    outputColor = 0.0;
            }

            const int labelCount = 12;
            [loop]
            for (int labelIndex = 0; labelIndex <= labelCount; ++labelIndex)
            {
                uint2 labelLocation;
                float labelValue;
                GetHistogramLabel((float)labelCount, labelIndex, summary.currentExposureEV100, labelLocation, labelValue);
                DrawMiniFloatExplicitPrecision(labelValue, 1.0, pixelCoord, 1u, labelLocation, outputColor);
            }

            float currentMarker = EvToUVLocation(summary.currentExposureEV100, summary.currentExposureEV100);
            float targetMarker = EvToUVLocation(summary.targetExposureEV100, summary.currentExposureEV100);
            float labelBarHeightScreen = labelBarHeight * _ScreenSize.y;
            if (uv.y < labelBarHeight)
            {
                DrawTriangleIndicator(float2(pixelCoord), labelBarHeightScreen, targetMarker, 0.007, float3(0.9, 0.75, 0.1), outputColor);
                DrawTriangleIndicator(float2(pixelCoord), labelBarHeightScreen, currentMarker, 0.007, float3(0.15, 0.15, 0.1), outputColor);
            }

            bool supportsTonemapCurve = _DebugTonemapMode == VIVID_TONEMAP_NEUTRAL
                || _DebugTonemapMode == VIVID_TONEMAP_ACES_APPROX
                || _DebugTonemapMode == VIVID_TONEMAP_ACES_FULL
                || _DebugTonemapMode == VIVID_TONEMAP_CUSTOM
                || _DebugTonemapMode == VIVID_TONEMAP_EXTERNAL;
            if (_ExposureDebugViewParams.z > 0.5 && supportsTonemapCurve)
            {
                float tonemappedValue = GetTonemappedValueAtLocation(uv.x, summary.currentExposureEV100, summary.effectiveExposureScale);
                float tonemapHeight = tonemappedValue * 0.95 * (frameHeight - labelBarHeight) + labelBarHeight;
                float curveWidth = 4.0 * _ScreenSize.w;
                if (uv.y < tonemapHeight && uv.y > (tonemapHeight - curveWidth))
                    outputColor = outputColor * 0.1;
            }
        }

        float3 FragSceneEV100(Varyings input) : SV_Target
        {
            float2 uv = input.uv;
            uint2 pixelCoord = uint2(input.positionCS.xy);
            float3 outputColor = 0.0;
            float sceneEV100 = ResolveAverageSceneEV100FromLuminance(Luminance(SampleSourceLinear(uv)));
            float minEV100 = _ExposureDebugRangeParams.x;
            float maxEV100 = max(_ExposureDebugRangeParams.y, minEV100 + VIVID_EXPOSURE_EPSILON);
            float evInRange = (sceneEV100 - minEV100) / (maxEV100 - minEV100);

            if (sceneEV100 < minEV100)
            {
                outputColor = 0.0;
            }
            else if (sceneEV100 > maxEV100)
            {
                outputColor = 1.0;
            }
            else
            {
                outputColor = ToHeat(evInRange);
            }

            float labelBarHeight = (VIVID_SMALL_FONT_HEIGHT + 4.0) * _ScreenSize.w;
            float2 sidebarSize = float2(0.9, 0.02);
            float2 sidebarBottomLeft = float2(0.05, labelBarHeight);
            float2 sidebarTopRight = sidebarBottomLeft + sidebarSize;
            float2 indicatorUv = saturate(_MousePixelCoord.xy * _ScreenSize.zw);
            float indicatorEV100 = ResolveAverageSceneEV100FromLuminance(Luminance(SampleSourceLinear(indicatorUv)));
            float indicatorEVRange = (indicatorEV100 - minEV100) / max(maxEV100 - minEV100, VIVID_EXPOSURE_EPSILON);
            float extremeMargin = 5.0 * _ScreenSize.z;
            DrawHeatSideBar(
                uv,
                sidebarBottomLeft,
                sidebarTopRight,
                indicatorEVRange,
                0.66,
                sidebarSize,
                extremeMargin,
                outputColor);

            float2 borderSize = 2.0 * _ScreenSize.zw;
            if (uv.y < labelBarHeight
                && uv.x >= (sidebarBottomLeft.x - borderSize.x)
                && uv.x <= (sidebarTopRight.x + borderSize.x))
            {
                outputColor *= 0.075;
            }

            const int labelCount = 8;
            int minLabelLocationX = (int)((sidebarBottomLeft.x - borderSize.x) * _ScreenSize.x) + 1;
            int maxLabelLocationX = (int)((sidebarTopRight.x + borderSize.x) * _ScreenSize.x) - (VIVID_SMALL_FONT_SPACING * 5);
            [unroll]
            for (int labelIndex = 0; labelIndex <= labelCount; ++labelIndex)
            {
                float t = labelIndex / (float)labelCount;
                float labelValue = lerp(minEV100, maxEV100, t);
                uint2 labelLocation = uint2((uint)lerp(minLabelLocationX, maxLabelLocationX, t), 0u);
                DrawMiniFloatExplicitPrecision(labelValue, 1.0, pixelCoord, 1u, labelLocation, outputColor);
            }

            uint2 textLocationShadow = uint2(_MousePixelCoord.x + VIVID_SMALL_FONT_SPACING + 1.0, _MousePixelCoord.y - 1.0);
            uint2 textLocation = uint2(_MousePixelCoord.x + VIVID_SMALL_FONT_SPACING, _MousePixelCoord.y);
            DrawMiniFloatExplicitPrecision(indicatorEV100, 1.0, pixelCoord, 1u, textLocationShadow, outputColor);
            DrawMiniFloatExplicitPrecision(indicatorEV100, 0.0, pixelCoord, 1u, textLocation, outputColor);

            uint2 markerShadow = uint2(_MousePixelCoord.x + 1.0, _MousePixelCoord.y - 1.0);
            uint2 marker = uint2(_MousePixelCoord.xy);
            DrawMiniCharacter('X', 1.0, pixelCoord, markerShadow, outputColor);
            DrawMiniCharacter('X', 0.0, pixelCoord, marker, outputColor);
            return outputColor;
        }

        float3 FragMetering(Varyings input) : SV_Target
        {
            float2 uv = input.uv;
            float3 color = SampleSource(uv).rgb;
            float pipFraction = 0.33;
            uint2 pixelCoord = uint2(input.positionCS.xy);
            float2 topRight = pipFraction * _ScreenSize.xy;

            if (all(pixelCoord < uint2(topRight)))
            {
                float2 pipUv = uv / pipFraction;
                float weight = ResolveMeteringWeight(pipUv);
                float3 pipColor = _ExposureDebugMeteringParams.x > 0.5
                    ? 1.0
                    : SampleSource(pipUv).rgb;
                return pipColor * weight;
            }

            if (all(pixelCoord < uint2(topRight + 3.0)))
                return 0.33;

            return color;
        }

        float3 FragHistogram(Varyings input) : SV_Target
        {
            float2 uv = input.uv;
            uint2 pixelCoord = uint2(input.positionCS.xy);
            float3 outputColor = SampleSource(uv).rgb;
            ExposureDebugSummary summary = SummarizeExposureDebug();

            if (_ExposureDebugViewParams.w > 0.5 && summary.histogramSum > VIVID_EXPOSURE_EPSILON)
            {
                float minPercentileBin;
                float maxPercentileBin;
                float percentile = ComputePixelPercentile(uv, summary.histogramSum, minPercentileBin, maxPercentileBin);
                float checker = Checker(pixelCoord);
                if (percentile < _ExposureDebugRangeParams.z)
                {
                    outputColor = checker > 0.5 ? float3(0.0, 0.0, 1.0) : outputColor * 0.33;
                }
                else if (percentile > _ExposureDebugRangeParams.w)
                {
                    outputColor = checker > 0.5 ? float3(1.0, 0.0, 0.0) : outputColor * 0.33;
                }
            }

            float histogramFrameHeight = 0.2;
            DrawHistogramFrame(
                uv,
                pixelCoord,
                histogramFrameHeight,
                float3(0.125, 0.125, 0.125),
                0.4,
                summary,
                outputColor);

            float3 textColor = 0.5;
            uint2 currentTextLocation = uint2(
                (uint)(VIVID_SMALL_FONT_SPACING * 0.5),
                (uint)(VIVID_SMALL_FONT_SPACING * 0.5 + histogramFrameHeight * _ScreenSize.y));
            DrawLiteralCurrentExposure(pixelCoord, currentTextLocation, textColor, outputColor);
            currentTextLocation.x += VIVID_SMALL_FONT_SPACING * 17;
            DrawMiniFloatExplicitPrecision(summary.currentExposureEV100, textColor, pixelCoord, 3u, currentTextLocation, outputColor);

            currentTextLocation = uint2(
                (uint)(VIVID_SMALL_FONT_SPACING * 0.5),
                (uint)(VIVID_SMALL_FONT_SPACING * 0.5 + histogramFrameHeight * _ScreenSize.y + VIVID_SMALL_FONT_HEIGHT + 2));
            DrawLiteralTargetExposure(pixelCoord, currentTextLocation, textColor, outputColor);
            currentTextLocation.x += VIVID_SMALL_FONT_SPACING * 16;
            DrawMiniFloatExplicitPrecision(summary.targetExposureEV100, textColor, pixelCoord, 3u, currentTextLocation, outputColor);

            currentTextLocation = uint2(
                (uint)(VIVID_SMALL_FONT_SPACING * 0.5),
                (uint)(VIVID_SMALL_FONT_SPACING * 0.5 + histogramFrameHeight * _ScreenSize.y + (VIVID_SMALL_FONT_HEIGHT + 2) * 2));
            DrawLiteralExposureCompensation(pixelCoord, currentTextLocation, textColor, outputColor);
            currentTextLocation.x += VIVID_SMALL_FONT_SPACING * 22;
            DrawMiniFloatExplicitPrecision(summary.exposureCompensationStops, textColor, pixelCoord, 3u, currentTextLocation, outputColor);

            return outputColor;
        }

        float4 FragCopy(Varyings input) : SV_Target
        {
            return SampleSource(input.uv);
        }
        ENDHLSL

        Pass
        {
            Name "SceneEV100"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma fragment FragSceneEV100
            ENDHLSL
        }

        Pass
        {
            Name "Metering"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma fragment FragMetering
            ENDHLSL
        }

        Pass
        {
            Name "Histogram"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma fragment FragHistogram
            ENDHLSL
        }

        Pass
        {
            Name "Copy"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma fragment FragCopy
            ENDHLSL
        }
    }
    Fallback Off
}
