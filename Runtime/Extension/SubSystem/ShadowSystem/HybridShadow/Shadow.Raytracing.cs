using System;
using System.Diagnostics;

namespace UnityEngine.Rendering.Universal
{
    public enum ShadowClassifyMode
    {
        ByNormal,
        ByCascadeRange,
        ByCascades
    }

    [Serializable, DebuggerDisplay(k_DebuggerDisplay)]
    public class ShadowClassifyModeParameters : VolumeParameter<ShadowClassifyMode>
    {
        public ShadowClassifyModeParameters(ShadowClassifyMode value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }

    public partial class Shadows
    {
        #region Hybrid Shadow


        [Tooltip("Controls the ray length for ray traced directional shadows.")]
        public MinFloatParameter dirShadowsRayLength = new MinFloatParameter(1000.0f, 0.01f);

        [Tooltip("Shadow sample count for soft Shadow.")]
        public ClampedIntParameter sampleCount = new ClampedIntParameter(1, 1, 32);

        [Tooltip("Shadow sample radius for soft Shadow.")]
        public ClampedFloatParameter radius = new ClampedFloatParameter(0.1f, 0f, 0.5f);

        [Tooltip("Controls character self shadows layer.")]
        public LayerMaskParameter characterLayerMask = new LayerMaskParameter(0);


        public ShadowClassifyModeParameters shadowClassifyMode = new ShadowClassifyModeParameters(ShadowClassifyMode.ByCascades, true);

        #endregion
    }
}