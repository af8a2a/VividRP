using System;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{

    public enum SkyGeneratedCubemapQuality
    {
        PlatformDefault = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Ultra = 4
    }
    /// <summary>
    /// Sky Intensity Mode.
    /// </summary>
    public enum SkyIntensityMode
    {
        /// <summary>Intensity is expressed as an exposure.</summary>
        Exposure,
        /// <summary>Intensity is expressed in lux.</summary>
        Lux,
        /// <summary>Intensity is expressed as a multiplier.</summary>
        Multiplier,
    }



    [Serializable]
    [VolumeComponentMenu("VividRP/Sky Settings")]
    public sealed class SkySettingsVolume : VolumeComponent
    {
        public EnumParameter<SkyType> skyType = new(SkyType.HDRI);
        public EnumParameter<SkyUpdateMode> updateMode = new(SkyUpdateMode.OnChanged);
        public MinFloatParameter updatePeriod = new(0.0f, 0.0f);
        public EnumParameter<SkyGeneratedCubemapQuality> generatedCubemapQuality = new(SkyGeneratedCubemapQuality.PlatformDefault);

        protected override void OnEnable()
        {
            skyType ??= new EnumParameter<SkyType>(SkyType.HDRI);
            updateMode ??= new EnumParameter<SkyUpdateMode>(SkyUpdateMode.OnChanged);
            updatePeriod ??= new MinFloatParameter(0.0f, 0.0f);
            generatedCubemapQuality ??= new EnumParameter<SkyGeneratedCubemapQuality>(SkyGeneratedCubemapQuality.PlatformDefault);
            base.OnEnable();
        }


        internal static int GetGeneratedCubemapViewSampleCount(SkySettingsVolume settings = null)
        {
            var quality = settings?.generatedCubemapQuality?.value ?? SkyGeneratedCubemapQuality.PlatformDefault;
            return quality switch
            {
                SkyGeneratedCubemapQuality.Low => 8,
                SkyGeneratedCubemapQuality.High => 16,
                SkyGeneratedCubemapQuality.Ultra => 24,
                _ => 12
            };
        }

        internal static int GetGeneratedCubemapLightSampleCount(SkySettingsVolume settings = null)
        {
            var quality = settings?.generatedCubemapQuality?.value ?? SkyGeneratedCubemapQuality.PlatformDefault;
            return quality switch
            {
                SkyGeneratedCubemapQuality.Low => 4,
                SkyGeneratedCubemapQuality.High => 8,
                SkyGeneratedCubemapQuality.Ultra => 12,
                _ => 6
            };
        }

        internal static int GetGeneratedCubemapResolution(SkySettingsVolume settings = null)
        {
            var quality = settings?.generatedCubemapQuality?.value ?? SkyGeneratedCubemapQuality.PlatformDefault;
            return quality switch
            {
                SkyGeneratedCubemapQuality.Low => 16,
                SkyGeneratedCubemapQuality.High => 64,
                SkyGeneratedCubemapQuality.Ultra => 128,
                _ => 32
            };
        }
    }
}
