using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum StrandHairDebugViewMode
    {
        ShadedPreview = 0,
        TotalCoverage = 1,
        LayerCount = 2,
        SegmentId = 3,
        NormalizedDepth12 = 4,
        Tangent = 5,
        BakedL1SHAO = 6,
        DirectSelfShadow = 7,
    }

    public static class StrandHairAtlasContract
    {
        public const int TileSize = 64;
        public const int TilePixels = TileSize * TileSize; // 4096
        public const int LayerCount = 4; // 3 ordered front layers + 1 additive coverage
        public const int TileUintStride = TilePixels * LayerCount; // 16384 uints = 64 KB/tile
        public const int DefaultMaxAtlasTiles = 512; // 2M pixels = 32 MB
        public const int HighMaxAtlasTiles = 1024;   // 4M pixels = 64 MB
    }

    /// <summary>
    /// Pure deterministic tile allocation result for one visible hair groom model.
    /// </summary>
    public struct StrandHairModelAtlasAllocation
    {
        public int EntryIndex;
        public Vector2Int TileOrigin;
        public Vector2Int TileGridSize;
        public int FirstAtlasTile;
        public int DownscaleFactor;
        public Vector2Int ComposeMinPixel;
        public Vector2Int ComposeSize;
        public float DepthRangeNear;
        public float DepthRangeInvSpan;
        public float CameraDistanceSq;
        public bool IsVisible;

        public int TileCount => TileGridSize.x * TileGridSize.y;
    }

    /// <summary>
    /// Zero-allocation deterministic screen-space tile allocator for the 64x64
    /// Tiled Visibility Atlas (IJGC SIGGRAPH 2025: per-model separate tiles +
    /// automatic x2/x4 downscale when overlapping grooms exceed budget).
    /// </summary>
    public static class StrandHairAtlasAllocator
    {
        private static readonly Vector3[] s_CornerOffsets =
        {
            new Vector3(-1f, -1f, -1f),
            new Vector3( 1f, -1f, -1f),
            new Vector3(-1f,  1f, -1f),
            new Vector3( 1f,  1f, -1f),
            new Vector3(-1f, -1f,  1f),
            new Vector3( 1f, -1f,  1f),
            new Vector3(-1f,  1f,  1f),
            new Vector3( 1f,  1f,  1f),
        };

        public static int AllocateModels(
            IReadOnlyList<HairGroomRenderEntry> entries,
            Matrix4x4 viewMatrix,
            Matrix4x4 viewProjMatrix,
            Vector3 cameraPosWS,
            float nearClipPlane,
            int screenWidth,
            int screenHeight,
            int maxAtlasTiles,
            List<StrandHairModelAtlasAllocation> destination,
            out int totalAllocatedTiles,
            out int chosenDownscale)
        {
            destination.Clear();
            totalAllocatedTiles = 0;
            chosenDownscale = 1;

            if (entries == null || entries.Count == 0 || screenWidth <= 0 || screenHeight <= 0 || maxAtlasTiles <= 0)
                return 0;

            float nearEps = Mathf.Max(nearClipPlane, 1e-3f);

            // 1. Project each model's world bounds and reject off-screen / behind-camera models
            for (int i = 0; i < entries.Count; i++)
            {
                HairGroomRenderEntry entry = entries[i];
                if (entry.Asset == null || entry.Asset.SegmentCount <= 0)
                    continue;

                if (!ProjectBoundsToScreen(
                        entry.WorldBounds,
                        viewMatrix,
                        viewProjMatrix,
                        nearEps,
                        screenWidth,
                        screenHeight,
                        out int minPxX,
                        out int minPxY,
                        out int maxPxX,
                        out int maxPxY,
                        out float minViewZ,
                        out float maxViewZ))
                {
                    continue;
                }

                Vector3 delta = entry.WorldBounds.center - cameraPosWS;
                float distSq = delta.sqrMagnitude;
                float span = Mathf.Max(maxViewZ - minViewZ, 1e-3f);

                destination.Add(new StrandHairModelAtlasAllocation
                {
                    EntryIndex = i,
                    ComposeMinPixel = new Vector2Int(minPxX, minPxY),
                    ComposeSize = new Vector2Int(maxPxX - minPxX + 1, maxPxY - minPxY + 1),
                    DepthRangeNear = Mathf.Max(minViewZ - 0.01f, 0.0f),
                    DepthRangeInvSpan = 1.0f / (span + 0.02f),
                    CameraDistanceSq = distSq,
                    IsVisible = true,
                });
            }

            int visibleCount = destination.Count;
            if (visibleCount == 0)
                return 0;

            // 2. Insertion sort back-to-front (largest CameraDistanceSq first) with zero allocations
            for (int i = 1; i < visibleCount; i++)
            {
                var key = destination[i];
                int j = i - 1;
                while (j >= 0 && destination[j].CameraDistanceSq < key.CameraDistanceSq)
                {
                    destination[j + 1] = destination[j];
                    j--;
                }
                destination[j + 1] = key;
            }

            // 3. Pick smallest downscale factor (1, 2, or 4) that fits all visible models in maxAtlasTiles
            int downscale = 1;
            while (downscale <= 4)
            {
                int needed = ComputeTileLayout(destination, screenWidth, screenHeight, downscale);
                if (needed <= maxAtlasTiles || downscale == 4)
                {
                    chosenDownscale = downscale;
                    totalAllocatedTiles = Mathf.Min(needed, maxAtlasTiles);
                    break;
                }
                downscale <<= 1;
            }

            // 4. Assign contiguous FirstAtlasTile ranges; drop tail models only if even x4 overflows
            int cursor = 0;
            for (int i = 0; i < visibleCount; i++)
            {
                var alloc = destination[i];
                int tiles = alloc.TileCount;
                if (tiles <= 0 || cursor + tiles > maxAtlasTiles)
                {
                    alloc.IsVisible = false;
                    alloc.FirstAtlasTile = 0;
                    alloc.TileGridSize = Vector2Int.zero;
                }
                else
                {
                    alloc.FirstAtlasTile = cursor;
                    cursor += tiles;
                }
                destination[i] = alloc;
            }

            totalAllocatedTiles = cursor;
            return visibleCount;
        }

        private static int ComputeTileLayout(
            List<StrandHairModelAtlasAllocation> list,
            int screenWidth,
            int screenHeight,
            int downscale)
        {
            int atlasW = Mathf.Max(1, (screenWidth + downscale - 1) / downscale);
            int atlasH = Mathf.Max(1, (screenHeight + downscale - 1) / downscale);
            int maxTileX = Mathf.Max(0, (atlasW - 1) / StrandHairAtlasContract.TileSize);
            int maxTileY = Mathf.Max(0, (atlasH - 1) / StrandHairAtlasContract.TileSize);

            int totalTiles = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var alloc = list[i];
                int minX = alloc.ComposeMinPixel.x / downscale;
                int minY = alloc.ComposeMinPixel.y / downscale;
                int maxX = (alloc.ComposeMinPixel.x + alloc.ComposeSize.x - 1) / downscale;
                int maxY = (alloc.ComposeMinPixel.y + alloc.ComposeSize.y - 1) / downscale;

                int tileMinX = Mathf.Clamp(minX / StrandHairAtlasContract.TileSize, 0, maxTileX);
                int tileMinY = Mathf.Clamp(minY / StrandHairAtlasContract.TileSize, 0, maxTileY);
                int tileMaxX = Mathf.Clamp(maxX / StrandHairAtlasContract.TileSize, 0, maxTileX);
                int tileMaxY = Mathf.Clamp(maxY / StrandHairAtlasContract.TileSize, 0, maxTileY);

                alloc.TileOrigin = new Vector2Int(tileMinX, tileMinY);
                alloc.TileGridSize = new Vector2Int(tileMaxX - tileMinX + 1, tileMaxY - tileMinY + 1);
                alloc.DownscaleFactor = downscale;
                list[i] = alloc;
                totalTiles += alloc.TileCount;
            }

            return totalTiles;
        }

        private static bool ProjectBoundsToScreen(
            Bounds worldBounds,
            Matrix4x4 viewMatrix,
            Matrix4x4 viewProjMatrix,
            float nearEps,
            int screenWidth,
            int screenHeight,
            out int minPxX,
            out int minPxY,
            out int maxPxX,
            out int maxPxY,
            out float minViewZ,
            out float maxViewZ)
        {
            Vector3 center = worldBounds.center;
            Vector3 extents = worldBounds.extents;

            minViewZ = float.PositiveInfinity;
            maxViewZ = float.NegativeInfinity;
            float minU = float.PositiveInfinity;
            float minV = float.PositiveInfinity;
            float maxU = float.NegativeInfinity;
            float maxV = float.NegativeInfinity;
            bool anyBehindNear = false;
            bool anyInFront = false;

            for (int c = 0; c < 8; c++)
            {
                Vector3 offset = s_CornerOffsets[c];
                Vector3 cornerWS = new Vector3(
                    center.x + extents.x * offset.x,
                    center.y + extents.y * offset.y,
                    center.z + extents.z * offset.z);

                Vector3 viewPos = viewMatrix.MultiplyPoint3x4(cornerWS);
                // Unity view space looks down -Z -> positive forward depth is -viewPos.z
                float vz = -viewPos.z;
                if (vz < minViewZ) minViewZ = vz;
                if (vz > maxViewZ) maxViewZ = vz;

                Vector4 clip = viewProjMatrix * new Vector4(cornerWS.x, cornerWS.y, cornerWS.z, 1.0f);
                if (clip.w <= nearEps)
                {
                    anyBehindNear = true;
                    continue;
                }

                anyInFront = true;
                float invW = 1.0f / clip.w;
                float ndcX = clip.x * invW;
                float ndcY = clip.y * invW;
                float u = ndcX * 0.5f + 0.5f;
                float v = SystemInfo.graphicsUVStartsAtTop ? (0.5f - ndcY * 0.5f) : (ndcY * 0.5f + 0.5f);

                if (u < minU) minU = u;
                if (v < minV) minV = v;
                if (u > maxU) maxU = u;
                if (v > maxV) maxV = v;
            }

            if (!anyInFront || maxViewZ <= nearEps)
            {
                minPxX = minPxY = maxPxX = maxPxY = 0;
                return false;
            }

            // Conservative fullscreen fallback when camera sits inside/intersects the near plane of bounds
            if (anyBehindNear)
            {
                minU = 0.0f;
                minV = 0.0f;
                maxU = 1.0f;
                maxV = 1.0f;
                minViewZ = Mathf.Max(minViewZ, nearEps);
            }

            if (maxU < 0.0f || minU > 1.0f || maxV < 0.0f || minV > 1.0f)
            {
                minPxX = minPxY = maxPxX = maxPxY = 0;
                return false;
            }

            const int padPx = 4;
            minPxX = Mathf.Clamp(Mathf.FloorToInt(minU * screenWidth) - padPx, 0, screenWidth - 1);
            minPxY = Mathf.Clamp(Mathf.FloorToInt(minV * screenHeight) - padPx, 0, screenHeight - 1);
            maxPxX = Mathf.Clamp(Mathf.CeilToInt(maxU * screenWidth) + padPx, 0, screenWidth - 1);
            maxPxY = Mathf.Clamp(Mathf.CeilToInt(maxV * screenHeight) + padPx, 0, screenHeight - 1);
            minViewZ = Mathf.Max(minViewZ, nearEps);
            maxViewZ = Mathf.Max(maxViewZ, minViewZ + 1e-3f);
            return maxPxX >= minPxX && maxPxY >= minPxY;
        }
    }

    /// <summary>
    /// Reusable GPU scratch buffers owned per active groom slot (zero steady-state allocations).
    /// </summary>
    public sealed class StrandHairModelScratchSlot : IDisposable
    {
        public GraphicsBuffer PostTransformClipBuffer { get; private set; }
        public GraphicsBuffer WorldPosRadiusBuffer { get; private set; }
        public GraphicsBuffer VisibleSegmentListBuffer { get; private set; }
        public GraphicsBuffer IndirectRasterArgsBuffer { get; private set; }
        public GraphicsBuffer VisibleVertexMaskBuffer { get; private set; }
        public GraphicsBuffer VertexShadingBuffer { get; private set; }

        public void EnsureCapacity(int vertexCount, int segmentCount)
        {
            if (vertexCount <= 0 || segmentCount <= 0)
                return;

            if (PostTransformClipBuffer == null
                || !PostTransformClipBuffer.IsValid()
                || PostTransformClipBuffer.count < vertexCount)
            {
                PostTransformClipBuffer?.Dispose();
                PostTransformClipBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    vertexCount,
                    sizeof(float) * 4)
                {
                    name = "StrandHair_PostTransformClip",
                };
            }

            if (WorldPosRadiusBuffer == null
                || !WorldPosRadiusBuffer.IsValid()
                || WorldPosRadiusBuffer.count < vertexCount)
            {
                WorldPosRadiusBuffer?.Dispose();
                WorldPosRadiusBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    vertexCount,
                    sizeof(float) * 4)
                {
                    name = "StrandHair_WorldPosRadius",
                };
            }

            if (VisibleSegmentListBuffer == null
                || !VisibleSegmentListBuffer.IsValid()
                || VisibleSegmentListBuffer.count < segmentCount)
            {
                VisibleSegmentListBuffer?.Dispose();
                VisibleSegmentListBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    segmentCount,
                    sizeof(uint))
                {
                    name = "StrandHair_VisibleSegmentList",
                };
            }

            if (IndirectRasterArgsBuffer == null || !IndirectRasterArgsBuffer.IsValid())
            {
                IndirectRasterArgsBuffer?.Dispose();
                IndirectRasterArgsBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                    4,
                    sizeof(uint))
                {
                    name = "StrandHair_IndirectRasterArgs",
                };
            }

            int maskUintCount = (vertexCount + 31) >> 5;
            if (VisibleVertexMaskBuffer == null
                || !VisibleVertexMaskBuffer.IsValid()
                || VisibleVertexMaskBuffer.count < maskUintCount)
            {
                VisibleVertexMaskBuffer?.Dispose();
                VisibleVertexMaskBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    maskUintCount,
                    sizeof(uint))
                {
                    name = "StrandHair_VisibleVertexMask",
                };
            }

            if (VertexShadingBuffer == null
                || !VertexShadingBuffer.IsValid()
                || VertexShadingBuffer.count < vertexCount)
            {
                VertexShadingBuffer?.Dispose();
                VertexShadingBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    vertexCount,
                    sizeof(float) * 4)
                {
                    name = "StrandHair_VertexShading",
                };
            }
        }

        public void Dispose()
        {
            PostTransformClipBuffer?.Dispose();
            PostTransformClipBuffer = null;
            WorldPosRadiusBuffer?.Dispose();
            WorldPosRadiusBuffer = null;
            VisibleSegmentListBuffer?.Dispose();
            VisibleSegmentListBuffer = null;
            IndirectRasterArgsBuffer?.Dispose();
            IndirectRasterArgsBuffer = null;
            VisibleVertexMaskBuffer?.Dispose();
            VisibleVertexMaskBuffer = null;
            VertexShadingBuffer?.Dispose();
            VertexShadingBuffer = null;
        }
    }

    /// <summary>
    /// Frame-scoped state passed between StrandHairVisibilityPass and StrandHairCompositionPass
    /// via Unity's ContextContainer (zero per-frame heap allocations).
    /// </summary>
    public sealed class StrandHairFrameData : ContextItem
    {
        public readonly List<HairGroomRenderEntry> ActiveEntries = new List<HairGroomRenderEntry>(16);
        public readonly List<StrandHairModelAtlasAllocation> Allocations = new List<StrandHairModelAtlasAllocation>(16);

        public GraphicsBuffer VisAtlasBuffer;
        public int TotalAllocatedTiles;
        public int DownscaleFactor = 1;
        public int ScreenWidth;
        public int ScreenHeight;
        public float ProjScaleY;
        public bool HasVisibleHair;
        public bool VisibilityRecordedThisFrame;

        public override void Reset()
        {
            ActiveEntries.Clear();
            Allocations.Clear();
            VisAtlasBuffer = null;
            TotalAllocatedTiles = 0;
            DownscaleFactor = 1;
            ScreenWidth = 0;
            ScreenHeight = 0;
            ProjScaleY = 0.0f;
            HasVisibleHair = false;
            VisibilityRecordedThisFrame = false;
        }
    }

    /// <summary>
    /// Persistent GPU pool for the global Tiled Visibility Atlas and per-model scratch slots.
    /// </summary>
    public sealed class StrandHairGpuPool : IDisposable
    {
        public static StrandHairDebugViewMode GlobalDebugViewMode { get; set; } = StrandHairDebugViewMode.ShadedPreview;
        public static int GlobalMaxAtlasTiles { get; set; } = StrandHairAtlasContract.HighMaxAtlasTiles;

        private GraphicsBuffer m_VisAtlasBuffer;
        private int m_VisAtlasTileCapacity;
        private readonly List<StrandHairModelScratchSlot> m_Slots = new List<StrandHairModelScratchSlot>(8);

        public GraphicsBuffer EnsureVisAtlasBuffer(int maxTiles)
        {
            int targetTiles = Mathf.Clamp(maxTiles, 64, StrandHairAtlasContract.HighMaxAtlasTiles);
            if (m_VisAtlasBuffer == null
                || !m_VisAtlasBuffer.IsValid()
                || m_VisAtlasTileCapacity < targetTiles)
            {
                m_VisAtlasBuffer?.Dispose();
                m_VisAtlasBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    targetTiles * StrandHairAtlasContract.TileUintStride,
                    sizeof(uint))
                {
                    name = "StrandHair_TiledVisibilityAtlas",
                };
                m_VisAtlasTileCapacity = targetTiles;
            }

            return m_VisAtlasBuffer;
        }

        public StrandHairModelScratchSlot GetOrCreateSlot(int index, int vertexCount, int segmentCount)
        {
            while (m_Slots.Count <= index)
                m_Slots.Add(new StrandHairModelScratchSlot());

            var slot = m_Slots[index];
            slot.EnsureCapacity(vertexCount, segmentCount);
            return slot;
        }

        public void Dispose()
        {
            m_VisAtlasBuffer?.Dispose();
            m_VisAtlasBuffer = null;
            m_VisAtlasTileCapacity = 0;
            for (int i = 0; i < m_Slots.Count; i++)
                m_Slots[i]?.Dispose();
            m_Slots.Clear();
        }
    }
}
