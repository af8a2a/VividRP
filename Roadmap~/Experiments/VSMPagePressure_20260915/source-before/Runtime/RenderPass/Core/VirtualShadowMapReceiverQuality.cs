using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core
{
    // Receiver-only uniforms. Never inputs to clipmap layout, residency identity,
    // caster culling or static-cache invalidation.
    internal static class VirtualShadowMapReceiverQuality
    {
        internal static readonly int ParametersId = Shader.PropertyToID("_VSMReceiverQuality");
        internal static readonly int ViewProjectionId = Shader.PropertyToID("_VSMReceiverViewProjection");
        internal static readonly int SMRTParametersId = Shader.PropertyToID("_VSMSMRTParameters");

        internal static readonly int SMRTSampleIndexOffsetId = Shader.PropertyToID("_VSMSMRTSampleIndexOffset");

        internal static int BuildSMRTSampleIndexOffset(
            CascadedShadowSettingsVolume settings, VividAdditionalCameraData camera, int frameIndex)
        {
            if (settings == null || !settings.virtualShadowMapSMRT.value
                || !settings.virtualShadowMapSMRTJointSampling.value || camera == null || !camera.enableTSR)
                return 0;
            return CalculateSMRTSampleIndexOffset(frameIndex, camera.tsrJitterPhaseCount);
        }

        internal static int CalculateSMRTSampleIndexOffset(int frameIndex, int jitterPhaseCount)
        {
            // At a fixed jitter phase the native BND stride is P modulo 256.
            // Even P needs one extra index per cycle: P+1 is coprime to 256.
            // Odd P already visits every index; changing it would lose coverage.
            if (frameIndex < 0 || jitterPhaseCount <= 1 || (jitterPhaseCount & 1) != 0)
                return 0;
            return (frameIndex / jitterPhaseCount) & 255;
        }

        internal static Vector4 BuildSMRTParameters(CascadedShadowSettingsVolume settings, float angularDiameter)
            => settings == null || !settings.virtualShadowMapSMRT.value || angularDiameter <= 0
                ? Vector4.zero : new Vector4(
                    settings.virtualShadowMapSMRTRayCount.value,
                    settings.virtualShadowMapSMRTSamplesPerRay.value,
                    settings.virtualShadowMapSMRTMaxRayLength.value,
                    Mathf.Tan(Mathf.Min(angularDiameter, 10) * (0.5f * Mathf.Deg2Rad)));

        internal static Vector4 BuildParameters(CascadedShadowSettingsVolume settings)
            => settings == null ? Vector4.zero : BuildParameters(
                settings.virtualShadowMapScreenDensity.value,
                settings.virtualShadowMapTargetTexelPixels.value,
                settings.virtualShadowMapResolutionLodBias.value,
                settings.virtualShadowMapCoverageTransition.overrideState
                    ? settings.virtualShadowMapCoverageTransition.value : settings.virtualShadowMapTransition.value);

        internal static Vector4 BuildParameters(bool enabled, float targetTexelPixels, float lodBias,
            float coverageTransition = -1)
            => new(enabled ? 1 : 0,
                Mathf.Clamp(targetTexelPixels, 0.25f, 8) * Mathf.Pow(2, Mathf.Clamp(lodBias, -4, 4)),
                0.5f * Mathf.Clamp(coverageTransition, 0, 0.5f), coverageTransition >= 0 ? 1 : 0);
    }
}
