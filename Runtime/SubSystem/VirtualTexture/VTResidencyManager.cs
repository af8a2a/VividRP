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

    internal sealed class VTResidencyManager : IDisposable
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

        private struct VTPhysicalPageSlotState
        {
            public int VirtualPageIndex;
            public int Generation;
            public int LastAllocationFrame;
            public VirtualTextureViewId AffinityViewId;
            public int LastAffinityFrame;
        }

        private readonly VTPageRuntimeState[] m_PageStates;
        private readonly int[] m_PageMips;
        private readonly VTPhysicalPageSlotState[] m_PhysicalSlots;
        private readonly Stack<int> m_FreePhysicalPages;
        private readonly LinkedList<int> m_LruPhysicalPages = new();
        private readonly LinkedListNode<int>[] m_LruNodes;
        private readonly List<VTRequest> m_PendingRequests = new();
        private readonly Texture2DArray m_PhysicalCache;

        private int m_ResidentPageCount;
        private int m_NextGeneration;

        internal VTResidencyManager(
            string spaceName,
            in VirtualTextureSpaceDesc desc,
            int totalPageCount,
            int[] mipOffsets)
        {
            m_PageStates = new VTPageRuntimeState[totalPageCount];
            for (int pageIndex = 0; pageIndex < m_PageStates.Length; pageIndex++)
                m_PageStates[pageIndex].PhysicalPageId = -1;

            m_PageMips = BuildPageMipTable(desc, mipOffsets, totalPageCount);

            m_PhysicalSlots = new VTPhysicalPageSlotState[desc.CachePageCount];
            for (int slotIndex = 0; slotIndex < m_PhysicalSlots.Length; slotIndex++)
            {
                m_PhysicalSlots[slotIndex].VirtualPageIndex = -1;
                m_PhysicalSlots[slotIndex].AffinityViewId = VirtualTextureViewId.Invalid;
                m_PhysicalSlots[slotIndex].LastAffinityFrame = -1;
            }

            m_LruNodes = new LinkedListNode<int>[desc.CachePageCount];
            m_FreePhysicalPages = new Stack<int>(desc.CachePageCount);
            for (int slotIndex = desc.CachePageCount - 1; slotIndex >= 0; slotIndex--)
                m_FreePhysicalPages.Push(slotIndex);

            m_PhysicalCache = new Texture2DArray(
                desc.PhysicalPageSize,
                desc.PhysicalPageSize,
                desc.CachePageCount,
                desc.GraphicsFormat,
                TextureCreationFlags.None)
            {
                name = $"VividVT_{spaceName}_PhysicalCache",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            m_PhysicalCache.Apply(false, false);
        }

        internal int ResidentPageCount => m_ResidentPageCount;

        internal int FreePageCount => m_FreePhysicalPages.Count;

        internal int PendingRequestCount => m_PendingRequests.Count;

        internal Texture2DArray PhysicalCache => m_PhysicalCache;

        internal IReadOnlyList<VTRequest> PendingRequests => m_PendingRequests;

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
                TouchPhysicalPage(pageState.PhysicalPageId, viewId, frameIndex, viewId.IsValid);
                return true;
            }

            if (pageState.PendingUpload)
                return false;

            if (!TryAllocatePhysicalPage(
                    desc,
                    mipOffsets,
                    pageIndex,
                    viewId,
                    viewId,
                    viewId.IsValid,
                    frameIndex,
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
            TouchPhysicalPage(physicalPageId, viewId, frameIndex, viewId.IsValid);
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
                    TouchPhysicalPage(
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
                        request.ViewId,
                        request.IsActiveView,
                        frameIndex);
                    continue;
                }

                if (allocatedThisFrame >= desc.MaxUploadsPerFrame)
                    continue;

                if (!TryAllocatePhysicalPage(
                        desc,
                        mipOffsets,
                        pageIndex,
                        activeViewId,
                        request.ViewId,
                        request.IsActiveView,
                        frameIndex,
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
                m_PendingRequests.Add(new VTRequest(
                    spaceId,
                    request.PageCoord,
                    physicalPageId,
                    generation,
                    request.HitCount,
                    frameIndex));
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

            pageState.PendingUpload = false;
            pageState.Resident = true;
            m_PageStates[pageIndex] = pageState;
            m_ResidentPageCount += 1;
            RemovePendingRequest(pageIndex, request.Generation, desc, mipOffsets);
            TouchPhysicalPage(
                pageState.PhysicalPageId,
                VirtualTextureViewId.Invalid,
                request.RequestFrame,
                updateAffinity: false);
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

        public void Dispose()
        {
            if (m_PhysicalCache != null)
                CoreUtils.Destroy(m_PhysicalCache);

            m_PendingRequests.Clear();
            m_LruPhysicalPages.Clear();
            m_FreePhysicalPages.Clear();
        }

        private bool TryAllocatePhysicalPage(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int pageIndex,
            VirtualTextureViewId activeViewId,
            VirtualTextureViewId allocationViewId,
            bool updateAffinity,
            int frameIndex,
            out int physicalPageId,
            out int generation,
            out bool evicted)
        {
            physicalPageId = -1;
            generation = 0;
            evicted = false;

            if (m_FreePhysicalPages.Count > 0)
            {
                physicalPageId = m_FreePhysicalPages.Pop();
            }
            else
            {
                physicalPageId = FindEvictionCandidate(frameIndex, activeViewId);
                if (physicalPageId < 0)
                    return false;

                evicted = EvictPhysicalPage(physicalPageId, desc, mipOffsets);
            }

            generation = ++m_NextGeneration;
            VTPhysicalPageSlotState slotState = m_PhysicalSlots[physicalPageId];
            slotState.VirtualPageIndex = pageIndex;
            slotState.Generation = generation;
            slotState.LastAllocationFrame = frameIndex;
            m_PhysicalSlots[physicalPageId] = slotState;
            TouchPhysicalPage(physicalPageId, allocationViewId, frameIndex, updateAffinity);
            return true;
        }

        private bool EvictPhysicalPage(int physicalPageId, in VirtualTextureSpaceDesc desc, int[] mipOffsets)
        {
            VTPhysicalPageSlotState slotState = m_PhysicalSlots[physicalPageId];
            if (slotState.VirtualPageIndex < 0)
                return false;

            int evictedPageIndex = slotState.VirtualPageIndex;
            VTPageRuntimeState pageState = m_PageStates[evictedPageIndex];
            if (pageState.Resident)
                m_ResidentPageCount -= 1;

            pageState.Resident = false;
            pageState.PendingUpload = false;
            pageState.PhysicalPageId = -1;
            m_PageStates[evictedPageIndex] = pageState;
            RemovePendingRequest(evictedPageIndex, slotState.Generation, desc, mipOffsets);

            slotState.VirtualPageIndex = -1;
            slotState.LastAllocationFrame = -1;
            slotState.AffinityViewId = VirtualTextureViewId.Invalid;
            slotState.LastAffinityFrame = -1;
            m_PhysicalSlots[physicalPageId] = slotState;
            return true;
        }

        private int FindEvictionCandidate(int frameIndex, VirtualTextureViewId activeViewId)
        {
            int candidatePhysicalPageId = -1;
            int candidateMip = int.MaxValue;
            int fallbackPhysicalPageId = -1;
            int fallbackMip = int.MaxValue;

            LinkedListNode<int> node = m_LruPhysicalPages.First;
            while (node != null)
            {
                int physicalPageId = node.Value;
                if (!CanEvict(physicalPageId, frameIndex))
                {
                    node = node.Next;
                    continue;
                }

                int pageIndex = m_PhysicalSlots[physicalPageId].VirtualPageIndex;
                int pageMip = pageIndex >= 0 && pageIndex < m_PageMips.Length
                    ? m_PageMips[pageIndex]
                    : int.MaxValue;

                if (fallbackPhysicalPageId < 0 || pageMip < fallbackMip)
                {
                    fallbackPhysicalPageId = physicalPageId;
                    fallbackMip = pageMip;
                }

                if (IsProtectedByActiveViewAffinity(physicalPageId, activeViewId))
                {
                    node = node.Next;
                    continue;
                }

                if (candidatePhysicalPageId < 0 || pageMip < candidateMip)
                {
                    candidatePhysicalPageId = physicalPageId;
                    candidateMip = pageMip;
                    if (candidateMip == 0)
                        break;
                }

                node = node.Next;
            }

            return candidatePhysicalPageId >= 0 ? candidatePhysicalPageId : fallbackPhysicalPageId;
        }

        private bool CanEvict(int physicalPageId, int frameIndex)
        {
            if (physicalPageId < 0 || physicalPageId >= m_PhysicalSlots.Length)
                return false;

            VTPhysicalPageSlotState slotState = m_PhysicalSlots[physicalPageId];
            if (slotState.VirtualPageIndex < 0 || slotState.LastAllocationFrame == frameIndex)
                return false;

            VTPageRuntimeState pageState = m_PageStates[slotState.VirtualPageIndex];
            return !pageState.PendingUpload && !pageState.Locked;
        }

        private bool IsProtectedByActiveViewAffinity(
            int physicalPageId,
            VirtualTextureViewId activeViewId)
        {
            if ((!activeViewId.IsValid && !activeViewId.IsCameraTypeOnly)
                || physicalPageId < 0
                || physicalPageId >= m_PhysicalSlots.Length)
            {
                return false;
            }

            VTPhysicalPageSlotState slotState = m_PhysicalSlots[physicalPageId];
            if (slotState.LastAffinityFrame < 0)
                return false;

            return activeViewId.IsValid
                ? slotState.AffinityViewId.Equals(activeViewId)
                : slotState.AffinityViewId.CameraType == activeViewId.CameraType;
        }

        private void TouchPhysicalPage(
            int physicalPageId,
            VirtualTextureViewId viewId,
            int frameIndex,
            bool updateAffinity)
        {
            if (physicalPageId < 0 || physicalPageId >= m_LruNodes.Length)
                return;

            if (updateAffinity && viewId.IsValid)
            {
                VTPhysicalPageSlotState slotState = m_PhysicalSlots[physicalPageId];
                slotState.AffinityViewId = viewId;
                slotState.LastAffinityFrame = frameIndex;
                m_PhysicalSlots[physicalPageId] = slotState;
            }

            LinkedListNode<int> node = m_LruNodes[physicalPageId];
            if (node == null)
            {
                node = new LinkedListNode<int>(physicalPageId);
                m_LruNodes[physicalPageId] = node;
                m_LruPhysicalPages.AddLast(node);
                return;
            }

            if (node.List != null && node != m_LruPhysicalPages.Last)
            {
                m_LruPhysicalPages.Remove(node);
                m_LruPhysicalPages.AddLast(node);
            }
        }

        private void UpdatePendingRequestPriority(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int pageIndex,
            int priority,
            VirtualTextureViewId viewId,
            bool updateAffinity,
            int frameIndex)
        {
            for (int requestIndex = 0; requestIndex < m_PendingRequests.Count; requestIndex++)
            {
                VTRequest request = m_PendingRequests[requestIndex];
                if (VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, request.PageCoord) != pageIndex)
                    continue;

                TouchPhysicalPage(request.PhysicalPageId, viewId, frameIndex, updateAffinity);
                if (priority <= request.Priority)
                    return;

                m_PendingRequests[requestIndex] = new VTRequest(
                    request.SpaceId,
                    request.PageCoord,
                    request.PhysicalPageId,
                    request.Generation,
                    priority,
                    Mathf.Min(request.RequestFrame, frameIndex));
                return;
            }
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
