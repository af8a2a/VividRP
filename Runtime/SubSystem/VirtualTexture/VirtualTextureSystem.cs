using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class VirtualTextureSystem
    {
        private struct VirtualPageRuntimeState
        {
            public int PhysicalPageId;
            public int Generation;
            public int LastAllocationFrame;
            public bool Resident;
            public bool PendingUpload;
            public bool Locked;
        }

        private struct PhysicalPageSlotState
        {
            public int VirtualPageIndex;
            public int Generation;
            public int LastAllocationFrame;
        }

        private sealed class VirtualTextureSpaceState : IDisposable
        {
            private readonly VirtualPageRuntimeState[] m_PageStates;
            private readonly PhysicalPageSlotState[] m_PhysicalSlots;
            private readonly int[] m_BestPhysicalPageIds;
            private readonly int[] m_BestResolvedMips;
            private readonly Stack<int> m_FreePhysicalPages;
            private readonly LinkedList<int> m_LruPhysicalPages = new();
            private readonly LinkedListNode<int>[] m_LruNodes;
            private readonly List<VirtualTextureUploadRequest> m_PendingUploads = new();
            private readonly VirtualTexturePageTableEntry[] m_PageTableEntries;
            private readonly Texture2DArray m_PhysicalCache;
            private readonly GraphicsBuffer m_PageTableBuffer;
            private readonly int[] m_MipOffsets;
            private readonly VirtualTextureSpaceShaderParams m_ShaderParams;
            private readonly VirtualTextureSpaceDesc m_Desc;

            private int m_ResidentPageCount;
            private bool m_PageTableDirty;
            private int m_NextGeneration;

            internal VirtualTextureSpaceState(int spaceId, in VirtualTextureSpaceDesc desc)
            {
                SpaceId = spaceId;
                m_Desc = desc;
                m_MipOffsets = VirtualTextureSpaceUtility.BuildMipOffsets(desc.VirtualPageCountX, desc.VirtualPageCountY, desc.MipCount);
                TotalPageCount = VirtualTextureSpaceUtility.GetTotalPageCount(desc.VirtualPageCountX, desc.VirtualPageCountY, desc.MipCount);
                m_ShaderParams = new VirtualTextureSpaceShaderParams(spaceId, desc, TotalPageCount);

                m_PageStates = new VirtualPageRuntimeState[TotalPageCount];
                for (int pageIndex = 0; pageIndex < m_PageStates.Length; pageIndex++)
                    m_PageStates[pageIndex].PhysicalPageId = -1;

                m_PhysicalSlots = new PhysicalPageSlotState[desc.CachePageCount];
                for (int slotIndex = 0; slotIndex < m_PhysicalSlots.Length; slotIndex++)
                    m_PhysicalSlots[slotIndex].VirtualPageIndex = -1;

                m_BestPhysicalPageIds = new int[TotalPageCount];
                Array.Fill(m_BestPhysicalPageIds, -1);
                m_BestResolvedMips = new int[TotalPageCount];
                m_LruNodes = new LinkedListNode<int>[desc.CachePageCount];
                m_FreePhysicalPages = new Stack<int>(desc.CachePageCount);
                for (int slotIndex = desc.CachePageCount - 1; slotIndex >= 0; slotIndex--)
                    m_FreePhysicalPages.Push(slotIndex);

                m_PageTableEntries = new VirtualTexturePageTableEntry[TotalPageCount];
                for (int pageIndex = 0; pageIndex < m_PageTableEntries.Length; pageIndex++)
                    m_PageTableEntries[pageIndex] = VirtualTexturePageTableEntry.Invalid();

                m_PageTableBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, TotalPageCount, sizeof(uint))
                {
                    name = $"VividVT_{desc.SpaceName}_PageTable"
                };
                m_PhysicalCache = new Texture2DArray(
                    desc.PhysicalPageSize,
                    desc.PhysicalPageSize,
                    desc.CachePageCount,
                    desc.GraphicsFormat,
                    TextureCreationFlags.None)
                {
                    name = $"VividVT_{desc.SpaceName}_PhysicalCache",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                m_PhysicalCache.Apply(false, false);

                MarkPageTableDirty();
                RematerializePageTable();
                RefreshPageTableBuffer();
            }

            internal int SpaceId { get; }

            internal int TotalPageCount { get; }

            internal int ResidentPageCount => m_ResidentPageCount;

            internal int FreePageCount => m_FreePhysicalPages.Count;

            internal int PendingUploadCount => m_PendingUploads.Count;

            internal GraphicsBuffer PageTableBuffer => m_PageTableBuffer;

            internal Texture2DArray PhysicalCache => m_PhysicalCache;

            internal IReadOnlyList<VirtualTextureUploadRequest> PendingUploads => m_PendingUploads;

            internal int[] MipOffsets => m_MipOffsets;

            internal VirtualTextureSpaceShaderParams ShaderParams => m_ShaderParams;

            internal VirtualTextureSpaceDesc Descriptor => m_Desc;

            internal int ProcessRequests(IReadOnlyList<VirtualTextureAggregatedFeedbackRequest> requests, int frameIndex)
            {
                int evictionCount = 0;
                int allocatedThisFrame = 0;
                bool anyPageTableChange = false;

                if (requests == null)
                    return evictionCount;

                for (int requestIndex = 0; requestIndex < requests.Count; requestIndex++)
                {
                    VirtualTextureAggregatedFeedbackRequest request = requests[requestIndex];
                    if (!VirtualTextureSpaceUtility.IsCoordValid(m_Desc, request.PageCoord))
                        continue;

                    int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(m_Desc, m_MipOffsets, request.PageCoord);
                    VirtualPageRuntimeState pageState = m_PageStates[pageIndex];
                    if (pageState.Resident)
                    {
                        TouchPhysicalPage(pageState.PhysicalPageId);
                        continue;
                    }

                    if (pageState.PendingUpload)
                    {
                        UpdatePendingUploadPriority(pageIndex, request.HitCount, frameIndex);
                        continue;
                    }

                    if (allocatedThisFrame >= m_Desc.MaxUploadsPerFrame)
                        continue;

                    if (!TryAllocatePhysicalPage(pageIndex, frameIndex, out int physicalPageId, out int generation, out bool evicted))
                        continue;

                    if (evicted)
                        evictionCount += 1;

                    pageState.PhysicalPageId = physicalPageId;
                    pageState.Generation = generation;
                    pageState.LastAllocationFrame = frameIndex;
                    pageState.PendingUpload = true;
                    pageState.Resident = false;
                    m_PageStates[pageIndex] = pageState;
                    m_PendingUploads.Add(new VirtualTextureUploadRequest(
                        SpaceId,
                        request.PageCoord,
                        physicalPageId,
                        generation,
                        request.HitCount,
                        frameIndex));
                    allocatedThisFrame += 1;
                    anyPageTableChange = true;
                }

                if (anyPageTableChange)
                    RematerializePageTable();

                return evictionCount;
            }

            internal bool TryCommitUpload(in VirtualTextureUploadRequest request)
            {
                if (request.SpaceId != SpaceId || !VirtualTextureSpaceUtility.IsCoordValid(m_Desc, request.PageCoord))
                    return false;

                int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(m_Desc, m_MipOffsets, request.PageCoord);
                VirtualPageRuntimeState pageState = m_PageStates[pageIndex];
                if (!pageState.PendingUpload
                    || pageState.PhysicalPageId != request.PhysicalPageId
                    || pageState.Generation != request.Generation)
                {
                    return false;
                }

                pageState.PendingUpload = false;
                pageState.Resident = true;
                m_PageStates[pageIndex] = pageState;
                m_ResidentPageCount += 1;
                RemovePendingUpload(pageIndex, request.Generation);
                TouchPhysicalPage(pageState.PhysicalPageId);
                RematerializePageTable();
                return true;
            }

            internal bool TrySetPageLocked(in VirtualTexturePageCoord coord, bool locked)
            {
                if (!VirtualTextureSpaceUtility.IsCoordValid(m_Desc, coord))
                    return false;

                int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(m_Desc, m_MipOffsets, coord);
                VirtualPageRuntimeState pageState = m_PageStates[pageIndex];
                if (pageState.Locked == locked)
                    return true;

                pageState.Locked = locked;
                m_PageStates[pageIndex] = pageState;
                RematerializePageTable();
                return true;
            }

            internal bool TryGetPageTableEntry(in VirtualTexturePageCoord coord, out VirtualTexturePageTableEntry entry)
            {
                if (!VirtualTextureSpaceUtility.IsCoordValid(m_Desc, coord))
                {
                    entry = default;
                    return false;
                }

                int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(m_Desc, m_MipOffsets, coord);
                entry = m_PageTableEntries[pageIndex];
                return true;
            }

            internal void RefreshPageTableBuffer()
            {
                if (!m_PageTableDirty)
                    return;

                m_PageTableBuffer.SetData(m_PageTableEntries);
                m_PageTableDirty = false;
            }

            internal VirtualTextureSpaceBinding CreateBinding(
                GraphicsBuffer feedbackRequests,
                GraphicsBuffer feedbackCounter)
            {
                return new VirtualTextureSpaceBinding(
                    SpaceId,
                    m_Desc.SpaceName,
                    m_PageTableBuffer,
                    m_PhysicalCache,
                    feedbackRequests,
                    feedbackCounter,
                    m_ShaderParams,
                    m_MipOffsets);
            }

            public void Dispose()
            {
                m_PageTableBuffer?.Dispose();
                if (m_PhysicalCache != null)
                    CoreUtils.Destroy(m_PhysicalCache);

                m_PendingUploads.Clear();
                m_LruPhysicalPages.Clear();
                m_FreePhysicalPages.Clear();
            }

            private bool TryAllocatePhysicalPage(
                int pageIndex,
                int frameIndex,
                out int physicalPageId,
                out int generation,
                out bool evicted)
            {
                physicalPageId = -1;
                generation = 0;
                evicted = false;

                if (m_FreePhysicalPages.Count > 0)
                {
                    physicalPageId = m_FreePhysicalPages.Pop();
                }
                else
                {
                    physicalPageId = FindEvictionCandidate(frameIndex);
                    if (physicalPageId < 0)
                        return false;

                    evicted = EvictPhysicalPage(physicalPageId);
                }

                generation = ++m_NextGeneration;
                PhysicalPageSlotState slotState = m_PhysicalSlots[physicalPageId];
                slotState.VirtualPageIndex = pageIndex;
                slotState.Generation = generation;
                slotState.LastAllocationFrame = frameIndex;
                m_PhysicalSlots[physicalPageId] = slotState;
                TouchPhysicalPage(physicalPageId);
                return true;
            }

            private bool EvictPhysicalPage(int physicalPageId)
            {
                PhysicalPageSlotState slotState = m_PhysicalSlots[physicalPageId];
                if (slotState.VirtualPageIndex < 0)
                    return false;

                VirtualPageRuntimeState pageState = m_PageStates[slotState.VirtualPageIndex];
                if (pageState.Resident)
                    m_ResidentPageCount -= 1;

                pageState.Resident = false;
                pageState.PendingUpload = false;
                pageState.PhysicalPageId = -1;
                m_PageStates[slotState.VirtualPageIndex] = pageState;
                RemovePendingUpload(slotState.VirtualPageIndex, slotState.Generation);

                slotState.VirtualPageIndex = -1;
                slotState.LastAllocationFrame = -1;
                m_PhysicalSlots[physicalPageId] = slotState;
                return true;
            }

            private int FindEvictionCandidate(int frameIndex)
            {
                LinkedListNode<int> node = m_LruPhysicalPages.First;
                while (node != null)
                {
                    int physicalPageId = node.Value;
                    if (CanEvict(physicalPageId, frameIndex))
                        return physicalPageId;

                    node = node.Next;
                }

                return -1;
            }

            private bool CanEvict(int physicalPageId, int frameIndex)
            {
                if (physicalPageId < 0 || physicalPageId >= m_PhysicalSlots.Length)
                    return false;

                PhysicalPageSlotState slotState = m_PhysicalSlots[physicalPageId];
                if (slotState.VirtualPageIndex < 0 || slotState.LastAllocationFrame == frameIndex)
                    return false;

                VirtualPageRuntimeState pageState = m_PageStates[slotState.VirtualPageIndex];
                return !pageState.PendingUpload && !pageState.Locked;
            }

            private void TouchPhysicalPage(int physicalPageId)
            {
                if (physicalPageId < 0 || physicalPageId >= m_LruNodes.Length)
                    return;

                LinkedListNode<int> node = m_LruNodes[physicalPageId];
                if (node == null)
                {
                    node = new LinkedListNode<int>(physicalPageId);
                    m_LruNodes[physicalPageId] = node;
                    m_LruPhysicalPages.AddLast(node);
                    return;
                }

                if (node.List != null && node != m_LruPhysicalPages.Last)
                {
                    m_LruPhysicalPages.Remove(node);
                    m_LruPhysicalPages.AddLast(node);
                }
            }

            private void UpdatePendingUploadPriority(int pageIndex, int priority, int frameIndex)
            {
                for (int requestIndex = 0; requestIndex < m_PendingUploads.Count; requestIndex++)
                {
                    VirtualTextureUploadRequest request = m_PendingUploads[requestIndex];
                    if (VirtualTextureSpaceUtility.GetFlatIndex(m_Desc, m_MipOffsets, request.PageCoord) != pageIndex)
                        continue;

                    if (priority <= request.Priority)
                        return;

                    m_PendingUploads[requestIndex] = new VirtualTextureUploadRequest(
                        request.SpaceId,
                        request.PageCoord,
                        request.PhysicalPageId,
                        request.Generation,
                        priority,
                        Mathf.Min(request.RequestFrame, frameIndex));
                    return;
                }
            }

            private void RemovePendingUpload(int pageIndex, int generation)
            {
                for (int requestIndex = m_PendingUploads.Count - 1; requestIndex >= 0; requestIndex--)
                {
                    VirtualTextureUploadRequest request = m_PendingUploads[requestIndex];
                    if (request.Generation != generation)
                        continue;

                    if (VirtualTextureSpaceUtility.GetFlatIndex(m_Desc, m_MipOffsets, request.PageCoord) != pageIndex)
                        continue;

                    m_PendingUploads.RemoveAt(requestIndex);
                    return;
                }
            }

            private void RematerializePageTable()
            {
                for (int mip = m_Desc.MipCount - 1; mip >= 0; mip--)
                {
                    int mipWidth = VirtualTextureSpaceUtility.GetPageCountX(m_Desc.VirtualPageCountX, mip);
                    int mipHeight = VirtualTextureSpaceUtility.GetPageCountY(m_Desc.VirtualPageCountY, mip);
                    int mipOffset = m_MipOffsets[mip];
                    int parentWidth = mip < m_Desc.MipCount - 1
                        ? VirtualTextureSpaceUtility.GetPageCountX(m_Desc.VirtualPageCountX, mip + 1)
                        : 0;
                    int parentOffset = mip < m_Desc.MipCount - 1 ? m_MipOffsets[mip + 1] : 0;

                    for (int y = 0; y < mipHeight; y++)
                    {
                        for (int x = 0; x < mipWidth; x++)
                        {
                            int pageIndex = mipOffset + y * mipWidth + x;
                            VirtualPageRuntimeState pageState = m_PageStates[pageIndex];

                            bool hasBestMapping = false;
                            int bestPhysicalPageId = -1;
                            int bestResolvedMip = 0;
                            VirtualTexturePageTableEntry entry;

                            if (pageState.Resident)
                            {
                                hasBestMapping = true;
                                bestPhysicalPageId = pageState.PhysicalPageId;
                                bestResolvedMip = mip;
                                entry = new VirtualTexturePageTableEntry(
                                    bestPhysicalPageId,
                                    bestResolvedMip,
                                    true,
                                    false,
                                    false,
                                    pageState.Locked);
                            }
                            else
                            {
                                if (mip < m_Desc.MipCount - 1)
                                {
                                    int parentIndex = parentOffset + (y >> 1) * parentWidth + (x >> 1);
                                    if (m_BestPhysicalPageIds[parentIndex] >= 0)
                                    {
                                        hasBestMapping = true;
                                        bestPhysicalPageId = m_BestPhysicalPageIds[parentIndex];
                                        bestResolvedMip = m_BestResolvedMips[parentIndex];
                                    }
                                }

                                entry = hasBestMapping
                                    ? new VirtualTexturePageTableEntry(
                                        bestPhysicalPageId,
                                        bestResolvedMip,
                                        false,
                                        true,
                                        pageState.PendingUpload,
                                        pageState.Locked)
                                    : VirtualTexturePageTableEntry.Invalid(pageState.PendingUpload, pageState.Locked);
                            }

                            m_PageTableEntries[pageIndex] = entry;
                            m_BestPhysicalPageIds[pageIndex] = hasBestMapping ? bestPhysicalPageId : -1;
                            m_BestResolvedMips[pageIndex] = hasBestMapping ? bestResolvedMip : 0;
                        }
                    }
                }

                MarkPageTableDirty();
            }

            private void MarkPageTableDirty()
            {
                m_PageTableDirty = true;
            }
        }

        private static readonly Dictionary<int, VirtualTextureSpaceState> s_Spaces = new();
        private static readonly Dictionary<string, int> s_SpaceIdsByName = new(StringComparer.Ordinal);
        private static readonly VirtualTextureFeedbackCameraSystem s_FeedbackCameraSystem = new();
        private static readonly List<VirtualTextureFeedbackBatch> s_CompletedReadbacks = new();
        private static readonly List<VirtualTextureFeedbackBatch> s_InjectedReadbacks = new();
        private static readonly Dictionary<int, List<VirtualTextureAggregatedFeedbackRequest>> s_GroupedRequests = new();

        private static bool s_Initialized;
        private static int s_NextSpaceId = 1;
        private static int s_FallbackFrameIndex = -1;

        internal static void Initialize()
        {
            if (s_Initialized)
                return;

            FrameContextSystem.SubsystemPreRender -= Update;
            FrameContextSystem.SubsystemPreRender += Update;
            s_Initialized = true;
        }

        internal static void Deinitialize()
        {
            if (!s_Initialized)
            {
                VirtualTextureStatsRegistry.Clear();
                return;
            }

            FrameContextSystem.SubsystemPreRender -= Update;

            foreach (VirtualTextureSpaceState state in s_Spaces.Values)
                state.Dispose();

            s_Spaces.Clear();
            s_SpaceIdsByName.Clear();
            s_CompletedReadbacks.Clear();
            s_InjectedReadbacks.Clear();
            s_GroupedRequests.Clear();
            s_FeedbackCameraSystem.Dispose();
            s_NextSpaceId = 1;
            s_FallbackFrameIndex = -1;
            s_Initialized = false;
            VirtualTextureStatsRegistry.Clear();
        }

        internal static int RegisterSpace(in VirtualTextureSpaceDesc desc)
        {
            Initialize();

            if (s_SpaceIdsByName.TryGetValue(desc.SpaceName, out int existingSpaceId))
            {
                VirtualTextureSpaceState existingState = s_Spaces[existingSpaceId];
                if (!existingState.Descriptor.Equals(desc))
                {
                    throw new InvalidOperationException(
                        $"[VividRP] VT space '{desc.SpaceName}' is already registered with a different descriptor.");
                }

                return existingSpaceId;
            }

            int spaceId = s_NextSpaceId++;
            var spaceState = new VirtualTextureSpaceState(spaceId, desc);
            s_Spaces.Add(spaceId, spaceState);
            s_SpaceIdsByName.Add(desc.SpaceName, spaceId);
            return spaceId;
        }

        internal static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            if (!s_Initialized)
                Initialize();

            var virtualTextureFrameData = frameData?.GetOrCreate<VividVirtualTextureFrameData>();
            virtualTextureFrameData?.Reset();
            s_FeedbackCameraSystem.PurgeDestroyedCameras();

            int lastReadbackFrame = -1;
            CollectCompletedReadbacks(ref lastReadbackFrame);

            int faultCount = 0;
            for (int batchIndex = 0; batchIndex < s_CompletedReadbacks.Count; batchIndex++)
                faultCount += s_CompletedReadbacks[batchIndex].RequestCount;

            List<VirtualTextureAggregatedFeedbackRequest> aggregatedRequests =
                VirtualTextureFeedbackProcessor.Aggregate(s_CompletedReadbacks);
            int deduplicatedRequestCount = aggregatedRequests.Count;
            GroupRequestsBySpace(aggregatedRequests);

            int frameIndex = ResolveFrameIndex(frameData);
            int evictionCount = 0;
            foreach (VirtualTextureSpaceState spaceState in s_Spaces.Values)
            {
                if (s_GroupedRequests.TryGetValue(spaceState.SpaceId, out List<VirtualTextureAggregatedFeedbackRequest> spaceRequests))
                    evictionCount += spaceState.ProcessRequests(spaceRequests, frameIndex);

                spaceState.RefreshPageTableBuffer();
            }

            s_GroupedRequests.Clear();
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
            string statusMessage = s_Spaces.Count == 0 ? "[VividRP] VT has no registered spaces." : string.Empty;

            foreach (VirtualTextureSpaceState spaceState in s_Spaces.Values)
            {
                GraphicsBuffer feedbackRequests = null;
                GraphicsBuffer feedbackCounter = null;

                if (supportsFeedback && cameraFeedbackState != null)
                {
                    VirtualTextureFeedbackBufferState feedbackBufferState =
                        cameraFeedbackState.GetOrCreateSpaceState(spaceState.SpaceId);
                    if (!feedbackBufferState.TryPrepareForFrame(
                            cmd,
                            spaceState.Descriptor.SpaceName,
                            camera,
                            spaceState.Descriptor.FeedbackCapacity,
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

                residentPageCount += spaceState.ResidentPageCount;
                freePageCount += spaceState.FreePageCount;
                pendingUploadCount += spaceState.PendingUploadCount;
                virtualTextureFrameData?.AddBinding(spaceState.CreateBinding(feedbackRequests, feedbackCounter));
            }

            VirtualTextureStatsRegistry.Report(new VirtualTextureStats(
                s_Spaces.Count,
                residentPageCount,
                freePageCount,
                pendingUploadCount,
                evictionCount,
                faultCount,
                deduplicatedRequestCount,
                lastReadbackFrame,
                statusMessage));
        }

        internal static bool TryGetPendingUploadRequests(int spaceId, out IReadOnlyList<VirtualTextureUploadRequest> requests)
        {
            if (s_Spaces.TryGetValue(spaceId, out VirtualTextureSpaceState state))
            {
                requests = state.PendingUploads;
                return true;
            }

            requests = Array.Empty<VirtualTextureUploadRequest>();
            return false;
        }

        internal static bool CommitUpload(in VirtualTextureUploadRequest request)
        {
            return s_Spaces.TryGetValue(request.SpaceId, out VirtualTextureSpaceState state) && state.TryCommitUpload(request);
        }

        internal static bool SetPageLocked(int spaceId, in VirtualTexturePageCoord coord, bool locked = true)
        {
            return s_Spaces.TryGetValue(spaceId, out VirtualTextureSpaceState state) && state.TrySetPageLocked(coord, locked);
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
            if (s_Spaces.TryGetValue(spaceId, out VirtualTextureSpaceState state))
                return state.TryGetPageTableEntry(coord, out entry);

            entry = default;
            return false;
        }

        internal static int GetResidentPageCountForTesting(int spaceId)
        {
            return s_Spaces.TryGetValue(spaceId, out VirtualTextureSpaceState state) ? state.ResidentPageCount : 0;
        }

        internal static int GetFreePageCountForTesting(int spaceId)
        {
            return s_Spaces.TryGetValue(spaceId, out VirtualTextureSpaceState state) ? state.FreePageCount : 0;
        }

        internal static int GetPendingUploadCountForTesting(int spaceId)
        {
            return s_Spaces.TryGetValue(spaceId, out VirtualTextureSpaceState state) ? state.PendingUploadCount : 0;
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
            s_GroupedRequests.Clear();
            if (aggregatedRequests == null)
                return;

            for (int requestIndex = 0; requestIndex < aggregatedRequests.Count; requestIndex++)
            {
                VirtualTextureAggregatedFeedbackRequest request = aggregatedRequests[requestIndex];
                if (!s_Spaces.TryGetValue(request.SpaceId, out VirtualTextureSpaceState spaceState))
                    continue;

                if (!VirtualTextureSpaceUtility.IsCoordValid(spaceState.Descriptor, request.PageCoord))
                    continue;

                if (!s_GroupedRequests.TryGetValue(request.SpaceId, out List<VirtualTextureAggregatedFeedbackRequest> requests))
                {
                    requests = new List<VirtualTextureAggregatedFeedbackRequest>();
                    s_GroupedRequests.Add(request.SpaceId, requests);
                }

                requests.Add(request);
            }
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
