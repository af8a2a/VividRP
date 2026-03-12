using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable, VolumeComponentMenu("Post-processing/Channel Mixer")]
    public sealed class ChannelMixer : VolumeComponent, IPostProcessComponent
    {
        [Header("Red Output")]
        public ClampedFloatParameter redOutRedIn = new(100f, -200f, 200f);
        public ClampedFloatParameter redOutGreenIn = new(0f, -200f, 200f);
        public ClampedFloatParameter redOutBlueIn = new(0f, -200f, 200f);

        [Header("Green Output")]
        public ClampedFloatParameter greenOutRedIn = new(0f, -200f, 200f);
        public ClampedFloatParameter greenOutGreenIn = new(100f, -200f, 200f);
        public ClampedFloatParameter greenOutBlueIn = new(0f, -200f, 200f);

        [Header("Blue Output")]
        public ClampedFloatParameter blueOutRedIn = new(0f, -200f, 200f);
        public ClampedFloatParameter blueOutGreenIn = new(0f, -200f, 200f);
        public ClampedFloatParameter blueOutBlueIn = new(100f, -200f, 200f);

        public bool IsActive()
        {
            return !Mathf.Approximately(redOutRedIn.value, 100f)
                || !Mathf.Approximately(redOutGreenIn.value, 0f)
                || !Mathf.Approximately(redOutBlueIn.value, 0f)
                || !Mathf.Approximately(greenOutRedIn.value, 0f)
                || !Mathf.Approximately(greenOutGreenIn.value, 100f)
                || !Mathf.Approximately(greenOutBlueIn.value, 0f)
                || !Mathf.Approximately(blueOutRedIn.value, 0f)
                || !Mathf.Approximately(blueOutGreenIn.value, 0f)
                || !Mathf.Approximately(blueOutBlueIn.value, 100f);
        }
    }
}
