using System;
using Unity.Mathematics;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace UnityEngine.Rendering.Universal
{
    //todo
    //prepare constBuffer in CPU side
    public class LumaPreservingMapperUtil
    {
        public class LpmConstants
        {
            public UInt32[] ctl; // Store data from LPMSetup call 24*4

            public UInt32
                shoulder; // Use optional extra shoulderContrast tuning (set to false if shoulderContrast is 1.0).

            public UInt32 con; // Use first RGB conversion matrix, if 'soft' then 'con' must be true also.
            public UInt32 soft; // Use soft gamut mapping.
            public UInt32 con2; // Use last RGB conversion matrix.
            public UInt32 clip; // Use clipping in last conversion matrix.
            public UInt32 scaleOnly; // Scale only for last conversion matrix (used for 709 HDR to scRGB).
            public UInt32 displayMode; // Display mode of monitor
            public UInt32 pad; // Struct padding
        };

        static LpmConstants getLpmConstants()
        {
            LpmConstants lpmConstants = new LpmConstants();
            lpmConstants.ctl = new UInt32[24 * 4];

            return lpmConstants;
        }

        static void ffxOpAAddOneF3(Vector3 d, Vector3 a, Vector3 b)
        {
            d = a + b;
        }

        static void LpmSetupOut(uint i, uint[] v, ref LpmConstants lpmConstants)
        {
            for (int j = 0; j < 4; ++j)
            {
                lpmConstants.ctl[i * 4 + j] = v[j];
            }
        }


        static LpmConstants FfxCalculateLpmConsts(
            // Path control.
            bool shoulder, // Use optional extra shoulderContrast tuning (set to false if shoulderContrast is 1.0).

            // Prefab start, "LPM_CONFIG_".
            bool con, // Use first RGB conversion matrix, if 'soft' then 'con' must be true also.
            bool soft, // Use soft gamut mapping.
            bool con2, // Use last RGB conversion matrix.
            bool clip, // Use clipping in last conversion matrix.
            bool scaleOnly, // Scale only for last conversion matrix (used for 709 HDR to scRGB).

            // Gamut control, "LPM_COLORS_".
            Vector2 xyRedW,
            Vector2 xyGreenW,
            Vector2 xyBlueW,
            Vector2 xyWhiteW, // Chroma coordinates for working color space.
            Vector2 xyRedO,
            Vector2 xyGreenO,
            Vector2 xyBlueO,
            Vector2 xyWhiteO, // For the output color space.
            Vector2 xyRedC,
            Vector2 xyGreenC,
            Vector2 xyBlueC,
            Vector2 xyWhiteC,
            float scaleC, // For the output container color space (if con2).

            // Prefab end.
            float softGap,
            // Range of 0 to a little over zero, controls how much feather region in out-of-gamut mapping, 0=clip.

            // Tonemapping control.
            float hdrMax, // Maximum input value.
            float exposure, // Number of stops between 'hdrMax' and 18% mid-level on input.
            float contrast, // Input range {0.0 (no extra contrast) to 1.0 (maximum contrast)}.
            float shoulderContrast, // Shoulder shaping, 1.0 = no change (fast path).
            Vector3 saturation, // A per channel adjustment, use <0 decrease, 0=no change, >0 increase.
            Vector3 crosstalk) // One channel must be 1.0, the rest can be <= 1.0 but not zero.
        {
            var LpmConstant = getLpmConstants();

            // Contrast needs to be 1.0 based for no contrast.
            contrast += 1.0f;

            // Saturation is based on contrast.

            saturation.x += contrast;
            saturation.y += contrast;
            saturation.z += contrast;

            // The 'softGap' must actually be above zero.
            softGap = Mathf.Max(softGap, (1.0f / 1024.0f));

            float midIn = hdrMax * (0.18f) * math.exp2(-exposure);
            float midOut = 0.18f;

            Vector2 toneScaleBias;
            float cs = contrast * shoulderContrast;
            float z0 = -Mathf.Pow(midIn, contrast);
            float z1 = Mathf.Pow(hdrMax, cs) * Mathf.Pow(midIn, contrast);
            float z2 = Mathf.Pow(hdrMax, contrast) * Mathf.Pow(midIn, cs) * midOut;
            float z3 = Mathf.Pow(hdrMax, cs) * midOut;
            float z4 = Mathf.Pow(midIn, cs) * midOut;
            toneScaleBias.x = -((z0 + (midOut * (z1 - z2)) * math.rcp(z3 - z4)) * math.rcp(z4));

            float w0 = Mathf.Pow(hdrMax, cs) * Mathf.Pow(midIn, contrast);
            float w1 = Mathf.Pow(hdrMax, contrast) * Mathf.Pow(midIn, cs) * midOut;
            float w2 = Mathf.Pow(hdrMax, cs) * midOut;
            float w3 = Mathf.Pow(midIn, cs) * midOut;
            toneScaleBias.y = (w0 - w1) * math.rcp(w2 - w3);

            Vector3 lumaW = Vector3.zero;
            Vector3 rgbToXyzXW = Vector3.zero;
            Vector3 rgbToXyzYW = Vector3.zero;
            Vector3 rgbToXyzZW = Vector3.zero;
            LpmColRgbToXyz(ref rgbToXyzXW, ref rgbToXyzYW, ref rgbToXyzZW, xyRedW, xyGreenW, xyBlueW, xyWhiteW);

            // Use the Y vector of the matrix for the associated luma coef.
            // For safety, make sure the vector sums to 1.0.
            ffxOpAMulOneF3(ref lumaW, rgbToXyzYW, math.rcp(rgbToXyzYW[0] + rgbToXyzYW[1] + rgbToXyzYW[2]));

            // The 'lumaT' for crosstalk mapping is always based on the output color space, unless soft conversion is not used.
            Vector3 lumaT = Vector3.zero;
            Vector3 rgbToXyzXO = Vector3.zero;
            Vector3 rgbToXyzYO = Vector3.zero;
            Vector3 rgbToXyzZO = Vector3.zero;
            LpmColRgbToXyz(ref rgbToXyzXO, ref rgbToXyzYO, ref rgbToXyzZO, xyRedO, xyGreenO, xyBlueO, xyWhiteO);

            if (soft)
                ffxOpACpyF3(ref lumaT, rgbToXyzYO);
            else
                ffxOpACpyF3(ref lumaT, rgbToXyzYW);

            ffxOpAMulOneF3(ref lumaT, lumaT, math.rcp(lumaT[0] + lumaT[1] + lumaT[2]));
            Vector3 rcpLumaT = Vector3.zero;
            ffxOpARcpF3(ref rcpLumaT, lumaT);

            Vector2 softGap2 = Vector2.zero;
            if (soft)
            {
                softGap2[0] = softGap;
                softGap2[1] = ((1.0f) - softGap) * math.rcp(softGap * (0.693147180559f));
            }

            // First conversion is always working to output.
            Vector3 conR = Vector3.zero;
            Vector3 conG = Vector3.zero;
            Vector3 conB = Vector3.zero;

            if (con)
            {
                Vector3 xyzToRgbRO = Vector3.zero;
                Vector3 xyzToRgbGO = Vector3.zero;
                Vector3 xyzToRgbBO = Vector3.zero;
                LpmMatInv3x3(ref xyzToRgbRO, ref xyzToRgbGO, ref xyzToRgbBO, rgbToXyzXO, rgbToXyzYO, rgbToXyzZO);
                LpmMatMul3x3(ref conR, ref conG, ref conB, xyzToRgbRO, xyzToRgbGO, xyzToRgbBO, rgbToXyzXW, rgbToXyzYW,
                    rgbToXyzZW);
            }

            // The last conversion is always output to container.
            Vector3 con2R = Vector3.zero;
            Vector3 con2G = Vector3.zero;
            Vector3 con2B = Vector3.zero;

            if (con2)
            {
                Vector3 rgbToXyzXC = Vector3.zero;
                Vector3 rgbToXyzYC = Vector3.zero;
                Vector3 rgbToXyzZC = Vector3.zero;
                LpmColRgbToXyz(ref rgbToXyzXC, ref rgbToXyzYC, ref rgbToXyzZC, xyRedC, xyGreenC, xyBlueC, xyWhiteC);

                Vector3 xyzToRgbRC = Vector3.zero;
                Vector3 xyzToRgbGC = Vector3.zero;
                Vector3 xyzToRgbBC = Vector3.zero;
                LpmMatInv3x3(ref xyzToRgbRC, ref xyzToRgbGC, ref xyzToRgbBC, rgbToXyzXC, rgbToXyzYC, rgbToXyzZC);
                LpmMatMul3x3(ref con2R, ref con2G, ref con2B, xyzToRgbRC, xyzToRgbGC, xyzToRgbBC, rgbToXyzXO,
                    rgbToXyzYO,
                    rgbToXyzZO);
                ffxOpAMulOneF3(ref con2R, con2R, scaleC);
                ffxOpAMulOneF3(ref con2G, con2G, scaleC);
                ffxOpAMulOneF3(ref con2B, con2B, scaleC);
            }

            if (scaleOnly)
                con2R[0] = scaleC;


            // Pack into control block.
            uint[] map0 = new uint[4];
            map0[0] = ffxAsUInt32(saturation[0]);
            map0[1] = ffxAsUInt32(saturation[1]);
            map0[2] = ffxAsUInt32(saturation[2]);
            map0[3] = ffxAsUInt32(contrast);
            LpmSetupOut(0, map0, ref LpmConstant);

            uint[] map1 = new uint[4];
            map1[0] = ffxAsUInt32(toneScaleBias[0]);
            map1[1] = ffxAsUInt32(toneScaleBias[1]);
            map1[2] = ffxAsUInt32(lumaT[0]);
            map1[3] = ffxAsUInt32(lumaT[1]);
            LpmSetupOut(1, map1, ref LpmConstant);

            uint[] map2 = new uint[4];
            ;
            map2[0] = ffxAsUInt32(lumaT[2]);
            map2[1] = ffxAsUInt32(crosstalk[0]);
            map2[2] = ffxAsUInt32(crosstalk[1]);
            map2[3] = ffxAsUInt32(crosstalk[2]);
            LpmSetupOut(2, map2, ref LpmConstant);

            uint[] map3 = new uint[4];
            map3[0] = ffxAsUInt32(rcpLumaT[0]);
            map3[1] = ffxAsUInt32(rcpLumaT[1]);
            map3[2] = ffxAsUInt32(rcpLumaT[2]);
            map3[3] = ffxAsUInt32(con2R[0]);
            LpmSetupOut(3, map3, ref LpmConstant);

            var map4 = new uint[4];
            map4[0] = ffxAsUInt32(con2R[1]);
            map4[1] = ffxAsUInt32(con2R[2]);
            map4[2] = ffxAsUInt32(con2G[0]);
            map4[3] = ffxAsUInt32(con2G[1]);
            LpmSetupOut(4, map4, ref LpmConstant);

            var map5 = new uint[4];
            map5[0] = ffxAsUInt32(con2G[2]);
            map5[1] = ffxAsUInt32(con2B[0]);
            map5[2] = ffxAsUInt32(con2B[1]);
            map5[3] = ffxAsUInt32(con2B[2]);
            LpmSetupOut(5, map5, ref LpmConstant);

            var map6 = new uint[4];
            map6[0] = ffxAsUInt32(shoulderContrast);
            map6[1] = ffxAsUInt32(lumaW[0]);
            map6[2] = ffxAsUInt32(lumaW[1]);
            map6[3] = ffxAsUInt32(lumaW[2]);
            LpmSetupOut(6, map6, ref LpmConstant);

            var map7 = new uint[4];
            ;
            map7[0] = ffxAsUInt32(softGap2[0]);
            map7[1] = ffxAsUInt32(softGap2[1]);
            map7[2] = ffxAsUInt32(conR[0]);
            map7[3] = ffxAsUInt32(conR[1]);
            LpmSetupOut(7, map7, ref LpmConstant);

            var map8 = new uint[4];
            ;
            map8[0] = ffxAsUInt32(conR[2]);
            map8[1] = ffxAsUInt32(conG[0]);
            map8[2] = ffxAsUInt32(conG[1]);
            map8[3] = ffxAsUInt32(conG[2]);
            LpmSetupOut(8, map8, ref LpmConstant);

            var map9 = new uint[4];
            ;
            map9[0] = ffxAsUInt32(conB[0]);
            map9[1] = ffxAsUInt32(conB[1]);
            map9[2] = ffxAsUInt32(conB[2]);
            map9[3] = ffxAsUInt32(0);
            LpmSetupOut(9, map9, ref LpmConstant);

            // // Packed 16-bit part of control block.
            // uint4 map16 = uint4.zero;
            // Vector2 map16x = Vector2.zero;
            // Vector2 map16y = Vector2.zero;
            // Vector2 map16z = Vector2.zero;
            // Vector2 map16w = Vector2.zero;
            // map16x[0] = saturation[0];
            // map16x[1] = saturation[1];
            // map16y[0] = saturation[2];
            // map16y[1] = contrast;
            // map16z[0] = toneScaleBias[0];
            // map16z[1] = toneScaleBias[1];
            // map16w[0] = lumaT[0];
            // map16w[1] = lumaT[1];
            // map16[0] = ffxPackHalf2x16(map16x);
            // map16[1] = ffxPackHalf2x16(map16y);
            // map16[2] = ffxPackHalf2x16(map16z);
            // map16[3] = ffxPackHalf2x16(map16w);
            // LpmSetupOut(16, map16);
            //
            // FfxUInt32x4 map17;
            // FfxFloat32x2 map17x;
            // FfxFloat32x2 map17y;
            // FfxFloat32x2 map17z;
            // FfxFloat32x2 map17w;
            // map17x[0] = lumaT[2];
            // map17x[1] = crosstalk[0];
            // map17y[0] = crosstalk[1];
            // map17y[1] = crosstalk[2];
            // map17z[0] = rcpLumaT[0];
            // map17z[1] = rcpLumaT[1];
            // map17w[0] = rcpLumaT[2];
            // map17w[1] = con2R[0];
            // map17[0] = ffxPackHalf2x16(map17x);
            // map17[1] = ffxPackHalf2x16(map17y);
            // map17[2] = ffxPackHalf2x16(map17z);
            // map17[3] = ffxPackHalf2x16(map17w);
            // LpmSetupOut(17, map17);
            //
            // FfxUInt32x4 map18;
            // FfxFloat32x2 map18x;
            // FfxFloat32x2 map18y;
            // FfxFloat32x2 map18z;
            // FfxFloat32x2 map18w;
            // map18x[0] = con2R[1];
            // map18x[1] = con2R[2];
            // map18y[0] = con2G[0];
            // map18y[1] = con2G[1];
            // map18z[0] = con2G[2];
            // map18z[1] = con2B[0];
            // map18w[0] = con2B[1];
            // map18w[1] = con2B[2];
            // map18[0] = ffxPackHalf2x16(map18x);
            // map18[1] = ffxPackHalf2x16(map18y);
            // map18[2] = ffxPackHalf2x16(map18z);
            // map18[3] = ffxPackHalf2x16(map18w);
            // LpmSetupOut(18, map18);
            //
            // FfxUInt32x4 map19;
            // FfxFloat32x2 map19x;
            // FfxFloat32x2 map19y;
            // FfxFloat32x2 map19z;
            // FfxFloat32x2 map19w;
            // map19x[0] = shoulderContrast;
            // map19x[1] = lumaW[0];
            // map19y[0] = lumaW[1];
            // map19y[1] = lumaW[2];
            // map19z[0] = softGap2[0];
            // map19z[1] = softGap2[1];
            // map19w[0] = conR[0];
            // map19w[1] = conR[1];
            // map19[0] = ffxPackHalf2x16(map19x);
            // map19[1] = ffxPackHalf2x16(map19y);
            // map19[2] = ffxPackHalf2x16(map19z);
            // map19[3] = ffxPackHalf2x16(map19w);
            // LpmSetupOut(19, map19);
            //
            // FfxUInt32x4 map20;
            // FfxFloat32x2 map20x;
            // FfxFloat32x2 map20y;
            // FfxFloat32x2 map20z;
            // FfxFloat32x2 map20w;
            // map20x[0] = conR[2];
            // map20x[1] = conG[0];
            // map20y[0] = conG[1];
            // map20y[1] = conG[2];
            // map20z[0] = conB[0];
            // map20z[1] = conB[1];
            // map20w[0] = conB[2];
            // map20w[1] = 0.0;
            // map20[0] = ffxPackHalf2x16(map20x);
            // map20[1] = ffxPackHalf2x16(map20y);
            // map20[2] = ffxPackHalf2x16(map20z);
            // map20[3] = ffxPackHalf2x16(map20w);
            // LpmSetupOut(20, map20);
            return LpmConstant;
        }

        #region FFXMathHelper

        // Computes z from xy, returns xyz.
        static void LpmColXyToZ(ref Vector3 d, Vector2 s)
        {
            d[0] = s[0];
            d[1] = s[1];
            d[2] = (1.0f) - (s[0] + s[1]);
        }

        static void LpmMatTrn3x3(ref Vector3 ox, ref Vector3 oy, ref Vector3 oz,
            Vector3 ix, Vector3 iy, Vector3 iz)
        {
            ox[0] = ix[0];
            ox[1] = iy[0];
            ox[2] = iz[0];
            oy[0] = ix[1];
            oy[1] = iy[1];
            oy[2] = iz[1];
            oz[0] = ix[2];
            oz[1] = iy[2];
            oz[2] = iz[2];
        }

        static void ffxOpAMulOneF3(ref Vector3 d, Vector3 a, float b)
        {
            d[0] = a[0] * b;
            d[1] = a[1] * b;
            d[2] = a[2] * b;
        }

        static void LpmMatInv3x3(ref Vector3 ox, ref Vector3 oy, ref Vector3 oz,
            Vector3 ix, Vector3 iy, Vector3 iz)
        {
            float i = math.rcp(ix[0] * (iy[1] * iz[2] - iz[1] * iy[2]) - ix[1] * (iy[0] * iz[2] - iy[2] * iz[0]) +
                               ix[2] * (iy[0] * iz[1] - iy[1] * iz[0]));
            ox[0] = (iy[1] * iz[2] - iz[1] * iy[2]) * i;
            ox[1] = (ix[2] * iz[1] - ix[1] * iz[2]) * i;
            ox[2] = (ix[1] * iy[2] - ix[2] * iy[1]) * i;
            oy[0] = (iy[2] * iz[0] - iy[0] * iz[2]) * i;
            oy[1] = (ix[0] * iz[2] - ix[2] * iz[0]) * i;
            oy[2] = (iy[0] * ix[2] - ix[0] * iy[2]) * i;
            oz[0] = (iy[0] * iz[1] - iz[0] * iy[1]) * i;
            oz[1] = (iz[0] * ix[1] - ix[0] * iz[1]) * i;
            oz[2] = (ix[0] * iy[1] - iy[0] * ix[1]) * i;
        }

        static float ffxDot3(Vector3 a, Vector3 b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        static void ffxOpAMulF3(ref Vector3 d, Vector3 a, Vector3 b)
        {
            d[0] = a[0] * b[0];
            d[1] = a[1] * b[1];
            d[2] = a[2] * b[2];
        }

        static void ffxOpACpyF3(ref Vector3 d, Vector3 a)
        {
            d[0] = a[0];
            d[1] = a[1];
            d[2] = a[2];
            return;
        }

        static void LpmColRgbToXyz(ref Vector3 ox, ref Vector3 oy,
            ref Vector3 oz,
            Vector2 r, Vector2 g, Vector2 b, Vector2 w)
        {
            // Expand from xy to xyz.
            Vector3 rz = Vector3.zero;
            Vector3 gz = Vector3.zero;
            Vector3 bz = Vector3.zero;
            LpmColXyToZ(ref rz, r);
            LpmColXyToZ(ref gz, g);
            LpmColXyToZ(ref bz, b);

            Vector3 r3 = Vector3.zero;
            Vector3 g3 = Vector3.zero;
            Vector3 b3 = Vector3.zero;
            LpmMatTrn3x3(ref r3, ref g3, ref b3, rz, gz, bz);

            // Convert white xyz to XYZ.
            Vector3 w3 = Vector3.zero;
            LpmColXyToZ(ref w3, w);
            ffxOpAMulOneF3(ref w3, w3, math.rcp(w[1]));

            // Compute xyz to XYZ scalars for primaries.
            Vector3 rv = Vector3.zero;
            Vector3 gv = Vector3.zero;
            Vector3 bv = Vector3.zero;
            LpmMatInv3x3(ref rv, ref gv, ref bv, r3, g3, b3);

            Vector3 s = Vector3.zero;
            ;
            s[0] = ffxDot3(rv, w3);
            s[1] = ffxDot3(gv, w3);
            s[2] = ffxDot3(bv, w3);

            // Scale.
            ffxOpAMulF3(ref ox, r3, s);
            ffxOpAMulF3(ref oy, g3, s);
            ffxOpAMulF3(ref oz, b3, s);
        }

        static void ffxOpARcpF3(ref Vector3 d, Vector3 a)
        {
            d[0] = math.rcp(a[0]);
            d[1] = math.rcp(a[1]);
            d[2] = math.rcp(a[2]);
            return;
        }

        static void LpmMatMul3x3(
            ref Vector3 ox, ref Vector3 oy, ref Vector3 oz,
            Vector3 ax, Vector3 ay, Vector3 az, Vector3 bx, Vector3 by, Vector3 bz)
        {
            Vector3 bx2 = Vector3.zero;
            Vector3 by2 = Vector3.zero;
            Vector3 bz2 = Vector3.zero;

            LpmMatTrn3x3(ref bx2, ref by2, ref bz2, bx, by, bz);
            ox[0] = ffxDot3(ax, bx2);
            ox[1] = ffxDot3(ax, by2);
            ox[2] = ffxDot3(ax, bz2);
            oy[0] = ffxDot3(ay, bx2);
            oy[1] = ffxDot3(ay, by2);
            oy[2] = ffxDot3(ay, bz2);
            oz[0] = ffxDot3(az, bx2);
            oz[1] = ffxDot3(az, by2);
            oz[2] = ffxDot3(az, bz2);
        }

        static uint ffxAsUInt32(float x)
        {
            unsafe
            {
                return *(uint*)&x;
            }
        }

 
        #endregion
    }
}