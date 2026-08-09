using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class FSR3UpscalerPassTests
    {
        [Test]
        public void QualityMode_MapsToExpectedRenderSize()
        {
            Assert.That(FSR3UpscalerUtility.GetUpscaleRatio(VividFsr3QualityMode.NativeAA), Is.EqualTo(1.0f));
            Assert.That(FSR3UpscalerUtility.GetUpscaleRatio(VividFsr3QualityMode.Quality), Is.EqualTo(1.5f));
            Assert.That(FSR3UpscalerUtility.GetUpscaleRatio(VividFsr3QualityMode.Balanced), Is.EqualTo(1.7f));
            Assert.That(FSR3UpscalerUtility.GetUpscaleRatio(VividFsr3QualityMode.Performance), Is.EqualTo(2.0f));
            Assert.That(FSR3UpscalerUtility.GetUpscaleRatio(VividFsr3QualityMode.UltraPerformance), Is.EqualTo(3.0f));

            Assert.That(
                FSR3UpscalerUtility.ResolveRenderSize(3840, 2160, VividFsr3QualityMode.NativeAA),
                Is.EqualTo(new Vector2Int(3840, 2160)));
            Assert.That(
                FSR3UpscalerUtility.ResolveRenderSize(3840, 2160, VividFsr3QualityMode.Quality),
                Is.EqualTo(new Vector2Int(2560, 1440)));
            Assert.That(
                FSR3UpscalerUtility.ResolveRenderSize(3840, 2160, VividFsr3QualityMode.Performance),
                Is.EqualTo(new Vector2Int(1920, 1080)));
            Assert.That(
                FSR3UpscalerUtility.ResolveRenderSize(3840, 2160, VividFsr3QualityMode.UltraPerformance),
                Is.EqualTo(new Vector2Int(1280, 720)));
        }

        [Test]
        public void Jitter_UsesSdkPhaseCountAndHaltonOffset()
        {
            Assert.That(FSR3UpscalerUtility.GetJitterPhaseCount(1920, 3840), Is.EqualTo(32));

            var offset = FSR3UpscalerUtility.GetJitterOffset(0, 32);
            Assert.That(offset.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(offset.y, Is.EqualTo(-1.0f / 6.0f).Within(0.0001f));
        }

        [Test]
        public void CameraState_UsesCameraHistoryAndPreservesValidFrame()
        {
            var cameraObject = new GameObject("FSR3CameraHistoryTests.Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var history = camera.GetVividCameraHistory();
            var state = new FSR3UpscalerPass.CameraState();

            try
            {
                history.BeginFrame(8, 8);
                Assert.That(
                    state.Prepare(
                        camera,
                        new Vector2Int(8, 8),
                        new Vector2Int(16, 16),
                        VividFsr3QualityMode.Balanced,
                        1,
                        false),
                    Is.True);
                Assert.That(
                    history.TryGetTexture(CameraHistoryIds.Fsr3Accumulation, out var accumulation),
                    Is.True);
                Assert.That(accumulation.FrameCount, Is.EqualTo(2));
                Assert.That(
                    history.TryGetTexture(CameraHistoryIds.Fsr3FrameInfo, out var frameInfo),
                    Is.True);
                Assert.That(frameInfo.FrameCount, Is.EqualTo(1));

                state.MarkHistoryWritten();
                history.CommitFrame();
                history.BeginFrame(8, 8);

                Assert.That(
                    state.Prepare(
                        camera,
                        new Vector2Int(8, 8),
                        new Vector2Int(16, 16),
                        VividFsr3QualityMode.Balanced,
                        2,
                        false),
                    Is.False);
            }
            finally
            {
                history.AbortFrame();
                state.Dispose();
                CameraHistorySystem.Dispose();
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ConfigureColorDescriptor_ReusesDescriptorInstance()
        {
            var descriptor = new RenderGraphTextureDesc();

            var configured = FSR3UpscalerPass.ConfigureColorDescriptor(
                descriptor,
                "FSR3_TestDescriptor",
                1920,
                1080,
                GraphicsFormat.R16G16_SFloat);

            Assert.That(configured, Is.SameAs(descriptor));
            Assert.That(descriptor.Name, Is.EqualTo("FSR3_TestDescriptor"));
            Assert.That(descriptor.Width, Is.EqualTo(1920));
            Assert.That(descriptor.Height, Is.EqualTo(1080));
            Assert.That(descriptor.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16_SFloat));
            Assert.That(descriptor.EnableRandomWrite, Is.True);
            Assert.That(descriptor.AnisoLevel, Is.EqualTo(1));
        }

        [Test]
        public void ConfigureRcasConfig_DoesNotAllocate_WhenArrayIsReused()
        {
            var config = new int[4];
            FSR3UpscalerPass.ConfigureRcasConfig(config, 0.2f);

            var allocatedBefore = System.GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 32; index++)
                FSR3UpscalerPass.ConfigureRcasConfig(config, 0.2f);

            var allocatedBytes = System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.That(allocatedBytes, Is.Zero);
        }
    }
}
