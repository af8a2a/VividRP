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

    internal readonly struct VTRequestPreparationResult
    {
        internal VTRequestPreparationResult(
            in VTResidencyClassificationResult classification,
            in VTPrefetchCandidate candidate,
            bool hasCandidate)
        {
            Classification = classification;
            Candidate = candidate;
            HasCandidate = hasCandidate;
        }

        internal VTResidencyClassificationResult Classification { get; }

        internal VTPrefetchCandidate Candidate { get; }

        internal bool HasCandidate { get; }
    }

    [BurstCompile]
    internal struct VTRequestPreparationJob : IJobParallelFor
    {
        internal const byte ResidentFlag = 1 << 0;
        internal const byte PendingFlag = 1 << 1;

        [ReadOnly]
        public NativeSlice<VirtualTextureAggregatedFeedbackRequest> Requests;

        [ReadOnly]
        public NativeArray<byte> PageStateFlags;

        [ReadOnly]
        public NativeArray<int> MipOffsets;

        [WriteOnly]
        public NativeArray<VTRequestPreparationResult> Results;

        public int VirtualPageCountX;
        public int VirtualPageCountY;
        public int MipCount;
        public int MaxRefinementMipStep;

        public void Execute(int index)
        {
            VirtualTextureAggregatedFeedbackRequest request = Requests[index];
            VirtualTexturePageCoord coord = request.PageCoord;
            if (!IsCoordValid(coord.X, coord.Y, coord.Mip))
            {
                var invalidClassification = new VTResidencyClassificationResult(
                    pageIndex: -1,
                    mipGap: -1,
                    VTResidencyRequestClassification.Invalid);
                Results[index] = new VTRequestPreparationResult(
                    invalidClassification,
                    default,
                    hasCandidate: false);
                return;
            }

            int pageIndex = GetFlatIndex(coord.X, coord.Y, coord.Mip);
            byte pageFlags = PageStateFlags[pageIndex];
            VTResidencyRequestClassification classification = (pageFlags & ResidentFlag) != 0
                ? VTResidencyRequestClassification.Resident
                : (pageFlags & PendingFlag) != 0
                    ? VTResidencyRequestClassification.Pending
                    : VTResidencyRequestClassification.Missing;
            int mipGap = ResolveMipGap(coord.X, coord.Y, coord.Mip);
            var classificationResult = new VTResidencyClassificationResult(
                pageIndex,
                mipGap,
                classification);
            if (classification == VTResidencyRequestClassification.Resident)
            {
                Results[index] = new VTRequestPreparationResult(
                    classificationResult,
                    default,
                    hasCandidate: false);
                return;
            }

#if VT_DEBUG
            VirtualTexturePageCoord sourceCoord = coord;
            VTPageRequestKind requestKind = VTPageRequestKind.Demand;
#endif
            if (classification == VTResidencyRequestClassification.Missing
                && mipGap > MaxRefinementMipStep)
            {
                int ancestorDelta = mipGap - MaxRefinementMipStep;
                var refinementCoord = new VirtualTexturePageCoord(
                    coord.X >> ancestorDelta,
                    coord.Y >> ancestorDelta,
                    coord.Mip + ancestorDelta);
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

            var candidate = new VTPrefetchCandidate(
                request
#if VT_DEBUG
                , sourceCoord,
                mipGap,
                requestKind
#endif
            );
            Results[index] = new VTRequestPreparationResult(
                classificationResult,
                candidate,
                hasCandidate: true);
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

    internal readonly struct VTRequestPreparationCandidateKey
        : IEquatable<VTRequestPreparationCandidateKey>
    {
        internal VTRequestPreparationCandidateKey(in VirtualTexturePageCoord coord)
        {
            X = coord.X;
            Y = coord.Y;
            Mip = coord.Mip;
        }

        private readonly int X;
        private readonly int Y;
        private readonly int Mip;

        public bool Equals(VTRequestPreparationCandidateKey other)
        {
            return X == other.X && Y == other.Y && Mip == other.Mip;
        }

        public override bool Equals(object obj)
        {
            return obj is VTRequestPreparationCandidateKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X;
                hash = (hash * 397) ^ Y;
                return (hash * 397) ^ Mip;
            }
        }
    }

    internal struct VTRequestPreparationCandidateComparer : IComparer<VTPrefetchCandidate>
    {
        public int Compare(VTPrefetchCandidate left, VTPrefetchCandidate right)
        {
            return new VTFeedbackPriorityComparer().Compare(left.Request, right.Request);
        }
    }

    [BurstCompile]
    internal struct VTRequestPreparationConsumeJob : IJob
    {
        [ReadOnly]
        public NativeArray<VTRequestPreparationResult> PreparationResults;

        public NativeList<VTPrefetchCandidate> Candidates;
        public NativeParallelHashMap<VTRequestPreparationCandidateKey, int> CandidateIndices;
        public int RequestCount;

        public void Execute()
        {
            Candidates.Clear();
            CandidateIndices.Clear();

            for (int requestIndex = 0; requestIndex < RequestCount; requestIndex++)
            {
                VTRequestPreparationResult preparedRequest =
                    PreparationResults[requestIndex];
                if (preparedRequest.HasCandidate)
                    AddOrMerge(preparedRequest.Candidate);
            }

            if (Candidates.Length > 1)
                Candidates.Sort(new VTRequestPreparationCandidateComparer());
        }

        private void AddOrMerge(in VTPrefetchCandidate candidate)
        {
            VirtualTextureAggregatedFeedbackRequest request = candidate.Request;
            var key = new VTRequestPreparationCandidateKey(request.PageCoord);
            if (!CandidateIndices.TryGetValue(key, out int existingIndex))
            {
                CandidateIndices.Add(key, Candidates.Length);
                Candidates.Add(candidate);
                return;
            }

            VTPrefetchCandidate existingCandidate = Candidates[existingIndex];
            VirtualTextureAggregatedFeedbackRequest existing = existingCandidate.Request;
            int combinedHitCount = existing.HitCount > int.MaxValue - request.HitCount
                ? int.MaxValue
                : existing.HitCount + request.HitCount;
            bool isActiveView = existing.IsActiveView || request.IsActiveView;
            int cameraPriority = existing.CameraPriority < request.CameraPriority
                ? existing.CameraPriority
                : request.CameraPriority;
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
            Candidates[existingIndex] = existingCandidate.WithRequest(mergedRequest);
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
            int transitionAncestorPageIndex,
            bool locked,
            int transitionPhase)
        {
            PhysicalPageId = physicalPageId;
            Generation = generation;
            LastAllocationFrame = lastAllocationFrame;
            Resident = resident;
            PendingUpload = pendingUpload;
            TransitionQueued = transitionQueued;
            TransitionAncestorPageIndex = transitionAncestorPageIndex;
            Locked = locked;
            TransitionPhase = transitionPhase;
        }

        internal int PhysicalPageId { get; }

        internal int Generation { get; }

        internal int LastAllocationFrame { get; }

        internal bool Resident { get; }

        internal bool PendingUpload { get; }

        internal bool TransitionQueued { get; }

        internal int TransitionAncestorPageIndex { get; }

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

    internal readonly struct VTPrefetchCandidate
    {
        internal VTPrefetchCandidate(
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

        internal VTPrefetchCandidate WithRequest(
            in VirtualTextureAggregatedFeedbackRequest request)
        {
            return new VTPrefetchCandidate(
                request
#if VT_DEBUG
                , SourceCoord,
                MipGap,
                RequestKind
#endif
            );
        }
    }

    internal sealed class VTResidencyManager : IDisposable, IVTPhysicalPoolOwner
    {
        private const int k_InlineRequestPreparationThreshold = 64;
        private const int k_RequestPreparationBatchSize = 64;
        private const int k_MaxRefinementMipStep = 2;
        internal const int ColdStartFrameCount = 32;
        internal const int ColdStartMaxRefinementMipStep = 4;
        internal const int ColdStartPageTransitionFrameCount = 4;
        internal const int ColdStartMaxTransitionStartsPerFrame = 16;
        internal const int PageTransitionFrameCount = 8;
        internal const int MaxTransitionStartsPerFrame = 8;

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
            public byte TransitionFrameCount;
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
        private NativeList<VTPrefetchCandidate> m_PreparedCandidates;
        private NativeParallelHashMap<VTRequestPreparationCandidateKey, int>
            m_PreparedCandidateIndices;
        private readonly int[] m_PendingRequestIndices;
        private readonly List<int> m_DirtyPageTableUpdates = new();
        private readonly List<int> m_TransitioningPageIndices = new();
        private readonly List<int> m_QueuedTransitionPageIndices = new();

        private NativeArray<VTRequestPreparationResult> m_RequestPreparationResults;
        private JobHandle m_RequestPreparationJobHandle;
        private bool m_HasOutstandingRequestPreparationJob;
        private int m_PreparedRequestCount = -1;

        private int m_ResidentPageCount;
        private uint m_PendingRequestRevision;
        private bool m_PageTableDirty;
        private bool m_LastRequestPreparationUsedParallelJob;
        private bool m_ColdStartActivated;
        private int m_ColdStartFrameIndex = int.MinValue;
#if UNITY_INCLUDE_TESTS
        private int m_ProcessRequestsCallCount;
        private int m_ProcessPrefetchCandidateCallCount;
#endif

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
            m_PreparedCandidates = new NativeList<VTPrefetchCandidate>(1, Allocator.Persistent);
            m_PreparedCandidateIndices =
                new NativeParallelHashMap<VTRequestPreparationCandidateKey, int>(
                    1,
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

        internal int RequestPreparationCapacity => m_RequestPreparationResults.IsCreated
            ? m_RequestPreparationResults.Length
            : 0;

        internal bool LastRequestPreparationUsedParallelJob =>
            m_LastRequestPreparationUsedParallelJob;

        internal int PrefetchCandidateCount => m_PreparedCandidates.Length;

        internal VTPrefetchCandidate GetPrefetchCandidate(int index)
        {
            return m_PreparedCandidates[index];
        }

#if UNITY_INCLUDE_TESTS
        internal int ProcessRequestsCallCount => m_ProcessRequestsCallCount;

        internal int ProcessPrefetchCandidateCallCount => m_ProcessPrefetchCandidateCallCount;
#endif

        internal bool IsColdStartActive(int frameIndex)
        {
            if (!m_ColdStartActivated
                || frameIndex < m_ColdStartFrameIndex)
            {
                return false;
            }

            return (long)frameIndex - m_ColdStartFrameIndex < ColdStartFrameCount;
        }

        internal int ResolveTransitionStartBudget(int frameIndex)
        {
            return IsColdStartActive(frameIndex)
                ? ColdStartMaxTransitionStartsPerFrame
                : MaxTransitionStartsPerFrame;
        }

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
            int frameIndex,
            int maxNewRequests)
        {
            ScheduleRequestPreparation(requests, frameIndex, out JobHandle preparationHandle);
            preparationHandle.Complete();
            CompleteRequestPreparation();
            TouchPreparedResidentRequests(requests, frameIndex);
            return ProcessPreparedRequests(
                desc,
                mipOffsets,
                spaceId,
                requests,
                activeViewId,
                frameIndex,
                maxNewRequests);
        }

        internal VTResidencyProcessResult ProcessPreparedRequests(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int spaceId,
            NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests,
            VirtualTextureViewId activeViewId,
            int frameIndex,
            int maxNewRequests)
        {
#if UNITY_INCLUDE_TESTS
            m_ProcessRequestsCallCount += 1;
#endif
            int evictionCount = 0;
            int allocatedThisFrame = 0;
            int allocationLimit = Mathf.Min(
                desc.MaxResidencyAllocationsPerFrame,
                Mathf.Max(0, maxNewRequests));
            int pendingMipGapSum = 0;
            int pendingMipGapMax = 0;
            int pendingMipGapSampleCount = 0;
            bool pageTableChanged = false;

            CompleteRequestPreparation();
            if (m_PreparedRequestCount != requests.Length)
            {
                throw new InvalidOperationException(
                    "VT request preparation results do not match the request batch.");
            }
            m_PreparedRequestCount = -1;

            if (requests.Length == 0)
            {
                return new VTResidencyProcessResult(evictionCount, pageTableChanged);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyDemandMarker.Auto())
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyApplyMarker.Auto())
                {
                    for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
                    {
                        VTResidencyClassificationResult classification =
                            m_RequestPreparationResults[requestIndex].Classification;
                        if (classification.Classification == VTResidencyRequestClassification.Invalid)
                            continue;

                        if (classification.MipGap >= 0)
                        {
                            pendingMipGapSum += classification.MipGap;
                            pendingMipGapMax = Mathf.Max(pendingMipGapMax, classification.MipGap);
                            pendingMipGapSampleCount += 1;
                        }
                    }

                    for (int requestIndex = 0; requestIndex < m_PreparedCandidates.Length; requestIndex++)
                    {
                        VTPrefetchCandidate refinementRequest = m_PreparedCandidates[requestIndex];
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

            return new VTResidencyProcessResult(
                evictionCount,
                pageTableChanged,
                pendingMipGapSum,
                pendingMipGapMax,
                pendingMipGapSampleCount,
                prefetchRequestCount: 0,
                allocatedRequestCount: allocatedThisFrame);
        }

        internal int ScheduleRequestPreparation(
            NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests,
            int frameIndex,
            out JobHandle preparationHandle)
        {
            CompleteRequestPreparation();
            int requestCount = requests.Length;
            m_PreparedRequestCount = requestCount;
            if (requestCount == 0)
            {
                m_LastRequestPreparationUsedParallelJob = false;
                m_PreparedCandidates.Clear();
                m_PreparedCandidateIndices.Clear();
                preparationHandle = default;
                return 0;
            }

            ActivateColdStart(frameIndex);
            EnsureRequestPreparationCapacity(requestCount);
            var job = new VTRequestPreparationJob
            {
                Requests = requests,
                PageStateFlags = m_PageStateFlags,
                MipOffsets = m_NativeMipOffsets,
                Results = m_RequestPreparationResults,
                VirtualPageCountX = m_Desc.VirtualPageCountX,
                VirtualPageCountY = m_Desc.VirtualPageCountY,
                MipCount = m_Desc.MipCount,
                MaxRefinementMipStep = IsColdStartActive(frameIndex)
                    ? ColdStartMaxRefinementMipStep
                    : k_MaxRefinementMipStep,
            };
            var consumeJob = new VTRequestPreparationConsumeJob
            {
                PreparationResults = m_RequestPreparationResults,
                Candidates = m_PreparedCandidates,
                CandidateIndices = m_PreparedCandidateIndices,
                RequestCount = requestCount,
            };

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureRequestPreparationMarker.Auto())
            {
                m_LastRequestPreparationUsedParallelJob =
                    requestCount > k_InlineRequestPreparationThreshold;
                if (m_LastRequestPreparationUsedParallelJob)
                {
                    using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureRequestPreparationScheduleMarker.Auto())
                    {
                        JobHandle prepareHandle = job.Schedule(
                            requestCount,
                            k_RequestPreparationBatchSize);
                        using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureRequestPreparationConsumeScheduleMarker.Auto())
                            m_RequestPreparationJobHandle = consumeJob.Schedule(prepareHandle);
                        m_HasOutstandingRequestPreparationJob = true;
                        preparationHandle = m_RequestPreparationJobHandle;
                    }

                    return 2;
                }

                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureRequestPreparationRunInlineMarker.Auto())
                {
                    job.Run(requestCount);
                    consumeJob.Run();
                }
            }

            preparationHandle = default;
            return 0;
        }

        internal void CompleteRequestPreparation()
        {
            if (!m_HasOutstandingRequestPreparationJob)
                return;

            try
            {
                m_RequestPreparationJobHandle.Complete();
            }
            finally
            {
                m_RequestPreparationJobHandle = default;
                m_HasOutstandingRequestPreparationJob = false;
            }
        }

        internal void TouchPreparedResidentRequests(
            NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests,
            int frameIndex)
        {
            CompleteRequestPreparation();
            if (m_PreparedRequestCount != requests.Length)
            {
                throw new InvalidOperationException(
                    "VT request preparation results do not match the request batch.");
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyResidentTouchMarker.Auto())
                TouchResolvedResidentRequestsBeforeAllocation(requests, frameIndex);
        }

        internal void DiscardRequestPreparation()
        {
            CompleteRequestPreparation();
            m_PreparedRequestCount = -1;
            if (m_PreparedCandidates.IsCreated)
                m_PreparedCandidates.Clear();
            if (m_PreparedCandidateIndices.IsCreated)
                m_PreparedCandidateIndices.Clear();
        }

        internal VTResidencyProcessResult ProcessPrefetchCandidate(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int spaceId,
            in VTPrefetchCandidate candidate,
            VirtualTextureViewId activeViewId,
            Vector2Int prefetchBias,
            int frameIndex,
            int maxResidencyRequests,
            int maxPrefetchRequests)
        {
#if UNITY_INCLUDE_TESTS
            m_ProcessPrefetchCandidateCallCount += 1;
#endif
            int residencyAllocationLimit = Mathf.Min(
                desc.MaxResidencyAllocationsPerFrame,
                Mathf.Max(0, maxResidencyRequests));
            if (residencyAllocationLimit <= 0
                || desc.NeighborPrefetchCount <= 0
                || !VirtualTextureSpaceUtility.IsCoordValid(desc, candidate.Request.PageCoord))
            {
                return default;
            }

            int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(
                desc,
                mipOffsets,
                candidate.Request.PageCoord);
            VTResidencyRequestClassification classification = ClassifyPageState(
                m_PageStates[pageIndex]);
            if (classification == VTResidencyRequestClassification.Invalid
                || classification == VTResidencyRequestClassification.Resident)
            {
                return default;
            }

            int allocatedThisFrame = 0;
            int evictionCount = 0;
            bool pageTableChanged = false;
#if VT_DEBUG
            VTPageRequestDebugInfo requestDebugInfo = candidate.CreateDebugInfo();
#endif
            // Demand may have returned unused allocation budget after a shared attach.
            // Reprocess the scalar seed so Missing can consume that budget and Pending
            // retains its priority-promotion semantics, without rebuilding the batch.
            TryProcessClassifiedRequest(
                desc,
                spaceId,
                candidate.Request,
                activeViewId,
                frameIndex,
                false,
                false,
#if VT_DEBUG
                requestDebugInfo,
#endif
                residencyAllocationLimit,
                pageIndex,
                classification,
                ref allocatedThisFrame,
                ref evictionCount,
                ref pageTableChanged);

            int prefetchRequestCount = 0;
            int remainingResidencyAllocationBudget = Mathf.Max(
                0,
                residencyAllocationLimit - allocatedThisFrame);
            int neighborAllocationBudget = Mathf.Min(
                remainingResidencyAllocationBudget,
                Mathf.Max(0, maxPrefetchRequests));
            if (neighborAllocationBudget > 0)
            {
                int neighborAllocationLimit = allocatedThisFrame + neighborAllocationBudget;
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyPrefetchNeighborsMarker.Auto())
                {
                    prefetchRequestCount = ProcessNeighborPrefetchRequests(
                        desc,
                        mipOffsets,
                        spaceId,
                        candidate,
                        activeViewId,
                        prefetchBias,
                        frameIndex,
                        neighborAllocationLimit,
                        ref allocatedThisFrame,
                        ref evictionCount,
                        ref pageTableChanged);
                }
            }

            return new VTResidencyProcessResult(
                evictionCount,
                pageTableChanged,
                prefetchRequestCount: prefetchRequestCount,
                allocatedRequestCount: allocatedThisFrame);
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
                VTResidencyClassificationResult classification =
                    m_RequestPreparationResults[requestIndex].Classification;
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
            {
                StartQueuedPageTransitions(
                    residencyFrameIndex,
                    ResolveTransitionStartBudget(residencyFrameIndex));
            }
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
                pageState.TransitionAncestorPageIndex,
                pageState.Locked,
                pageState.TransitionPhase);
        }

        internal VTResidencyRequestClassification GetPageClassification(int pageIndex)
        {
            return ClassifyPageState(m_PageStates[pageIndex]);
        }

        internal bool AdvancePageTransitions(
            int frameIndex,
            int maxPhaseAdvancesThisCall = int.MaxValue,
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
                TouchTransitionAncestor(pageState.TransitionAncestorPageIndex, frameIndex);
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
                    : CalculateTransitionPhase(
                        pageState.LastAllocationFrame,
                        frameIndex,
                        pageState.TransitionFrameCount);
                if (targetPhase <= pageState.TransitionPhase
                    || pageState.LastTransitionPhaseFrame == frameIndex
                    || phaseAdvancesThisCall >= maxPhaseAdvancesThisCall)
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
#endif
                phaseAdvancesThisCall += 1;
                changed = true;
            }

            return StartQueuedPageTransitions(frameIndex, maxTransitionStartsThisCall) || changed;
        }

        internal void ResetPageTransitionsForRuntimeReset()
        {
            m_TransitioningPageIndices.Clear();
            m_QueuedTransitionPageIndices.Clear();
            m_ColdStartActivated = false;
            m_ColdStartFrameIndex = int.MinValue;
        }

        internal bool StartQueuedPageTransitionsOnly(int frameIndex, int maxStartsThisCall)
        {
            return StartQueuedPageTransitions(frameIndex, maxStartsThisCall);
        }

        internal static byte CalculateTransitionPhase(int residencyFrameIndex, int frameIndex)
        {
            return CalculateTransitionPhase(
                residencyFrameIndex,
                frameIndex,
                PageTransitionFrameCount);
        }

        private static byte CalculateTransitionPhase(
            int residencyFrameIndex,
            int frameIndex,
            int transitionFrameCount)
        {
            if (residencyFrameIndex < 0 || frameIndex < residencyFrameIndex)
                return 0;

            long age = (long)frameIndex - residencyFrameIndex;
            return age >= Mathf.Max(1, transitionFrameCount)
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
            DiscardRequestPreparation();
            m_PhysicalPool.FlushOwner(this);
            m_PendingRequests.Clear();
            m_DirtyPageTableUpdates.Clear();
            m_TransitioningPageIndices.Clear();
            m_QueuedTransitionPageIndices.Clear();
            m_PageTableDirty = false;

            if (m_RequestPreparationResults.IsCreated)
                m_RequestPreparationResults.Dispose();
            if (m_PreparedCandidates.IsCreated)
                m_PreparedCandidates.Dispose();
            if (m_PreparedCandidateIndices.IsCreated)
                m_PreparedCandidateIndices.Dispose();
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

        private void EnsureRequestPreparationCapacity(int requiredCapacity)
        {
            int newCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, requiredCapacity));
            if (requiredCapacity > RequestPreparationCapacity)
            {
                if (m_RequestPreparationResults.IsCreated)
                    m_RequestPreparationResults.Dispose();

                m_RequestPreparationResults = new NativeArray<VTRequestPreparationResult>(
                    newCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }

            if (m_PreparedCandidates.Capacity < requiredCapacity)
                m_PreparedCandidates.Capacity = newCapacity;
            if (m_PreparedCandidateIndices.Capacity < requiredCapacity)
                m_PreparedCandidateIndices.Capacity = newCapacity;
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
                !isPrefetch,
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
            bool allowEviction,
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

            // Speculative work may attach an existing shared page, but it must never
            // displace a visible resident page once the pool is full.
            if (!allowEviction && m_PhysicalPool.FreePageCount <= 0)
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
            in VTPrefetchCandidate refinementRequest,
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
            in VTPrefetchCandidate refinementRequest,
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
                flags |= VTRequestPreparationJob.ResidentFlag;
            if (pageState.PendingUpload)
                flags |= VTRequestPreparationJob.PendingFlag;
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
                pageState.TransitionFrameCount = 0;
                return;
            }

            pageState.TransitionPhase = 0;
            pageState.TransitionFrameCount = (byte)(IsColdStartActive(frameIndex)
                ? ColdStartPageTransitionFrameCount
                : PageTransitionFrameCount);
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
            TouchTransitionAncestor(ancestorPageIndex, frameIndex);
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
                        m_QueuedTransitionPageIndices.RemoveAt(listIndex);
                }

                int bestListIndex = -1;
                int bestPageIndex = -1;
                int bestAncestorPageIndex = -1;
                int bestMip = int.MinValue;
                for (int listIndex = m_QueuedTransitionPageIndices.Count - 1;
                     listIndex >= 0;
                     listIndex--)
                {
                    int pageIndex = m_QueuedTransitionPageIndices[listIndex];
                    VTPageRuntimeState pageState = m_PageStates[pageIndex];
                    m_PhysicalPool.Touch(
                        pageState.PhysicalPageId,
                        VirtualTextureViewId.Invalid,
                        frameIndex,
                        updateAffinity: false);
                    if (!TryResolveStableTransitionStartAncestor(
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
                        ResolveTransitionStartBudget(frameIndex)))
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

        private void ActivateColdStart(int frameIndex)
        {
            if (m_ColdStartActivated || frameIndex < 0)
                return;

            m_ColdStartActivated = true;
            m_ColdStartFrameIndex = frameIndex;
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
                    continue;
                if (!parentState.Resident)
                    continue;
                if (parentState.TransitionQueued
                    || parentState.TransitionTracked
                    || (!parentState.Locked
                        && parentState.TransitionPhase
                        < VirtualTexturePageTableEntry.MaxTransitionPhase))
                {
                    continue;
                }

                ancestorPageIndex = parentPageIndex;
                return true;
            }

            return true;
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
                m_PageStates[pageIndex].TransitionAncestorPageIndex);
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

#endif

        private void TouchTransitionAncestor(int ancestorPageIndex, int frameIndex)
        {
            if (ancestorPageIndex < 0 || ancestorPageIndex >= m_PageStates.Length)
                return;

            VTPageRuntimeState ancestorState = m_PageStates[ancestorPageIndex];
            if (!ancestorState.Resident)
                return;

            // This exact page remains the visible source for the whole transition. Do not
            // switch protection to a closer ancestor that becomes stable in the meantime.
            m_PhysicalPool.Touch(
                ancestorState.PhysicalPageId,
                VirtualTextureViewId.Invalid,
                frameIndex,
                updateAffinity: false);
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
