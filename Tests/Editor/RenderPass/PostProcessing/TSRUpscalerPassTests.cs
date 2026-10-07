using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class TSRUpscalerPassTests
    {
        [Test]
        public void QualityMode_MapsToExpectedRenderSize()
        {
            Assert.That(TSRUpscalerUtility.GetUpscaleRatio(VividTsrQualityMode.NativeAA), Is.EqualTo(1.0f));
            Assert.That(TSRUpscalerUtility.GetUpscaleRatio(VividTsrQualityMode.Quality), Is.EqualTo(1.5f));
            Assert.That(TSRUpscalerUtility.GetUpscaleRatio(VividTsrQualityMode.Balanced), Is.EqualTo(1.7f));
            Assert.That(TSRUpscalerUtility.GetUpscaleRatio(VividTsrQualityMode.Performance), Is.EqualTo(2.0f));
            Assert.That(TSRUpscalerUtility.GetUpscaleRatio(VividTsrQualityMode.UltraPerformance), Is.EqualTo(3.0f));

            Assert.That(
                TSRUpscalerUtility.ResolveRenderSize(3840, 2160, VividTsrQualityMode.NativeAA),
                Is.EqualTo(new Vector2Int(3840, 2160)));
            Assert.That(
                TSRUpscalerUtility.ResolveRenderSize(3840, 2160, VividTsrQualityMode.Quality),
                Is.EqualTo(new Vector2Int(2560, 1440)));
            Assert.That(
                TSRUpscalerUtility.ResolveRenderSize(3840, 2160, VividTsrQualityMode.Balanced),
                Is.EqualTo(new Vector2Int(2259, 1271)));
            Assert.That(
                TSRUpscalerUtility.ResolveRenderSize(3840, 2160, VividTsrQualityMode.Performance),
                Is.EqualTo(new Vector2Int(1920, 1080)));
            Assert.That(
                TSRUpscalerUtility.ResolveRenderSize(3840, 2160, VividTsrQualityMode.UltraPerformance),
                Is.EqualTo(new Vector2Int(1280, 720)));
        }

        [Test]
        public void Jitter_UsesOutputScalePhaseCountAndHaltonOffset()
        {
            Assert.That(TSRUpscalerUtility.GetJitterPhaseCount(1920, 3840), Is.EqualTo(32));

            var offset = TSRUpscalerUtility.GetJitterOffset(0, 32);
            Assert.That(offset.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(offset.y, Is.EqualTo(-1.0f / 6.0f).Within(0.0001f));
        }

        [Test]
        public void ConfigureColorDescriptor_ReusesDescriptorInstance()
        {
            var descriptor = new RenderGraphTextureDesc();

            var configured = TSRUpscalerPass.ConfigureColorDescriptor(
                descriptor,
                "TSR_TestDescriptor",
                1920,
                1080,
                GraphicsFormat.R16G16_SFloat);

            Assert.That(configured, Is.SameAs(descriptor));
            Assert.That(descriptor.Name, Is.EqualTo("TSR_TestDescriptor"));
            Assert.That(descriptor.Width, Is.EqualTo(1920));
            Assert.That(descriptor.Height, Is.EqualTo(1080));
            Assert.That(descriptor.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16_SFloat));
            Assert.That(descriptor.EnableRandomWrite, Is.True);
            Assert.That(descriptor.AnisoLevel, Is.EqualTo(1));
        }

        [Test]
        public void ShadingGuideDescriptors_StableConfigurationDoesNotAllocate()
        {
            var input = new RenderGraphTextureDesc();
            var history = new RenderGraphTextureDesc();
            for (int i = 0; i < 32; i++)
            {
                TSRUpscalerPass.ConfigureColorDescriptor(input, "TSR_InputShadingGuide", 1920, 1080, GraphicsFormat.R16G16B16A16_SFloat);
                TSRUpscalerPass.ConfigureColorDescriptor(history, "TSR_HistoryShadingGuide", 1920, 1080, GraphicsFormat.R16G16B16A16_SFloat);
            }
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                TSRUpscalerPass.ConfigureColorDescriptor(input, "TSR_InputShadingGuide", 1920, 1080, GraphicsFormat.R16G16B16A16_SFloat);
                TSRUpscalerPass.ConfigureColorDescriptor(history, "TSR_HistoryShadingGuide", 1920, 1080, GraphicsFormat.R16G16B16A16_SFloat);
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [TestCase(nameof(VividRPCoreResources.TSRDilateVelocityCompute))]
        [TestCase(nameof(VividRPCoreResources.TSRReprojectHistoryCompute))]
        [TestCase(nameof(VividRPCoreResources.TSRRejectShadingCompute))]
        [TestCase(nameof(VividRPCoreResources.TSRSpatialAntiAliasingCompute))]
        [TestCase(nameof(VividRPCoreResources.TSRUpdateHistoryCompute))]
        [TestCase(nameof(VividRPCoreResources.TSRResolveHistoryCompute))]
        [TestCase(nameof(VividRPCoreResources.TSRSharpenCompute))]
        public void ShaderKeywords_StableLookupAndRecordingDoNotAllocate(string resourceField)
        {
            var shader = CloneTsrShader(resourceField);
            using var cmd = new CommandBuffer();
            var cache = new TSRUpscalerPass.ShaderKeywordCache();
            try
            {
                var keywords = cache.Get(shader);
                Assert.That(keywords.WaveOps.isValid, Is.True);
                Assert.That(keywords.PairedGuides.isValid,
                    Is.EqualTo(resourceField == nameof(VividRPCoreResources.TSRRejectShadingCompute)));
                for (int i = 0; i < 32; i++)
                {
                    cache.Get(shader).Set(cmd, shader, (i & 1) != 0, (i & 2) != 0);
                    cmd.Clear();
                }

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++)
                {
                    cache.Get(shader).Set(cmd, shader, (i & 1) != 0, (i & 2) != 0);
                    cmd.Clear();
                }
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero, "Stable TSR keyword lookup and command recording must not allocate.");
            }
            finally
            {
                cache.Clear();
                Object.DestroyImmediate(shader);
            }
        }

        [Test]
        public void ShaderKeywords_PreserveToggleStateAndShaderOwnership()
        {
            var first = CloneTsrShader(nameof(VividRPCoreResources.TSRRejectShadingCompute));
            var second = Object.Instantiate(first);
            using var cmd = new CommandBuffer();
            var cache = new TSRUpscalerPass.ShaderKeywordCache();
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    bool waveOps = (i & 1) != 0;
                    bool pairedGuides = (i & 2) != 0;
                    var firstKeywords = cache.Get(first);
                    var secondKeywords = cache.Get(second);
                    Assert.That(firstKeywords.Space, Is.EqualTo(first.keywordSpace));
                    Assert.That(secondKeywords.Space, Is.EqualTo(second.keywordSpace));
                    firstKeywords.Set(cmd, first, waveOps, pairedGuides);
                    secondKeywords.Set(cmd, second, !waveOps, !pairedGuides);
                    Graphics.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    Assert.That(first.IsKeywordEnabled(firstKeywords.WaveOps), Is.EqualTo(waveOps));
                    Assert.That(first.IsKeywordEnabled(firstKeywords.PairedGuides), Is.EqualTo(pairedGuides));
                    Assert.That(second.IsKeywordEnabled(secondKeywords.WaveOps), Is.EqualTo(!waveOps));
                    Assert.That(second.IsKeywordEnabled(secondKeywords.PairedGuides), Is.EqualTo(!pairedGuides));
                    cache.Clear();
                }

                var missing = cache.Get(null);
                Assert.That(missing.WaveOps.isValid, Is.False);
                Assert.That(missing.PairedGuides.isValid, Is.False);
                missing.Set(cmd, null, true, true);
            }
            finally
            {
                cache.Clear();
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        private static ComputeShader CloneTsrShader(string resourceField)
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var source = (ComputeShader)typeof(VividRPCoreResources).GetField(resourceField).GetValue(resources);
            Assert.That(source, Is.Not.Null, resourceField);
            return Object.Instantiate(source);
        }

        [Test]
        public void PersistentHistory_RetainsIndependentFramesAndResetsOnCameraCut()
        {
            var go = new GameObject("TSR.PersistentHistory.Tests");
            var camera = go.AddComponent<Camera>();
            var history = camera.GetVividCameraHistory();
            var state = new TSRUpscalerPass.CameraState();
            var size = new Vector2Int(8, 8);
            UnityEngine.Rendering.RTHandle firstSlot = null;
            try
            {
                for (int frame = 0; frame < 70; frame++)
                {
                    history.BeginFrame(8, 8);
                    bool reset = state.Prepare(camera, size, size, VividTsrQualityMode.NativeAA, 16, frame, false);
                    Assert.That(reset, Is.EqualTo(frame == 0));
                    if (frame == 0) firstSlot = state.PersistentColor[0].GetCurrent();
                    Assert.That(state.PersistentColor[0].GetCurrent(), Is.SameAs(firstSlot));
                    Assert.That(state.CanResurrect, Is.EqualTo(frame > 1));
                    if (frame == 62)
                    {
                        Assert.That(state.PersistentReadSlot, Is.EqualTo(1));
                        Assert.That(state.PersistentViewProjection[1].m03, Is.EqualTo(31));
                    }
                    int store = state.PersistentStoreSlot;
                    Assert.That(store, Is.EqualTo(frame % 31 == 0 ? frame / 31 % 2 : -1));
                    if (store >= 0) state.StorePersistentTransform(store, Matrix4x4.Translate(new Vector3(frame, 0, 0)), Vector2.zero);
                    state.MarkHistoryWritten(); history.CommitFrame();
                }
                history.BeginFrame(8, 8);
                Assert.That(state.Prepare(camera, size, size, VividTsrQualityMode.NativeAA, 16, 70, true), Is.True);
                Assert.That(state.CanResurrect, Is.False);
                Assert.That(state.PersistentStoreSlot, Is.Zero);
            }
            finally
            {
                history.AbortFrame(); state.Dispose(); CameraHistorySystem.Dispose(); Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CameraState_UsesCameraHistoryAndPreservesValidFrame()
        {
            var cameraObject = new GameObject("TSRCameraHistoryTests.Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var history = camera.GetVividCameraHistory();
            var state = new TSRUpscalerPass.CameraState();

            try
            {
                history.BeginFrame(8, 8);
                Assert.That(
                    state.Prepare(
                        camera,
                        new Vector2Int(8, 8),
                        new Vector2Int(16, 16),
                        VividTsrQualityMode.Balanced,
                        16,
                        1,
                        false),
                    Is.True);
                Assert.That(
                    history.TryGetTexture(CameraHistoryIds.TsrHistoryColor, out var historyColor),
                    Is.True);
                Assert.That(historyColor.FrameCount, Is.EqualTo(2));
                Assert.That(
                    history.TryGetTexture(CameraHistoryIds.TsrResurrectionMeta, out var resurrectionMeta),
                    Is.True);
                Assert.That(resurrectionMeta.FrameCount, Is.EqualTo(2));

                state.MarkHistoryWritten();
                history.CommitFrame();
                history.BeginFrame(8, 8);

                Assert.That(
                    state.Prepare(
                        camera,
                        new Vector2Int(8, 8),
                        new Vector2Int(16, 16),
                        VividTsrQualityMode.Balanced,
                        16,
                        2,
                        false),
                    Is.False);
                Assert.That(state.Prepare(camera, new Vector2Int(8, 8), new Vector2Int(16, 16),
                    VividTsrQualityMode.Balanced, 16, 2, false, true), Is.True,
                    "Enabling paired guides must discard history from the previous comparison mode.");
                Assert.That(history.TryGetTexture(CameraHistoryIds.TsrShadingGuide, out var shadingGuide), Is.True);
                Assert.That(shadingGuide.FrameCount, Is.EqualTo(2));
                Assert.That(shadingGuide.GetCurrent().rt.width, Is.EqualTo(8));
                state.MarkHistoryWritten();
                history.CommitFrame();
                history.BeginFrame(8, 8);
                Assert.That(state.Prepare(camera, new Vector2Int(8, 8), new Vector2Int(16, 16),
                    VividTsrQualityMode.Balanced, 16, 3, false, true), Is.False);
                var renderSize = new Vector2Int(8, 8);
                var outputSize = new Vector2Int(16, 16);
                for (int i = 0; i < 32; i++)
                    state.Prepare(camera, renderSize, outputSize, VividTsrQualityMode.Balanced, 16, 3, false, true);
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++)
                    state.Prepare(camera, renderSize, outputSize, VividTsrQualityMode.Balanced, 16, 3, false, true);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero, "Stable Guide history preparation must reuse owned resources.");
                Assert.That(state.Prepare(camera, new Vector2Int(8, 8), new Vector2Int(16, 16),
                    VividTsrQualityMode.Balanced, 16, 3, false, false), Is.True,
                    "Disabling paired guides must also discard experiment history.");
            }
            finally
            {
                history.AbortFrame();
                state.Dispose();
                CameraHistorySystem.Dispose();
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
