using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal enum VTResidencyRequestClassification : byte
    {
        Invalid = 0,
        Resident = 1,
        Pending = 2,
        Missing = 3,
    }

    internal readonly struct VTResidencyClassificationInput
    {
        internal VTResidencyClassificationInput(in VirtualTexturePageCoord coord)
        {
            X = coord.X;
            Y = coord.Y;
            Mip = coord.Mip;
        }

        internal readonly int X;

        internal readonly int Y;

        internal readonly int Mip;
    }

    internal readonly struct VTResidencyClassificationResult
    {
        internal VTResidencyClassificationResult(
            int pageIndex,
            int mipGap,
            VTResidencyRequestClassification classification)
        {
            PageIndex = pageIndex;
            MipGap = mipGap;
            Classification = classification;
        }

        internal readonly int PageIndex;

        internal readonly int MipGap;

        internal readonly VTResidencyRequestClassification Classification;
    }

    [BurstCompile]
    internal struct VTResidencyClassificationJob : IJobParallelFor
    {
        internal const byte ResidentFlag = 1 << 0;
        internal const byte PendingFlag = 1 << 1;

        [ReadOnly]
        public NativeArray<VTResidencyClassificationInput> Inputs;

        [ReadOnly]
        public NativeArray<byte> PageStateFlags;

        [ReadOnly]
        public NativeArray<int> MipOffsets;

        [WriteOnly]
        public NativeArray<VTResidencyClassificationResult> Results;

        public int VirtualPageCountX;
        public int VirtualPageCountY;
        public int MipCount;

        public void Execute(int index)
        {
            VTResidencyClassificationInput input = Inputs[index];
            if (!IsCoordValid(input.X, input.Y, input.Mip))
            {
                Results[index] = new VTResidencyClassificationResult(
                    pageIndex: -1,
                    mipGap: -1,
                    VTResidencyRequestClassification.Invalid);
                return;
            }

            int pageIndex = GetFlatIndex(input.X, input.Y, input.Mip);
            byte pageFlags = PageStateFlags[pageIndex];
            VTResidencyRequestClassification classification = (pageFlags & ResidentFlag) != 0
                ? VTResidencyRequestClassification.Resident
                : (pageFlags & PendingFlag) != 0
                    ? VTResidencyRequestClassification.Pending
                    : VTResidencyRequestClassification.Missing;
            int mipGap = ResolveMipGap(input.X, input.Y, input.Mip);
            Results[index] = new VTResidencyClassificationResult(pageIndex, mipGap, classification);
        }

        private int ResolveMipGap(int x, int y, int requestedMip)
        {
            for (int mip = requestedMip; mip < MipCount; mip++)
            {
                int mipDelta = mip - requestedMip;
                int ancestorIndex = GetFlatIndex(x >> mipDelta, y >> mipDelta, mip);
                if ((PageStateFlags[ancestorIndex] & ResidentFlag) != 0)
                    return mipDelta;
            }

            return -1;
        }

        private bool IsCoordValid(int x, int y, int mip)
        {
            if (mip < 0 || mip >= MipCount || x < 0 || y < 0)
                return false;

            return x < GetPageCount(VirtualPageCountX, mip)
                   && y < GetPageCount(VirtualPageCountY, mip);
        }

        private int GetFlatIndex(int x, int y, int mip)
        {
            return MipOffsets[mip] + y * GetPageCount(VirtualPageCountX, mip) + x;
        }

        private static int GetPageCount(int virtualPageCount, int mip)
        {
            int pageCount = virtualPageCount >> mip;
            return pageCount > 0 ? pageCount : 1;
        }
    }

    internal readonly struct VTPageResidencyState
    {
        internal VTPageResidencyState(
            int physicalPageId,
            int generation,
            int lastAllocationFrame,
            bool resident,
            bool pendingUpload,
            bool transitionQueued,
            bool locked,
            int transitionPhase)
        {
            PhysicalPageId = physicalPageId;
            Generation = generation;
            LastAllocationFrame = lastAllocationFrame;
            Resident = resident;
            PendingUpload = pendingUpload;
            TransitionQueued = transitionQueued;
            Locked = locked;
            TransitionPhase = transitionPhase;
        }

        internal int PhysicalPageId { get; }

        internal int Generation { get; }

        internal int LastAllocationFrame { get; }

        internal bool Resident { get; }

        internal bool PendingUpload { get; }

        internal bool TransitionQueued { get; }

        internal bool Locked { get; }

        internal int TransitionPhase { get; }
    }

    internal readonly struct VTResidencyProcessResult
    {
        internal VTResidencyProcessResult(
            int evictionCount,
            bool pageTableChanged,
            int pendingMipGapSum = 0,
            int pendingMipGapMax = 0,
            int pendingMipGapSampleCount = 0,
            int prefetchRequestCount = 0,
            int allocatedRequestCount = 0)
        {
            EvictionCount = evictionCount;
            PageTableChanged = pageTableChanged;
            PendingMipGapSum = pendingMipGapSum;
            PendingMipGapMax = pendingMipGapMax;
            PendingMipGapSampleCount = pendingMipGapSampleCount;
            PrefetchRequestCount = prefetchRequestCount;
            AllocatedRequestCount = allocatedRequestCount;
        }

        internal int EvictionCount { get; }

        internal bool PageTableChanged { get; }

        internal int PendingMipGapSum { get; }

        internal int PendingMipGapMax { get; }

        internal int PendingMipGapSampleCount { get; }

        internal int PrefetchRequestCount { get; }

        internal int AllocatedRequestCount { get; }
    }

    internal sealed class VTResidencyManager : IDisposable, IVTPhysicalPoolOwner
    {
        private const int k_InlineClassificationThreshold = 64;
        private const int k_ClassificationBatchSize = 64;
        private const int k_MaxRefinementMipStep = 2;
        internal const int PageTransitionFrameCount = 8;
        internal const int MaxTransitionStartsPerFrame = 8;
        internal const int MaxTransitionPhaseAdvancesPerFrame = 4;

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
            public byte TransitionPhase;
            public bool TransitionTracked;
            public bool TransitionQueued;
            public int TransitionAncestorPageIndex;
            public int LastTransitionPhaseFrame;
        }

        private readonly struct VTRefinementRequest
        {
            internal VTRefinementRequest(
                in VirtualTextureAggregatedFeedbackRequest request
#if VT_DEBUG
                , in VirtualTexturePageCoord sourceCoord,
                int mipGap,
                VTPageRequestKind requestKind
#endif
            )
            {
                Request = request;
#if VT_DEBUG
                SourceCoord = sourceCoord;
                MipGap = mipGap;
                RequestKind = requestKind;
#endif
            }

            internal VirtualTextureAggregatedFeedbackRequest Request { get; }

#if VT_DEBUG
            internal VirtualTexturePageCoord SourceCoord { get; }

            internal int MipGap { get; }

            internal VTPageRequestKind RequestKind { get; }

            internal VTPageRequestDebugInfo CreateDebugInfo()
            {
                return new VTPageRequestDebugInfo(
                    RequestKind,
                    SourceCoord,
                    Request.PageCoord,
                    MipGap,
                    VTRequestPriorityUtility.ComputeMipWeightedScore(
                        Request.HitCount,
                        Request.PageCoord.Mip));
            }

            internal VTPageRequestDebugInfo CreateNeighborDebugInfo(
                in VirtualTexturePageCoord neighborCoord)
            {
                return new VTPageRequestDebugInfo(
                    VTPageRequestKind.Neighbor,
                    SourceCoord,
                    neighborCoord,
                    MipGap,
                    VTRequestPriorityUtility.ComputeMipWeightedScore(
                        Request.HitCount,
                        Request.PageCoord.Mip));
            }
#endif

            internal VTRefinementRequest WithRequest(
                in VirtualTextureAggregatedFeedbackRequest request)
            {
                return new VTRefinementRequest(
                    request
#if VT_DEBUG
                    , SourceCoord,
                    MipGap,
                    RequestKind
#endif
                );
            }
        }

        private readonly int m_SpaceId;
        private readonly VTProducerHandle m_ProducerHandle;
        private readonly string m_ProducerName;
        private readonly VirtualTextureSpaceDesc m_Desc;
        private readonly int[] m_MipOffsets;
        private readonly VTPageRuntimeState[] m_PageStates;
        private NativeArray<byte> m_PageStateFlags;
        private NativeArray<int> m_NativeMipOffsets;
        private readonly int[] m_PageMips;
        private readonly VTPhysicalPool m_PhysicalPool;
        private readonly List<VTRequest> m_PendingRequests = new();
        private readonly List<VTRefinementRequest> m_RefinementRequests = new();
        private readonly Dictionary<VirtualTexturePageCoord, int> m_RefinementRequestIndices = new();
        private readonly int[] m_PendingRequestIndices;
        private readonly List<int> m_DirtyPageTableUpdates = new();
        private readonly List<int> m_TransitioningPageIndices = new();
        private readonly List<int> m_QueuedTransitionPageIndices = new();

        private NativeArray<VTResidencyClassificationInput> m_ClassificationInputs;
        private NativeArray<VTResidencyClassificationResult> m_ClassificationResults;

        private int m_ResidentPageCount;
        private uint m_PendingRequestRevision;
        private bool m_PageTableDirty;
        private bool m_LastClassificationUsedParallelJob;

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
            m_PageStateFlags = new NativeArray<byte>(
                totalPageCount,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            m_NativeMipOffsets = new NativeArray<int>(
                mipOffsets,
                Allocator.Persistent);
            m_PendingRequestIndices = new int[totalPageCount];
            for (int pageIndex = 0; pageIndex < m_PageStates.Length; pageIndex++)
            {
                m_PageStates[pageIndex].PhysicalPageId = -1;
                m_PageStates[pageIndex].TransitionAncestorPageIndex = -1;
                m_PendingRequestIndices[pageIndex] = -1;
            }

            m_PageMips = BuildPageMipTable(desc, mipOffsets, totalPageCount);
        }

        public int SpaceId => m_SpaceId;

        internal int ResidentPageCount => m_ResidentPageCount;

        internal int FreePageCount => m_PhysicalPool.FreePageCount;

        internal int PendingRequestCount => m_PendingRequests.Count;

        internal uint PendingRequestRevision => m_PendingRequestRevision;

        internal VTPhysicalPool PhysicalPool => m_PhysicalPool;

        internal Texture2D PhysicalCache => m_PhysicalPool.Texture;

        internal IReadOnlyList<VTRequest> PendingRequests => m_PendingRequests;

        internal IReadOnlyList<int> DirtyPageTableUpdates => m_DirtyPageTableUpdates;

        internal int ClassificationCapacity => m_ClassificationInputs.IsCreated
            ? m_ClassificationInputs.Length
            : 0;

        internal bool LastClassificationUsedParallelJob => m_LastClassificationUsedParallelJob;

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
                SchedulePageTransition(pageIndex, ref pageState, frameIndex);
                SetPageState(pageIndex, pageState);
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

