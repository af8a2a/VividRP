using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingSettingsVolumeTests
    {
        [Test]
        public void Defaults_EnableHdriLightingVisibilityImportanceSamplingAndMis()
        {
            var volume = ScriptableObject.CreateInstance<ReferencedPathTracingSettingsVolume>();

            try
            {
                Assert.That(volume.deterministicSampling.value, Is.False);
                Assert.That(volume.fixedSeed.value, Is.EqualTo(0x13579B));
                Assert.That(
                    volume.pathSamplingMode.value,
                    Is.EqualTo(
                        ReferencedPathTracingSamplingMode.IndexedBnd));
                Assert.That(volume.maxBounceCount.value, Is.EqualTo(4));
                Assert.That(
                    volume.russianRouletteStartBounce.value,
                    Is.EqualTo(3));
                Assert.That(volume.enableReGIR.value, Is.True);
                Assert.That(
                    volume.shadingPointLightSelection.value,
                    Is.True);
                Assert.That(
                    volume.globalLightProposalProbability.value,
                    Is.EqualTo(0.25f));
                Assert.That(volume.lightSpatialIndex.value, Is.True);
                Assert.That(
                    volume.enableShaderExecutionReordering.value,
                    Is.False);
                Assert.That(volume.targetSampleCount.value, Is.EqualTo(2048));
                Assert.That(
                    volume.environmentMode.value,
                    Is.EqualTo(ReferencedPathTracingEnvironmentMode.Hdri));
                Assert.That(volume.environmentLighting.value, Is.True);
                Assert.That(volume.environmentCameraVisible.value, Is.True);
                Assert.That(
                    volume.environmentSamplingMode.value,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling));
                Assert.That(
                    volume.environmentEstimatorMode.value,
                    Is.EqualTo(ReferencedPathTracingEnvironmentEstimatorMode.Mis));
                Assert.That(
                    volume.referenceAtmosphereCameraVisible.value,
                    Is.True);
                Assert.That(volume.referenceAtmosphereHoldout.value, Is.False);
                Assert.That(volume.referenceClouds.value, Is.False);
                Assert.That(volume.referenceCloudsCameraVisible.value, Is.True);
                Assert.That(volume.referenceCloudsHoldout.value, Is.False);
                Assert.That(volume.referenceGroundCameraVisible.value, Is.True);
                Assert.That(volume.referenceGroundHoldout.value, Is.False);
                Assert.That(
                    typeof(ReferencedPathTracingSettingsVolume)
                        .GetField("transportDebugMode"),
                    Is.Null);
                Assert.That(
                    typeof(ReferencedPathTracingSettingsVolume)
                        .GetField("environmentDebugMode"),
                    Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void IntegratorState_TracksCanonicalSettingsButNotCaptureTargetInSignature()
        {
            var volume =
                ScriptableObject.CreateInstance<ReferencedPathTracingSettingsVolume>();

            try
            {
                volume.active = true;
                volume.deterministicSampling.value = true;
                volume.fixedSeed.value = 12345;
                volume.pathSamplingMode.value =
                    ReferencedPathTracingSamplingMode.IndexedBnd;
                volume.maxBounceCount.value = 6;
                volume.russianRouletteStartBounce.value = 5;
                volume.enableReGIR.value = false;
                volume.shadingPointLightSelection.value = true;
                volume.globalLightProposalProbability.value = 0.25f;
                volume.lightSpatialIndex.value = true;
                volume.enableShaderExecutionReordering.value = false;
                volume.targetSampleCount.value = 1024;
                var original =
                    ReferencedPathTracingIntegratorState.Resolve(volume);

                volume.targetSampleCount.value = 4096;
                var captureTargetChanged =
                    ReferencedPathTracingIntegratorState.Resolve(volume);
                volume.enableShaderExecutionReordering.value = true;
                var shaderExecutionReorderingChanged =
                    ReferencedPathTracingIntegratorState.Resolve(volume);
                volume.fixedSeed.value = 12346;
                var seedChanged =
                    ReferencedPathTracingIntegratorState.Resolve(volume);
                volume.fixedSeed.value = 12345;
                volume.environmentEstimatorMode.value =
                    ReferencedPathTracingEnvironmentEstimatorMode.LightOnly;
                var estimatorChanged =
                    ReferencedPathTracingIntegratorState.Resolve(volume);
                volume.environmentEstimatorMode.value =
                    ReferencedPathTracingEnvironmentEstimatorMode.Mis;
                volume.shadingPointLightSelection.value = false;
                var lightProposalModeChanged =
                    ReferencedPathTracingIntegratorState.Resolve(volume);
                volume.shadingPointLightSelection.value = true;
                volume.globalLightProposalProbability.value = 0.5f;
                var lightProposalProbabilityChanged =
                    ReferencedPathTracingIntegratorState.Resolve(volume);
                volume.globalLightProposalProbability.value = 0.25f;
                volume.lightSpatialIndex.value = false;
                var lightSpatialIndexChanged =
                    ReferencedPathTracingIntegratorState.Resolve(volume);
                volume.lightSpatialIndex.value = true;
                volume.pathSamplingMode.value =
                    ReferencedPathTracingSamplingMode.IndexedHash;
                var pathSamplingModeChanged =
                    ReferencedPathTracingIntegratorState.Resolve(volume);
                volume.pathSamplingMode.value =
                    (ReferencedPathTracingSamplingMode)999;
                var invalidPathSamplingMode =
                    ReferencedPathTracingIntegratorState.Resolve(volume);

                Assert.That(original.deterministicSampling, Is.True);
                Assert.That(original.fixedSeed, Is.EqualTo(12345));
                Assert.That(
                    original.pathSamplingMode,
                    Is.EqualTo(
                        ReferencedPathTracingSamplingMode.IndexedBnd));
                Assert.That(original.maxBounceCount, Is.EqualTo(6));
                Assert.That(
                    original.russianRouletteStartBounce,
                    Is.EqualTo(5));
                Assert.That(original.enableReGIR, Is.False);
                Assert.That(
                    original.shadingPointLightSelection,
                    Is.True);
                Assert.That(
                    original.globalLightProposalProbability,
                    Is.EqualTo(0.25f));
                Assert.That(original.lightSpatialIndex, Is.True);
                Assert.That(
                    original.enableShaderExecutionReordering,
                    Is.False);
                Assert.That(
                    original.estimatorMode,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentEstimatorMode.Mis));
                Assert.That(
                    shaderExecutionReorderingChanged
                        .enableShaderExecutionReordering,
                    Is.True);
                Assert.That(original.targetSampleCount, Is.EqualTo(1024));
                Assert.That(
                    ReferencedPathTracingIntegratorState.Version,
                    Is.EqualTo(8));
                Assert.That(
                    captureTargetChanged.signature,
                    Is.EqualTo(original.signature));
                Assert.That(
                    shaderExecutionReorderingChanged.signature,
                    Is.EqualTo(original.signature));
                Assert.That(
                    seedChanged.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    estimatorChanged.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    lightProposalModeChanged.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    lightProposalProbabilityChanged.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    lightSpatialIndexChanged.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    pathSamplingModeChanged.pathSamplingMode,
                    Is.EqualTo(
                        ReferencedPathTracingSamplingMode.IndexedHash));
                Assert.That(
                    pathSamplingModeChanged.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    invalidPathSamplingMode.pathSamplingMode,
                    Is.EqualTo(
                        ReferencedPathTracingSamplingMode.IndexedBnd));
                Assert.That(
                    invalidPathSamplingMode.signature,
                    Is.EqualTo(original.signature));
                Assert.That(
                    original.ResolveEffectiveSignature(
                        ReferencedPathTracingSamplingMode.IndexedBnd),
                    Is.Not.EqualTo(
                        original.ResolveEffectiveSignature(
                            ReferencedPathTracingSamplingMode.IndexedHash)));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void ShadingNormalContract_KeepsOpaqueReflectionAboveGeometry()
        {
            var viewDirection = new Vector3(0.995f, 0.1f, 0.0f).normalized;
            var geometricNormal = Vector3.up;
            var shadingNormal = new Vector3(1.0f, 0.01f, 0.0f).normalized;

            var consistentNormal =
                ReferencedPathTracingShadingNormalContract
                    .ComputeConsistentNormal(
                        viewDirection,
                        geometricNormal,
                        shadingNormal);
            var reflectedDirection = Vector3.Reflect(
                -viewDirection,
                consistentNormal);

            Assert.That(
                Vector3.Dot(consistentNormal, viewDirection),
                Is.GreaterThan(0.0f));
            Assert.That(
                Vector3.Dot(consistentNormal, geometricNormal),
                Is.GreaterThan(0.0f));
            Assert.That(
                Vector3.Dot(reflectedDirection, geometricNormal),
                Is.GreaterThanOrEqualTo(
                    ReferencedPathTracingShadingNormalContract
                        .ReflectionHorizonEpsilon
                    - 1e-5f));
        }

        [Test]
        public void ShadingNormalContract_SoftensOnlyDivergentDiffuseTerminator()
        {
            var aligned =
                ReferencedPathTracingShadingNormalContract
                    .EvaluateDiffuseShadowTerminator(
                        Vector3.up,
                        Vector3.up,
                        Vector3.up);
            var divergent =
                ReferencedPathTracingShadingNormalContract
                    .EvaluateDiffuseShadowTerminator(
                        new Vector3(1.0f, 0.02f, 0.0f),
                        Vector3.up,
                        new Vector3(0.8f, 0.6f, 0.0f));

            Assert.That(aligned, Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(divergent, Is.InRange(0.0f, 1.0f));
            Assert.That(divergent, Is.LessThan(aligned));
        }

        [Test]
        public void SamplingContract_UsesStableNonOverlappingDimensions()
        {
            Assert.That(
                ReferencedPathTracingSamplingContract.Version,
                Is.EqualTo(3));
            Assert.That(
                ReferencedPathTracingSamplingContract.FilmDimension,
                Is.EqualTo(0));
            Assert.That(
                ReferencedPathTracingSamplingContract.LensDimension,
                Is.EqualTo(2));
            Assert.That(
                ReferencedPathTracingSamplingContract.BounceBaseDimension,
                Is.EqualTo(8));
            Assert.That(
                ReferencedPathTracingSamplingContract.BounceDimensionStride,
                Is.EqualTo(16));
            Assert.That(
                ReferencedPathTracingSamplingContract
                    .AtmosphereSunDimensionOffset,
                Is.EqualTo(12));
            Assert.That(
                ReferencedPathTracingSamplingContract.FutureDimensionOffset,
                Is.EqualTo(14));

            var usedDimensions = new System.Collections.Generic.HashSet<int>();
            for (var bounceIndex = 0;
                 bounceIndex
                    < ReferencedPathTracingSettingsVolume
                        .MaximumSupportedBounceCount;
                 bounceIndex++)
            {
                for (var offset = 0;
                     offset
                        < ReferencedPathTracingSamplingContract
                            .BounceDimensionStride;
                     offset++)
                {
                    Assert.That(
                        usedDimensions.Add(
                            ReferencedPathTracingSamplingContract
                                .GetBounceDimension(
                                    bounceIndex,
                                    offset)),
                        Is.True);
                }
            }

            Assert.That(
                ReferencedPathTracingSamplingContract
                    .MaximumUsedDimension,
                Is.LessThan(
                    ReferencedPathTracingSamplingContract
                        .DimensionCapacity));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => ReferencedPathTracingSamplingContract
                    .GetBounceDimension(-1, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => ReferencedPathTracingSamplingContract
                    .GetBounceDimension(0, 16));
        }

        [Test]
        public void LightProposalPolicy_PreservesSupportAndEvaluatesMixturePdf()
        {
            Assert.That(
                ReferencedPathTracingLightProposalPolicy
                    .ResolveGlobalProposalProbability(
                        false,
                        0.25f,
                        4.0f,
                        2.0f),
                Is.EqualTo(1.0f));
            Assert.That(
                ReferencedPathTracingLightProposalPolicy
                    .ResolveGlobalProposalProbability(
                        true,
                        0.25f,
                        4.0f,
                        0.0f),
                Is.EqualTo(1.0f));
            Assert.That(
                ReferencedPathTracingLightProposalPolicy
                    .ResolveGlobalProposalProbability(
                        true,
                        float.NaN,
                        4.0f,
                        2.0f),
                Is.EqualTo(0.25f));
            Assert.That(
                ReferencedPathTracingLightProposalPolicy
                    .ResolveGlobalProposalProbability(
                        true,
                        0.25f,
                        4.0f,
                        2.0f),
                Is.EqualTo(0.25f));
            Assert.That(
                ReferencedPathTracingLightProposalPolicy.EvaluateMixturePdf(
                    0.25f,
                    0.1f,
                    0.4f),
                Is.EqualTo(0.325f).Within(1e-6f));
            Assert.That(
                ReferencedPathTracingLightProposalPolicy.EvaluateMixturePdf(
                        0.25f,
                        0.1f,
                        0.4f)
                    + ReferencedPathTracingLightProposalPolicy
                        .EvaluateMixturePdf(
                            0.25f,
                            0.9f,
                            0.6f),
                Is.EqualTo(1.0f).Within(1e-6f));
        }

        [Test]
        public void EstimatorPolicy_PreservesOnlyReachableStrategiesInEveryMode()
        {
            Assert.That(
                ReferencedPathTracingEstimatorPolicy.IsNeeEligible(
                    ReferencedPathTracingEnvironmentEstimatorMode.Mis,
                    true),
                Is.True);
            Assert.That(
                ReferencedPathTracingEstimatorPolicy.IsNeeEligible(
                    ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly,
                    true),
                Is.False);
            Assert.That(
                ReferencedPathTracingEstimatorPolicy.IsNeeEligible(
                    ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly,
                    false),
                Is.True);

            Assert.That(
                ReferencedPathTracingEstimatorPolicy.GetLightEstimatorWeight(
                    ReferencedPathTracingEnvironmentEstimatorMode.Mis,
                    true,
                    false,
                    0.25f,
                    0.25f),
                Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(
                ReferencedPathTracingEstimatorPolicy.GetLightEstimatorWeight(
                    ReferencedPathTracingEnvironmentEstimatorMode.LightOnly,
                    true,
                    false,
                    0.25f,
                    0.25f),
                Is.EqualTo(1.0f));
            Assert.That(
                ReferencedPathTracingEstimatorPolicy.GetLightEstimatorWeight(
                    ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly,
                    true,
                    false,
                    0.25f,
                    0.25f),
                Is.Zero);
            Assert.That(
                ReferencedPathTracingEstimatorPolicy.GetLightEstimatorWeight(
                    ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly,
                    false,
                    true,
                    0.0f,
                    0.25f),
                Is.EqualTo(1.0f));

            Assert.That(
                ReferencedPathTracingEstimatorPolicy.GetBsdfEstimatorWeight(
                    ReferencedPathTracingEnvironmentEstimatorMode.Mis,
                    false,
                    0.25f,
                    0.25f),
                Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(
                ReferencedPathTracingEstimatorPolicy.GetBsdfEstimatorWeight(
                    ReferencedPathTracingEnvironmentEstimatorMode.LightOnly,
                    false,
                    0.25f,
                    0.25f),
                Is.Zero);
            Assert.That(
                ReferencedPathTracingEstimatorPolicy.GetBsdfEstimatorWeight(
                    ReferencedPathTracingEnvironmentEstimatorMode.LightOnly,
                    true,
                    0.25f,
                    0.25f),
                Is.EqualTo(1.0f));
            Assert.That(
                ReferencedPathTracingEstimatorPolicy.GetBsdfEstimatorWeight(
                    ReferencedPathTracingEnvironmentEstimatorMode.LightOnly,
                    false,
                    0.25f,
                    0.0f),
                Is.EqualTo(1.0f));
        }

        [Test]
        public void TransportConformanceGate_ValidatesMeansHistogramAndPdfReciprocity()
        {
            var evidence = CreateValidTransportConformanceEvidence();

            Assert.That(
                ReferencedPathTracingTransportConformanceGate.Validate(
                    evidence,
                    out var failure),
                Is.True,
                failure);

            evidence.estimatorMeasurements[1].meanLuminance = 1.2f;
            Assert.That(
                ReferencedPathTracingTransportConformanceGate.Validate(
                    evidence,
                    out failure),
                Is.False);
            Assert.That(failure, Does.Contain("means disagree"));

            evidence = CreateValidTransportConformanceEvidence();
            evidence.lightProposalMeasurements[1].meanLuminance = 1.2f;
            Assert.That(
                ReferencedPathTracingTransportConformanceGate.Validate(
                    evidence,
                    out failure),
                Is.False);
            Assert.That(failure, Does.Contain("light-proposal means disagree"));

            evidence = CreateValidTransportConformanceEvidence();
            evidence.lightSelection.observedSelectionCounts[0] = 9000;
            evidence.lightSelection.observedSelectionCounts[1] = 1000;
            Assert.That(
                ReferencedPathTracingTransportConformanceGate.Validate(
                    evidence,
                    out failure),
                Is.False);
            Assert.That(failure, Does.Contain("Light-selection bin"));

            evidence = CreateValidTransportConformanceEvidence();
            evidence.pdfConsistency.maximumRelativeError = 0.01f;
            Assert.That(
                ReferencedPathTracingTransportConformanceGate.Validate(
                    evidence,
                    out failure),
                Is.False);
            Assert.That(failure, Does.Contain("PDF consistency"));
        }

        private static ReferencedPathTracingTransportConformanceEvidence
            CreateValidTransportConformanceEvidence()
        {
            return new ReferencedPathTracingTransportConformanceEvidence
            {
                status = ReferencedPathTracingValidationStatus.Passed,
                estimatorMeasurements = new[]
                {
                    CreateEstimatorMeasurement(
                        ReferencedPathTracingEnvironmentEstimatorMode.Mis,
                        1.0f),
                    CreateEstimatorMeasurement(
                        ReferencedPathTracingEnvironmentEstimatorMode.LightOnly,
                        1.005f),
                    CreateEstimatorMeasurement(
                        ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly,
                        0.995f)
                },
                lightProposalMeasurements = new[]
                {
                    CreateLightProposalMeasurement(
                        false,
                        1.0f,
                        1.0f,
                        0.4f),
                    CreateLightProposalMeasurement(
                        true,
                        0.25f,
                        1.005f,
                        0.25f)
                },
                lightSelection = new ReferencedPathTracingLightSelectionEvidence
                {
                    sampleCount = 10000,
                    declaredSelectionPdfs = new[] { 0.25f, 0.75f },
                    observedSelectionCounts = new[] { 2500, 7500 }
                },
                pdfConsistency =
                    new ReferencedPathTracingPdfConsistencyEvidence
                {
                    comparisonCount = 10000,
                    nonFiniteCount = 0,
                    maximumRelativeError = 1e-6f
                }
            };
        }

        private static ReferencedPathTracingLightProposalMeasurement
            CreateLightProposalMeasurement(
                bool enabled,
                float globalProposalProbability,
                float meanLuminance,
                float luminanceVariance)
        {
            return new ReferencedPathTracingLightProposalMeasurement
            {
                shadingPointSelectionEnabled = enabled,
                globalProposalProbability = globalProposalProbability,
                sampleCount = 4096,
                meanLuminance = meanLuminance,
                standardError = 0.005f,
                luminanceVariance = luminanceVariance,
                finitePixelFraction = 1.0f,
                negativeRadianceFraction = 0.0f
            };
        }

        private static ReferencedPathTracingEstimatorMeasurement
            CreateEstimatorMeasurement(
                ReferencedPathTracingEnvironmentEstimatorMode mode,
                float meanLuminance)
        {
            return new ReferencedPathTracingEstimatorMeasurement
            {
                estimatorMode = mode,
                sampleCount = 4096,
                meanLuminance = meanLuminance,
                standardError = 0.005f,
                finitePixelFraction = 1.0f,
                negativeRadianceFraction = 0.0f
            };
        }
    }
}
