using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Stores world-space light data for all lights in the scene.
    /// </summary>
    internal class WorldLightClusterData : IDisposable
    {
        private NativeList<WorldLightData> m_LightData;
        private List<Light> m_LightReferences; // Managed list for Unity Object references
        private GraphicsBuffer m_GPUBuffer;
        private int m_MaxLights;
        private bool m_GPUBufferDirty = true;

        /// <summary>
        /// Gets the number of lights currently stored.
        /// </summary>
        public int LightCount => m_LightData.IsCreated ? m_LightData.Length : 0;

        /// <summary>
        /// Gets the maximum number of lights that can be stored.
        /// </summary>
        public int MaxLights => m_MaxLights;

        public WorldLightClusterData(int maxLights)
        {
            m_MaxLights = maxLights;
            m_LightData = new NativeList<WorldLightData>(maxLights, Allocator.Persistent);
            m_LightReferences = new List<Light>(maxLights);
        }

        /// <summary>
        /// Adds a light to the cluster data.
        /// </summary>
        /// <param name="light">The light to add.</param>
        /// <returns>Index of the added light, or -1 if failed.</returns>
        public int AddLight(Light light)
        {
            if (light == null || m_LightData.Length >= m_MaxLights)
                return -1;

            var lightData = CreateWorldLightData(light);
            if (lightData.HasValue)
            {
                int index = m_LightData.Length;
                m_LightData.Add(lightData.Value);
                m_LightReferences.Add(light);
                m_GPUBufferDirty = true;
                return index;
            }

            return -1;
        }

        /// <summary>
        /// Gets light data by index.
        /// </summary>
        public WorldLightData? GetLightData(int index)
        {
            if (index < 0 || index >= m_LightData.Length)
                return null;

            return m_LightData[index];
        }

        /// <summary>
        /// Gets the light reference by index.
        /// </summary>
        public Light GetLight(int index)
        {
            if (index < 0 || index >= m_LightReferences.Count)
                return null;

            return m_LightReferences[index];
        }

        /// <summary>
        /// Clears all light data.
        /// </summary>
        public void Clear()
        {
            m_LightData.Clear();
            m_LightReferences.Clear();
            m_GPUBufferDirty = true;
        }

        /// <summary>
        /// Gets or creates the GPU buffer containing light data.
        /// </summary>
        public GraphicsBuffer GetGPUBuffer()
        {
            if (m_GPUBufferDirty || m_GPUBuffer == null)
            {
                UpdateGPUBuffer();
            }

            return m_GPUBuffer;
        }

        /// <summary>
        /// Updates the GPU buffer with current light data.
        /// </summary>
        private void UpdateGPUBuffer()
        {
            int count = m_LightData.Length;
            if (count == 0)
            {
                if (m_GPUBuffer != null)
                {
                    m_GPUBuffer.Release();
                    m_GPUBuffer = null;
                }
                m_GPUBufferDirty = false;
                return;
            }

            // Ensure buffer is the right size
            int bufferSize = Math.Max(count, 1);
            if (m_GPUBuffer == null || m_GPUBuffer.count != bufferSize)
            {
                if (m_GPUBuffer != null)
                {
                    m_GPUBuffer.Release();
                }

                m_GPUBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bufferSize, 
                    Marshal.SizeOf<WorldLightDataGPU>());
            }

            // Convert to GPU format and upload
            var gpuData = new NativeArray<WorldLightDataGPU>(count, Allocator.Temp);
            for (int i = 0; i < count; i++)
            {
                gpuData[i] = ConvertToGPU(m_LightData[i]);
            }

            m_GPUBuffer.SetData(gpuData);
            gpuData.Dispose();
            m_GPUBufferDirty = false;
        }

        /// <summary>
        /// Creates WorldLightData from a Unity Light.
        /// </summary>
        private WorldLightData? CreateWorldLightData(Light light)
        {
            if (light == null)
                return null;

            var data = new WorldLightData
            {
                positionWS = light.transform.position,
                directionWS = -light.transform.forward,
                color = light.color.linear * light.intensity,
                range = light.range,
                lightType = light.type,
                spotAngle = light.type == LightType.Spot ? light.spotAngle : 0.0f,
                innerSpotAngle = light.type == LightType.Spot ? light.innerSpotAngle : 0.0f,
                shadowStrength = light.shadows != LightShadows.None ? light.shadowStrength : 0.0f,
                cookie = light.cookie != null ? 1.0f : 0.0f,
                lightLayerMask = 0xFFFFFFFF, // Default: affect all layers
                enabled = light.enabled ? 1u : 0u
            };

            // Handle area lights
            if (light.type == LightType.Rectangle || light.type == LightType.Disc)
            {
                data.areaSize = light.areaSize;
            }

            // Calculate bounding sphere
            data.boundingSphere = CalculateBoundingSphere(light, data);

            return data;
        }

        /// <summary>
        /// Calculates the bounding sphere for a light.
        /// </summary>
        private BoundingSphere CalculateBoundingSphere(Light light, WorldLightData data)
        {
            float3 center = data.positionWS;
            float radius = data.range;

            // Adjust for spot lights
            if (light.type == LightType.Spot)
            {
                float halfAngle = math.radians(data.spotAngle * 0.5f);
                float coneRadius = math.tan(halfAngle) * data.range;
                radius = math.length(new float3(coneRadius, coneRadius, data.range));
            }
            // Adjust for area lights
            else if (light.type == LightType.Rectangle)
            {
                float maxExtent = math.max(data.areaSize.x, data.areaSize.y);
                radius = math.length(new float3(maxExtent, maxExtent, data.range));
            }

            return new BoundingSphere { position = center, radius = radius };
        }

        /// <summary>
        /// Converts WorldLightData to GPU format.
        /// </summary>
        private WorldLightDataGPU ConvertToGPU(WorldLightData data)
        {
            return new WorldLightDataGPU
            {
                positionWS = data.positionWS,
                directionWS = data.directionWS,
                color = new float3(data.color.r, data.color.g, data.color.b),
                range = data.range,
                rangeSquared = data.range * data.range,
                spotAngleCos = math.cos(math.radians(data.spotAngle * 0.5f)),
                spotAngleSin = math.sin(math.radians(data.spotAngle * 0.5f)),
                innerSpotAngleCos = math.cos(math.radians(data.innerSpotAngle * 0.5f)),
                areaSize = data.areaSize,
                boundingSphereCenter = data.boundingSphere.position,
                boundingSphereRadius = data.boundingSphere.radius,
                lightType = (uint)data.lightType,
                shadowStrength = data.shadowStrength,
                cookie = data.cookie,
                lightLayerMask = data.lightLayerMask,
                enabled = data.enabled
            };
        }

        public void Dispose()
        {
            if (m_LightData.IsCreated)
            {
                m_LightData.Dispose();
            }

            m_LightReferences?.Clear();
            m_LightReferences = null;

            if (m_GPUBuffer != null)
            {
                m_GPUBuffer.Release();
                m_GPUBuffer = null;
            }
        }
    }
}

