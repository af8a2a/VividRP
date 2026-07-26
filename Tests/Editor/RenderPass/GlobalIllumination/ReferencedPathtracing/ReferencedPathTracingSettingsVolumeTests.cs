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
                Assert.That(volume.maxBounceCount.value, Is.EqualTo(4));
                Assert.That(
                    volume.russianRouletteStartBounce.value,
                    Is.EqualTo(3));
                Assert.That(volume.enableReGIR.value, Is.True);
                Assert.That(
                    volume.enableShaderExecutionReordering.value,
                    Is.False);
                Assert.That(volume.targetSampleCount.value, Is.EqualTo(2048));
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
                    volume.environmentDebugMode.value,
                    Is.EqualTo(ReferencedPathTracingEnvironmentDebugMode.Combined));
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
                volume.maxBounceCount.value = 6;
                volume.russianRouletteStartBounce.value = 5;
                volume.enableReGIR.value = false;
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

                Assert.That(original.deterministicSampling, Is.True);
                Assert.That(original.fixedSeed, Is.EqualTo(12345));
                Assert.That(original.maxBounceCount, Is.EqualTo(6));
                Assert.That(
                    original.russianRouletteStartBounce,
                    Is.EqualTo(5));
                Assert.That(original.enableReGIR, Is.False);
                Assert.That(
                    original.enableShaderExecutionReordering,
                    Is.False);
                Assert.That(
                    shaderExecutionReorderingChanged
                        .enableShaderExecutionReordering,
                    Is.True);
                Assert.That(original.targetSampleCount, Is.EqualTo(1024));
                Assert.That(
                    ReferencedPathTracingIntegratorState.Version,
                    Is.EqualTo(2));
                Assert.That(
                    captureTargetChanged.signature,
                    Is.EqualTo(original.signature));
                Assert.That(
                    shaderExecutionReorderingChanged.signature,
                    Is.EqualTo(original.signature));
                Assert.That(
                    seedChanged.signature,
                    Is.Not.EqualTo(original.signature));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }
    }
}
