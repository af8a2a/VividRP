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
        private int m_GPUMeshletOcclusionTestAllKernel = -1;
        private int m_FixupOccludedMeshletIndirectArgsKernel = -1;
        private int m_PrepareOcclusionRetestKernel = -1;
        private int m_OcclusionRetestKernel = -1;
        private int m_FixupVisibleMeshletIndirectDrawArgsKernel = -1;
        private bool m_IsDisposed;

        public VividGPUDrivenCullingDispatcher(bool supportsOcclusion = true)
        {
            BufferSet = new VividGPUDrivenCullingBuffers(supportsOcclusion);
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
            float meshLODErrorThreshold,
            VividGPUDrivenOcclusionCullingParameters occlusionParameters = default
        )
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            VividGPUCullingContext cullingContext;
            VividGPULODSelectionContext lodSelectionContext;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenCullBuildContextMarker.Auto())
            {
                VividGPUDrivenCullingContextUtility.Build(
                    camera,
                    passMask,
                    out cullingContext,
                    out lodSelectionContext
                );
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenCullDispatchMarker.Auto())
            {
                Dispatch(
                    cmd,
                    cullingContext,
                    lodSelectionContext,
                    sceneData,
                    sceneBuffers,
                    gpuInstanceCullingCompute,
                    meshletListBuildCompute,
                    gpuMeshletCullingCompute,
                    fixupVisibleMeshletIndirectDrawArgsCompute,
                    forcedMeshLODNodeDepth,
                    meshLODErrorThreshold,
                    occlusionParameters
                );
            }
        }

        public void Dispatch(
            CommandBuffer cmd,
            in VividGPUCullingContext cullingContext,
            in VividGPULODSelectionContext lodSelectionContext,
            VividGPUDrivenSceneData sceneData,
            VividGPUDrivenBufferSet sceneBuffers,
            ComputeShader gpuInstanceCullingCompute,
            ComputeShader meshletListBuildCompute,
            ComputeShader gpuMeshletCullingCompute,
            ComputeShader fixupVisibleMeshletIndirectDrawArgsCompute,
            int forcedMeshLODNodeDepth,
            float meshLODErrorThreshold,
            VividGPUDrivenOcclusionCullingParameters occlusionParameters = default
        )
        {
            ThrowIfDisposed();

            if (cmd == null)
            {
                throw new ArgumentNullException(nameof(cmd));
            }

            if (sceneData == null)
            {
                throw new ArgumentNullException(nameof(sceneData));
            }

            if (sceneBuffers == null)
            {
                throw new ArgumentNullException(nameof(sceneBuffers));
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenCullDispatchEnsureCapacityMarker.Auto())
            {
                BufferSet.EnsureCapacity(sceneData);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenCullDispatchResetBuffersMarker.Auto())
            {
                BufferSet.Reset(cmd);
            }

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

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenCullDispatchEnsureKernelsMarker.Auto())
            {
                EnsureKernels(
                    gpuInstanceCullingCompute,
                    meshletListBuildCompute,
                    gpuMeshletCullingCompute,
                    fixupVisibleMeshletIndirectDrawArgsCompute
                );
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenCullDispatchUploadContextsMarker.Auto())
            {
                BufferSet.UploadContexts(cmd, cullingContext, lodSelectionContext);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenCullDispatchInstanceCullingMarker.Auto())
            {
                DispatchGPUInstanceCulling(cmd, sceneData.InstanceCount, sceneBuffers);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenCullDispatchMeshletListBuildMarker.Auto())
            {
                DispatchMeshletListBuild(cmd, sceneBuffers, forcedMeshLODNodeDepth, meshLODErrorThreshold);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenCullDispatchFixupDrawArgsMarker.Auto())
            {
                DispatchFixupVisibleMeshletIndirectDrawArgs(cmd);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenCullDispatchMeshletCullingMarker.Auto())
            {
                DispatchGPUMeshletCulling(cmd, sceneBuffers, occlusionParameters);
            }
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
            m_GPUMeshletOcclusionTestAllKernel = -1;
            m_FixupOccludedMeshletIndirectArgsKernel = -1;
            m_PrepareOcclusionRetestKernel = -1;
            m_OcclusionRetestKernel = -1;
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

        private void DispatchGPUMeshletCulling(
            CommandBuffer cmd,
            VividGPUDrivenBufferSet sceneBuffers,
            in VividGPUDrivenOcclusionCullingParameters occlusionParameters)
        {
            bool testOcclusion = occlusionParameters.IsEnabled
                && BufferSet.SupportsOcclusion
                && m_GPUMeshletOcclusionTestAllKernel >= 0;
            int kernel = testOcclusion
                ? m_GPUMeshletOcclusionTestAllKernel
                : m_GPUMeshletCullingKernel;

            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                kernel,
                VividGPUDrivenShaderIDs._CullingContexts,
                BufferSet.CullingContextBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                kernel,
                VividGPUDrivenShaderIDs._InstanceData,
                sceneBuffers.InstanceDataBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                kernel,
                VividGPUDrivenShaderIDs._MaterialData,
                sceneBuffers.MaterialDataBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                kernel,
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
                kernel,
                VividGPUDrivenShaderIDs._VisibleMeshletRenderRequestCounter,
                BufferSet.VisibleMeshletRenderRequestCounterBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                kernel,
                VividGPUDrivenShaderIDs._CandidateMeshletRenderRequests,
                BufferSet.CandidateMeshletRenderRequestsBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                kernel,
                VividGPUDrivenShaderIDs._VisibleMeshletRenderRequests,
                BufferSet.VisibleMeshletRenderRequestsBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                kernel,
                VividGPUDrivenShaderIDs._VisibleRendererListMeshletCounts,
                BufferSet.VisibleRendererListMeshletCountsBuffer
            );
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                kernel,
                VividGPUDrivenShaderIDs._VisibleMeshletIndirectDrawArgs,
                BufferSet.VisibleMeshletIndirectDrawArgsBuffer
            );

            if (testOcclusion)
            {
                BindOcclusionTestParameters(
                    cmd,
                    m_GPUMeshletCullingCompute,
                    kernel,
                    occlusionParameters,
                    testMode: 1);
                cmd.SetComputeBufferParam(
                    m_GPUMeshletCullingCompute,
                    kernel,
                    VividGPUDrivenShaderIDs._OccludedMeshletRenderRequests,
                    BufferSet.OccludedMeshletRenderRequestsBuffer);
                cmd.SetComputeBufferParam(
                    m_GPUMeshletCullingCompute,
                    kernel,
                    VividGPUDrivenShaderIDs._OccludedMeshletRenderRequestCounter,
                    BufferSet.OccludedMeshletRenderRequestCounterBuffer);
                cmd.SetComputeIntParam(
                    m_GPUMeshletCullingCompute,
                    VividGPUDrivenShaderIDs._OccludedMeshletCapacity,
                    BufferSet.MaxVisibleMeshletRenderRequestCount);
            }

            cmd.DispatchCompute(
                m_GPUMeshletCullingCompute,
                kernel,
                BufferSet.GPUMeshletCullingIndirectDispatchArgsBuffer,
                0
            );

            if (!testOcclusion)
                return;

            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                m_FixupOccludedMeshletIndirectArgsKernel,
                VividGPUDrivenShaderIDs._OccludedMeshletRenderRequestCounter,
                BufferSet.OccludedMeshletRenderRequestCounterBuffer);
            cmd.SetComputeBufferParam(
                m_GPUMeshletCullingCompute,
                m_FixupOccludedMeshletIndirectArgsKernel,
                VividGPUDrivenShaderIDs._OccludedMeshletIndirectDispatchArgs,
                BufferSet.OccludedMeshletIndirectDispatchArgsBuffer);
            cmd.SetComputeIntParam(
                m_GPUMeshletCullingCompute,
                VividGPUDrivenShaderIDs._OccludedMeshletCapacity,
                BufferSet.MaxVisibleMeshletRenderRequestCount);
            cmd.DispatchCompute(
                m_GPUMeshletCullingCompute,
                m_FixupOccludedMeshletIndirectArgsKernel,
                1,
                1,
                1);
        }

        internal bool DispatchOcclusionRetest(
            CommandBuffer cmd,
            VividGPUDrivenBufferSet sceneBuffers,
            ComputeShader gpuMeshletCullingCompute,
            RTHandle currentOccluderDepthPyramid,
            Matrix4x4 currentViewProjectionMatrix,
            int width,
            int height,
            int textureWidth,
            int textureHeight,
            int mipCount)
        {
            ThrowIfDisposed();
            if (cmd == null
                || sceneBuffers == null
                || gpuMeshletCullingCompute == null
                || currentOccluderDepthPyramid == null
                || !BufferSet.SupportsOcclusion)
            {
                return false;
            }

            EnsureMeshletCullingKernels(gpuMeshletCullingCompute);
            if (m_PrepareOcclusionRetestKernel < 0 || m_OcclusionRetestKernel < 0)
                return false;

            cmd.SetComputeBufferParam(
                gpuMeshletCullingCompute,
                m_PrepareOcclusionRetestKernel,
                VividGPUDrivenShaderIDs._VisibleMeshletIndirectDrawArgs,
                BufferSet.VisibleMeshletIndirectDrawArgsBuffer);
            cmd.SetComputeBufferParam(
                gpuMeshletCullingCompute,
                m_PrepareOcclusionRetestKernel,
                VividGPUDrivenShaderIDs._RecoveredMeshletIndirectDrawArgs,
                BufferSet.RecoveredMeshletIndirectDrawArgsBuffer);
            cmd.SetComputeBufferParam(
                gpuMeshletCullingCompute,
                m_PrepareOcclusionRetestKernel,
                VividGPUDrivenShaderIDs._RecoveredRendererListMeshletCounts,
                BufferSet.RecoveredRendererListMeshletCountsBuffer);
            cmd.DispatchCompute(gpuMeshletCullingCompute, m_PrepareOcclusionRetestKernel, 1, 1, 1);

            var parameters = new VividGPUDrivenOcclusionCullingParameters(
                currentOccluderDepthPyramid,
                currentViewProjectionMatrix,
                width,
                height,
                textureWidth,
                textureHeight,
                mipCount,
                VividGPUDrivenOcclusionHistorySystem.ConservativeDepthBias);
            BindOcclusionTestParameters(
                cmd,
                gpuMeshletCullingCompute,
                m_OcclusionRetestKernel,
                parameters,
                testMode: 2);
            cmd.SetComputeBufferParam(
                gpuMeshletCullingCompute,
                m_OcclusionRetestKernel,
                VividGPUDrivenShaderIDs._InstanceData,
                sceneBuffers.InstanceDataBuffer);
            cmd.SetComputeBufferParam(
                gpuMeshletCullingCompute,
                m_OcclusionRetestKernel,
                VividGPUDrivenShaderIDs._MaterialData,
                sceneBuffers.MaterialDataBuffer);
            cmd.SetComputeBufferParam(
                gpuMeshletCullingCompute,
                m_OcclusionRetestKernel,
                VividGPUDrivenShaderIDs._Meshlets,
                sceneBuffers.MeshletsBuffer);
            cmd.SetComputeIntParam(
                gpuMeshletCullingCompute,
                VividGPUDrivenShaderIDs._MeshletCount,
                sceneBuffers.MeshletCount);
            cmd.SetComputeBufferParam(
                gpuMeshletCullingCompute,
                m_OcclusionRetestKernel,
                VividGPUDrivenShaderIDs._OccludedMeshletRenderRequests,
                BufferSet.OccludedMeshletRenderRequestsBuffer);
            cmd.SetComputeBufferParam(
                gpuMeshletCullingCompute,
                m_OcclusionRetestKernel,
                VividGPUDrivenShaderIDs._OccludedMeshletRenderRequestCounter,
                BufferSet.OccludedMeshletRenderRequestCounterBuffer);
            cmd.SetComputeBufferParam(
                gpuMeshletCullingCompute,
                m_OcclusionRetestKernel,
                VividGPUDrivenShaderIDs._RecoveredMeshletRenderRequests,
                BufferSet.RecoveredMeshletRenderRequestsBuffer);
            cmd.SetComputeBufferParam(
                gpuMeshletCullingCompute,
                m_OcclusionRetestKernel,
                VividGPUDrivenShaderIDs._RecoveredRendererListMeshletCounts,
                BufferSet.RecoveredRendererListMeshletCountsBuffer);
            cmd.SetComputeBufferParam(
                gpuMeshletCullingCompute,
                m_OcclusionRetestKernel,
                VividGPUDrivenShaderIDs._RecoveredMeshletIndirectDrawArgs,
                BufferSet.RecoveredMeshletIndirectDrawArgsBuffer);
            cmd.SetComputeIntParam(
                gpuMeshletCullingCompute,
                VividGPUDrivenShaderIDs._OccludedMeshletCapacity,
                BufferSet.MaxVisibleMeshletRenderRequestCount);
            cmd.DispatchCompute(
                gpuMeshletCullingCompute,
                m_OcclusionRetestKernel,
                BufferSet.OccludedMeshletIndirectDispatchArgsBuffer,
                0);
            return true;
        }

        private static void BindOcclusionTestParameters(
            CommandBuffer cmd,
            ComputeShader computeShader,
            int kernel,
            in VividGPUDrivenOcclusionCullingParameters parameters,
            int testMode)
        {
            cmd.SetComputeTextureParam(
                computeShader,
                kernel,
                VividGPUDrivenShaderIDs._OccluderDepthPyramid,
                parameters.DepthPyramid);
            cmd.SetComputeMatrixParam(
                computeShader,
                VividGPUDrivenShaderIDs._OccluderViewProjectionMatrix,
                parameters.ViewProjectionMatrix);
            cmd.SetComputeVectorParam(
                computeShader,
                VividGPUDrivenShaderIDs._OccluderDepthPyramidSize,
                new Vector4(
                    parameters.Width,
                    parameters.Height,
                    1.0f / Mathf.Max(1, parameters.Width),
                    1.0f / Mathf.Max(1, parameters.Height)));
            cmd.SetComputeVectorParam(
                computeShader,
                VividGPUDrivenShaderIDs._OccluderDepthPyramidTextureSize,
                new Vector4(
                    parameters.TextureWidth,
                    parameters.TextureHeight,
                    1.0f / Mathf.Max(1, parameters.TextureWidth),
                    1.0f / Mathf.Max(1, parameters.TextureHeight)));
            cmd.SetComputeIntParam(
                computeShader,
                VividGPUDrivenShaderIDs._OccluderDepthPyramidMipCount,
                parameters.MipCount);
            cmd.SetComputeIntParam(
                computeShader,
                VividGPUDrivenShaderIDs._OcclusionTestMode,
                testMode);
            cmd.SetComputeFloatParam(
                computeShader,
                VividGPUDrivenShaderIDs._OcclusionDepthBias,
                parameters.DepthBias);
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
                EnsureMeshletCullingKernels(gpuMeshletCullingCompute);
            }

            if (!ReferenceEquals(m_FixupVisibleMeshletIndirectDrawArgsCompute, fixupVisibleMeshletIndirectDrawArgsCompute))
            {
                m_FixupVisibleMeshletIndirectDrawArgsCompute = fixupVisibleMeshletIndirectDrawArgsCompute;
                m_FixupVisibleMeshletIndirectDrawArgsKernel = m_FixupVisibleMeshletIndirectDrawArgsCompute != null
                    ? m_FixupVisibleMeshletIndirectDrawArgsCompute.FindKernel("CS")
                    : -1;
            }
        }

        private void EnsureMeshletCullingKernels(ComputeShader gpuMeshletCullingCompute)
        {
            if (ReferenceEquals(m_GPUMeshletCullingCompute, gpuMeshletCullingCompute)
                && m_GPUMeshletCullingKernel >= 0)
            {
                return;
            }

            m_GPUMeshletCullingCompute = gpuMeshletCullingCompute;
            if (m_GPUMeshletCullingCompute == null)
            {
                m_GPUMeshletCullingKernel = -1;
                m_GPUMeshletOcclusionTestAllKernel = -1;
                m_FixupOccludedMeshletIndirectArgsKernel = -1;
                m_PrepareOcclusionRetestKernel = -1;
                m_OcclusionRetestKernel = -1;
                return;
            }

            m_GPUMeshletCullingKernel = m_GPUMeshletCullingCompute.FindKernel("CS");
            m_GPUMeshletOcclusionTestAllKernel = m_GPUMeshletCullingCompute.FindKernel("CSOcclusionTestAll");
            m_FixupOccludedMeshletIndirectArgsKernel = m_GPUMeshletCullingCompute.FindKernel("CSFixupOccludedMeshletIndirectArgs");
            m_PrepareOcclusionRetestKernel = m_GPUMeshletCullingCompute.FindKernel("CSPrepareOcclusionRetest");
            m_OcclusionRetestKernel = m_GPUMeshletCullingCompute.FindKernel("CSOcclusionTestCulled");
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
