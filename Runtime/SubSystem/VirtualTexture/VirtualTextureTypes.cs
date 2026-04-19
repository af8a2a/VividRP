using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public readonly struct VirtualTexturePageCoord : IEquatable<VirtualTexturePageCoord>
    {
        public VirtualTexturePageCoord(int x, int y, int mip)
        {
            X = x;
            Y = y;
            Mip = mip;
        }

        public int X { get; }

        public int Y { get; }

        public int Mip { get; }

        public bool Equals(VirtualTexturePageCoord other)
        {
            return X == other.X && Y == other.Y && Mip == other.Mip;
        }

        public override bool Equals(object obj)
        {
            return obj is VirtualTexturePageCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Mip);
        }

        public override string ToString()
        {
            return $"({X}, {Y}, mip:{Mip})";
        }
    }

    public readonly struct VirtualTextureUploadRequest : IEquatable<VirtualTextureUploadRequest>
    {
        public VirtualTextureUploadRequest(
            int spaceId,
            VirtualTexturePageCoord pageCoord,
            int physicalPageId,
            int generation,
            int priority,
            int requestFrame)
        {
            SpaceId = spaceId;
            PageCoord = pageCoord;
            PhysicalPageId = physicalPageId;
            Generation = generation;
            Priority = priority;
            RequestFrame = requestFrame;
        }

        public int SpaceId { get; }

        public VirtualTexturePageCoord PageCoord { get; }

        public int PhysicalPageId { get; }

        public int Generation { get; }

        public int Priority { get; }

        public int RequestFrame { get; }

        public bool Equals(VirtualTextureUploadRequest other)
        {
            return SpaceId == other.SpaceId
                   && PageCoord.Equals(other.PageCoord)
                   && PhysicalPageId == other.PhysicalPageId
                   && Generation == other.Generation
                   && Priority == other.Priority
                   && RequestFrame == other.RequestFrame;
        }

        public override bool Equals(object obj)
        {
            return obj is VirtualTextureUploadRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SpaceId, PageCoord, PhysicalPageId, Generation, Priority, RequestFrame);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct VirtualTexturePageTableEntry
    {
        public const int PhysicalPageIdBitCount = 20;
        public const int ResolvedMipBitCount = 6;
        public const int ResidentBitOffset = 26;
        public const int FallbackBitOffset = 27;
        public const int PendingUploadBitOffset = 28;
        public const int LockedBitOffset = 29;
        public const uint PhysicalPageIdMask = (1u << PhysicalPageIdBitCount) - 1u;
        public const uint ResolvedMipMask = (1u << ResolvedMipBitCount) - 1u;
        public const int MaxPhysicalPageCount = (int)PhysicalPageIdMask;
        public const int InvalidPhysicalPageId = MaxPhysicalPageCount;

        public VirtualTexturePageTableEntry(
            int physicalPageId,
            int resolvedMip,
            bool resident,
            bool fallback,
            bool pendingUpload,
            bool locked)
        {
            if ((uint)physicalPageId > PhysicalPageIdMask)
                throw new ArgumentOutOfRangeException(nameof(physicalPageId));
            if ((uint)resolvedMip > ResolvedMipMask)
                throw new ArgumentOutOfRangeException(nameof(resolvedMip));

            PackedValue = ((uint)physicalPageId & PhysicalPageIdMask)
                          | (((uint)resolvedMip & ResolvedMipMask) << PhysicalPageIdBitCount)
                          | (resident ? (1u << ResidentBitOffset) : 0u)
                          | (fallback ? (1u << FallbackBitOffset) : 0u)
                          | (pendingUpload ? (1u << PendingUploadBitOffset) : 0u)
                          | (locked ? (1u << LockedBitOffset) : 0u);
        }

        public uint PackedValue { get; }

        public int PhysicalPageId => (int)(PackedValue & PhysicalPageIdMask);

        public int ResolvedMip => (int)((PackedValue >> PhysicalPageIdBitCount) & ResolvedMipMask);

        public bool Resident => (PackedValue & (1u << ResidentBitOffset)) != 0u;

        public bool Fallback => (PackedValue & (1u << FallbackBitOffset)) != 0u;

        public bool PendingUpload => (PackedValue & (1u << PendingUploadBitOffset)) != 0u;

        public bool Locked => (PackedValue & (1u << LockedBitOffset)) != 0u;

        public bool IsMapped => Resident || Fallback;

        public static VirtualTexturePageTableEntry Invalid(bool pendingUpload = false, bool locked = false)
        {
            return new VirtualTexturePageTableEntry(
                InvalidPhysicalPageId,
                0,
                false,
                false,
                pendingUpload,
                locked);
        }
    }

    public readonly struct VirtualTextureSpaceShaderParams
    {
        internal const int IntCount = 12;

        public VirtualTextureSpaceShaderParams(
            int spaceId,
            VirtualTextureSpaceDesc desc,
            int pageTableEntryCount)
        {
            SpaceId = spaceId;
            PageSize = desc.PageSize;
            BorderSize = desc.BorderSize;
            PhysicalPageSize = desc.PhysicalPageSize;
            VirtualPageCountX = desc.VirtualPageCountX;
            VirtualPageCountY = desc.VirtualPageCountY;
            MipCount = desc.MipCount;
            CachePageCount = desc.CachePageCount;
            FeedbackCapacity = desc.FeedbackCapacity;
            PageTableEntryCount = pageTableEntryCount;
            PhysicalPageWidth = desc.PhysicalPageSize;
            PhysicalPageHeight = desc.PhysicalPageSize;
        }

        public int SpaceId { get; }

        public int PageSize { get; }

        public int BorderSize { get; }

        public int PhysicalPageSize { get; }

        public int VirtualPageCountX { get; }

        public int VirtualPageCountY { get; }

        public int MipCount { get; }

        public int CachePageCount { get; }

        public int FeedbackCapacity { get; }

        public int PageTableEntryCount { get; }

        public int PhysicalPageWidth { get; }

        public int PhysicalPageHeight { get; }

        public int[] ToIntArray()
        {
            return new[]
            {
                SpaceId,
                PageSize,
                BorderSize,
                PhysicalPageSize,
                VirtualPageCountX,
                VirtualPageCountY,
                MipCount,
                CachePageCount,
                FeedbackCapacity,
                PageTableEntryCount,
                PhysicalPageWidth,
                PhysicalPageHeight,
            };
        }
    }

    internal readonly struct VirtualTextureSpaceBinding
    {
        internal VirtualTextureSpaceBinding(
            int spaceId,
            string spaceName,
            GraphicsBuffer pageTableBuffer,
            Texture2DArray physicalCache,
            GraphicsBuffer feedbackRequests,
            GraphicsBuffer feedbackCounter,
            VirtualTextureSpaceShaderParams shaderParams,
            int[] mipOffsets)
        {
            SpaceId = spaceId;
            SpaceName = spaceName;
            PageTableBuffer = pageTableBuffer;
            PhysicalCache = physicalCache;
            FeedbackRequests = feedbackRequests;
            FeedbackCounter = feedbackCounter;
            ShaderParams = shaderParams;
            MipOffsets = mipOffsets;
        }

        public int SpaceId { get; }

        public string SpaceName { get; }

        public GraphicsBuffer PageTableBuffer { get; }

        public Texture2DArray PhysicalCache { get; }

        public GraphicsBuffer FeedbackRequests { get; }

        public GraphicsBuffer FeedbackCounter { get; }

        public VirtualTextureSpaceShaderParams ShaderParams { get; }

        public int[] MipOffsets { get; }

        public bool HasFeedback => FeedbackRequests != null && FeedbackCounter != null;
    }

    internal readonly struct VirtualTextureStats
    {
        internal VirtualTextureStats(
            int activeSpaceCount,
            int residentPageCount,
            int freePageCount,
            int pendingUploadCount,
            int evictionCount,
            int faultCount,
            int deduplicatedRequestCount,
            int lastReadbackFrame,
            string statusMessage)
        {
            ActiveSpaceCount = activeSpaceCount;
            ResidentPageCount = residentPageCount;
            FreePageCount = freePageCount;
            PendingUploadCount = pendingUploadCount;
            EvictionCount = evictionCount;
            FaultCount = faultCount;
            DeduplicatedRequestCount = deduplicatedRequestCount;
            LastReadbackFrame = lastReadbackFrame;
            StatusMessage = statusMessage;
        }

        internal int ActiveSpaceCount { get; }

        internal int ResidentPageCount { get; }

        internal int FreePageCount { get; }

        internal int PendingUploadCount { get; }

        internal int EvictionCount { get; }

        internal int FaultCount { get; }

        internal int DeduplicatedRequestCount { get; }

        internal int LastReadbackFrame { get; }

        internal string StatusMessage { get; }
    }

    internal static class VirtualTextureStatsRegistry
    {
        private static VirtualTextureStats s_LastStats;

        internal static VirtualTextureStats LastStats => s_LastStats;

        internal static void Report(VirtualTextureStats stats)
        {
            s_LastStats = stats;
        }

        internal static void Clear()
        {
            s_LastStats = default;
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal static class VirtualTextureShaderIDs
    {
        public static readonly int _VTPageTable = Shader.PropertyToID(nameof(_VTPageTable));
        public static readonly int _VTPhysicalCache = Shader.PropertyToID(nameof(_VTPhysicalCache));
        public static readonly int _VTFeedbackRequests = Shader.PropertyToID(nameof(_VTFeedbackRequests));
        public static readonly int _VTFeedbackCounter = Shader.PropertyToID(nameof(_VTFeedbackCounter));
        public static readonly int _VTSpaceParams = Shader.PropertyToID(nameof(_VTSpaceParams));
        public static readonly int _VTMipOffsets = Shader.PropertyToID(nameof(_VTMipOffsets));
    }

    internal static class VirtualTextureSpaceUtility
    {
        internal static int[] BuildMipOffsets(int virtualPageCountX, int virtualPageCountY, int mipCount)
        {
            var offsets = new int[mipCount];
            int runningOffset = 0;
            for (int mip = 0; mip < mipCount; mip++)
            {
                offsets[mip] = runningOffset;
                runningOffset += GetPageCountX(virtualPageCountX, mip) * GetPageCountY(virtualPageCountY, mip);
            }

            return offsets;
        }

        internal static int GetTotalPageCount(int virtualPageCountX, int virtualPageCountY, int mipCount)
        {
            int total = 0;
            for (int mip = 0; mip < mipCount; mip++)
                total += GetPageCountX(virtualPageCountX, mip) * GetPageCountY(virtualPageCountY, mip);

            return total;
        }

        internal static int GetPageCountX(int virtualPageCountX, int mip)
        {
            return Mathf.Max(1, virtualPageCountX >> mip);
        }

        internal static int GetPageCountY(int virtualPageCountY, int mip)
        {
            return Mathf.Max(1, virtualPageCountY >> mip);
        }

        internal static bool IsCoordValid(in VirtualTextureSpaceDesc desc, in VirtualTexturePageCoord coord)
        {
            if (coord.Mip < 0 || coord.Mip >= desc.MipCount)
                return false;

            return coord.X >= 0
                   && coord.Y >= 0
                   && coord.X < GetPageCountX(desc.VirtualPageCountX, coord.Mip)
                   && coord.Y < GetPageCountY(desc.VirtualPageCountY, coord.Mip);
        }

        internal static int GetFlatIndex(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            in VirtualTexturePageCoord coord)
        {
            if (!IsCoordValid(desc, coord))
                throw new ArgumentOutOfRangeException(nameof(coord));

            int mipWidth = GetPageCountX(desc.VirtualPageCountX, coord.Mip);
            return mipOffsets[coord.Mip] + coord.Y * mipWidth + coord.X;
        }
    }
}
