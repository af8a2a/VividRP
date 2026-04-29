using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core
{
    internal static class FSR3UpscalerUtility
    {
        public const float NativeAAUpscaleRatio = 1.0f;
        public const float QualityUpscaleRatio = 1.5f;
        public const float BalancedUpscaleRatio = 1.7f;
        public const float PerformanceUpscaleRatio = 2.0f;
        public const float UltraPerformanceUpscaleRatio = 3.0f;

        public static float GetUpscaleRatio(VividFsr3QualityMode quality)
        {
            return quality switch
            {
                VividFsr3QualityMode.NativeAA => NativeAAUpscaleRatio,
                VividFsr3QualityMode.Quality => QualityUpscaleRatio,
                VividFsr3QualityMode.Balanced => BalancedUpscaleRatio,
                VividFsr3QualityMode.Performance => PerformanceUpscaleRatio,
                VividFsr3QualityMode.UltraPerformance => UltraPerformanceUpscaleRatio,
                _ => BalancedUpscaleRatio,
            };
        }

        public static Vector2Int ResolveRenderSize(int outputWidth, int outputHeight, VividFsr3QualityMode quality)
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

        public static Vector2 GetMotionVectorScale(int renderWidth, int renderHeight)
        {
            return new Vector2(-Mathf.Max(1, renderWidth), -Mathf.Max(1, renderHeight));
        }

        public static Vector4 GetDeviceToViewDepthConstants(
            float nearPlane,
            float farPlane,
            float verticalFovRadians,
            float aspect,
            bool reversedDepth)
        {
            nearPlane = Mathf.Max(0.0001f, nearPlane);
            farPlane = Mathf.Max(nearPlane + 0.0001f, farPlane);
            aspect = Mathf.Max(0.0001f, aspect);
            verticalFovRadians = Mathf.Clamp(verticalFovRadians, 0.0001f, Mathf.PI - 0.0001f);

            var minDepth = nearPlane;
            var maxDepth = farPlane;
            if (reversedDepth)
            {
                minDepth = farPlane;
                maxDepth = nearPlane;
            }

            var q = maxDepth / (minDepth - maxDepth);
            var matrixElementC = q;
            var matrixElementE = q * minDepth;
            var cotHalfFovY = Mathf.Cos(0.5f * verticalFovRadians) / Mathf.Sin(0.5f * verticalFovRadians);
            var projectionX = cotHalfFovY / aspect;
            var projectionY = cotHalfFovY;

            return new Vector4(
                -matrixElementC,
                matrixElementE,
                1.0f / projectionX,
                1.0f / projectionY);
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
