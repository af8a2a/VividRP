using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    public sealed class VividSdfSurfaceCache : ScriptableObject
    {
        internal const int CardsPerMesh = 6;

        [Serializable]
        internal struct Entry
        {
            public Mesh Mesh;
            public Material Material;
            public Bounds Bounds;
        }

        [SerializeField] private Texture2DArray m_AlbedoDepth;
        [SerializeField] private Texture2DArray m_NormalCoverage;
        [SerializeField] private Entry[] m_Entries;
        [SerializeField] private int m_LocalCardEntry = -1;
        [SerializeField] private int m_LocalCardFace;
        [SerializeField] private Bounds m_LocalCardBounds;
        private Dictionary<(Mesh, Material), int> m_Indices;
        private RTHandle m_AlbedoDepthHandle, m_NormalCoverageHandle;

        internal RTHandle AlbedoDepthHandle => m_AlbedoDepthHandle ??= RTHandles.Alloc(m_AlbedoDepth);
        internal RTHandle NormalCoverageHandle => m_NormalCoverageHandle ??= RTHandles.Alloc(m_NormalCoverage);
        internal Vector2Int CardSize => new(m_AlbedoDepth.width, m_AlbedoDepth.height);
        internal ulong CardVersion => ((ulong)m_AlbedoDepth.updateCount << 32) | m_NormalCoverage.updateCount;
        private bool HasLocalCard => m_LocalCardEntry >= 0 && m_LocalCardEntry < m_Entries.Length
            && (uint)m_LocalCardFace < CardsPerMesh && m_AlbedoDepth.depth == m_Entries.Length * CardsPerMesh + 1;
        internal bool IsUsable => m_Entries != null && m_Entries.Length != 0
            && m_AlbedoDepth != null && m_NormalCoverage != null
            && (m_AlbedoDepth.depth == m_Entries.Length * CardsPerMesh || HasLocalCard)
            && m_NormalCoverage.depth == m_AlbedoDepth.depth
            && m_NormalCoverage.width == m_AlbedoDepth.width && m_NormalCoverage.height == m_AlbedoDepth.height;

        internal void Initialize(Entry[] entries, Texture2DArray albedoDepth, Texture2DArray normalCoverage,
            int localCardEntry = -1, int localCardFace = 0, Bounds localCardBounds = default)
        {
            OnDisable();
            m_Entries = entries;
            m_AlbedoDepth = albedoDepth;
            m_NormalCoverage = normalCoverage;
            m_LocalCardEntry = localCardEntry;
            m_LocalCardFace = localCardFace;
            m_LocalCardBounds = localCardBounds;
            m_Indices = null;
        }

        internal void GetLocalCard(out int owner, out Vector4 minAndSlice, out Vector4 extentAndFace)
        {
            owner = 0;
            minAndSlice = extentAndFace = default;
            if (!HasLocalCard)
                return;

            Vector3 min = m_LocalCardBounds.min;
            Vector3 extent = m_LocalCardBounds.size;
            owner = m_LocalCardEntry * CardsPerMesh + 1;
            minAndSlice = new Vector4(min.x, min.y, min.z, m_AlbedoDepth.depth - 1);
            extentAndFace = new Vector4(extent.x, extent.y, extent.z, m_LocalCardFace);
        }

        private void OnDisable()
        {
            m_AlbedoDepthHandle?.Release();
            m_NormalCoverageHandle?.Release();
            m_AlbedoDepthHandle = m_NormalCoverageHandle = null;
        }

        internal bool TryGetCards(Mesh mesh, Material material, out Bounds bounds, out int firstCard)
        {
            if (m_Indices == null)
            {
                m_Indices = new Dictionary<(Mesh, Material), int>(m_Entries.Length);
                for (int i = 0; i < m_Entries.Length; i++)
                {
                    var entry = m_Entries[i];
                    if (entry.Mesh != null && entry.Material != null)
                        m_Indices.TryAdd((entry.Mesh, entry.Material), i);
                }
            }

            if (m_Indices.TryGetValue((mesh, material), out int index))
            {
                bounds = m_Entries[index].Bounds;
                firstCard = index * CardsPerMesh;
                return true;
            }
            bounds = default;
            firstCard = 0;
            return false;
        }
    }

    [Serializable]
    public sealed class VividSdfSurfaceCacheParameter : VolumeParameter<VividSdfSurfaceCache>
    {
        public VividSdfSurfaceCacheParameter(VividSdfSurfaceCache value, bool overrideState = false)
            : base(value, overrideState) { }
    }
}
