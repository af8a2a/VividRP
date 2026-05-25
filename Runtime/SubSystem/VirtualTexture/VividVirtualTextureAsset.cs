using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace VividRP.Runtime
{
    public enum VividVirtualTextureCodec
    {
        RawRGBA32 = 0,
    }

    [Serializable]
    public struct VividVirtualTextureLayerDescriptor : IEquatable<VividVirtualTextureLayerDescriptor>
    {
        [SerializeField]
        private GraphicsFormat m_Format;

        [SerializeField]
        private bool m_SRGB;

        [SerializeField]
        private Color32 m_FallbackColor;

        public VividVirtualTextureLayerDescriptor(
            GraphicsFormat format,
            bool sRGB,
            Color32 fallbackColor)
        {
            m_Format = format;
            m_SRGB = sRGB;
            m_FallbackColor = fallbackColor;
        }

        public GraphicsFormat Format => m_Format;

        public bool SRGB => m_SRGB;

        public Color32 FallbackColor => m_FallbackColor;

        public bool Equals(VividVirtualTextureLayerDescriptor other)
        {
            return m_Format == other.m_Format
                   && m_SRGB == other.m_SRGB
                   && m_FallbackColor.Equals(other.m_FallbackColor);
        }

        public override bool Equals(object obj)
        {
            return obj is VividVirtualTextureLayerDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(m_Format, m_SRGB, m_FallbackColor);
        }
    }

    [Serializable]
    public struct VividVirtualTextureChunkDescriptor : IEquatable<VividVirtualTextureChunkDescriptor>
    {
        [SerializeField]
        private int m_FirstMip;

        [SerializeField]
        private int m_MipCount;

        [SerializeField]
        private int m_ByteOffset;

        [SerializeField]
        private int m_ByteSize;

        [SerializeField]
        private VividVirtualTextureCodec m_Codec;

        public VividVirtualTextureChunkDescriptor(
            int firstMip,
            int mipCount,
            int byteOffset,
            int byteSize,
            VividVirtualTextureCodec codec)
        {
            m_FirstMip = firstMip;
            m_MipCount = mipCount;
            m_ByteOffset = byteOffset;
            m_ByteSize = byteSize;
            m_Codec = codec;
        }

        public int FirstMip => m_FirstMip;

        public int MipCount => m_MipCount;

        public int ByteOffset => m_ByteOffset;

        public int ByteSize => m_ByteSize;

        public VividVirtualTextureCodec Codec => m_Codec;

        public bool ContainsMip(int mip)
        {
            return mip >= m_FirstMip && mip < m_FirstMip + m_MipCount;
        }

        public bool ContainsByteRange(int relativeOffset, int byteSize)
        {
            return relativeOffset >= 0
                   && byteSize >= 0
                   && relativeOffset <= m_ByteSize
                   && byteSize <= m_ByteSize - relativeOffset;
        }

        public bool Equals(VividVirtualTextureChunkDescriptor other)
        {
            return m_FirstMip == other.m_FirstMip
                   && m_MipCount == other.m_MipCount
                   && m_ByteOffset == other.m_ByteOffset
                   && m_ByteSize == other.m_ByteSize
                   && m_Codec == other.m_Codec;
        }

        public override bool Equals(object obj)
        {
            return obj is VividVirtualTextureChunkDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(m_FirstMip, m_MipCount, m_ByteOffset, m_ByteSize, m_Codec);
        }
    }

    [Serializable]
    public struct VividVirtualTextureTileDescriptor : IEquatable<VividVirtualTextureTileDescriptor>
    {
        [SerializeField]
        private int m_Mip;

        [SerializeField]
        private int m_X;

        [SerializeField]
        private int m_Y;

        [SerializeField]
        private int m_ChunkIndex;

        [SerializeField]
        private int m_ByteOffset;

        [SerializeField]
        private int m_ByteSize;

        public VividVirtualTextureTileDescriptor(
            int mip,
            int x,
            int y,
            int chunkIndex,
            int byteOffset,
            int byteSize)
        {
            m_Mip = mip;
            m_X = x;
            m_Y = y;
            m_ChunkIndex = chunkIndex;
            m_ByteOffset = byteOffset;
            m_ByteSize = byteSize;
        }

        public int Mip => m_Mip;

        public int X => m_X;

        public int Y => m_Y;

        public int ChunkIndex => m_ChunkIndex;

        public int ByteOffset => m_ByteOffset;

        public int ByteSize => m_ByteSize;

        public bool Equals(VividVirtualTextureTileDescriptor other)
        {
            return m_Mip == other.m_Mip
                   && m_X == other.m_X
                   && m_Y == other.m_Y
                   && m_ChunkIndex == other.m_ChunkIndex
                   && m_ByteOffset == other.m_ByteOffset
                   && m_ByteSize == other.m_ByteSize;
        }

        public override bool Equals(object obj)
        {
            return obj is VividVirtualTextureTileDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(m_Mip, m_X, m_Y, m_ChunkIndex, m_ByteOffset, m_ByteSize);
        }
    }

    internal readonly struct VividVirtualTextureTilePayload
    {
        internal VividVirtualTextureTilePayload(byte[] data, int byteOffset, int byteSize)
        {
            Data = data;
            ByteOffset = byteOffset;
            ByteSize = byteSize;
        }

        internal byte[] Data { get; }

        internal int ByteOffset { get; }

        internal int ByteSize { get; }

        internal bool IsValid => Data != null && ByteOffset >= 0 && ByteSize >= 0 && ByteOffset <= Data.Length - ByteSize;
    }

    internal readonly struct VividVirtualTextureTilePayloadLocation
    {
        internal VividVirtualTextureTilePayloadLocation(int byteOffset, int byteSize, VividVirtualTextureCodec codec)
        {
            ByteOffset = byteOffset;
            ByteSize = byteSize;
            Codec = codec;
        }

        internal int ByteOffset { get; }

        internal int ByteSize { get; }

        internal VividVirtualTextureCodec Codec { get; }

        internal bool IsValid => ByteOffset >= 0 && ByteSize >= 0;
    }

    public sealed class VividVirtualTextureAsset : ScriptableObject, VTProducer
    {
        [SerializeField]
        private VividVirtualTextureBuiltData m_BuiltData;

        public string Name => name;

        public VividVirtualTextureBuiltData BuiltData => m_BuiltData;

        public string SourceTextureGUID => m_BuiltData != null ? m_BuiltData.SourceTextureGUID : string.Empty;

        public string SourceTexturePath => m_BuiltData != null ? m_BuiltData.SourceTexturePath : string.Empty;

        public int PageSize => m_BuiltData != null ? m_BuiltData.PageSize : 0;

        public int BorderSize => m_BuiltData != null ? m_BuiltData.BorderSize : 0;

        public int MipCount => m_BuiltData != null ? m_BuiltData.MipCount : 0;

        public int VirtualPageCountX => m_BuiltData != null ? m_BuiltData.VirtualPageCountX : 0;

        public int VirtualPageCountY => m_BuiltData != null ? m_BuiltData.VirtualPageCountY : 0;

        public int ChunkCount => m_BuiltData != null ? m_BuiltData.ChunkCount : 0;

        public int TileCount => m_BuiltData != null ? m_BuiltData.TileCount : 0;

        internal void Initialize(VividVirtualTextureBuiltData builtData)
        {
            m_BuiltData = builtData;
        }

        internal VirtualTextureSpaceDesc CreateSpaceDesc(
            string spaceName,
            int cachePageCount,
            int maxUploadsPerFrame,
            int feedbackCapacity)
        {
            if (m_BuiltData == null)
                throw new InvalidOperationException($"[VividRP] Virtual texture asset '{name}' has no built data.");

            return m_BuiltData.CreateSpaceDesc(spaceName, cachePageCount, maxUploadsPerFrame, feedbackCapacity);
        }
    }
}
