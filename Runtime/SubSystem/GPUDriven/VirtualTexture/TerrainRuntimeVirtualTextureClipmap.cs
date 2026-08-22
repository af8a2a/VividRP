using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.SubSystem.Decal;

namespace VividRP.Runtime.GPUDriven.VirtualTexture
{
    [Flags]
    internal enum TerrainRuntimeVirtualTextureRecordFlags : uint
    {
        None = 0u,
        ReceiveDecals = 1u << 0,
        HasDecalCache = 1u << 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerrainRuntimeVirtualTextureDecalGPUData
    {
        internal float4x4 WorldToDecal;
        internal float4 BaseColor;
        internal VividSurfaceBindingData SurfaceBinding;
        internal float4 TangentWS;
        internal float4 BitangentWS;
        internal float4 NormalWS;
        internal float BlendDistance;
        internal float Metallic;
        internal float Roughness;
        internal float Padding0;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerrainRuntimeVirtualTexturePageDecalIndexGPUData
    {
        internal uint DecalIndex;
        internal uint SourceMip;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerrainRuntimeVirtualTextureRecordGPUData
    {
        internal uint LevelStartIndex;
        internal uint LevelCount;
        internal uint Revision;
        internal uint Padding0;
        internal float4 WorldToTerrainUvX;
        internal float4 WorldToTerrainUvY;
        internal float4 WorldToTerrainLocalY;
        internal float2 LocalHeightRange;
        internal float2 Padding1;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerrainRuntimeVirtualTextureLevelGPUData
    {
        internal uint2 AtlasPageOrigin;
        internal int2 WindowPageOrigin;
        internal uint2 TotalPageCount;
        internal uint2 Padding0;
    }

    internal sealed class TerrainRuntimeVirtualTextureClipmap : IDisposable
    {
        internal const int LevelCount = 3;
        internal const int WindowPageCount = 8;
        internal const int PagesPerLevel = WindowPageCount * WindowPageCount;

        private const int ThreadGroupSize = 8;

        internal readonly struct PageCandidate
        {
            internal PageCandidate(
                Level level,
                int cellIndex,
                int feedbackHitCount,
                float distanceSquared)
            {
                Level = level;
                CellIndex = cellIndex;
                FeedbackHitCount = feedbackHitCount;
                DistanceSquared = distanceSquared;
            }

            internal Level Level { get; }

            internal int CellIndex { get; }

            internal int FeedbackHitCount { get; }

            internal float DistanceSquared { get; }
        }

        internal sealed class Level
        {
            private static readonly int s_OutputPagesId = Shader.PropertyToID("_OutputPages");
            private static readonly int s_Control0Id = Shader.PropertyToID("_Control0");
            private static readonly int s_Control1Id = Shader.PropertyToID("_Control1");
            private static readonly int s_HeightTextureId = Shader.PropertyToID("_TerrainHeightTexture");
            private static readonly int s_DecalDataId = Shader.PropertyToID("_RVTDecals");
            private static readonly int s_PageDecalIndicesId = Shader.PropertyToID("_RVTPageDecalIndices");
            private static readonly int s_PageDecalRangeId = Shader.PropertyToID("_RVTPageDecalRange");
            private static readonly int s_TerrainLocalToWorldId = Shader.PropertyToID("_RVTLocalToWorld");
            private static readonly int s_TerrainSizeId = Shader.PropertyToID("_RVTTerrainSize");
            private static readonly int s_HeightTexelSizeId = Shader.PropertyToID("_RVTHeightTexelSize");
            private static readonly int s_LogicalPageAndSliceId = Shader.PropertyToID("_RVTLogicalPageAndSlice");
            private static readonly int s_PageLayoutId = Shader.PropertyToID("_RVTPageLayout");
            private static readonly int s_OutputDimensionsId = Shader.PropertyToID("_RVTOutputDimensions");
            private static readonly int s_LayerTilingOffsetId = Shader.PropertyToID("_RVTLayerTilingOffset");
            private static readonly int s_LayerMaterialParamsId = Shader.PropertyToID("_RVTLayerMaterialParams");
            private static readonly int[] s_BaseColorIds = CreateTextureIds("_BaseColor");
            private static readonly int[] s_NormalIds = CreateTextureIds("_Normal");
            private static readonly int[] s_MaskIds = CreateTextureIds("_Mask");

            private readonly TerrainRuntimeVirtualTextureClipmap m_Owner;
            private readonly bool[] m_Dirty = new bool[PagesPerLevel];
            private readonly bool[] m_Approved = new bool[PagesPerLevel];
            private readonly bool[] m_Resident = new bool[PagesPerLevel];
            private readonly int[] m_FeedbackHitCounts = new int[PagesPerLevel];
            private readonly uint[] m_ContentGenerations = new uint[PagesPerLevel];
            private readonly uint[] m_UploadGenerations = new uint[PagesPerLevel];
            private readonly uint[] m_ResidentGenerations = new uint[PagesPerLevel];
            private readonly int[] m_PageDecalStarts = new int[PagesPerLevel];
            private readonly int[] m_PageDecalCounts = new int[PagesPerLevel];
            private readonly List<VirtualTexturePageCoord>[] m_PageSourceDependencies =
                new List<VirtualTexturePageCoord>[PagesPerLevel];
            private readonly Dictionary<long, List<VTPagePinLease>> m_UploadPinLeases = new();
            private bool m_WindowInitialized;

            internal Level(
                TerrainRuntimeVirtualTextureClipmap owner,
                int levelIndex,
                RectInt pageRegion,
                Vector2Int totalPageCount)
            {
                m_Owner = owner;
                LevelIndex = levelIndex;
                PageRegion = pageRegion;
                TotalPageCount = new Vector2Int(
                    Mathf.Max(1, totalPageCount.x),
                    Mathf.Max(1, totalPageCount.y));
                WindowPageOrigin = Vector2Int.zero;
                for (int cellIndex = 0; cellIndex < PagesPerLevel; cellIndex++)
                {
                    m_PageSourceDependencies[cellIndex] = new List<VirtualTexturePageCoord>();
                    m_ContentGenerations[cellIndex] = 1u;
                }
                MarkAllDirty();
            }

            internal int LevelIndex { get; }

            internal RectInt PageRegion { get; }

            internal Vector2Int TotalPageCount { get; }

            internal Vector2Int WindowPageOrigin { get; private set; }

            internal Vector2Int ResolveLogicalPageForTesting(int ringX, int ringY)
            {
                return ResolveLogicalPage(ringY * WindowPageCount + ringX);
            }

            internal bool IsPageApproved(in VirtualTexturePageCoord coord)
            {
                return coord.Mip == 0
                       && TryGetCellIndex(coord.X, coord.Y, out int cellIndex)
                       && m_Approved[cellIndex];
            }

            internal bool TryPreparePageUpload(
                int spaceId,
                in VTRequest request)
            {
                return GetPageUploadStatus(spaceId, request) == VTPageRequestStatus.Available;
            }

            internal VTPageRequestStatus GetPageUploadStatus(
                int spaceId,
                in VTRequest request)
            {
                if (!TryGetCellIndex(request.PageCoord.X, request.PageCoord.Y, out int cellIndex)
                    || !m_Approved[cellIndex])
                {
                    return VTPageRequestStatus.Pending;
                }

                bool hasPhysicalUploadIdentity = request.PhysicalPageId >= 0;
                long uploadKey = hasPhysicalUploadIdentity ? GetUploadKey(request) : 0L;
                if (hasPhysicalUploadIdentity && m_UploadPinLeases.ContainsKey(uploadKey))
                    return VTPageRequestStatus.Available;

                List<VirtualTexturePageCoord> dependencies = m_PageSourceDependencies[cellIndex];
                if (dependencies.Count > 0
                    && !VirtualTextureSystem.TryGetSpaceBinding(spaceId, out _))
                {
                    return VTPageRequestStatus.Pending;
                }

                bool allResident = true;
                for (int dependencyIndex = 0; dependencyIndex < dependencies.Count; dependencyIndex++)
                {
                    VirtualTexturePageCoord dependency = dependencies[dependencyIndex];
                    if (m_Owner != null
                        && m_Owner.HasPermanentSourceFailure(dependency))
                    {
                        return VTPageRequestStatus.Invalid;
                    }

                    bool exactResident = VirtualTextureSystem.TryGetPageTableEntry(
                                             spaceId,
                                             dependency,
                                             out VirtualTexturePageTableEntry entry)
                                         && entry.Resident
                                         && !entry.PendingUpload
                                         && entry.ResolvedMip == dependency.Mip;
                    if (exactResident
                        && !VirtualTextureSystem.IsPageTableEntryPendingUpload(spaceId, dependency))
                    {
                        continue;
                    }

                    if (!exactResident)
                    {
                        VirtualTextureSystem.TryQueuePageResidentWithinBudget(
                            spaceId,
                            dependency,
                            locked: false,
                            frameIndex: request.RequestFrame);
                    }
                    allResident = false;
                }

                if (!allResident)
                    return VTPageRequestStatus.Pending;

                if (dependencies.Count == 0 || !hasPhysicalUploadIdentity)
                    return VTPageRequestStatus.Available;

                var leases = new List<VTPagePinLease>(dependencies.Count);
                for (int dependencyIndex = 0; dependencyIndex < dependencies.Count; dependencyIndex++)
                {
                    if (VirtualTextureSystem.TryAcquirePagePinLease(
                            spaceId,
                            dependencies[dependencyIndex],
                            out VTPagePinLease lease))
                    {
                        leases.Add(lease);
                        continue;
                    }

                    for (int leaseIndex = 0; leaseIndex < leases.Count; leaseIndex++)
                        leases[leaseIndex].Dispose();
                    return VTPageRequestStatus.Pending;
                }

                m_UploadPinLeases.Add(uploadKey, leases);
                return VTPageRequestStatus.Available;
            }

            internal void ReleasePageUploadDependencies(in VTRequest request)
            {
                if (request.PhysicalPageId < 0)
                    return;

                if (!m_UploadPinLeases.Remove(GetUploadKey(request), out List<VTPagePinLease> leases))
                    return;

                for (int leaseIndex = 0; leaseIndex < leases.Count; leaseIndex++)
                    leases[leaseIndex].Dispose();
            }

            internal void CancelPageUpload(in VTRequest request)
            {
                ReleasePageUploadDependencies(request);
                if (!TryGetCellIndex(request.PageCoord.X, request.PageCoord.Y, out int cellIndex))
                    return;

                m_Approved[cellIndex] = false;
                m_Dirty[cellIndex] = true;
            }

            internal void RetirePageUploadDependencies(IReadOnlyList<VTRequest> liveRequests)
            {
                if (m_UploadPinLeases.Count == 0)
                    return;

                var liveRuntimeRequests = new HashSet<long>();
                if (liveRequests != null)
                {
                    for (int requestIndex = 0; requestIndex < liveRequests.Count; requestIndex++)
                    {
                        VTRequest request = liveRequests[requestIndex];
                        if (request.PhysicalPageId >= 0
                            && TryGetCellIndex(request.PageCoord.X, request.PageCoord.Y, out _))
                            liveRuntimeRequests.Add(GetUploadKey(request));
                    }
                }

                var retiredRequests = new List<long>();
                foreach (KeyValuePair<long, List<VTPagePinLease>> pair in m_UploadPinLeases)
                {
                    if (!liveRuntimeRequests.Contains(pair.Key))
                        retiredRequests.Add(pair.Key);
                }
                for (int retiredIndex = 0; retiredIndex < retiredRequests.Count; retiredIndex++)
                {
                    long retiredRequest = retiredRequests[retiredIndex];
                    List<VTPagePinLease> leases = m_UploadPinLeases[retiredRequest];
                    for (int leaseIndex = 0; leaseIndex < leases.Count; leaseIndex++)
                        leases[leaseIndex].Dispose();
                    m_UploadPinLeases.Remove(retiredRequest);
                }
            }

            internal void RecordFeedback(
                in VirtualTexturePageCoord coord,
                in VTRequestPriorityKey priorityKey)
            {
                if (coord.Mip != 0 || !TryGetCellIndex(coord.X, coord.Y, out int cellIndex))
                    return;

                m_FeedbackHitCounts[cellIndex] = Mathf.Max(
                    m_FeedbackHitCounts[cellIndex],
                    priorityKey.HitCount);
            }

            internal void RecordPageUpload(
                CommandBuffer cmd,
                RenderTexture stagingTexture,
                int baseSlice,
                in VTRequest request)
            {
                if (!TryGetCellIndex(request.PageCoord.X, request.PageCoord.Y, out int cellIndex))
                    return;

                Vector2Int logicalPage = ResolveLogicalPage(cellIndex);
                ComputeShader shader = m_Owner.m_PageProducerCompute;
                int kernel = m_Owner.m_PageProducerKernel;
                for (int layerIndex = 0; layerIndex < VividTerrainData.MaximumSurfaceLayerCount; layerIndex++)
                {
                    VividTerrainLayerData layer = layerIndex < m_Owner.m_LayerCount
                        ? m_Owner.m_TerrainData.Layers[layerIndex]
                        : default;
                    cmd.SetComputeTextureParam(
                        shader,
                        kernel,
                        s_BaseColorIds[layerIndex],
                        layerIndex < m_Owner.m_LayerCount && layer.DiffuseTexture != null
                            ? layer.DiffuseTexture
                            : Texture2D.whiteTexture);
                    cmd.SetComputeTextureParam(
                        shader,
                        kernel,
                        s_NormalIds[layerIndex],
                        layerIndex < m_Owner.m_LayerCount && layer.NormalMapTexture != null
                            ? layer.NormalMapTexture
                            : Texture2D.whiteTexture);
                    cmd.SetComputeTextureParam(
                        shader,
                        kernel,
                        s_MaskIds[layerIndex],
                        layerIndex < m_Owner.m_LayerCount && layer.MaskMapTexture != null
                            ? layer.MaskMapTexture
                            : Texture2D.whiteTexture);
                }

                cmd.SetComputeTextureParam(
                    shader,
                    kernel,
                    s_Control0Id,
                    m_Owner.m_ControlCount > 0
                        ? m_Owner.m_TerrainData.ControlMaps[0]
                        : Texture2D.blackTexture);
                cmd.SetComputeTextureParam(
                    shader,
                    kernel,
                    s_Control1Id,
                    m_Owner.m_ControlCount > 1
                        ? m_Owner.m_TerrainData.ControlMaps[1]
                        : Texture2D.blackTexture);
                cmd.SetComputeTextureParam(
                    shader,
                    kernel,
                    s_HeightTextureId,
                    m_Owner.m_TerrainData.NormalizedHeightTexture != null
                        ? m_Owner.m_TerrainData.NormalizedHeightTexture
                        : Texture2D.blackTexture);
                cmd.SetComputeTextureParam(shader, kernel, s_OutputPagesId, stagingTexture);
                cmd.SetComputeBufferParam(shader, kernel, s_DecalDataId, m_Owner.m_DecalDataBuffer);
                cmd.SetComputeBufferParam(
                    shader,
                    kernel,
                    s_PageDecalIndicesId,
                    m_Owner.m_PageDecalIndexBuffer);
                cmd.SetComputeIntParams(
                    shader,
                    s_PageDecalRangeId,
                    new[] { m_PageDecalStarts[cellIndex], m_PageDecalCounts[cellIndex] });
                cmd.SetComputeMatrixParam(
                    shader,
                    s_TerrainLocalToWorldId,
                    m_Owner.m_Terrain.transform.localToWorldMatrix);
                Vector3 terrainSize = m_Owner.m_TerrainData.Size;
                cmd.SetComputeVectorParam(
                    shader,
                    s_TerrainSizeId,
                    new Vector4(terrainSize.x, terrainSize.y, terrainSize.z, 0.0f));
                Texture2D heightTexture = m_Owner.m_TerrainData.NormalizedHeightTexture;
                cmd.SetComputeVectorParam(
                    shader,
                    s_HeightTexelSizeId,
                    heightTexture != null
                        ? new Vector4(
                            1.0f / heightTexture.width,
                            1.0f / heightTexture.height,
                            heightTexture.width,
                            heightTexture.height)
                        : Vector4.zero);
                cmd.SetComputeVectorArrayParam(shader, s_LayerTilingOffsetId, m_Owner.m_LayerTilingOffsets);
                cmd.SetComputeVectorArrayParam(shader, s_LayerMaterialParamsId, m_Owner.m_LayerMaterialParams);
                cmd.SetComputeIntParams(
                    shader,
                    s_LogicalPageAndSliceId,
                    new[] { logicalPage.x, logicalPage.y, baseSlice, m_Owner.m_LayerCount });
                cmd.SetComputeIntParams(
                    shader,
                    s_PageLayoutId,
                    new[]
                    {
                        VirtualTextureGPUDrivenTextureBackend.PageSize,
                        m_Owner.m_Backend.VirtualTextureSpaceDesc.StackDesc.BorderSize,
                        VirtualTextureGPUDrivenTextureBackend.PageSize
                        + m_Owner.m_Backend.VirtualTextureSpaceDesc.StackDesc.BorderSize * 2,
                        m_Owner.m_ControlCount,
                    });
                cmd.SetComputeVectorParam(
                    shader,
                    s_OutputDimensionsId,
                    new Vector4(
                        TotalPageCount.x * VirtualTextureGPUDrivenTextureBackend.PageSize,
                        TotalPageCount.y * VirtualTextureGPUDrivenTextureBackend.PageSize,
                        1.0f / (TotalPageCount.x * VirtualTextureGPUDrivenTextureBackend.PageSize),
                        1.0f / (TotalPageCount.y * VirtualTextureGPUDrivenTextureBackend.PageSize)));

                m_Owner.BindSourceVirtualTextureResources(cmd, shader, kernel);

                int physicalPageSize = VirtualTextureGPUDrivenTextureBackend.PageSize
                                       + m_Owner.m_Backend.VirtualTextureSpaceDesc.StackDesc.BorderSize * 2;
                int groupCount = Mathf.CeilToInt(physicalPageSize / (float)ThreadGroupSize);
                cmd.DispatchCompute(shader, kernel, groupCount, groupCount, 1);
            }

            internal void UpdateWindow(Vector2 terrainUv, List<VTPageRegion> flushRegions)
            {
                var centerPage = new Vector2Int(
                    Mathf.FloorToInt(Mathf.Clamp01(terrainUv.x) * TotalPageCount.x),
                    Mathf.FloorToInt(Mathf.Clamp01(terrainUv.y) * TotalPageCount.y));
                var newOrigin = new Vector2Int(
                    Mathf.Clamp(centerPage.x - WindowPageCount / 2, 0, Mathf.Max(0, TotalPageCount.x - WindowPageCount)),
                    Mathf.Clamp(centerPage.y - WindowPageCount / 2, 0, Mathf.Max(0, TotalPageCount.y - WindowPageCount)));
                if (m_WindowInitialized && newOrigin == WindowPageOrigin)
                    return;

                Vector2Int previousOrigin = WindowPageOrigin;
                WindowPageOrigin = newOrigin;
                if (!m_WindowInitialized)
                {
                    m_WindowInitialized = true;
                    MarkAllDirty();
                    return;
                }

                Vector2Int delta = newOrigin - previousOrigin;
                if (Mathf.Abs(delta.x) >= WindowPageCount || Mathf.Abs(delta.y) >= WindowPageCount)
                {
                    MarkAllDirty();
                    flushRegions.Add(new VTPageRegion(0, PageRegion));
                    return;
                }

                var previousWindow = new RectInt(
                    previousOrigin.x,
                    previousOrigin.y,
                    WindowPageCount,
                    WindowPageCount);
                for (int localY = 0; localY < WindowPageCount; localY++)
                {
                    for (int localX = 0; localX < WindowPageCount; localX++)
                    {
                        var logicalPage = new Vector2Int(newOrigin.x + localX, newOrigin.y + localY);
                        if (previousWindow.Contains(logicalPage))
                            continue;

                        int ringX = PositiveModulo(logicalPage.x, WindowPageCount);
                        int ringY = PositiveModulo(logicalPage.y, WindowPageCount);
                        int cellIndex = ringY * WindowPageCount + ringX;
                        m_Dirty[cellIndex] = true;
                        m_Approved[cellIndex] = false;
                        m_Resident[cellIndex] = false;
                        m_FeedbackHitCounts[cellIndex] = 0;
                        flushRegions.Add(new VTPageRegion(
                            0,
                            new RectInt(PageRegion.x + ringX, PageRegion.y + ringY, 1, 1)));
                    }
                }
            }

            internal void RetireResidentPages(int spaceId)
            {
                for (int cellIndex = 0; cellIndex < PagesPerLevel; cellIndex++)
                {
                    VirtualTexturePageCoord coord = GetVirtualPageCoord(cellIndex);
                    bool exactResident =
                        VirtualTextureSystem.TryGetPageTableEntry(
                            spaceId,
                            coord,
                            out VirtualTexturePageTableEntry entry)
                        && entry.Resident
                        && entry.ResolvedMip == 0;
                    if (m_Approved[cellIndex])
                    {
                        bool uploadPending = entry.PendingUpload
                                             || VirtualTextureSystem.IsPageRefreshPending(spaceId, coord);
                        if (uploadPending || !exactResident)
                            continue;

                        m_Resident[cellIndex] = true;
                        m_ResidentGenerations[cellIndex] = m_UploadGenerations[cellIndex];
                        m_Approved[cellIndex] = false;
                        m_Dirty[cellIndex] = m_ResidentGenerations[cellIndex]
                                                   != m_ContentGenerations[cellIndex];
                        m_FeedbackHitCounts[cellIndex] = 0;
                        continue;
                    }

                    if (exactResident)
                    {
                        m_Resident[cellIndex] = true;
                        m_Dirty[cellIndex] = m_ResidentGenerations[cellIndex]
                                                   != m_ContentGenerations[cellIndex];
                        m_FeedbackHitCounts[cellIndex] = 0;
                        continue;
                    }

                    if (!m_Resident[cellIndex])
                        continue;

                    m_Resident[cellIndex] = false;
                    m_Dirty[cellIndex] = true;
                }
            }

            internal void GatherCandidates(List<PageCandidate> candidates)
            {
                var windowCenter = new Vector2(
                    WindowPageOrigin.x + WindowPageCount * 0.5f,
                    WindowPageOrigin.y + WindowPageCount * 0.5f);
                for (int cellIndex = 0; cellIndex < PagesPerLevel; cellIndex++)
                {
                    if (!m_Dirty[cellIndex] || m_Approved[cellIndex])
                        continue;

                    Vector2Int logicalPage = ResolveLogicalPage(cellIndex);
                    if (logicalPage.x < 0
                        || logicalPage.y < 0
                        || logicalPage.x >= TotalPageCount.x
                        || logicalPage.y >= TotalPageCount.y)
                    {
                        continue;
                    }

                    var pageCenter = new Vector2(logicalPage.x + 0.5f, logicalPage.y + 0.5f);
                    candidates.Add(new PageCandidate(
                        this,
                        cellIndex,
                        m_FeedbackHitCounts[cellIndex],
                        (pageCenter - windowCenter).sqrMagnitude));
                }
            }

            internal bool TryApproveAndQueue(int spaceId, int frameIndex, int cellIndex)
            {
                if (cellIndex < 0
                    || cellIndex >= PagesPerLevel
                    || !m_Dirty[cellIndex]
                    || m_Approved[cellIndex])
                {
                    return false;
                }

                m_Approved[cellIndex] = true;
                VirtualTexturePageCoord coord = GetVirtualPageCoord(cellIndex);
                bool queued = m_Resident[cellIndex]
                    ? VirtualTextureSystem.TryQueuePageRefresh(spaceId, coord, frameIndex)
                    : VirtualTextureSystem.TryQueuePageResident(
                        spaceId,
                        coord,
                        locked: false,
                        frameIndex: frameIndex);
                if (queued)
                {
                    m_UploadGenerations[cellIndex] = m_ContentGenerations[cellIndex];
                    m_FeedbackHitCounts[cellIndex] = 0;
                    return true;
                }

                m_Approved[cellIndex] = false;
                return false;
            }

            internal bool HasApprovedUploads
            {
                get
                {
                    for (int cellIndex = 0; cellIndex < PagesPerLevel; cellIndex++)
                    {
                        if (m_Approved[cellIndex])
                            return true;
                    }

                    return false;
                }
            }

            internal void SetPageDecalData(
                int cellIndex,
                int startIndex,
                int count,
                List<VirtualTexturePageCoord> dependencies)
            {
                m_PageDecalStarts[cellIndex] = startIndex;
                m_PageDecalCounts[cellIndex] = count;
                List<VirtualTexturePageCoord> target = m_PageSourceDependencies[cellIndex];
                target.Clear();
                if (dependencies != null)
                    target.AddRange(dependencies);
            }

            internal void MarkWorldBoundsDirty(Bounds worldBounds, uint generation)
            {
                for (int cellIndex = 0; cellIndex < PagesPerLevel; cellIndex++)
                {
                    Vector2Int logicalPage = ResolveLogicalPage(cellIndex);
                    if (logicalPage.x < 0
                        || logicalPage.y < 0
                        || logicalPage.x >= TotalPageCount.x
                        || logicalPage.y >= TotalPageCount.y
                        || !m_Owner.GetPageWorldBounds(this, logicalPage).Intersects(worldBounds))
                    {
                        continue;
                    }

                    m_Dirty[cellIndex] = true;
                    m_ContentGenerations[cellIndex] = generation;
                }
            }

            internal void Dispose()
            {
                foreach (List<VTPagePinLease> leases in m_UploadPinLeases.Values)
                {
                    for (int leaseIndex = 0; leaseIndex < leases.Count; leaseIndex++)
                        leases[leaseIndex].Dispose();
                }
                m_UploadPinLeases.Clear();
            }

            internal TerrainRuntimeVirtualTextureLevelGPUData CreateGPUData()
            {
                return new TerrainRuntimeVirtualTextureLevelGPUData
                {
                    AtlasPageOrigin = new uint2((uint)PageRegion.x, (uint)PageRegion.y),
                    WindowPageOrigin = new int2(WindowPageOrigin.x, WindowPageOrigin.y),
                    TotalPageCount = new uint2((uint)TotalPageCount.x, (uint)TotalPageCount.y),
                    Padding0 = 0u,
                };
            }

            private void MarkAllDirty()
            {
                Array.Fill(m_Dirty, true);
                Array.Clear(m_Approved, 0, m_Approved.Length);
                Array.Clear(m_Resident, 0, m_Resident.Length);
                Array.Clear(m_FeedbackHitCounts, 0, m_FeedbackHitCounts.Length);
                uint generation = m_Owner != null ? m_Owner.m_ContentGeneration : 1u;
                for (int cellIndex = 0; cellIndex < m_ContentGenerations.Length; cellIndex++)
                    m_ContentGenerations[cellIndex] = generation;
            }

            private bool TryGetCellIndex(int pageX, int pageY, out int cellIndex)
            {
                int localX = pageX - PageRegion.x;
                int localY = pageY - PageRegion.y;
                if ((uint)localX >= WindowPageCount || (uint)localY >= WindowPageCount)
                {
                    cellIndex = -1;
                    return false;
                }

                cellIndex = localY * WindowPageCount + localX;
                return true;
            }

            internal Vector2Int ResolveLogicalPage(int cellIndex)
            {
                int ringX = cellIndex % WindowPageCount;
                int ringY = cellIndex / WindowPageCount;
                return new Vector2Int(
                    WindowPageOrigin.x + PositiveModulo(ringX - WindowPageOrigin.x, WindowPageCount),
                    WindowPageOrigin.y + PositiveModulo(ringY - WindowPageOrigin.y, WindowPageCount));
            }

            private VirtualTexturePageCoord GetVirtualPageCoord(int cellIndex)
            {
                return new VirtualTexturePageCoord(
                    PageRegion.x + cellIndex % WindowPageCount,
                    PageRegion.y + cellIndex / WindowPageCount,
                    0);
            }

            private static long GetUploadKey(in VTRequest request)
            {
                return ((long)(uint)request.PhysicalPageId << 32)
                       | (uint)request.Generation;
            }

            private static int[] CreateTextureIds(string prefix)
            {
                var ids = new int[VividTerrainData.MaximumSurfaceLayerCount];
                for (int index = 0; index < ids.Length; index++)
                    ids[index] = Shader.PropertyToID(prefix + index);
                return ids;
            }
        }

        private readonly VividTerrain m_Terrain;
        private readonly VividTerrainData m_TerrainData;
        private readonly VirtualTextureGPUDrivenTextureBackend m_Backend;
        private readonly ComputeShader m_PageProducerCompute;
        private readonly int m_PageProducerKernel;
        private readonly int m_LayerCount;
        private readonly int m_ControlCount;
        private readonly Vector4[] m_LayerTilingOffsets = new Vector4[VividTerrainData.MaximumSurfaceLayerCount];
        private readonly Vector4[] m_LayerMaterialParams = new Vector4[VividTerrainData.MaximumSurfaceLayerCount];
        private readonly float[] m_VirtualTextureSpaceParams = new float[33];
        private readonly float[] m_VirtualTextureMipOffsets =
            new float[VirtualTextureFeedbackProcessor.MaxMipCount];
        private readonly Vector4[] m_VirtualTextureLayerFallbacks =
            new Vector4[VTStackDesc.MaxLayerCount];
        private readonly List<RuntimeDecal> m_RuntimeDecals = new();
        private readonly List<TerrainRuntimeVirtualTextureDecalGPUData> m_DecalDataUpload = new();
        private readonly List<TerrainRuntimeVirtualTexturePageDecalIndexGPUData> m_PageDecalIndexUpload = new();
        private readonly List<Bounds> m_PendingDirtyBounds = new();
        private readonly List<TerrainVirtualTextureDecalData> m_PendingDecals = new();
        private readonly Dictionary<EntityId, TerrainVirtualTextureDecalData> m_AppliedSnapshotDecals = new();
        private readonly Dictionary<EntityId, TerrainVirtualTextureDecalData> m_PendingSnapshotDecals = new();
        private readonly List<VirtualTexturePageCoord> m_PageDependencyScratch = new();
        private readonly HashSet<VirtualTexturePageCoord> m_PageDependencySet = new();
        private GraphicsBuffer m_DecalDataBuffer;
        private GraphicsBuffer m_PageDecalIndexBuffer;
        private uint m_PendingDecalRevision;
        private uint m_ContentGeneration = 1u;
        private bool m_HasPendingDecalSnapshot;
        private bool m_ReceiveDecals;
        private bool m_PendingReceiveDecals;
        private bool m_MaterialReceivesDecals;

        private sealed class RuntimeDecal : IDisposable
        {
            internal TerrainVirtualTextureDecalData Source;
            internal VirtualTextureGPUDrivenTextureBackend.ExternalSurfaceBindingLease BindingLease;
            internal int GPUDataIndex;

            public void Dispose()
            {
                BindingLease?.Dispose();
                BindingLease = null;
            }
        }

        internal TerrainRuntimeVirtualTextureClipmap(
            VividTerrain terrain,
            VividTerrainData terrainData,
            VirtualTextureGPUDrivenTextureBackend backend,
            ComputeShader pageProducerCompute,
            RectInt[] pageRegions,
            uint revision)
        {
            m_Terrain = terrain != null ? terrain : throw new ArgumentNullException(nameof(terrain));
            m_TerrainData = terrainData != null ? terrainData : throw new ArgumentNullException(nameof(terrainData));
            m_Backend = backend != null ? backend : throw new ArgumentNullException(nameof(backend));
            m_PageProducerCompute = pageProducerCompute != null
                ? pageProducerCompute
                : throw new ArgumentNullException(nameof(pageProducerCompute));
            if (!m_PageProducerCompute.HasKernel("CS"))
                throw new ArgumentException("Terrain RVT page producer is missing the 'CS' kernel.", nameof(pageProducerCompute));
            if (pageRegions == null || pageRegions.Length != LevelCount)
                throw new ArgumentException("Terrain RVT requires three atlas page regions.", nameof(pageRegions));

            m_PageProducerKernel = m_PageProducerCompute.FindKernel("CS");
            m_LayerCount = terrainData.SupportedSurfaceLayerCount;
            m_ControlCount = terrainData.RequiredControlMapCount;
            Revision = revision;
            EntityId = terrain.GetEntityId();
            Levels = new Level[LevelCount];
            VividVirtualTextureAsset composite = terrainData.CompositeVirtualTexture;
            for (int levelIndex = 0; levelIndex < LevelCount; levelIndex++)
            {
                int scale = 1 << (LevelCount - levelIndex);
                Levels[levelIndex] = new Level(
                    this,
                    levelIndex,
                    pageRegions[levelIndex],
                    new Vector2Int(
                        Mathf.Max(1, composite.VirtualPageCountX * scale),
                        Mathf.Max(1, composite.VirtualPageCountY * scale)));
            }

            for (int layerIndex = 0; layerIndex < VividTerrainData.MaximumSurfaceLayerCount; layerIndex++)
            {
                if (layerIndex >= m_LayerCount)
                {
                    m_LayerTilingOffsets[layerIndex] = Vector4.zero;
                    m_LayerMaterialParams[layerIndex] = Vector4.zero;
                    continue;
                }

                VividTerrainLayerData layer = terrainData.Layers[layerIndex];
                m_LayerTilingOffsets[layerIndex] = VividTerrainSurfaceUtility.GetLayerTilingOffset(
                    terrainData.Size,
                    layer.TileSize,
                    layer.TileOffset);
                int presence = (layer.DiffuseTexture != null ? 1 : 0)
                               | (layer.NormalMapTexture != null ? 2 : 0)
                               | (layer.MaskMapTexture != null ? 4 : 0);
                m_LayerMaterialParams[layerIndex] = new Vector4(
                    layer.NormalScale,
                    layer.Metallic,
                    layer.Smoothness,
                    presence);
            }

            EnsureBuffer(
                ref m_DecalDataBuffer,
                1,
                Marshal.SizeOf<TerrainRuntimeVirtualTextureDecalGPUData>(),
                "VividTerrainRVTDecals");
            EnsureBuffer(
                ref m_PageDecalIndexBuffer,
                1,
                Marshal.SizeOf<TerrainRuntimeVirtualTexturePageDecalIndexGPUData>(),
                "VividTerrainRVTPageDecalIndices");
        }

        internal EntityId EntityId { get; }

        internal VividTerrain Terrain => m_Terrain;

        internal VividTerrainData TerrainData => m_TerrainData;

        internal uint Revision { get; }

        internal uint RecordIndex { get; set; }

        internal uint LastTouchedUpdate { get; set; }

        internal uint CreatedUpdate { get; set; }

        internal Level[] Levels { get; }

        internal TerrainRuntimeVirtualTextureRecordGPUData CreateGPUData(uint levelStartIndex)
        {
            Matrix4x4 worldToLocal = m_Terrain.transform.worldToLocalMatrix;
            Vector3 terrainSize = m_TerrainData.Size;
            float inverseSizeX = Mathf.Abs(terrainSize.x) > Mathf.Epsilon
                ? 1.0f / terrainSize.x
                : 0.0f;
            float inverseSizeZ = Mathf.Abs(terrainSize.z) > Mathf.Epsilon
                ? 1.0f / terrainSize.z
                : 0.0f;
            Bounds localBounds = m_TerrainData.LocalBounds;
            return new TerrainRuntimeVirtualTextureRecordGPUData
            {
                LevelStartIndex = levelStartIndex,
                LevelCount = TerrainRuntimeVirtualTextureClipmap.LevelCount,
                Revision = Revision,
                Padding0 = (uint)((m_MaterialReceivesDecals ? TerrainRuntimeVirtualTextureRecordFlags.ReceiveDecals : 0)
                                  | (m_ReceiveDecals && m_RuntimeDecals.Count > 0
                                      ? TerrainRuntimeVirtualTextureRecordFlags.HasDecalCache
                                      : 0)),
                WorldToTerrainUvX = new float4(
                    worldToLocal.m00 * inverseSizeX,
                    worldToLocal.m01 * inverseSizeX,
                    worldToLocal.m02 * inverseSizeX,
                    worldToLocal.m03 * inverseSizeX),
                WorldToTerrainUvY = new float4(
                    worldToLocal.m20 * inverseSizeZ,
                    worldToLocal.m21 * inverseSizeZ,
                    worldToLocal.m22 * inverseSizeZ,
                    worldToLocal.m23 * inverseSizeZ),
                WorldToTerrainLocalY = new float4(
                    worldToLocal.m10,
                    worldToLocal.m11,
                    worldToLocal.m12,
                    worldToLocal.m13),
                LocalHeightRange = new float2(localBounds.min.y, localBounds.max.y),
            };
        }

        internal void QueueDecalSnapshot(
            in TerrainVirtualTextureDecalSnapshot snapshot,
            bool enableVirtualTextureDecals,
            bool materialReceivesDecals)
        {
            bool supportsProjection = enableVirtualTextureDecals
                                      && materialReceivesDecals
                                      && m_TerrainData.TryValidateRuntimeDecalProjection(out _);
            bool snapshotChanged = snapshot.Revision != m_PendingDecalRevision;
            bool pendingReceiveChanged = supportsProjection != m_PendingReceiveDecals;
            bool materialReceiveChanged = materialReceivesDecals != m_MaterialReceivesDecals;
            m_MaterialReceivesDecals = materialReceivesDecals;
            if (!snapshotChanged
                && !pendingReceiveChanged
                && !materialReceiveChanged
                && !m_HasPendingDecalSnapshot)
                return;

            if (snapshotChanged)
            {
                m_PendingDecalRevision = snapshot.Revision;
                m_PendingDecals.Clear();
                for (int decalIndex = 0; decalIndex < snapshot.Decals.Count; decalIndex++)
                    m_PendingDecals.Add(snapshot.Decals[decalIndex]);
            }

            if (snapshotChanged || pendingReceiveChanged)
            {
                RebuildPendingSnapshotDirtyBounds();
                if (supportsProjection != m_ReceiveDecals)
                    m_PendingDirtyBounds.Add(GetTerrainWorldBounds());
            }

            m_PendingReceiveDecals = supportsProjection;
            m_HasPendingDecalSnapshot = true;
            TryApplyPendingDecalSnapshot();
        }

        internal void TryApplyPendingDecalSnapshot()
        {
            if (!m_HasPendingDecalSnapshot || HasApprovedUploads())
                return;

            for (int decalIndex = 0; decalIndex < m_RuntimeDecals.Count; decalIndex++)
                m_RuntimeDecals[decalIndex].Dispose();
            m_RuntimeDecals.Clear();
            m_DecalDataUpload.Clear();

            Bounds terrainBounds = GetTerrainWorldBounds();
            if (m_PendingReceiveDecals)
            {
                for (int decalIndex = 0; decalIndex < m_PendingDecals.Count; decalIndex++)
                {
                    TerrainVirtualTextureDecalData decal = m_PendingDecals[decalIndex];
                    if (!terrainBounds.Intersects(decal.WorldBounds))
                        continue;
                    if (!m_Backend.TryAcquireExternalSurfaceBinding(
                            decal.VirtualTextureAsset,
                            out VirtualTextureGPUDrivenTextureBackend.ExternalSurfaceBindingLease bindingLease,
                            out string reason))
                    {
                        m_Backend.WarnInvalidTerrainDecal(decal.Projector, reason);
                        continue;
                    }

                    Matrix4x4 decalToWorld = decal.WorldToDecal.inverse;
                    var runtimeDecal = new RuntimeDecal
                    {
                        Source = decal,
                        BindingLease = bindingLease,
                        GPUDataIndex = m_DecalDataUpload.Count,
                    };
                    m_RuntimeDecals.Add(runtimeDecal);
                    m_DecalDataUpload.Add(new TerrainRuntimeVirtualTextureDecalGPUData
                    {
                        WorldToDecal = (float4x4)decal.WorldToDecal,
                        BaseColor = (Vector4)decal.BaseColor,
                        SurfaceBinding = bindingLease.Binding,
                        TangentWS = new float4(
                            decalToWorld.MultiplyVector(Vector3.right).normalized,
                            0.0f),
                        BitangentWS = new float4(
                            decalToWorld.MultiplyVector(Vector3.forward).normalized,
                            0.0f),
                        NormalWS = new float4(
                            decalToWorld.MultiplyVector(Vector3.up).normalized,
                            0.0f),
                        BlendDistance = decal.NormalizedBlendDistance,
                        Metallic = decal.Metallic,
                        Roughness = decal.Roughness,
                    });
                }
            }

            IncrementContentGeneration();
            RebuildPageDecalData();
            EnsureBuffer(
                ref m_DecalDataBuffer,
                Mathf.Max(1, m_DecalDataUpload.Count),
                Marshal.SizeOf<TerrainRuntimeVirtualTextureDecalGPUData>(),
                "VividTerrainRVTDecals");
            if (m_DecalDataUpload.Count > 0)
                m_DecalDataBuffer.SetData(m_DecalDataUpload);
            UploadPageDecalIndexBuffer();

            for (int dirtyIndex = 0; dirtyIndex < m_PendingDirtyBounds.Count; dirtyIndex++)
            {
                Bounds dirtyBounds = m_PendingDirtyBounds[dirtyIndex];
                for (int levelIndex = 0; levelIndex < Levels.Length; levelIndex++)
                    Levels[levelIndex].MarkWorldBoundsDirty(dirtyBounds, m_ContentGeneration);
            }

            m_PendingDirtyBounds.Clear();
            m_AppliedSnapshotDecals.Clear();
            foreach (KeyValuePair<EntityId, TerrainVirtualTextureDecalData> pair
                     in m_PendingSnapshotDecals)
            {
                m_AppliedSnapshotDecals.Add(pair.Key, pair.Value);
            }
            m_ReceiveDecals = m_PendingReceiveDecals;
            m_HasPendingDecalSnapshot = false;
        }

        private void RebuildPendingSnapshotDirtyBounds()
        {
            m_PendingDirtyBounds.Clear();
            m_PendingSnapshotDecals.Clear();
            for (int decalIndex = 0; decalIndex < m_PendingDecals.Count; decalIndex++)
            {
                TerrainVirtualTextureDecalData current = m_PendingDecals[decalIndex];
                m_PendingSnapshotDecals[current.EntityId] = current;
                if (!m_AppliedSnapshotDecals.TryGetValue(
                        current.EntityId,
                        out TerrainVirtualTextureDecalData previous))
                {
                    m_PendingDirtyBounds.Add(current.WorldBounds);
                    continue;
                }

                if (!DecalSystem.VirtualTextureDataEquals(previous, current))
                {
                    Bounds union = previous.WorldBounds;
                    union.Encapsulate(current.WorldBounds);
                    m_PendingDirtyBounds.Add(union);
                }
            }

            foreach (KeyValuePair<EntityId, TerrainVirtualTextureDecalData> previous
                     in m_AppliedSnapshotDecals)
            {
                if (!m_PendingSnapshotDecals.ContainsKey(previous.Key))
                    m_PendingDirtyBounds.Add(previous.Value.WorldBounds);
            }
        }

        internal Bounds GetPageWorldBounds(Level level, Vector2Int logicalPage)
        {
            Vector3 terrainSize = m_TerrainData.Size;
            Bounds localBounds = m_TerrainData.LocalBounds;
            float minX = logicalPage.x / (float)level.TotalPageCount.x * terrainSize.x;
            float maxX = (logicalPage.x + 1) / (float)level.TotalPageCount.x * terrainSize.x;
            float minZ = logicalPage.y / (float)level.TotalPageCount.y * terrainSize.z;
            float maxZ = (logicalPage.y + 1) / (float)level.TotalPageCount.y * terrainSize.z;
            Matrix4x4 localToWorld = m_Terrain.transform.localToWorldMatrix;
            Vector3 first = localToWorld.MultiplyPoint3x4(new Vector3(minX, localBounds.min.y, minZ));
            var bounds = new Bounds(first, Vector3.zero);
            for (int corner = 1; corner < 8; corner++)
            {
                bounds.Encapsulate(localToWorld.MultiplyPoint3x4(new Vector3(
                    (corner & 1) != 0 ? maxX : minX,
                    (corner & 2) != 0 ? localBounds.max.y : localBounds.min.y,
                    (corner & 4) != 0 ? maxZ : minZ)));
            }
            return bounds;
        }

        private Bounds GetTerrainWorldBounds()
        {
            Bounds localBounds = m_TerrainData.LocalBounds;
            Matrix4x4 localToWorld = m_Terrain.transform.localToWorldMatrix;
            Vector3 first = localToWorld.MultiplyPoint3x4(localBounds.min);
            var bounds = new Bounds(first, Vector3.zero);
            for (int corner = 1; corner < 8; corner++)
            {
                bounds.Encapsulate(localToWorld.MultiplyPoint3x4(new Vector3(
                    (corner & 1) != 0 ? localBounds.max.x : localBounds.min.x,
                    (corner & 2) != 0 ? localBounds.max.y : localBounds.min.y,
                    (corner & 4) != 0 ? localBounds.max.z : localBounds.min.z)));
            }
            return bounds;
        }

        private bool HasApprovedUploads()
        {
            for (int levelIndex = 0; levelIndex < Levels.Length; levelIndex++)
            {
                if (Levels[levelIndex].HasApprovedUploads)
                    return true;
            }
            return false;
        }

        private void RebuildPageDecalData()
        {
            m_PageDecalIndexUpload.Clear();
            for (int levelIndex = 0; levelIndex < Levels.Length; levelIndex++)
            {
                Level level = Levels[levelIndex];
                for (int cellIndex = 0; cellIndex < PagesPerLevel; cellIndex++)
                {
                    Vector2Int logicalPage = level.ResolveLogicalPage(cellIndex);
                    int startIndex = m_PageDecalIndexUpload.Count;
                    m_PageDependencyScratch.Clear();
                    m_PageDependencySet.Clear();
                    if (logicalPage.x >= 0
                        && logicalPage.y >= 0
                        && logicalPage.x < level.TotalPageCount.x
                        && logicalPage.y < level.TotalPageCount.y)
                    {
                        Bounds pageBounds = GetPageWorldBounds(level, logicalPage);
                        for (int decalIndex = 0; decalIndex < m_RuntimeDecals.Count; decalIndex++)
                        {
                            RuntimeDecal decal = m_RuntimeDecals[decalIndex];
                            if (!pageBounds.Intersects(decal.Source.WorldBounds))
                                continue;

                            int sourceMip = ResolveSourceMip(level, decal);
                            m_PageDecalIndexUpload.Add(
                                new TerrainRuntimeVirtualTexturePageDecalIndexGPUData
                                {
                                    DecalIndex = (uint)decal.GPUDataIndex,
                                    SourceMip = (uint)sourceMip,
                                });
                            GatherSourceDependencies(
                                level,
                                logicalPage,
                                decal,
                                sourceMip,
                                m_PageDependencySet,
                                m_PageDependencyScratch);
                        }
                    }

                    level.SetPageDecalData(
                        cellIndex,
                        startIndex,
                        m_PageDecalIndexUpload.Count - startIndex,
                        m_PageDependencyScratch);
                }
            }
        }

        private void UploadPageDecalIndexBuffer()
        {
            EnsureBuffer(
                ref m_PageDecalIndexBuffer,
                Mathf.Max(1, m_PageDecalIndexUpload.Count),
                Marshal.SizeOf<TerrainRuntimeVirtualTexturePageDecalIndexGPUData>(),
                "VividTerrainRVTPageDecalIndices");
            if (m_PageDecalIndexUpload.Count > 0)
                m_PageDecalIndexBuffer.SetData(m_PageDecalIndexUpload);
        }

        private int ResolveSourceMip(Level level, RuntimeDecal decal)
        {
            Vector3 terrainSize = m_TerrainData.Size;
            Matrix4x4 localToWorld = m_Terrain.transform.localToWorldMatrix;
            Vector3 worldDx = localToWorld.MultiplyVector(new Vector3(
                terrainSize.x / (level.TotalPageCount.x * VirtualTextureGPUDrivenTextureBackend.PageSize),
                0.0f,
                0.0f));
            Vector3 worldDz = localToWorld.MultiplyVector(new Vector3(
                0.0f,
                0.0f,
                terrainSize.z / (level.TotalPageCount.y * VirtualTextureGPUDrivenTextureBackend.PageSize)));
            Vector3 decalDx = decal.Source.WorldToDecal.MultiplyVector(worldDx);
            Vector3 decalDz = decal.Source.WorldToDecal.MultiplyVector(worldDz);
            float sourceWidth = decal.BindingLease.PageRegion.width
                                * VirtualTextureGPUDrivenTextureBackend.PageSize;
            float sourceHeight = decal.BindingLease.PageRegion.height
                                 * VirtualTextureGPUDrivenTextureBackend.PageSize;
            float footprint = Mathf.Max(
                new Vector2(decalDx.x * sourceWidth, decalDx.z * sourceHeight).magnitude,
                new Vector2(decalDz.x * sourceWidth, decalDz.z * sourceHeight).magnitude);
            return Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Log(Mathf.Max(1.0f, footprint), 2.0f)),
                0,
                decal.BindingLease.MaxMip);
        }

        private void GatherSourceDependencies(
            Level level,
            Vector2Int logicalPage,
            RuntimeDecal decal,
            int sourceMip,
            HashSet<VirtualTexturePageCoord> dependencySet,
            List<VirtualTexturePageCoord> dependencies)
        {
            Bounds pageBounds = GetPageWorldBounds(level, logicalPage);
            Vector3 min = pageBounds.min;
            Vector3 max = pageBounds.max;
            Vector2 uvMin = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 uvMax = new(float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 positionWS = new(
                    (corner & 1) != 0 ? max.x : min.x,
                    (corner & 2) != 0 ? max.y : min.y,
                    (corner & 4) != 0 ? max.z : min.z);
                Vector3 positionDS = decal.Source.WorldToDecal.MultiplyPoint3x4(positionWS);
                Vector2 uv = new(positionDS.x + 0.5f, positionDS.z + 0.5f);
                uvMin = Vector2.Min(uvMin, uv);
                uvMax = Vector2.Max(uvMax, uv);
            }

            uvMin = Vector2.Max(Vector2.zero, uvMin);
            uvMax = Vector2.Min(Vector2.one, uvMax);
            if (uvMin.x > uvMax.x || uvMin.y > uvMax.y)
                return;

            int pageCountX = Mathf.Max(1, decal.BindingLease.PageRegion.width >> sourceMip);
            int pageCountY = Mathf.Max(1, decal.BindingLease.PageRegion.height >> sourceMip);
            int minPageX = Mathf.Clamp(Mathf.FloorToInt(uvMin.x * pageCountX), 0, pageCountX - 1);
            int minPageY = Mathf.Clamp(Mathf.FloorToInt(uvMin.y * pageCountY), 0, pageCountY - 1);
            int maxPageX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uvMax.x, 0.999999f) * pageCountX), 0, pageCountX - 1);
            int maxPageY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uvMax.y, 0.999999f) * pageCountY), 0, pageCountY - 1);
            int atlasOriginX = decal.BindingLease.PageRegion.x >> sourceMip;
            int atlasOriginY = decal.BindingLease.PageRegion.y >> sourceMip;
            for (int pageY = minPageY; pageY <= maxPageY; pageY++)
            {
                for (int pageX = minPageX; pageX <= maxPageX; pageX++)
                {
                    var coord = new VirtualTexturePageCoord(
                        atlasOriginX + pageX,
                        atlasOriginY + pageY,
                        sourceMip);
                    if (dependencySet.Add(coord))
                        dependencies.Add(coord);
                }
            }
        }

        private void IncrementContentGeneration()
        {
            unchecked
            {
                m_ContentGeneration++;
                if (m_ContentGeneration == 0u)
                    m_ContentGeneration = 1u;
            }
        }

        private bool HasPermanentSourceFailure(in VirtualTexturePageCoord coord)
        {
            int basePageX = coord.X << coord.Mip;
            int basePageY = coord.Y << coord.Mip;
            var basePage = new Vector2Int(basePageX, basePageY);
            for (int decalIndex = 0; decalIndex < m_RuntimeDecals.Count; decalIndex++)
            {
                RuntimeDecal decal = m_RuntimeDecals[decalIndex];
                if (!decal.BindingLease.PageRegion.Contains(basePage)
                    || !m_Backend.HasPermanentStreamFailure(decal.BindingLease.PageRegion))
                {
                    continue;
                }

                m_Backend.WarnInvalidTerrainDecal(
                    decal.Source.Projector,
                    "its virtual texture source has a permanent streaming failure");
                return true;
            }

            return false;
        }

        private static void EnsureBuffer(
            ref GraphicsBuffer buffer,
            int count,
            int stride,
            string name)
        {
            if (buffer != null && buffer.count >= count && buffer.stride == stride)
                return;

            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, count), stride)
            {
                name = name,
            };
        }

        internal void UpdateCamera(Vector3 cameraPosition, List<VTPageRegion> flushRegions)
        {
            Vector3 localPosition = m_Terrain.transform.InverseTransformPoint(cameraPosition);
            Vector3 terrainSize = m_TerrainData.Size;
            var terrainUv = new Vector2(
                Mathf.Abs(terrainSize.x) > Mathf.Epsilon ? localPosition.x / terrainSize.x : 0.0f,
                Mathf.Abs(terrainSize.z) > Mathf.Epsilon ? localPosition.z / terrainSize.z : 0.0f);
            for (int levelIndex = 0; levelIndex < Levels.Length; levelIndex++)
                Levels[levelIndex].UpdateWindow(terrainUv, flushRegions);
            RebuildPageDecalData();
            UploadPageDecalIndexBuffer();
        }

        private void BindSourceVirtualTextureResources(
            CommandBuffer cmd,
            ComputeShader shader,
            int kernel)
        {
            if (!VirtualTextureSystem.TryGetSpaceBinding(
                    m_Backend.VirtualTextureSpaceId,
                    out VirtualTextureSpaceBinding binding))
            {
                return;
            }

            Array.Clear(m_VirtualTextureSpaceParams, 0, m_VirtualTextureSpaceParams.Length);
            Array.Clear(m_VirtualTextureMipOffsets, 0, m_VirtualTextureMipOffsets.Length);
            Array.Clear(m_VirtualTextureLayerFallbacks, 0, m_VirtualTextureLayerFallbacks.Length);
            binding.ShaderParams.CopyTo(m_VirtualTextureSpaceParams);
            int mipOffsetCount = binding.MipOffsets != null
                ? Mathf.Min(binding.MipOffsets.Length, m_VirtualTextureMipOffsets.Length)
                : 0;
            for (int mipIndex = 0; mipIndex < mipOffsetCount; mipIndex++)
                m_VirtualTextureMipOffsets[mipIndex] = binding.MipOffsets[mipIndex];
            Array.Copy(
                binding.LayerFallbacks,
                m_VirtualTextureLayerFallbacks,
                Mathf.Min(binding.LayerFallbacks.Length, m_VirtualTextureLayerFallbacks.Length));

            cmd.SetComputeBufferParam(
                shader,
                kernel,
                VirtualTextureShaderIDs._VTPageTable,
                binding.PageTableBuffer);
            Texture2D fallback = binding.PhysicalCache;
            for (int physicalGroup = 0;
                 physicalGroup < VirtualTextureShaderIDs.PhysicalCaches.Length;
                 physicalGroup++)
            {
                Texture2D cache = physicalGroup < binding.PhysicalCaches.Count
                    ? binding.PhysicalCaches[physicalGroup]
                    : null;
                cmd.SetComputeTextureParam(
                    shader,
                    kernel,
                    VirtualTextureShaderIDs.PhysicalCaches[physicalGroup],
                    cache != null ? cache : fallback);
            }

            cmd.SetComputeFloatParams(
                shader,
                VirtualTextureShaderIDs._VTSpaceParams,
                m_VirtualTextureSpaceParams);
            cmd.SetComputeFloatParams(
                shader,
                VirtualTextureShaderIDs._VTMipOffsets,
                m_VirtualTextureMipOffsets);
            cmd.SetComputeVectorArrayParam(
                shader,
                VirtualTextureShaderIDs._VTLayerFallbacks,
                m_VirtualTextureLayerFallbacks);
        }

        public void Dispose()
        {
            for (int levelIndex = 0; levelIndex < Levels.Length; levelIndex++)
                Levels[levelIndex].Dispose();
            for (int decalIndex = 0; decalIndex < m_RuntimeDecals.Count; decalIndex++)
                m_RuntimeDecals[decalIndex].Dispose();
            m_RuntimeDecals.Clear();
            m_AppliedSnapshotDecals.Clear();
            m_PendingSnapshotDecals.Clear();
            m_DecalDataBuffer?.Dispose();
            m_DecalDataBuffer = null;
            m_PageDecalIndexBuffer?.Dispose();
            m_PageDecalIndexBuffer = null;
        }

        private static int PositiveModulo(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }
    }
}
