using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct BloomSettingsData
    {
        private static readonly BloomSettingsData s_Default = new()
        {
            enabled = false,
            threshold = 0f,
            intensity = 0f,
            scatter = 0.7f,
            tint = Color.white,
            dirtTexture = null,
            dirtIntensity = 0f,
            anamorphic = 0f,
            resolution = BloomResolution.Half,
            highQualityPrefiltering = false,
            highQualityFiltering = true,
            experimentalSpdDownsample = false
        };

        public bool enabled;
        public float threshold;
        public float intensity;
        public float scatter;
        public Color tint;
        public Texture dirtTexture;
        public float dirtIntensity;
        public float anamorphic;
        public BloomResolution resolution;
        public bool highQualityPrefiltering;
        public bool highQualityFiltering;
        public bool experimentalSpdDownsample;

        public static BloomSettingsData CreateDefault()
        {
            return s_Default;
        }
    }

    internal static class BloomSettingsResolver
    {
        internal static BloomSettingsData Resolve()
        {
            var settings = BloomSettingsData.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var bloom = stack.GetComponent<Bloom>();
            if (bloom == null || !bloom.IsActive())
                return settings;

            settings.enabled = true;
            settings.threshold = bloom.threshold.value;
            settings.intensity = bloom.intensity.value;
            settings.scatter = bloom.scatter.value;
            settings.tint = bloom.tint.value;
            settings.dirtTexture = bloom.dirtTexture.value;
            settings.dirtIntensity = bloom.dirtIntensity.value;
            settings.anamorphic = bloom.anamorphic.value;
            settings.resolution = bloom.resolution.value;
            settings.highQualityPrefiltering = bloom.highQualityPrefiltering.value;
            settings.highQualityFiltering = bloom.highQualityFiltering.value;
            settings.experimentalSpdDownsample = bloom.experimentalSpdDownsample.value;
            return settings;
        }
    }
}
