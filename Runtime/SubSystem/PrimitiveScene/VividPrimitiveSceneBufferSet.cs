using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.PrimitiveScene
{
    internal sealed class VividPrimitiveSceneBufferSet : IDisposable
    {
        private const int TableCount = 7;
        private const int StagingBufferCount = 3;
        private const int RawBufferStride = sizeof(uint);
        private const int ScatterThreadGroupSize = 64;
        private const int MaxThreadGroupsPerDispatch = 65535;
        private const int MaxScatterUpdatesPerDispatch = ScatterThreadGroupSize * MaxThreadGroupsPerDispatch;

        private static readonly ProfilerMarker s_UploadMarker = new("VividRP.PrimitiveScene.Upload");
        private static readonly int s_ScatterSourceId = Shader.PropertyToID("_VividPrimitiveScatterSource");
        private static readonly int s_ScatterDstIndicesId = Shader.PropertyToID("_VividPrimitiveScatterDstIndices");
        private static readonly int s_ScatterSourceByteOffsetId = Shader.PropertyToID("_VividPrimitiveScatterSourceByteOffset");
        private static readonly int s_ScatterIndexBaseId = Shader.PropertyToID("_VividPrimitiveScatterIndexBase");
        private static readonly int s_ScatterCountId = Shader.PropertyToID("_VividPrimitiveScatterCount");
        private static readonly int s_ScatterDst16Id = Shader.PropertyToID("_VividPrimitiveScatterDst16");
        private static readonly int s_ScatterDst64Id = Shader.PropertyToID("_VividPrimitiveScatterDst64");
        private static readonly int s_ScatterDst80Id = Shader.PropertyToID("_VividPrimitiveScatterDst80");
        private static readonly int s_ScatterDst128Id = Shader.PropertyToID("_VividPrimitiveScatterDst128");

        private readonly List<VividPrimitiveDirtyRange> m_DirtyRanges = new();
        private readonly GraphicsBuffer[] m_StagingDataBuffers = new GraphicsBuffer[StagingBufferCount];
        private readonly GraphicsBuffer[] m_StagingIndexBuffers = new GraphicsBuffer[StagingBufferCount];
        private readonly ScatterBatch[] m_ScatterBatches = new ScatterBatch[TableCount];

        private GraphicsBuffer m_PrimitiveDataBuffer;
        private GraphicsBuffer m_TransformDataBuffer;
        private GraphicsBuffer m_PreviousTransformDataBuffer;
        private GraphicsBuffer m_DrawSectionDataBuffer;
        private GraphicsBuffer m_GeometryDataBuffer;
        private GraphicsBuffer m_MaterialDataBuffer;
        private GraphicsBuffer m_LegacyInstanceMappingBuffer;
        private CommandBuffer m_ImmediateCommandBuffer;
        private ComputeShader m_ScatterShader;
        private int m_ScatterKernel16 = -1;
        private int m_ScatterKernel64 = -1;
        private int m_ScatterKernel80 = -1;
        private int m_ScatterKernel128 = -1;
        private int m_StagingBufferIndex = -1;
        private bool m_LastUploadUsedComputeScatter;
        private bool m_IsDisposed;

#if UNITY_INCLUDE_TESTS
        internal bool ForceFallbackUploadForTests { get; set; }
#endif

        internal GraphicsBuffer PrimitiveDataBuffer => m_PrimitiveDataBuffer;
        internal GraphicsBuffer TransformDataBuffer => m_TransformDataBuffer;
        internal GraphicsBuffer PreviousTransformDataBuffer => m_PreviousTransformDataBuffer;
        internal GraphicsBuffer DrawSectionDataBuffer => m_DrawSectionDataBuffer;
        internal GraphicsBuffer GeometryDataBuffer => m_GeometryDataBuffer;
        internal GraphicsBuffer MaterialDataBuffer => m_MaterialDataBuffer;
        internal GraphicsBuffer LegacyInstanceMappingBuffer => m_LegacyInstanceMappingBuffer;
        internal bool LastUploadUsedComputeScatter => m_LastUploadUsedComputeScatter;

        internal bool HasPendingUploads(VividPrimitiveScene scene)
        {
            ThrowIfDisposed();
            if (scene == null)
                throw new ArgumentNullException(nameof(scene));

            return NeedsTableUpload(m_PrimitiveDataBuffer, scene.PrimitiveTable)
                || NeedsTableUpload(m_TransformDataBuffer, scene.TransformTable)
                || NeedsTableUpload(m_PreviousTransformDataBuffer, scene.PreviousTransformTable)
                || NeedsTableUpload(m_DrawSectionDataBuffer, scene.DrawSectionTable)
                || NeedsTableUpload(m_GeometryDataBuffer, scene.GeometryTable)
                || NeedsTableUpload(m_MaterialDataBuffer, scene.MaterialTable)
                || NeedsTableUpload(m_LegacyInstanceMappingBuffer, scene.LegacyInstanceMappingTable);
        }

        internal void UploadIfNeeded(VividPrimitiveScene scene, CommandBuffer cmd)
        {
            if (HasPendingUploads(scene))
                Upload(scene, cmd);
        }

        internal void Upload(VividPrimitiveScene scene)
        {
            Upload(scene, null);
        }

        internal unsafe void Upload(VividPrimitiveScene scene, CommandBuffer cmd)
        {
            ThrowIfDisposed();
            if (scene == null)
                throw new ArgumentNullException(nameof(scene));

            using (s_UploadMarker.Auto())
            {
                int dirtyPageCount = CountDirtyPages(scene);
                int uploadRangeCount = 0;
                long uploadByteCount = 0L;
                int batchCount = 0;
                long totalScatterBytes = 0L;
                long totalScatterIndices = 0L;
                m_LastUploadUsedComputeScatter = false;

                PrepareTableUpload(
                    TableKind.Primitive,
                    ref m_PrimitiveDataBuffer,
                    scene.PrimitiveTable,
                    "VividPrimitiveScene_Primitives",
                    ref uploadRangeCount,
                    ref uploadByteCount,
                    ref batchCount,
                    ref totalScatterBytes,
                    ref totalScatterIndices);
                PrepareTableUpload(
                    TableKind.Transform,
                    ref m_TransformDataBuffer,
                    scene.TransformTable,
                    "VividPrimitiveScene_Transforms",
                    ref uploadRangeCount,
                    ref uploadByteCount,
                    ref batchCount,
                    ref totalScatterBytes,
                    ref totalScatterIndices);
                PrepareTableUpload(
                    TableKind.PreviousTransform,
                    ref m_PreviousTransformDataBuffer,
                    scene.PreviousTransformTable,
                    "VividPrimitiveScene_PreviousTransforms",
                    ref uploadRangeCount,
                    ref uploadByteCount,
                    ref batchCount,
                    ref totalScatterBytes,
                    ref totalScatterIndices);
                PrepareTableUpload(
                    TableKind.DrawSection,
                    ref m_DrawSectionDataBuffer,
                    scene.DrawSectionTable,
                    "VividPrimitiveScene_DrawSections",
                    ref uploadRangeCount,
                    ref uploadByteCount,
                    ref batchCount,
                    ref totalScatterBytes,
                    ref totalScatterIndices);
                PrepareTableUpload(
                    TableKind.Geometry,
                    ref m_GeometryDataBuffer,
                    scene.GeometryTable,
                    "VividPrimitiveScene_Geometries",
                    ref uploadRangeCount,
                    ref uploadByteCount,
                    ref batchCount,
                    ref totalScatterBytes,
                    ref totalScatterIndices);
                PrepareTableUpload(
                    TableKind.Material,
                    ref m_MaterialDataBuffer,
                    scene.MaterialTable,
                    "VividPrimitiveScene_Materials",
                    ref uploadRangeCount,
                    ref uploadByteCount,
                    ref batchCount,
                    ref totalScatterBytes,
                    ref totalScatterIndices);
                PrepareTableUpload(
                    TableKind.LegacyInstanceMapping,
                    ref m_LegacyInstanceMappingBuffer,
                    scene.LegacyInstanceMappingTable,
                    "VividPrimitiveScene_LegacyInstanceMappings",
                    ref uploadRangeCount,
                    ref uploadByteCount,
                    ref batchCount,
                    ref totalScatterBytes,
                    ref totalScatterIndices);

                if (batchCount > 0)
                {
                    if (TryUploadBatchesWithScatterCompute(
                        scene,
                        cmd,
                        batchCount,
                        totalScatterBytes,
                        totalScatterIndices,
                        ref uploadRangeCount,
                        ref uploadByteCount))
                    {
                        m_LastUploadUsedComputeScatter = true;
                    }
                    else
                    {
                        UploadBatchesWithDirtyRangesFallback(
                            scene,
                            batchCount,
                            ref uploadRangeCount,
                            ref uploadByteCount);
                    }
                }

                scene.SetLastUploadStats(dirtyPageCount, uploadRangeCount, uploadByteCount);
            }
        }

        internal void BindGlobals(CommandBuffer cmd, VividPrimitiveScene scene)
        {
            ThrowIfDisposed();
            if (cmd == null)
                throw new ArgumentNullException(nameof(cmd));
            if (scene == null)
                throw new ArgumentNullException(nameof(scene));

            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._VividPrimitiveData, m_PrimitiveDataBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._VividPrimitiveTransformData, m_TransformDataBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._VividPrimitivePreviousTransformData, m_PreviousTransformDataBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._VividPrimitiveDrawSectionData, m_DrawSectionDataBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._VividPrimitiveGeometryData, m_GeometryDataBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._VividPrimitiveMaterialData, m_MaterialDataBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._VividLegacyInstanceMappingData, m_LegacyInstanceMappingBuffer);
            cmd.SetGlobalInt(VividGPUDrivenShaderIDs._VividPrimitiveCount, scene.PrimitiveTable.Count);
            cmd.SetGlobalInt(VividGPUDrivenShaderIDs._VividPrimitiveDrawSectionCount, scene.DrawSectionTable.Count);
            cmd.SetGlobalInt(VividGPUDrivenShaderIDs._VividPrimitiveGeometryCount, scene.GeometryTable.Count);
            cmd.SetGlobalInt(VividGPUDrivenShaderIDs._VividPrimitiveMaterialCount, scene.MaterialTable.Count);
            cmd.SetGlobalInt(VividGPUDrivenShaderIDs._VividLegacyInstanceMappingCount, scene.LegacyInstanceMappingTable.Count);
            cmd.SetGlobalInt(VividGPUDrivenShaderIDs._VividPrimitiveSceneRevision, unchecked((int) scene.GetStats().SceneRevision));
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            m_PrimitiveDataBuffer?.Dispose();
            m_TransformDataBuffer?.Dispose();
            m_PreviousTransformDataBuffer?.Dispose();
            m_DrawSectionDataBuffer?.Dispose();
            m_GeometryDataBuffer?.Dispose();
            m_MaterialDataBuffer?.Dispose();
            m_LegacyInstanceMappingBuffer?.Dispose();
            for (int index = 0; index < StagingBufferCount; index++)
            {
                m_StagingDataBuffers[index]?.Dispose();
                m_StagingDataBuffers[index] = null;
                m_StagingIndexBuffers[index]?.Dispose();
                m_StagingIndexBuffers[index] = null;
            }
            m_ImmediateCommandBuffer?.Release();
            m_ImmediateCommandBuffer = null;

            m_PrimitiveDataBuffer = null;
            m_TransformDataBuffer = null;
            m_PreviousTransformDataBuffer = null;
            m_DrawSectionDataBuffer = null;
            m_GeometryDataBuffer = null;
            m_MaterialDataBuffer = null;
            m_LegacyInstanceMappingBuffer = null;
            m_ScatterShader = null;
            m_ScatterKernel16 = -1;
            m_ScatterKernel64 = -1;
            m_ScatterKernel80 = -1;
            m_ScatterKernel128 = -1;
            m_StagingBufferIndex = -1;
            m_DirtyRanges.Clear();
            m_IsDisposed = true;
        }

        private void PrepareTableUpload<T>(
            TableKind kind,
            ref GraphicsBuffer buffer,
            VividPrimitiveGpuTable<T> table,
            string bufferName,
            ref int uploadRangeCount,
            ref long uploadByteCount,
            ref int batchCount,
            ref long totalScatterBytes,
            ref long totalScatterIndices)
            where T : struct
        {
            int stride = UnsafeUtility.SizeOf<T>();
            int requiredCount = Mathf.Max(1, table.Count);
            bool recreated = buffer == null
                || !buffer.IsValid()
                || buffer.target != GraphicsBuffer.Target.Structured
                || buffer.stride != stride
                || buffer.count < requiredCount;
            if (recreated)
            {
                int capacity = Mathf.NextPowerOfTwo(requiredCount);
                buffer?.Dispose();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, stride)
                {
                    name = bufferName,
                };

                if (table.Count > 0)
                {
                    buffer.SetData(table.Data, 0, 0, table.Count);
                    uploadRangeCount++;
                    uploadByteCount += (long) table.Count * stride;
                }
                table.ClearDirtyPages();
                return;
            }

            if (table.IsAllDirty)
            {
                if (table.Count > 0)
                {
                    buffer.SetData(table.Data, 0, 0, table.Count);
                    uploadRangeCount++;
                    uploadByteCount += (long) table.Count * stride;
                }
                table.ClearDirtyPages();
                return;
            }

            int dirtyRecordCount = table.DirtyRecordCount;
            if (dirtyRecordCount <= 0)
                return;

            m_ScatterBatches[batchCount++] = new ScatterBatch(
                kind,
                stride,
                dirtyRecordCount,
                totalScatterBytes,
                totalScatterIndices);
            totalScatterBytes += (long) dirtyRecordCount * stride;
            totalScatterIndices += dirtyRecordCount;
        }

        private unsafe bool TryUploadBatchesWithScatterCompute(
            VividPrimitiveScene scene,
            CommandBuffer cmd,
            int batchCount,
            long totalScatterBytes,
            long totalScatterIndices,
            ref int uploadRangeCount,
            ref long uploadByteCount)
        {
#if UNITY_INCLUDE_TESTS
            if (ForceFallbackUploadForTests)
                return false;
#endif
            if (!SystemInfo.supportsComputeShaders || !TryResolveScatterShader())
                return false;
            if (totalScatterBytes <= 0L
                || totalScatterIndices <= 0L
                || (totalScatterBytes & 15L) != 0L
                || totalScatterBytes > int.MaxValue
                || totalScatterIndices > int.MaxValue)
            {
                return false;
            }

            long maxBufferBytes = SystemInfo.maxGraphicsBufferSize;
            if (maxBufferBytes > 0L
                && (totalScatterBytes > maxBufferBytes
                    || totalScatterIndices * sizeof(uint) > maxBufferBytes))
            {
                return false;
            }

            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                ScatterBatch batch = m_ScatterBatches[batchIndex];
                if (!TryGetScatterTarget(batch.Kind, batch.Stride, out _, out _, out _))
                    return false;
            }

            int totalDwords = (int) (totalScatterBytes / RawBufferStride);
            int totalIndices = (int) totalScatterIndices;

            m_StagingBufferIndex = (m_StagingBufferIndex + 1) % StagingBufferCount;
            GraphicsBuffer stagingDataBuffer = EnsureStagingDataBuffer(m_StagingBufferIndex, totalDwords);
            GraphicsBuffer stagingIndexBuffer = EnsureStagingIndexBuffer(m_StagingBufferIndex, totalIndices);

            NativeArray<int> mappedData = stagingDataBuffer.LockBufferForWrite<int>(0, totalDwords);
            bool indexLocked = false;
            NativeArray<uint> mappedIndices = default;
            try
            {
                mappedIndices = stagingIndexBuffer.LockBufferForWrite<uint>(0, totalIndices);
                indexLocked = true;

                byte* dataBase = (byte*) mappedData.GetUnsafePtr();
                uint* indexBase = (uint*) mappedIndices.GetUnsafePtr();
                for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
                {
                    ScatterBatch batch = m_ScatterBatches[batchIndex];
                    CopyBatchDirtyRecords(
                        scene,
                        batch.Kind,
                        dataBase + batch.SourceByteOffset,
                        indexBase + batch.IndexOffset,
                        batch.Stride);
                }
            }
            finally
            {
                if (indexLocked)
                    stagingIndexBuffer.UnlockBufferAfterWrite<uint>(totalIndices);
                stagingDataBuffer.UnlockBufferAfterWrite<int>(totalDwords);
            }

            bool executeImmediately = cmd == null;
            CommandBuffer targetCmd = cmd ?? GetOrCreateImmediateCommandBuffer();
            if (executeImmediately)
                targetCmd.Clear();

            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                ScatterBatch batch = m_ScatterBatches[batchIndex];
                if (!TryGetScatterTarget(batch.Kind, batch.Stride, out GraphicsBuffer destination, out int kernel, out int destinationId))
                    return false;

                targetCmd.SetComputeBufferParam(m_ScatterShader, kernel, s_ScatterSourceId, stagingDataBuffer);
                targetCmd.SetComputeBufferParam(m_ScatterShader, kernel, s_ScatterDstIndicesId, stagingIndexBuffer);
                targetCmd.SetComputeBufferParam(m_ScatterShader, kernel, destinationId, destination);

                int dispatched = 0;
                while (dispatched < batch.RecordCount)
                {
                    int chunkCount = Math.Min(MaxScatterUpdatesPerDispatch, batch.RecordCount - dispatched);
                    int byteOffset = (int) batch.SourceByteOffset + dispatched * batch.Stride;
                    int indexBase = (int) batch.IndexOffset + dispatched;
                    targetCmd.SetComputeIntParam(m_ScatterShader, s_ScatterSourceByteOffsetId, byteOffset);
                    targetCmd.SetComputeIntParam(m_ScatterShader, s_ScatterIndexBaseId, indexBase);
                    targetCmd.SetComputeIntParam(m_ScatterShader, s_ScatterCountId, chunkCount);
                    int threadGroups = (chunkCount + ScatterThreadGroupSize - 1) / ScatterThreadGroupSize;
                    targetCmd.DispatchCompute(m_ScatterShader, kernel, threadGroups, 1, 1);
                    dispatched += chunkCount;
                }

                uploadRangeCount++;
                uploadByteCount += (long) batch.RecordCount * batch.Stride;
            }

            if (executeImmediately)
            {
                Graphics.ExecuteCommandBuffer(targetCmd);
                targetCmd.Clear();
            }

            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
                ClearBatchDirty(scene, m_ScatterBatches[batchIndex].Kind);

            return true;
        }

        private void UploadBatchesWithDirtyRangesFallback(
            VividPrimitiveScene scene,
            int batchCount,
            ref int uploadRangeCount,
            ref long uploadByteCount)
        {
            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                switch (m_ScatterBatches[batchIndex].Kind)
                {
                    case TableKind.Primitive:
                        UploadDirtyRanges(m_PrimitiveDataBuffer, scene.PrimitiveTable, ref uploadRangeCount, ref uploadByteCount);
                        break;
                    case TableKind.Transform:
                        UploadDirtyRanges(m_TransformDataBuffer, scene.TransformTable, ref uploadRangeCount, ref uploadByteCount);
                        break;
                    case TableKind.PreviousTransform:
                        UploadDirtyRanges(m_PreviousTransformDataBuffer, scene.PreviousTransformTable, ref uploadRangeCount, ref uploadByteCount);
                        break;
                    case TableKind.DrawSection:
                        UploadDirtyRanges(m_DrawSectionDataBuffer, scene.DrawSectionTable, ref uploadRangeCount, ref uploadByteCount);
                        break;
                    case TableKind.Geometry:
                        UploadDirtyRanges(m_GeometryDataBuffer, scene.GeometryTable, ref uploadRangeCount, ref uploadByteCount);
                        break;
                    case TableKind.Material:
                        UploadDirtyRanges(m_MaterialDataBuffer, scene.MaterialTable, ref uploadRangeCount, ref uploadByteCount);
                        break;
                    case TableKind.LegacyInstanceMapping:
                        UploadDirtyRanges(m_LegacyInstanceMappingBuffer, scene.LegacyInstanceMappingTable, ref uploadRangeCount, ref uploadByteCount);
                        break;
                }
            }
        }

        private void UploadDirtyRanges<T>(
            GraphicsBuffer buffer,
            VividPrimitiveGpuTable<T> table,
            ref int uploadRangeCount,
            ref long uploadByteCount)
            where T : struct
        {
            int stride = UnsafeUtility.SizeOf<T>();
            table.CollectDirtyRanges(m_DirtyRanges);
            for (int index = 0; index < m_DirtyRanges.Count; index++)
            {
                VividPrimitiveDirtyRange range = m_DirtyRanges[index];
                buffer.SetData(table.Data, range.Start, range.Start, range.Count);
                uploadRangeCount++;
                uploadByteCount += (long) range.Count * stride;
            }
            table.ClearDirtyPages();
        }

        private unsafe void CopyBatchDirtyRecords(
            VividPrimitiveScene scene,
            TableKind kind,
            byte* destinationBytes,
            uint* destinationIndices,
            int stride)
        {
            switch (kind)
            {
                case TableKind.Primitive:
                    scene.PrimitiveTable.CopyDirtyRecords(destinationBytes, destinationIndices, stride);
                    break;
                case TableKind.Transform:
                    scene.TransformTable.CopyDirtyRecords(destinationBytes, destinationIndices, stride);
                    break;
                case TableKind.PreviousTransform:
                    scene.PreviousTransformTable.CopyDirtyRecords(destinationBytes, destinationIndices, stride);
                    break;
                case TableKind.DrawSection:
                    scene.DrawSectionTable.CopyDirtyRecords(destinationBytes, destinationIndices, stride);
                    break;
                case TableKind.Geometry:
                    scene.GeometryTable.CopyDirtyRecords(destinationBytes, destinationIndices, stride);
                    break;
                case TableKind.Material:
                    scene.MaterialTable.CopyDirtyRecords(destinationBytes, destinationIndices, stride);
                    break;
                case TableKind.LegacyInstanceMapping:
                    scene.LegacyInstanceMappingTable.CopyDirtyRecords(destinationBytes, destinationIndices, stride);
                    break;
            }
        }

        private static void ClearBatchDirty(VividPrimitiveScene scene, TableKind kind)
        {
            switch (kind)
            {
                case TableKind.Primitive:
                    scene.PrimitiveTable.ClearDirtyPages();
                    break;
                case TableKind.Transform:
                    scene.TransformTable.ClearDirtyPages();
                    break;
                case TableKind.PreviousTransform:
                    scene.PreviousTransformTable.ClearDirtyPages();
                    break;
                case TableKind.DrawSection:
                    scene.DrawSectionTable.ClearDirtyPages();
                    break;
                case TableKind.Geometry:
                    scene.GeometryTable.ClearDirtyPages();
                    break;
                case TableKind.Material:
                    scene.MaterialTable.ClearDirtyPages();
                    break;
                case TableKind.LegacyInstanceMapping:
                    scene.LegacyInstanceMappingTable.ClearDirtyPages();
                    break;
            }
        }

        private bool TryGetScatterTarget(
            TableKind kind,
            int stride,
            out GraphicsBuffer destination,
            out int kernel,
            out int destinationId)
        {
            destination = kind switch
            {
                TableKind.Primitive => m_PrimitiveDataBuffer,
                TableKind.Transform => m_TransformDataBuffer,
                TableKind.PreviousTransform => m_PreviousTransformDataBuffer,
                TableKind.DrawSection => m_DrawSectionDataBuffer,
                TableKind.Geometry => m_GeometryDataBuffer,
                TableKind.Material => m_MaterialDataBuffer,
                TableKind.LegacyInstanceMapping => m_LegacyInstanceMappingBuffer,
                _ => null,
            };

            switch (stride)
            {
                case 16:
                    kernel = m_ScatterKernel16;
                    destinationId = s_ScatterDst16Id;
                    break;
                case 64:
                    kernel = m_ScatterKernel64;
                    destinationId = s_ScatterDst64Id;
                    break;
                case 80:
                    kernel = m_ScatterKernel80;
                    destinationId = s_ScatterDst80Id;
                    break;
                case 128:
                    kernel = m_ScatterKernel128;
                    destinationId = s_ScatterDst128Id;
                    break;
                default:
                    kernel = -1;
                    destinationId = 0;
                    return false;
            }

            return destination != null && kernel >= 0;
        }

        private bool TryResolveScatterShader()
        {
            ComputeShader resolved = PipelineResourceManager.Get<VividRPCoreResources>()?.PrimitiveSceneScatterUploadCompute;
            if (ReferenceEquals(resolved, m_ScatterShader)
                && m_ScatterKernel16 >= 0
                && m_ScatterKernel64 >= 0
                && m_ScatterKernel80 >= 0
                && m_ScatterKernel128 >= 0)
            {
                return true;
            }

            m_ScatterShader = resolved;
            m_ScatterKernel16 = -1;
            m_ScatterKernel64 = -1;
            m_ScatterKernel80 = -1;
            m_ScatterKernel128 = -1;
            if (m_ScatterShader == null)
                return false;

            try
            {
                m_ScatterKernel16 = m_ScatterShader.FindKernel("Scatter16");
                m_ScatterKernel64 = m_ScatterShader.FindKernel("Scatter64");
                m_ScatterKernel80 = m_ScatterShader.FindKernel("Scatter80");
                m_ScatterKernel128 = m_ScatterShader.FindKernel("Scatter128");
            }
            catch (ArgumentException)
            {
                m_ScatterShader = null;
                m_ScatterKernel16 = -1;
                m_ScatterKernel64 = -1;
                m_ScatterKernel80 = -1;
                m_ScatterKernel128 = -1;
                return false;
            }

            return m_ScatterKernel16 >= 0
                && m_ScatterKernel64 >= 0
                && m_ScatterKernel80 >= 0
                && m_ScatterKernel128 >= 0;
        }

        private GraphicsBuffer EnsureStagingDataBuffer(int ringIndex, int requiredDwords)
        {
            int capacity = NextPowerOfTwoOrExact(Math.Max(1, requiredDwords));
            GraphicsBuffer buffer = m_StagingDataBuffers[ringIndex];
            if (buffer != null && buffer.IsValid() && buffer.count >= capacity)
                return buffer;

            buffer?.Dispose();
            buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Raw,
                GraphicsBuffer.UsageFlags.LockBufferForWrite,
                capacity,
                RawBufferStride)
            {
                name = $"VividPrimitiveScene_ScatterData_{ringIndex}",
            };
            m_StagingDataBuffers[ringIndex] = buffer;
            return buffer;
        }

        private GraphicsBuffer EnsureStagingIndexBuffer(int ringIndex, int requiredIndices)
        {
            int capacity = NextPowerOfTwoOrExact(Math.Max(1, requiredIndices));
            GraphicsBuffer buffer = m_StagingIndexBuffers[ringIndex];
            if (buffer != null && buffer.IsValid() && buffer.count >= capacity)
                return buffer;

            buffer?.Dispose();
            buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                GraphicsBuffer.UsageFlags.LockBufferForWrite,
                capacity,
                sizeof(uint))
            {
                name = $"VividPrimitiveScene_ScatterIndices_{ringIndex}",
            };
            m_StagingIndexBuffers[ringIndex] = buffer;
            return buffer;
        }

        private CommandBuffer GetOrCreateImmediateCommandBuffer()
        {
            m_ImmediateCommandBuffer ??= new CommandBuffer
            {
                name = "VividRP.PrimitiveScene.ScatterUpload",
            };
            return m_ImmediateCommandBuffer;
        }

        private static bool NeedsTableUpload<T>(GraphicsBuffer buffer, VividPrimitiveGpuTable<T> table)
            where T : struct
        {
            int stride = UnsafeUtility.SizeOf<T>();
            int requiredCount = Mathf.Max(1, table.Count);
            return buffer == null
                || !buffer.IsValid()
                || buffer.target != GraphicsBuffer.Target.Structured
                || buffer.stride != stride
                || buffer.count < requiredCount
                || table.HasDirtyRecords;
        }

        private static int NextPowerOfTwoOrExact(int value)
        {
            if (value <= 1)
                return 1;
            if (value >= 1 << 30)
                return value;
            return Mathf.NextPowerOfTwo(value);
        }

        private static int CountDirtyPages(VividPrimitiveScene scene)
        {
            return scene.PrimitiveTable.DirtyPageCount
                + scene.TransformTable.DirtyPageCount
                + scene.PreviousTransformTable.DirtyPageCount
                + scene.DrawSectionTable.DirtyPageCount
                + scene.GeometryTable.DirtyPageCount
                + scene.MaterialTable.DirtyPageCount
                + scene.LegacyInstanceMappingTable.DirtyPageCount;
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
                throw new ObjectDisposedException(nameof(VividPrimitiveSceneBufferSet));
        }

        private enum TableKind : byte
        {
            Primitive = 0,
            Transform = 1,
            PreviousTransform = 2,
            DrawSection = 3,
            Geometry = 4,
            Material = 5,
            LegacyInstanceMapping = 6,
        }

        private readonly struct ScatterBatch
        {
            internal ScatterBatch(
                TableKind kind,
                int stride,
                int recordCount,
                long sourceByteOffset,
                long indexOffset)
            {
                Kind = kind;
                Stride = stride;
                RecordCount = recordCount;
                SourceByteOffset = sourceByteOffset;
                IndexOffset = indexOffset;
            }

            internal TableKind Kind { get; }
            internal int Stride { get; }
            internal int RecordCount { get; }
            internal long SourceByteOffset { get; }
            internal long IndexOffset { get; }
        }
    }
}
