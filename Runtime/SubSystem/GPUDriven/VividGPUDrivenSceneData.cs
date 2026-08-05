using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    public sealed class VividGPUDrivenSceneData
    {
        private readonly List<VividInstanceData> m_Instances = new();
        private readonly List<VividMaterialData> m_Materials = new();
        private readonly List<VividSurfaceBindingData> m_SurfaceBindings = new();
        private readonly List<VividTerrainMaterialData> m_TerrainMaterials = new();
        private readonly List<VividTerrainLayerGPUData> m_TerrainLayers = new();
        private readonly List<VividMeshLODNode> m_MeshLODNodes = new();
        private readonly List<VividMeshlet> m_Meshlets = new();
        private readonly List<VividMeshletVertex> m_Vertices = new();
        private readonly List<byte> m_Indices = new();
        private int m_MaxMeshletListBuildJobCount;
        private int m_MaxVisibleMeshletRenderRequestCount;

        public IReadOnlyList<VividInstanceData> Instances => m_Instances;

        public IReadOnlyList<VividMaterialData> Materials => m_Materials;

        public IReadOnlyList<VividSurfaceBindingData> SurfaceBindings => m_SurfaceBindings;

        public IReadOnlyList<VividTerrainMaterialData> TerrainMaterials => m_TerrainMaterials;

        public IReadOnlyList<VividTerrainLayerGPUData> TerrainLayers => m_TerrainLayers;

        public IReadOnlyList<VividMeshLODNode> MeshLODNodes => m_MeshLODNodes;

        public IReadOnlyList<VividMeshlet> Meshlets => m_Meshlets;

        public IReadOnlyList<VividMeshletVertex> Vertices => m_Vertices;

        public IReadOnlyList<byte> Indices => m_Indices;

        public int InstanceCount => m_Instances.Count;

        public int MaterialCount => m_Materials.Count;

        public int SurfaceBindingCount => m_SurfaceBindings.Count;

        public int TerrainMaterialCount => m_TerrainMaterials.Count;

        public int TerrainLayerCount => m_TerrainLayers.Count;

        public int MeshLODNodeCount => m_MeshLODNodes.Count;

        public int MeshletCount => m_Meshlets.Count;

        public int VertexCount => m_Vertices.Count;

        public int IndexCount => m_Indices.Count;

        internal int MaxMeshletListBuildJobCount => Mathf.Max(1, m_MaxMeshletListBuildJobCount);

        internal int MaxVisibleMeshletRenderRequestCount => Mathf.Max(1, m_MaxVisibleMeshletRenderRequestCount);

        internal List<VividInstanceData> MutableInstances => m_Instances;

        internal List<VividMaterialData> MutableMaterials => m_Materials;

        internal List<VividSurfaceBindingData> MutableSurfaceBindings => m_SurfaceBindings;

        internal List<VividTerrainMaterialData> MutableTerrainMaterials => m_TerrainMaterials;

        internal List<VividTerrainLayerGPUData> MutableTerrainLayers => m_TerrainLayers;

        internal List<VividMeshLODNode> MutableMeshLODNodes => m_MeshLODNodes;

        internal List<VividMeshlet> MutableMeshlets => m_Meshlets;

        internal List<VividMeshletVertex> MutableVertices => m_Vertices;

        internal List<byte> MutableIndices => m_Indices;

        internal void AddInstance(
            in VividInstanceData instanceData,
            int maxVisibleMeshletRenderRequestCount)
        {
            m_Instances.Add(instanceData);

            uint maxNodesPerJob = global::VividRP.Runtime.GPUDriven.Meshlets.VividMeshletListBuildJob.MaxLODNodesPerThreadGroup;
            if (maxNodesPerJob == 0)
                maxNodesPerJob = 1;

            ulong jobCount = ((ulong) instanceData.TotalMeshLODCount + maxNodesPerJob - 1u) / maxNodesPerJob;
            m_MaxMeshletListBuildJobCount = SaturatingAdd(
                m_MaxMeshletListBuildJobCount,
                jobCount > int.MaxValue ? int.MaxValue : (int) jobCount);
            m_MaxVisibleMeshletRenderRequestCount = SaturatingAdd(
                m_MaxVisibleMeshletRenderRequestCount,
                Mathf.Max(0, maxVisibleMeshletRenderRequestCount));
        }

        internal void ClearInstances()
        {
            m_Instances.Clear();
            m_MaxMeshletListBuildJobCount = 0;
            m_MaxVisibleMeshletRenderRequestCount = 0;
        }

        internal void ClearMaterials()
        {
            m_Materials.Clear();
        }

        internal void ClearSurfaceBindings()
        {
            m_SurfaceBindings.Clear();
        }

        internal void ClearTerrainMaterials()
        {
            m_TerrainMaterials.Clear();
            m_TerrainLayers.Clear();
        }

        internal void ClearDynamic()
        {
            ClearInstances();
            ClearMaterials();
            ClearSurfaceBindings();
            ClearTerrainMaterials();
        }

        internal void Clear()
        {
            ClearDynamic();
            m_MeshLODNodes.Clear();
            m_Meshlets.Clear();
            m_Vertices.Clear();
            m_Indices.Clear();
        }

        private static int SaturatingAdd(int current, int increment)
        {
            if (increment <= 0)
                return current;

            return current > int.MaxValue - increment
                ? int.MaxValue
                : current + increment;
        }
    }
}
