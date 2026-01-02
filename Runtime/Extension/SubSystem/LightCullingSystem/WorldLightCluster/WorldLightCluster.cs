using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// World-space light culling system for path tracing and multi-bounce global illumination.
    /// Provides efficient light queries for arbitrary world positions, not limited to camera-visible lights.
    /// </summary>
    public class WorldLightCluster : IDisposable, IWorldLightQuery
    {
        private const int k_DefaultMaxLights = 1024;
        private const int k_DefaultGridResolution = 32;
        private const float k_DefaultCellSize = 10.0f;

        private WorldLightClusterData m_ClusterData;
        private WorldLightSpatialGrid m_SpatialGrid;
        private bool m_IsInitialized = false;

        /// <summary>
        /// Gets the current cluster data.
        /// </summary>
        internal WorldLightClusterData ClusterData => m_ClusterData;

        /// <summary>
        /// Gets whether the cluster is initialized.
        /// </summary>
        public bool IsInitialized => m_IsInitialized;

        /// <summary>
        /// Initializes the world light cluster with default settings.
        /// </summary>
        public void Initialize()
        {
            Initialize(k_DefaultMaxLights, k_DefaultGridResolution, k_DefaultCellSize);
        }

        /// <summary>
        /// Initializes the world light cluster with custom settings.
        /// </summary>
        /// <param name="maxLights">Maximum number of lights to store.</param>
        /// <param name="gridResolution">Resolution of the spatial grid (per axis).</param>
        /// <param name="cellSize">Size of each grid cell in world units.</param>
        public void Initialize(int maxLights, int gridResolution, float cellSize)
        {
            if (m_IsInitialized)
            {
                Cleanup();
            }

            m_ClusterData = new WorldLightClusterData(maxLights);
            m_SpatialGrid = new WorldLightSpatialGrid(gridResolution, cellSize);
            m_IsInitialized = true;
        }

        /// <summary>
        /// Updates the cluster with all lights in the scene.
        /// </summary>
        /// <param name="lightData">Current frame light data (for visible lights).</param>
        /// <param name="worldBounds">World-space bounding box for the scene.</param>
        public void UpdateCluster(UniversalLightData lightData, Bounds worldBounds)
        {
            if (!m_IsInitialized)
            {
                Debug.LogWarning("WorldLightCluster not initialized. Call Initialize() first.");
                return;
            }

            // Collect all lights in the scene (not just visible ones)
            var allLights = CollectAllSceneLights();
            
            // Update spatial grid bounds
            m_SpatialGrid.SetWorldBounds(worldBounds);
            
            // Clear previous data
            m_ClusterData.Clear();
            m_SpatialGrid.Clear();

            // Process and add lights
            foreach (var light in allLights)
            {
                if (light == null || !light.enabled || !light.gameObject.activeInHierarchy)
                    continue;

                // Skip baked lights for path tracing
                if (light.bakingOutput.lightmapBakeType == LightmapBakeType.Baked)
                    continue;

                var lightIndex = m_ClusterData.AddLight(light);
                if (lightIndex >= 0)
                {
                    m_SpatialGrid.AddLight(lightIndex, light);
                }
            }

            // Build spatial grid
            m_SpatialGrid.Build();
        }

        /// <summary>
        /// Queries lights that may affect the given world position.
        /// </summary>
        /// <param name="positionWS">World-space position to query.</param>
        /// <param name="maxDistance">Maximum distance to search for lights.</param>
        /// <param name="result">Output list of light indices.</param>
        /// <returns>Number of lights found.</returns>
        public int QueryLights(float3 positionWS, float maxDistance, List<int> result)
        {
            if (!m_IsInitialized || result == null)
                return 0;

            result.Clear();
            return m_SpatialGrid.QueryLights(positionWS, maxDistance, m_ClusterData, result);
        }

        /// <summary>
        /// Queries lights that may affect the given world position (NativeArray version).
        /// </summary>
        /// <param name="positionWS">World-space position to query.</param>
        /// <param name="maxDistance">Maximum distance to search for lights.</param>
        /// <param name="result">Output NativeList of light indices.</param>
        /// <returns>Number of lights found.</returns>
        public int QueryLights(float3 positionWS, float maxDistance, NativeList<int> result)
        {
            if (!m_IsInitialized || !result.IsCreated)
                return 0;

            result.Clear();
            return m_SpatialGrid.QueryLights(positionWS, maxDistance, m_ClusterData, result);
        }

        /// <summary>
        /// Gets light data by index.
        /// </summary>
        /// <param name="lightIndex">Index of the light.</param>
        /// <returns>Light data, or null if index is invalid.</returns>
        public WorldLightData? GetLightData(int lightIndex)
        {
            if (!m_IsInitialized || lightIndex < 0 || lightIndex >= m_ClusterData.LightCount)
                return null;

            return m_ClusterData.GetLightData(lightIndex);
        }

        /// <summary>
        /// Gets the GPU buffer containing all light data.
        /// </summary>
        /// <returns>GraphicsBuffer containing WorldLightDataGPU array, or null if not initialized.</returns>
        public GraphicsBuffer GetGPULightDataBuffer()
        {
            if (!m_IsInitialized)
                return null;

            return m_ClusterData.GetGPUBuffer();
        }

        /// <summary>
        /// Gets the GPU buffer containing spatial grid data.
        /// </summary>
        /// <returns>GraphicsBuffer containing grid cell data, or null if not initialized.</returns>
        public GraphicsBuffer GetGPUSpatialGridBuffer()
        {
            if (!m_IsInitialized)
                return null;

            return m_SpatialGrid.GetGPUBuffer();
        }

        /// <summary>
        /// Collects all lights in the scene (not just camera-visible ones).
        /// </summary>
        private List<Light> CollectAllSceneLights()
        {
            var lights = new List<Light>();
            
            // Use LightManager if available, otherwise fallback to FindObjectsByType
            if (LightManager.DirectionalLights != null)
            {
                lights.AddRange(LightManager.DirectionalLights);
            }
            if (LightManager.PointLights != null)
            {
                lights.AddRange(LightManager.PointLights);
            }
            if (LightManager.SpotLights != null)
            {
                lights.AddRange(LightManager.SpotLights);
            }
            if (LightManager.AreaLight != null)
            {
                lights.AddRange(LightManager.AreaLight);
            }

            // Fallback: find all lights if LightManager is not populated
            if (lights.Count == 0)
            {
                var allLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                lights.AddRange(allLights);
            }

            return lights;
        }

        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public void Cleanup()
        {
            if (m_IsInitialized)
            {
                m_ClusterData?.Dispose();
                m_SpatialGrid?.Dispose();
                m_IsInitialized = false;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Cleanup();
        }
    }
}

