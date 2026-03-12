using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable, VolumeComponentMenu("Post-processing/Lift Gamma Gain")]
    public sealed class LiftGammaGain : VolumeComponent, IPostProcessComponent
    {
        public ColorParameter lift = new(new Color(1f, 1f, 1f, 0f), false, true, true);
        public ColorParameter gamma = new(new Color(1f, 1f, 1f, 0f), false, true, true);
        public ColorParameter gain = new(new Color(1f, 1f, 1f, 0f), false, true, true);

        public bool IsActive()
        {
            return !ColorGradingCurvePresets.IsApproximately(lift.value, new Color(1f, 1f, 1f, 0f))
                || !ColorGradingCurvePresets.IsApproximately(gamma.value, new Color(1f, 1f, 1f, 0f))
                || !ColorGradingCurvePresets.IsApproximately(gain.value, new Color(1f, 1f, 1f, 0f));
        }
    }
}