#if VT_DEBUG
            var requestDebugInfo = new VTPageRequestDebugInfo(
                locked ? VTPageRequestKind.Bootstrap : VTPageRequestKind.Demand,
                coord,
                coord,
                mipGap: 0,
                VTRequestPriorityUtility.ComputeMipWeightedScore(int.MaxValue, coord.Mip));
#endif
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
#if VT_DEBUG
                    requestDebugInfo,
#endif
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
            SchedulePageTransition(pageIndex, ref pageState, frameIndex);
            SetPageState(pageIndex, pageState);
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
                PromotePendingRequestToLocked(pageIndex, frameIndex);
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
                SchedulePageTransition(pageIndex, ref pageState, frameIndex);
                SetPageState(pageIndex, pageState);
                m_ResidentPageCount += 1;
                MarkPageTableDirty(pageIndex);
                return true;
            }

#if VT_DEBUG
            var requestDebugInfo = new VTPageRequestDebugInfo(
                VTPageRequestKind.Locked,
                coord,
                coord,
                mipGap: 0,
                VTRequestPriorityUtility.ComputeMipWeightedScore(int.MaxValue, coord.Mip));
#endif
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
#if VT_DEBUG
                    requestDebugInfo,
#endif
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
            SetPageState(pageIndex, pageState);
            AddPendingRequest(pageIndex, new VTRequest(
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
            NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests,
            VirtualTextureViewId activeViewId,
            Vector2Int prefetchBias,
            int frameIndex,
            int maxNewRequests,
            bool allowNeighborPrefetch)
        {
            int evictionCount = 0;
            int allocatedThisFrame = 0;
            int allocationLimit = Mathf.Min(
                desc.MaxUploadsPerFrame,
                Mathf.Max(0, maxNewRequests));
            int pendingMipGapSum = 0;
            int pendingMipGapMax = 0;
            int pendingMipGapSampleCount = 0;
            int prefetchRequestCount = 0;
            bool pageTableChanged = false;

            if (requests.Length == 0)
            {
                m_LastClassificationUsedParallelJob = false;
                return new VTResidencyProcessResult(evictionCount, pageTableChanged);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyDemandMarker.Auto())
            {
                ClassifyRequests(requests);
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyResidentTouchMarker.Auto())
                    TouchResolvedResidentRequestsBeforeAllocation(requests, frameIndex);
                BuildRefinementRequests(requests);
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyApplyMarker.Auto())
                {
                    for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
                    {
                        VTResidencyClassificationResult classification = m_ClassificationResults[requestIndex];
                        if (classification.Classification == VTResidencyRequestClassification.Invalid)
                            continue;

                        if (classification.MipGap >= 0)
                        {
                            pendingMipGapSum += classification.MipGap;
                            pendingMipGapMax = Mathf.Max(pendingMipGapMax, classification.MipGap);
                            pendingMipGapSampleCount += 1;
                        }
                    }

                    for (int requestIndex = 0; requestIndex < m_RefinementRequests.Count; requestIndex++)
                    {
                        VTRefinementRequest refinementRequest = m_RefinementRequests[requestIndex];
#if VT_DEBUG
                        VTPageRequestDebugInfo requestDebugInfo = refinementRequest.CreateDebugInfo();
#endif
                        TryProcessRequest(
                            desc,
                            mipOffsets,
                            spaceId,
                            refinementRequest.Request,
                            activeViewId,
                            frameIndex,
                            false,
#if VT_DEBUG
                            requestDebugInfo,
#endif
                            allocationLimit,
                            ref allocatedThisFrame,
                            ref evictionCount,
                            ref pageTableChanged);
                    }
                }
            }

            if (allowNeighborPrefetch
                && desc.NeighborPrefetchCount > 0
                && allocatedThisFrame < allocationLimit)
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyPrefetchMarker.Auto())
                {
                    for (int requestIndex = 0; requestIndex < m_RefinementRequests.Count; requestIndex++)
                    {
                        VTRefinementRequest request = m_RefinementRequests[requestIndex];
                        if (!VirtualTextureSpaceUtility.IsCoordValid(desc, request.Request.PageCoord))
                            continue;

                        prefetchRequestCount += ProcessNeighborPrefetchRequests(
                            desc,
                            mipOffsets,
                            spaceId,
                            request,
                            activeViewId,
                            prefetchBias,
                            frameIndex,
                            allocationLimit,
                            ref allocatedThisFrame,
                            ref evictionCount,
                            ref pageTableChanged);

                        if (allocatedThisFrame >= allocationLimit)
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
                prefetchRequestCount,
                allocatedThisFrame);
        }

        private void BuildRefinementRequests(
            NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests)
        {
            m_RefinementRequests.Clear();
            m_RefinementRequestIndices.Clear();

            for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                VTResidencyClassificationResult classification = m_ClassificationResults[requestIndex];
                if (classification.Classification == VTResidencyRequestClassification.Invalid
                    || classification.Classification == VTResidencyRequestClassification.Resident)
                {
                    continue;
                }

                VirtualTextureAggregatedFeedbackRequest request = requests[requestIndex];
#if VT_DEBUG
                VirtualTexturePageCoord sourceCoord = request.PageCoord;
                VTPageRequestKind requestKind = VTPageRequestKind.Demand;
#endif
                if (classification.Classification == VTResidencyRequestClassification.Missing
                    && classification.MipGap > k_MaxRefinementMipStep)
                {
                    int ancestorDelta = classification.MipGap - k_MaxRefinementMipStep;
                    VirtualTexturePageCoord requestedCoord = request.PageCoord;
                    var refinementCoord = new VirtualTexturePageCoord(
                        requestedCoord.X >> ancestorDelta,
                        requestedCoord.Y >> ancestorDelta,
                        requestedCoord.Mip + ancestorDelta);
                    request = new VirtualTextureAggregatedFeedbackRequest(
                        request.SpaceId,
                        refinementCoord,
                        request.HitCount,
                        request.CameraPriority,
                        request.ViewId,
                        request.IsActiveView);
#if VT_DEBUG
                    requestKind = VTPageRequestKind.Refinement;
#endif
                }

                AddOrMergeRefinementRequest(new VTRefinementRequest(
                    request
#if VT_DEBUG
                    , sourceCoord,
                    classification.MipGap,
                    requestKind
#endif
                ));
            }

            if (m_RefinementRequests.Count > 1)
                m_RefinementRequests.Sort(RefinementRequestComparer.Instance);
        }

        private void AddOrMergeRefinementRequest(
            in VTRefinementRequest refinementRequest)
        {
            VirtualTextureAggregatedFeedbackRequest request = refinementRequest.Request;
            if (!m_RefinementRequestIndices.TryGetValue(request.PageCoord, out int existingIndex))
            {
                m_RefinementRequestIndices.Add(request.PageCoord, m_RefinementRequests.Count);
                m_RefinementRequests.Add(refinementRequest);
                return;
            }

            VTRefinementRequest existingRefinementRequest = m_RefinementRequests[existingIndex];
            VirtualTextureAggregatedFeedbackRequest existing = existingRefinementRequest.Request;
            int combinedHitCount = existing.HitCount > int.MaxValue - request.HitCount
                ? int.MaxValue
                : existing.HitCount + request.HitCount;
            bool isActiveView = existing.IsActiveView || request.IsActiveView;
            int cameraPriority = Mathf.Min(existing.CameraPriority, request.CameraPriority);
            VirtualTextureViewId viewId = existing.ViewId;
            if ((request.IsActiveView && !existing.IsActiveView)
                || (request.IsActiveView == existing.IsActiveView
                    && request.CameraPriority < existing.CameraPriority))
            {
                viewId = request.ViewId;
            }

            var mergedRequest = new VirtualTextureAggregatedFeedbackRequest(
                request.SpaceId,
                request.PageCoord,
                combinedHitCount,
                cameraPriority,
                viewId,
                isActiveView);
            m_RefinementRequests[existingIndex] = existingRefinementRequest.WithRequest(mergedRequest);
        }

        private sealed class RefinementRequestComparer : IComparer<VTRefinementRequest>
        {
            internal static readonly RefinementRequestComparer Instance = new();

            private RefinementRequestComparer()
            {
            }

            public int Compare(
                VTRefinementRequest left,
                VTRefinementRequest right)
            {
                return new VTFeedbackPriorityComparer().Compare(left.Request, right.Request);
            }
        }

        private void TouchResolvedResidentRequestsBeforeAllocation(
            NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests,
            int frameIndex)
        {
            // Completed GPU feedback can be several frames old. Touch every page that actually
            // served the request before servicing faults. This includes a resident ancestor for
            // fallback requests, otherwise an early fault can evict coarse coverage still in use.
            for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                VTResidencyClassificationResult classification = m_ClassificationResults[requestIndex];
                if (classification.Classification == VTResidencyRequestClassification.Invalid
                    || classification.MipGap < 0)
                {
                    continue;
                }

                VirtualTextureAggregatedFeedbackRequest request = requests[requestIndex];
                VirtualTexturePageCoord requestedCoord = request.PageCoord;
                int resolvedMip = requestedCoord.Mip + classification.MipGap;
                var resolvedCoord = new VirtualTexturePageCoord(
                    requestedCoord.X >> classification.MipGap,
                    requestedCoord.Y >> classification.MipGap,
                    resolvedMip);
                if (!VirtualTextureSpaceUtility.IsCoordValid(m_Desc, resolvedCoord))
                    continue;

                int resolvedPageIndex = VirtualTextureSpaceUtility.GetFlatIndex(
                    m_Desc,
                    m_MipOffsets,
                    resolvedCoord);
                if (resolvedPageIndex < 0 || resolvedPageIndex >= m_PageStates.Length)
                    continue;

                VTPageRuntimeState pageState = m_PageStates[resolvedPageIndex];
                if (!pageState.Resident)
                    continue;

                m_PhysicalPool.Touch(
                    pageState.PhysicalPageId,
                    request.ViewId,
                    frameIndex,
                    request.IsActiveView);
            }
        }

        internal bool TryCommitRequest(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            in VTRequest request,
            int commitFrameIndex = -1)
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

            if (!m_PhysicalPool.TryCommitPage(
                    request.PhysicalPageId,
                    request.Generation,
                    commitFrameIndex))
                return false;

            pageState.PendingUpload = false;
            pageState.Resident = true;
            int residencyFrameIndex = commitFrameIndex >= 0
                ? commitFrameIndex
                : request.RequestFrame;
            SchedulePageTransition(pageIndex, ref pageState, residencyFrameIndex);
            SetPageState(pageIndex, pageState);
            m_ResidentPageCount += 1;
            RemovePendingRequest(pageIndex, request.Generation);
            m_PhysicalPool.Touch(
                pageState.PhysicalPageId,
                VirtualTextureViewId.Invalid,
                residencyFrameIndex,
                updateAffinity: false);
            MarkPageTableDirty(pageIndex);
            if (commitFrameIndex < 0)
                StartQueuedPageTransitions(residencyFrameIndex, MaxTransitionStartsPerFrame);
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
            if (locked && pageState.TransitionQueued)
            {
                pageState.TransitionQueued = false;
                pageState.TransitionAncestorPageIndex = -1;
                pageState.TransitionPhase = VirtualTexturePageTableEntry.MaxTransitionPhase;
                m_PhysicalPool.TrySetVisibilityPending(
                    pageState.PhysicalPageId,
                    pageState.Generation,
                    this,
                    pageIndex,
                    visibilityPending: false);
            }
            if (locked && pageState.TransitionTracked)
            {
                pageState.TransitionPhase = VirtualTexturePageTableEntry.MaxTransitionPhase;
                pageState.TransitionTracked = false;
            }
            SetPageState(pageIndex, pageState);
            if (pageState.PendingUpload)
                IncrementPendingRequestRevision();
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
                pageState.TransitionQueued,
                pageState.Locked,
                pageState.TransitionPhase);
        }

        internal bool AdvancePageTransitions(
            int frameIndex,
            int maxPhaseAdvancesThisCall = MaxTransitionPhaseAdvancesPerFrame,
            int maxTransitionStartsThisCall = MaxTransitionStartsPerFrame)
        {
            if (frameIndex < 0)
                return false;

            bool changed = false;
            int phaseAdvancesThisCall = 0;
            for (int transitionIndex = 0;
                 transitionIndex < m_TransitioningPageIndices.Count;
                 transitionIndex++)
            {
                int pageIndex = m_TransitioningPageIndices[transitionIndex];
                if (pageIndex < 0 || pageIndex >= m_PageStates.Length)
                {
                    m_TransitioningPageIndices.RemoveAt(transitionIndex);
                    transitionIndex -= 1;
                    continue;
                }

                VTPageRuntimeState pageState = m_PageStates[pageIndex];
                if (!pageState.TransitionTracked)
                {
                    m_TransitioningPageIndices.RemoveAt(transitionIndex);
                    transitionIndex -= 1;
                    continue;
                }

                if (!pageState.Resident)
                {
                    pageState.TransitionTracked = false;
                    pageState.TransitionAncestorPageIndex = -1;
                    SetPageState(pageIndex, pageState);
                    m_TransitioningPageIndices.RemoveAt(transitionIndex);
                    transitionIndex -= 1;
                    continue;
                }

                m_PhysicalPool.Touch(
                    pageState.PhysicalPageId,
                    VirtualTextureViewId.Invalid,
                    frameIndex,
                    updateAffinity: false);
                TouchTransitionAncestors(pageIndex, frameIndex);
#if VT_DEBUG
                VTDebugTransitionAncestor observedAncestor = ResolveDebugTransitionAncestor(pageIndex);
                m_PhysicalPool.DebugValidatePageTransitionAncestor(
                    m_SpaceId,
                    pageIndex,
                    m_PageMips[pageIndex],
                    pageState.PhysicalPageId,
                    pageState.Generation,
                    frameIndex,
                    pageState.TransitionPhase,
                    observedAncestor);
#endif

                byte targetPhase = pageState.Locked
                    ? (byte)VirtualTexturePageTableEntry.MaxTransitionPhase
                    : CalculateTransitionPhase(pageState.LastAllocationFrame, frameIndex);
                if (targetPhase <= pageState.TransitionPhase
                    || pageState.LastTransitionPhaseFrame == frameIndex
                    || phaseAdvancesThisCall >= maxPhaseAdvancesThisCall)
                {
                    continue;
                }

                if (!pageState.Locked
                    && !m_PhysicalPool.TryAcquireTransitionPhaseAdvance(
                        frameIndex,
                        MaxTransitionPhaseAdvancesPerFrame))
                {
                    continue;
                }

                byte previousPhase = pageState.TransitionPhase;
                byte nextPhase = (byte)VirtualTexturePageTableEntry.MaxTransitionPhase;
                bool completed = nextPhase >= VirtualTexturePageTableEntry.MaxTransitionPhase;
                pageState.TransitionPhase = nextPhase;
                pageState.TransitionTracked = !completed;
                pageState.LastTransitionPhaseFrame = frameIndex;
                if (completed)
                    pageState.TransitionAncestorPageIndex = -1;
                SetPageState(pageIndex, pageState);
                if (completed)
                {
                    m_TransitioningPageIndices.RemoveAt(transitionIndex);
                    transitionIndex -= 1;
                }

                MarkPageTableDirty(pageIndex);
#if VT_DEBUG
                m_PhysicalPool.DebugNotifyPageTransitionPhase(
                    m_SpaceId,
                    pageIndex,
                    m_PageMips[pageIndex],
                    pageState.PhysicalPageId,
                    pageState.Generation,
                    frameIndex,
                    previousPhase,
                    nextPhase);
                Debug.Log(
                    $"[VividRP][VT_DEBUG][PageTransitionReveal] space={m_Desc.SpaceName} "
                    + $"producer={m_ProducerName} frame={frameIndex} pageIndex={pageIndex} "
                    + $"mip={m_PageMips[pageIndex]} slot={pageState.PhysicalPageId} "
                    + $"phase={previousPhase}->{nextPhase} targetPhase={targetPhase} "
                    + $"mode=atomic ancestorVisibleBeforeReveal=True "
                    + $"cohortFrame={pageState.LastAllocationFrame} "
                    + $"ageFrames={frameIndex - pageState.LastAllocationFrame} completed={completed}");
#endif
                phaseAdvancesThisCall += 1;
                changed = true;
            }

            return StartQueuedPageTransitions(frameIndex, maxTransitionStartsThisCall) || changed;
        }

        internal bool StartQueuedPageTransitionsOnly(int frameIndex, int maxStartsThisCall)
        {
            return StartQueuedPageTransitions(frameIndex, maxStartsThisCall);
        }

        internal static byte CalculateTransitionPhase(int residencyFrameIndex, int frameIndex)
        {
            if (residencyFrameIndex < 0 || frameIndex < residencyFrameIndex)
                return 0;

            long age = (long)frameIndex - residencyFrameIndex;
            return age >= PageTransitionFrameCount
                ? (byte)VirtualTexturePageTableEntry.MaxTransitionPhase
                : (byte)0;
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
            pageState.TransitionPhase = VirtualTexturePageTableEntry.MaxTransitionPhase;
            pageState.TransitionTracked = false;
            pageState.TransitionQueued = false;
            pageState.TransitionAncestorPageIndex = -1;
            pageState.LastTransitionPhaseFrame = int.MinValue;
            SetPageState(pageIndex, pageState);
            RemovePendingRequest(pageIndex, generation);
            MarkPageTableDirty(pageIndex);
            return true;
        }

        public void Dispose()
        {
            m_PhysicalPool.FlushOwner(this);
            m_PendingRequests.Clear();
            m_DirtyPageTableUpdates.Clear();
            m_TransitioningPageIndices.Clear();
            m_QueuedTransitionPageIndices.Clear();
            m_PageTableDirty = false;

            if (m_ClassificationInputs.IsCreated)
                m_ClassificationInputs.Dispose();
            if (m_ClassificationResults.IsCreated)
                m_ClassificationResults.Dispose();
            if (m_PageStateFlags.IsCreated)
                m_PageStateFlags.Dispose();
            if (m_NativeMipOffsets.IsCreated)
                m_NativeMipOffsets.Dispose();
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

        private void ClassifyRequests(NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests)
        {
            int requestCount = requests.Length;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyClassificationMarker.Auto())
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyClassificationPrepareMarker.Auto())
                {
                    EnsureClassificationCapacity(requestCount);
                    for (int requestIndex = 0; requestIndex < requestCount; requestIndex++)
                    {
                        m_ClassificationInputs[requestIndex] = new VTResidencyClassificationInput(
                            requests[requestIndex].PageCoord);
                    }
                }

                var job = new VTResidencyClassificationJob
                {
                    Inputs = m_ClassificationInputs,
                    PageStateFlags = m_PageStateFlags,
                    MipOffsets = m_NativeMipOffsets,
                    Results = m_ClassificationResults,
                    VirtualPageCountX = m_Desc.VirtualPageCountX,
                    VirtualPageCountY = m_Desc.VirtualPageCountY,
                    MipCount = m_Desc.MipCount,
                };

                m_LastClassificationUsedParallelJob = requestCount > k_InlineClassificationThreshold;
                if (m_LastClassificationUsedParallelJob)
                {
                    using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyClassificationScheduleMarker.Auto())
                    {
                        job.Schedule(requestCount, k_ClassificationBatchSize).Complete();
                    }
                }
                else
                {
                    using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyClassificationRunInlineMarker.Auto())
                    {
                        job.Run(requestCount);
                    }
                }
            }
        }

        private void EnsureClassificationCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= ClassificationCapacity)
                return;

            int newCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, requiredCapacity));
            if (m_ClassificationInputs.IsCreated)
                m_ClassificationInputs.Dispose();
            if (m_ClassificationResults.IsCreated)
                m_ClassificationResults.Dispose();

            m_ClassificationInputs = new NativeArray<VTResidencyClassificationInput>(
                newCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            m_ClassificationResults = new NativeArray<VTResidencyClassificationResult>(
                newCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        private bool TryProcessRequest(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int spaceId,
            in VirtualTextureAggregatedFeedbackRequest request,
            VirtualTextureViewId activeViewId,
            int frameIndex,
            bool isPrefetch,
#if VT_DEBUG
            in VTPageRequestDebugInfo requestDebugInfo,
#endif
            int allocationLimit,
            ref int allocatedThisFrame,
            ref int evictionCount,
            ref bool pageTableChanged)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord))
                return false;

            int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, request.PageCoord);
            VTResidencyRequestClassification classification = ClassifyPageState(m_PageStates[pageIndex]);
            return TryProcessClassifiedRequest(
                desc,
                spaceId,
                request,
                activeViewId,
                frameIndex,
                isPrefetch,
#if VT_DEBUG
                requestDebugInfo,
#endif
                allocationLimit,
                pageIndex,
                classification,
                ref allocatedThisFrame,
                ref evictionCount,
                ref pageTableChanged);
        }

        private bool TryProcessClassifiedRequest(
            in VirtualTextureSpaceDesc desc,
            int spaceId,
            in VirtualTextureAggregatedFeedbackRequest request,
            VirtualTextureViewId activeViewId,
            int frameIndex,
            bool isPrefetch,
#if VT_DEBUG
            in VTPageRequestDebugInfo requestDebugInfo,
#endif
            int allocationLimit,
            int pageIndex,
            VTResidencyRequestClassification classification,
            ref int allocatedThisFrame,
            ref int evictionCount,
            ref bool pageTableChanged)
        {
            if (pageIndex < 0 || pageIndex >= m_PageStates.Length)
                return false;

            VTPageRuntimeState pageState = m_PageStates[pageIndex];
            VTResidencyRequestClassification currentClassification = ClassifyPageState(pageState);
            if (classification != currentClassification)
                classification = currentClassification;

            if (classification == VTResidencyRequestClassification.Resident)
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyResidentTouchMarker.Auto())
                {
                    m_PhysicalPool.Touch(
                        pageState.PhysicalPageId,
                        request.ViewId,
                        frameIndex,
                        !isPrefetch && request.IsActiveView);
                }

                return false;
            }

            if (classification == VTResidencyRequestClassification.Pending)
            {
                if (!isPrefetch)
                {
                    using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyPendingPriorityMarker.Auto())
                    {
                        UpdatePendingRequestPriority(
                            pageIndex,
                            request.HitCount,
                            request.CameraPriority,
                            request.IsActiveView,
                            request.ViewId,
                            request.IsActiveView,
                            frameIndex);
                    }
                }

                return false;
            }

            if (allocatedThisFrame >= allocationLimit)
                return false;

            VirtualTextureViewId attachViewId = isPrefetch ? VirtualTextureViewId.Invalid : request.ViewId;
            bool attached;
            int sharedPhysicalPageId;
            int sharedGeneration;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyAttachLookupMarker.Auto())
            {
                attached = m_PhysicalPool.TryAttachResidentPage(
                    this,
                    m_ProducerHandle,
                    m_ProducerName,
                    pageIndex,
                    request.PageCoord,
                    attachViewId,
                    frameIndex,
                    locked: false,
                    out sharedPhysicalPageId,
                    out sharedGeneration);
            }

            if (attached)
            {
                pageState.PhysicalPageId = sharedPhysicalPageId;
                pageState.Generation = sharedGeneration;
                pageState.LastAllocationFrame = frameIndex;
                pageState.PendingUpload = false;
                pageState.Resident = true;
                pageState.Locked = false;
                SchedulePageTransition(pageIndex, ref pageState, frameIndex);
                SetPageState(pageIndex, pageState);
                m_ResidentPageCount += 1;
                MarkPageTableDirty(pageIndex);
                pageTableChanged = true;
                return false;
            }

            // Prefetch is speculative and must never displace a visible resident page.
            // Once the shared pool is full, demand requests alone decide which LRU page
            // is worth replacing.
            if (isPrefetch && m_PhysicalPool.FreePageCount <= 0)
                return false;

            VirtualTextureViewId evictionViewId = isPrefetch
                ? activeViewId
                : ResolveEvictionViewId(activeViewId, request);
            VirtualTextureViewId allocationViewId = isPrefetch
                ? VirtualTextureViewId.Invalid
                : request.ViewId;
            bool updateAffinity = !isPrefetch && HasViewAffinity(request.ViewId);
            bool allocated;
            int physicalPageId;
            int generation;
            bool evicted;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyAllocateEvictMarker.Auto())
            {
                allocated = m_PhysicalPool.TryAllocatePage(
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
#if VT_DEBUG
                    requestDebugInfo,
#endif
                    out physicalPageId,
                    out generation,
                    out evicted);
            }

            if (!allocated)
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
            SetPageState(pageIndex, pageState);
            MarkPageTableDirty(pageIndex);
            AddPendingRequest(pageIndex, new VTRequest(
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
            in VTRefinementRequest refinementRequest,
            VirtualTextureViewId activeViewId,
            Vector2Int prefetchBias,
            int frameIndex,
            int allocationLimit,
            ref int allocatedThisFrame,
            ref int evictionCount,
            ref bool pageTableChanged)
        {
            VirtualTextureAggregatedFeedbackRequest request = refinementRequest.Request;
            int scheduledPrefetchCount = 0;
            int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(desc.VirtualPageCountX, request.PageCoord.Mip);
            int pageCountY = VirtualTextureSpaceUtility.GetPageCountY(desc.VirtualPageCountY, request.PageCoord.Mip);
            TryProcessNeighborPrefetchOffset(
                desc,
                mipOffsets,
                spaceId,
                refinementRequest,
                activeViewId,
                frameIndex,
                allocationLimit,
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
                refinementRequest,
                activeViewId,
                frameIndex,
                allocationLimit,
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
                if (allocatedThisFrame >= allocationLimit
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
                    refinementRequest,
                    activeViewId,
                    frameIndex,
                    allocationLimit,
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
            in VTRefinementRequest refinementRequest,
            VirtualTextureViewId activeViewId,
            int frameIndex,
            int allocationLimit,
            int offsetX,
            int offsetY,
            int pageCountX,
            int pageCountY,
            ref int allocatedThisFrame,
            ref int evictionCount,
            ref bool pageTableChanged,
            ref int scheduledPrefetchCount)
        {
            VirtualTextureAggregatedFeedbackRequest request = refinementRequest.Request;
            if ((offsetX == 0 && offsetY == 0)
                || allocatedThisFrame >= allocationLimit
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
#if VT_DEBUG
            VTPageRequestDebugInfo requestDebugInfo = refinementRequest.CreateNeighborDebugInfo(
                prefetchRequest.PageCoord);
#endif
            if (TryProcessRequest(
                    desc,
                    mipOffsets,
                    spaceId,
                    prefetchRequest,
                    activeViewId,
                    frameIndex,
                    true,
#if VT_DEBUG
                    requestDebugInfo,
#endif
                    allocationLimit,
                    ref allocatedThisFrame,
                    ref evictionCount,
                    ref pageTableChanged))
            {
                scheduledPrefetchCount += 1;
            }
        }

        private void UpdatePendingRequestPriority(
            int pageIndex,
            int priority,
            int cameraPriority,
            bool isActiveView,
            VirtualTextureViewId viewId,
            bool updateAffinity,
            int frameIndex)
        {
            if (!TryGetPendingRequest(pageIndex, out int requestIndex, out VTRequest request))
                return;

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
            IncrementPendingRequestRevision();
        }

        private void PromotePendingRequestToLocked(
            int pageIndex,
            int frameIndex)
        {
            if (!TryGetPendingRequest(pageIndex, out int requestIndex, out VTRequest request))
                return;

            m_PhysicalPool.Touch(
                request.PhysicalPageId,
                VirtualTextureViewId.Invalid,
                frameIndex,
                updateAffinity: false);
            var promotedRequest = new VTRequest(
                request.SpaceId,
                request.PageCoord,
                request.PhysicalPageId,
                request.Generation,
                int.MaxValue,
                Mathf.Min(request.RequestFrame, frameIndex),
                int.MinValue,
                request.IsActiveView);
            if (promotedRequest.Equals(request))
                return;

            m_PendingRequests[requestIndex] = promotedRequest;
            IncrementPendingRequestRevision();
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

        private static VTResidencyRequestClassification ClassifyPageState(in VTPageRuntimeState pageState)
        {
            if (pageState.Resident)
                return VTResidencyRequestClassification.Resident;
            if (pageState.PendingUpload)
                return VTResidencyRequestClassification.Pending;

            return VTResidencyRequestClassification.Missing;
        }

        private void SetPageState(int pageIndex, in VTPageRuntimeState pageState)
        {
            m_PageStates[pageIndex] = pageState;
            byte flags = 0;
            if (pageState.Resident)
                flags |= VTResidencyClassificationJob.ResidentFlag;
            if (pageState.PendingUpload)
                flags |= VTResidencyClassificationJob.PendingFlag;
            m_PageStateFlags[pageIndex] = flags;
        }

        private void SchedulePageTransition(
            int pageIndex,
            ref VTPageRuntimeState pageState,
            int frameIndex)
        {
            pageState.LastAllocationFrame = frameIndex;
            if (pageState.Locked)
            {
                pageState.TransitionPhase = VirtualTexturePageTableEntry.MaxTransitionPhase;
                pageState.TransitionTracked = false;
                pageState.TransitionQueued = false;
                pageState.TransitionAncestorPageIndex = -1;
                return;
            }

            pageState.TransitionPhase = 0;
            if (pageState.TransitionTracked || pageState.TransitionQueued)
                return;

            pageState.TransitionQueued = true;
            pageState.TransitionAncestorPageIndex = -1;
            m_QueuedTransitionPageIndices.Add(pageIndex);
            m_PhysicalPool.TrySetVisibilityPending(
                pageState.PhysicalPageId,
                pageState.Generation,
                this,
                pageIndex,
                visibilityPending: true);
        }

        private void BeginPageTransition(
            int pageIndex,
            ref VTPageRuntimeState pageState,
            int frameIndex,
            int ancestorPageIndex)
        {
            pageState.LastAllocationFrame = frameIndex;
            pageState.TransitionPhase = 0;
            pageState.TransitionQueued = false;
            pageState.TransitionAncestorPageIndex = ancestorPageIndex;
            pageState.TransitionTracked = true;
            pageState.LastTransitionPhaseFrame = int.MinValue;
            m_TransitioningPageIndices.Add(pageIndex);
            m_PhysicalPool.TrySetVisibilityPending(
                pageState.PhysicalPageId,
                pageState.Generation,
                this,
                pageIndex,
                visibilityPending: false);
#if VT_DEBUG
            VTDebugTransitionAncestor ancestor = CreateDebugTransitionAncestor(ancestorPageIndex);
            m_PhysicalPool.DebugNotifyPageTransitionBegin(
                m_SpaceId,
                pageIndex,
                m_PageMips[pageIndex],
                pageState.PhysicalPageId,
                pageState.Generation,
                frameIndex,
                ancestor);
            Debug.Log(
                $"[VividRP][VT_DEBUG][PageTransitionBegin] space={m_Desc.SpaceName} "
                + $"producer={m_ProducerName} frame={frameIndex} pageIndex={pageIndex} "
                + $"mip={m_PageMips[pageIndex]} slot={pageState.PhysicalPageId} "
                + $"ancestor={FormatDebugTransitionAncestor(in ancestor)} "
                + $"cohortFrame={frameIndex} durationFrames={PageTransitionFrameCount}");
#endif
        }

        private bool StartQueuedPageTransitions(int frameIndex, int maxStartsThisCall)
        {
            if (frameIndex < 0
                || maxStartsThisCall <= 0
                || m_QueuedTransitionPageIndices.Count == 0)
            {
                return false;
            }

            bool changed = false;
            int startedThisCall = 0;
            while (startedThisCall < maxStartsThisCall)
            {
                int bestListIndex = -1;
                int bestPageIndex = -1;
                int bestAncestorPageIndex = -1;
                int bestMip = int.MinValue;
                for (int listIndex = m_QueuedTransitionPageIndices.Count - 1;
                     listIndex >= 0;
                     listIndex--)
                {
                    int pageIndex = m_QueuedTransitionPageIndices[listIndex];
                    if (pageIndex < 0 || pageIndex >= m_PageStates.Length)
                    {
                        m_QueuedTransitionPageIndices.RemoveAt(listIndex);
                        continue;
                    }

                    VTPageRuntimeState pageState = m_PageStates[pageIndex];
                    if (!pageState.Resident || !pageState.TransitionQueued)
                    {
                        m_QueuedTransitionPageIndices.RemoveAt(listIndex);
                        continue;
                    }

                    m_PhysicalPool.Touch(
                        pageState.PhysicalPageId,
                        VirtualTextureViewId.Invalid,
                        frameIndex,
                        updateAffinity: false);
                    if (WouldChangeActiveTransitionAncestor(pageIndex)
                        || !TryResolveStableTransitionStartAncestor(
                            pageIndex,
                            out int ancestorPageIndex))
                    {
                        continue;
                    }

                    int mip = m_PageMips[pageIndex];
                    if (bestListIndex >= 0
                        && (mip < bestMip || (mip == bestMip && pageIndex > bestPageIndex)))
                    {
                        continue;
                    }

                    bestListIndex = listIndex;
                    bestPageIndex = pageIndex;
                    bestAncestorPageIndex = ancestorPageIndex;
                    bestMip = mip;
                }

                if (bestListIndex < 0)
                    break;

                if (!m_PhysicalPool.TryAcquireTransitionStart(
                        frameIndex,
                        MaxTransitionStartsPerFrame))
                {
                    break;
                }

                m_QueuedTransitionPageIndices.RemoveAt(bestListIndex);
                VTPageRuntimeState bestPageState = m_PageStates[bestPageIndex];
                if (!bestPageState.Resident || !bestPageState.TransitionQueued)
                    continue;

                BeginPageTransition(
                    bestPageIndex,
                    ref bestPageState,
                    frameIndex,
                    bestAncestorPageIndex);
                SetPageState(bestPageIndex, bestPageState);
                MarkPageTableDirty(bestPageIndex);
                startedThisCall += 1;
                changed = true;
            }

            return changed;
        }

        private bool TryResolveStableTransitionStartAncestor(
            int pageIndex,
            out int ancestorPageIndex)
        {
            ancestorPageIndex = -1;
            GetPageLocalCoord(pageIndex, out int x, out int y, out int mip);
            for (int parentMip = mip + 1; parentMip < m_Desc.MipCount; parentMip++)
            {
                x >>= 1;
                y >>= 1;
                int parentPageIndex = GetPageIndex(x, y, parentMip);
                VTPageRuntimeState parentState = m_PageStates[parentPageIndex];
                if (parentState.PendingUpload)
                    return false;
                if (!parentState.Resident)
                    continue;
                if (parentState.TransitionQueued
                    || parentState.TransitionTracked
                    || (!parentState.Locked
                        && parentState.TransitionPhase
                        < VirtualTexturePageTableEntry.MaxTransitionPhase))
                {
                    return false;
                }

                ancestorPageIndex = parentPageIndex;
                return true;
            }

            return true;
        }

        private bool WouldChangeActiveTransitionAncestor(int candidatePageIndex)
        {
            int candidateMip = m_PageMips[candidatePageIndex];
            for (int transitionIndex = 0;
                 transitionIndex < m_TransitioningPageIndices.Count;
                 transitionIndex++)
            {
                int childPageIndex = m_TransitioningPageIndices[transitionIndex];
                if (childPageIndex < 0 || childPageIndex >= m_PageStates.Length)
                    continue;

                VTPageRuntimeState childState = m_PageStates[childPageIndex];
                if (!childState.TransitionTracked
                    || !IsAncestorPage(candidatePageIndex, childPageIndex))
                {
                    continue;
                }

                int currentAncestorPageIndex = childState.TransitionAncestorPageIndex;
                if (currentAncestorPageIndex < 0
                    || candidateMip < m_PageMips[currentAncestorPageIndex])
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsAncestorPage(int ancestorPageIndex, int descendantPageIndex)
        {
            int ancestorMip = m_PageMips[ancestorPageIndex];
            int descendantMip = m_PageMips[descendantPageIndex];
            if (ancestorMip <= descendantMip)
                return false;

            GetPageLocalCoord(ancestorPageIndex, out int ancestorX, out int ancestorY, out _);
            GetPageLocalCoord(descendantPageIndex, out int descendantX, out int descendantY, out _);
            int mipDelta = ancestorMip - descendantMip;
            return descendantX >> mipDelta == ancestorX
                   && descendantY >> mipDelta == ancestorY;
        }

        private int ResolveStableTransitionAncestorPageIndex(int pageIndex)
        {
            GetPageLocalCoord(pageIndex, out int x, out int y, out int mip);
            for (int parentMip = mip + 1; parentMip < m_Desc.MipCount; parentMip++)
            {
                x >>= 1;
                y >>= 1;
                int parentPageIndex = GetPageIndex(x, y, parentMip);
                VTPageRuntimeState parentState = m_PageStates[parentPageIndex];
                if (!parentState.Resident || parentState.TransitionQueued)
                    continue;
                if (parentState.Locked
                    || parentState.TransitionPhase >= VirtualTexturePageTableEntry.MaxTransitionPhase)
                {
                    return parentPageIndex;
                }
            }

            return -1;
        }

        private void GetPageLocalCoord(int pageIndex, out int x, out int y, out int mip)
        {
            mip = m_PageMips[pageIndex];
            int mipWidth = VirtualTextureSpaceUtility.GetPageCountX(
                m_Desc.VirtualPageCountX,
                mip);
            int localIndex = pageIndex - m_MipOffsets[mip];
            x = localIndex % mipWidth;
            y = localIndex / mipWidth;
        }

        private int GetPageIndex(int x, int y, int mip)
        {
            int mipWidth = VirtualTextureSpaceUtility.GetPageCountX(
                m_Desc.VirtualPageCountX,
                mip);
            return m_MipOffsets[mip] + y * mipWidth + x;
        }

#if VT_DEBUG
        private VTDebugTransitionAncestor ResolveDebugTransitionAncestor(int pageIndex)
        {
            return CreateDebugTransitionAncestor(
                ResolveStableTransitionAncestorPageIndex(pageIndex));
        }

        private VTDebugTransitionAncestor CreateDebugTransitionAncestor(int ancestorPageIndex)
        {
            if (ancestorPageIndex < 0 || ancestorPageIndex >= m_PageStates.Length)
                return VTDebugTransitionAncestor.Invalid;

            VTPageRuntimeState ancestorState = m_PageStates[ancestorPageIndex];
            return new VTDebugTransitionAncestor(
                ancestorPageIndex,
                m_PageMips[ancestorPageIndex],
                ancestorState.PhysicalPageId,
                ancestorState.Generation);
        }

        private static string FormatDebugTransitionAncestor(
            in VTDebugTransitionAncestor ancestor)
        {
            return ancestor.IsValid
                ? $"(page:{ancestor.PageIndex},mip:{ancestor.Mip},slot:{ancestor.PhysicalPageId},generation:{ancestor.Generation})"
                : "invalid";
        }
#endif

        private void TouchTransitionAncestors(int pageIndex, int frameIndex)
        {
            int mip = m_PageMips[pageIndex];
            int mipWidth = VirtualTextureSpaceUtility.GetPageCountX(m_Desc.VirtualPageCountX, mip);
            int localIndex = pageIndex - m_MipOffsets[mip];
            int x = localIndex % mipWidth;
            int y = localIndex / mipWidth;

            for (int parentMip = mip + 1; parentMip < m_Desc.MipCount; parentMip++)
            {
                x >>= 1;
                y >>= 1;
                int parentWidth = VirtualTextureSpaceUtility.GetPageCountX(
                    m_Desc.VirtualPageCountX,
                    parentMip);
                int parentPageIndex = m_MipOffsets[parentMip] + y * parentWidth + x;
                VTPageRuntimeState parentState = m_PageStates[parentPageIndex];
                if (!parentState.Resident)
                    continue;

                m_PhysicalPool.Touch(
                    parentState.PhysicalPageId,
                    VirtualTextureViewId.Invalid,
                    frameIndex,
                    updateAffinity: false);
                if (parentState.Locked
                    || parentState.TransitionPhase >= VirtualTexturePageTableEntry.MaxTransitionPhase)
                {
                    break;
                }
            }
        }

        private void MarkPageTableDirty(int pageIndex)
        {
            if (pageIndex < 0)
                return;

            m_PageTableDirty = true;
            m_DirtyPageTableUpdates.Add(pageIndex);
        }

        private void AddPendingRequest(int pageIndex, in VTRequest request)
        {
            int requestIndex = m_PendingRequests.Count;
            m_PendingRequests.Add(request);
            m_PendingRequestIndices[pageIndex] = requestIndex;
            IncrementPendingRequestRevision();
        }

        private bool TryGetPendingRequest(int pageIndex, out int requestIndex, out VTRequest request)
        {
            requestIndex = pageIndex >= 0 && pageIndex < m_PendingRequestIndices.Length
                ? m_PendingRequestIndices[pageIndex]
                : -1;
            if (requestIndex < 0 || requestIndex >= m_PendingRequests.Count)
            {
                request = default;
                return false;
            }

            request = m_PendingRequests[requestIndex];
            return true;
        }

        private void RemovePendingRequest(int pageIndex, int generation)
        {
            if (!TryGetPendingRequest(pageIndex, out int requestIndex, out VTRequest request)
                || request.Generation != generation)
            {
                return;
            }

            int lastRequestIndex = m_PendingRequests.Count - 1;
            if (requestIndex != lastRequestIndex)
            {
                VTRequest movedRequest = m_PendingRequests[lastRequestIndex];
                m_PendingRequests[requestIndex] = movedRequest;
                int movedPageIndex = VirtualTextureSpaceUtility.GetFlatIndex(
                    m_Desc,
                    m_MipOffsets,
                    movedRequest.PageCoord);
                m_PendingRequestIndices[movedPageIndex] = requestIndex;
            }

            m_PendingRequests.RemoveAt(lastRequestIndex);
            m_PendingRequestIndices[pageIndex] = -1;
            IncrementPendingRequestRevision();
        }

        private void IncrementPendingRequestRevision()
        {
            unchecked
            {
                m_PendingRequestRevision += 1u;
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
