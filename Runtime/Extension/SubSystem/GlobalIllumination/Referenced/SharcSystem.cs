using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// SHARC (Spatially Hashed Radiance Cache) system for path tracing acceleration.
    /// Manages the hash grid buffers and resolve compute shader.
    /// </summary>
    public class SharcSystem : IDisposable
    {
        // Singleton instance
        private static SharcSystem s_Instance;
        public static SharcSystem instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = new SharcSystem();
                return s_Instance;
            }
        }

        // Buffer sizes
        private int m_HashEntriesCount;
        private bool m_IsInitialized;

        // GPU Buffers
        private GraphicsBuffer m_HashEntriesBuffer;     // uint64_t hash keys
        private GraphicsBuffer m_LockBuffer;            // uint locks for non-64-bit atomic fallback
        private GraphicsBuffer m_AccumulationBuffer;    // SharcAccumulationData (uint4)
        private GraphicsBuffer m_ResolvedBuffer;        // SharcPackedData (fp16x4 + uint2)

        // Resolve compute shader
        private ComputeShader m_ResolveShader;
        private int m_ResolveKernel;

        // Previous camera position for level blending
        private Vector3 m_PreviousCameraPosition;

        // Shader property IDs
        public static class ShaderIDs
        {
            public static readonly int _SharcHashEntriesBuffer = Shader.PropertyToID("_SharcHashEntriesBuffer");
            public static readonly int _SharcLockBuffer = Shader.PropertyToID("_SharcLockBuffer");
            public static readonly int _SharcAccumulationBuffer = Shader.PropertyToID("_SharcAccumulationBuffer");
            public static readonly int _SharcResolvedBuffer = Shader.PropertyToID("_SharcResolvedBuffer");

            public static readonly int _SharcCameraPosition = Shader.PropertyToID("_SharcCameraPosition");
            public static readonly int _SharcCameraPositionPrev = Shader.PropertyToID("_SharcCameraPositionPrev");
            public static readonly int _SharcSceneScale = Shader.PropertyToID("_SharcSceneScale");
            public static readonly int _SharcRoughnessThreshold = Shader.PropertyToID("_SharcRoughnessThreshold");
            public static readonly int _SharcEntriesNum = Shader.PropertyToID("_SharcEntriesNum");
            public static readonly int _SharcEnableAntifirefly = Shader.PropertyToID("_SharcEnableAntifirefly");
            public static readonly int _SharcDebug = Shader.PropertyToID("_SharcDebug");
            public static readonly int _SharcAccumulationFrameNum = Shader.PropertyToID("_SharcAccumulationFrameNum");
            public static readonly int _SharcStaleFrameNumMax = Shader.PropertyToID("_SharcStaleFrameNumMax");
        }

        /// <summary>
        /// SharcAccumulationData structure size in bytes (uint4 = 16 bytes)
        /// </summary>
        private const int kAccumulationDataSize = 16;

        /// <summary>
        /// SharcPackedData structure size in bytes (fp16x4 + uint + uint = 8 + 4 + 4 = 16 bytes)
        /// </summary>
        private const int kPackedDataSize = 16;

        private SharcSystem()
        {
            m_IsInitialized = false;
            m_PreviousCameraPosition = Vector3.zero;
        }

        /// <summary>
        /// Initialize SHARC buffers with the specified capacity
        /// </summary>
        /// <param name="entriesCount">Number of hash entries (typically entriesK * 1024)</param>
        public void Initialize(int entriesCount)
        {
            if (m_IsInitialized && m_HashEntriesCount == entriesCount)
                return;

            Release();

            m_HashEntriesCount = entriesCount;

            // Create hash entries buffer (uint64_t per entry)
            m_HashEntriesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, entriesCount, sizeof(ulong));

            // Create lock buffer for fallback (uint per entry)
            m_LockBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, entriesCount, sizeof(uint));

            // Create accumulation buffer (uint4 per entry)
            m_AccumulationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, entriesCount, kAccumulationDataSize);

            // Create resolved buffer (SharcPackedData per entry)
            m_ResolvedBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, entriesCount, kPackedDataSize);

            // Clear all buffers
            ClearBuffers();

            m_IsInitialized = true;

            Debug.Log($"[SHARC] Initialized with {entriesCount} entries ({entriesCount * (sizeof(ulong) + sizeof(uint) + kAccumulationDataSize + kPackedDataSize) / (1024 * 1024)}MB)");
        }

        /// <summary>
        /// Clear all SHARC buffers to zero
        /// </summary>
        public void ClearBuffers()
        {
            if (m_HashEntriesBuffer != null)
            {
                // Fill with HASH_GRID_INVALID_HASH_KEY (0)
                var zeroData = new ulong[m_HashEntriesCount];
                m_HashEntriesBuffer.SetData(zeroData);
            }

            if (m_LockBuffer != null)
            {
                var zeroData = new uint[m_HashEntriesCount];
                m_LockBuffer.SetData(zeroData);
            }

            if (m_AccumulationBuffer != null)
            {
                var zeroData = new uint[m_HashEntriesCount * 4];
                m_AccumulationBuffer.SetData(zeroData);
            }

            if (m_ResolvedBuffer != null)
            {
                var zeroData = new uint[m_HashEntriesCount * 4];
                m_ResolvedBuffer.SetData(zeroData);
            }
        }

        /// <summary>
        /// Bind SHARC buffers to a command buffer for ray tracing
        /// </summary>
        public void BindBuffers(CommandBuffer cmd, RayTracingShader shader)
        {
            if (!m_IsInitialized)
                return;

            cmd.SetRayTracingBufferParam(shader, ShaderIDs._SharcHashEntriesBuffer, m_HashEntriesBuffer);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs._SharcLockBuffer, m_LockBuffer);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs._SharcAccumulationBuffer, m_AccumulationBuffer);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs._SharcResolvedBuffer, m_ResolvedBuffer);
        }

        /// <summary>
        /// Bind SHARC parameters to a command buffer
        /// </summary>
        public void BindParameters(CommandBuffer cmd, RayTracingShader shader, GlobalIllumination settings, Vector3 cameraPosition)
        {
            if (!m_IsInitialized)
                return;

            cmd.SetRayTracingVectorParam(shader, ShaderIDs._SharcCameraPosition, cameraPosition);
            cmd.SetRayTracingFloatParam(shader, ShaderIDs._SharcSceneScale, settings.sharcSceneScale.value);
            cmd.SetRayTracingFloatParam(shader, ShaderIDs._SharcRoughnessThreshold, settings.sharcRoughnessThreshold.value);
            cmd.SetRayTracingIntParam(shader, ShaderIDs._SharcEntriesNum, m_HashEntriesCount);
            cmd.SetRayTracingIntParam(shader, ShaderIDs._SharcEnableAntifirefly, settings.sharcAntiFirefly.value ? 1 : 0);
            cmd.SetRayTracingIntParam(shader, ShaderIDs._SharcDebug, settings.sharcDebug.value ? 1 : 0);
        }

        /// <summary>
        /// Bind SHARC buffers to a compute shader for resolve pass
        /// </summary>
        public void BindBuffersCompute(CommandBuffer cmd, ComputeShader shader, int kernel)
        {
            if (!m_IsInitialized)
                return;

            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs._SharcHashEntriesBuffer, m_HashEntriesBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs._SharcLockBuffer, m_LockBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs._SharcAccumulationBuffer, m_AccumulationBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs._SharcResolvedBuffer, m_ResolvedBuffer);
        }

        /// <summary>
        /// Get hash entries buffer for RenderGraph import
        /// </summary>
        public GraphicsBuffer HashEntriesBuffer => m_HashEntriesBuffer;

        /// <summary>
        /// Get lock buffer for RenderGraph import
        /// </summary>
        public GraphicsBuffer LockBuffer => m_LockBuffer;

        /// <summary>
        /// Get accumulation buffer for RenderGraph import
        /// </summary>
        public GraphicsBuffer AccumulationBuffer => m_AccumulationBuffer;

        /// <summary>
        /// Get resolved buffer for RenderGraph import
        /// </summary>
        public GraphicsBuffer ResolvedBuffer => m_ResolvedBuffer;

        /// <summary>
        /// Get number of hash entries
        /// </summary>
        public int EntriesCount => m_HashEntriesCount;

        /// <summary>
        /// Check if SHARC is initialized
        /// </summary>
        public bool IsInitialized => m_IsInitialized;

        /// <summary>
        /// Update previous camera position (call after rendering)
        /// </summary>
        public void UpdatePreviousCameraPosition(Vector3 position)
        {
            m_PreviousCameraPosition = position;
        }

        /// <summary>
        /// Get previous camera position
        /// </summary>
        public Vector3 PreviousCameraPosition => m_PreviousCameraPosition;

        /// <summary>
        /// Release all GPU resources
        /// </summary>
        public void Release()
        {
            m_HashEntriesBuffer?.Release();
            m_HashEntriesBuffer = null;

            m_LockBuffer?.Release();
            m_LockBuffer = null;

            m_AccumulationBuffer?.Release();
            m_AccumulationBuffer = null;

            m_ResolvedBuffer?.Release();
            m_ResolvedBuffer = null;

            m_IsInitialized = false;
        }

        public void Dispose()
        {
            Release();
            s_Instance = null;
        }
    }
}
