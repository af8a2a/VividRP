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

        protected override void OnEnable()
        {
            EnsureDefaultSkyCubemapAssigned();
            base.OnEnable();
        }

        internal static Cubemap GetDefaultSkyCubemap()
        {
            return PipelineResourceManager.Get<VividRPCoreResources>()?.DefaultHDRISkyCubemap;
        }

        public Cubemap GetSkyCubemapOrDefault()
        {
            return skyCubemap.value != null ? skyCubemap.value : GetDefaultSkyCubemap();
        }

        public bool HasSkyCubemap()
        {
            return GetSkyCubemapOrDefault() != null;
        }

        private void EnsureDefaultSkyCubemapAssigned()
        {
            if (skyCubemap == null)
                skyCubemap = new NoInterpCubemapParameter(null);

            if (skyCubemap.value != null)
                return;

            skyCubemap.value = GetDefaultSkyCubemap();
        }
    }
}
