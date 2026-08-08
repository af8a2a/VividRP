using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace VividRP.Runtime
{
    internal readonly struct VTFeedbackBatchMetadata
    {
        internal VTFeedbackBatchMetadata(
            int startIndex,
            int endIndex,
            int cameraPriority,
            VirtualTextureViewId viewId,
            bool isActiveView)
        {
            StartIndex = startIndex;
            EndIndex = endIndex;
            CameraPriority = cameraPriority;
            ViewId = viewId;
            IsActiveView = isActiveView ? (byte)1 : (byte)0;
        }

        internal readonly int StartIndex;
        internal readonly int EndIndex;
        internal readonly int CameraPriority;
        internal readonly VirtualTextureViewId ViewId;
        internal readonly byte IsActiveView;
    }

    internal readonly struct VTFeedbackRawRequest
    {
        internal VTFeedbackRawRequest(
            ulong key,
            int sequence,
            int cameraPriority,
            VirtualTextureViewId viewId,
            byte isActiveView)
        {
            Key = key;
            Sequence = sequence;
            CameraPriority = cameraPriority;
            ViewId = viewId;
            IsActiveView = isActiveView;
        }

        internal readonly ulong Key;
        internal readonly int Sequence;
        internal readonly int CameraPriority;
        internal readonly VirtualTextureViewId ViewId;
        internal readonly byte IsActiveView;
    }

    internal readonly struct VTFeedbackSpaceRange
    {
        internal VTFeedbackSpaceRange(int spaceId, int startIndex, int count)
        {
            SpaceId = spaceId;
            StartIndex = startIndex;
            Count = count;
        }

        internal readonly int SpaceId;
        internal readonly int StartIndex;
        internal readonly int Count;
    }

    internal struct VTFeedbackRawRequestComparer : IComparer<VTFeedbackRawRequest>
    {
        public int Compare(VTFeedbackRawRequest left, VTFeedbackRawRequest right)
        {
            int keyCompare = left.Key.CompareTo(right.Key);
            return keyCompare != 0 ? keyCompare : left.Sequence.CompareTo(right.Sequence);
        }
    }

    internal struct VTFeedbackPriorityComparer : IComparer<VirtualTextureAggregatedFeedbackRequest>
    {
        public int Compare(
            VirtualTextureAggregatedFeedbackRequest left,
            VirtualTextureAggregatedFeedbackRequest right)
        {
            if (left.IsActiveView != right.IsActiveView)
                return left.IsActiveView ? -1 : 1;

            int cameraCompare = left.CameraPriority.CompareTo(right.CameraPriority);
            if (cameraCompare != 0)
                return cameraCompare;

            int scoreCompare = VTRequestPriorityUtility.CompareMipWeightedScoreDescending(
                left.HitCount,
                left.PageCoord.Mip,
                right.HitCount,
                right.PageCoord.Mip);
            if (scoreCompare != 0)
                return scoreCompare;

            // Equal weighted value still favors broader coverage deterministically.
            int mipCompare = right.PageCoord.Mip.CompareTo(left.PageCoord.Mip);
            if (mipCompare != 0)
                return mipCompare;

            int hitCompare = right.HitCount.CompareTo(left.HitCount);
            if (hitCompare != 0)
                return hitCompare;

            int spaceCompare = left.SpaceId.CompareTo(right.SpaceId);
            if (spaceCompare != 0)
                return spaceCompare;

            int yCompare = left.PageCoord.Y.CompareTo(right.PageCoord.Y);
            return yCompare != 0 ? yCompare : left.PageCoord.X.CompareTo(right.PageCoord.X);
        }
    }

    internal struct VTFeedbackSpaceComparer : IComparer<VirtualTextureAggregatedFeedbackRequest>
    {
        public int Compare(
            VirtualTextureAggregatedFeedbackRequest left,
            VirtualTextureAggregatedFeedbackRequest right)
        {
            int spaceCompare = left.SpaceId.CompareTo(right.SpaceId);
            return spaceCompare != 0
                ? spaceCompare
                : new VTFeedbackPriorityComparer().Compare(left, right);
        }
    }

    [BurstCompile]
    internal struct VTFeedbackPrepareInputsJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<ulong> Keys;

        [ReadOnly]
        public NativeArray<VTFeedbackBatchMetadata> Batches;

        [WriteOnly]
        public NativeArray<VTFeedbackRawRequest> Requests;

        public int BatchCount;

        public void Execute(int index)
        {
            VTFeedbackBatchMetadata batch = ResolveBatch(index);
            Requests[index] = new VTFeedbackRawRequest(
                Keys[index],
                index,
                batch.CameraPriority,
                batch.ViewId,
                batch.IsActiveView);
        }

        private VTFeedbackBatchMetadata ResolveBatch(int requestIndex)
        {
            int low = 0;
            int high = BatchCount - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                VTFeedbackBatchMetadata batch = Batches[middle];
                if (requestIndex < batch.StartIndex)
                {
                    high = middle - 1;
                    continue;
                }

                if (requestIndex >= batch.EndIndex)
                {
                    low = middle + 1;
                    continue;
                }

                return batch;
            }

            return default;
        }
    }

    [BurstCompile]
    internal struct VTFeedbackReduceJob : IJob
    {
        [ReadOnly]
        public NativeArray<VTFeedbackRawRequest> Requests;

        public int RequestCount;
        public NativeList<VirtualTextureAggregatedFeedbackRequest> AggregatedRequests;

        public void Execute()
        {
            if (RequestCount <= 0)
                return;

            VTFeedbackRawRequest first = Requests[0];
            ulong key = first.Key;
            int hitCount = 1;
            int cameraPriority = first.CameraPriority;
            VirtualTextureViewId viewId = first.ViewId;
            byte isActiveView = first.IsActiveView;

            for (int requestIndex = 1; requestIndex < RequestCount; requestIndex++)
            {
                VTFeedbackRawRequest request = Requests[requestIndex];
                if (request.Key != key)
                {
                    WriteRequest(key, hitCount, cameraPriority, viewId, isActiveView);
                    key = request.Key;
                    hitCount = 1;
                    cameraPriority = request.CameraPriority;
                    viewId = request.ViewId;
                    isActiveView = request.IsActiveView;
                    continue;
                }

                hitCount += 1;
                if (request.IsActiveView != 0)
                {
                    isActiveView = 1;
                    viewId = request.ViewId;
                }
                else if (isActiveView == 0 && request.CameraPriority < cameraPriority)
                {
                    viewId = request.ViewId;
                }

                if (request.CameraPriority < cameraPriority)
                    cameraPriority = request.CameraPriority;
            }

            WriteRequest(key, hitCount, cameraPriority, viewId, isActiveView);
        }

        private void WriteRequest(
            ulong key,
            int hitCount,
            int cameraPriority,
            VirtualTextureViewId viewId,
            byte isActiveView)
        {
            VirtualTextureFeedbackProcessor.DecodeKey(
                key,
                out int spaceId,
                out VirtualTexturePageCoord pageCoord);
            AggregatedRequests.AddNoResize(new VirtualTextureAggregatedFeedbackRequest(
                spaceId,
                pageCoord,
                hitCount,
                cameraPriority,
                viewId,
                isActiveView != 0));
        }
    }

    [BurstCompile]
    internal struct VTFeedbackSortRawRequestsJob : IJob
    {
        public NativeArray<VTFeedbackRawRequest> Requests;
        public int RequestCount;

        public void Execute()
        {
            new NativeSlice<VTFeedbackRawRequest>(Requests, 0, RequestCount).Sort(
                new VTFeedbackRawRequestComparer());
        }
    }

    [BurstCompile]
    internal struct VTFeedbackPrepareGroupedJob : IJob
    {
        [ReadOnly]
        public NativeArray<VirtualTextureAggregatedFeedbackRequest> AggregatedRequests;

        public NativeList<VirtualTextureAggregatedFeedbackRequest> GroupedRequests;
        public NativeArray<int> Counters;
        public VirtualTextureViewId ActiveViewId;
        public CameraType ActiveCameraType;

        public void Execute()
        {
            int activeViewRequestCount = 0;
            for (int requestIndex = 0; requestIndex < AggregatedRequests.Length; requestIndex++)
            {
                VirtualTextureAggregatedFeedbackRequest request = AggregatedRequests[requestIndex];
                GroupedRequests.AddNoResize(request);
                if ((ActiveViewId.IsValid && request.ViewId.Equals(ActiveViewId))
                    || (!request.ViewId.IsValid && request.ViewId.CameraType == ActiveCameraType))
                {
                    activeViewRequestCount += 1;
                }
            }

            Counters[0] = activeViewRequestCount;
        }
    }

    [BurstCompile]
    internal struct VTFeedbackBuildRangesJob : IJob
    {
        [ReadOnly]
        public NativeArray<VirtualTextureAggregatedFeedbackRequest> GroupedRequests;

        public NativeList<VTFeedbackSpaceRange> SpaceRanges;

        public void Execute()
        {
            if (GroupedRequests.Length == 0)
                return;

            int rangeStart = 0;
            int spaceId = GroupedRequests[0].SpaceId;
            for (int requestIndex = 1; requestIndex < GroupedRequests.Length; requestIndex++)
            {
                int nextSpaceId = GroupedRequests[requestIndex].SpaceId;
                if (nextSpaceId == spaceId)
                    continue;

                SpaceRanges.AddNoResize(new VTFeedbackSpaceRange(
                    spaceId,
                    rangeStart,
                    requestIndex - rangeStart));
                spaceId = nextSpaceId;
                rangeStart = requestIndex;
            }

            SpaceRanges.AddNoResize(new VTFeedbackSpaceRange(
                spaceId,
                rangeStart,
                GroupedRequests.Length - rangeStart));
        }
    }

    [BurstCompile]
    internal struct VTFeedbackAggregateInlineJob : IJob
    {
        [ReadOnly]
        public NativeArray<ulong> Keys;

        [ReadOnly]
        public NativeArray<VTFeedbackBatchMetadata> Batches;

        public NativeArray<VTFeedbackRawRequest> RawRequests;
        public NativeList<VirtualTextureAggregatedFeedbackRequest> AggregatedRequests;
        public NativeList<VirtualTextureAggregatedFeedbackRequest> GroupedRequests;
        public NativeList<VTFeedbackSpaceRange> SpaceRanges;
        public NativeArray<int> Counters;
        public int RequestCount;
        public int BatchCount;
        public VirtualTextureViewId ActiveViewId;
        public CameraType ActiveCameraType;

        public void Execute()
        {
            var prepareJob = new VTFeedbackPrepareInputsJob
            {
                Keys = Keys,
                Batches = Batches,
                Requests = RawRequests,
                BatchCount = BatchCount,
            };
            for (int requestIndex = 0; requestIndex < RequestCount; requestIndex++)
                prepareJob.Execute(requestIndex);

            new NativeSlice<VTFeedbackRawRequest>(RawRequests, 0, RequestCount).Sort(
                new VTFeedbackRawRequestComparer());
            new VTFeedbackReduceJob
            {
                Requests = RawRequests,
                RequestCount = RequestCount,
                AggregatedRequests = AggregatedRequests,
            }.Execute();
            AggregatedRequests.Sort(new VTFeedbackPriorityComparer());
            new VTFeedbackPrepareGroupedJob
            {
                AggregatedRequests = AggregatedRequests.AsArray(),
                GroupedRequests = GroupedRequests,
                Counters = Counters,
                ActiveViewId = ActiveViewId,
                ActiveCameraType = ActiveCameraType,
            }.Execute();
            GroupedRequests.Sort(new VTFeedbackSpaceComparer());
            new VTFeedbackBuildRangesJob
            {
                GroupedRequests = GroupedRequests.AsArray(),
                SpaceRanges = SpaceRanges,
            }.Execute();
        }
    }

    internal sealed class VTFeedbackNativeAggregator : IDisposable
    {
        private const int k_InlineThreshold = 64;
        private const int k_JobBatchSize = 64;

        private NativeArray<ulong> m_Keys;
        private NativeArray<VTFeedbackBatchMetadata> m_Batches;
        private NativeArray<VTFeedbackRawRequest> m_RawRequests;
        private NativeList<VirtualTextureAggregatedFeedbackRequest> m_AggregatedRequests;
        private NativeList<VirtualTextureAggregatedFeedbackRequest> m_GroupedRequests;
        private NativeList<VTFeedbackSpaceRange> m_SpaceRanges;
        private NativeArray<int> m_Counters;
        private JobHandle m_OutstandingJobHandle;
        private bool m_HasOutstandingJobs;
        private bool m_LastUsedParallelJobs;
        private bool m_IsDisposed;

        internal VTFeedbackNativeAggregator()
        {
            m_AggregatedRequests = new NativeList<VirtualTextureAggregatedFeedbackRequest>(1, Allocator.Persistent);
            m_GroupedRequests = new NativeList<VirtualTextureAggregatedFeedbackRequest>(1, Allocator.Persistent);
            m_SpaceRanges = new NativeList<VTFeedbackSpaceRange>(1, Allocator.Persistent);
            m_Counters = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        internal NativeArray<VirtualTextureAggregatedFeedbackRequest> AggregatedRequests =>
            m_AggregatedRequests.AsArray();

        internal NativeArray<VTFeedbackSpaceRange> SpaceRanges => m_SpaceRanges.AsArray();

        internal int ActiveViewRequestCount => m_Counters.IsCreated ? m_Counters[0] : 0;

        internal int RequestCapacity => m_Keys.IsCreated ? m_Keys.Length : 0;

        internal int BatchCapacity => m_Batches.IsCreated ? m_Batches.Length : 0;

        internal bool LastUsedParallelJobs => m_LastUsedParallelJobs;

        internal void Aggregate(
            IReadOnlyList<VirtualTextureFeedbackBatch> batches,
            VirtualTextureViewId priorityViewId,
            VirtualTextureViewId activeViewId,
            CameraType activeCameraType)
        {
            ThrowIfDisposed();
            Clear();
            if (batches == null || batches.Count == 0)
                return;

            int requestCount = 0;
            int batchCount = 0;
            for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                int count = batches[batchIndex].RequestCount;
                if (count <= 0)
                    continue;

                requestCount += count;
                batchCount += 1;
            }

            if (requestCount == 0)
                return;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackAggregateAccumulateMarker.Auto())
            {
                EnsureCapacity(requestCount, batchCount);
                int requestStart = 0;
                int nativeBatchIndex = 0;
                for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
                {
                    VirtualTextureFeedbackBatch batch = batches[batchIndex];
                    int count = batch.RequestCount;
                    if (count <= 0)
                        continue;

                    batch.CopyRequestsTo(m_Keys, requestStart, count);
                    bool isActiveView = VirtualTextureFeedbackProcessor.IsActiveViewBatch(
                        batch,
                        priorityViewId);
                    VirtualTextureViewId viewId = isActiveView
                        ? VirtualTextureFeedbackProcessor.ResolveFeedbackViewId(batch, priorityViewId)
                        : batch.ViewId;
                    m_Batches[nativeBatchIndex] = new VTFeedbackBatchMetadata(
                        requestStart,
                        requestStart + count,
                        VirtualTextureFeedbackProcessor.GetCameraPriority(batch.CameraType),
                        viewId,
                        isActiveView);
                    requestStart += count;
                    nativeBatchIndex += 1;
                }
            }

            m_LastUsedParallelJobs = requestCount > k_InlineThreshold;
            if (!m_LastUsedParallelJobs)
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackAggregateSortMarker.Auto())
                {
                    new VTFeedbackAggregateInlineJob
                    {
                        Keys = m_Keys,
                        Batches = m_Batches,
                        RawRequests = m_RawRequests,
                        AggregatedRequests = m_AggregatedRequests,
                        GroupedRequests = m_GroupedRequests,
                        SpaceRanges = m_SpaceRanges,
                        Counters = m_Counters,
                        RequestCount = requestCount,
                        BatchCount = batchCount,
                        ActiveViewId = activeViewId,
                        ActiveCameraType = activeCameraType,
                    }.Run();
                }

                return;
            }

            try
            {
                JobHandle prepareHandle = TrackOutstandingJob(new VTFeedbackPrepareInputsJob
                {
                    Keys = m_Keys,
                    Batches = m_Batches,
                    Requests = m_RawRequests,
                    BatchCount = batchCount,
                }.Schedule(requestCount, k_JobBatchSize));

                JobHandle keySortHandle;
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackAggregateSortMarker.Auto())
                {
                    keySortHandle = TrackOutstandingJob(new VTFeedbackSortRawRequestsJob
                    {
                        Requests = m_RawRequests,
                        RequestCount = requestCount,
                    }.Schedule(prepareHandle));
                }

                JobHandle reduceHandle;
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackAggregateDecodeMarker.Auto())
                {
                    reduceHandle = TrackOutstandingJob(new VTFeedbackReduceJob
                    {
                        Requests = m_RawRequests,
                        RequestCount = requestCount,
                        AggregatedRequests = m_AggregatedRequests,
                    }.Schedule(keySortHandle));
                }

                JobHandle prioritySortHandle = TrackOutstandingJob(m_AggregatedRequests
                    .SortJobDefer(new VTFeedbackPriorityComparer())
                    .Schedule(reduceHandle));
                JobHandle prepareGroupedHandle = TrackOutstandingJob(new VTFeedbackPrepareGroupedJob
                {
                    AggregatedRequests = m_AggregatedRequests.AsDeferredJobArray(),
                    GroupedRequests = m_GroupedRequests,
                    Counters = m_Counters,
                    ActiveViewId = activeViewId,
                    ActiveCameraType = activeCameraType,
                }.Schedule(prioritySortHandle));
                JobHandle groupedSortHandle = TrackOutstandingJob(m_GroupedRequests
                    .SortJobDefer(new VTFeedbackSpaceComparer())
                    .Schedule(prepareGroupedHandle));
                TrackOutstandingJob(new VTFeedbackBuildRangesJob
                {
                    GroupedRequests = m_GroupedRequests.AsDeferredJobArray(),
                    SpaceRanges = m_SpaceRanges,
                }.Schedule(groupedSortHandle));
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackGroupBySpaceMarker.Auto())
                    CompleteOutstandingJobs();
            }
            catch
            {
                try
                {
                    CompleteOutstandingJobs();
                }
                catch
                {
                    // Preserve the scheduling exception after releasing every container safety handle.
                }

                throw;
            }
        }

        internal bool TryGetRequestsForSpace(
            int spaceId,
            out NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests)
        {
            NativeArray<VTFeedbackSpaceRange> ranges = m_SpaceRanges.AsArray();
            int low = 0;
            int high = ranges.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                VTFeedbackSpaceRange range = ranges[middle];
                if (spaceId < range.SpaceId)
                {
                    high = middle - 1;
                    continue;
                }

                if (spaceId > range.SpaceId)
                {
                    low = middle + 1;
                    continue;
                }

                requests = new NativeSlice<VirtualTextureAggregatedFeedbackRequest>(
                    m_GroupedRequests.AsArray(),
                    range.StartIndex,
                    range.Count);
                return true;
            }

            requests = default;
            return false;
        }

        internal void Clear()
        {
            CompleteOutstandingJobs();
            if (m_AggregatedRequests.IsCreated)
                m_AggregatedRequests.Clear();
            if (m_GroupedRequests.IsCreated)
                m_GroupedRequests.Clear();
            if (m_SpaceRanges.IsCreated)
                m_SpaceRanges.Clear();
            if (m_Counters.IsCreated)
                m_Counters[0] = 0;
            m_LastUsedParallelJobs = false;
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            CompleteOutstandingJobs();
            if (m_Keys.IsCreated)
                m_Keys.Dispose();
            if (m_Batches.IsCreated)
                m_Batches.Dispose();
            if (m_RawRequests.IsCreated)
                m_RawRequests.Dispose();
            if (m_AggregatedRequests.IsCreated)
                m_AggregatedRequests.Dispose();
            if (m_GroupedRequests.IsCreated)
                m_GroupedRequests.Dispose();
            if (m_SpaceRanges.IsCreated)
                m_SpaceRanges.Dispose();
            if (m_Counters.IsCreated)
                m_Counters.Dispose();
            m_IsDisposed = true;
        }

        private void EnsureCapacity(int requestCount, int batchCount)
        {
            if (requestCount > RequestCapacity)
            {
                int capacity = Mathf.NextPowerOfTwo(requestCount);
                if (m_Keys.IsCreated)
                    m_Keys.Dispose();
                if (m_RawRequests.IsCreated)
                    m_RawRequests.Dispose();
                m_Keys = new NativeArray<ulong>(
                    capacity,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                m_RawRequests = new NativeArray<VTFeedbackRawRequest>(
                    capacity,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }

            if (batchCount > BatchCapacity)
            {
                int capacity = Mathf.NextPowerOfTwo(batchCount);
                if (m_Batches.IsCreated)
                    m_Batches.Dispose();
                m_Batches = new NativeArray<VTFeedbackBatchMetadata>(
                    capacity,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }

            int outputCapacity = Mathf.NextPowerOfTwo(requestCount);
            if (m_AggregatedRequests.Capacity < outputCapacity)
                m_AggregatedRequests.Capacity = outputCapacity;
            if (m_GroupedRequests.Capacity < outputCapacity)
                m_GroupedRequests.Capacity = outputCapacity;
            if (m_SpaceRanges.Capacity < outputCapacity)
                m_SpaceRanges.Capacity = outputCapacity;
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
                throw new ObjectDisposedException(nameof(VTFeedbackNativeAggregator));
        }

        private JobHandle TrackOutstandingJob(JobHandle handle)
        {
            m_OutstandingJobHandle = handle;
            m_HasOutstandingJobs = true;
            return handle;
        }

        private void CompleteOutstandingJobs()
        {
            if (!m_HasOutstandingJobs)
                return;

            try
            {
                m_OutstandingJobHandle.Complete();
            }
            finally
            {
                m_OutstandingJobHandle = default;
                m_HasOutstandingJobs = false;
            }
        }
    }
}
