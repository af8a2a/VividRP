using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace VividRP.Runtime
{
    public enum VividVirtualTextureBuildProfile
    {
        Generic = 0,
        GPUDrivenSurface = 1,
    }

    public enum VividVirtualTextureAddressMode
    {
        Repeat = 0,
        Clamp = 1,
    }

    public enum VividVirtualTextureCodec
    {
        RawRGBA32 = 0,
    }

    public enum VividVirtualTextureStorageProfile
    {
        LegacyRGBA32 = 0,
        DesktopBCn = 1,
    }

    public enum VividVirtualTextureStreamCompression
    {
        None = 0,
        Zstd = 1,
        GDeflate = 2,
    }

    public enum VividVirtualTextureMaskStorage
    {
        PackedRGBA = 0,
        SingleChannelR = 1,
    }

    public enum VividVirtualTextureIOBackendMode
    {
        Auto = 0,
        AsyncReadManager = 1,
        DirectStorage = 2,
    }

    public enum VividVirtualTextureBCQuality
    {
        Fast = 0,
        Normal = 1,
        High = 2,
    }

    public enum VTLayerDataEncoding
    {
        RGBA = 0,
        LegacyNormalAG = 1,
        NormalRG = 2,
        SingleChannelR = 3,
    }

    [Flags]
    public enum VividVirtualTextureChunkFlags
    {
        None = 0,
        MipTail = 1 << 0,
        LegacySynthetic = 1 << 1,
    }

    [Serializable]
    public struct VividVirtualTextureLayerDescriptor : IEquatable<VividVirtualTextureLayerDescriptor>
    {
        [SerializeField]
        private VTLayerSemantic m_Semantic;

        [SerializeField]
        private GraphicsFormat m_Format;

        [SerializeField]
        private bool m_SRGB;

        [SerializeField]
        private Color32 m_FallbackColor;

        [SerializeField]
        private int m_PhysicalGroup;

        [SerializeField]
        private VTLayerDataEncoding m_Encoding;

        [SerializeField]
        private bool m_HasExplicitEncoding;

        public VividVirtualTextureLayerDescriptor(
            GraphicsFormat format,
            bool sRGB,
            Color32 fallbackColor)
            : this(VTLayerSemantic.BaseColor, format, sRGB, fallbackColor, 0, VTLayerDataEncoding.RGBA)
        {
        }

        public VividVirtualTextureLayerDescriptor(
            VTLayerSemantic semantic,
            GraphicsFormat format,
            bool sRGB,
            Color32 fallbackColor,
            int physicalGroup = 0,
            VTLayerDataEncoding encoding = VTLayerDataEncoding.RGBA)
        {
            m_Semantic = semantic;
            m_Format = format;
            m_SRGB = sRGB;
            m_FallbackColor = fallbackColor;
            m_PhysicalGroup = Mathf.Max(0, physicalGroup);
            m_Encoding = encoding;
            m_HasExplicitEncoding = true;
        }

        public VTLayerSemantic Semantic => m_Semantic;

        public GraphicsFormat Format => m_Format;

        public bool SRGB => m_SRGB;

        public Color32 FallbackColor => m_FallbackColor;

        public int PhysicalGroup => m_PhysicalGroup;

        public VTLayerDataEncoding Encoding => m_HasExplicitEncoding
            ? m_Encoding
            : m_Semantic == VTLayerSemantic.Normal
                ? VTLayerDataEncoding.LegacyNormalAG
                : VTLayerDataEncoding.RGBA;

        public bool Equals(VividVirtualTextureLayerDescriptor other)
        {
            return m_Semantic == other.m_Semantic
                   && m_Format == other.m_Format
                   && m_SRGB == other.m_SRGB
                   && m_FallbackColor.Equals(other.m_FallbackColor)
                   && m_PhysicalGroup == other.m_PhysicalGroup
                   && Encoding == other.Encoding;
        }

        public override bool Equals(object obj)
        {
            return obj is VividVirtualTextureLayerDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(m_Semantic, m_Format, m_SRGB, m_FallbackColor, m_PhysicalGroup, Encoding);
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

        [SerializeField]
        private long m_FileOffset;

        [SerializeField]
        private int m_StoredByteSize;

        [SerializeField]
        private int m_DecodedByteSize;

        [SerializeField]
        private VividVirtualTextureStreamCompression m_Compression;

        [SerializeField]
        private uint m_DecodedPayloadCRC;

        [SerializeField]
        private int m_FirstTile;

        [SerializeField]
        private int m_TileCount;

        [SerializeField]
        private VividVirtualTextureChunkFlags m_Flags;

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
            m_FileOffset = 0;
            m_StoredByteSize = 0;
            m_DecodedByteSize = 0;
            m_Compression = VividVirtualTextureStreamCompression.None;
            m_DecodedPayloadCRC = 0;
            m_FirstTile = 0;
            m_TileCount = 0;
            m_Flags = VividVirtualTextureChunkFlags.None;
        }

        public VividVirtualTextureChunkDescriptor(
            int firstMip,
            int mipCount,
            int firstTile,
            int tileCount,
            long fileOffset,
            int storedByteSize,
            int decodedByteSize,
            VividVirtualTextureStreamCompression compression,
            uint decodedPayloadCRC,
            VividVirtualTextureChunkFlags flags = VividVirtualTextureChunkFlags.None)
        {
            if (fileOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(fileOffset));
            if (storedByteSize < 0)
                throw new ArgumentOutOfRangeException(nameof(storedByteSize));
            if (decodedByteSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(decodedByteSize));

            m_FirstMip = firstMip;
            m_MipCount = mipCount;
            m_ByteOffset = fileOffset <= int.MaxValue ? (int)fileOffset : 0;
            m_ByteSize = storedByteSize;
            m_Codec = VividVirtualTextureCodec.RawRGBA32;
            m_FileOffset = fileOffset;
            m_StoredByteSize = storedByteSize;
            m_DecodedByteSize = decodedByteSize;
            m_Compression = compression;
            m_DecodedPayloadCRC = decodedPayloadCRC;
            m_FirstTile = firstTile;
            m_TileCount = tileCount;
            m_Flags = flags;
        }

        public int FirstMip => m_FirstMip;

        public int MipCount => m_MipCount;

        public int ByteOffset => m_ByteOffset;

        public int ByteSize => m_ByteSize;

        public VividVirtualTextureCodec Codec => m_Codec;

        public bool UsesContainerSchemaV2 => m_DecodedByteSize > 0;

        public long FileOffset => UsesContainerSchemaV2 ? m_FileOffset : m_ByteOffset;

        public int StoredByteSize => UsesContainerSchemaV2 ? m_StoredByteSize : m_ByteSize;

        public int DecodedByteSize => UsesContainerSchemaV2 ? m_DecodedByteSize : m_ByteSize;

        public VividVirtualTextureStreamCompression Compression => UsesContainerSchemaV2
            ? m_Compression
            : VividVirtualTextureStreamCompression.None;

        public uint DecodedPayloadCRC => m_DecodedPayloadCRC;

        public int FirstTile => m_FirstTile;

        public int TileCount => m_TileCount;

        public VividVirtualTextureChunkFlags Flags => m_Flags;

        public bool ContainsMip(int mip)
        {
            return mip >= m_FirstMip && mip < m_FirstMip + m_MipCount;
        }

        public bool ContainsByteRange(int relativeOffset, int byteSize)
        {
            return relativeOffset >= 0
                   && byteSize >= 0
                   && relativeOffset <= DecodedByteSize
                   && byteSize <= DecodedByteSize - relativeOffset;
        }

        public bool Equals(VividVirtualTextureChunkDescriptor other)
        {
            return m_FirstMip == other.m_FirstMip
                   && m_MipCount == other.m_MipCount
                   && m_ByteOffset == other.m_ByteOffset
                   && m_ByteSize == other.m_ByteSize
                   && m_Codec == other.m_Codec
                   && m_FileOffset == other.m_FileOffset
                   && m_StoredByteSize == other.m_StoredByteSize
                   && m_DecodedByteSize == other.m_DecodedByteSize
                   && m_Compression == other.m_Compression
                   && m_DecodedPayloadCRC == other.m_DecodedPayloadCRC
                   && m_FirstTile == other.m_FirstTile
                   && m_TileCount == other.m_TileCount
                   && m_Flags == other.m_Flags;
        }

        public override bool Equals(object obj)
        {
            return obj is VividVirtualTextureChunkDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(m_FirstMip);
            hash.Add(m_MipCount);
            hash.Add(m_ByteOffset);
            hash.Add(m_ByteSize);
            hash.Add(m_Codec);
            hash.Add(m_FileOffset);
            hash.Add(m_StoredByteSize);
            hash.Add(m_DecodedByteSize);
            hash.Add(m_Compression);
            hash.Add(m_DecodedPayloadCRC);
            hash.Add(m_FirstTile);
            hash.Add(m_TileCount);
            hash.Add(m_Flags);
            return hash.ToHashCode();
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
        internal VividVirtualTextureTilePayloadLocation(
            int chunkIndex,
            long fileOffset,
            int storedByteSize,
            int decodedByteSize,
            int tileByteOffset,
            int tileByteSize,
            VividVirtualTextureStreamCompression compression,
            uint decodedPayloadCRC,
            VividVirtualTextureChunkFlags flags)
        {
            ChunkIndex = chunkIndex;
            FileOffset = fileOffset;
            StoredByteSize = storedByteSize;
            DecodedByteSize = decodedByteSize;
            TileByteOffset = tileByteOffset;
            TileByteSize = tileByteSize;
            Compression = compression;
            DecodedPayloadCRC = decodedPayloadCRC;
            Flags = flags;
        }

        internal int ChunkIndex { get; }

        internal long FileOffset { get; }

        internal int StoredByteSize { get; }

        internal int DecodedByteSize { get; }

        internal int TileByteOffset { get; }

        internal int TileByteSize { get; }

        internal VividVirtualTextureStreamCompression Compression { get; }

        internal uint DecodedPayloadCRC { get; }

        internal VividVirtualTextureChunkFlags Flags { get; }

        internal int ByteOffset => checked((int)FileOffset + TileByteOffset);

        internal int ByteSize => TileByteSize;

        internal VividVirtualTextureCodec Codec => VividVirtualTextureCodec.RawRGBA32;

        internal bool IsValid => ChunkIndex >= 0
                                 && FileOffset >= 0
                                 && StoredByteSize >= 0
                                 && DecodedByteSize >= 0
                                 && TileByteOffset >= 0
                                 && TileByteSize >= 0
                                 && TileByteOffset <= DecodedByteSize - TileByteSize;
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

        public VividVirtualTextureBuildProfile BuildProfile => m_BuiltData != null
            ? m_BuiltData.BuildProfile
            : VividVirtualTextureBuildProfile.Generic;

        public int ContentLayerMask => m_BuiltData != null ? m_BuiltData.ContentLayerMask : 0;

        public uint ContentVersion => m_BuiltData != null ? m_BuiltData.ContentVersion : 0u;

        public int ContainerSchemaVersion => m_BuiltData != null ? m_BuiltData.ContainerSchemaVersion : 0;

        public VividVirtualTextureStorageProfile StorageProfile => m_BuiltData != null
            ? m_BuiltData.StorageProfile
            : VividVirtualTextureStorageProfile.LegacyRGBA32;

        public VividVirtualTextureMaskStorage MaskStorage => m_BuiltData != null
            ? m_BuiltData.MaskStorage
            : VividVirtualTextureMaskStorage.PackedRGBA;

        public VividVirtualTextureAddressMode AddressMode => m_BuiltData != null
            ? m_BuiltData.AddressMode
            : VividVirtualTextureAddressMode.Repeat;

        internal void Initialize(VividVirtualTextureBuiltData builtData)
        {
            m_BuiltData = builtData;
        }

        internal VirtualTextureSpaceDesc CreateSpaceDesc(
            string spaceName,
            int cachePageCount,
            int maxUploadsPerFrame,
            int feedbackCapacity,
            int neighborPrefetchCount = 0)
        {
            if (m_BuiltData == null)
                throw new InvalidOperationException($"[VividRP] Virtual texture asset '{name}' has no built data.");

            return m_BuiltData.CreateSpaceDesc(
                spaceName,
                cachePageCount,
                maxUploadsPerFrame,
                feedbackCapacity,
                neighborPrefetchCount);
        }
    }
}
