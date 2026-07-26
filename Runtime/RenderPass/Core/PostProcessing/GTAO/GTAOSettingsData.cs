using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct GTAOSettingsData
    {
        public bool enabled;
        public AmbientOcclusionImplementation implementation;
        public int qualityLevel;
        public int denoisePasses;
        public float radius;
        public float falloffRange;
        public float finalValuePower;
        public bool cacaoDownsampled;
        public float cacaoShadowMultiplier;
        public float cacaoShadowPower;
        public float cacaoShadowClamp;
        public float cacaoHorizonAngleThreshold;
        public float cacaoFadeOutFrom;
        public float cacaoFadeOutTo;
        public float cacaoAdaptiveQualityLimit;
        public int cacaoBlurPasses;
        public float cacaoSharpness;
        public float cacaoDetailShadowStrength;
        public float cacaoBilateralSigmaSquared;
        public float cacaoBilateralSimilarityDistanceSigma;

        public static GTAOSettingsData CreateDefault()
        {
            return new GTAOSettingsData
            {
                enabled = false,
                implementation = AmbientOcclusionImplementation.GTAO,
                qualityLevel = 2,
                denoisePasses = 1,
                radius = 0.5f,
                falloffRange = 0.615f,
                finalValuePower = 2.2f,
                cacaoDownsampled = false,
                cacaoShadowMultiplier = 1.0f,
                cacaoShadowPower = 1.5f,
                cacaoShadowClamp = 0.98f,
                cacaoHorizonAngleThreshold = 0.06f,
                cacaoFadeOutFrom = 50.0f,
                cacaoFadeOutTo = 300.0f,
                cacaoAdaptiveQualityLimit = 0.45f,
                cacaoBlurPasses = 2,
                cacaoSharpness = 0.98f,
                cacaoDetailShadowStrength = 0.5f,
                cacaoBilateralSigmaSquared = 5.0f,
                cacaoBilateralSimilarityDistanceSigma = 0.01f
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

            var ambientOcclusion = stack.GetComponent<AmbientOcclusion>();
            if (ambientOcclusion == null || !ambientOcclusion.IsActive())
                return settings;

            settings.enabled = true;
            settings.implementation = ambientOcclusion.implementation.value;
            settings.qualityLevel = settings.implementation == AmbientOcclusionImplementation.FidelityFXCACAO
                ? Mathf.Clamp(ambientOcclusion.qualityLevel.value, 0, 4)
                : Mathf.Clamp(ambientOcclusion.qualityLevel.value, 0, 3);
            settings.denoisePasses = Mathf.Clamp(ambientOcclusion.denoisePasses.value, 0, 3);
            settings.radius = Mathf.Max(0.0001f, ambientOcclusion.radius.value);
            settings.falloffRange = Mathf.Clamp(
                ambientOcclusion.falloffRange.value,
                0.001f,
                1.0f);
            settings.finalValuePower = Mathf.Max(
                0.001f,
                ambientOcclusion.finalValuePower.value);
            settings.cacaoDownsampled = ambientOcclusion.cacaoDownsampled.value;
            settings.cacaoShadowMultiplier = Mathf.Clamp(
                ambientOcclusion.cacaoShadowMultiplier.value,
                0.0f,
                5.0f);
            settings.cacaoShadowPower = Mathf.Clamp(
                ambientOcclusion.cacaoShadowPower.value,
                0.5f,
                5.0f);
            settings.cacaoShadowClamp = Mathf.Clamp01(
                ambientOcclusion.cacaoShadowClamp.value);
            settings.cacaoHorizonAngleThreshold = Mathf.Clamp(
                ambientOcclusion.cacaoHorizonAngleThreshold.value,
                0.0f,
                0.2f);
            settings.cacaoFadeOutFrom = Mathf.Max(
                0.0f,
                ambientOcclusion.cacaoFadeOutFrom.value);
            settings.cacaoFadeOutTo = Mathf.Max(
                settings.cacaoFadeOutFrom + 0.001f,
                ambientOcclusion.cacaoFadeOutTo.value);
            settings.cacaoAdaptiveQualityLimit = Mathf.Clamp01(
                ambientOcclusion.cacaoAdaptiveQualityLimit.value);
            settings.cacaoBlurPasses = Mathf.Clamp(
                ambientOcclusion.cacaoBlurPasses.value,
                0,
                8);
            settings.cacaoSharpness = Mathf.Clamp01(
                ambientOcclusion.cacaoSharpness.value);
            settings.cacaoDetailShadowStrength = Mathf.Clamp(
                ambientOcclusion.cacaoDetailShadowStrength.value,
                0.0f,
                5.0f);
            settings.cacaoBilateralSigmaSquared = Mathf.Max(
                0.0001f,
                ambientOcclusion.cacaoBilateralSigmaSquared.value);
            settings.cacaoBilateralSimilarityDistanceSigma =
                Mathf.Max(
                    0.0001f,
                    ambientOcclusion.cacaoBilateralSimilarityDistanceSigma.value);
            return settings;
        }
    }
}
