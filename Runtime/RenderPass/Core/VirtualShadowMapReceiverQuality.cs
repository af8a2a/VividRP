using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core
{
    // Receiver-only uniforms. Never inputs to clipmap layout, residency identity,
    // caster culling or static-cache invalidation.
    internal static class VirtualShadowMapReceiverQuality
    {
        internal static readonly int ParametersId = Shader.PropertyToID("_VSMReceiverQuality");
        internal static readonly int ViewProjectionId = Shader.PropertyToID("_VSMReceiverViewProjection");

        internal static Vector4 BuildParameters(CascadedShadowSettingsVolume settings)
            => settings == null ? Vector4.zero : BuildParameters(
                settings.virtualShadowMapScreenDensity.value,
                settings.virtualShadowMapTargetTexelPixels.value,
                settings.virtualShadowMapResolutionLodBias.value);

        internal static Vector4 BuildParameters(bool enabled, float targetTexelPixels, float lodBias)
            => new(enabled ? 1 : 0,
                Mathf.Clamp(targetTexelPixels, 0.25f, 8) * Mathf.Pow(2, Mathf.Clamp(lodBias, -4, 4)), 0, 0);
    }
}
