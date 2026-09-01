using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using Unity.Scripting.LifecycleManagement;

namespace VividRP.Runtime
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct VTPageTableScatterUpdate
    {
        internal const int Stride = sizeof(uint) * 2;

        internal readonly uint DestinationIndex;
        internal readonly uint PackedValue;

        internal VTPageTableScatterUpdate(int destinationIndex, uint packedValue)
        {
            DestinationIndex = checked((uint)destinationIndex);
            PackedValue = packedValue;
        }
    }

    internal sealed class VTPageTableScatterUploader
    {
        private const string KernelName = "ScatterPageTableUpdates";
        private const int ThreadGroupSize = 64;
        private const int MaxThreadGroupsPerDispatch = 65535;
        private const int MaxUpdatesPerDispatch = ThreadGroupSize * MaxThreadGroupsPerDispatch;

        [NoAutoStaticsCleanup]
        private static readonly int s_UpdatesId = Shader.PropertyToID("_VTPageTableUpdates");
        [NoAutoStaticsCleanup]
        private static readonly int s_DestinationId = Shader.PropertyToID("_VTPageTableDestination");
        [NoAutoStaticsCleanup]
        private static readonly int s_UpdateBaseId = Shader.PropertyToID("_VTPageTableUpdateBase");
        [NoAutoStaticsCleanup]
        private static readonly int s_UpdateCountId = Shader.PropertyToID("_VTPageTableUpdateCount");
        [NoAutoStaticsCleanup]
        private static readonly BaseRenderFunc<ScatterPassData, ComputeGraphContext> s_RenderFunc = Execute;
        [NoAutoStaticsCleanup]
        private static readonly Comparison<VTPageTableSpace> s_SpaceIdComparison = CompareSpaceIds;

        private readonly List<VTPageTableSpace> m_PendingSpaces = new();
        private readonly List<PendingSlice> m_PendingSlices = new();
        private readonly List<UploadChunk> m_UploadChunks = new();
        private readonly List<DispatchSlice> m_DispatchSlices = new();

        private VTPageTableScatterUpdate[] m_Updates = Array.Empty<VTPageTableScatterUpdate>();
        private ComputeShader m_Shader;
        private int m_Kernel = -1;
        private bool m_HasActiveBatch;
        private int m_ActiveUpdateCount;
        private int m_ActiveSpaceCount;
        private int m_ActiveChunkCount;
        private int m_ActiveDispatchCount;
        private int m_ScatterBatchCount;
        private int m_LastScatterEntryCount;
        private int m_LastScatterSpaceCount;
        private int m_LastScatterChunkCount;
        private int m_LastScatterDispatchCount;
        private int m_LastTransientSetDataCallCount;
        private int m_LastLegacySetDataCallCount;

        internal int ScatterBatchCount => m_ScatterBatchCount;

        internal int LastScatterEntryCount => m_LastScatterEntryCount;

        internal int LastScatterSpaceCount => m_LastScatterSpaceCount;

        internal int LastScatterChunkCount => m_LastScatterChunkCount;

        internal int LastScatterDispatchCount => m_LastScatterDispatchCount;

        internal int LastTransientSetDataCallCount => m_LastTransientSetDataCallCount;

        internal int LastLegacySetDataCallCount => m_LastLegacySetDataCallCount;

        internal bool Record(
            RenderGraph renderGraph,
            Dictionary<int, VTPageTableSpace>.ValueCollection addressSpaces)
        {
            if (renderGraph == null)
                throw new ArgumentNullException(nameof(renderGraph));
            if (addressSpaces == null)
                throw new ArgumentNullException(nameof(addressSpaces));

            if (m_HasActiveBatch)
                Abort();

            CollectPendingSpaces(addressSpaces);
            if (m_PendingSpaces.Count == 0)
                return false;

            if (!TryResolveShader())
            {
                UploadImmediateFallback();
                return false;
            }

            long totalUpdateCount = 0;
            for (int spaceIndex = 0; spaceIndex < m_PendingSpaces.Count; spaceIndex++)
                totalUpdateCount += m_PendingSpaces[spaceIndex].PageTablePendingUploadEntryCount;

            int maxChunkEntryCount = ResolveMaxChunkEntryCount();
            if (totalUpdateCount <= 0
                || totalUpdateCount > int.MaxValue
                || maxChunkEntryCount <= 0
                || !TryEnsureUpdateCapacity((int)totalUpdateCount))
            {
                UploadImmediateFallback();
                return false;
            }

            BuildPendingSlices((int)totalUpdateCount);

            using var builder = renderGraph.AddComputePass<ScatterPassData>(
                "VirtualTexture/PageTableScatter",
                out var passData);

            m_UploadChunks.Clear();
            m_DispatchSlices.Clear();
            BuildRenderGraphResources(builder, maxChunkEntryCount);

            passData.Shader = m_Shader;
            passData.Kernel = m_Kernel;
            passData.Updates = m_Updates;
            passData.UploadChunks = m_UploadChunks;
            passData.DispatchSlices = m_DispatchSlices;

            builder.AllowPassCulling(false);
            builder.EnableAsyncCompute(false);
            builder.SetRenderFunc(s_RenderFunc);

            m_HasActiveBatch = true;
            m_ActiveUpdateCount = (int)totalUpdateCount;
            m_ActiveSpaceCount = m_PendingSlices.Count;
            m_ActiveChunkCount = m_UploadChunks.Count;
            m_ActiveDispatchCount = m_DispatchSlices.Count;
            return true;
        }

        internal void Commit()
        {
            if (!m_HasActiveBatch)
                return;

            for (int sliceIndex = 0; sliceIndex < m_PendingSlices.Count; sliceIndex++)
            {
                PendingSlice slice = m_PendingSlices[sliceIndex];
                slice.AddressSpace.CommitPendingPageTableUpload(
                    slice.PendingVersion,
                    slice.FullUpload,
                    slice.UpdateCount);
            }

            m_ScatterBatchCount += 1;
            m_LastScatterEntryCount = m_ActiveUpdateCount;
            m_LastScatterSpaceCount = m_ActiveSpaceCount;
            m_LastScatterChunkCount = m_ActiveChunkCount;
            m_LastScatterDispatchCount = m_ActiveDispatchCount;
            m_LastTransientSetDataCallCount = m_ActiveChunkCount;
            m_LastLegacySetDataCallCount = 0;
            ClearActiveBatch();
        }

        internal void Abort()
        {
            if (!m_HasActiveBatch)
                return;

            ClearActiveBatch();
        }

        internal void Reset()
        {
            Abort();
            m_PendingSpaces.Clear();
            m_PendingSlices.Clear();
            m_UploadChunks.Clear();
            m_DispatchSlices.Clear();
            m_Updates = Array.Empty<VTPageTableScatterUpdate>();
            m_Shader = null;
            m_Kernel = -1;
            m_ScatterBatchCount = 0;
            m_LastScatterEntryCount = 0;
            m_LastScatterSpaceCount = 0;
            m_LastScatterChunkCount = 0;
            m_LastScatterDispatchCount = 0;
            m_LastTransientSetDataCallCount = 0;
            m_LastLegacySetDataCallCount = 0;
        }

        private void CollectPendingSpaces(Dictionary<int, VTPageTableSpace>.ValueCollection addressSpaces)
        {
            m_PendingSpaces.Clear();
            foreach (VTPageTableSpace addressSpace in addressSpaces)
            {
                if (addressSpace?.PageTablePendingUploadEntryCount > 0)
                    m_PendingSpaces.Add(addressSpace);
            }

            m_PendingSpaces.Sort(s_SpaceIdComparison);
        }

        private static int CompareSpaceIds(VTPageTableSpace left, VTPageTableSpace right)
        {
            return left.SpaceId.CompareTo(right.SpaceId);
        }

        private bool TryResolveShader()
        {
            if (!SystemInfo.supportsComputeShaders)
                return false;

            ComputeShader resolvedShader =
                PipelineResourceManager.Get<VividRPCoreResources>()?.VirtualTexturePageTableScatterCompute;
            if (ReferenceEquals(resolvedShader, m_Shader) && m_Kernel >= 0)
                return true;

            m_Shader = resolvedShader;
            m_Kernel = -1;
            if (m_Shader == null)
                return false;

            try
            {
                m_Kernel = m_Shader.FindKernel(KernelName);
            }
            catch (ArgumentException)
            {
                m_Shader = null;
                m_Kernel = -1;
            }

            return m_Shader != null && m_Kernel >= 0;
        }

        private static int ResolveMaxChunkEntryCount()
        {
            long maxGraphicsBufferSize = SystemInfo.maxGraphicsBufferSize;
            if (maxGraphicsBufferSize <= 0)
                return int.MaxValue;

            return (int)Math.Min(
                int.MaxValue,
                maxGraphicsBufferSize / VTPageTableScatterUpdate.Stride);
        }

        private bool TryEnsureUpdateCapacity(int requiredCount)
        {
            if (m_Updates.Length >= requiredCount)
                return true;

            int capacity = NextPowerOfTwoOrExact(requiredCount);
            try
            {
                m_Updates = new VTPageTableScatterUpdate[capacity];
                return true;
            }
            catch (OutOfMemoryException)
            {
                m_Updates = Array.Empty<VTPageTableScatterUpdate>();
                return false;
            }
        }

        private static int NextPowerOfTwoOrExact(int value)
        {
            if (value <= 1)
                return 1;
            if (value >= 1 << 30)
                return value;

            return Mathf.NextPowerOfTwo(value);
        }

        private void BuildPendingSlices(int totalUpdateCount)
        {
            m_PendingSlices.Clear();
            int updateOffset = 0;
            for (int spaceIndex = 0; spaceIndex < m_PendingSpaces.Count; spaceIndex++)
            {
                VTPageTableSpace addressSpace = m_PendingSpaces[spaceIndex];
                int copiedCount = addressSpace.CopyPendingPageTableUpdates(
                    m_Updates,
                    updateOffset,
                    out int pendingVersion,
                    out bool fullUpload);
                if (copiedCount <= 0)
                    continue;

                m_PendingSlices.Add(new PendingSlice(
                    addressSpace,
                    addressSpace.PageTableBuffer,
                    updateOffset,
                    copiedCount,
                    pendingVersion,
                    fullUpload));
                updateOffset += copiedCount;
            }

            if (updateOffset != totalUpdateCount)
            {
                throw new InvalidOperationException(
                    $"Expected to pack {totalUpdateCount} VT page-table updates, but packed {updateOffset}.");
            }
        }

        private void BuildRenderGraphResources(
            IComputeRenderGraphBuilder builder,
            int maxChunkEntryCount)
        {
            var destinationHandles = new BufferHandle[m_PendingSlices.Count];
            for (int sliceIndex = 0; sliceIndex < m_PendingSlices.Count; sliceIndex++)
            {
                PendingSlice slice = m_PendingSlices[sliceIndex];
                BufferHandle destinationHandle = PassRecorder.ImportBufferHandle(slice.DestinationBuffer);
                if (!destinationHandle.IsValid())
                    throw new InvalidOperationException("Could not import a VT page-table buffer into RenderGraph.");

                destinationHandles[sliceIndex] = destinationHandle;
                builder.UseBuffer(destinationHandle, AccessFlags.Write);
            }

            int totalUpdateCount = GetPackedUpdateCount();
            int chunkStart = 0;
            int chunkIndex = 0;
            while (chunkStart < totalUpdateCount)
            {
                int chunkCount = Math.Min(maxChunkEntryCount, totalUpdateCount - chunkStart);
                BufferHandle uploadBuffer = builder.CreateTransientBuffer(new BufferDesc(
                    chunkCount,
                    VTPageTableScatterUpdate.Stride)
                {
                    name = $"VividVT_PageTableScatterUpload_{chunkIndex}",
                    target = GraphicsBuffer.Target.Structured,
                });
                m_UploadChunks.Add(new UploadChunk(uploadBuffer, chunkStart, chunkCount));

                int chunkEnd = chunkStart + chunkCount;
                for (int sliceIndex = 0; sliceIndex < m_PendingSlices.Count; sliceIndex++)
                {
                    PendingSlice slice = m_PendingSlices[sliceIndex];
                    int sliceStart = slice.UpdateStart;
                    int sliceEnd = sliceStart + slice.UpdateCount;
                    int intersectionStart = Math.Max(chunkStart, sliceStart);
                    int intersectionEnd = Math.Min(chunkEnd, sliceEnd);
                    if (intersectionStart >= intersectionEnd)
                        continue;

                    AddDispatchSlices(
                        uploadBuffer,
                        destinationHandles[sliceIndex],
                        intersectionStart - chunkStart,
                        intersectionEnd - intersectionStart);
                }

                chunkStart = chunkEnd;
                chunkIndex += 1;
            }
        }

        private int GetPackedUpdateCount()
        {
            if (m_PendingSlices.Count == 0)
                return 0;

            PendingSlice lastSlice = m_PendingSlices[^1];
            return lastSlice.UpdateStart + lastSlice.UpdateCount;
        }

        private void AddDispatchSlices(
            BufferHandle uploadBuffer,
            BufferHandle destinationBuffer,
            int sourceStart,
            int updateCount)
        {
            int dispatchedCount = 0;
            while (dispatchedCount < updateCount)
            {
                int dispatchCount = Math.Min(
                    MaxUpdatesPerDispatch,
                    updateCount - dispatchedCount);
                m_DispatchSlices.Add(new DispatchSlice(
                    uploadBuffer,
                    destinationBuffer,
                    sourceStart + dispatchedCount,
                    dispatchCount));
                dispatchedCount += dispatchCount;
            }
        }

        private void UploadImmediateFallback()
        {
            int legacyCallCount = 0;
            for (int spaceIndex = 0; spaceIndex < m_PendingSpaces.Count; spaceIndex++)
            {
                VTPageTableSpace addressSpace = m_PendingSpaces[spaceIndex];
                int previousCallCount = addressSpace.PageTableLegacySetDataCallCount;
                addressSpace.RefreshPageTableBufferImmediately();
                legacyCallCount += addressSpace.PageTableLegacySetDataCallCount - previousCallCount;
            }

            m_LastScatterEntryCount = 0;
            m_LastScatterSpaceCount = 0;
            m_LastScatterChunkCount = 0;
            m_LastScatterDispatchCount = 0;
            m_LastTransientSetDataCallCount = 0;
            m_LastLegacySetDataCallCount = legacyCallCount;
            m_PendingSpaces.Clear();
        }

        private void ClearActiveBatch()
        {
            m_HasActiveBatch = false;
            m_ActiveUpdateCount = 0;
            m_ActiveSpaceCount = 0;
            m_ActiveChunkCount = 0;
            m_ActiveDispatchCount = 0;
            m_PendingSpaces.Clear();
            m_PendingSlices.Clear();
            m_UploadChunks.Clear();
            m_DispatchSlices.Clear();
        }

        private static void Execute(ScatterPassData data, ComputeGraphContext context)
        {
            if (data.Shader == null || data.Kernel < 0)
                throw new InvalidOperationException("The VT page-table scatter shader is not available.");

            for (int chunkIndex = 0; chunkIndex < data.UploadChunks.Count; chunkIndex++)
            {
                UploadChunk chunk = data.UploadChunks[chunkIndex];
                GraphicsBuffer uploadBuffer = chunk.Buffer;
                if (uploadBuffer == null)
                    throw new InvalidOperationException("The VT page-table scatter upload buffer is invalid.");

                context.cmd.SetBufferData(
                    uploadBuffer,
                    data.Updates,
                    chunk.SourceStart,
                    0,
                    chunk.UpdateCount);
            }

            for (int dispatchIndex = 0; dispatchIndex < data.DispatchSlices.Count; dispatchIndex++)
            {
                DispatchSlice dispatch = data.DispatchSlices[dispatchIndex];
                context.cmd.SetComputeBufferParam(
                    data.Shader,
                    data.Kernel,
                    s_UpdatesId,
                    dispatch.UploadBuffer);
                context.cmd.SetComputeBufferParam(
                    data.Shader,
                    data.Kernel,
                    s_DestinationId,
                    dispatch.DestinationBuffer);
                context.cmd.SetComputeIntParam(data.Shader, s_UpdateBaseId, dispatch.SourceStart);
                context.cmd.SetComputeIntParam(data.Shader, s_UpdateCountId, dispatch.UpdateCount);
                context.cmd.DispatchCompute(
                    data.Shader,
                    data.Kernel,
                    (dispatch.UpdateCount + ThreadGroupSize - 1) / ThreadGroupSize,
                    1,
                    1);
            }
        }

        private sealed class ScatterPassData
        {
            public ComputeShader Shader;
            public int Kernel;
            public VTPageTableScatterUpdate[] Updates;
            public List<UploadChunk> UploadChunks;
            public List<DispatchSlice> DispatchSlices;
        }

        private readonly struct PendingSlice
        {
            internal PendingSlice(
                VTPageTableSpace addressSpace,
                GraphicsBuffer destinationBuffer,
                int updateStart,
                int updateCount,
                int pendingVersion,
                bool fullUpload)
            {
                AddressSpace = addressSpace;
                DestinationBuffer = destinationBuffer;
                UpdateStart = updateStart;
                UpdateCount = updateCount;
                PendingVersion = pendingVersion;
                FullUpload = fullUpload;
            }

            internal VTPageTableSpace AddressSpace { get; }
            internal GraphicsBuffer DestinationBuffer { get; }
            internal int UpdateStart { get; }
            internal int UpdateCount { get; }
            internal int PendingVersion { get; }
            internal bool FullUpload { get; }
        }

        private readonly struct UploadChunk
        {
            internal UploadChunk(BufferHandle buffer, int sourceStart, int updateCount)
            {
                Buffer = buffer;
                SourceStart = sourceStart;
                UpdateCount = updateCount;
            }

            internal BufferHandle Buffer { get; }
            internal int SourceStart { get; }
            internal int UpdateCount { get; }
        }

        private readonly struct DispatchSlice
        {
            internal DispatchSlice(
                BufferHandle uploadBuffer,
                BufferHandle destinationBuffer,
                int sourceStart,
                int updateCount)
            {
                UploadBuffer = uploadBuffer;
                DestinationBuffer = destinationBuffer;
                SourceStart = sourceStart;
                UpdateCount = updateCount;
            }

            internal BufferHandle UploadBuffer { get; }
            internal BufferHandle DestinationBuffer { get; }
            internal int SourceStart { get; }
            internal int UpdateCount { get; }
        }
    }
}
