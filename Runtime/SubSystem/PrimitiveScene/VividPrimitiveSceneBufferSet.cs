using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.PrimitiveScene
{
    internal sealed class VividPrimitiveSceneBufferSet : IDisposable
    {
        private static readonly ProfilerMarker s_UploadMarker = new("VividRP.PrimitiveScene.Upload");

        private readonly List<VividPrimitiveDirtyRange> m_DirtyRanges = new();

        private GraphicsBuffer m_PrimitiveDataBuffer;
        private GraphicsBuffer m_TransformDataBuffer;
        private GraphicsBuffer m_PreviousTransformDataBuffer;
        private GraphicsBuffer m_DrawSectionDataBuffer;
        private GraphicsBuffer m_GeometryDataBuffer;
        private GraphicsBuffer m_MaterialDataBuffer;
        private GraphicsBuffer m_LegacyInstanceMappingBuffer;
        private bool m_IsDisposed;

        internal GraphicsBuffer PrimitiveDataBuffer => m_PrimitiveDataBuffer;
        internal GraphicsBuffer TransformDataBuffer => m_TransformDataBuffer;
        internal GraphicsBuffer PreviousTransformDataBuffer => m_PreviousTransformDataBuffer;
        internal GraphicsBuffer DrawSectionDataBuffer => m_DrawSectionDataBuffer;
        internal GraphicsBuffer GeometryDataBuffer => m_GeometryDataBuffer;
        internal GraphicsBuffer MaterialDataBuffer => m_MaterialDataBuffer;
        internal GraphicsBuffer LegacyInstanceMappingBuffer => m_LegacyInstanceMappingBuffer;

        internal void Upload(VividPrimitiveScene scene)
        {
            ThrowIfDisposed();
            if (scene == null)
                throw new ArgumentNullException(nameof(scene));

            using (s_UploadMarker.Auto())
            {
                int dirtyPageCount = CountDirtyPages(scene);
                int uploadRangeCount = 0;
                long uploadByteCount = 0L;
                UploadTable(
                    ref m_PrimitiveDataBuffer,
                    scene.PrimitiveTable,
                    "VividPrimitiveScene_Primitives",
                    ref uploadRangeCount,
                    ref uploadByteCount);
                UploadTable(
                    ref m_TransformDataBuffer,
                    scene.TransformTable,
                    "VividPrimitiveScene_Transforms",
                    ref uploadRangeCount,
                    ref uploadByteCount);
                UploadTable(
                    ref m_PreviousTransformDataBuffer,
                    scene.PreviousTransformTable,
                    "VividPrimitiveScene_PreviousTransforms",
                    ref uploadRangeCount,
                    ref uploadByteCount);
                UploadTable(
                    ref m_DrawSectionDataBuffer,
                    scene.DrawSectionTable,
                    "VividPrimitiveScene_DrawSections",
                    ref uploadRangeCount,
                    ref uploadByteCount);
                UploadTable(
                    ref m_GeometryDataBuffer,
                    scene.GeometryTable,
                    "VividPrimitiveScene_Geometries",
                    ref uploadRangeCount,
                    ref uploadByteCount);
                UploadTable(
                    ref m_MaterialDataBuffer,
                    scene.MaterialTable,
                    "VividPrimitiveScene_Materials",
                    ref uploadRangeCount,
                    ref uploadByteCount);
                UploadTable(
                    ref m_LegacyInstanceMappingBuffer,
                    scene.LegacyInstanceMappingTable,
                    "VividPrimitiveScene_LegacyInstanceMappings",
                    ref uploadRangeCount,
                    ref uploadByteCount);
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
            m_PrimitiveDataBuffer = null;
            m_TransformDataBuffer = null;
            m_PreviousTransformDataBuffer = null;
            m_DrawSectionDataBuffer = null;
            m_GeometryDataBuffer = null;
            m_MaterialDataBuffer = null;
            m_LegacyInstanceMappingBuffer = null;
            m_DirtyRanges.Clear();
            m_IsDisposed = true;
        }

        private void UploadTable<T>(
            ref GraphicsBuffer buffer,
            VividPrimitiveGpuTable<T> table,
            string bufferName,
            ref int uploadRangeCount,
            ref long uploadByteCount)
            where T : struct
        {
            int stride = UnsafeUtility.SizeOf<T>();
            int requiredCount = Mathf.Max(1, table.Count);
            bool recreated = buffer == null
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
    }
}
