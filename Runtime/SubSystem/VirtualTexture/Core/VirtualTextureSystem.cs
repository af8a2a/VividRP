using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal sealed class VTPagePinLease : IDisposable
    {
        private readonly int m_SpaceId;
        private readonly VirtualTexturePageCoord m_Coord;
        private bool m_IsDisposed;

        internal VTPagePinLease(int spaceId, in VirtualTexturePageCoord coord)
        {
            m_SpaceId = spaceId;
            m_Coord = coord;
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            m_IsDisposed = true;
            VirtualTextureSystem.ReleasePagePin(m_SpaceId, m_Coord);
        }
    }

    internal sealed class VirtualTextureSystem : VividSubsystem<VirtualTextureSystem>
    {
        private readonly struct PagePinKey : IEquatable<PagePinKey>
        {
            internal PagePinKey(int spaceId, in VirtualTexturePageCoord coord)
            {
                SpaceId = spaceId;
                Coord = coord;
            }

            internal int SpaceId { get; }
            internal VirtualTexturePageCoord Coord { get; }

            public bool Equals(PagePinKey other) =>
                SpaceId == other.SpaceId && Coord.Equals(other.Coord);

            public override bool Equals(object obj) =>
                obj is PagePinKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(SpaceId, Coord);
        }

        private struct PagePinState
        {
            internal int RefCount;
            internal bool WasLocked;
        }

        private readonly struct ProspectivePhysicalPoolStats
        {
            internal ProspectivePhysicalPoolStats(
                int totalPageCount,
                int recentVisiblePageCount,
                int predictedPageCount,
                int lockedPageCount)
            {
                TotalPageCount = Mathf.Max(0, totalPageCount);
                RecentVisiblePageCount = Mathf.Clamp(
                    recentVisiblePageCount,
                    0,
                    TotalPageCount);
                PredictedPageCount = Mathf.Clamp(
                    predictedPageCount,
                    0,
                    TotalPageCount - RecentVisiblePageCount);
                LockedPageCount = Mathf.Clamp(
                    lockedPageCount,
                    0,
                    TotalPageCount);
            }

            internal int TotalPageCount { get; }

            internal int RecentVisiblePageCount { get; }

            internal int PredictedPageCount { get; }

            internal int LockedPageCount { get; }

            internal float ProjectedResidency => TotalPageCount > 0
                ? (RecentVisiblePageCount + PredictedPageCount) / (float)TotalPageCount
                : 0f;

            internal bool CanApplyResidencyBias => TotalPageCount > 0
                && LockedPageCount / (float)TotalPageCount
                <= VTAdaptiveMipBiasController.ResidencyLockedUpperBound;
        }

        private sealed class PhysicalPoolAdaptiveMipBiasState
        {
            internal readonly VTAdaptiveMipBiasController Controller = new();

            internal int LastEvictedPageCount;
        }

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
        private static readonly Comparison<VTPageTableSpace>
            s_AddressSpaceIdComparison = CompareAddressSpacesById;
        private static readonly List<VirtualTextureFeedbackBatch> s_InjectedReadbacks = new();
        private static readonly Dictionary<FeedbackMotionKey, FeedbackMotionState> s_FeedbackMotionStates = new();
        private static readonly Dictionary<int, Vector2Int> s_PrefetchBiasBySpace = new();
        private static readonly Dictionary<int, int> s_RemainingResidencyBudgetBySpace = new();
        private static readonly Dictionary<int, int> s_AllocatedResidencyRequestsBySpace = new();
        private static readonly Dictionary<int, int> s_AllocatedPrefetchRequestsBySpace = new();
        private static readonly Dictionary<int, int> s_ScheduledUploadsBySpace = new();
        private static readonly Dictionary<VTPhysicalPool, int> s_PendingDataByPhysicalPool = new();
        private static readonly Dictionary<VTPhysicalPool, int> s_PendingUploadByPhysicalPool = new();
        private static readonly Dictionary<VTPhysicalPool, int> s_ResidencyBudgetByPhysicalPool = new();
        private static readonly Dictionary<VTPhysicalPool, VTPhysicalPoolStats> s_PhysicalPoolStatsByPool = new();
        private static readonly Dictionary<VTPhysicalPool, PhysicalPoolAdaptiveMipBiasState>
            s_AdaptiveMipBiasStateByPhysicalPool = new();
        private static readonly Dictionary<VTPhysicalPool, int> s_FeedbackFaultHitsByPhysicalPool = new();
        private static readonly Dictionary<int, bool> s_AdaptiveMipBiasEnabledBySpace = new();
        private static readonly Dictionary<VTPhysicalPool, float> s_AdaptiveMipBiasByPhysicalPool = new();
        private static readonly Dictionary<int, float> s_AdaptiveMipBiasBySpace = new();
        private static readonly Dictionary<PagePinKey, PagePinState> s_PagePins = new();
        private static readonly List<PagePinKey> s_PagePinKeysToRemove = new();
        private static readonly List<FeedbackMotionKey> s_FeedbackMotionKeysToRemove = new();
        private static readonly List<VTPageTableSpace> s_UploadSpaceOrder = new();
        private static readonly List<VTPendingUploadCandidate> s_PendingUploadCandidates = new();
        private static readonly List<ResidencyPriorityCandidate> s_ResidencyPriorityCandidates = new();
        private static readonly List<VTRequestPreparationBatch> s_RequestPreparationBatches = new();
        private static readonly List<PrefetchPriorityCandidate> s_PrefetchPriorityCandidates = new();
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
        private static int s_LastDemandPrefetchCandidateCount;
        private static int s_LastPrefetchCandidateProcessCount;
        private static float s_MaxAdaptiveMipBias;
        private static float s_MaxAdaptiveControllerMipBias;
#if UNITY_INCLUDE_TESTS
        private static int s_PhysicalPoolFreePageCollectionCount;
        private static int s_PhysicalPoolStatsCollectionCount;
        private static int s_UploadSpaceSortCount;
        private static int s_LastRequestPreparationScheduledJobCount;
        private static int s_LastRequestPreparationWaitCount;
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
                in VirtualTextureAggregatedFeedbackRequest request,
                int producerPriority)
            {
                Request = request;
                PriorityKey = VTRequestPriorityKey.FromFeedbackRequest(
                    request,
                    producerPriority);
            }

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

        private readonly struct VTRequestPreparationBatch
        {
            internal VTRequestPreparationBatch(
                VTPageTableSpace addressSpace,
                NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests,
                int assignedSpaceBudget,
                int remainingSpaceRequestBudget)
            {
                AddressSpace = addressSpace;
                Requests = requests;
                AssignedSpaceBudget = assignedSpaceBudget;
                RemainingSpaceRequestBudget = remainingSpaceRequestBudget;
            }

            internal VTPageTableSpace AddressSpace { get; }

            internal NativeSlice<VirtualTextureAggregatedFeedbackRequest> Requests { get; }

            internal int AssignedSpaceBudget { get; }

            internal int RemainingSpaceRequestBudget { get; }
        }

        private readonly struct PrefetchPriorityCandidate
        {
            internal PrefetchPriorityCandidate(
                in VTPrefetchCandidate candidate,
                int producerPriority)
            {
                Candidate = candidate;
                PriorityKey = VTRequestPriorityKey.FromFeedbackRequest(
                    candidate.Request,
                    producerPriority);
            }

            internal VTPrefetchCandidate Candidate { get; }

            internal VTRequestPriorityKey PriorityKey { get; }
        }

        private sealed class PrefetchPriorityCandidateComparer : IComparer<PrefetchPriorityCandidate>
        {
            internal static readonly PrefetchPriorityCandidateComparer Instance = new();

            private PrefetchPriorityCandidateComparer()
            {
            }

            public int Compare(PrefetchPriorityCandidate left, PrefetchPriorityCandidate right)
            {
                int priorityCompare = VTRequestPriorityUtility.Compare(
                    left.PriorityKey,
                    right.PriorityKey);
                if (priorityCompare != 0)
                    return priorityCompare;

                VirtualTextureAggregatedFeedbackRequest leftRequest = left.Candidate.Request;
                VirtualTextureAggregatedFeedbackRequest rightRequest = right.Candidate.Request;
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
            s_PendingDataByPhysicalPool.Clear();
            s_PendingUploadByPhysicalPool.Clear();
            s_ResidencyBudgetByPhysicalPool.Clear();
            s_PhysicalPoolStatsByPool.Clear();
            s_AdaptiveMipBiasStateByPhysicalPool.Clear();
            s_FeedbackFaultHitsByPhysicalPool.Clear();
            s_AdaptiveMipBiasEnabledBySpace.Clear();
            s_AdaptiveMipBiasByPhysicalPool.Clear();
            s_AdaptiveMipBiasBySpace.Clear();
            s_PagePins.Clear();
            s_PagePinKeysToRemove.Clear();
            s_FeedbackMotionKeysToRemove.Clear();
            s_UploadSpaceOrder.Clear();
            s_PendingUploadCandidates.Clear();
            s_ResidencyPriorityCandidates.Clear();
            s_RequestPreparationBatches.Clear();
            s_PrefetchPriorityCandidates.Clear();
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
            s_LastDemandPrefetchCandidateCount = 0;
            s_LastPrefetchCandidateProcessCount = 0;
            s_MaxAdaptiveMipBias = 0f;
            s_MaxAdaptiveControllerMipBias = 0f;
#if UNITY_INCLUDE_TESTS
            s_PhysicalPoolFreePageCollectionCount = 0;
            s_PhysicalPoolStatsCollectionCount = 0;
            s_UploadSpaceSortCount = 0;
            s_LastRequestPreparationScheduledJobCount = 0;
            s_LastRequestPreparationWaitCount = 0;
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

        internal static void SetAdaptiveMipBiasEnabled(int spaceId, bool enabled)
        {
            Initialize();
            if (!s_PageTableSpaces.ContainsKey(spaceId))
                return;

            s_AdaptiveMipBiasEnabledBySpace[spaceId] = enabled;
            if (!enabled)
                s_AdaptiveMipBiasBySpace[spaceId] = 0f;
        }

        internal static float ResolveAdaptiveMipBias(int spaceId, float fallbackMipBias)
        {
            if (s_AdaptiveMipBiasEnabledBySpace.TryGetValue(spaceId, out bool enabled)
                && !enabled)
            {
                return 0f;
            }

            return s_AdaptiveMipBiasBySpace.TryGetValue(spaceId, out float mipBias)
                ? mipBias
                : Mathf.Max(0f, fallbackMipBias);
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

            s_PagePins.Clear();
            s_PagePinKeysToRemove.Clear();
            s_FeedbackAggregator.Clear();
            s_CompletedReadbacks.Clear();
            s_InjectedReadbacks.Clear();
            s_FeedbackMotionStates.Clear();
            s_PrefetchBiasBySpace.Clear();
            s_RemainingResidencyBudgetBySpace.Clear();
            s_AllocatedResidencyRequestsBySpace.Clear();
            s_AllocatedPrefetchRequestsBySpace.Clear();
            s_ScheduledUploadsBySpace.Clear();
            s_PendingDataByPhysicalPool.Clear();
            s_PendingUploadByPhysicalPool.Clear();
            s_ResidencyBudgetByPhysicalPool.Clear();
            s_PhysicalPoolStatsByPool.Clear();
            foreach (PhysicalPoolAdaptiveMipBiasState state in s_AdaptiveMipBiasStateByPhysicalPool.Values)
            {
                state.Controller.Reset();
                state.LastEvictedPageCount = 0;
            }
            s_FeedbackFaultHitsByPhysicalPool.Clear();
            s_AdaptiveMipBiasByPhysicalPool.Clear();
            s_AdaptiveMipBiasBySpace.Clear();
            s_FeedbackMotionKeysToRemove.Clear();
            s_UploadSpaceOrder.Clear();
            s_PendingUploadCandidates.Clear();
            s_ResidencyPriorityCandidates.Clear();
            s_RequestPreparationBatches.Clear();
            s_PrefetchPriorityCandidates.Clear();
            s_DemandEvictionBudgetFrameIndex = int.MinValue;
            s_RemainingDemandEvictionBudget = 0;
            s_GlobalFrameIndex = int.MinValue;
            s_AllocatedResidencyRequestCount = 0;
            s_AllocatedPrefetchRequestCount = 0;
            s_LastResidencyCandidateCount = 0;
            s_LastDemandPrefetchCandidateCount = 0;
            s_LastPrefetchCandidateProcessCount = 0;
            s_MaxAdaptiveMipBias = 0f;
            s_MaxAdaptiveControllerMipBias = 0f;
#if UNITY_INCLUDE_TESTS
            s_PhysicalPoolFreePageCollectionCount = 0;
            s_PhysicalPoolStatsCollectionCount = 0;
            s_UploadSpaceSortCount = 0;
            s_LastRequestPreparationScheduledJobCount = 0;
            s_LastRequestPreparationWaitCount = 0;
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

                s_TransitionSchedulingSpaces.Sort(s_AddressSpaceIdComparison);
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

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackAggregateMarker.Auto())
            {
                s_FeedbackAggregator.Schedule(
                    s_CompletedReadbacks,
                    cachePriorityViewId,
                    activeViewId,
                    activeCameraType);
            }

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
            s_FeedbackFaultHitsByPhysicalPool.Clear();
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackReadbackStatsMarker.Auto())
            {
                for (int batchIndex = 0; batchIndex < s_CompletedReadbacks.Count; batchIndex++)
                {
                    VirtualTextureFeedbackBatch batch = s_CompletedReadbacks[batchIndex];
                    AccumulateFeedbackFaultHitsByPhysicalPool(batch);
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

            s_FeedbackAggregator.Complete();
            int deduplicatedRequestCount = s_FeedbackAggregator.AggregatedRequests.Length;
            int activeViewDeduplicatedRequestCount = s_FeedbackAggregator.ActiveViewRequestCount;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrefetchBiasMarker.Auto())
                ResolvePrefetchBiasBySpace(cachePriorityViewId);

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
            s_PrefetchPriorityCandidates.Clear();
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyDemandPassMarker.Auto())
            {
                s_RequestPreparationBatches.Clear();
                JobHandle combinedPreparationHandle = default;
                bool hasScheduledPreparationJobs = false;
#if UNITY_INCLUDE_TESTS
                s_LastRequestPreparationScheduledJobCount = 0;
                s_LastRequestPreparationWaitCount = 0;
#endif
                try
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

                        int scheduledJobCount = addressSpace.ScheduleRequestPreparation(
                            spaceRequests,
                            frameIndex,
                            out JobHandle preparationHandle);
                        s_RequestPreparationBatches.Add(new VTRequestPreparationBatch(
                            addressSpace,
                            spaceRequests,
                            assignedSpaceBudget,
                            remainingSpaceRequestBudget));
                        if (scheduledJobCount > 0)
                        {
                            hasScheduledPreparationJobs = true;
                            combinedPreparationHandle = JobHandle.CombineDependencies(
                                combinedPreparationHandle,
                                preparationHandle);
#if UNITY_INCLUDE_TESTS
                            s_LastRequestPreparationScheduledJobCount += scheduledJobCount;
#endif
                        }
                    }

                    if (hasScheduledPreparationJobs)
                    {
                        using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureRequestPreparationWaitMarker.Auto())
                            combinedPreparationHandle.Complete();
#if UNITY_INCLUDE_TESTS
                        if (s_LastRequestPreparationScheduledJobCount > 0)
                            s_LastRequestPreparationWaitCount = 1;
#endif
                    }

                    // Protect every resident page resolved by this feedback wave before
                    // any space is allowed to allocate or evict from a shared pool.
                    for (int batchIndex = 0;
                         batchIndex < s_RequestPreparationBatches.Count;
                         batchIndex++)
                    {
                        VTRequestPreparationBatch batch = s_RequestPreparationBatches[batchIndex];
                        batch.AddressSpace.TouchPreparedResidentRequests(
                            batch.Requests,
                            frameIndex);
                    }

                    for (int batchIndex = 0;
                         batchIndex < s_RequestPreparationBatches.Count;
                         batchIndex++)
                    {
                        VTRequestPreparationBatch batch = s_RequestPreparationBatches[batchIndex];
                        VTPageTableSpace addressSpace = batch.AddressSpace;
                        using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyMarker.Auto())
                        {
                            VTResidencyProcessResult residencyResult;
                            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyProcessRequestsMarker.Auto())
                            {
                                residencyResult = addressSpace.ProcessPreparedRequests(
                                    batch.Requests,
                                    cachePriorityViewId,
                                    frameIndex,
                                    batch.AssignedSpaceBudget,
                                    rebuildPageTable: false);
                            }

                            allocatedResidencyRequestCount += residencyResult.AllocatedRequestCount;
                            RecordAllocatedResidencyRequests(
                                addressSpace.SpaceId,
                                residencyResult.AllocatedRequestCount);
                            s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId] = Mathf.Max(
                                0,
                                batch.RemainingSpaceRequestBudget - residencyResult.AllocatedRequestCount);
                            AccumulateResidencyStats(
                                residencyResult,
                                ref evictionCount,
                                ref pendingMipGapSum,
                                ref pendingMipGapMax,
                                ref pendingMipGapSampleCount);

                            if (addressSpace.Descriptor.NeighborPrefetchCount > 0)
                            {
                                // Copy the refinement-merged seeds produced by the completed
                                // preparation chain. Its Consume job rebuilds them next time.
                                for (int candidateIndex = 0;
                                     candidateIndex < addressSpace.PrefetchCandidateCount;
                                     candidateIndex++)
                                {
                                    s_PrefetchPriorityCandidates.Add(new PrefetchPriorityCandidate(
                                        addressSpace.GetPrefetchCandidate(candidateIndex),
                                        addressSpace.ProducerPriority));
                                }
                            }
                        }
                    }
                }
                finally
                {
                    for (int batchIndex = 0;
                         batchIndex < s_RequestPreparationBatches.Count;
                         batchIndex++)
                    {
                        s_RequestPreparationBatches[batchIndex]
                            .AddressSpace
                            .CompleteRequestPreparation();
                    }
                    s_RequestPreparationBatches.Clear();
                }
            }

            remainingResidencyRequestBudget = Mathf.Max(
                0,
                globalResidencyRequestBudget - allocatedResidencyRequestCount);
            s_LastDemandPrefetchCandidateCount = s_PrefetchPriorityCandidates.Count;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyPrefetchPassMarker.Auto())
            {
                s_LastPrefetchCandidateProcessCount = 0;
                if (s_PrefetchPriorityCandidates.Count > 1)
                    s_PrefetchPriorityCandidates.Sort(PrefetchPriorityCandidateComparer.Instance);
                int remainingGlobalPrefetchRequestBudget = Mathf.Max(
                    0,
                    s_MaxPrefetchAllocationsPerFrame - s_AllocatedPrefetchRequestCount);
                for (int candidateIndex = 0;
                     candidateIndex < s_PrefetchPriorityCandidates.Count
                     && remainingResidencyRequestBudget > 0;
                     candidateIndex++)
                {
                    PrefetchPriorityCandidate priorityCandidate =
                        s_PrefetchPriorityCandidates[candidateIndex];
                    VTPrefetchCandidate candidate = priorityCandidate.Candidate;
                    VirtualTextureAggregatedFeedbackRequest request = candidate.Request;
                    // Demand may have attached this exact page from another space after
                    // the candidate snapshot was built.
                    if (!s_PageTableSpaces.TryGetValue(request.SpaceId, out VTPageTableSpace addressSpace)
                        || addressSpace.Descriptor.NeighborPrefetchCount <= 0)
                    {
                        continue;
                    }

                    VTResidencyRequestClassification classification =
                        addressSpace.GetExactResidencyClassification(request.PageCoord);
                    if (!IsResidencyCandidateEligible(classification))
                        continue;

                    int spaceResidencyRequestBudget = s_RemainingResidencyBudgetBySpace[addressSpace.SpaceId];
                    int remainingSpacePrefetchRequestBudget = Mathf.Max(
                        0,
                        addressSpace.Descriptor.MaxPrefetchAllocationsPerFrame
                        - GetAllocatedPrefetchRequestCount(addressSpace.SpaceId));
                    if (spaceResidencyRequestBudget <= 0)
                        continue;

                    int neighborPrefetchRequestBudget = Mathf.Min(
                        remainingGlobalPrefetchRequestBudget,
                        remainingSpacePrefetchRequestBudget);
                    // Demand already promoted/touched Pending seeds. With no neighbor
                    // budget left, only Missing seeds have useful scalar work remaining.
                    if (classification == VTResidencyRequestClassification.Pending
                        && neighborPrefetchRequestBudget <= 0)
                    {
                        continue;
                    }

                    s_PrefetchBiasBySpace.TryGetValue(addressSpace.SpaceId, out Vector2Int prefetchBias);
                    s_LastPrefetchCandidateProcessCount += 1;
                    VTResidencyProcessResult residencyResult;
                    using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureResidencyProcessPrefetchCandidateMarker.Auto())
                    {
                        residencyResult = addressSpace.ProcessPrefetchCandidate(
                            candidate,
                            cachePriorityViewId,
                            prefetchBias,
                            frameIndex,
                            Mathf.Min(
                                remainingResidencyRequestBudget,
                                spaceResidencyRequestBudget),
                            neighborPrefetchRequestBudget);
                    }
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
            s_PrefetchPriorityCandidates.Clear();

            s_RemainingDemandEvictionBudget = Mathf.Max(
                0,
                s_RemainingDemandEvictionBudget - evictionCount);

            evictionCount += CollectAndSchedulePendingUploads(frameIndex, cmd);
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
            CollectPendingRequestCounts(
                out int pendingDataCount,
                out int physicalPendingUploadCount);
            int pendingUploadCount = pendingDataCount + physicalPendingUploadCount;
            VTPhysicalPoolStats physicalPoolStats;
            ProspectivePhysicalPoolStats prospectivePhysicalPoolStats;
            VTPhysicalPool prospectivePhysicalPool;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStatsPhysicalPoolsMarker.Auto())
            {
                physicalPoolStats = CollectPhysicalPoolStats(
                    frameIndex,
                    globalResidencyRequestBudget,
                    out prospectivePhysicalPoolStats,
                    out prospectivePhysicalPool);
            }
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
                            physicalPendingUploadCount,
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
                                debugSettings.virtualTextureFallbackSampleCountOverride >= 0,
                            physicalPoolPageCount:
                                prospectivePhysicalPoolStats.TotalPageCount,
                            recentVisiblePhysicalPageCount:
                                prospectivePhysicalPoolStats.RecentVisiblePageCount,
                            predictedPhysicalPageCount:
                                prospectivePhysicalPoolStats.PredictedPageCount,
                            lockedPhysicalPageCount:
                                prospectivePhysicalPoolStats.LockedPageCount));
                }
                float adaptiveMipBiasOverride = debugSettings.virtualTextureAdaptiveMipBiasOverride;
                if (adaptiveMipBiasOverride >= 0f)
                    adaptiveMipBias = adaptiveMipBiasOverride;

                adaptiveMipBias = UpdateAdaptiveMipBiasByPhysicalPool(
                    frameIndex,
                    globalResidencyRequestBudget,
                    blockedUploadCount,
                    streamSaturatedRequestCount,
                    prospectivePhysicalPool,
                    debugSettings,
                    adaptiveMipBias);
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
                    VTStreamChunkManager.SharedDecodedCacheBudget,
                    pendingDataCount,
                    physicalPendingUploadCount);
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
                        VTStreamChunkManager.SharedDecodedCacheBudget,
                        pendingDataCount,
                        physicalPendingUploadCount);
                    VirtualTextureStatsRegistry.ReportView(viewStats);
                }
            }
        }

        internal static bool RecordPageTableUpdates(RenderGraph renderGraph)
        {
            Initialize();
            return s_PageTableScatterUploader.Record(renderGraph, s_PageTableSpaces.Values);
        }

        internal static bool TryGetSpaceBinding(
            int spaceId,
            out VirtualTextureSpaceBinding binding)
        {
            if (s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace))
            {
                binding = addressSpace.CreateBinding(
                    allocationId: 0,
                    privateSpace: false,
                    feedbackRequests: null,
                    feedbackCounter: null,
                    feedbackResidentHash: null,
                    feedbackRequestCapacity: 0,
                    feedbackResidentHashCapacity: 0,
                    feedbackState: null);
                return binding.IsValid;
            }

            binding = default;
            return false;
        }

        internal static bool IsPageTableEntryPendingUpload(
            int spaceId,
            in VirtualTexturePageCoord coord)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                   && addressSpace.IsPageTableEntryPendingUpload(coord);
        }

        internal static bool RefreshPageTableBufferImmediatelyForTesting(int spaceId)
        {
            if (!s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace))
                return false;

            addressSpace.RefreshPageTableBufferImmediately();
            return true;
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
            foreach (KeyValuePair<int, VTPageTableSpace> pair in s_PageTableSpaces)
            {
                if (producerHandles.Contains(pair.Value.ProducerHandle))
                    flushedCount += pair.Value.FlushPendingDataRequests();
            }

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
            RemovePagePinsForRegion(spaceId, mip, pageRegion);
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
                RemovePagePinsForRegion(spaceId, region.Mip, region.PageRegion);
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

        internal static bool TryQueuePageResidentWithinBudget(
            int spaceId,
            in VirtualTexturePageCoord coord,
            bool locked = false,
            int frameIndex = 0)
        {
            if (!s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace))
                return false;

            VTResidencyRequestClassification classification =
                addressSpace.GetExactResidencyClassification(coord);
            if (classification == VTResidencyRequestClassification.Invalid)
                return false;
            if (classification != VTResidencyRequestClassification.Missing)
                return true;

            if (s_AllocatedResidencyRequestCount >= s_MaxResidencyAllocationsPerFrame
                || GetAllocatedResidencyRequestCount(spaceId)
                >= addressSpace.Descriptor.MaxResidencyAllocationsPerFrame)
            {
                return false;
            }

            int allocationFrameIndex = s_GlobalFrameIndex != int.MinValue
                ? s_GlobalFrameIndex
                : frameIndex;
            if (!addressSpace.TryQueuePageResident(coord, locked, allocationFrameIndex))
                return false;

            RecordAllocatedResidencyRequests(spaceId, 1);
            if (s_RemainingResidencyBudgetBySpace.TryGetValue(spaceId, out int remainingBudget))
            {
                s_RemainingResidencyBudgetBySpace[spaceId] = Mathf.Max(
                    0,
                    remainingBudget - 1);
            }
            return true;
        }

        internal static bool TryQueuePageRefresh(
            int spaceId,
            in VirtualTexturePageCoord coord,
            int frameIndex = 0)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                   && addressSpace.TryQueuePageRefresh(coord, frameIndex);
        }

        internal static bool IsPageRefreshPending(
            int spaceId,
            in VirtualTexturePageCoord coord)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                   && addressSpace.IsPageRefreshPending(coord);
        }

        internal static bool TryAcquirePagePinLease(
            int spaceId,
            in VirtualTexturePageCoord coord,
            out VTPagePinLease lease)
        {
            lease = null;
            var key = new PagePinKey(spaceId, coord);
            if (s_PagePins.TryGetValue(key, out PagePinState existing))
            {
                existing.RefCount += 1;
                s_PagePins[key] = existing;
                lease = new VTPagePinLease(spaceId, coord);
                return true;
            }

            if (!TryGetPageTableEntry(spaceId, coord, out VirtualTexturePageTableEntry entry)
                || !entry.Resident
                || entry.PendingUpload
                || entry.ResolvedMip != coord.Mip
                || !SetPageLocked(spaceId, coord, true))
            {
                return false;
            }

            s_PagePins.Add(key, new PagePinState
            {
                RefCount = 1,
                WasLocked = entry.Locked,
            });
            lease = new VTPagePinLease(spaceId, coord);
            return true;
        }

        internal static void ReleasePagePin(
            int spaceId,
            in VirtualTexturePageCoord coord)
        {
            var key = new PagePinKey(spaceId, coord);
            if (!s_PagePins.TryGetValue(key, out PagePinState state))
                return;

            state.RefCount -= 1;
            if (state.RefCount > 0)
            {
                s_PagePins[key] = state;
                return;
            }

            s_PagePins.Remove(key);
            if (!state.WasLocked)
                SetPageLocked(spaceId, coord, false);
        }

        internal static int GetPagePinCountForTesting(
            int spaceId,
            in VirtualTexturePageCoord coord)
        {
            return s_PagePins.TryGetValue(new PagePinKey(spaceId, coord), out PagePinState state)
                ? state.RefCount
                : 0;
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

        internal static int GetPendingDataCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PendingDataRequestCount
                : 0;
        }

        internal static int GetPhysicalPendingUploadCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PendingUploadRequestCount
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
                   && addressSpace.CommitPendingPageTableUpload(
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

        internal static int GetRequestPreparationCapacityForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.RequestPreparationCapacity
                : 0;
        }

        internal static bool WasLastRequestPreparationParallelForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                   && addressSpace.LastRequestPreparationUsedParallelJob;
        }

        internal static int GetLastRequestPreparationScheduledJobCountForTesting()
        {
#if UNITY_INCLUDE_TESTS
            return s_LastRequestPreparationScheduledJobCount;
#else
            return 0;
#endif
        }

        internal static int GetLastRequestPreparationWaitCountForTesting()
        {
#if UNITY_INCLUDE_TESTS
            return s_LastRequestPreparationWaitCount;
#else
            return 0;
#endif
        }

        internal static int GetLastResidencyCandidateCountForTesting()
        {
            return s_LastResidencyCandidateCount;
        }

        internal static int GetLastDemandPrefetchCandidateCountForTesting()
        {
            return s_LastDemandPrefetchCandidateCount;
        }

        internal static int GetLastPrefetchCandidateProcessCountForTesting()
        {
            return s_LastPrefetchCandidateProcessCount;
        }

