using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal readonly struct VTPendingUploadCandidate
    {
        internal VTPendingUploadCandidate(
            VTPageTableSpace addressSpace,
            in VTRequest request,
            bool locked,
            int fairnessRank)
        {
            AddressSpace = addressSpace;
            Request = request;
            Locked = locked;
            FairnessRank = fairnessRank;
        }

        internal VTPageTableSpace AddressSpace { get; }

        internal VTRequest Request { get; }

        internal bool Locked { get; }

        internal int FairnessRank { get; }
    }

    internal sealed class VTPageTableSpace : IDisposable, IVTUploadRequestCommitter
    {
        private readonly int[] m_MipOffsets;
        private readonly VTResidencyManager m_ResidencyManager;
        private readonly VTPageTableUpdater m_PageTableUpdater;
        private readonly VirtualTextureSpaceShaderParams m_ShaderParams;
        private readonly Vector4[] m_LayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];
        private readonly IVTPageProducer m_PageProducer;
        private readonly List<VTPendingUploadCandidate> m_LocalUploadCandidates = new();
        private readonly List<IVTPageProducerTask> m_ProducerTasks = new();
        private readonly List<PendingUploadSortEntry> m_PendingUploadSortEntries = new();
        private readonly List<VTRequest> m_SortedPendingRequests = new();
        private readonly List<VTRequest> m_EligiblePendingRequests = new();
        private readonly List<VTRequest> m_ResidentRefreshRequests = new();
        private readonly List<VTRequest> m_LiveProducerRequests = new();
        private uint m_CachedPendingRequestRevision;
        private int m_PendingOrderCacheBuildCount;
        private int m_PendingOrderCacheHitCount;
        private bool m_HasPendingOrderCache;
        private Texture2DArray m_ResidentPageStagingTexture;
        private readonly Texture2D[] m_ResidentPageConvertedStagingTextures =
            new Texture2D[VTStackDesc.MaxLayerCount];
        private readonly Texture2DArray[] m_ResidentPageEncodedStagingTextures =
            new Texture2DArray[VTStackDesc.MaxLayerCount];
        private Color32[] m_ResidentPageScratchPixels;
        private IVTPageProducer m_FallbackResidentPageProducer;

        internal VTPageTableSpace(
            int spaceId,
            in VirtualTextureSpaceDesc desc,
            in VTRegisteredProducer producer,
            VTPhysicalPool physicalPool)
        {
            SpaceId = spaceId;
            Descriptor = desc;
            ProducerHandle = producer.Handle;
            ProducerName = producer.Name;
            m_MipOffsets = VirtualTextureSpaceUtility.BuildMipOffsets(desc.VirtualPageCountX, desc.VirtualPageCountY, desc.MipCount);
            TotalPageCount = desc.PageTableEntryCount;
            m_ShaderParams = new VirtualTextureSpaceShaderParams(spaceId, desc, TotalPageCount);
            BuildLayerFallbacks(desc, m_LayerFallbacks);
            PhysicalPool = physicalPool ?? throw new ArgumentNullException(nameof(physicalPool));
            m_ResidencyManager = new VTResidencyManager(
                spaceId,
                ProducerHandle,
                ProducerName,
                desc.SpaceName,
                desc,
                TotalPageCount,
                m_MipOffsets,
                PhysicalPool);
            m_PageTableUpdater = new VTPageTableUpdater(desc.SpaceName, TotalPageCount);
            m_PageProducer = producer.PageProducer;
            BootstrapLowestMip(frameIndex: 0);
            m_PageTableUpdater.Rebuild(desc, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
            m_PageTableUpdater.RefreshBufferImmediate();
        }

        internal int SpaceId { get; }

        internal VTProducerHandle ProducerHandle { get; }

        internal string ProducerName { get; }

        internal VirtualTextureSpaceDesc Descriptor { get; }

        internal VTPhysicalPool PhysicalPool { get; }

        internal VTStackDesc StackDesc => Descriptor.StackDesc;

        internal int TotalPageCount { get; }

        internal int ResidentPageCount => m_ResidencyManager.ResidentPageCount;

        internal int FreePageCount => m_ResidencyManager.FreePageCount;

        internal int PendingRequestCount => m_ResidencyManager.PendingRequestCount;

        internal uint PendingRequestRevision => m_ResidencyManager.PendingRequestRevision;

        internal int PendingOrderCacheBuildCount => m_PendingOrderCacheBuildCount;

        internal int PendingOrderCacheHitCount => m_PendingOrderCacheHitCount;

        internal int PageTableRebuildCount => m_PageTableUpdater.RebuildCount;

        internal int PageTableLastRecomputedEntryCount => m_PageTableUpdater.LastRecomputedEntryCount;

        internal int PageTableLastUploadedEntryCount => m_PageTableUpdater.LastUploadedEntryCount;

        internal int PageTableSparseUploadCount => m_PageTableUpdater.SparseUploadCount;

        internal int PageTableFullUploadCount => m_PageTableUpdater.FullUploadCount;

        internal int PageTableScatterUploadCount => m_PageTableUpdater.ScatterUploadCount;

        internal int PageTableLegacySetDataCallCount => m_PageTableUpdater.LegacySetDataCallCount;

        internal int PageTablePendingUploadEntryCount => m_PageTableUpdater.PendingUploadEntryCount;

        internal GraphicsBuffer PageTableBuffer => m_PageTableUpdater.PageTableBuffer;

        internal VTPageTableUpdater PageTableUpdater => m_PageTableUpdater;

        internal IReadOnlyList<VTRequest> PendingRequests => m_ResidencyManager.PendingRequests;

        internal int ResidencyClassificationCapacity => m_ResidencyManager.ClassificationCapacity;

        internal bool LastResidencyClassificationUsedParallelJob =>
            m_ResidencyManager.LastClassificationUsedParallelJob;

        internal int[] MipOffsets => m_MipOffsets;

        internal bool RequiresNewPhysicalPage(in VirtualTexturePageCoord coord)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(Descriptor, coord))
                return false;

            int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(
                Descriptor,
                m_MipOffsets,
                coord);
            VTPageResidencyState pageState = m_ResidencyManager.GetPageState(pageIndex);
            return !pageState.Resident && !pageState.PendingUpload;
        }

        internal VTResidencyProcessResult ProcessRequests(
            NativeSlice<VirtualTextureAggregatedFeedbackRequest> requests,
            VirtualTextureViewId activeViewId,
            Vector2Int prefetchBias,
            int frameIndex,
            int maxNewRequests = int.MaxValue,
            bool allowNeighborPrefetch = true,
            bool rebuildPageTable = true)
        {
            VTResidencyProcessResult result = m_ResidencyManager.ProcessRequests(
                Descriptor,
                m_MipOffsets,
                SpaceId,
                requests,
                activeViewId,
                prefetchBias,
                frameIndex,
                maxNewRequests,
                allowNeighborPrefetch);

            if (!rebuildPageTable)
                return result;

            if (result.PageTableChanged)
            {
                m_ResidencyManager.ConsumePageTableDirtyFlag();
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTexturePageTableRebuildMarker.Auto())
                    m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
                m_ResidencyManager.ClearDirtyPageTableUpdates();
            }
            else
            {
                RebuildPageTableIfDirty();
            }

            return result;
        }

        internal void CollectPendingUploads(VTUploadScheduler uploadScheduler, CommandBuffer cmd)
        {
            m_LocalUploadCandidates.Clear();
            CollectPendingUploadCandidates(uploadScheduler, fairnessRank: 0, m_LocalUploadCandidates);

            int skippedUploadCount = 0;
            for (int candidateIndex = 0; candidateIndex < m_LocalUploadCandidates.Count; candidateIndex++)
            {
                if (!TrySchedulePendingUpload(uploadScheduler, cmd, m_LocalUploadCandidates[candidateIndex].Request))
                    skippedUploadCount += 1;
            }

            uploadScheduler?.AddSkippedUploadCount(skippedUploadCount);
            m_LocalUploadCandidates.Clear();
        }

        internal bool TryCommitRequest(in VTRequest request)
        {
            return TryCommitRequestInternal(request, rebuildPageTable: true);
        }

        internal bool TrySetPageLocked(in VirtualTexturePageCoord coord, bool locked)
        {
            if (!m_ResidencyManager.TrySetPageLocked(Descriptor, m_MipOffsets, coord, locked))
                return false;

            m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
            return true;
        }

        internal bool TryQueuePageResident(
            in VirtualTexturePageCoord coord,
            bool locked,
            int frameIndex)
        {
            if (!m_ResidencyManager.TryQueuePageResident(
                    Descriptor,
                    m_MipOffsets,
                    SpaceId,
                    coord,
                    locked,
                    frameIndex))
            {
                return false;
            }

            return true;
        }

        internal bool TryMakePageResident(
            in VirtualTexturePageCoord coord,
            bool locked,
            int frameIndex)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(Descriptor, coord))
                return false;

            if (m_PageTableUpdater.TryGetEntry(Descriptor, m_MipOffsets, coord, out VirtualTexturePageTableEntry entry)
                && entry.Resident)
            {
                bool lockUpdated = m_ResidencyManager.TrySetPageLocked(
                    Descriptor,
                    m_MipOffsets,
                    coord,
                    locked);
                RebuildPageTable();
                return lockUpdated;
            }

            if (!m_ResidencyManager.TryAllocateResidentPage(
                    Descriptor,
                    m_MipOffsets,
                    SpaceId,
                    coord,
                    VirtualTextureViewId.Invalid,
                    frameIndex,
                    locked,
                    out VTRequest request))
            {
                return false;
            }

            try
            {
                if (!TryUploadResidentPage(request, allowFallbackProducer: false, out _))
                {
                    RollbackResidentPage(coord);
                    return false;
                }
            }
            catch
            {
                RollbackResidentPage(coord);
                throw;
            }

            m_ResidencyManager.TrySetPageLocked(Descriptor, m_MipOffsets, coord, locked);
            RebuildPageTable();
            return true;
        }

        internal bool TryGetPageTableEntry(in VirtualTexturePageCoord coord, out VirtualTexturePageTableEntry entry)
        {
            RebuildPageTableIfDirty();
            return m_PageTableUpdater.TryGetEntry(Descriptor, m_MipOffsets, coord, out entry);
        }

        internal int CopyPendingPageTableUpdates(
            VTPageTableScatterUpdate[] destination,
            int destinationStartIndex,
            out int pendingVersion,
            out bool fullUpload)
        {
            return m_PageTableUpdater.CopyPendingUpdates(
                destination,
                destinationStartIndex,
                out pendingVersion,
                out fullUpload);
        }

        internal void RefreshPageTableBufferImmediately()
        {
            m_PageTableUpdater.RefreshBufferImmediate();
        }

        internal void AdvancePageTransitions(int frameIndex)
        {
            m_ResidencyManager.AdvancePageTransitions(frameIndex);
        }

        internal bool AdvancePageTransitionPhases(
            int frameIndex,
            int maxPhaseAdvancesThisCall)
        {
            return m_ResidencyManager.AdvancePageTransitions(
                frameIndex,
                maxPhaseAdvancesThisCall,
                maxTransitionStartsThisCall: 0);
        }

        internal bool StartQueuedPageTransitions(int frameIndex, int maxStartsThisCall)
        {
            return m_ResidencyManager.StartQueuedPageTransitionsOnly(
                frameIndex,
                maxStartsThisCall);
        }

        internal bool RebuildPageTableIfDirty(int frameIndex = -1)
        {
            m_ResidencyManager.AdvancePageTransitions(
                frameIndex,
                maxPhaseAdvancesThisCall: 0,
                maxTransitionStartsThisCall: 0);
            if (!m_ResidencyManager.ConsumePageTableDirtyFlag())
                return false;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTexturePageTableRebuildMarker.Auto())
                m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
            return true;
        }

        internal int FlushRegion(int mip, RectInt pageRegion)
        {
            int flushedCount = m_ResidencyManager.FlushRegion(mip, pageRegion);
            if (flushedCount <= 0)
                return 0;

            m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
            return flushedCount;
        }

        internal int FlushRegions(IReadOnlyList<VTPageRegion> pageRegions)
        {
            if (pageRegions == null || pageRegions.Count == 0)
                return 0;

            int flushedCount = 0;
            for (int regionIndex = 0; regionIndex < pageRegions.Count; regionIndex++)
            {
                VTPageRegion region = pageRegions[regionIndex];
                flushedCount += m_ResidencyManager.FlushRegion(region.Mip, region.PageRegion);
            }

            if (flushedCount <= 0)
                return 0;

            m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
            return flushedCount;
        }

        internal int ClearRuntimeState()
        {
            RetireProducerRequests(Array.Empty<VTRequest>());
            m_LocalUploadCandidates.Clear();
            m_ProducerTasks.Clear();
            m_PendingUploadSortEntries.Clear();
            m_SortedPendingRequests.Clear();
            m_EligiblePendingRequests.Clear();
            m_ResidentRefreshRequests.Clear();
            m_LiveProducerRequests.Clear();
            m_CachedPendingRequestRevision = 0;
            m_HasPendingOrderCache = false;

            int flushedCount = 0;
            for (int mip = 0; mip < Descriptor.MipCount; mip++)
            {
                int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(
                    Descriptor.VirtualPageCountX,
                    mip);
                int pageCountY = VirtualTextureSpaceUtility.GetPageCountY(
                    Descriptor.VirtualPageCountY,
                    mip);
                flushedCount += m_ResidencyManager.FlushRegion(
                    mip,
                    new RectInt(0, 0, pageCountX, pageCountY));
            }

            m_ResidencyManager.ResetPageTransitionsForRuntimeReset();

            return flushedCount;
        }

        internal void BootstrapRuntimeState(int frameIndex)
        {
            BootstrapLowestMip(frameIndex);
            m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
            m_PageTableUpdater.RefreshBufferImmediate();
        }

        internal VirtualTextureSpaceBinding CreateBinding(
            int allocationId,
            bool privateSpace,
            ComputeBuffer feedbackRequests,
            ComputeBuffer feedbackCounter,
            ComputeBuffer feedbackResidentHash,
            int feedbackResidentHashCapacity,
            VirtualTextureFeedbackBufferState feedbackState)
        {
            return new VirtualTextureSpaceBinding(
                -1,
                allocationId,
                privateSpace,
                SpaceId,
                Descriptor.SpaceName,
                ProducerHandle,
                m_PageTableUpdater.PageTableBuffer,
                m_ResidencyManager.PhysicalPool.Textures,
                feedbackRequests,
                feedbackCounter,
                feedbackResidentHash,
                feedbackResidentHashCapacity,
                feedbackState,
                m_ShaderParams,
                m_MipOffsets,
                m_LayerFallbacks);
        }

        public void Dispose()
        {
            CoreUtils.Destroy(m_ResidentPageStagingTexture);
            m_ResidentPageStagingTexture = null;
            for (int physicalGroup = 0; physicalGroup < m_ResidentPageConvertedStagingTextures.Length; physicalGroup++)
            {
                CoreUtils.Destroy(m_ResidentPageConvertedStagingTextures[physicalGroup]);
                m_ResidentPageConvertedStagingTextures[physicalGroup] = null;
                CoreUtils.Destroy(m_ResidentPageEncodedStagingTextures[physicalGroup]);
                m_ResidentPageEncodedStagingTextures[physicalGroup] = null;
            }
            m_ResidentPageScratchPixels = null;
            m_FallbackResidentPageProducer = null;
            m_PageTableUpdater.Dispose();
            m_ResidencyManager.Dispose();
        }

        private void BootstrapLowestMip(int frameIndex)
        {
            int lowestMip = Descriptor.MipCount - 1;
            int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(Descriptor.VirtualPageCountX, lowestMip);
            int pageCountY = VirtualTextureSpaceUtility.GetPageCountY(Descriptor.VirtualPageCountY, lowestMip);
            for (int y = 0; y < pageCountY; y++)
            {
                for (int x = 0; x < pageCountX; x++)
                {
                    var coord = new VirtualTexturePageCoord(x, y, lowestMip);
                    if (!m_ResidencyManager.TryAllocateResidentPage(
                            Descriptor,
                            m_MipOffsets,
                            SpaceId,
                            coord,
                            VirtualTextureViewId.Invalid,
                            frameIndex,
                            locked: true,
                            out VTRequest request))
                    {
                        throw new InvalidOperationException(
                            $"[VividRP] Failed to seed VT lowest mip page {coord} for space '{Descriptor.SpaceName}'.");
                    }

                    if (!TryUploadResidentPage(request, allowFallbackProducer: true, out bool usedFallback))
                    {
                        throw new InvalidOperationException(
                            $"[VividRP] Failed to produce VT lowest mip page {coord} for space '{Descriptor.SpaceName}'.");
                    }

                    // Streamed compressed producers cannot synchronously block construction on IO/decode.
                    // Keep the locked fallback resident, then overwrite the same physical page once its
                    // encoded payload becomes available through the normal global upload scheduler.
                    if (usedFallback && m_PageProducer is VividVirtualTextureAssetProducer)
                        m_ResidentRefreshRequests.Add(request);
                }
            }
        }

        private bool TryUploadResidentPage(
            in VTRequest request,
            bool allowFallbackProducer,
            out bool usedFallback)
        {
            usedFallback = false;
            bool hasPayload = TryProduceUploadPayload(m_PageProducer, request, out VTPageUploadPayload payload);
            if (hasPayload && payload.Finalizer is not IVTPageFinalizer and not IVTEncodedPageFinalizer)
            {
                payload.Finalizer?.Dispose();
                payload = default;
                hasPayload = false;
            }

            if (!hasPayload)
            {
                if (!allowFallbackProducer)
                    return false;

                usedFallback = true;

                if (UsesCompressedStorage(StackDesc))
                {
                    payload = new VTPageUploadPayload(request, new VTConstantEncodedPageFinalizer(StackDesc));
                }
                else
                {
                    m_FallbackResidentPageProducer ??=
                        VTRuntimeProducerUtility.CreateAdapter(VTProceduralPageProducer.Instance, Descriptor);
                    if (!TryProduceUploadPayload(m_FallbackResidentPageProducer, request, out payload))
                        return false;
                }
            }

            if (payload.Finalizer is IVTEncodedPageFinalizer encodedFinalizer)
                return UploadEncodedResidentPage(request, payload, encodedFinalizer);

            EnsureResidentPageUploadStorage();
            try
            {
                VTPageUploadUtility.FinalizePayloadRender(payload, null);
                for (int layerIndex = 0; layerIndex < StackDesc.LayerCount; layerIndex++)
                {
                    VTPageUploadUtility.WritePayloadLayerToStagingTexture(
                        m_ResidentPageStagingTexture,
                        layerIndex,
                        m_ResidentPageScratchPixels,
                        payload,
                        layerIndex);
                }
            }
            finally
            {
                payload.Finalizer?.Dispose();
            }

            m_ResidentPageStagingTexture.Apply(false, false);
            for (int layerIndex = 0; layerIndex < StackDesc.LayerCount; layerIndex++)
            {
                int physicalGroup = PhysicalPool.GetLayerPhysicalGroup(layerIndex);
                Texture2D physicalCache = PhysicalPool.GetTextureForGroup(physicalGroup);
                if (physicalCache == null)
                    continue;

                int physicalLayerIndex = PhysicalPool.GetLayerPhysicalLayerIndex(layerIndex);
                RectInt destinationTile = PhysicalPool.GetPhysicalTileRect(
                    physicalGroup,
                    request.PhysicalPageId,
                    physicalLayerIndex);
                if (m_ResidentPageStagingTexture.graphicsFormat == physicalCache.graphicsFormat)
                {
                    Graphics.CopyTexture(
                        m_ResidentPageStagingTexture,
                        layerIndex,
                        0,
                        0,
                        0,
                        destinationTile.width,
                        destinationTile.height,
                        physicalCache,
                        0,
                        0,
                        destinationTile.x,
                        destinationTile.y);
                    continue;
                }

                Texture2D convertedStagingTexture = GetResidentPageConvertedStagingTexture(
                    physicalGroup,
                    physicalCache.graphicsFormat);
                if (!Graphics.ConvertTexture(
                    m_ResidentPageStagingTexture,
                    layerIndex,
                    convertedStagingTexture,
                    0))
                {
                    throw new InvalidOperationException(
                        $"[VividRP] Failed to convert VT bootstrap layer {layerIndex} into " +
                        $"physical group {physicalGroup} for space '{Descriptor.SpaceName}'.");
                }

                Graphics.CopyTexture(
                    convertedStagingTexture,
                    0,
                    0,
                    0,
                    0,
                    destinationTile.width,
                    destinationTile.height,
                    physicalCache,
                    0,
                    0,
                    destinationTile.x,
                    destinationTile.y);
            }

            return true;
        }

        private void EnsureResidentPageUploadStorage()
        {
            m_ResidentPageStagingTexture ??= VTPageUploadUtility.CreateStagingTexture(
                Descriptor.SpaceName,
                Descriptor.PhysicalPageSize,
                StackDesc.LayerCount,
                "ResidentPage");
            m_ResidentPageScratchPixels ??=
                new Color32[Descriptor.PhysicalPageSize * Descriptor.PhysicalPageSize];
        }

        private Texture2D GetResidentPageConvertedStagingTexture(
            int physicalGroup,
            UnityEngine.Experimental.Rendering.GraphicsFormat graphicsFormat)
        {
            if (physicalGroup < 0 || physicalGroup >= m_ResidentPageConvertedStagingTextures.Length)
                throw new ArgumentOutOfRangeException(nameof(physicalGroup));

            Texture2D texture = m_ResidentPageConvertedStagingTextures[physicalGroup];
            if (texture != null && texture.graphicsFormat == graphicsFormat)
                return texture;

            CoreUtils.Destroy(texture);
            texture = new Texture2D(
                Descriptor.PhysicalPageSize,
                Descriptor.PhysicalPageSize,
                graphicsFormat,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.None)
            {
                name = $"VividVT_{Descriptor.SpaceName}_ResidentPageConverted_Group{physicalGroup}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            texture.Apply(false, true);
            m_ResidentPageConvertedStagingTextures[physicalGroup] = texture;
            return texture;
        }

        private void RollbackResidentPage(in VirtualTexturePageCoord coord)
        {
            m_ResidencyManager.FlushRegion(coord.Mip, new RectInt(coord.X, coord.Y, 1, 1));
            RebuildPageTable();
        }

        private void RebuildPageTable()
        {
            m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            m_ResidencyManager.ClearDirtyPageTableUpdates();
        }

        private static void BuildLayerFallbacks(in VirtualTextureSpaceDesc desc, Vector4[] output)
        {
            if (output == null)
                return;

            for (int layerIndex = 0; layerIndex < output.Length; layerIndex++)
            {
                Color32 fallbackColor = layerIndex < desc.StackDesc.LayerCount
                    ? desc.StackDesc.GetLayer(layerIndex).FallbackColor
                    : new Color32(0, 0, 0, 255);
                output[layerIndex] = new Vector4(
                    fallbackColor.r / 255f,
                    fallbackColor.g / 255f,
                    fallbackColor.b / 255f,
                    fallbackColor.a / 255f);
            }
        }

        internal void CollectPendingUploadCandidates(
            VTUploadScheduler uploadScheduler,
            int fairnessRank,
            List<VTPendingUploadCandidate> output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            IReadOnlyList<VTRequest> pendingRequests = PendingRequests;
            int pendingRequestCount = pendingRequests?.Count ?? 0;
            if (pendingRequestCount == 0 && m_ResidentRefreshRequests.Count == 0)
            {
                RetireProducerRequests(Array.Empty<VTRequest>());
                return;
            }

            if (uploadScheduler == null || m_PageProducer == null || !uploadScheduler.IsEnabled)
            {
                uploadScheduler?.AddSkippedUploadCount(pendingRequestCount + m_ResidentRefreshRequests.Count);
                return;
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingRetireMarker.Auto())
            {
                m_LiveProducerRequests.Clear();
                for (int refreshIndex = 0; refreshIndex < m_ResidentRefreshRequests.Count; refreshIndex++)
                    m_LiveProducerRequests.Add(m_ResidentRefreshRequests[refreshIndex]);
                for (int requestIndex = 0; requestIndex < pendingRequestCount; requestIndex++)
                    m_LiveProducerRequests.Add(pendingRequests[requestIndex]);
                RetireProducerRequests(m_LiveProducerRequests);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingGatherTasksMarker.Auto())
            {
                m_ProducerTasks.Clear();
                m_PageProducer.GatherTasks(m_ProducerTasks);
                m_ProducerTasks.Clear();
            }

            IReadOnlyList<VTRequest> orderedRequests;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingOrderMarker.Auto())
                orderedRequests = GetOrderedPendingRequests(pendingRequests);
            int duplicateUploadCount;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingInFlightMarker.Auto())
            {
                duplicateUploadCount = uploadScheduler.FilterInFlightRequests(
                    orderedRequests,
                    m_EligiblePendingRequests);
            }

            uploadScheduler.AddSkippedUploadCount(duplicateUploadCount);
            for (int refreshIndex = 0; refreshIndex < m_ResidentRefreshRequests.Count; refreshIndex++)
            {
                VTRequest request = m_ResidentRefreshRequests[refreshIndex];
                if (uploadScheduler.IsRequestInFlight(request))
                {
                    uploadScheduler.AddSkippedUploadCount(1);
                    continue;
                }

                output.Add(new VTPendingUploadCandidate(this, request, locked: true, fairnessRank));
            }

            for (int requestIndex = 0; requestIndex < m_EligiblePendingRequests.Count; requestIndex++)
            {
                VTRequest request = m_EligiblePendingRequests[requestIndex];
                bool locked = m_ResidencyManager.IsPageLocked(
                    Descriptor,
                    m_MipOffsets,
                    request.PageCoord);
                output.Add(new VTPendingUploadCandidate(this, request, locked, fairnessRank));
            }
        }

        internal bool TrySchedulePendingUpload(
            VTUploadScheduler uploadScheduler,
            CommandBuffer cmd,
            in VTRequest request)
        {
            if (uploadScheduler == null || m_PageProducer == null || cmd == null || !uploadScheduler.IsEnabled)
                return false;

            VTPageRequestStatus status;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingRequestPageMarker.Auto())
                status = m_PageProducer.RequestPageData(Descriptor, request);
            if (status != VTPageRequestStatus.Available)
            {
                if (status == VTPageRequestStatus.Invalid)
                {
                    m_PageProducer.CancelRequest(Descriptor, request);
                    if (!RemoveResidentRefreshRequest(request))
                        RollbackResidentPage(request.PageCoord);
                }

                return false;
            }

            if (!uploadScheduler.TryReserveUpload(Descriptor.SpaceName, Descriptor))
                return false;

            IVTPageUploadFinalizer finalizer;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingProducePageMarker.Auto())
                finalizer = m_PageProducer.ProducePageData(Descriptor, request);
            if (finalizer == null)
            {
                uploadScheduler.ReleaseUploadReservation(Descriptor);
                return false;
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsCollectPendingEnqueueMarker.Auto())
            {
                uploadScheduler.EnqueueReservedUpload(
                    Descriptor.SpaceName,
                    Descriptor,
                    m_ResidencyManager.PhysicalPool,
                    new VTPageUploadPayload(request, finalizer));
            }

            return true;
        }

        private bool UploadEncodedResidentPage(
            in VTRequest request,
            in VTPageUploadPayload payload,
            IVTEncodedPageFinalizer encodedFinalizer)
        {
            if (encodedFinalizer.LayerCount != StackDesc.LayerCount)
            {
                payload.Finalizer.Dispose();
                return false;
            }

            var touchedGroups = new bool[PhysicalPool.Desc.PhysicalGroupCount];
            try
            {
                for (int layerIndex = 0; layerIndex < StackDesc.LayerCount; layerIndex++)
                {
                    int physicalGroup = PhysicalPool.GetLayerPhysicalGroup(layerIndex);
                    int physicalLayerIndex = PhysicalPool.GetLayerPhysicalLayerIndex(layerIndex);
                    Texture2DArray stagingTexture = GetResidentPageEncodedStagingTexture(physicalGroup);
                    encodedFinalizer.FinalizeEncodedUploadLayer(stagingTexture, physicalLayerIndex, layerIndex);
                    touchedGroups[physicalGroup] = true;
                }

                for (int physicalGroup = 0; physicalGroup < touchedGroups.Length; physicalGroup++)
                {
                    if (touchedGroups[physicalGroup])
                        GetResidentPageEncodedStagingTexture(physicalGroup).Apply(false, false);
                }

                for (int layerIndex = 0; layerIndex < StackDesc.LayerCount; layerIndex++)
                {
                    int physicalGroup = PhysicalPool.GetLayerPhysicalGroup(layerIndex);
                    int physicalLayerIndex = PhysicalPool.GetLayerPhysicalLayerIndex(layerIndex);
                    Texture2DArray stagingTexture = GetResidentPageEncodedStagingTexture(physicalGroup);
                    Texture2D physicalCache = PhysicalPool.GetTextureForGroup(physicalGroup);
                    if (physicalCache == null || stagingTexture.graphicsFormat != physicalCache.graphicsFormat)
                        return false;

                    RectInt destinationTile = PhysicalPool.GetPhysicalTileRect(
                        physicalGroup,
                        request.PhysicalPageId,
                        physicalLayerIndex);
                    Graphics.CopyTexture(
                        stagingTexture,
                        physicalLayerIndex,
                        0,
                        0,
                        0,
                        destinationTile.width,
                        destinationTile.height,
                        physicalCache,
                        0,
                        0,
                        destinationTile.x,
                        destinationTile.y);
                }
            }
            finally
            {
                payload.Finalizer.Dispose();
            }

            return true;
        }

        private Texture2DArray GetResidentPageEncodedStagingTexture(int physicalGroup)
        {
            if (physicalGroup < 0 || physicalGroup >= m_ResidentPageEncodedStagingTextures.Length)
                throw new ArgumentOutOfRangeException(nameof(physicalGroup));

            Texture2DArray texture = m_ResidentPageEncodedStagingTextures[physicalGroup];
            if (texture != null)
                return texture;

            texture = VTPageUploadUtility.CreateEncodedStagingTexture(
                Descriptor.SpaceName,
                Descriptor.PhysicalPageSize,
                Mathf.Max(1, PhysicalPool.GetGroupLayerCount(physicalGroup)),
                PhysicalPool.Desc.GetGroupStorageFormat(physicalGroup),
                $"ResidentPageEncoded_Group{physicalGroup}");
            m_ResidentPageEncodedStagingTextures[physicalGroup] = texture;
            return texture;
        }

        private static bool UsesCompressedStorage(in VTStackDesc stackDesc)
        {
            for (int layerIndex = 0; layerIndex < stackDesc.LayerCount; layerIndex++)
            {
                if (UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(
                        stackDesc.GetLayer(layerIndex).GraphicsFormat))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryProduceUploadPayload(
            IVTPageProducer producer,
            in VTRequest request,
            out VTPageUploadPayload payload)
        {
            payload = default;
            if (producer == null)
                return false;

            if (producer.RequestPageData(Descriptor, request) != VTPageRequestStatus.Available)
                return false;

            IVTPageUploadFinalizer finalizer = producer.ProducePageData(Descriptor, request);
            if (finalizer == null)
                return false;

            payload = new VTPageUploadPayload(request, finalizer);
            return true;
        }

        private void RetireProducerRequests(IReadOnlyList<VTRequest> liveRequests)
        {
            if (m_PageProducer is IVTPageRequestRetirement retirement)
                retirement.RetireRequests(liveRequests);
        }

        private IReadOnlyList<VTRequest> GetOrderedPendingRequests(IReadOnlyList<VTRequest> pendingRequests)
        {
            if (pendingRequests == null || pendingRequests.Count == 0)
                return Array.Empty<VTRequest>();
            if (pendingRequests.Count == 1)
                return pendingRequests;

            uint pendingRequestRevision = m_ResidencyManager.PendingRequestRevision;
            if (m_HasPendingOrderCache
                && m_CachedPendingRequestRevision == pendingRequestRevision
                && m_SortedPendingRequests.Count == pendingRequests.Count)
            {
                m_PendingOrderCacheHitCount += 1;
                return m_SortedPendingRequests;
            }

            m_PendingUploadSortEntries.Clear();
            m_SortedPendingRequests.Clear();
            for (int requestIndex = 0; requestIndex < pendingRequests.Count; requestIndex++)
            {
                VTRequest request = pendingRequests[requestIndex];
                bool locked = m_ResidencyManager.IsPageLocked(
                    Descriptor,
                    m_MipOffsets,
                    request.PageCoord);
                m_PendingUploadSortEntries.Add(new PendingUploadSortEntry(request, locked));
            }

            if (m_PendingUploadSortEntries.Count > 1)
                m_PendingUploadSortEntries.Sort(PendingUploadRequestComparer.Instance);

            for (int entryIndex = 0; entryIndex < m_PendingUploadSortEntries.Count; entryIndex++)
                m_SortedPendingRequests.Add(m_PendingUploadSortEntries[entryIndex].Request);

            m_CachedPendingRequestRevision = pendingRequestRevision;
            m_HasPendingOrderCache = true;
            m_PendingOrderCacheBuildCount += 1;
            return m_SortedPendingRequests;
        }

        private readonly struct PendingUploadSortEntry
        {
            internal PendingUploadSortEntry(in VTRequest request, bool locked)
            {
                Request = request;
                Locked = locked;
                IsActiveView = request.IsActiveView;
                CameraPriority = request.CameraPriority;
                Priority = request.Priority;
                MipWeightedPriority = VTRequestPriorityUtility.ComputeMipWeightedScore(
                    request.Priority,
                    request.PageCoord.Mip);
                RequestFrame = request.RequestFrame;
                Mip = request.PageCoord.Mip;
                Y = request.PageCoord.Y;
                X = request.PageCoord.X;
            }

            internal VTRequest Request { get; }

            internal bool Locked { get; }

            internal bool IsActiveView { get; }

            internal int CameraPriority { get; }

            internal int Priority { get; }

            internal long MipWeightedPriority { get; }

            internal int RequestFrame { get; }

            internal int Mip { get; }

            internal int Y { get; }

            internal int X { get; }
        }

        private sealed class PendingUploadRequestComparer : IComparer<PendingUploadSortEntry>
        {
            internal static readonly PendingUploadRequestComparer Instance = new();

            private PendingUploadRequestComparer()
            {
            }

            public int Compare(PendingUploadSortEntry left, PendingUploadSortEntry right)
            {
                if (left.Locked != right.Locked)
                    return left.Locked ? -1 : 1;

                if (left.IsActiveView != right.IsActiveView)
                    return left.IsActiveView ? -1 : 1;

                int cameraCompare = left.CameraPriority.CompareTo(right.CameraPriority);
                if (cameraCompare != 0)
                    return cameraCompare;

                int scoreCompare = right.MipWeightedPriority.CompareTo(left.MipWeightedPriority);
                if (scoreCompare != 0)
                    return scoreCompare;

                int mipCompare = right.Mip.CompareTo(left.Mip);
                if (mipCompare != 0)
                    return mipCompare;

                int priorityCompare = right.Priority.CompareTo(left.Priority);
                if (priorityCompare != 0)
                    return priorityCompare;

                int frameCompare = left.RequestFrame.CompareTo(right.RequestFrame);
                if (frameCompare != 0)
                    return frameCompare;

                int yCompare = left.Y.CompareTo(right.Y);
                if (yCompare != 0)
                    return yCompare;

                return left.X.CompareTo(right.X);
            }
        }

        private bool TryCommitRequestInternal(
            in VTRequest request,
            bool rebuildPageTable,
            int commitFrameIndex = -1)
        {
            if (request.SpaceId != SpaceId)
                return false;

            if (RemoveResidentRefreshRequest(request))
                return true;

            if (!m_ResidencyManager.TryCommitRequest(
                    Descriptor,
                    m_MipOffsets,
                    request,
                    commitFrameIndex))
                return false;

            if (rebuildPageTable)
            {
                m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
                m_ResidencyManager.ClearDirtyPageTableUpdates();
            }

            return true;
        }

        private bool RemoveResidentRefreshRequest(in VTRequest request)
        {
            for (int requestIndex = 0; requestIndex < m_ResidentRefreshRequests.Count; requestIndex++)
            {
                VTRequest candidate = m_ResidentRefreshRequests[requestIndex];
                if (candidate.SpaceId != request.SpaceId
                    || !candidate.PageCoord.Equals(request.PageCoord)
                    || candidate.PhysicalPageId != request.PhysicalPageId
                    || candidate.Generation != request.Generation)
                {
                    continue;
                }

                m_ResidentRefreshRequests.RemoveAt(requestIndex);
                return true;
            }

            return false;
        }

        bool IVTUploadRequestCommitter.TryCommitUpload(in VTRequest request, int frameIndex)
        {
            return TryCommitRequestInternal(
                request,
                rebuildPageTable: false,
                commitFrameIndex: frameIndex);
        }
    }
}
