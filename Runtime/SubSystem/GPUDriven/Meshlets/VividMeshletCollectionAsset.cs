using System;
using UnityEngine;
using UnityEngine.Serialization;
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

        [SerializeField, HideInInspector, FormerlySerializedAs("MeshLODLevelNodeCounts")]
        private int[] m_LegacyMeshLODLevelNodeCounts = Array.Empty<int>();

        [SerializeField, HideInInspector, FormerlySerializedAs("MeshLODNodes")]
        private VividMeshLODNodeLegacy64[] m_LegacyMeshLODNodes = Array.Empty<VividMeshLODNodeLegacy64>();

        [SerializeField, HideInInspector, FormerlySerializedAs("Meshlets")]
        private VividMeshletLegacy64[] m_LegacyMeshlets = Array.Empty<VividMeshletLegacy64>();

        [SerializeField, HideInInspector, FormerlySerializedAs("VertexBuffer")]
        private VividMeshletVertexLegacy64[] m_LegacyVertexBuffer = Array.Empty<VividMeshletVertexLegacy64>();

        [SerializeField, HideInInspector, FormerlySerializedAs("IndexBuffer")]
        private byte[] m_LegacyIndexBuffer = Array.Empty<byte>();

        [NonSerialized] private int[] m_MeshLODLevelNodeCounts = Array.Empty<int>();
        [NonSerialized] private VividMeshLODNode[] m_MeshLODNodes = Array.Empty<VividMeshLODNode>();
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
            set => SetMeshData(value, MeshLODNodes, Meshlets, VertexBuffer, IndexBuffer);
        }

        public VividMeshLODNode[] MeshLODNodes
        {
            get
            {
                EnsureMeshDataLoaded();
                return m_MeshLODNodes;
            }
            set => SetMeshData(MeshLODLevelNodeCounts, value, Meshlets, VertexBuffer, IndexBuffer);
        }

        public VividMeshlet[] Meshlets
        {
            get
            {
                EnsureMeshDataLoaded();
                return m_Meshlets;
            }
            set => SetMeshData(MeshLODLevelNodeCounts, MeshLODNodes, value, VertexBuffer, IndexBuffer);
        }

        public VividMeshletVertex[] VertexBuffer
        {
            get
            {
                EnsureMeshDataLoaded();
                return m_VertexBuffer;
            }
            set => SetMeshData(MeshLODLevelNodeCounts, MeshLODNodes, Meshlets, value, IndexBuffer);
        }

        public byte[] IndexBuffer
        {
            get
            {
                EnsureMeshDataLoaded();
                return m_IndexBuffer;
            }
            set => SetMeshData(MeshLODLevelNodeCounts, MeshLODNodes, Meshlets, VertexBuffer, value);
        }

        public uint ContentVersion => m_ContentVersion;

        internal static uint GlobalContentRevision => s_GlobalContentRevision;

        public void SetMeshData(
            int[] meshLODLevelNodeCounts,
            VividMeshLODNode[] meshLODNodes,
            VividMeshlet[] meshlets,
            VividMeshletVertex[] vertexBuffer,
            byte[] indexBuffer)
        {
            m_MeshLODLevelNodeCounts = meshLODLevelNodeCounts ?? Array.Empty<int>();
            m_MeshLODNodes = meshLODNodes ?? Array.Empty<VividMeshLODNode>();
            m_Meshlets = meshlets ?? Array.Empty<VividMeshlet>();
            m_VertexBuffer = vertexBuffer ?? Array.Empty<VividMeshletVertex>();
            m_IndexBuffer = indexBuffer ?? Array.Empty<byte>();
            m_MeshDataLoaded = true;
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
            if (!m_MeshDataLoaded)
            {
                if (!TryMigrateLegacyMeshData())
                {
                    return;
                }
            }

            m_MeshDataSerializationVersion = VividMeshletCollectionBinarySerializer.CurrentVersion;
            m_MeshDataBlob = VividMeshletCollectionBinarySerializer.Serialize(
                m_MeshLODLevelNodeCounts,
                m_MeshLODNodes,
                m_Meshlets,
                m_VertexBuffer,
                m_IndexBuffer
            );
            ClearLegacyMeshData();
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

            if (!TryMigrateLegacyMeshData())
            {
                try
                {
                    VividMeshletCollectionBinarySerializer.Deserialize(
                        m_MeshDataBlob,
                        out m_MeshLODLevelNodeCounts,
                        out m_MeshLODNodes,
                        out m_Meshlets,
                        out m_VertexBuffer,
                        out m_IndexBuffer
                    );
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
            }

            m_MeshDataLoaded = true;
        }

        private bool TryMigrateLegacyMeshData()
        {
            if (!HasLegacyMeshData())
            {
                return false;
            }

            SetMeshData(
                m_LegacyMeshLODLevelNodeCounts,
                VividMeshletCollectionBinarySerializer.ConvertLegacyMeshLODNodes(m_LegacyMeshLODNodes),
                VividMeshletCollectionBinarySerializer.ConvertLegacyMeshlets(m_LegacyMeshlets),
                VividMeshletCollectionBinarySerializer.ConvertLegacyVertices(m_LegacyVertexBuffer),
                m_LegacyIndexBuffer
            );
            ClearLegacyMeshData();
            return true;
        }

        private bool HasLegacyMeshData()
        {
            return m_LegacyMeshLODLevelNodeCounts is { Length: > 0 }
                   || m_LegacyMeshLODNodes is { Length: > 0 }
                   || m_LegacyMeshlets is { Length: > 0 }
                   || m_LegacyVertexBuffer is { Length: > 0 }
                   || m_LegacyIndexBuffer is { Length: > 0 };
        }

        private void ClearRuntimeMeshData()
        {
            m_MeshLODLevelNodeCounts = Array.Empty<int>();
            m_MeshLODNodes = Array.Empty<VividMeshLODNode>();
            m_Meshlets = Array.Empty<VividMeshlet>();
            m_VertexBuffer = Array.Empty<VividMeshletVertex>();
            m_IndexBuffer = Array.Empty<byte>();
        }

        private void ClearLegacyMeshData()
        {
            m_LegacyMeshLODLevelNodeCounts = Array.Empty<int>();
            m_LegacyMeshLODNodes = Array.Empty<VividMeshLODNodeLegacy64>();
            m_LegacyMeshlets = Array.Empty<VividMeshletLegacy64>();
            m_LegacyVertexBuffer = Array.Empty<VividMeshletVertexLegacy64>();
            m_LegacyIndexBuffer = Array.Empty<byte>();
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
