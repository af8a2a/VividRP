using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public sealed class TemporalDenoiserSetting : VolumeComponent
    {
        [Tooltip("The quality of AntiAliasing")]
        public MotionBlurQualityParameter quality = new MotionBlurQualityParameter(MotionBlurQuality.Low);

        [Tooltip("Sampling Distance")] public ClampedFloatParameter spread = new ClampedFloatParameter(1.0f, 0f, 1f);

        [Tooltip("Feedback")] public ClampedFloatParameter feedback = new ClampedFloatParameter(0.0f, 0f, 1f);

        public BoolParameter enabled = new BoolParameter(false);
        public bool IsActive() => enabled.value;
    }
}