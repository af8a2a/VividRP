using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable]
    [VolumeComponentMenu("Post-processing/Ground Truth Ambient Occlusion")]
    public sealed class GTAO : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Whether GTAO is enabled.")]
        public BoolParameter enabled = new(false);

        [Tooltip("Quality level: 0 = Low, 1 = Medium, 2 = High, 3 = Ultra.")]
        public ClampedIntParameter qualityLevel = new(2, 0, 3);

        [Tooltip("Edge-aware denoise passes. 0 keeps the final resolve only.")]
        public ClampedIntParameter denoisePasses = new(1, 0, 3);

        [Tooltip("Ambient occlusion radius in view-space units.")]
        public ClampedFloatParameter radius = new(0.5f, 0.0f, 100.0f);

        [Tooltip("Controls the distance range where occlusion fades out.")]
        public ClampedFloatParameter falloffRange = new(0.615f, 0.0f, 1.0f);

        [Tooltip("Final visibility shaping power.")]
        public ClampedFloatParameter finalValuePower = new(2.2f, 0.5f, 5.0f);

        public bool IsActive()
        {
            return enabled.value && radius.value > 0.0001f;
        }
    }
}
