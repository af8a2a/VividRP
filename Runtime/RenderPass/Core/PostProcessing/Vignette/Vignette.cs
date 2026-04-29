using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum VignetteMode
    {
        Procedural,
        Masked
    }

    [Serializable]
    public sealed class VignetteModeParameter : VolumeParameter<VignetteMode>
    {
        public VignetteModeParameter(VignetteMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("Post-processing/Vignette")]
    public sealed class Vignette : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Specifies the mode VividRP uses to display the vignette effect.")]
        public VignetteModeParameter mode = new(VignetteMode.Procedural);

        [Tooltip("Specifies the color of the vignette.")]
        public ColorParameter color = new(Color.black, false, false, true);

        [Tooltip("Sets the center point for the vignette.")]
        public Vector2Parameter center = new(new Vector2(0.5f, 0.5f));

        [Tooltip("Use the slider to set the strength of the Vignette effect.")]
        public ClampedFloatParameter intensity = new(0f, 0f, 1f);

        [Tooltip("Controls the smoothness of the vignette borders.")]
        public ClampedFloatParameter smoothness = new(0.2f, 0.01f, 1f);

        [Tooltip("Controls how round the vignette is, lower values result in a more square vignette.")]
        public ClampedFloatParameter roundness = new(1f, 0f, 1f);

        [Tooltip("When enabled, the vignette is perfectly round. When disabled, the vignette matches shape with the current aspect ratio.")]
        public BoolParameter rounded = new(false);

        [Tooltip("Specifies a black and white mask Texture to use as a vignette.")]
        public Texture2DParameter mask = new(null);

        [Range(0f, 1f)]
        [Tooltip("Controls the opacity of the mask vignette. Lower values result in a more transparent vignette.")]
        public ClampedFloatParameter opacity = new(1f, 0f, 1f);

        public bool IsActive()
        {
            return (mode.value == VignetteMode.Procedural && intensity.value > 0f)
                || (mode.value == VignetteMode.Masked && opacity.value > 0f && mask.value != null);
        }
    }
}
