using System;
using UnityEngine;
using Unity.Scripting.LifecycleManagement;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    public class VividMeshletCollectionAsset : ScriptableObject, ISerializationCallbackReceiver
    {
        [NoAutoStaticsCleanup]
        private static uint s_GlobalContentRevision = 1u;

        [SerializeField, HideInInspector] private uint m_ContentVersion = 1u;
        [SerializeField, HideInInspector] private uint m_MeshDataSerializationVersion = VividMeshletCollectionBinarySerializer.CurrentVersion;
        [SerializeField, HideInInspector] private byte[] m_MeshDataBlob = Array.Empty<byte>();

        [NonSerialized] private int[] m_MeshLODLevelNodeCounts = Array.Empty<int>();
        [NonSerialized] private VividMeshLODNodePacked[] m_MeshLODNodes = Array.Empty<VividMeshLODNodePacked>();
        [NonSerialized] private VividMeshLODGroup[] m_MeshLODGroups = Array.Empty<VividMeshLODGroup>();
        [NonSerialized] private VividMeshlet[] m_Meshlets = Array.Empty<VividMeshlet>();
        [NonSerialized] private VividMeshletVertex[] m_VertexBuffer = Array.Empty<VividMeshletVertex>();
        [NonSerialized] private byte[] m_IndexBuffer = Array.Empty<byte>();
        [NonSerialized] private bool m_MeshDataLoaded;

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

        public int[] MeshLODLevelNodeCounts
        {
            get
            {
                EnsureMeshDataLoaded();
                return m_MeshLODLevelNodeCounts;
            }
            set => SetMeshData(value, MeshLODNodes, MeshLODGroups, Meshlets, VertexBuffer, IndexBuffer);
        }

        public VividMeshLODNodePacked[] MeshLODNodes
        {
            get
            {
                EnsureMeshDataLoaded();
                return m_MeshLODNodes;
            }
            set => SetMeshData(MeshLODLevelNodeCounts, value, MeshLODGroups, Meshlets, VertexBuffer, IndexBuffer);
        }

        public VividMeshLODGroup[] MeshLODGroups
        {
            get
            {
                EnsureMeshDataLoaded();
                return m_MeshLODGroups;
            }
        }

        public VividMeshlet[] Meshlets
        {
            get
            {
                EnsureMeshDataLoaded();
                return m_Meshlets;
            }
            set => SetMeshData(MeshLODLevelNodeCounts, MeshLODNodes, MeshLODGroups, value, VertexBuffer, IndexBuffer);
        }

        public VividMeshletVertex[] VertexBuffer
        {
            get
            {
                EnsureMeshDataLoaded();
                return m_VertexBuffer;
            }
            set => SetMeshData(MeshLODLevelNodeCounts, MeshLODNodes, MeshLODGroups, Meshlets, value, IndexBuffer);
        }

        public byte[] IndexBuffer
        {
            get
            {
                EnsureMeshDataLoaded();
                return m_IndexBuffer;
            }
            set => SetMeshData(MeshLODLevelNodeCounts, MeshLODNodes, MeshLODGroups, Meshlets, VertexBuffer, value);
        }

        // Reimport .vmeshletcollection sources (or rebake terrain) to restore automatic LOD.
        public bool RequiresLODRebuild => m_MeshDataSerializationVersion is 4u or 5u;

        public uint ContentVersion => m_ContentVersion;

        internal static uint GlobalContentRevision => s_GlobalContentRevision;

        public void SetMeshData(
            int[] meshLODLevelNodeCounts,
            VividMeshLODNodePacked[] meshLODNodes,
            VividMeshLODGroup[] meshLODGroups,
            VividMeshlet[] meshlets,
            VividMeshletVertex[] vertexBuffer,
            byte[] indexBuffer)
        {
            meshLODNodes ??= Array.Empty<VividMeshLODNodePacked>();
            meshLODGroups ??= Array.Empty<VividMeshLODGroup>();
            VividMeshLODGroupTable.Validate(meshLODNodes, meshLODGroups.Length);
            m_MeshLODLevelNodeCounts = meshLODLevelNodeCounts ?? Array.Empty<int>();
            m_MeshLODGroups = meshLODGroups;
            m_MeshLODNodes = meshLODNodes;
            m_Meshlets = meshlets ?? Array.Empty<VividMeshlet>();
            m_VertexBuffer = vertexBuffer ?? Array.Empty<VividMeshletVertex>();
            m_IndexBuffer = indexBuffer ?? Array.Empty<byte>();
            m_MeshDataLoaded = true;
            m_MeshDataSerializationVersion = VividMeshletCollectionBinarySerializer.CurrentVersion;
        }

        // Compatibility for callers supplying pre-v7 authoring data. Convert once;
        // the asset retains only the compact node array and shared groups.
        public void SetMeshData(int[] meshLODLevelNodeCounts, VividMeshLODNode[] meshLODNodes,
            VividMeshlet[] meshlets, VividMeshletVertex[] vertexBuffer, byte[] indexBuffer)
        {
            var table = new VividMeshLODGroupTable();
            table.Build(meshLODNodes ?? Array.Empty<VividMeshLODNode>());
            SetMeshData(meshLODLevelNodeCounts, table.Nodes.ToArray(), table.Groups.ToArray(),
                meshlets, vertexBuffer, indexBuffer);
        }

        public void MarkChanged()
        {
            m_ContentVersion = IncrementRevision(m_ContentVersion);
            s_GlobalContentRevision = IncrementRevision(s_GlobalContentRevision);
        }

        private static uint IncrementRevision(uint revision)
        {
            return revision == uint.MaxValue ? 1u : revision + 1u;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            if (!m_MeshDataLoaded || RequiresLODRebuild)
            {
                return;
            }

            m_MeshDataSerializationVersion = VividMeshletCollectionBinarySerializer.CurrentVersion;
            m_MeshDataBlob = VividMeshletCollectionBinarySerializer.Serialize(
                m_MeshLODLevelNodeCounts,
                m_MeshLODNodes,
                m_MeshLODGroups,
                m_Meshlets,
                m_VertexBuffer,
                m_IndexBuffer
            );
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            ClearRuntimeMeshData();
            m_MeshDataLoaded = false;
        }

        private void EnsureMeshDataLoaded()
        {
            if (m_MeshDataLoaded)
            {
                return;
            }

            try
            {
                VividMeshletCollectionBinarySerializer.Deserialize(
                    m_MeshDataBlob,
                    out m_MeshLODLevelNodeCounts,
                    out m_MeshLODNodes,
                    out m_MeshLODGroups,
                    out m_Meshlets,
                    out m_VertexBuffer,
                    out m_IndexBuffer
                );
                if (RequiresLODRebuild)
                {
                    MeshLODLevelCount = m_MeshLODLevelNodeCounts.Length;
                }
                if (m_MeshDataSerializationVersion < 5u)
                {
                    Bounds = VividMeshletVertexPacking.ExpandBoundsForQuantization(Bounds);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[VividRP] Failed to deserialize meshlet data for asset '{name}'. " +
                    $"Stored format version: {m_MeshDataSerializationVersion}. {exception.Message}",
                    this
                );
                ClearRuntimeMeshData();
            }

            m_MeshDataLoaded = true;
        }

        private void ClearRuntimeMeshData()
        {
            m_MeshLODLevelNodeCounts = Array.Empty<int>();
            m_MeshLODNodes = Array.Empty<VividMeshLODNodePacked>();
            m_MeshLODGroups = Array.Empty<VividMeshLODGroup>();
            m_Meshlets = Array.Empty<VividMeshlet>();
            m_VertexBuffer = Array.Empty<VividMeshletVertex>();
            m_IndexBuffer = Array.Empty<byte>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            MarkChanged();
        }
#endif

        private void OnDestroy()
        {
            s_GlobalContentRevision = IncrementRevision(s_GlobalContentRevision);
        }
    }
}
