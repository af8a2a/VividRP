using System;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class LumenReflectionTests
    {
        [Test]
        public void DefaultSettings_MatchUeLumenReflectionDefaults()
        {
            var settings = ScriptableObject.CreateInstance<LumenIndirectDiffuse>();
            try
            {
                Assert.That(settings.lumenReflections.value, Is.True);
                Assert.That(settings.lumenReflectionDownsampleFactor.value, Is.EqualTo(1));
                Assert.That(settings.lumenReflectionMaxRoughnessToTrace.value, Is.EqualTo(0.4f).Within(1e-6f));
                Assert.That(settings.lumenReflectionMaxRoughnessToTraceForFoliage.value, Is.EqualTo(0.2f).Within(1e-6f));
                Assert.That(settings.lumenReflectionRoughnessFadeLength.value, Is.EqualTo(0.1f).Within(1e-6f));
                Assert.That(settings.lumenReflectionSmoothBias.value, Is.EqualTo(0.0f).Within(1e-6f));
                Assert.That(settings.lumenReflectionGGXSamplingBias.value, Is.EqualTo(0.1f).Within(1e-6f));
                Assert.That(settings.lumenReflectionMaxRayIntensity.value, Is.EqualTo(40.0f).Within(1e-6f));
                Assert.That(settings.lumenReflectionScreenTraces.value, Is.True);
                Assert.That(settings.lumenReflectionMeshSdfTraces.value, Is.True);
                Assert.That(settings.lumenReflectionScreenSpaceReconstruction.value, Is.True);
                Assert.That(settings.lumenReflectionReconstructionSamples.value, Is.EqualTo(5));
                Assert.That(settings.lumenReflectionReconstructionKernelRadius.value, Is.EqualTo(8.0f).Within(1e-6f));
                Assert.That(settings.lumenReflectionTemporalFilter.value, Is.True);
                Assert.That(settings.lumenReflectionTemporalMaxFramesAccumulated.value, Is.EqualTo(12.0f).Within(1e-6f));
                Assert.That(settings.lumenReflectionTemporalNeighborhoodClampScale.value, Is.EqualTo(1.0f).Within(1e-6f));
                Assert.That(settings.lumenReflectionTemporalDistanceThreshold.value, Is.EqualTo(0.03f).Within(1e-6f));
                Assert.That(settings.lumenReflectionDenoiserTonemapRange.value, Is.EqualTo(10.0f).Within(1e-6f));
                Assert.That(settings.lumenReflectionBilateralFilter.value, Is.True);
                Assert.That(settings.lumenReflectionBilateralFilterKernelRadius.value, Is.EqualTo(8.0f).Within(1e-6f));
                Assert.That(settings.lumenReflectionBilateralFilterNumSamples.value, Is.EqualTo(4));
                Assert.That(settings.lumenReflectionBilateralFilterDepthWeightScale.value, Is.EqualTo(10000.0f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(0.0f, false, 1.0f)]
        [TestCase(0.25f, false, 1.0f)]
        [TestCase(0.30f, false, 1.0f)]
        [TestCase(0.35f, false, 0.5f)]
        [TestCase(0.40f, false, 0.0f)]
        [TestCase(0.80f, false, 0.0f)]
        [TestCase(0.05f, true, 1.0f)]
        [TestCase(0.15f, true, 0.5f)]
        [TestCase(0.20f, true, 0.0f)]
        [TestCase(0.30f, true, 0.0f)]
        public void CombineReflectionsAlpha_MatchesUeRoughnessTransition(
            float roughness, bool hasBackfaceDiffuse, float expectedAlpha)
        {
            float alpha = LumenReflectionPass.ComputeCombineReflectionsAlpha(
                roughness,
                hasBackfaceDiffuse,
                maxRoughnessToTrace: 0.4f,
                maxRoughnessToTraceForFoliage: 0.2f,
                invRoughnessFadeLength: 10.0f);

            Assert.That(alpha, Is.EqualTo(expectedAlpha).Within(1e-5f));
        }

        [Test]
        public void ApplySmoothBias_MatchesUeCubicSmoothstep()
        {
            Assert.That(LumenReflectionPass.ApplySmoothBias(0.5f, 0.0f), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(LumenReflectionPass.ApplySmoothBias(0.5f, -0.5f), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(LumenReflectionPass.ApplySmoothBias(0.0f, 1.0f), Is.EqualTo(0.0f).Within(1e-6f));
            Assert.That(LumenReflectionPass.ApplySmoothBias(1.0f, 1.0f), Is.EqualTo(1.0f).Within(1e-6f));

            // r = 0.5 -> r^3 * (3 - 2r) = 0.125 * 2 = 0.25
            Assert.That(LumenReflectionPass.ApplySmoothBias(0.5f, 1.0f), Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(LumenReflectionPass.ApplySmoothBias(0.5f, 0.5f), Is.EqualTo(0.375f).Within(1e-6f));

            // r = 0.2 -> 0.008 * 2.6 = 0.0208
            Assert.That(LumenReflectionPass.ApplySmoothBias(0.2f, 1.0f), Is.EqualTo(0.0208f).Within(1e-6f));
        }

        [TestCase(1920, 1080, 1, true, 1, 1920, 1080, 240, 135, 240, 135)]
        [TestCase(1920, 1080, 2, true, 2, 960, 540, 240, 135, 120, 68)]
        [TestCase(1920, 1080, 2, false, 1, 1920, 1080, 240, 135, 240, 135)]
        [TestCase(1281, 721, 2, true, 2, 641, 361, 161, 91, 81, 46)]
        [TestCase(1280, 720, 4, true, 2, 640, 360, 160, 90, 80, 45)]
        public void ComputeTracingAndTileDimensions_MatchesTileGridAndAllocatesZeroBytes(
            int width,
            int height,
            int requestedDownsample,
            bool supportsDownsampled,
            int expectedDownsample,
            int expectedTracingWidth,
            int expectedTracingHeight,
            int expectedResolveTilesX,
            int expectedResolveTilesY,
            int expectedTracingTilesX,
            int expectedTracingTilesY)
        {
            LumenReflectionPass.ComputeTracingAndTileDimensions(
                width,
                height,
                requestedDownsample,
                supportsDownsampled,
                out int downsample,
                out int tracingWidth,
                out int tracingHeight,
                out int resolveTilesX,
                out int resolveTilesY,
                out int tracingTilesX,
                out int tracingTilesY);

            Assert.That(downsample, Is.EqualTo(expectedDownsample));
            Assert.That(tracingWidth, Is.EqualTo(expectedTracingWidth));
            Assert.That(tracingHeight, Is.EqualTo(expectedTracingHeight));
            Assert.That(resolveTilesX, Is.EqualTo(expectedResolveTilesX));
            Assert.That(resolveTilesY, Is.EqualTo(expectedResolveTilesY));
            Assert.That(tracingTilesX, Is.EqualTo(expectedTracingTilesX));
            Assert.That(tracingTilesY, Is.EqualTo(expectedTracingTilesY));

            for (int i = 0; i < 32; i++)
            {
                LumenReflectionPass.ComputeTracingAndTileDimensions(
                    width, height, requestedDownsample, supportsDownsampled,
                    out _, out _, out _, out _, out _, out _, out _);
                _ = LumenReflectionPass.BuildCombineParameters(0.4f, 0.2f, 10.0f, true);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                LumenReflectionPass.ComputeTracingAndTileDimensions(
                    width, height, requestedDownsample, supportsDownsampled,
                    out _, out _, out _, out _, out _, out _, out _);
                _ = LumenReflectionPass.BuildCombineParameters(0.4f, 0.2f, 10.0f, true);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void PipelineResources_ProvideLumenReflectionComputeShadersAndAllKernels()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            Assert.That(resources, Is.Not.Null);
            Assert.That(resources.LumenReflectionsTracingCompute, Is.Not.Null);
            Assert.That(resources.LumenReflectionsDenoiserCompute, Is.Not.Null);

            string[] tracingKernels =
            {
                "InitReflectionIndirectArgsCS",
                "ReflectionTileClassifyCS",
                "ReflectionTileClassificationBuildListsCS",
                "ReflectionTileClassificationBuildListsDownsampledCS",
                "ReflectionClearUnusedTraceTileDataCS",
                "ReflectionGenerateRaysCS",
                "ReflectionClearTracesCS",
                "ReflectionTraceScreenTexturesCS",
                "SetupCompactionIndirectArgsCS",
                "ReflectionCompactTracesCS",
                "SetupCompactedTracesIndirectArgsCS",
                "ReflectionTraceMeshSDFsCS",
                "ReflectionTraceMeshSDFsFeedbackCS",
                "ReflectionTraceVoxelsCS",
                "ReflectionTraceVoxelsFeedbackCS",
            };
            foreach (string kernel in tracingKernels)
            {
                Assert.That(resources.LumenReflectionsTracingCompute.HasKernel(kernel), Is.True,
                    $"Missing tracing kernel: {kernel}");
            }

            string[] denoiserKernels =
            {
                "LumenReflectionDenoiserClearCS",
                "LumenReflectionResolveCS",
                "LumenReflectionResolveSpatialCS",
                "LumenReflectionResolveDownsampledCS",
                "LumenReflectionResolveDownsampledSpatialCS",
                "LumenReflectionDenoiserTemporalCS",
                "LumenReflectionDenoiserTemporalValidHistoryCS",
                "LumenReflectionDenoiserSpatialBypassCS",
                "LumenReflectionDenoiserSpatialCS",
                "LumenDebugReflectionCS",
            };
            foreach (string kernel in denoiserKernels)
            {
                Assert.That(resources.LumenReflectionsDenoiserCompute.HasKernel(kernel), Is.True,
                    $"Missing denoiser kernel: {kernel}");
            }
        }
    }
}
