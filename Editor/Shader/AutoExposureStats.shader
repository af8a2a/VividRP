Shader "Hidden/VividRP/Editor/Auto Exposure Stats"
{
    CGINCLUDE

        #include "UnityCG.cginc"
        #pragma editor_sync_compilation
        #pragma target 4.5

        #define VIVID_EXPOSURE_EPSILON 1e-4
        #define VIVID_EXPOSURE_HISTOGRAM_BINS 64
        #define VIVID_SMALL_FONT_WIDTH 5
        #define VIVID_SMALL_FONT_HEIGHT 7
        #define VIVID_SMALL_FONT_SPACING 6

        float4 _PreviewState;     // x: disabled alpha, y: manual mode, z: dark skin
        float4 _StatusFlags;      // x: active, y: apply physical camera, z: has physical camera preview, w: meter mask assigned
        float4 _HistogramMarkers; // x: clamp min, y: clamp max, z: average, w: histogram width
        float4 _GaugeMarkers;     // x: current exposure gauge, y: target exposure gauge, z: compensation gauge, w: EV gauge
        float4 _PercentMarkers;   // x: low percent, y: high percent, z: enabled state
        float4 _HistogramLabelRange;     // x: min EV100 label, y: max EV100 label, z/w: source histogram EV100 range
        float4 _HistogramExposureValues; // x: current exposure EV100, y: target exposure EV100, z: exposure compensation, w: scene EV100
        float4 _HistogramPercentileBins; // x: low percentile bin, y: high percentile bin, z: live histogram, w: live stats
        float _HistogramSamples[64];

        struct ExposureStatsSummary
        {
            float histogramMax;
            float lowPercentileBin;
            float highPercentileBin;
            float currentExposureEV100;
            float targetExposureEV100;
            float exposureCompensationStops;
            float averageSceneEV100;
        };

        float2 ResolveScreenSize()
        {
            return max(_ScreenParams.xy, float2(1.0, 1.0));
        }

        float2 ResolveInvScreenSize()
        {
            return rcp(ResolveScreenSize());
        }

        float3 ResolvePanelColor(float darkSkin)
        {
            return darkSkin > 0.5 ? float3(0.025, 0.027, 0.032) : float3(0.095, 0.102, 0.115);
        }

        float3 ResolveFrameColor(float darkSkin)
        {
            return darkSkin > 0.5 ? float3(0.125, 0.125, 0.125) : float3(0.155, 0.160, 0.170);
        }

        float3 ResolveTextColor(float darkSkin)
        {
            return darkSkin > 0.5 ? float3(0.55, 0.55, 0.55) : float3(0.72, 0.72, 0.72);
        }

        float LineMask(float value, float center, float halfWidth)
        {
            return 1.0 - smoothstep(halfWidth, halfWidth + 0.0025, abs(value - center));
        }

        float SampleHistogramBin(uint binIndex)
        {
            int clampedIndex = min(max((int)binIndex, 0), VIVID_EXPOSURE_HISTOGRAM_BINS - 1);
            return saturate(_HistogramSamples[clampedIndex]);
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

        void DrawMiniCharacterInternal(uint ascii, float3 fontColor, uint2 currentPixelCoord, inout uint2 cursor, inout float3 color, bool flipVertical)
        {
            int2 localCoord = int2(currentPixelCoord) - int2(cursor);
            if (flipVertical)
                localCoord.y = VIVID_SMALL_FONT_HEIGHT - 1 - localCoord.y;

            if (SampleMiniGlyph(localCoord, ascii))
                color = fontColor;

            cursor.x += VIVID_SMALL_FONT_SPACING;
        }

        void DrawMiniCharacter(uint ascii, float3 fontColor, uint2 currentPixelCoord, inout uint2 cursor, inout float3 color)
        {
            DrawMiniCharacterInternal(ascii, fontColor, currentPixelCoord, cursor, color, false);
        }

        void DrawMiniCharacterFlippedY(uint ascii, float3 fontColor, uint2 currentPixelCoord, inout uint2 cursor, inout float3 color)
        {
            DrawMiniCharacterInternal(ascii, fontColor, currentPixelCoord, cursor, color, true);
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
            if (value != value)
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

        void DrawMiniUnsignedIntegerFlippedY(uint value, float3 fontColor, uint2 currentPixelCoord, inout uint2 cursor, inout float3 color)
        {
            uint divisor = 1u;
            while (value / divisor >= 10u && divisor < 100000000u)
                divisor *= 10u;

            [loop]
            while (divisor > 0u)
            {
                uint digit = (value / divisor) % 10u;
                DrawMiniCharacterFlippedY('0' + digit, fontColor, currentPixelCoord, cursor, color);
                divisor /= 10u;
            }
        }

        void DrawMiniFixedDigitsFlippedY(uint value, uint digitCount, float3 fontColor, uint2 currentPixelCoord, inout uint2 cursor, inout float3 color)
        {
            uint divisor = Pow10(max(digitCount, 1u) - 1u);

            [loop]
            for (uint index = 0u; index < max(digitCount, 1u); ++index)
            {
                uint digit = divisor > 0u ? (value / divisor) % 10u : 0u;
                DrawMiniCharacterFlippedY('0' + digit, fontColor, currentPixelCoord, cursor, color);
                divisor = max(divisor / 10u, 0u);
            }
        }

        void DrawMiniFloatExplicitPrecisionFlippedY(float value, float3 fontColor, uint2 currentPixelCoord, uint digitCount, inout uint2 cursor, inout float3 color)
        {
            if (value != value)
            {
                DrawMiniCharacterFlippedY('N', fontColor, currentPixelCoord, cursor, color);
                DrawMiniCharacterFlippedY('a', fontColor, currentPixelCoord, cursor, color);
                DrawMiniCharacterFlippedY('N', fontColor, currentPixelCoord, cursor, color);
                return;
            }

            float absValue = abs(value);
            if (value < 0.0)
                DrawMiniCharacterFlippedY('-', fontColor, currentPixelCoord, cursor, color);

            DrawMiniUnsignedIntegerFlippedY((uint)absValue, fontColor, currentPixelCoord, cursor, color);
            DrawMiniCharacterFlippedY('.', fontColor, currentPixelCoord, cursor, color);

            uint multiplier = Pow10(digitCount);
            uint fracValue = (uint)(frac(absValue) * multiplier);
            DrawMiniFixedDigitsFlippedY(fracValue, digitCount, fontColor, currentPixelCoord, cursor, color);
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

        float2 GetHistogramLabelRange(float currentExposureEV100)
        {
            float minEV100 = _HistogramLabelRange.x;
            float maxEV100 = max(_HistogramLabelRange.y, minEV100 + VIVID_EXPOSURE_EPSILON);
            return float2(minEV100, maxEV100);
        }

        float EvToUVLocation(float ev100, float currentExposureEV100)
        {
            float2 labelRange = GetHistogramLabelRange(currentExposureEV100);
            return saturate((ev100 - labelRange.x) / max(labelRange.y - labelRange.x, VIVID_EXPOSURE_EPSILON));
        }

        ExposureStatsSummary SummarizeExposureStats()
        {
            ExposureStatsSummary summary;
            summary.histogramMax = 0.0;
            summary.lowPercentileBin = clamp(_HistogramPercentileBins.x, 0.0, (float)(VIVID_EXPOSURE_HISTOGRAM_BINS - 1));
            summary.highPercentileBin = clamp(max(_HistogramPercentileBins.y, summary.lowPercentileBin), 0.0, (float)(VIVID_EXPOSURE_HISTOGRAM_BINS - 1));
            summary.currentExposureEV100 = _HistogramExposureValues.x;
            summary.targetExposureEV100 = _HistogramExposureValues.y;
            summary.exposureCompensationStops = _HistogramExposureValues.z;
            summary.averageSceneEV100 = _HistogramExposureValues.w;

            [unroll]
            for (uint bucketIndex = 0u; bucketIndex < VIVID_EXPOSURE_HISTOGRAM_BINS; ++bucketIndex)
                summary.histogramMax = max(summary.histogramMax, SampleHistogramBin(bucketIndex));

            return summary;
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
            float barSize = ResolveScreenSize().x / VIVID_EXPOSURE_HISTOGRAM_BINS;
            float bin = coordOnX / max(barSize, 1.0);
            float locationWithinBin = barSize * frac(bin);
            binIndex = (uint)clamp(floor(bin), 0.0, (float)(VIVID_EXPOSURE_HISTOGRAM_BINS - 1));
            isEdgeOfBin = barSize > 2.0 && (locationWithinBin < 1.0 || locationWithinBin > (barSize - 1.0));

            float histogramValue = SampleHistogramBin(binIndex);
            histogramValue /= max(maxHistogramValue, VIVID_EXPOSURE_EPSILON);
            histogramValue *= 0.95 * (frameHeight - labelBarHeight);
            histogramValue += labelBarHeight;
            return histogramValue;
        }

        void GetHistogramLabel(float labelCount, float labelIndex, float currentExposureEV100, out uint2 labelLocation, out float labelValue)
        {
            float2 screenSize = ResolveScreenSize();
            int minLabelLocationX = (int)(VIVID_SMALL_FONT_SPACING * 0.25);
            int maxLabelLocationX = (int)screenSize.x - (VIVID_SMALL_FONT_SPACING * 6);
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
            float screenWidth = ResolveScreenSize().x;
            float minScreenPos = (uvXLocation - widthNdc * indicatorWidth * 0.5) * screenWidth;
            float maxScreenPos = (uvXLocation + widthNdc * indicatorWidth * 0.5) * screenWidth;

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
            float2 borderSize = 2.0 * ResolveInvScreenSize();
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
            ExposureStatsSummary summary,
            inout float3 outputColor)
        {
            float labelBarHeight = (VIVID_SMALL_FONT_HEIGHT + 4.0) / ResolveScreenSize().y;

            if (!DrawEmptyFrame(uv, backgroundColor, backgroundAlpha, frameHeight, labelBarHeight, outputColor))
                return;

            bool isEdgeOfBin = false;
            uint binIndex = 0u;
            float histogramValue = GetHistogramInfo(pixelCoord.x, summary.histogramMax, labelBarHeight, frameHeight, summary.currentExposureEV100, binIndex, isEdgeOfBin);

            if (uv.y < histogramValue && uv.y > labelBarHeight)
            {
                isEdgeOfBin = isEdgeOfBin || (uv.y > histogramValue - ResolveInvScreenSize().y);
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
                DrawMiniFloatExplicitPrecisionFlippedY(labelValue, 1.0, pixelCoord, 1u, labelLocation, outputColor);
            }

            float currentMarker = EvToUVLocation(summary.currentExposureEV100, summary.currentExposureEV100);
            float targetMarker = EvToUVLocation(summary.targetExposureEV100, summary.currentExposureEV100);
            float labelBarHeightScreen = labelBarHeight * ResolveScreenSize().y;
            if (uv.y < labelBarHeight)
            {
                DrawTriangleIndicator(float2(pixelCoord), labelBarHeightScreen, targetMarker, 0.007, float3(0.9, 0.75, 0.1), outputColor);
                DrawTriangleIndicator(float2(pixelCoord), labelBarHeightScreen, currentMarker, 0.007, float3(0.15, 0.15, 0.1), outputColor);
            }
        }

        void DrawHistogramText(
            uint2 pixelCoord,
            float frameHeight,
            float3 textColor,
            ExposureStatsSummary summary,
            inout float3 outputColor)
        {
            float screenHeight = ResolveScreenSize().y;
            uint2 currentTextLocation = uint2(
                (uint)(VIVID_SMALL_FONT_SPACING * 0.5),
                (uint)(VIVID_SMALL_FONT_SPACING * 0.5 + frameHeight * screenHeight));
            DrawLiteralCurrentExposure(pixelCoord, currentTextLocation, textColor, outputColor);
            currentTextLocation.x += VIVID_SMALL_FONT_SPACING * 17;
            DrawMiniFloatExplicitPrecision(summary.currentExposureEV100, textColor, pixelCoord, 3u, currentTextLocation, outputColor);

            currentTextLocation = uint2(
                (uint)(VIVID_SMALL_FONT_SPACING * 0.5),
                (uint)(VIVID_SMALL_FONT_SPACING * 0.5 + frameHeight * screenHeight + VIVID_SMALL_FONT_HEIGHT + 2));
            DrawLiteralTargetExposure(pixelCoord, currentTextLocation, textColor, outputColor);
            currentTextLocation.x += VIVID_SMALL_FONT_SPACING * 16;
            DrawMiniFloatExplicitPrecision(summary.targetExposureEV100, textColor, pixelCoord, 3u, currentTextLocation, outputColor);

            currentTextLocation = uint2(
                (uint)(VIVID_SMALL_FONT_SPACING * 0.5),
                (uint)(VIVID_SMALL_FONT_SPACING * 0.5 + frameHeight * screenHeight + (VIVID_SMALL_FONT_HEIGHT + 2) * 2));
            DrawLiteralExposureCompensation(pixelCoord, currentTextLocation, textColor, outputColor);
            currentTextLocation.x += VIVID_SMALL_FONT_SPACING * 22;
            DrawMiniFloatExplicitPrecision(summary.exposureCompensationStops, textColor, pixelCoord, 3u, currentTextLocation, outputColor);
        }

        float4 Frag(v2f_img input) : SV_Target
        {
            float darkSkin = _PreviewState.z;
            float alpha = _PreviewState.x;
            uint2 pixelCoord = uint2(input.pos.xy);
            float3 outputColor = ResolvePanelColor(darkSkin);
            float3 frameColor = ResolveFrameColor(darkSkin);
            float3 textColor = ResolveTextColor(darkSkin);
            ExposureStatsSummary summary = SummarizeExposureStats();
            float histogramFrameHeight = 0.48;

            DrawHistogramFrame(
                input.uv,
                pixelCoord,
                histogramFrameHeight,
                frameColor,
                0.88,
                summary,
                outputColor);

            DrawHistogramText(
                pixelCoord,
                histogramFrameHeight,
                textColor,
                summary,
                outputColor);

            return float4(saturate(outputColor * alpha), 1.0);
        }

    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            ENDCG
        }
    }
}
