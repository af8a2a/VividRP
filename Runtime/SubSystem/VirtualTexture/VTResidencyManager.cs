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
        internal VTResidencyProcessResult(int evictionCount, bool pageTableChanged)
        {
            EvictionCount = evictionCount;
            PageTableChanged = pageTableChanged;
        }

        internal int EvictionCount { get; }

        internal bool PageTableChanged { get; }
    }

    internal sealed class VTResidencyManager : IDisposable, IVTPhysicalPoolOwner
    {
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
        private readonly VTProducer m_Producer;
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
            VTProducer producer,
            string spaceName,
            in VirtualTextureSpaceDesc desc,
            int totalPageCount,
            int[] mipOffsets,
            VTPhysicalPool physicalPool)
        {
            m_SpaceId = spaceId;
            m_Producer = producer ?? VTNullProducer.Instance;
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
                    m_Producer,
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
                    m_Producer,
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

        internal VTResidencyProcessResult ProcessRequests(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int spaceId,
            IReadOnlyList<VirtualTextureAggregatedFeedbackRequest> requests,
            VirtualTextureViewId activeViewId,
            int frameIndex)
        {
            int evictionCount = 0;
            int allocatedThisFrame = 0;
            bool pageTableChanged = false;

            if (requests == null)
                return new VTResidencyProcessResult(evictionCount, pageTableChanged);

            for (int requestIndex = 0; requestIndex < requests.Count; requestIndex++)
            {
                VirtualTextureAggregatedFeedbackRequest request = requests[requestIndex];
                if (!VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord))
                    continue;

                int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, request.PageCoord);
                VTPageRuntimeState pageState = m_PageStates[pageIndex];
                if (pageState.Resident)
                {
                    m_PhysicalPool.Touch(
                        pageState.PhysicalPageId,
                        request.ViewId,
                        frameIndex,
                        request.IsActiveView);
                    continue;
                }

                if (pageState.PendingUpload)
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
                    continue;
                }

                if (allocatedThisFrame >= desc.MaxUploadsPerFrame)
                    continue;

                if (m_PhysicalPool.TryAttachResidentPage(
                        this,
                        m_Producer,
                        pageIndex,
                        request.PageCoord,
                        request.ViewId,
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
                    continue;
                }

                VirtualTextureViewId evictionViewId = ResolveEvictionViewId(activeViewId, request);
                if (!m_PhysicalPool.TryAllocatePage(
                        this,
                        m_Producer,
                        pageIndex,
                        m_PageMips[pageIndex],
                        request.PageCoord,
                        evictionViewId,
                        request.ViewId,
                        HasViewAffinity(request.ViewId),
                        frameIndex,
                        locked: false,
                        pendingUpload: true,
                        out int physicalPageId,
                        out int generation,
                        out bool evicted))
                    continue;

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
                    request.IsActiveView));
                allocatedThisFrame += 1;
                pageTableChanged = true;
            }

            return new VTResidencyProcessResult(evictionCount, pageTableChanged);
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

        private static bool HasViewAffinity(VirtualTextureViewId viewId)
        {
            return viewId.IsValid || viewId.IsCameraTypeOnly;
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
