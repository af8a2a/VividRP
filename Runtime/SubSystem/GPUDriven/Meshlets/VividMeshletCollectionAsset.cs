using System;
using UnityEngine;
using VividRP.Runtime.GPUDriven.MeshOptimizer;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    public class VividMeshletCollectionAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] private uint m_ContentVersion = 1u;

        public static readonly VividMeshOptimizer.MeshletGenerationParams MeshletGenerationParams = new()
        {
            MaxVertices = VividMeshletConfiguration.MaxMeshletVertices,
            MaxTriangles = VividMeshletConfiguration.MaxMeshletTriangles,
            ConeWeight = VividMeshletConfiguration.MeshletConeWeight,
        };

        [HideInInspector] public string SourceMeshGUID = string.Empty;
        [HideInInspector] public long SourceMeshLocalFileID;
        [HideInInspector] public string SourceMeshName = string.Empty;
        [HideInInspector] public int SourceSubmeshIndex = -1;

        public Bounds Bounds;
        public int MeshLODLevelCount;
        public int LeafMeshletCount;
        public int[] MeshLODLevelNodeCounts = Array.Empty<int>();
        public VividMeshLODNode[] MeshLODNodes = Array.Empty<VividMeshLODNode>();
        public VividMeshlet[] Meshlets = Array.Empty<VividMeshlet>();
        public VividMeshletVertex[] VertexBuffer = Array.Empty<VividMeshletVertex>();
        public byte[] IndexBuffer = Array.Empty<byte>();

        public uint ContentVersion => m_ContentVersion;

        public void MarkChanged()
        {
            unchecked
            {
                m_ContentVersion = m_ContentVersion == uint.MaxValue ? 1u : m_ContentVersion + 1u;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            MarkChanged();
        }
#endif
    }
}
