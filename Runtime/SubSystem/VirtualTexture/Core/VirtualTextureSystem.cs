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
        private static readonly List<VTPageTableSpace> s_TransitionSchedulingSpaces = new();
        private static readonly List<VirtualTextureFeedbackBatch> s_InjectedReadbacks = new();
        private static readonly Dictionary<FeedbackMotionKey, FeedbackMotionState> s_FeedbackMotionStates = new();
        private static readonly Dictionary<int, Vector2Int> s_PrefetchBiasBySpace = new();
        private static readonly Dictionary<int, int> s_RemainingResidencyBudgetBySpace = new();
        private static readonly Dictionary<int, int> s_AllocatedResidencyRequestsBySpace = new();
        private static readonly Dictionary<int, int> s_AllocatedPrefetchRequestsBySpace = new();
        private static readonly Dictionary<int, int> s_ScheduledUploadsBySpace = new();
        private static readonly List<FeedbackMotionKey> s_FeedbackMotionKeysToRemove = new();
        private static readonly List<VTPageTableSpace> s_UploadSpaceOrder = new();
        private static readonly List<VTPendingUploadCandidate> s_PendingUploadCandidates = new();
        private static readonly List<ResidencyPriorityCandidate> s_ResidencyPriorityCandidates = new();
        private static readonly UploadCommitterResolver s_UploadCommitterResolver = new();
        private static readonly VTAdaptiveMipBiasController s_AdaptiveMipBiasController = new();
        private static readonly VTPageTableScatterUploader s_PageTableScatterUploader = new();
        private static VTUploadScheduler s_UploadScheduler = new();
        private static VTFeedbackNativeAggregator s_FeedbackAggregator;

        private const int DefaultMaxResidencyAllocationsPerFrame = 64;
        private const int MaxDemandEvictionsPerFrame = 4;
        private static int s_GlobalFrameIndex = int.MinValue;
        private static int s_MaxResidencyAllocationsPerFrame = DefaultMaxResidencyAllocationsPerFrame;
        private static int s_MaxPrefetchAllocationsPerFrame = int.MaxValue;
        private static int s_AllocatedResidencyRequestCount;
        private static int s_AllocatedPrefetchRequestCount;
        private static int s_DemandEvictionBudgetFrameIndex = int.MinValue;
        private static int s_RemainingDemandEvictionBudget;
        private static int s_LastResidencyCandidateCount;
        private static int s_LastPrefetchProcessRequestsCallCount;
#if UNITY_INCLUDE_TESTS
        private static int s_PhysicalPoolFreePageCollectionCount;
        private static int s_PhysicalPoolStatsCollectionCount;
