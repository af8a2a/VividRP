using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable, VolumeComponentMenu("Post-processing/Split Toning")]
    public sealed class SplitToning : VolumeComponent, IPostProcessComponent
    {
        public NoInterpColorParameter shadows = new(new Color(0.5f, 0.5f, 0.5f, 1f), false, false, true);
        public NoInterpColorParameter highlights = new(new Color(0.5f, 0.5f, 0.5f, 1f), false, false, true);
        public ClampedFloatParameter balance = new(0f, -100f, 100f);

        public bool IsActive()
        {
            return !ColorGradingCurvePresets.IsApproximately(shadows.value, new Color(0.5f, 0.5f, 0.5f, 1f))
                || !ColorGradingCurvePresets.IsApproximately(highlights.value, new Color(0.5f, 0.5f, 0.5f, 1f))
                || !Mathf.Approximately(balance.value, 0f);
        }
    }
}
