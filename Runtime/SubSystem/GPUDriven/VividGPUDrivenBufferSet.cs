using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class VividGPUDrivenBufferSet : IDisposable
    {
        private const int RawBufferStride = sizeof(uint);

        private GraphicsBuffer m_InstanceDataBuffer;
        private GraphicsBuffer m_MaterialDataBuffer;
        private GraphicsBuffer m_MeshLODNodesBuffer;
        private GraphicsBuffer m_MeshletsBuffer;
        private GraphicsBuffer m_SharedVertexBuffer;
        private GraphicsBuffer m_SharedIndexBuffer;
        private byte[] m_PaddedIndexUploadData = Array.Empty<byte>();
        private bool m_IsDisposed;

        public GraphicsBuffer InstanceDataBuffer => m_InstanceDataBuffer;

        public GraphicsBuffer MaterialDataBuffer => m_MaterialDataBuffer;

        public GraphicsBuffer MeshLODNodesBuffer => m_MeshLODNodesBuffer;

        public GraphicsBuffer MeshletsBuffer => m_MeshletsBuffer;

        public GraphicsBuffer SharedVertexBuffer => m_SharedVertexBuffer;

        public GraphicsBuffer SharedIndexBuffer => m_SharedIndexBuffer;

        public int InstanceCount { get; private set; }

        public int MaterialCount { get; private set; }

        public int MeshLODNodeCount { get; private set; }

        public int MeshletCount { get; private set; }

        public int SharedVertexCount { get; private set; }

        public int SharedIndexCount { get; private set; }

        public void Upload(
            VividGPUDrivenSceneData sceneData,
            bool uploadInstanceData = true,
            bool uploadMaterialData = true,
            bool uploadStaticData = true
        )
        {
            ThrowIfDisposed();

            if (sceneData == null)
            {
                throw new ArgumentNullException(nameof(sceneData));
            }

            InstanceCount = sceneData.InstanceCount;
            MaterialCount = sceneData.MaterialCount;
            MeshLODNodeCount = sceneData.MeshLODNodeCount;
            MeshletCount = sceneData.MeshletCount;
            SharedVertexCount = sceneData.VertexCount;
            SharedIndexCount = sceneData.IndexCount;

            bool shouldUploadInstanceData = uploadInstanceData || RequiresInstanceBufferUpload(sceneData);
            bool shouldUploadMaterialData = uploadMaterialData || RequiresMaterialBufferUpload(sceneData);
            bool shouldUploadStaticData = uploadStaticData || RequiresStaticBufferUpload(sceneData);

            if (shouldUploadInstanceData)
            {
                UploadStructuredBuffer(
                    ref m_InstanceDataBuffer,
                    sceneData.MutableInstances,
                    UnsafeUtility.SizeOf<VividInstanceData>(),
                    "VividGPUDriven_InstanceData"
                );
            }
            if (shouldUploadMaterialData)
            {
                UploadStructuredBuffer(
                    ref m_MaterialDataBuffer,
                    sceneData.MutableMaterials,
                    UnsafeUtility.SizeOf<VividMaterialData>(),
                    "VividGPUDriven_MaterialData"
                );
            }

            if (shouldUploadStaticData)
            {
                UploadStructuredBuffer(
                    ref m_MeshLODNodesBuffer,
                    sceneData.MutableMeshLODNodes,
                    UnsafeUtility.SizeOf<VividMeshLODNode>(),
                    "VividGPUDriven_MeshLODNodes"
                );
                UploadStructuredBuffer(
                    ref m_MeshletsBuffer,
                    sceneData.MutableMeshlets,
                    UnsafeUtility.SizeOf<VividMeshlet>(),
                    "VividGPUDriven_Meshlets"
                );
                UploadStructuredBuffer(
                    ref m_SharedVertexBuffer,
                    sceneData.MutableVertices,
                    UnsafeUtility.SizeOf<VividMeshletVertex>(),
                    "VividGPUDriven_SharedVertices"
                );
                UploadRawIndexBuffer(sceneData.MutableIndices);
            }
        }

        public void BindGlobals(CommandBuffer cmd)
        {
            ThrowIfDisposed();

            if (cmd == null)
            {
                throw new ArgumentNullException(nameof(cmd));
            }

            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._InstanceData, InstanceDataBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._MaterialData, MaterialDataBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._MeshLODNodes, MeshLODNodesBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._Meshlets, MeshletsBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._SharedVertexBuffer, SharedVertexBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._SharedIndexBuffer, SharedIndexBuffer);

            cmd.SetGlobalInteger(VividGPUDrivenShaderIDs._InstanceDataCount, InstanceCount);
            cmd.SetGlobalInteger(VividGPUDrivenShaderIDs._MaterialDataCount, MaterialCount);
            cmd.SetGlobalInteger(VividGPUDrivenShaderIDs._MeshLODNodeCount, MeshLODNodeCount);
            cmd.SetGlobalInteger(VividGPUDrivenShaderIDs._MeshletCount, MeshletCount);
            cmd.SetGlobalInteger(VividGPUDrivenShaderIDs._SharedVertexCount, SharedVertexCount);
            cmd.SetGlobalInteger(VividGPUDrivenShaderIDs._SharedIndexCount, SharedIndexCount);
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            m_InstanceDataBuffer?.Dispose();
            m_MaterialDataBuffer?.Dispose();
            m_MeshLODNodesBuffer?.Dispose();
            m_MeshletsBuffer?.Dispose();
            m_SharedVertexBuffer?.Dispose();
            m_SharedIndexBuffer?.Dispose();

            m_InstanceDataBuffer = null;
            m_MaterialDataBuffer = null;
            m_MeshLODNodesBuffer = null;
            m_MeshletsBuffer = null;
            m_SharedVertexBuffer = null;
            m_SharedIndexBuffer = null;
            m_PaddedIndexUploadData = Array.Empty<byte>();
            m_IsDisposed = true;
        }

        private void UploadRawIndexBuffer(List<byte> indices)
        {
            int alignedByteCount = AlignUp(Mathf.Max(1, indices.Count), RawBufferStride);
            int rawBufferCount = alignedByteCount / RawBufferStride;
            EnsureRawBuffer(ref m_SharedIndexBuffer, rawBufferCount, "VividGPUDriven_SharedIndices");

            if (indices.Count == 0)
            {
                return;
            }

            if (m_PaddedIndexUploadData.Length < alignedByteCount)
            {
                m_PaddedIndexUploadData = new byte[alignedByteCount];
            }
            else
            {
                Array.Clear(m_PaddedIndexUploadData, 0, alignedByteCount);
            }

            indices.CopyTo(m_PaddedIndexUploadData, 0);
            SharedIndexBuffer.SetData(m_PaddedIndexUploadData, 0, 0, alignedByteCount);
        }

        private static void UploadStructuredBuffer<T>(
            ref GraphicsBuffer buffer,
            List<T> data,
            int stride,
            string bufferName
        )
            where T : struct
        {
            int count = Mathf.Max(1, data.Count);
            EnsureStructuredBuffer(ref buffer, count, stride, bufferName);

            if (data.Count > 0)
            {
                buffer.SetData(data);
            }
        }

        private static void EnsureStructuredBuffer(
            ref GraphicsBuffer buffer,
            int count,
            int stride,
            string bufferName
        )
        {
            if (buffer != null && buffer.count == count && buffer.stride == stride)
            {
                return;
            }

            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride)
            {
                name = bufferName,
            };
        }

        private static void EnsureRawBuffer(
            ref GraphicsBuffer buffer,
            int count,
            string bufferName
        )
        {
            if (buffer != null && buffer.count == count && buffer.stride == RawBufferStride)
            {
                return;
            }

            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, count, RawBufferStride)
            {
                name = bufferName,
            };
        }

        private static int AlignUp(int value, int alignment)
        {
            return ((value + alignment - 1) / alignment) * alignment;
        }

        private bool RequiresStaticBufferUpload(VividGPUDrivenSceneData sceneData)
        {
            return !IsStructuredBufferCompatible(m_MeshLODNodesBuffer, sceneData.MeshLODNodeCount, UnsafeUtility.SizeOf<VividMeshLODNode>()) ||
                   !IsStructuredBufferCompatible(m_MeshletsBuffer, sceneData.MeshletCount, UnsafeUtility.SizeOf<VividMeshlet>()) ||
                   !IsStructuredBufferCompatible(m_SharedVertexBuffer, sceneData.VertexCount, UnsafeUtility.SizeOf<VividMeshletVertex>()) ||
                   !IsRawBufferCompatible(m_SharedIndexBuffer, sceneData.IndexCount);
        }

        private bool RequiresInstanceBufferUpload(VividGPUDrivenSceneData sceneData)
        {
            return !IsStructuredBufferCompatible(m_InstanceDataBuffer, sceneData.InstanceCount, UnsafeUtility.SizeOf<VividInstanceData>());
        }

        private bool RequiresMaterialBufferUpload(VividGPUDrivenSceneData sceneData)
        {
            return !IsStructuredBufferCompatible(m_MaterialDataBuffer, sceneData.MaterialCount, UnsafeUtility.SizeOf<VividMaterialData>());
        }

        private static bool IsStructuredBufferCompatible(GraphicsBuffer buffer, int count, int stride)
        {
            return buffer != null &&
                   buffer.target == GraphicsBuffer.Target.Structured &&
                   buffer.count == Mathf.Max(1, count) &&
                   buffer.stride == stride;
        }

        private static bool IsRawBufferCompatible(GraphicsBuffer buffer, int byteCount)
        {
            int alignedByteCount = AlignUp(Mathf.Max(1, byteCount), RawBufferStride);
            int rawBufferCount = alignedByteCount / RawBufferStride;
            return buffer != null &&
                   buffer.target == GraphicsBuffer.Target.Raw &&
                   buffer.count == rawBufferCount &&
                   buffer.stride == RawBufferStride;
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(nameof(VividGPUDrivenBufferSet));
            }
        }
    }
}
