using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class VividGPUDrivenCullingDispatcher : IDisposable
    {
        private ComputeShader m_GPUInstanceCullingCompute;
        private ComputeShader m_MeshletListBuildCompute;
        private ComputeShader m_GPUMeshletCullingCompute;
        private ComputeShader m_FixupVisibleMeshletIndirectDrawArgsCompute;
        private int m_GPUInstanceCullingKernel = -1;
        private int m_MeshletListBuildKernel = -1;
        private int m_GPUMeshletCullingKernel = -1;
        private int m_FixupVisibleMeshletIndirectDrawArgsKernel = -1;
        private bool m_IsDisposed;

        public VividGPUDrivenCullingDispatcher()
        {
            BufferSet = new VividGPUDrivenCullingBuffers();
        }

        public VividGPUDrivenCullingBuffers BufferSet { get; }

        public void Dispatch(
            CommandBuffer cmd,
            Camera camera,
            VividGPUDrivenSceneData sceneData,
            VividGPUDrivenBufferSet sceneBuffers,
            ComputeShader gpuInstanceCullingCompute,
            ComputeShader meshletListBuildCompute,
            ComputeShader gpuMeshletCullingCompute,
            ComputeShader fixupVisibleMeshletIndirectDrawArgsCompute,
            VividInstancePassMask passMask,
            int forcedMeshLODNodeDepth,
            float meshLODErrorThreshold
        )
        {
            ThrowIfDisposed();

            if (cmd == null)
            {
                throw new ArgumentNullException(nameof(cmd));
            }

            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (sceneData == null)
            {
                throw new ArgumentNullException(nameof(sceneData));
            }

            if (sceneBuffers == null)
            {
                throw new ArgumentNullException(nameof(sceneBuffers));
            }

            BufferSet.EnsureCapacity(sceneData);
            BufferSet.Reset(cmd);

            if (sceneData.InstanceCount == 0 ||
                sceneBuffers.InstanceDataBuffer == null ||
                sceneBuffers.MaterialDataBuffer == null ||
                sceneBuffers.MeshLODNodesBuffer == null ||
                sceneBuffers.MeshletsBuffer == null ||
                gpuInstanceCullingCompute == null ||
                meshletListBuildCompute == null ||
                gpuMeshletCullingCompute == null ||
                fixupVisibleMeshletIndirectDrawArgsCompute == null)
            {
                return;
            }

            EnsureKernels(
                gpuInstanceCullingCompute,
                meshletListBuildCompute,
                gpuMeshletCullingCompute,
                fixupVisibleMeshletIndirectDrawArgsCompute
            );

            VividGPUDrivenCullingContextUtility.Build(
                camera,
                passMask,
                out VividGPUCullingContext cullingContext,
                out VividGPULODSelectionContext lodSelectionContext
            );

            BufferSet.UploadContexts(cmd, cullingContext, lodSelectionContext);

            DispatchGPUInstanceCulling(cmd, sceneData.InstanceCount, sceneBuffers);
            DispatchMeshletListBuild(cmd, sceneBuffers, forcedMeshLODNodeDepth, meshLODErrorThreshold);
            DispatchFixupVisibleMeshletIndirectDrawArgs(cmd);
            DispatchGPUMeshletCulling(cmd, sceneBuffers);
        }

        public void BindGlobals(CommandBuffer cmd)
        {
            ThrowIfDisposed();
            BufferSet.BindGlobals(cmd);
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            BufferSet.Dispose();
            m_GPUInstanceCullingCompute = null;
            m_MeshletListBuildCompute = null;
            m_GPUMeshletCullingCompute = null;
            m_FixupVisibleMeshletIndirectDrawArgsCompute = null;
            m_GPUInstanceCullingKernel = -1;
            m_MeshletListBuildKernel = -1;
            m_GPUMeshletCullingKernel = -1;
            m_FixupVisibleMeshletIndirectDrawArgsKernel = -1;
            m_IsDisposed = true;
        }

        private void DispatchGPUInstanceCulling(
            CommandBuffer cmd,
            int instanceCount,
            VividGPUDrivenBufferSet sceneBuffers
        )
        {
            cmd.SetComputeBufferParam(
                m_GPUInstanceCullingCompute,
                m_GPUInstanceCullingKernel,
                VividGPUDrivenShaderIDs._CullingContexts,
                BufferSet.CullingContextBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUInstanceCullingCompute,
                m_GPUInstanceCullingKernel,
                VividGPUDrivenShaderIDs._InstanceData,
                sceneBuffers.InstanceDataBuffer
            );
            cmd.SetComputeIntParam(
                m_GPUInstanceCullingCompute,
                VividGPUDrivenShaderIDs._InstanceDataCount,
                instanceCount
            );
            cmd.SetComputeBufferParam(
                m_GPUInstanceCullingCompute,
                m_GPUInstanceCullingKernel,
                VividGPUDrivenShaderIDs._MeshletListBuildJobs,
                BufferSet.MeshletListBuildJobsBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUInstanceCullingCompute,
                m_GPUInstanceCullingKernel,
                VividGPUDrivenShaderIDs._MeshletListBuildJobCounter,
                BufferSet.MeshletListBuildJobCounterBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUInstanceCullingCompute,
                m_GPUInstanceCullingKernel,
                VividGPUDrivenShaderIDs._MeshletListBuildIndirectArgs,
                BufferSet.MeshletListBuildIndirectArgsBuffer
            );

            int threadGroupCountX =
                (instanceCount + (int) Meshlets.VividMeshletComputeShaders.GPUInstanceCullingThreadGroupSize - 1) /
                (int) Meshlets.VividMeshletComputeShaders.GPUInstanceCullingThreadGroupSize;
            cmd.DispatchCompute(m_GPUInstanceCullingCompute, m_GPUInstanceCullingKernel, Mathf.Max(1, threadGroupCountX), 1, 1);
        }

        private void DispatchMeshletListBuild(
            CommandBuffer cmd,
            VividGPUDrivenBufferSet sceneBuffers,
            int forcedMeshLODNodeDepth,
            float meshLODErrorThreshold
        )
        {
            cmd.SetComputeBufferParam(
                m_MeshletListBuildCompute,
                m_MeshletListBuildKernel,
                VividGPUDrivenShaderIDs._CullingContexts,
                BufferSet.CullingContextBuffer
            );
            cmd.SetComputeBufferParam(
                m_MeshletListBuildCompute,
                m_MeshletListBuildKernel,
                VividGPUDrivenShaderIDs._LODSelectionContexts,
                BufferSet.LodSelectionContextBuffer
            );
            cmd.SetComputeBufferParam(
                m_MeshletListBuildCompute,
                m_MeshletListBuildKernel,
                VividGPUDrivenShaderIDs._InstanceData,
                sceneBuffers.InstanceDataBuffer
            );
            cmd.SetComputeIntParam(
                m_MeshletListBuildCompute,
                VividGPUDrivenShaderIDs._InstanceDataCount,
                sceneBuffers.InstanceCount
            );
            cmd.SetComputeBufferParam(
                m_MeshletListBuildCompute,
                m_MeshletListBuildKernel,
                VividGPUDrivenShaderIDs._MaterialData,
                sceneBuffers.MaterialDataBuffer
            );
            cmd.SetComputeBufferParam(
                m_MeshletListBuildCompute,
                m_MeshletListBuildKernel,
                VividGPUDrivenShaderIDs._MeshLODNodes,
                sceneBuffers.MeshLODNodesBuffer
            );
            cmd.SetComputeIntParam(
                m_MeshletListBuildCompute,
                VividGPUDrivenShaderIDs._MeshLODNodeCount,
                sceneBuffers.MeshLODNodeCount
            );
            cmd.SetComputeBufferParam(
                m_MeshletListBuildCompute,
                m_MeshletListBuildKernel,
                VividGPUDrivenShaderIDs._MeshletListBuildJobs,
                BufferSet.MeshletListBuildJobsBuffer
            );
            cmd.SetComputeBufferParam(
                m_MeshletListBuildCompute,
                m_MeshletListBuildKernel,
                VividGPUDrivenShaderIDs._MeshletListBuildJobCounter,
                BufferSet.MeshletListBuildJobCounterBuffer
            );
            cmd.SetComputeBufferParam(
                m_MeshletListBuildCompute,
                m_MeshletListBuildKernel,
                VividGPUDrivenShaderIDs._CandidateMeshletRenderRequests,
                BufferSet.CandidateMeshletRenderRequestsBuffer
            );
            cmd.SetComputeBufferParam(
                m_MeshletListBuildCompute,
                m_MeshletListBuildKernel,
                VividGPUDrivenShaderIDs._VisibleMeshletRenderRequestCounter,
                BufferSet.VisibleMeshletRenderRequestCounterBuffer
            );
            cmd.SetComputeBufferParam(
                m_MeshletListBuildCompute,
                m_MeshletListBuildKernel,
                VividGPUDrivenShaderIDs._VisibleRendererListMeshletCounts,
                BufferSet.VisibleRendererListMeshletCountsBuffer
            );
            cmd.SetComputeIntParam(
                m_MeshletListBuildCompute,
                VividGPUDrivenShaderIDs._ForcedMeshLODNodeDepth,
                forcedMeshLODNodeDepth < 0 ? int.MaxValue : forcedMeshLODNodeDepth
            );
            cmd.SetComputeFloatParam(
                m_MeshletListBuildCompute,
                VividGPUDrivenShaderIDs._MeshLODErrorThreshold,
                Mathf.Max(0.0f, meshLODErrorThreshold)
            );

            cmd.DispatchCompute(
                m_MeshletListBuildCompute,
                m_MeshletListBuildKernel,
                BufferSet.MeshletListBuildIndirectArgsBuffer,
                0
            );
        }

        private void DispatchFixupVisibleMeshletIndirectDrawArgs(CommandBuffer cmd)
        {
            cmd.SetComputeBufferParam(
                m_FixupVisibleMeshletIndirectDrawArgsCompute,
                m_FixupVisibleMeshletIndirectDrawArgsKernel,
                VividGPUDrivenShaderIDs._VisibleMeshletRenderRequestCounter,
                BufferSet.VisibleMeshletRenderRequestCounterBuffer
            );
            cmd.SetComputeBufferParam(
                m_FixupVisibleMeshletIndirectDrawArgsCompute,
                m_FixupVisibleMeshletIndirectDrawArgsKernel,
                VividGPUDrivenShaderIDs._VisibleRendererListMeshletCounts,
                BufferSet.VisibleRendererListMeshletCountsBuffer
            );
            cmd.SetComputeBufferParam(
                m_FixupVisibleMeshletIndirectDrawArgsCompute,
                m_FixupVisibleMeshletIndirectDrawArgsKernel,
                VividGPUDrivenShaderIDs._VisibleMeshletIndirectDrawArgs,
                BufferSet.VisibleMeshletIndirectDrawArgsBuffer
            );
            cmd.SetComputeBufferParam(
                m_FixupVisibleMeshletIndirectDrawArgsCompute,
                m_FixupVisibleMeshletIndirectDrawArgsKernel,
                VividGPUDrivenShaderIDs._GPUMeshletCullingIndirectDispatchArgs,
                BufferSet.GPUMeshletCullingIndirectDispatchArgsBuffer
            );
            cmd.DispatchCompute(m_FixupVisibleMeshletIndirectDrawArgsCompute, m_FixupVisibleMeshletIndirectDrawArgsKernel, 1, 1, 1);
        }

        private void DispatchGPUMeshletCulling(CommandBuffer cmd, VividGPUDrivenBufferSet sceneBuffers)
        {
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                m_GPUMeshletCullingKernel,
                VividGPUDrivenShaderIDs._CullingContexts,
                BufferSet.CullingContextBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                m_GPUMeshletCullingKernel,
                VividGPUDrivenShaderIDs._InstanceData,
                sceneBuffers.InstanceDataBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                m_GPUMeshletCullingKernel,
                VividGPUDrivenShaderIDs._MaterialData,
                sceneBuffers.MaterialDataBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                m_GPUMeshletCullingKernel,
                VividGPUDrivenShaderIDs._Meshlets,
                sceneBuffers.MeshletsBuffer
            );
            cmd.SetComputeIntParam(
                m_GPUMeshletCullingCompute,
                VividGPUDrivenShaderIDs._MeshletCount,
                sceneBuffers.MeshletCount
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                m_GPUMeshletCullingKernel,
                VividGPUDrivenShaderIDs._VisibleMeshletRenderRequestCounter,
                BufferSet.VisibleMeshletRenderRequestCounterBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                m_GPUMeshletCullingKernel,
                VividGPUDrivenShaderIDs._CandidateMeshletRenderRequests,
                BufferSet.CandidateMeshletRenderRequestsBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                m_GPUMeshletCullingKernel,
                VividGPUDrivenShaderIDs._VisibleMeshletRenderRequests,
                BufferSet.VisibleMeshletRenderRequestsBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                m_GPUMeshletCullingKernel,
                VividGPUDrivenShaderIDs._VisibleRendererListMeshletCounts,
                BufferSet.VisibleRendererListMeshletCountsBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                m_GPUMeshletCullingKernel,
                VividGPUDrivenShaderIDs._VisibleMeshletIndirectDrawArgs,
                BufferSet.VisibleMeshletIndirectDrawArgsBuffer
            );

            cmd.DispatchCompute(
                m_GPUMeshletCullingCompute,
                m_GPUMeshletCullingKernel,
                BufferSet.GPUMeshletCullingIndirectDispatchArgsBuffer,
                0
            );
        }

        private void EnsureKernels(
            ComputeShader gpuInstanceCullingCompute,
            ComputeShader meshletListBuildCompute,
            ComputeShader gpuMeshletCullingCompute,
            ComputeShader fixupVisibleMeshletIndirectDrawArgsCompute
        )
        {
            if (!ReferenceEquals(m_GPUInstanceCullingCompute, gpuInstanceCullingCompute))
            {
                m_GPUInstanceCullingCompute = gpuInstanceCullingCompute;
                m_GPUInstanceCullingKernel = m_GPUInstanceCullingCompute.FindKernel("CS");
            }

            if (!ReferenceEquals(m_MeshletListBuildCompute, meshletListBuildCompute))
            {
                m_MeshletListBuildCompute = meshletListBuildCompute;
                m_MeshletListBuildKernel = m_MeshletListBuildCompute.FindKernel("CS");
            }

            if (!ReferenceEquals(m_GPUMeshletCullingCompute, gpuMeshletCullingCompute))
            {
                m_GPUMeshletCullingCompute = gpuMeshletCullingCompute;
                m_GPUMeshletCullingKernel = m_GPUMeshletCullingCompute.FindKernel("CS");
            }

            if (!ReferenceEquals(m_FixupVisibleMeshletIndirectDrawArgsCompute, fixupVisibleMeshletIndirectDrawArgsCompute))
            {
                m_FixupVisibleMeshletIndirectDrawArgsCompute = fixupVisibleMeshletIndirectDrawArgsCompute;
                m_FixupVisibleMeshletIndirectDrawArgsKernel = m_FixupVisibleMeshletIndirectDrawArgsCompute != null
                    ? m_FixupVisibleMeshletIndirectDrawArgsCompute.FindKernel("CS")
                    : -1;
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(nameof(VividGPUDrivenCullingDispatcher));
            }
        }
    }
}
