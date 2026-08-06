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
        private string m_StreamDataPath = string.Empty;

        [SerializeField]
        private int m_StreamDataByteSize;

        [SerializeField]
        private byte[] m_RawData = Array.Empty<byte>();

        [SerializeField]
        private VividVirtualTextureBuildProfile m_BuildProfile;

        [SerializeField]
        private int m_ContentLayerMask = 1;

        [SerializeField]
        private uint m_ContentVersion = 1;

        [SerializeField]
        private VividVirtualTextureAddressMode m_AddressMode;

        [SerializeField]
        private string m_RuntimeStreamDataPath = string.Empty;

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

        public int RawDataByteSize => HasInlineRawData ? m_RawData.Length : m_StreamDataByteSize;

        public string StreamDataPath => m_StreamDataPath;

        public int StreamDataByteSize => m_StreamDataByteSize;

        public bool HasInlineRawData => m_RawData != null && m_RawData.Length > 0;

        public bool HasStreamData => !string.IsNullOrWhiteSpace(m_StreamDataPath) && m_StreamDataByteSize > 0;

        public VividVirtualTextureBuildProfile BuildProfile => m_BuildProfile;

        public int ContentLayerMask => m_ContentLayerMask;

        public uint ContentVersion => m_ContentVersion;

        public VividVirtualTextureAddressMode AddressMode => m_AddressMode;

        public string RuntimeStreamDataPath => m_RuntimeStreamDataPath;

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
            byte[] rawData,
            string streamDataPath = null,
            int streamDataByteSize = 0,
            VividVirtualTextureBuildProfile buildProfile = VividVirtualTextureBuildProfile.Generic,
            int contentLayerMask = 1,
            uint contentVersion = 1,
            VividVirtualTextureAddressMode addressMode = VividVirtualTextureAddressMode.Clamp,
            string runtimeStreamDataPath = null)
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
            m_StreamDataPath = streamDataPath ?? string.Empty;
            m_StreamDataByteSize = Mathf.Max(0, streamDataByteSize);
            m_BuildProfile = buildProfile;
            m_ContentLayerMask = Mathf.Max(0, contentLayerMask);
            m_ContentVersion = contentVersion != 0 ? contentVersion : 1u;
            m_AddressMode = addressMode;
            m_RuntimeStreamDataPath = runtimeStreamDataPath ?? string.Empty;
        }

        internal VirtualTextureSpaceDesc CreateSpaceDesc(
            string spaceName,
            int cachePageCount,
            int maxUploadsPerFrame,
            int feedbackCapacity,
            int neighborPrefetchCount = 0)
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
                    CreateStackLayers(),
                    maxUploadsPerFrame,
                    feedbackCapacity,
                    neighborPrefetchCount));
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
            if (!HasInlineRawData
                || !TryGetTilePayloadLocation(coord, out VividVirtualTextureTilePayloadLocation location))
            {
                return false;
            }

            payload = new VividVirtualTextureTilePayload(m_RawData, location.ByteOffset, location.ByteSize);
            return payload.IsValid;
        }

        internal bool TryGetTilePayloadLocation(
            in VirtualTexturePageCoord coord,
            out VividVirtualTextureTilePayloadLocation location)
        {
            location = default;
            if (!TryGetTileDescriptor(coord, out VividVirtualTextureTileDescriptor tile))
            {
                return false;
            }

            if (m_Chunks == null || tile.ChunkIndex < 0 || tile.ChunkIndex >= m_Chunks.Length)
                return false;

            int dataByteSize = RawDataByteSize;
            VividVirtualTextureChunkDescriptor chunk = m_Chunks[tile.ChunkIndex];
            if (chunk.Codec != VividVirtualTextureCodec.RawRGBA32
                || !chunk.ContainsMip(tile.Mip)
                || !chunk.ContainsByteRange(tile.ByteOffset, tile.ByteSize))
            {
                return false;
            }

            int absoluteOffset = chunk.ByteOffset + tile.ByteOffset;
            if (absoluteOffset < 0 || tile.ByteSize < 0 || absoluteOffset > dataByteSize - tile.ByteSize)
                return false;

            location = new VividVirtualTextureTilePayloadLocation(absoluteOffset, tile.ByteSize, chunk.Codec);
            return true;
        }

        internal bool Matches(in VirtualTextureSpaceDesc desc)
        {
            return desc.PageSize == m_PageSize
                   && desc.BorderSize == m_BorderSize
                   && desc.VirtualPageCountX == m_VirtualPageCountX
                   && desc.VirtualPageCountY == m_VirtualPageCountY
                   && desc.MipCount == m_MipCount
                   && MatchesStack(desc.StackDesc);
        }

        private VTLayerDesc[] CreateStackLayers()
        {
            if (m_Layers == null || m_Layers.Length == 0)
            {
                return new[]
                {
                    new VTLayerDesc(
                        VTLayerSemantic.BaseColor,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        false,
                        new Color32(0, 0, 0, 255)),
                };
            }

            var layers = new VTLayerDesc[m_Layers.Length];
            for (int layerIndex = 0; layerIndex < m_Layers.Length; layerIndex++)
            {
                VividVirtualTextureLayerDescriptor layer = m_Layers[layerIndex];
                layers[layerIndex] = new VTLayerDesc(
                    layer.Semantic,
                    layer.Format,
                    layer.SRGB,
                    layer.FallbackColor,
                    layer.PhysicalGroup);
            }

            return layers;
        }

        internal bool MatchesStack(in VTStackDesc stackDesc)
        {
            if (stackDesc.LayerCount != Mathf.Max(1, LayerCount))
                return false;

            if (m_Layers == null || m_Layers.Length == 0)
                return stackDesc.GraphicsFormat == GraphicsFormat.R8G8B8A8_UNorm;

            for (int layerIndex = 0; layerIndex < m_Layers.Length; layerIndex++)
            {
                VTLayerDesc stackLayer = stackDesc.GetLayer(layerIndex);
                VividVirtualTextureLayerDescriptor builtLayer = m_Layers[layerIndex];
                if (stackLayer.Semantic != builtLayer.Semantic
                    || stackLayer.GraphicsFormat != builtLayer.Format
                    || stackLayer.SRGB != builtLayer.SRGB
                    || !stackLayer.FallbackColor.Equals(builtLayer.FallbackColor)
                    || stackLayer.PhysicalGroup != builtLayer.PhysicalGroup)
                {
                    return false;
                }
            }

            return true;
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
