using System.Collections.Generic;
using System.Linq;
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

        private sealed class StatusPageProducer :
            IVTPageProducer,
            IVTPrioritizedPageProducer,
            IVTPageRequestRetirement
        {
            internal StatusPageProducer(
                in VirtualTextureSpaceDesc desc,
                VTPageRequestStatus status,
                int producerPriority = 0)
            {
                VTProducerDesc baseDesc = VTProducerDesc.FromSpaceDesc(nameof(StatusPageProducer), desc);
                ProducerDesc = new VTProducerDesc(
                    baseDesc.Name,
                    baseDesc.TileSize,
                    baseDesc.BorderSize,
                    baseDesc.VirtualPageCountX,
                    baseDesc.VirtualPageCountY,
                    baseDesc.MipCount,
                    baseDesc.LayerCount,
                    baseDesc.Format,
                    baseDesc.SRGB,
                    baseDesc.FallbackColor,
                    producerPriority,
                    baseDesc.ContinuousUpdate,
                    baseDesc.PersistentLowestMip);
                Status = status;
            }

            public string Name => nameof(StatusPageProducer);

            public VTProducerDesc ProducerDesc { get; }

            internal VTPageRequestStatus Status { get; set; }

            internal int RequestCount { get; private set; }

            internal int ProduceCount { get; private set; }

            internal int GatherTaskCount { get; private set; }

            internal int CancelCount { get; private set; }

            internal int RetirementCount { get; private set; }

            internal int LastRetiredRequestCount { get; private set; } = -1;

            internal List<VirtualTexturePageCoord> RequestedCoords { get; } = new();

            internal List<VTRequestPriorityKey> RequestedPriorityKeys { get; } = new();

            public VTPageRequestStatus RequestPageData(in VirtualTextureSpaceDesc desc, in VTRequest request)
            {
                VTRequestPriorityKey priorityKey = VTRequestPriorityKey.FromRequest(
                    request,
                    locked: false,
                    producerPriority: ProducerDesc.ProducerPriority);
                return RequestPageData(desc, request, priorityKey);
            }

            public VTPageRequestStatus RequestPageData(
                in VirtualTextureSpaceDesc desc,
                in VTRequest request,
                in VTRequestPriorityKey priorityKey)
            {
                RequestCount += 1;
                RequestedCoords.Add(request.PageCoord);
                RequestedPriorityKeys.Add(priorityKey);
                return Status;
            }

            public IVTPageUploadFinalizer ProducePageData(in VirtualTextureSpaceDesc desc, in VTRequest request)
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

            public void RetireRequests(IReadOnlyList<VTRequest> liveRequests)
            {
                RetirementCount += 1;
                LastRetiredRequestCount = liveRequests?.Count ?? 0;
            }

            internal void ResetCounters()
            {
                RequestCount = 0;
                ProduceCount = 0;
                GatherTaskCount = 0;
                CancelCount = 0;
                RetirementCount = 0;
                LastRetiredRequestCount = -1;
                RequestedCoords.Clear();
                RequestedPriorityKeys.Clear();
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

        private sealed class RecordingCpuFinalizer : IVTPageFinalizer
        {
            internal int FinalizeRenderCount { get; private set; }

            internal int FinalizeUploadCount { get; private set; }

            internal bool IsDisposed { get; private set; }

            public void FinalizeRender(CommandBuffer cmd)
            {
                FinalizeRenderCount += 1;
            }

            public void FinalizeUpload(Texture2DArray stagingTexture, int slice, Color32[] scratchPixels)
            {
                FinalizeUploadCount += 1;
                for (int pixelIndex = 0; pixelIndex < scratchPixels.Length; pixelIndex++)
                    scratchPixels[pixelIndex] = new Color32(16, 32, 48, 255);
                stagingTexture.SetPixels32(scratchPixels, slice, 0);
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        private sealed class RecordingGpuFinalizer : IVTGpuPageFinalizer
        {
            internal RecordingGpuFinalizer(int layerCount)
            {
                LayerCount = layerCount;
            }

            public int LayerCount { get; }

            internal int RecordCount { get; private set; }

            internal int BaseSlice { get; private set; } = -1;

            internal RenderTexture StagingTexture { get; private set; }

            internal bool IsDisposed { get; private set; }

            public void RecordGpuUpload(CommandBuffer cmd, RenderTexture stagingTexture, int baseSlice)
            {
                RecordCount += 1;
                BaseSlice = baseSlice;
                StagingTexture = stagingTexture;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        private sealed class RecordingFinalizerProducer : IVTPageProducer
        {
            private readonly System.Func<VirtualTexturePageCoord, bool> m_UseGpu;
            private readonly int m_GpuLayerCount;

            internal RecordingFinalizerProducer(
                in VirtualTextureSpaceDesc desc,
                System.Func<VirtualTexturePageCoord, bool> useGpu,
                int gpuLayerCount = 1)
            {
                ProducerDesc = VTProducerDesc.FromSpaceDesc(nameof(RecordingFinalizerProducer), desc);
                m_UseGpu = useGpu;
                m_GpuLayerCount = gpuLayerCount;
            }

            public string Name => nameof(RecordingFinalizerProducer);

            public VTProducerDesc ProducerDesc { get; }

            internal List<RecordingCpuFinalizer> CpuFinalizers { get; } = new();

            internal List<RecordingGpuFinalizer> GpuFinalizers { get; } = new();

            internal List<VirtualTexturePageCoord> ProducedCoords { get; } = new();

            public VTPageRequestStatus RequestPageData(in VirtualTextureSpaceDesc desc, in VTRequest request)
            {
                return VTPageRequestStatus.Available;
            }

            public IVTPageUploadFinalizer ProducePageData(in VirtualTextureSpaceDesc desc, in VTRequest request)
            {
                ProducedCoords.Add(request.PageCoord);
                if (m_UseGpu(request.PageCoord))
                {
                    var finalizer = new RecordingGpuFinalizer(m_GpuLayerCount);
                    GpuFinalizers.Add(finalizer);
                    return finalizer;
                }

                var cpuFinalizer = new RecordingCpuFinalizer();
                CpuFinalizers.Add(cpuFinalizer);
                return cpuFinalizer;
            }

            public void GatherTasks(List<IVTPageProducerTask> tasks)
            {
            }

            public void CancelRequest(in VirtualTextureSpaceDesc desc, in VTRequest request)
            {
            }

            internal void ResetRecords()
            {
                CpuFinalizers.Clear();
                GpuFinalizers.Clear();
                ProducedCoords.Clear();
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
        public void FlushRegion_CancelsOnlyUploadsInsideReleasedRegion()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("RegionCancel", maxUploadsPerFrame: 2);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, new TestRuntimeProducer());
            var releasedCoord = new VirtualTexturePageCoord(0, 0, 0);
            var retainedCoord = new VirtualTexturePageCoord(1, 0, 0);

            IssueFeedback(spaceId, releasedCoord, retainedCoord);

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(2));

            Assert.That(
                VirtualTextureSystem.FlushRegion(spaceId, 0, new RectInt(0, 0, 1, 1)),
                Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(1));

            m_FenceFactory.Handles[0].IsPassed = true;
            UpdateOnce();

            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.Zero);
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                releasedCoord,
                out VirtualTexturePageTableEntry releasedEntry), Is.True);
            Assert.That(releasedEntry.Resident, Is.False);
            Assert.That(releasedEntry.PendingUpload, Is.False);
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                retainedCoord,
                out VirtualTexturePageTableEntry retainedEntry), Is.True);
            Assert.That(retainedEntry.Resident, Is.True);
            Assert.That(retainedEntry.PendingUpload, Is.False);
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
        public void UploadOrder_ReusesCachedSortUntilPendingRevisionChanges()
        {
            VirtualTextureSpaceDesc desc = CreateDesc(
                "PendingOrderCache",
                maxUploadsPerFrame: 2);
            var producer = new StatusPageProducer(desc, VTPageRequestStatus.Pending);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            var firstCoord = new VirtualTexturePageCoord(0, 0, 0);
            var secondCoord = new VirtualTexturePageCoord(1, 0, 0);

            IssueFeedback(spaceId, firstCoord, secondCoord);

            uint initialRevision = VirtualTextureSystem.GetPendingRequestRevisionForTesting(spaceId);
            int initialBuildCount = VirtualTextureSystem.GetPendingOrderCacheBuildCountForTesting(spaceId);
            int initialHitCount = VirtualTextureSystem.GetPendingOrderCacheHitCountForTesting(spaceId);
            Assert.That(initialRevision, Is.GreaterThan(0u));
            Assert.That(initialBuildCount, Is.EqualTo(1));

            UpdateOnce();

            Assert.That(VirtualTextureSystem.GetPendingRequestRevisionForTesting(spaceId), Is.EqualTo(initialRevision));
            Assert.That(VirtualTextureSystem.GetPendingOrderCacheBuildCountForTesting(spaceId), Is.EqualTo(initialBuildCount));
            Assert.That(VirtualTextureSystem.GetPendingOrderCacheHitCountForTesting(spaceId), Is.EqualTo(initialHitCount + 1));

            IssueFeedback(spaceId, firstCoord, firstCoord);

            Assert.That(VirtualTextureSystem.GetPendingRequestRevisionForTesting(spaceId), Is.GreaterThan(initialRevision));
            Assert.That(VirtualTextureSystem.GetPendingOrderCacheBuildCountForTesting(spaceId), Is.EqualTo(initialBuildCount + 1));
        }

        [Test]
        public void UploadSpaceOrder_SkipsSortUntilPendingWorkExists()
        {
            VirtualTextureSpaceDesc firstDesc = CreateDesc("IdleUploadSpaceA");
            VirtualTextureSpaceDesc secondDesc = CreateDesc("IdleUploadSpaceB");
            var firstProducer = new StatusPageProducer(firstDesc, VTPageRequestStatus.Pending);
            var secondProducer = new StatusPageProducer(secondDesc, VTPageRequestStatus.Pending);
            int firstSpaceId = VirtualTextureSystem.RegisterAddressSpace(firstDesc, firstProducer);
            VirtualTextureSystem.RegisterAddressSpace(secondDesc, secondProducer);
            int initialSortCount = VirtualTextureSystem.GetUploadSpaceSortCountForTesting();

            UpdateOnce();

            Assert.That(VirtualTextureSystem.GetUploadSpaceSortCountForTesting(), Is.EqualTo(initialSortCount));

            IssueFeedback(firstSpaceId, new VirtualTexturePageCoord(0, 0, 0));

            Assert.That(VirtualTextureSystem.GetUploadSpaceSortCountForTesting(), Is.EqualTo(initialSortCount + 1));
        }

        [Test]
        public void ProducerRetirement_RunsOnlyWhenLiveRequestRevisionChanges()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("RevisionGatedRetirement");
            var producer = new StatusPageProducer(desc, VTPageRequestStatus.Pending);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            var coord = new VirtualTexturePageCoord(0, 0, 0);
            producer.ResetCounters();

            UpdateOnce();

            Assert.That(producer.RetirementCount, Is.EqualTo(1));
            Assert.That(producer.LastRetiredRequestCount, Is.Zero);

            UpdateOnce();

            Assert.That(producer.RetirementCount, Is.EqualTo(1));

            IssueFeedback(spaceId, coord);

            Assert.That(producer.RetirementCount, Is.EqualTo(2));
            Assert.That(producer.LastRetiredRequestCount, Is.EqualTo(1));

            UpdateOnce();

            Assert.That(producer.RetirementCount, Is.EqualTo(2));

            IssueFeedback(spaceId, coord, coord);

            Assert.That(producer.RetirementCount, Is.EqualTo(3));
            Assert.That(producer.LastRetiredRequestCount, Is.EqualTo(1));

            UpdateOnce();

            Assert.That(producer.RetirementCount, Is.EqualTo(3));

            producer.Status = VTPageRequestStatus.Invalid;
            UpdateOnce();

            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.Zero);
            Assert.That(producer.RetirementCount, Is.EqualTo(3));
            int sortCountAfterPendingRemoval = VirtualTextureSystem.GetUploadSpaceSortCountForTesting();

            UpdateOnce();

            Assert.That(producer.RetirementCount, Is.EqualTo(4));
            Assert.That(producer.LastRetiredRequestCount, Is.Zero);
            Assert.That(
                VirtualTextureSystem.GetUploadSpaceSortCountForTesting(),
                Is.EqualTo(sortCountAfterPendingRemoval));

            UpdateOnce();

            Assert.That(producer.RetirementCount, Is.EqualTo(4));
            Assert.That(
                VirtualTextureSystem.GetUploadSpaceSortCountForTesting(),
                Is.EqualTo(sortCountAfterPendingRemoval));
        }

        [Test]
        public void UploadOrder_SchedulesHigherMipWeightedHitCountFirst()
        {
            VirtualTextureSpaceDesc desc = CreateDesc(
                "MipWeightedUploadOrder",
                maxUploadsPerFrame: 1);
            var producer = new StatusPageProducer(desc, VTPageRequestStatus.Available);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            producer.ResetCounters();
            var fineCoord = new VirtualTexturePageCoord(0, 0, 0);
            var coarseCoord = new VirtualTexturePageCoord(0, 0, 1);

            IssueFeedback(spaceId, fineCoord, fineCoord, fineCoord, coarseCoord);

            Assert.That(producer.RequestedCoords, Has.Count.EqualTo(1));
            Assert.That(producer.RequestedCoords[0], Is.EqualTo(fineCoord));
            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(
                spaceId,
                out IReadOnlyList<VirtualTextureUploadRequest> pendingRequests), Is.True);
            Assert.That(pendingRequests, Has.Count.EqualTo(1));
            Assert.That(pendingRequests[0].PageCoord, Is.EqualTo(fineCoord));
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

        [TestCase(false, TestName = "Uploads_ShareGlobalPageBudgetAcrossCameraUpdatesInSameFrame")]
        [TestCase(true, TestName = "Uploads_ShareGlobalByteBudgetAcrossCameraUpdatesInSameFrame")]
        public void Uploads_ShareGlobalBudgetAcrossCameraUpdatesInSameFrame(bool useByteBudget)
        {
            VirtualTextureSpaceDesc desc = CreateDesc(
                useByteBudget ? "MultiCameraByteBudget" : "MultiCameraPageBudget",
                maxUploadsPerFrame: 2,
                cachePageCount: 4);
            var producer = new StatusPageProducer(desc, VTPageRequestStatus.Available);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            producer.ResetCounters();

            RunWithTwoCameras("VTBudgetCamera", (firstCamera, secondCamera, commandBuffer) =>
            {
                int uploadByteSize = desc.PhysicalPageSize * desc.PhysicalPageSize * 4;
                VirtualTextureSystem.SetUploadPageBudgetForTesting(useByteBudget ? 2 : 1);
                VirtualTextureSystem.SetUploadMemoryBudgetForTesting(
                    useByteBudget ? uploadByteSize : int.MaxValue);

                var firstCoord = new VirtualTexturePageCoord(0, 0, 0);
                var secondCoord = new VirtualTexturePageCoord(3, 3, 0);
                IssueFeedback(firstCamera, 73, commandBuffer, spaceId, firstCoord);

                Assert.That(producer.ProduceCount, Is.EqualTo(1));
                Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));

                IssueFeedback(secondCamera, 73, commandBuffer, spaceId, secondCoord);

                Assert.That(producer.ProduceCount, Is.EqualTo(1));
                Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
                Assert.That(
                    VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId),
                    Is.EqualTo(2));

                m_FenceFactory.Handles[0].IsPassed = true;
                IssueFeedback(secondCamera, 74, commandBuffer, spaceId, secondCoord);

                Assert.That(producer.ProduceCount, Is.EqualTo(2));
                Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void Uploads_SharePerSpacePageBudgetAcrossCameraUpdatesInSameFrame()
        {
            VirtualTextureSpaceDesc desc = CreateDesc(
                "MultiCameraSpaceBudget",
                maxUploadsPerFrame: 1,
                cachePageCount: 4);
            var producer = new StatusPageProducer(desc, VTPageRequestStatus.Available);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            producer.ResetCounters();

            RunWithTwoCameras("VTSpaceBudgetCamera", (firstCamera, secondCamera, commandBuffer) =>
            {
                VirtualTextureSystem.SetUploadPageBudgetForTesting(2);
                VirtualTextureSystem.SetUploadMemoryBudgetForTesting(int.MaxValue);

                IssueFeedback(
                    firstCamera,
                    73,
                    commandBuffer,
                    spaceId,
                    new VirtualTexturePageCoord(0, 0, 0));
                IssueFeedback(
                    secondCamera,
                    73,
                    commandBuffer,
                    spaceId,
                    new VirtualTexturePageCoord(3, 3, 0));

                Assert.That(producer.ProduceCount, Is.EqualTo(1));
                Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
                Assert.That(
                    VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public void Uploads_UseRemainingGlobalBudgetAcrossCameraUpdatesInSameFrame()
        {
            VirtualTextureSpaceDesc firstDesc = CreateDesc(
                "MultiCameraRemainingBudgetA",
                maxUploadsPerFrame: 1,
                cachePageCount: 4);
            VirtualTextureSpaceDesc secondDesc = CreateDesc(
                "MultiCameraRemainingBudgetB",
                maxUploadsPerFrame: 1,
                cachePageCount: 4);
            var firstProducer = new StatusPageProducer(firstDesc, VTPageRequestStatus.Available);
            var secondProducer = new StatusPageProducer(secondDesc, VTPageRequestStatus.Available);
            int firstSpaceId = VirtualTextureSystem.RegisterAddressSpace(firstDesc, firstProducer);
            int secondSpaceId = VirtualTextureSystem.RegisterAddressSpace(secondDesc, secondProducer);
            firstProducer.ResetCounters();
            secondProducer.ResetCounters();

            RunWithTwoCameras("VTRemainingBudgetCamera", (firstCamera, secondCamera, commandBuffer) =>
            {
                VirtualTextureSystem.SetUploadPageBudgetForTesting(2);
                VirtualTextureSystem.SetUploadMemoryBudgetForTesting(int.MaxValue);

                IssueFeedback(
                    firstCamera,
                    73,
                    commandBuffer,
                    firstSpaceId,
                    new VirtualTexturePageCoord(0, 0, 0));
                IssueFeedback(
                    secondCamera,
                    73,
                    commandBuffer,
                    secondSpaceId,
                    new VirtualTexturePageCoord(0, 0, 0));

                Assert.That(firstProducer.ProduceCount, Is.EqualTo(1));
                Assert.That(secondProducer.ProduceCount, Is.EqualTo(1));
                Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void Uploads_SharePerSpaceScheduleBudgetAcrossCameraUpdatesInSameFrame()
        {
            VirtualTextureSpaceDesc desc = CreateDesc(
                "MultiCameraSpaceScheduleBudget",
                maxUploadsPerFrame: 1,
                cachePageCount: 4);
            var producer = new StatusPageProducer(desc, VTPageRequestStatus.Available);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            producer.ResetCounters();

            VirtualTextureSystem.SetUploadMemoryBudgetForTesting(1);
            IssueFeedback(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            IssueFeedback(spaceId, new VirtualTexturePageCoord(3, 3, 0));

            Assert.That(producer.ProduceCount, Is.Zero);
            Assert.That(
                VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId),
                Is.EqualTo(2));

            RunWithTwoCameras("VTSpaceScheduleBudgetCamera", (firstCamera, secondCamera, commandBuffer) =>
            {
                VirtualTextureSystem.SetUploadPageBudgetForTesting(2);
                VirtualTextureSystem.SetUploadMemoryBudgetForTesting(int.MaxValue);

                IssueFeedback(firstCamera, 73, commandBuffer, spaceId);

                Assert.That(producer.ProduceCount, Is.EqualTo(1));
                Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));

                IssueFeedback(secondCamera, 73, commandBuffer, spaceId);

                Assert.That(producer.ProduceCount, Is.EqualTo(1));
                Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));

                m_FenceFactory.Handles[0].IsPassed = true;
                IssueFeedback(secondCamera, 74, commandBuffer, spaceId);

                Assert.That(producer.ProduceCount, Is.EqualTo(2));
                Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void Uploads_AccountForEachPhysicalGroupStorageFormat()
        {
            VirtualTextureSpaceDesc desc = CreateLayeredUploadDesc("FormatAwareMemoryBudget");
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, new TestRuntimeProducer());
            var pageCoord = new VirtualTexturePageCoord(0, 0, 0);
            int physicalPixelCount = desc.PhysicalPageSize * desc.PhysicalPageSize;
            int legacyRgbaOnlyByteSize = physicalPixelCount * 4 * desc.StackDesc.LayerCount;
            int formatAwareByteSize = physicalPixelCount * (4 + 8);

            VirtualTextureSystem.SetUploadMemoryBudgetForTesting(legacyRgbaOnlyByteSize);
            IssueFeedback(spaceId, pageCoord);

            Assert.That(m_FenceFactory.Handles, Is.Empty);
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(1));

            VirtualTextureSystem.SetUploadMemoryBudgetForTesting(formatAwareByteSize);
            UpdateOnce();

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(1));
        }

        [Test]
        public void Residency_UsesGlobalAllocationBudgetAcrossSpaces_AndKeepsHighestPriorityRequest()
        {
            VirtualTextureSpaceDesc lowDesc = CreateDesc("GlobalResidencyLow");
            VirtualTextureSpaceDesc highDesc = CreateDesc("GlobalResidencyHigh");
            var lowProducer = new StatusPageProducer(lowDesc, VTPageRequestStatus.Pending);
            var highProducer = new StatusPageProducer(highDesc, VTPageRequestStatus.Pending);
            int lowSpaceId = VirtualTextureSystem.RegisterAddressSpace(lowDesc, lowProducer);
            int highSpaceId = VirtualTextureSystem.RegisterAddressSpace(highDesc, highProducer);
            var lowCoord = new VirtualTexturePageCoord(0, 0, 0);
            var highCoord = new VirtualTexturePageCoord(1, 0, 0);
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.SetResidencyAllocationBudgetForTesting(1);
                VirtualTextureSystem.InjectCompletedReadbackForTesting(
                    CameraType.SceneView,
                    VirtualTextureFeedbackProcessor.EncodeKey(lowSpaceId, lowCoord));
                VirtualTextureSystem.InjectCompletedReadbackForTesting(
                    CameraType.Game,
                    VirtualTextureFeedbackProcessor.EncodeKey(highSpaceId, highCoord),
                    VirtualTextureFeedbackProcessor.EncodeKey(highSpaceId, highCoord));

                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);

                Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(highSpaceId), Is.EqualTo(1));
                Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(lowSpaceId), Is.EqualTo(0));
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        [Test]
        public void Residency_CanAllocateMorePagesThanTheUploadBudget()
        {
            VirtualTextureSpaceDesc desc = CreateDesc(
                "IndependentResidencyBudget",
                maxUploadsPerFrame: 1,
                cachePageCount: 4,
                maxResidencyAllocationsPerFrame: 2);
            var producer = new StatusPageProducer(desc, VTPageRequestStatus.Available);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            producer.ResetCounters();

            VirtualTextureSystem.ConfigureBudgets(
                maxResidencyAllocationsPerFrame: 2,
                maxPrefetchAllocationsPerFrame: 0,
                maxPageUploadsPerFrame: 1,
                maxUploadBytesPerFrame: int.MaxValue);
            IssueFeedback(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 0),
                new VirtualTexturePageCoord(1, 0, 0));

            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(2));
            Assert.That(producer.ProduceCount, Is.EqualTo(1));
            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
        }

        [Test]
        public void Uploads_CanDrainMorePendingPagesThanTheResidencyBudget()
        {
            VirtualTextureSpaceDesc desc = CreateDesc(
                "IndependentUploadBudget",
                maxUploadsPerFrame: 2,
                cachePageCount: 4,
                maxResidencyAllocationsPerFrame: 2);
            var producer = new StatusPageProducer(desc, VTPageRequestStatus.Pending);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            producer.ResetCounters();

            VirtualTextureSystem.ConfigureBudgets(
                maxResidencyAllocationsPerFrame: 2,
                maxPrefetchAllocationsPerFrame: 0,
                maxPageUploadsPerFrame: 2,
                maxUploadBytesPerFrame: int.MaxValue);
            IssueFeedback(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 0),
                new VirtualTexturePageCoord(1, 0, 0));

            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(2));
            Assert.That(producer.ProduceCount, Is.Zero);
            Assert.That(m_FenceFactory.Handles, Is.Empty);

            producer.Status = VTPageRequestStatus.Available;
            producer.ResetCounters();
            VirtualTextureSystem.ConfigureBudgets(
                maxResidencyAllocationsPerFrame: 1,
                maxPrefetchAllocationsPerFrame: 0,
                maxPageUploadsPerFrame: 2,
                maxUploadBytesPerFrame: int.MaxValue);
            UpdateOnce();

            Assert.That(producer.ProduceCount, Is.EqualTo(2));
            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
        }

        [Test]
        public void Uploads_MergeCandidatesAcrossSpaces_BeforeApplyingGlobalBackpressure()
        {
            VirtualTextureSpaceDesc lowDesc = CreateDesc("GlobalUploadLow");
            VirtualTextureSpaceDesc highDesc = CreateDesc("GlobalUploadHigh");
            var lowProducer = new StatusPageProducer(lowDesc, VTPageRequestStatus.Pending);
            var highProducer = new StatusPageProducer(highDesc, VTPageRequestStatus.Pending);
            int lowSpaceId = VirtualTextureSystem.RegisterAddressSpace(lowDesc, lowProducer);
            int highSpaceId = VirtualTextureSystem.RegisterAddressSpace(highDesc, highProducer);
            var lowCoord = new VirtualTexturePageCoord(0, 0, 0);
            var highCoord = new VirtualTexturePageCoord(1, 0, 0);
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.SetUploadPageBudgetForTesting(2);
                VirtualTextureSystem.InjectCompletedReadbackForTesting(
                    CameraType.SceneView,
                    VirtualTextureFeedbackProcessor.EncodeKey(lowSpaceId, lowCoord));
                VirtualTextureSystem.InjectCompletedReadbackForTesting(
                    CameraType.Game,
                    VirtualTextureFeedbackProcessor.EncodeKey(highSpaceId, highCoord),
                    VirtualTextureFeedbackProcessor.EncodeKey(highSpaceId, highCoord),
                    VirtualTextureFeedbackProcessor.EncodeKey(highSpaceId, highCoord));
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);

                Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(lowSpaceId), Is.EqualTo(1));
                Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(highSpaceId), Is.EqualTo(1));

                lowProducer.Status = VTPageRequestStatus.Available;
                highProducer.Status = VTPageRequestStatus.Available;
                lowProducer.ResetCounters();
                highProducer.ResetCounters();
                VirtualTextureSystem.SetUploadPageBudgetForTesting(1);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);

                Assert.That(highProducer.ProduceCount, Is.EqualTo(1));
                Assert.That(lowProducer.ProduceCount, Is.EqualTo(0));
                Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        [Test]
        public void ResidencyAndUploads_UseProducerPriorityWhenViewAndPageScoresTie()
        {
            VirtualTextureSpaceDesc lowDesc = CreateDesc("ProducerPriorityLow");
            VirtualTextureSpaceDesc highDesc = CreateDesc("ProducerPriorityHigh");
            var lowProducer = new StatusPageProducer(
                lowDesc,
                VTPageRequestStatus.Available,
                producerPriority: 0);
            var highProducer = new StatusPageProducer(
                highDesc,
                VTPageRequestStatus.Available,
                producerPriority: 10);
            int lowSpaceId = VirtualTextureSystem.RegisterAddressSpace(lowDesc, lowProducer);
            int highSpaceId = VirtualTextureSystem.RegisterAddressSpace(highDesc, highProducer);
            var commandBuffer = new CommandBuffer();

            try
            {
                lowProducer.ResetCounters();
                highProducer.ResetCounters();
                VirtualTextureSystem.ConfigureBudgets(
                    maxResidencyAllocationsPerFrame: 1,
                    maxPrefetchAllocationsPerFrame: 0,
                    maxPageUploadsPerFrame: 1,
                    maxUploadBytesPerFrame: int.MaxValue);
                VirtualTextureSystem.InjectCompletedReadbackForTesting(
                    CameraType.Game,
                    VirtualTextureFeedbackProcessor.EncodeKey(
                        lowSpaceId,
                        new VirtualTexturePageCoord(0, 0, 0)),
                    VirtualTextureFeedbackProcessor.EncodeKey(
                        highSpaceId,
                        new VirtualTexturePageCoord(0, 0, 0)));
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);

                Assert.That(highProducer.ProduceCount, Is.EqualTo(1));
                Assert.That(lowProducer.ProduceCount, Is.Zero);
                Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(highSpaceId), Is.EqualTo(1));
                Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(lowSpaceId), Is.Zero);
                Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
            }
            finally
            {
                commandBuffer.Dispose();
            }
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
            Assert.That(VirtualTextureSystem.GetCpuUploadStagingTextureCountForTesting(), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetGpuUploadStagingTextureCountForTesting(), Is.Zero);

            m_FenceFactory.Handles[0].IsPassed = true;
            UpdateOnce();

            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(0));
            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(4));
        }

        [Test]
        public void Uploads_GpuOnlyBatch_RecordsOneDispatchPerPageAndDoesNotAllocateCpuScratch()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("GpuOnly", maxUploadsPerFrame: 2);
            var producer = new RecordingFinalizerProducer(desc, _ => true);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            producer.ResetRecords();

            IssueFeedback(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 0),
                new VirtualTexturePageCoord(1, 0, 0));

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
            Assert.That(producer.GpuFinalizers, Has.Count.EqualTo(2));
            Assert.That(producer.GpuFinalizers[0].RecordCount, Is.EqualTo(1));
            Assert.That(producer.GpuFinalizers[1].RecordCount, Is.EqualTo(1));
            Assert.That(
                new[] { producer.GpuFinalizers[0].BaseSlice, producer.GpuFinalizers[1].BaseSlice },
                Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(producer.GpuFinalizers[0].StagingTexture, Is.SameAs(producer.GpuFinalizers[1].StagingTexture));
            Assert.That(producer.GpuFinalizers[0].IsDisposed, Is.True);
            Assert.That(producer.GpuFinalizers[1].IsDisposed, Is.True);
            Assert.That(VirtualTextureSystem.GetGpuUploadStagingTextureCountForTesting(), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetCpuUploadStagingTextureCountForTesting(), Is.Zero);
            Assert.That(VirtualTextureSystem.GetUploadScratchPixelCountForTesting(), Is.Zero);

            VirtualTextureStats stats = VirtualTextureStatsRegistry.LastStats;
            Assert.That(stats.GpuProducedPageCount, Is.EqualTo(2));
            Assert.That(stats.GpuDispatchCount, Is.EqualTo(2));
            Assert.That(stats.CpuProducedPageCount, Is.Zero);

            UpdateOnce();
            VirtualTextureStats resetStats = VirtualTextureStatsRegistry.LastStats;
            Assert.That(resetStats.GpuProducedPageCount, Is.Zero);
            Assert.That(resetStats.GpuDispatchCount, Is.Zero);
            Assert.That(resetStats.CpuProducedPageCount, Is.Zero);
        }

        [Test]
        public void Uploads_MixedCpuAndGpuBatch_UsesPackedSlicesAndReportsBothProducerTypes()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("MixedCpuGpu", maxUploadsPerFrame: 2);
            var producer = new RecordingFinalizerProducer(desc, coord => coord.X == 1);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            producer.ResetRecords();

            IssueFeedback(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 0),
                new VirtualTexturePageCoord(1, 0, 0));

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
            Assert.That(producer.CpuFinalizers, Has.Count.EqualTo(1));
            Assert.That(producer.GpuFinalizers, Has.Count.EqualTo(1));
            Assert.That(producer.CpuFinalizers[0].FinalizeRenderCount, Is.EqualTo(1));
            Assert.That(producer.CpuFinalizers[0].FinalizeUploadCount, Is.EqualTo(1));
            Assert.That(producer.CpuFinalizers[0].IsDisposed, Is.True);
            Assert.That(producer.GpuFinalizers[0].RecordCount, Is.EqualTo(1));
            Assert.That(producer.GpuFinalizers[0].BaseSlice, Is.InRange(0, 1));
            Assert.That(producer.GpuFinalizers[0].IsDisposed, Is.True);
            Assert.That(VirtualTextureSystem.GetCpuUploadStagingTextureCountForTesting(), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetGpuUploadStagingTextureCountForTesting(), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetUploadScratchPixelCountForTesting(), Is.GreaterThan(0));

            VirtualTextureStats stats = VirtualTextureStatsRegistry.LastStats;
            Assert.That(stats.GpuProducedPageCount, Is.EqualTo(1));
            Assert.That(stats.GpuDispatchCount, Is.EqualTo(1));
            Assert.That(stats.CpuProducedPageCount, Is.EqualTo(1));
        }

        [Test]
        public void Uploads_GpuFinalizerWithWrongLayerCount_IsRejectedWithoutDispatchOrFence()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("WrongGpuLayerCount");
            var producer = new RecordingFinalizerProducer(desc, _ => true, gpuLayerCount: 2);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            producer.ResetRecords();

            IssueFeedback(spaceId, new VirtualTexturePageCoord(0, 0, 0));

            Assert.That(m_FenceFactory.Handles, Is.Empty);
            Assert.That(producer.GpuFinalizers, Has.Count.EqualTo(1));
            Assert.That(producer.GpuFinalizers[0].RecordCount, Is.Zero);
            Assert.That(producer.GpuFinalizers[0].IsDisposed, Is.True);
            Assert.That(VirtualTextureSystem.GetGpuUploadStagingTextureCountForTesting(), Is.Zero);
            Assert.That(VirtualTextureStatsRegistry.LastStats.SkippedUploadCount, Is.EqualTo(1));
        }

        [Test]
        public void FinalizeUploads_PrioritizesQosWithinSharedPool_WhenQueueExceedsBatchCapacity()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("QueuedUploadQos", maxUploadsPerFrame: 1);
            using var physicalPool = new VTPhysicalPool(
                "QueuedUploadQos",
                VTPhysicalPoolDesc.FromSpaceDesc(desc));
            using var scheduler = new VTUploadScheduler();
            using var commandBuffer = new CommandBuffer();
            var lowFinalizer = new RecordingCpuFinalizer();
            var highFinalizer = new RecordingCpuFinalizer();
            var lowRequest = new VTRequest(
                spaceId: 1,
                pageCoord: new VirtualTexturePageCoord(0, 0, 0),
                physicalPageId: 0,
                generation: 1,
                priority: 64,
                requestFrame: 1,
                cameraPriority: 0,
                isActiveView: false);
            var highRequest = new VTRequest(
                spaceId: 2,
                pageCoord: new VirtualTexturePageCoord(1, 0, 0),
                physicalPageId: 1,
                generation: 1,
                priority: 1,
                requestFrame: 1,
                cameraPriority: 0,
                isActiveView: true);

            scheduler.EnqueueReservedUpload(
                desc.SpaceName,
                desc,
                physicalPool,
                new VTPageUploadPayload(lowRequest, lowFinalizer),
                VTRequestPriorityKey.FromRequest(
                    lowRequest,
                    locked: false,
                    producerPriority: 0));
            scheduler.EnqueueReservedUpload(
                desc.SpaceName,
                desc,
                physicalPool,
                new VTPageUploadPayload(highRequest, highFinalizer),
                VTRequestPriorityKey.FromRequest(
                    highRequest,
                    locked: true,
                    producerPriority: 0));

            Assert.That(scheduler.FinalizeUploads(commandBuffer), Is.True);

            Assert.That(highFinalizer.FinalizeRenderCount, Is.EqualTo(1));
            Assert.That(highFinalizer.FinalizeUploadCount, Is.EqualTo(1));
            Assert.That(lowFinalizer.FinalizeRenderCount, Is.Zero);
            Assert.That(lowFinalizer.FinalizeUploadCount, Is.Zero);
            Assert.That(highFinalizer.IsDisposed, Is.True);
            Assert.That(lowFinalizer.IsDisposed, Is.True);
            Assert.That(scheduler.LastSkippedUploadCount, Is.EqualTo(1));
            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
        }

        [Test]
        public void QueueResident_IsIdempotentLockedAndPrioritizedAheadOfFeedback()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("LockedResidentPriority", maxUploadsPerFrame: 1);
            var producer = new RecordingFinalizerProducer(desc, _ => false);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            producer.ResetRecords();
            var lockedCoord = new VirtualTexturePageCoord(1, 0, 0);
            var feedbackCoord = new VirtualTexturePageCoord(0, 0, 0);

            Assert.That(VirtualTextureSystem.TryQueuePageResident(spaceId, lockedCoord, true, frameIndex: 1), Is.True);
            Assert.That(VirtualTextureSystem.TryQueuePageResident(spaceId, lockedCoord, true, frameIndex: 2), Is.True);
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                lockedCoord,
                out VirtualTexturePageTableEntry pendingEntry), Is.True);
            Assert.That(pendingEntry.PendingUpload, Is.True);
            Assert.That(pendingEntry.Resident, Is.False);
            Assert.That(pendingEntry.Locked, Is.True);

            IssueFeedback(spaceId, feedbackCoord);

            Assert.That(producer.ProducedCoords, Has.Count.EqualTo(1));
            Assert.That(producer.ProducedCoords[0], Is.EqualTo(lockedCoord));
            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
        }

        [Test]
        public void QueueResident_PromotesExistingFeedbackPendingRequestToLockedPriority()
        {
            VirtualTextureSpaceDesc desc = CreateDesc(
                "PromotePendingResident",
                maxUploadsPerFrame: 2);
            var producer = new StatusPageProducer(desc, VTPageRequestStatus.Pending);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            var coord = new VirtualTexturePageCoord(0, 0, 0);
            var otherCoord = new VirtualTexturePageCoord(1, 0, 0);

            IssueFeedback(spaceId, coord, otherCoord);
            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(
                spaceId,
                out IReadOnlyList<VirtualTextureUploadRequest> feedbackRequests), Is.True);
            Assert.That(feedbackRequests, Has.Count.EqualTo(2));
            Assert.That(
                feedbackRequests.Single(request => request.PageCoord.Equals(coord)).Priority,
                Is.LessThan(int.MaxValue));
            uint feedbackRevision = VirtualTextureSystem.GetPendingRequestRevisionForTesting(spaceId);
            int feedbackOrderBuildCount = VirtualTextureSystem.GetPendingOrderCacheBuildCountForTesting(spaceId);

            Assert.That(VirtualTextureSystem.TryQueuePageResident(spaceId, coord, true, frameIndex: 2), Is.True);

            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(
                spaceId,
                out IReadOnlyList<VirtualTextureUploadRequest> lockedRequests), Is.True);
            Assert.That(lockedRequests, Has.Count.EqualTo(2));
            Assert.That(
                lockedRequests.Single(request => request.PageCoord.Equals(coord)).Priority,
                Is.EqualTo(int.MaxValue));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                coord,
                out VirtualTexturePageTableEntry pendingEntry), Is.True);
            Assert.That(pendingEntry.PendingUpload, Is.True);
            Assert.That(pendingEntry.Locked, Is.True);
            Assert.That(VirtualTextureSystem.GetPendingRequestRevisionForTesting(spaceId), Is.GreaterThan(feedbackRevision));

            producer.ResetCounters();
            UpdateOnce();

            Assert.That(
                VirtualTextureSystem.GetPendingOrderCacheBuildCountForTesting(spaceId),
                Is.EqualTo(feedbackOrderBuildCount + 1));
            Assert.That(producer.RequestedCoords, Is.Not.Empty);
            Assert.That(producer.RequestedCoords[0], Is.EqualTo(coord));
            Assert.That(producer.RequestedPriorityKeys[0].Locked, Is.True);
            Assert.That(producer.RequestedPriorityKeys[0].IOTier, Is.EqualTo(VTIOPriorityTier.Critical));
        }

        [Test]
        public void PendingRequestIndex_RemainsValidAfterSwapBackRemoval()
        {
            VirtualTextureSpaceDesc desc = CreateDesc(
                "PendingRequestIndex",
                maxUploadsPerFrame: 3);
            var producer = new StatusPageProducer(desc, VTPageRequestStatus.Pending);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            var firstCoord = new VirtualTexturePageCoord(0, 0, 0);
            var removedCoord = new VirtualTexturePageCoord(1, 0, 0);
            var movedCoord = new VirtualTexturePageCoord(2, 0, 0);

            IssueFeedback(spaceId, firstCoord, removedCoord, movedCoord);
            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(
                spaceId,
                out IReadOnlyList<VirtualTextureUploadRequest> initialRequests), Is.True);
            Assert.That(initialRequests, Has.Count.EqualTo(3));

            VirtualTextureUploadRequest removedRequest = initialRequests.Single(
                request => request.PageCoord.Equals(removedCoord));
            Assert.That(VirtualTextureSystem.CommitUpload(removedRequest), Is.True);

            IssueFeedback(spaceId, movedCoord, movedCoord);

            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(
                spaceId,
                out IReadOnlyList<VirtualTextureUploadRequest> updatedRequests), Is.True);
            Assert.That(updatedRequests, Has.Count.EqualTo(2));
            Assert.That(updatedRequests.Single(
                request => request.PageCoord.Equals(movedCoord)).Priority, Is.EqualTo(2));
            Assert.That(updatedRequests.Single(
                request => request.PageCoord.Equals(firstCoord)).Priority, Is.EqualTo(1));
        }

        [Test]
        public void Uploads_GrowSharedPool_WhenLaterSpaceNeedsLargerBatch()
        {
            int smallSpaceId = VirtualTextureSystem.RegisterAddressSpace(
                CreateDesc("SmallSharedPool", cachePageCount: 5),
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

            m_FenceFactory.Handles[0].IsPassed = true;
            UpdateOnce();

            VirtualTextureSpaceDesc laterDesc = CreateDesc("LaterLargeSharedPool", maxUploadsPerFrame: 3);
            var laterProducer = new StatusPageProducer(laterDesc, VTPageRequestStatus.Available);
            int laterSpaceId = VirtualTextureSystem.RegisterAddressSpace(laterDesc, laterProducer);
            laterProducer.ResetCounters();
            IssueFeedback(
                laterSpaceId,
                new VirtualTexturePageCoord(0, 0, 0),
                new VirtualTexturePageCoord(1, 0, 0),
                new VirtualTexturePageCoord(2, 0, 0));

            Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(3));
            Assert.That(laterProducer.ProduceCount, Is.EqualTo(3));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(laterSpaceId), Is.EqualTo(3));
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

        private static VirtualTextureSpaceDesc CreateDesc(
            string name,
            int maxUploadsPerFrame = 1,
            int cachePageCount = 4,
            int maxResidencyAllocationsPerFrame = 0)
        {
            return new VirtualTextureSpaceDesc(
                name,
                pageSize: 128,
                borderSize: 4,
                virtualPageCountX: 4,
                virtualPageCountY: 4,
                mipCount: 3,
                cachePageCount: cachePageCount,
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: maxUploadsPerFrame,
                feedbackCapacity: 32,
                maxResidencyAllocationsPerFrame: maxResidencyAllocationsPerFrame);
        }

        private static VirtualTextureSpaceDesc CreateLayeredUploadDesc(string name)
        {
            var stackDesc = new VTStackDesc(
                pageSize: 128,
                borderSize: 4,
                cachePageCount: 4,
                layers: new[]
                {
                    new VTLayerDesc(
                        VTLayerSemantic.BaseColor,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        sRGB: false,
                        new Color32(0, 0, 0, 255),
                        physicalGroup: 0),
                    new VTLayerDesc(
                        VTLayerSemantic.Normal,
                        GraphicsFormat.R16G16B16A16_SFloat,
                        sRGB: false,
                        new Color32(128, 128, 255, 255),
                        physicalGroup: 1),
                },
                maxUploadsPerFrame: 1,
                feedbackCapacity: 32);
            return new VirtualTextureSpaceDesc(name, 4, 4, 3, stackDesc);
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

        private static void RunWithTwoCameras(
            string namePrefix,
            System.Action<Camera, Camera, CommandBuffer> action)
        {
            var firstCameraObject = new GameObject($"{namePrefix}A");
            var secondCameraObject = new GameObject($"{namePrefix}B");
            var commandBuffer = new CommandBuffer();

            try
            {
                action(
                    firstCameraObject.AddComponent<Camera>(),
                    secondCameraObject.AddComponent<Camera>(),
                    commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(firstCameraObject);
                Object.DestroyImmediate(secondCameraObject);
            }
        }

        private static void IssueFeedback(
            Camera camera,
            int frameIndex,
            CommandBuffer commandBuffer,
            int spaceId,
            params VirtualTexturePageCoord[] coords)
        {
            commandBuffer.Clear();
            foreach (VirtualTexturePageCoord coord in coords)
            {
                VirtualTextureSystem.InjectCompletedReadbackForTesting(
                    camera,
                    VirtualTextureFeedbackProcessor.EncodeKey(spaceId, coord));
            }

            VirtualTextureSystem.Update(CreateFrameData(camera, frameIndex), commandBuffer);
        }

        private static ContextContainer CreateFrameData(Camera camera, int frameIndex)
        {
            var frameData = new ContextContainer();
            VividCameraData cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.SetCamera(camera);
            cameraData.actualWidth = 512;
            cameraData.actualHeight = 512;
            cameraData.pixelWidth = 512;
            cameraData.pixelHeight = 512;
            cameraData.pixelRect = new Rect(0f, 0f, 512f, 512f);
            cameraData.frameIndex = frameIndex;
            return frameData;
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
