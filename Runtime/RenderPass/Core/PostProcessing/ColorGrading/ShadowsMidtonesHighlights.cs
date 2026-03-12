using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable, VolumeComponentMenu("Post-processing/Shadows Midtones Highlights")]
    public sealed class ShadowsMidtonesHighlights : VolumeComponent, IPostProcessComponent
    {
        public ColorParameter shadows = new(new Color(1f, 1f, 1f, 0f), false, true, true);
        public ColorParameter midtones = new(new Color(1f, 1f, 1f, 0f), false, true, true);
        public ColorParameter highlights = new(new Color(1f, 1f, 1f, 0f), false, true, true);
        public ClampedFloatParameter shadowsStart = new(0f, 0f, 1f);
        public ClampedFloatParameter shadowsEnd = new(0.3f, 0f, 1f);
        public ClampedFloatParameter highlightsStart = new(0.55f, 0f, 1f);
        public ClampedFloatParameter highlightsEnd = new(1f, 0f, 1f);

        public bool IsActive()
        {
            return !ColorGradingCurvePresets.IsApproximately(shadows.value, new Color(1f, 1f, 1f, 0f))
                || !ColorGradingCurvePresets.IsApproximately(midtones.value, new Color(1f, 1f, 1f, 0f))
                || !ColorGradingCurvePresets.IsApproximately(highlights.value, new Color(1f, 1f, 1f, 0f))
                || !Mathf.Approximately(shadowsStart.value, 0f)
                || !Mathf.Approximately(shadowsEnd.value, 0.3f)
                || !Mathf.Approximately(highlightsStart.value, 0.55f)
                || !Mathf.Approximately(highlightsEnd.value, 1f);
        }
    }
}
