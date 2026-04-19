using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct DiffusionSettingsData
    {
        public bool enabled;
        public DiffusionMode mode;
        public float multiply;
        public float blurScale;
        public float filter;
        public float intensity;
        public float blurIntensity;

        public static DiffusionSettingsData CreateDefault()
        {
            return new DiffusionSettingsData
            {
                enabled = false,
                mode = DiffusionMode.Filter,
                multiply = 0.5f,
                blurScale = 0.5f,
                filter = 0.5f,
                intensity = 0f,
                blurIntensity = 1f
            };
        }
    }

    internal static class DiffusionSettingsResolver
    {
        internal static DiffusionSettingsData Resolve()
        {
            var settings = DiffusionSettingsData.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var diffusion = stack.GetComponent<Diffusion>();
            if (diffusion == null || !diffusion.IsActive())
                return settings;

            settings.enabled = true;
            settings.mode = diffusion.mode.value;
            settings.multiply = diffusion.multiply.value;
            settings.blurScale = diffusion.blurScale.value;
            settings.filter = diffusion.filter.value;
            settings.intensity = diffusion.intensity.value;
            settings.blurIntensity = diffusion.blurIntensity.value;
            return settings;
        }
    }
}
