using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable, VolumeComponentMenu("Post-processing/White Balance")]
    public sealed class WhiteBalance : VolumeComponent, IPostProcessComponent
    {
        public ClampedFloatParameter temperature = new(0f, -100f, 100f);
        public ClampedFloatParameter tint = new(0f, -100f, 100f);

        public bool IsActive()
        {
            return !Mathf.Approximately(temperature.value, 0f)
                || !Mathf.Approximately(tint.value, 0f);
        }
    }
}
