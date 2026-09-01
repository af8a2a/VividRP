using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class VividGPUDrivenBufferSet : IDisposable
    {
        private const int RawBufferStride = sizeof(uint);

        private GraphicsBuffer m_InstanceDataBuffer;
        private GraphicsBuffer m_MaterialDataBuffer;
        private GraphicsBuffer m_DualSlabMaterialDataBuffer;
        private GraphicsBuffer m_MaterialParameterDataBuffer;
        private GraphicsBuffer m_MaterialResourceDataBuffer;
        private GraphicsBuffer m_MaterialRuntimeHeaderBuffer;
        private GraphicsBuffer m_MaterialProgramBuffer;
        private GraphicsBuffer m_SurfaceBindingDataBuffer;
        private GraphicsBuffer m_TerrainMaterialDataBuffer;
        private GraphicsBuffer m_TerrainLayerDataBuffer;
        private GraphicsBuffer m_MeshLODNodesBuffer;
        private GraphicsBuffer m_MeshletsBuffer;
        private GraphicsBuffer m_SharedVertexBuffer;
        private GraphicsBuffer m_SharedIndexBuffer;
        private VividInstanceData[] m_InstanceUploadData = Array.Empty<VividInstanceData>();
        private VividMaterialData[] m_MaterialUploadData = Array.Empty<VividMaterialData>();
        private VividDualSlabMaterialData[] m_DualSlabMaterialUploadData =
            Array.Empty<VividDualSlabMaterialData>();
        private uint4[] m_MaterialParameterUploadData = Array.Empty<uint4>();
        private VividMaterialResourceData[] m_MaterialResourceUploadData =
            Array.Empty<VividMaterialResourceData>();
        private VividMaterialRuntimeHeader[] m_MaterialRuntimeHeaderUploadData =
            Array.Empty<VividMaterialRuntimeHeader>();
        private VividMaterialProgramData[] m_MaterialProgramUploadData =
            Array.Empty<VividMaterialProgramData>();
        private VividSurfaceBindingData[] m_SurfaceBindingUploadData = Array.Empty<VividSurfaceBindingData>();
        private VividTerrainMaterialData[] m_TerrainMaterialUploadData = Array.Empty<VividTerrainMaterialData>();
        private VividTerrainLayerGPUData[] m_TerrainLayerUploadData = Array.Empty<VividTerrainLayerGPUData>();
        private VividMeshLODNode[] m_MeshLODNodeUploadData = Array.Empty<VividMeshLODNode>();
        private VividMeshlet[] m_MeshletUploadData = Array.Empty<VividMeshlet>();
        private VividMeshletVertex[] m_VertexUploadData = Array.Empty<VividMeshletVertex>();
        private byte[] m_PaddedIndexUploadData = Array.Empty<byte>();
        private bool m_IsDisposed;

        public GraphicsBuffer InstanceDataBuffer => m_InstanceDataBuffer;

        public GraphicsBuffer MaterialDataBuffer => m_MaterialDataBuffer;

        public GraphicsBuffer DualSlabMaterialDataBuffer => m_DualSlabMaterialDataBuffer;

        public GraphicsBuffer MaterialParameterDataBuffer =>
            m_MaterialParameterDataBuffer;

        public GraphicsBuffer MaterialResourceDataBuffer =>
            m_MaterialResourceDataBuffer;

        public GraphicsBuffer MaterialRuntimeHeaderBuffer => m_MaterialRuntimeHeaderBuffer;

        public GraphicsBuffer MaterialProgramBuffer => m_MaterialProgramBuffer;

        public GraphicsBuffer SurfaceBindingDataBuffer => m_SurfaceBindingDataBuffer;

        public GraphicsBuffer TerrainMaterialDataBuffer => m_TerrainMaterialDataBuffer;

        public GraphicsBuffer TerrainLayerDataBuffer => m_TerrainLayerDataBuffer;

        public GraphicsBuffer MeshLODNodesBuffer => m_MeshLODNodesBuffer;

        public GraphicsBuffer MeshletsBuffer => m_MeshletsBuffer;

        public GraphicsBuffer SharedVertexBuffer => m_SharedVertexBuffer;

        public GraphicsBuffer SharedIndexBuffer => m_SharedIndexBuffer;

        public int InstanceCount { get; private set; }

        public int MaterialCount { get; private set; }

        public int DualSlabMaterialCount { get; private set; }

        public int MaterialParameterLaneCount { get; private set; }

        public int MaterialResourceCount { get; private set; }

        public int MaterialRuntimeHeaderCount { get; private set; }

        public int MaterialProgramCount { get; private set; }

        public int SurfaceBindingCount { get; private set; }

        public int TerrainMaterialCount { get; private set; }

        public int TerrainLayerCount { get; private set; }

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
            DualSlabMaterialCount = sceneData.DualSlabMaterialCount;
            MaterialParameterLaneCount = sceneData.MaterialParameterLaneCount;
            MaterialResourceCount = sceneData.MaterialResourceCount;
            MaterialRuntimeHeaderCount = sceneData.MaterialRuntimeHeaderCount;
            MaterialProgramCount = sceneData.MaterialProgramCount;
            SurfaceBindingCount = sceneData.SurfaceBindingCount;
            TerrainMaterialCount = sceneData.TerrainMaterialCount;
            TerrainLayerCount = sceneData.TerrainLayerCount;
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
                    ref m_InstanceUploadData,
                    UnsafeUtility.SizeOf<VividInstanceData>(),
                    "VividGPUDriven_InstanceData"
                );
            }
            if (shouldUploadMaterialData)
            {
                UploadStructuredBuffer(
                    ref m_MaterialDataBuffer,
                    sceneData.MutableMaterials,
                    ref m_MaterialUploadData,
                    UnsafeUtility.SizeOf<VividMaterialData>(),
                    "VividGPUDriven_MaterialData"
                );
                UploadStructuredBuffer(
                    ref m_DualSlabMaterialDataBuffer,
                    sceneData.MutableDualSlabMaterials,
                    ref m_DualSlabMaterialUploadData,
                    UnsafeUtility.SizeOf<VividDualSlabMaterialData>(),
                    "VividGPUDriven_DualSlabMaterialData"
                );
                UploadStructuredBuffer(
                    ref m_MaterialParameterDataBuffer,
                    sceneData.MutableMaterialParameterLanes,
                    ref m_MaterialParameterUploadData,
                    UnsafeUtility.SizeOf<uint4>(),
                    "VividGPUDriven_MaterialParameterData"
                );
                UploadStructuredBuffer(
                    ref m_MaterialResourceDataBuffer,
                    sceneData.MutableMaterialResources,
                    ref m_MaterialResourceUploadData,
                    UnsafeUtility.SizeOf<VividMaterialResourceData>(),
                    "VividGPUDriven_MaterialResourceData"
                );
                UploadStructuredBuffer(
                    ref m_MaterialRuntimeHeaderBuffer,
                    sceneData.MutableMaterialRuntimeHeaders,
                    ref m_MaterialRuntimeHeaderUploadData,
                    UnsafeUtility.SizeOf<VividMaterialRuntimeHeader>(),
                    "VividGPUDriven_MaterialRuntimeHeaders"
                );
                UploadStructuredBuffer(
                    ref m_MaterialProgramBuffer,
                    sceneData.MutableMaterialPrograms,
                    ref m_MaterialProgramUploadData,
                    UnsafeUtility.SizeOf<VividMaterialProgramData>(),
                    "VividGPUDriven_MaterialPrograms"
                );
                UploadStructuredBuffer(
                    ref m_SurfaceBindingDataBuffer,
                    sceneData.MutableSurfaceBindings,
                    ref m_SurfaceBindingUploadData,
                    UnsafeUtility.SizeOf<VividSurfaceBindingData>(),
                    "VividGPUDriven_SurfaceBindingData"
                );
                UploadStructuredBuffer(
                    ref m_TerrainMaterialDataBuffer,
                    sceneData.MutableTerrainMaterials,
                    ref m_TerrainMaterialUploadData,
                    UnsafeUtility.SizeOf<VividTerrainMaterialData>(),
                    "VividGPUDriven_TerrainMaterialData"
                );
                UploadStructuredBuffer(
                    ref m_TerrainLayerDataBuffer,
                    sceneData.MutableTerrainLayers,
                    ref m_TerrainLayerUploadData,
                    UnsafeUtility.SizeOf<VividTerrainLayerGPUData>(),
                    "VividGPUDriven_TerrainLayerData"
                );
            }

            if (shouldUploadStaticData)
            {
                UploadStructuredBuffer(
                    ref m_MeshLODNodesBuffer,
                    sceneData.MutableMeshLODNodes,
                    ref m_MeshLODNodeUploadData,
                    UnsafeUtility.SizeOf<VividMeshLODNode>(),
                    "VividGPUDriven_MeshLODNodes"
                );
                UploadStructuredBuffer(
                    ref m_MeshletsBuffer,
                    sceneData.MutableMeshlets,
                    ref m_MeshletUploadData,
                    UnsafeUtility.SizeOf<VividMeshlet>(),
                    "VividGPUDriven_Meshlets"
                );
                UploadStructuredBuffer(
                    ref m_SharedVertexBuffer,
                    sceneData.MutableVertices,
                    ref m_VertexUploadData,
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
            cmd.SetGlobalBuffer(
                VividGPUDrivenShaderIDs._DualSlabMaterialData,
                DualSlabMaterialDataBuffer);
            cmd.SetGlobalBuffer(
                VividGPUDrivenShaderIDs._MaterialParameterData,
                MaterialParameterDataBuffer);
            cmd.SetGlobalBuffer(
                VividGPUDrivenShaderIDs._MaterialResourceData,
                MaterialResourceDataBuffer);
            cmd.SetGlobalBuffer(
                VividGPUDrivenShaderIDs._MaterialRuntimeHeaders,
                MaterialRuntimeHeaderBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._MaterialPrograms, MaterialProgramBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._SurfaceBindingData, SurfaceBindingDataBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._TerrainMaterialData, TerrainMaterialDataBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._TerrainLayerData, TerrainLayerDataBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._MeshLODNodes, MeshLODNodesBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._Meshlets, MeshletsBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._SharedVertexBuffer, SharedVertexBuffer);
            cmd.SetGlobalBuffer(VividGPUDrivenShaderIDs._SharedIndexBuffer, SharedIndexBuffer);

            cmd.SetGlobalInteger(VividGPUDrivenShaderIDs._InstanceDataCount, InstanceCount);
            cmd.SetGlobalInteger(VividGPUDrivenShaderIDs._MaterialDataCount, MaterialCount);
            cmd.SetGlobalInteger(
                VividGPUDrivenShaderIDs._DualSlabMaterialDataCount,
                DualSlabMaterialCount);
            cmd.SetGlobalInteger(
                VividGPUDrivenShaderIDs._MaterialParameterDataCount,
                MaterialParameterLaneCount);
            cmd.SetGlobalInteger(
                VividGPUDrivenShaderIDs._MaterialResourceDataCount,
                MaterialResourceCount);
            cmd.SetGlobalInteger(
                VividGPUDrivenShaderIDs._MaterialRuntimeHeaderCount,
                MaterialRuntimeHeaderCount);
            cmd.SetGlobalInteger(
                VividGPUDrivenShaderIDs._MaterialProgramCount,
                MaterialProgramCount);
            cmd.SetGlobalInteger(VividGPUDrivenShaderIDs._SurfaceBindingDataCount, SurfaceBindingCount);
            cmd.SetGlobalInteger(VividGPUDrivenShaderIDs._TerrainMaterialDataCount, TerrainMaterialCount);
            cmd.SetGlobalInteger(VividGPUDrivenShaderIDs._TerrainLayerDataCount, TerrainLayerCount);
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
            m_DualSlabMaterialDataBuffer?.Dispose();
            m_MaterialParameterDataBuffer?.Dispose();
            m_MaterialResourceDataBuffer?.Dispose();
            m_MaterialRuntimeHeaderBuffer?.Dispose();
            m_MaterialProgramBuffer?.Dispose();
            m_SurfaceBindingDataBuffer?.Dispose();
            m_TerrainMaterialDataBuffer?.Dispose();
            m_TerrainLayerDataBuffer?.Dispose();
            m_MeshLODNodesBuffer?.Dispose();
            m_MeshletsBuffer?.Dispose();
            m_SharedVertexBuffer?.Dispose();
            m_SharedIndexBuffer?.Dispose();

            m_InstanceDataBuffer = null;
            m_MaterialDataBuffer = null;
            m_DualSlabMaterialDataBuffer = null;
            m_MaterialParameterDataBuffer = null;
            m_MaterialResourceDataBuffer = null;
            m_MaterialRuntimeHeaderBuffer = null;
            m_MaterialProgramBuffer = null;
            m_SurfaceBindingDataBuffer = null;
            m_TerrainMaterialDataBuffer = null;
            m_TerrainLayerDataBuffer = null;
            m_MeshLODNodesBuffer = null;
            m_MeshletsBuffer = null;
            m_SharedVertexBuffer = null;
            m_SharedIndexBuffer = null;
            m_InstanceUploadData = Array.Empty<VividInstanceData>();
            m_MaterialUploadData = Array.Empty<VividMaterialData>();
            m_DualSlabMaterialUploadData = Array.Empty<VividDualSlabMaterialData>();
            m_MaterialParameterUploadData = Array.Empty<uint4>();
            m_MaterialResourceUploadData = Array.Empty<VividMaterialResourceData>();
            m_MaterialRuntimeHeaderUploadData = Array.Empty<VividMaterialRuntimeHeader>();
            m_MaterialProgramUploadData = Array.Empty<VividMaterialProgramData>();
            m_SurfaceBindingUploadData = Array.Empty<VividSurfaceBindingData>();
            m_TerrainMaterialUploadData = Array.Empty<VividTerrainMaterialData>();
            m_TerrainLayerUploadData = Array.Empty<VividTerrainLayerGPUData>();
            m_MeshLODNodeUploadData = Array.Empty<VividMeshLODNode>();
            m_MeshletUploadData = Array.Empty<VividMeshlet>();
            m_VertexUploadData = Array.Empty<VividMeshletVertex>();
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
            ref T[] uploadData,
            int stride,
            string bufferName
        )
            where T : struct
        {
            int count = Mathf.Max(1, data.Count);
            EnsureStructuredBuffer(ref buffer, count, stride, bufferName);

            if (data.Count > 0)
            {
                EnsureUploadArrayCapacity(ref uploadData, data.Count);
                data.CopyTo(uploadData, 0);
                buffer.SetData(uploadData, 0, 0, data.Count);
            }
        }

        private static void EnsureUploadArrayCapacity<T>(ref T[] uploadData, int count)
        {
            if (uploadData.Length >= count)
            {
                return;
            }

            uploadData = new T[count];
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
            return !IsStructuredBufferCompatible(m_MaterialDataBuffer, sceneData.MaterialCount, UnsafeUtility.SizeOf<VividMaterialData>()) ||
                   !IsStructuredBufferCompatible(
                       m_DualSlabMaterialDataBuffer,
                       sceneData.DualSlabMaterialCount,
                       UnsafeUtility.SizeOf<VividDualSlabMaterialData>()) ||
                   !IsStructuredBufferCompatible(
                       m_MaterialParameterDataBuffer,
                       sceneData.MaterialParameterLaneCount,
                       UnsafeUtility.SizeOf<uint4>()) ||
                   !IsStructuredBufferCompatible(
                       m_MaterialResourceDataBuffer,
                       sceneData.MaterialResourceCount,
                       UnsafeUtility.SizeOf<VividMaterialResourceData>()) ||
                   !IsStructuredBufferCompatible(
                       m_MaterialRuntimeHeaderBuffer,
                       sceneData.MaterialRuntimeHeaderCount,
                       UnsafeUtility.SizeOf<VividMaterialRuntimeHeader>()) ||
                   !IsStructuredBufferCompatible(
                       m_MaterialProgramBuffer,
                       sceneData.MaterialProgramCount,
                       UnsafeUtility.SizeOf<VividMaterialProgramData>()) ||
                   !IsStructuredBufferCompatible(
                       m_SurfaceBindingDataBuffer,
                       sceneData.SurfaceBindingCount,
                       UnsafeUtility.SizeOf<VividSurfaceBindingData>()) ||
                   !IsStructuredBufferCompatible(
                       m_TerrainMaterialDataBuffer,
                       sceneData.TerrainMaterialCount,
                       UnsafeUtility.SizeOf<VividTerrainMaterialData>()) ||
                   !IsStructuredBufferCompatible(
                       m_TerrainLayerDataBuffer,
                       sceneData.TerrainLayerCount,
                       UnsafeUtility.SizeOf<VividTerrainLayerGPUData>());
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
