// Copyright (c) 2023 Advanced Micro Devices, Inc. All rights reserved.
// SPDX-License-Identifier: MIT

using UnityEngine;

namespace VividRP.Runtime
{
    internal readonly struct LpmTonemapperShaderData
    {
        internal readonly Vector4 Params0;
        internal readonly Vector4 Params1;
        internal readonly Vector4 Params2;
        internal readonly Vector4 Params3;
        internal readonly Vector4 Params6;
        internal readonly Vector4 Flags;

        internal LpmTonemapperShaderData(
            Vector4 params0,
            Vector4 params1,
            Vector4 params2,
            Vector4 params3,
            Vector4 params6,
            Vector4 flags)
        {
            Params0 = params0;
            Params1 = params1;
            Params2 = params2;
            Params3 = params3;
            Params6 = params6;
            Flags = flags;
        }
    }

    internal static class LpmTonemapperUtility
    {
        private const float Epsilon = 1e-6f;
        private const float MidGray = 0.18f;
        private static readonly Vector3 Rec709Luma = new(0.212639f, 0.715169f, 0.072192f);

        internal static LpmTonemapperShaderData Create709Ldr(
            bool shoulder,
            float hdrMax,
            float exposure,
            float contrast,
            float shoulderContrast,
            Vector3 saturation,
            Vector3 crosstalk)
        {
            hdrMax = Mathf.Max(Epsilon, hdrMax);
            contrast = Mathf.Clamp01(contrast) + 1f;
            shoulderContrast = Mathf.Max(Epsilon, shoulderContrast);
            saturation = ClampVector(saturation, -1f, 1f) + new Vector3(contrast, contrast, contrast);
            crosstalk = ClampVector(crosstalk, 0f, 1f);

            var midIn = hdrMax * MidGray * Mathf.Pow(2f, -exposure);
            midIn = Mathf.Max(Epsilon, midIn);
            var midOut = MidGray;
            var contrastShoulder = contrast * shoulderContrast;

            var toneScaleBias = BuildToneScaleBias(hdrMax, midIn, midOut, contrast, contrastShoulder);
            var rcpLuma = new Vector3(
                1f / Mathf.Max(Epsilon, Rec709Luma.x),
                1f / Mathf.Max(Epsilon, Rec709Luma.y),
                1f / Mathf.Max(Epsilon, Rec709Luma.z));

            return new LpmTonemapperShaderData(
                new Vector4(saturation.x, saturation.y, saturation.z, contrast),
                new Vector4(toneScaleBias.x, toneScaleBias.y, Rec709Luma.x, Rec709Luma.y),
                new Vector4(Rec709Luma.z, crosstalk.x, crosstalk.y, crosstalk.z),
                new Vector4(rcpLuma.x, rcpLuma.y, rcpLuma.z, 0f),
                new Vector4(shoulderContrast, Rec709Luma.x, Rec709Luma.y, Rec709Luma.z),
                new Vector4(shoulder ? 1f : 0f, 0f, 0f, 0f));
        }

        private static Vector2 BuildToneScaleBias(
            float hdrMax,
            float midIn,
            float midOut,
            float contrast,
            float contrastShoulder)
        {
            var z0 = -SafePow(midIn, contrast);
            var z1 = SafePow(hdrMax, contrastShoulder) * SafePow(midIn, contrast);
            var z2 = SafePow(hdrMax, contrast) * SafePow(midIn, contrastShoulder) * midOut;
            var z3 = SafePow(hdrMax, contrastShoulder) * midOut;
            var z4 = SafePow(midIn, contrastShoulder) * midOut;

            var scaleNumerator = z0 + midOut * SafeDivide(z1 - z2, z3 - z4);
            var toneScale = -SafeDivide(scaleNumerator, z4);

            var w0 = SafePow(hdrMax, contrastShoulder) * SafePow(midIn, contrast);
            var w1 = SafePow(hdrMax, contrast) * SafePow(midIn, contrastShoulder) * midOut;
            var w2 = SafePow(hdrMax, contrastShoulder) * midOut;
            var w3 = SafePow(midIn, contrastShoulder) * midOut;
            var toneBias = SafeDivide(w0 - w1, w2 - w3);

            return new Vector2(toneScale, toneBias);
        }

        private static float SafePow(float value, float power)
        {
            return Mathf.Pow(Mathf.Max(Epsilon, value), power);
        }

        private static float SafeDivide(float numerator, float denominator)
        {
            if (Mathf.Abs(denominator) >= Epsilon)
                return numerator / denominator;

            return numerator / (denominator < 0f ? -Epsilon : Epsilon);
        }

        private static Vector3 ClampVector(Vector3 value, float min, float max)
        {
            return new Vector3(
                Mathf.Clamp(value.x, min, max),
                Mathf.Clamp(value.y, min, max),
                Mathf.Clamp(value.z, min, max));
        }
    }
}
