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
        private static readonly VirtualTextureFeedbackCameraSystem s_FeedbackCameraSystem = new();
        private static readonly List<VirtualTextureFeedbackBatch> s_CompletedReadbacks = new();
        private static readonly List<VirtualTextureFeedbackBatch> s_InjectedReadbacks = new();
        private static readonly VirtualTextureFeedbackProcessor.Scratch s_AggregationScratch = new();
        private static readonly List<VirtualTextureAggregatedFeedbackRequest> s_AggregatedRequests = new();
        private static readonly Dictionary<int, List<VirtualTextureAggregatedFeedbackRequest>> s_GroupedRequests = new();

        private static int s_NextSpaceId = 1;
        private static int s_FallbackFrameIndex = -1;

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

            s_AddressSpaces.Clear();
            s_SpaceIdsByName.Clear();
            s_CompletedReadbacks.Clear();
            s_InjectedReadbacks.Clear();
            s_AggregatedRequests.Clear();
            ClearGroupedRequests();
            s_GroupedRequests.Clear();
            s_FeedbackCameraSystem.Dispose();
            s_NextSpaceId = 1;
            s_FallbackFrameIndex = -1;
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

                return existingSpaceId;
            }

            int spaceId = s_NextSpaceId++;
            var addressSpace = new VTAddressSpace(spaceId, desc, producer);
            s_AddressSpaces.Add(spaceId, addressSpace);
            s_SpaceIdsByName.Add(desc.SpaceName, spaceId);
            return spaceId;
        }

        internal static int RegisterOrReconfigureAddressSpace(in VirtualTextureSpaceDesc desc, VTProducer producer)
        {
            Initialize();

            if (s_SpaceIdsByName.TryGetValue(desc.SpaceName, out int existingSpaceId))
            {
                VTAddressSpace existingAddressSpace = s_AddressSpaces[existingSpaceId];
                VTProducer resolvedProducer = ResolveStoredProducer(producer);
                if (existingAddressSpace.Descriptor.Equals(desc)
                    && ReferenceEquals(existingAddressSpace.Producer, resolvedProducer))
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

            int frameIndex = ResolveFrameIndex(frameData);
            int evictionCount = 0;
            int inFlightUploadBatchCount = 0;
            int duplicateUploadCount = 0;
            int skippedUploadCount = 0;
            foreach (KeyValuePair<int, VTAddressSpace> pair in s_AddressSpaces)
            {
                VTAddressSpace addressSpace = pair.Value;
                if (s_GroupedRequests.TryGetValue(addressSpace.SpaceId, out List<VirtualTextureAggregatedFeedbackRequest> spaceRequests))
                    evictionCount += addressSpace.ProcessRequests(spaceRequests, cachePriorityViewId, frameIndex, cmd);
                else
                    evictionCount += addressSpace.ProcessRequests(null, cachePriorityViewId, frameIndex, cmd);

                inFlightUploadBatchCount += addressSpace.InFlightUploadBatchCount;
                duplicateUploadCount += addressSpace.LastDuplicateUploadCount;
                skippedUploadCount += addressSpace.LastSkippedUploadCount;
                addressSpace.RefreshPageTableBuffer();
            }

            ClearGroupedRequests();
            s_AggregatedRequests.Clear();
            s_CompletedReadbacks.Clear();

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
                            addressSpace.PendingRequestCount > 0 || addressSpace.InFlightUploadBatchCount > 0,
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
                false);
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
                    true);
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
                producerName = addressSpace.Producer?.Name;
                return true;
            }

            producerName = null;
            return false;
        }

        internal static void SetUploadFenceFactoryForTesting(IVTUploadFenceFactory fenceFactory)
        {
            VTUploadScheduler.SetFenceFactoryForTesting(fenceFactory);
        }

        private static VTProducer ResolveStoredProducer(VTProducer producer)
        {
            return producer ?? VTNullProducer.Instance;
        }

        private static void ReplaceAddressSpace(int spaceId, in VirtualTextureSpaceDesc desc, VTProducer producer)
        {
            if (!s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace existingAddressSpace))
                return;

            existingAddressSpace.Dispose();
            RemoveFeedbackStateForSpace(spaceId);
            s_AddressSpaces[spaceId] = new VTAddressSpace(spaceId, desc, producer);
        }

        private static void RemoveAddressSpace(int spaceId)
        {
            if (!s_AddressSpaces.TryGetValue(spaceId, out VTAddressSpace addressSpace))
                return;

            s_AddressSpaces.Remove(spaceId);
            s_SpaceIdsByName.Remove(addressSpace.Descriptor.SpaceName);
            addressSpace.Dispose();
            RemoveFeedbackStateForSpace(spaceId);
        }

        private static void RemoveFeedbackStateForSpace(int spaceId)
        {
            s_FeedbackCameraSystem.RemoveSpaceState(spaceId);
            RemoveQueuedFeedbackForSpace(s_CompletedReadbacks, spaceId);
            RemoveQueuedFeedbackForSpace(s_InjectedReadbacks, spaceId);
            s_AggregatedRequests.RemoveAll(request => request.SpaceId == spaceId);
            s_GroupedRequests.Remove(spaceId);
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
