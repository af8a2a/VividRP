using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.VirtualTexture
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct TerrainRuntimeVirtualTextureRecordGPUData
    {
        internal uint LevelStartIndex;
        internal uint LevelCount;
        internal uint Revision;
        internal uint Padding0;
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
                cmd.SetComputeTextureParam(shader, kernel, s_OutputPagesId, stagingTexture);
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
                        VirtualTextureGPUDrivenTextureBackend.BorderSize,
                        VirtualTextureGPUDrivenTextureBackend.PageSize
                        + VirtualTextureGPUDrivenTextureBackend.BorderSize * 2,
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

                int physicalPageSize = VirtualTextureGPUDrivenTextureBackend.PageSize
                                       + VirtualTextureGPUDrivenTextureBackend.BorderSize * 2;
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
                    if (exactResident)
                    {
                        m_Approved[cellIndex] = false;
                        m_Resident[cellIndex] = true;
                        m_Dirty[cellIndex] = false;
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
                if (VirtualTextureSystem.TryQueuePageResident(
                        spaceId,
                        GetVirtualPageCoord(cellIndex),
                        locked: false,
                        frameIndex: frameIndex))
                {
                    m_FeedbackHitCounts[cellIndex] = 0;
                    return true;
                }

                m_Approved[cellIndex] = false;
                return false;
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

            private Vector2Int ResolveLogicalPage(int cellIndex)
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
        private readonly ComputeShader m_PageProducerCompute;
        private readonly int m_PageProducerKernel;
        private readonly int m_LayerCount;
        private readonly int m_ControlCount;
        private readonly Vector4[] m_LayerTilingOffsets = new Vector4[VividTerrainData.MaximumSurfaceLayerCount];
        private readonly Vector4[] m_LayerMaterialParams = new Vector4[VividTerrainData.MaximumSurfaceLayerCount];

        internal TerrainRuntimeVirtualTextureClipmap(
            VividTerrain terrain,
            VividTerrainData terrainData,
            ComputeShader pageProducerCompute,
            RectInt[] pageRegions,
            uint revision)
        {
            m_Terrain = terrain != null ? terrain : throw new ArgumentNullException(nameof(terrain));
            m_TerrainData = terrainData != null ? terrainData : throw new ArgumentNullException(nameof(terrainData));
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
        }

        internal EntityId EntityId { get; }

        internal uint Revision { get; }

        internal uint RecordIndex { get; set; }

        internal uint LastTouchedUpdate { get; set; }

        internal uint CreatedUpdate { get; set; }

        internal Level[] Levels { get; }

        internal void UpdateCamera(Vector3 cameraPosition, List<VTPageRegion> flushRegions)
        {
            Vector3 localPosition = m_Terrain.transform.InverseTransformPoint(cameraPosition);
            Vector3 terrainSize = m_TerrainData.Size;
            var terrainUv = new Vector2(
                Mathf.Abs(terrainSize.x) > Mathf.Epsilon ? localPosition.x / terrainSize.x : 0.0f,
                Mathf.Abs(terrainSize.z) > Mathf.Epsilon ? localPosition.z / terrainSize.z : 0.0f);
            for (int levelIndex = 0; levelIndex < Levels.Length; levelIndex++)
                Levels[levelIndex].UpdateWindow(terrainUv, flushRegions);
        }

        public void Dispose()
        {
        }

        private static int PositiveModulo(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }
    }
}
