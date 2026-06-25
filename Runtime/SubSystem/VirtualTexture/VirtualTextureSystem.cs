using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VirtualTextureSystem : VividSubsystem<VirtualTextureSystem>
    {
        private static readonly Dictionary<int, VTAddressSpace> s_AddressSpaces = new();
        private static readonly Dictionary<string, int> s_SpaceIdsByName = new(StringComparer.Ordinal);
        private static readonly Dictionary<int, VTAllocatedVirtualTexture> s_Allocations = new();
        private static readonly Dictionary<string, int> s_AllocationIdsByName = new(StringComparer.Ordinal);
        private static readonly Dictionary<int, int> s_AllocationIdBySpaceId = new();
        private static readonly Dictionary<VTPhysicalPoolDesc, VTPhysicalPool> s_PhysicalPools = new();
        private static readonly VTProducerRegistry s_ProducerRegistry = new();
        private static readonly VirtualTextureFeedbackCameraSystem s_FeedbackCameraSystem = new();
        private static readonly List<VirtualTextureFeedbackBatch> s_CompletedReadbacks = new();
        private static readonly List<VirtualTextureFeedbackBatch> s_InjectedReadbacks = new();
        private static readonly VirtualTextureFeedbackProcessor.Scratch s_AggregationScratch = new();
        private static readonly List<VirtualTextureAggregatedFeedbackRequest> s_AggregatedRequests = new();
        private static readonly Dictionary<int, List<VirtualTextureAggregatedFeedbackRequest>> s_GroupedRequests = new();
        private static readonly Dictionary<FeedbackMotionKey, FeedbackMotionState> s_FeedbackMotionStates = new();
        private static readonly Dictionary<int, Vector2Int> s_PrefetchBiasBySpace = new();
        private static readonly List<FeedbackMotionKey> s_FeedbackMotionKeysToRemove = new();
        private static readonly UploadCommitterResolver s_UploadCommitterResolver = new();
        private static VTUploadScheduler s_UploadScheduler = new();

        private static int s_NextSpaceId = 1;
        private static int s_NextAllocationId = 1;
        private static int s_FallbackFrameIndex = -1;

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
        }

        protected override void OnDeinitialize()
        {
            foreach (KeyValuePair<int, VTAddressSpace> pair in s_AddressSpaces)
                pair.Value.Dispose();

            foreach (VTPhysicalPool pool in s_PhysicalPools.Values)
                pool.Dispose();

            s_AddressSpaces.Clear();
            s_SpaceIdsByName.Clear();
            s_Allocations.Clear();
            s_AllocationIdsByName.Clear();
            s_AllocationIdBySpaceId.Clear();
            s_PhysicalPools.Clear();
            s_ProducerRegistry.Dispose();
            s_CompletedReadbacks.Clear();
            s_InjectedReadbacks.Clear();
            s_AggregatedRequests.Clear();
            ClearGroupedRequests();
            s_GroupedRequests.Clear();
            s_FeedbackMotionStates.Clear();
            s_PrefetchBiasBySpace.Clear();
            s_FeedbackMotionKeysToRemove.Clear();
            s_FeedbackCameraSystem.Dispose();
            s_NextSpaceId = 1;
            s_NextAllocationId = 1;
            s_FallbackFrameIndex = -1;
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
                VTAddressSpace existingAddressSpace = s_AddressSpaces[existingSpaceId];
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
                VTAddressSpace existingAddressSpace = s_AddressSpaces[existingSpaceId];
                bool sameProducer = s_ProducerRegistry.IsSameProducer(existingAddressSpace.ProducerHandle, producer);
                if (existingAddressSpace.Descriptor.Equals(desc)
                    && sameProducer)
                {
                    return existingSpaceId;
                }

                ReplaceAddressSpace(existingSpaceId, desc, producer);
                return existingSpaceId;
            }

            return RegisterAddressSpace(desc, producer);
        }

        internal static bool UnregisterAddressSpace(int spaceId)
        {
            Initialize();

            if (!s_AddressSpaces.ContainsKey(spaceId))
                return false;

            RemoveAddressSpace(spaceId);
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
            if (!IsInitialized)
                Initialize();

            VividVirtualTextureFrameData virtualTextureFrameData = frameData?.GetOrCreate<VividVirtualTextureFrameData>();
            virtualTextureFrameData?.Reset();
            s_FeedbackCameraSystem.PurgeDestroyedCameras();

            VividCameraData cameraData = TryGetCameraData(frameData);
            Camera camera = cameraData?.camera;
            VirtualTextureViewId activeViewId = VirtualTextureViewId.FromCameraData(cameraData);
            CameraType activeCameraType = camera != null ? camera.cameraType : default;
            VirtualTextureViewId cachePriorityViewId = ResolveCachePriorityViewId(activeViewId, activeCameraType);
            VirtualTextureFeedbackViewSignature activeViewSignature =
                VirtualTextureFeedbackViewSignature.FromCameraData(cameraData);

            int lastReadbackFrame = -1;
            CollectCompletedReadbacks(camera, activeViewId, activeCameraType, ref lastReadbackFrame);

            int faultCount = 0;
            int feedbackOverflowCount = 0;
            int fallbackSampleCount = 0;
            int activeViewFaultCount = 0;
            int activeViewFeedbackOverflowCount = 0;
            int activeViewFallbackSampleCount = 0;
            int activeViewLastReadbackFrame = -1;
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

            VirtualTextureFeedbackProcessor.Aggregate(
                s_CompletedReadbacks,
                s_AggregationScratch,
                s_AggregatedRequests,
                cachePriorityViewId);
            int deduplicatedRequestCount = s_AggregatedRequests.Count;
            int activeViewDeduplicatedRequestCount = CountRequestsFromView(
                s_AggregatedRequests,
                activeViewId,
                activeCameraType);
            GroupRequestsBySpace(s_AggregatedRequests);
            ResolvePrefetchBiasBySpace(cachePriorityViewId);

            int frameIndex = ResolveFrameIndex(frameData);
            int evictionCount = 0;
            int pendingMipGapSum = 0;
            int pendingMipGapMax = 0;
            int pendingMipGapSampleCount = 0;
            int prefetchRequestCount = 0;
            s_UploadScheduler.BeginFrame();
            s_UploadScheduler.CommitCompletedUploads(s_UploadCommitterResolver);
            foreach (KeyValuePair<int, VTAddressSpace> pair in s_AddressSpaces)
            {
                VTAddressSpace addressSpace = pair.Value;
                VTResidencyProcessResult residencyResult;
                s_PrefetchBiasBySpace.TryGetValue(addressSpace.SpaceId, out Vector2Int prefetchBias);
                if (s_GroupedRequests.TryGetValue(addressSpace.SpaceId, out List<VirtualTextureAggregatedFeedbackRequest> spaceRequests))
                    residencyResult = addressSpace.ProcessRequests(spaceRequests, cachePriorityViewId, prefetchBias, frameIndex);
                else
                    residencyResult = addressSpace.ProcessRequests(null, cachePriorityViewId, prefetchBias, frameIndex);

                evictionCount += residencyResult.EvictionCount;
                pendingMipGapSum += residencyResult.PendingMipGapSum;
                pendingMipGapMax = Mathf.Max(pendingMipGapMax, residencyResult.PendingMipGapMax);
                pendingMipGapSampleCount += residencyResult.PendingMipGapSampleCount;
                prefetchRequestCount += residencyResult.PrefetchRequestCount;
                addressSpace.CollectPendingUploads(s_UploadScheduler, cmd);
            }

            s_UploadScheduler.FinalizeUploads(cmd);
            int inFlightUploadBatchCount = s_UploadScheduler.InFlightBatchCount;
            int duplicateUploadCount = s_UploadScheduler.LastDuplicateUploadCount;
            int skippedUploadCount = s_UploadScheduler.LastSkippedUploadCount;
            foreach (KeyValuePair<int, VTAddressSpace> pair in s_AddressSpaces)
            {
                pair.Value.RebuildPageTableIfDirty();
                pair.Value.RefreshPageTableBuffer();
            }

            ClearGroupedRequests();
            s_AggregatedRequests.Clear();
            s_CompletedReadbacks.Clear();
            s_PrefetchBiasBySpace.Clear();

            bool supportsFeedback = IsFeedbackSupported(camera);
            VirtualTextureFeedbackCameraState cameraFeedbackState = supportsFeedback
                ? s_FeedbackCameraSystem.GetOrCreateBase(camera)
                : null;

            int residentPageCount = 0;
            int freePageCount = 0;
            int pendingUploadCount = 0;
            int feedbackCapacity = 0;
            string statusMessage = s_AddressSpaces.Count == 0 ? "[VividRP] VT has no registered spaces." : string.Empty;

            foreach (KeyValuePair<int, VTAddressSpace> pair in s_AddressSpaces)
            {
                VTAddressSpace addressSpace = pair.Value;
                ComputeBuffer feedbackRequests = null;
                ComputeBuffer feedbackCounter = null;
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

                residentPageCount += addressSpace.ResidentPageCount;
                freePageCount += addressSpace.FreePageCount;
                pendingUploadCount += addressSpace.PendingRequestCount;
                virtualTextureFrameData?.AddBinding(addressSpace.CreateBinding(feedbackRequests, feedbackCounter));
            }

            VTPhysicalPoolStats physicalPoolStats = CollectPhysicalPoolStats();
            residentPageCount = physicalPoolStats.ResidentPageCount;
            freePageCount = physicalPoolStats.FreePageCount;

            var globalStats = new VTDebugStats(
                s_AddressSpaces.Count,
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
                prefetchRequestCount);
            VirtualTextureStatsRegistry.Report(globalStats);

            if (camera != null)
            {
                var viewStats = new VTDebugStats(
                    s_AddressSpaces.Count,
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
                    prefetchRequestCount);
                VirtualTextureStatsRegistry.ReportView(viewStats);
            }
        }

        internal static bool TryGetPendingRequests(int spaceId, out IReadOnlyList<VTRequest> requests)
        {
            if (s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace))
            {
                requests = addressSpace.PendingRequests;
                return true;
            }

            requests = Array.Empty<VTRequest>();
            return false;
        }

        internal static bool TryGetPendingUploadRequests(int spaceId, out IReadOnlyList<VirtualTextureUploadRequest> requests)
        {
            if (s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace))
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
            return s_AddressSpaces.TryGetValue(request.SpaceId, out VTAddressSpace addressSpace)
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
            foreach (KeyValuePair<int, VTAddressSpace> pair in s_AddressSpaces)
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
                foreach (KeyValuePair<int, VTAddressSpace> pair in s_AddressSpaces)
                    pair.Value.RebuildPageTableIfDirty();
            }

            return flushedCount;
        }

        internal static int FlushRegion(int spaceId, int mip, RectInt pageRegion)
        {
            if (!s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace))
                return 0;

            s_UploadScheduler.CancelUploadsForSpace(spaceId);
            return addressSpace.FlushRegion(mip, pageRegion);
        }

        internal static bool SetPageLocked(int spaceId, in VirtualTexturePageCoord coord, bool locked = true)
        {
            return s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace)
                   && addressSpace.TrySetPageLocked(coord, locked);
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
            if (s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace))
                return addressSpace.TryGetPageTableEntry(coord, out entry);

            entry = default;
            return false;
        }

        internal static int GetResidentPageCountForTesting(int spaceId)
        {
            return s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace)
                ? addressSpace.ResidentPageCount
                : 0;
        }

        internal static int GetFreePageCountForTesting(int spaceId)
        {
            return s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace)
                ? addressSpace.FreePageCount
                : 0;
        }

        internal static int GetPendingUploadCountForTesting(int spaceId)
        {
            return s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace)
                ? addressSpace.PendingRequestCount
                : 0;
        }

        internal static int GetPhysicalPoolCountForTesting()
        {
            return s_PhysicalPools.Count;
        }

        internal static VTPhysicalPoolStats GetPhysicalPoolStatsForTesting()
        {
            return CollectPhysicalPoolStats();
        }

        internal static bool TryGetPhysicalCacheForTesting(int spaceId, out Texture2DArray physicalCache)
        {
            if (s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace))
            {
                physicalCache = addressSpace.PhysicalPool.Texture;
                return true;
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
            if (s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace))
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

        private sealed class UploadCommitterResolver : IVTUploadRequestCommitterResolver
        {
            public IVTUploadRequestCommitter ResolveCommitter(int spaceId)
            {
                return s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace)
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

        private static VTAddressSpace CreateAddressSpace(
            int allocationId,
            int spaceId,
            in VirtualTextureSpaceDesc desc,
            VTProducerHandle producerHandle)
        {
            if (!s_ProducerRegistry.TryGet(producerHandle, out VTRegisteredProducer producer))
                throw new ArgumentException($"[VividRP] VT producer handle '{producerHandle}' is not registered.");

            VTPhysicalPool physicalPool = AcquirePhysicalPool(desc);
            try
            {
                return new VTAddressSpace(allocationId, spaceId, desc, producer, physicalPool);
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
            VTAddressSpace addressSpace = CreateAddressSpace(
                allocationId,
                spaceId,
                desc.SpaceDesc,
                desc.ProducerHandle);

            var allocation = new VTAllocatedVirtualTexture(allocationId, spaceId, desc);
            s_AddressSpaces.Add(spaceId, addressSpace);
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

        private static void ReplaceAddressSpace(int spaceId, in VirtualTextureSpaceDesc desc, VTProducer producer)
        {
            if (!s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace existingAddressSpace))
                return;

            VTProducerHandle producerHandle = s_ProducerRegistry.Register(desc, producer);
            int allocationId = existingAddressSpace.AllocationId;
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
                s_AddressSpaces[spaceId] = CreateAddressSpace(allocationId, spaceId, desc, producerHandle);
            }
            catch
            {
                s_ProducerRegistry.Release(producerHandle);
                s_AddressSpaces.Remove(spaceId);
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

        private static void RemoveAddressSpace(int spaceId)
        {
            if (!s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace))
                return;

            s_AddressSpaces.Remove(spaceId);
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
            s_AggregatedRequests.RemoveAll(request => request.SpaceId == spaceId);
            s_GroupedRequests.Remove(spaceId);
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

        private static void CollectCompletedReadbacks(
            Camera activeCamera,
            VirtualTextureViewId activeViewId,
            CameraType activeCameraType,
            ref int lastReadbackFrame)
        {
            if (activeCamera != null)
            {
                if (s_FeedbackCameraSystem.TryGetBase(activeCamera, out VirtualTextureFeedbackCameraState activeCameraState))
                    CollectCompletedReadbacks(activeCameraState, ref lastReadbackFrame);
            }
            else
            {
                foreach (KeyValuePair<Camera, VirtualTextureFeedbackCameraState> cameraPair in s_FeedbackCameraSystem.EnumerateStates())
                    CollectCompletedReadbacks(cameraPair.Value, ref lastReadbackFrame);
            }

            for (int batchIndex = s_InjectedReadbacks.Count - 1; batchIndex >= 0; batchIndex--)
            {
                VirtualTextureFeedbackBatch batch = s_InjectedReadbacks[batchIndex];
                if (activeViewId.IsValid && !IsBatchFromView(batch, activeViewId, activeCameraType))
                    continue;

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

        private static void RemoveQueuedFeedbackForSpace(List<VirtualTextureFeedbackBatch> batches, int spaceId)
        {
            for (int batchIndex = batches.Count - 1; batchIndex >= 0; batchIndex--)
            {
                VirtualTextureFeedbackBatch batch = batches[batchIndex];
                int requestCount = Mathf.Min(batch.RequestCount, batch.Requests.Length);
                int keptCount = 0;
                ulong[] keptRequests = null;

                for (int requestIndex = 0; requestIndex < requestCount; requestIndex++)
                {
                    ulong key = batch.Requests[requestIndex];
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

        private static void GroupRequestsBySpace(IReadOnlyList<VirtualTextureAggregatedFeedbackRequest> aggregatedRequests)
        {
            ClearGroupedRequests();
            if (aggregatedRequests == null)
                return;

            for (int requestIndex = 0; requestIndex < aggregatedRequests.Count; requestIndex++)
            {
                VirtualTextureAggregatedFeedbackRequest request = aggregatedRequests[requestIndex];
                if (!s_AddressSpaces.TryGetValue(request.SpaceId, out VTAddressSpace addressSpace))
                    continue;

                if (!VirtualTextureSpaceUtility.IsCoordValid(addressSpace.Descriptor, request.PageCoord))
                    continue;

                if (!s_GroupedRequests.TryGetValue(request.SpaceId, out List<VirtualTextureAggregatedFeedbackRequest> requests))
                {
                    requests = new List<VirtualTextureAggregatedFeedbackRequest>();
                    s_GroupedRequests.Add(request.SpaceId, requests);
                }

                requests.Add(request);
            }
        }

        private static void ResolvePrefetchBiasBySpace(VirtualTextureViewId viewId)
        {
            s_PrefetchBiasBySpace.Clear();
            foreach (KeyValuePair<int, List<VirtualTextureAggregatedFeedbackRequest>> pair in s_GroupedRequests)
            {
                List<VirtualTextureAggregatedFeedbackRequest> requests = pair.Value;
                if (requests == null || requests.Count == 0)
                    continue;

                FeedbackMotionKey key = new(pair.Key, viewId);
                Vector2 centroid = ComputeFeedbackCentroid(requests);
                Vector2Int bias = Vector2Int.zero;
                if (s_FeedbackMotionStates.TryGetValue(key, out FeedbackMotionState previousState))
                    bias = QuantizePrefetchBias(centroid - previousState.Centroid);

                s_FeedbackMotionStates[key] = new FeedbackMotionState(centroid);
                if (bias != Vector2Int.zero)
                    s_PrefetchBiasBySpace[pair.Key] = bias;
            }
        }

        private static Vector2 ComputeFeedbackCentroid(IReadOnlyList<VirtualTextureAggregatedFeedbackRequest> requests)
        {
            Vector2 weightedSum = Vector2.zero;
            int totalWeight = 0;
            for (int requestIndex = 0; requestIndex < requests.Count; requestIndex++)
            {
                VirtualTextureAggregatedFeedbackRequest request = requests[requestIndex];
                int mipScale = 1 << Mathf.Clamp(request.PageCoord.Mip, 0, 20);
                int weight = Mathf.Max(1, request.HitCount);
                weightedSum += new Vector2(
                    (request.PageCoord.X + 0.5f) * mipScale,
                    (request.PageCoord.Y + 0.5f) * mipScale) * weight;
                totalWeight += weight;
            }

            return totalWeight > 0 ? weightedSum / totalWeight : Vector2.zero;
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

        private static void ClearGroupedRequests()
        {
            foreach (KeyValuePair<int, List<VirtualTextureAggregatedFeedbackRequest>> pair in s_GroupedRequests)
                pair.Value.Clear();
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

        private static int CountRequestsFromView(
            IReadOnlyList<VirtualTextureAggregatedFeedbackRequest> requests,
            VirtualTextureViewId viewId,
            CameraType cameraType)
        {
            if (requests == null)
                return 0;

            int count = 0;
            for (int requestIndex = 0; requestIndex < requests.Count; requestIndex++)
            {
                VirtualTextureAggregatedFeedbackRequest request = requests[requestIndex];
                if ((viewId.IsValid && request.ViewId.Equals(viewId))
                    || (!request.ViewId.IsValid && request.ViewId.CameraType == cameraType))
                {
                    count += 1;
                }
            }

            return count;
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
