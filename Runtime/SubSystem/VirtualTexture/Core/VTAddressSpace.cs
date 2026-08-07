using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal readonly struct VTPendingUploadCandidate
    {
        internal VTPendingUploadCandidate(
            VTPageTableSpace addressSpace,
            in VTRequest request,
            bool locked,
            int fairnessRank)
        {
            AddressSpace = addressSpace;
            Request = request;
            Locked = locked;
            FairnessRank = fairnessRank;
        }

        internal VTPageTableSpace AddressSpace { get; }

        internal VTRequest Request { get; }

        internal bool Locked { get; }

        internal int FairnessRank { get; }
    }

    internal sealed class VTPageTableSpace : IDisposable, IVTUploadRequestCommitter
    {
        private readonly int[] m_MipOffsets;
        private readonly VTResidencyManager m_ResidencyManager;
        private readonly VTPageTableUpdater m_PageTableUpdater;
        private readonly VirtualTextureSpaceShaderParams m_ShaderParams;
        private readonly Vector4[] m_LayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];
        private readonly IVTPageProducer m_PageProducer;
        private readonly List<VTPendingUploadCandidate> m_LocalUploadCandidates = new();
        private readonly List<IVTPageProducerTask> m_ProducerTasks = new();
        private readonly List<PendingUploadSortEntry> m_PendingUploadSortEntries = new();
        private readonly List<VTRequest> m_SortedPendingRequests = new();
        private readonly List<VTRequest> m_EligiblePendingRequests = new();
        private uint m_CachedPendingRequestRevision;
        private int m_PendingOrderCacheBuildCount;
        private int m_PendingOrderCacheHitCount;
        private bool m_HasPendingOrderCache;
        private Texture2DArray m_ResidentPageStagingTexture;
        private Color32[] m_ResidentPageScratchPixels;
        private IVTPageProducer m_FallbackResidentPageProducer;

        internal VTPageTableSpace(
            int spaceId,
            in VirtualTextureSpaceDesc desc,
            in VTRegisteredProducer producer,
            VTPhysicalPool physicalPool)
        {
            SpaceId = spaceId;
            Descriptor = desc;
            ProducerHandle = producer.Handle;
            ProducerName = producer.Name;
            m_MipOffsets = VirtualTextureSpaceUtility.BuildMipOffsets(desc.VirtualPageCountX, desc.VirtualPageCountY, desc.MipCount);
            TotalPageCount = desc.PageTableEntryCount;
            m_ShaderParams = new VirtualTextureSpaceShaderParams(spaceId, desc, TotalPageCount);
            BuildLayerFallbacks(desc, m_LayerFallbacks);
            PhysicalPool = physicalPool ?? throw new ArgumentNullException(nameof(physicalPool));
            m_ResidencyManager = new VTResidencyManager(
                spaceId,
                ProducerHandle,
                ProducerName,
                desc.SpaceName,
                desc,
                TotalPageCount,
                m_MipOffsets,
                PhysicalPool);
            m_PageTableUpdater = new VTPageTableUpdater(desc.SpaceName, TotalPageCount);
            m_PageProducer = producer.PageProducer;
            BootstrapLowestMip();
            m_PageTableUpdater.Rebuild(desc, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
            m_PageTableUpdater.RefreshBuffer();
        }

        internal int SpaceId { get; }

        internal VTProducerHandle ProducerHandle { get; }

        internal string ProducerName { get; }

        internal VirtualTextureSpaceDesc Descriptor { get; }

        internal VTPhysicalPool PhysicalPool { get; }

        internal VTStackDesc StackDesc => Descriptor.StackDesc;

        internal int TotalPageCount { get; }

        internal int ResidentPageCount => m_ResidencyManager.ResidentPageCount;

        internal int FreePageCount => m_ResidencyManager.FreePageCount;

        internal int PendingRequestCount => m_ResidencyManager.PendingRequestCount;

        internal uint PendingRequestRevision => m_ResidencyManager.PendingRequestRevision;

        internal int PendingOrderCacheBuildCount => m_PendingOrderCacheBuildCount;

        internal int PendingOrderCacheHitCount => m_PendingOrderCacheHitCount;

        internal int PageTableRebuildCount => m_PageTableUpdater.RebuildCount;

        internal int PageTableLastRecomputedEntryCount => m_PageTableUpdater.LastRecomputedEntryCount;

        internal int PageTableLastUploadedEntryCount => m_PageTableUpdater.LastUploadedEntryCount;

        internal int PageTableSparseUploadCount => m_PageTableUpdater.SparseUploadCount;

        internal int PageTableFullUploadCount => m_PageTableUpdater.FullUploadCount;

        internal IReadOnlyList<VTRequest> PendingRequests => m_ResidencyManager.PendingRequests;

        internal int ResidencyClassificationCapacity => m_ResidencyManager.ClassificationCapacity;

        internal bool LastResidencyClassificationUsedParallelJob =>
            m_ResidencyManager.LastClassificationUsedParallelJob;

        internal int[] MipOffsets => m_MipOffsets;

        internal bool RequiresNewPhysicalPage(in VirtualTexturePageCoord coord)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(Descriptor, coord))
                return false;

            int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(
                Descriptor,
                m_MipOffsets,
                coord);
            VTPageResidencyState pageState = m_ResidencyManager.GetPageState(pageIndex);
            return !pageState.Resident && !pageState.PendingUpload;
        }

        internal VTResidencyProcessResult ProcessRequests(
            NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests,
            VirtualTextureViewId activeViewId,
            Vector2Int prefetchBias,
            int frameIndex,
            int maxNewRequests = int.MaxValue,
            bool allowNeighborPrefetch = true,
            bool rebuildPageTable = true)
        {
            VTResidencyProcessResult result = m_ResidencyManager.ProcessRequests(
                Descriptor,
                m_MipOffsets,
                SpaceId,
                requests,
                activeViewId,
                prefetchBias,
                frameIndex,
                maxNewRequests,
                allowNeighborPrefetch);

            if (!rebuildPageTable)
                return result;

            if (result.PageTableChanged)
            {
                m_ResidencyManager.ConsumePageTableDirtyFlag();
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTexturePageTableRebuildMarker.Auto())
                    m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
                m_ResidencyManager.ClearDirtyPageTableUpdates();
            }
            else
            {
                RebuildPageTableIfDirty();
            }

            return result;
        }

        internal void CollectPendingUploads(VTUploadScheduler uploadScheduler, CommandBuffer cmd)
        {
            m_LocalUploadCandidates.Clear();
            CollectPendingUploadCandidates(uploadScheduler, fairnessRank: 0, m_LocalUploadCandidates);

            int skippedUploadCount = 0;
            for (int candidateIndex = 0; candidateIndex < m_LocalUploadCandidates.Count; candidateIndex++)
            {
                if (!TrySchedulePendingUpload(uploadScheduler, cmd, m_LocalUploadCandidates[candidateIndex].Request))
                    skippedUploadCount += 1;
            }

            uploadScheduler?.AddSkippedUploadCount(skippedUploadCount);
            m_LocalUploadCandidates.Clear();
        }

        internal bool TryCommitRequest(in VTRequest request)
        {
            return TryCommitRequestInternal(request, rebuildPageTable: true);
        }

        internal bool TrySetPageLocked(in VirtualTexturePageCoord coord, bool locked)
        {
            if (!m_ResidencyManager.TrySetPageLocked(Descriptor, m_MipOffsets, coord, locked))
                return false;

            m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
            return true;
        }

        internal bool TryQueuePageResident(
            in VirtualTexturePageCoord coord,
            bool locked,
            int frameIndex)
        {
            if (!m_ResidencyManager.TryQueuePageResident(
                    Descriptor,
                    m_MipOffsets,
                    SpaceId,
                    coord,
                    locked,
                    frameIndex))
            {
                return false;
            }

            return true;
        }

        internal bool TryMakePageResident(
            in VirtualTexturePageCoord coord,
            bool locked,
            int frameIndex)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(Descriptor, coord))
                return false;

            if (m_PageTableUpdater.TryGetEntry(Descriptor, m_MipOffsets, coord, out VirtualTexturePageTableEntry entry)
                && entry.Resident)
            {
                bool lockUpdated = m_ResidencyManager.TrySetPageLocked(
                    Descriptor,
                    m_MipOffsets,
                    coord,
                    locked);
                RebuildAndRefreshPageTable();
                return lockUpdated;
            }

            if (!m_ResidencyManager.TryAllocateResidentPage(
                    Descriptor,
                    m_MipOffsets,
                    SpaceId,
                    coord,
                    VirtualTextureViewId.Invalid,
                    frameIndex,
                    locked,
                    out VTRequest request))
            {
                return false;
            }

            try
            {
                if (!TryUploadResidentPage(request, allowFallbackProducer: false))
                {
                    RollbackResidentPage(coord);
                    return false;
                }
            }
            catch
            {
                RollbackResidentPage(coord);
                throw;
            }

            m_ResidencyManager.TrySetPageLocked(Descriptor, m_MipOffsets, coord, locked);
            RebuildAndRefreshPageTable();
            return true;
        }

        internal bool TryGetPageTableEntry(in VirtualTexturePageCoord coord, out VirtualTexturePageTableEntry entry)
        {
            RebuildPageTableIfDirty();
            return m_PageTableUpdater.TryGetEntry(Descriptor, m_MipOffsets, coord, out entry);
        }

        internal void RefreshPageTableBuffer()
        {
            m_PageTableUpdater.RefreshBuffer();
        }

        internal bool RebuildPageTableIfDirty()
        {
            if (!m_ResidencyManager.ConsumePageTableDirtyFlag())
                return false;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTexturePageTableRebuildMarker.Auto())
                m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
            return true;
        }

        internal int FlushRegion(int mip, RectInt pageRegion)
        {
            int flushedCount = m_ResidencyManager.FlushRegion(mip, pageRegion);
            if (flushedCount <= 0)
                return 0;

            m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
            return flushedCount;
        }

        internal int FlushRegions(IReadOnlyList<VTPageRegion> pageRegions)
        {
            if (pageRegions == null || pageRegions.Count == 0)
                return 0;

            int flushedCount = 0;
            for (int regionIndex = 0; regionIndex < pageRegions.Count; regionIndex++)
            {
                VTPageRegion region = pageRegions[regionIndex];
                flushedCount += m_ResidencyManager.FlushRegion(region.Mip, region.PageRegion);
            }

            if (flushedCount <= 0)
                return 0;

            m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
            return flushedCount;
        }

        internal VirtualTextureSpaceBinding CreateBinding(
            int allocationId,
            bool privateSpace,
            ComputeBuffer feedbackRequests,
            ComputeBuffer feedbackCounter)
        {
            return new VirtualTextureSpaceBinding(
                -1,
                allocationId,
                privateSpace,
                SpaceId,
                Descriptor.SpaceName,
                ProducerHandle,
                m_PageTableUpdater.PageTableBuffer,
                m_ResidencyManager.PhysicalPool.Textures,
                feedbackRequests,
                feedbackCounter,
                m_ShaderParams,
                m_MipOffsets,
                m_LayerFallbacks);
        }

        public void Dispose()
        {
            CoreUtils.Destroy(m_ResidentPageStagingTexture);
            m_ResidentPageStagingTexture = null;
            m_ResidentPageScratchPixels = null;
            m_FallbackResidentPageProducer = null;
            m_PageTableUpdater.Dispose();
            m_ResidencyManager.Dispose();
        }

        private void BootstrapLowestMip()
        {
            int lowestMip = Descriptor.MipCount - 1;
            int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(Descriptor.VirtualPageCountX, lowestMip);
            int pageCountY = VirtualTextureSpaceUtility.GetPageCountY(Descriptor.VirtualPageCountY, lowestMip);
            for (int y = 0; y < pageCountY; y++)
            {
                for (int x = 0; x < pageCountX; x++)
                {
                    var coord = new VirtualTexturePageCoord(x, y, lowestMip);
                    if (!m_ResidencyManager.TryAllocateResidentPage(
                            Descriptor,
                            m_MipOffsets,
                            SpaceId,
                            coord,
                            VirtualTextureViewId.Invalid,
                            frameIndex: 0,
                            locked: true,
                            out VTRequest request))
                    {
                        throw new InvalidOperationException(
                            $"[VividRP] Failed to seed VT lowest mip page {coord} for space '{Descriptor.SpaceName}'.");
                    }

                    if (!TryUploadResidentPage(request, allowFallbackProducer: true))
                    {
                        throw new InvalidOperationException(
                            $"[VividRP] Failed to produce VT lowest mip page {coord} for space '{Descriptor.SpaceName}'.");
                    }
                }
            }
        }

        private bool TryUploadResidentPage(in VTRequest request, bool allowFallbackProducer)
        {
            bool hasCpuPayload = TryProduceUploadPayload(m_PageProducer, request, out VTPageUploadPayload payload)
                                 && payload.Finalizer is IVTPageFinalizer;
            if (!hasCpuPayload)
            {
                payload.Finalizer?.Dispose();
                payload = default;
                if (!allowFallbackProducer)
                    return false;

                m_FallbackResidentPageProducer ??=
                    VTRuntimeProducerUtility.CreateAdapter(VTProceduralPageProducer.Instance, Descriptor);
                if (!TryProduceUploadPayload(m_FallbackResidentPageProducer, request, out payload))
                    return false;
            }

            EnsureResidentPageUploadStorage();
            try
            {
                VTPageUploadUtility.FinalizePayloadRender(payload, null);
                for (int layerIndex = 0; layerIndex < StackDesc.LayerCount; layerIndex++)
                {
                    VTPageUploadUtility.WritePayloadLayerToStagingTexture(
                        m_ResidentPageStagingTexture,
                        layerIndex,
                        m_ResidentPageScratchPixels,
                        payload,
                        layerIndex);
                }
            }
            finally
            {
                payload.Finalizer?.Dispose();
            }

            m_ResidentPageStagingTexture.Apply(false, false);
            for (int layerIndex = 0; layerIndex < StackDesc.LayerCount; layerIndex++)
            {
                int physicalGroup = PhysicalPool.GetLayerPhysicalGroup(layerIndex);
                Texture2DArray physicalCache = PhysicalPool.GetTextureForGroup(physicalGroup);
                if (physicalCache == null)
                    continue;

                int groupLayerCount = Mathf.Max(1, PhysicalPool.GetGroupLayerCount(physicalGroup));
                int physicalLayerIndex = PhysicalPool.GetLayerPhysicalLayerIndex(layerIndex);
                int destinationSlice = request.PhysicalPageId * groupLayerCount + physicalLayerIndex;
                if (m_ResidentPageStagingTexture.graphicsFormat == physicalCache.graphicsFormat)
                {
                    Graphics.CopyTexture(
                        m_ResidentPageStagingTexture,
                        layerIndex,
                        0,
                        physicalCache,
                        destinationSlice,
                        0);
                    continue;
                }

                if (!Graphics.ConvertTexture(
                    m_ResidentPageStagingTexture,
                    layerIndex,
                    physicalCache,
                    destinationSlice))
                {
                    throw new InvalidOperationException(
                        $"[VividRP] Failed to convert VT bootstrap layer {layerIndex} into " +
                        $"physical group {physicalGroup} for space '{Descriptor.SpaceName}'.");
                }
            }

            return true;
        }

        private void EnsureResidentPageUploadStorage()
        {
            m_ResidentPageStagingTexture ??= VTPageUploadUtility.CreateStagingTexture(
                Descriptor.SpaceName,
                Descriptor.PhysicalPageSize,
                StackDesc.LayerCount,
                "ResidentPage");
            m_ResidentPageScratchPixels ??=
                new Color32[Descriptor.PhysicalPageSize * Descriptor.PhysicalPageSize];
        }

        private void RollbackResidentPage(in VirtualTexturePageCoord coord)
        {
            m_ResidencyManager.FlushRegion(coord.Mip, new RectInt(coord.X, coord.Y, 1, 1));
            RebuildAndRefreshPageTable();
        }

        private void RebuildAndRefreshPageTable()
        {
            m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
            m_PageTableUpdater.RefreshBuffer();
        }

        private static void BuildLayerFallbacks(in VirtualTextureSpaceDesc desc, Vector4[] output)
        {
            if (output == null)
                return;

            for (int layerIndex = 0; layerIndex < output.Length; layerIndex++)
            {
                Color32 fallbackColor = layerIndex < desc.StackDesc.LayerCount
                    ? desc.StackDesc.GetLayer(layerIndex).FallbackColor
                    : new Color32(0, 0, 0, 255);
                output[layerIndex] = new Vector4(
                    fallbackColor.r / 255f,
                    fallbackColor.g / 255f,
                    fallbackColor.b / 255f,
                    fallbackColor.a / 255f);
            }
        }

        internal void CollectPendingUploadCandidates(
            VTUploadScheduler uploadScheduler,
            int fairnessRank,
            List<VTPendingUploadCandidate> output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            IReadOnlyList<VTRequest> pendingRequests = PendingRequests;
            if (pendingRequests == null || pendingRequests.Count == 0)
            {
                RetireProducerRequests(Array.Empty<VTRequest>());
                return;
            }

            if (uploadScheduler == null || m_PageProducer == null || !uploadScheduler.IsEnabled)
            {
                uploadScheduler?.AddSkippedUploadCount(pendingRequests.Count);
                return;
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingRetireMarker.Auto())
                RetireProducerRequests(pendingRequests);

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingGatherTasksMarker.Auto())
            {
                m_ProducerTasks.Clear();
                m_PageProducer.GatherTasks(m_ProducerTasks);
                m_ProducerTasks.Clear();
            }

            IReadOnlyList<VTRequest> orderedRequests;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingOrderMarker.Auto())
                orderedRequests = GetOrderedPendingRequests(pendingRequests);
            int duplicateUploadCount;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingInFlightMarker.Auto())
            {
                duplicateUploadCount = uploadScheduler.FilterInFlightRequests(
                    orderedRequests,
                    m_EligiblePendingRequests);
            }

            uploadScheduler.AddSkippedUploadCount(duplicateUploadCount);
            for (int requestIndex = 0; requestIndex < m_EligiblePendingRequests.Count; requestIndex++)
            {
                VTRequest request = m_EligiblePendingRequests[requestIndex];
                bool locked = m_ResidencyManager.IsPageLocked(
                    Descriptor,
                    m_MipOffsets,
                    request.PageCoord);
                output.Add(new VTPendingUploadCandidate(this, request, locked, fairnessRank));
            }
        }

        internal bool TrySchedulePendingUpload(
            VTUploadScheduler uploadScheduler,
            CommandBuffer cmd,
            in VTRequest request)
        {
            if (uploadScheduler == null || m_PageProducer == null || cmd == null || !uploadScheduler.IsEnabled)
                return false;

            VTPageRequestStatus status;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingRequestPageMarker.Auto())
                status = m_PageProducer.RequestPageData(Descriptor, request);
            if (status != VTPageRequestStatus.Available)
            {
                if (status == VTPageRequestStatus.Invalid)
                    m_PageProducer.CancelRequest(Descriptor, request);

                return false;
            }

            if (!uploadScheduler.TryReserveUpload(Descriptor.SpaceName, Descriptor))
                return false;

            IVTPageUploadFinalizer finalizer;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingProducePageMarker.Auto())
                finalizer = m_PageProducer.ProducePageData(Descriptor, request);
            if (finalizer == null)
            {
                uploadScheduler.ReleaseUploadReservation(Descriptor);
                return false;
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingEnqueueMarker.Auto())
            {
                uploadScheduler.EnqueueReservedUpload(
                    Descriptor.SpaceName,
                    Descriptor,
                    m_ResidencyManager.PhysicalPool,
                    new VTPageUploadPayload(request, finalizer));
            }

            return true;
        }

        private bool TryProduceUploadPayload(
            IVTPageProducer producer,
            in VTRequest request,
            out VTPageUploadPayload payload)
        {
            payload = default;
            if (producer == null)
                return false;

            if (producer.RequestPageData(Descriptor, request) != VTPageRequestStatus.Available)
                return false;

            IVTPageUploadFinalizer finalizer = producer.ProducePageData(Descriptor, request);
            if (finalizer == null)
                return false;

            payload = new VTPageUploadPayload(request, finalizer);
            return true;
        }

        private void RetireProducerRequests(IReadOnlyList<VTRequest> liveRequests)
        {
            if (m_PageProducer is IVTPageRequestRetirement retirement)
                retirement.RetireRequests(liveRequests);
        }

        private IReadOnlyList<VTRequest> GetOrderedPendingRequests(IReadOnlyList<VTRequest> pendingRequests)
        {
            if (pendingRequests == null || pendingRequests.Count == 0)
                return Array.Empty<VTRequest>();
            if (pendingRequests.Count == 1)
                return pendingRequests;

            uint pendingRequestRevision = m_ResidencyManager.PendingRequestRevision;
            if (m_HasPendingOrderCache
                && m_CachedPendingRequestRevision == pendingRequestRevision
                && m_SortedPendingRequests.Count == pendingRequests.Count)
            {
                m_PendingOrderCacheHitCount += 1;
                return m_SortedPendingRequests;
            }

            m_PendingUploadSortEntries.Clear();
            m_SortedPendingRequests.Clear();
            for (int requestIndex = 0; requestIndex < pendingRequests.Count; requestIndex++)
            {
                VTRequest request = pendingRequests[requestIndex];
                bool locked = m_ResidencyManager.IsPageLocked(
                    Descriptor,
                    m_MipOffsets,
                    request.PageCoord);
                m_PendingUploadSortEntries.Add(new PendingUploadSortEntry(request, locked));
            }

            if (m_PendingUploadSortEntries.Count > 1)
                m_PendingUploadSortEntries.Sort(PendingUploadRequestComparer.Instance);

            for (int entryIndex = 0; entryIndex < m_PendingUploadSortEntries.Count; entryIndex++)
                m_SortedPendingRequests.Add(m_PendingUploadSortEntries[entryIndex].Request);

            m_CachedPendingRequestRevision = pendingRequestRevision;
            m_HasPendingOrderCache = true;
            m_PendingOrderCacheBuildCount += 1;
            return m_SortedPendingRequests;
        }

        private readonly struct PendingUploadSortEntry
        {
            internal PendingUploadSortEntry(in VTRequest request, bool locked)
            {
                Request = request;
                Locked = locked;
                IsActiveView = request.IsActiveView;
                CameraPriority = request.CameraPriority;
                Priority = request.Priority;
                RequestFrame = request.RequestFrame;
                Mip = request.PageCoord.Mip;
                Y = request.PageCoord.Y;
                X = request.PageCoord.X;
            }

            internal VTRequest Request { get; }

            internal bool Locked { get; }

            internal bool IsActiveView { get; }

            internal int CameraPriority { get; }

            internal int Priority { get; }

            internal int RequestFrame { get; }

            internal int Mip { get; }

            internal int Y { get; }

            internal int X { get; }
        }

        private sealed class PendingUploadRequestComparer : IComparer<PendingUploadSortEntry>
        {
            internal static readonly PendingUploadRequestComparer Instance = new();

            private PendingUploadRequestComparer()
            {
            }

            public int Compare(PendingUploadSortEntry left, PendingUploadSortEntry right)
            {
                if (left.Locked != right.Locked)
                    return left.Locked ? -1 : 1;

                if (left.IsActiveView != right.IsActiveView)
                    return left.IsActiveView ? -1 : 1;

                int cameraCompare = left.CameraPriority.CompareTo(right.CameraPriority);
                if (cameraCompare != 0)
                    return cameraCompare;

                int priorityCompare = right.Priority.CompareTo(left.Priority);
                if (priorityCompare != 0)
                    return priorityCompare;

                int frameCompare = left.RequestFrame.CompareTo(right.RequestFrame);
                if (frameCompare != 0)
                    return frameCompare;

                int mipCompare = left.Mip.CompareTo(right.Mip);
                if (mipCompare != 0)
                    return mipCompare;

                int yCompare = left.Y.CompareTo(right.Y);
                if (yCompare != 0)
                    return yCompare;

                return left.X.CompareTo(right.X);
            }
        }

        private bool TryCommitRequestInternal(in VTRequest request, bool rebuildPageTable)
        {
            if (request.SpaceId != SpaceId)
                return false;

            if (!m_ResidencyManager.TryCommitRequest(Descriptor, m_MipOffsets, request))
                return false;

            if (rebuildPageTable)
            {
                m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
                m_ResidencyManager.ClearDirtyPageTableUpdates();
            }

            return true;
        }

        bool IVTUploadRequestCommitter.TryCommitUpload(in VTRequest request)
        {
            return TryCommitRequestInternal(request, rebuildPageTable: false);
        }
    }
}
