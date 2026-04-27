using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public readonly struct VividVolumetricFogSettings
    {
        public VividVolumetricFogSettings(
            bool enabled,
            Vector3 scattering,
            float extinction,
            float baseHeight,
            float maximumHeight,
            float anisotropy,
            float globalLightProbeDimmer,
            float depthExtent,
            float sliceDistributionUniformity,
            VividVolumetricFogDenoisingMode denoisingMode,
            bool directionalLightsOnly,
            float densityCutoff,
            VBufferParameters vBufferParameters)
        {
            Enabled = enabled;
            Scattering = scattering;
            Extinction = extinction;
            BaseHeight = baseHeight;
            MaximumHeight = maximumHeight;
            Anisotropy = anisotropy;
            GlobalLightProbeDimmer = globalLightProbeDimmer;
            DepthExtent = depthExtent;
            SliceDistributionUniformity = sliceDistributionUniformity;
            DenoisingMode = denoisingMode;
            DirectionalLightsOnly = directionalLightsOnly;
            DensityCutoff = densityCutoff;
            VBufferParameters = vBufferParameters;
        }

        public bool Enabled { get; }
        public Vector3 Scattering { get; }
        public float Extinction { get; }
        public float BaseHeight { get; }
        public float MaximumHeight { get; }
        public float Anisotropy { get; }
        public float GlobalLightProbeDimmer { get; }
        public float DepthExtent { get; }
        public float SliceDistributionUniformity { get; }
        public VividVolumetricFogDenoisingMode DenoisingMode { get; }
        public bool DirectionalLightsOnly { get; }
        public float DensityCutoff { get; }
        public VBufferParameters VBufferParameters { get; }

        public bool GaussianFilteringEnabled => Enabled && DenoisingMode == VividVolumetricFogDenoisingMode.Gaussian;

        public static VividVolumetricFogSettings Disabled(int cameraWidth, int cameraHeight)
        {
            var vBuffer = VividVolumetricUtility.ComputeVBufferParameters(
                Mathf.Max(cameraWidth, 1),
                Mathf.Max(cameraHeight, 1),
                VividVolumetricFogVolume.DefaultScreenResolutionPercentage,
                VividVolumetricFogVolume.DefaultVolumeSliceCount,
                VividVolumetricFogVolume.DefaultDepthExtent,
                0.0f);

            return new VividVolumetricFogSettings(
                false,
                Vector3.zero,
                0.0f,
                0.0f,
                1.0f,
                0.0f,
                0.0f,
                VividVolumetricFogVolume.DefaultDepthExtent,
                0.0f,
                VividVolumetricFogDenoisingMode.None,
                false,
                0.0f,
                vBuffer);
        }
    }

    public readonly struct VBufferParameters
    {
        public VBufferParameters(
            int viewportWidth,
            int viewportHeight,
            int sliceCount,
            float screenPercentage,
            float depthExtent,
            float sliceDistributionUniformity)
        {
            ViewportWidth = Mathf.Max(1, viewportWidth);
            ViewportHeight = Mathf.Max(1, viewportHeight);
            SliceCount = Mathf.Clamp(sliceCount, VividVolumetricFogVolume.MinVolumeSliceCount, VividVolumetricFogVolume.MaxVolumeSliceCount);
            ScreenPercentage = Mathf.Clamp(
                screenPercentage,
                VividVolumetricFogVolume.MinScreenResolutionPercentage,
                VividVolumetricFogVolume.MaxScreenResolutionPercentage);
            DepthExtent = Mathf.Max(depthExtent, 0.01f);
            SliceDistributionUniformity = Mathf.Clamp01(sliceDistributionUniformity);
        }

        public int ViewportWidth { get; }
        public int ViewportHeight { get; }
        public int SliceCount { get; }
        public float ScreenPercentage { get; }
        public float DepthExtent { get; }
        public float SliceDistributionUniformity { get; }
        public float RcpViewportWidth => 1.0f / Mathf.Max(ViewportWidth, 1);
        public float RcpViewportHeight => 1.0f / Mathf.Max(ViewportHeight, 1);
        public float RcpSliceCount => 1.0f / Mathf.Max(SliceCount, 1);
        public float DepthDistributionPower => Mathf.Lerp(2.0f, 1.0f, SliceDistributionUniformity);
    }

    internal static class VividVolumetricUtility
    {
        internal static VividVolumetricFogSettings ResolveSettings(ContextContainer frameData)
        {
            var cameraData = frameData?.GetOrCreate<VividCameraData>();
            var cameraWidth = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualWidth ?? 0,
                cameraData?.pixelWidth ?? 0,
                Screen.width);
            var cameraHeight = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualHeight ?? 0,
                cameraData?.pixelHeight ?? 0,
                Screen.height);
            var volume = VividVolumeManagerUtility.GetVolumetricFogVolume();
            if (volume == null || !volume.IsActive())
                return VividVolumetricFogSettings.Disabled(cameraWidth, cameraHeight);

            ResolveQuality(volume, out var screenPercentage, out var sliceCount);
            var vBuffer = ComputeVBufferParameters(
                cameraWidth,
                cameraHeight,
                screenPercentage,
                sliceCount,
                volume.depthExtent.value,
                volume.sliceDistributionUniformity.value);

            return new VividVolumetricFogSettings(
                true,
                volume.GetScattering(),
                volume.GetExtinction(),
                volume.baseHeight.value,
                Mathf.Max(volume.maximumHeight.value, volume.baseHeight.value + 0.01f),
                volume.anisotropy.value,
                volume.globalLightProbeDimmer.value,
                volume.depthExtent.value,
                volume.sliceDistributionUniformity.value,
                volume.denoisingMode.value,
                volume.directionalLightsOnly.value,
                volume.volumetricLightingDensityCutoff.value,
                vBuffer);
        }

        internal static void ResolveQuality(
            VividVolumetricFogVolume volume,
            out float screenPercentage,
            out int sliceCount)
        {
            if (volume == null)
            {
                screenPercentage = VividVolumetricFogVolume.DefaultScreenResolutionPercentage;
                sliceCount = VividVolumetricFogVolume.DefaultVolumeSliceCount;
                return;
            }

            if (volume.fogControlMode.value == VividVolumetricFogControlMode.Manual)
            {
                screenPercentage = volume.screenResolutionPercentage.value;
                sliceCount = volume.volumeSliceCount.value;
                return;
            }

            var budget = Mathf.Max(volume.volumetricFogBudget.value, 0.001f);
            var depthRatio = Mathf.Max(volume.resolutionDepthRatio.value, 0.001f);
            var scale = Mathf.Sqrt(budget / depthRatio);
            screenPercentage = Mathf.Clamp(
                VividVolumetricFogVolume.DefaultScreenResolutionPercentage * scale,
                VividVolumetricFogVolume.MinScreenResolutionPercentage,
                VividVolumetricFogVolume.MaxScreenResolutionPercentage);
            sliceCount = Mathf.Clamp(
                Mathf.RoundToInt(VividVolumetricFogVolume.DefaultVolumeSliceCount * depthRatio * scale),
                VividVolumetricFogVolume.MinVolumeSliceCount,
                VividVolumetricFogVolume.MaxVolumeSliceCount);
        }

        internal static VBufferParameters ComputeVBufferParameters(
            int cameraWidth,
            int cameraHeight,
            float screenPercentage,
            int sliceCount,
            float depthExtent,
            float sliceDistributionUniformity)
        {
            screenPercentage = Mathf.Clamp(
                screenPercentage,
                VividVolumetricFogVolume.MinScreenResolutionPercentage,
                VividVolumetricFogVolume.MaxScreenResolutionPercentage);
            var scale = screenPercentage / 100.0f;
            var viewportWidth = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(cameraWidth, 1) * scale));
            var viewportHeight = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(cameraHeight, 1) * scale));

            return new VBufferParameters(
                viewportWidth,
                viewportHeight,
                sliceCount,
                screenPercentage,
                depthExtent,
                sliceDistributionUniformity);
        }

        internal static ShaderVariablesVolumetric BuildShaderVariables(
            in VividVolumetricFogSettings settings,
            int cameraWidth,
            int cameraHeight,
            int localFogCount)
        {
            var vBuffer = settings.VBufferParameters;
            var heightRange = Mathf.Max(settings.MaximumHeight - settings.BaseHeight, 0.01f);

            return new ShaderVariablesVolumetric
            {
                _VBufferViewportSize = new Vector4(
                    vBuffer.ViewportWidth,
                    vBuffer.ViewportHeight,
                    vBuffer.SliceCount,
                    vBuffer.RcpSliceCount),
                _VBufferViewportScale = new Vector4(
                    Mathf.Max(cameraWidth, 1) / (float)Mathf.Max(vBuffer.ViewportWidth, 1),
                    Mathf.Max(cameraHeight, 1) / (float)Mathf.Max(vBuffer.ViewportHeight, 1),
                    vBuffer.RcpViewportWidth,
                    vBuffer.RcpViewportHeight),
                _VBufferDepthEncodingParams = new Vector4(
                    Mathf.Max(settings.DepthExtent, 0.01f),
                    1.0f / Mathf.Max(settings.DepthExtent, 0.01f),
                    vBuffer.DepthDistributionPower,
                    1.0f / Mathf.Max(vBuffer.DepthDistributionPower, 0.0001f)),
                _VBufferFogScattering = new Vector4(
                    settings.Scattering.x,
                    settings.Scattering.y,
                    settings.Scattering.z,
                    settings.Extinction),
                _VBufferFogHeightParams = new Vector4(
                    settings.BaseHeight,
                    settings.MaximumHeight,
                    1.0f / heightRange,
                    settings.Anisotropy),
                _VBufferFogControlParams = new Vector4(
                    settings.Enabled ? 1.0f : 0.0f,
                    settings.DirectionalLightsOnly ? 1.0f : 0.0f,
                    settings.DensityCutoff,
                    settings.GlobalLightProbeDimmer),
                _VBufferLocalFogParams = new Vector4(
                    Mathf.Max(localFogCount, 0),
                    settings.GaussianFilteringEnabled ? 1.0f : 0.0f,
                    0.0f,
                    0.0f)
            };
        }
    }
}
