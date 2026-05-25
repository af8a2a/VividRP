using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace VividRP.Runtime
{
    public sealed class VividVirtualTextureBuiltData : ScriptableObject
    {
        [SerializeField]
        private string m_SourceTextureGUID = string.Empty;

        [SerializeField]
        private string m_SourceTexturePath = string.Empty;

        [SerializeField]
        private int m_PageSize = 128;

        [SerializeField]
        private int m_BorderSize = 4;

        [SerializeField]
        private int m_VirtualPageCountX = 1;

        [SerializeField]
        private int m_VirtualPageCountY = 1;

        [SerializeField]
        private int m_MipCount = 1;

        [SerializeField]
        private VividVirtualTextureLayerDescriptor[] m_Layers = Array.Empty<VividVirtualTextureLayerDescriptor>();

        [SerializeField]
        private VividVirtualTextureChunkDescriptor[] m_Chunks = Array.Empty<VividVirtualTextureChunkDescriptor>();

        [SerializeField]
        private VividVirtualTextureTileDescriptor[] m_Tiles = Array.Empty<VividVirtualTextureTileDescriptor>();

        [SerializeField]
        private int[] m_MipTileOffsets = Array.Empty<int>();

        [SerializeField]
        private byte[] m_RawData = Array.Empty<byte>();

        public string SourceTextureGUID => m_SourceTextureGUID;

        public string SourceTexturePath => m_SourceTexturePath;

        public int PageSize => m_PageSize;

        public int BorderSize => m_BorderSize;

        public int PhysicalPageSize => m_PageSize + m_BorderSize * 2;

        public int VirtualPageCountX => m_VirtualPageCountX;

        public int VirtualPageCountY => m_VirtualPageCountY;

        public int MipCount => m_MipCount;

        public int LayerCount => m_Layers?.Length ?? 0;

        public int ChunkCount => m_Chunks?.Length ?? 0;

        public int TileCount => m_Tiles?.Length ?? 0;

        public int RawDataByteSize => m_RawData?.Length ?? 0;

        public IReadOnlyList<VividVirtualTextureLayerDescriptor> Layers => m_Layers;

        public IReadOnlyList<VividVirtualTextureChunkDescriptor> Chunks => m_Chunks;

        public IReadOnlyList<VividVirtualTextureTileDescriptor> Tiles => m_Tiles;

        public Color32 FallbackColor => LayerCount > 0 ? m_Layers[0].FallbackColor : new Color32(0, 0, 0, 255);

        public GraphicsFormat GraphicsFormat => LayerCount > 0 ? m_Layers[0].Format : GraphicsFormat.R8G8B8A8_UNorm;

        internal void Initialize(
            string sourceTextureGUID,
            string sourceTexturePath,
            int pageSize,
            int borderSize,
            int virtualPageCountX,
            int virtualPageCountY,
            int mipCount,
            VividVirtualTextureLayerDescriptor[] layers,
            VividVirtualTextureChunkDescriptor[] chunks,
            VividVirtualTextureTileDescriptor[] tiles,
            int[] mipTileOffsets,
            byte[] rawData)
        {
            m_SourceTextureGUID = sourceTextureGUID ?? string.Empty;
            m_SourceTexturePath = sourceTexturePath ?? string.Empty;
            m_PageSize = pageSize;
            m_BorderSize = borderSize;
            m_VirtualPageCountX = virtualPageCountX;
            m_VirtualPageCountY = virtualPageCountY;
            m_MipCount = mipCount;
            m_Layers = layers ?? Array.Empty<VividVirtualTextureLayerDescriptor>();
            m_Chunks = chunks ?? Array.Empty<VividVirtualTextureChunkDescriptor>();
            m_Tiles = tiles ?? Array.Empty<VividVirtualTextureTileDescriptor>();
            m_MipTileOffsets = mipTileOffsets ?? Array.Empty<int>();
            m_RawData = rawData ?? Array.Empty<byte>();
        }

        internal VirtualTextureSpaceDesc CreateSpaceDesc(
            string spaceName,
            int cachePageCount,
            int maxUploadsPerFrame,
            int feedbackCapacity)
        {
            return new VirtualTextureSpaceDesc(
                string.IsNullOrWhiteSpace(spaceName) ? name : spaceName,
                m_VirtualPageCountX,
                m_VirtualPageCountY,
                m_MipCount,
                new VTStackDesc(
                    m_PageSize,
                    m_BorderSize,
                    cachePageCount,
                    GraphicsFormat,
                    maxUploadsPerFrame,
                    feedbackCapacity));
        }

        internal bool TryGetTileDescriptor(
            in VirtualTexturePageCoord coord,
            out VividVirtualTextureTileDescriptor tile)
        {
            tile = default;
            if (!IsCoordValid(coord) || m_Tiles == null || m_MipTileOffsets == null || coord.Mip >= m_MipTileOffsets.Length)
                return false;

            int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(m_VirtualPageCountX, coord.Mip);
            int tileIndex = m_MipTileOffsets[coord.Mip] + coord.Y * pageCountX + coord.X;
            if (tileIndex < 0 || tileIndex >= m_Tiles.Length)
                return false;

            tile = m_Tiles[tileIndex];
            return tile.Mip == coord.Mip && tile.X == coord.X && tile.Y == coord.Y;
        }

        internal bool TryGetTilePayload(
            in VirtualTexturePageCoord coord,
            out VividVirtualTextureTilePayload payload)
        {
            payload = default;
            if (!TryGetTileDescriptor(coord, out VividVirtualTextureTileDescriptor tile))
                return false;

            if (m_Chunks == null
                || tile.ChunkIndex < 0
                || tile.ChunkIndex >= m_Chunks.Length
                || m_RawData == null)
            {
                return false;
            }

            VividVirtualTextureChunkDescriptor chunk = m_Chunks[tile.ChunkIndex];
            if (chunk.Codec != VividVirtualTextureCodec.RawRGBA32
                || !chunk.ContainsMip(tile.Mip)
                || !chunk.ContainsByteRange(tile.ByteOffset, tile.ByteSize))
            {
                return false;
            }

            int absoluteOffset = chunk.ByteOffset + tile.ByteOffset;
            payload = new VividVirtualTextureTilePayload(m_RawData, absoluteOffset, tile.ByteSize);
            return payload.IsValid;
        }

        internal bool Matches(in VirtualTextureSpaceDesc desc)
        {
            return desc.PageSize == m_PageSize
                   && desc.BorderSize == m_BorderSize
                   && desc.VirtualPageCountX == m_VirtualPageCountX
                   && desc.VirtualPageCountY == m_VirtualPageCountY
                   && desc.MipCount == m_MipCount
                   && desc.GraphicsFormat == GraphicsFormat;
        }

        private bool IsCoordValid(in VirtualTexturePageCoord coord)
        {
            if (coord.Mip < 0 || coord.Mip >= m_MipCount)
                return false;

            return coord.X >= 0
                   && coord.Y >= 0
                   && coord.X < VirtualTextureSpaceUtility.GetPageCountX(m_VirtualPageCountX, coord.Mip)
                   && coord.Y < VirtualTextureSpaceUtility.GetPageCountY(m_VirtualPageCountY, coord.Mip);
        }
    }
}
