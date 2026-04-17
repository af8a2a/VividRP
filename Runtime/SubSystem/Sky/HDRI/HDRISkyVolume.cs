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
        /// <summary>Exposure of the sky.</summary>
        [Tooltip("Sets the exposure of the sky in EV.")]
        public FloatParameter exposure = new FloatParameter(0.0f);
        /// <summary>Intensity Multipler of the sky.</summary>
        [Tooltip("Sets the intensity multiplier for the sky.")]
        public MinFloatParameter multiplier = new MinFloatParameter(1.0f, 0.0f);
        
        
        /// <summary>Informative helper that displays the relative intensity (in Lux) for the current HDR texture set in HDRI Sky.</summary>
        [Tooltip("Informative helper that displays the relative intensity (in Lux) for the current HDR texture set in HDRI Sky.")]
        public MinFloatParameter upperHemisphereLuxValue = new MinFloatParameter(1.0f, 0.0f);
        /// <summary>Absolute intensity (in lux) of the sky.</summary>
        [Tooltip("Sets the absolute intensity (in Lux) of the current HDR texture set in HDRI Sky. Functions as a Lux intensity multiplier for the sky.")]
        public FloatParameter desiredLuxValue = new FloatParameter(20000);

        
        /// <summary>Intensity mode of the sky.</summary>
        [Tooltip("Specifies the intensity mode VividRP uses for the sky.")]
        public EnumParameter<SkyIntensityMode> skyIntensityMode = new (SkyIntensityMode.Exposure);
        public ClampedFloatParameter rotation = new(0f, -180f, 180f);

        protected override void OnEnable()
        {
            EnsureDefaultSkyCubemapAssigned();
            if (upperHemisphereLuxValue != null && skyCubemap != null)
                upperHemisphereLuxValue.overrideState = skyCubemap.overrideState;
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
        
        
        /// <summary>
        /// Returns the sky intensity as determined by this SkySetting.
        /// </summary>
        /// <returns>The sky intensity.</returns>
        public float GetIntensityFromSettings()
        {
            return SkyIntensityUtility.GetIntensityFromSettings(
                skyIntensityMode.value,
                exposure.value,
                multiplier.value,
                upperHemisphereLuxValue.value,
                desiredLuxValue.value);
        }

    }
}
