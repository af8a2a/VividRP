using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum AutoExposureMode
    {
        Histogram,
        Manual,
    }

    [Serializable]
    public sealed class AutoExposureModeParameter : VolumeParameter<AutoExposureMode>
    {
        public AutoExposureModeParameter(AutoExposureMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("Post-processing/Auto Exposure")]
    public sealed class AutoExposure : VolumeComponent, IPostProcessComponent
    {
        private const float DefaultHistogramLogMinEV100 = -10f;
        private const float DefaultHistogramLogMaxEV100 = 6f;
        private const float HistogramLogRangeLimitMinEV100 = -20f;
        private const float HistogramLogRangeLimitMaxEV100 = 20f;

        [Tooltip("Enables automatic exposure metering.")]
        public BoolParameter enabled = new(false);

        [Tooltip("Selects the exposure metering mode.")]
        public AutoExposureModeParameter mode = new(AutoExposureMode.Histogram);

        [Tooltip("Lower histogram percentile retained when estimating the scene luminance.")]
        public ClampedFloatParameter lowPercent = new(80f, 1f, 99f);

        [Tooltip("Upper histogram percentile retained when estimating the scene luminance.")]
        public ClampedFloatParameter highPercent = new(95f, 1f, 99f);

        [Tooltip("Legacy brightness clamp kept for backwards compatibility with older serialized assets.")]
        public MinFloatParameter minBrightness = new(0.03f, 0f);

        [Tooltip("Legacy brightness clamp kept for backwards compatibility with older serialized assets.")]
        public MinFloatParameter maxBrightness = new(2f, 0f);

        [Tooltip("Minimum EV100 allowed during histogram adaptation, matching Unreal's extended luminance range workflow.")]
        public FloatParameter minEV100 = new(-5.058894f);

        [Tooltip("Maximum EV100 allowed during histogram adaptation, matching Unreal's extended luminance range workflow.")]
        public FloatParameter maxEV100 = new(1f);

        [Tooltip("Adaptation speed in f-stops per second when moving toward a brighter exposure.")]
        public MinFloatParameter speedUp = new(3f, 0.02f);

        [Tooltip("Adaptation speed in f-stops per second when moving toward a darker exposure.")]
        public MinFloatParameter speedDown = new(1f, 0.02f);

        [Tooltip("Fixed manual exposure in EV100 stops.")]
        public FloatParameter manualEV100 = new(0f);

        [Tooltip("Uses the camera aperture, shutter speed, and ISO to derive Manual EV100. Only affects Manual mode.")]
        public BoolParameter applyPhysicalCameraExposure = new(false);

        [Tooltip("Exposure compensation in EV stops applied on top of the resolved exposure result.")]
        public FloatParameter exposureCompensation = new(0f);

        [AdditionalProperty]
        [Tooltip("Histogram EV100 range, matching Unreal's Histogram Min/Max EV100 controls.")]
        public FloatRangeParameter histogramLogRange = new(
            new Vector2(DefaultHistogramLogMinEV100, DefaultHistogramLogMaxEV100),
            HistogramLogRangeLimitMinEV100,
            HistogramLogRangeLimitMaxEV100);

        [AdditionalProperty]
        [Tooltip("Optional weighting texture used for exposure metering.")]
        public Texture2DParameter meterMask = new(null);

        [SerializeField, HideInInspector]
        private FloatParameter histogramLogMin = new(DefaultHistogramLogMinEV100);

        [SerializeField, HideInInspector]
        private FloatParameter histogramLogMax = new(DefaultHistogramLogMaxEV100);

        protected override void OnEnable()
        {
            EnsureParameters();
            MigrateLegacyHistogramLogRangeIfNeeded();
            SyncLegacyHistogramLogRangeFields();

            if (meterMask == null)
                meterMask = new Texture2DParameter(null);

            base.OnEnable();
        }

        private void OnValidate()
        {
            EnsureParameters();
            MigrateLegacyHistogramLogRangeIfNeeded();
            SyncLegacyHistogramLogRangeFields();
        }

        public bool IsActive()
        {
            if (!enabled.value)
                return false;

            if (mode.value == AutoExposureMode.Manual)
                return true;

            var minWhitePointLuminance = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(minEV100.value);
            var maxWhitePointLuminance = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(maxEV100.value);

            return maxWhitePointLuminance >= minWhitePointLuminance
                && speedUp.value > 0f
                && speedDown.value > 0f;
        }

        private void EnsureParameters()
        {
            histogramLogRange ??= new FloatRangeParameter(
                new Vector2(DefaultHistogramLogMinEV100, DefaultHistogramLogMaxEV100),
                HistogramLogRangeLimitMinEV100,
                HistogramLogRangeLimitMaxEV100);
            histogramLogMin ??= new FloatParameter(DefaultHistogramLogMinEV100);
            histogramLogMax ??= new FloatParameter(DefaultHistogramLogMaxEV100);
            meterMask ??= new Texture2DParameter(null);
        }

        private void MigrateLegacyHistogramLogRangeIfNeeded()
        {
            var currentRange = histogramLogRange.value;
            var currentRangeIsDefault = !histogramLogRange.overrideState
                && Mathf.Approximately(currentRange.x, DefaultHistogramLogMinEV100)
                && Mathf.Approximately(currentRange.y, DefaultHistogramLogMaxEV100);
            var legacyRangeHasCustomValue = histogramLogMin.overrideState
                || histogramLogMax.overrideState
                || !Mathf.Approximately(histogramLogMin.value, DefaultHistogramLogMinEV100)
                || !Mathf.Approximately(histogramLogMax.value, DefaultHistogramLogMaxEV100);

            if (!currentRangeIsDefault || !legacyRangeHasCustomValue)
                return;

            histogramLogRange.value = new Vector2(histogramLogMin.value, histogramLogMax.value);
            histogramLogRange.overrideState = histogramLogMin.overrideState || histogramLogMax.overrideState;
        }

        private void SyncLegacyHistogramLogRangeFields()
        {
            var currentRange = histogramLogRange.value;
            histogramLogMin.value = currentRange.x;
            histogramLogMax.value = currentRange.y;
            histogramLogMin.overrideState = histogramLogRange.overrideState;
            histogramLogMax.overrideState = histogramLogRange.overrideState;
        }
    }
}
