using UnityEngine;

namespace VividRP.Runtime
{
    internal static class HaltonJitter
    {
        public static float Halton(int index, int radix)
        {
            float result = 0f;
            float fraction = 1f / radix;
            while (index > 0)
            {
                result += (index % radix) * fraction;
                index /= radix;
                fraction /= radix;
            }

            return result;
        }

        public static Vector2 Get(int frameIndex, int sampleCount)
        {
            sampleCount = Mathf.Max(1, sampleCount);
            int index = (frameIndex % sampleCount) + 1;
            return new Vector2(Halton(index, 2) - 0.5f, Halton(index, 3) - 0.5f);
        }
    }
}
