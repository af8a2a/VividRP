using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    //copy from NRD
    public static class Sequence
    {
        const uint ML_BAYER_LINEAR = 0;
        const uint ML_BAYER_REVERSEBITS = 1;
        private const uint ML_BAYER_DEFAULT = ML_BAYER_REVERSEBITS;

        // https://en.wikipedia.org/wiki/Ordered_dithering


        // Bit operations
        static uint ReverseBits4(uint x)
        {
            x = ((x & 0x5) << 1) | ((x & 0xA) >> 1);
            x = ((x & 0x3) << 2) | ((x & 0xC) >> 2);

            return x;
        }

        static uint ReverseBits32(uint x)
        {
            x = (x << 16) | (x >> 16);
            x = ((x & 0x55555555) << 1) | ((x & 0xAAAAAAAA) >> 1);
            x = ((x & 0x33333333) << 2) | ((x & 0xCCCCCCCC) >> 2);
            x = ((x & 0x0F0F0F0F) << 4) | ((x & 0xF0F0F0F0) >> 4);
            x = ((x & 0x00FF00FF) << 8) | ((x & 0xFF00FF00) >> 8);

            return x;
        }


        // RESULT: [0; 15]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Bayer4x4ui(uint2 samplePos, uint frameIndex, uint mode = ML_BAYER_DEFAULT)
        {
            uint2 samplePosWrap = samplePos & 3;
            uint a = 2068378560 * (1 - (samplePosWrap.x >> 1)) + 1500172770 * (samplePosWrap.x >> 1);
            uint b = (samplePosWrap.y + ((samplePosWrap.x & 1) << 2)) << 2;

            uint sampleOffset = mode == ML_BAYER_REVERSEBITS ? ReverseBits4(frameIndex) : frameIndex;

            return ((a >> (int)b) + sampleOffset) & 0xF;
        }


        // Halton
        public static float Halton(uint n, uint @base)
        {
            float a = 1.0f;
            float b = 0.0f;
            float baseInv = 1.0f / (float)@base;

            while (n != 0)
            {
                a *= baseInv;
                b += a * (n % @base);
                n = (uint)(n * baseInv);
            }

            return b;
        }

        public static float Halton2(uint n) // optimized Halton( n, 2 )
        {
            return ReverseBits32(n) * 2.3283064365386963e-10f;
        }

        public static float Halton1D(uint n)
        {
            return Halton2(n);
        }

        public static float2 Halton2D(uint n)
        {
            return new float2(Halton2(n), Halton(n, 3));
        }
    }
}