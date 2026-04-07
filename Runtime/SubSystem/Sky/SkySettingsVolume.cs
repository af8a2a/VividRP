using System;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum SkyGeneratedCubemapResolution
    {
        Resolution32 = 32,
        Resolution64 = 64,
        Resolution128 = 128,
        Resolution256 = 256
    }

    public enum SkySpecularPrefilterResolution
    {
        Source = 0,
        Resolution32 = 32,
        Resolution64 = 64,
        Resolution128 = 128,
        Resolution256 = 256,
        Resolution512 = 512
    }

    public enum SkySpecularPrefilterQuality
    {
        PlatformDefault = 0,
        Low = 21,
        Medium = 34,
        High = 55,
        Ultra = 89
    }

    [Serializable]
    [VolumeComponentMenu("VividRP/Sky Settings")]
    public sealed class SkySettingsVolume : VolumeComponent
    {
        public EnumParameter<SkyType> skyType = new(SkyType.HDRI);
        public EnumParameter<SkyUpdateMode> updateMode = new(SkyUpdateMode.OnChanged);
        public MinFloatParameter updatePeriod = new(0.0f, 0.0f);
        public EnumParameter<SkyGeneratedCubemapResolution> generatedCubemapResolution = new(SkyGeneratedCubemapResolution.Resolution64);
        public EnumParameter<SkySpecularPrefilterResolution> specularPrefilterResolution = new(SkySpecularPrefilterResolution.Source);
        public EnumParameter<SkySpecularPrefilterQuality> specularPrefilterQuality = new(SkySpecularPrefilterQuality.PlatformDefault);

        protected override void OnEnable()
        {
            skyType ??= new EnumParameter<SkyType>(SkyType.HDRI);
            updateMode ??= new EnumParameter<SkyUpdateMode>(SkyUpdateMode.OnChanged);
            updatePeriod ??= new MinFloatParameter(0.0f, 0.0f);
            generatedCubemapResolution ??= new EnumParameter<SkyGeneratedCubemapResolution>(SkyGeneratedCubemapResolution.Resolution64);
            specularPrefilterResolution ??= new EnumParameter<SkySpecularPrefilterResolution>(SkySpecularPrefilterResolution.Source);
            specularPrefilterQuality ??= new EnumParameter<SkySpecularPrefilterQuality>(SkySpecularPrefilterQuality.PlatformDefault);
            base.OnEnable();
        }

        internal static int GetGeneratedCubemapResolution(SkySettingsVolume settings = null)
        {
            var resolution = settings?.generatedCubemapResolution != null
                ? (int)settings.generatedCubemapResolution.value
                : (int)SkyGeneratedCubemapResolution.Resolution64;
            return Math.Max(32, resolution);
        }

        internal static int GetSpecularPrefilterResolution(SkySettingsVolume settings = null)
        {
            return settings?.specularPrefilterResolution != null
                ? (int)settings.specularPrefilterResolution.value
                : (int)SkySpecularPrefilterResolution.Source;
        }

        internal static int GetSpecularPrefilterMaxSampleCount(SkySettingsVolume settings = null)
        {
            return settings?.specularPrefilterQuality != null
                ? Math.Max(0, (int)settings.specularPrefilterQuality.value)
                : (int)SkySpecularPrefilterQuality.PlatformDefault;
        }
    }
}
