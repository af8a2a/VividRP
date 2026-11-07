using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    internal static class VividGlobalSdfClipmap
    {
        // Keep the resolution and level count in sync with GlobalSdf.hlsl.
        internal const int Resolution = 128;
        internal const int LevelCount = 3;
        internal const int RegionsPerLevel = 3;
        internal const int BrickShift = 3;
        internal const int BrickSize = 1 << BrickShift;
        internal const int BrickResolution = Resolution >> BrickShift;
        internal const int PagesPerSlice = BrickResolution * BrickResolution;
        internal const int PagesPerLevel = PagesPerSlice * BrickResolution;
        internal const int PageCount = LevelCount * PagesPerLevel;
        internal const float Band = 8f;
        internal const int ThinBrickShift = 3;
        // An unaligned 128-cell window can span one extra brick on each axis.
        internal const int ThinBrickResolution = (Resolution >> ThinBrickShift) + 1;
        internal const int ThinBrickWordsPerLevel =
            (ThinBrickResolution * ThinBrickResolution * ThinBrickResolution + 31) / 32;
        internal const int ThinBrickWordCount = LevelCount * ThinBrickWordsPerLevel;
        internal const float DefaultVoxelSize = 0.125f;

        internal struct Region
        {
            public int3 Min;
            public int3 Size;
        }

        internal static void MarkRequiredPages(IReadOnlyList<VividMeshSdfGpuScene.Node> nodes, int3 origin,
            float voxel, uint layerMask, bool[] required)
        {
            Array.Clear(required, 0, PagesPerLevel);
            int3 last = origin + Resolution - 1;
            // Leave a small margin for the CPU/GPU world-position and box-distance arithmetic.
            float band = (Band + 0.001f) * voxel;
            float3 windowMin = (float3)origin * voxel;
            float3 windowMax = (float3)last * voxel;
            for (int index = 0; index < nodes.Count;)
            {
                var node = nodes[index];
                float3 min = node.Min;
                float3 max = node.Max;
                if ((node.LayerMask & layerMask) == 0 || math.any(min - band > windowMax)
                    || math.any(max + band < windowMin))
                {
                    index = (int)node.EscapeIndex;
                    continue;
                }
                index++;
                if (node.LeafCount == 0)
                    continue;

                // Composition limits distances by BVH leaf bounds, which can enclose multiple instances.
                int3 firstBrick = (int3)math.max(origin, math.floor((min - band) / voxel)) >> BrickShift;
                int3 lastBrick = (int3)math.min(last, math.ceil((max + band) / voxel)) >> BrickShift;
                for (int z = firstBrick.z; z <= lastBrick.z; z++)
                for (int y = firstBrick.y; y <= lastBrick.y; y++)
                for (int x = firstBrick.x; x <= lastBrick.x; x++)
                {
                    int3 brick = new int3(x, y, z);
                    int3 physical = brick & (BrickResolution - 1);
                    int page = physical.x + BrickResolution * physical.y + PagesPerSlice * physical.z;
                    if (required[page])
                        continue;
                    int3 cell = brick << BrickShift;
                    float3 sampleMin = (float3)math.max(origin, cell) * voxel;
                    float3 sampleMax = (float3)math.min(last, cell + BrickSize - 1) * voxel;
                    float3 gap = math.max(0f, math.max(min - sampleMax, sampleMin - max));
                    required[page] = math.lengthsq(gap) <= band * band;
                }
            }
        }

        internal static int GetUpdateRegions(int3 previous, int3 next, bool reset, Region[] regions, int offset,
            int resolution = Resolution)
        {
            int3 end = next + resolution;
            int3 overlapMin = math.max(previous, next);
            int3 overlapMax = math.min(previous + resolution, end);
            if (reset || math.any(overlapMin >= overlapMax))
            {
                regions[offset] = new Region { Min = next, Size = new int3(resolution) };
                return 1;
            }

            // Restrict each subsequent slab to the overlap on axes already processed.
            int count = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                if (next[axis] == previous[axis])
                    continue;
                int3 slabMin = next;
                int3 slabMax = end;
                if (next[axis] > previous[axis])
                    slabMin[axis] = overlapMax[axis];
                else
                    slabMax[axis] = overlapMin[axis];
                regions[offset + count++] = new Region { Min = slabMin, Size = slabMax - slabMin };
                next[axis] = overlapMin[axis];
                end[axis] = overlapMax[axis];
            }
            return count;
        }
    }

    internal sealed class VividGlobalSdfCameraState : CameraRelativeState
    {
        private const int GrowthReserve = VividGlobalSdfClipmap.PagesPerSlice / 2;
        private static readonly uint[] s_EmptyThinBricks = new uint[VividGlobalSdfClipmap.ThinBrickWordCount];
        internal readonly RTHandle[] Textures = new RTHandle[VividGlobalSdfClipmap.LevelCount];
        internal readonly int3[] Origins = new int3[VividGlobalSdfClipmap.LevelCount];
        internal readonly int[] NewPageCounts = new int[VividGlobalSdfClipmap.LevelCount];
        internal readonly int[] NewPages = new int[VividGlobalSdfClipmap.PageCount];
        private readonly int[] m_Capacities = new int[VividGlobalSdfClipmap.LevelCount];
        private readonly uint[] m_Pages = new uint[VividGlobalSdfClipmap.PageCount];
        private readonly bool[] m_Required = new bool[VividGlobalSdfClipmap.PagesPerLevel];
        private readonly bool[] m_UsedSlots = new bool[VividGlobalSdfClipmap.PagesPerLevel];
        internal GraphicsBuffer Pages;
        internal GraphicsBuffer ThinBricks;
        internal bool Valid;
        internal uint SceneRevision;
        internal int LayerMask;
        internal float BaseVoxelSize;

        internal void EnsureBuffers()
        {
            if (Pages == null || !Pages.IsValid())
            {
                Valid = false;
                Pages?.Dispose();
                Pages = new GraphicsBuffer(GraphicsBuffer.Target.Structured, VividGlobalSdfClipmap.PageCount,
                    sizeof(uint)) { name = "Global SDF Page Table" };
                Pages.SetData(m_Pages);
            }
            if (ThinBricks == null || !ThinBricks.IsValid())
            {
                Valid = false;
                ThinBricks?.Dispose();
                ThinBricks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, VividGlobalSdfClipmap.ThinBrickWordCount,
                    sizeof(uint)) { name = "Global SDF Thin Shell Bricks" };
                ThinBricks.SetData(s_EmptyThinBricks);
            }
        }

        internal bool UpdateResidency(int level, int3 origin, float voxel, uint layerMask, GraphicsFormat format,
            bool rebuild, VividMeshSdfGpuScene scene)
        {
            const int pagesPerLevel = VividGlobalSdfClipmap.PagesPerLevel;
            const int pagesPerSlice = VividGlobalSdfClipmap.PagesPerSlice;
            const int brickSize = VividGlobalSdfClipmap.BrickSize;
            VividGlobalSdfClipmap.MarkRequiredPages(scene.BvhNodes, origin, voxel, layerMask, m_Required);
            int required = 0;
            for (int page = 0; page < pagesPerLevel; page++)
                if (m_Required[page])
                    required++;

            int capacity = m_Capacities[level];
            bool resized = required > capacity || (rebuild && required < capacity / 2);
            if (resized)
                capacity = required == 0 ? 0 : math.min(pagesPerLevel,
                    CoreUtils.DivRoundUp(required + GrowthReserve, pagesPerSlice) * pagesPerSlice);
            if (resized || Textures[level]?.rt == null || !Textures[level].rt.IsCreated())
            {
                int size = capacity == 0 ? brickSize : VividGlobalSdfClipmap.Resolution;
                int depth = math.max(1, capacity / pagesPerSlice) * brickSize;
                Textures[level]?.Release();
                Textures[level] = RTHandles.Alloc(size, size, slices: depth, dimension: TextureDimension.Tex3D,
                    colorFormat: format, filterMode: FilterMode.Point, enableRandomWrite: true,
                    name: "Global SDF Brick Atlas " + level);
                m_Capacities[level] = capacity;
                rebuild = true;
            }

            int offset = level * pagesPerLevel;
            Array.Clear(m_UsedSlots, 0, pagesPerLevel);
            bool changed = false;
            // Page entries are one-based. Zero means every sample has the positive band value.
            for (int page = 0; page < pagesPerLevel; page++)
            {
                uint slot = m_Pages[offset + page];
                if (slot == 0)
                    continue;
                if (!m_Required[page] || slot > capacity)
                {
                    m_Pages[offset + page] = 0;
                    changed = true;
                }
                else
                    m_UsedSlots[(int)slot - 1] = true;
            }

            int nextFree = 0;
            int newCount = 0;
            for (int page = 0; page < pagesPerLevel; page++)
            {
                if (!m_Required[page] || m_Pages[offset + page] != 0)
                    continue;
                while (m_UsedSlots[nextFree])
                    nextFree++;
                NewPages[offset + newCount++] = nextFree;
                m_Pages[offset + page] = (uint)++nextFree;
                changed = true;
            }
            NewPageCounts[level] = rebuild ? 0 : newCount;
            if (changed)
                Pages.SetData(m_Pages, offset, offset, pagesPerLevel);
            return rebuild;
        }

        public override void Dispose()
        {
            for (int i = 0; i < Textures.Length; i++)
            {
                Textures[i]?.Release();
                Textures[i] = null;
            }
            ThinBricks?.Dispose();
            ThinBricks = null;
            Pages?.Dispose();
            Pages = null;
            Valid = false;
        }
    }

    internal sealed class VividGlobalSdfFrameData : ContextItem
    {
        internal readonly Vector4[] Levels = new Vector4[VividGlobalSdfClipmap.LevelCount];
        internal readonly Vector4[] Scroll = new Vector4[VividGlobalSdfClipmap.LevelCount];
        internal readonly int3[] Origins = new int3[VividGlobalSdfClipmap.LevelCount];
        internal VividGlobalSdfCameraState State;
        internal RenderGraphTexture[] Outputs;
        internal VividMeshSdfGpuScene MeshScene;
        internal RenderGraphBuffer ThinBricks;
        internal RenderGraphBuffer Pages;
        internal uint SceneRevision;
        internal int LayerMask;
        internal float BaseVoxelSize;
        internal bool Recorded;

        internal void Commit()
        {
            if (State == null || !Recorded)
                return;
            System.Array.Copy(Origins, State.Origins, Origins.Length);
            State.SceneRevision = SceneRevision;
            State.LayerMask = LayerMask;
            State.BaseVoxelSize = BaseVoxelSize;
            State.Valid = true;
        }

        public override void Reset()
        {
            State = null;
            Outputs = null;
            MeshScene = null;
            ThinBricks = null;
            Pages = null;
            Recorded = false;
        }
    }
}
