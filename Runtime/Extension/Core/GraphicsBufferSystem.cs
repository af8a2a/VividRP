using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public enum GraphicsBufferSystemBufferID
    {
        SPD,
        //LightDataBuffer,
        //LightIndicesBuffer,

        //AdditionalLightShadowParamsStructuredBuffer,
        //AdditionalLightShadowSliceMatricesStructuredBuffer,
        /// <summary>SSR GraphicsBuffer.</summary>
        SSRDispatchIndirectBuffer,
        RTRTReflectionIndirectBuffer,

        //SSRTileListBuffer,
        /// <summary>GPU Lights GraphicsBuffer.</summary>
        GPULightsLightBoundsBuffer,
        GPULightsLightVolumeDataBuffer,

        //GPULightsAABBBounds,
        //GPULightsCoarseLightList,
        //GPULightsPerVoxelLightLists,
        //GPULightsPerVoxelOffset,
        //GPULightsPerTileLogBaseTweak,
        //GPULightsGlobalLightLIstAtomic,
        //GPULightsData,
        //DirectionalLightsData,
        /// <summary>DepthPyramid MipLevelOffset GraphicsBuffer.</summary>
        DepthPyramidMipLevelOffset,

        /// <summary>Deferred Lighting GraphicsBuffer.</summary>
        DeferredLightingIndirect,

        //DeferredLightingTileList,
        /// <summary>ScreenSpace Shadow GraphicsBuffer.</summary>
        ScreenSpaceShadowIndirect,
        
        ShadowTileCountBuffer,
    }

    /// <summary>
    /// GraphicsBufferSystem only used for managing "Persistent" GraphicsBuffers in DanbaidongRP. "Transient" Buffer use renderGraph.
    /// Each camera uses one system, and the GraphicsBuffer will be set to the maximum size across cameras.
    /// 
    /// !!!IMPORTANT!!!
    /// Please note that buffers may not be reliable if you intend to access the buffer from the previous frame.
    /// </summary>
    class GraphicsBufferSystem : IDisposable
    {
        static Dictionary<int, GraphicsBuffer> s_GraphicsBuffers = new Dictionary<int, GraphicsBuffer>();
        bool m_DisposedValue = false;

        static GraphicsBufferSystem m_Instance = null;

        GraphicsBufferSystem()
        {
        }

        internal static GraphicsBufferSystem instance
        {
            get
            {
                if (m_Instance == null)
                    m_Instance = new GraphicsBufferSystem();

                return m_Instance;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bufferId"></param>
        /// <param name="desc"></param>
        /// <returns></returns>
        internal GraphicsBuffer GetGraphicsBuffer(GraphicsBufferSystemBufferID bufferId, BufferDesc desc)
        {
            int id = (int)bufferId;
            if (!s_GraphicsBuffers.ContainsKey(id))
            {
                var buffer = new GraphicsBuffer(desc.target, desc.usageFlags, desc.count, desc.stride);
                s_GraphicsBuffers.Add(id, buffer);
                return buffer;
            }

            GraphicsBuffer mbuffer;
            s_GraphicsBuffers.TryGetValue(id, out mbuffer);
            // Update reference
            if (GetOrUpdateBuffer(ref mbuffer, desc))
            {
                s_GraphicsBuffers.Remove(id);
                s_GraphicsBuffers.Add(id, mbuffer);
            }


            return mbuffer;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="bufferId"></param>
        /// <param name="size"></param>
        /// <param name="bufferTarget"></param>
        /// <returns></returns>
        internal GraphicsBuffer GetGraphicsBuffer<T>(GraphicsBufferSystemBufferID bufferId, int size, string name = "",
            GraphicsBuffer.Target bufferTarget = GraphicsBuffer.Target.Structured) where T : struct
        {
            var desc = new BufferDesc(size, Marshal.SizeOf<T>(), bufferTarget)
            {
                name = name
            };
            var buffer = GetGraphicsBuffer(bufferId, desc);
            if (bufferTarget == GraphicsBuffer.Target.IndirectArguments)
            {
                InitIndirectGraphicsBufferValue(buffer);
            }

            return buffer;
        }

        internal void InitIndirectGraphicsBufferValue(GraphicsBuffer buffer)
        {
            uint[] array = new uint[buffer.count];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = (i % 3) == 0 ? 0u : 1u;
            }

            buffer.SetData(array);
        }

        /// <summary>
        /// Note that we should ensure that the buffer stride is not changed.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="desc"></param>
        /// <returns>if reference updated or not</returns>
        bool GetOrUpdateBuffer(ref GraphicsBuffer buffer, BufferDesc desc)
        {
            bool bufUpdated = false;
            if (buffer == null)
            {
                buffer = new GraphicsBuffer(desc.target, desc.usageFlags, desc.count, desc.stride);
                bufUpdated = true;
            }
            else if (desc.count > buffer.count)
            {
                buffer.Release();
                buffer = new GraphicsBuffer(desc.target, desc.usageFlags, desc.count, desc.stride);
                bufUpdated = true;
            }

            return bufUpdated;
        }

        void DisposeBuffer(ref GraphicsBuffer buffer)
        {
            if (buffer != null)
            {
                CoreUtils.SafeRelease(buffer);
                buffer = null;
            }
        }

        public static void ClearAll()
        {
            if (m_Instance != null)
                m_Instance.Dispose();

            m_Instance = null;
        }

        /// <summary>
        /// Dispose implementation
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (!m_DisposedValue)
            {
                if (disposing)
                {
                    ReleaseAll();
                }

                m_DisposedValue = true;
            }
        }

        /// <summary>
        /// Deallocate and clear all buffers.
        /// </summary>
        public void ReleaseAll()
        {
            foreach (var item in s_GraphicsBuffers)
            {
                var buffer = item.Value;
                DisposeBuffer(ref buffer);
            }

            s_GraphicsBuffers.Clear();
        }
    }
}