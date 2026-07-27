using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass.Core
{
    internal readonly struct ReferencedPathTracingEnvironmentState
        : IEquatable<ReferencedPathTracingEnvironmentState>
    {
        private const ulong FnvOffsetBasis = 14695981039346656037ul;
        private const ulong FnvPrime = 1099511628211ul;

        internal ReferencedPathTracingEnvironmentState(
            ReferencedPathTracingEnvironmentMode mode,
            bool hasHdri,
            bool lightingEnabled,
            bool cameraVisible,
            ReferencedPathTracingEnvironmentSamplingMode samplingMode,
            ReferencedPathTracingEnvironmentEstimatorMode estimatorMode,
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
            this.mode = mode;
            this.hasHdri = hasHdri;
            this.lightingEnabled = lightingEnabled;
            this.cameraVisible = cameraVisible;
            this.samplingMode = samplingMode;
            this.estimatorMode = estimatorMode;
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
                mode,
                hasHdri,
                lightingEnabled,
                cameraVisible,
                samplingMode,
                estimatorMode,
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
            // density. Camera visibility, background resolution, estimator mode, and
            // display exposure must not cause a CDF rebuild.
            samplingSignature = ComputeSamplingSignature(
                mode,
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

        internal ReferencedPathTracingEnvironmentMode mode { get; }
        internal bool hasHdri { get; }
        internal bool lightingEnabled { get; }
        internal bool cameraVisible { get; }
        internal bool importanceSamplingEnabled { get; }
        internal bool neeEnabled { get; }
        internal ReferencedPathTracingEnvironmentSamplingMode samplingMode { get; }
        internal ReferencedPathTracingEnvironmentEstimatorMode estimatorMode { get; }
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
            var mode = useVolumeSettings
                ? SanitizeEnvironmentMode(settings.environmentMode.value)
                : ReferencedPathTracingEnvironmentMode.Hdri;
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
            var hasHdri = mode == ReferencedPathTracingEnvironmentMode.Hdri
                && skyData != null
                && skyData.activeSkyType == SkyType.HDRI
                && SkyManager.HasValidSkyTexture(skyData.specularCubemap);
            if (!hasHdri)
            {
                return new ReferencedPathTracingEnvironmentState(
                    mode,
                    false,
                    false,
                    false,
                    samplingMode,
                    estimatorMode,
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
                mode,
                true,
                lightingRequested && hasRadiance,
                cameraVisibilityRequested,
                samplingMode,
                estimatorMode,
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

        internal static ReferencedPathTracingEnvironmentMode SanitizeEnvironmentMode(
            ReferencedPathTracingEnvironmentMode mode)
        {
            return mode == ReferencedPathTracingEnvironmentMode.ReferenceAtmosphere
                ? mode
                : ReferencedPathTracingEnvironmentMode.Hdri;
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
            ReferencedPathTracingEnvironmentMode mode,
            bool hasHdri,
            bool lightingEnabled,
            bool cameraVisible,
            ReferencedPathTracingEnvironmentSamplingMode samplingMode,
            ReferencedPathTracingEnvironmentEstimatorMode estimatorMode,
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
            Hash(ref hash, (uint)mode);
            Hash(ref hash, hasHdri);
            Hash(ref hash, lightingEnabled);
            Hash(ref hash, cameraVisible);
            Hash(ref hash, (uint)samplingMode);
            Hash(ref hash, (uint)estimatorMode);
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
            ReferencedPathTracingEnvironmentMode mode,
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
            Hash(ref hash, (uint)mode);
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

    [Flags]
    internal enum ReferencedPathTracingAtmosphereFlags
    {
        None = 0,
        Active = 1 << 0,
        LightingEnabled = 1 << 1,
        AtmosphereCameraVisible = 1 << 2,
        AtmosphereHoldout = 1 << 3,
        CloudsEnabled = 1 << 4,
        CloudsCameraVisible = 1 << 5,
        CloudsHoldout = 1 << 6,
        GroundCameraVisible = 1 << 7,
        GroundHoldout = 1 << 8
    }

    internal struct ReferencedPathTracingAtmosphereParameters
    {
        internal Vector3 planetCenter;
        internal float bottomRadius;
        internal float topRadius;
        internal Vector3 groundAlbedo;
        internal Vector3 rayleighScattering;
        internal Vector3 rayleighExtinction;
        internal float rayleighScaleHeight;
        internal Vector3 mieScattering;
        internal Vector3 mieExtinction;
        internal float mieScaleHeight;
        internal float mieAnisotropy;
        internal Vector3 ozoneExtinction;
        internal float ozoneLayerStart;
        internal float ozoneLayerWidth;
        internal float intensityMultiplier;
    }

    /// <summary>
    /// Resource-independent Phase 2 atmosphere snapshot. A0 captures only physical parameters
    /// and policy flags; no raster sky cubemap, precomputed LUT, or atmosphere radiance is
    /// consumed by the reference integrator until the later atmosphere milestones.
    /// </summary>
    internal readonly struct ReferencedPathTracingAtmosphereState
        : IEquatable<ReferencedPathTracingAtmosphereState>
    {
        internal const int ContractVersion = 1;
        internal const int OpticalDepthContractVersion = 1;

        private ReferencedPathTracingAtmosphereState(
            ReferencedPathTracingAtmosphereFlags flags,
            int skyHash,
            in ReferencedPathTracingAtmosphereParameters parameters,
            bool hasSun,
            ulong sunLightEntityId,
            Vector3 sunDirection,
            Vector3 sunIlluminance,
            float sunAngularDiameter,
            float sunShadowStrength)
        {
            this.flags = flags;
            this.skyHash = skyHash;
            this.parameters = parameters;
            this.hasSun = hasSun;
            this.sunLightEntityId = sunLightEntityId;
            this.sunDirection = sunDirection;
            this.sunIlluminance = sunIlluminance;
            this.sunAngularDiameter = sunAngularDiameter;
            this.sunShadowStrength = sunShadowStrength;
            opticalDepthSignature =
                ComputeOpticalDepthSignature(parameters);
            signature = ComputeSignature(
                flags,
                parameters,
                hasSun,
                sunLightEntityId,
                sunDirection,
                sunIlluminance,
                sunAngularDiameter,
                sunShadowStrength);
        }

        internal ReferencedPathTracingAtmosphereFlags flags { get; }
        internal int skyHash { get; }
        internal ReferencedPathTracingAtmosphereParameters parameters { get; }
        internal bool active =>
            (flags & ReferencedPathTracingAtmosphereFlags.Active) != 0;
        internal bool hasSun { get; }
        internal ulong sunLightEntityId { get; }
        internal Vector3 sunDirection { get; }
        internal Vector3 sunIlluminance { get; }
        internal float sunAngularDiameter { get; }
        internal float sunShadowStrength { get; }
        internal ulong opticalDepthSignature { get; }
        internal ulong signature { get; }

        internal static ReferencedPathTracingAtmosphereState Resolve(
            ContextContainer frameData,
            ReferencedPathTracingSettingsVolume settings = null)
        {
            if (frameData == null)
                return default;

            var skyData = frameData.GetOrCreate<VividSkyData>();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var lightData = frameData.GetOrCreate<VividLightData>();
            lightData?.CompleteLightGridPrepare();
            return Resolve(
                skyData,
                VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume(),
                cameraData,
                lightData,
                settings);
        }

        internal static ReferencedPathTracingAtmosphereState Resolve(
            VividSkyData skyData,
            PhysicallyBasedSkyVolume volume,
            VividCameraData cameraData,
            VividLightData lightData,
            ReferencedPathTracingSettingsVolume settings)
        {
            settings ??=
                VividVolumeManagerUtility.GetReferencedPathTracingSettingsVolume();
            var useVolumeSettings = settings != null && settings.active;
            var environmentMode = useVolumeSettings
                ? ReferencedPathTracingEnvironmentState.SanitizeEnvironmentMode(
                    settings.environmentMode.value)
                : ReferencedPathTracingEnvironmentMode.Hdri;
            if (environmentMode
                != ReferencedPathTracingEnvironmentMode.ReferenceAtmosphere)
            {
                return default;
            }

            var flags = ResolvePolicyFlags(settings, useVolumeSettings);
            ReferencedPathTracingAtmosphereParameters parameters = default;
            var hasPhysicalSky = skyData != null
                && skyData.activeSkyType == SkyType.PhysicallyBased
                && TryBuildPhysicalParameters(
                    volume,
                    cameraData,
                    out parameters);
            if (hasPhysicalSky)
                flags |= ReferencedPathTracingAtmosphereFlags.Active;

            var hasSun = hasPhysicalSky
                && lightData?.hasMainDirectionalLight == true;
            var sunLight = hasSun
                ? lightData.mainDirectionalLight
                : default;
            var sunLightEntityId = hasSun
                ? EntityId.ToULong(lightData.mainDirectionalLightEntityId)
                : EntityId.ToULong(EntityId.None);
            var sunDirection = hasSun
                && sunLight.directionWS.sqrMagnitude > 1e-8f
                    ? sunLight.directionWS.normalized
                    : Vector3.zero;
            var sunIlluminance = hasSun
                ? SanitizeVector(sunLight.color)
                : Vector3.zero;
            var sunAngularDiameter = hasSun
                ? SanitizeNonNegative(sunLight.angularDiameter)
                : 0.0f;
            var sunShadowStrength = hasSun
                ? Mathf.Clamp01(SanitizeNonNegative(sunLight.shadowStrength))
                : 0.0f;

            return new ReferencedPathTracingAtmosphereState(
                flags,
                hasPhysicalSky ? skyData.skyHash : 0,
                parameters,
                hasSun,
                sunLightEntityId,
                sunDirection,
                sunIlluminance,
                sunAngularDiameter,
                sunShadowStrength);
        }

        public bool Equals(ReferencedPathTracingAtmosphereState other)
        {
            return signature == other.signature;
        }

        public override bool Equals(object obj)
        {
            return obj is ReferencedPathTracingAtmosphereState other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            return signature.GetHashCode();
        }

        private static ReferencedPathTracingAtmosphereFlags ResolvePolicyFlags(
            ReferencedPathTracingSettingsVolume settings,
            bool useVolumeSettings)
        {
            var lightingEnabled =
                !useVolumeSettings || settings.environmentLighting.value;
            var environmentCameraVisible =
                !useVolumeSettings || settings.environmentCameraVisible.value;
            var atmosphereCameraVisible =
                !useVolumeSettings
                || settings.referenceAtmosphereCameraVisible.value;
            var cloudsEnabled =
                useVolumeSettings && settings.referenceClouds.value;
            var cloudsCameraVisible =
                !useVolumeSettings || settings.referenceCloudsCameraVisible.value;
            var groundCameraVisible =
                !useVolumeSettings
                || settings.referenceGroundCameraVisible.value;

            var flags = ReferencedPathTracingAtmosphereFlags.None;
            if (lightingEnabled)
                flags |= ReferencedPathTracingAtmosphereFlags.LightingEnabled;
            if (environmentCameraVisible && atmosphereCameraVisible)
                flags |=
                    ReferencedPathTracingAtmosphereFlags.AtmosphereCameraVisible;
            if (useVolumeSettings && settings.referenceAtmosphereHoldout.value)
                flags |= ReferencedPathTracingAtmosphereFlags.AtmosphereHoldout;
            if (cloudsEnabled)
                flags |= ReferencedPathTracingAtmosphereFlags.CloudsEnabled;
            if (environmentCameraVisible
                && cloudsEnabled
                && cloudsCameraVisible)
            {
                flags |=
                    ReferencedPathTracingAtmosphereFlags.CloudsCameraVisible;
            }
            if (useVolumeSettings && settings.referenceCloudsHoldout.value)
                flags |= ReferencedPathTracingAtmosphereFlags.CloudsHoldout;
            if (environmentCameraVisible && groundCameraVisible)
                flags |= ReferencedPathTracingAtmosphereFlags.GroundCameraVisible;
            if (useVolumeSettings && settings.referenceGroundHoldout.value)
                flags |= ReferencedPathTracingAtmosphereFlags.GroundHoldout;
            return flags;
        }

        private static bool TryBuildPhysicalParameters(
            PhysicallyBasedSkyVolume volume,
            VividCameraData cameraData,
            out ReferencedPathTracingAtmosphereParameters parameters)
        {
            parameters = default;
            if (volume == null || !volume.IsActive())
                return false;

            var bottomRadius = Mathf.Max(
                SanitizeNonNegative(volume.planetRadius.value),
                1000.0f);
            var atmosphericDepth = Mathf.Max(
                SanitizeNonNegative(volume.GetMaximumAltitude()),
                1.0f);
            var rayleighScaleHeight = Mathf.Max(
                SanitizeNonNegative(volume.GetAirScaleHeight()),
                1.0f);
            var mieScaleHeight = Mathf.Max(
                SanitizeNonNegative(volume.GetAerosolScaleHeight()),
                1.0f);
            var ozoneLayerStart = Mathf.Max(
                SanitizeNonNegative(
                    volume.GetOzoneLayerMinimumAltitude()),
                0.0f);
            var ozoneLayerWidth = Mathf.Max(
                SanitizeNonNegative(volume.GetOzoneLayerWidth()),
                1.0f);
            var cameraPosition = cameraData?.camera != null
                ? cameraData.camera.transform.position
                : Vector3.zero;
            var planet = SkyPlanet.Resolve(
                bottomRadius,
                VividVolumeManagerUtility.GetSkySettingsVolume(),
                cameraPosition);
            var groundAlbedo = volume.groundTint.value.linear;

            var planetCenterRadius = planet.GetPlanetCenterRadius();
            var mieExtinction = SanitizeNonNegative(
                volume.GetAerosolExtinctionCoefficient());
            parameters.planetCenter = new Vector3(
                planetCenterRadius.x,
                planetCenterRadius.y,
                planetCenterRadius.z);
            parameters.bottomRadius = bottomRadius;
            parameters.topRadius = bottomRadius + atmosphericDepth;
            parameters.groundAlbedo = new Vector3(
                groundAlbedo.r,
                groundAlbedo.g,
                groundAlbedo.b);
            parameters.groundAlbedo =
                SanitizeVector(parameters.groundAlbedo);
            parameters.rayleighExtinction = SanitizeVector(
                volume.GetAirExtinctionCoefficient());
            parameters.rayleighScattering = SanitizeVector(
                volume.GetAirScatteringCoefficient());
            parameters.rayleighScaleHeight = rayleighScaleHeight;
            parameters.mieScattering = SanitizeVector(
                volume.GetAerosolScatteringCoefficient());
            parameters.mieExtinction = Vector3.one * mieExtinction;
            parameters.mieScaleHeight = mieScaleHeight;
            parameters.mieAnisotropy =
                IsFinite(volume.aerosolAnisotropy.value)
                    ? Mathf.Clamp(
                        volume.aerosolAnisotropy.value,
                        -1.0f,
                        1.0f)
                    : 0.0f;
            parameters.ozoneExtinction = SanitizeVector(
                volume.GetOzoneExtinctionCoefficient());
            parameters.ozoneLayerStart =
                bottomRadius + ozoneLayerStart;
            parameters.ozoneLayerWidth = ozoneLayerWidth;
            parameters.intensityMultiplier = SanitizeNonNegative(
                volume.GetIntensityMultiplier());
            return true;
        }

        private static ulong ComputeSignature(
            ReferencedPathTracingAtmosphereFlags flags,
            in ReferencedPathTracingAtmosphereParameters parameters,
            bool hasSun,
            ulong sunLightEntityId,
            Vector3 sunDirection,
            Vector3 sunIlluminance,
            float sunAngularDiameter,
            float sunShadowStrength)
        {
            var hash = ReferencedPathTracingStableHash.OffsetBasis;
            ReferencedPathTracingStableHash.Add(ref hash, ContractVersion);
            ReferencedPathTracingStableHash.Add(ref hash, (int)flags);
            AddVector(ref hash, parameters.planetCenter);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.bottomRadius);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.topRadius);
            AddVector(ref hash, parameters.groundAlbedo);
            AddVector(ref hash, parameters.rayleighScattering);
            AddVector(ref hash, parameters.rayleighExtinction);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.rayleighScaleHeight);
            AddVector(ref hash, parameters.mieScattering);
            AddVector(ref hash, parameters.mieExtinction);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.mieScaleHeight);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.mieAnisotropy);
            AddVector(ref hash, parameters.ozoneExtinction);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.ozoneLayerStart);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.ozoneLayerWidth);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.intensityMultiplier);
            ReferencedPathTracingStableHash.Add(ref hash, hasSun);
            ReferencedPathTracingStableHash.Add(ref hash, sunLightEntityId);
            AddVector(ref hash, sunDirection);
            AddVector(ref hash, sunIlluminance);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                sunAngularDiameter);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                sunShadowStrength);
            return hash;
        }

        private static ulong ComputeOpticalDepthSignature(
            in ReferencedPathTracingAtmosphereParameters parameters)
        {
            var hash = ReferencedPathTracingStableHash.OffsetBasis;
            ReferencedPathTracingStableHash.Add(
                ref hash,
                OpticalDepthContractVersion);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ReferencedPathTracingEnvironmentImportanceLayout
                    .AtmosphereVersion);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ReferencedPathTracingEnvironmentImportanceLayout
                    .AtmosphereRadialResolution);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ReferencedPathTracingEnvironmentImportanceLayout
                    .AtmosphereZenithResolution);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ReferencedPathTracingEnvironmentImportanceLayout
                    .AtmosphereReferenceSampleCount);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.bottomRadius);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.topRadius);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.rayleighScaleHeight);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.mieScaleHeight);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.ozoneLayerStart);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                parameters.ozoneLayerWidth);
            return hash;
        }

        private static void AddVector(ref ulong hash, Vector3 value)
        {
            ReferencedPathTracingStableHash.Add(ref hash, value.x);
            ReferencedPathTracingStableHash.Add(ref hash, value.y);
            ReferencedPathTracingStableHash.Add(ref hash, value.z);
        }

        private static Vector3 SanitizeVector(Vector3 value)
        {
            return new Vector3(
                SanitizeNonNegative(value.x),
                SanitizeNonNegative(value.y),
                SanitizeNonNegative(value.z));
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value)
                ? Mathf.Max(value, 0.0f)
                : 0.0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [Serializable]
    public sealed class ReferencedPathTracingAtmosphereMetadata
    {
        public int contractVersion;
        public bool active;
        public int flags;
        public bool lightingEnabled;
        public bool atmosphereCameraVisible;
        public bool atmosphereHoldout;
        public bool cloudsEnabled;
        public bool cloudsCameraVisible;
        public bool cloudsHoldout;
        public bool groundCameraVisible;
        public bool groundHoldout;
        public int skyHash;
        public Vector3 planetCenter;
        public float bottomRadius;
        public float topRadius;
        public Color groundAlbedo;
        public Vector3 rayleighScattering;
        public Vector3 rayleighExtinction;
        public float rayleighScaleHeight;
        public Vector3 mieScattering;
        public float mieExtinction;
        public float mieScaleHeight;
        public float mieAnisotropy;
        public Vector3 ozoneExtinction;
        public float ozoneLayerStart;
        public float ozoneLayerWidth;
        public float physicalIntensityMultiplier;
        public bool hasSun;
        public string sunLightEntityId;
        public Vector3 sunDirection;
        public Vector3 sunIlluminance;
        public float sunAngularDiameter;
        public float sunShadowStrength;

        internal static ReferencedPathTracingAtmosphereMetadata Capture(
            in ReferencedPathTracingAtmosphereState state)
        {
            var parameters = state.parameters;
            return new ReferencedPathTracingAtmosphereMetadata
            {
                contractVersion =
                    ReferencedPathTracingAtmosphereState.ContractVersion,
                active = state.active,
                flags = (int)state.flags,
                lightingEnabled = HasFlag(
                    state.flags,
                    ReferencedPathTracingAtmosphereFlags.LightingEnabled),
                atmosphereCameraVisible = HasFlag(
                    state.flags,
                    ReferencedPathTracingAtmosphereFlags
                        .AtmosphereCameraVisible),
                atmosphereHoldout = HasFlag(
                    state.flags,
                    ReferencedPathTracingAtmosphereFlags.AtmosphereHoldout),
                cloudsEnabled = HasFlag(
                    state.flags,
                    ReferencedPathTracingAtmosphereFlags.CloudsEnabled),
                cloudsCameraVisible = HasFlag(
                    state.flags,
                    ReferencedPathTracingAtmosphereFlags.CloudsCameraVisible),
                cloudsHoldout = HasFlag(
                    state.flags,
                    ReferencedPathTracingAtmosphereFlags.CloudsHoldout),
                groundCameraVisible = HasFlag(
                    state.flags,
                    ReferencedPathTracingAtmosphereFlags.GroundCameraVisible),
                groundHoldout = HasFlag(
                    state.flags,
                    ReferencedPathTracingAtmosphereFlags.GroundHoldout),
                skyHash = state.skyHash,
                planetCenter = parameters.planetCenter,
                bottomRadius = parameters.bottomRadius,
                topRadius = parameters.topRadius,
                groundAlbedo = new Color(
                    parameters.groundAlbedo.x,
                    parameters.groundAlbedo.y,
                    parameters.groundAlbedo.z,
                    1.0f),
                rayleighScattering = parameters.rayleighScattering,
                rayleighExtinction = parameters.rayleighExtinction,
                rayleighScaleHeight = parameters.rayleighScaleHeight,
                mieScattering = parameters.mieScattering,
                mieExtinction = Mathf.Max(
                    parameters.mieExtinction.x,
                    Mathf.Max(
                        parameters.mieExtinction.y,
                        parameters.mieExtinction.z)),
                mieScaleHeight = parameters.mieScaleHeight,
                mieAnisotropy = parameters.mieAnisotropy,
                ozoneExtinction = parameters.ozoneExtinction,
                ozoneLayerStart = parameters.ozoneLayerStart,
                ozoneLayerWidth = parameters.ozoneLayerWidth,
                physicalIntensityMultiplier =
                    parameters.intensityMultiplier,
                hasSun = state.hasSun,
                sunLightEntityId = state.sunLightEntityId.ToString(),
                sunDirection = state.sunDirection,
                sunIlluminance = state.sunIlluminance,
                sunAngularDiameter = state.sunAngularDiameter,
                sunShadowStrength = state.sunShadowStrength
            };
        }

        private static bool HasFlag(
            ReferencedPathTracingAtmosphereFlags value,
            ReferencedPathTracingAtmosphereFlags flag)
        {
            return (value & flag) != 0;
        }
    }

    /// <summary>
    /// Runtime-compatible metadata contract for raw reference captures. Editor capture tooling may
    /// enrich assetName with an asset GUID, while these fields remain available in player builds.
    /// Display exposure is deliberately absent because raw path radiance is scene-linear.
    /// </summary>
    [Serializable]
    public sealed class ReferencedPathTracingEnvironmentMetadata
    {
        internal const int ContractVersion = 3;

        public int contractVersion;
        public ReferencedPathTracingEnvironmentMode mode;
        public string assetName;
        public int textureIdentityHash;
        public int skyHash;
        public int contentHash;
        public int backgroundResolution;
        public int lightingResolution;
        public bool lightingEnabled;
        public bool cameraVisible;
        public float rotation;
        public float physicalIntensityMultiplier;
        public ReferencedPathTracingEnvironmentSamplingMode samplingMode;
        public ReferencedPathTracingEnvironmentEstimatorMode estimatorMode;
        public ReferencedPathTracingEnvironmentDebugMode debugMode;
        public int pdfVersion;
        public bool rawRadianceIsPreExposed;
        public ReferencedPathTracingAtmosphereMetadata atmosphere;

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
                mode = state.mode,
                assetName = state.mode == ReferencedPathTracingEnvironmentMode.Hdri
                    && skyData?.specularCubemap != null
                    ? skyData.specularCubemap.name
                    : string.Empty,
                textureIdentityHash = state.textureIdentityHash,
                skyHash = state.skyHash,
                contentHash = state.contentHash,
                backgroundResolution = state.backgroundResolution,
                lightingResolution = state.lightingResolution,
                lightingEnabled = state.lightingEnabled,
                cameraVisible = state.cameraVisible,
                rotation = state.rotation,
                physicalIntensityMultiplier = state.intensityMultiplier,
                samplingMode = state.samplingMode,
                estimatorMode = state.estimatorMode,
                debugMode =
                    ReferencedPathTracingEnvironmentDebugMode.Combined,
                pdfVersion =
                    ReferencedPathTracingEnvironmentImportanceLayout.Version,
                rawRadianceIsPreExposed = false,
                atmosphere = state.mode
                        == ReferencedPathTracingEnvironmentMode
                            .ReferenceAtmosphere
                    ? ReferencedPathTracingAtmosphereMetadata.Capture(default)
                    : null
            };
        }

        internal static ReferencedPathTracingEnvironmentMetadata Capture(
            ContextContainer frameData,
            ReferencedPathTracingSettingsVolume settings = null)
        {
            var skyData = frameData?.GetOrCreate<VividSkyData>();
            var metadata = Capture(skyData, settings);
            if (metadata.mode
                != ReferencedPathTracingEnvironmentMode.ReferenceAtmosphere)
            {
                return metadata;
            }

            metadata.atmosphere =
                ReferencedPathTracingAtmosphereMetadata.Capture(
                    ReferencedPathTracingAtmosphereState.Resolve(
                        frameData,
                        settings));
            return metadata;
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
