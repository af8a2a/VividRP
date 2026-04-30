using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum VividFogColorMode
    {
        [InspectorName("Sky Color")]
        SkyColor = 0,
        [InspectorName("Constant Color")]
        ConstantColor = 1
    }

    public enum VividVolumetricFogControlMode
    {
        Balance = 0,
        Manual = 1
    }

    public enum VividVolumetricFogDenoisingMode
    {
        None = 0,
        Reprojection = 2,
        Gaussian = 1,
        Both = 3
    }

    public enum VividVolumetricFogQualityTier
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3,
        Custom = 4
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
        public const float DefaultDepthExtent = 50.0f;
        public const float DefaultMeanFreePath = 2000.0f;
        public const float DefaultMaxFogDistance = 5000.0f;
        public const float DefaultMipFogFar = 1000.0f;
        public const float DefaultVolumetricFogBudget = 1.0f / 3.0f;

        [Tooltip("Enables VividRP volumetric fog for explicit Volumetric RenderGraph passes.")]
        public BoolParameter enabled = new(false);

        [Tooltip("Average distance, in meters, before light is scattered or absorbed.")]
        public MinFloatParameter meanFreePath = new(DefaultMeanFreePath, 0.001f);

        [Tooltip("World-space base height of the fog layer.")]
        public FloatParameter baseHeight = new(0.0f);

        [Tooltip("World-space maximum height of the fog layer.")]
        public FloatParameter maximumHeight = new(500.0f);

        [Tooltip("Maximum distance affected by height fog.")]
        public MinFloatParameter maxFogDistance = new(DefaultMaxFogDistance, 0.0f);

        [Tooltip("Controls the color source for non-volumetric fog authoring.")]
        public EnumParameter<VividFogColorMode> colorMode = new(VividFogColorMode.SkyColor);

        [Tooltip("Constant tint used when Color Mode is Constant Color.")]
        public ColorParameter tint = new(Color.white, false, false, true);

        [Tooltip("Distance at which mip fog starts sampling lower sky mips.")]
        public MinFloatParameter mipFogNear = new(0.0f, 0.0f);

        [Tooltip("Distance at which mip fog reaches its maximum sky mip.")]
        public MinFloatParameter mipFogFar = new(DefaultMipFogFar, 0.0f);

        [Tooltip("Maximum sky mip used by mip fog.")]
        public ClampedFloatParameter mipFogMaxMip = new(0.5f, 0.0f, 1.0f);

        [Tooltip("Enables volumetric fog inside the global fog component.")]
        public BoolParameter volumetricFog = new(true);

        [Tooltip("Scattering albedo of the global fog volume.")]
        public ColorParameter albedo = new(Color.white, false, false, true);

        [Tooltip("Phase anisotropy. Positive values bias scattering forward.")]
        public ClampedFloatParameter anisotropy = new(0.0f, -0.95f, 0.95f);

        [Tooltip("Contribution multiplier for ambient probe lighting inside the volume.")]
        public ClampedFloatParameter globalLightProbeDimmer = new(0.0f, 0.0f, 1.0f);

        [Tooltip("Maximum view distance covered by the volumetric buffer.")]
        public MinFloatParameter depthExtent = new(DefaultDepthExtent, 0.01f);

        [Tooltip("Blends logarithmic depth slicing toward uniform slicing.")]
        public ClampedFloatParameter sliceDistributionUniformity = new(1.0f, 0.0f, 1.0f);

        [Tooltip("HDRP-style quality tier used for authoring parity. Custom exposes explicit VBuffer quality controls.")]
        public EnumParameter<VividVolumetricFogQualityTier> tier =
            new(VividVolumetricFogQualityTier.Custom);

        [Tooltip("Controls whether VBuffer resolution is budget-driven or set manually.")]
        public EnumParameter<VividVolumetricFogControlMode> fogControlMode =
            new(VividVolumetricFogControlMode.Balance);

        [Tooltip("Normalized VBuffer cost target used by Balance mode.")]
        public ClampedFloatParameter volumetricFogBudget = new(DefaultVolumetricFogBudget, 0.0f, 1.0f);

        [Tooltip("Depth resolution multiplier used by Balance mode.")]
        public ClampedFloatParameter resolutionDepthRatio = new(0.5f, 0.0f, 1.0f);

        [Tooltip("Manual VBuffer screen resolution percentage.")]
        public ClampedFloatParameter screenResolutionPercentage =
            new(DefaultScreenResolutionPercentage, MinScreenResolutionPercentage, MaxScreenResolutionPercentage);

        [Tooltip("Manual number of VBuffer depth slices.")]
        public ClampedIntParameter volumeSliceCount =
            new(DefaultVolumeSliceCount, MinVolumeSliceCount, MaxVolumeSliceCount);

        [Tooltip("Temporal and spatial denoising mode for the VBuffer lighting result.")]
        public EnumParameter<VividVolumetricFogDenoisingMode> denoisingMode =
            new(VividVolumetricFogDenoisingMode.Both);

        [Tooltip("Limits volumetric lighting to directional lights.")]
        public BoolParameter directionalLightsOnly = new(false);

        [Tooltip("Extinction values at or below this threshold skip in-scattered lighting evaluation. Density still contributes transmittance.")]
        public ClampedFloatParameter volumetricLightingDensityCutoff = new(0.0f, 0.0f, 0.01f);

        [Tooltip("Controls the amount of multiple scattering approximation applied by fog authoring. Reserved for HDRP parity.")]
        public ClampedFloatParameter multipleScatteringIntensity = new(0.0f, 0.0f, 1.0f);

        public bool IsActive()
        {
            return active
                && enabled.value
                && volumetricFog.value
                && meanFreePath.value > 0.0f
                && depthExtent.value > 0.0f;
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
