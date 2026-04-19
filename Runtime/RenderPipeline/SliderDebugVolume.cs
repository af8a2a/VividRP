using System;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Obsolete("SliderDebugVolume is deprecated. Use the Rendering Debugger instead.")]
    [Serializable]
    public sealed class SliderDebugVolume : VolumeComponent
    {
        public ClampedFloatParameter slider = new(50f, 0f, 100f);

        public bool IsActive()
        {
            return active && slider != null && slider.overrideState;
        }
    }
}
