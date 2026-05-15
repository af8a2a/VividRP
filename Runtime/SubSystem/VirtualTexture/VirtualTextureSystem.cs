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
            foreach (VTAddressSpace addressSpace in s_AddressSpaces.Values)
                addressSpace.Dispose();

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

            int lastReadbackFrame = -1;
            CollectCompletedReadbacks(ref lastReadbackFrame);

            int faultCount = 0;
            for (int batchIndex = 0; batchIndex < s_CompletedReadbacks.Count; batchIndex++)
                faultCount += s_CompletedReadbacks[batchIndex].RequestCount;

            VirtualTextureFeedbackProcessor.Aggregate(s_CompletedReadbacks, s_AggregationScratch, s_AggregatedRequests);
            int deduplicatedRequestCount = s_AggregatedRequests.Count;
            GroupRequestsBySpace(s_AggregatedRequests);

            int frameIndex = ResolveFrameIndex(frameData);
            int evictionCount = 0;
            foreach (VTAddressSpace addressSpace in s_AddressSpaces.Values)
            {
                if (s_GroupedRequests.TryGetValue(addressSpace.SpaceId, out List<VirtualTextureAggregatedFeedbackRequest> spaceRequests))
                    evictionCount += addressSpace.ProcessRequests(spaceRequests, frameIndex, cmd);
                else
                    evictionCount += addressSpace.ProcessRequests(null, frameIndex, cmd);

                addressSpace.RefreshPageTableBuffer();
            }

            ClearGroupedRequests();
            s_AggregatedRequests.Clear();
            s_CompletedReadbacks.Clear();

            VividCameraData cameraData = TryGetCameraData(frameData);
            Camera camera = cameraData?.camera;
            bool supportsFeedback = IsFeedbackSupported(camera);
            VirtualTextureFeedbackCameraState cameraFeedbackState = supportsFeedback
                ? s_FeedbackCameraSystem.GetOrCreateBase(camera)
                : null;

            int residentPageCount = 0;
            int freePageCount = 0;
            int pendingUploadCount = 0;
            string statusMessage = s_AddressSpaces.Count == 0 ? "[VividRP] VT has no registered spaces." : string.Empty;

            foreach (VTAddressSpace addressSpace in s_AddressSpaces.Values)
            {
                ComputeBuffer feedbackRequests = null;
                ComputeBuffer feedbackCounter = null;

                if (supportsFeedback && cameraFeedbackState != null)
                {
                    VirtualTextureFeedbackBufferState feedbackBufferState =
                        cameraFeedbackState.GetOrCreateSpaceState(addressSpace.SpaceId);
                    if (!feedbackBufferState.TryPrepareForFrame(
                            cmd,
                            addressSpace.Descriptor.SpaceName,
                            camera,
                            addressSpace.StackDesc.FeedbackCapacity,
                            frameIndex,
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

            VirtualTextureStatsRegistry.Report(new VTDebugStats(
                s_AddressSpaces.Count,
                residentPageCount,
                freePageCount,
                pendingUploadCount,
                evictionCount,
                faultCount,
                deduplicatedRequestCount,
                lastReadbackFrame,
                statusMessage));
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
                var uploadRequests = new VirtualTextureUploadRequest[pendingRequests.Count];
                for (int requestIndex = 0; requestIndex < pendingRequests.Count; requestIndex++)
                    uploadRequests[requestIndex] = new VirtualTextureUploadRequest(pendingRequests[requestIndex]);

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
            if (requestKeys == null || requestKeys.Length == 0)
                return;

            s_InjectedReadbacks.Add(new VirtualTextureFeedbackBatch(cameraType, requestKeys, requestKeys.Length, Time.frameCount));
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

        private static void CollectCompletedReadbacks(ref int lastReadbackFrame)
        {
            foreach (KeyValuePair<Camera, VirtualTextureFeedbackCameraState> cameraPair in s_FeedbackCameraSystem.EnumerateStates())
            {
                VirtualTextureFeedbackCameraState cameraState = cameraPair.Value;
                foreach (KeyValuePair<int, VirtualTextureFeedbackBufferState> spacePair in cameraState.EnumerateSpaceStates())
                    spacePair.Value.CollectCompletedReadbacks(s_CompletedReadbacks, ref lastReadbackFrame);
            }

            for (int batchIndex = 0; batchIndex < s_InjectedReadbacks.Count; batchIndex++)
            {
                VirtualTextureFeedbackBatch batch = s_InjectedReadbacks[batchIndex];
                s_CompletedReadbacks.Add(batch);
                lastReadbackFrame = Mathf.Max(lastReadbackFrame, batch.FrameIndex);
            }

            s_InjectedReadbacks.Clear();
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
            foreach (List<VirtualTextureAggregatedFeedbackRequest> requests in s_GroupedRequests.Values)
                requests.Clear();
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
