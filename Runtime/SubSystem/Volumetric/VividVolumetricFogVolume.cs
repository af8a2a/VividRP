using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum VividVolumetricFogControlMode
    {
        Balance = 0,
        Manual = 1
    }

    public enum VividVolumetricFogDenoisingMode
    {
        None = 0,
        Gaussian = 1
    }

    [Serializable]
    [VolumeComponentMenu("VividRP/Volumetric Fog")]
    public sealed class VividVolumetricFogVolume : VolumeComponent
    {
        public const float MinScreenResolutionPercentage = 6.25f;
        public const float DefaultScreenResolutionPercentage = 12.5f;
        public const float MaxScreenResolutionPercentage = 50.0f;
        public const int MinVolumeSliceCount = 1;
        public const int DefaultVolumeSliceCount = 64;
        public const int MaxVolumeSliceCount = 512;
        public const float DefaultDepthExtent = 64.0f;
        public const float DefaultMeanFreePath = 100.0f;

        [Tooltip("Enables VividRP volumetric fog for explicit Volumetric RenderGraph passes.")]
        public BoolParameter enabled = new(false);

        [Header("Global Fog")]
        [Tooltip("Scattering albedo of the global fog volume.")]
        public ColorParameter albedo = new(Color.white, false, false, true);

        [Tooltip("Average distance, in meters, before light is scattered or absorbed.")]
        public MinFloatParameter meanFreePath = new(DefaultMeanFreePath, 0.001f);

        [Tooltip("World-space base height of the fog layer.")]
        public FloatParameter baseHeight = new(0.0f);

        [Tooltip("World-space maximum height of the fog layer.")]
        public FloatParameter maximumHeight = new(50.0f);

        [Tooltip("Phase anisotropy. Positive values bias scattering forward.")]
        public ClampedFloatParameter anisotropy = new(0.0f, -0.95f, 0.95f);

        [Tooltip("Contribution multiplier for ambient probe lighting inside the volume.")]
        public ClampedFloatParameter globalLightProbeDimmer = new(1.0f, 0.0f, 1.0f);

        [Header("VBuffer")]
        [Tooltip("Maximum view distance covered by the volumetric buffer.")]
        public MinFloatParameter depthExtent = new(DefaultDepthExtent, 0.01f);

        [Tooltip("Blends logarithmic depth slicing toward uniform slicing.")]
        public ClampedFloatParameter sliceDistributionUniformity = new(0.0f, 0.0f, 1.0f);

        [Tooltip("Controls whether VBuffer resolution is budget-driven or set manually.")]
        public EnumParameter<VividVolumetricFogControlMode> fogControlMode =
            new(VividVolumetricFogControlMode.Balance);

        [Tooltip("Normalized VBuffer cost target used by Balance mode.")]
        public ClampedFloatParameter volumetricFogBudget = new(0.25f, 0.0f, 1.0f);

        [Tooltip("Depth resolution multiplier used by Balance mode.")]
        public ClampedFloatParameter resolutionDepthRatio = new(0.5f, 0.0f, 1.0f);

        [Tooltip("Manual VBuffer screen resolution percentage.")]
        public ClampedFloatParameter screenResolutionPercentage =
            new(DefaultScreenResolutionPercentage, MinScreenResolutionPercentage, MaxScreenResolutionPercentage);

        [Tooltip("Manual number of VBuffer depth slices.")]
        public ClampedIntParameter volumeSliceCount =
            new(DefaultVolumeSliceCount, MinVolumeSliceCount, MaxVolumeSliceCount);

        [Tooltip("Spatial denoising mode for the VBuffer lighting result.")]
        public EnumParameter<VividVolumetricFogDenoisingMode> denoisingMode =
            new(VividVolumetricFogDenoisingMode.Gaussian);

        [Tooltip("Limits volumetric lighting to directional lights.")]
        public BoolParameter directionalLightsOnly = new(false);

        [Tooltip("Extinction values at or below this threshold skip in-scattered lighting evaluation. Density still contributes transmittance.")]
        public ClampedFloatParameter volumetricLightingDensityCutoff = new(0.00001f, 0.0f, 0.01f);

        public bool IsActive()
        {
            return active
                && enabled.value
                && meanFreePath.value > 0.0f
                && depthExtent.value > 0.0f
                && volumeSliceCount.value > 0;
        }

        internal float GetExtinction()
        {
            return 1.0f / Mathf.Max(meanFreePath.value, 0.001f);
        }

        internal Vector3 GetScattering()
        {
            var extinction = GetExtinction();
            var color = albedo.value;
            return new Vector3(color.r * extinction, color.g * extinction, color.b * extinction);
        }
    }
}
