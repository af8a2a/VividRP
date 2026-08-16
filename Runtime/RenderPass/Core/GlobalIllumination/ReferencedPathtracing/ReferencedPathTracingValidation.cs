using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.ObjectDispatching;
using Object = UnityEngine.Object;

namespace VividRP.Runtime.RenderPass.Core
{
    internal readonly struct ReferencedPathTracingIntegratorState
        : IEquatable<ReferencedPathTracingIntegratorState>
    {
        internal const int Version = 15;

        internal ReferencedPathTracingIntegratorState(
            bool deterministicSampling,
            int fixedSeed,
            ReferencedPathTracingSamplingMode pathSamplingMode,
            int maxBounceCount,
            int russianRouletteStartBounce,
            bool enableReGIR,
            bool shadingPointLightSelection,
            float globalLightProposalProbability,
            bool lightSpatialIndex,
            bool enableShaderExecutionReordering,
            bool enableRTXTF,
            ReferencedPathTracingRTXTFMode rtxtfFilter,
            float rtxtfGaussianSigma,
            ReferencedPathTracingEnvironmentEstimatorMode estimatorMode,
            int targetSampleCount)
        {
            this.deterministicSampling = deterministicSampling;
            this.fixedSeed = Mathf.Max(0, fixedSeed);
            this.pathSamplingMode =
                SanitizePathSamplingMode(pathSamplingMode);
            this.maxBounceCount = Mathf.Clamp(
                maxBounceCount,
                1,
                ReferencedPathTracingSettingsVolume.MaximumSupportedBounceCount);
            this.russianRouletteStartBounce = Mathf.Clamp(
                russianRouletteStartBounce,
                1,
                ReferencedPathTracingSettingsVolume.MaximumSupportedBounceCount);
            this.enableReGIR = enableReGIR;
            this.shadingPointLightSelection = shadingPointLightSelection;
            this.globalLightProposalProbability =
                ReferencedPathTracingLightProposalPolicy
                    .SanitizeGlobalProposalProbability(
                        globalLightProposalProbability,
                        ReferencedPathTracingLightProposalPolicy
                            .DefaultGlobalProposalProbability);
            this.lightSpatialIndex = lightSpatialIndex;
            this.enableShaderExecutionReordering =
                enableShaderExecutionReordering;
            this.enableRTXTF = enableRTXTF;
            this.rtxtfFilter = SanitizeRTXTFMode(rtxtfFilter);
            this.rtxtfGaussianSigma = Mathf.Clamp(
                float.IsFinite(rtxtfGaussianSigma)
                    ? rtxtfGaussianSigma
                    : 0.7f,
                0.05f,
                4.0f);
            this.estimatorMode = SanitizeEstimatorMode(estimatorMode);
            this.targetSampleCount = Mathf.Clamp(
                targetSampleCount,
                1,
                ReferencedPathTracingSettingsVolume.MaximumTargetSampleCount);

            var hash = ReferencedPathTracingStableHash.OffsetBasis;
            ReferencedPathTracingStableHash.Add(ref hash, Version);
            ReferencedPathTracingStableHash.Add(ref hash, deterministicSampling);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                deterministicSampling ? this.fixedSeed : 0);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                (int)this.pathSamplingMode);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ReferencedPathTracingSamplingContract.Version);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ReferencedPathTracingShadingNormalContract.Version);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ReferencedPathTracingThinWalledTransmissionContract.Version);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ReferencedPathTracingSolidTransmissionContract.Version);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ReferencedPathTracingFaceSubsurfaceTransmissionContract.Version);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ReferencedPathTracingGeometryOpacityContract.Version);
            ReferencedPathTracingStableHash.Add(ref hash, this.maxBounceCount);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                this.russianRouletteStartBounce);
            ReferencedPathTracingStableHash.Add(ref hash, enableReGIR);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                shadingPointLightSelection);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                this.globalLightProposalProbability);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                lightSpatialIndex);
            ReferencedPathTracingStableHash.Add(ref hash, enableRTXTF);
            if (enableRTXTF)
            {
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    (int)this.rtxtfFilter);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    this.rtxtfGaussianSigma);
            }
            ReferencedPathTracingStableHash.Add(
                ref hash,
                (int)this.estimatorMode);
            // SER only changes execution scheduling; it must not invalidate a
            // mathematically identical reference accumulation.
            signature = hash;
        }

        internal bool deterministicSampling { get; }
        internal int fixedSeed { get; }
        internal ReferencedPathTracingSamplingMode pathSamplingMode { get; }
        internal int maxBounceCount { get; }
        internal int russianRouletteStartBounce { get; }
        internal bool enableReGIR { get; }
        internal bool shadingPointLightSelection { get; }
        internal float globalLightProposalProbability { get; }
        internal bool lightSpatialIndex { get; }
        internal bool enableShaderExecutionReordering { get; }
        internal bool enableRTXTF { get; }
        internal ReferencedPathTracingRTXTFMode rtxtfFilter { get; }
        internal float rtxtfGaussianSigma { get; }
        internal ReferencedPathTracingEnvironmentEstimatorMode estimatorMode { get; }
        internal int targetSampleCount { get; }
        internal ulong signature { get; }

        internal ulong ResolveEffectiveSignature(
            ReferencedPathTracingSamplingMode effectiveSamplingMode)
        {
            var hash = ReferencedPathTracingStableHash.OffsetBasis;
            ReferencedPathTracingStableHash.Add(ref hash, signature);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                (int)effectiveSamplingMode);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ReferencedPathTracingSamplingContract.Version);
            return hash;
        }

        internal static ReferencedPathTracingIntegratorState Resolve(
            ReferencedPathTracingSettingsVolume settings = null)
        {
            settings ??=
                VividVolumeManagerUtility.GetReferencedPathTracingSettingsVolume();
            var useVolumeSettings = settings != null && settings.active;
            return new ReferencedPathTracingIntegratorState(
                useVolumeSettings && settings.deterministicSampling.value,
                useVolumeSettings ? settings.fixedSeed.value : 0x13579B,
                useVolumeSettings
                    ? settings.pathSamplingMode.value
                    : ReferencedPathTracingSamplingMode.IndexedBnd,
                useVolumeSettings ? settings.maxBounceCount.value : 4,
                useVolumeSettings
                    ? settings.russianRouletteStartBounce.value
                    : 3,
                !useVolumeSettings || settings.enableReGIR.value,
                !useVolumeSettings
                    || settings.shadingPointLightSelection.value,
                useVolumeSettings
                    ? settings.globalLightProposalProbability.value
                    : ReferencedPathTracingLightProposalPolicy
                        .DefaultGlobalProposalProbability,
                !useVolumeSettings || settings.lightSpatialIndex.value,
                useVolumeSettings
                    && settings.enableShaderExecutionReordering.value,
                !useVolumeSettings || settings.enableRTXTF.value,
                useVolumeSettings
                    ? settings.rtxtfFilter.value
                    : ReferencedPathTracingRTXTFMode.Linear,
                useVolumeSettings
                    ? settings.rtxtfGaussianSigma.value
                    : 0.7f,
                useVolumeSettings
                    ? settings.environmentEstimatorMode.value
                    : ReferencedPathTracingEnvironmentEstimatorMode.Mis,
                useVolumeSettings ? settings.targetSampleCount.value : 2048);
        }

        private static ReferencedPathTracingSamplingMode
            SanitizePathSamplingMode(
                ReferencedPathTracingSamplingMode mode)
        {
            return mode == ReferencedPathTracingSamplingMode.IndexedHash
                ? ReferencedPathTracingSamplingMode.IndexedHash
                : ReferencedPathTracingSamplingMode.IndexedBnd;
        }

        private static ReferencedPathTracingEnvironmentEstimatorMode
            SanitizeEstimatorMode(
                ReferencedPathTracingEnvironmentEstimatorMode mode)
        {
            return mode is ReferencedPathTracingEnvironmentEstimatorMode.LightOnly
                or ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly
                    ? mode
                    : ReferencedPathTracingEnvironmentEstimatorMode.Mis;
        }

        private static ReferencedPathTracingRTXTFMode SanitizeRTXTFMode(
            ReferencedPathTracingRTXTFMode mode)
        {
            return mode is ReferencedPathTracingRTXTFMode.Cubic
                or ReferencedPathTracingRTXTFMode.Gaussian
                    ? mode
                    : ReferencedPathTracingRTXTFMode.Linear;
        }

        public bool Equals(ReferencedPathTracingIntegratorState other)
        {
            return signature == other.signature;
        }

        public override bool Equals(object obj)
        {
            return obj is ReferencedPathTracingIntegratorState other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            return signature.GetHashCode();
        }
    }

    internal static class ReferencedPathTracingSamplingContract
    {
        internal const int Version = 11;
        internal const int DimensionCapacity = 256;
        internal const int FilmDimension = 0;
        internal const int LensDimension = 2;
        internal const int CameraReservedDimension = 4;
        internal const int BounceBaseDimension = 8;
        internal const int BounceDimensionStride = 20;
        internal const int BsdfDimensionOffset = 0;
        internal const int NeeDimensionOffset = 3;
        internal const int RussianRouletteDimensionOffset = 6;
        internal const int StochasticAlphaDimensionOffset = 7;
        internal const int VolumeDimensionOffset = 8;
        internal const int AtmosphereSunDimensionOffset = 12;
        internal const int CloudDimensionOffset = 14;
        internal const int HairBsdfExtraDimensionOffset = 17;
        internal const int RTXTFDimensionOffset = 18;
        internal const int FutureDimensionOffset = 18;
        internal const int SubsurfaceBaseDimension = 168;
        internal const int SubsurfaceDimensionStride = 4;
        internal const int GlobalFogBaseDimension = 200;
        internal const int GlobalFogDimensionStride = 3;
        internal const int GlobalFogDistanceDimensionOffset = 0;
        internal const int GlobalFogPhaseDimensionOffset = 1;
        internal const int LocalFogBaseDimension = 224;
        internal const int LocalFogDimensionStride = 4;
        internal const int LocalFogDistanceDimensionOffset = 0;
        internal const int LocalFogPhaseDimensionOffset = 2;
        internal const int MaximumUsedDimension =
            LocalFogBaseDimension
            + ReferencedPathTracingSettingsVolume.MaximumSupportedBounceCount
                * LocalFogDimensionStride
            - 1;

        internal static int GetBounceDimension(
            int bounceIndex,
            int dimensionOffset)
        {
            if (bounceIndex < 0
                || bounceIndex
                    >= ReferencedPathTracingSettingsVolume
                        .MaximumSupportedBounceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(bounceIndex));
            }

            if (dimensionOffset < 0
                || dimensionOffset >= BounceDimensionStride)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dimensionOffset));
            }

            return BounceBaseDimension
                + bounceIndex * BounceDimensionStride
                + dimensionOffset;
        }

        internal static int GetGlobalFogDimension(
            int bounceIndex,
            int dimensionOffset)
        {
            if (bounceIndex < 0
                || bounceIndex
                    >= ReferencedPathTracingSettingsVolume
                        .MaximumSupportedBounceCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bounceIndex));
            }

            if (dimensionOffset < 0
                || dimensionOffset >= GlobalFogDimensionStride)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dimensionOffset));
            }

            return GlobalFogBaseDimension
                + bounceIndex * GlobalFogDimensionStride
                + dimensionOffset;
        }

        internal static int GetSubsurfaceDimension(
            int bounceIndex,
            int dimensionOffset)
        {
            if (bounceIndex < 0
                || bounceIndex
                    >= ReferencedPathTracingSettingsVolume
                        .MaximumSupportedBounceCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bounceIndex));
            }

            if (dimensionOffset < 0
                || dimensionOffset >= SubsurfaceDimensionStride)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dimensionOffset));
            }

            return SubsurfaceBaseDimension
                + bounceIndex * SubsurfaceDimensionStride
                + dimensionOffset;
        }

        internal static int GetLocalFogDimension(
            int bounceIndex,
            int dimensionOffset)
        {
            if (bounceIndex < 0
                || bounceIndex
                    >= ReferencedPathTracingSettingsVolume
                        .MaximumSupportedBounceCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bounceIndex));
            }

            if (dimensionOffset < 0
                || dimensionOffset >= LocalFogDimensionStride)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dimensionOffset));
            }

            return LocalFogBaseDimension
                + bounceIndex * LocalFogDimensionStride
                + dimensionOffset;
        }
    }

    internal static class ReferencedPathTracingShadingNormalContract
    {
        internal const int Version = 1;
        internal const float ViewCosineThreshold = 0.1f;
        internal const float ReflectionHorizonEpsilon = 0.001f;
        private const float DirectionEpsilon = 0.000001f;

        internal static Vector3 ComputeConsistentNormal(
            Vector3 viewDirection,
            Vector3 geometricNormal,
            Vector3 shadingNormal)
        {
            viewDirection = NormalizeOrFallback(
                viewDirection,
                Vector3.forward);
            geometricNormal = NormalizeOrFallback(
                geometricNormal,
                Vector3.up);
            shadingNormal = NormalizeOrFallback(
                shadingNormal,
                geometricNormal);

            if (Vector3.Dot(shadingNormal, geometricNormal) < 0.0f)
                shadingNormal = -shadingNormal;

            var viewCosine = Vector3.Dot(viewDirection, shadingNormal);
            if (viewCosine <= ViewCosineThreshold)
            {
                var blend = Mathf.Clamp01(
                    Mathf.Max(viewCosine, 0.0f)
                    / ViewCosineThreshold);
                shadingNormal = NormalizeOrFallback(
                    Vector3.Lerp(geometricNormal, shadingNormal, blend),
                    geometricNormal);
            }

            var reflectedDirection = Vector3.Reflect(
                -viewDirection,
                shadingNormal);
            var reflectedGeometricCosine =
                Vector3.Dot(reflectedDirection, geometricNormal);
            if (reflectedGeometricCosine
                < ReflectionHorizonEpsilon)
            {
                reflectedDirection = NormalizeOrFallback(
                    reflectedDirection
                    - reflectedGeometricCosine * geometricNormal
                    + ReflectionHorizonEpsilon * geometricNormal,
                    geometricNormal);
                shadingNormal = NormalizeOrFallback(
                    viewDirection + reflectedDirection,
                    geometricNormal);
            }

            return Vector3.Dot(shadingNormal, viewDirection)
                        > DirectionEpsilon
                    && Vector3.Dot(shadingNormal, geometricNormal)
                        > DirectionEpsilon
                ? shadingNormal
                : geometricNormal;
        }

        internal static float EvaluateDiffuseShadowTerminator(
            Vector3 direction,
            Vector3 shadingNormal,
            Vector3 interpolatedNormal)
        {
            direction = NormalizeOrFallback(direction, Vector3.forward);
            shadingNormal = NormalizeOrFallback(
                shadingNormal,
                Vector3.up);
            interpolatedNormal = NormalizeOrFallback(
                interpolatedNormal,
                shadingNormal);
            if (Vector3.Dot(interpolatedNormal, shadingNormal) < 0.0f)
                interpolatedNormal = -interpolatedNormal;

            var normalCosine = Mathf.Clamp01(
                Mathf.Abs(Vector3.Dot(
                    interpolatedNormal,
                    shadingNormal)));
            var tangentSquared =
                (1.0f - normalCosine * normalCosine)
                / (normalCosine * normalCosine + DirectionEpsilon);
            var alphaSquared = Mathf.Clamp01(0.125f * tangentSquared);
            var lightCosine = Mathf.Clamp01(
                Vector3.Dot(interpolatedNormal, direction));
            if (lightCosine <= 0.0f)
                return 0.0f;

            var lightTangentSquared =
                (1.0f - lightCosine * lightCosine)
                / (lightCosine * lightCosine + DirectionEpsilon);
            return Mathf.Clamp01(
                2.0f
                / (1.0f
                    + Mathf.Sqrt(
                        1.0f
                        + alphaSquared * lightTangentSquared)));
        }

        private static Vector3 NormalizeOrFallback(
            Vector3 value,
            Vector3 fallback)
        {
            return value.sqrMagnitude > DirectionEpsilon
                ? value.normalized
                : fallback.normalized;
        }
    }

    internal static class ReferencedPathTracingThinWalledTransmissionContract
    {
        internal const int Version = 1;
        internal const float MinimumIor = 1.0f;
        internal const float MaximumIor = 3.0f;
        private const float DirectionEpsilon = 0.000001f;

        internal static float ResolveEffectiveWeight(
            bool thinWalled,
            float transmissionWeight,
            float metalness)
        {
            if (!thinWalled)
                return 0.0f;

            return Mathf.Clamp01(transmissionWeight)
                * (1.0f - Mathf.Clamp01(metalness));
        }

        internal static float ResolveIor(float ior)
        {
            if (float.IsNaN(ior) || float.IsInfinity(ior))
                return 1.5f;

            return Mathf.Clamp(ior, MinimumIor, MaximumIor);
        }

        internal static bool IsDirectionSupported(
            Vector3 direction,
            Vector3 geometricNormal,
            Vector3 shadingNormal,
            bool allowTransmission)
        {
            direction = NormalizeOrFallback(direction, Vector3.forward);
            geometricNormal = NormalizeOrFallback(
                geometricNormal,
                Vector3.up);
            shadingNormal = NormalizeOrFallback(
                shadingNormal,
                geometricNormal);

            var geometricCosine =
                Vector3.Dot(direction, geometricNormal);
            var shadingCosine =
                Vector3.Dot(direction, shadingNormal);
            var reflection =
                geometricCosine > DirectionEpsilon
                && shadingCosine > DirectionEpsilon;
            var transmission =
                geometricCosine < -DirectionEpsilon
                && shadingCosine < -DirectionEpsilon;
            return reflection || (allowTransmission && transmission);
        }

        internal static float EvaluateSelectionCosine(
            Vector3 normal,
            Vector3 directionToLight,
            bool allowTransmission)
        {
            normal = NormalizeOrFallback(normal, Vector3.up);
            directionToLight = NormalizeOrFallback(
                directionToLight,
                normal);
            var cosine = Vector3.Dot(normal, directionToLight);
            return allowTransmission
                ? Mathf.Abs(cosine)
                : Mathf.Clamp01(cosine);
        }

        private static Vector3 NormalizeOrFallback(
            Vector3 value,
            Vector3 fallback)
        {
            return value.sqrMagnitude > DirectionEpsilon
                ? value.normalized
                : fallback.normalized;
        }
    }

    internal static class ReferencedPathTracingSolidTransmissionContract
    {
        internal const int Version = 2;
        internal const int MaximumMediumDepth = 4;
        private const float MinimumTransmissionColor = 1e-3f;
        private const float MinimumTransmissionDistance = 1e-3f;
        private const float MaximumOpticalDepth = 80.0f;

        internal static float ResolveEffectiveWeight(
            float transmissionWeight,
            float metalness)
        {
            return Mathf.Clamp01(transmissionWeight)
                * (1.0f - Mathf.Clamp01(metalness));
        }

        internal static Vector3 ResolveExtinction(
            Vector3 transmissionColor,
            float transmissionDepth)
        {
            ResolveVolume(
                transmissionColor,
                transmissionDepth,
                Vector3.zero,
                out Vector3 extinction,
                out _);
            return extinction;
        }

        internal static void ResolveVolume(
            Vector3 transmissionColor,
            float transmissionDepth,
            Vector3 transmissionScatter,
            out Vector3 extinction,
            out Vector3 scatteringAlbedo)
        {
            if (float.IsNaN(transmissionDepth)
                || float.IsInfinity(transmissionDepth)
                || transmissionDepth <= 0.0f)
            {
                extinction = Vector3.zero;
                scatteringAlbedo = Vector3.zero;
                return;
            }

            float resolvedTransmissionDepth = Mathf.Max(
                transmissionDepth,
                MinimumTransmissionDistance);
            var baseExtinction = new Vector3(
                ResolveExtinctionChannel(
                    transmissionColor.x,
                    resolvedTransmissionDepth),
                ResolveExtinctionChannel(
                    transmissionColor.y,
                    resolvedTransmissionDepth),
                ResolveExtinctionChannel(
                    transmissionColor.z,
                    resolvedTransmissionDepth));
            var scattering = new Vector3(
                ResolveScatteringChannel(
                    transmissionScatter.x,
                    resolvedTransmissionDepth),
                ResolveScatteringChannel(
                    transmissionScatter.y,
                    resolvedTransmissionDepth),
                ResolveScatteringChannel(
                    transmissionScatter.z,
                    resolvedTransmissionDepth));
            var absorption = baseExtinction - scattering;
            float minimumAbsorption = Mathf.Min(
                absorption.x,
                Mathf.Min(absorption.y, absorption.z));
            if (minimumAbsorption < 0.0f)
            {
                absorption -= Vector3.one * minimumAbsorption;
            }

            extinction = absorption + scattering;
            scatteringAlbedo = new Vector3(
                ResolveScatteringAlbedoChannel(
                    scattering.x,
                    extinction.x),
                ResolveScatteringAlbedoChannel(
                    scattering.y,
                    extinction.y),
                ResolveScatteringAlbedoChannel(
                    scattering.z,
                    extinction.z));
        }

        internal static float ResolveScatteringAnisotropy(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0.0f;

            return Mathf.Clamp(value, -0.95f, 0.95f);
        }

        internal static Vector3 EvaluateTransmittance(
            Vector3 extinction,
            float distance)
        {
            if (float.IsNaN(distance) || distance <= 0.0f)
                return Vector3.one;

            if (float.IsInfinity(distance))
                distance = float.MaxValue;

            return new Vector3(
                EvaluateTransmittanceChannel(extinction.x, distance),
                EvaluateTransmittanceChannel(extinction.y, distance),
                EvaluateTransmittanceChannel(extinction.z, distance));
        }

        internal static float ResolveExteriorIor(
            bool isFrontFace,
            float activeMediumIor,
            float parentMediumIor,
            bool exitsActiveMedium)
        {
            float exteriorIor =
                !isFrontFace && exitsActiveMedium
                    ? parentMediumIor
                    : activeMediumIor;
            return ReferencedPathTracingThinWalledTransmissionContract
                .ResolveIor(exteriorIor);
        }

        private static float ResolveExtinctionChannel(
            float transmissionColor,
            float transmissionDepth)
        {
            float color = Mathf.Clamp(
                transmissionColor,
                MinimumTransmissionColor,
                1.0f);
            return -Mathf.Log(color) / transmissionDepth;
        }

        private static float ResolveScatteringChannel(
            float transmissionScatter,
            float transmissionDepth)
        {
            if (float.IsNaN(transmissionScatter)
                || float.IsInfinity(transmissionScatter))
            {
                return 0.0f;
            }

            return Mathf.Max(transmissionScatter, 0.0f)
                / transmissionDepth;
        }

        private static float ResolveScatteringAlbedoChannel(
            float scattering,
            float extinction)
        {
            return extinction > 0.0f
                ? Mathf.Clamp01(scattering / extinction)
                : 0.0f;
        }

        private static float EvaluateTransmittanceChannel(
            float extinction,
            float distance)
        {
            float opticalDepth = Mathf.Min(
                Mathf.Max(extinction, 0.0f) * distance,
                MaximumOpticalDepth);
            return Mathf.Exp(-opticalDepth);
        }
    }

    internal static class ReferencedPathTracingFaceSubsurfaceTransmissionContract
    {
        internal const int Version = 1;
        private const float MinimumMeanFreePath = 1e-6f;

        internal static float ResolveEffectiveWeight(
            float subsurfaceWeight,
            float transmissionWeight,
            float metalness)
        {
            return Mathf.Clamp01(subsurfaceWeight)
                * Mathf.Clamp01(transmissionWeight)
                * (1.0f - Mathf.Clamp01(metalness));
        }

        internal static Vector3 Evaluate(
            Vector3 albedo,
            Vector3 meanFreePath,
            float thickness)
        {
            float resolvedThickness =
                float.IsNaN(thickness) || thickness <= 0.0f
                    ? 0.0f
                    : thickness;
            if (float.IsInfinity(resolvedThickness))
                resolvedThickness = float.MaxValue;

            return new Vector3(
                EvaluateChannel(
                    albedo.x,
                    meanFreePath.x,
                    resolvedThickness),
                EvaluateChannel(
                    albedo.y,
                    meanFreePath.y,
                    resolvedThickness),
                EvaluateChannel(
                    albedo.z,
                    meanFreePath.z,
                    resolvedThickness));
        }

        private static float EvaluateChannel(
            float albedo,
            float meanFreePath,
            float thickness)
        {
            float resolvedAlbedo = float.IsNaN(albedo)
                || float.IsInfinity(albedo)
                ? 0.0f
                : Mathf.Clamp01(albedo);
            float resolvedMeanFreePath = float.IsNaN(meanFreePath)
                || float.IsInfinity(meanFreePath)
                ? MinimumMeanFreePath
                : Mathf.Max(meanFreePath, MinimumMeanFreePath);
            return resolvedAlbedo
                * Mathf.Exp(-thickness / resolvedMeanFreePath);
        }
    }

    internal static class ReferencedPathTracingGeometryOpacityContract
    {
        internal const int Version = 2;

        internal static float ResolveOpacity(
            float baseAlpha,
            float opacityMapRed)
        {
            return Mathf.Clamp01(baseAlpha)
                * Mathf.Clamp01(opacityMapRed);
        }

        internal static float ResolveBranchProbability(float opacity)
        {
            return Mathf.Clamp01(opacity);
        }

        internal static Vector3 EvaluateExpectedComposite(
            float opacity,
            Vector3 surfaceRadiance,
            Vector3 transmittedRadiance)
        {
            opacity = Mathf.Clamp01(opacity);
            return surfaceRadiance * opacity
                + transmittedRadiance * (1.0f - opacity);
        }
    }

    internal static class ReferencedPathTracingSceneSignatureUtility
    {
        private sealed class RendererComparer : IComparer<Renderer>
        {
            public int Compare(Renderer left, Renderer right)
            {
                if (ReferenceEquals(left, right))
                    return 0;
                if (left == null)
                    return 1;
                if (right == null)
                    return -1;
                return left.GetEntityId().CompareTo(right.GetEntityId());
            }
        }

        private sealed class RendererObjectTracker : ObjectTracker<Renderer>
        {
            internal RendererObjectTracker()
                : base(ObjectDispatcherService.TypeTrackingFlags.SceneObjects)
            {
            }

            public override void ProcessData(
                List<Object> changed,
                NativeArray<EntityId> changedId,
                NativeArray<EntityId> destroyedId)
            {
                ProcessRendererChanges(changed, destroyedId);
            }
        }

        private static readonly List<Material> s_SharedMaterials = new();
        private static readonly List<Renderer> s_Renderers = new();
        private static readonly Dictionary<EntityId, int>
            s_RendererIndices = new();
        private static readonly RendererComparer s_RendererComparer = new();
        private static RendererObjectTracker s_RendererObjectTracker;
        private static bool s_RendererTrackingInitialized;
        private static bool s_RenderersNeedSort;

        internal static ulong Resolve()
        {
            EnsureRendererTracking();
            ObjectDispatcherService.ProcessUpdates();
            PrepareTrackedRenderers();
            var database = VividMeshletRendererDatabase.instance;
            var hash = ReferencedPathTracingStableHash.OffsetBasis;
            ReferencedPathTracingStableHash.Add(
                ref hash,
                Compute(s_Renderers, true));
            ReferencedPathTracingStableHash.Add(
                ref hash,
                Compute(
                    database?.rendererData,
                    database?.rendererResources));
            return hash;
        }

        internal static ulong Compute(
            IReadOnlyList<Renderer> renderers)
        {
            return Compute(renderers, false);
        }

        private static ulong Compute(
            IReadOnlyList<Renderer> renderers,
            bool activeRenderersOnly)
        {
            var hash = ReferencedPathTracingStableHash.OffsetBasis;
            var rendererCount = renderers?.Count ?? 0;
            var supportedRendererCount = 0;
            for (var rendererIndex = 0;
                 rendererIndex < rendererCount;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (IsIncluded(renderer, activeRenderersOnly)
                    && TryResolveMesh(renderer, out _))
                {
                    supportedRendererCount++;
                }
            }

            ReferencedPathTracingStableHash.Add(
                ref hash,
                supportedRendererCount);

            for (var rendererIndex = 0;
                 rendererIndex < rendererCount;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (!IsIncluded(renderer, activeRenderersOnly)
                    || !TryResolveMesh(renderer, out var mesh))
                {
                    continue;
                }

                var gameObject = renderer.gameObject;
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    EntityId.ToULong(renderer.GetEntityId()));
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    renderer.enabled);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    gameObject != null && gameObject.activeInHierarchy);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    renderer.forceRenderingOff);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    gameObject != null ? gameObject.layer : 0);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    (ulong)renderer.renderingLayerMask);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    (int)renderer.shadowCastingMode);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    (int)renderer.rayTracingMode);
                AddMatrix(ref hash, renderer.localToWorldMatrix);

                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    EntityId.ToULong(mesh.GetEntityId()));

                s_SharedMaterials.Clear();
                renderer.GetSharedMaterials(s_SharedMaterials);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    s_SharedMaterials.Count);
                for (var materialIndex = 0;
                     materialIndex < s_SharedMaterials.Count;
                     materialIndex++)
                {
                    AddMaterial(
                        ref hash,
                        s_SharedMaterials[materialIndex]);
                }
            }

            s_SharedMaterials.Clear();
            return hash;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRendererTracking()
        {
            if (s_RendererObjectTracker != null)
            {
                ObjectDispatcherService.UnregisterObjectTracker(
                    s_RendererObjectTracker);
            }

            s_RendererObjectTracker = null;
            s_Renderers.Clear();
            s_RendererIndices.Clear();
            s_RendererTrackingInitialized = false;
            s_RenderersNeedSort = false;
        }

        private static void EnsureRendererTracking()
        {
            if (s_RendererTrackingInitialized)
                return;

            s_RendererTrackingInitialized = true;
            // Seed objects that existed before change tracking was enabled.
            var renderers = Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include);
            for (var index = 0; index < renderers.Length; index++)
                AddOrUpdateRenderer(renderers[index]);

            s_RendererObjectTracker = new RendererObjectTracker();
            ObjectDispatcherService.RegisterObjectTracker(
                s_RendererObjectTracker);
        }

        private static void ProcessRendererChanges(
            List<Object> changed,
            NativeArray<EntityId> destroyedIds)
        {
            for (var index = 0; index < destroyedIds.Length; index++)
                RemoveRenderer(destroyedIds[index]);

            for (var index = 0; index < changed.Count; index++)
            {
                if (changed[index] is Renderer renderer)
                    AddOrUpdateRenderer(renderer);
            }
        }

        private static void AddOrUpdateRenderer(Renderer renderer)
        {
            if (renderer == null)
                return;

            var entityId = renderer.GetEntityId();
            if (entityId.Equals(EntityId.None))
                return;

            if (s_RendererIndices.TryGetValue(
                    entityId,
                    out var existingIndex)
                && existingIndex >= 0
                && existingIndex < s_Renderers.Count)
            {
                s_Renderers[existingIndex] = renderer;
                return;
            }

            s_RendererIndices[entityId] = s_Renderers.Count;
            s_Renderers.Add(renderer);
            s_RenderersNeedSort = true;
        }

        private static void RemoveRenderer(EntityId entityId)
        {
            if (entityId.Equals(EntityId.None)
                || !s_RendererIndices.TryGetValue(
                    entityId,
                    out var rendererIndex))
            {
                return;
            }

            var lastIndex = s_Renderers.Count - 1;
            var lastRenderer = s_Renderers[lastIndex];
            s_Renderers[rendererIndex] = lastRenderer;
            s_Renderers.RemoveAt(lastIndex);
            s_RendererIndices.Remove(entityId);
            if (rendererIndex != lastIndex && lastRenderer != null)
            {
                s_RendererIndices[lastRenderer.GetEntityId()] =
                    rendererIndex;
            }

            s_RenderersNeedSort = true;
        }

        private static void PrepareTrackedRenderers()
        {
            for (var index = s_Renderers.Count - 1; index >= 0; index--)
            {
                if (s_Renderers[index] != null)
                    continue;

                s_Renderers.RemoveAt(index);
                s_RenderersNeedSort = true;
            }

            if (!s_RenderersNeedSort)
                return;

            s_Renderers.Sort(s_RendererComparer);
            s_RendererIndices.Clear();
            for (var index = 0; index < s_Renderers.Count; index++)
            {
                var renderer = s_Renderers[index];
                if (renderer != null)
                {
                    s_RendererIndices[renderer.GetEntityId()] = index;
                }
            }

            s_RenderersNeedSort = false;
        }

        private static bool IsIncluded(
            Renderer renderer,
            bool activeRenderersOnly)
        {
            if (renderer == null)
                return false;

            var gameObject = renderer.gameObject;
            return !activeRenderersOnly
                || (gameObject != null && gameObject.activeInHierarchy);
        }

        private static bool TryResolveMesh(
            Renderer renderer,
            out Mesh mesh)
        {
            mesh = null;
            if (renderer == null)
                return false;

            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
                mesh = skinnedMeshRenderer.sharedMesh;
            else if (renderer is MeshRenderer
                && renderer.TryGetComponent<MeshFilter>(out var meshFilter))
                mesh = meshFilter.sharedMesh;

            return mesh != null;
        }

        internal static ulong Compute(
            IReadOnlyList<VividMeshletRendererRenderData> rendererData,
            IReadOnlyList<VividMeshletRendererResources> rendererResources)
        {
            var hash = ReferencedPathTracingStableHash.OffsetBasis;
            var rendererCount = rendererData?.Count ?? 0;
            ReferencedPathTracingStableHash.Add(ref hash, rendererCount);

            for (var rendererIndex = 0;
                 rendererIndex < rendererCount;
                 rendererIndex++)
            {
                var data = rendererData[rendererIndex];
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    EntityId.ToULong(data.meshletRendererEntityId));
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    EntityId.ToULong(data.sourceMeshEntityId));
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    (ulong)data.renderingLayerMask);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    unchecked((int)data.flags));
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    (int)data.shadowCastingMode);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    data.subMeshCount);
                AddMatrix(ref hash, data.objectToWorldMatrix);

                if (rendererResources == null
                    || rendererIndex >= rendererResources.Count)
                {
                    ReferencedPathTracingStableHash.Add(ref hash, 0);
                    continue;
                }

                var materials = rendererResources[rendererIndex]
                    .SharedMaterials;
                var materialCount = materials?.Length ?? 0;
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    materialCount);
                for (var materialIndex = 0;
                     materialIndex < materialCount;
                     materialIndex++)
                {
                    AddMaterial(ref hash, materials[materialIndex]);
                }
            }

            return hash;
        }

        private static void AddMaterial(
            ref ulong hash,
            Material material)
        {
            ReferencedPathTracingStableHash.Add(
                ref hash,
                material != null
                    ? EntityId.ToULong(material.GetEntityId())
                    : EntityId.ToULong(EntityId.None));
            ReferencedPathTracingStableHash.Add(
                ref hash,
                material != null ? material.ComputeCRC() : 0);
        }

        private static void AddMatrix(
            ref ulong hash,
            Matrix4x4 matrix)
        {
            for (var index = 0; index < 16; index++)
                ReferencedPathTracingStableHash.Add(ref hash, matrix[index]);
        }
    }

    internal static class ReferencedPathTracingFrameSignatureUtility
    {
        internal static ulong Compute(
            ContextContainer frameData,
            VividCameraData cameraData,
            int width,
            int height,
            ulong effectiveIntegratorSignature,
            ReferencedPathTracingEnvironmentState environmentState,
            ReferencedPathTracingAtmosphereState atmosphereState,
            ReferencedPathTracingGlobalFogState globalFogState,
            ReferencedPathTracingLocalFogState localFogState,
            ReferencedPathTracingCameraBackgroundState cameraBackgroundState,
            ReferencedPathTracingPhysicalCameraState physicalCameraState)
        {
            var hash = ReferencedPathTracingStableHash.OffsetBasis;
            ReferencedPathTracingStableHash.Add(ref hash, width);
            ReferencedPathTracingStableHash.Add(ref hash, height);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                effectiveIntegratorSignature);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                environmentState.signature);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                atmosphereState.signature);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                globalFogState.signature);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                localFogState.signature);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                cameraBackgroundState.signature);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                physicalCameraState.signature);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ReferencedPathTracingSceneSignatureUtility.Resolve());

            if (cameraData != null)
            {
                AddMatrix(ref hash, cameraData.GetViewMatrix());
                AddMatrix(
                    ref hash,
                    cameraData.GetProjectionMatrixNoJitter());
            }

            ReferencedPathTracingLightSignatureUtility.Resolve(
                frameData?.GetOrCreate<VividLightData>(),
                out var mainLightDirection,
                out var mainLightColor,
                out var mainLightAngularDiameter,
                out var mainLightShadowStrength,
                out var localLightSignature);
            AddVector(ref hash, mainLightDirection);
            AddVector(ref hash, mainLightColor);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                mainLightAngularDiameter);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                mainLightShadowStrength);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                localLightSignature);
            return hash;
        }

        private static void AddMatrix(ref ulong hash, Matrix4x4 matrix)
        {
            for (var index = 0; index < 16; index++)
                ReferencedPathTracingStableHash.Add(ref hash, matrix[index]);
        }

        private static void AddVector(ref ulong hash, Vector3 vector)
        {
            ReferencedPathTracingStableHash.Add(ref hash, vector.x);
            ReferencedPathTracingStableHash.Add(ref hash, vector.y);
            ReferencedPathTracingStableHash.Add(ref hash, vector.z);
        }
    }

    internal static class ReferencedPathTracingSampleSequence
    {
        private sealed class SequenceState : CameraRelativeState
        {
            internal bool hasSignature;
            internal bool resetRequested;
            internal int lastRenderFrameIndex = -1;
            internal uint sampleIndex;
            internal uint maximumSampleCount;
            internal ulong frameSignature;

            public override void Dispose()
            {
                hasSignature = false;
                resetRequested = false;
                lastRenderFrameIndex = -1;
                sampleIndex = 0;
                maximumSampleCount = 0;
                frameSignature = 0;
            }
        }

        private static readonly CameraRelativeSystem<SequenceState> s_States =
            new();

        internal static uint Resolve(
            Camera camera,
            int renderFrameIndex,
            ulong frameSignature,
            bool isFirstFrame,
            uint maximumSampleCount = uint.MaxValue)
        {
            if (camera == null)
                return 0;

            maximumSampleCount = Math.Max(1u, maximumSampleCount);
            var state = s_States.GetOrCreateBase(camera);
            if (!state.hasSignature
                || state.resetRequested
                || state.frameSignature != frameSignature
                || state.maximumSampleCount != maximumSampleCount
                || isFirstFrame)
            {
                state.sampleIndex = 0;
                state.resetRequested = false;
            }
            else if (state.lastRenderFrameIndex != renderFrameIndex
                && state.sampleIndex < maximumSampleCount)
            {
                state.sampleIndex++;
            }

            state.hasSignature = true;
            state.lastRenderFrameIndex = renderFrameIndex;
            state.frameSignature = frameSignature;
            state.maximumSampleCount = maximumSampleCount;
            s_States.PurgeDestroyedCameras();
            return state.sampleIndex;
        }

        internal static void RequestReset(Camera camera)
        {
            if (camera != null && s_States.TryGetBase(camera, out var state))
                state.resetRequested = true;
        }

        internal static void Dispose()
        {
            s_States.Dispose();
        }
    }

    public enum ReferencedPathTracingValidationStatus
    {
        NotRun = 0,
        Passed = 1,
        Failed = 2
    }

    [Serializable]
    public sealed class ReferencedPathTracingAtmosphereValidationCase
    {
        public string id;
        public string purpose;
        public float cameraAltitude;
        public float sunElevationDegrees;
        public bool groundCameraVisible;
        public bool cloudsEnabled;
        public RenderingSpace renderingSpace;
        public ReferencedPathTracingAtmosphereTransportMode transportMode;
        public int targetSampleCount;
    }

    public static class ReferencedPathTracingAtmosphereValidationCorpus
    {
        public const int Version = 1;

        private static readonly ReferencedPathTracingAtmosphereValidationCase[]
            s_Cases =
            {
                Create(
                    "atmosphere-sea-level-noon-clear",
                    "Sea-level noon transport with visible spherical ground.",
                    2.0f,
                    90.0f,
                    true,
                    false,
                    RenderingSpace.World,
                    ReferencedPathTracingAtmosphereTransportMode
                        .NumericalReference),
                Create(
                    "atmosphere-sea-level-sunrise-clear",
                    "Long horizon transport at sunrise.",
                    2.0f,
                    0.5f,
                    true,
                    false,
                    RenderingSpace.World,
                    ReferencedPathTracingAtmosphereTransportMode
                        .NumericalReference),
                Create(
                    "atmosphere-high-altitude-noon-clear",
                    "High-altitude observer inside the atmosphere.",
                    12000.0f,
                    90.0f,
                    true,
                    false,
                    RenderingSpace.World,
                    ReferencedPathTracingAtmosphereTransportMode
                        .NumericalReference),
                Create(
                    "atmosphere-space-noon-clear",
                    "Observer outside the atmosphere looking through the shell.",
                    100000.0f,
                    90.0f,
                    true,
                    false,
                    RenderingSpace.World,
                    ReferencedPathTracingAtmosphereTransportMode
                        .NumericalReference),
                Create(
                    "atmosphere-sea-level-noon-ground-hidden",
                    "Ground camera visibility disabled while transport remains active.",
                    2.0f,
                    90.0f,
                    false,
                    false,
                    RenderingSpace.World,
                    ReferencedPathTracingAtmosphereTransportMode
                        .NumericalReference),
                Create(
                    "atmosphere-camera-space-noon-clear",
                    "Camera-relative planet contract with scene-hit precedence.",
                    0.0f,
                    90.0f,
                    true,
                    false,
                    RenderingSpace.Camera,
                    ReferencedPathTracingAtmosphereTransportMode
                        .NumericalReference),
                Create(
                    "atmosphere-sea-level-noon-cloud-preview",
                    "Cloud-on approximation validation against the numerical clear-sky baseline.",
                    2.0f,
                    90.0f,
                    true,
                    true,
                    RenderingSpace.World,
                    ReferencedPathTracingAtmosphereTransportMode
                        .OptimizedPreview)
            };

        public static IReadOnlyList<
            ReferencedPathTracingAtmosphereValidationCase> Cases =>
                s_Cases;

        public static bool TryGetCase(
            string id,
            out ReferencedPathTracingAtmosphereValidationCase validationCase)
        {
            for (var index = 0; index < s_Cases.Length; index++)
            {
                if (string.Equals(
                        s_Cases[index].id,
                        id,
                        StringComparison.Ordinal))
                {
                    validationCase = s_Cases[index];
                    return true;
                }
            }

            validationCase = null;
            return false;
        }

        private static ReferencedPathTracingAtmosphereValidationCase Create(
            string id,
            string purpose,
            float cameraAltitude,
            float sunElevationDegrees,
            bool groundCameraVisible,
            bool cloudsEnabled,
            RenderingSpace renderingSpace,
            ReferencedPathTracingAtmosphereTransportMode transportMode)
        {
            return new ReferencedPathTracingAtmosphereValidationCase
            {
                id = id,
                purpose = purpose,
                cameraAltitude = cameraAltitude,
                sunElevationDegrees = sunElevationDegrees,
                groundCameraVisible = groundCameraVisible,
                cloudsEnabled = cloudsEnabled,
                renderingSpace = renderingSpace,
                transportMode = transportMode,
                targetSampleCount = 8192
            };
        }
    }

    [Serializable]
    public sealed class ReferencedPathTracingAtmosphereValidationEvidence
    {
        public int contractVersion;
        public int corpusVersion;
        public string caseId;
        public ReferencedPathTracingValidationStatus status;
        public bool timedOut;
        public int accumulatedSampleCount;
        public float finitePixelFraction;
        public float negativeRadianceFraction;
        public float atmosphereTrackingOverflowFraction;
        public float cloudTrackingOverflowFraction;
        public int maximumAtmosphereTrackingStepCount;
        public int maximumCloudTrackingStepCount;
        public float relativeMeanError;
        public float gpuMilliseconds;
        public string referenceImageSha256;
        public string notes;
    }

    public static class ReferencedPathTracingAtmosphereValidationGate
    {
        public const int ContractVersion = 1;
        public const float MinimumFinitePixelFraction = 1.0f;
        public const float MaximumNegativeRadianceFraction = 0.0f;
        public const float MaximumTrackingOverflowFraction = 0.0f;
        public const float MaximumNumericalReferenceRelativeMeanError = 0.02f;
        public const float MaximumOptimizedPreviewRelativeMeanError = 0.05f;
        public const float MaximumCameraAltitudeError = 1.0f;
        public const float MaximumSunElevationErrorDegrees = 0.25f;

        public static bool ValidateMetadata(
            string caseId,
            ReferencedPathTracingEnvironmentMetadata environment,
            out string failure)
        {
            if (!ReferencedPathTracingAtmosphereValidationCorpus.TryGetCase(
                    caseId,
                    out var validationCase))
            {
                return Fail(
                    $"Unknown Reference Atmosphere corpus case '{caseId}'.",
                    out failure);
            }

            var atmosphere = environment?.atmosphere;
            if (environment == null
                || environment.mode
                    != ReferencedPathTracingEnvironmentMode.ReferenceAtmosphere
                || atmosphere == null
                || !atmosphere.active
                || atmosphere.contractVersion
                    != ReferencedPathTracingAtmosphereState.ContractVersion
                || atmosphere.validationContractVersion != ContractVersion)
            {
                return Fail(
                    "Reference Atmosphere metadata contract is incomplete.",
                    out failure);
            }

            if (atmosphere.transportMode != validationCase.transportMode
                || atmosphere.cloudsEnabled != validationCase.cloudsEnabled
                || atmosphere.groundCameraVisible
                    != validationCase.groundCameraVisible
                || atmosphere.cameraRelativeRenderingSpace
                    != (validationCase.renderingSpace
                        == RenderingSpace.Camera))
            {
                return Fail(
                    "Reference Atmosphere mode or visibility does not match the corpus case.",
                    out failure);
            }

            if (!IsFinite(atmosphere.observerAltitude)
                || !IsFinite(atmosphere.sunElevationDegrees)
                || Mathf.Abs(
                        atmosphere.observerAltitude
                        - validationCase.cameraAltitude)
                    > MaximumCameraAltitudeError
                || Mathf.Abs(
                        atmosphere.sunElevationDegrees
                        - validationCase.sunElevationDegrees)
                    > MaximumSunElevationErrorDegrees)
            {
                return Fail(
                    "Reference Atmosphere observer altitude or sun elevation does not match the corpus case.",
                    out failure);
            }

            var optimized =
                validationCase.transportMode
                    == ReferencedPathTracingAtmosphereTransportMode
                        .OptimizedPreview;
            if (atmosphere.usesOpticalDepthLutApproximation != optimized)
            {
                return Fail(
                    "Atmosphere approximation metadata does not match the selected transport mode.",
                    out failure);
            }

            if (!optimized
                && (!atmosphere.numericalReferenceEligible
                    || atmosphere.cloudMultipleScatteringMode
                        != ReferencedPathTracingCloudMultipleScatteringMode
                            .Off))
            {
                return Fail(
                    "Numerical Reference capture contains a preview-only approximation.",
                    out failure);
            }

            failure = string.Empty;
            return true;
        }

        public static bool ValidateEvidence(
            ReferencedPathTracingAtmosphereValidationEvidence evidence,
            out string failure)
        {
            if (evidence == null
                || !ReferencedPathTracingAtmosphereValidationCorpus.TryGetCase(
                    evidence.caseId,
                    out var validationCase))
            {
                return Fail(
                    "Reference Atmosphere validation evidence is missing or has an unknown case.",
                    out failure);
            }

            if (evidence.contractVersion != ContractVersion
                || evidence.corpusVersion
                    != ReferencedPathTracingAtmosphereValidationCorpus.Version
                || evidence.status != ReferencedPathTracingValidationStatus.Passed)
            {
                return Fail(
                    "Reference Atmosphere validation version or status is invalid.",
                    out failure);
            }

            if (evidence.timedOut
                || evidence.accumulatedSampleCount
                    != validationCase.targetSampleCount)
            {
                return Fail(
                    "Reference Atmosphere capture timed out or has the wrong SPP.",
                    out failure);
            }

            if (!IsFraction(evidence.finitePixelFraction)
                || !IsFraction(evidence.negativeRadianceFraction)
                || !IsFraction(evidence.atmosphereTrackingOverflowFraction)
                || !IsFraction(evidence.cloudTrackingOverflowFraction)
                || !IsFinite(evidence.relativeMeanError)
                || !IsFinite(evidence.gpuMilliseconds)
                || evidence.finitePixelFraction
                    < MinimumFinitePixelFraction
                || evidence.negativeRadianceFraction
                    > MaximumNegativeRadianceFraction
                || evidence.atmosphereTrackingOverflowFraction
                    > MaximumTrackingOverflowFraction
                || evidence.cloudTrackingOverflowFraction
                    > MaximumTrackingOverflowFraction
                || evidence.maximumAtmosphereTrackingStepCount < 0
                || evidence.maximumAtmosphereTrackingStepCount
                    > ReferencedPathTracingEnvironmentImportanceLayout
                        .MaximumAtmosphereTrackingStepCount
                || evidence.maximumCloudTrackingStepCount < 0
                || evidence.maximumCloudTrackingStepCount
                    > ReferencedPathTracingEnvironmentImportanceLayout
                        .MaximumCloudTrackingStepCount
                || evidence.gpuMilliseconds < 0.0f)
            {
                return Fail(
                    "Reference Atmosphere finite, overflow, or timing metrics are invalid.",
                    out failure);
            }

            var maximumRelativeError =
                validationCase.transportMode
                    == ReferencedPathTracingAtmosphereTransportMode
                        .NumericalReference
                    ? MaximumNumericalReferenceRelativeMeanError
                    : MaximumOptimizedPreviewRelativeMeanError;
            if (evidence.relativeMeanError < 0.0f
                || evidence.relativeMeanError > maximumRelativeError
                || !IsSha256(evidence.referenceImageSha256))
            {
                return Fail(
                    "Reference Atmosphere image comparison exceeds the case threshold.",
                    out failure);
            }

            failure = string.Empty;
            return true;
        }

        public static bool ValidateCorpus(
            IEnumerable<ReferencedPathTracingAtmosphereValidationEvidence>
                evidence,
            out string failure)
        {
            if (evidence == null)
                return Fail("Atmosphere corpus evidence is missing.", out failure);

            var byId =
                new Dictionary<
                    string,
                    ReferencedPathTracingAtmosphereValidationEvidence>(
                    StringComparer.Ordinal);
            foreach (var item in evidence)
            {
                if (item == null
                    || string.IsNullOrWhiteSpace(item.caseId)
                    || !byId.TryAdd(item.caseId, item))
                {
                    return Fail(
                        "Atmosphere corpus contains null, unnamed, or duplicate evidence.",
                        out failure);
                }
            }

            foreach (var validationCase
                in ReferencedPathTracingAtmosphereValidationCorpus.Cases)
            {
                if (!byId.TryGetValue(validationCase.id, out var item))
                {
                    return Fail(
                        $"Missing atmosphere corpus case '{validationCase.id}'.",
                        out failure);
                }

                if (!ValidateEvidence(item, out failure))
                    return false;
            }

            if (byId.Count
                != ReferencedPathTracingAtmosphereValidationCorpus.Cases.Count)
            {
                return Fail(
                    "Unexpected atmosphere corpus evidence is present.",
                    out failure);
            }

            failure = string.Empty;
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFraction(float value)
        {
            return IsFinite(value) && value >= 0.0f && value <= 1.0f;
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
                return false;

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if ((character < '0' || character > '9')
                    && (character < 'a' || character > 'f')
                    && (character < 'A' || character > 'F'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Fail(string message, out string failure)
        {
            failure = message;
            return false;
        }
    }

    internal static class ReferencedPathTracingEstimatorPolicy
    {
        internal static bool IsNeeEligible(
            ReferencedPathTracingEnvironmentEstimatorMode mode,
            bool bsdfReachable)
        {
            return mode != ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly
                || !bsdfReachable;
        }

        internal static float GetLightEstimatorWeight(
            ReferencedPathTracingEnvironmentEstimatorMode mode,
            bool bsdfReachable,
            bool singular,
            float lightPdf,
            float bsdfPdf)
        {
            if (singular || !bsdfReachable)
                return 1.0f;

            if (mode == ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly)
                return 0.0f;
            if (mode == ReferencedPathTracingEnvironmentEstimatorMode.LightOnly)
                return 1.0f;

            return PowerHeuristic(lightPdf, bsdfPdf);
        }

        internal static float GetBsdfEstimatorWeight(
            ReferencedPathTracingEnvironmentEstimatorMode mode,
            bool sampledDeltaEvent,
            float bsdfPdf,
            float lightPdf)
        {
            if (sampledDeltaEvent || !IsFinitePositive(lightPdf))
                return 1.0f;

            if (mode == ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly)
                return 1.0f;
            if (mode == ReferencedPathTracingEnvironmentEstimatorMode.LightOnly)
                return 0.0f;

            return PowerHeuristic(bsdfPdf, lightPdf);
        }

        private static float PowerHeuristic(float pdfA, float pdfB)
        {
            pdfA = IsFinitePositive(pdfA) ? pdfA : 0.0f;
            pdfB = IsFinitePositive(pdfB) ? pdfB : 0.0f;
            var maximumPdf = Mathf.Max(pdfA, pdfB);
            if (maximumPdf <= 0.0f)
                return 0.0f;

            var normalizedA = pdfA / maximumPdf;
            var normalizedB = pdfB / maximumPdf;
            var squaredA = normalizedA * normalizedA;
            var squaredB = normalizedB * normalizedB;
            return squaredA / Mathf.Max(squaredA + squaredB, 1e-20f);
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0.0f
                && !float.IsNaN(value)
                && !float.IsInfinity(value);
        }
    }

    internal static class ReferencedPathTracingLightProposalPolicy
    {
        internal const float DefaultGlobalProposalProbability = 0.25f;
        internal const float MinimumGlobalProposalProbability = 0.05f;

        internal static float SanitizeGlobalProposalProbability(
            float probability,
            float fallback)
        {
            var finiteFallback = IsFinite(fallback)
                ? fallback
                : DefaultGlobalProposalProbability;
            return Mathf.Clamp(
                IsFinite(probability) ? probability : finiteFallback,
                MinimumGlobalProposalProbability,
                1.0f);
        }

        internal static float ResolveGlobalProposalProbability(
            bool shadingPointSelectionEnabled,
            float configuredProbability,
            float globalTotalWeight,
            float localTotalWeight)
        {
            if (!IsFinitePositive(globalTotalWeight))
                return IsFinitePositive(localTotalWeight) ? 0.0f : 1.0f;
            if (!shadingPointSelectionEnabled
                || !IsFinitePositive(localTotalWeight))
            {
                return 1.0f;
            }

            return SanitizeGlobalProposalProbability(
                configuredProbability,
                DefaultGlobalProposalProbability);
        }

        internal static float EvaluateMixturePdf(
            float globalProposalProbability,
            float globalPdf,
            float localPdf)
        {
            var probability = Mathf.Clamp01(
                IsFinite(globalProposalProbability)
                    ? globalProposalProbability
                    : 1.0f);
            globalPdf = IsFiniteNonNegative(globalPdf) ? globalPdf : 0.0f;
            localPdf = IsFiniteNonNegative(localPdf) ? localPdf : 0.0f;
            return probability * globalPdf
                + (1.0f - probability) * localPdf;
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0.0f && IsFinite(value);
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return value >= 0.0f && IsFinite(value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [Serializable]
    public sealed class ReferencedPathTracingValidationEvidence
    {
        public ReferencedPathTracingValidationStatus status;
        public string graphicsApi;
        public string deviceName;
        public string referenceImageSha256;
        public float finitePixelFraction;
        public float negativeRadianceFraction;
        public float meanLuminance;
        public float relativeMeanError;
        public string notes;
    }

    [Serializable]
    public sealed class ReferencedPathTracingEstimatorMeasurement
    {
        public ReferencedPathTracingEnvironmentEstimatorMode estimatorMode;
        public int sampleCount;
        public float meanLuminance;
        public float standardError;
        public float finitePixelFraction;
        public float negativeRadianceFraction;
    }

    [Serializable]
    public sealed class ReferencedPathTracingLightSelectionEvidence
    {
        public int sampleCount;
        public float[] declaredSelectionPdfs;
        public int[] observedSelectionCounts;
    }

    [Serializable]
    public sealed class ReferencedPathTracingPdfConsistencyEvidence
    {
        public int comparisonCount;
        public int nonFiniteCount;
        public float maximumRelativeError;
    }

    [Serializable]
    public sealed class ReferencedPathTracingLightProposalMeasurement
    {
        public bool shadingPointSelectionEnabled;
        public float globalProposalProbability;
        public int sampleCount;
        public float meanLuminance;
        public float standardError;
        public float luminanceVariance;
        public float finitePixelFraction;
        public float negativeRadianceFraction;
    }

    [Serializable]
    public sealed class ReferencedPathTracingTransportConformanceEvidence
    {
        public ReferencedPathTracingValidationStatus status;
        public ReferencedPathTracingEstimatorMeasurement[] estimatorMeasurements;
        public ReferencedPathTracingLightProposalMeasurement[]
            lightProposalMeasurements;
        public ReferencedPathTracingLightSelectionEvidence lightSelection;
        public ReferencedPathTracingPdfConsistencyEvidence pdfConsistency;
        public string notes;
    }

    public static class ReferencedPathTracingTransportConformanceGate
    {
        public const float MeanRelativeTolerance = 0.02f;
        public const float MeanStandardErrorMultiplier = 4.0f;
        public const float HistogramStandardDeviationMultiplier = 6.0f;
        public const float HistogramAbsoluteFractionTolerance = 0.002f;
        public const float MaximumPdfRelativeError = 1e-4f;

        public static bool Validate(
            ReferencedPathTracingTransportConformanceEvidence evidence,
            out string failure)
        {
            if (evidence == null)
                return Fail("Transport conformance evidence is missing.", out failure);
            if (evidence.status != ReferencedPathTracingValidationStatus.Passed)
            {
                return Fail(
                    "Transport conformance evidence has not passed.",
                    out failure);
            }

            if (!ValidateEstimatorMeasurements(
                    evidence.estimatorMeasurements,
                    out failure)
                || !ValidateLightProposalMeasurements(
                    evidence.lightProposalMeasurements,
                    out failure)
                || !ValidateLightSelection(evidence.lightSelection, out failure)
                || !ValidatePdfConsistency(evidence.pdfConsistency, out failure))
            {
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidateEstimatorMeasurements(
            IReadOnlyList<ReferencedPathTracingEstimatorMeasurement> measurements,
            out string failure)
        {
            if (measurements == null || measurements.Count != 3)
            {
                return Fail(
                    "MIS, Light Only, and BSDF Only measurements are required.",
                    out failure);
            }

            var byMode =
                new Dictionary<
                    ReferencedPathTracingEnvironmentEstimatorMode,
                    ReferencedPathTracingEstimatorMeasurement>();
            foreach (var measurement in measurements)
            {
                if (measurement == null
                    || measurement.sampleCount <= 0
                    || !IsFiniteNonNegative(measurement.meanLuminance)
                    || !IsFiniteNonNegative(measurement.standardError)
                    || measurement.finitePixelFraction != 1.0f
                    || measurement.negativeRadianceFraction != 0.0f
                    || !byMode.TryAdd(
                        measurement.estimatorMode,
                        measurement))
                {
                    return Fail(
                        "Estimator measurements contain invalid or duplicate data.",
                        out failure);
                }
            }

            if (!byMode.TryGetValue(
                    ReferencedPathTracingEnvironmentEstimatorMode.Mis,
                    out var mis)
                || !byMode.TryGetValue(
                    ReferencedPathTracingEnvironmentEstimatorMode.LightOnly,
                    out var lightOnly)
                || !byMode.TryGetValue(
                    ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly,
                    out var bsdfOnly))
            {
                return Fail(
                    "All three transport estimator modes must be measured.",
                    out failure);
            }

            if (!MeansAgree(mis, lightOnly)
                || !MeansAgree(mis, bsdfOnly)
                || !MeansAgree(lightOnly, bsdfOnly))
            {
                return Fail(
                    "Transport estimator means disagree beyond statistical tolerance.",
                    out failure);
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidateLightProposalMeasurements(
            IReadOnlyList<ReferencedPathTracingLightProposalMeasurement>
                measurements,
            out string failure)
        {
            if (measurements == null || measurements.Count != 2)
            {
                return Fail(
                    "Global-only and shading-point light-proposal measurements are required.",
                    out failure);
            }

            ReferencedPathTracingLightProposalMeasurement globalOnly = null;
            ReferencedPathTracingLightProposalMeasurement shadingPoint = null;
            foreach (var measurement in measurements)
            {
                if (measurement == null
                    || measurement.sampleCount <= 0
                    || !IsFiniteNonNegative(measurement.meanLuminance)
                    || !IsFiniteNonNegative(measurement.standardError)
                    || !IsFiniteNonNegative(measurement.luminanceVariance)
                    || measurement.finitePixelFraction != 1.0f
                    || measurement.negativeRadianceFraction != 0.0f)
                {
                    return Fail(
                        "Light-proposal measurements contain invalid data.",
                        out failure);
                }

                if (measurement.shadingPointSelectionEnabled)
                {
                    if (shadingPoint != null
                        || measurement.globalProposalProbability
                            < ReferencedPathTracingLightProposalPolicy
                                .MinimumGlobalProposalProbability
                        || measurement.globalProposalProbability > 1.0f
                        || float.IsNaN(
                            measurement.globalProposalProbability)
                        || float.IsInfinity(
                            measurement.globalProposalProbability))
                    {
                        return Fail(
                            "Shading-point light-proposal evidence has an invalid support floor.",
                            out failure);
                    }

                    shadingPoint = measurement;
                }
                else
                {
                    if (globalOnly != null
                        || measurement.globalProposalProbability != 1.0f)
                    {
                        return Fail(
                            "Global-only light-proposal evidence must declare probability one.",
                            out failure);
                    }

                    globalOnly = measurement;
                }
            }

            if (globalOnly == null || shadingPoint == null)
            {
                return Fail(
                    "Both light-proposal modes must be measured.",
                    out failure);
            }

            if (!MeansAgree(
                    globalOnly.meanLuminance,
                    globalOnly.standardError,
                    shadingPoint.meanLuminance,
                    shadingPoint.standardError))
            {
                return Fail(
                    "Global-only and shading-point light-proposal means disagree beyond statistical tolerance.",
                    out failure);
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidateLightSelection(
            ReferencedPathTracingLightSelectionEvidence evidence,
            out string failure)
        {
            if (evidence == null
                || evidence.sampleCount <= 0
                || evidence.declaredSelectionPdfs == null
                || evidence.observedSelectionCounts == null
                || evidence.declaredSelectionPdfs.Length == 0
                || evidence.declaredSelectionPdfs.Length
                    != evidence.observedSelectionCounts.Length)
            {
                return Fail("Light-selection evidence is incomplete.", out failure);
            }

            double probabilitySum = 0.0;
            long observedSum = 0;
            for (var index = 0;
                 index < evidence.declaredSelectionPdfs.Length;
                 index++)
            {
                var probability = evidence.declaredSelectionPdfs[index];
                var observed = evidence.observedSelectionCounts[index];
                if (!IsFiniteNonNegative(probability)
                    || probability > 1.0f
                    || observed < 0)
                {
                    return Fail(
                        "Light-selection evidence contains invalid values.",
                        out failure);
                }

                probabilitySum += probability;
                observedSum += observed;
                if ((probability == 0.0f && observed != 0)
                    || (probability == 1.0f
                        && observed != evidence.sampleCount))
                {
                    return Fail(
                        $"Light-selection bin {index} violates proposal support.",
                        out failure);
                }

                var expected = evidence.sampleCount * (double)probability;
                var variance =
                    evidence.sampleCount
                    * (double)probability
                    * (1.0 - probability);
                var statisticalTolerance =
                    HistogramStandardDeviationMultiplier
                    * Math.Sqrt(Math.Max(variance, 0.0));
                var absoluteTolerance =
                    HistogramAbsoluteFractionTolerance
                    * evidence.sampleCount;
                if (Math.Abs(observed - expected)
                    > Math.Max(statisticalTolerance, absoluteTolerance))
                {
                    return Fail(
                        $"Light-selection bin {index} disagrees with its declared PDF.",
                        out failure);
                }
            }

            if (Math.Abs(probabilitySum - 1.0) > 1e-4
                || observedSum != evidence.sampleCount)
            {
                return Fail(
                    "Light-selection probabilities or counts do not normalize.",
                    out failure);
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidatePdfConsistency(
            ReferencedPathTracingPdfConsistencyEvidence evidence,
            out string failure)
        {
            if (evidence == null
                || evidence.comparisonCount <= 0
                || evidence.nonFiniteCount != 0
                || !IsFiniteNonNegative(evidence.maximumRelativeError)
                || evidence.maximumRelativeError > MaximumPdfRelativeError)
            {
                return Fail(
                    "Sample/evaluate PDF consistency is outside tolerance.",
                    out failure);
            }

            failure = string.Empty;
            return true;
        }

        private static bool MeansAgree(
            ReferencedPathTracingEstimatorMeasurement first,
            ReferencedPathTracingEstimatorMeasurement second)
        {
            return MeansAgree(
                first.meanLuminance,
                first.standardError,
                second.meanLuminance,
                second.standardError);
        }

        private static bool MeansAgree(
            float firstMean,
            float firstStandardError,
            float secondMean,
            float secondStandardError)
        {
            var difference = Math.Abs(
                (double)firstMean - secondMean);
            var relativeTolerance = MeanRelativeTolerance
                * Math.Max(
                    Math.Max(firstMean, secondMean),
                    1e-6f);
            var standardErrorTolerance =
                MeanStandardErrorMultiplier
                * Math.Sqrt(
                    firstStandardError * firstStandardError
                    + secondStandardError * secondStandardError);
            return difference
                <= Math.Max(relativeTolerance, standardErrorTolerance);
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return value >= 0.0f
                && !float.IsNaN(value)
                && !float.IsInfinity(value);
        }

        private static bool Fail(string message, out string failure)
        {
            failure = message;
            return false;
        }
    }

    [Serializable]
    public sealed class ReferencedPathTracingCaptureMetadata
    {
        public int freezeContractVersion;
        public int corpusVersion;
        public int integratorVersion;
        public string corpusCaseId;
        public int width;
        public int height;
        public int targetSampleCount;
        public ulong accumulatedSampleCount;
        public bool deterministicSampling;
        public int fixedSeed;
        public ReferencedPathTracingSamplingMode pathSamplingMode;
        public int samplingContractVersion;
        public int physicalCameraContractVersion;
        public bool usesPhysicalCameraDof;
        public int shadingNormalContractVersion;
        public int thinWalledTransmissionContractVersion;
        // Kept as the serialized field name for capture compatibility. Version
        // 2 and later identify the scalar geometry-opacity contract.
        public int coloredOpacityContractVersion;
        public int maxBounceCount;
        public int russianRouletteStartBounce;
        public ulong integratorSignature;
        public ReferencedPathTracingEnvironmentEstimatorMode estimatorMode;
        public ReferencedPathTracingTransportDebugMode transportDebugMode;
        public bool usesShadingPointLightSelection;
        public float globalLightProposalProbability;
        public bool usesLightSpatialIndex;
        public int lightSpatialIndexVersion;
        public int lightSpatialIndexResolution;
        public int lightSpatialIndexCellCapacity;
        public bool usesReGIR;
        public bool usesDenoiser;
        public bool usesRasterGI;
        public bool rawRadianceIsPreExposed;
        public bool hasMainDirectionalLight;
        public int localLightCount;
        public int unsupportedMaterialCount;
        public bool standardLitOnly;
        public bool imageOriginBottomLeft;
        public ReferencedPathTracingEnvironmentMetadata environment;
        public ReferencedPathTracingAtmosphereValidationEvidence
            atmosphereValidation;
        public ReferencedPathTracingValidationEvidence validation;
        public ReferencedPathTracingTransportConformanceEvidence
            transportConformance;
    }

    [Serializable]
    public sealed class ReferencedPathTracingV1CorpusCase
    {
        public string id;
        public string purpose;
        public int width;
        public int height;
        public int targetSampleCount;
        public int fixedSeed;
        public ReferencedPathTracingSamplingMode pathSamplingMode;
        public int maxBounceCount;
        public int russianRouletteStartBounce;
        public ReferencedPathTracingEnvironmentSamplingMode samplingMode;
        public ReferencedPathTracingEnvironmentEstimatorMode estimatorMode;
        public bool usesShadingPointLightSelection;
        public float globalLightProposalProbability;
        public bool usesLightSpatialIndex;

        internal ReferencedPathTracingV1CorpusCase(
            string id,
            string purpose)
        {
            this.id = id;
            this.purpose = purpose;
            width = 512;
            height = 512;
            targetSampleCount = 2048;
            fixedSeed = 0x13579B;
            pathSamplingMode =
                ReferencedPathTracingSamplingMode.IndexedBnd;
            maxBounceCount = 4;
            russianRouletteStartBounce = 3;
            samplingMode =
                ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling;
            estimatorMode =
                ReferencedPathTracingEnvironmentEstimatorMode.Mis;
            usesShadingPointLightSelection = true;
            globalLightProposalProbability =
                ReferencedPathTracingLightProposalPolicy
                    .DefaultGlobalProposalProbability;
            usesLightSpatialIndex = true;
        }
    }

    public static class ReferencedPathTracingV1Corpus
    {
        public const int Version = 1;

        private static readonly ReferencedPathTracingV1CorpusCase[] s_Cases =
        {
            new(
                "hdri-constant-furnace",
                "Constant white and gray diffuse furnace energy check."),
            new(
                "hdri-rotation-tint-intensity",
                "Rotation, tint, and physical-intensity matrix."),
            new(
                "hdri-bright-emitter",
                "Localized high-luminance emitter and MIS variance check."),
            new(
                "hdri-openpbr-sphere-grid",
                "Diffuse, roughness, metalness, and coat OpenPBR grid."),
            new(
                "hdri-interior-occlusion",
                "Interior doorway and environment visibility."),
            new(
                "hdri-alpha-foliage-shadow",
                "Alpha-tested foliage environment shadowing."),
            new(
                "hdri-camera-hidden-lighting",
                "Camera-hidden but lighting-enabled environment.")
        };

        private static readonly IReadOnlyList<ReferencedPathTracingV1CorpusCase>
            s_ReadOnlyCases = Array.AsReadOnly(s_Cases);

        public static IReadOnlyList<ReferencedPathTracingV1CorpusCase> Cases =>
            s_ReadOnlyCases;

        public static bool TryGetCase(
            string id,
            out ReferencedPathTracingV1CorpusCase corpusCase)
        {
            for (var index = 0; index < s_Cases.Length; index++)
            {
                if (!string.Equals(
                        s_Cases[index].id,
                        id,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                corpusCase = s_Cases[index];
                return true;
            }

            corpusCase = null;
            return false;
        }
    }

    public static class ReferencedPathTracingV1FreezeGate
    {
        public const int ContractVersion = 11;
        public const float MinimumFinitePixelFraction = 1.0f;
        public const float MaximumNegativeRadianceFraction = 0.0f;
        public const float MaximumRelativeMeanError = 0.02f;

        public static bool ValidateCaptureContract(
            ReferencedPathTracingCaptureMetadata metadata,
            out string failure)
        {
            if (metadata == null)
                return Fail("Capture metadata is missing.", out failure);
            if (!ReferencedPathTracingV1Corpus.TryGetCase(
                    metadata.corpusCaseId,
                    out var corpusCase))
            {
                return Fail(
                    $"Unknown HDRI corpus case '{metadata.corpusCaseId}'.",
                    out failure);
            }

            if (metadata.freezeContractVersion != ContractVersion
                || metadata.corpusVersion != ReferencedPathTracingV1Corpus.Version
                || metadata.integratorVersion
                    != ReferencedPathTracingIntegratorState.Version)
            {
                return Fail("V1 contract version mismatch.", out failure);
            }

            if (metadata.width != corpusCase.width
                || metadata.height != corpusCase.height)
            {
                return Fail("Capture resolution does not match the corpus.", out failure);
            }

            if (metadata.accumulatedSampleCount
                    != (ulong)corpusCase.targetSampleCount
                || metadata.targetSampleCount
                    != corpusCase.targetSampleCount)
            {
                return Fail("Capture SPP does not match the corpus.", out failure);
            }

            if (!metadata.deterministicSampling
                || metadata.fixedSeed != corpusCase.fixedSeed
                || metadata.integratorSignature == 0)
            {
                return Fail("Canonical deterministic seed is not active.", out failure);
            }

            if (metadata.pathSamplingMode
                    != corpusCase.pathSamplingMode
                || metadata.pathSamplingMode
                    != ReferencedPathTracingSamplingMode.IndexedBnd
                || metadata.samplingContractVersion
                    != ReferencedPathTracingSamplingContract.Version)
            {
                return Fail(
                    "Path sampling contract does not match the corpus.",
                    out failure);
            }

            if (metadata.physicalCameraContractVersion
                    != ReferencedPathTracingPhysicalCameraState.Version
                || metadata.usesPhysicalCameraDof)
            {
                return Fail(
                    "V1 canonical captures require pinhole camera transport.",
                    out failure);
            }

            if (metadata.shadingNormalContractVersion
                != ReferencedPathTracingShadingNormalContract.Version)
            {
                return Fail(
                    "Shading-normal transport contract does not match the corpus.",
                    out failure);
            }

            if (metadata.thinWalledTransmissionContractVersion
                != ReferencedPathTracingThinWalledTransmissionContract.Version)
            {
                return Fail(
                    "Thin-walled transmission contract does not match the corpus.",
                    out failure);
            }

            if (metadata.coloredOpacityContractVersion
                != ReferencedPathTracingGeometryOpacityContract.Version)
            {
                return Fail(
                    "Geometry-opacity transport contract does not match the corpus.",
                    out failure);
            }

            if (metadata.maxBounceCount != corpusCase.maxBounceCount
                || metadata.russianRouletteStartBounce
                    != corpusCase.russianRouletteStartBounce)
            {
                return Fail("Integrator depth does not match the V1 corpus.", out failure);
            }

            if (metadata.estimatorMode != corpusCase.estimatorMode
                || metadata.transportDebugMode
                    != ReferencedPathTracingTransportDebugMode.Combined)
            {
                return Fail(
                    "Transport estimator or diagnostic mode does not match the corpus.",
                    out failure);
            }

            if (metadata.usesShadingPointLightSelection
                    != corpusCase.usesShadingPointLightSelection
                || !IsFinite(metadata.globalLightProposalProbability)
                || Math.Abs(
                    metadata.globalLightProposalProbability
                    - corpusCase.globalLightProposalProbability) > 1e-6f)
            {
                return Fail(
                    "Light-proposal settings do not match the corpus.",
                    out failure);
            }

            if (metadata.usesLightSpatialIndex
                    != corpusCase.usesLightSpatialIndex
                || metadata.lightSpatialIndexVersion
                    != ReferencedPathTracingLightSpatialIndexBuilder.Version
                || metadata.lightSpatialIndexResolution
                    != ReferencedPathTracingLightSpatialIndexBuilder
                        .GridResolution
                || metadata.lightSpatialIndexCellCapacity
                    != ReferencedPathTracingLightSpatialIndexBuilder
                        .CellCapacity)
            {
                return Fail(
                    "Reference Light Spatial Index settings do not match the corpus.",
                    out failure);
            }

            if (metadata.usesReGIR
                || metadata.usesDenoiser
                || metadata.usesRasterGI
                || metadata.rawRadianceIsPreExposed)
            {
                return Fail(
                    "Canonical radiance depends on a forbidden temporal or display subsystem.",
                    out failure);
            }

            if (metadata.hasMainDirectionalLight
                || metadata.localLightCount != 0)
            {
                return Fail(
                    "HDRI V1 corpus must not contain non-environment lights.",
                    out failure);
            }

            if (!metadata.standardLitOnly
                || metadata.unsupportedMaterialCount != 0)
            {
                return Fail("Unsupported material coverage is non-zero.", out failure);
            }

            if (metadata.environment == null
                || metadata.environment.contractVersion
                    != ReferencedPathTracingEnvironmentMetadata.ContractVersion
                || metadata.environment.mode
                    != ReferencedPathTracingEnvironmentMode.Hdri
                || metadata.environment.atmosphere != null
                || metadata.environment.contentHash == 0
                || metadata.environment.pdfVersion
                    != ReferencedPathTracingEnvironmentImportanceLayout.Version
                || metadata.environment.backgroundResolution <= 0
                || metadata.environment.lightingResolution <= 0
                || metadata.environment.physicalIntensityMultiplier <= 0.0f
                || metadata.environment.rawRadianceIsPreExposed
                || !metadata.environment.lightingEnabled
                || metadata.environment.samplingMode != corpusCase.samplingMode
                || metadata.environment.estimatorMode != corpusCase.estimatorMode
                || metadata.environment.debugMode
                    != ReferencedPathTracingEnvironmentDebugMode.Combined)
            {
                return Fail("HDRI environment contract does not match the corpus.", out failure);
            }

            var cameraShouldBeVisible = !string.Equals(
                corpusCase.id,
                "hdri-camera-hidden-lighting",
                StringComparison.Ordinal);
            if (metadata.environment.cameraVisible != cameraShouldBeVisible)
            {
                return Fail(
                    "Environment camera visibility does not match the corpus case.",
                    out failure);
            }

            failure = string.Empty;
            return true;
        }

        public static bool ValidateFrozenCapture(
            ReferencedPathTracingCaptureMetadata metadata,
            out string failure)
        {
            if (!ValidateCaptureContract(metadata, out failure))
                return false;

            var evidence = metadata.validation;
            if (evidence == null
                || evidence.status != ReferencedPathTracingValidationStatus.Passed)
            {
                return Fail("GPU validation evidence has not passed.", out failure);
            }

            if (!IsSha256(evidence.referenceImageSha256))
            {
                return Fail("Canonical EXR SHA-256 is missing.", out failure);
            }

            if (!IsFinite(evidence.finitePixelFraction)
                || !IsFinite(evidence.negativeRadianceFraction)
                || !IsFinite(evidence.relativeMeanError)
                || evidence.negativeRadianceFraction < 0.0f
                || evidence.relativeMeanError < 0.0f
                || evidence.finitePixelFraction < MinimumFinitePixelFraction
                || evidence.negativeRadianceFraction
                    > MaximumNegativeRadianceFraction
                || evidence.relativeMeanError > MaximumRelativeMeanError)
            {
                return Fail("GPU image metrics exceed the V1 thresholds.", out failure);
            }

            if (!ReferencedPathTracingTransportConformanceGate.Validate(
                    metadata.transportConformance,
                    out failure))
            {
                return false;
            }

            if (!ValidateLightProposalEvidenceContract(
                    metadata,
                    out failure))
            {
                return false;
            }

            failure = string.Empty;
            return true;
        }

        public static bool ValidateCorpus(
            IEnumerable<ReferencedPathTracingCaptureMetadata> captures,
            out string failure)
        {
            if (captures == null)
                return Fail("HDRI corpus captures are missing.", out failure);

            var captureById =
                new Dictionary<string, ReferencedPathTracingCaptureMetadata>(
                    StringComparer.Ordinal);
            foreach (var capture in captures)
            {
                if (capture == null
                    || string.IsNullOrWhiteSpace(capture.corpusCaseId))
                {
                    return Fail("A corpus capture has no case ID.", out failure);
                }

                if (!captureById.TryAdd(capture.corpusCaseId, capture))
                {
                    return Fail(
                        $"Duplicate corpus capture '{capture.corpusCaseId}'.",
                        out failure);
                }
            }

            foreach (var corpusCase in ReferencedPathTracingV1Corpus.Cases)
            {
                if (!captureById.TryGetValue(corpusCase.id, out var capture))
                {
                    return Fail(
                        $"Missing corpus capture '{corpusCase.id}'.",
                        out failure);
                }

                if (!ValidateFrozenCapture(capture, out failure))
                    return false;
            }

            if (captureById.Count != ReferencedPathTracingV1Corpus.Cases.Count)
                return Fail("Unexpected captures are present in the V1 corpus.", out failure);

            failure = string.Empty;
            return true;
        }

        internal static ReferencedPathTracingCaptureMetadata BuildMetadata(
            string corpusCaseId,
            ContextContainer frameData,
            ulong accumulatedSampleCount,
            int unsupportedMaterialCount)
        {
            var cameraData = frameData?.Get<VividCameraData>();
            var pathTracingData =
                frameData?.GetOrCreate<VividReferencedPathTracingData>();
            var integratorState =
                ReferencedPathTracingIntegratorState.Resolve();
            var lightData = frameData?.GetOrCreate<VividLightData>();
            var lightDatabase = VividLightRenderDatabase.instance;
            lightDatabase.CompleteSceneLightPrepare();
            var lightListBuild = ReferencedPathTracingLightListBuilder.Build(
                lightDatabase.sceneLightData);
            var localLightCount = 0;
            for (var lightIndex = 0;
                 lightIndex < lightListBuild.records.Length;
                 lightIndex++)
            {
                if (lightListBuild.records[lightIndex].lightType
                    != (uint)ReferencedPathTracingLightType.Directional)
                {
                    localLightCount++;
                }
            }
            var width = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualWidth ?? 0,
                cameraData?.pixelWidth ?? 0,
                Screen.width);
            var height = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualHeight ?? 0,
                cameraData?.pixelHeight ?? 0,
                Screen.height);

            return new ReferencedPathTracingCaptureMetadata
            {
                freezeContractVersion = ContractVersion,
                corpusVersion = ReferencedPathTracingV1Corpus.Version,
                integratorVersion = ReferencedPathTracingIntegratorState.Version,
                corpusCaseId = corpusCaseId,
                width = width,
                height = height,
                targetSampleCount = integratorState.targetSampleCount,
                accumulatedSampleCount = accumulatedSampleCount,
                deterministicSampling =
                    integratorState.deterministicSampling,
                fixedSeed = integratorState.fixedSeed,
                pathSamplingMode = pathTracingData?.isValid == true
                    ? pathTracingData.pathSamplingMode
                    : integratorState.pathSamplingMode,
                samplingContractVersion =
                    pathTracingData?.isValid == true
                        ? pathTracingData.samplingContractVersion
                        : 0,
                physicalCameraContractVersion =
                    ReferencedPathTracingPhysicalCameraState.Version,
                usesPhysicalCameraDof =
                    pathTracingData?.isValid == true
                    && pathTracingData.physicalCameraDofEnabled,
                shadingNormalContractVersion =
                    ReferencedPathTracingShadingNormalContract.Version,
                thinWalledTransmissionContractVersion =
                    ReferencedPathTracingThinWalledTransmissionContract
                        .Version,
                coloredOpacityContractVersion =
                    ReferencedPathTracingGeometryOpacityContract.Version,
                maxBounceCount = integratorState.maxBounceCount,
                russianRouletteStartBounce =
                    integratorState.russianRouletteStartBounce,
                integratorSignature =
                    pathTracingData?.integratorSignature
                    ?? integratorState.signature,
                estimatorMode = integratorState.estimatorMode,
                transportDebugMode =
                    ReferencedPathTracingTransportDebugMode.Combined,
                usesShadingPointLightSelection =
                    integratorState.shadingPointLightSelection,
                globalLightProposalProbability =
                    integratorState.globalLightProposalProbability,
                usesLightSpatialIndex =
                    integratorState.lightSpatialIndex,
                lightSpatialIndexVersion =
                    (int)ReferencedPathTracingLightSpatialIndexBuilder.Version,
                lightSpatialIndexResolution =
                    ReferencedPathTracingLightSpatialIndexBuilder
                        .GridResolution,
                lightSpatialIndexCellCapacity =
                    ReferencedPathTracingLightSpatialIndexBuilder
                        .CellCapacity,
                usesReGIR = integratorState.enableReGIR,
                usesDenoiser = false,
                usesRasterGI = false,
                rawRadianceIsPreExposed = false,
                hasMainDirectionalLight =
                    lightData?.hasMainDirectionalLight == true,
                localLightCount = localLightCount,
                unsupportedMaterialCount =
                    Mathf.Max(0, unsupportedMaterialCount),
                standardLitOnly = unsupportedMaterialCount <= 0,
                imageOriginBottomLeft = true,
                environment =
                    ReferencedPathTracingEnvironmentMetadata.Capture(
                        frameData),
                validation = new ReferencedPathTracingValidationEvidence
                {
                    status = ReferencedPathTracingValidationStatus.NotRun,
                    graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                    deviceName = SystemInfo.graphicsDeviceName,
                    notes =
                        "Pending canonical GPU comparison and metric approval."
                },
                transportConformance =
                    new ReferencedPathTracingTransportConformanceEvidence
                {
                    status = ReferencedPathTracingValidationStatus.NotRun,
                    notes =
                        "Pending estimator-mean, proposal on/off, light-selection, PDF-consistency, and shading-normal grazing-angle validation."
                }
            };
        }

        private static bool Fail(string message, out string failure)
        {
            failure = message;
            return false;
        }

        private static bool ValidateLightProposalEvidenceContract(
            ReferencedPathTracingCaptureMetadata metadata,
            out string failure)
        {
            var measurements =
                metadata.transportConformance?.lightProposalMeasurements;
            if (measurements == null)
            {
                return Fail(
                    "Light-proposal evidence is missing.",
                    out failure);
            }

            for (var index = 0; index < measurements.Length; index++)
            {
                var measurement = measurements[index];
                if (measurement == null
                    || !measurement.shadingPointSelectionEnabled)
                {
                    continue;
                }

                if (Math.Abs(
                        measurement.globalProposalProbability
                        - metadata.globalLightProposalProbability) <= 1e-6f)
                {
                    failure = string.Empty;
                    return true;
                }

                break;
            }

            return Fail(
                "Light-proposal evidence does not match the capture settings.",
                out failure);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
                return false;

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if ((character < '0' || character > '9')
                    && (character < 'a' || character > 'f')
                    && (character < 'A' || character > 'F'))
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal static class ReferencedPathTracingStableHash
    {
        internal const ulong OffsetBasis = 14695981039346656037ul;
        private const ulong Prime = 1099511628211ul;

        internal static void Add(ref ulong hash, bool value)
        {
            Add(ref hash, value ? 1u : 0u);
        }

        internal static void Add(ref ulong hash, int value)
        {
            Add(ref hash, unchecked((uint)value));
        }

        internal static void Add(ref ulong hash, float value)
        {
            Add(ref hash, unchecked((uint)value.GetHashCode()));
        }

        internal static void Add(ref ulong hash, ulong value)
        {
            Add(ref hash, unchecked((uint)value));
            Add(ref hash, unchecked((uint)(value >> 32)));
        }

        private static void Add(ref ulong hash, uint value)
        {
            hash ^= value;
            hash *= Prime;
        }
    }
}
