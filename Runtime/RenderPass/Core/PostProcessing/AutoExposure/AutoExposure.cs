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
        [Tooltip("Enables histogram-based automatic exposure.")]
        public BoolParameter enabled = new(false);

        [Tooltip("Histogram computes exposure from scene luminance. Manual uses a fixed exposure value.")]
        public AutoExposureModeParameter mode = new(AutoExposureMode.Histogram);

        [Tooltip("Lower histogram percentile retained when estimating the scene luminance.")]
        public ClampedFloatParameter lowPercent = new(80f, 1f, 99f);

        [Tooltip("Upper histogram percentile retained when estimating the scene luminance.")]
        public ClampedFloatParameter highPercent = new(95f, 1f, 99f);

        [Tooltip("Minimum average scene luminance allowed during exposure adaptation.")]
        public MinFloatParameter minBrightness = new(0.03f, 0f);

        [Tooltip("Maximum average scene luminance allowed during exposure adaptation.")]
        public MinFloatParameter maxBrightness = new(2f, 0f);

        [Tooltip("Adaptation speed in f-stops per second when moving toward a brighter exposure.")]
        public MinFloatParameter speedUp = new(3f, 0.02f);

        [Tooltip("Adaptation speed in f-stops per second when moving toward a darker exposure.")]
        public MinFloatParameter speedDown = new(1f, 0.02f);

        [Tooltip("Exposure compensation in EV stops. In Manual mode this becomes the fixed exposure value.")]
        public FloatParameter exposureCompensation = new(0f);

        [AdditionalProperty]
        [Tooltip("Minimum histogram range in log2 luminance.")]
        public FloatParameter histogramLogMin = new(-10f);

        [AdditionalProperty]
        [Tooltip("Maximum histogram range in log2 luminance.")]
        public FloatParameter histogramLogMax = new(6f);

        [AdditionalProperty]
        [Tooltip("Optional weighting texture used for exposure metering.")]
        public Texture2DParameter meterMask = new(null);

        protected override void OnEnable()
        {
            if (meterMask == null)
                meterMask = new Texture2DParameter(null);

            base.OnEnable();
        }

        public bool IsActive()
        {
            if (!enabled.value)
                return false;

            if (mode.value == AutoExposureMode.Manual)
                return true;

            return maxBrightness.value >= minBrightness.value
                && speedUp.value > 0f
                && speedDown.value > 0f;
        }
    }
}
