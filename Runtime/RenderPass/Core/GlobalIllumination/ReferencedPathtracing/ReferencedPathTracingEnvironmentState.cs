using System;
using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core
{
    internal readonly struct ReferencedPathTracingEnvironmentState
        : IEquatable<ReferencedPathTracingEnvironmentState>
    {
        private const ulong FnvOffsetBasis = 14695981039346656037ul;
        private const ulong FnvPrime = 1099511628211ul;

        internal ReferencedPathTracingEnvironmentState(
            bool hasHdri,
            bool lightingEnabled,
            bool cameraVisible,
            ReferencedPathTracingEnvironmentSamplingMode samplingMode,
            ReferencedPathTracingEnvironmentEstimatorMode estimatorMode,
            ReferencedPathTracingEnvironmentDebugMode debugMode,
            Color tint,
            float intensityMultiplier,
            float rotation,
            int maxMipLevel,
            int skyHash,
            int contentHash,
            int backgroundResolution,
            int lightingResolution,
            int textureIdentityHash)
        {
            this.hasHdri = hasHdri;
            this.lightingEnabled = lightingEnabled;
            this.cameraVisible = cameraVisible;
            this.samplingMode = samplingMode;
            this.estimatorMode = estimatorMode;
            this.debugMode = debugMode;
            importanceSamplingEnabled =
                lightingEnabled
                && samplingMode
                    == ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling;
            neeEnabled =
                lightingEnabled
                && samplingMode
                    != ReferencedPathTracingEnvironmentSamplingMode.BsdfOnly
                && estimatorMode
                    != ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly;
            this.tint = tint;
            this.intensityMultiplier = intensityMultiplier;
            this.rotation = rotation;
            this.maxMipLevel = maxMipLevel;
            this.skyHash = skyHash;
            this.contentHash = contentHash;
            this.backgroundResolution = backgroundResolution;
            this.lightingResolution = lightingResolution;
            this.textureIdentityHash = textureIdentityHash;
            signature = ComputeSignature(
                hasHdri,
                lightingEnabled,
                cameraVisible,
                samplingMode,
                estimatorMode,
                debugMode,
                tint,
                intensityMultiplier,
                rotation,
                maxMipLevel,
                skyHash,
                contentHash,
                backgroundResolution,
                lightingResolution,
                textureIdentityHash);
            // The distribution cache only tracks inputs that alter radiance or its directional
            // density. Camera visibility, background resolution, debug/estimator modes, and
            // display exposure must not cause a CDF rebuild.
            samplingSignature = ComputeSamplingSignature(
                hasHdri,
                lightingEnabled,
                samplingMode,
                tint,
                intensityMultiplier,
                rotation,
                maxMipLevel,
                contentHash,
                lightingResolution,
                textureIdentityHash);
        }

        internal bool hasHdri { get; }
        internal bool lightingEnabled { get; }
        internal bool cameraVisible { get; }
        internal bool importanceSamplingEnabled { get; }
        internal bool neeEnabled { get; }
        internal ReferencedPathTracingEnvironmentSamplingMode samplingMode { get; }
        internal ReferencedPathTracingEnvironmentEstimatorMode estimatorMode { get; }
        internal ReferencedPathTracingEnvironmentDebugMode debugMode { get; }
        internal Color tint { get; }
        internal float intensityMultiplier { get; }
        internal float rotation { get; }
        internal int maxMipLevel { get; }
        internal int skyHash { get; }
        internal int contentHash { get; }
        internal int backgroundResolution { get; }
        internal int lightingResolution { get; }
        internal int textureIdentityHash { get; }
        internal ulong signature { get; }
        internal ulong samplingSignature { get; }

        internal static ReferencedPathTracingEnvironmentState Resolve(
            VividSkyData skyData,
            ReferencedPathTracingSettingsVolume settings = null)
        {
            settings ??= VividVolumeManagerUtility.GetReferencedPathTracingSettingsVolume();

            var useVolumeSettings = settings != null && settings.active;
            var lightingRequested = useVolumeSettings
                ? settings.environmentLighting.value
                : true;
            var cameraVisibilityRequested = useVolumeSettings
                ? settings.environmentCameraVisible.value
                : true;
            var samplingMode = useVolumeSettings
                ? SanitizeSamplingMode(settings.environmentSamplingMode.value)
                : ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling;
            var estimatorMode = useVolumeSettings
                ? SanitizeEstimatorMode(settings.environmentEstimatorMode.value)
                : ReferencedPathTracingEnvironmentEstimatorMode.Mis;
            var debugMode = useVolumeSettings
                ? SanitizeDebugMode(settings.environmentDebugMode.value)
                : ReferencedPathTracingEnvironmentDebugMode.Combined;

            var hasHdri = skyData != null
                && skyData.activeSkyType == SkyType.HDRI
                && SkyManager.HasValidSkyTexture(skyData.specularCubemap);
            if (!hasHdri)
            {
                return new ReferencedPathTracingEnvironmentState(
                    false,
                    false,
                    false,
                    samplingMode,
                    estimatorMode,
                    debugMode,
                    Color.white,
                    0.0f,
                    0.0f,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0);
            }

            var tint = SanitizeTint(skyData.tint);
            var intensityMultiplier = SanitizeNonNegative(skyData.exposure);
            var rotation = IsFinite(skyData.rotation) ? skyData.rotation : 0.0f;
            var maxMipLevel = SkyManager.GetSpecularCubemapMaxMip(skyData);
            var contentHash = skyData.skyContentHash != 0
                ? skyData.skyContentHash
                : SkyManager.GetSkyTextureContentHash(skyData.specularCubemap);
            var backgroundResolution = Mathf.Max(1, skyData.specularCubemap.width);
            var lightingResolution =
                SkyManager.GetSpecularCubemapResolution(skyData);
            var textureIdentityHash = EntityId
                .ToULong(skyData.specularCubemap.GetEntityId())
                .GetHashCode();
            var hasRadiance =
                intensityMultiplier > 0.0f
                && (tint.r > 0.0f || tint.g > 0.0f || tint.b > 0.0f);

            return new ReferencedPathTracingEnvironmentState(
                true,
                lightingRequested && hasRadiance,
                cameraVisibilityRequested,
                samplingMode,
                estimatorMode,
                debugMode,
                tint,
                intensityMultiplier,
                rotation,
                maxMipLevel,
                skyData.skyHash,
                contentHash,
                backgroundResolution,
                lightingResolution,
                textureIdentityHash);
        }

        public bool Equals(ReferencedPathTracingEnvironmentState other)
        {
            return signature == other.signature;
        }

        public override bool Equals(object obj)
        {
            return obj is ReferencedPathTracingEnvironmentState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return signature.GetHashCode();
        }

        private static ReferencedPathTracingEnvironmentSamplingMode SanitizeSamplingMode(
            ReferencedPathTracingEnvironmentSamplingMode mode)
        {
            switch (mode)
            {
                case ReferencedPathTracingEnvironmentSamplingMode.BsdfOnly:
                case ReferencedPathTracingEnvironmentSamplingMode.UniformSphere:
                    return mode;
                default:
                    return ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling;
            }
        }

        private static ReferencedPathTracingEnvironmentDebugMode SanitizeDebugMode(
            ReferencedPathTracingEnvironmentDebugMode mode)
        {
            switch (mode)
            {
                case ReferencedPathTracingEnvironmentDebugMode.EnvironmentOnly:
                case ReferencedPathTracingEnvironmentDebugMode.PrimaryBackgroundOnly:
                case ReferencedPathTracingEnvironmentDebugMode.IndirectMissOnly:
                    return mode;
                default:
                    return ReferencedPathTracingEnvironmentDebugMode.Combined;
            }
        }

        private static ReferencedPathTracingEnvironmentEstimatorMode SanitizeEstimatorMode(
            ReferencedPathTracingEnvironmentEstimatorMode mode)
        {
            switch (mode)
            {
                case ReferencedPathTracingEnvironmentEstimatorMode.LightOnly:
                case ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly:
                    return mode;
                default:
                    return ReferencedPathTracingEnvironmentEstimatorMode.Mis;
            }
        }

        private static Color SanitizeTint(Color value)
        {
            return new Color(
                SanitizeNonNegative(value.r),
                SanitizeNonNegative(value.g),
                SanitizeNonNegative(value.b),
                1.0f);
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) ? Mathf.Max(value, 0.0f) : 0.0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static ulong ComputeSignature(
            bool hasHdri,
            bool lightingEnabled,
            bool cameraVisible,
            ReferencedPathTracingEnvironmentSamplingMode samplingMode,
            ReferencedPathTracingEnvironmentEstimatorMode estimatorMode,
            ReferencedPathTracingEnvironmentDebugMode debugMode,
            Color tint,
            float intensityMultiplier,
            float rotation,
            int maxMipLevel,
            int skyHash,
            int contentHash,
            int backgroundResolution,
            int lightingResolution,
            int textureIdentityHash)
        {
            var hash = FnvOffsetBasis;
            Hash(ref hash, hasHdri);
            Hash(ref hash, lightingEnabled);
            Hash(ref hash, cameraVisible);
            Hash(ref hash, (uint)samplingMode);
            Hash(ref hash, (uint)estimatorMode);
            Hash(ref hash, (uint)debugMode);
            Hash(ref hash, tint.r);
            Hash(ref hash, tint.g);
            Hash(ref hash, tint.b);
            Hash(ref hash, intensityMultiplier);
            Hash(ref hash, rotation);
            Hash(ref hash, unchecked((uint)maxMipLevel));
            Hash(ref hash, unchecked((uint)skyHash));
            Hash(ref hash, unchecked((uint)contentHash));
            Hash(ref hash, unchecked((uint)backgroundResolution));
            Hash(ref hash, unchecked((uint)lightingResolution));
            Hash(ref hash, unchecked((uint)textureIdentityHash));
            return hash;
        }

        private static ulong ComputeSamplingSignature(
            bool hasHdri,
            bool lightingEnabled,
            ReferencedPathTracingEnvironmentSamplingMode samplingMode,
            Color tint,
            float intensityMultiplier,
            float rotation,
            int maxMipLevel,
            int contentHash,
            int lightingResolution,
            int textureIdentityHash)
        {
            var hash = FnvOffsetBasis;
            Hash(ref hash, hasHdri);
            Hash(ref hash, lightingEnabled);
            Hash(ref hash, (uint)samplingMode);
            Hash(ref hash, tint.r);
            Hash(ref hash, tint.g);
            Hash(ref hash, tint.b);
            Hash(ref hash, intensityMultiplier);
            Hash(ref hash, rotation);
            Hash(ref hash, unchecked((uint)maxMipLevel));
            Hash(ref hash, unchecked((uint)contentHash));
            Hash(ref hash, unchecked((uint)lightingResolution));
            Hash(ref hash, unchecked((uint)textureIdentityHash));
            return hash;
        }

        private static void Hash(ref ulong hash, bool value)
        {
            Hash(ref hash, value ? 1u : 0u);
        }

        private static void Hash(ref ulong hash, float value)
        {
            Hash(ref hash, unchecked((uint)value.GetHashCode()));
        }

        private static void Hash(ref ulong hash, uint value)
        {
            hash ^= value;
            hash *= FnvPrime;
        }
    }

    /// <summary>
    /// Runtime-compatible metadata contract for raw reference captures. Editor capture tooling may
    /// enrich assetName with an asset GUID, while these fields remain available in player builds.
    /// Display exposure is deliberately absent because raw path radiance is scene-linear.
    /// </summary>
    [Serializable]
    internal sealed class ReferencedPathTracingEnvironmentMetadata
    {
        internal const int ContractVersion = 1;

        public int contractVersion;
        public string assetName;
        public int textureIdentityHash;
        public int skyHash;
        public int contentHash;
        public int backgroundResolution;
        public int lightingResolution;
        public float rotation;
        public float physicalIntensityMultiplier;
        public ReferencedPathTracingEnvironmentSamplingMode samplingMode;
        public ReferencedPathTracingEnvironmentEstimatorMode estimatorMode;
        public int pdfVersion;
        public bool rawRadianceIsPreExposed;

        internal static ReferencedPathTracingEnvironmentMetadata Capture(
            VividSkyData skyData,
            ReferencedPathTracingSettingsVolume settings = null)
        {
            var state = ReferencedPathTracingEnvironmentState.Resolve(
                skyData,
                settings);
            return new ReferencedPathTracingEnvironmentMetadata
            {
                contractVersion = ContractVersion,
                assetName = skyData?.specularCubemap != null
                    ? skyData.specularCubemap.name
                    : string.Empty,
                textureIdentityHash = state.textureIdentityHash,
                skyHash = state.skyHash,
                contentHash = state.contentHash,
                backgroundResolution = state.backgroundResolution,
                lightingResolution = state.lightingResolution,
                rotation = state.rotation,
                physicalIntensityMultiplier = state.intensityMultiplier,
                samplingMode = state.samplingMode,
                estimatorMode = state.estimatorMode,
                pdfVersion =
                    ReferencedPathTracingEnvironmentImportanceLayout.Version,
                rawRadianceIsPreExposed = false
            };
        }
    }

    internal readonly struct ReferencedPathTracingCameraBackgroundState
        : IEquatable<ReferencedPathTracingCameraBackgroundState>
    {
        private const ulong FnvOffsetBasis = 14695981039346656037ul;
        private const ulong FnvPrime = 1099511628211ul;

        private ReferencedPathTracingCameraBackgroundState(
            bool skyRequested,
            Color clearColor)
        {
            this.skyRequested = skyRequested;
            this.clearColor = clearColor;

            var hash = FnvOffsetBasis;
            Hash(ref hash, skyRequested);
            Hash(ref hash, clearColor.r);
            Hash(ref hash, clearColor.g);
            Hash(ref hash, clearColor.b);
            Hash(ref hash, clearColor.a);
            signature = hash;
        }

        internal bool skyRequested { get; }
        internal Color clearColor { get; }
        internal ulong signature { get; }

        internal static ReferencedPathTracingCameraBackgroundState Resolve(
            VividCameraData cameraData)
        {
            var camera = cameraData?.camera;
            if (camera == null)
            {
                return new ReferencedPathTracingCameraBackgroundState(
                    false,
                    Color.clear);
            }

            var clearColor = camera.backgroundColor.linear;
            clearColor = new Color(
                SanitizeNonNegative(clearColor.r),
                SanitizeNonNegative(clearColor.g),
                SanitizeNonNegative(clearColor.b),
                IsFinite(clearColor.a) ? Mathf.Clamp01(clearColor.a) : 0.0f);

            return new ReferencedPathTracingCameraBackgroundState(
                camera.clearFlags == CameraClearFlags.Skybox,
                clearColor);
        }

        public bool Equals(ReferencedPathTracingCameraBackgroundState other)
        {
            return signature == other.signature;
        }

        public override bool Equals(object obj)
        {
            return obj is ReferencedPathTracingCameraBackgroundState other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            return signature.GetHashCode();
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) ? Mathf.Max(value, 0.0f) : 0.0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void Hash(ref ulong hash, bool value)
        {
            Hash(ref hash, value ? 1u : 0u);
        }

        private static void Hash(ref ulong hash, float value)
        {
            Hash(ref hash, unchecked((uint)value.GetHashCode()));
        }

        private static void Hash(ref ulong hash, uint value)
        {
            hash ^= value;
            hash *= FnvPrime;
        }
    }
}
