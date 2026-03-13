Shader "Hidden/VividRP/Editor/Custom Tonemapper Curve"
{
    CGINCLUDE

        #include "UnityCG.cginc"
        #pragma editor_sync_compilation
        #pragma target 3.5

        float4 _CustomToneCurve;
        float4 _ToeSegmentA;
        float4 _ToeSegmentB;
        float4 _MidSegmentA;
        float4 _MidSegmentB;
        float4 _ShoSegmentA;
        float4 _ShoSegmentB;
        float4 _GTToneMap_Params0;
        float4 _GTToneMap_Params1;
        float4 _Variants; // x: disabled state, y: x-scale, z: preview mode, w: unused

        float EvalCustomSegment(float x, float4 segmentA, float2 segmentB)
        {
            const float kOffsetX = segmentA.x;
            const float kOffsetY = segmentA.y;
            const float kScaleX  = segmentA.z;
            const float kScaleY  = segmentA.w;
            const float kLnA     = segmentB.x;
            const float kB       = segmentB.y;

            float x0 = (x - kOffsetX) * kScaleX;
            float y0 = (x0 > 0.0) ? exp(kLnA + kB * log(x0)) : 0.0;
            return y0 * kScaleY + kOffsetY;
        }

        float EvalCustomCurve(float x, float3 curve, float4 toeSegmentA, float2 toeSegmentB, float4 midSegmentA, float2 midSegmentB, float4 shoSegmentA, float2 shoSegmentB)
        {
            float4 segmentA;
            float2 segmentB;

            if (x < curve.y)
            {
                segmentA = toeSegmentA;
                segmentB = toeSegmentB;
            }
            else if (x < curve.z)
            {
                segmentA = midSegmentA;
                segmentB = midSegmentB;
            }
            else
            {
                segmentA = shoSegmentA;
                segmentB = shoSegmentB;
            }

            return EvalCustomSegment(x, segmentA, segmentB);
        }

        // curve: x: inverseWhitePoint, y: x0, z: x1
        float CustomTonemap(float x, float3 curve, float4 toeSegmentA, float2 toeSegmentB, float4 midSegmentA, float2 midSegmentB, float4 shoSegmentA, float2 shoSegmentB)
        {
            float normX = x * curve.x;
            return EvalCustomCurve(normX.x, curve, toeSegmentA, toeSegmentB, midSegmentA, midSegmentB, shoSegmentA, shoSegmentB);
        }

        float W_f(float x, float e0, float e1)
        {
            if (x <= e0)
                return 0.0;

            if (x >= e1)
                return 1.0;

            float a = (x - e0) / (e1 - e0);
            return a * a * (3.0 - 2.0 * a);
        }

        float H_f(float x, float e0, float e1)
        {
            if (x <= e0)
                return 0.0;

            if (x >= e1)
                return 1.0;

            return (x - e0) / (e1 - e0);
        }

        float GranTurismoTonemap(float x, float P, float a, float m, float l, float c, float b)
        {
            float l0 = (P - m) * l / a;
            float L_x = m + a * (x - m);
            float T_x = m * pow(x / m, c) + b;
            float S0 = m + l0;
            float S1 = m + a * l0;
            float C2 = a * P / (P - S1);
            float S_x = P - (P - S1) * exp(-(C2 * (x - S0) / P));
            float w0_x = 1.0 - W_f(x, 0.0, m);
            float w2_x = H_f(x, m + l0, m + l0);
            float w1_x = 1.0 - w0_x - w2_x;
            float f_x = T_x * w0_x + L_x * w1_x + S_x * w2_x;
            return f_x;
        }

        float4 DrawCurve(v2f_img i, float3 background, float3 curveColor)
        {
            float y;
            if (_Variants.z > 0.5)
            {
                y = GranTurismoTonemap(i.uv.x, _GTToneMap_Params0.x, _GTToneMap_Params0.y, _GTToneMap_Params0.z, _GTToneMap_Params0.w, _GTToneMap_Params1.x, _GTToneMap_Params1.y);
            }
            else
            {
                y = CustomTonemap(i.uv.x * _Variants.y,
                    _CustomToneCurve,
                    _ToeSegmentA,
                    _ToeSegmentB.xy,
                    _MidSegmentA,
                    _MidSegmentB.xy,
                    _ShoSegmentA,
                    _ShoSegmentB.xy
                );
            }

            float aa = fwidth(i.uv.y - y);
            float curve = smoothstep(y - aa, y, i.uv.y) - smoothstep(y, y + aa, i.uv.y);
            float3 color = lerp(background, curveColor, curve * _Variants.xxx);

            return float4(color, 1.0);
        }

        float4 FragCurveDark(v2f_img i) : SV_Target
        {
            return DrawCurve(i, (pow(0.196, 2.2)).xxx, (pow(0.7, 2.2)).xxx);
        }

        float4 FragCurveLight(v2f_img i) : SV_Target
        {
            return DrawCurve(i, (pow(0.635, 2.2)).xxx, (pow(0.2, 2.2)).xxx);
        }

    ENDCG

    SubShader
    {
        Tags{ "RenderPipeline" = "HDRenderPipeline" }
        Cull Off ZWrite Off ZTest Always

        // (0) Dark skin
        Pass
        {
            CGPROGRAM

                #pragma vertex vert_img
                #pragma fragment FragCurveDark

            ENDCG
        }

        // (1) Light skin
        Pass
        {
            CGPROGRAM

                #pragma vertex vert_img
                #pragma fragment FragCurveLight

            ENDCG
        }
    }
}
