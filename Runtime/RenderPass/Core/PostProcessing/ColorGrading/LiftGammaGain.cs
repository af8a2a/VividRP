using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable, VolumeComponentMenu("Post-processing/Lift Gamma Gain")]
    public sealed class LiftGammaGain : VolumeComponent, IPostProcessComponent
    {
        public Vector4Parameter lift = new(new Vector4(1f, 1f, 1f, 0f));
        public Vector4Parameter gamma = new(new Vector4(1f, 1f, 1f, 0f));
        public Vector4Parameter gain = new(new Vector4(1f, 1f, 1f, 0f));

        public bool IsActive()
        {
            return !ColorGradingCurvePresets.IsApproximately(lift.value, new Vector4(1f, 1f, 1f, 0f))
                || !ColorGradingCurvePresets.IsApproximately(gamma.value, new Vector4(1f, 1f, 1f, 0f))
                || !ColorGradingCurvePresets.IsApproximately(gain.value, new Vector4(1f, 1f, 1f, 0f));
        }
    }
}
