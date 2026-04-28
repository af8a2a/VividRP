using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core
{
    internal static class TSRUpscalerUtility
    {
        public const float NativeAAUpscaleRatio = 1.0f;
        public const float QualityUpscaleRatio = 1.5f;
        public const float BalancedUpscaleRatio = 1.7f;
        public const float PerformanceUpscaleRatio = 2.0f;
        public const float UltraPerformanceUpscaleRatio = 3.0f;

        public static float GetUpscaleRatio(VividTsrQualityMode quality)
        {
            return quality switch
            {
                VividTsrQualityMode.NativeAA => NativeAAUpscaleRatio,
                VividTsrQualityMode.Quality => QualityUpscaleRatio,
                VividTsrQualityMode.Balanced => BalancedUpscaleRatio,
                VividTsrQualityMode.Performance => PerformanceUpscaleRatio,
                VividTsrQualityMode.UltraPerformance => UltraPerformanceUpscaleRatio,
                _ => BalancedUpscaleRatio,
            };
        }

        public static Vector2Int ResolveRenderSize(int outputWidth, int outputHeight, VividTsrQualityMode quality)
        {
            var ratio = Mathf.Max(1.0f, GetUpscaleRatio(quality));
            return new Vector2Int(
                Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, outputWidth) / ratio)),
                Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, outputHeight) / ratio)));
        }

        public static int GetJitterPhaseCount(int renderWidth, int displayWidth)
        {
            renderWidth = Mathf.Max(1, renderWidth);
            displayWidth = Mathf.Max(1, displayWidth);
            var scale = (float)displayWidth / renderWidth;
            return Mathf.Max(1, Mathf.CeilToInt(8.0f * scale * scale));
        }

        public static Vector2 GetJitterOffset(int frameIndex, int phaseCount)
        {
            phaseCount = Mathf.Max(1, phaseCount);
            var sampleIndex = PositiveModulo(frameIndex, phaseCount) + 1;
            return new Vector2(
                Halton(sampleIndex, 2) - 0.5f,
                Halton(sampleIndex, 3) - 0.5f);
        }

        private static float Halton(int index, int radix)
        {
            var result = 0.0f;
            var fraction = 1.0f / radix;
            while (index > 0)
            {
                result += (index % radix) * fraction;
                index /= radix;
                fraction /= radix;
            }

            return result;
        }

        private static int PositiveModulo(int value, int divisor)
        {
            var result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}
