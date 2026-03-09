using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable]
    [VolumeComponentMenu("VividRP/HDRI Sky")]
    public sealed class HDRISkyVolume : VolumeComponent
    {
        public NoInterpCubemapParameter skyCubemap = new(null);
        public ColorParameter tint = new(Color.white, true, true, true);
        public MinFloatParameter exposure = new(1f, 0f);
        public ClampedFloatParameter rotation = new(0f, -180f, 180f);

        public bool HasSkyCubemap()
        {
            return skyCubemap.value != null;
        }
    }
}
