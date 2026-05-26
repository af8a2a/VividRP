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

        private sealed class StatusPageProducer : IVTPageProducer
        {
            internal StatusPageProducer(in VirtualTextureSpaceDesc desc, VTPageRequestStatus status)
            {
                ProducerDesc = VTProducerDesc.FromSpaceDesc(nameof(StatusPageProducer), desc);
                Status = status;
            }

            public string Name => nameof(StatusPageProducer);

            public VTProducerDesc ProducerDesc { get; }

            internal VTPageRequestStatus Status { get; set; }

            internal int RequestCount { get; private set; }

            internal int ProduceCount { get; private set; }

            internal int GatherTaskCount { get; private set; }

            internal int CancelCount { get; private set; }

            public VTPageRequestStatus RequestPageData(in VirtualTextureSpaceDesc desc, in VTRequest request)
            {
                RequestCount += 1;
                return Status;
            }

            public IVTPageFinalizer ProducePageData(in VirtualTextureSpaceDesc desc, in VTRequest request)
            {
                ProduceCount += 1;
                return new SolidColorFinalizer(request);
            }

            public void GatherTasks(List<IVTPageProducerTask> tasks)
            {
                GatherTaskCount += 1;
            }

            public void CancelRequest(in VirtualTextureSpaceDesc desc, in VTRequest request)
            {
                CancelCount += 1;
            }

            internal void ResetCounters()
            {
                RequestCount = 0;
                ProduceCount = 0;
                GatherTaskCount = 0;
                CancelCount = 0;
            }
        }

        private sealed class SolidColorFinalizer : IVTPageFinalizer
        {
            private readonly VTRequest m_Request;

            internal SolidColorFinalizer(in VTRequest request)
            {
                m_Request = request;
            }

            public void FinalizeRender(CommandBuffer cmd)
            {
            }

            public void FinalizeUpload(Texture2DArray stagingTexture, int slice, Color32[] scratchPixels)
            {
                for (int index = 0; index < scratchPixels.Length; index++)
                {
                    scratchPixels[index] = new Color32(
                        (byte)(16 + m_Request.PageCoord.X * 32),
                        (byte)(32 + m_Request.PageCoord.Y * 32),
                        (byte)(48 + m_Request.PageCoord.Mip * 48),
                        255);
                }

                stagingTexture.SetPixels32(scratchPixels, slice, 0);
            }

            public void Dispose()
            {
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

        [Test]
        public void Uploads_DoNotRescheduleInFlightPage_WhenPendingPriorityIncreases()
        {
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(CreateDesc("PriorityUpdate"), new TestRuntimeProducer());
            var pageCoord = new VirtualTexturePageCoord(0, 0, 0);

            IssueFeedback(spaceId, pageCoord);

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out IReadOnlyList<VirtualTextureUploadRequest> initialRequests), Is.True);
            Assert.That(initialRequests, Has.Count.EqualTo(1));
            Assert.That(initialRequests[0].Priority, Is.EqualTo(1));

            IssueFeedback(spaceId, pageCoord, pageCoord);

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out IReadOnlyList<VirtualTextureUploadRequest> updatedRequests), Is.True);
            Assert.That(updatedRequests, Has.Count.EqualTo(1));
            Assert.That(updatedRequests[0].PageCoord, Is.EqualTo(pageCoord));
            Assert.That(updatedRequests[0].PhysicalPageId, Is.EqualTo(initialRequests[0].PhysicalPageId));
            Assert.That(updatedRequests[0].Generation, Is.EqualTo(initialRequests[0].Generation));
            Assert.That(updatedRequests[0].Priority, Is.EqualTo(2));

            VirtualTextureStats stats = VirtualTextureStatsRegistry.LastStats;
            Assert.That(stats.InFlightUploadBatchCount, Is.EqualTo(1));
            Assert.That(stats.DuplicateUploadCount, Is.EqualTo(1));
            Assert.That(stats.SkippedUploadCount, Is.EqualTo(1));
        }

        [Test]
        public void Uploads_RespectFrameMemoryBudget_AndKeepPageTablePending()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("MemoryBudget");
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, new TestRuntimeProducer());
            var pageCoord = new VirtualTexturePageCoord(0, 0, 0);

            VirtualTextureSystem.SetUploadMemoryBudgetForTesting(1);
            IssueFeedback(spaceId, pageCoord);

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(0));
            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                pageCoord,
                out VirtualTexturePageTableEntry pendingEntry), Is.True);
            Assert.That(pendingEntry.PendingUpload, Is.True);
            Assert.That(pendingEntry.Resident, Is.False);
            Assert.That(pendingEntry.Fallback, Is.True);

            VirtualTextureStats budgetStats = VirtualTextureStatsRegistry.LastStats;
            Assert.That(budgetStats.SkippedUploadCount, Is.EqualTo(1));

            VirtualTextureSystem.SetUploadMemoryBudgetForTesting(int.MaxValue);
            UpdateOnce();

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(1));
            m_FenceFactory.Handles[0].IsPassed = true;
            UpdateOnce();

            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(0));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                pageCoord,
                out VirtualTexturePageTableEntry residentEntry), Is.True);
            Assert.That(residentEntry.Resident, Is.True);
            Assert.That(residentEntry.PendingUpload, Is.False);
        }

        [Test]
        public void Uploads_FinalizeMultipleTilesInOneFrame_WhenBatchCapacityAllows()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("MultiFinalize", maxUploadsPerFrame: 3);
            var producer = new StatusPageProducer(desc, VTPageRequestStatus.Available);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            producer.ResetCounters();

            IssueFeedback(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 0),
                new VirtualTexturePageCoord(1, 0, 0),
                new VirtualTexturePageCoord(2, 0, 0));

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
            Assert.That(producer.RequestCount, Is.EqualTo(3));
            Assert.That(producer.ProduceCount, Is.EqualTo(3));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(3));
            Assert.That(VirtualTextureStatsRegistry.LastStats.InFlightUploadBatchCount, Is.EqualTo(1));

            m_FenceFactory.Handles[0].IsPassed = true;
            UpdateOnce();

            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(0));
            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(4));
        }

        [Test]
        public void Uploads_GrowSharedPool_WhenLaterSpaceNeedsLargerBatch()
        {
            int smallSpaceId = VirtualTextureSystem.RegisterAddressSpace(
                CreateDesc("SmallSharedPool"),
                new TestRuntimeProducer());
            IssueFeedback(smallSpaceId, new VirtualTexturePageCoord(0, 0, 0));

            VirtualTextureSpaceDesc largeDesc = CreateDesc("LargeSharedPool", maxUploadsPerFrame: 3);
            var largeProducer = new StatusPageProducer(largeDesc, VTPageRequestStatus.Available);
            int largeSpaceId = VirtualTextureSystem.RegisterAddressSpace(largeDesc, largeProducer);
            largeProducer.ResetCounters();

            IssueFeedback(
                largeSpaceId,
                new VirtualTexturePageCoord(0, 0, 0),
                new VirtualTexturePageCoord(1, 0, 0),
                new VirtualTexturePageCoord(2, 0, 0));

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(2));
            Assert.That(largeProducer.RequestCount, Is.EqualTo(3));
            Assert.That(largeProducer.ProduceCount, Is.EqualTo(3));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(largeSpaceId), Is.EqualTo(3));
        }

        [Test]
        public void Uploads_CancelStaleInFlightRequest_WhenAddressSpaceIsReconfigured()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("ReconfigureSpace");
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, new TestRuntimeProducer());
            var pageCoord = new VirtualTexturePageCoord(0, 0, 0);

            IssueFeedback(spaceId, pageCoord);
            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));

            var newProducer = new TestRuntimeProducer();
            Assert.That(VirtualTextureSystem.RegisterOrReconfigureAddressSpace(desc, newProducer), Is.EqualTo(spaceId));
            IssueFeedback(spaceId, pageCoord);

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(2));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(1));

            m_FenceFactory.Handles[0].IsPassed = true;
            UpdateOnce();
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                pageCoord,
                out VirtualTexturePageTableEntry pendingEntry), Is.True);
            Assert.That(pendingEntry.PendingUpload, Is.True);
            Assert.That(pendingEntry.Resident, Is.False);
            Assert.That(pendingEntry.Fallback, Is.True);

            m_FenceFactory.Handles[1].IsPassed = true;
            UpdateOnce();
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(0));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                pageCoord,
                out VirtualTexturePageTableEntry residentEntry), Is.True);
            Assert.That(residentEntry.Resident, Is.True);
        }

        [TestCase(VTPageRequestStatus.Pending)]
        [TestCase(VTPageRequestStatus.Saturated)]
        public void Uploads_DeferProducerRequests_UntilPageDataIsAvailable(VTPageRequestStatus unavailableStatus)
        {
            VirtualTextureSpaceDesc desc = CreateDesc($"Deferred{unavailableStatus}");
            var producer = new StatusPageProducer(desc, unavailableStatus);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            var pageCoord = new VirtualTexturePageCoord(0, 0, 0);
            producer.ResetCounters();

            IssueFeedback(spaceId, pageCoord);

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(0));
            Assert.That(producer.GatherTaskCount, Is.EqualTo(1));
            Assert.That(producer.RequestCount, Is.EqualTo(1));
            Assert.That(producer.ProduceCount, Is.EqualTo(0));
            Assert.That(producer.CancelCount, Is.EqualTo(0));
            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                pageCoord,
                out VirtualTexturePageTableEntry deferredEntry), Is.True);
            Assert.That(deferredEntry.PendingUpload, Is.True);
            Assert.That(deferredEntry.Resident, Is.False);

            producer.Status = VTPageRequestStatus.Available;
            UpdateOnce();

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
            Assert.That(producer.RequestCount, Is.EqualTo(2));
            Assert.That(producer.ProduceCount, Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                pageCoord,
                out VirtualTexturePageTableEntry uploadingEntry), Is.True);
            Assert.That(uploadingEntry.PendingUpload, Is.True);
            Assert.That(uploadingEntry.Resident, Is.False);

            m_FenceFactory.Handles[0].IsPassed = true;
            UpdateOnce();

            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(2));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(0));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                pageCoord,
                out VirtualTexturePageTableEntry residentEntry), Is.True);
            Assert.That(residentEntry.Resident, Is.True);
            Assert.That(residentEntry.PendingUpload, Is.False);
        }

        private static VirtualTextureSpaceDesc CreateDesc(string name, int maxUploadsPerFrame = 1)
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
                maxUploadsPerFrame: maxUploadsPerFrame,
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
