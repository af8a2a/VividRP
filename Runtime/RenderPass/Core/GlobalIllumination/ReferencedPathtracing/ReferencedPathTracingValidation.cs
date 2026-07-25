using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass.Core
{
    internal readonly struct ReferencedPathTracingIntegratorState
        : IEquatable<ReferencedPathTracingIntegratorState>
    {
        internal const int Version = 1;

        internal ReferencedPathTracingIntegratorState(
            bool deterministicSampling,
            int fixedSeed,
            int maxBounceCount,
            int russianRouletteStartBounce,
            bool enableReGIR,
            int targetSampleCount)
        {
            this.deterministicSampling = deterministicSampling;
            this.fixedSeed = Mathf.Max(0, fixedSeed);
            this.maxBounceCount = Mathf.Clamp(
                maxBounceCount,
                1,
                ReferencedPathTracingSettingsVolume.MaximumSupportedBounceCount);
            this.russianRouletteStartBounce = Mathf.Clamp(
                russianRouletteStartBounce,
                1,
                ReferencedPathTracingSettingsVolume.MaximumSupportedBounceCount);
            this.enableReGIR = enableReGIR;
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
            ReferencedPathTracingStableHash.Add(ref hash, this.maxBounceCount);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                this.russianRouletteStartBounce);
            ReferencedPathTracingStableHash.Add(ref hash, enableReGIR);
            signature = hash;
        }

        internal bool deterministicSampling { get; }
        internal int fixedSeed { get; }
        internal int maxBounceCount { get; }
        internal int russianRouletteStartBounce { get; }
        internal bool enableReGIR { get; }
        internal int targetSampleCount { get; }
        internal ulong signature { get; }

        internal static ReferencedPathTracingIntegratorState Resolve(
            ReferencedPathTracingSettingsVolume settings = null)
        {
            settings ??=
                VividVolumeManagerUtility.GetReferencedPathTracingSettingsVolume();
            var useVolumeSettings = settings != null && settings.active;
            return new ReferencedPathTracingIntegratorState(
                useVolumeSettings && settings.deterministicSampling.value,
                useVolumeSettings ? settings.fixedSeed.value : 0x13579B,
                useVolumeSettings ? settings.maxBounceCount.value : 4,
                useVolumeSettings
                    ? settings.russianRouletteStartBounce.value
                    : 3,
                !useVolumeSettings || settings.enableReGIR.value,
                useVolumeSettings ? settings.targetSampleCount.value : 2048);
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

    internal static class ReferencedPathTracingFrameSignatureUtility
    {
        internal static ulong Compute(
            ContextContainer frameData,
            VividCameraData cameraData,
            int width,
            int height,
            ReferencedPathTracingIntegratorState integratorState,
            ReferencedPathTracingEnvironmentState environmentState,
            ReferencedPathTracingCameraBackgroundState cameraBackgroundState)
        {
            var hash = ReferencedPathTracingStableHash.OffsetBasis;
            ReferencedPathTracingStableHash.Add(ref hash, width);
            ReferencedPathTracingStableHash.Add(ref hash, height);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                integratorState.signature);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                environmentState.signature);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                cameraBackgroundState.signature);

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
            if (!integratorState.enableReGIR)
                localLightSignature = 0ul;
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
        public int maxBounceCount;
        public int russianRouletteStartBounce;
        public ulong integratorSignature;
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
        public int maxBounceCount;
        public int russianRouletteStartBounce;
        public ReferencedPathTracingEnvironmentSamplingMode samplingMode;
        public ReferencedPathTracingEnvironmentEstimatorMode estimatorMode;

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
            maxBounceCount = 4;
            russianRouletteStartBounce = 3;
            samplingMode =
                ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling;
            estimatorMode =
                ReferencedPathTracingEnvironmentEstimatorMode.Mis;
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
        public const int ContractVersion = 1;
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

            if (metadata.maxBounceCount != corpusCase.maxBounceCount
                || metadata.russianRouletteStartBounce
                    != corpusCase.russianRouletteStartBounce)
            {
                return Fail("Integrator depth does not match the V1 corpus.", out failure);
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
                || metadata.environment.estimatorMode != corpusCase.estimatorMode)
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
            lightData?.CompleteReGIRPrepare();
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
                maxBounceCount = integratorState.maxBounceCount,
                russianRouletteStartBounce =
                    integratorState.russianRouletteStartBounce,
                integratorSignature =
                    pathTracingData?.integratorSignature
                    ?? integratorState.signature,
                usesReGIR = integratorState.enableReGIR,
                usesDenoiser = false,
                usesRasterGI = false,
                rawRadianceIsPreExposed = false,
                hasMainDirectionalLight =
                    lightData?.hasMainDirectionalLight == true,
                localLightCount = lightData != null
                    ? Mathf.Max(0, lightData.reGIRLightCount)
                    : 0,
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
                }
            };
        }

        private static bool Fail(string message, out string failure)
        {
            failure = message;
            return false;
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
