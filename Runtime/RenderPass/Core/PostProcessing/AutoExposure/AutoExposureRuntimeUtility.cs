using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct AutoExposureSettingsData
    {
        public bool enabled;
        public float exposureLowPercent;
        public float exposureHighPercent;
        public float minAverageLuminance;
        public float maxAverageLuminance;
        public float exposureCompensation;
        public float deltaTime;
        public float exposureSpeedUp;
        public float exposureSpeedDown;
        public float histogramScale;
        public float histogramBias;
        public float luminanceMin;
        public float exponentialUpM;
        public float exponentialDownM;
        public float startDistance;
        public float forceTarget;
        public Texture meterMask;

        public static AutoExposureSettingsData CreateDefault()
        {
            var histogramScaleBias = AutoExposureSettingsResolver.BuildHistogramScaleBias(-10f, 6f);

            return new AutoExposureSettingsData
            {
                enabled = false,
                exposureLowPercent = 0.8f,
                exposureHighPercent = 0.95f,
                minAverageLuminance = AutoExposureSettingsResolver.MiddleGrey,
                maxAverageLuminance = AutoExposureSettingsResolver.MiddleGrey,
                exposureCompensation = 1f,
                deltaTime = 1f / 60f,
                exposureSpeedUp = 1f,
                exposureSpeedDown = 1f,
                histogramScale = histogramScaleBias.x,
                histogramBias = histogramScaleBias.y,
                luminanceMin = Mathf.Pow(2f, -10f),
                exponentialUpM = 1f,
                exponentialDownM = 1f,
                startDistance = AutoExposureSettingsResolver.DefaultStartDistance,
                forceTarget = 1f,
                meterMask = null,
            };
        }
    }

    internal static class AutoExposureSettingsResolver
    {
        internal const float MiddleGrey = 0.18f;
        internal const float DefaultStartDistance = 1.5f;

        private const float PercentToScale = 0.01f;
        private const float MinSpeed = 0.001f;
        private const float FrameTimeEpsilon = 1f / 60f;

        internal static AutoExposureSettingsData Resolve(bool isFirstFrame)
        {
            var settings = AutoExposureSettingsData.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var autoExposure = stack.GetComponent<AutoExposure>();
            if (autoExposure == null)
                return settings;

            var exposureHighPercent = Mathf.Clamp(autoExposure.highPercent.value, 1f, 99f) * PercentToScale;
            var exposureLowPercent = Mathf.Min(
                Mathf.Clamp(autoExposure.lowPercent.value, 1f, 99f) * PercentToScale,
                exposureHighPercent);

            var minWhitePointLuminance = Mathf.Max(0f, autoExposure.minBrightness.value);
            var maxWhitePointLuminance = Mathf.Max(minWhitePointLuminance, autoExposure.maxBrightness.value);

            var histogramScaleBias = BuildHistogramScaleBias(
                autoExposure.histogramLogMin.value,
                autoExposure.histogramLogMax.value);
            var validRange = maxWhitePointLuminance > minWhitePointLuminance;
            var validSpeeds = autoExposure.speedUp.value > 0f && autoExposure.speedDown.value > 0f;

            settings.enabled = autoExposure.IsActive();
            settings.exposureLowPercent = exposureLowPercent;
            settings.exposureHighPercent = exposureHighPercent;
            settings.minAverageLuminance = minWhitePointLuminance * MiddleGrey;
            settings.maxAverageLuminance = maxWhitePointLuminance * MiddleGrey;
            settings.exposureCompensation = ResolveExposureCompensation(autoExposure.exposureCompensation.value);
            settings.deltaTime = Mathf.Max(Time.deltaTime, 1e-6f);
            settings.exposureSpeedUp = Mathf.Max(autoExposure.speedUp.value, MinSpeed);
            settings.exposureSpeedDown = Mathf.Max(autoExposure.speedDown.value, MinSpeed);
            settings.histogramScale = histogramScaleBias.x;
            settings.histogramBias = histogramScaleBias.y;
            settings.luminanceMin = Mathf.Pow(2f, Mathf.Min(autoExposure.histogramLogMin.value, autoExposure.histogramLogMax.value - 1e-4f));
            settings.exponentialUpM = ComputeExponentialTransitionMultiplier(settings.exposureSpeedUp, DefaultStartDistance);
            settings.exponentialDownM = ComputeExponentialTransitionMultiplier(settings.exposureSpeedDown, DefaultStartDistance);
            settings.startDistance = DefaultStartDistance;
            settings.forceTarget = isFirstFrame || !validRange || !validSpeeds ? 1f : 0f;
            settings.meterMask = autoExposure.meterMask.value;
            return settings;
        }

        internal static float ResolveExposureCompensation(float compensationStops)
        {
            return Mathf.Pow(2f, compensationStops);
        }

        internal static Vector2 BuildHistogramScaleBias(float histogramLogMin, float histogramLogMax)
        {
            var resolvedLogMax = Mathf.Max(histogramLogMax, histogramLogMin + 1e-4f);
            var resolvedLogMin = Mathf.Min(histogramLogMin, resolvedLogMax - 1e-4f);
            var histogramDelta = Mathf.Max(resolvedLogMax - resolvedLogMin, 1e-4f);
            var histogramScale = 1f / histogramDelta;
            var histogramBias = -resolvedLogMin * histogramScale;
            return new Vector2(histogramScale, histogramBias);
        }

        internal static float ComputeExponentialTransitionMultiplier(float adaptationSpeed, float startDistance)
        {
            var safeSpeed = Mathf.Max(adaptationSpeed, MinSpeed);
            var startTime = startDistance / safeSpeed;
            var denominator = (1f - Mathf.Pow(2f, -FrameTimeEpsilon * safeSpeed)) * startTime;
            return denominator > 1e-6f ? FrameTimeEpsilon / denominator : 1f;
        }
    }
}
