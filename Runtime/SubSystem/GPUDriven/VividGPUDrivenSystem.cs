using System;
using VividRP.Runtime.GPUDriven.Bindless;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    public sealed class VividGPUDrivenSystem : IDisposable
    {
        private static VividGPUDrivenSystem s_Instance;

        private readonly VividGPUDrivenBufferSet m_BufferSet;
        private readonly VividGPUDrivenCullingDispatcher m_CullingDispatcher;
        private readonly VividGPUDrivenSceneDataBuilder m_SceneDataBuilder;
        private bool m_IsDisposed;

        public VividGPUDrivenSystem()
            : this(new BindlessTextureContainer(), new VividGPUDrivenSceneDataBuilder())
        {
        }

        internal VividGPUDrivenSystem(IBindlessTextureDescriptorAllocator allocator)
            : this(new BindlessTextureContainer(allocator), new VividGPUDrivenSceneDataBuilder())
        {
        }

        private VividGPUDrivenSystem(
            BindlessTextureContainer bindlessTextureContainer,
            VividGPUDrivenSceneDataBuilder sceneDataBuilder
        )
        {
            BindlessTextureContainer = bindlessTextureContainer ?? throw new ArgumentNullException(nameof(bindlessTextureContainer));
            m_BufferSet = new VividGPUDrivenBufferSet();
            m_CullingDispatcher = new VividGPUDrivenCullingDispatcher();
            m_SceneDataBuilder = sceneDataBuilder ?? throw new ArgumentNullException(nameof(sceneDataBuilder));
            SceneData = new VividGPUDrivenSceneData();
            ForcedMeshLODNodeDepth = VividGPUDrivenCullingContextUtility.DefaultForcedMeshLODNodeDepth;
            MeshLODErrorThreshold = VividGPUDrivenCullingContextUtility.DefaultMeshLODErrorThreshold;
        }

        public static VividGPUDrivenSystem instance => s_Instance ??= new VividGPUDrivenSystem();

        public static bool HasInstance => s_Instance != null && !s_Instance.m_IsDisposed;

        public BindlessTextureContainer BindlessTextureContainer { get; }

        public VividGPUDrivenSceneData SceneData { get; }

        public bool IsAvailable => BindlessTextureContainer.IsAvailable;

        public string UnavailableReason => BindlessTextureContainer.UnavailableReason;

        internal VividGPUDrivenBufferSet BufferSet => m_BufferSet;

        internal VividGPUDrivenCullingBuffers CullingBufferSet => m_CullingDispatcher.BufferSet;

        public int ForcedMeshLODNodeDepth { get; set; }

        public float MeshLODErrorThreshold { get; set; }

        public GraphicsBuffer VisibleMeshletRenderRequestsBuffer => m_CullingDispatcher.BufferSet.VisibleMeshletRenderRequestsBuffer;

        public GraphicsBuffer VisibleMeshletIndirectDrawArgsBuffer => m_CullingDispatcher.BufferSet.VisibleMeshletIndirectDrawArgsBuffer;

        public static bool TryGetCurrentVisibleMeshletBuffers(
            out GraphicsBuffer visibleMeshletRenderRequestsBuffer,
            out GraphicsBuffer visibleMeshletIndirectDrawArgsBuffer)
        {
            if (s_Instance == null || s_Instance.m_IsDisposed)
            {
                visibleMeshletRenderRequestsBuffer = null;
                visibleMeshletIndirectDrawArgsBuffer = null;
                return false;
            }

            visibleMeshletRenderRequestsBuffer = s_Instance.VisibleMeshletRenderRequestsBuffer;
            visibleMeshletIndirectDrawArgsBuffer = s_Instance.VisibleMeshletIndirectDrawArgsBuffer;
            return visibleMeshletRenderRequestsBuffer != null && visibleMeshletIndirectDrawArgsBuffer != null;
        }

        public void PrepareFrame()
        {
            ThrowIfDisposed();

            BindlessTextureContainer.PreRender();
            bool staticDataChanged = m_SceneDataBuilder.Build(
                SceneData,
                VividMeshletRendererDatabase.instance,
                BindlessTextureContainer,
                out bool materialDataChanged
            );
            m_BufferSet.Upload(SceneData, materialDataChanged, staticDataChanged);
        }

        public void Cull(
            Camera camera,
            CommandBuffer cmd,
            ComputeShader gpuInstanceCullingCompute,
            ComputeShader meshletListBuildCompute,
            ComputeShader gpuMeshletCullingCompute = null,
            ComputeShader fixupVisibleMeshletIndirectDrawArgsCompute = null,
            VividInstancePassMask passMask = VividInstancePassMask.Main
        )
        {
            ThrowIfDisposed();

            cmd.BeginSample("GPUDrivenCulling");
            m_CullingDispatcher.Dispatch(
                cmd,
                camera,
                SceneData,
                m_BufferSet,
                gpuInstanceCullingCompute,
                meshletListBuildCompute,
                gpuMeshletCullingCompute,
                fixupVisibleMeshletIndirectDrawArgsCompute,
                passMask,
                ForcedMeshLODNodeDepth,
                MeshLODErrorThreshold
            );
            cmd.EndSample("GPUDrivenCulling");
        }

        public void BindGlobals(CommandBuffer cmd)
        {
            ThrowIfDisposed();
            m_BufferSet.BindGlobals(cmd);
            m_CullingDispatcher.BindGlobals(cmd);
        }

        public static void Shutdown()
        {
            s_Instance?.Dispose();
            s_Instance = null;
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            SceneData.Clear();
            m_BufferSet.Dispose();
            m_CullingDispatcher.Dispose();
            BindlessTextureContainer.Dispose();
            m_IsDisposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(nameof(VividGPUDrivenSystem));
            }
        }
    }
}
