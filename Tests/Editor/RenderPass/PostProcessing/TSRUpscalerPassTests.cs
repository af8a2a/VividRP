using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
