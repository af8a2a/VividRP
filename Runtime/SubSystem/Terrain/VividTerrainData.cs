using System;
using System.Collections.Generic;
using UnityEngine;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime
{
    [Serializable]
    public struct VividTerrainBakeSettings
    {
        public const int DefaultHeightSampleStride = 1;
        public const int DefaultChunkQuadCount = 64;
        public const int LegacyMaxMeshLODLevelCount = 1;
        public const int DefaultMaxMeshLODLevelCount = 4;
        public const int MaximumMeshLODLevelCount = 8;

        [SerializeField, Min(1)]
        private int m_HeightSampleStride;

        [SerializeField, Range(1, 256)]
        private int m_ChunkQuadCount;

        [SerializeField]
        private bool m_OptimizeVertexCache;

        [SerializeField, Range(1, MaximumMeshLODLevelCount)]
        private int m_MaxMeshLODLevelCount;

        public VividTerrainBakeSettings(
            int heightSampleStride,
            int chunkQuadCount,
            bool optimizeVertexCache = true,
            int maxMeshLODLevelCount = DefaultMaxMeshLODLevelCount)
        {
            m_HeightSampleStride = Mathf.Max(1, heightSampleStride);
            m_ChunkQuadCount = Mathf.Clamp(chunkQuadCount, 1, 256);
            m_OptimizeVertexCache = optimizeVertexCache;
            m_MaxMeshLODLevelCount = Mathf.Clamp(
                maxMeshLODLevelCount,
                LegacyMaxMeshLODLevelCount,
                MaximumMeshLODLevelCount
            );
        }

        public int HeightSampleStride => Mathf.Max(1, m_HeightSampleStride);

        public int ChunkQuadCount => Mathf.Clamp(m_ChunkQuadCount, 1, 256);

        public bool OptimizeVertexCache => m_OptimizeVertexCache;

        public int MaxMeshLODLevelCount => m_MaxMeshLODLevelCount <= 0
            ? LegacyMaxMeshLODLevelCount
            : Mathf.Clamp(
                m_MaxMeshLODLevelCount,
                LegacyMaxMeshLODLevelCount,
                MaximumMeshLODLevelCount
            );

        public static VividTerrainBakeSettings Default => new(
            DefaultHeightSampleStride,
            DefaultChunkQuadCount,
            optimizeVertexCache: true,
            maxMeshLODLevelCount: DefaultMaxMeshLODLevelCount);
    }

    [Serializable]
    public struct VividTerrainLayerData
    {
        [SerializeField]
        private Texture2D m_DiffuseTexture;

        [SerializeField]
        private Texture2D m_NormalMapTexture;

        [SerializeField]
        private Texture2D m_MaskMapTexture;

        [SerializeField]
        private VividVirtualTextureAsset m_StreamedVirtualTexture;

        [SerializeField]
        private Vector2 m_TileSize;

        [SerializeField]
        private Vector2 m_TileOffset;

        [SerializeField]
        private Color m_Specular;

        [SerializeField]
        private float m_Metallic;

        [SerializeField]
        private float m_Smoothness;

        [SerializeField]
        private float m_NormalScale;

        public VividTerrainLayerData(
            Texture2D diffuseTexture,
            Texture2D normalMapTexture,
            Texture2D maskMapTexture,
            Vector2 tileSize,
            Vector2 tileOffset,
            Color specular,
            float metallic,
            float smoothness,
            float normalScale,
            VividVirtualTextureAsset streamedVirtualTexture = null)
        {
            m_DiffuseTexture = diffuseTexture;
            m_NormalMapTexture = normalMapTexture;
            m_MaskMapTexture = maskMapTexture;
            m_StreamedVirtualTexture = streamedVirtualTexture;
            m_TileSize = tileSize;
            m_TileOffset = tileOffset;
            m_Specular = specular;
            m_Metallic = metallic;
            m_Smoothness = smoothness;
            m_NormalScale = normalScale;
        }

        public Texture2D DiffuseTexture => m_DiffuseTexture;

        public Texture2D NormalMapTexture => m_NormalMapTexture;

        public Texture2D MaskMapTexture => m_MaskMapTexture;

        public VividVirtualTextureAsset StreamedVirtualTexture => m_StreamedVirtualTexture;

        public Vector2 TileSize => m_TileSize;

        public Vector2 TileOffset => m_TileOffset;

        public Color Specular => m_Specular;

        public float Metallic => m_Metallic;

        public float Smoothness => m_Smoothness;

        public float NormalScale => m_NormalScale;

        internal VividTerrainLayerData WithStreamedVirtualTexture(VividVirtualTextureAsset streamedVirtualTexture)
        {
            return new VividTerrainLayerData(
                m_DiffuseTexture,
                m_NormalMapTexture,
                m_MaskMapTexture,
                m_TileSize,
                m_TileOffset,
                m_Specular,
                m_Metallic,
                m_Smoothness,
                m_NormalScale,
                streamedVirtualTexture);
        }
    }

    [Serializable]
    public struct VividTerrainChunkData
    {
        [SerializeField]
        private Vector2Int m_Coordinate;

        [SerializeField]
        private Vector2Int m_HeightmapSampleMin;

        [SerializeField]
        private Vector2Int m_HeightmapSampleMax;

        [SerializeField]
        private Bounds m_LocalBounds;

        [SerializeField]
        private VividMeshletCollectionAsset m_MeshletCollection;

        public VividTerrainChunkData(
            Vector2Int coordinate,
            Vector2Int heightmapSampleMin,
            Vector2Int heightmapSampleMax,
            Bounds localBounds,
            VividMeshletCollectionAsset meshletCollection)
        {
            m_Coordinate = coordinate;
            m_HeightmapSampleMin = heightmapSampleMin;
            m_HeightmapSampleMax = heightmapSampleMax;
            m_LocalBounds = localBounds;
            m_MeshletCollection = meshletCollection;
        }

        public Vector2Int Coordinate => m_Coordinate;

        public Vector2Int HeightmapSampleMin => m_HeightmapSampleMin;

        public Vector2Int HeightmapSampleMax => m_HeightmapSampleMax;

        public Bounds LocalBounds => m_LocalBounds;

        public VividMeshletCollectionAsset MeshletCollection => m_MeshletCollection;

        public bool HasGeometry => m_MeshletCollection != null;

        public int LODCount => HasGeometry ? Mathf.Max(0, m_MeshletCollection.MeshLODLevelCount) : 0;

        public bool UsesSupportedLODCount => !HasGeometry
                                             || LODCount is >= VividTerrainData.MinimumChunkLODCount
                                                 and <= VividTerrainData.MaximumChunkLODCount;
    }

    [CreateAssetMenu(menuName = "VividRP/Terrain Data", fileName = "New Vivid Terrain Data")]
    public sealed class VividTerrainData : ScriptableObject
    {
        public const uint CurrentBakeVersion = 3u;
        public const int MaximumSurfaceLayerCount = 8;
        public const int MaximumControlMapCount = MaximumSurfaceLayerCount / 4;
        public const int MinimumChunkLODCount = VividTerrainBakeSettings.LegacyMaxMeshLODLevelCount;
        public const int MaximumChunkLODCount = VividTerrainBakeSettings.MaximumMeshLODLevelCount;

        [SerializeField, HideInInspector]
        private uint m_BakeVersion = CurrentBakeVersion;

        [SerializeField, HideInInspector]
        private string m_SourceTerrainDataGUID = string.Empty;

        [SerializeField, HideInInspector]
        private string m_SourceTerrainDataName = string.Empty;

        [SerializeField]
        private int m_SourceHeightmapResolution;

        [SerializeField]
        private Vector3 m_Size;

        [SerializeField]
        private Bounds m_LocalBounds;

        [SerializeField]
        private Vector2Int m_ChunkGridSize;

        [SerializeField]
        private VividTerrainBakeSettings m_BakeSettings = VividTerrainBakeSettings.Default;

        [SerializeField]
        private Material m_SourceMaterial;

        [SerializeField]
        private VividTerrainLayerData[] m_Layers = Array.Empty<VividTerrainLayerData>();

        [SerializeField]
        private Texture2D[] m_ControlMaps = Array.Empty<Texture2D>();

        [SerializeField]
        private VividVirtualTextureAsset[] m_ControlVirtualTextures = Array.Empty<VividVirtualTextureAsset>();

        [SerializeField]
        private VividTerrainChunkData[] m_Chunks = Array.Empty<VividTerrainChunkData>();

        public uint BakeVersion => m_BakeVersion;

        public string SourceTerrainDataGUID => m_SourceTerrainDataGUID;

        public string SourceTerrainDataName => m_SourceTerrainDataName;

        public int SourceHeightmapResolution => m_SourceHeightmapResolution;

        public Vector3 Size => m_Size;

        public Bounds LocalBounds => m_LocalBounds;

        public Vector2Int ChunkGridSize => m_ChunkGridSize;

        public VividTerrainBakeSettings BakeSettings => m_BakeSettings;

        public Material SourceMaterial => m_SourceMaterial;

        public IReadOnlyList<VividTerrainLayerData> Layers => m_Layers;

        public IReadOnlyList<Texture2D> ControlMaps => m_ControlMaps ?? Array.Empty<Texture2D>();

        public IReadOnlyList<VividVirtualTextureAsset> ControlVirtualTextures =>
            m_ControlVirtualTextures ?? Array.Empty<VividVirtualTextureAsset>();

        public int SupportedSurfaceLayerCount => Mathf.Min(Layers.Count, MaximumSurfaceLayerCount);

        public int RequiredControlMapCount => SupportedSurfaceLayerCount > 1
            ? Mathf.CeilToInt(SupportedSurfaceLayerCount / 4.0f)
            : 0;

        public bool HasCompleteControlMapData
        {
            get
            {
                if (ControlMaps.Count < RequiredControlMapCount)
                    return false;

                for (int controlMapIndex = 0; controlMapIndex < RequiredControlMapCount; controlMapIndex++)
                {
                    if (ControlMaps[controlMapIndex] == null)
                        return false;
                }

                return true;
            }
        }

        public IReadOnlyList<VividTerrainChunkData> Chunks => m_Chunks;

        public bool IsValid => TryValidate(out _);

        public int GeometryChunkCount
        {
            get
            {
                if (m_Chunks == null)
                {
                    return 0;
                }

                int count = 0;
                for (int chunkIndex = 0; chunkIndex < m_Chunks.Length; chunkIndex++)
                {
                    if (m_Chunks[chunkIndex].HasGeometry)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public Vector2Int GeometryChunkLODRange
        {
            get
            {
                int minimumLODCount = int.MaxValue;
                int maximumLODCount = 0;
                if (m_Chunks != null)
                {
                    for (int chunkIndex = 0; chunkIndex < m_Chunks.Length; chunkIndex++)
                    {
                        VividTerrainChunkData chunk = m_Chunks[chunkIndex];
                        if (!chunk.HasGeometry)
                        {
                            continue;
                        }

                        minimumLODCount = Mathf.Min(minimumLODCount, chunk.LODCount);
                        maximumLODCount = Mathf.Max(maximumLODCount, chunk.LODCount);
                    }
                }

                return maximumLODCount > 0
                    ? new Vector2Int(minimumLODCount, maximumLODCount)
                    : Vector2Int.zero;
            }
        }

        public bool TryValidate(out string reason)
        {
            if (m_BakeVersion != CurrentBakeVersion)
            {
                reason = $"Bake version {m_BakeVersion} is not supported; expected {CurrentBakeVersion}.";
                return false;
            }

            if (m_SourceHeightmapResolution < 2)
            {
                reason = "Source heightmap resolution must be at least 2.";
                return false;
            }

            if (m_ChunkGridSize.x <= 0 || m_ChunkGridSize.y <= 0)
            {
                reason = "Chunk grid dimensions must both be positive.";
                return false;
            }

            long expectedChunkCount = (long) m_ChunkGridSize.x * m_ChunkGridSize.y;
            if (m_Chunks == null || m_Chunks.LongLength != expectedChunkCount)
            {
                reason = $"Chunk grid {m_ChunkGridSize.x}x{m_ChunkGridSize.y} expects {expectedChunkCount} chunks, but the asset contains {m_Chunks?.LongLength ?? 0}.";
                return false;
            }

            int maximumHeightmapSample = m_SourceHeightmapResolution - 1;
            for (int chunkIndex = 0; chunkIndex < m_Chunks.Length; chunkIndex++)
            {
                VividTerrainChunkData chunk = m_Chunks[chunkIndex];
                var expectedCoordinate = new Vector2Int(
                    chunkIndex % m_ChunkGridSize.x,
                    chunkIndex / m_ChunkGridSize.x
                );
                if (chunk.Coordinate != expectedCoordinate)
                {
                    reason = $"Chunk {chunkIndex} has coordinate {chunk.Coordinate}; expected row-major coordinate {expectedCoordinate}.";
                    return false;
                }

                Vector2Int sampleMin = chunk.HeightmapSampleMin;
                Vector2Int sampleMax = chunk.HeightmapSampleMax;
                if (sampleMin.x < 0
                    || sampleMin.y < 0
                    || sampleMax.x <= sampleMin.x
                    || sampleMax.y <= sampleMin.y
                    || sampleMax.x > maximumHeightmapSample
                    || sampleMax.y > maximumHeightmapSample)
                {
                    reason = $"Chunk {chunk.Coordinate} has invalid heightmap sample range {sampleMin}..{sampleMax}.";
                    return false;
                }

                if (!chunk.UsesSupportedLODCount)
                {
                    reason = $"Chunk {chunk.Coordinate} contains {chunk.LODCount} mesh LOD levels; supported terrain chunks require {MinimumChunkLODCount}..{MaximumChunkLODCount}.";
                    return false;
                }

                if (chunk.HasGeometry && chunk.LODCount > m_BakeSettings.MaxMeshLODLevelCount)
                {
                    reason = $"Chunk {chunk.Coordinate} contains {chunk.LODCount} mesh LOD levels, exceeding its baked limit of {m_BakeSettings.MaxMeshLODLevelCount}.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        internal void Initialize(
            string sourceTerrainDataGUID,
            string sourceTerrainDataName,
            int sourceHeightmapResolution,
            Vector3 size,
            Bounds localBounds,
            Vector2Int chunkGridSize,
            VividTerrainBakeSettings bakeSettings,
            Material sourceMaterial,
            VividTerrainLayerData[] layers,
            VividTerrainChunkData[] chunks,
            Texture2D[] controlMaps = null)
        {
            m_BakeVersion = CurrentBakeVersion;
            m_SourceTerrainDataGUID = sourceTerrainDataGUID ?? string.Empty;
            m_SourceTerrainDataName = sourceTerrainDataName ?? string.Empty;
            m_SourceHeightmapResolution = sourceHeightmapResolution;
            m_Size = size;
            m_LocalBounds = localBounds;
            m_ChunkGridSize = chunkGridSize;
            m_BakeSettings = bakeSettings;
            m_SourceMaterial = sourceMaterial;
            m_Layers = layers ?? Array.Empty<VividTerrainLayerData>();
            m_ControlMaps = controlMaps ?? Array.Empty<Texture2D>();
            m_ControlVirtualTextures = Array.Empty<VividVirtualTextureAsset>();
            m_Chunks = chunks ?? Array.Empty<VividTerrainChunkData>();
        }

        internal void SetStreamedVirtualTextures(
            VividVirtualTextureAsset[] layerVirtualTextures,
            VividVirtualTextureAsset[] controlVirtualTextures)
        {
            if (layerVirtualTextures != null && layerVirtualTextures.Length != m_Layers.Length)
            {
                throw new ArgumentException(
                    $"Expected {m_Layers.Length} terrain layer VT assets, but received {layerVirtualTextures.Length}.",
                    nameof(layerVirtualTextures));
            }

            for (int layerIndex = 0; layerIndex < m_Layers.Length; layerIndex++)
            {
                m_Layers[layerIndex] = m_Layers[layerIndex].WithStreamedVirtualTexture(
                    layerVirtualTextures?[layerIndex]);
            }

            m_ControlVirtualTextures = controlVirtualTextures ?? Array.Empty<VividVirtualTextureAsset>();
        }

        internal void SetControlMaps(Texture2D[] controlMaps)
        {
            m_ControlMaps = controlMaps ?? Array.Empty<Texture2D>();
        }
    }
}
