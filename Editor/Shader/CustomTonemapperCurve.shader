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

        float3 AgXLook(float3 val)
        {
            float3 offset = 0.0;
            float3 slope = 1.0;
            float3 power = 1.35;
            float sat = 1.4;

            val = pow(val * slope + offset, power);

            const float3 lw = float3(0.2126, 0.7152, 0.0722);
            float luma = dot(val, lw);

            return luma + sat * (val - luma);
        }

        float3 AgXDefaultContrastApprox(float3 x)
        {
            float3 x2 = x * x;
            float3 x4 = x2 * x2;

            return +15.5 * x4 * x2
                - 40.14 * x4 * x
                + 31.96 * x4
                - 6.868 * x2 * x
                + 0.4298 * x2
                + 0.1191 * x
                - 0.00232;
        }

        float3 AgX(float3 val)
        {
            const float3x3 agx_mat = float3x3(
                0.842479062253094, 0.0423282422610123, 0.0423756549057051,
                0.0784335999999992, 0.878468636469772, 0.0784336,
                0.0792237451477643, 0.0791661274605434, 0.879142973793104);

            const float min_ev = -12.47393;
            const float max_ev = 4.026069;

            val = mul(agx_mat, val);
            val = clamp(log2(val), min_ev, max_ev);
            val = (val - min_ev) / (max_ev - min_ev);
            val = AgXDefaultContrastApprox(val);

            return val;
        }

        float3 AgXEotf(float3 val)
        {
            const float3x3 agx_mat_inv = float3x3(
                1.19687900512017, -0.0528968517574562, -0.0529716355144438,
                -0.0980208811401368, 1.15190312990417, -0.0980434501171241,
                -0.0990297440797205, -0.0989611768448433, 1.15107367264116);

            val = mul(agx_mat_inv, val);
            return val;
        }

        float3 TonemapAgX(float3 color)
        {
            color = AgX(color);
            color = AgXLook(color);
            color = AgXEotf(color);
            return color;
        }

        float3 KhronosPbrNeutralTonemap(float3 color)
        {
            const float startCompression = 0.8 - 0.04;
            const float desaturation = 0.15;

            float x = min(color.r, min(color.g, color.b));
            float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
            color -= offset;

            float peak = max(color.r, max(color.g, color.b));
            if (peak < startCompression)
                return color;

            const float d = 1.0 - startCompression;
            float newPeak = 1.0 - d * d / (peak + d - startCompression);
            color *= newPeak / peak;

            float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
            return lerp(color, newPeak.xxx, g);
        }

        float4 DrawCurve(v2f_img i, float3 background, float3 curveColor)
        {
            float y;
            if (_Variants.z > 2.5)
            {
                y = KhronosPbrNeutralTonemap(i.uv.xxx).x;
            }
            else if (_Variants.z > 1.5)
            {
                y = TonemapAgX(i.uv.xxx).x;
            }
            else if (_Variants.z > 0.5)
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
