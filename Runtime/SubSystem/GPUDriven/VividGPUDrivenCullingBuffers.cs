using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class VividGPUDrivenCullingBuffers : IDisposable
    {
        private NativeArray<VividGPUCullingContext> m_CullingContextUpload;
        private NativeArray<VividGPULODSelectionContext> m_LodSelectionContextUpload;
        private NativeArray<IndirectDispatchArgs> m_InitialIndirectDispatchArgsUpload;
        private NativeArray<uint> m_ZeroUintUpload;
        private NativeArray<uint> m_ZeroRendererListCountsUpload;
        private NativeArray<uint> m_ZeroIndirectDrawArgsWordsUpload;

        private GraphicsBuffer m_CullingContextBuffer;
        private GraphicsBuffer m_LodSelectionContextBuffer;
        private GraphicsBuffer m_MeshletListBuildJobsBuffer;
        private GraphicsBuffer m_MeshletListBuildJobCounterBuffer;
        private GraphicsBuffer m_MeshletListBuildIndirectArgsBuffer;
        private GraphicsBuffer m_CandidateMeshletRenderRequestsBuffer;
        private GraphicsBuffer m_GPUMeshletCullingIndirectDispatchArgsBuffer;
        private GraphicsBuffer m_VisibleMeshletRenderRequestsBuffer;
        private GraphicsBuffer m_VisibleMeshletRenderRequestCounterBuffer;
        private GraphicsBuffer m_VisibleRendererListMeshletCountsBuffer;
        private GraphicsBuffer m_VisibleMeshletIndirectDrawArgsBuffer;
        private bool m_IsDisposed;

        public VividGPUDrivenCullingBuffers()
        {
            m_CullingContextUpload = new NativeArray<VividGPUCullingContext>(1, Allocator.Persistent);
            m_LodSelectionContextUpload = new NativeArray<VividGPULODSelectionContext>(1, Allocator.Persistent);
            m_InitialIndirectDispatchArgsUpload = new NativeArray<IndirectDispatchArgs>(1, Allocator.Persistent);
            m_InitialIndirectDispatchArgsUpload[0] = new IndirectDispatchArgs
            {
                ThreadGroupsX = 0,
                ThreadGroupsY = 1,
                ThreadGroupsZ = 1,
            };
            m_ZeroUintUpload = new NativeArray<uint>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            m_ZeroRendererListCountsUpload = new NativeArray<uint>(
                Mathf.Max(1, (int)VividRendererListID.Count),
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            m_ZeroIndirectDrawArgsWordsUpload = new NativeArray<uint>(
                Mathf.Max(4, (int)VividRendererListID.Count * 4),
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
        }

        public GraphicsBuffer CullingContextBuffer => m_CullingContextBuffer;

        public GraphicsBuffer LodSelectionContextBuffer => m_LodSelectionContextBuffer;

        public GraphicsBuffer MeshletListBuildJobsBuffer => m_MeshletListBuildJobsBuffer;

        public GraphicsBuffer MeshletListBuildJobCounterBuffer => m_MeshletListBuildJobCounterBuffer;

        public GraphicsBuffer MeshletListBuildIndirectArgsBuffer => m_MeshletListBuildIndirectArgsBuffer;

        public GraphicsBuffer CandidateMeshletRenderRequestsBuffer => m_CandidateMeshletRenderRequestsBuffer;

        public GraphicsBuffer GPUMeshletCullingIndirectDispatchArgsBuffer => m_GPUMeshletCullingIndirectDispatchArgsBuffer;

        public GraphicsBuffer VisibleMeshletRenderRequestsBuffer => m_VisibleMeshletRenderRequestsBuffer;

        public GraphicsBuffer VisibleMeshletRenderRequestCounterBuffer => m_VisibleMeshletRenderRequestCounterBuffer;

        public GraphicsBuffer VisibleRendererListMeshletCountsBuffer => m_VisibleRendererListMeshletCountsBuffer;

        public GraphicsBuffer VisibleMeshletIndirectDrawArgsBuffer => m_VisibleMeshletIndirectDrawArgsBuffer;

        public int MaxMeshletListBuildJobCount { get; private set; }

        public int MaxVisibleMeshletRenderRequestCount { get; private set; }

        public void EnsureCapacity(VividGPUDrivenSceneData sceneData)
        {
            ThrowIfDisposed();

            if (sceneData == null)
            {
                throw new ArgumentNullException(nameof(sceneData));
            }

            MaxMeshletListBuildJobCount = VividGPUDrivenCullingCapacityUtility.GetMaxMeshletListBuildJobCount(sceneData);
            MaxVisibleMeshletRenderRequestCount = VividGPUDrivenCullingCapacityUtility.GetMaxVisibleMeshletRenderRequestCount(sceneData);

            EnsureStructuredBuffer(
                ref m_CullingContextBuffer,
                1,
                UnsafeUtility.SizeOf<VividGPUCullingContext>(),
                "VividGPUDriven_CullingContext"
            );
            EnsureStructuredBuffer(
                ref m_LodSelectionContextBuffer,
                1,
                UnsafeUtility.SizeOf<VividGPULODSelectionContext>(),
                "VividGPUDriven_LODSelectionContext"
            );
            EnsureStructuredBuffer(
                ref m_MeshletListBuildJobsBuffer,
                MaxMeshletListBuildJobCount,
                UnsafeUtility.SizeOf<Meshlets.VividMeshletListBuildJob>(),
                "VividGPUDriven_MeshletListBuildJobs"
            );
            EnsureStructuredBuffer(
                ref m_MeshletListBuildJobCounterBuffer,
                1,
                sizeof(uint),
                "VividGPUDriven_MeshletListBuildJobCounter"
            );
            EnsureIndirectArgsBuffer(
                ref m_MeshletListBuildIndirectArgsBuffer,
                "VividGPUDriven_MeshletListBuildIndirectArgs"
            );
            EnsureStructuredBuffer(
                ref m_CandidateMeshletRenderRequestsBuffer,
                MaxVisibleMeshletRenderRequestCount,
                UnsafeUtility.SizeOf<VividMeshletRenderRequestPacked>(),
                "VividGPUDriven_CandidateMeshletRenderRequests"
            );
            EnsureIndirectArgsBuffer(
                ref m_GPUMeshletCullingIndirectDispatchArgsBuffer,
                "VividGPUDriven_GPUMeshletCullingIndirectDispatchArgs"
            );
            EnsureStructuredBuffer(
                ref m_VisibleMeshletRenderRequestsBuffer,
                MaxVisibleMeshletRenderRequestCount,
                UnsafeUtility.SizeOf<VividMeshletRenderRequestPacked>(),
                "VividGPUDriven_VisibleMeshletRenderRequests"
            );
            EnsureStructuredBuffer(
                ref m_VisibleMeshletRenderRequestCounterBuffer,
                1,
                sizeof(uint),
                "VividGPUDriven_VisibleMeshletRenderRequestCounter"
            );
            EnsureStructuredBuffer(
                ref m_VisibleRendererListMeshletCountsBuffer,
                Mathf.Max(1, (int) VividRendererListID.Count),
                sizeof(uint),
                "VividGPUDriven_VisibleRendererListMeshletCounts"
            );
            EnsureIndirectDrawArgsBuffer(
                ref m_VisibleMeshletIndirectDrawArgsBuffer,
                Mathf.Max(1, (int) VividRendererListID.Count),
                "VividGPUDriven_VisibleMeshletIndirectDrawArgs"
            );
        }

        public void UploadContexts(
            CommandBuffer cmd,
            in VividGPUCullingContext cullingContext,
            in VividGPULODSelectionContext lodSelectionContext
        )
        {
            ThrowIfDisposed();

            if (cmd == null)
            {
                throw new ArgumentNullException(nameof(cmd));
            }

            m_CullingContextUpload[0] = cullingContext;
            m_LodSelectionContextUpload[0] = lodSelectionContext;

            cmd.SetBufferData(CullingContextBuffer, m_CullingContextUpload);
            cmd.SetBufferData(LodSelectionContextBuffer, m_LodSelectionContextUpload);
        }

        public void Reset(CommandBuffer cmd)
        {
            ThrowIfDisposed();

            if (cmd == null)
            {
                throw new ArgumentNullException(nameof(cmd));
            }

            cmd.SetBufferData(MeshletListBuildJobCounterBuffer, m_ZeroUintUpload);
            cmd.SetBufferData(MeshletListBuildIndirectArgsBuffer, m_InitialIndirectDispatchArgsUpload);
            cmd.SetBufferData(GPUMeshletCullingIndirectDispatchArgsBuffer, m_InitialIndirectDispatchArgsUpload);
            cmd.SetBufferData(VisibleMeshletRenderRequestCounterBuffer, m_ZeroUintUpload);
            cmd.SetBufferData(VisibleRendererListMeshletCountsBuffer, m_ZeroRendererListCountsUpload);
            cmd.SetBufferData(VisibleMeshletIndirectDrawArgsBuffer, m_ZeroIndirectDrawArgsWordsUpload);
        }

        public void BindGlobals(CommandBuffer cmd)
        {
            ThrowIfDisposed();

            if (cmd == null)
            {
                throw new ArgumentNullException(nameof(cmd));
            }

            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._CullingContexts, CullingContextBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._LODSelectionContexts, LodSelectionContextBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._MeshletListBuildJobs, MeshletListBuildJobsBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._MeshletListBuildJobCounter, MeshletListBuildJobCounterBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._MeshletListBuildIndirectArgs, MeshletListBuildIndirectArgsBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._VisibleMeshletRenderRequests, VisibleMeshletRenderRequestsBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._VisibleMeshletRenderRequestCounter, VisibleMeshletRenderRequestCounterBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._VisibleRendererListMeshletCounts, VisibleRendererListMeshletCountsBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._VisibleMeshletIndirectDrawArgs, VisibleMeshletIndirectDrawArgsBuffer);
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            m_CullingContextBuffer?.Dispose();
            m_LodSelectionContextBuffer?.Dispose();
            m_MeshletListBuildJobsBuffer?.Dispose();
            m_MeshletListBuildJobCounterBuffer?.Dispose();
            m_MeshletListBuildIndirectArgsBuffer?.Dispose();
            m_CandidateMeshletRenderRequestsBuffer?.Dispose();
            m_GPUMeshletCullingIndirectDispatchArgsBuffer?.Dispose();
            m_VisibleMeshletRenderRequestsBuffer?.Dispose();
            m_VisibleMeshletRenderRequestCounterBuffer?.Dispose();
            m_VisibleRendererListMeshletCountsBuffer?.Dispose();
            m_VisibleMeshletIndirectDrawArgsBuffer?.Dispose();
            DisposeNativeArray(ref m_CullingContextUpload);
            DisposeNativeArray(ref m_LodSelectionContextUpload);
            DisposeNativeArray(ref m_InitialIndirectDispatchArgsUpload);
            DisposeNativeArray(ref m_ZeroUintUpload);
            DisposeNativeArray(ref m_ZeroRendererListCountsUpload);
            DisposeNativeArray(ref m_ZeroIndirectDrawArgsWordsUpload);

            m_CullingContextBuffer = null;
            m_LodSelectionContextBuffer = null;
            m_MeshletListBuildJobsBuffer = null;
            m_MeshletListBuildJobCounterBuffer = null;
            m_MeshletListBuildIndirectArgsBuffer = null;
            m_CandidateMeshletRenderRequestsBuffer = null;
            m_GPUMeshletCullingIndirectDispatchArgsBuffer = null;
            m_VisibleMeshletRenderRequestsBuffer = null;
            m_VisibleMeshletRenderRequestCounterBuffer = null;
            m_VisibleRendererListMeshletCountsBuffer = null;
            m_VisibleMeshletIndirectDrawArgsBuffer = null;
            MaxMeshletListBuildJobCount = 0;
            MaxVisibleMeshletRenderRequestCount = 0;
            m_IsDisposed = true;
        }

        private static void EnsureStructuredBuffer(
            ref GraphicsBuffer buffer,
            int count,
            int stride,
            string bufferName
        )
        {
            EnsureStructuredBuffer(ref buffer, count, stride, GraphicsBuffer.Target.Structured, bufferName);
        }

        private static void EnsureStructuredBuffer(
            ref GraphicsBuffer buffer,
            int count,
            int stride,
            GraphicsBuffer.Target target,
            string bufferName
        )
        {
            int clampedCount = Mathf.Max(1, count);
            int clampedStride = Mathf.Max(1, stride);

            if (buffer != null && buffer.count == clampedCount && buffer.stride == clampedStride && buffer.target == target)
            {
                return;
            }

            buffer?.Dispose();
            buffer = new GraphicsBuffer(target, clampedCount, clampedStride)
            {
                name = bufferName,
            };
        }

        private static void EnsureIndirectArgsBuffer(ref GraphicsBuffer buffer, string bufferName)
        {
            const int elementCount = 3;
            const int stride = sizeof(uint);
            GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments;

            if (buffer != null && buffer.count == elementCount && buffer.stride == stride)
            {
                return;
            }

            buffer?.Dispose();
            buffer = new GraphicsBuffer(target, elementCount, stride)
            {
                name = bufferName,
            };
        }

        private static void EnsureIndirectDrawArgsBuffer(ref GraphicsBuffer buffer, int commandCount, string bufferName)
        {
            int elementCount = Mathf.Max(4, commandCount * 4);
            const int stride = sizeof(uint);
            GraphicsBuffer.Target target = GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments;

            if (buffer != null && buffer.count == elementCount && buffer.stride == stride && buffer.target == target)
            {
                return;
            }

            buffer?.Dispose();
            buffer = new GraphicsBuffer(target, elementCount, stride)
            {
                name = bufferName,
            };
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(nameof(VividGPUDrivenCullingBuffers));
            }
        }

        private static void DisposeNativeArray<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (!array.IsCreated)
            {
                return;
            }

            array.Dispose();
            array = default;
        }
    }
}
