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

    internal readonly struct VirtualTextureViewId : IEquatable<VirtualTextureViewId>
    {
        internal static readonly VirtualTextureViewId Invalid = new(EntityId.None, default, false);

        internal VirtualTextureViewId(EntityId cameraId, CameraType cameraType)
            : this(cameraId, cameraType, false)
        {
        }

        private VirtualTextureViewId(EntityId cameraId, CameraType cameraType, bool isCameraTypeOnly)
        {
            CameraId = cameraId;
            CameraType = cameraType;
            m_IsCameraTypeOnly = isCameraTypeOnly && SupportsCameraTypeOnly(cameraType);
        }

        private readonly bool m_IsCameraTypeOnly;

        internal EntityId CameraId { get; }

        internal CameraType CameraType { get; }

        internal bool IsValid => !CameraId.Equals(EntityId.None);

        internal bool IsCameraTypeOnly => !IsValid && m_IsCameraTypeOnly;

        internal static VirtualTextureViewId FromCamera(Camera camera)
        {
            return camera != null
                ? new VirtualTextureViewId(camera.GetEntityId(), camera.cameraType)
                : Invalid;
        }

        internal static VirtualTextureViewId FromCameraData(VividCameraData cameraData)
        {
            Camera camera = cameraData?.camera;
            if (camera == null)
                return Invalid;

            EntityId cameraId = cameraData.cameraEntityId;
            if (cameraId.Equals(EntityId.None))
                cameraId = camera.GetEntityId();

            return new VirtualTextureViewId(cameraId, camera.cameraType);
        }

        internal static VirtualTextureViewId FromCameraType(CameraType cameraType)
        {
            return new VirtualTextureViewId(EntityId.None, cameraType, true);
        }

        public bool Equals(VirtualTextureViewId other)
        {
            return CameraId.Equals(other.CameraId)
                   && CameraType == other.CameraType
                   && m_IsCameraTypeOnly == other.m_IsCameraTypeOnly;
        }

        public override bool Equals(object obj)
        {
            return obj is VirtualTextureViewId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(CameraId, CameraType, m_IsCameraTypeOnly);
        }

        public override string ToString()
        {
            return IsValid ? $"{CameraType}:{CameraId}" : $"{CameraType}:<none>";
        }

        private static bool SupportsCameraTypeOnly(CameraType cameraType)
        {
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }
    }

    public readonly struct VirtualTextureUploadRequest : IEquatable<VirtualTextureUploadRequest>
    {
        private readonly VTRequest m_Request;

        public VirtualTextureUploadRequest(
            int spaceId,
            VirtualTexturePageCoord pageCoord,
            int physicalPageId,
            int generation,
            int priority,
            int requestFrame)
        {
            m_Request = new VTRequest(spaceId, pageCoord, physicalPageId, generation, priority, requestFrame);
        }

        internal VirtualTextureUploadRequest(in VTRequest request)
        {
            m_Request = request;
        }

        internal VTRequest Request => m_Request;

        public int SpaceId => m_Request.SpaceId;

        public VirtualTexturePageCoord PageCoord => m_Request.PageCoord;

        public int PhysicalPageId => m_Request.PhysicalPageId;

        public int Generation => m_Request.Generation;

        public int Priority => m_Request.Priority;

        public int RequestFrame => m_Request.RequestFrame;

        public bool Equals(VirtualTextureUploadRequest other)
        {
            return m_Request.Equals(other.m_Request);
        }

        public override bool Equals(object obj)
        {
            return obj is VirtualTextureUploadRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            return m_Request.GetHashCode();
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

        public float[] ToFloatArray()
        {
            int[] ints = ToIntArray();
            var floats = new float[ints.Length];
            for (int index = 0; index < ints.Length; index++)
                floats[index] = ints[index];

            return floats;
        }
    }

    internal readonly struct VirtualTextureSpaceBinding
    {
        internal VirtualTextureSpaceBinding(
            int spaceId,
            string spaceName,
            GraphicsBuffer pageTableBuffer,
            Texture2DArray physicalCache,
            ComputeBuffer feedbackRequests,
            ComputeBuffer feedbackCounter,
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

        public ComputeBuffer FeedbackRequests { get; }

        public ComputeBuffer FeedbackCounter { get; }

        public VirtualTextureSpaceShaderParams ShaderParams { get; }

        public int[] MipOffsets { get; }

        public bool HasFeedback => FeedbackRequests != null && FeedbackCounter != null;

        public bool IsValid => PageTableBuffer != null && PhysicalCache != null;
    }

    internal readonly struct VirtualTextureStats
    {
        private readonly VTDebugStats m_Stats;

        internal VirtualTextureStats(
            int activeSpaceCount,
            int residentPageCount,
            int freePageCount,
            int pendingUploadCount,
            int evictionCount,
            int faultCount,
            int deduplicatedRequestCount,
            int feedbackOverflowCount,
            int inFlightUploadBatchCount,
            int duplicateUploadCount,
            int skippedUploadCount,
            int fallbackSampleCount,
            int lastReadbackFrame,
            string statusMessage)
        {
            m_Stats = new VTDebugStats(
                activeSpaceCount,
                residentPageCount,
                freePageCount,
                pendingUploadCount,
                evictionCount,
                faultCount,
                deduplicatedRequestCount,
                feedbackOverflowCount,
                inFlightUploadBatchCount,
                duplicateUploadCount,
                skippedUploadCount,
                fallbackSampleCount,
                lastReadbackFrame,
                statusMessage);
        }

        internal VirtualTextureStats(in VTDebugStats stats)
        {
            m_Stats = stats;
        }

        internal VTDebugStats Stats => m_Stats;

        internal int ActiveSpaceCount => m_Stats.ActiveSpaceCount;

        internal int ResidentPageCount => m_Stats.ResidentPageCount;

        internal int FreePageCount => m_Stats.FreePageCount;

        internal int PendingUploadCount => m_Stats.PendingUploadCount;

        internal int EvictionCount => m_Stats.EvictionCount;

        internal int FaultCount => m_Stats.FaultCount;

        internal int DeduplicatedRequestCount => m_Stats.DeduplicatedRequestCount;

        internal int FeedbackOverflowCount => m_Stats.FeedbackOverflowCount;

        internal int InFlightUploadBatchCount => m_Stats.InFlightUploadBatchCount;

        internal int DuplicateUploadCount => m_Stats.DuplicateUploadCount;

        internal int SkippedUploadCount => m_Stats.SkippedUploadCount;

        internal int FallbackSampleCount => m_Stats.FallbackSampleCount;

        internal int LastReadbackFrame => m_Stats.LastReadbackFrame;

        internal string StatusMessage => m_Stats.StatusMessage;

        internal VirtualTextureViewId ViewId => m_Stats.ViewId;

        internal CameraType CameraType => m_Stats.CameraType;

        internal string CameraName => m_Stats.CameraName;

        internal int CameraFrameIndex => m_Stats.CameraFrameIndex;

        internal int ActualWidth => m_Stats.ActualWidth;

        internal int ActualHeight => m_Stats.ActualHeight;

        internal int PixelWidth => m_Stats.PixelWidth;

        internal int PixelHeight => m_Stats.PixelHeight;

        internal bool FeedbackSupported => m_Stats.FeedbackSupported;

        internal int FeedbackCapacity => m_Stats.FeedbackCapacity;

        internal bool IsViewSpecific => m_Stats.IsViewSpecific;

        internal string ViewLabel => m_Stats.ViewLabel;

        internal string RenderSizeLabel => m_Stats.RenderSizeLabel;

        internal string PixelSizeLabel => m_Stats.PixelSizeLabel;
    }

    internal static class VirtualTextureStatsRegistry
    {
        internal static VirtualTextureStats LastStats => new(VTDebugStatsRegistry.LastStats);

        internal static VirtualTextureStats DisplayStats => new(VTDebugStatsRegistry.DisplayStats);

        internal static VirtualTextureStats GetDisplayStats(
            VirtualTextureStatsViewMode viewMode,
            Camera selectedCamera)
        {
            return new VirtualTextureStats(VTDebugStatsRegistry.GetDisplayStats(viewMode, selectedCamera));
        }

        internal static void Report(VirtualTextureStats stats)
        {
            VTDebugStatsRegistry.Report(stats.Stats);
        }

        internal static void Report(in VTDebugStats stats)
        {
            VTDebugStatsRegistry.Report(stats);
        }

        internal static void ReportView(in VTDebugStats stats)
        {
            VTDebugStatsRegistry.ReportView(stats);
        }

        internal static void Clear()
        {
            VTDebugStatsRegistry.Clear();
        }

        internal static void SetFocusedViewOverrideForTesting(
            VirtualTextureViewId viewId,
            CameraType cameraType)
        {
            VTDebugStatsRegistry.SetFocusedViewOverrideForTesting(viewId, cameraType);
        }

        internal static void ClearFocusedViewOverrideForTesting()
        {
            VTDebugStatsRegistry.ClearFocusedViewOverrideForTesting();
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal static class VirtualTextureShaderIDs
    {
        public static readonly int _VTPageTable = Shader.PropertyToID(nameof(_VTPageTable));
        public static readonly int _VTPhysicalCache = Shader.PropertyToID(nameof(_VTPhysicalCache));
        public static readonly int _VTFeedbackRequests = Shader.PropertyToID(nameof(_VTFeedbackRequests));
        public static readonly int _VTFeedbackCounter = Shader.PropertyToID(nameof(_VTFeedbackCounter));
        public static readonly int _VTFeedbackEnabled = Shader.PropertyToID(nameof(_VTFeedbackEnabled));
        public static readonly int _VTSpaceParams = Shader.PropertyToID(nameof(_VTSpaceParams));
        public static readonly int _VTMipOffsets = Shader.PropertyToID(nameof(_VTMipOffsets));
        public static readonly int _VTDebugMode = Shader.PropertyToID(nameof(_VTDebugMode));
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

        internal static VirtualTexturePageCoord GetPageCoord(
            int virtualPageCountX,
            int virtualPageCountY,
            int mip,
            Vector2 virtualUv)
        {
            int pageCountX = GetPageCountX(virtualPageCountX, mip);
            int pageCountY = GetPageCountY(virtualPageCountY, mip);
            float clampedX = Mathf.Clamp01(virtualUv.x);
            float clampedY = Mathf.Clamp01(virtualUv.y);
            return new VirtualTexturePageCoord(
                Mathf.Min((int)(clampedX * pageCountX), pageCountX - 1),
                Mathf.Min((int)(clampedY * pageCountY), pageCountY - 1),
                mip);
        }

        internal static Vector2 ComputePageLocalUv(
            int virtualPageCountX,
            int virtualPageCountY,
            int mip,
            Vector2 virtualUv)
        {
            VirtualTexturePageCoord pageCoord = GetPageCoord(
                virtualPageCountX,
                virtualPageCountY,
                mip,
                virtualUv);
            return ComputePageLocalUv(
                virtualPageCountX,
                virtualPageCountY,
                pageCoord,
                virtualUv);
        }

        internal static Vector2 ComputePageLocalUv(
            int virtualPageCountX,
            int virtualPageCountY,
            in VirtualTexturePageCoord pageCoord,
            Vector2 virtualUv)
        {
            int pageCountX = GetPageCountX(virtualPageCountX, pageCoord.Mip);
            int pageCountY = GetPageCountY(virtualPageCountY, pageCoord.Mip);
            float pageUvX = Mathf.Clamp01(virtualUv.x) * pageCountX;
            float pageUvY = Mathf.Clamp01(virtualUv.y) * pageCountY;
            return new Vector2(
                Mathf.Clamp01(pageUvX - pageCoord.X),
                Mathf.Clamp01(pageUvY - pageCoord.Y));
        }

        internal static Vector3 ComputePhysicalUVW(
            in VirtualTextureSpaceDesc desc,
            Vector2 virtualUv,
            in VirtualTexturePageTableEntry resolvedEntry)
        {
            if (!resolvedEntry.IsMapped)
                return Vector3.zero;

            Vector2 localUv = ComputePageLocalUv(
                desc.VirtualPageCountX,
                desc.VirtualPageCountY,
                resolvedEntry.ResolvedMip,
                virtualUv);
            // Match shader sampling: normalized hardware filtering owns the half-texel offset.
            Vector2 texelCoord = localUv * desc.PageSize
                                  + Vector2.one * desc.BorderSize;
            float physicalPageSize = Mathf.Max(desc.PhysicalPageSize, 1);
            return new Vector3(
                texelCoord.x / physicalPageSize,
                texelCoord.y / physicalPageSize,
                resolvedEntry.PhysicalPageId);
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
