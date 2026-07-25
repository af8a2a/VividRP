using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using UnityRenderGraph = UnityEngine.Rendering.RenderGraphModule.RenderGraph;

namespace VividRP.Editor.Tests
{
    public sealed class CameraHistoryTests
    {
        private static readonly CameraHistoryId s_TestHistoryId =
            CameraHistoryId.Create("CameraHistoryTests");

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RTHandles.Initialize(1, 1);
        }

        [SetUp]
        public void SetUp()
        {
            CameraHistorySystem.Dispose();
        }

        [TearDown]
        public void TearDown()
        {
            CameraHistorySystem.Dispose();
        }

        [Test]
        public void GetVividCameraHistory_ReusesCameraRelativeOwner()
        {
            using var cameraScope = new CameraScope("HistoryOwnerCamera");

            var first = cameraScope.Camera.GetVividCameraHistory();
            var second = cameraScope.Camera.GetVividCameraHistory();

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void CommitFrame_RotatesWrittenPingPongHistory()
        {
            using var cameraScope = new CameraScope("HistoryRotationCamera");
            var history = cameraScope.Camera.GetVividCameraHistory();
            var descriptor = CreateDescriptor(64, 32);

            history.BeginFrame(64, 32);
            var texture = history.GetOrCreateTexture(s_TestHistoryId, 2, descriptor);
            var firstCurrent = texture.GetCurrent();
            var firstPrevious = texture.GetPrevious();
            Assert.That(texture.IsValid(), Is.False);

            texture.MarkWritten();
            history.CommitFrame();

            history.BeginFrame(64, 32);
            texture = history.GetOrCreateTexture(s_TestHistoryId, 2, descriptor);
            Assert.That(texture.IsValid(), Is.True);
            Assert.That(texture.GetPrevious(), Is.SameAs(firstCurrent));
            Assert.That(texture.GetCurrent(), Is.SameAs(firstPrevious));
            history.AbortFrame();
        }

        [Test]
        public void AbortFrame_DoesNotRotateOrValidateHistory()
        {
            using var cameraScope = new CameraScope("HistoryAbortCamera");
            var history = cameraScope.Camera.GetVividCameraHistory();
            var descriptor = CreateDescriptor(64, 32);

            history.BeginFrame(64, 32);
            var texture = history.GetOrCreateTexture(s_TestHistoryId, 2, descriptor);
            var firstCurrent = texture.GetCurrent();
            var firstPrevious = texture.GetPrevious();
            texture.MarkWritten();
            history.AbortFrame();

            history.BeginFrame(64, 32);
            texture = history.GetOrCreateTexture(s_TestHistoryId, 2, descriptor);
            Assert.That(texture.IsValid(), Is.False);
            Assert.That(texture.GetCurrent(), Is.SameAs(firstCurrent));
            Assert.That(texture.GetPrevious(), Is.SameAs(firstPrevious));
            history.AbortFrame();
        }

        [Test]
        public void ThreeFrameHistory_ProvidesOlderFramesByAge()
        {
            using var cameraScope = new CameraScope("HistoryRingCamera");
            var history = cameraScope.Camera.GetVividCameraHistory();
            var descriptor = CreateDescriptor(32, 16);

            history.BeginFrame(32, 16);
            var texture = history.GetOrCreateTexture(s_TestHistoryId, 3, descriptor);
            var firstWritten = texture.GetCurrent();
            texture.MarkWritten();
            history.CommitFrame();

            history.BeginFrame(32, 16);
            texture = history.GetOrCreateTexture(s_TestHistoryId, 3, descriptor);
            var secondWritten = texture.GetCurrent();
            Assert.That(texture.IsValid(1), Is.True);
            Assert.That(texture.IsValid(2), Is.False);
            texture.MarkWritten();
            history.CommitFrame();

            history.BeginFrame(32, 16);
            texture = history.GetOrCreateTexture(s_TestHistoryId, 3, descriptor);
            Assert.That(texture.IsValid(1), Is.True);
            Assert.That(texture.IsValid(2), Is.True);
            Assert.That(texture.GetFrame(1), Is.SameAs(secondWritten));
            Assert.That(texture.GetFrame(2), Is.SameAs(firstWritten));
            history.AbortFrame();
        }

        [Test]
        public void DescriptorChange_ReallocatesAndInvalidatesHistory()
        {
            using var cameraScope = new CameraScope("HistoryResizeCamera");
            var history = cameraScope.Camera.GetVividCameraHistory();
            var firstDescriptor = CreateDescriptor(32, 16);

            history.BeginFrame(32, 16);
            var firstTexture = history.GetOrCreateTexture(s_TestHistoryId, 2, firstDescriptor);
            var firstHandle = firstTexture.GetCurrent();
            firstTexture.MarkWritten();
            history.CommitFrame();

            history.BeginFrame(64, 32);
            var secondTexture = history.GetOrCreateTexture(
                s_TestHistoryId,
                2,
                CreateDescriptor(64, 32));

            Assert.That(secondTexture, Is.Not.SameAs(firstTexture));
            Assert.That(secondTexture.GetCurrent(), Is.Not.SameAs(firstHandle));
            Assert.That(secondTexture.IsValid(), Is.False);
            history.AbortFrame();
        }

        [Test]
        public void AllocationName_AppendsCameraAndResourceIndex()
        {
            using var cameraScope = new CameraScope("NamedHistoryCamera");
            var history = cameraScope.Camera.GetVividCameraHistory();

            history.BeginFrame(16, 16);
            var texture = history.GetOrCreateTexture(
                s_TestHistoryId,
                2,
                CreateDescriptor(16, 16));

            Assert.That(texture.GetFrame(0).name, Does.EndWith("_NamedHistoryCamera_0"));
            Assert.That(texture.GetFrame(1).name, Does.EndWith("_NamedHistoryCamera_1"));
            history.AbortFrame();
        }

        [Test]
        public void CommitFrame_RotatesWrittenPingPongBuffer()
        {
            using var cameraScope = new CameraScope("BufferRotationCamera");
            var history = cameraScope.Camera.GetVividCameraHistory();
            var descriptor = CreateBufferDescriptor(32, sizeof(uint));

            history.BeginFrame(64, 32);
            var buffer = history.GetOrCreateBuffer(s_TestHistoryId, 2, descriptor);
            var firstCurrent = buffer.GetCurrent();
            var firstPrevious = buffer.GetPrevious();
            Assert.That(buffer.IsValid(), Is.False);

            buffer.MarkWritten();
            history.CommitFrame();

            history.BeginFrame(64, 32);
            buffer = history.GetOrCreateBuffer(s_TestHistoryId, 2, descriptor);
            Assert.That(buffer.IsValid(), Is.True);
            Assert.That(buffer.GetPrevious(), Is.SameAs(firstCurrent));
            Assert.That(buffer.GetCurrent(), Is.SameAs(firstPrevious));
            history.AbortFrame();
        }

        [Test]
        public void AbortFrame_DoesNotRotateOrValidateBufferHistory()
        {
            using var cameraScope = new CameraScope("BufferAbortCamera");
            var history = cameraScope.Camera.GetVividCameraHistory();
            var descriptor = CreateBufferDescriptor(32, sizeof(uint));

            history.BeginFrame(64, 32);
            var buffer = history.GetOrCreateBuffer(s_TestHistoryId, 2, descriptor);
            var firstCurrent = buffer.GetCurrent();
            var firstPrevious = buffer.GetPrevious();
            buffer.MarkWritten();
            history.AbortFrame();

            history.BeginFrame(64, 32);
            buffer = history.GetOrCreateBuffer(s_TestHistoryId, 2, descriptor);
            Assert.That(buffer.IsValid(), Is.False);
            Assert.That(buffer.GetCurrent(), Is.SameAs(firstCurrent));
            Assert.That(buffer.GetPrevious(), Is.SameAs(firstPrevious));
            history.AbortFrame();
        }

        [Test]
        public void ThreeFrameBufferHistory_ProvidesOlderFramesByAge()
        {
            using var cameraScope = new CameraScope("BufferRingCamera");
            var history = cameraScope.Camera.GetVividCameraHistory();
            var descriptor = CreateBufferDescriptor(16, sizeof(uint));

            history.BeginFrame(32, 16);
            var buffer = history.GetOrCreateBuffer(s_TestHistoryId, 3, descriptor);
            var firstWritten = buffer.GetCurrent();
            buffer.MarkWritten();
            history.CommitFrame();

            history.BeginFrame(32, 16);
            buffer = history.GetOrCreateBuffer(s_TestHistoryId, 3, descriptor);
            var secondWritten = buffer.GetCurrent();
            Assert.That(buffer.IsValid(1), Is.True);
            Assert.That(buffer.IsValid(2), Is.False);
            buffer.MarkWritten();
            history.CommitFrame();

            history.BeginFrame(32, 16);
            buffer = history.GetOrCreateBuffer(s_TestHistoryId, 3, descriptor);
            Assert.That(buffer.IsValid(1), Is.True);
            Assert.That(buffer.IsValid(2), Is.True);
            Assert.That(buffer.GetFrame(1), Is.SameAs(secondWritten));
            Assert.That(buffer.GetFrame(2), Is.SameAs(firstWritten));
            history.AbortFrame();
        }

        [Test]
        public void BufferDescriptorChange_ReallocatesAndInvalidatesHistory()
        {
            using var cameraScope = new CameraScope("BufferResizeCamera");
            var history = cameraScope.Camera.GetVividCameraHistory();
            var firstDescriptor = CreateBufferDescriptor(16, sizeof(uint));

            history.BeginFrame(32, 16);
            var firstBuffer = history.GetOrCreateBuffer(s_TestHistoryId, 2, firstDescriptor);
            var firstHandle = firstBuffer.GetCurrent();
            firstBuffer.MarkWritten();
            history.CommitFrame();

            history.BeginFrame(32, 16);
            var secondBuffer = history.GetOrCreateBuffer(
                s_TestHistoryId,
                2,
                CreateBufferDescriptor(32, sizeof(uint)));

            Assert.That(secondBuffer, Is.Not.SameAs(firstBuffer));
            Assert.That(secondBuffer.GetCurrent(), Is.Not.SameAs(firstHandle));
            Assert.That(secondBuffer.IsValid(), Is.False);
            history.AbortFrame();
        }

        [Test]
        public void BufferAllocationName_AppendsCameraAndResourceIndex()
        {
            using var cameraScope = new CameraScope("NamedBufferHistoryCamera");
            var history = cameraScope.Camera.GetVividCameraHistory();
            var allocationNames = new List<string>();

            history.BeginFrame(16, 16);
            history.GetOrCreateBuffer(
                s_TestHistoryId,
                2,
                CreateBufferDescriptor(8, sizeof(uint)),
                AllocateBuffer);

            Assert.That(allocationNames[0], Does.EndWith("_NamedBufferHistoryCamera_0"));
            Assert.That(allocationNames[1], Does.EndWith("_NamedBufferHistoryCamera_1"));
            history.AbortFrame();

            GraphicsBuffer AllocateBuffer(
                in CameraHistoryBufferDescriptor descriptor,
                string resourceName,
                int resourceIndex)
            {
                allocationNames.Add(resourceName);
                return new GraphicsBuffer(descriptor.Target, descriptor.Count, descriptor.Stride)
                {
                    name = resourceName,
                };
            }
        }

        [Test]
        public void SingleFrameBuffer_UsesCurrentSlotAsValidHistory()
        {
            using var cameraScope = new CameraScope("SingleBufferHistoryCamera");
            var history = cameraScope.Camera.GetVividCameraHistory();
            var descriptor = CreateBufferDescriptor(8, sizeof(uint));

            history.BeginFrame(16, 16);
            var buffer = history.GetOrCreateBuffer(s_TestHistoryId, 1, descriptor);
            var current = buffer.GetCurrent();
            Assert.That(buffer.IsValid(0), Is.False);
            buffer.MarkWritten();
            history.CommitFrame();

            history.BeginFrame(16, 16);
            buffer = history.GetOrCreateBuffer(s_TestHistoryId, 1, descriptor);
            Assert.That(buffer.GetCurrent(), Is.SameAs(current));
            Assert.That(buffer.IsValid(0), Is.True);
            history.AbortFrame();
        }

        [Test]
        public void BufferDescriptorBridge_CopiesRenderGraphSettings()
        {
            var descriptor = new RenderGraphBufferDesc
            {
                Count = 48,
                Stride = 16,
                Target = GraphicsBuffer.Target.Raw,
            };

            var historyDescriptor = CameraHistoryRenderGraphBridge.CreateDescriptor(descriptor);

            Assert.That(historyDescriptor.Count, Is.EqualTo(48));
            Assert.That(historyDescriptor.Stride, Is.EqualTo(16));
            Assert.That(historyDescriptor.Target, Is.EqualTo(GraphicsBuffer.Target.Raw));
            Assert.That(historyDescriptor.UsageFlags, Is.EqualTo(GraphicsBuffer.UsageFlags.None));
        }

        [Test]
        public void BufferBridge_ReusesImportedHandleAndClearsWrapperAfterFrame()
        {
            using var cameraScope = new CameraScope("BufferBridgeCamera");
            var history = cameraScope.Camera.GetVividCameraHistory();
            var renderGraph = new UnityRenderGraph("Camera History Buffer Bridge Test");
            var wrapper = new RenderGraphBuffer();

            history.BeginFrame(16, 16);
            var buffer = history.GetOrCreateBuffer(
                s_TestHistoryId,
                2,
                CreateBufferDescriptor(8, sizeof(uint)));

            try
            {
                SetCurrentRenderGraph(renderGraph);
                var first = CameraHistoryRenderGraphBridge.Import(buffer, 0);
                var second = CameraHistoryRenderGraphBridge.Import(buffer, 0);
                var bound = CameraHistoryRenderGraphBridge.Bind(wrapper, buffer, 0);

                Assert.That(first.IsValid(), Is.True);
                Assert.That(second, Is.EqualTo(first));
                Assert.That(bound, Is.EqualTo(first));
                Assert.That(wrapper.HasImportedHandle, Is.True);

                PassRecorder.AbortFrame();
                Assert.That(wrapper.HasImportedHandle, Is.False);
            }
            finally
            {
                PassRecorder.AbortFrame();
                history.AbortFrame();
                renderGraph.Cleanup();
            }
        }

        private static CameraHistoryTextureDescriptor CreateDescriptor(int width, int height)
        {
            return new CameraHistoryTextureDescriptor(
                width,
                height,
                GraphicsFormat.R8G8B8A8_UNorm,
                enableRandomWrite: true);
        }

        private static CameraHistoryBufferDescriptor CreateBufferDescriptor(int count, int stride)
        {
            return new CameraHistoryBufferDescriptor(
                count,
                stride,
                GraphicsBuffer.Target.Structured);
        }

        private static void SetCurrentRenderGraph(UnityRenderGraph renderGraph)
        {
            var field = typeof(PassRecorder).GetField(
                "s_CurrentRenderGraph",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            field.SetValue(null, renderGraph);
        }

        private sealed class CameraScope : IDisposable
        {
            private readonly GameObject m_GameObject;

            internal CameraScope(string name)
            {
                m_GameObject = new GameObject(name);
                Camera = m_GameObject.AddComponent<Camera>();
            }

            internal Camera Camera { get; }

            public void Dispose()
            {
                if (m_GameObject != null)
                    UnityEngine.Object.DestroyImmediate(m_GameObject);
            }
        }
    }
}
