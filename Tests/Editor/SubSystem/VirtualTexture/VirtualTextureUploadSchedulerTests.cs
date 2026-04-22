using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureUploadSchedulerTests
    {
        private sealed class TestRuntimeProducer : IVTRuntimePageProducer
        {
            public string Name => nameof(TestRuntimeProducer);

            public void WritePage(in VirtualTextureSpaceDesc desc, in VTRequest request, Color32[] outputPixels)
            {
                for (int index = 0; index < outputPixels.Length; index++)
                {
                    outputPixels[index] = new Color32(
                        (byte)(32 + request.PageCoord.X * 32),
                        (byte)(64 + request.PageCoord.Y * 32),
                        (byte)(96 + request.PageCoord.Mip * 48),
                        255);
                }
            }
        }

        private sealed class ManualFenceHandle : IVTUploadFenceHandle
        {
            public bool IsPassed { get; set; }
        }

        private sealed class ManualFenceFactory : IVTUploadFenceFactory
        {
            public readonly List<ManualFenceHandle> Handles = new();

            public IVTUploadFenceHandle Create(CommandBuffer cmd)
            {
                var handle = new ManualFenceHandle();
                Handles.Add(handle);
                return handle;
            }
        }

        private ManualFenceFactory m_FenceFactory;

        [SetUp]
        public void SetUp()
        {
            m_FenceFactory = new ManualFenceFactory();
            VirtualTextureSystem.SetUploadFenceFactoryForTesting(m_FenceFactory);
        }

        [TearDown]
        public void TearDown()
        {
            VirtualTextureSystem.SetUploadFenceFactoryForTesting(null);
            VirtualTextureSystem.Deinitialize();
            m_FenceFactory = null;
        }

        [Test]
        public void Uploads_CommitOnlyAfterFencePasses()
        {
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(CreateDesc("FenceCommit"), new TestRuntimeProducer());

            IssueFeedback(spaceId, new VirtualTexturePageCoord(0, 0, 0));

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 0),
                out VirtualTexturePageTableEntry pendingEntry), Is.True);
            Assert.That(pendingEntry.PendingUpload, Is.True);
            Assert.That(pendingEntry.Resident, Is.False);

            UpdateOnce();

            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(1));

            m_FenceFactory.Handles[0].IsPassed = true;
            UpdateOnce();

            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(2));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(0));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 0),
                out VirtualTexturePageTableEntry residentEntry), Is.True);
            Assert.That(residentEntry.Resident, Is.True);
            Assert.That(residentEntry.PendingUpload, Is.False);
        }

        [Test]
        public void Uploads_StallCleanly_WhenBothBatchesAreInFlight()
        {
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(CreateDesc("DoubleBuffer"), new TestRuntimeProducer());

            IssueFeedback(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            IssueFeedback(spaceId, new VirtualTexturePageCoord(1, 0, 0));

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(2));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(2));

            IssueFeedback(spaceId, new VirtualTexturePageCoord(2, 0, 0));
            UpdateOnce();

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(2));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(3));
            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out IReadOnlyList<VirtualTextureUploadRequest> requests), Is.True);
            Assert.That(requests, Has.Count.EqualTo(3));
        }

        private static VirtualTextureSpaceDesc CreateDesc(string name)
        {
            return new VirtualTextureSpaceDesc(
                name,
                pageSize: 128,
                borderSize: 4,
                virtualPageCountX: 4,
                virtualPageCountY: 4,
                mipCount: 3,
                cachePageCount: 4,
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: 1,
                feedbackCapacity: 32);
        }

        private static void IssueFeedback(int spaceId, params VirtualTexturePageCoord[] coords)
        {
            var commandBuffer = new CommandBuffer();
            var frameData = new ContextContainer();

            try
            {
                foreach (VirtualTexturePageCoord coord in coords)
                    VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, VirtualTextureFeedbackProcessor.EncodeKey(spaceId, coord));

                VirtualTextureSystem.Update(frameData, commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        private static void UpdateOnce()
        {
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }
    }
}
