using System.Collections.Generic;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    internal static class AutoExposureBenchmarkPresets
    {
        private const float PercentToScale = 0.01f;
        private const float MinSpeed = 0.001f;
        private const float MinDeltaTime = 1e-6f;

        public static IEnumerable<AutoExposurePresetDefinition> All => AutoExposureCommonPresets.All;

        public static IEnumerable<AutoExposurePresetDefinition> Histogram => AutoExposureCommonPresets.Histogram;

        public static IEnumerable<AutoExposurePresetDefinition> Manual => AutoExposureCommonPresets.Manual;

        public static AutoExposureSettingsData CreateSettingsData(
            this AutoExposurePresetDefinition preset,
            Camera camera = null,
            float deltaTime = 1f / 60f,
            bool isFirstFrame = false)
        {
            var settings = AutoExposureSettingsData.CreateDefault();
            var curve = preset.CreateExposureCompensationCurve();
            var resolvedManualEV100 = AutoExposureSettingsResolver.ResolveManualEV100(
                camera,
                preset.ManualEV100,
                preset.ApplyPhysicalCameraExposure);
            var curveTextureData = AutoExposureCompensationCurveUtility.Resolve(curve);
            var exposureCompensationSettings = AutoExposureSettingsResolver.ResolveExposureCompensation(
                preset.ExposureCompensation);

            settings.enabled = true;
            settings.exposureMode = preset.Mode == AutoExposureMode.Manual
                ? preset.ApplyPhysicalCameraExposure
                    ? AutoExposureExposureMode.UsePhysicalCamera
                    : AutoExposureExposureMode.Fixed
                : AutoExposureExposureMode.AutomaticHistogram;
            settings.mode = preset.Mode;
            settings.meteringMode = AutoExposureMeteringMode.Average;
            settings.adaptationMode = AutoExposureAdaptationMode.Progressive;
            settings.applyPhysicalCameraExposure = preset.ApplyPhysicalCameraExposure;
            settings.targetMidGray = AutoExposureSettingsResolver.MiddleGrey;
            settings.manualEV100 = resolvedManualEV100;
            settings.exposureCompensationSettings = exposureCompensationSettings;
            settings.exposureCompensationCurveStops = preset.Mode == AutoExposureMode.Manual
                ? AutoExposureSettingsResolver.ResolveExposureCompensationCurveStops(curve, resolvedManualEV100)
                : 0f;
            settings.exposureCompensationAll = AutoExposureSettingsResolver.ResolveExposureCompensationAll(
                settings.exposureCompensationSettings,
                settings.exposureCompensationCurveStops);
            settings.manualAverageSceneLuminance = AutoExposureSettingsResolver.ResolveAverageSceneLuminanceFromEV100(
                resolvedManualEV100);
            settings.fixedExposureScale = AutoExposureSettingsResolver.ResolveManualExposureScale(
                resolvedManualEV100,
                settings.exposureCompensationAll);
            settings.exposureCompensationCurveTexture = curveTextureData.texture;
            settings.exposureCompensationCurveMinEV100 = curveTextureData.minEV100;
            settings.exposureCompensationCurveInvRange = curveTextureData.invRange;
            settings.exposureCompensationCurveEnabled = curveTextureData.enabled;

            if (preset.Mode == AutoExposureMode.Manual)
            {
                settings.forceTarget = 1f;
                return settings;
            }

            var minWhitePointLuminance = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(preset.MinEV100);
            var maxWhitePointLuminance = Mathf.Max(
                minWhitePointLuminance,
                AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(preset.MaxEV100));
            var histogramScaleBias = AutoExposureSettingsResolver.BuildHistogramScaleBiasFromEV100(
                preset.HistogramLogRangeEV100.x,
                preset.HistogramLogRangeEV100.y);
            var histogramLogRange = AutoExposureSettingsResolver.ResolveHistogramLogRangeFromEV100(
                preset.HistogramLogRangeEV100.x,
                preset.HistogramLogRangeEV100.y);
            var exposureHighPercent = Mathf.Clamp(preset.Percent.y, 1f, 99f) * PercentToScale;
            var exposureLowPercent = Mathf.Min(
                Mathf.Clamp(preset.Percent.x, 1f, 99f) * PercentToScale,
                exposureHighPercent);
            var clampedSpeedUp = Mathf.Max(preset.SpeedUp, MinSpeed);
            var clampedSpeedDown = Mathf.Max(preset.SpeedDown, MinSpeed);

            settings.exposureLowPercent = exposureLowPercent;
            settings.exposureHighPercent = exposureHighPercent;
            settings.minAverageLuminance = minWhitePointLuminance * AutoExposureSettingsResolver.MiddleGrey;
            settings.maxAverageLuminance = maxWhitePointLuminance * AutoExposureSettingsResolver.MiddleGrey;
            settings.deltaTime = Mathf.Max(deltaTime, MinDeltaTime);
            settings.exposureSpeedUp = clampedSpeedUp;
            settings.exposureSpeedDown = clampedSpeedDown;
            settings.histogramScale = histogramScaleBias.x;
            settings.histogramBias = histogramScaleBias.y;
            settings.luminanceMin = Mathf.Pow(2f, histogramLogRange.x);
            settings.exponentialUpM = AutoExposureSettingsResolver.ComputeExponentialTransitionMultiplier(
                clampedSpeedUp,
                AutoExposureSettingsResolver.DefaultStartDistance);
            settings.exponentialDownM = AutoExposureSettingsResolver.ComputeExponentialTransitionMultiplier(
                clampedSpeedDown,
                AutoExposureSettingsResolver.DefaultStartDistance);
            settings.startDistance = AutoExposureSettingsResolver.DefaultStartDistance;
            settings.forceTarget = isFirstFrame
                || preset.MinEV100 >= preset.MaxEV100
                || preset.SpeedUp <= 0f
                || preset.SpeedDown <= 0f
                ? 1f
                : 0f;
            return settings;
        }
    }
}
