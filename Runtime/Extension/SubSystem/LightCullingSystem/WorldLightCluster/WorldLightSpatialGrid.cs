using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// 3D spatial grid for efficient light queries in world space.
    /// </summary>
    internal class WorldLightSpatialGrid : IDisposable
    {
        private int m_GridResolution;
        private float m_CellSize;
        private Bounds m_WorldBounds;
        private float3 m_GridMin;
        private float3 m_GridMax;
        private float3 m_GridSize;
        private float3 m_InvCellSize;

        // Grid data: each cell contains a list of light indices
        private NativeParallelMultiHashMap<int3, int> m_GridCells;
        private NativeList<int3> m_NonEmptyCells;
        private GraphicsBuffer m_GPUBuffer;
        private bool m_GPUBufferDirty = true;

        /// <summary>
        /// Initializes a new spatial grid.
        /// </summary>
        /// <param name="gridResolution">Resolution per axis (total cells = resolution^3).</param>
        /// <param name="cellSize">Size of each cell in world units.</param>
        public WorldLightSpatialGrid(int gridResolution, float cellSize)
        {
            m_GridResolution = gridResolution;
            m_CellSize = cellSize;
            m_InvCellSize = 1.0f / cellSize;
            m_GridCells = new NativeParallelMultiHashMap<int3, int>(1024, Allocator.Persistent);
            m_NonEmptyCells = new NativeList<int3>(256, Allocator.Persistent);
        }

        /// <summary>
        /// Sets the world bounds for the grid.
        /// </summary>
        public void SetWorldBounds(Bounds bounds)
        {
            m_WorldBounds = bounds;
            m_GridMin = bounds.min;
            m_GridMax = bounds.max;
            m_GridSize = bounds.size;
        }

        /// <summary>
        /// Clears all grid data.
        /// </summary>
        public void Clear()
        {
            m_GridCells.Clear();
            m_NonEmptyCells.Clear();
            m_GPUBufferDirty = true;
        }

        /// <summary>
        /// Adds a light to the spatial grid.
        /// </summary>
        /// <param name="lightIndex">Index of the light in the cluster data.</param>
        /// <param name="light">The light component.</param>
        public void AddLight(int lightIndex, Light light)
        {
            if (light == null)
                return;

            var position = (float3)light.transform.position;
            var range = light.range;

            // Calculate grid cells that intersect with the light's influence
            var minCell = WorldToGrid(position - range);
            var maxCell = WorldToGrid(position + range);

            // Clamp to grid bounds
            minCell = math.max(minCell, int3.zero);
            maxCell = math.min(maxCell, new int3(m_GridResolution - 1));

            // Add light to all intersecting cells
            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    for (int z = minCell.z; z <= maxCell.z; z++)
                    {
                        var cellCoord = new int3(x, y, z);
                        m_GridCells.Add(cellCoord, lightIndex);

                        // Track non-empty cells for GPU buffer building
                        if (!m_NonEmptyCells.Contains(cellCoord))
                        {
                            m_NonEmptyCells.Add(cellCoord);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Builds the spatial grid (called after all lights are added).
        /// </summary>
        public void Build()
        {
            // Grid is built incrementally as lights are added
            // This method can be used for additional optimizations if needed
            m_GPUBufferDirty = true;
        }

        /// <summary>
        /// Queries lights that may affect the given world position.
        /// </summary>
        /// <param name="positionWS">World-space position to query.</param>
        /// <param name="maxDistance">Maximum distance to search.</param>
        /// <param name="clusterData">Cluster data containing light information.</param>
        /// <param name="result">Output list of light indices.</param>
        /// <returns>Number of lights found.</returns>
        public int QueryLights(float3 positionWS, float maxDistance, WorldLightClusterData clusterData, List<int> result)
        {
            if (result == null)
                return 0;

            result.Clear();

            // Calculate grid cells to search
            var searchMin = WorldToGrid(positionWS - maxDistance);
            var searchMax = WorldToGrid(positionWS + maxDistance);

            // Clamp to grid bounds
            searchMin = math.max(searchMin, int3.zero);
            searchMax = math.min(searchMax, new int3(m_GridResolution - 1));

            var foundIndices = new HashSet<int>();

            // Search all cells in the query region
            for (int x = searchMin.x; x <= searchMax.x; x++)
            {
                for (int y = searchMin.y; y <= searchMax.y; y++)
                {
                    for (int z = searchMin.z; z <= searchMax.z; z++)
                    {
                        var cellCoord = new int3(x, y, z);
                        if (m_GridCells.TryGetFirstValue(cellCoord, out int lightIndex, out var iterator))
                        {
                            do
                            {
                                if (!foundIndices.Contains(lightIndex))
                                {
                                    // Verify light is within max distance
                                    var lightData = clusterData.GetLightData(lightIndex);
                                    if (lightData.HasValue)
                                    {
                                        float distSq = math.distancesq(positionWS, lightData.Value.positionWS);
                                        float maxDistSq = maxDistance * maxDistance;
                                        if (distSq <= maxDistSq)
                                        {
                                            foundIndices.Add(lightIndex);
                                            result.Add(lightIndex);
                                        }
                                    }
                                }
                            } while (m_GridCells.TryGetNextValue(out lightIndex, ref iterator));
                        }
                    }
                }
            }

            return result.Count;
        }

        /// <summary>
        /// Queries lights that may affect the given world position (NativeList version).
        /// </summary>
        public int QueryLights(float3 positionWS, float maxDistance, WorldLightClusterData clusterData, NativeList<int> result)
        {
            if (!result.IsCreated)
                return 0;

            result.Clear();

            // Calculate grid cells to search
            var searchMin = WorldToGrid(positionWS - maxDistance);
            var searchMax = WorldToGrid(positionWS + maxDistance);

            // Clamp to grid bounds
            searchMin = math.max(searchMin, int3.zero);
            searchMax = math.min(searchMax, new int3(m_GridResolution - 1));

            var foundIndices = new NativeHashSet<int>(64, Allocator.Temp);

            // Search all cells in the query region
            for (int x = searchMin.x; x <= searchMax.x; x++)
            {
                for (int y = searchMin.y; y <= searchMax.y; y++)
                {
                    for (int z = searchMin.z; z <= searchMax.z; z++)
                    {
                        var cellCoord = new int3(x, y, z);
                        if (m_GridCells.TryGetFirstValue(cellCoord, out int lightIndex, out var iterator))
                        {
                            do
                            {
                                if (!foundIndices.Contains(lightIndex))
                                {
                                    // Verify light is within max distance
                                    var lightData = clusterData.GetLightData(lightIndex);
                                    if (lightData.HasValue)
                                    {
                                        float distSq = math.distancesq(positionWS, lightData.Value.positionWS);
                                        float maxDistSq = maxDistance * maxDistance;
                                        if (distSq <= maxDistSq)
                                        {
                                            foundIndices.Add(lightIndex);
                                            result.Add(lightIndex);
                                        }
                                    }
                                }
                            } while (m_GridCells.TryGetNextValue(out lightIndex, ref iterator));
                        }
                    }
                }
            }

            foundIndices.Dispose();
            return result.Length;
        }

        /// <summary>
        /// Converts world position to grid coordinates.
        /// </summary>
        private int3 WorldToGrid(float3 worldPos)
        {
            float3 localPos = worldPos - m_GridMin;
            int3 gridPos = (int3)(localPos * m_InvCellSize);
            return gridPos;
        }

        /// <summary>
        /// Gets the GPU buffer containing spatial grid data.
        /// </summary>
        public GraphicsBuffer GetGPUBuffer()
        {
            // TODO: Implement GPU buffer for spatial grid if needed for shader queries
            // For now, return null as CPU queries are sufficient for initial implementation
            return null;
        }

        public void Dispose()
        {
            if (m_GridCells.IsCreated)
            {
                m_GridCells.Dispose();
            }

            if (m_NonEmptyCells.IsCreated)
            {
                m_NonEmptyCells.Dispose();
            }

            if (m_GPUBuffer != null)
            {
                m_GPUBuffer.Release();
                m_GPUBuffer = null;
            }
        }
    }
}

