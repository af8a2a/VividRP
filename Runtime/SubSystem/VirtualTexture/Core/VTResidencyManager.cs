using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal readonly struct VTPageResidencyState
    {
        internal VTPageResidencyState(
            int physicalPageId,
            int generation,
            int lastAllocationFrame,
            bool resident,
            bool pendingUpload,
            bool locked)
        {
            PhysicalPageId = physicalPageId;
            Generation = generation;
            LastAllocationFrame = lastAllocationFrame;
            Resident = resident;
            PendingUpload = pendingUpload;
            Locked = locked;
        }

        internal int PhysicalPageId { get; }

        internal int Generation { get; }

        internal int LastAllocationFrame { get; }

        internal bool Resident { get; }

        internal bool PendingUpload { get; }

        internal bool Locked { get; }
    }

    internal readonly struct VTResidencyProcessResult
    {
        internal VTResidencyProcessResult(
            int evictionCount,
            bool pageTableChanged,
            int pendingMipGapSum = 0,
            int pendingMipGapMax = 0,
            int pendingMipGapSampleCount = 0,
            int prefetchRequestCount = 0)
        {
            EvictionCount = evictionCount;
            PageTableChanged = pageTableChanged;
            PendingMipGapSum = pendingMipGapSum;
            PendingMipGapMax = pendingMipGapMax;
            PendingMipGapSampleCount = pendingMipGapSampleCount;
            PrefetchRequestCount = prefetchRequestCount;
        }

        internal int EvictionCount { get; }

        internal bool PageTableChanged { get; }

        internal int PendingMipGapSum { get; }

        internal int PendingMipGapMax { get; }

        internal int PendingMipGapSampleCount { get; }

        internal int PrefetchRequestCount { get; }
    }

    internal sealed class VTResidencyManager : IDisposable, IVTPhysicalPoolOwner
    {
        private static readonly Vector2Int[] s_NeighborOffsets =
        {
            new(-1, 0),
            new(1, 0),
            new(0, -1),
            new(0, 1),
        };

        private struct VTPageRuntimeState
        {
            public int PhysicalPageId;
            public int Generation;
            public int LastAllocationFrame;
            public bool Resident;
            public bool PendingUpload;
            public bool Locked;
        }

        private readonly int m_SpaceId;
        private readonly VTProducerHandle m_ProducerHandle;
        private readonly string m_ProducerName;
        private readonly VirtualTextureSpaceDesc m_Desc;
        private readonly int[] m_MipOffsets;
        private readonly VTPageRuntimeState[] m_PageStates;
        private readonly int[] m_PageMips;
        private readonly VTPhysicalPool m_PhysicalPool;
        private readonly List<VTRequest> m_PendingRequests = new();
        private readonly List<int> m_DirtyPageTableUpdates = new();

        private int m_ResidentPageCount;
        private bool m_PageTableDirty;

        internal VTResidencyManager(
            int spaceId,
            VTProducerHandle producerHandle,
            string producerName,
            string spaceName,
            in VirtualTextureSpaceDesc desc,
            int totalPageCount,
            int[] mipOffsets,
            VTPhysicalPool physicalPool)
        {
            m_SpaceId = spaceId;
            m_ProducerHandle = producerHandle;
            m_ProducerName = producerName;
            m_Desc = desc;
            m_MipOffsets = mipOffsets;
            m_PhysicalPool = physicalPool ?? throw new ArgumentNullException(nameof(physicalPool));
            m_PageStates = new VTPageRuntimeState[totalPageCount];
            for (int pageIndex = 0; pageIndex < m_PageStates.Length; pageIndex++)
                m_PageStates[pageIndex].PhysicalPageId = -1;

            m_PageMips = BuildPageMipTable(desc, mipOffsets, totalPageCount);
        }

        public int SpaceId => m_SpaceId;

        internal int ResidentPageCount => m_ResidentPageCount;

        internal int FreePageCount => m_PhysicalPool.FreePageCount;

        internal int PendingRequestCount => m_PendingRequests.Count;

        internal VTPhysicalPool PhysicalPool => m_PhysicalPool;

        internal Texture2DArray PhysicalCache => m_PhysicalPool.Texture;

        internal IReadOnlyList<VTRequest> PendingRequests => m_PendingRequests;

        internal IReadOnlyList<int> DirtyPageTableUpdates => m_DirtyPageTableUpdates;

        internal bool TryAllocateResidentPage(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int spaceId,
            in VirtualTexturePageCoord coord,
            VirtualTextureViewId viewId,
            int frameIndex,
            bool locked,
            out VTRequest request)
        {
            request = default;
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, coord))
                return false;

            int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, coord);
            VTPageRuntimeState pageState = m_PageStates[pageIndex];
            if (pageState.Resident)
            {
                request = new VTRequest(
                    spaceId,
                    coord,
                    pageState.PhysicalPageId,
                    pageState.Generation,
                    int.MaxValue,
                    frameIndex);
                m_PhysicalPool.Touch(pageState.PhysicalPageId, viewId, frameIndex, HasViewAffinity(viewId));
                return true;
            }

            if (pageState.PendingUpload)
                return false;

            if (m_PhysicalPool.TryAttachResidentPage(
                    this,
                    m_ProducerHandle,
                    m_ProducerName,
                    pageIndex,
                    coord,
                    viewId,
                    frameIndex,
                    locked,
                    out int sharedPhysicalPageId,
                    out int sharedGeneration))
            {
                pageState.PhysicalPageId = sharedPhysicalPageId;
                pageState.Generation = sharedGeneration;
                pageState.LastAllocationFrame = frameIndex;
                pageState.PendingUpload = false;
                pageState.Resident = true;
                pageState.Locked = locked;
                m_PageStates[pageIndex] = pageState;
                m_ResidentPageCount += 1;
                MarkPageTableDirty(pageIndex);
                request = new VTRequest(
                    spaceId,
                    coord,
                    sharedPhysicalPageId,
                    sharedGeneration,
                    int.MaxValue,
                    frameIndex);
                return true;
            }

            if (!m_PhysicalPool.TryAllocatePage(
                    this,
                    m_ProducerHandle,
                    m_ProducerName,
                    pageIndex,
                    m_PageMips[pageIndex],
                    coord,
                    viewId,
                    viewId,
                    HasViewAffinity(viewId),
                    frameIndex,
                    locked,
                    pendingUpload: false,
                    out int physicalPageId,
                    out int generation,
                    out _))
            {
                return false;
            }

            pageState.PhysicalPageId = physicalPageId;
            pageState.Generation = generation;
            pageState.LastAllocationFrame = frameIndex;
            pageState.PendingUpload = false;
            pageState.Resident = true;
            pageState.Locked = locked;
            m_PageStates[pageIndex] = pageState;
            m_ResidentPageCount += 1;
            MarkPageTableDirty(pageIndex);
            request = new VTRequest(
                spaceId,
                coord,
                physicalPageId,
                generation,
                int.MaxValue,
                frameIndex);
            return true;
        }

        internal bool TryQueuePageResident(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int spaceId,
            in VirtualTexturePageCoord coord,
            bool locked,
            int frameIndex)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, coord))
                return false;

            int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, coord);
            VTPageRuntimeState pageState = m_PageStates[pageIndex];
            if (pageState.Resident)
            {
                TrySetPageLocked(desc, mipOffsets, coord, locked);
                m_PhysicalPool.Touch(
                    pageState.PhysicalPageId,
                    VirtualTextureViewId.Invalid,
                    frameIndex,
                    updateAffinity: false);
                return true;
            }

            if (pageState.PendingUpload)
            {
                TrySetPageLocked(desc, mipOffsets, coord, locked);
                PromotePendingRequestToLocked(
                    desc,
                    mipOffsets,
                    pageIndex,
                    frameIndex);
                return true;
            }

            if (m_PhysicalPool.TryAttachResidentPage(
                    this,
                    m_ProducerHandle,
                    m_ProducerName,
                    pageIndex,
                    coord,
                    VirtualTextureViewId.Invalid,
                    frameIndex,
                    locked,
                    out int sharedPhysicalPageId,
                    out int sharedGeneration))
            {
                pageState.PhysicalPageId = sharedPhysicalPageId;
                pageState.Generation = sharedGeneration;
                pageState.LastAllocationFrame = frameIndex;
                pageState.PendingUpload = false;
                pageState.Resident = true;
                pageState.Locked = locked;
                m_PageStates[pageIndex] = pageState;
                m_ResidentPageCount += 1;
                MarkPageTableDirty(pageIndex);
                return true;
            }

            if (!m_PhysicalPool.TryAllocatePage(
                    this,
                    m_ProducerHandle,
                    m_ProducerName,
                    pageIndex,
                    m_PageMips[pageIndex],
                    coord,
                    VirtualTextureViewId.Invalid,
                    VirtualTextureViewId.Invalid,
                    updateAffinity: false,
                    frameIndex,
                    locked,
                    pendingUpload: true,
                    out int physicalPageId,
                    out int generation,
                    out _))
            {
                return false;
            }

            pageState.PhysicalPageId = physicalPageId;
            pageState.Generation = generation;
            pageState.LastAllocationFrame = frameIndex;
            pageState.PendingUpload = true;
            pageState.Resident = false;
            pageState.Locked = locked;
            m_PageStates[pageIndex] = pageState;
            m_PendingRequests.Add(new VTRequest(
                spaceId,
                coord,
                physicalPageId,
                generation,
                int.MaxValue,
                frameIndex,
                int.MinValue,
                isActiveView: false));
            MarkPageTableDirty(pageIndex);
            return true;
        }

        internal VTResidencyProcessResult ProcessRequests(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int spaceId,
            IReadOnlyList<VirtualTextureAggregatedFeedbackRequest> requests,
            VirtualTextureViewId activeViewId,
            Vector2Int prefetchBias,
            int frameIndex)
        {
            int evictionCount = 0;
            int allocatedThisFrame = 0;
            int pendingMipGapSum = 0;
            int pendingMipGapMax = 0;
            int pendingMipGapSampleCount = 0;
            int prefetchRequestCount = 0;
            bool pageTableChanged = false;

            if (requests == null)
                return new VTResidencyProcessResult(evictionCount, pageTableChanged);

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyDemandMarker.Auto())
            {
                for (int requestIndex = 0; requestIndex < requests.Count; requestIndex++)
                {
                    VirtualTextureAggregatedFeedbackRequest request = requests[requestIndex];
                    if (!VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord))
                        continue;

                    AccumulatePendingMipGap(
                        desc,
                        mipOffsets,
                        request.PageCoord,
                        ref pendingMipGapSum,
                        ref pendingMipGapMax,
                        ref pendingMipGapSampleCount);

                    TryProcessRequest(
                        desc,
                        mipOffsets,
                        spaceId,
                        request,
                        activeViewId,
                        frameIndex,
                        isPrefetch: false,
                        ref allocatedThisFrame,
                        ref evictionCount,
                        ref pageTableChanged);
                }
            }

            if (desc.NeighborPrefetchCount > 0 && allocatedThisFrame < desc.MaxUploadsPerFrame)
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyPrefetchMarker.Auto())
                {
                    for (int requestIndex = 0; requestIndex < requests.Count; requestIndex++)
                    {
                        VirtualTextureAggregatedFeedbackRequest request = requests[requestIndex];
                        if (!VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord))
                            continue;

                        prefetchRequestCount += ProcessNeighborPrefetchRequests(
                            desc,
                            mipOffsets,
                            spaceId,
                            request,
                            activeViewId,
                            prefetchBias,
                            frameIndex,
                            ref allocatedThisFrame,
                            ref evictionCount,
                            ref pageTableChanged);

                        if (allocatedThisFrame >= desc.MaxUploadsPerFrame)
                            break;
                    }
                }
            }

            return new VTResidencyProcessResult(
                evictionCount,
                pageTableChanged,
                pendingMipGapSum,
                pendingMipGapMax,
                pendingMipGapSampleCount,
                prefetchRequestCount);
        }

        internal bool TryCommitRequest(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            in VTRequest request)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord))
                return false;

            int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, request.PageCoord);
            VTPageRuntimeState pageState = m_PageStates[pageIndex];
            if (!pageState.PendingUpload
                || pageState.PhysicalPageId != request.PhysicalPageId
                || pageState.Generation != request.Generation)
            {
                return false;
            }

            if (!m_PhysicalPool.TryCommitPage(request.PhysicalPageId, request.Generation))
                return false;

            pageState.PendingUpload = false;
            pageState.Resident = true;
            m_PageStates[pageIndex] = pageState;
            m_ResidentPageCount += 1;
            RemovePendingRequest(pageIndex, request.Generation, desc, mipOffsets);
            m_PhysicalPool.Touch(
                pageState.PhysicalPageId,
                VirtualTextureViewId.Invalid,
                request.RequestFrame,
                updateAffinity: false);
            MarkPageTableDirty(pageIndex);
            return true;
        }

        internal bool TrySetPageLocked(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            in VirtualTexturePageCoord coord,
            bool locked)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, coord))
                return false;

            int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, coord);
            VTPageRuntimeState pageState = m_PageStates[pageIndex];
            if (pageState.Locked == locked)
                return true;

            pageState.Locked = locked;
            m_PageStates[pageIndex] = pageState;
            if (pageState.PhysicalPageId >= 0)
            {
                m_PhysicalPool.TrySetLocked(
                    pageState.PhysicalPageId,
                    pageState.Generation,
                    this,
                    pageIndex,
                    locked);
            }

            MarkPageTableDirty(pageIndex);
            return true;
        }

        internal bool IsPageLocked(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            in VirtualTexturePageCoord coord)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, coord))
                return false;

            int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, coord);
            return m_PageStates[pageIndex].Locked;
        }

        internal VTPageResidencyState GetPageState(int pageIndex)
        {
            VTPageRuntimeState pageState = m_PageStates[pageIndex];
            return new VTPageResidencyState(
                pageState.PhysicalPageId,
                pageState.Generation,
                pageState.LastAllocationFrame,
                pageState.Resident,
                pageState.PendingUpload,
                pageState.Locked);
        }

        internal bool ConsumePageTableDirtyFlag()
        {
            bool dirty = m_PageTableDirty;
            m_PageTableDirty = false;
            return dirty;
        }

        internal void ClearDirtyPageTableUpdates()
        {
            m_DirtyPageTableUpdates.Clear();
        }

        internal int FlushRegion(int mip, RectInt pageRegion)
        {
            return m_PhysicalPool.FlushRegion(m_SpaceId, mip, pageRegion);
        }

        public bool OnPhysicalPageInvalidated(int pageIndex, int generation)
        {
            if (pageIndex < 0 || pageIndex >= m_PageStates.Length)
                return false;

            VTPageRuntimeState pageState = m_PageStates[pageIndex];
            if (pageState.Generation != generation)
                return false;

            if (pageState.Resident)
                m_ResidentPageCount -= 1;

            pageState.Resident = false;
            pageState.PendingUpload = false;
            pageState.Locked = false;
            pageState.PhysicalPageId = -1;
            m_PageStates[pageIndex] = pageState;
            RemovePendingRequest(pageIndex, generation, m_Desc, m_MipOffsets);
            MarkPageTableDirty(pageIndex);
            return true;
        }

        public void Dispose()
        {
            m_PhysicalPool.FlushOwner(this);
            m_PendingRequests.Clear();
            m_DirtyPageTableUpdates.Clear();
            m_PageTableDirty = false;
        }

        private static VirtualTextureViewId ResolveEvictionViewId(
            VirtualTextureViewId activeViewId,
            in VirtualTextureAggregatedFeedbackRequest request)
        {
            return request.IsActiveView && HasViewAffinity(request.ViewId)
                ? request.ViewId
                : activeViewId;
        }

        private static VirtualTextureAggregatedFeedbackRequest CreatePrefetchRequest(
            in VirtualTextureAggregatedFeedbackRequest sourceRequest,
            in VirtualTexturePageCoord pageCoord)
        {
            return new VirtualTextureAggregatedFeedbackRequest(
                sourceRequest.SpaceId,
                pageCoord,
                hitCount: 0,
                ResolvePrefetchCameraPriority(sourceRequest.CameraPriority),
                sourceRequest.ViewId,
                isActiveView: false);
        }

        private static int ResolvePrefetchCameraPriority(int sourceCameraPriority)
        {
            return sourceCameraPriority == int.MaxValue
                ? int.MaxValue
                : sourceCameraPriority + 1;
        }

        private static bool HasViewAffinity(VirtualTextureViewId viewId)
        {
            return viewId.IsValid || viewId.IsCameraTypeOnly;
        }

        private bool TryProcessRequest(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int spaceId,
            in VirtualTextureAggregatedFeedbackRequest request,
            VirtualTextureViewId activeViewId,
            int frameIndex,
            bool isPrefetch,
            ref int allocatedThisFrame,
            ref int evictionCount,
            ref bool pageTableChanged)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord))
                return false;

            int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, request.PageCoord);
            VTPageRuntimeState pageState = m_PageStates[pageIndex];
            if (pageState.Resident)
            {
                m_PhysicalPool.Touch(
                    pageState.PhysicalPageId,
                    request.ViewId,
                    frameIndex,
                    !isPrefetch && request.IsActiveView);
                return false;
            }

            if (pageState.PendingUpload)
            {
                if (!isPrefetch)
                {
                    UpdatePendingRequestPriority(
                        desc,
                        mipOffsets,
                        pageIndex,
                        request.HitCount,
                        request.CameraPriority,
                        request.IsActiveView,
                        request.ViewId,
                        request.IsActiveView,
                        frameIndex);
                }

                return false;
            }

            if (allocatedThisFrame >= desc.MaxUploadsPerFrame)
                return false;

            VirtualTextureViewId attachViewId = isPrefetch ? VirtualTextureViewId.Invalid : request.ViewId;
            if (m_PhysicalPool.TryAttachResidentPage(
                    this,
                    m_ProducerHandle,
                    m_ProducerName,
                    pageIndex,
                    request.PageCoord,
                    attachViewId,
                    frameIndex,
                    locked: false,
                    out int sharedPhysicalPageId,
                    out int sharedGeneration))
            {
                pageState.PhysicalPageId = sharedPhysicalPageId;
                pageState.Generation = sharedGeneration;
                pageState.LastAllocationFrame = frameIndex;
                pageState.PendingUpload = false;
                pageState.Resident = true;
                pageState.Locked = false;
                m_PageStates[pageIndex] = pageState;
                m_ResidentPageCount += 1;
                MarkPageTableDirty(pageIndex);
                pageTableChanged = true;
                return false;
            }

            VirtualTextureViewId evictionViewId = isPrefetch
                ? activeViewId
                : ResolveEvictionViewId(activeViewId, request);
            VirtualTextureViewId allocationViewId = isPrefetch
                ? VirtualTextureViewId.Invalid
                : request.ViewId;
            bool updateAffinity = !isPrefetch && HasViewAffinity(request.ViewId);
            if (!m_PhysicalPool.TryAllocatePage(
                    this,
                    m_ProducerHandle,
                    m_ProducerName,
                    pageIndex,
                    m_PageMips[pageIndex],
                    request.PageCoord,
                    evictionViewId,
                    allocationViewId,
                    updateAffinity,
                    frameIndex,
                    locked: false,
                    pendingUpload: true,
                    out int physicalPageId,
                    out int generation,
                    out bool evicted))
            {
                return false;
            }

            if (evicted)
                evictionCount += 1;

            pageState.PhysicalPageId = physicalPageId;
            pageState.Generation = generation;
            pageState.LastAllocationFrame = frameIndex;
            pageState.PendingUpload = true;
            pageState.Resident = false;
            m_PageStates[pageIndex] = pageState;
            MarkPageTableDirty(pageIndex);
            m_PendingRequests.Add(new VTRequest(
                spaceId,
                request.PageCoord,
                physicalPageId,
                generation,
                request.HitCount,
                frameIndex,
                request.CameraPriority,
                !isPrefetch && request.IsActiveView));
            allocatedThisFrame += 1;
            pageTableChanged = true;
            return isPrefetch;
        }

        private int ProcessNeighborPrefetchRequests(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int spaceId,
            in VirtualTextureAggregatedFeedbackRequest request,
            VirtualTextureViewId activeViewId,
            Vector2Int prefetchBias,
            int frameIndex,
            ref int allocatedThisFrame,
            ref int evictionCount,
            ref bool pageTableChanged)
        {
            int scheduledPrefetchCount = 0;
            int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(desc.VirtualPageCountX, request.PageCoord.Mip);
            int pageCountY = VirtualTextureSpaceUtility.GetPageCountY(desc.VirtualPageCountY, request.PageCoord.Mip);
            TryProcessNeighborPrefetchOffset(
                desc,
                mipOffsets,
                spaceId,
                request,
                activeViewId,
                frameIndex,
                prefetchBias.x,
                0,
                pageCountX,
                pageCountY,
                ref allocatedThisFrame,
                ref evictionCount,
                ref pageTableChanged,
                ref scheduledPrefetchCount);
            TryProcessNeighborPrefetchOffset(
                desc,
                mipOffsets,
                spaceId,
                request,
                activeViewId,
                frameIndex,
                0,
                prefetchBias.y,
                pageCountX,
                pageCountY,
                ref allocatedThisFrame,
                ref evictionCount,
                ref pageTableChanged,
                ref scheduledPrefetchCount);

            for (int offsetIndex = 0; offsetIndex < s_NeighborOffsets.Length; offsetIndex++)
            {
                if (allocatedThisFrame >= desc.MaxUploadsPerFrame
                    || scheduledPrefetchCount >= desc.NeighborPrefetchCount)
                {
                    break;
                }

                Vector2Int offset = s_NeighborOffsets[offsetIndex];
                if ((offset.x == prefetchBias.x && offset.y == 0)
                    || (offset.x == 0 && offset.y == prefetchBias.y))
                {
                    continue;
                }

                TryProcessNeighborPrefetchOffset(
                    desc,
                    mipOffsets,
                    spaceId,
                    request,
                    activeViewId,
                    frameIndex,
                    offset.x,
                    offset.y,
                    pageCountX,
                    pageCountY,
                    ref allocatedThisFrame,
                    ref evictionCount,
                    ref pageTableChanged,
                    ref scheduledPrefetchCount);
            }

            return scheduledPrefetchCount;
        }

        private void TryProcessNeighborPrefetchOffset(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int spaceId,
            in VirtualTextureAggregatedFeedbackRequest request,
            VirtualTextureViewId activeViewId,
            int frameIndex,
            int offsetX,
            int offsetY,
            int pageCountX,
            int pageCountY,
            ref int allocatedThisFrame,
            ref int evictionCount,
            ref bool pageTableChanged,
            ref int scheduledPrefetchCount)
        {
            if ((offsetX == 0 && offsetY == 0)
                || allocatedThisFrame >= desc.MaxUploadsPerFrame
                || scheduledPrefetchCount >= desc.NeighborPrefetchCount)
            {
                return;
            }

            int neighborX = request.PageCoord.X + offsetX;
            int neighborY = request.PageCoord.Y + offsetY;
            if (neighborX < 0 || neighborX >= pageCountX || neighborY < 0 || neighborY >= pageCountY)
                return;

            var prefetchRequest = CreatePrefetchRequest(
                request,
                new VirtualTexturePageCoord(neighborX, neighborY, request.PageCoord.Mip));
            if (TryProcessRequest(
                    desc,
                    mipOffsets,
                    spaceId,
                    prefetchRequest,
                    activeViewId,
                    frameIndex,
                    isPrefetch: true,
                    ref allocatedThisFrame,
                    ref evictionCount,
                    ref pageTableChanged))
            {
                scheduledPrefetchCount += 1;
            }
        }

        private void AccumulatePendingMipGap(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            in VirtualTexturePageCoord coord,
            ref int pendingMipGapSum,
            ref int pendingMipGapMax,
            ref int pendingMipGapSampleCount)
        {
            if (!TryResolveResidentMip(desc, mipOffsets, coord, out int resolvedMip))
                return;

            int mipGap = Mathf.Max(0, resolvedMip - coord.Mip);
            pendingMipGapSum += mipGap;
            pendingMipGapMax = Mathf.Max(pendingMipGapMax, mipGap);
            pendingMipGapSampleCount += 1;
        }

        private bool TryResolveResidentMip(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            in VirtualTexturePageCoord coord,
            out int resolvedMip)
        {
            resolvedMip = 0;
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, coord))
                return false;

            for (int mip = coord.Mip; mip < desc.MipCount; mip++)
            {
                int mipDelta = mip - coord.Mip;
                var ancestorCoord = new VirtualTexturePageCoord(coord.X >> mipDelta, coord.Y >> mipDelta, mip);
                if (!VirtualTextureSpaceUtility.IsCoordValid(desc, ancestorCoord))
                    continue;

                int ancestorIndex = VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, ancestorCoord);
                if (!m_PageStates[ancestorIndex].Resident)
                    continue;

                resolvedMip = mip;
                return true;
            }

            return false;
        }

        private void UpdatePendingRequestPriority(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int pageIndex,
            int priority,
            int cameraPriority,
            bool isActiveView,
            VirtualTextureViewId viewId,
            bool updateAffinity,
            int frameIndex)
        {
            for (int requestIndex = 0; requestIndex < m_PendingRequests.Count; requestIndex++)
            {
                VTRequest request = m_PendingRequests[requestIndex];
                if (VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, request.PageCoord) != pageIndex)
                    continue;

                m_PhysicalPool.Touch(request.PhysicalPageId, viewId, frameIndex, updateAffinity);
                if (!IsPendingRequestPriorityImproved(request, priority, cameraPriority, isActiveView))
                    return;

                m_PendingRequests[requestIndex] = new VTRequest(
                    request.SpaceId,
                    request.PageCoord,
                    request.PhysicalPageId,
                    request.Generation,
                    priority,
                    Mathf.Min(request.RequestFrame, frameIndex),
                    cameraPriority,
                    isActiveView);
                return;
            }
        }

        private void PromotePendingRequestToLocked(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int pageIndex,
            int frameIndex)
        {
            for (int requestIndex = 0; requestIndex < m_PendingRequests.Count; requestIndex++)
            {
                VTRequest request = m_PendingRequests[requestIndex];
                if (VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, request.PageCoord) != pageIndex)
                    continue;

                m_PhysicalPool.Touch(
                    request.PhysicalPageId,
                    VirtualTextureViewId.Invalid,
                    frameIndex,
                    updateAffinity: false);
                m_PendingRequests[requestIndex] = new VTRequest(
                    request.SpaceId,
                    request.PageCoord,
                    request.PhysicalPageId,
                    request.Generation,
                    int.MaxValue,
                    Mathf.Min(request.RequestFrame, frameIndex),
                    int.MinValue,
                    request.IsActiveView);
                return;
            }
        }

        private static bool IsPendingRequestPriorityImproved(
            in VTRequest request,
            int priority,
            int cameraPriority,
            bool isActiveView)
        {
            if (isActiveView != request.IsActiveView)
                return isActiveView;

            if (cameraPriority != request.CameraPriority)
                return cameraPriority < request.CameraPriority;

            return priority > request.Priority;
        }

        private void MarkPageTableDirty(int pageIndex)
        {
            if (pageIndex < 0)
                return;

            m_PageTableDirty = true;
            m_DirtyPageTableUpdates.Add(pageIndex);
        }

        private void RemovePendingRequest(
            int pageIndex,
            int generation,
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets)
        {
            if (pageIndex < 0)
                return;

            for (int requestIndex = m_PendingRequests.Count - 1; requestIndex >= 0; requestIndex--)
            {
                VTRequest request = m_PendingRequests[requestIndex];
                if (request.Generation != generation)
                    continue;

                if (mipOffsets != null
                    && VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, request.PageCoord) != pageIndex)
                {
                    continue;
                }

                m_PendingRequests.RemoveAt(requestIndex);
                return;
            }
        }

        private static int[] BuildPageMipTable(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int totalPageCount)
        {
            var pageMips = new int[totalPageCount];
            for (int mip = 0; mip < desc.MipCount; mip++)
            {
                int mipWidth = VirtualTextureSpaceUtility.GetPageCountX(desc.VirtualPageCountX, mip);
                int mipHeight = VirtualTextureSpaceUtility.GetPageCountY(desc.VirtualPageCountY, mip);
                int mipOffset = mipOffsets[mip];
                int mipPageCount = mipWidth * mipHeight;
                for (int pageIndex = 0; pageIndex < mipPageCount; pageIndex++)
                    pageMips[mipOffset + pageIndex] = mip;
            }

            return pageMips;
        }
    }
}
