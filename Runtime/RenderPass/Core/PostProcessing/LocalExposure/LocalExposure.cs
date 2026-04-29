using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable]
    [VolumeComponentMenu("Post-processing/Local Exposure")]
    public sealed class LocalExposure : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Enables local exposure contrast compression.")]
        public BoolParameter enabled = new(false);

        [Tooltip("Reduces local exposure contrast in highlight regions. Values below 1 enable highlight compression.")]
        public ClampedFloatParameter highlightContrastScale = new(1f, 0f, 1f);

        [Tooltip("Reduces local exposure contrast in shadow regions. Values below 1 enable shadow compression.")]
        public ClampedFloatParameter shadowContrastScale = new(1f, 0f, 1f);

        [Tooltip("Optional multiplier curve for highlight contrast, evaluated by average scene EV100.")]
        public NoInterpAnimationCurveParameter highlightContrastCurve = new(null);

        [Tooltip("Optional multiplier curve for shadow contrast, evaluated by average scene EV100.")]
        public NoInterpAnimationCurveParameter shadowContrastCurve = new(null);

        [Tooltip("Scales the detail layer after luminance base/detail decomposition.")]
        public ClampedFloatParameter detailStrength = new(1f, 0f, 4f);

        [Tooltip("Blends between bilateral-filtered luminance and blurred luminance for the base layer.")]
        public ClampedFloatParameter blurredLuminanceBlend = new(0.6f, 0f, 1f);

        [Tooltip("Approximate screen percentage used to derive the blurred luminance kernel radius.")]
        public ClampedFloatParameter blurredLuminanceKernelSizePercent = new(50f, 0f, 100f);

        [Tooltip("Threshold used to determine highlight regions.")]
        public ClampedFloatParameter highlightThreshold = new(0f, 0f, 4f);

        [Tooltip("Threshold used to determine shadow regions.")]
        public ClampedFloatParameter shadowThreshold = new(0f, 0f, 4f);

        [Tooltip("Strength of the highlight threshold transition.")]
        public ClampedFloatParameter highlightThresholdStrength = new(1f, 0f, 1f);

        [Tooltip("Strength of the shadow threshold transition.")]
        public ClampedFloatParameter shadowThresholdStrength = new(1f, 0f, 1f);

        [Tooltip("Logarithmic adjustment for local exposure middle grey.")]
        public ClampedFloatParameter middleGreyBias = new(0f, -15f, 15f);

        protected override void OnEnable()
        {
            EnsureParameters();
            base.OnEnable();
        }

        private void OnValidate()
        {
            EnsureParameters();
        }

        public bool IsActive()
        {
            return enabled.value;
        }

        private void EnsureParameters()
        {
            enabled ??= new BoolParameter(false);
            highlightContrastScale ??= new ClampedFloatParameter(1f, 0f, 1f);
            shadowContrastScale ??= new ClampedFloatParameter(1f, 0f, 1f);
            highlightContrastCurve ??= new NoInterpAnimationCurveParameter(null);
            shadowContrastCurve ??= new NoInterpAnimationCurveParameter(null);
            detailStrength ??= new ClampedFloatParameter(1f, 0f, 4f);
            blurredLuminanceBlend ??= new ClampedFloatParameter(0.6f, 0f, 1f);
            blurredLuminanceKernelSizePercent ??= new ClampedFloatParameter(50f, 0f, 100f);
            highlightThreshold ??= new ClampedFloatParameter(0f, 0f, 4f);
            shadowThreshold ??= new ClampedFloatParameter(0f, 0f, 4f);
            highlightThresholdStrength ??= new ClampedFloatParameter(1f, 0f, 1f);
            shadowThresholdStrength ??= new ClampedFloatParameter(1f, 0f, 1f);
            middleGreyBias ??= new ClampedFloatParameter(0f, -15f, 15f);
        }
    }
}
