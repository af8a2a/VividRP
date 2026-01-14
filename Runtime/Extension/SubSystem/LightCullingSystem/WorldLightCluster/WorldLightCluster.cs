using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// World-space light culling system for path tracing and multi-bounce global illumination.
    /// Provides GPU-side light queries for arbitrary world positions via 3D spatial grid.
    /// Supports punctual lights (Point/Spot) and area lights (Rectangle).
    /// </summary>
    public class WorldLightCluster : IDisposable
    {
        private const int k_DefaultMaxLights = 512;
        private const int k_DefaultGridResolution = 32;
        private const float k_DefaultCellSize = 10.0f;
        private const int k_MaxLightsPerCell = 32;

        // Settings
        private int m_MaxLights;
        private int m_GridResolution;
        private float m_CellSize;
        
        // World bounds
        private float3 m_WorldMin;
        private float3 m_WorldMax;
        private float3 m_InvCellSize;

        // Light data (reuses GPULightData format)
        private NativeList<GPULightData> m_LightData;
        private List<Light> m_LightReferences;
        private int m_LightCount;

        // Spatial grid data
        private NativeArray<uint2> m_GridCells;        // (offset, count) per cell
        private NativeList<uint> m_LightIndices;       // Flat list of light indices
        
        // GPU buffers
        private GraphicsBuffer m_LightDataBuffer;      // StructuredBuffer<GPULightData>
        private GraphicsBuffer m_GridCellBuffer;       // StructuredBuffer<uint2> - (offset, count)
        private GraphicsBuffer m_LightIndicesBuffer;   // StructuredBuffer<uint>
        
        private bool m_IsInitialized = false;
        private bool m_IsDirty = true;

        /// <summary>
        /// Gets whether the cluster is initialized.
        /// </summary>
        public bool IsInitialized => m_IsInitialized;

        /// <summary>
        /// Gets the number of lights in the cluster.
        /// </summary>
        public int LightCount => m_LightCount;

        /// <summary>
        /// Gets the grid resolution per axis.
        /// </summary>
        public int GridResolution => m_GridResolution;

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
        public void Initialize(int maxLights, int gridResolution, float cellSize)
        {
            if (m_IsInitialized)
            {
                Cleanup();
            }

            m_MaxLights = maxLights;
            m_GridResolution = gridResolution;
            m_CellSize = cellSize;
            m_InvCellSize = 1.0f / cellSize;

            // Allocate native arrays
            m_LightData = new NativeList<GPULightData>(maxLights, Allocator.Persistent);
            m_LightReferences = new List<Light>(maxLights);
            
            int totalCells = gridResolution * gridResolution * gridResolution;
            m_GridCells = new NativeArray<uint2>(totalCells, Allocator.Persistent);
            m_LightIndices = new NativeList<uint>(maxLights * 8, Allocator.Persistent); // Average 8 cells per light

            m_IsInitialized = true;
            m_IsDirty = true;
        }

        /// <summary>
        /// Sets the world bounds for the spatial grid.
        /// </summary>
        public void SetWorldBounds(Bounds bounds)
        {
            m_WorldMin = bounds.min;
            m_WorldMax = bounds.max;
            m_IsDirty = true;
        }

        /// <summary>
        /// Sets the world bounds for the spatial grid.
        /// </summary>
        public void SetWorldBounds(float3 min, float3 max)
        {
            m_WorldMin = min;
            m_WorldMax = max;
            m_IsDirty = true;
        }

        /// <summary>
        /// Updates the cluster with all punctual lights in the scene.
        /// </summary>
        public void UpdateCluster()
        {
            if (!m_IsInitialized)
            {
                Debug.LogWarning("WorldLightCluster not initialized. Call Initialize() first.");
                return;
            }

            // Clear previous data
            m_LightData.Clear();
            m_LightReferences.Clear();
            m_LightIndices.Clear();
            
            // Reset grid cells
            for (int i = 0; i < m_GridCells.Length; i++)
            {
                m_GridCells[i] = uint2.zero;
            }

            // Collect all punctual lights
            CollectPunctualLights();

            // Build spatial grid
            BuildSpatialGrid();

            m_LightCount = m_LightData.Length;
            m_IsDirty = true;
        }

        /// <summary>
        /// Collects all punctual lights (Point/Spot) and area lights (Rectangle) from the scene.
        /// </summary>
        private void CollectPunctualLights()
        {
            // Collect from LightManager first
            AddLightsFromList(LightManager.PointLights);
            AddLightsFromList(LightManager.SpotLights);

            // Collect rectangle/area lights
            AddAreaLightsFromList(LightManager.AreaLight);
        }

        private void AddLightsFromList(List<Light> lights)
        {
            if (lights == null) return;
            
            foreach (var light in lights)
            {
                AddLight(light);
            }
        }

        private void AddLight(Light light)
        {
            if (light == null || !light.enabled || !light.gameObject.activeInHierarchy)
                return;

            // Skip baked lights
            if (light.bakingOutput.lightmapBakeType == LightmapBakeType.Baked)
                return;

            // Skip if not punctual
            if (light.type != LightType.Point && light.type != LightType.Spot)
                return;

            if (m_LightData.Length >= m_MaxLights)
                return;

            var gpuLightData = CreateGPULightData(light, m_LightData.Length);
            m_LightData.Add(gpuLightData);
            m_LightReferences.Add(light);
        }

        private void AddAreaLightsFromList(List<Light> lights)
        {
            if (lights == null) return;

            foreach (var light in lights)
            {
                AddRectangleLight(light);
            }
        }

        private void AddRectangleLight(Light light)
        {
            if (light == null || !light.enabled || !light.gameObject.activeInHierarchy)
                return;

            // Skip baked lights
            if (light.bakingOutput.lightmapBakeType == LightmapBakeType.Baked)
                return;

            // Only process rectangle lights
            if (light.type != LightType.Rectangle)
                return;

            if (m_LightData.Length >= m_MaxLights)
                return;

            var gpuLightData = CreateGPURectangleLightData(light, m_LightData.Length);
            m_LightData.Add(gpuLightData);
            m_LightReferences.Add(light);
        }

        /// <summary>
        /// Creates GPULightData from a Unity Light (matching existing format).
        /// </summary>
        private GPULightData CreateGPULightData(Light light, int lightIndex)
        {
            var additionalData = light.GetUniversalAdditionalLightData();
            var transform = light.transform;

            Vector4 lightAttenuation = Vector4.zero;
            Vector4 lightSpotDir = Vector4.zero;
            
            // Calculate attenuation (matching UniversalRenderPipeline.InitializeLightConstants_Common)
            float lightRangeSqr = light.range * light.range;
            float fadeStartDistanceSqr = 0.8f * 0.8f * lightRangeSqr;
            float fadeRangeSqr = (fadeStartDistanceSqr - lightRangeSqr);
            float lightRangeSqrOverFadeRangeSqr = -lightRangeSqr / fadeRangeSqr;
            float oneOverLightRangeSqr = 1.0f / Mathf.Max(0.0001f, lightRangeSqr);
            lightAttenuation.x = oneOverLightRangeSqr;
            lightAttenuation.y = lightRangeSqrOverFadeRangeSqr;

            if (light.type == LightType.Spot)
            {
                float cosOuterAngle = Mathf.Cos(Mathf.Deg2Rad * light.spotAngle * 0.5f);
                float cosInnerAngle = Mathf.Cos(Mathf.Deg2Rad * light.innerSpotAngle * 0.5f);
                float smoothAngleRange = Mathf.Max(0.001f, cosInnerAngle - cosOuterAngle);
                float invAngleRange = 1.0f / smoothAngleRange;
                float add = -cosOuterAngle * invAngleRange;
                
                lightAttenuation.z = invAngleRange;
                lightAttenuation.w = add;
                lightSpotDir = -transform.forward;
            }

            int lightFlags = 0;
            if (light.bakingOutput.lightmapBakeType == LightmapBakeType.Mixed)
                lightFlags |= (int)LightFlag.SubtractiveMixedLighting;

            uint lightLayerMask = RenderingLayerUtils.ToValidRenderingLayers(additionalData.renderingLayers);

            float shapeRadius = additionalData.shapeRadius;
            
            // Value of max smoothness derived from AngularDiameter
            float maxSmoothness = Mathf.Clamp01(1.35f / (1.0f + Mathf.Pow(1.15f * (0.0315f * additionalData.angularDiameter + 0.4f), 2f)) - 0.11f);
            float minRoughness = (1.0f - maxSmoothness) * (1.0f - maxSmoothness);

            const float hugeValue = 16777216.0f;
            const float sqrtHuge = 4096.0f;
            float rangeAttenuationScale = sqrtHuge / (light.range * light.range);
            float rangeAttenuationBias = hugeValue;

            return new GPULightData
            {
                positionWS = transform.position,
                lightLayerMask = lightLayerMask,
                color = light.color.linear.ColorToVector3() * light.intensity,
                lightFlags = lightFlags,
                lightAttenuation = lightAttenuation,
                dir = lightSpotDir,
                shadowLightIndex = -1,
                lightOcclusionProbInfo = Vector4.zero,
                cookieLightIndex = -1,
                shadowType = (int)light.shadows,
                baseContribution = additionalData.baseContribution,
                minRoughness = minRoughness,
                size = new Vector4(shapeRadius * shapeRadius, 0, 0, 0),
                forward = transform.forward,
                rangeAttenuationScale = rangeAttenuationScale,
                up = transform.up,
                rangeAttenuationBias = rangeAttenuationBias,
                right = transform.right,
                volumetricLightDimmer = additionalData.volumetricDimmer
            };
        }

        /// <summary>
        /// Creates GPULightData from a Rectangle area light.
        /// Light type is encoded in lightFlags bits 16-19 (GPULIGHTTYPE_RECTANGLE = 6).
        /// </summary>
        private GPULightData CreateGPURectangleLightData(Light light, int lightIndex)
        {
            var additionalData = light.GetUniversalAdditionalLightData();
            var transform = light.transform;

            // Rectangle light dimensions
            float width = light.areaSize.x;
            float height = light.areaSize.y;

            // Calculate range attenuation
            const float hugeValue = 16777216.0f;
            const float sqrtHuge = 4096.0f;
            float rangeAttenuationScale = sqrtHuge / (light.range * light.range);
            float rangeAttenuationBias = hugeValue;

            // Encode light type in lightFlags bits 16-19: GPULIGHTTYPE_RECTANGLE = 6
            const int GPULIGHTTYPE_RECTANGLE = 6;
            int lightFlags = (GPULIGHTTYPE_RECTANGLE << 16);
            if (light.bakingOutput.lightmapBakeType == LightmapBakeType.Mixed)
                lightFlags |= (int)LightFlag.SubtractiveMixedLighting;

            uint lightLayerMask = RenderingLayerUtils.ToValidRenderingLayers(additionalData.renderingLayers);

            return new GPULightData
            {
                positionWS = transform.position,
                lightLayerMask = lightLayerMask,
                color = light.color.linear.ColorToVector3() * light.intensity,
                lightFlags = lightFlags,
                lightAttenuation = Vector4.zero, // Not used for area lights
                dir = Vector4.zero,
                shadowLightIndex = -1,
                lightOcclusionProbInfo = Vector4.zero,
                cookieLightIndex = -1,
                shadowType = (int)light.shadows,
                baseContribution = additionalData.baseContribution,
                minRoughness = 0,
                size = new Vector4(width, height, 1.0f / width, 1.0f / height),
                forward = transform.forward,
                rangeAttenuationScale = rangeAttenuationScale,
                up = transform.up,
                rangeAttenuationBias = rangeAttenuationBias,
                right = transform.right,
                volumetricLightDimmer = additionalData.volumetricDimmer
            };
        }

        /// <summary>
        /// Builds the spatial grid for GPU queries.
        /// </summary>
        private void BuildSpatialGrid()
        {
            // Temporary storage for lights per cell
            var cellLights = new NativeList<NativeList<uint>>(m_GridCells.Length, Allocator.Temp);
            for (int i = 0; i < m_GridCells.Length; i++)
            {
                cellLights.Add(new NativeList<uint>(k_MaxLightsPerCell, Allocator.Temp));
            }

            // Assign lights to cells
            for (int lightIndex = 0; lightIndex < m_LightData.Length; lightIndex++)
            {
                var lightData = m_LightData[lightIndex];
                float3 lightPos = lightData.positionWS;
                float range = GetLightRange(lightData);

                // Calculate cell range for this light
                int3 minCell = WorldToGrid(lightPos - range);
                int3 maxCell = WorldToGrid(lightPos + range);

                // Clamp to grid bounds
                minCell = math.max(minCell, int3.zero);
                maxCell = math.min(maxCell, new int3(m_GridResolution - 1));

                // Add light to all overlapping cells
                for (int z = minCell.z; z <= maxCell.z; z++)
                {
                    for (int y = minCell.y; y <= maxCell.y; y++)
                    {
                        for (int x = minCell.x; x <= maxCell.x; x++)
                        {
                            int cellIndex = GetCellIndex(x, y, z);
                            if (cellIndex >= 0 && cellIndex < cellLights.Length)
                            {
                                var list = cellLights[cellIndex];
                                if (list.Length < k_MaxLightsPerCell)
                                {
                                    list.Add((uint)lightIndex);
                                    cellLights[cellIndex] = list;
                                }
                            }
                        }
                    }
                }
            }

            // Build flat light index buffer and cell offset/count
            uint currentOffset = 0;
            for (int i = 0; i < m_GridCells.Length; i++)
            {
                var list = cellLights[i];
                uint count = (uint)list.Length;
                
                m_GridCells[i] = new uint2(currentOffset, count);
                
                for (int j = 0; j < list.Length; j++)
                {
                    m_LightIndices.Add(list[j]);
                }
                
                currentOffset += count;
                list.Dispose();
            }

            cellLights.Dispose();
        }

        private float GetLightRange(GPULightData lightData)
        {
            // For punctual lights, use lightAttenuation.x
            float oneOverRangeSqr = lightData.lightAttenuation.x;
            if (oneOverRangeSqr > 0)
            {
                return Mathf.Sqrt(1.0f / oneOverRangeSqr);
            }

            // For area lights, use rangeAttenuationScale
            // rangeAttenuationScale = sqrtHuge / (range * range), so range = sqrt(sqrtHuge / scale)
            const float sqrtHuge = 4096.0f;
            if (lightData.rangeAttenuationScale > 0)
            {
                return Mathf.Sqrt(sqrtHuge / lightData.rangeAttenuationScale);
            }

            return 100.0f; // Fallback
        }

        private int3 WorldToGrid(float3 worldPos)
        {
            float3 localPos = worldPos - m_WorldMin;
            return (int3)(localPos * m_InvCellSize);
        }

        private int GetCellIndex(int x, int y, int z)
        {
            if (x < 0 || x >= m_GridResolution ||
                y < 0 || y >= m_GridResolution ||
                z < 0 || z >= m_GridResolution)
                return -1;
                
            return x + y * m_GridResolution + z * m_GridResolution * m_GridResolution;
        }

        /// <summary>
        /// Updates GPU buffers with current data.
        /// </summary>
        public void UpdateGPUBuffers()
        {
            if (!m_IsInitialized || !m_IsDirty)
                return;

            // Update light data buffer
            int lightCount = m_LightData.Length;
            if (lightCount > 0)
            {
                EnsureBuffer(ref m_LightDataBuffer, lightCount, Marshal.SizeOf<GPULightData>(), "WorldLightData");
                m_LightDataBuffer.SetData(m_LightData.AsArray());
            }

            // Update grid cell buffer
            int cellCount = m_GridCells.Length;
            if (cellCount > 0)
            {
                EnsureBuffer(ref m_GridCellBuffer, cellCount, Marshal.SizeOf<uint2>(), "WorldLightGridCells");
                m_GridCellBuffer.SetData(m_GridCells);
            }

            // Update light indices buffer
            int indexCount = m_LightIndices.Length;
            if (indexCount > 0)
            {
                EnsureBuffer(ref m_LightIndicesBuffer, indexCount, sizeof(uint), "WorldLightIndices");
                m_LightIndicesBuffer.SetData(m_LightIndices.AsArray());
            }
            else
            {
                // Ensure at least 1 element buffer
                EnsureBuffer(ref m_LightIndicesBuffer, 1, sizeof(uint), "WorldLightIndices");
            }

            m_IsDirty = false;
        }

        private void EnsureBuffer(ref GraphicsBuffer buffer, int count, int stride, string name)
        {
            if (buffer == null || buffer.count < count)
            {
                buffer?.Release();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);
            }
        }

        /// <summary>
        /// Binds GPU resources to global shader properties.
        /// </summary>
        public void BindGlobalShaderResources(CommandBuffer cmd)
        {
            UpdateGPUBuffers();

            if (m_LightDataBuffer != null)
                cmd.SetGlobalBuffer(ShaderIDs._WorldLightData, m_LightDataBuffer);
            if (m_GridCellBuffer != null)
                cmd.SetGlobalBuffer(ShaderIDs._WorldLightGridCells, m_GridCellBuffer);
            if (m_LightIndicesBuffer != null)
                cmd.SetGlobalBuffer(ShaderIDs._WorldLightIndices, m_LightIndicesBuffer);

            cmd.SetGlobalInt(ShaderIDs._WorldLightCount, m_LightCount);
            cmd.SetGlobalInt(ShaderIDs._WorldLightGridResolution, m_GridResolution);
            cmd.SetGlobalVector(ShaderIDs._WorldLightGridMin, new Vector4(m_WorldMin.x, m_WorldMin.y, m_WorldMin.z, 0));
            cmd.SetGlobalVector(ShaderIDs._WorldLightGridCellSize, new Vector4(m_CellSize, m_InvCellSize.x, 0, 0));
        }

        /// <summary>
        /// Gets the light data buffer.
        /// </summary>
        public GraphicsBuffer GetLightDataBuffer()
        {
            UpdateGPUBuffers();
            return m_LightDataBuffer;
        }

        /// <summary>
        /// Gets the grid cell buffer.
        /// </summary>
        public GraphicsBuffer GetGridCellBuffer()
        {
            UpdateGPUBuffers();
            return m_GridCellBuffer;
        }

        /// <summary>
        /// Gets the light indices buffer.
        /// </summary>
        public GraphicsBuffer GetLightIndicesBuffer()
        {
            UpdateGPUBuffers();
            return m_LightIndicesBuffer;
        }

        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public void Cleanup()
        {
            if (m_IsInitialized)
            {
                if (m_LightData.IsCreated) m_LightData.Dispose();
                if (m_GridCells.IsCreated) m_GridCells.Dispose();
                if (m_LightIndices.IsCreated) m_LightIndices.Dispose();
                
                m_LightReferences?.Clear();
                m_LightReferences = null;

                m_LightDataBuffer?.Release();
                m_GridCellBuffer?.Release();
                m_LightIndicesBuffer?.Release();
                
                m_LightDataBuffer = null;
                m_GridCellBuffer = null;
                m_LightIndicesBuffer = null;

                m_IsInitialized = false;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Cleanup();
        }

        /// <summary>
        /// Shader property IDs for world light cluster.
        /// </summary>
        internal static class ShaderIDs
        {
            public static readonly int _WorldLightData = Shader.PropertyToID("_WorldLightData");
            public static readonly int _WorldLightGridCells = Shader.PropertyToID("_WorldLightGridCells");
            public static readonly int _WorldLightIndices = Shader.PropertyToID("_WorldLightIndices");
            public static readonly int _WorldLightCount = Shader.PropertyToID("_WorldLightCount");
            public static readonly int _WorldLightGridResolution = Shader.PropertyToID("_WorldLightGridResolution");
            public static readonly int _WorldLightGridMin = Shader.PropertyToID("_WorldLightGridMin");
            public static readonly int _WorldLightGridCellSize = Shader.PropertyToID("_WorldLightGridCellSize");
        }
    }
}
