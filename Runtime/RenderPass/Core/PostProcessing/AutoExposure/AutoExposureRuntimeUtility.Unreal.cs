using UnityEngine;

namespace VividRP.Runtime
{
    internal static partial class AutoExposureSettingsResolver
    {
        internal static AutoExposureSettingsData ResolveUnreal(
            AutoExposure autoExposure,
            Camera camera,
            bool isFirstFrame)
        {
            var settings = AutoExposureSettingsData.CreateDefault();
            settings.implementation = AutoExposureImplementationPath.Unreal;
            settings.mode = autoExposure.mode.value;
            settings.applyPhysicalCameraExposure = autoExposure.applyPhysicalCameraExposure.value;
            settings.unrealExposureMeteringMask = autoExposure.exposureMeteringMask.value;
            settings.unrealBlackHistogramBucketInfluence = 0f;
            settings.manualEV100 = ResolveManualEV100(
                camera,
                autoExposure.manualEV100.value,
                settings.applyPhysicalCameraExposure);
            settings.targetMidGray = MiddleGrey;
            settings.exposureCompensationSettings = ResolveExposureCompensation(
                autoExposure.exposureCompensation.value);
            settings.exposureCompensationCurveStops = settings.mode == AutoExposureMode.Manual
                ? ResolveExposureCompensationCurveStops(
                    autoExposure.exposureCompensationCurve.value,
                    settings.manualEV100)
                : 0f;
            settings.exposureCompensationAll = ResolveExposureCompensationAll(
                settings.exposureCompensationSettings,
                settings.exposureCompensationCurveStops);
            settings.manualAverageSceneLuminance = ResolveAverageSceneLuminanceFromEV100(
                settings.manualEV100);
            settings.fixedExposureScale = ResolveManualExposureScale(
                settings.manualEV100,
                settings.exposureCompensationAll);

            var curveTextureData = AutoExposureCompensationCurveUtility.Resolve(
                autoExposure.exposureCompensationCurve.value);
            settings.exposureCompensationCurveTexture = curveTextureData.texture;
            settings.exposureCompensationCurveMinEV100 = curveTextureData.minEV100;
            settings.exposureCompensationCurveInvRange = curveTextureData.invRange;
            settings.exposureCompensationCurveEnabled = curveTextureData.enabled;

            if (settings.mode == AutoExposureMode.Manual)
            {
                settings.enabled = autoExposure.enabled.value;
                settings.forceTarget = 1f;
                return settings;
            }

            var usesHistogramPercentiles = settings.mode == AutoExposureMode.Histogram;
            var exposureHighPercent = usesHistogramPercentiles
                ? Mathf.Clamp(autoExposure.percent.max, 1f, 99f) * PercentToScale
                : 1f;
            var exposureLowPercent = usesHistogramPercentiles
                ? Mathf.Min(
                    Mathf.Clamp(autoExposure.percent.min, 1f, 99f) * PercentToScale,
                    exposureHighPercent)
                : 0f;
            var minWhitePointLuminance = ResolveWhitePointLuminanceFromEV100(
                autoExposure.minEV100.value);
            var maxWhitePointLuminance = Mathf.Max(
                minWhitePointLuminance,
                ResolveWhitePointLuminanceFromEV100(autoExposure.maxEV100.value));
            var histogramLogRangeValue = autoExposure.histogramLogRange.value;
            var histogramLogRange = ResolveHistogramLogRangeFromEV100(
                histogramLogRangeValue.x,
                histogramLogRangeValue.y);
            var histogramScaleBias = BuildHistogramScaleBias(
                histogramLogRange.x,
                histogramLogRange.y);
            var validRange = autoExposure.minEV100.value < autoExposure.maxEV100.value;
            var validSpeeds = autoExposure.speedUp.value > 0f
                && autoExposure.speedDown.value > 0f;

            settings.enabled = autoExposure.IsUnrealActive();
            settings.exposureLowPercent = exposureLowPercent;
            settings.exposureHighPercent = exposureHighPercent;
            settings.minAverageLuminance = minWhitePointLuminance * MiddleGrey;
            settings.maxAverageLuminance = maxWhitePointLuminance * MiddleGrey;
            settings.deltaTime = Mathf.Max(Time.deltaTime, 1e-6f);
            settings.exposureSpeedUp = Mathf.Max(autoExposure.speedUp.value, MinSpeed);
            settings.exposureSpeedDown = Mathf.Max(autoExposure.speedDown.value, MinSpeed);
            settings.histogramScale = histogramScaleBias.x;
            settings.histogramBias = histogramScaleBias.y;
            settings.luminanceMin = settings.mode == AutoExposureMode.Basic
                ? 1e-4f
                : Mathf.Pow(2f, histogramLogRange.x);
            settings.exponentialUpM = ComputeExponentialTransitionMultiplier(
                settings.exposureSpeedUp,
                DefaultStartDistance);
            settings.exponentialDownM = ComputeExponentialTransitionMultiplier(
                settings.exposureSpeedDown,
                DefaultStartDistance);
            settings.startDistance = DefaultStartDistance;
            settings.forceTarget = isFirstFrame || !validRange || !validSpeeds ? 1f : 0f;
            return settings;
        }
    }
}
