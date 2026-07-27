using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass.Core
{
    internal readonly struct ReferencedPathTracingIntegratorState
        : IEquatable<ReferencedPathTracingIntegratorState>
    {
        internal const int Version = 8;

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
        internal const int Version = 2;
        internal const int DimensionCapacity = 256;
        internal const int FilmDimension = 0;
        internal const int LensDimension = 2;
        internal const int CameraReservedDimension = 4;
        internal const int BounceBaseDimension = 8;
        internal const int BounceDimensionStride = 16;
        internal const int BsdfDimensionOffset = 0;
        internal const int NeeDimensionOffset = 3;
        internal const int RussianRouletteDimensionOffset = 6;
        internal const int StochasticAlphaDimensionOffset = 7;
        internal const int VolumeDimensionOffset = 8;
        internal const int FutureDimensionOffset = 12;
        internal const int MaximumUsedDimension =
            BounceBaseDimension
            + ReferencedPathTracingSettingsVolume.MaximumSupportedBounceCount
                * BounceDimensionStride
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

    internal static class ReferencedPathTracingFrameSignatureUtility
    {
        internal static ulong Compute(
            ContextContainer frameData,
            VividCameraData cameraData,
            int width,
            int height,
            ulong effectiveIntegratorSignature,
            ReferencedPathTracingEnvironmentState environmentState,
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
                cameraBackgroundState.signature);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                physicalCameraState.signature);

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
            internal ulong frameSignature;

            public override void Dispose()
            {
                hasSignature = false;
                resetRequested = false;
                lastRenderFrameIndex = -1;
                sampleIndex = 0;
                frameSignature = 0;
            }
        }

        private static readonly CameraRelativeSystem<SequenceState> s_States =
            new();

        internal static uint Resolve(
            Camera camera,
            int renderFrameIndex,
            ulong frameSignature,
            bool isFirstFrame)
        {
            if (camera == null)
                return 0;

            var state = s_States.GetOrCreateBase(camera);
            if (!state.hasSignature
                || state.resetRequested
                || state.frameSignature != frameSignature
                || isFirstFrame)
            {
                state.sampleIndex = 0;
                state.resetRequested = false;
            }
            else if (state.lastRenderFrameIndex != renderFrameIndex
                && state.sampleIndex < uint.MaxValue)
            {
                state.sampleIndex++;
            }

            state.hasSignature = true;
            state.lastRenderFrameIndex = renderFrameIndex;
            state.frameSignature = frameSignature;
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
        public const int ContractVersion = 7;
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
                        frameData?.GetOrCreate<VividSkyData>()),
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
