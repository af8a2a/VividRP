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

        [SerializeField, Min(1)]
        private int m_HeightSampleStride;

        [SerializeField, Range(1, 256)]
        private int m_ChunkQuadCount;

        [SerializeField]
        private bool m_OptimizeVertexCache;

        public VividTerrainBakeSettings(
            int heightSampleStride,
            int chunkQuadCount,
            bool optimizeVertexCache = true)
        {
            m_HeightSampleStride = Mathf.Max(1, heightSampleStride);
            m_ChunkQuadCount = Mathf.Clamp(chunkQuadCount, 1, 256);
            m_OptimizeVertexCache = optimizeVertexCache;
        }

        public int HeightSampleStride => Mathf.Max(1, m_HeightSampleStride);

        public int ChunkQuadCount => Mathf.Clamp(m_ChunkQuadCount, 1, 256);

        public bool OptimizeVertexCache => m_OptimizeVertexCache;

        public static VividTerrainBakeSettings Default => new(
            DefaultHeightSampleStride,
            DefaultChunkQuadCount,
            optimizeVertexCache: true);
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
            float normalScale)
        {
            m_DiffuseTexture = diffuseTexture;
            m_NormalMapTexture = normalMapTexture;
            m_MaskMapTexture = maskMapTexture;
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

        public Vector2 TileSize => m_TileSize;

        public Vector2 TileOffset => m_TileOffset;

        public Color Specular => m_Specular;

        public float Metallic => m_Metallic;

        public float Smoothness => m_Smoothness;

        public float NormalScale => m_NormalScale;
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
    }

    [CreateAssetMenu(menuName = "VividRP/Terrain Data", fileName = "New Vivid Terrain Data")]
    public sealed class VividTerrainData : ScriptableObject
    {
        public const uint CurrentBakeVersion = 1u;

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

        public IReadOnlyList<VividTerrainChunkData> Chunks => m_Chunks;

        public bool IsValid => m_BakeVersion == CurrentBakeVersion
                               && m_SourceHeightmapResolution >= 2
                               && m_ChunkGridSize.x > 0
                               && m_ChunkGridSize.y > 0
                               && m_Chunks is { Length: > 0 };

        public int GeometryChunkCount
        {
            get
            {
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
            VividTerrainChunkData[] chunks)
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
            m_Chunks = chunks ?? Array.Empty<VividTerrainChunkData>();
        }
    }
}
