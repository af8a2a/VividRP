using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum DiffusionMode
    {
        Max = 0,
        Filter = 1
    }

    [Serializable]
    public sealed class DiffusionModeParameter : VolumeParameter<DiffusionMode>
    {
        public DiffusionModeParameter(DiffusionMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("Post-processing/Diffusion")]
    public sealed class Diffusion : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Controls how the blurred contribution is combined with the source image.")]
        public DiffusionModeParameter mode = new(DiffusionMode.Filter);

        [Tooltip("Controls the source remap applied before Filter mode compositing.")]
        public ClampedFloatParameter multiply = new(0.5f, 0f, 1f);

        [Tooltip("Scales the blur radius applied to the source image.")]
        public ClampedFloatParameter blurScale = new(0.5f, 0f, 2f);

        [Tooltip("Controls the strength of the screen-style filter blend.")]
        public ClampedFloatParameter filter = new(0.5f, 0f, 1f);

        [Tooltip("Controls the Max blend strength.")]
        public ClampedFloatParameter intensity = new(0f, 0f, 1f);

        [Tooltip("Scales the blurred contribution before it is combined with the source.")]
        public ClampedFloatParameter blurIntensity = new(1f, 0f, 1f);

        [Tooltip("Whether diffusion is enabled.")]
        public BoolParameter enabled = new(false);

        public bool IsActive()
        {
            return enabled.value;
        }
    }
}