#if UNITY_INCLUDE_TESTS
        internal static int GetResidencyProcessRequestsCallCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.ResidencyProcessRequestsCallCount
                : 0;
        }

        internal static int GetPrefetchCandidateProcessCallCountForTesting(int spaceId)
        {
            return s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace)
                ? addressSpace.PrefetchCandidateProcessCallCount
                : 0;
        }
#endif

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

        internal static int GetUploadSpaceSortCountForTesting()
        {
            return s_UploadSpaceSortCount;
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
            return s_MaxAdaptiveControllerMipBias;
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

        internal static int AdaptiveRecentVisiblePhysicalPageCount =>
            s_AdaptiveMipBiasController.LastRecentVisiblePhysicalPageCount;

        internal static int AdaptivePredictedPhysicalPageCount =>
            s_AdaptiveMipBiasController.LastPredictedPhysicalPageCount;

        internal static int AdaptiveProspectivePhysicalPoolPageCount =>
            s_AdaptiveMipBiasController.LastProspectivePhysicalPoolPageCount;

        internal static int AdaptiveLockedPhysicalPageCount =>
            s_AdaptiveMipBiasController.LastLockedPhysicalPageCount;

        internal static float AdaptiveLockedPhysicalPageResidency =>
            s_AdaptiveMipBiasController.LastLockedPhysicalPageResidency;

        internal static float AdaptiveProspectiveResidency =>
            s_AdaptiveMipBiasController.LastProspectiveResidency;

        internal static float AdaptiveProspectiveResidencyPressure =>
            s_AdaptiveMipBiasController.LastProspectiveResidencyPressure;

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
            s_PendingDataByPhysicalPool.Remove(pool);
            s_PendingUploadByPhysicalPool.Remove(pool);
            s_ResidencyBudgetByPhysicalPool.Remove(pool);
            s_PhysicalPoolStatsByPool.Remove(pool);
            s_AdaptiveMipBiasStateByPhysicalPool.Remove(pool);
            s_FeedbackFaultHitsByPhysicalPool.Remove(pool);
            s_AdaptiveMipBiasByPhysicalPool.Remove(pool);
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
            return CollectPhysicalPoolStats(
                -1,
                0,
                out _,
                out _);
        }

        private static VTPhysicalPoolStats CollectPhysicalPoolStats(
            int frameIndex,
            int uploadBudget,
            out ProspectivePhysicalPoolStats prospectiveStats,
            out VTPhysicalPool prospectivePool)
        {
#if UNITY_INCLUDE_TESTS
            s_PhysicalPoolStatsCollectionCount += 1;
#endif
            int residentPageCount = 0;
            int freePageCount = 0;
            int lockedPageCount = 0;
            int evictedPageCount = 0;
            int totalPageCount = 0;
            int recentVisiblePageCount = 0;
            long allocatedByteCount = 0;
            long residentByteCount = 0;
            prospectiveStats = default;
            prospectivePool = null;
            s_PhysicalPoolStatsByPool.Clear();
            foreach (VTPhysicalPool pool in s_PhysicalPools.Values)
            {
                VTPhysicalPoolStats poolStats = pool.CollectStats(
                    frameIndex,
                    VTPhysicalPool.FeedbackEvictionProtectionFrames);
                s_PhysicalPoolStatsByPool[pool] = poolStats;
                residentPageCount += poolStats.ResidentPageCount;
                freePageCount += poolStats.FreePageCount;
                lockedPageCount += poolStats.LockedPageCount;
                evictedPageCount += poolStats.EvictedPageCount;
                totalPageCount += poolStats.TotalPageCount;
                recentVisiblePageCount += poolStats.RecentVisiblePageCount;
                allocatedByteCount = checked(
                    allocatedByteCount + poolStats.AllocatedByteCount);
                residentByteCount = checked(
                    residentByteCount + poolStats.ResidentByteCount);

                s_PendingDataByPhysicalPool.TryGetValue(
                    pool,
                    out int pendingDataCount);
                s_ResidencyBudgetByPhysicalPool.TryGetValue(
                    pool,
                    out int poolResidencyBudget);
                poolResidencyBudget = Mathf.Min(uploadBudget, poolResidencyBudget);
                int predictedPageCount = VTAdaptiveMipBiasController
                    .ComputePredictedPhysicalPageCount(
                        pendingDataCount,
                        poolResidencyBudget);
                var poolProspectiveStats = new ProspectivePhysicalPoolStats(
                    poolStats.TotalPageCount,
                    poolStats.RecentVisiblePageCount,
                    predictedPageCount,
                    poolStats.LockedPageCount);
                if (prospectiveStats.TotalPageCount == 0
                    || (poolProspectiveStats.CanApplyResidencyBias
                        && !prospectiveStats.CanApplyResidencyBias)
                    || (poolProspectiveStats.CanApplyResidencyBias
                        == prospectiveStats.CanApplyResidencyBias
                        && poolProspectiveStats.ProjectedResidency
                        > prospectiveStats.ProjectedResidency))
                {
                    prospectiveStats = poolProspectiveStats;
                    prospectivePool = pool;
                }
            }

            return new VTPhysicalPoolStats(
                s_PhysicalPools.Count,
                residentPageCount,
                freePageCount,
                lockedPageCount,
                evictedPageCount,
                allocatedByteCount,
                residentByteCount,
                totalPageCount,
                recentVisiblePageCount);
        }

        private static float UpdateAdaptiveMipBiasByPhysicalPool(
            int frameIndex,
            int uploadBudget,
            int blockedUploadCount,
            int streamSaturatedRequestCount,
            VTPhysicalPool prospectivePhysicalPool,
            VividRenderingDebugSettingsData debugSettings,
            float fallbackMipBias)
        {
            VTPhysicalPool fallbackFeedbackPhysicalPool = ResolveFeedbackPhysicalPool(
                prospectivePhysicalPool);
            long totalFeedbackFaultHitCount = 0;
            foreach (int faultHitCount in s_FeedbackFaultHitsByPhysicalPool.Values)
                totalFeedbackFaultHitCount += Mathf.Max(0, faultHitCount);
            float adaptiveMipBiasOverride = debugSettings.virtualTextureAdaptiveMipBiasOverride;
            s_AdaptiveMipBiasByPhysicalPool.Clear();
            s_AdaptiveMipBiasBySpace.Clear();
            float maxMipBias = 0f;
            float maxControllerMipBias = 0f;

            foreach (KeyValuePair<VTPhysicalPool, VTPhysicalPoolStats> pair in s_PhysicalPoolStatsByPool)
            {
                VTPhysicalPool pool = pair.Key;
                VTPhysicalPoolStats poolStats = pair.Value;
                if (!s_AdaptiveMipBiasStateByPhysicalPool.TryGetValue(
                        pool,
                        out PhysicalPoolAdaptiveMipBiasState state))
                {
                    state = new PhysicalPoolAdaptiveMipBiasState
                    {
                        LastEvictedPageCount = poolStats.EvictedPageCount,
                    };
                    s_AdaptiveMipBiasStateByPhysicalPool.Add(pool, state);
                }

                int evictionCount = Mathf.Max(
                    0,
                    poolStats.EvictedPageCount - state.LastEvictedPageCount);
                state.LastEvictedPageCount = poolStats.EvictedPageCount;
                bool adaptiveMipBiasEnabled = IsAdaptiveMipBiasEnabledForPool(pool);
                float poolMipBias = 0f;
                if (adaptiveMipBiasEnabled)
                {
                    s_PendingDataByPhysicalPool.TryGetValue(pool, out int pendingDataCount);
                    s_PendingUploadByPhysicalPool.TryGetValue(pool, out int pendingUploadCount);
                    s_ResidencyBudgetByPhysicalPool.TryGetValue(
                        pool,
                        out int poolResidencyBudget);
                    poolResidencyBudget = Mathf.Min(uploadBudget, poolResidencyBudget);
                    int predictedPageCount = VTAdaptiveMipBiasController
                        .ComputePredictedPhysicalPageCount(
                            pendingDataCount,
                            poolResidencyBudget);
                    VTAdaptiveMipBiasController globalController = s_AdaptiveMipBiasController;
                    float feedbackWeight = ResolveFeedbackWeight(
                        pool,
                        fallbackFeedbackPhysicalPool,
                        totalFeedbackFaultHitCount);
                    bool hasFreshFeedback = globalController.LastUpdateHadFreshFeedbackMeasurement;
                    poolMipBias = state.Controller.Update(
                        frameIndex,
                        new VTAdaptiveMipBiasInputs(
                            poolResidencyBudget,
                            SaturatingAddFeedbackCount(pendingDataCount, pendingUploadCount),
                            ScaleFeedbackCount(blockedUploadCount, feedbackWeight),
                            ScaleFeedbackCount(streamSaturatedRequestCount, feedbackWeight),
                            ScaleFeedbackCount(
                                globalController.LastFeedbackOverflowCount,
                                feedbackWeight),
                            ScaleFeedbackCount(
                                globalController.LastFallbackSampleCount,
                                feedbackWeight),
                            poolStats.FreePageCount,
                            evictionCount,
                            hasFreshFeedbackMeasurement: hasFreshFeedback,
                            measuredFeedbackOverflowCount:
                                ScaleFeedbackCount(
                                    globalController.LastMeasuredFeedbackOverflowCount,
                                    feedbackWeight),
                            measuredFallbackSampleCount:
                                ScaleFeedbackCount(
                                    globalController.LastMeasuredFallbackSampleCount,
                                    feedbackWeight),
                            measuredFaultOverflowCount:
                                ScaleFeedbackCount(
                                    globalController.LastMeasuredFaultOverflowCount,
                                    feedbackWeight),
                            measuredResidentOverflowCount:
                                ScaleFeedbackCount(
                                    globalController.LastMeasuredResidentOverflowCount,
                                    feedbackWeight),
                            measuredNonResidentFallbackSampleCount:
                                ScaleFeedbackCount(
                                    globalController.LastMeasuredNonResidentFallbackSampleCount,
                                    feedbackWeight),
                            measuredResidentFallbackSampleCount:
                                ScaleFeedbackCount(
                                    globalController.LastMeasuredResidentFallbackSampleCount,
                                    feedbackWeight),
                            feedbackMeasurementFrameIndex:
                                hasFreshFeedback
                                    ? globalController.LastFreshFeedbackFrameIndex
                                    : -1,
                            weightedAccessSampleCount:
                                ScaleFeedbackCount(
                                    globalController.LastWeightedAccessSampleCount,
                                    feedbackWeight),
                            measuredWeightedAccessSampleCount:
                                ScaleFeedbackCount(
                                    globalController.LastMeasuredWeightedAccessSampleCount,
                                    feedbackWeight),
                            acceptedFaultRequestCount:
                                ScaleFeedbackCount(
                                    globalController.LastMeasuredAcceptedFaultRequestCount,
                                    feedbackWeight),
                            acceptedResidentRequestCount:
                                ScaleFeedbackCount(
                                    globalController.LastMeasuredAcceptedResidentRequestCount,
                                    feedbackWeight),
                            feedbackOverflowOverrideActive:
                                feedbackWeight > 0f
                                && debugSettings.virtualTextureFeedbackOverflowCountOverride >= 0,
                            fallbackSampleOverrideActive:
                                feedbackWeight > 0f
                                && debugSettings.virtualTextureFallbackSampleCountOverride >= 0,
                            physicalPoolPageCount: poolStats.TotalPageCount,
                            recentVisiblePhysicalPageCount: poolStats.RecentVisiblePageCount,
                            predictedPhysicalPageCount: predictedPageCount,
                            lockedPhysicalPageCount: poolStats.LockedPageCount));
                }

                maxControllerMipBias = Mathf.Max(
                    maxControllerMipBias,
                    adaptiveMipBiasEnabled ? state.Controller.CurrentMipBias : 0f);
                if (adaptiveMipBiasEnabled && adaptiveMipBiasOverride >= 0f)
                    poolMipBias = adaptiveMipBiasOverride;
                poolMipBias = Mathf.Clamp(
                    poolMipBias,
                    0f,
                    VTAdaptiveMipBiasController.MaxMipBias);
                s_AdaptiveMipBiasByPhysicalPool[pool] = poolMipBias;
                maxMipBias = Mathf.Max(maxMipBias, poolMipBias);
            }

            foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
            {
                float spaceMipBias = s_AdaptiveMipBiasByPhysicalPool.TryGetValue(
                    addressSpace.PhysicalPool,
                    out float poolMipBias)
                    ? poolMipBias
                    : Mathf.Max(0f, fallbackMipBias);
                if (s_AdaptiveMipBiasEnabledBySpace.TryGetValue(
                        addressSpace.SpaceId,
                        out bool enabled)
                    && !enabled
                    && adaptiveMipBiasOverride < 0f)
                {
                    spaceMipBias = 0f;
                }

                s_AdaptiveMipBiasBySpace[addressSpace.SpaceId] = spaceMipBias;
            }

            s_MaxAdaptiveMipBias = s_PhysicalPoolStatsByPool.Count > 0
                ? maxMipBias
                : Mathf.Max(0f, fallbackMipBias);
            s_MaxAdaptiveControllerMipBias = s_PhysicalPoolStatsByPool.Count > 0
                ? maxControllerMipBias
                : s_AdaptiveMipBiasController.CurrentMipBias;
            return s_MaxAdaptiveMipBias;
        }

        private static VTPhysicalPool ResolveFeedbackPhysicalPool(
            VTPhysicalPool prospectivePhysicalPool)
        {
            VTPhysicalPool feedbackPhysicalPool = null;
            int maxFaultHitCount = 0;
            foreach (KeyValuePair<VTPhysicalPool, int> pair in s_FeedbackFaultHitsByPhysicalPool)
            {
                if (feedbackPhysicalPool != null && pair.Value <= maxFaultHitCount)
                    continue;

                feedbackPhysicalPool = pair.Key;
                maxFaultHitCount = pair.Value;
            }

            return feedbackPhysicalPool ?? prospectivePhysicalPool;
        }

        private static float ResolveFeedbackWeight(
            VTPhysicalPool pool,
            VTPhysicalPool fallbackFeedbackPhysicalPool,
            long totalFaultHitCount)
        {
            // Overflow/fallback counters are shared by the compacted feedback stream.
            // Exact request hit counts provide the best available pool attribution;
            // a counter-only batch falls back to the pool with the highest projected pressure.
            if (totalFaultHitCount <= 0)
                return ReferenceEquals(pool, fallbackFeedbackPhysicalPool) ? 1f : 0f;

            return s_FeedbackFaultHitsByPhysicalPool.TryGetValue(pool, out int poolFaultHitCount)
                ? Mathf.Clamp01((float)((double)poolFaultHitCount / totalFaultHitCount))
                : 0f;
        }

        private static int ScaleFeedbackCount(int count, float weight)
        {
            if (count <= 0 || weight <= 0f)
                return 0;

            return Mathf.Max(1, Mathf.RoundToInt(count * weight));
        }

        private static bool IsAdaptiveMipBiasEnabledForPool(VTPhysicalPool pool)
        {
            foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
            {
                if (!ReferenceEquals(addressSpace.PhysicalPool, pool))
                    continue;

                if (s_AdaptiveMipBiasEnabledBySpace.TryGetValue(
                        addressSpace.SpaceId,
                        out bool enabled)
                    && !enabled)
                {
                    return false;
                }
            }

            return true;
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
            s_AdaptiveMipBiasEnabledBySpace.Remove(spaceId);
            s_AdaptiveMipBiasBySpace.Remove(spaceId);
            RemovePagePinsForSpace(spaceId);
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

        private static void RemovePagePinsForRegion(
            int spaceId,
            int mip,
            RectInt pageRegion)
        {
            s_PagePinKeysToRemove.Clear();
            foreach (PagePinKey key in s_PagePins.Keys)
            {
                if (key.SpaceId == spaceId
                    && key.Coord.Mip == mip
                    && pageRegion.Contains(new Vector2Int(key.Coord.X, key.Coord.Y)))
                {
                    s_PagePinKeysToRemove.Add(key);
                }
            }

            for (int keyIndex = 0; keyIndex < s_PagePinKeysToRemove.Count; keyIndex++)
                s_PagePins.Remove(s_PagePinKeysToRemove[keyIndex]);
            s_PagePinKeysToRemove.Clear();
        }

        private static void RemovePagePinsForSpace(int spaceId)
        {
            s_PagePinKeysToRemove.Clear();
            foreach (PagePinKey key in s_PagePins.Keys)
            {
                if (key.SpaceId == spaceId)
                    s_PagePinKeysToRemove.Add(key);
            }

            for (int keyIndex = 0; keyIndex < s_PagePinKeysToRemove.Count; keyIndex++)
                s_PagePins.Remove(s_PagePinKeysToRemove[keyIndex]);
            s_PagePinKeysToRemove.Clear();
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

        private static void AccumulateFeedbackFaultHitsByPhysicalPool(
            in VirtualTextureFeedbackBatch batch)
        {
            if (!batch.RequestsReadbackValid)
                return;

            for (int requestIndex = 0; requestIndex < batch.RequestCount; requestIndex++)
            {
                int faultHitCount = batch.GetRequestHitCount(requestIndex);
                if (faultHitCount <= 0)
                    continue;

                VirtualTextureFeedbackProcessor.DecodeKey(
                    batch.GetRequest(requestIndex),
                    out int spaceId,
                    out _);
                if (!s_PageTableSpaces.TryGetValue(spaceId, out VTPageTableSpace addressSpace))
                    continue;

                VTPhysicalPool physicalPool = addressSpace.PhysicalPool;
                s_FeedbackFaultHitsByPhysicalPool.TryGetValue(
                    physicalPool,
                    out int accumulatedHitCount);
                s_FeedbackFaultHitsByPhysicalPool[physicalPool] = SaturatingAddFeedbackCount(
                    accumulatedHitCount,
                    faultHitCount);
            }
        }

        private static void CollectPendingRequestCounts(
            out int pendingDataCount,
            out int physicalPendingUploadCount)
        {
            pendingDataCount = 0;
            physicalPendingUploadCount = 0;
            s_PendingDataByPhysicalPool.Clear();
            s_PendingUploadByPhysicalPool.Clear();
            s_ResidencyBudgetByPhysicalPool.Clear();
            foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
            {
                int spacePendingDataCount = addressSpace.PendingDataRequestCount;
                int spacePendingUploadCount = addressSpace.PendingUploadRequestCount;
                pendingDataCount = SaturatingAddFeedbackCount(
                    pendingDataCount,
                    spacePendingDataCount);
                physicalPendingUploadCount = SaturatingAddFeedbackCount(
                    physicalPendingUploadCount,
                    spacePendingUploadCount);
                VTPhysicalPool physicalPool = addressSpace.PhysicalPool;
                s_PendingDataByPhysicalPool.TryGetValue(
                    physicalPool,
                    out int poolPendingDataCount);
                s_PendingDataByPhysicalPool[physicalPool] = SaturatingAddFeedbackCount(
                    poolPendingDataCount,
                    spacePendingDataCount);
                s_PendingUploadByPhysicalPool.TryGetValue(
                    physicalPool,
                    out int poolPendingUploadCount);
                s_PendingUploadByPhysicalPool[physicalPool] = SaturatingAddFeedbackCount(
                    poolPendingUploadCount,
                    spacePendingUploadCount);
                s_ResidencyBudgetByPhysicalPool.TryGetValue(
                    physicalPool,
                    out int poolResidencyBudget);
                s_ResidencyBudgetByPhysicalPool[physicalPool] = SaturatingAddFeedbackCount(
                    poolResidencyBudget,
                    addressSpace.Descriptor.MaxResidencyAllocationsPerFrame);
            }
        }

        private static void AccumulateResidencyStats(
            in VTResidencyProcessResult result,
            ref int evictionCount,
            ref int pendingMipGapSum,
            ref int pendingMipGapMax,
            ref int pendingMipGapSampleCount)
        {
            evictionCount += result.EvictionCount;
            pendingMipGapSum += result.PendingMipGapSum;
            pendingMipGapMax = Mathf.Max(pendingMipGapMax, result.PendingMipGapMax);
            pendingMipGapSampleCount += result.PendingMipGapSampleCount;
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

        private static int CollectAndSchedulePendingUploads(int frameIndex, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingMarker.Auto())
                return CollectAndSchedulePendingUploadsCore(frameIndex, cmd);
        }

        private static int CollectAndSchedulePendingUploadsCore(int frameIndex, CommandBuffer cmd)
        {
            bool hasPendingUploadWork = false;
            foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
            {
                addressSpace.RetireProducerRequestsIfChanged();
                hasPendingUploadWork |= addressSpace.HasPendingUploadWork;
            }

            if (!hasPendingUploadWork)
            {
                s_PendingUploadCandidates.Clear();
                s_UploadSpaceOrder.Clear();
                return 0;
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingBuildSpaceOrderMarker.Auto())
            {
                s_UploadSpaceOrder.Clear();
                foreach (VTPageTableSpace addressSpace in s_PageTableSpaces.Values)
                    s_UploadSpaceOrder.Add(addressSpace);
#if UNITY_INCLUDE_TESTS
                s_UploadSpaceSortCount += 1;
#endif
                s_UploadSpaceOrder.Sort(s_AddressSpaceIdComparison);
            }

            int spaceCount = s_UploadSpaceOrder.Count;
            int rotation = spaceCount > 0 ? (int)((uint)frameIndex % (uint)spaceCount) : 0;
            s_PendingUploadCandidates.Clear();
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
            int evictionCount = 0;
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

                    VTPendingUploadScheduleResult scheduleResult =
                        candidate.AddressSpace.TrySchedulePendingUpload(
                            s_UploadScheduler,
                            cmd,
                            candidate,
                            frameIndex,
                            s_RemainingDemandEvictionBudget > 0,
                            out bool evicted);
                    if (evicted)
                    {
                        evictionCount += 1;
                        s_RemainingDemandEvictionBudget = Mathf.Max(
                            0,
                            s_RemainingDemandEvictionBudget - 1);
                    }

                    if (scheduleResult == VTPendingUploadScheduleResult.Deferred)
                    {
                        skippedUploadCount += 1;
                        continue;
                    }

                    if (scheduleResult == VTPendingUploadScheduleResult.ResolvedResident)
                        continue;

                    RecordScheduledUpload(spaceId);
                }
            }

            s_UploadScheduler.AddSkippedUploadCount(skippedUploadCount);
            s_PendingUploadCandidates.Clear();
            s_UploadSpaceOrder.Clear();
            return evictionCount;
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
