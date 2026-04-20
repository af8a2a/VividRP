using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct GTAOSettingsData
    {
        public bool enabled;
        public int qualityLevel;
        public int denoisePasses;
        public float radius;
        public float falloffRange;
        public float finalValuePower;

        public static GTAOSettingsData CreateDefault()
        {
            return new GTAOSettingsData
            {
                enabled = false,
                qualityLevel = 2,
                denoisePasses = 1,
                radius = 0.5f,
                falloffRange = 0.615f,
                finalValuePower = 2.2f
            };
        }
    }

    internal static class GTAOSettingsResolver
    {
        internal static GTAOSettingsData Resolve()
        {
            var settings = GTAOSettingsData.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var gtao = stack.GetComponent<GTAO>();
            if (gtao == null || !gtao.IsActive())
                return settings;

            settings.enabled = true;
            settings.qualityLevel = Mathf.Clamp(gtao.qualityLevel.value, 0, 3);
            settings.denoisePasses = Mathf.Clamp(gtao.denoisePasses.value, 0, 3);
            settings.radius = Mathf.Max(0.0001f, gtao.radius.value);
            settings.falloffRange = Mathf.Clamp(gtao.falloffRange.value, 0.001f, 1.0f);
            settings.finalValuePower = Mathf.Max(0.001f, gtao.finalValuePower.value);
            return settings;
        }
    }
}
