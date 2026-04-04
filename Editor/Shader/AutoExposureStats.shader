Shader "Hidden/VividRP/Editor/Auto Exposure Stats"
{
    CGINCLUDE

        #include "UnityCG.cginc"
        #pragma editor_sync_compilation
        #pragma target 3.5

        float4 _PreviewState;     // x: disabled alpha, y: manual mode, z: dark skin
        float4 _StatusFlags;      // x: active, y: apply physical camera, z: has physical camera preview, w: meter mask assigned
        float4 _HistogramMarkers; // x: clamp min, y: clamp max, z: average, w: histogram width
        float4 _GaugeMarkers;     // x: current exposure gauge, y: target exposure gauge, z: compensation gauge, w: EV gauge
        float4 _PercentMarkers;   // x: low percent, y: high percent, z: enabled state

        float3 ResolvePanelColor(float darkSkin)
        {
            return darkSkin > 0.5 ? float3(0.07, 0.08, 0.10) : float3(0.77, 0.79, 0.82);
        }

        float3 ResolveSurfaceColor(float darkSkin)
        {
            return darkSkin > 0.5 ? float3(0.12, 0.13, 0.16) : float3(0.86, 0.88, 0.91);
        }

        float3 ResolveGridColor(float darkSkin)
        {
            return darkSkin > 0.5 ? float3(0.19, 0.21, 0.24) : float3(0.70, 0.73, 0.77);
        }

        float3 ResolveBorderColor(float darkSkin)
        {
            return darkSkin > 0.5 ? float3(0.92, 0.94, 0.97) : float3(0.18, 0.20, 0.24);
        }

        float3 ResolveAccentColor()
        {
            if (_StatusFlags.x < 0.5)
                return float3(0.92, 0.30, 0.26);

            if (_PreviewState.y > 0.5)
                return _StatusFlags.y > 0.5 ? float3(0.22, 0.84, 1.00) : float3(0.34, 0.56, 1.00);

            return float3(0.22, 0.92, 0.52);
        }

        float RectMask(float2 uv, float2 rectMin, float2 rectMax)
        {
            return step(rectMin.x, uv.x)
                * step(rectMin.y, uv.y)
                * step(uv.x, rectMax.x)
                * step(uv.y, rectMax.y);
        }

        float LineMask(float value, float center, float thickness)
        {
            return 1.0 - smoothstep(thickness, thickness + 0.004, abs(value - center));
        }

        float FrameMask(float2 uv, float2 rectMin, float2 rectMax, float thickness)
        {
            float horizontal = max(LineMask(uv.x, rectMin.x, thickness), LineMask(uv.x, rectMax.x, thickness))
                * step(rectMin.y, uv.y)
                * step(uv.y, rectMax.y);
            float vertical = max(LineMask(uv.y, rectMin.y, thickness), LineMask(uv.y, rectMax.y, thickness))
                * step(rectMin.x, uv.x)
                * step(uv.x, rectMax.x);
            return max(horizontal, vertical);
        }

        float SpanMask(float2 uv, float2 rectMin, float2 rectMax, float start, float end)
        {
            float2 clampedRange = float2(min(start, end), max(start, end));
            return RectMask(uv, rectMin, rectMax)
                * step(clampedRange.x, saturate((uv.x - rectMin.x) / max(rectMax.x - rectMin.x, 1e-5)))
                * step(saturate((uv.x - rectMin.x) / max(rectMax.x - rectMin.x, 1e-5)), clampedRange.y);
        }

        float SyntheticHistogram(float x)
        {
            float width = max(_HistogramMarkers.w, 0.08);
            float center = _HistogramMarkers.z;
            float primary = exp(-pow((x - center) / width, 2.0) * 2.6);
            float secondaryCenter = lerp(_HistogramMarkers.x, _HistogramMarkers.y, 0.72);
            float secondary = exp(-pow((x - secondaryCenter) / max(width * 0.55, 0.05), 2.0) * 2.0);
            return saturate(primary + secondary * 0.32);
        }

        float4 DrawHistogramPreview(v2f_img i, float3 color, float3 panelColor, float3 surfaceColor, float3 gridColor, float3 accentColor, float3 borderColor)
        {
            float2 histogramMin = float2(0.08, 0.24);
            float2 histogramMax = float2(0.92, 0.74);
            float histogramMask = RectMask(i.uv, histogramMin, histogramMax);
            float2 histogramUv = saturate((i.uv - histogramMin) / max(histogramMax - histogramMin, float2(1e-5, 1e-5)));

            float percentMask = RectMask(i.uv, float2(0.08, 0.08), float2(0.92, 0.15));
            float2 percentUv = saturate((i.uv - float2(0.08, 0.08)) / float2(0.84, 0.07));
            float gaugeMask = RectMask(i.uv, float2(0.08, 0.16), float2(0.92, 0.21));
            float2 gaugeUv = saturate((i.uv - float2(0.08, 0.16)) / float2(0.84, 0.05));

            float gridMask = histogramMask * max(
                max(LineMask(histogramUv.x, 0.25, 0.003), LineMask(histogramUv.x, 0.50, 0.003)),
                max(
                    max(LineMask(histogramUv.x, 0.75, 0.003), LineMask(histogramUv.y, 0.25, 0.003)),
                    max(LineMask(histogramUv.y, 0.50, 0.003), LineMask(histogramUv.y, 0.75, 0.003))));
            color = lerp(color, gridColor, gridMask * 0.45);

            float clampMin = min(_HistogramMarkers.x, _HistogramMarkers.y);
            float clampMax = max(_HistogramMarkers.x, _HistogramMarkers.y);
            float clampSpan = histogramMask
                * step(clampMin, histogramUv.x)
                * step(histogramUv.x, clampMax);
            color = lerp(color, accentColor * 0.24 + surfaceColor * 0.76, clampSpan * 0.90);

            float histogramHeight = SyntheticHistogram(histogramUv.x) * 0.82 + 0.04;
            float histogramFill = histogramMask * (1.0 - smoothstep(histogramHeight - 0.015, histogramHeight + 0.015, histogramUv.y));
            float histogramLine = histogramMask * LineMask(histogramUv.y, histogramHeight, 0.012);
            color = lerp(color, accentColor * 0.28 + panelColor * 0.72, histogramFill * 0.68);
            color = lerp(color, accentColor, histogramLine * 0.92);

            float minLine = histogramMask * LineMask(histogramUv.x, clampMin, 0.006);
            float maxLine = histogramMask * LineMask(histogramUv.x, clampMax, 0.006);
            float averageLine = histogramMask * LineMask(histogramUv.x, _HistogramMarkers.z, 0.008);
            color = lerp(color, float3(0.80, 0.85, 0.90), minLine * 0.95);
            color = lerp(color, float3(0.98, 0.98, 0.98), maxLine * 0.95);
            color = lerp(color, float3(0.22, 0.84, 1.00), averageLine * 0.95);

            color = lerp(color, surfaceColor * 0.85, percentMask * 0.95);
            float percentSpan = percentMask
                * step(_PercentMarkers.x, percentUv.x)
                * step(percentUv.x, _PercentMarkers.y);
            float percentMid = percentMask * LineMask(percentUv.x, 0.5, 0.005);
            float lowPercentLine = percentMask * LineMask(percentUv.x, _PercentMarkers.x, 0.008);
            float highPercentLine = percentMask * LineMask(percentUv.x, _PercentMarkers.y, 0.008);
            color = lerp(color, accentColor * 0.40 + surfaceColor * 0.60, percentSpan * 0.85);
            color = lerp(color, borderColor, percentMid * 0.65);
            color = lerp(color, float3(1.00, 0.84, 0.26), lowPercentLine * 0.95);
            color = lerp(color, float3(1.00, 0.44, 0.28), highPercentLine * 0.95);

            color = lerp(color, surfaceColor * 0.82, gaugeMask * 0.96);
            float gaugeNeutral = gaugeMask * LineMask(gaugeUv.x, 0.5, 0.005);
            float currentSpan = SpanMask(i.uv, float2(0.08, 0.16), float2(0.92, 0.21), 0.5, _GaugeMarkers.x);
            float targetLine = gaugeMask * LineMask(gaugeUv.x, _GaugeMarkers.y, 0.010);
            float currentLine = gaugeMask * LineMask(gaugeUv.x, _GaugeMarkers.x, 0.010);
            color = lerp(color, borderColor, gaugeNeutral * 0.75);
            color = lerp(color, float3(0.22, 0.84, 1.00), currentSpan * 0.50);
            color = lerp(color, float3(1.00, 0.78, 0.28), targetLine * 0.90);
            color = lerp(color, float3(0.26, 0.92, 1.00), currentLine * 0.95);

            float histogramFrame = FrameMask(i.uv, histogramMin, histogramMax, 0.004);
            float percentFrame = FrameMask(i.uv, float2(0.08, 0.08), float2(0.92, 0.15), 0.004);
            float gaugeFrame = FrameMask(i.uv, float2(0.08, 0.16), float2(0.92, 0.21), 0.004);
            color = lerp(color, borderColor, max(histogramFrame, max(percentFrame, gaugeFrame)) * 0.85);
            return float4(color, 1.0);
        }

        float4 DrawManualPreview(v2f_img i, float3 color, float3 panelColor, float3 surfaceColor, float3 accentColor, float3 borderColor)
        {
            float2 exposureMin = float2(0.08, 0.55);
            float2 exposureMax = float2(0.92, 0.67);
            float2 compMin = float2(0.08, 0.31);
            float2 compMax = float2(0.92, 0.43);
            float2 evMin = float2(0.08, 0.07);
            float2 evMax = float2(0.92, 0.19);

            float exposureMask = RectMask(i.uv, exposureMin, exposureMax);
            float2 exposureUv = saturate((i.uv - exposureMin) / max(exposureMax - exposureMin, float2(1e-5, 1e-5)));
            float compMask = RectMask(i.uv, compMin, compMax);
            float2 compUv = saturate((i.uv - compMin) / max(compMax - compMin, float2(1e-5, 1e-5)));
            float evMask = RectMask(i.uv, evMin, evMax);
            float2 evUv = saturate((i.uv - evMin) / max(evMax - evMin, float2(1e-5, 1e-5)));

            color = lerp(color, surfaceColor * 0.92, exposureMask * 0.96);
            color = lerp(color, surfaceColor * 0.92, compMask * 0.96);
            color = lerp(color, surfaceColor * 0.92, evMask * 0.96);

            float exposureNeutral = exposureMask * LineMask(exposureUv.x, 0.5, 0.005);
            float compNeutral = compMask * LineMask(compUv.x, 0.5, 0.005);
            float evNeutral = evMask * LineMask(evUv.x, 0.5, 0.005);
            color = lerp(color, borderColor, max(exposureNeutral, max(compNeutral, evNeutral)) * 0.75);

            float exposureSpan = SpanMask(i.uv, exposureMin, exposureMax, 0.5, _GaugeMarkers.x);
            float exposureMarker = exposureMask * LineMask(exposureUv.x, _GaugeMarkers.x, 0.012);
            color = lerp(color, accentColor * 0.42 + panelColor * 0.58, exposureSpan * 0.88);
            color = lerp(color, accentColor, exposureMarker * 0.96);

            float compSpan = SpanMask(i.uv, compMin, compMax, 0.5, _GaugeMarkers.z);
            float compMarker = compMask * LineMask(compUv.x, _GaugeMarkers.z, 0.012);
            color = lerp(color, float3(1.00, 0.72, 0.25), compSpan * 0.42);
            color = lerp(color, float3(1.00, 0.82, 0.32), compMarker * 0.94);

            float evSpan = SpanMask(i.uv, evMin, evMax, 0.5, _GaugeMarkers.w);
            float evMarker = evMask * LineMask(evUv.x, _GaugeMarkers.w, 0.012);
            color = lerp(color, float3(0.70, 0.56, 1.00), evSpan * 0.42);
            color = lerp(color, float3(0.84, 0.68, 1.00), evMarker * 0.94);

            float exposureFrame = FrameMask(i.uv, exposureMin, exposureMax, 0.004);
            float compFrame = FrameMask(i.uv, compMin, compMax, 0.004);
            float evFrame = FrameMask(i.uv, evMin, evMax, 0.004);
            color = lerp(color, borderColor, max(exposureFrame, max(compFrame, evFrame)) * 0.85);
            return float4(color, 1.0);
        }

        float4 Frag(v2f_img i) : SV_Target
        {
            float darkSkin = _PreviewState.z;
            float alpha = _PreviewState.x;
            float3 panelColor = ResolvePanelColor(darkSkin);
            float3 surfaceColor = ResolveSurfaceColor(darkSkin);
            float3 gridColor = ResolveGridColor(darkSkin);
            float3 borderColor = ResolveBorderColor(darkSkin);
            float3 accentColor = ResolveAccentColor();
            float3 color = panelColor;

            float headerMask = RectMask(i.uv, float2(0.04, 0.90), float2(0.96, 0.97));
            color = lerp(color, accentColor * 0.72 + surfaceColor * 0.28, headerMask * 0.95);

            float statusYMin = 0.80;
            float statusYMax = 0.87;
            float cellWidth = 0.18;
            float gap = 0.03;
            float activeCell = RectMask(i.uv, float2(0.08, statusYMin), float2(0.08 + cellWidth, statusYMax));
            float modeCell = RectMask(i.uv, float2(0.08 + cellWidth + gap, statusYMin), float2(0.08 + (cellWidth * 2.0) + gap, statusYMax));
            float physicalCell = RectMask(i.uv, float2(0.08 + (cellWidth + gap) * 2.0, statusYMin), float2(0.08 + (cellWidth * 3.0) + (gap * 2.0), statusYMax));
            float maskCell = RectMask(i.uv, float2(0.08 + (cellWidth + gap) * 3.0, statusYMin), float2(0.08 + (cellWidth * 4.0) + (gap * 3.0), statusYMax));

            color = lerp(color, lerp(surfaceColor, float3(0.24, 0.86, 0.48), _StatusFlags.x), activeCell * 0.95);
            color = lerp(color, _PreviewState.y > 0.5 ? float3(0.34, 0.56, 1.00) : float3(0.22, 0.92, 0.52), modeCell * 0.95);
            color = lerp(color, lerp(surfaceColor, float3(0.22, 0.84, 1.00), _StatusFlags.z), physicalCell * 0.95);
            color = lerp(color, lerp(surfaceColor, float3(1.00, 0.72, 0.25), _StatusFlags.w), maskCell * 0.95);

            if (_PreviewState.y > 0.5)
                color = DrawManualPreview(i, color, panelColor, surfaceColor, accentColor, borderColor).rgb;
            else
                color = DrawHistogramPreview(i, color, panelColor, surfaceColor, gridColor, accentColor, borderColor).rgb;

            float outerFrame = FrameMask(i.uv, float2(0.04, 0.04), float2(0.96, 0.97), 0.003);
            color = lerp(color, borderColor, outerFrame * 0.82);

            return float4(color * alpha, 1.0);
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
