using UnityEngine;

namespace VividRP.Runtime.VirtualShadowMap
{
    // Receiver-only uniforms. Never inputs to clipmap layout, residency identity,
    // caster culling or static-cache invalidation.
    internal static class VirtualShadowMapReceiverQuality
    {
        internal static readonly int PressureId = Shader.PropertyToID("_VSMPagePressure");
        internal static readonly int PressureRWId = Shader.PropertyToID("_VSMPagePressureRW");
        internal static readonly int ParametersId = Shader.PropertyToID("_VSMReceiverQuality");
        internal static readonly int ViewProjectionId = Shader.PropertyToID("_VSMReceiverViewProjection");
        internal static readonly int SMRTParametersId = Shader.PropertyToID("_VSMSMRTParameters");

        internal static readonly int SMRTSampleIndexOffsetId = Shader.PropertyToID("_VSMSMRTSampleIndexOffset");

        internal static int BuildSMRTSampleIndexOffset(
            CascadedShadowSettingsVolume settings, VividAdditionalCameraData camera, int frameIndex)
        {
            // UE STBN addresses time by StateFrameIndex; no TSR cycle remapping.
            return 0;
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
                    settings.virtualShadowMapSMRTRayLengthScale.value,
                    Mathf.Sin(Mathf.Clamp(angularDiameter, 0, 180) * (0.5f * Mathf.Deg2Rad)));

        internal static readonly int SMRTSettingsId = Shader.PropertyToID("_VSMSMRTSettings");

        internal static Vector4 BuildSMRTSettings(CascadedShadowSettingsVolume settings)
            => settings == null ? Vector4.zero : new Vector4(
                settings.virtualShadowMapSMRTExtrapolateMaxSlope.value,
                settings.virtualShadowMapSMRTTexelDitherScale.value,
                settings.virtualShadowMapSMRTAdaptiveRayCount.value, 0);

        internal static int SMRTPermutationIndex(int samples, bool adaptive, bool slope)
            => (slope ? 6 : 0) + (adaptive ? 3 : 0) + (samples == 2 ? 1 : samples == 4 ? 2 : 0);

        internal static bool BuildSMRTAdaptiveEnabled(CascadedShadowSettingsVolume settings, Vector4 smrtParameters)
            => settings != null && settings.virtualShadowMapSMRTAdaptiveRays.value
                && smrtParameters.x > 0 && smrtParameters.w > 0;

        internal static Vector4 BuildParameters(CascadedShadowSettingsVolume settings)
            => settings == null ? Vector4.zero : BuildClipmapParameters(
                settings.virtualShadowMapResolutionLodBias.value,
                settings.virtualShadowMapPagePressure.value, 2, 1, 1, PerformanceThrottleEnabled(settings));

        internal static Vector4 BuildParameters(CascadedShadowSettingsVolume settings,
            VividCameraData camera, int virtualResolution)
        {
            if (settings == null) return Vector4.zero;
            var projection = camera.GetProjectionMatrixNoJitter();
            float scaleX = Mathf.Max(Mathf.Abs(projection.m00), 1e-6f);
            int width = Mathf.Max(camera.actualWidth, 1);
            if (camera.camera != null && camera.camera.orthographic)
            {
                // UE's orthographic projection and OrthoWidth are centimetre based.
                // Perspective projection scales are unitless and need no conversion.
                scaleX *= 0.01f;
                width = Mathf.Max(width, Mathf.CeilToInt(2 / scaleX));
            }
            return BuildClipmapParameters(settings.virtualShadowMapResolutionLodBias.value,
                settings.virtualShadowMapPagePressure.value, virtualResolution, width, scaleX, PerformanceThrottleEnabled(settings));
        }

        private static bool PerformanceThrottleEnabled(CascadedShadowSettingsVolume settings)
            => settings.virtualShadowMapThrottleLoadBudget.value > 0;

        // UE FVirtualShadowMapClipmap: normalize to horizontal camera resolution,
        // including its doubled projection extent, then clamp the TOTAL bias.
        internal static Vector4 BuildClipmapParameters(float lodBias, bool pagePressure,
            int virtualResolution, int viewportWidth, float projectionScaleX, bool performanceThrottle = false)
            => new(pagePressure ? 2 : 1,
                Mathf.Max(0, lodBias + Mathf.Log(0.5f * Mathf.Max(virtualResolution, 1)
                    / (Mathf.Max(viewportWidth, 1) * Mathf.Max(Mathf.Abs(projectionScaleX), 1e-6f)), 2)),
                performanceThrottle ? 1 : 0, 0);

        internal static Vector4 BuildParameters(bool enabled, float targetTexelPixels, float lodBias,
            float coverageTransition = -1, bool pagePressure = false)
            // Serialized density/transition controls are retained for old assets;
            // UE selection is unconditional. This overload has unit LodScale.
            => BuildClipmapParameters(lodBias, pagePressure, 2, 1, 1);
    }
}
