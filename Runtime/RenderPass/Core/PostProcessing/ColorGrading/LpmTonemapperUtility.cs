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
        internal readonly Vector4 Params4;
        internal readonly Vector4 Params5;
        internal readonly Vector4 Params6;
        internal readonly Vector4 Params7;
        internal readonly Vector4 Params8;
        internal readonly Vector4 Params9;
        internal readonly Vector4 Flags;
        internal readonly Vector4 Flags2;
        internal readonly Vector4 ToneParams;
        internal readonly Vector4 ToneLuma;
        internal readonly Vector4 ScaleBiasSoftGap;
        internal readonly Vector4 TargetLuma;
        internal readonly Vector4 RcpTargetLuma;
        internal readonly Vector4 Crosstalk;
        internal readonly Vector4 ConR;
        internal readonly Vector4 ConG;
        internal readonly Vector4 ConB;
        internal readonly Vector4 Con2R;
        internal readonly Vector4 Con2G;
        internal readonly Vector4 Con2B;

        internal LpmTonemapperShaderData(
            Vector4 params0,
            Vector4 params1,
            Vector4 params2,
            Vector4 params3,
            Vector4 params4,
            Vector4 params5,
            Vector4 params6,
            Vector4 params7,
            Vector4 params8,
            Vector4 params9,
            Vector4 flags,
            Vector4 flags2)
        {
            Params0 = params0;
            Params1 = params1;
            Params2 = params2;
            Params3 = params3;
            Params4 = params4;
            Params5 = params5;
            Params6 = params6;
            Params7 = params7;
            Params8 = params8;
            Params9 = params9;
            Flags = flags;
            Flags2 = flags2;

            var usesSoft = flags.z > 0.5f;
            ToneParams = params0;
            ToneLuma = usesSoft
                ? new Vector4(params6.y, params6.z, params6.w, params6.x)
                : new Vector4(params1.z, params1.w, params2.x, params6.x);
            ScaleBiasSoftGap = new Vector4(params1.x, params1.y, params7.x, params7.y);
            TargetLuma = new Vector4(params1.z, params1.w, params2.x, flags.x);
            RcpTargetLuma = new Vector4(params3.x, params3.y, params3.z, params3.w);
            Crosstalk = new Vector4(params2.y, params2.z, params2.w, flags.z);
            ConR = new Vector4(params7.z, params7.w, params8.x, flags.w);
            ConG = new Vector4(params8.y, params8.z, params8.w, flags2.x);
            ConB = new Vector4(params9.x, params9.y, params9.z, flags2.y);
            Con2R = new Vector4(params3.w, params4.x, params4.y, 0f);
            Con2G = new Vector4(params4.z, params4.w, params5.x, 0f);
            Con2B = new Vector4(params5.y, params5.z, params5.w, 0f);
        }
    }

    internal static class LpmTonemapperUtility
    {
        private const float Epsilon = 1e-6f;
        private const float MidGray = 0.18f;
        private const float SoftGapMinimum = 1f / 1024f;
        private const float SoftGapLogScale = 0.693147180559f;

        private static readonly Vector2 D65 = new(0.3127f, 0.3290f);
        private static readonly Vector2 Rec709Red = new(0.64f, 0.33f);
        private static readonly Vector2 Rec709Green = new(0.30f, 0.60f);
        private static readonly Vector2 Rec709Blue = new(0.15f, 0.06f);
        private static readonly Vector2 DisplayP3Red = new(0.680f, 0.320f);
        private static readonly Vector2 DisplayP3Green = new(0.265f, 0.690f);
        private static readonly Vector2 DisplayP3Blue = new(0.150f, 0.060f);
        private static readonly Vector2 Rec2020Red = new(0.708f, 0.292f);
        private static readonly Vector2 Rec2020Green = new(0.170f, 0.797f);
        private static readonly Vector2 Rec2020Blue = new(0.131f, 0.046f);

        internal static LpmTonemapperShaderData Create709Ldr(
            bool shoulder,
            float hdrMax,
            float exposure,
            float contrast,
            float shoulderContrast,
            Vector3 saturation,
            Vector3 crosstalk)
        {
            return CreateForLinearOutput(
                LpmColorGamut.Rec709,
                LpmColorGamut.Rec709,
                shoulder,
                SoftGapMinimum,
                hdrMax,
                exposure,
                contrast,
                shoulderContrast,
                saturation,
                crosstalk);
        }

        internal static LpmTonemapperShaderData CreateForLinearOutput(
            LpmColorGamut workingGamut,
            LpmColorGamut outputGamut,
            bool shoulder,
            float softGap,
            float hdrMax,
            float exposure,
            float contrast,
            float shoulderContrast,
            Vector3 saturation,
            Vector3 crosstalk)
        {
            var convertGamut = workingGamut != outputGamut;
            return Create(
                shoulder,
                convertGamut,
                convertGamut,
                false,
                false,
                false,
                workingGamut,
                outputGamut,
                outputGamut,
                1f,
                softGap,
                hdrMax,
                exposure,
                contrast,
                shoulderContrast,
                saturation,
                crosstalk);
        }

        internal static LpmTonemapperShaderData Create(
            bool shoulder,
            bool con,
            bool soft,
            bool con2,
            bool clip,
            bool scaleOnly,
            LpmColorGamut workingGamut,
            LpmColorGamut outputGamut,
            LpmColorGamut containerGamut,
            float scaleC,
            float softGap,
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
            softGap = Mathf.Max(SoftGapMinimum, softGap);
            con |= soft;

            var midIn = hdrMax * MidGray * Mathf.Pow(2f, -exposure);
            midIn = Mathf.Max(Epsilon, midIn);
            var toneScaleBias = BuildToneScaleBias(hdrMax, midIn, MidGray, contrast, contrast * shoulderContrast);

            var working = GetPrimaries(workingGamut);
            var output = GetPrimaries(outputGamut);
            var container = GetPrimaries(containerGamut);

            RgbToXyz(out var rgbToXyzXW, out var rgbToXyzYW, out var rgbToXyzZW, working.Red, working.Green, working.Blue, D65);
            RgbToXyz(out var rgbToXyzXO, out var rgbToXyzYO, out var rgbToXyzZO, output.Red, output.Green, output.Blue, D65);

            var lumaW = NormalizeLuma(rgbToXyzYW);
            var lumaT = NormalizeLuma(soft ? rgbToXyzYO : rgbToXyzYW);
            var rcpLumaT = new Vector3(
                SafeDivide(1f, lumaT.x),
                SafeDivide(1f, lumaT.y),
                SafeDivide(1f, lumaT.z));

            var softGap2 = soft
                ? new Vector2(softGap, SafeDivide(1f - softGap, softGap * SoftGapLogScale))
                : Vector2.zero;

            var conR = Vector3.zero;
            var conG = Vector3.zero;
            var conB = Vector3.zero;
            if (con)
            {
                MatInv3x3(out var xyzToRgbRO, out var xyzToRgbGO, out var xyzToRgbBO, rgbToXyzXO, rgbToXyzYO, rgbToXyzZO);
                MatMul3x3(
                    out conR,
                    out conG,
                    out conB,
                    xyzToRgbRO,
                    xyzToRgbGO,
                    xyzToRgbBO,
                    rgbToXyzXW,
                    rgbToXyzYW,
                    rgbToXyzZW);
            }

            var con2R = Vector3.zero;
            var con2G = Vector3.zero;
            var con2B = Vector3.zero;
            if (con2)
            {
                RgbToXyz(out var rgbToXyzXC, out var rgbToXyzYC, out var rgbToXyzZC, container.Red, container.Green, container.Blue, D65);
                MatInv3x3(out var xyzToRgbRC, out var xyzToRgbGC, out var xyzToRgbBC, rgbToXyzXC, rgbToXyzYC, rgbToXyzZC);
                MatMul3x3(
                    out con2R,
                    out con2G,
                    out con2B,
                    xyzToRgbRC,
                    xyzToRgbGC,
                    xyzToRgbBC,
                    rgbToXyzXO,
                    rgbToXyzYO,
                    rgbToXyzZO);
                con2R *= scaleC;
                con2G *= scaleC;
                con2B *= scaleC;
            }

            if (scaleOnly)
                con2R.x = scaleC;

            return new LpmTonemapperShaderData(
                new Vector4(saturation.x, saturation.y, saturation.z, contrast),
                new Vector4(toneScaleBias.x, toneScaleBias.y, lumaT.x, lumaT.y),
                new Vector4(lumaT.z, crosstalk.x, crosstalk.y, crosstalk.z),
                new Vector4(rcpLumaT.x, rcpLumaT.y, rcpLumaT.z, con2R.x),
                new Vector4(con2R.y, con2R.z, con2G.x, con2G.y),
                new Vector4(con2G.z, con2B.x, con2B.y, con2B.z),
                new Vector4(shoulderContrast, lumaW.x, lumaW.y, lumaW.z),
                new Vector4(softGap2.x, softGap2.y, conR.x, conR.y),
                new Vector4(conR.z, conG.x, conG.y, conG.z),
                new Vector4(conB.x, conB.y, conB.z, 0f),
                new Vector4(shoulder ? 1f : 0f, con ? 1f : 0f, soft ? 1f : 0f, con2 ? 1f : 0f),
                new Vector4(clip ? 1f : 0f, scaleOnly ? 1f : 0f, 0f, 0f));
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

        private static LpmColorPrimaries GetPrimaries(LpmColorGamut gamut)
        {
            return gamut switch
            {
                LpmColorGamut.DisplayP3 => new LpmColorPrimaries(DisplayP3Red, DisplayP3Green, DisplayP3Blue),
                LpmColorGamut.Rec2020 => new LpmColorPrimaries(Rec2020Red, Rec2020Green, Rec2020Blue),
                _ => new LpmColorPrimaries(Rec709Red, Rec709Green, Rec709Blue)
            };
        }

        private static Vector3 NormalizeLuma(Vector3 value)
        {
            return value * SafeDivide(1f, value.x + value.y + value.z);
        }

        private static Vector3 XyToZ(Vector2 value)
        {
            return new Vector3(value.x, value.y, 1f - (value.x + value.y));
        }

        private static void RgbToXyz(
            out Vector3 ox,
            out Vector3 oy,
            out Vector3 oz,
            Vector2 red,
            Vector2 green,
            Vector2 blue,
            Vector2 white)
        {
            var rz = XyToZ(red);
            var gz = XyToZ(green);
            var bz = XyToZ(blue);
            MatTrn3x3(out var r3, out var g3, out var b3, rz, gz, bz);

            var w3 = XyToZ(white) * SafeDivide(1f, white.y);
            MatInv3x3(out var rv, out var gv, out var bv, r3, g3, b3);

            var scale = new Vector3(Vector3.Dot(rv, w3), Vector3.Dot(gv, w3), Vector3.Dot(bv, w3));
            ox = Vector3.Scale(r3, scale);
            oy = Vector3.Scale(g3, scale);
            oz = Vector3.Scale(b3, scale);
        }

        private static void MatInv3x3(
            out Vector3 ox,
            out Vector3 oy,
            out Vector3 oz,
            Vector3 ix,
            Vector3 iy,
            Vector3 iz)
        {
            var determinant =
                ix.x * (iy.y * iz.z - iz.y * iy.z) -
                ix.y * (iy.x * iz.z - iy.z * iz.x) +
                ix.z * (iy.x * iz.y - iy.y * iz.x);
            var invDeterminant = SafeDivide(1f, determinant);

            ox = new Vector3(
                (iy.y * iz.z - iz.y * iy.z) * invDeterminant,
                (ix.z * iz.y - ix.y * iz.z) * invDeterminant,
                (ix.y * iy.z - ix.z * iy.y) * invDeterminant);
            oy = new Vector3(
                (iy.z * iz.x - iy.x * iz.z) * invDeterminant,
                (ix.x * iz.z - ix.z * iz.x) * invDeterminant,
                (iy.x * ix.z - ix.x * iy.z) * invDeterminant);
            oz = new Vector3(
                (iy.x * iz.y - iz.x * iy.y) * invDeterminant,
                (iz.x * ix.y - ix.x * iz.y) * invDeterminant,
                (ix.x * iy.y - iy.x * ix.y) * invDeterminant);
        }

        private static void MatTrn3x3(
            out Vector3 ox,
            out Vector3 oy,
            out Vector3 oz,
            Vector3 ix,
            Vector3 iy,
            Vector3 iz)
        {
            ox = new Vector3(ix.x, iy.x, iz.x);
            oy = new Vector3(ix.y, iy.y, iz.y);
            oz = new Vector3(ix.z, iy.z, iz.z);
        }

        private static void MatMul3x3(
            out Vector3 ox,
            out Vector3 oy,
            out Vector3 oz,
            Vector3 ax,
            Vector3 ay,
            Vector3 az,
            Vector3 bx,
            Vector3 by,
            Vector3 bz)
        {
            MatTrn3x3(out var bx2, out var by2, out var bz2, bx, by, bz);
            ox = new Vector3(Vector3.Dot(ax, bx2), Vector3.Dot(ax, by2), Vector3.Dot(ax, bz2));
            oy = new Vector3(Vector3.Dot(ay, bx2), Vector3.Dot(ay, by2), Vector3.Dot(ay, bz2));
            oz = new Vector3(Vector3.Dot(az, bx2), Vector3.Dot(az, by2), Vector3.Dot(az, bz2));
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

        private readonly struct LpmColorPrimaries
        {
            internal readonly Vector2 Red;
            internal readonly Vector2 Green;
            internal readonly Vector2 Blue;

            internal LpmColorPrimaries(Vector2 red, Vector2 green, Vector2 blue)
            {
                Red = red;
                Green = green;
                Blue = blue;
            }
        }
    }
}
