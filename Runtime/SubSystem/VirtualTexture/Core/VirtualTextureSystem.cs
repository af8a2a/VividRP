using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal sealed class VirtualTextureSystem : VividSubsystem<VirtualTextureSystem>
    {
        private static readonly Dictionary<int, VTPageTableSpace> s_PageTableSpaces = new();
        private static readonly Dictionary<string, int> s_SpaceIdsByName = new(StringComparer.Ordinal);
        private static readonly Dictionary<int, VTAllocatedVirtualTexture> s_Allocations = new();
        private static readonly Dictionary<string, int> s_AllocationIdsByName = new(StringComparer.Ordinal);
        private static readonly Dictionary<int, int> s_AllocationIdBySpaceId = new();
        private static readonly Dictionary<VTPhysicalPoolDesc, VTPhysicalPool> s_PhysicalPools = new();
        private static readonly VTProducerRegistry s_ProducerRegistry = new();
        private static readonly VirtualTextureFeedbackCameraSystem s_FeedbackCameraSystem = new();
        private static readonly List<VirtualTextureFeedbackBatch> s_CompletedReadbacks = new();
        private static readonly List<VirtualTextureFeedbackBatch> s_InjectedReadbacks = new();
        private static readonly Dictionary<FeedbackMotionKey, FeedbackMotionState> s_FeedbackMotionStates = new();
        private static readonly Dictionary<int, Vector2Int> s_PrefetchBiasBySpace = new();
        private static readonly Dictionary<int, int> s_RemainingResidencyBudgetBySpace = new();
        private static readonly List<FeedbackMotionKey> s_FeedbackMotionKeysToRemove = new();
        private static readonly List<VTPageTableSpace> s_UploadSpaceOrder = new();
        private static readonly List<VTPendingUploadCandidate> s_PendingUploadCandidates = new();
        private static readonly UploadCommitterResolver s_UploadCommitterResolver = new();
        private static readonly VTAdaptiveMipBiasController s_AdaptiveMipBiasController = new();
        private static readonly VTPageTableScatterUploader s_PageTableScatterUploader = new();
        private static VTUploadScheduler s_UploadScheduler = new();
        private static VTFeedbackNativeAggregator s_FeedbackAggregator;

        private static int s_NextSpaceId = 1;
        private static int s_NextAllocationId = 1;
        private static int s_FallbackFrameIndex = -1;

        private sealed class PendingUploadCandidateComparer : IComparer<VTPendingUploadCandidate>
        {
            internal static readonly PendingUploadCandidateComparer Instance = new();

            private PendingUploadCandidateComparer()
            {
            }

            public int Compare(VTPendingUploadCandidate left, VTPendingUploadCandidate right)
            {
                if (left.Locked != right.Locked)
                    return left.Locked ? -1 : 1;

                VTRequest leftRequest = left.Request;
                VTRequest rightRequest = right.Request;
                if (leftRequest.IsActiveView != rightRequest.IsActiveView)
                    return leftRequest.IsActiveView ? -1 : 1;

                int cameraCompare = leftRequest.CameraPriority.CompareTo(rightRequest.CameraPriority);
                if (cameraCompare != 0)
                    return cameraCompare;

                int priorityCompare = rightRequest.Priority.CompareTo(leftRequest.Priority);
                if (priorityCompare != 0)
                    return priorityCompare;

                int frameCompare = leftRequest.RequestFrame.CompareTo(rightRequest.RequestFrame);
                if (frameCompare != 0)
                    return frameCompare;

                int mipCompare = leftRequest.PageCoord.Mip.CompareTo(rightRequest.PageCoord.Mip);
                if (mipCompare != 0)
                    return mipCompare;

                int fairnessCompare = left.FairnessRank.CompareTo(right.FairnessRank);
                if (fairnessCompare != 0)
                    return fairnessCompare;

                int spaceCompare = leftRequest.SpaceId.CompareTo(rightRequest.SpaceId);
                if (spaceCompare != 0)
                    return spaceCompare;

                int yCompare = leftRequest.PageCoord.Y.CompareTo(rightRequest.PageCoord.Y);
                return yCompare != 0
                    ? yCompare
                    : leftRequest.PageCoord.X.CompareTo(rightRequest.PageCoord.X);
            }
        }

        private readonly struct FeedbackMotionKey : IEquatable<FeedbackMotionKey>
        {
            internal FeedbackMotionKey(int spaceId, VirtualTextureViewId viewId)
            {
                SpaceId = spaceId;
                ViewId = viewId;
            }

            internal int SpaceId { get; }

            private VirtualTextureViewId ViewId { get; }

            public bool Equals(FeedbackMotionKey other)
            {
                return SpaceId == other.SpaceId && ViewId.Equals(other.ViewId);
            }

            public override bool Equals(object obj)
            {
                return obj is FeedbackMotionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(SpaceId, ViewId);
            }
        }

        private readonly struct FeedbackMotionState
        {
            internal FeedbackMotionState(Vector2 centroid)
            {
                Centroid = centroid;
            }

            internal Vector2 Centroid { get; }
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void AutoInitialize()
        {
            Initialize();
        }

        protected override void OnInitialize()
        {
            s_FeedbackAggregator = new VTFeedbackNativeAggregator();
        }

        protected override void OnDeinitialize()
        {
            foreach (KeyValuePair<int, VTPageTableSpace> pair in s_PageTableSpaces)
                pair.Value.Dispose();

            foreach (VTPhysicalPool pool in s_PhysicalPools.Values)
                pool.Dispose();

            s_PageTableSpaces.Clear();
            s_SpaceIdsByName.Clear();
            s_Allocations.Clear();
            s_AllocationIdsByName.Clear();
            s_AllocationIdBySpaceId.Clear();
            s_PhysicalPools.Clear();
            s_ProducerRegistry.Dispose();
            VTStreamChunkManager.ResetShared();
            s_CompletedReadbacks.Clear();
            s_InjectedReadbacks.Clear();
            s_FeedbackAggregator?.Dispose();
            s_FeedbackAggregator = null;
            s_FeedbackMotionStates.Clear();
            s_PrefetchBiasBySpace.Clear();
            s_RemainingResidencyBudgetBySpace.Clear();
            s_FeedbackMotionKeysToRemove.Clear();
            s_UploadSpaceOrder.Clear();
            s_PendingUploadCandidates.Clear();
            s_FeedbackCameraSystem.Dispose();
            s_NextSpaceId = 1;
            s_NextAllocationId = 1;
            s_FallbackFrameIndex = -1;
            s_AdaptiveMipBiasController.Reset();
            s_PageTableScatterUploader.Reset();
            s_UploadScheduler.Dispose();
            s_UploadScheduler = new VTUploadScheduler();
            VTUploadScheduler.ResetFenceFactory();
        }

        public new static void Deinitialize()
        {
            VividSubsystem<VirtualTextureSystem>.Deinitialize();
            VirtualTextureStatsRegistry.Clear();
        }

        internal static int RegisterSpace(in VirtualTextureSpaceDesc desc)
        {
            return RegisterAddressSpace(desc, null);
        }

        internal static VTProducerHandle RegisterProducer(in VirtualTextureSpaceDesc desc, VTProducer producer)
        {
            Initialize();
            return s_ProducerRegistry.Register(desc, producer);
        }

        internal static void ReleaseProducer(VTProducerHandle producerHandle)
        {
            Initialize();
            s_ProducerRegistry.Release(producerHandle);
        }

        internal static VTAllocatedVirtualTexture AllocateVirtualTexture(in VTAllocationDesc desc)
        {
            Initialize();

            if (!s_ProducerRegistry.TryGet(desc.ProducerHandle, out _))
                throw new ArgumentException($"[VividRP] VT producer handle '{desc.ProducerHandle}' is not registered.");

            if (s_SpaceIdsByName.TryGetValue(desc.SpaceDesc.SpaceName, out int existingSpaceId)
                && s_AllocationIdBySpaceId.TryGetValue(existingSpaceId, out int existingSpaceAllocationId))
            {
                VTAllocatedVirtualTexture existingSpaceAllocation = s_Allocations[existingSpaceAllocationId];
                if (!existingSpaceAllocation.Description.Equals(desc))
                {
                    throw new InvalidOperationException(
                        $"[VividRP] VT space '{desc.SpaceDesc.SpaceName}' is already allocated by '{existingSpaceAllocation.Name}'.");
                }

                return existingSpaceAllocation;
            }

            if (s_AllocationIdsByName.TryGetValue(desc.Name, out int existingAllocationId))
            {
                VTAllocatedVirtualTexture existingAllocation = s_Allocations[existingAllocationId];
                if (!existingAllocation.Description.Equals(desc))
                {
                    throw new InvalidOperationException(
                        $"[VividRP] VT allocation '{desc.Name}' is already registered with a different descriptor.");
                }

                return existingAllocation;
            }

            return CreateAllocation(desc);
        }

        internal static int RegisterAddressSpace(in VirtualTextureSpaceDesc desc, VTProducer producer)
        {
            Initialize();

            if (s_SpaceIdsByName.TryGetValue(desc.SpaceName, out int existingSpaceId))
            {
                VTPageTableSpace existingAddressSpace = s_PageTableSpaces[existingSpaceId];
                if (!existingAddressSpace.Descriptor.Equals(desc))
                {
                    throw new InvalidOperationException(
                        $"[VividRP] VT space '{desc.SpaceName}' is already registered with a different descriptor.");
                }

                if (!s_ProducerRegistry.IsSameProducer(existingAddressSpace.ProducerHandle, producer))
                {
                    throw new InvalidOperationException(
                        $"[VividRP] VT space '{desc.SpaceName}' is already registered with a different producer.");
                }

                return existingSpaceId;
            }

            VTProducerHandle producerHandle = s_ProducerRegistry.Register(desc, producer);
            try
            {
                VTAllocatedVirtualTexture allocation = AllocateVirtualTexture(
                    VTAllocationDesc.FromSpaceDesc(desc, producerHandle));
                return allocation.SpaceId;
            }
            catch
            {
                s_ProducerRegistry.Release(producerHandle);
                throw;
            }
        }

        internal static int RegisterOrReconfigureAddressSpace(in VirtualTextureSpaceDesc desc, VTProducer producer)
        {
            Initialize();

            if (s_SpaceIdsByName.TryGetValue(desc.SpaceName, out int existingSpaceId))
            {
                VTPageTableSpace existingAddressSpace = s_PageTableSpaces[existingSpaceId];
                bool sameProducer = s_ProducerRegistry.IsSameProducer(existingAddressSpace.ProducerHandle, producer);
                if (existingAddressSpace.Descriptor.Equals(desc)
                    && sameProducer)
                {
                    return existingSpaceId;
                }

                ReplacePageTableSpace(existingSpaceId, desc, producer);
                return existingSpaceId;
            }

            return RegisterAddressSpace(desc, producer);
        }

        internal static bool UnregisterAddressSpace(int spaceId)
        {
            Initialize();

            if (!s_PageTableSpaces.ContainsKey(spaceId))
                return false;

            RemovePageTableSpace(spaceId);
            return true;
        }

        internal static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureMarker.Auto())
            {
                UpdateCore(frameData, cmd);
            }
        }

        protected override void OnUpdate(ContextContainer frameData, CommandBuffer cmd)
        {
            Update(frameData, cmd);
        }

        private static void UpdateCore(ContextContainer frameData, CommandBuffer cmd)
        {
            VividVirtualTextureFrameData virtualTextureFrameData;
            VividCameraData cameraData;
            Camera camera;
            VirtualTextureViewId activeViewId;
            CameraType activeCameraType;
            VirtualTextureViewId cachePriorityViewId;
            VirtualTextureFeedbackViewSignature activeViewSignature;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFrameSetupMarker.Auto())
            {
                if (!IsInitialized)
                    Initialize();

                virtualTextureFrameData = frameData?.GetOrCreate<VividVirtualTextureFrameData>();
                virtualTextureFrameData?.Reset();
                s_FeedbackCameraSystem.PurgeDestroyedCameras();

                cameraData = TryGetCameraData(frameData);
                camera = cameraData?.camera;
                activeViewId = VirtualTextureViewId.FromCameraData(cameraData);
                activeCameraType = camera != null ? camera.cameraType : default;
                cachePriorityViewId = ResolveCachePriorityViewId(activeViewId, activeCameraType);
                activeViewSignature = VirtualTextureFeedbackViewSignature.FromCameraData(cameraData);
            }

            int lastReadbackFrame = -1;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackReadbackMarker.Auto())
                CollectCompletedReadbacks(ref lastReadbackFrame);

            int faultCount = 0;
            int feedbackOverflowCount = 0;
            int fallbackSampleCount = 0;
            int activeViewFaultCount = 0;
            int activeViewFeedbackOverflowCount = 0;
            int activeViewFallbackSampleCount = 0;
            int activeViewLastReadbackFrame = -1;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackReadbackStatsMarker.Auto())
            {
                for (int batchIndex = 0; batchIndex < s_CompletedReadbacks.Count; batchIndex++)
                {
                    VirtualTextureFeedbackBatch batch = s_CompletedReadbacks[batchIndex];
                    faultCount += batch.RequestCount;
                    feedbackOverflowCount += batch.FeedbackOverflowCount;
                    fallbackSampleCount += batch.FallbackSampleCount;

                    if (IsBatchFromView(batch, activeViewId, activeCameraType))
                    {
                        activeViewFaultCount += batch.RequestCount;
                        activeViewFeedbackOverflowCount += batch.FeedbackOverflowCount;
                        activeViewFallbackSampleCount += batch.FallbackSampleCount;
                        activeViewLastReadbackFrame = Mathf.Max(activeViewLastReadbackFrame, batch.FrameIndex);
                    }
                }
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackAggregateMarker.Auto())
            {
                s_FeedbackAggregator.Aggregate(
                    s_CompletedReadbacks,
                    cachePriorityViewId,
                    activeViewId,
                    activeCameraType);
            }

            int deduplicatedRequestCount = s_FeedbackAggregator.AggregatedRequests.Length;
            int activeViewDeduplicatedRequestCount = s_FeedbackAggregator.ActiveViewRequestCount;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrefetchBiasMarker.Auto())
                ResolvePrefetchBiasBySpace(cachePriorityViewId);

            int frameIndex = ResolveFrameIndex(frameData);
            int evictionCount = 0;
            int pendingMipGapSum = 0;
            int pendingMipGapMax = 0;
            int pendingMipGapSampleCount = 0;
            int prefetchRequestCount = 0;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsBeginFrameMarker.Auto())
            {
                VTVirtualTextureStreamRequestGate.BeginFrame();
                VTStreamChunkManager.Shared.BeginFrame();
                s_UploadScheduler.BeginFrame();
            }
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCommitCompletedMarker.Auto())
                s_UploadScheduler.CommitCompletedUploads(s_UploadCommitterResolver);

            int globalResidencyRequestBudget = s_UploadScheduler.MaxUploadsPerFrame;
            int remainingResidencyRequestBudget = globalResidencyRequestBudget;
            s_RemainingResidencyBudgetBySpace.Clear();
            foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
                s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId] = 0;

            NativeArray<VirtualTextureAggregatedFeedbackRequest> aggregatedRequests =
                s_FeedbackAggregator.AggregatedRequests;
            for (int requestIndex = 0;
                 requestIndex < aggregatedRequests.Length && remainingResidencyRequestBudget > 0;
                 requestIndex++)
            {
                VirtualTextureAggregatedFeedbackRequest request = aggregatedRequests[requestIndex];
                if (!s_PageTableSpaces.TryGetValue(request.SpaceId, out VTPageTableSpace addressSpace))
                    continue;

                int assignedSpaceBudget = s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId];
                if (assignedSpaceBudget >= addressSpace.Descriptor.MaxUploadsPerFrame
                    || !addressSpace.RequiresNewPhysicalPage(request.PageCoord))
                {
                    continue;
                }

                s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId] = assignedSpaceBudget + 1;
                remainingResidencyRequestBudget -= 1;
            }

            int allocatedResidencyRequestCount = 0;
            foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
            {
                int assignedSpaceBudget = s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId];
                if (!s_FeedbackAggregator.TryGetRequestsForSpace(
                        addressSpace.SpaceId,
                        out NativeSlice<VirtualTextureAggregatedFeedbackRequest> spaceRequests))
                {
                    s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId] =
                        addressSpace.Descriptor.MaxUploadsPerFrame;
                    continue;
                }

                s_PrefetchBiasBySpace.TryGetValue(addressSpace.SpaceId, out Vector2Int prefetchBias);
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyMarker.Auto())
                {
                    using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyProcessRequestsMarker.Auto())
                    {
                        VTResidencyProcessResult residencyResult = addressSpace.ProcessRequests(
                            spaceRequests,
                            cachePriorityViewId,
                            prefetchBias,
                            frameIndex,
                            assignedSpaceBudget,
                            allowNeighborPrefetch: false,
                            rebuildPageTable: false);
                        allocatedResidencyRequestCount += residencyResult.AllocatedRequestCount;
                        s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId] = Mathf.Max(
                            0,
                            addressSpace.Descriptor.MaxUploadsPerFrame - residencyResult.AllocatedRequestCount);
                        AccumulateResidencyStats(
                            residencyResult,
                            ref evictionCount,
                            ref pendingMipGapSum,
                            ref pendingMipGapMax,
                            ref pendingMipGapSampleCount,
                            ref prefetchRequestCount);
                    }
                }
            }

            remainingResidencyRequestBudget = Mathf.Max(
                0,
                globalResidencyRequestBudget - allocatedResidencyRequestCount);

            for (int requestIndex = 0;
                 requestIndex < aggregatedRequests.Length && remainingResidencyRequestBudget > 0;
                 requestIndex++)
            {
                VirtualTextureAggregatedFeedbackRequest request = aggregatedRequests[requestIndex];
                if (!s_PageTableSpaces.TryGetValue(request.SpaceId, out VTPageTableSpace addressSpace)
                    || addressSpace.Descriptor.NeighborPrefetchCount <= 0)
                {
                    continue;
                }

                int spaceResidencyRequestBudget = s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId];
                if (spaceResidencyRequestBudget <= 0)
                    continue;

                s_PrefetchBiasBySpace.TryGetValue(addressSpace.SpaceId, out Vector2Int prefetchBias);
                var requestSlice = new NativeSlice<VirtualTextureAggregatedFeedbackRequest>(
                    aggregatedRequests,
                    requestIndex,
                    1);
                VTResidencyProcessResult residencyResult = addressSpace.ProcessRequests(
                    requestSlice,
                    cachePriorityViewId,
                    prefetchBias,
                    frameIndex,
                    Mathf.Min(remainingResidencyRequestBudget, spaceResidencyRequestBudget),
                    allowNeighborPrefetch: true,
                    rebuildPageTable: false);
                remainingResidencyRequestBudget = Mathf.Max(
                    0,
                    remainingResidencyRequestBudget - residencyResult.AllocatedRequestCount);
                s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId] = Mathf.Max(
                    0,
                    spaceResidencyRequestBudget - residencyResult.AllocatedRequestCount);
                evictionCount += residencyResult.EvictionCount;
                prefetchRequestCount += residencyResult.PrefetchRequestCount;
            }

            CollectAndSchedulePendingUploads(frameIndex, cmd);
            VTStreamChunkManager.Shared.SubmitPendingReads();

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsFinalizeMarker.Auto())
                s_UploadScheduler.FinalizeUploads(cmd);
            int inFlightUploadBatchCount = s_UploadScheduler.InFlightBatchCount;
            int duplicateUploadCount = s_UploadScheduler.LastDuplicateUploadCount;
            int skippedUploadCount = s_UploadScheduler.LastSkippedUploadCount;
            int blockedUploadCount = Mathf.Max(0, skippedUploadCount - duplicateUploadCount);
            int streamSaturatedRequestCount = Mathf.Max(
                VTVirtualTextureStreamRequestGate.LastSaturatedRequestCount,
                VTStreamChunkManager.Shared.LastPressureCount);
            int cpuProducedPageCount = s_UploadScheduler.LastCpuProducedPageCount;
            int gpuProducedPageCount = s_UploadScheduler.LastGpuProducedPageCount;
            int gpuDispatchCount = s_UploadScheduler.LastGpuDispatchCount;
            int pendingUploadCount = CollectPendingUploadCount();
            float adaptiveMipBias = s_AdaptiveMipBiasController.Update(
                frameIndex,
                new VTAdaptiveMipBiasInputs(
                    globalResidencyRequestBudget,
                    pendingUploadCount,
                    blockedUploadCount,
                    streamSaturatedRequestCount,
                    feedbackOverflowCount,
                    fallbackSampleCount));
            if (virtualTextureFrameData != null)
                virtualTextureFrameData.AdaptiveMipBias = adaptiveMipBias;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTexturePageTableMarker.Auto())
            {
                foreach (KeyValuePair<int, VTPageTableSpace> pair in s_PageTableSpaces)
                    pair.Value.RebuildPageTableIfDirty();
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureCleanupMarker.Auto())
            {
                s_FeedbackAggregator.Clear();
                s_CompletedReadbacks.Clear();
                s_PrefetchBiasBySpace.Clear();
                s_RemainingResidencyBudgetBySpace.Clear();
            }

            bool supportsFeedback;
            VirtualTextureFeedbackCameraState cameraFeedbackState;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsMarker.Auto())
            {
                supportsFeedback = IsFeedbackSupported(camera);
                cameraFeedbackState = supportsFeedback
                    ? s_FeedbackCameraSystem.GetOrCreateBase(camera)
                    : null;
            }

            int residentPageCount = 0;
            int freePageCount = 0;
            int feedbackCapacity = 0;
            string statusMessage = s_PageTableSpaces.Count == 0 ? "[VividRP] VT has no registered spaces." : string.Empty;

            foreach (KeyValuePair<int, VTPageTableSpace> pair in s_PageTableSpaces)
            {
                VTPageTableSpace addressSpace = pair.Value;
                ComputeBuffer feedbackRequests = null;
                ComputeBuffer feedbackCounter = null;
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsMarker.Auto())
                {
                    feedbackCapacity += addressSpace.StackDesc.FeedbackCapacity;

                    if (supportsFeedback && cameraFeedbackState != null)
                    {
                        VirtualTextureFeedbackBufferState feedbackBufferState =
                            cameraFeedbackState.GetOrCreateSpaceState(addressSpace.SpaceId);
                        if (!feedbackBufferState.TryPrepareForFrame(
                                cmd,
                                addressSpace.Descriptor.SpaceName,
                                camera,
                                activeViewId,
                                activeViewSignature,
                                addressSpace.StackDesc.FeedbackCapacity,
                                frameIndex,
                                addressSpace.PendingRequestCount > 0
                                || s_UploadScheduler.HasInFlightUploadForSpace(addressSpace.SpaceId),
                                out feedbackRequests,
                                out feedbackCounter,
                                out string feedbackStatus)
                            && string.IsNullOrEmpty(statusMessage)
                            && !string.IsNullOrEmpty(feedbackStatus))
                        {
                            statusMessage = feedbackStatus;
                        }
                    }
                }

                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureBindingsMarker.Auto())
                {
                    residentPageCount += addressSpace.ResidentPageCount;
                    freePageCount += addressSpace.FreePageCount;
                    int allocationId = s_AllocationIdBySpaceId.TryGetValue(addressSpace.SpaceId, out int mappedAllocationId)
                        ? mappedAllocationId
                        : 0;
                    bool privateSpace = allocationId > 0
                                        && s_Allocations.TryGetValue(allocationId, out VTAllocatedVirtualTexture allocation)
                                        && allocation.Description.PrivateSpace;
                    virtualTextureFrameData?.AddBinding(addressSpace.CreateBinding(
                        allocationId,
                        privateSpace,
                        feedbackRequests,
                        feedbackCounter));
                }
            }

            VTPhysicalPoolStats physicalPoolStats;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStatsPhysicalPoolsMarker.Auto())
                physicalPoolStats = CollectPhysicalPoolStats();
            residentPageCount = physicalPoolStats.ResidentPageCount;
            freePageCount = physicalPoolStats.FreePageCount;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStatsMarker.Auto())
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStatsReportGlobalMarker.Auto())
            {
                var globalStats = new VTDebugStats(
                    s_PageTableSpaces.Count,
                    residentPageCount,
                    freePageCount,
                    pendingUploadCount,
                    evictionCount,
                    faultCount,
                    deduplicatedRequestCount,
                    feedbackOverflowCount,
                    inFlightUploadBatchCount,
                    duplicateUploadCount,
                    skippedUploadCount,
                    fallbackSampleCount,
                    lastReadbackFrame,
                    statusMessage,
                    VirtualTextureViewId.Invalid,
                    default,
                    null,
                    -1,
                    0,
                    0,
                    0,
                    0,
                    false,
                    feedbackCapacity,
                    false,
                    physicalPoolStats.PoolCount,
                    physicalPoolStats.ResidentPageCount,
                    physicalPoolStats.FreePageCount,
                    physicalPoolStats.LockedPageCount,
                    physicalPoolStats.EvictedPageCount,
                    pendingMipGapSum,
                    pendingMipGapMax,
                    pendingMipGapSampleCount,
                    prefetchRequestCount,
                    cpuProducedPageCount,
                    gpuProducedPageCount,
                    gpuDispatchCount,
                    streamSaturatedRequestCount,
                    adaptiveMipBias);
                VirtualTextureStatsRegistry.Report(globalStats);
            }

            if (camera != null)
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStatsMarker.Auto())
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStatsReportViewMarker.Auto())
                {
                    var viewStats = new VTDebugStats(
                        s_PageTableSpaces.Count,
                        residentPageCount,
                        freePageCount,
                        pendingUploadCount,
                        evictionCount,
                        activeViewFaultCount,
                        activeViewDeduplicatedRequestCount,
                        activeViewFeedbackOverflowCount,
                        inFlightUploadBatchCount,
                        duplicateUploadCount,
                        skippedUploadCount,
                        activeViewFallbackSampleCount,
                        activeViewLastReadbackFrame,
                        statusMessage,
                        activeViewId,
                        camera.cameraType,
                        ResolveCameraName(cameraData, camera),
                        frameIndex,
                        cameraData?.actualWidth ?? 0,
                        cameraData?.actualHeight ?? 0,
                        cameraData?.pixelWidth ?? 0,
                        cameraData?.pixelHeight ?? 0,
                        supportsFeedback,
                        feedbackCapacity,
                        true,
                        physicalPoolStats.PoolCount,
                        physicalPoolStats.ResidentPageCount,
                        physicalPoolStats.FreePageCount,
                        physicalPoolStats.LockedPageCount,
                        physicalPoolStats.EvictedPageCount,
                        pendingMipGapSum,
                        pendingMipGapMax,
                        pendingMipGapSampleCount,
                        prefetchRequestCount,
                        cpuProducedPageCount,
                        gpuProducedPageCount,
                        gpuDispatchCount,
                        streamSaturatedRequestCount,
                        adaptiveMipBias);
                    VirtualTextureStatsRegistry.ReportView(viewStats);
                }
            }
        }

        internal static bool RecordPageTableUpdates(RenderGraph renderGraph)
        {
            Initialize();
            return s_PageTableScatterUploader.Record(renderGraph, s_PageTableSpaces.Values);
        }

        internal static void CommitPageTableUpdates()
        {
            s_PageTableScatterUploader.Commit();
        }

        internal static void AbortPageTableUpdates()
        {
            s_PageTableScatterUploader.Abort();
        }

        internal static void RegisterPageTableReadDependencies(
            IRenderPass pass,
            VividVirtualTextureFrameData frameData)
        {
            if (pass == null || frameData == null || !PassRecorder.IsPassTextureImportActive)
                return;

            IReadOnlyList<VirtualTextureSpaceBinding> bindings = frameData.Bindings;
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                GraphicsBuffer pageTableBuffer = bindings[bindingIndex].PageTableBuffer;
                if (pageTableBuffer != null)
                    PassRecorder.ImportBufferForPass(pass, pageTableBuffer, AccessFlags.Read);
            }
        }

        internal static bool TryGetPendingRequests(int spaceId, out IReadOnlyList<VTRequest> requests)
        {
            if (s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace))
            {
                requests = addressSpace.PendingRequests;
                return true;
            }

            requests = Array.Empty<VTRequest>();
            return false;
        }

        internal static bool TryGetPendingUploadRequests(int spaceId, out IReadOnlyList<VirtualTextureUploadRequest> requests)
        {
            if (s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace))
            {
                IReadOnlyList<VTRequest> pendingRequests = addressSpace.PendingRequests;
                var uploadRequests = new List<VirtualTextureUploadRequest>(pendingRequests.Count);
                for (int requestIndex = 0; requestIndex < pendingRequests.Count; requestIndex++)
                    uploadRequests.Add(new VirtualTextureUploadRequest(pendingRequests[requestIndex]));

                requests = uploadRequests;
                return true;
            }

            requests = Array.Empty<VirtualTextureUploadRequest>();
            return false;
        }

        internal static bool CommitRequest(in VTRequest request)
        {
            return s_PageTableSpaces.TryGetValue(request.SpaceId, out VTPageTableSpace addressSpace)
                   && addressSpace.TryCommitRequest(request);
        }

        internal static bool CommitUpload(in VirtualTextureUploadRequest request)
        {
            return CommitRequest(request.Request);
        }

        internal static int FlushProducer(VTProducer producer)
        {
            Initialize();

            VTProducer resolvedProducer = ResolveStoredProducer(producer);
            foreach (KeyValuePair<int, VTPageTableSpace> pair in s_PageTableSpaces)
            {
                if (s_ProducerRegistry.IsSameProducer(pair.Value.ProducerHandle, resolvedProducer))
                    s_UploadScheduler.CancelUploadsForSpace(pair.Key);
            }

            string producerName = resolvedProducer.Name;
            int flushedCount = 0;
            foreach (VTPhysicalPool pool in s_PhysicalPools.Values)
                flushedCount += pool.FlushProducer(VTProducerHandle.Invalid, producerName);

            if (flushedCount > 0)
            {
                foreach (KeyValuePair<int, VTPageTableSpace> pair in s_PageTableSpaces)
                    pair.Value.RebuildPageTableIfDirty();
            }

            return flushedCount;
        }

        internal static int FlushRegion(int spaceId, int mip, RectInt pageRegion)
        {
            if (!s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace))
                return 0;

            s_UploadScheduler.CancelUploadsForRegion(spaceId, mip, pageRegion);
            return addressSpace.FlushRegion(mip, pageRegion);
        }

        internal static int FlushRegions(int spaceId, IReadOnlyList<VTPageRegion> pageRegions)
        {
            if (!s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                || pageRegions == null
                || pageRegions.Count == 0)
            {
                return 0;
            }

            for (int regionIndex = 0; regionIndex < pageRegions.Count; regionIndex++)
            {
                VTPageRegion region = pageRegions[regionIndex];
                s_UploadScheduler.CancelUploadsForRegion(spaceId, region.Mip, region.PageRegion);
            }

            return addressSpace.FlushRegions(pageRegions);
        }

        internal static bool SetPageLocked(int spaceId, in VirtualTexturePageCoord coord, bool locked = true)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                   && addressSpace.TrySetPageLocked(coord, locked);
        }

        internal static bool TryMakePageResident(
            int spaceId,
            in VirtualTexturePageCoord coord,
            bool locked = true,
            int frameIndex = 0)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                   && addressSpace.TryMakePageResident(coord, locked, frameIndex);
        }

        internal static bool TryQueuePageResident(
            int spaceId,
            in VirtualTexturePageCoord coord,
            bool locked = true,
            int frameIndex = 0)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                   && addressSpace.TryQueuePageResident(coord, locked, frameIndex);
        }

        internal static void InjectCompletedReadbackForTesting(CameraType cameraType, params ulong[] requestKeys)
        {
            InjectCompletedReadbackStatsForTesting(
                cameraType,
                feedbackOverflowCount: 0,
                fallbackSampleCount: 0,
                requestKeys: requestKeys);
        }

        internal static void InjectCompletedReadbackForTesting(Camera camera, params ulong[] requestKeys)
        {
            InjectCompletedReadbackStatsForTesting(
                camera,
                feedbackOverflowCount: 0,
                fallbackSampleCount: 0,
                requestKeys: requestKeys);
        }

        internal static void InjectCompletedReadbackStatsForTesting(
            CameraType cameraType,
            int feedbackOverflowCount,
            int fallbackSampleCount,
            params ulong[] requestKeys)
        {
            InjectCompletedReadbackStatsForTesting(
                VirtualTextureViewId.FromCameraType(cameraType),
                cameraType,
                feedbackOverflowCount,
                fallbackSampleCount,
                requestKeys);
        }

        internal static void InjectCompletedReadbackStatsForTesting(
            Camera camera,
            int feedbackOverflowCount,
            int fallbackSampleCount,
            params ulong[] requestKeys)
        {
            InjectCompletedReadbackStatsForTesting(
                VirtualTextureViewId.FromCamera(camera),
                camera != null ? camera.cameraType : CameraType.Game,
                feedbackOverflowCount,
                fallbackSampleCount,
                requestKeys);
        }

        private static void InjectCompletedReadbackStatsForTesting(
            VirtualTextureViewId viewId,
            CameraType cameraType,
            int feedbackOverflowCount,
            int fallbackSampleCount,
            params ulong[] requestKeys)
        {
            if (requestKeys == null || requestKeys.Length == 0)
            {
                if (feedbackOverflowCount <= 0 && fallbackSampleCount <= 0)
                    return;

                requestKeys = Array.Empty<ulong>();
            }

            s_InjectedReadbacks.Add(new VirtualTextureFeedbackBatch(
                viewId,
                cameraType,
                requestKeys,
                requestKeys.Length,
                Time.frameCount,
                feedbackOverflowCount,
                fallbackSampleCount));
        }

        internal static bool TryGetPageTableEntryForTesting(
            int spaceId,
            in VirtualTexturePageCoord coord,
            out VirtualTexturePageTableEntry entry)
        {
            return TryGetPageTableEntry(spaceId, coord, out entry);
        }

        internal static bool TryGetPageTableEntry(
            int spaceId,
            in VirtualTexturePageCoord coord,
            out VirtualTexturePageTableEntry entry)
        {
            if (s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace))
                return addressSpace.TryGetPageTableEntry(coord, out entry);

            entry = default;
            return false;
        }

        internal static int GetResidentPageCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.ResidentPageCount
                : 0;
        }

        internal static int GetFreePageCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.FreePageCount
                : 0;
        }

        internal static int GetPendingUploadCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PendingRequestCount
                : 0;
        }

        internal static int GetPageTableRebuildCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PageTableRebuildCount
                : 0;
        }

        internal static int GetPageTableLastRecomputedEntryCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PageTableLastRecomputedEntryCount
                : 0;
        }

        internal static int GetPageTableLastUploadedEntryCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PageTableLastUploadedEntryCount
                : 0;
        }

        internal static int GetPageTableSparseUploadCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PageTableSparseUploadCount
                : 0;
        }

        internal static int GetPageTableFullUploadCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PageTableFullUploadCount
                : 0;
        }

        internal static int GetPageTablePendingUploadEntryCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PageTablePendingUploadEntryCount
                : 0;
        }

        internal static int GetPageTableScatterUploadCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PageTableScatterUploadCount
                : 0;
        }

        internal static int GetPageTableLegacySetDataCallCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PageTableLegacySetDataCallCount
                : 0;
        }

        internal static int GetLastPageTableScatterEntryCountForTesting()
        {
            return s_PageTableScatterUploader.LastScatterEntryCount;
        }

        internal static int GetLastPageTableScatterSpaceCountForTesting()
        {
            return s_PageTableScatterUploader.LastScatterSpaceCount;
        }

        internal static int GetLastPageTableScatterChunkCountForTesting()
        {
            return s_PageTableScatterUploader.LastScatterChunkCount;
        }

        internal static int GetLastPageTableScatterDispatchCountForTesting()
        {
            return s_PageTableScatterUploader.LastScatterDispatchCount;
        }

        internal static int GetLastPageTableTransientSetDataCallCountForTesting()
        {
            return s_PageTableScatterUploader.LastTransientSetDataCallCount;
        }

        internal static int GetLastPageTableLegacySetDataCallCountForTesting()
        {
            return s_PageTableScatterUploader.LastLegacySetDataCallCount;
        }

        internal static bool TryCapturePendingPageTableUpdatesForTesting(
            int spaceId,
            out VTPageTableScatterUpdate[] updates,
            out int pendingVersion,
            out bool fullUpload)
        {
            if (!s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                || addressSpace.PageTablePendingUploadEntryCount <= 0)
            {
                updates = Array.Empty<VTPageTableScatterUpdate>();
                pendingVersion = 0;
                fullUpload = false;
                return false;
            }

            updates = new VTPageTableScatterUpdate[addressSpace.PageTablePendingUploadEntryCount];
            int copiedCount = addressSpace.CopyPendingPageTableUpdates(
                updates,
                0,
                out pendingVersion,
                out fullUpload);
            if (copiedCount == updates.Length)
                return true;

            Array.Resize(ref updates, copiedCount);
            return copiedCount > 0;
        }

        internal static bool CommitCapturedPageTableUpdatesForTesting(
            int spaceId,
            int pendingVersion,
            bool fullUpload,
            int uploadedEntryCount)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                   && addressSpace.PageTableUpdater.CommitPendingUpload(
                       pendingVersion,
                       fullUpload,
                       uploadedEntryCount);
        }

        internal static uint GetPendingRequestRevisionForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PendingRequestRevision
                : 0u;
        }

        internal static int GetPendingOrderCacheBuildCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PendingOrderCacheBuildCount
                : 0;
        }

        internal static int GetPendingOrderCacheHitCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PendingOrderCacheHitCount
                : 0;
        }

        internal static int GetResidencyClassificationCapacityForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.ResidencyClassificationCapacity
                : 0;
        }

        internal static bool WasLastResidencyClassificationParallelForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                   && addressSpace.LastResidencyClassificationUsedParallelJob;
        }

        internal static int GetPhysicalPoolCountForTesting()
        {
            return s_PhysicalPools.Count;
        }

        internal static VTPhysicalPoolStats GetPhysicalPoolStatsForTesting()
        {
            return CollectPhysicalPoolStats();
        }

        internal static bool TryGetPhysicalCacheForTesting(int spaceId, out Texture2D physicalCache)
        {
            return TryGetPhysicalCacheForTesting(spaceId, 0, out physicalCache);
        }

        internal static bool TryGetPhysicalCacheForTesting(
            int spaceId,
            int physicalGroup,
            out Texture2D physicalCache)
        {
            if (s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace))
            {
                physicalCache = addressSpace.PhysicalPool.GetTextureForGroup(physicalGroup);
                return physicalCache != null;
            }

            physicalCache = null;
            return false;
        }

        internal static bool IsCameraFeedbackStateCreatedForTesting(Camera camera)
        {
            if (camera == null)
                return false;

            foreach (KeyValuePair<Camera, VirtualTextureFeedbackCameraState> pair in s_FeedbackCameraSystem.EnumerateStates())
            {
                if (ReferenceEquals(pair.Key, camera))
                    return true;
            }

            return false;
        }

        internal static bool TryGetProducerNameForTesting(int spaceId, out string producerName)
        {
            if (s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace))
            {
                return s_ProducerRegistry.TryGetProducerName(addressSpace.ProducerHandle, out producerName);
            }

            producerName = null;
            return false;
        }

        internal static bool TryGetAllocationForTesting(
            int spaceId,
            out VTAllocatedVirtualTexture allocation)
        {
            if (s_AllocationIdBySpaceId.TryGetValue(spaceId, out int allocationId)
                && s_Allocations.TryGetValue(allocationId, out allocation))
            {
                return true;
            }

            allocation = null;
            return false;
        }

        internal static void SetUploadFenceFactoryForTesting(IVTUploadFenceFactory fenceFactory)
        {
            VTUploadScheduler.SetFenceFactoryForTesting(fenceFactory);
        }

        internal static void SetUploadMemoryBudgetForTesting(int maxUploadBytesPerFrame)
        {
            s_UploadScheduler.MaxUploadBytesPerFrame = maxUploadBytesPerFrame;
        }

        internal static void SetUploadPageBudgetForTesting(int maxUploadsPerFrame)
        {
            s_UploadScheduler.MaxUploadsPerFrame = maxUploadsPerFrame;
        }

        internal static float GetAdaptiveMipBiasForTesting()
        {
            return s_AdaptiveMipBiasController.CurrentMipBias;
        }

        internal static int GetGpuUploadStagingTextureCountForTesting()
        {
            return s_UploadScheduler.GpuStagingTextureCount;
        }

        internal static int GetCpuUploadStagingTextureCountForTesting()
        {
            return s_UploadScheduler.CpuStagingTextureCount;
        }

        internal static int GetUploadScratchPixelCountForTesting()
        {
            return s_UploadScheduler.ScratchPixelCount;
        }

        private sealed class UploadCommitterResolver : IVTUploadRequestCommitterResolver
        {
            public IVTUploadRequestCommitter ResolveCommitter(int spaceId)
            {
                return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                    ? addressSpace
                    : null;
            }
        }

        private static VTProducer ResolveStoredProducer(VTProducer producer)
        {
            return producer ?? VTNullProducer.Instance;
        }

        private static VTPhysicalPool AcquirePhysicalPool(in VirtualTextureSpaceDesc desc)
        {
            VTPhysicalPoolDesc poolDesc = VTPhysicalPoolDesc.FromSpaceDesc(desc);
            if (!s_PhysicalPools.TryGetValue(poolDesc, out VTPhysicalPool pool))
            {
                pool = new VTPhysicalPool(desc.SpaceName, poolDesc);
                s_PhysicalPools.Add(poolDesc, pool);
            }

            pool.AddRef();
            return pool;
        }

        private static VTPageTableSpace CreatePageTableSpace(
            int spaceId,
            in VirtualTextureSpaceDesc desc,
            VTProducerHandle producerHandle)
        {
            if (!s_ProducerRegistry.TryGet(producerHandle, out VTRegisteredProducer producer))
                throw new ArgumentException($"[VividRP] VT producer handle '{producerHandle}' is not registered.");

            VTPhysicalPool physicalPool = AcquirePhysicalPool(desc);
            try
            {
                return new VTPageTableSpace(spaceId, desc, producer, physicalPool);
            }
            catch
            {
                ReleasePhysicalPool(physicalPool);
                throw;
            }
        }

        private static VTAllocatedVirtualTexture CreateAllocation(in VTAllocationDesc desc)
        {
            int allocationId = s_NextAllocationId++;
            int spaceId = s_NextSpaceId++;
            VTPageTableSpace addressSpace = CreatePageTableSpace(
                spaceId,
                desc.SpaceDesc,
                desc.ProducerHandle);

            var allocation = new VTAllocatedVirtualTexture(allocationId, spaceId, desc);
            s_PageTableSpaces.Add(spaceId, addressSpace);
            s_SpaceIdsByName.Add(desc.SpaceDesc.SpaceName, spaceId);
            s_Allocations.Add(allocationId, allocation);
            s_AllocationIdsByName.Add(desc.Name, allocationId);
            s_AllocationIdBySpaceId.Add(spaceId, allocationId);
            return allocation;
        }

        private static void ReleasePhysicalPool(VTPhysicalPool pool)
        {
            if (pool == null || pool.ReleaseRef() > 0)
                return;

            s_PhysicalPools.Remove(pool.Desc);
            pool.Dispose();
        }

        private static VTPhysicalPoolStats CollectPhysicalPoolStats()
        {
            int residentPageCount = 0;
            int freePageCount = 0;
            int lockedPageCount = 0;
            int evictedPageCount = 0;
            foreach (VTPhysicalPool pool in s_PhysicalPools.Values)
            {
                residentPageCount += pool.ResidentPageCount;
                freePageCount += pool.FreePageCount;
                lockedPageCount += pool.LockedPageCount;
                evictedPageCount += pool.EvictedPageCount;
            }

            return new VTPhysicalPoolStats(
                s_PhysicalPools.Count,
                residentPageCount,
                freePageCount,
                lockedPageCount,
                evictedPageCount);
        }

        private static void ReplacePageTableSpace(int spaceId, in VirtualTextureSpaceDesc desc, VTProducer producer)
        {
            if (!s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace existingAddressSpace))
                return;

            VTProducerHandle producerHandle = s_ProducerRegistry.Register(desc, producer);
            if (!s_AllocationIdBySpaceId.TryGetValue(spaceId, out int allocationId))
            {
                s_ProducerRegistry.Release(producerHandle);
                throw new InvalidOperationException($"[VividRP] VT page-table space '{spaceId}' has no allocation.");
            }

            string allocationName = existingAddressSpace.Descriptor.SpaceName;
            if (s_Allocations.TryGetValue(allocationId, out VTAllocatedVirtualTexture existingAllocation))
                allocationName = existingAllocation.Name;
            var allocationDesc = VTAllocationDesc.FromSpaceDesc(desc, producerHandle);
            s_UploadScheduler.CancelUploadsForSpace(spaceId);
            VTPhysicalPool oldPhysicalPool = existingAddressSpace.PhysicalPool;
            VTProducerHandle oldProducerHandle = existingAddressSpace.ProducerHandle;
            existingAddressSpace.Dispose();
            ReleasePhysicalPool(oldPhysicalPool);
            s_ProducerRegistry.Release(oldProducerHandle);
            RemoveFeedbackStateForSpace(spaceId);
            try
            {
                s_PageTableSpaces[spaceId] = CreatePageTableSpace(spaceId, desc, producerHandle);
            }
            catch
            {
                s_ProducerRegistry.Release(producerHandle);
                s_PageTableSpaces.Remove(spaceId);
                s_SpaceIdsByName.Remove(existingAddressSpace.Descriptor.SpaceName);
                s_AllocationIdBySpaceId.Remove(spaceId);
                s_Allocations.Remove(allocationId);
                s_AllocationIdsByName.Remove(allocationName);
                throw;
            }

            if (s_AllocationIdBySpaceId.TryGetValue(spaceId, out int existingAllocationId)
                && s_Allocations.ContainsKey(existingAllocationId))
            {
                s_Allocations[existingAllocationId] =
                    new VTAllocatedVirtualTexture(existingAllocationId, spaceId, allocationDesc);
            }
        }

        private static void RemovePageTableSpace(int spaceId)
        {
            if (!s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace))
                return;

            s_PageTableSpaces.Remove(spaceId);
            s_SpaceIdsByName.Remove(addressSpace.Descriptor.SpaceName);
            if (s_AllocationIdBySpaceId.TryGetValue(spaceId, out int allocationId))
            {
                s_AllocationIdBySpaceId.Remove(spaceId);
                if (s_Allocations.TryGetValue(allocationId, out VTAllocatedVirtualTexture allocation))
                    s_AllocationIdsByName.Remove(allocation.Name);
                s_Allocations.Remove(allocationId);
            }

            s_UploadScheduler.CancelUploadsForSpace(spaceId);
            VTPhysicalPool physicalPool = addressSpace.PhysicalPool;
            VTProducerHandle producerHandle = addressSpace.ProducerHandle;
            addressSpace.Dispose();
            ReleasePhysicalPool(physicalPool);
            s_ProducerRegistry.Release(producerHandle);
            RemoveFeedbackStateForSpace(spaceId);
        }

        private static void RemoveFeedbackStateForSpace(int spaceId)
        {
            s_FeedbackCameraSystem.RemoveSpaceState(spaceId);
            RemoveQueuedFeedbackForSpace(s_CompletedReadbacks, spaceId);
            RemoveQueuedFeedbackForSpace(s_InjectedReadbacks, spaceId);
            RemoveFeedbackMotionStateForSpace(spaceId);
        }

        private static void RemoveFeedbackMotionStateForSpace(int spaceId)
        {
            s_FeedbackMotionKeysToRemove.Clear();
            foreach (FeedbackMotionKey key in s_FeedbackMotionStates.Keys)
            {
                if (key.SpaceId != spaceId)
                    continue;

                s_FeedbackMotionKeysToRemove.Add(key);
            }

            for (int keyIndex = 0; keyIndex < s_FeedbackMotionKeysToRemove.Count; keyIndex++)
                s_FeedbackMotionStates.Remove(s_FeedbackMotionKeysToRemove[keyIndex]);
        }

        private static void CollectCompletedReadbacks(ref int lastReadbackFrame)
        {
            foreach (KeyValuePair<Camera, VirtualTextureFeedbackCameraState> cameraPair in s_FeedbackCameraSystem.EnumerateStates())
                CollectCompletedReadbacks(cameraPair.Value, ref lastReadbackFrame);

            for (int batchIndex = s_InjectedReadbacks.Count - 1; batchIndex >= 0; batchIndex--)
            {
                VirtualTextureFeedbackBatch batch = s_InjectedReadbacks[batchIndex];
                s_CompletedReadbacks.Add(batch);
                lastReadbackFrame = Mathf.Max(lastReadbackFrame, batch.FrameIndex);
                s_InjectedReadbacks.RemoveAt(batchIndex);
            }
        }

        private static void CollectCompletedReadbacks(
            VirtualTextureFeedbackCameraState cameraState,
            ref int lastReadbackFrame)
        {
            if (cameraState == null)
                return;

            foreach (KeyValuePair<int, VirtualTextureFeedbackBufferState> spacePair in cameraState.EnumerateSpaceStates())
                spacePair.Value.CollectCompletedReadbacks(s_CompletedReadbacks, ref lastReadbackFrame);
        }

        private static int CollectPendingUploadCount()
        {
            int pendingUploadCount = 0;
            foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
                pendingUploadCount += addressSpace.PendingRequestCount;

            return pendingUploadCount;
        }

        private static void AccumulateResidencyStats(
            in VTResidencyProcessResult result,
            ref int evictionCount,
            ref int pendingMipGapSum,
            ref int pendingMipGapMax,
            ref int pendingMipGapSampleCount,
            ref int prefetchRequestCount)
        {
            evictionCount += result.EvictionCount;
            pendingMipGapSum += result.PendingMipGapSum;
            pendingMipGapMax = Mathf.Max(pendingMipGapMax, result.PendingMipGapMax);
            pendingMipGapSampleCount += result.PendingMipGapSampleCount;
            prefetchRequestCount += result.PrefetchRequestCount;
        }

        private static void CollectAndSchedulePendingUploads(int frameIndex, CommandBuffer cmd)
        {
            s_UploadSpaceOrder.Clear();
            foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
                s_UploadSpaceOrder.Add(addressSpace);
            s_UploadSpaceOrder.Sort(CompareAddressSpacesById);

            s_PendingUploadCandidates.Clear();
            int spaceCount = s_UploadSpaceOrder.Count;
            int rotation = spaceCount > 0 ? (int)((uint)frameIndex % (uint)spaceCount) : 0;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingMarker.Auto())
            {
                for (int spaceIndex = 0; spaceIndex < spaceCount; spaceIndex++)
                {
                    int fairnessRank = (spaceIndex - rotation + spaceCount) % spaceCount;
                    s_UploadSpaceOrder[spaceIndex].CollectPendingUploadCandidates(
                        s_UploadScheduler,
                        fairnessRank,
                        s_PendingUploadCandidates);
                }
            }

            if (s_PendingUploadCandidates.Count > 1)
                s_PendingUploadCandidates.Sort(PendingUploadCandidateComparer.Instance);

            int skippedUploadCount = 0;
            for (int candidateIndex = 0; candidateIndex < s_PendingUploadCandidates.Count; candidateIndex++)
            {
                VTPendingUploadCandidate candidate = s_PendingUploadCandidates[candidateIndex];
                if (!candidate.AddressSpace.TrySchedulePendingUpload(
                        s_UploadScheduler,
                        cmd,
                        candidate.Request))
                {
                    skippedUploadCount += 1;
                }
            }

            s_UploadScheduler.AddSkippedUploadCount(skippedUploadCount);
            s_PendingUploadCandidates.Clear();
            s_UploadSpaceOrder.Clear();
        }

        private static int CompareAddressSpacesById(VTPageTableSpace left, VTPageTableSpace right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return 1;
            if (right == null)
                return -1;
            return left.SpaceId.CompareTo(right.SpaceId);
        }

        private static void RemoveQueuedFeedbackForSpace(List<VirtualTextureFeedbackBatch> batches, int spaceId)
        {
            for (int batchIndex = batches.Count - 1; batchIndex >= 0; batchIndex--)
            {
                VirtualTextureFeedbackBatch batch = batches[batchIndex];
                int requestCount = Mathf.Min(batch.RequestCount, batch.RequestCapacity);
                int keptCount = 0;
                ulong[] keptRequests = null;

                for (int requestIndex = 0; requestIndex < requestCount; requestIndex++)
                {
                    ulong key = batch.GetRequest(requestIndex);
                    VirtualTextureFeedbackProcessor.DecodeKey(
                        key,
                        out int requestSpaceId,
                        out _);
                    if (requestSpaceId == spaceId)
                        continue;

                    keptRequests ??= new ulong[requestCount];
                    keptRequests[keptCount] = key;
                    keptCount += 1;
                }

                if (keptCount == requestCount)
                    continue;

                if (keptCount == 0)
                {
                    batches.RemoveAt(batchIndex);
                    continue;
                }

                Array.Resize(ref keptRequests, keptCount);
                batches[batchIndex] = new VirtualTextureFeedbackBatch(
                    batch.ViewId,
                    batch.CameraType,
                    keptRequests,
                    keptCount,
                    batch.FrameIndex,
                    batch.FeedbackOverflowCount,
                    batch.FallbackSampleCount);
            }
        }

        private static void ResolvePrefetchBiasBySpace(VirtualTextureViewId viewId)
        {
            s_PrefetchBiasBySpace.Clear();
            foreach (KeyValuePair<int, VTPageTableSpace> pair in s_PageTableSpaces)
            {
                if (!s_FeedbackAggregator.TryGetRequestsForSpace(
                        pair.Key,
                        out NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests)
                    || requests.Length == 0
                    || !TryComputeFeedbackCentroid(
                        requests,
                        pair.Value.Descriptor,
                        out Vector2 centroid))
                {
                    continue;
                }

                FeedbackMotionKey key = new(pair.Key, viewId);
                Vector2Int bias = Vector2Int.zero;
                if (s_FeedbackMotionStates.TryGetValue(key, out FeedbackMotionState previousState))
                    bias = QuantizePrefetchBias(centroid - previousState.Centroid);

                s_FeedbackMotionStates[key] = new FeedbackMotionState(centroid);
                if (bias != Vector2Int.zero)
                    s_PrefetchBiasBySpace[pair.Key] = bias;
            }
        }

        private static bool TryComputeFeedbackCentroid(
            NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests,
            in VirtualTextureSpaceDesc desc,
            out Vector2 centroid)
        {
            Vector2 weightedSum = Vector2.zero;
            int totalWeight = 0;
            for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                VirtualTextureAggregatedFeedbackRequest request = requests[requestIndex];
                if (!VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord))
                    continue;

                int mipScale = 1 << Mathf.Clamp(request.PageCoord.Mip, 0, 20);
                int weight = Mathf.Max(1, request.HitCount);
                weightedSum += new Vector2(
                    (request.PageCoord.X + 0.5f) * mipScale,
                    (request.PageCoord.Y + 0.5f) * mipScale) * weight;
                totalWeight += weight;
            }

            centroid = totalWeight > 0 ? weightedSum / totalWeight : Vector2.zero;
            return totalWeight > 0;
        }

        private static Vector2Int QuantizePrefetchBias(Vector2 delta)
        {
            const float Threshold = 0.25f;
            int x = Mathf.Abs(delta.x) >= Threshold
                ? (delta.x > 0f ? 1 : -1)
                : 0;
            int y = Mathf.Abs(delta.y) >= Threshold
                ? (delta.y > 0f ? 1 : -1)
                : 0;
            return new Vector2Int(x, y);
        }

        private static bool IsBatchFromView(
            in VirtualTextureFeedbackBatch batch,
            VirtualTextureViewId viewId,
            CameraType cameraType)
        {
            if (!viewId.IsValid && !viewId.IsCameraTypeOnly)
                return false;

            return viewId.IsValid
                ? batch.ViewId.Equals(viewId)
                  || (!batch.ViewId.IsValid && batch.CameraType == cameraType)
                : batch.CameraType == viewId.CameraType;
        }

        private static VirtualTextureViewId ResolveCachePriorityViewId(
            VirtualTextureViewId renderViewId,
            CameraType renderCameraType)
        {
            if (!renderViewId.IsValid && !renderViewId.IsCameraTypeOnly)
                return renderViewId;

            if (!VTDebugStatsRegistry.TryGetFocusedViewForSystem(
                    out VirtualTextureViewId focusedViewId,
                    out CameraType focusedCameraType))
            {
                return renderViewId;
            }

            if (focusedViewId.IsValid)
                return focusedViewId;

            if (focusedCameraType == renderCameraType && renderViewId.IsValid)
                return renderViewId;

            return VirtualTextureViewId.FromCameraType(focusedCameraType);
        }

        private static string ResolveCameraName(VividCameraData cameraData, Camera camera)
        {
            if (cameraData != null && !string.IsNullOrEmpty(cameraData.cameraName))
                return cameraData.cameraName;

            return camera != null ? camera.name : null;
        }

        private static int ResolveFrameIndex(ContextContainer frameData)
        {
            VividCameraData cameraData = TryGetCameraData(frameData);
            if (cameraData != null && cameraData.frameIndex >= 0)
            {
                s_FallbackFrameIndex = Mathf.Max(s_FallbackFrameIndex, cameraData.frameIndex);
                return cameraData.frameIndex;
            }

            int currentFrameIndex = Time.frameCount;
            s_FallbackFrameIndex = s_FallbackFrameIndex < 0
                ? currentFrameIndex
                : Mathf.Max(s_FallbackFrameIndex + 1, currentFrameIndex);
            return s_FallbackFrameIndex;
        }

        private static VividCameraData TryGetCameraData(ContextContainer frameData)
        {
            if (frameData == null || !frameData.Contains<VividCameraData>())
                return null;

            return frameData.Get<VividCameraData>();
        }

        private static bool IsFeedbackSupported(Camera camera)
        {
            if (camera == null)
                return false;

            return camera.cameraType == CameraType.Game || camera.cameraType == CameraType.SceneView;
        }
    }
}