#endif

        private static int s_NextSpaceId = 1;
        private static int s_NextAllocationId = 1;
        private static int s_FallbackFrameIndex = -1;
        private static bool s_RuntimeStateResetRequested;
        private static int s_FeedbackRequestReadbackErrorCount;
        private static int s_FeedbackCounterReadbackErrorCount;
        private static int s_FeedbackLastReadbackErrorFrameIndex = -1;
        private static int s_PendingAdaptiveFeedbackOverflowCount;
        private static int s_PendingAdaptiveFallbackSampleCount;
        private static int s_PendingAdaptiveFaultOverflowCount;
        private static int s_PendingAdaptiveResidentOverflowCount;
        private static int s_PendingAdaptiveNonResidentFallbackSampleCount;
        private static int s_PendingAdaptiveResidentFallbackSampleCount;
        private static int s_PendingAdaptiveWeightedResolvedSampleCount;
        private static int s_PendingAdaptiveAcceptedFaultRequestCount;
        private static int s_PendingAdaptiveAcceptedResidentRequestCount;
        private static int s_PendingAdaptiveFeedbackMeasurementFrameIndex = -1;
        private static bool s_HasPendingAdaptiveFeedbackMeasurement;

        private sealed class PendingUploadCandidateComparer : IComparer<VTPendingUploadCandidate>
        {
            internal static readonly PendingUploadCandidateComparer Instance = new();

            private PendingUploadCandidateComparer()
            {
            }

            public int Compare(VTPendingUploadCandidate left, VTPendingUploadCandidate right)
            {
                VTRequest leftRequest = left.Request;
                VTRequest rightRequest = right.Request;
                int priorityCompare = VTRequestPriorityUtility.Compare(
                    left.PriorityKey,
                    right.PriorityKey);
                if (priorityCompare != 0)
                    return priorityCompare;

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

        private readonly struct ResidencyPriorityCandidate
        {
            internal ResidencyPriorityCandidate(
                int requestIndex,
                in VirtualTextureAggregatedFeedbackRequest request,
                int producerPriority)
            {
                RequestIndex = requestIndex;
                Request = request;
                PriorityKey = VTRequestPriorityKey.FromFeedbackRequest(
                    request,
                    producerPriority);
            }

            internal int RequestIndex { get; }

            internal VirtualTextureAggregatedFeedbackRequest Request { get; }

            internal VTRequestPriorityKey PriorityKey { get; }
        }

        private sealed class ResidencyPriorityCandidateComparer : IComparer<ResidencyPriorityCandidate>
        {
            internal static readonly ResidencyPriorityCandidateComparer Instance = new();

            private ResidencyPriorityCandidateComparer()
            {
            }

            public int Compare(ResidencyPriorityCandidate left, ResidencyPriorityCandidate right)
            {
                int priorityCompare = VTRequestPriorityUtility.Compare(
                    left.PriorityKey,
                    right.PriorityKey);
                if (priorityCompare != 0)
                    return priorityCompare;

                VirtualTextureAggregatedFeedbackRequest leftRequest = left.Request;
                VirtualTextureAggregatedFeedbackRequest rightRequest = right.Request;
                int spaceCompare = leftRequest.SpaceId.CompareTo(rightRequest.SpaceId);
                if (spaceCompare != 0)
                    return spaceCompare;

                int yCompare = leftRequest.PageCoord.Y.CompareTo(rightRequest.PageCoord.Y);
                return yCompare != 0
                    ? yCompare
                    : leftRequest.PageCoord.X.CompareTo(rightRequest.PageCoord.X);
            }
        }

        private static bool IsResidencyCandidateEligible(
            VTResidencyRequestClassification classification)
        {
            // Pending seeds still drive priority promotion and neighbor prefetch.
            return classification == VTResidencyRequestClassification.Pending
                   || classification == VTResidencyRequestClassification.Missing;
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
            s_TransitionSchedulingSpaces.Clear();
            s_InjectedReadbacks.Clear();
            s_FeedbackAggregator?.Dispose();
            s_FeedbackAggregator = null;
            s_FeedbackMotionStates.Clear();
            s_PrefetchBiasBySpace.Clear();
            s_RemainingResidencyBudgetBySpace.Clear();
            s_AllocatedResidencyRequestsBySpace.Clear();
            s_AllocatedPrefetchRequestsBySpace.Clear();
            s_ScheduledUploadsBySpace.Clear();
            s_FeedbackMotionKeysToRemove.Clear();
            s_UploadSpaceOrder.Clear();
            s_PendingUploadCandidates.Clear();
            s_ResidencyPriorityCandidates.Clear();
            s_FeedbackCameraSystem.Dispose();
            s_NextSpaceId = 1;
            s_NextAllocationId = 1;
            s_FallbackFrameIndex = -1;
            s_GlobalFrameIndex = int.MinValue;
            s_MaxResidencyAllocationsPerFrame = DefaultMaxResidencyAllocationsPerFrame;
            s_MaxPrefetchAllocationsPerFrame = int.MaxValue;
            s_AllocatedResidencyRequestCount = 0;
            s_AllocatedPrefetchRequestCount = 0;
            s_LastResidencyCandidateCount = 0;
            s_LastPrefetchProcessRequestsCallCount = 0;
#if UNITY_INCLUDE_TESTS
            s_PhysicalPoolFreePageCollectionCount = 0;
            s_PhysicalPoolStatsCollectionCount = 0;
#endif
            s_RuntimeStateResetRequested = false;
            s_FeedbackRequestReadbackErrorCount = 0;
            s_FeedbackCounterReadbackErrorCount = 0;
            s_FeedbackLastReadbackErrorFrameIndex = -1;
            ResetPendingAdaptiveFeedbackMeasurement();
            s_DemandEvictionBudgetFrameIndex = int.MinValue;
            s_RemainingDemandEvictionBudget = 0;
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

        internal static void RequestRuntimeStateReset()
        {
            Initialize();
            s_RuntimeStateResetRequested = true;
        }

        private static void ResetRuntimeState(int frameIndex)
        {
            s_RuntimeStateResetRequested = false;
            s_PageTableScatterUploader.Reset();

            foreach (int spaceId in s_PageTableSpaces.Keys)
                s_UploadScheduler.CancelUploadsForSpace(spaceId);

            s_FeedbackCameraSystem.ResetStreamStates();

#if VT_DEBUG
            // A user-requested reset intentionally cancels every pending page. Keep it
            // out of the runtime anomaly timeline so it cannot masquerade as flicker.
            foreach (VTPhysicalPool physicalPool in s_PhysicalPools.Values)
                physicalPool.DebugResetTimeline();
#endif

            int flushedPageCount = 0;
            foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
                flushedPageCount += addressSpace.ClearRuntimeState();

            int recreatedAtlasCount = 0;
            foreach (VTPhysicalPool physicalPool in s_PhysicalPools.Values)
            {
                physicalPool.ResetRuntimeState();
                recreatedAtlasCount += physicalPool.Textures.Count;
            }

            VTStreamChunkManager.ResetSharedState();
            foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
                addressSpace.BootstrapRuntimeState(frameIndex);

            s_FeedbackAggregator.Clear();
            s_CompletedReadbacks.Clear();
            s_InjectedReadbacks.Clear();
            s_FeedbackMotionStates.Clear();
            s_PrefetchBiasBySpace.Clear();
            s_RemainingResidencyBudgetBySpace.Clear();
            s_AllocatedResidencyRequestsBySpace.Clear();
            s_AllocatedPrefetchRequestsBySpace.Clear();
            s_ScheduledUploadsBySpace.Clear();
            s_FeedbackMotionKeysToRemove.Clear();
            s_UploadSpaceOrder.Clear();
            s_PendingUploadCandidates.Clear();
            s_ResidencyPriorityCandidates.Clear();
            s_DemandEvictionBudgetFrameIndex = int.MinValue;
            s_RemainingDemandEvictionBudget = 0;
            s_GlobalFrameIndex = int.MinValue;
            s_AllocatedResidencyRequestCount = 0;
            s_AllocatedPrefetchRequestCount = 0;
            s_LastResidencyCandidateCount = 0;
            s_LastPrefetchProcessRequestsCallCount = 0;
#if UNITY_INCLUDE_TESTS
            s_PhysicalPoolFreePageCollectionCount = 0;
            s_PhysicalPoolStatsCollectionCount = 0;
#endif
            s_AdaptiveMipBiasController.Reset();
            s_FeedbackRequestReadbackErrorCount = 0;
            s_FeedbackCounterReadbackErrorCount = 0;
            s_FeedbackLastReadbackErrorFrameIndex = -1;
            ResetPendingAdaptiveFeedbackMeasurement();
            VirtualTextureStatsRegistry.Clear();

            Debug.Log(
                $"[VividRP] Reset virtual texture runtime state. "
                + $"spaces={s_PageTableSpaces.Count}, flushedPages={flushedPageCount}, "
                + $"recreatedAtlases={recreatedAtlasCount}.");
        }

        internal static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureMarker.Auto())
            {
                UpdateCore(frameData, cmd);
            }
        }

        private static void AccumulatePendingAdaptiveFeedbackMeasurement(
            int feedbackOverflowCount,
            int fallbackSampleCount,
            int faultOverflowCount,
            int residentOverflowCount,
            int nonResidentFallbackSampleCount,
            int residentFallbackSampleCount,
            int weightedResolvedSampleCount,
            int acceptedFaultRequestCount,
            int acceptedResidentRequestCount,
            int feedbackMeasurementFrameIndex)
        {
            s_PendingAdaptiveFeedbackOverflowCount = SaturatingAddFeedbackCount(
                s_PendingAdaptiveFeedbackOverflowCount,
                feedbackOverflowCount);
            s_PendingAdaptiveFallbackSampleCount = SaturatingAddFeedbackCount(
                s_PendingAdaptiveFallbackSampleCount,
                fallbackSampleCount);
            s_PendingAdaptiveFaultOverflowCount = SaturatingAddFeedbackCount(
                s_PendingAdaptiveFaultOverflowCount,
                faultOverflowCount);
            s_PendingAdaptiveResidentOverflowCount = SaturatingAddFeedbackCount(
                s_PendingAdaptiveResidentOverflowCount,
                residentOverflowCount);
            s_PendingAdaptiveNonResidentFallbackSampleCount = SaturatingAddFeedbackCount(
                s_PendingAdaptiveNonResidentFallbackSampleCount,
                nonResidentFallbackSampleCount);
            s_PendingAdaptiveResidentFallbackSampleCount = SaturatingAddFeedbackCount(
                s_PendingAdaptiveResidentFallbackSampleCount,
                residentFallbackSampleCount);
            s_PendingAdaptiveWeightedResolvedSampleCount = SaturatingAddFeedbackCount(
                s_PendingAdaptiveWeightedResolvedSampleCount,
                weightedResolvedSampleCount);
            s_PendingAdaptiveAcceptedFaultRequestCount = SaturatingAddFeedbackCount(
                s_PendingAdaptiveAcceptedFaultRequestCount,
                acceptedFaultRequestCount);
            s_PendingAdaptiveAcceptedResidentRequestCount = SaturatingAddFeedbackCount(
                s_PendingAdaptiveAcceptedResidentRequestCount,
                acceptedResidentRequestCount);
            s_PendingAdaptiveFeedbackMeasurementFrameIndex = Mathf.Max(
                s_PendingAdaptiveFeedbackMeasurementFrameIndex,
                feedbackMeasurementFrameIndex);
            s_HasPendingAdaptiveFeedbackMeasurement = true;
        }

        private static void ResetPendingAdaptiveFeedbackMeasurement()
        {
            s_PendingAdaptiveFeedbackOverflowCount = 0;
            s_PendingAdaptiveFallbackSampleCount = 0;
            s_PendingAdaptiveFaultOverflowCount = 0;
            s_PendingAdaptiveResidentOverflowCount = 0;
            s_PendingAdaptiveNonResidentFallbackSampleCount = 0;
            s_PendingAdaptiveResidentFallbackSampleCount = 0;
            s_PendingAdaptiveWeightedResolvedSampleCount = 0;
            s_PendingAdaptiveAcceptedFaultRequestCount = 0;
            s_PendingAdaptiveAcceptedResidentRequestCount = 0;
            s_PendingAdaptiveFeedbackMeasurementFrameIndex = -1;
            s_HasPendingAdaptiveFeedbackMeasurement = false;
        }

        private static int SaturatingAddFeedbackCount(int left, int right)
        {
            return (int)Math.Min(int.MaxValue, (long)Mathf.Max(0, left) + Mathf.Max(0, right));
        }

        protected override void OnUpdate(ContextContainer frameData, CommandBuffer cmd)
        {
            Update(frameData, cmd);
        }

        private static void AdvanceAndSchedulePageTransitions(int frameIndex)
        {
            int spaceCount;
            int frameOffset;
            int maxTransitionStartRounds;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureTransitionsCollectMarker.Auto())
            {
                s_TransitionSchedulingSpaces.Clear();
                foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
                    s_TransitionSchedulingSpaces.Add(addressSpace);

                if (s_TransitionSchedulingSpaces.Count == 0)
                    return;

                s_TransitionSchedulingSpaces.Sort(CompareAddressSpacesById);
                spaceCount = s_TransitionSchedulingSpaces.Count;
                frameOffset = frameIndex >= 0 ? frameIndex % spaceCount : 0;
                maxTransitionStartRounds = VTResidencyManager.MaxTransitionStartsPerFrame;
                for (int spaceIndex = 0; spaceIndex < spaceCount; spaceIndex++)
                {
                    maxTransitionStartRounds = Mathf.Max(
                        maxTransitionStartRounds,
                        s_TransitionSchedulingSpaces[spaceIndex]
                            .ResolveTransitionStartBudget(frameIndex));
                }
            }
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureTransitionsStartMarker.Auto())
            {
                for (int round = 0;
                     round < maxTransitionStartRounds;
                     round++)
                {
                    bool startedAny = false;
                    for (int relativeIndex = 0; relativeIndex < spaceCount; relativeIndex++)
                    {
                        int spaceIndex = (frameOffset + relativeIndex) % spaceCount;
                        startedAny |= s_TransitionSchedulingSpaces[spaceIndex]
                            .StartQueuedPageTransitions(frameIndex, maxStartsThisCall: 1);
                    }

                    if (!startedAny)
                        break;
                }
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureTransitionsAdvanceMarker.Auto())
            {
                for (int relativeIndex = 0; relativeIndex < spaceCount; relativeIndex++)
                {
                    int spaceIndex = (frameOffset + relativeIndex) % spaceCount;
                    VTPageTableSpace addressSpace = s_TransitionSchedulingSpaces[spaceIndex];
                    addressSpace.AdvancePageTransitionPhases(
                        frameIndex,
                        int.MaxValue);
                }
            }
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
            int frameIndex;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFrameSetupMarker.Auto())
            {
                if (!IsInitialized)
                    Initialize();

                frameIndex = ResolveFrameIndex(frameData);
                if (s_RuntimeStateResetRequested)
                    ResetRuntimeState(frameIndex);

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

            // Advance the diagnostics clock before readback collection. Transition pages
            // are touched after feedback aggregation and before residency can evict them.
#if VT_DEBUG
            foreach (VTPhysicalPool physicalPool in s_PhysicalPools.Values)
                physicalPool.DebugAdvanceTimelineFrame(frameIndex);
#endif

            int lastReadbackFrame = -1;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackReadbackMarker.Auto())
                CollectCompletedReadbacks(ref lastReadbackFrame);

            int faultCount = 0;
            int feedbackOverflowCount = 0;
            int fallbackSampleCount = 0;
            int faultOverflowCount = 0;
            int residentOverflowCount = 0;
            int nonResidentFallbackSampleCount = 0;
            int residentFallbackSampleCount = 0;
            int weightedResolvedSampleCount = 0;
            int measuredAcceptedFaultRequestCount = 0;
            int measuredAcceptedResidentRequestCount = 0;
            int requestReadbackErrorCount = 0;
            int counterReadbackErrorCount = 0;
            int feedbackMeasurementFrameIndex = -1;
            bool hasFreshFeedbackMeasurement = false;
            int activeViewFaultCount = 0;
            int activeViewFeedbackOverflowCount = 0;
            int activeViewFallbackSampleCount = 0;
            int activeViewLastReadbackFrame = -1;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackReadbackStatsMarker.Auto())
            {
                for (int batchIndex = 0; batchIndex < s_CompletedReadbacks.Count; batchIndex++)
                {
                    VirtualTextureFeedbackBatch batch = s_CompletedReadbacks[batchIndex];
                    faultCount = SaturatingAddFeedbackCount(
                        faultCount,
                        batch.AcceptedFaultRequestCount);
                    feedbackOverflowCount = SaturatingAddFeedbackCount(
                        feedbackOverflowCount,
                        batch.FeedbackOverflowCount);
                    fallbackSampleCount = SaturatingAddFeedbackCount(
                        fallbackSampleCount,
                        batch.FallbackSampleCount);
                    faultOverflowCount = SaturatingAddFeedbackCount(
                        faultOverflowCount,
                        batch.FaultOverflowCount);
                    residentOverflowCount = SaturatingAddFeedbackCount(
                        residentOverflowCount,
                        batch.ResidentOverflowCount);
                    nonResidentFallbackSampleCount = SaturatingAddFeedbackCount(
                        nonResidentFallbackSampleCount,
                        batch.NonResidentFallbackSampleCount);
                    residentFallbackSampleCount = SaturatingAddFeedbackCount(
                        residentFallbackSampleCount,
                        batch.ResidentFallbackSampleCount);
                    weightedResolvedSampleCount = SaturatingAddFeedbackCount(
                        weightedResolvedSampleCount,
                        batch.WeightedResolvedSampleCount);
                    requestReadbackErrorCount += batch.RequestsReadbackValid ? 0 : 1;
                    counterReadbackErrorCount += batch.CounterReadbackValid ? 0 : 1;
                    hasFreshFeedbackMeasurement |= batch.CounterReadbackValid;
                    if (batch.CounterReadbackValid)
                    {
                        // Only fault traffic drives adaptive overflow pressure. Resident
                        // accesses use the same physical buffer, so subtract the accepted
                        // resident entries before normalizing rejected fault attempts.
                        measuredAcceptedFaultRequestCount = SaturatingAddFeedbackCount(
                            measuredAcceptedFaultRequestCount,
                            batch.AcceptedFaultRequestCount);
                        measuredAcceptedResidentRequestCount = SaturatingAddFeedbackCount(
                            measuredAcceptedResidentRequestCount,
                            batch.AcceptedResidentRequestCount);
                        feedbackMeasurementFrameIndex = Mathf.Max(
                            feedbackMeasurementFrameIndex,
                            batch.FrameIndex);
                    }

                    if (IsBatchFromView(batch, activeViewId, activeCameraType))
                    {
                        activeViewFaultCount += Mathf.Max(
                            0,
                            batch.AcceptedFaultRequestCount);
                        activeViewFeedbackOverflowCount += batch.FeedbackOverflowCount;
                        activeViewFallbackSampleCount += batch.FallbackSampleCount;
                        activeViewLastReadbackFrame = Mathf.Max(activeViewLastReadbackFrame, batch.FrameIndex);
                    }
                }
            }

            s_FeedbackRequestReadbackErrorCount = (int)Math.Min(
                int.MaxValue,
                (long)s_FeedbackRequestReadbackErrorCount + requestReadbackErrorCount);
            s_FeedbackCounterReadbackErrorCount = (int)Math.Min(
                int.MaxValue,
                (long)s_FeedbackCounterReadbackErrorCount + counterReadbackErrorCount);
            if (requestReadbackErrorCount > 0 || counterReadbackErrorCount > 0)
                s_FeedbackLastReadbackErrorFrameIndex = frameIndex;

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

            // Starts are invisible (phase zero resolves the stable ancestor), so upload
            // work may continue in parallel. Each page publishes after its own transition
            // interval; unrelated feedback and uploads in the same space cannot delay it.
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureTransitionsMarker.Auto())
                AdvanceAndSchedulePageTransitions(frameIndex);

            int evictionCount = 0;
            int pendingMipGapSum = 0;
            int pendingMipGapMax = 0;
            int pendingMipGapSampleCount = 0;
            int prefetchRequestCount = 0;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsBeginFrameMarker.Auto())
            {
                // Reset global budgets once, while still polling progress for every camera.
                bool isFirstUpdateForFrame = s_GlobalFrameIndex != frameIndex;
                if (isFirstUpdateForFrame)
                    VTVirtualTextureStreamRequestGate.BeginFrame();

                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStreamBeginFrameMarker.Auto())
                {
                    if (isFirstUpdateForFrame)
                        VTStreamChunkManager.Shared.BeginFrame();
                    else
                        VTStreamChunkManager.Shared.PollProgress();
                }

                if (isFirstUpdateForFrame)
                {
                    s_UploadScheduler.BeginFrame();
                    s_AllocatedResidencyRequestCount = 0;
                    s_AllocatedPrefetchRequestCount = 0;
                    s_AllocatedResidencyRequestsBySpace.Clear();
                    s_AllocatedPrefetchRequestsBySpace.Clear();
                    s_ScheduledUploadsBySpace.Clear();
                    s_GlobalFrameIndex = frameIndex;
                }
                else
                {
                    s_UploadScheduler.DiscardQueuedUploads();
                }
            }
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCommitCompletedMarker.Auto())
                s_UploadScheduler.CommitCompletedUploads(s_UploadCommitterResolver, frameIndex);

            int globalResidencyRequestBudget;
            int remainingResidencyRequestBudget;
            NativeArray<VirtualTextureAggregatedFeedbackRequest> aggregatedRequests;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyBudgetMarker.Auto())
            {
                int freePhysicalPageCountBeforeResidency;
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyBudgetPoolStatsMarker.Auto())
                    freePhysicalPageCountBeforeResidency = CollectFreePhysicalPageCount();
                if (s_DemandEvictionBudgetFrameIndex != frameIndex)
                {
                    s_DemandEvictionBudgetFrameIndex = frameIndex;
                    s_RemainingDemandEvictionBudget = MaxDemandEvictionsPerFrame;
                }

                int guardedResidencyRequestBudget = freePhysicalPageCountBeforeResidency > int.MaxValue - s_RemainingDemandEvictionBudget
                    ? int.MaxValue
                    : freePhysicalPageCountBeforeResidency + s_RemainingDemandEvictionBudget;
                int remainingFrameResidencyRequestBudget = Mathf.Max(
                    0,
                    s_MaxResidencyAllocationsPerFrame - s_AllocatedResidencyRequestCount);
                globalResidencyRequestBudget = Mathf.Min(
                    remainingFrameResidencyRequestBudget,
                    guardedResidencyRequestBudget);
                remainingResidencyRequestBudget = globalResidencyRequestBudget;
                aggregatedRequests = s_FeedbackAggregator.AggregatedRequests;

                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyBudgetAssignMarker.Auto())
                {
                    s_ResidencyPriorityCandidates.Clear();
                    for (int requestIndex = 0; requestIndex < aggregatedRequests.Length; requestIndex++)
                    {
                        VirtualTextureAggregatedFeedbackRequest request = aggregatedRequests[requestIndex];
                        if (!s_PageTableSpaces.TryGetValue(request.SpaceId, out VTPageTableSpace addressSpace)
                            || !IsResidencyCandidateEligible(
                                addressSpace.GetExactResidencyClassification(request.PageCoord)))
                        {
                            continue;
                        }

                        s_ResidencyPriorityCandidates.Add(new ResidencyPriorityCandidate(
                            requestIndex,
                            request,
                            addressSpace.ProducerPriority));
                    }
                    if (s_ResidencyPriorityCandidates.Count > 1)
                        s_ResidencyPriorityCandidates.Sort(ResidencyPriorityCandidateComparer.Instance);
                    s_LastResidencyCandidateCount = s_ResidencyPriorityCandidates.Count;

                    s_RemainingResidencyBudgetBySpace.Clear();
                    foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
                        s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId] = 0;

                    for (int candidateIndex = 0;
                         candidateIndex < s_ResidencyPriorityCandidates.Count
                         && remainingResidencyRequestBudget > 0;
                         candidateIndex++)
                    {
                        VirtualTextureAggregatedFeedbackRequest request =
                            s_ResidencyPriorityCandidates[candidateIndex].Request;
                        VTPageTableSpace addressSpace = s_PageTableSpaces[request.SpaceId];

                        int assignedSpaceBudget = s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId];
                        int allocatedSpaceRequestCount = GetAllocatedResidencyRequestCount(addressSpace.SpaceId);
                        int remainingSpaceRequestBudget = Mathf.Max(
                            0,
                            addressSpace.Descriptor.MaxResidencyAllocationsPerFrame
                            - allocatedSpaceRequestCount);
                        // Pending stays eligible for Prefetch but does not reserve another
                        // demand allocation from the physical-page budget.
                        if (assignedSpaceBudget >= remainingSpaceRequestBudget
                            || !addressSpace.RequiresNewPhysicalPage(request.PageCoord))
                        {
                            continue;
                        }

                        s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId] = assignedSpaceBudget + 1;
                        remainingResidencyRequestBudget -= 1;
                    }
                }
            }

            int allocatedResidencyRequestCount = 0;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyDemandPassMarker.Auto())
            {
                foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
                {
                    int assignedSpaceBudget = s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId];
                    int remainingSpaceRequestBudget = Mathf.Max(
                        0,
                        addressSpace.Descriptor.MaxResidencyAllocationsPerFrame
                        - GetAllocatedResidencyRequestCount(addressSpace.SpaceId));
                    if (!s_FeedbackAggregator.TryGetRequestsForSpace(
                            addressSpace.SpaceId,
                            out NativeSlice<VirtualTextureAggregatedFeedbackRequest> spaceRequests))
                    {
                        s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId] =
                            remainingSpaceRequestBudget;
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
                            RecordAllocatedResidencyRequests(
                                addressSpace.SpaceId,
                                residencyResult.AllocatedRequestCount);
                            s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId] = Mathf.Max(
                                0,
                                remainingSpaceRequestBudget - residencyResult.AllocatedRequestCount);
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
            }

            remainingResidencyRequestBudget = Mathf.Max(
                0,
                globalResidencyRequestBudget - allocatedResidencyRequestCount);

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyPrefetchPassMarker.Auto())
            {
                s_LastPrefetchProcessRequestsCallCount = 0;
                int remainingGlobalPrefetchRequestBudget = Mathf.Max(
                    0,
                    s_MaxPrefetchAllocationsPerFrame - s_AllocatedPrefetchRequestCount);
                for (int candidateIndex = 0;
                     candidateIndex < s_ResidencyPriorityCandidates.Count
                     && remainingResidencyRequestBudget > 0
                     && remainingGlobalPrefetchRequestBudget > 0;
                     candidateIndex++)
                {
                    ResidencyPriorityCandidate candidate = s_ResidencyPriorityCandidates[candidateIndex];
                    VirtualTextureAggregatedFeedbackRequest request = candidate.Request;
                    // Demand may have attached this exact page from another space after
                    // the candidate snapshot was built.
                    if (!s_PageTableSpaces.TryGetValue(request.SpaceId, out VTPageTableSpace addressSpace)
                        || addressSpace.Descriptor.NeighborPrefetchCount <= 0
                        || !IsResidencyCandidateEligible(
                            addressSpace.GetExactResidencyClassification(request.PageCoord)))
                    {
                        continue;
                    }

                    int spaceResidencyRequestBudget = s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId];
                    int remainingSpacePrefetchRequestBudget = Mathf.Max(
                        0,
                        addressSpace.Descriptor.MaxPrefetchAllocationsPerFrame
                        - GetAllocatedPrefetchRequestCount(addressSpace.SpaceId));
                    if (spaceResidencyRequestBudget <= 0
                        || remainingSpacePrefetchRequestBudget <= 0
                        || addressSpace.FreePageCount <= 0)
                    {
                        continue;
                    }

                    s_PrefetchBiasBySpace.TryGetValue(addressSpace.SpaceId, out Vector2Int prefetchBias);
                    var requestSlice = new NativeSlice<VirtualTextureAggregatedFeedbackRequest>(
                        aggregatedRequests,
                        candidate.RequestIndex,
                        1);
                    s_LastPrefetchProcessRequestsCallCount += 1;
                    VTResidencyProcessResult residencyResult = addressSpace.ProcessRequests(
                        requestSlice,
                        cachePriorityViewId,
                        prefetchBias,
                        frameIndex,
                        Mathf.Min(
                            Mathf.Min(remainingResidencyRequestBudget, spaceResidencyRequestBudget),
                            Mathf.Min(
                                remainingGlobalPrefetchRequestBudget,
                                remainingSpacePrefetchRequestBudget)),
                        allowNeighborPrefetch: true,
                        rebuildPageTable: false);
                    remainingResidencyRequestBudget = Mathf.Max(
                        0,
                        remainingResidencyRequestBudget - residencyResult.AllocatedRequestCount);
                    RecordAllocatedResidencyRequests(
                        addressSpace.SpaceId,
                        residencyResult.AllocatedRequestCount);
                    RecordAllocatedPrefetchRequests(
                        addressSpace.SpaceId,
                        residencyResult.PrefetchRequestCount);
                    remainingGlobalPrefetchRequestBudget = Mathf.Max(
                        0,
                        remainingGlobalPrefetchRequestBudget - residencyResult.PrefetchRequestCount);
                    s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId] = Mathf.Max(
                        0,
                        spaceResidencyRequestBudget - residencyResult.AllocatedRequestCount);
                    evictionCount += residencyResult.EvictionCount;
                    prefetchRequestCount += residencyResult.PrefetchRequestCount;
                }
            }
            s_ResidencyPriorityCandidates.Clear();

            s_RemainingDemandEvictionBudget = Mathf.Max(
                0,
                s_RemainingDemandEvictionBudget - evictionCount);

            CollectAndSchedulePendingUploads(frameIndex, cmd);
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStreamSubmitReadsMarker.Auto())
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
            VTPhysicalPoolStats physicalPoolStats;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStatsPhysicalPoolsMarker.Auto())
                physicalPoolStats = CollectPhysicalPoolStats();
            float adaptiveMipBias;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStatsAdaptiveMipBiasMarker.Auto())
            {
                VividRenderingDebugSettingsData debugSettings =
                    VividRenderingDebugDisplaySettings.Data;
                if (s_AdaptiveMipBiasController.HasUpdatedFrame(frameIndex))
                {
                    // Feedback can complete while a later camera is rendering the same frame.
                    // Carry it forward because the global controller intentionally updates once.
                    if (hasFreshFeedbackMeasurement)
                    {
                        AccumulatePendingAdaptiveFeedbackMeasurement(
                            feedbackOverflowCount,
                            fallbackSampleCount,
                            faultOverflowCount,
                            residentOverflowCount,
                            nonResidentFallbackSampleCount,
                            residentFallbackSampleCount,
                            weightedResolvedSampleCount,
                            measuredAcceptedFaultRequestCount,
                            measuredAcceptedResidentRequestCount,
                            feedbackMeasurementFrameIndex);
                    }

                    adaptiveMipBias = s_AdaptiveMipBiasController.CurrentMipBias;
                }
                else
                {
                    int measuredFeedbackOverflowCount = SaturatingAddFeedbackCount(
                        feedbackOverflowCount,
                        s_PendingAdaptiveFeedbackOverflowCount);
                    int measuredFallbackSampleCount = SaturatingAddFeedbackCount(
                        fallbackSampleCount,
                        s_PendingAdaptiveFallbackSampleCount);
                    int measuredFaultOverflowCount = SaturatingAddFeedbackCount(
                        faultOverflowCount,
                        s_PendingAdaptiveFaultOverflowCount);
                    int measuredResidentOverflowCount = SaturatingAddFeedbackCount(
                        residentOverflowCount,
                        s_PendingAdaptiveResidentOverflowCount);
                    int measuredNonResidentFallbackSampleCount = SaturatingAddFeedbackCount(
                        nonResidentFallbackSampleCount,
                        s_PendingAdaptiveNonResidentFallbackSampleCount);
                    int measuredResidentFallbackSampleCount = SaturatingAddFeedbackCount(
                        residentFallbackSampleCount,
                        s_PendingAdaptiveResidentFallbackSampleCount);
                    int measuredWeightedResolvedSampleCount = SaturatingAddFeedbackCount(
                        weightedResolvedSampleCount,
                        s_PendingAdaptiveWeightedResolvedSampleCount);
                    int adaptiveAcceptedFaultRequestCount = SaturatingAddFeedbackCount(
                        measuredAcceptedFaultRequestCount,
                        s_PendingAdaptiveAcceptedFaultRequestCount);
                    int adaptiveAcceptedResidentRequestCount = SaturatingAddFeedbackCount(
                        measuredAcceptedResidentRequestCount,
                        s_PendingAdaptiveAcceptedResidentRequestCount);
                    int measuredFeedbackFrameIndex = Mathf.Max(
                        feedbackMeasurementFrameIndex,
                        s_PendingAdaptiveFeedbackMeasurementFrameIndex);
                    bool hasMeasuredFeedback = hasFreshFeedbackMeasurement
                                               || s_HasPendingAdaptiveFeedbackMeasurement;
                    ResetPendingAdaptiveFeedbackMeasurement();

                    int adaptiveFeedbackOverflowCount =
                        debugSettings.virtualTextureFeedbackOverflowCountOverride >= 0
                            ? debugSettings.virtualTextureFeedbackOverflowCountOverride
                            : measuredFaultOverflowCount;
                    int adaptiveFallbackSampleCount =
                        debugSettings.virtualTextureFallbackSampleCountOverride >= 0
                            ? debugSettings.virtualTextureFallbackSampleCountOverride
                            : measuredNonResidentFallbackSampleCount;
                    adaptiveMipBias = s_AdaptiveMipBiasController.Update(
                        frameIndex,
                        new VTAdaptiveMipBiasInputs(
                            globalResidencyRequestBudget,
                            pendingUploadCount,
                            blockedUploadCount,
                            streamSaturatedRequestCount,
                            adaptiveFeedbackOverflowCount,
                            adaptiveFallbackSampleCount,
                            physicalPoolStats.FreePageCount,
                            evictionCount,
                            hasFreshFeedbackMeasurement: hasMeasuredFeedback,
                            measuredFeedbackOverflowCount: measuredFeedbackOverflowCount,
                            measuredFallbackSampleCount: measuredFallbackSampleCount,
                            measuredFaultOverflowCount: measuredFaultOverflowCount,
                            measuredResidentOverflowCount: measuredResidentOverflowCount,
                            measuredNonResidentFallbackSampleCount: measuredNonResidentFallbackSampleCount,
                            measuredResidentFallbackSampleCount: measuredResidentFallbackSampleCount,
                            feedbackMeasurementFrameIndex: measuredFeedbackFrameIndex,
                            weightedAccessSampleCount: measuredWeightedResolvedSampleCount,
                            measuredWeightedAccessSampleCount: measuredWeightedResolvedSampleCount,
                            acceptedFaultRequestCount: adaptiveAcceptedFaultRequestCount,
                            acceptedResidentRequestCount: adaptiveAcceptedResidentRequestCount,
                            feedbackOverflowOverrideActive:
                                debugSettings.virtualTextureFeedbackOverflowCountOverride >= 0,
                            fallbackSampleOverrideActive:
                                debugSettings.virtualTextureFallbackSampleCountOverride >= 0));
                }
                float adaptiveMipBiasOverride = debugSettings.virtualTextureAdaptiveMipBiasOverride;
                if (adaptiveMipBiasOverride >= 0f)
                    adaptiveMipBias = adaptiveMipBiasOverride;
            }
            if (virtualTextureFrameData != null)
                virtualTextureFrameData.AdaptiveMipBias = adaptiveMipBias;
            activeViewSignature = activeViewSignature.WithAdaptiveMipBias(adaptiveMipBias);
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
            int feedbackPageCapacity = 0;
            long pageTableByteCount = 0;
            string statusMessage = s_PageTableSpaces.Count == 0 ? "[VividRP] VT has no registered spaces." : string.Empty;

            foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
            {
                feedbackCapacity = checked(
                    feedbackCapacity + addressSpace.StackDesc.FeedbackCapacity);
                feedbackPageCapacity = checked(
                    feedbackPageCapacity + addressSpace.StackDesc.CachePageCount);
            }

            ComputeBuffer sharedFeedbackRequests = null;
            ComputeBuffer sharedFeedbackCounter = null;
            ComputeBuffer sharedFeedbackHash = null;
            int sharedFeedbackHashCapacity = 0;
            VirtualTextureFeedbackBufferState feedbackBufferState = null;
            if (supportsFeedback && cameraFeedbackState != null && feedbackCapacity > 0)
            {
                bool forceImmediateReadback = false;
                foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
                {
                    if (addressSpace.PendingRequestCount > 0
                        || s_UploadScheduler.HasInFlightUploadForSpace(addressSpace.SpaceId))
                    {
                        forceImmediateReadback = true;
                        break;
                    }
                }

                feedbackBufferState = cameraFeedbackState.GetOrCreateStreamState();
                if (!feedbackBufferState.TryPrepareForFrame(
                        cmd,
                        ResolveCameraName(cameraData, camera),
                        camera,
                        activeViewId,
                        activeViewSignature,
                        feedbackCapacity,
                        feedbackPageCapacity,
                        frameIndex,
                        forceImmediateReadback,
                        out sharedFeedbackRequests,
                        out sharedFeedbackCounter,
                        out sharedFeedbackHash,
                        out sharedFeedbackHashCapacity,
                        out string feedbackStatus)
                    && string.IsNullOrEmpty(statusMessage)
                    && !string.IsNullOrEmpty(feedbackStatus))
                {
                    statusMessage = feedbackStatus;
                }
            }

            foreach (KeyValuePair<int, VTPageTableSpace> pair in s_PageTableSpaces)
            {
                VTPageTableSpace addressSpace = pair.Value;
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsMarker.Auto())
                {
                    pageTableByteCount = checked(
                        pageTableByteCount
                        + (long)addressSpace.TotalPageCount * sizeof(uint));
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
                        sharedFeedbackRequests,
                        sharedFeedbackCounter,
                        sharedFeedbackHash,
                        feedbackCapacity,
                        sharedFeedbackHashCapacity,
                        feedbackBufferState));
                }
            }

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
                    adaptiveMipBias,
                    physicalPoolStats.AllocatedByteCount,
                    physicalPoolStats.ResidentByteCount,
                    pageTableByteCount,
                    VTStreamChunkManager.SharedReadyByteCount,
                    VTStreamChunkManager.SharedDecodedCacheBudget);
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
                        adaptiveMipBias,
                        physicalPoolStats.AllocatedByteCount,
                        physicalPoolStats.ResidentByteCount,
                        pageTableByteCount,
                        VTStreamChunkManager.SharedReadyByteCount,
                        VTStreamChunkManager.SharedDecodedCacheBudget);
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
            var producerHandles = new HashSet<VTProducerHandle>();
            foreach (KeyValuePair<int, VTPageTableSpace> pair in s_PageTableSpaces)
            {
                VTProducerHandle producerHandle = pair.Value.ProducerHandle;
                if (!s_ProducerRegistry.IsSameProducer(producerHandle, resolvedProducer))
                    continue;

                s_UploadScheduler.CancelUploadsForSpace(pair.Key);
                producerHandles.Add(producerHandle);
            }

            int flushedCount = 0;
            foreach (VTPhysicalPool pool in s_PhysicalPools.Values)
            {
                foreach (VTProducerHandle producerHandle in producerHandles)
                    flushedCount += pool.FlushProducer(producerHandle, producerName: null);
            }

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
            // The explicit residency queue is the correctness-critical bootstrap/mip-tail path
            // (normally locked), so it intentionally bypasses feedback-driven allocation budgets.
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

        internal static void InjectCompletedResidentAccessReadbackForTesting(
            CameraType cameraType,
            params ulong[] requestKeys)
        {
            if (requestKeys == null || requestKeys.Length == 0)
                return;

            s_InjectedReadbacks.Add(new VirtualTextureFeedbackBatch(
                VirtualTextureViewId.FromCameraType(cameraType),
                cameraType,
                requestKeys,
                requestKeys.Length,
                Time.frameCount,
                residentAccessCount: requestKeys.Length));
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
                fallbackSampleCount,
                requestKeys);
        }

        private static void InjectCompletedReadbackStatsForTesting(
            VirtualTextureViewId viewId,
            CameraType cameraType,
            int feedbackOverflowCount,
            int fallbackSampleCount,
            int weightedResolvedSampleCount,
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
                fallbackSampleCount,
                weightedResolvedSampleCount: weightedResolvedSampleCount));
        }

        internal static bool TryGetPageTableEntryForTesting(
            int spaceId,
            in VirtualTexturePageCoord coord,
            out VirtualTexturePageTableEntry entry)
        {
            return TryGetPageTableEntry(spaceId, coord, out entry);
        }

        internal static void AdvancePageTransitionsForTesting(int frameIndex)
        {
            AdvanceAndSchedulePageTransitions(frameIndex);
            foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
                addressSpace.RebuildPageTableIfDirty(frameIndex);
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

        internal static int GetLastResidencyCandidateCountForTesting()
        {
            return s_LastResidencyCandidateCount;
        }

        internal static int GetLastPrefetchProcessRequestsCallCountForTesting()
        {
            return s_LastPrefetchProcessRequestsCallCount;
        }

        internal static int GetPhysicalPoolCountForTesting()
        {
            return s_PhysicalPools.Count;
        }

        internal static VTPhysicalPoolStats GetPhysicalPoolStatsForTesting()
        {
            return CollectPhysicalPoolStats();
        }

#if UNITY_INCLUDE_TESTS
        internal static int GetPhysicalPoolFreePageCollectionCountForTesting()
        {
            return s_PhysicalPoolFreePageCollectionCount;
        }

        internal static int GetPhysicalPoolStatsCollectionCountForTesting()
        {
            return s_PhysicalPoolStatsCollectionCount;
        }
#endif

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

        internal static void ConfigureBudgets(
            int maxResidencyAllocationsPerFrame,
            int maxPrefetchAllocationsPerFrame,
            int maxPageUploadsPerFrame,
            int maxUploadBytesPerFrame)
        {
            s_MaxResidencyAllocationsPerFrame = NormalizeBudget(maxResidencyAllocationsPerFrame);
            s_MaxPrefetchAllocationsPerFrame = NormalizeBudget(maxPrefetchAllocationsPerFrame);
            s_UploadScheduler.MaxUploadsPerFrame = maxPageUploadsPerFrame;
            s_UploadScheduler.MaxUploadBytesPerFrame = maxUploadBytesPerFrame;
        }

        internal static void SetResidencyAllocationBudgetForTesting(
            int maxResidencyAllocationsPerFrame)
        {
            s_MaxResidencyAllocationsPerFrame = NormalizeBudget(maxResidencyAllocationsPerFrame);
        }

        internal static void SetPrefetchAllocationBudgetForTesting(
            int maxPrefetchAllocationsPerFrame)
        {
            s_MaxPrefetchAllocationsPerFrame = NormalizeBudget(maxPrefetchAllocationsPerFrame);
        }

        internal static float GetAdaptiveMipBiasForTesting()
        {
            return s_AdaptiveMipBiasController.CurrentMipBias;
        }

        internal static int AdaptiveFeedbackOverflowInputCount =>
            s_AdaptiveMipBiasController.LastFeedbackOverflowCount;

        internal static int AdaptiveFallbackSampleInputCount =>
            s_AdaptiveMipBiasController.LastFallbackSampleCount;

        internal static int AdaptiveMeasuredFeedbackOverflowCount =>
            s_AdaptiveMipBiasController.LastMeasuredFeedbackOverflowCount;

        internal static int AdaptiveMeasuredFallbackSampleCount =>
            s_AdaptiveMipBiasController.LastMeasuredFallbackSampleCount;

        internal static int AdaptiveMeasuredFaultOverflowCount =>
            s_AdaptiveMipBiasController.LastMeasuredFaultOverflowCount;

        internal static int AdaptiveMeasuredResidentOverflowCount =>
            s_AdaptiveMipBiasController.LastMeasuredResidentOverflowCount;

        internal static int AdaptiveMeasuredNonResidentFallbackSampleCount =>
            s_AdaptiveMipBiasController.LastMeasuredNonResidentFallbackSampleCount;

        internal static int AdaptiveMeasuredResidentFallbackSampleCount =>
            s_AdaptiveMipBiasController.LastMeasuredResidentFallbackSampleCount;

        internal static int AdaptiveMeasuredWeightedResolvedSampleCount =>
            s_AdaptiveMipBiasController.LastMeasuredWeightedAccessSampleCount;

        internal static int AdaptiveMeasuredAcceptedFaultRequestCount =>
            s_AdaptiveMipBiasController.LastMeasuredAcceptedFaultRequestCount;

        internal static int AdaptiveMeasuredAcceptedResidentRequestCount =>
            s_AdaptiveMipBiasController.LastMeasuredAcceptedResidentRequestCount;

        internal static float AdaptiveFeedbackOverflowPressure =>
            s_AdaptiveMipBiasController.LastFeedbackOverflowPressure;

        internal static float AdaptiveFallbackPressure =>
            s_AdaptiveMipBiasController.LastFallbackPressure;

        internal static float AdaptiveFallbackCoverage =>
            s_AdaptiveMipBiasController.LastFallbackCoverage;

        internal static float AdaptiveTotalPressure =>
            s_AdaptiveMipBiasController.LastPressure;

        internal static float AdaptiveTargetMipBias =>
            s_AdaptiveMipBiasController.LastTargetMipBias;

        internal static int AdaptiveLastFreshFeedbackFrameIndex =>
            s_AdaptiveMipBiasController.LastFreshFeedbackFrameIndex;

        internal static int AdaptiveLastFreshFeedbackOverflowCount =>
            s_AdaptiveMipBiasController.LastFreshMeasuredFeedbackOverflowCount;

        internal static int AdaptiveLastFreshFallbackSampleCount =>
            s_AdaptiveMipBiasController.LastFreshMeasuredFallbackSampleCount;

        internal static int AdaptiveLastFreshFaultOverflowCount =>
            s_AdaptiveMipBiasController.LastFreshMeasuredFaultOverflowCount;

        internal static int AdaptiveLastFreshResidentOverflowCount =>
            s_AdaptiveMipBiasController.LastFreshMeasuredResidentOverflowCount;

        internal static int AdaptiveLastFreshNonResidentFallbackSampleCount =>
            s_AdaptiveMipBiasController.LastFreshMeasuredNonResidentFallbackSampleCount;

        internal static int AdaptiveLastFreshResidentFallbackSampleCount =>
            s_AdaptiveMipBiasController.LastFreshMeasuredResidentFallbackSampleCount;

        internal static int AdaptiveLastFreshWeightedResolvedSampleCount =>
            s_AdaptiveMipBiasController.LastFreshMeasuredWeightedAccessSampleCount;

        internal static float AdaptiveLastFreshFeedbackOverflowPressure =>
            s_AdaptiveMipBiasController.LastFreshFeedbackOverflowPressure;

        internal static float AdaptiveLastFreshFallbackPressure =>
            s_AdaptiveMipBiasController.LastFreshFallbackPressure;

        internal static int FeedbackRequestReadbackErrorCount =>
            s_FeedbackRequestReadbackErrorCount;

        internal static int FeedbackCounterReadbackErrorCount =>
            s_FeedbackCounterReadbackErrorCount;

        internal static int FeedbackLastReadbackErrorFrameIndex =>
            s_FeedbackLastReadbackErrorFrameIndex;

        internal static bool AdaptiveFeedbackMeasurementWasFresh =>
            s_AdaptiveMipBiasController.LastUpdateHadFreshFeedbackMeasurement;

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
            if ((uint)s_NextSpaceId > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    "[VividRP] VT feedback keys support at most 65535 address spaces per runtime epoch.");
            }

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

        private static int CollectFreePhysicalPageCount()
        {
#if UNITY_INCLUDE_TESTS
            s_PhysicalPoolFreePageCollectionCount += 1;
#endif
            int freePageCount = 0;
            foreach (VTPhysicalPool pool in s_PhysicalPools.Values)
            {
                int poolFreePageCount = pool.FreePageCount;
                if (freePageCount > int.MaxValue - poolFreePageCount)
                    return int.MaxValue;

                freePageCount += poolFreePageCount;
            }

            return freePageCount;
        }

        private static VTPhysicalPoolStats CollectPhysicalPoolStats()
        {
#if UNITY_INCLUDE_TESTS
            s_PhysicalPoolStatsCollectionCount += 1;
#endif
            int residentPageCount = 0;
            int freePageCount = 0;
            int lockedPageCount = 0;
            int evictedPageCount = 0;
            long allocatedByteCount = 0;
            long residentByteCount = 0;
            foreach (VTPhysicalPool pool in s_PhysicalPools.Values)
            {
                residentPageCount += pool.ResidentPageCount;
                freePageCount += pool.FreePageCount;
                lockedPageCount += pool.LockedPageCount;
                evictedPageCount += pool.EvictedPageCount;
                allocatedByteCount = checked(allocatedByteCount + pool.AllocatedByteCount);
                residentByteCount = checked(residentByteCount + pool.ResidentByteCount);
            }

            return new VTPhysicalPoolStats(
                s_PhysicalPools.Count,
                residentPageCount,
                freePageCount,
                lockedPageCount,
                evictedPageCount,
                allocatedByteCount,
                residentByteCount);
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
            // A camera owns one compact stream shared by every space. Stream-level counters
            // cannot be split when the topology changes, so conservatively discard the batch.
            s_FeedbackCameraSystem.ResetStreamStates();
            s_CompletedReadbacks.Clear();
            s_InjectedReadbacks.Clear();
            ResetPendingAdaptiveFeedbackMeasurement();
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

            if (cameraState.TryGetStreamState(out VirtualTextureFeedbackBufferState streamState))
                streamState.CollectCompletedReadbacks(s_CompletedReadbacks, ref lastReadbackFrame);
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

        private static int GetAllocatedResidencyRequestCount(int spaceId)
        {
            return s_AllocatedResidencyRequestsBySpace.TryGetValue(spaceId, out int requestCount)
                ? requestCount
                : 0;
        }

        private static void RecordAllocatedResidencyRequests(int spaceId, int requestCount)
        {
            if (requestCount <= 0)
                return;

            s_AllocatedResidencyRequestCount += requestCount;
            s_AllocatedResidencyRequestsBySpace[spaceId] =
                GetAllocatedResidencyRequestCount(spaceId) + requestCount;
        }

        private static int GetAllocatedPrefetchRequestCount(int spaceId)
        {
            return s_AllocatedPrefetchRequestsBySpace.TryGetValue(spaceId, out int requestCount)
                ? requestCount
                : 0;
        }

        private static void RecordAllocatedPrefetchRequests(int spaceId, int requestCount)
        {
            if (requestCount <= 0)
                return;

            s_AllocatedPrefetchRequestCount += requestCount;
            s_AllocatedPrefetchRequestsBySpace[spaceId] =
                GetAllocatedPrefetchRequestCount(spaceId) + requestCount;
        }

        private static int NormalizeBudget(int budget)
        {
            return budget <= 0 ? int.MaxValue : budget;
        }

        private static int GetScheduledUploadCount(int spaceId)
        {
            return s_ScheduledUploadsBySpace.TryGetValue(spaceId, out int uploadCount)
                ? uploadCount
                : 0;
        }

        private static void RecordScheduledUpload(int spaceId)
        {
            s_ScheduledUploadsBySpace[spaceId] = GetScheduledUploadCount(spaceId) + 1;
        }

        private static void CollectAndSchedulePendingUploads(int frameIndex, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingMarker.Auto())
                CollectAndSchedulePendingUploadsCore(frameIndex, cmd);
        }

        private static void CollectAndSchedulePendingUploadsCore(int frameIndex, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingBuildSpaceOrderMarker.Auto())
            {
                s_UploadSpaceOrder.Clear();
                foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
                    s_UploadSpaceOrder.Add(addressSpace);
                s_UploadSpaceOrder.Sort(CompareAddressSpacesById);
            }

            s_PendingUploadCandidates.Clear();
            int spaceCount = s_UploadSpaceOrder.Count;
            int rotation = spaceCount > 0 ? (int)((uint)frameIndex % (uint)spaceCount) : 0;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingGatherCandidatesMarker.Auto())
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

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingSortCandidatesMarker.Auto())
            {
                if (s_PendingUploadCandidates.Count > 1)
                    s_PendingUploadCandidates.Sort(PendingUploadCandidateComparer.Instance);
            }

            int skippedUploadCount = 0;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingScheduleCandidatesMarker.Auto())
            {
                for (int candidateIndex = 0; candidateIndex < s_PendingUploadCandidates.Count; candidateIndex++)
                {
                    VTPendingUploadCandidate candidate = s_PendingUploadCandidates[candidateIndex];
                    int spaceId = candidate.AddressSpace.SpaceId;
                    if (GetScheduledUploadCount(spaceId)
                        >= candidate.AddressSpace.Descriptor.MaxUploadsPerFrame)
                    {
                        skippedUploadCount += 1;
                        continue;
                    }

                    if (!candidate.AddressSpace.TrySchedulePendingUpload(
                            s_UploadScheduler,
                            cmd,
                            candidate))
                    {
                        skippedUploadCount += 1;
                        continue;
                    }

                    RecordScheduledUpload(spaceId);
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

            bool supportedCamera = camera.cameraType == CameraType.Game
                                   || camera.cameraType == CameraType.SceneView;
            return supportedCamera && IsFeedbackPlatformSupported(
                SystemInfo.graphicsShaderLevel,
                SystemInfo.supportsAsyncGPUReadback,
                SystemInfo.supportedRandomWriteTargetCount);
        }

        internal static bool IsFeedbackPlatformSupported(
            int graphicsShaderLevel,
            bool supportsAsyncGpuReadback,
            int supportedRandomWriteTargetCount)
        {
            return graphicsShaderLevel >= 50
                   && supportsAsyncGpuReadback
                   && supportedRandomWriteTargetCount
                   > VirtualTextureFeedbackBindingUtility.HashUavSlot;
        }
    }
}
