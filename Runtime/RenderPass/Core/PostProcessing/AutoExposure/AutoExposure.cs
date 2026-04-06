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

    public enum AutoExposureExposureMode
    {
        Automatic,
        AutomaticHistogram,
        CurveMapping,
        Fixed,
        UsePhysicalCamera,
    }

    public enum AutoExposureMeteringMode
    {
        Average,
        Spot,
        CenterWeighted,
        MaskWeighted,
    }

    public enum AutoExposureAdaptationMode
    {
        Fixed,
        Progressive,
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
    public sealed class AutoExposureExposureModeParameter : VolumeParameter<AutoExposureExposureMode>
    {
        public AutoExposureExposureModeParameter(AutoExposureExposureMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class AutoExposureMeteringModeParameter : VolumeParameter<AutoExposureMeteringMode>
    {
        public AutoExposureMeteringModeParameter(AutoExposureMeteringMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class AutoExposureAdaptationModeParameter : VolumeParameter<AutoExposureAdaptationMode>
    {
        public AutoExposureAdaptationModeParameter(AutoExposureAdaptationMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class NoInterpAnimationCurveParameter : VolumeParameter<AnimationCurve>
    {
        public NoInterpAnimationCurveParameter(AnimationCurve value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(AnimationCurve from, AnimationCurve to, float t)
        {
            value = t > 0f ? to : from;
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
        private const float DefaultExposureCompensationCurveMinEV100 = -16f;
        private const float DefaultExposureCompensationCurveMaxEV100 = 16f;

        [Tooltip("Enables automatic exposure metering.")]
        public BoolParameter enabled = new(false);

        [Tooltip("Legacy runtime exposure mode kept for backwards compatibility with older serialized assets.")]
        public AutoExposureModeParameter mode = new(AutoExposureMode.Histogram);

        [Tooltip("Selects the HDRP-style exposure mode shown in the editor.")]
        public AutoExposureExposureModeParameter exposureMode = new(AutoExposureExposureMode.Automatic);

        [Tooltip("Lower histogram percentile retained when estimating the scene luminance.")]
        public FloatRangeParameter percent = new( new Vector2(80f, 95f), 1f, 99f);

        // [Tooltip("Upper histogram percentile retained when estimating the scene luminance.")]
        // public ClampedFloatParameter highPercent = new(95f, 1f, 99f);

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

        [Tooltip("Optional EV100-to-compensation curve in EV stops, matching Unreal's Exposure Compensation Curve.")]
        public NoInterpAnimationCurveParameter exposureCompensationCurve = new(CreateDefaultExposureCompensationCurve());

        [Tooltip("HDRP-style metering pattern used when evaluating automatic exposure.")]
        public AutoExposureMeteringModeParameter meteringMode = new(AutoExposureMeteringMode.Average);

        [Tooltip("HDRP-style adaptation behavior used when transitioning between exposures.")]
        public AutoExposureAdaptationModeParameter adaptationMode = new(AutoExposureAdaptationMode.Progressive);

        [Tooltip("Target middle gray value used while tuning HDRP-style exposure settings.")]
        public ClampedFloatParameter targetMidGray = new(0.18f, 0.01f, 1f);

        [Tooltip("Curve used for HDRP-style curve remapping mode.")]
        public NoInterpAnimationCurveParameter curveMap = new(CreateDefaultCurveMap());

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
            MigrateLegacyExposureModeIfNeeded();
            MigrateLegacyHistogramLogRangeIfNeeded();
            SyncLegacyModeFields();
            SyncLegacyHistogramLogRangeFields();

            if (meterMask == null)
                meterMask = new Texture2DParameter(null);

            base.OnEnable();
        }

        private void OnValidate()
        {
            EnsureParameters();
            MigrateLegacyExposureModeIfNeeded();
            MigrateLegacyHistogramLogRangeIfNeeded();
            SyncLegacyModeFields();
            SyncLegacyHistogramLogRangeFields();
        }

        public bool IsActive()
        {
            if (!enabled.value)
                return false;

            if (AutoExposureExposureModeUtility.UsesManualSettings(ResolveExposureMode()))
                return true;

            var minWhitePointLuminance = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(minEV100.value);
            var maxWhitePointLuminance = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(maxEV100.value);
            var usesFixedAdaptation = adaptationMode != null
                && adaptationMode.value == AutoExposureAdaptationMode.Fixed;

            return maxWhitePointLuminance >= minWhitePointLuminance
                && (usesFixedAdaptation || (speedUp.value > 0f && speedDown.value > 0f));
        }

        private void EnsureParameters()
        {
            exposureMode ??= new AutoExposureExposureModeParameter(AutoExposureExposureMode.Automatic);
            histogramLogRange ??= new FloatRangeParameter(
                new Vector2(DefaultHistogramLogMinEV100, DefaultHistogramLogMaxEV100),
                HistogramLogRangeLimitMinEV100,
                HistogramLogRangeLimitMaxEV100);
            histogramLogMin ??= new FloatParameter(DefaultHistogramLogMinEV100);
            histogramLogMax ??= new FloatParameter(DefaultHistogramLogMaxEV100);
            meterMask ??= new Texture2DParameter(null);
            exposureCompensationCurve ??= new NoInterpAnimationCurveParameter(CreateDefaultExposureCompensationCurve());
            meteringMode ??= new AutoExposureMeteringModeParameter(AutoExposureMeteringMode.Average);
            adaptationMode ??= new AutoExposureAdaptationModeParameter(AutoExposureAdaptationMode.Progressive);
            targetMidGray ??= new ClampedFloatParameter(0.18f, 0.01f, 1f);
            curveMap ??= new NoInterpAnimationCurveParameter(CreateDefaultCurveMap());

            if (exposureCompensationCurve.value == null)
                exposureCompensationCurve.value = CreateDefaultExposureCompensationCurve();

            if (curveMap.value == null)
                curveMap.value = CreateDefaultCurveMap();
        }

        public AutoExposureExposureMode ResolveExposureMode()
        {
            if (exposureMode == null)
                return ResolveExposureModeFromLegacyFields(mode.value, applyPhysicalCameraExposure.value);

            var defaultExposureMode = exposureMode.value == AutoExposureExposureMode.Automatic
                && !exposureMode.overrideState;
            var legacyFieldsIndicateCustomMode = mode.value != AutoExposureMode.Histogram
                || applyPhysicalCameraExposure.value
                || mode.overrideState
                || applyPhysicalCameraExposure.overrideState;

            if (defaultExposureMode && legacyFieldsIndicateCustomMode)
                return ResolveExposureModeFromLegacyFields(mode.value, applyPhysicalCameraExposure.value);

            return exposureMode.value;
        }

        private void MigrateLegacyExposureModeIfNeeded()
        {
            var resolvedMode = ResolveExposureModeFromLegacyFields(mode.value, applyPhysicalCameraExposure.value);
            var exposureModeIsDefault = !exposureMode.overrideState
                && exposureMode.value == AutoExposureExposureMode.Automatic;
            var legacyFieldsAreCustom = mode.overrideState
                || applyPhysicalCameraExposure.overrideState
                || mode.value != AutoExposureMode.Histogram
                || applyPhysicalCameraExposure.value;

            if (!exposureModeIsDefault || !legacyFieldsAreCustom)
                return;

            exposureMode.value = resolvedMode;
            exposureMode.overrideState = mode.overrideState || applyPhysicalCameraExposure.overrideState;
        }

        private void SyncLegacyModeFields()
        {
            var resolvedMode = ResolveExposureMode();
            mode.value = AutoExposureExposureModeUtility.ResolveRuntimeMode(resolvedMode);
            mode.overrideState = exposureMode.overrideState;
            applyPhysicalCameraExposure.value = AutoExposureExposureModeUtility.UsesPhysicalCamera(resolvedMode);
            applyPhysicalCameraExposure.overrideState = exposureMode.overrideState;
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

        private static AnimationCurve CreateDefaultExposureCompensationCurve()
        {
            var curve = new AnimationCurve(
                new Keyframe(DefaultExposureCompensationCurveMinEV100, 0f),
                new Keyframe(DefaultExposureCompensationCurveMaxEV100, 0f));
            curve.preWrapMode = WrapMode.ClampForever;
            curve.postWrapMode = WrapMode.ClampForever;
            return curve;
        }

        private static AnimationCurve CreateDefaultCurveMap()
        {
            var curve = AnimationCurve.Linear(-10f, -10f, 10f, 10f);
            curve.preWrapMode = WrapMode.ClampForever;
            curve.postWrapMode = WrapMode.ClampForever;
            return curve;
        }

        private static AutoExposureExposureMode ResolveExposureModeFromLegacyFields(
            AutoExposureMode legacyMode,
            bool legacyUsesPhysicalCamera)
        {
            if (legacyMode == AutoExposureMode.Manual)
                return legacyUsesPhysicalCamera ? AutoExposureExposureMode.UsePhysicalCamera : AutoExposureExposureMode.Fixed;

            return AutoExposureExposureMode.AutomaticHistogram;
        }
    }

    public static class AutoExposureExposureModeUtility
    {
        public static bool UsesManualSettings(AutoExposureExposureMode mode)
        {
            return mode == AutoExposureExposureMode.Fixed
                || mode == AutoExposureExposureMode.UsePhysicalCamera;
        }

        public static bool UsesPhysicalCamera(AutoExposureExposureMode mode)
        {
            return mode == AutoExposureExposureMode.UsePhysicalCamera;
        }

        public static bool UsesHistogramSettings(AutoExposureExposureMode mode)
        {
            return mode == AutoExposureExposureMode.AutomaticHistogram;
        }

        public static bool UsesCurveRemapping(AutoExposureExposureMode mode)
        {
            return mode == AutoExposureExposureMode.CurveMapping;
        }

        public static AutoExposureMode ResolveRuntimeMode(AutoExposureExposureMode mode)
        {
            return UsesManualSettings(mode)
                ? AutoExposureMode.Manual
                : AutoExposureMode.Histogram;
        }
    }
}
