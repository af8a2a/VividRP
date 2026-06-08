using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Runtime
{
    internal struct ScreenSpaceReflectionSettingsData
    {
        public bool enabled;
        public ScreenSpaceReflectionExecutionPath executionPath;
        public float intensity;
        public float minSmoothness;
        public float smoothnessFadeStart;
        public bool reflectSky;
        public float clampValue;
        public float depthBufferThickness;
        public float screenFadeDistance;
        public int rayMaxIterations;
        public float accumulationFactor;
        public float biasFactor;
        public float reBlurDenoiserRadius;
        public float reBlurAntiFlickeringStrength;

        public static ScreenSpaceReflectionSettingsData CreateDefault()
        {
            return new ScreenSpaceReflectionSettingsData
            {
                enabled = false,
                executionPath = ScreenSpaceReflectionExecutionPath.Vivid,
                intensity = 1.0f,
                minSmoothness = 0.9f,
                smoothnessFadeStart = 0.9f,
                reflectSky = true,
                clampValue = 100.0f,
                depthBufferThickness = 0.01f,
                screenFadeDistance = 0.1f,
                rayMaxIterations = 32,
                accumulationFactor = 0.75f,
                biasFactor = 0.5f,
                reBlurDenoiserRadius = 1.0f,
                reBlurAntiFlickeringStrength = 0.5f
            };
        }
    }

    internal static class ScreenSpaceReflectionSettingsResolver
    {
        internal static ScreenSpaceReflectionSettingsData Resolve()
        {
            var settings = ScreenSpaceReflectionSettingsData.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var ssr = stack.GetComponent<ScreenSpaceReflection>();
            if (ssr == null || !ssr.IsActive())
                return settings;

            settings.enabled = true;
            settings.executionPath = ssr.executionPath?.value ?? ScreenSpaceReflectionExecutionPath.Vivid;
            settings.intensity = Mathf.Clamp(ssr.intensity.value, 0.0f, 2.0f);
            settings.minSmoothness = Mathf.Clamp01(ssr.minSmoothness.value);
            settings.smoothnessFadeStart = Mathf.Clamp01(Mathf.Max(ssr.smoothnessFadeStart.value, settings.minSmoothness));
            settings.reflectSky = ssr.reflectSky.value;
            settings.clampValue = Mathf.Max(ssr.clampValue.value, 0.001f);
            settings.depthBufferThickness = Mathf.Clamp(ssr.depthBufferThickness.value, 0.0001f, 1.0f);
            settings.screenFadeDistance = Mathf.Clamp(ssr.screenFadeDistance.value, 0.0001f, 1.0f);
            settings.rayMaxIterations = Mathf.Clamp(ssr.rayMaxIterations.value, 1, 128);
            settings.accumulationFactor = Mathf.Clamp01(ssr.accumulationFactor.value);
            settings.biasFactor = Mathf.Clamp01(ssr.biasFactor.value);
            settings.reBlurDenoiserRadius = Mathf.Clamp01(ssr.reBlurDenoiserRadius.value);
            settings.reBlurAntiFlickeringStrength = Mathf.Clamp01(ssr.reBlurAntiFlickeringStrength.value);
            return settings;
        }
    }
}
