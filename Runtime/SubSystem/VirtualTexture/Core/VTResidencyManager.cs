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
        private const int k_InlineClassificationThreshold = 64;
        private const int k_ClassificationBatchSize = 64;

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
        private NativeArray<byte> m_PageStateFlags;
        private NativeArray<int> m_NativeMipOffsets;
        private readonly int[] m_PageMips;
        private readonly VTPhysicalPool m_PhysicalPool;
        private readonly List<VTRequest> m_PendingRequests = new();
        private readonly int[] m_PendingRequestIndices;
        private readonly List<int> m_DirtyPageTableUpdates = new();

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

        internal Texture2DArray PhysicalCache => m_PhysicalPool.Texture;

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
                SetPageState(pageIndex, pageState);
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
            int frameIndex)
        {
            int evictionCount = 0;
            int allocatedThisFrame = 0;
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

                        TryProcessClassifiedRequest(
                            desc,
                            spaceId,
                            requests[requestIndex],
                            activeViewId,
                            frameIndex,
                            isPrefetch: false,
                            classification.PageIndex,
                            classification.Classification,
                            ref allocatedThisFrame,
                            ref evictionCount,
                            ref pageTableChanged);
                    }
                }
            }

            if (desc.NeighborPrefetchCount > 0 && allocatedThisFrame < desc.MaxUploadsPerFrame)
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyPrefetchMarker.Auto())
                {
                    for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
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
            SetPageState(pageIndex, pageState);
            m_ResidentPageCount += 1;
            RemovePendingRequest(pageIndex, request.Generation);
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

            if (allocatedThisFrame >= desc.MaxUploadsPerFrame)
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
                SetPageState(pageIndex, pageState);
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
