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

        [Serializable]
        internal struct LocalCard
        {
            public int EntryIndex;
            public Bounds Bounds;
            public int Face;
        }

        [SerializeField] private Texture2DArray m_AlbedoDepth;
        [SerializeField] private Texture2DArray m_NormalCoverage;
        [SerializeField] private Entry[] m_Entries;
        [SerializeField, HideInInspector] private LocalCard[] m_LocalCards;
        // Kept for assets baked with the single-local-card layout.
        [SerializeField] private int m_LocalCardEntry = -1;
        [SerializeField] private int m_LocalCardFace;
        [SerializeField] private Bounds m_LocalCardBounds;
        private Dictionary<(Mesh, Material), int> m_Indices;
        private RTHandle m_AlbedoDepthHandle, m_NormalCoverageHandle;
        private GraphicsBuffer m_LocalCardTable;
        private int[] m_LocalCardCounts;

        internal RTHandle AlbedoDepthHandle => m_AlbedoDepthHandle ??= RTHandles.Alloc(m_AlbedoDepth);
        internal RTHandle NormalCoverageHandle => m_NormalCoverageHandle ??= RTHandles.Alloc(m_NormalCoverage);
        internal Vector2Int CardSize => new(m_AlbedoDepth.width, m_AlbedoDepth.height);
        internal ulong CardVersion => ((ulong)m_AlbedoDepth.updateCount << 32) | m_NormalCoverage.updateCount;
        private bool HasLegacyLocalCard => m_LocalCardEntry >= 0 && m_LocalCardEntry < m_Entries.Length
            && (uint)m_LocalCardFace < CardsPerMesh && IsValidLocalCardBounds(m_LocalCardBounds)
            && m_AlbedoDepth.depth == m_Entries.Length * CardsPerMesh + 1;
        private int LocalCardCount => m_LocalCards is { Length: > 0 } ? m_LocalCards.Length : HasLegacyLocalCard ? 1 : 0;
        internal GraphicsBuffer LocalCardTable => m_LocalCardTable ??= CreateLocalCardTable();
        internal int GetLocalCardCount(int firstCard)
        {
            _ = LocalCardTable;
            return m_LocalCardCounts[firstCard / CardsPerMesh];
        }
        internal bool IsUsable => m_Entries != null && m_Entries.Length != 0
            && m_AlbedoDepth != null && m_NormalCoverage != null
            && m_AlbedoDepth.depth == m_Entries.Length * CardsPerMesh + LocalCardCount
            && m_NormalCoverage.depth == m_AlbedoDepth.depth
            && m_NormalCoverage.width == m_AlbedoDepth.width && m_NormalCoverage.height == m_AlbedoDepth.height;

        internal void Initialize(Entry[] entries, Texture2DArray albedoDepth, Texture2DArray normalCoverage,
            LocalCard[] localCards)
        {
            OnDisable();
            m_Entries = entries;
            m_AlbedoDepth = albedoDepth;
            m_NormalCoverage = normalCoverage;
            m_LocalCards = localCards;
            m_LocalCardEntry = -1;
            m_Indices = null;
        }

        internal static bool IsValidLocalCardBounds(Bounds bounds)
        {
            Vector3 size = bounds.max - bounds.min;
            return size.x > 0f && size.y > 0f && size.z > 0f
                && float.IsFinite(size.x) && float.IsFinite(size.y) && float.IsFinite(size.z);
        }

        private LocalCard ReadLocalCard(int index)
        {
            return m_LocalCards is { Length: > 0 } ? m_LocalCards[index]
                : new LocalCard { EntryIndex = m_LocalCardEntry, Bounds = m_LocalCardBounds, Face = m_LocalCardFace };
        }

        private GraphicsBuffer CreateLocalCardTable()
        {
            int entries = m_Entries.Length;
            int count = LocalCardCount;
            // One float4 range per entry, followed by two float4s per local card.
            var table = new Vector4[entries + 2 * count];
            var offsets = new int[entries];
            var counts = new int[entries];
            for (int i = 0; i < count; i++)
            {
                var card = ReadLocalCard(i);
                if ((uint)card.EntryIndex >= (uint)entries || (uint)card.Face >= CardsPerMesh
                    || !IsValidLocalCardBounds(card.Bounds))
                    throw new InvalidOperationException("Invalid baked SDF local card.");
                counts[card.EntryIndex]++;
            }
            int row = entries;
            for (int i = 0; i < entries; i++)
            {
                int end = row + 2 * counts[i];
                table[i] = new Vector4(row, end, 0f, 0f);
                offsets[i] = row;
                row = end;
            }
            int baseSlices = entries * CardsPerMesh;
            for (int i = 0; i < count; i++)
            {
                var card = ReadLocalCard(i);
                Vector3 min = card.Bounds.min;
                Vector3 extent = card.Bounds.size;
                row = offsets[card.EntryIndex];
                table[row] = new Vector4(min.x, min.y, min.z, baseSlices + i);
                table[row + 1] = new Vector4(extent.x, extent.y, extent.z, card.Face);
                offsets[card.EntryIndex] += 2;
            }
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, table.Length, 4 * sizeof(float))
                { name = "SDF Local Surface Card Table" };
            try
            {
                buffer.SetData(table);
                m_LocalCardCounts = counts;
                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }

        private void OnDisable()
        {
            m_AlbedoDepthHandle?.Release();
            m_NormalCoverageHandle?.Release();
            m_AlbedoDepthHandle = m_NormalCoverageHandle = null;
            m_LocalCardTable?.Dispose();
            m_LocalCardTable = null;
            m_LocalCardCounts = null;
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
