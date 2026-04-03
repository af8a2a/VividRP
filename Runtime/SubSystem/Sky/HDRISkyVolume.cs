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
        [Tooltip("Exposure compensation in EV stops. 0 keeps the source HDRI intensity unchanged, 1 is 2x brighter, -1 is 2x darker.")]
        public FloatParameter exposure = new(0f);
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

        internal static float ResolveExposureMultiplier(float exposureStops)
        {
            return Mathf.Pow(2f, exposureStops);
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
