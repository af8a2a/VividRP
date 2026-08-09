using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class VividGPUDrivenCullingBuffers : IDisposable
    {
        internal const int IndirectDrawArgsWordCount = 4;
        internal const int IndirectDrawArgsByteStride = sizeof(uint) * IndirectDrawArgsWordCount;

        private const int RendererListCount = (int)VividRendererListID.Count;

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
        private GraphicsBuffer m_OccludedMeshletRenderRequestsBuffer;
        private GraphicsBuffer m_OccludedMeshletRenderRequestCounterBuffer;
        private GraphicsBuffer m_OccludedMeshletIndirectDispatchArgsBuffer;
        private GraphicsBuffer m_RecoveredMeshletRenderRequestsBuffer;
        private GraphicsBuffer m_RecoveredRendererListMeshletCountsBuffer;
        private GraphicsBuffer m_RecoveredMeshletIndirectDrawArgsBuffer;
        private bool m_IsDisposed;

        public VividGPUDrivenCullingBuffers(bool supportsOcclusion = true)
        {
            SupportsOcclusion = supportsOcclusion;
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
                Mathf.Max(1, RendererListCount),
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            m_ZeroIndirectDrawArgsWordsUpload = new NativeArray<uint>(
                Mathf.Max(IndirectDrawArgsWordCount, RendererListCount * IndirectDrawArgsWordCount),
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            CullingContextCount = 1;
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

        public GraphicsBuffer OccludedMeshletRenderRequestsBuffer => m_OccludedMeshletRenderRequestsBuffer;

        public GraphicsBuffer OccludedMeshletRenderRequestCounterBuffer => m_OccludedMeshletRenderRequestCounterBuffer;

        public GraphicsBuffer OccludedMeshletIndirectDispatchArgsBuffer => m_OccludedMeshletIndirectDispatchArgsBuffer;

        public GraphicsBuffer RecoveredMeshletRenderRequestsBuffer => m_RecoveredMeshletRenderRequestsBuffer;

        public GraphicsBuffer RecoveredRendererListMeshletCountsBuffer => m_RecoveredRendererListMeshletCountsBuffer;

        public GraphicsBuffer RecoveredMeshletIndirectDrawArgsBuffer => m_RecoveredMeshletIndirectDrawArgsBuffer;

        public bool SupportsOcclusion { get; }

        public int MaxMeshletListBuildJobCount { get; private set; }

        public int MaxVisibleMeshletRenderRequestCount { get; private set; }

        public int CullingContextCount { get; private set; }

        public void EnsureCapacity(VividGPUDrivenSceneData sceneData)
        {
            EnsureCapacity(sceneData, 1);
        }

        public void EnsureCapacity(VividGPUDrivenSceneData sceneData, int cullingContextCount)
        {
            ThrowIfDisposed();

            if (sceneData == null)
            {
                throw new ArgumentNullException(nameof(sceneData));
            }

            if (cullingContextCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cullingContextCount));
            }

            if (SupportsOcclusion && cullingContextCount != 1)
            {
                throw new ArgumentException(
                    "Batched culling contexts do not support occlusion buffers.",
                    nameof(cullingContextCount));
            }

            MaxMeshletListBuildJobCount = VividGPUDrivenCullingCapacityUtility.GetMaxMeshletListBuildJobCount(sceneData);
            MaxVisibleMeshletRenderRequestCount = VividGPUDrivenCullingCapacityUtility.GetMaxVisibleMeshletRenderRequestCount(sceneData);
            CullingContextCount = cullingContextCount;

            int totalMeshletListBuildJobCount = MultiplyCapacity(
                MaxMeshletListBuildJobCount,
                cullingContextCount);
            int totalVisibleMeshletRenderRequestCount = MultiplyCapacity(
                MaxVisibleMeshletRenderRequestCount,
                cullingContextCount);
            int totalRendererListCount = MultiplyCapacity(RendererListCount, cullingContextCount);

            EnsureUploadCapacity(cullingContextCount, totalRendererListCount);
            UpdateInitialIndirectDispatchArgs(cullingContextCount);

            EnsureStructuredBuffer(
                ref m_CullingContextBuffer,
                cullingContextCount,
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
                totalMeshletListBuildJobCount,
                UnsafeUtility.SizeOf<Meshlets.VividMeshletListBuildJob>(),
                "VividGPUDriven_MeshletListBuildJobs"
            );
            EnsureStructuredBuffer(
                ref m_MeshletListBuildJobCounterBuffer,
                cullingContextCount,
                sizeof(uint),
                "VividGPUDriven_MeshletListBuildJobCounter"
            );
            EnsureIndirectArgsBuffer(
                ref m_MeshletListBuildIndirectArgsBuffer,
                "VividGPUDriven_MeshletListBuildIndirectArgs"
            );
            EnsureStructuredBuffer(
                ref m_CandidateMeshletRenderRequestsBuffer,
                totalVisibleMeshletRenderRequestCount,
                UnsafeUtility.SizeOf<VividMeshletRenderRequestPacked>(),
                "VividGPUDriven_CandidateMeshletRenderRequests"
            );
            EnsureIndirectArgsBuffer(
                ref m_GPUMeshletCullingIndirectDispatchArgsBuffer,
                "VividGPUDriven_GPUMeshletCullingIndirectDispatchArgs"
            );
            EnsureStructuredBuffer(
                ref m_VisibleMeshletRenderRequestsBuffer,
                totalVisibleMeshletRenderRequestCount,
                UnsafeUtility.SizeOf<VividMeshletRenderRequestPacked>(),
                "VividGPUDriven_VisibleMeshletRenderRequests"
            );
            EnsureStructuredBuffer(
                ref m_VisibleMeshletRenderRequestCounterBuffer,
                cullingContextCount,
                sizeof(uint),
                "VividGPUDriven_VisibleMeshletRenderRequestCounter"
            );
            EnsureStructuredBuffer(
                ref m_VisibleRendererListMeshletCountsBuffer,
                totalRendererListCount,
                sizeof(uint),
                "VividGPUDriven_VisibleRendererListMeshletCounts"
            );
            EnsureIndirectDrawArgsBuffer(
                ref m_VisibleMeshletIndirectDrawArgsBuffer,
                totalRendererListCount,
                "VividGPUDriven_VisibleMeshletIndirectDrawArgs"
            );
            if (SupportsOcclusion)
            {
                EnsureStructuredBuffer(
                    ref m_OccludedMeshletRenderRequestsBuffer,
                    MaxVisibleMeshletRenderRequestCount,
                    UnsafeUtility.SizeOf<VividMeshletRenderRequestPacked>(),
                    "VividGPUDriven_OccludedMeshletRenderRequests"
                );
                EnsureStructuredBuffer(
                    ref m_OccludedMeshletRenderRequestCounterBuffer,
                    1,
                    sizeof(uint),
                    "VividGPUDriven_OccludedMeshletRenderRequestCounter"
                );
                EnsureIndirectArgsBuffer(
                    ref m_OccludedMeshletIndirectDispatchArgsBuffer,
                    "VividGPUDriven_OccludedMeshletIndirectDispatchArgs"
                );
                EnsureStructuredBuffer(
                    ref m_RecoveredMeshletRenderRequestsBuffer,
                    MaxVisibleMeshletRenderRequestCount,
                    UnsafeUtility.SizeOf<VividMeshletRenderRequestPacked>(),
                    "VividGPUDriven_RecoveredMeshletRenderRequests"
                );
                EnsureStructuredBuffer(
                    ref m_RecoveredRendererListMeshletCountsBuffer,
                    Mathf.Max(1, (int) VividRendererListID.Count),
                    sizeof(uint),
                    "VividGPUDriven_RecoveredRendererListMeshletCounts"
                );
                EnsureIndirectDrawArgsBuffer(
                    ref m_RecoveredMeshletIndirectDrawArgsBuffer,
                    Mathf.Max(1, (int) VividRendererListID.Count),
                    "VividGPUDriven_RecoveredMeshletIndirectDrawArgs"
                );
            }
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

            VividGPUCullingContext uploadContext = cullingContext;
            SetContextOffsets(ref uploadContext, 0);
            m_CullingContextUpload[0] = uploadContext;
            m_LodSelectionContextUpload[0] = lodSelectionContext;

            cmd.SetBufferData(CullingContextBuffer, m_CullingContextUpload);
            cmd.SetBufferData(LodSelectionContextBuffer, m_LodSelectionContextUpload);
        }

        public void UploadContexts(
            CommandBuffer cmd,
            VividGPUCullingContext[] cullingContexts,
            int cullingContextCount,
            in VividGPULODSelectionContext lodSelectionContext
        )
        {
            ThrowIfDisposed();

            if (cmd == null)
            {
                throw new ArgumentNullException(nameof(cmd));
            }

            if (cullingContexts == null)
            {
                throw new ArgumentNullException(nameof(cullingContexts));
            }

            if (cullingContextCount <= 0 || cullingContextCount > cullingContexts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(cullingContextCount));
            }

            if (cullingContextCount != CullingContextCount)
            {
                throw new InvalidOperationException(
                    "EnsureCapacity must be called with the same culling context count before uploading contexts.");
            }

            for (int contextIndex = 0; contextIndex < cullingContextCount; contextIndex++)
            {
                VividGPUCullingContext uploadContext = cullingContexts[contextIndex];
                SetContextOffsets(ref uploadContext, contextIndex);
                m_CullingContextUpload[contextIndex] = uploadContext;
            }

            m_LodSelectionContextUpload[0] = lodSelectionContext;
            cmd.SetBufferData(CullingContextBuffer, m_CullingContextUpload);
            cmd.SetBufferData(LodSelectionContextBuffer, m_LodSelectionContextUpload);
        }

        internal static int GetContextOffset(int contextIndex, int perContextCapacity)
        {
            if (contextIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contextIndex));
            }

            if (perContextCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(perContextCapacity));
            }

            return MultiplyCapacity(perContextCapacity, contextIndex);
        }

        internal static int GetIndirectDrawArgsCommandIndex(int contextIndex, int rendererListIndex)
        {
            if (contextIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contextIndex));
            }

            if (rendererListIndex < 0 || rendererListIndex >= RendererListCount)
            {
                throw new ArgumentOutOfRangeException(nameof(rendererListIndex));
            }

            return checked(contextIndex * RendererListCount + rendererListIndex);
        }

        internal static int GetIndirectDrawArgsByteOffset(int contextIndex, int rendererListIndex)
        {
            return checked(GetIndirectDrawArgsCommandIndex(contextIndex, rendererListIndex)
                           * IndirectDrawArgsByteStride);
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
            if (SupportsOcclusion)
            {
                cmd.SetBufferData(OccludedMeshletRenderRequestCounterBuffer, m_ZeroUintUpload);
                cmd.SetBufferData(OccludedMeshletIndirectDispatchArgsBuffer, m_InitialIndirectDispatchArgsUpload);
                cmd.SetBufferData(RecoveredRendererListMeshletCountsBuffer, m_ZeroRendererListCountsUpload);
                cmd.SetBufferData(RecoveredMeshletIndirectDrawArgsBuffer, m_ZeroIndirectDrawArgsWordsUpload);
            }
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
            if (SupportsOcclusion)
            {
                cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._OccludedMeshletRenderRequests, OccludedMeshletRenderRequestsBuffer);
                cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._OccludedMeshletRenderRequestCounter, OccludedMeshletRenderRequestCounterBuffer);
                cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._OccludedMeshletIndirectDispatchArgs, OccludedMeshletIndirectDispatchArgsBuffer);
                cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._RecoveredMeshletRenderRequests, RecoveredMeshletRenderRequestsBuffer);
                cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._RecoveredRendererListMeshletCounts, RecoveredRendererListMeshletCountsBuffer);
                cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._RecoveredMeshletIndirectDrawArgs, RecoveredMeshletIndirectDrawArgsBuffer);
            }
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
            m_OccludedMeshletRenderRequestsBuffer?.Dispose();
            m_OccludedMeshletRenderRequestCounterBuffer?.Dispose();
            m_OccludedMeshletIndirectDispatchArgsBuffer?.Dispose();
            m_RecoveredMeshletRenderRequestsBuffer?.Dispose();
            m_RecoveredRendererListMeshletCountsBuffer?.Dispose();
            m_RecoveredMeshletIndirectDrawArgsBuffer?.Dispose();
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
            m_OccludedMeshletRenderRequestsBuffer = null;
            m_OccludedMeshletRenderRequestCounterBuffer = null;
            m_OccludedMeshletIndirectDispatchArgsBuffer = null;
            m_RecoveredMeshletRenderRequestsBuffer = null;
            m_RecoveredRendererListMeshletCountsBuffer = null;
            m_RecoveredMeshletIndirectDrawArgsBuffer = null;
            MaxMeshletListBuildJobCount = 0;
            MaxVisibleMeshletRenderRequestCount = 0;
            CullingContextCount = 0;
            m_IsDisposed = true;
        }

        private void SetContextOffsets(ref VividGPUCullingContext cullingContext, int contextIndex)
        {
            int meshletListBuildJobsOffset = GetContextOffset(
                contextIndex,
                MaxMeshletListBuildJobCount);
            int meshletRenderRequestsOffset = GetContextOffset(
                contextIndex,
                MaxVisibleMeshletRenderRequestCount);

            cullingContext.BaseStartInstance = (uint)meshletRenderRequestsOffset;
            cullingContext.MeshletListBuildJobsOffset = (uint)meshletListBuildJobsOffset;
            cullingContext.MeshletRenderRequestsOffset = (uint)meshletRenderRequestsOffset;
        }

        private void EnsureUploadCapacity(int cullingContextCount, int totalRendererListCount)
        {
            ResizeNativeArray(
                ref m_CullingContextUpload,
                cullingContextCount,
                NativeArrayOptions.UninitializedMemory);
            ResizeNativeArray(
                ref m_ZeroUintUpload,
                cullingContextCount,
                NativeArrayOptions.ClearMemory);
            ResizeNativeArray(
                ref m_ZeroRendererListCountsUpload,
                totalRendererListCount,
                NativeArrayOptions.ClearMemory);
            ResizeNativeArray(
                ref m_ZeroIndirectDrawArgsWordsUpload,
                MultiplyCapacity(totalRendererListCount, IndirectDrawArgsWordCount),
                NativeArrayOptions.ClearMemory);
        }

        private void UpdateInitialIndirectDispatchArgs(int cullingContextCount)
        {
            m_InitialIndirectDispatchArgsUpload[0] = new IndirectDispatchArgs
            {
                ThreadGroupsX = 0,
                ThreadGroupsY = (uint)cullingContextCount,
                ThreadGroupsZ = 1,
            };
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
            int elementCount = Mathf.Max(
                IndirectDrawArgsWordCount,
                MultiplyCapacity(commandCount, IndirectDrawArgsWordCount));
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

        private static int MultiplyCapacity(int count, int multiplier)
        {
            long result = (long)count * multiplier;
            if (result < 0 || result > int.MaxValue)
            {
                throw new InvalidOperationException("GPU-driven culling buffer capacity exceeds the supported range.");
            }

            return (int)result;
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

        private static void ResizeNativeArray<T>(
            ref NativeArray<T> array,
            int length,
            NativeArrayOptions options)
            where T : struct
        {
            if (array.IsCreated && array.Length == length)
            {
                return;
            }

            DisposeNativeArray(ref array);
            array = new NativeArray<T>(length, Allocator.Persistent, options);
        }
    }
}
