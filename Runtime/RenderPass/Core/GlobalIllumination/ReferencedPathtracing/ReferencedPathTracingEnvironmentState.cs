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
            ReferencedPathTracingEnvironmentDebugMode debugMode,
            Color tint,
            float intensityMultiplier,
            float rotation,
            int maxMipLevel,
            int skyHash,
            int textureIdentityHash)
        {
            this.hasHdri = hasHdri;
            this.lightingEnabled = lightingEnabled;
            this.cameraVisible = cameraVisible;
            this.samplingMode = samplingMode;
            this.debugMode = debugMode;
            importanceSamplingEnabled =
                lightingEnabled
                && samplingMode
                    == ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling;
            this.tint = tint;
            this.intensityMultiplier = intensityMultiplier;
            this.rotation = rotation;
            this.maxMipLevel = maxMipLevel;
            this.skyHash = skyHash;
            this.textureIdentityHash = textureIdentityHash;
            signature = ComputeSignature(
                hasHdri,
                lightingEnabled,
                cameraVisible,
                samplingMode,
                debugMode,
                tint,
                intensityMultiplier,
                rotation,
                maxMipLevel,
                skyHash,
                textureIdentityHash);
        }

        internal bool hasHdri { get; }
        internal bool lightingEnabled { get; }
        internal bool cameraVisible { get; }
        internal bool importanceSamplingEnabled { get; }
        internal ReferencedPathTracingEnvironmentSamplingMode samplingMode { get; }
        internal ReferencedPathTracingEnvironmentDebugMode debugMode { get; }
        internal Color tint { get; }
        internal float intensityMultiplier { get; }
        internal float rotation { get; }
        internal int maxMipLevel { get; }
        internal int skyHash { get; }
        internal int textureIdentityHash { get; }
        internal ulong signature { get; }

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
                    debugMode,
                    Color.white,
                    0.0f,
                    0.0f,
                    0,
                    0,
                    0);
            }

            var tint = SanitizeTint(skyData.tint);
            var intensityMultiplier = SanitizeNonNegative(skyData.exposure);
            var rotation = IsFinite(skyData.rotation) ? skyData.rotation : 0.0f;
            var maxMipLevel = SkyManager.GetSpecularCubemapMaxMip(skyData);
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
                debugMode,
                tint,
                intensityMultiplier,
                rotation,
                maxMipLevel,
                skyData.skyHash,
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
            ReferencedPathTracingEnvironmentDebugMode debugMode,
            Color tint,
            float intensityMultiplier,
            float rotation,
            int maxMipLevel,
            int skyHash,
            int textureIdentityHash)
        {
            var hash = FnvOffsetBasis;
            Hash(ref hash, hasHdri);
            Hash(ref hash, lightingEnabled);
            Hash(ref hash, cameraVisible);
            Hash(ref hash, (uint)samplingMode);
            Hash(ref hash, (uint)debugMode);
            Hash(ref hash, tint.r);
            Hash(ref hash, tint.g);
            Hash(ref hash, tint.b);
            Hash(ref hash, intensityMultiplier);
            Hash(ref hash, rotation);
            Hash(ref hash, unchecked((uint)maxMipLevel));
            Hash(ref hash, unchecked((uint)skyHash));
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
