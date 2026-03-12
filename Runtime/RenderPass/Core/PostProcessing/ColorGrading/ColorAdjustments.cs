using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable, VolumeComponentMenu("Post-processing/Color Adjustments")]
    public sealed class ColorAdjustments : VolumeComponent, IPostProcessComponent
    {
        public ClampedFloatParameter postExposure = new(0f, -20f, 20f);
        public ClampedFloatParameter contrast = new(0f, -100f, 100f);
        public ColorParameter colorFilter = new(Color.white, false, false, true);
        public ClampedFloatParameter hueShift = new(0f, -180f, 180f);
        public ClampedFloatParameter saturation = new(0f, -100f, 100f);

        public bool IsActive()
        {
            return !Mathf.Approximately(postExposure.value, 0f)
                || !Mathf.Approximately(contrast.value, 0f)
                || !ColorGradingCurvePresets.IsApproximately(colorFilter.value, Color.white)
                || !Mathf.Approximately(hueShift.value, 0f)
                || !Mathf.Approximately(saturation.value, 0f);
        }
    }
}
