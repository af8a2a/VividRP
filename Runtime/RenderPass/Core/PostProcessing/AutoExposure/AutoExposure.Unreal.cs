using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum AutoExposureMode
    {
        Histogram = 0,
        Manual = 1,
        Basic = 2,
    }

    [Serializable]
    public sealed class AutoExposureModeParameter : VolumeParameter<AutoExposureMode>
    {
        public AutoExposureModeParameter(AutoExposureMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    public sealed partial class AutoExposure
    {
        private const int CurrentUnrealSettingsVersion = 1;
        private const float ExposureLimitMinEV100 = -10f;
        private const float ExposureLimitMaxEV100 = 20f;
        private const float DefaultHistogramLogMinEV100 = -10f;
        private const float DefaultHistogramLogMaxEV100 = 20f;
        private const float LegacyDefaultHistogramLogMaxEV100 = 6f;
        private const float DefaultExposureCompensationCurveMinEV100 = -16f;
        private const float DefaultExposureCompensationCurveMaxEV100 = 16f;

        [SerializeField, HideInInspector]
        private int m_UnrealSettingsVersion;

        [Tooltip("Selects the Unreal exposure mode.")]
        public AutoExposureModeParameter mode = new(AutoExposureMode.Histogram);

        [Tooltip("Sets the lower and upper histogram percentages used to estimate exposure.")]
        public FloatRangeParameter percent = new(new Vector2(10f, 90f), 1f, 99f);

        [Tooltip("Legacy brightness clamp kept for backwards compatibility with older serialized assets.")]
        public MinFloatParameter minBrightness = new(0.03f, 0f);

        [Tooltip("Legacy brightness clamp kept for backwards compatibility with older serialized assets.")]
        public MinFloatParameter maxBrightness = new(8f, 0f);

        [Tooltip("Minimum EV100 allowed during histogram adaptation, matching Unreal's extended luminance range workflow.")]
        public ClampedFloatParameter minEV100 =
            new(ExposureLimitMinEV100, ExposureLimitMinEV100, ExposureLimitMaxEV100);

        [Tooltip("Maximum EV100 allowed during histogram adaptation, matching Unreal's extended luminance range workflow.")]
        public ClampedFloatParameter maxEV100 =
            new(ExposureLimitMaxEV100, ExposureLimitMinEV100, ExposureLimitMaxEV100);

        [Tooltip("Adaptation speed in f-stops per second when moving toward a brighter exposure.")]
        public MinFloatParameter speedUp = new(3f, 0.02f);

        [Tooltip("Adaptation speed in f-stops per second when moving toward a darker exposure.")]
        public MinFloatParameter speedDown = new(1f, 0.02f);

        [Tooltip("Fixed manual exposure in EV100 stops.")]
        public FloatParameter manualEV100 = new(0f);

        [Tooltip("Uses the camera aperture, shutter speed, and ISO to derive Manual EV100. Only affects Manual mode.")]
        public BoolParameter applyPhysicalCameraExposure = new(true);

        [Tooltip("Exposure compensation in EV stops applied on top of the resolved exposure result.")]
        public FloatParameter exposureCompensation = new(1f);

        [Tooltip("Weights Unreal auto exposure metering with a screen-space texture.")]
        public Texture2DParameter exposureMeteringMask = new(null);

        [Tooltip("Optional EV100-to-compensation curve in EV stops, matching Unreal's Exposure Compensation Curve.")]
        public NoInterpAnimationCurveParameter exposureCompensationCurve =
            new(CreateDefaultExposureCompensationCurve());

        [AdditionalProperty]
        [Tooltip("Histogram EV100 range, matching Unreal's Histogram Min/Max EV100 controls.")]
        public FloatRangeParameter histogramLogRange = new(
            new Vector2(DefaultHistogramLogMinEV100, DefaultHistogramLogMaxEV100),
            ExposureLimitMinEV100,
            ExposureLimitMaxEV100);

        [SerializeField, HideInInspector]
        private FloatParameter histogramLogMin = new(DefaultHistogramLogMinEV100);

        [SerializeField, HideInInspector]
        private FloatParameter histogramLogMax = new(DefaultHistogramLogMaxEV100);

        private void EnsureUnrealParameters()
        {
            mode ??= new AutoExposureModeParameter(AutoExposureMode.Histogram);
            percent ??= new FloatRangeParameter(new Vector2(10f, 90f), 1f, 99f);
            minBrightness ??= new MinFloatParameter(0.03f, 0f);
            maxBrightness ??= new MinFloatParameter(8f, 0f);
            minEV100 ??= new ClampedFloatParameter(
                ExposureLimitMinEV100,
                ExposureLimitMinEV100,
                ExposureLimitMaxEV100);
            maxEV100 ??= new ClampedFloatParameter(
                ExposureLimitMaxEV100,
                ExposureLimitMinEV100,
                ExposureLimitMaxEV100);
            speedUp ??= new MinFloatParameter(3f, 0.02f);
            speedDown ??= new MinFloatParameter(1f, 0.02f);
            manualEV100 ??= new FloatParameter(0f);
            applyPhysicalCameraExposure ??= new BoolParameter(true);
            exposureCompensation ??= new FloatParameter(1f);
            exposureMeteringMask ??= new Texture2DParameter(null);
            histogramLogRange ??= new FloatRangeParameter(
                new Vector2(DefaultHistogramLogMinEV100, DefaultHistogramLogMaxEV100),
                ExposureLimitMinEV100,
                ExposureLimitMaxEV100);
            histogramLogMin ??= new FloatParameter(DefaultHistogramLogMinEV100);
            histogramLogMax ??= new FloatParameter(DefaultHistogramLogMaxEV100);
            exposureCompensationCurve ??=
                new NoInterpAnimationCurveParameter(CreateDefaultExposureCompensationCurve());

            if (exposureCompensationCurve.value == null)
                exposureCompensationCurve.value = CreateDefaultExposureCompensationCurve();
        }

        private void MigrateLegacyHistogramLogRangeIfNeeded()
        {
            var currentRange = histogramLogRange.value;
            var currentRangeIsDefault = !histogramLogRange.overrideState
                && Mathf.Approximately(currentRange.x, DefaultHistogramLogMinEV100)
                && (Mathf.Approximately(currentRange.y, DefaultHistogramLogMaxEV100)
                    || Mathf.Approximately(
                        currentRange.y,
                        LegacyDefaultHistogramLogMaxEV100));
            var legacyRangeHasCustomValue = histogramLogMin.overrideState
                || histogramLogMax.overrideState
                || !Mathf.Approximately(
                    histogramLogMin.value,
                    DefaultHistogramLogMinEV100)
                || !Mathf.Approximately(
                    histogramLogMax.value,
                    DefaultHistogramLogMaxEV100);

            if (!currentRangeIsDefault || !legacyRangeHasCustomValue)
                return;

            histogramLogRange.value =
                new Vector2(histogramLogMin.value, histogramLogMax.value);
            histogramLogRange.overrideState =
                histogramLogMin.overrideState || histogramLogMax.overrideState;
        }

        private void MigrateLegacyUnrealMeteringMaskIfNeeded()
        {
            if (m_UnrealSettingsVersion >= CurrentUnrealSettingsVersion)
                return;

            // Before the implementation split, Unreal sampled HDRP's serialized
            // meterMask/weightTextureMask field. Copy it once, then keep both
            // path-specific parameters independent.
            if (exposureMeteringMask.value == null
                && weightTextureMask?.value != null)
            {
                exposureMeteringMask.value = weightTextureMask.value;
                exposureMeteringMask.overrideState =
                    weightTextureMask.overrideState;
            }

            m_UnrealSettingsVersion = CurrentUnrealSettingsVersion;
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
    }
}
