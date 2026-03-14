using System;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable]
    [VolumeComponentMenu("VividRP/Debug/Slider Debug")]
    public sealed class SliderDebugVolume : VolumeComponent
    {
        public ClampedFloatParameter slider = new(50f, 0f, 100f);

        public bool IsActive()
        {
            return active && slider != null && slider.overrideState;
        }
    }
}
