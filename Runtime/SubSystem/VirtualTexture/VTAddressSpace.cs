using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VTAddressSpace : IDisposable, IVTUploadRequestCommitter
    {
        private readonly int[] m_MipOffsets;
        private readonly VTResidencyManager m_ResidencyManager;
        private readonly VTPageTableUpdater m_PageTableUpdater;
        private readonly VirtualTextureSpaceShaderParams m_ShaderParams;
        private readonly Vector4[] m_LayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];
        private readonly IVTPageProducer m_PageProducer;
        private readonly List<VTPageUploadPayload> m_UploadPayloads = new();
        private readonly List<IVTPageProducerTask> m_ProducerTasks = new();
        private readonly List<VTRequest> m_SortedPendingRequests = new();

        internal VTAddressSpace(
            int allocationId,
            int spaceId,
            in VirtualTextureSpaceDesc desc,
            in VTRegisteredProducer producer,
            VTPhysicalPool physicalPool)
        {
            AllocationId = allocationId;
            SpaceId = spaceId;
            Descriptor = desc;
            ProducerHandle = producer.Handle;
            ProducerName = producer.Name;
            m_MipOffsets = VirtualTextureSpaceUtility.BuildMipOffsets(desc.VirtualPageCountX, desc.VirtualPageCountY, desc.MipCount);
            TotalPageCount = VirtualTextureSpaceUtility.GetTotalPageCount(desc.VirtualPageCountX, desc.VirtualPageCountY, desc.MipCount);
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

        internal int AllocationId { get; }

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

        internal IReadOnlyList<VTRequest> PendingRequests => m_ResidencyManager.PendingRequests;

        internal int[] MipOffsets => m_MipOffsets;

        internal VTResidencyProcessResult ProcessRequests(
            IReadOnlyList<VirtualTextureAggregatedFeedbackRequest> requests,
            VirtualTextureViewId activeViewId,
            Vector2Int prefetchBias,
            int frameIndex)
        {
            VTResidencyProcessResult result = m_ResidencyManager.ProcessRequests(
                Descriptor,
                m_MipOffsets,
                SpaceId,
                requests,
                activeViewId,
                prefetchBias,
                frameIndex);

            if (result.PageTableChanged)
            {
                m_ResidencyManager.ConsumePageTableDirtyFlag();
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
            SchedulePendingUploads(uploadScheduler, cmd);
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

        internal bool TryGetPageTableEntry(in VirtualTexturePageCoord coord, out VirtualTexturePageTableEntry entry)
        {
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

        internal VirtualTextureSpaceBinding CreateBinding(
            ComputeBuffer feedbackRequests,
            ComputeBuffer feedbackCounter)
        {
            return new VirtualTextureSpaceBinding(
                -1,
                AllocationId,
                SpaceId,
                Descriptor.SpaceName,
                ProducerHandle,
                m_PageTableUpdater.PageTableBuffer,
                m_ResidencyManager.PhysicalCache,
                feedbackRequests,
                feedbackCounter,
                m_ShaderParams,
                m_MipOffsets,
                m_LayerFallbacks);
        }

        public void Dispose()
        {
            m_PageTableUpdater.Dispose();
            m_ResidencyManager.Dispose();
        }

        private void BootstrapLowestMip()
        {
            int lowestMip = Descriptor.MipCount - 1;
            int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(Descriptor.VirtualPageCountX, lowestMip);
            int pageCountY = VirtualTextureSpaceUtility.GetPageCountY(Descriptor.VirtualPageCountY, lowestMip);
            IVTPageProducer bootstrapProducer = m_PageProducer;
            IVTPageProducer fallbackBootstrapProducer = null;
            Texture2DArray bootstrapTexture = VTPageUploadUtility.CreateStagingTexture(
                Descriptor.SpaceName,
                Descriptor.PhysicalPageSize,
                StackDesc.LayerCount,
                "Bootstrap");
            var scratchPixels = new Color32[Descriptor.PhysicalPageSize * Descriptor.PhysicalPageSize];

            try
            {
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

                        if (!TryProduceUploadPayload(bootstrapProducer, request, out VTPageUploadPayload payload))
                        {
                            fallbackBootstrapProducer ??=
                                VTRuntimeProducerUtility.CreateAdapter(VTProceduralPageProducer.Instance, Descriptor);

                            if (!TryProduceUploadPayload(fallbackBootstrapProducer, request, out payload))
                            {
                                throw new InvalidOperationException(
                                    $"[VividRP] Failed to produce VT lowest mip page {coord} for space '{Descriptor.SpaceName}'.");
                            }
                        }

                        try
                        {
                            VTPageUploadUtility.FinalizePayloadRender(payload, null);
                            for (int layerIndex = 0; layerIndex < StackDesc.LayerCount; layerIndex++)
                            {
                                VTPageUploadUtility.WritePayloadLayerToStagingTexture(
                                    bootstrapTexture,
                                    layerIndex,
                                    scratchPixels,
                                    payload,
                                    layerIndex);
                            }
                        }
                        finally
                        {
                            payload.Finalizer?.Dispose();
                        }

                        bootstrapTexture.Apply(false, false);
                        int destinationBaseSlice = request.PhysicalPageId * StackDesc.LayerCount;
                        for (int layerIndex = 0; layerIndex < StackDesc.LayerCount; layerIndex++)
                        {
                            Graphics.CopyTexture(
                                bootstrapTexture,
                                layerIndex,
                                0,
                                m_ResidencyManager.PhysicalCache,
                                destinationBaseSlice + layerIndex,
                                0);
                        }
                    }
                }
            }
            finally
            {
                CoreUtils.Destroy(bootstrapTexture);
            }
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

        private void SchedulePendingUploads(VTUploadScheduler uploadScheduler, CommandBuffer cmd)
        {
            m_UploadPayloads.Clear();

            IReadOnlyList<VTRequest> pendingRequests = PendingRequests;
            if (pendingRequests == null || pendingRequests.Count == 0)
            {
                RetireProducerRequests(Array.Empty<VTRequest>());
                return;
            }

            if (uploadScheduler == null || m_PageProducer == null || cmd == null || !uploadScheduler.IsEnabled)
            {
                uploadScheduler?.AddSkippedUploadCount(pendingRequests.Count);
                return;
            }

            RetireProducerRequests(pendingRequests);
            m_ProducerTasks.Clear();
            m_PageProducer.GatherTasks(m_ProducerTasks);
            m_ProducerTasks.Clear();

            uploadScheduler.CountInFlightDuplicates(pendingRequests);
            int capacity = uploadScheduler.GetAvailableBatchCapacity(Descriptor.SpaceName, Descriptor);
            if (capacity <= 0)
            {
                uploadScheduler.AddSkippedUploadCount(pendingRequests.Count);
                return;
            }

            int skippedUploadCount = 0;
            IReadOnlyList<VTRequest> orderedRequests = GetOrderedPendingRequests(pendingRequests);
            for (int requestIndex = 0; requestIndex < orderedRequests.Count; requestIndex++)
            {
                VTRequest request = orderedRequests[requestIndex];
                if (uploadScheduler.IsRequestInFlight(request))
                {
                    skippedUploadCount += 1;
                    continue;
                }

                if (m_UploadPayloads.Count >= capacity)
                {
                    skippedUploadCount += 1;
                    continue;
                }

                VTPageRequestStatus status = m_PageProducer.RequestPageData(Descriptor, request);
                if (status != VTPageRequestStatus.Available)
                {
                    if (status == VTPageRequestStatus.Invalid)
                        m_PageProducer.CancelRequest(Descriptor, request);

                    skippedUploadCount += 1;
                    continue;
                }

                if (!uploadScheduler.TryReserveUpload(Descriptor.SpaceName, Descriptor))
                {
                    skippedUploadCount += 1;
                    continue;
                }

                IVTPageFinalizer finalizer = m_PageProducer.ProducePageData(Descriptor, request);
                if (finalizer == null)
                {
                    uploadScheduler.ReleaseUploadReservation(Descriptor);
                    skippedUploadCount += 1;
                    continue;
                }

                m_UploadPayloads.Add(new VTPageUploadPayload(request, finalizer));
            }

            uploadScheduler.AddSkippedUploadCount(skippedUploadCount);

            for (int payloadIndex = 0; payloadIndex < m_UploadPayloads.Count; payloadIndex++)
            {
                uploadScheduler.EnqueueReservedUpload(
                    Descriptor.SpaceName,
                    Descriptor,
                    m_ResidencyManager.PhysicalCache,
                    m_UploadPayloads[payloadIndex]);
            }

            m_UploadPayloads.Clear();
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

            IVTPageFinalizer finalizer = producer.ProducePageData(Descriptor, request);
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
            if (pendingRequests == null || pendingRequests.Count <= 1)
                return pendingRequests ?? Array.Empty<VTRequest>();

            m_SortedPendingRequests.Clear();
            for (int requestIndex = 0; requestIndex < pendingRequests.Count; requestIndex++)
                m_SortedPendingRequests.Add(pendingRequests[requestIndex]);

            m_SortedPendingRequests.Sort(PendingUploadRequestComparer.Instance);
            return m_SortedPendingRequests;
        }

        private sealed class PendingUploadRequestComparer : IComparer<VTRequest>
        {
            internal static readonly PendingUploadRequestComparer Instance = new();

            private PendingUploadRequestComparer()
            {
            }

            public int Compare(VTRequest left, VTRequest right)
            {
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

                int mipCompare = left.PageCoord.Mip.CompareTo(right.PageCoord.Mip);
                if (mipCompare != 0)
                    return mipCompare;

                int yCompare = left.PageCoord.Y.CompareTo(right.PageCoord.Y);
                if (yCompare != 0)
                    return yCompare;

                return left.PageCoord.X.CompareTo(right.PageCoord.X);
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
            return TryCommitRequestInternal(request, rebuildPageTable: true);
        }
    }
}
