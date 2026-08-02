using System.Collections.Generic;

namespace VividRP.Runtime.GPUDriven
{
    public sealed class VividGPUDrivenSceneData
    {
        private readonly List<VividInstanceData> m_Instances = new();
        private readonly List<VividMaterialData> m_Materials = new();
        private readonly List<VividSurfaceBindingData> m_SurfaceBindings = new();
        private readonly List<VividMeshLODNode> m_MeshLODNodes = new();
        private readonly List<VividMeshlet> m_Meshlets = new();
        private readonly List<VividMeshletVertex> m_Vertices = new();
        private readonly List<byte> m_Indices = new();

        public IReadOnlyList<VividInstanceData> Instances => m_Instances;

        public IReadOnlyList<VividMaterialData> Materials => m_Materials;

        public IReadOnlyList<VividSurfaceBindingData> SurfaceBindings => m_SurfaceBindings;

        public IReadOnlyList<VividMeshLODNode> MeshLODNodes => m_MeshLODNodes;

        public IReadOnlyList<VividMeshlet> Meshlets => m_Meshlets;

        public IReadOnlyList<VividMeshletVertex> Vertices => m_Vertices;

        public IReadOnlyList<byte> Indices => m_Indices;

        public int InstanceCount => m_Instances.Count;

        public int MaterialCount => m_Materials.Count;

        public int SurfaceBindingCount => m_SurfaceBindings.Count;

        public int MeshLODNodeCount => m_MeshLODNodes.Count;

        public int MeshletCount => m_Meshlets.Count;

        public int VertexCount => m_Vertices.Count;

        public int IndexCount => m_Indices.Count;

        internal List<VividInstanceData> MutableInstances => m_Instances;

        internal List<VividMaterialData> MutableMaterials => m_Materials;

        internal List<VividSurfaceBindingData> MutableSurfaceBindings => m_SurfaceBindings;

        internal List<VividMeshLODNode> MutableMeshLODNodes => m_MeshLODNodes;

        internal List<VividMeshlet> MutableMeshlets => m_Meshlets;

        internal List<VividMeshletVertex> MutableVertices => m_Vertices;

        internal List<byte> MutableIndices => m_Indices;

        internal void ClearInstances()
        {
            m_Instances.Clear();
        }

        internal void ClearMaterials()
        {
            m_Materials.Clear();
        }

        internal void ClearSurfaceBindings()
        {
            m_SurfaceBindings.Clear();
        }

        internal void ClearDynamic()
        {
            ClearInstances();
            ClearMaterials();
            ClearSurfaceBindings();
        }

        internal void Clear()
        {
            ClearDynamic();
            m_MeshLODNodes.Clear();
            m_Meshlets.Clear();
            m_Vertices.Clear();
            m_Indices.Clear();
        }
    }
}
