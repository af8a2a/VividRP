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
            float sliceDistributionUniformity,
            float nearClipPlane,
            float farClipPlane,
            float verticalFoVRadians)
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
            NearClipPlane = Mathf.Max(nearClipPlane, 0.0001f);
            FarClipPlane = Mathf.Max(farClipPlane, NearClipPlane + 0.0001f);
            VerticalFoVRadians = Mathf.Clamp(
                verticalFoVRadians,
                1.0f * Mathf.Deg2Rad,
                179.0f * Mathf.Deg2Rad);

            ComputeDepthRange(
                ViewportWidth,
                ViewportHeight,
                DepthExtent,
                NearClipPlane,
                FarClipPlane,
                VerticalFoVRadians,
                out var nearDistance,
                out var farDistance);

            var distribution = Mathf.Max(2.0f - 2.0f * SliceDistributionUniformity, 0.001f);
            var depthEncodingParams = ComputeLogarithmicDepthEncodingParams(
                nearDistance,
                farDistance,
                distribution);
            var depthDecodingParams = ComputeLogarithmicDepthDecodingParams(
                nearDistance,
                farDistance,
                distribution);
            DepthEncodingParams = depthEncodingParams;
            DepthDecodingParams = depthDecodingParams;
            LastSliceDistance = DecodeLogarithmicDepth(
                1.0f - 0.5f / Mathf.Max(SliceCount, 1),
                depthDecodingParams);
            UnitDepthTexelSpacing = ComputeZPlaneTexelSpacing(1.0f, VerticalFoVRadians, ViewportHeight);
        }

        public int ViewportWidth { get; }
        public int ViewportHeight { get; }
        public int SliceCount { get; }
        public float ScreenPercentage { get; }
        public float DepthExtent { get; }
        public float SliceDistributionUniformity { get; }
        public float NearClipPlane { get; }
        public float FarClipPlane { get; }
        public float VerticalFoVRadians { get; }
        public Vector4 DepthEncodingParams { get; }
        public Vector4 DepthDecodingParams { get; }
        public float LastSliceDistance { get; }
        public float UnitDepthTexelSpacing { get; }
        public float RcpViewportWidth => 1.0f / Mathf.Max(ViewportWidth, 1);
        public float RcpViewportHeight => 1.0f / Mathf.Max(ViewportHeight, 1);
        public float RcpSliceCount => 1.0f / Mathf.Max(SliceCount, 1);

        public float EncodeLogarithmicDepth(float distance)
        {
            return DepthEncodingParams.x
                + DepthEncodingParams.y * Mathf.Log(Mathf.Max(0.0f, distance - DepthEncodingParams.z), 2.0f);
        }

        public float DecodeLogarithmicDepth(float encodedDepth)
        {
            return DecodeLogarithmicDepth(encodedDepth, DepthDecodingParams);
        }

        public float ComputeSliceLength(int sliceIndex)
        {
            var slice = Mathf.Clamp(sliceIndex, 0, Mathf.Max(SliceCount - 1, 0));
            var start = DecodeLogarithmicDepth(slice * RcpSliceCount);
            var end = DecodeLogarithmicDepth((slice + 1.0f) * RcpSliceCount);
            return Mathf.Max(end - start, 0.0001f);
        }

        private static void ComputeDepthRange(
            int viewportWidth,
            int viewportHeight,
            float depthExtent,
            float nearClipPlane,
            float farClipPlane,
            float verticalFoVRadians,
            out float nearDistance,
            out float farDistance)
        {
            var aspectRatio = viewportWidth / (float)Mathf.Max(viewportHeight, 1);
            var farPlaneHeight = 2.0f * Mathf.Tan(0.5f * verticalFoVRadians) * farClipPlane;
            var farPlaneWidth = farPlaneHeight * aspectRatio;
            var farPlaneMaxDimension = Mathf.Max(farPlaneWidth, farPlaneHeight);
            var farPlaneDistance = Mathf.Sqrt(
                farClipPlane * farClipPlane
                + 0.25f * farPlaneMaxDimension * farPlaneMaxDimension);

            nearDistance = nearClipPlane;
            farDistance = Mathf.Min(nearDistance + Mathf.Max(depthExtent, 0.01f), farPlaneDistance);
            farDistance = Mathf.Max(farDistance, nearDistance + 0.0001f);
        }

        private static Vector4 ComputeLogarithmicDepthEncodingParams(
            float nearDistance,
            float farDistance,
            float distribution)
        {
            var encodedRange = Mathf.Log(distribution * (farDistance - nearDistance) + 1.0f, 2.0f);
            var rcpEncodedRange = 1.0f / Mathf.Max(encodedRange, 0.0001f);
            return new Vector4(
                Mathf.Log(distribution, 2.0f) * rcpEncodedRange,
                rcpEncodedRange,
                nearDistance - 1.0f / distribution,
                0.0f);
        }

        private static Vector4 ComputeLogarithmicDepthDecodingParams(
            float nearDistance,
            float farDistance,
            float distribution)
        {
            return new Vector4(
                1.0f / distribution,
                Mathf.Log(distribution * (farDistance - nearDistance) + 1.0f, 2.0f),
                nearDistance - 1.0f / distribution,
                0.0f);
        }

        private static float DecodeLogarithmicDepth(float encodedDepth, Vector4 decodingParams)
        {
            return decodingParams.x * Mathf.Pow(2.0f, encodedDepth * decodingParams.y)
                + decodingParams.z;
        }

        private static float ComputeZPlaneTexelSpacing(
            float planeDepth,
            float verticalFoVRadians,
            int viewportHeight)
        {
            return Mathf.Tan(0.5f * verticalFoVRadians)
                * (2.0f / Mathf.Max(viewportHeight, 1))
                * planeDepth;
        }
    }

    internal static class VividVolumetricUtility
    {
        internal const float HeightFogScaleHeightFromLayerDepth = 0.144765f;

        internal static VividVolumetricFogSettings ResolveSettings(ContextContainer frameData)
        {
            var cameraData = frameData?.GetOrCreate<VividCameraData>();
            var camera = cameraData?.camera;
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
                volume.sliceDistributionUniformity.value,
                ResolveNearClipPlane(camera),
                ResolveFarClipPlane(camera),
                ResolveVerticalFoVRadians(camera));

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

            var budget = Mathf.Clamp01(volume.volumetricFogBudget.value);
            var depthRatio = Mathf.Clamp01(volume.resolutionDepthRatio.value);
            var maxScreenPercentage =
                (1.0f - depthRatio)
                * (VividVolumetricFogVolume.MaxScreenResolutionPercentage - VividVolumetricFogVolume.MinScreenResolutionPercentage)
                + VividVolumetricFogVolume.MinScreenResolutionPercentage;
            screenPercentage = Mathf.Lerp(
                VividVolumetricFogVolume.MinScreenResolutionPercentage,
                maxScreenPercentage,
                budget);

            var maxSliceCount = Mathf.Max(1.0f, depthRatio * VividVolumetricFogVolume.MaxVolumeSliceCount);
            sliceCount = Mathf.Clamp(
                (int)Mathf.Lerp(1.0f, maxSliceCount, budget),
                VividVolumetricFogVolume.MinVolumeSliceCount,
                VividVolumetricFogVolume.MaxVolumeSliceCount);
        }

        internal static VBufferParameters ComputeVBufferParameters(
            int cameraWidth,
            int cameraHeight,
            float screenPercentage,
            int sliceCount,
            float depthExtent,
            float sliceDistributionUniformity,
            float nearClipPlane = 0.3f,
            float farClipPlane = 1000.0f,
            float verticalFoVRadians = 60.0f * Mathf.Deg2Rad)
        {
            screenPercentage = Mathf.Clamp(
                screenPercentage,
                VividVolumetricFogVolume.MinScreenResolutionPercentage,
                VividVolumetricFogVolume.MaxScreenResolutionPercentage);
            var scale = screenPercentage / 100.0f;
            var viewportWidth = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(cameraWidth, 1) * scale));
            var viewportHeight = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(cameraHeight, 1) * scale));

            return new VBufferParameters(
                viewportWidth,
                viewportHeight,
                sliceCount,
                screenPercentage,
                depthExtent,
                sliceDistributionUniformity,
                nearClipPlane,
                farClipPlane,
                verticalFoVRadians);
        }

        internal static ShaderVariablesVolumetric BuildShaderVariables(
            in VividVolumetricFogSettings settings,
            int cameraWidth,
            int cameraHeight,
            int localFogCount,
            VividCameraData cameraData = null)
        {
            var vBuffer = settings.VBufferParameters;
            var heightFogScaleHeight = ComputeHeightFogScaleHeight(settings.BaseHeight, settings.MaximumHeight);
            var camera = cameraData?.camera;

            return new ShaderVariablesVolumetric
            {
                _VBufferCoordToViewDirWS = ComputePixelCoordToWorldSpaceViewDirectionMatrix(
                    ResolveVerticalFoVRadians(camera),
                    ResolveLensShift(camera),
                    new Vector4(
                        vBuffer.ViewportWidth,
                        vBuffer.ViewportHeight,
                        vBuffer.RcpViewportWidth,
                        vBuffer.RcpViewportHeight),
                    cameraData?.GetViewMatrix() ?? ResolveViewMatrix(camera),
                    camera != null && camera.orthographic),
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
                _VBufferLightingViewportScale = new Vector4(
                    ComputeViewportScale(vBuffer.ViewportWidth, vBuffer.ViewportWidth),
                    ComputeViewportScale(vBuffer.ViewportHeight, vBuffer.ViewportHeight),
                    ComputeViewportScale(vBuffer.SliceCount, vBuffer.SliceCount),
                    0.0f),
                _VBufferLightingViewportLimit = new Vector4(
                    ComputeViewportLimit(vBuffer.ViewportWidth, vBuffer.ViewportWidth),
                    ComputeViewportLimit(vBuffer.ViewportHeight, vBuffer.ViewportHeight),
                    ComputeViewportLimit(vBuffer.SliceCount, vBuffer.SliceCount),
                    0.0f),
                _VBufferDepthEncodingParams = vBuffer.DepthEncodingParams,
                _VBufferDepthDecodingParams = vBuffer.DepthDecodingParams,
                _VBufferGeometryParams = new Vector4(
                    vBuffer.UnitDepthTexelSpacing,
                    100.0f / Mathf.Max(vBuffer.ScreenPercentage, 0.0001f),
                    vBuffer.LastSliceDistance,
                    camera != null && camera.orthographic ? 1.0f : 0.0f),
                _VBufferFogScattering = new Vector4(
                    settings.Scattering.x,
                    settings.Scattering.y,
                    settings.Scattering.z,
                    settings.Extinction),
                _VBufferFogHeightParams = new Vector4(
                    settings.BaseHeight,
                    settings.MaximumHeight,
                    1.0f / heightFogScaleHeight,
                    settings.Anisotropy),
                _VBufferFogControlParams = new Vector4(
                    settings.Enabled ? 1.0f : 0.0f,
                    settings.DirectionalLightsOnly ? 1.0f : 0.0f,
                    ComputeEffectiveDensityCutoff(settings.DensityCutoff),
                    settings.GlobalLightProbeDimmer),
                _VBufferLocalFogParams = new Vector4(
                    Mathf.Max(localFogCount, 0),
                    settings.GaussianFilteringEnabled ? 1.0f : 0.0f,
                    ComputeMaxZDilationRadius(vBuffer.ScreenPercentage),
                    0.0f)
            };
        }

        internal static int ComputeMaxZDilationRadius(float screenPercentage)
        {
            var ratio = Mathf.Clamp(screenPercentage, 0.0f, 100.0f) / 100.0f;
            if (ratio < 0.1f)
                return 2;

            return ratio < 0.5f ? 1 : 0;
        }

        internal static float ComputeViewportScale(int viewportSize, int bufferSize)
        {
            return Mathf.Max(viewportSize, 1) / (float)Mathf.Max(bufferSize, 1);
        }

        internal static float ComputeViewportLimit(int viewportSize, int bufferSize)
        {
            return (Mathf.Max(viewportSize, 1) - 0.5f) / Mathf.Max(bufferSize, 1);
        }

        internal static float ComputeEffectiveDensityCutoff(float densityCutoff)
        {
            return Mathf.Max(densityCutoff, 0.0f);
        }

        internal static float ComputeHeightFogScaleHeight(float baseHeight, float maximumHeight)
        {
            var layerDepth = Mathf.Max(0.01f, maximumHeight - baseHeight);
            return Mathf.Max(layerDepth * HeightFogScaleHeightFromLayerDepth, 0.0001f);
        }

        internal static float ComputeHeightFogMultiplier(float height, float baseHeight, float maximumHeight)
        {
            var heightAboveBase = Mathf.Max(height - baseHeight, 0.0f);
            var rcpScaleHeight = 1.0f / ComputeHeightFogScaleHeight(baseHeight, maximumHeight);
            return Mathf.Exp(-heightAboveBase * rcpScaleHeight);
        }

        private static float ResolveNearClipPlane(Camera camera)
        {
            return camera != null ? Mathf.Max(camera.nearClipPlane, 0.0001f) : 0.3f;
        }

        private static float ResolveFarClipPlane(Camera camera)
        {
            var nearClip = ResolveNearClipPlane(camera);
            return camera != null ? Mathf.Max(camera.farClipPlane, nearClip + 0.0001f) : 1000.0f;
        }

        private static float ResolveVerticalFoVRadians(Camera camera)
        {
            if (camera == null)
                return 60.0f * Mathf.Deg2Rad;

            return Mathf.Clamp(camera.fieldOfView, 1.0f, 179.0f) * Mathf.Deg2Rad;
        }

        private static Vector2 ResolveLensShift(Camera camera)
        {
            return camera != null && camera.usePhysicalProperties ? camera.lensShift : Vector2.zero;
        }

        private static Matrix4x4 ResolveViewMatrix(Camera camera)
        {
            return camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;
        }

        private static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(
            float verticalFoVRadians,
            Vector2 lensShift,
            Vector4 screenSize,
            Matrix4x4 worldToViewMatrix,
            bool isOrthographic)
        {
            Matrix4x4 viewSpaceRasterTransform;
            if (isOrthographic)
            {
                viewSpaceRasterTransform = new Matrix4x4(
                    new Vector4(-2.0f * screenSize.z, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, -2.0f * screenSize.w, 0.0f, 0.0f),
                    new Vector4(1.0f, 1.0f, -1.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            }
            else
            {
                var aspectRatio = screenSize.x * screenSize.w;
                var tanHalfVerticalFoV = Mathf.Tan(0.5f * verticalFoVRadians);

                var m21 = (1.0f - 2.0f * lensShift.y) * tanHalfVerticalFoV;
                var m11 = -2.0f * screenSize.w * tanHalfVerticalFoV;
                var m20 = (1.0f - 2.0f * lensShift.x) * tanHalfVerticalFoV * aspectRatio;
                var m00 = -2.0f * screenSize.z * tanHalfVerticalFoV * aspectRatio;

                viewSpaceRasterTransform = new Matrix4x4(
                    new Vector4(m00, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, m11, 0.0f, 0.0f),
                    new Vector4(m20, m21, -1.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            }

            worldToViewMatrix.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            worldToViewMatrix.SetRow(2, -worldToViewMatrix.GetRow(2));
            return Matrix4x4.Transpose(worldToViewMatrix.transpose * viewSpaceRasterTransform);
        }
    }
}
