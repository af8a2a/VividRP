using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    public class GranTurismo : VolumeComponent, IPostProcessComponent
    {
        /// <summary>
        /// The maximum brightness of the screen.
        /// </summary>
        [Tooltip("The maximum brightness of the screen.")]
        public ClampedFloatParameter maxBrightness = new ClampedFloatParameter(1.0f, 1.0f, 20.0f);

        /// <summary>
        /// The contrast GT Tonemapping.
        /// </summary>
        [Tooltip("The contrast.")] public ClampedFloatParameter contrast = new ClampedFloatParameter(1.11f, 0.0f, 5.0f);

        /// <summary>
        /// Linear section start. This controls linear start point in 0.0-1.0.
        /// </summary>
        [Tooltip("Linear section start. This controls linear start point in 0.0-1.0.")]
        public ClampedFloatParameter linearSectionStart = new ClampedFloatParameter(0.2f, 0.0f, 1.0f);

        /// <summary>
        /// Linear section Length. This controls linear length.
        /// </summary>
        [Tooltip("Linear section Length. This controls linear length.")]
        public ClampedFloatParameter linearSectionLength = new ClampedFloatParameter(0.4f, 0.0f, 1.0f);

        /// <summary>
        /// Black tightness pow. Pow of curve that before linearSectionStart (Dark part).
        /// </summary>
        [Tooltip("Black tightness pow. Pow of curve that before linearSectionStart (Dark part).")]
        public ClampedFloatParameter blackPow = new ClampedFloatParameter(1.29f, 1.0f, 3.0f);

        /// <summary>
        /// Black tightness min. Add of curve that before linearSectionStart (Dark part).
        /// </summary>
        [Tooltip("Black tightness min. Add of curve that before linearSectionStart (Dark part).")]
        public ClampedFloatParameter blackMin = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);


        public BoolParameter enable = new BoolParameter(false);

        public bool IsActive()
        {
            return enable.value;
        }
    }
}