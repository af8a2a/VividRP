using System;
using System.Collections.Generic;
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

    internal readonly struct VTPageRegion
    {
        internal VTPageRegion(int mip, RectInt pageRegion)
        {
            Mip = mip;
            PageRegion = pageRegion;
        }

        internal int Mip { get; }

        internal RectInt PageRegion { get; }
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
            int requestFrame,
            int cameraPriority = int.MaxValue,
            bool isActiveView = false)
        {
            m_Request = new VTRequest(
                spaceId,
                pageCoord,
                physicalPageId,
                generation,
                priority,
                requestFrame,
                cameraPriority,
                isActiveView);
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

        public int CameraPriority => m_Request.CameraPriority;

        public bool IsActiveView => m_Request.IsActiveView;

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
        public const int PackedBitCount = 32;
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
        public const int MaxPhysicalPageId = InvalidPhysicalPageId - 1;
        public const int ReservedBitCount = PackedBitCount - LockedBitOffset - 1;

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
            if (resident && fallback)
                throw new ArgumentException("A page table entry cannot be both directly resident and a fallback.");
            if ((resident || fallback) && physicalPageId == InvalidPhysicalPageId)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(physicalPageId),
                    "Mapped page table entries must not use the reserved invalid physical page id.");
            }

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
        internal const int IntCount = 33;

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
            LayerCount = Mathf.Max(1, desc.StackDesc.LayerCount);
            BaseColorLayerIndex = desc.StackDesc.GetLayerIndexOrDefault(VTLayerSemantic.BaseColor, 0);
            NormalLayerIndex = desc.StackDesc.TryGetLayerIndex(VTLayerSemantic.Normal, out int normalLayerIndex)
                ? normalLayerIndex
                : -1;
            MaskLayerIndex = desc.StackDesc.TryGetLayerIndex(VTLayerSemantic.Mask, out int maskLayerIndex)
                ? maskLayerIndex
                : -1;
            Layer0SRGB = GetLayerSRGBFlag(desc.StackDesc, 0);
            Layer1SRGB = GetLayerSRGBFlag(desc.StackDesc, 1);
            Layer2SRGB = GetLayerSRGBFlag(desc.StackDesc, 2);
            Layer3SRGB = GetLayerSRGBFlag(desc.StackDesc, 3);
            PhysicalGroup0LayerCount = GetPhysicalGroupLayerCount(desc.StackDesc, 0);
            PhysicalGroup1LayerCount = GetPhysicalGroupLayerCount(desc.StackDesc, 1);
            PhysicalGroup2LayerCount = GetPhysicalGroupLayerCount(desc.StackDesc, 2);
            PhysicalGroup3LayerCount = GetPhysicalGroupLayerCount(desc.StackDesc, 3);
            Layer0PhysicalGroup = GetLayerPhysicalGroup(desc.StackDesc, 0);
            Layer1PhysicalGroup = GetLayerPhysicalGroup(desc.StackDesc, 1);
            Layer2PhysicalGroup = GetLayerPhysicalGroup(desc.StackDesc, 2);
            Layer3PhysicalGroup = GetLayerPhysicalGroup(desc.StackDesc, 3);
            Layer0PhysicalLayerIndex = GetLayerPhysicalLayerIndex(desc.StackDesc, 0);
            Layer1PhysicalLayerIndex = GetLayerPhysicalLayerIndex(desc.StackDesc, 1);
            Layer2PhysicalLayerIndex = GetLayerPhysicalLayerIndex(desc.StackDesc, 2);
            Layer3PhysicalLayerIndex = GetLayerPhysicalLayerIndex(desc.StackDesc, 3);
            LayerEncodingWord = PackLayerEncodings(desc.StackDesc);
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

        public int LayerCount { get; }

        public int BaseColorLayerIndex { get; }

        public int NormalLayerIndex { get; }

        public int MaskLayerIndex { get; }

        public int Layer0SRGB { get; }

        public int Layer1SRGB { get; }

        public int Layer2SRGB { get; }

        public int Layer3SRGB { get; }

        public int PhysicalGroup0LayerCount { get; }

        public int PhysicalGroup1LayerCount { get; }

        public int PhysicalGroup2LayerCount { get; }

        public int PhysicalGroup3LayerCount { get; }

        public int Layer0PhysicalGroup { get; }

        public int Layer1PhysicalGroup { get; }

        public int Layer2PhysicalGroup { get; }

        public int Layer3PhysicalGroup { get; }

        public int Layer0PhysicalLayerIndex { get; }

        public int Layer1PhysicalLayerIndex { get; }

        public int Layer2PhysicalLayerIndex { get; }

        public int Layer3PhysicalLayerIndex { get; }

        public int LayerEncodingWord { get; }

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
                LayerCount,
                BaseColorLayerIndex,
                NormalLayerIndex,
                MaskLayerIndex,
                Layer0SRGB,
                Layer1SRGB,
                Layer2SRGB,
                Layer3SRGB,
                PhysicalGroup0LayerCount,
                PhysicalGroup1LayerCount,
                PhysicalGroup2LayerCount,
                PhysicalGroup3LayerCount,
                Layer0PhysicalGroup,
                Layer1PhysicalGroup,
                Layer2PhysicalGroup,
                Layer3PhysicalGroup,
                Layer0PhysicalLayerIndex,
                Layer1PhysicalLayerIndex,
                Layer2PhysicalLayerIndex,
                Layer3PhysicalLayerIndex,
                LayerEncodingWord,
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

        internal void CopyTo(float[] destination)
        {
            if (destination == null)
                return;

            int length = Mathf.Min(IntCount, destination.Length);
            for (int index = 0; index < length; index++)
                destination[index] = GetValue(index);
        }

        private int GetValue(int index)
        {
            return index switch
            {
                0 => SpaceId,
                1 => PageSize,
                2 => BorderSize,
                3 => PhysicalPageSize,
                4 => VirtualPageCountX,
                5 => VirtualPageCountY,
                6 => MipCount,
                7 => CachePageCount,
                8 => FeedbackCapacity,
                9 => PageTableEntryCount,
                10 => PhysicalPageWidth,
                11 => PhysicalPageHeight,
                12 => LayerCount,
                13 => BaseColorLayerIndex,
                14 => NormalLayerIndex,
                15 => MaskLayerIndex,
                16 => Layer0SRGB,
                17 => Layer1SRGB,
                18 => Layer2SRGB,
                19 => Layer3SRGB,
                20 => PhysicalGroup0LayerCount,
                21 => PhysicalGroup1LayerCount,
                22 => PhysicalGroup2LayerCount,
                23 => PhysicalGroup3LayerCount,
                24 => Layer0PhysicalGroup,
                25 => Layer1PhysicalGroup,
                26 => Layer2PhysicalGroup,
                27 => Layer3PhysicalGroup,
                28 => Layer0PhysicalLayerIndex,
                29 => Layer1PhysicalLayerIndex,
                30 => Layer2PhysicalLayerIndex,
                31 => Layer3PhysicalLayerIndex,
                32 => LayerEncodingWord,
                _ => 0,
            };
        }

        private static int PackLayerEncodings(in VTStackDesc stackDesc)
        {
            int packed = 0;
            for (int layerIndex = 0; layerIndex < Mathf.Min(4, stackDesc.LayerCount); layerIndex++)
                packed |= ((int)stackDesc.GetLayer(layerIndex).Encoding & 0x3) << (layerIndex * 2);

            return packed;
        }

        private static int GetLayerSRGBFlag(in VTStackDesc stackDesc, int layerIndex)
        {
            return layerIndex >= 0 && layerIndex < stackDesc.LayerCount && stackDesc.GetLayer(layerIndex).SRGB
                ? 1
                : 0;
        }

        private static int GetPhysicalGroupLayerCount(in VTStackDesc stackDesc, int physicalGroup)
        {
            if (physicalGroup < 0)
                return 0;

            int count = 0;
            for (int layerIndex = 0; layerIndex < stackDesc.LayerCount; layerIndex++)
            {
                if (stackDesc.GetLayer(layerIndex).PhysicalGroup == physicalGroup)
                    count += 1;
            }

            return count;
        }

        private static int GetLayerPhysicalGroup(in VTStackDesc stackDesc, int layerIndex)
        {
            return layerIndex >= 0 && layerIndex < stackDesc.LayerCount
                ? stackDesc.GetLayer(layerIndex).PhysicalGroup
                : 0;
        }

        private static int GetLayerPhysicalLayerIndex(in VTStackDesc stackDesc, int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= stackDesc.LayerCount)
                return 0;

            int physicalGroup = stackDesc.GetLayer(layerIndex).PhysicalGroup;
            int physicalLayerIndex = 0;
            for (int candidateIndex = 0; candidateIndex < layerIndex; candidateIndex++)
            {
                if (stackDesc.GetLayer(candidateIndex).PhysicalGroup == physicalGroup)
                    physicalLayerIndex += 1;
            }

            return physicalLayerIndex;
        }
    }

    internal readonly struct VirtualTextureSpaceBinding
    {
        internal VirtualTextureSpaceBinding(
            int bindingIndex,
            int allocationId,
            bool privateSpace,
            int spaceId,
            string spaceName,
            VTProducerHandle producerHandle,
            GraphicsBuffer pageTableBuffer,
            IReadOnlyList<Texture2D> physicalCaches,
            ComputeBuffer feedbackRequests,
            ComputeBuffer feedbackCounter,
            VirtualTextureSpaceShaderParams shaderParams,
            int[] mipOffsets,
            Vector4[] layerFallbacks)
        {
            BindingIndex = bindingIndex;
            AllocationId = allocationId;
            PrivateSpace = privateSpace;
            SpaceId = spaceId;
            SpaceName = spaceName;
            ProducerHandle = producerHandle;
            PageTableBuffer = pageTableBuffer;
            PhysicalCaches = physicalCaches ?? Array.Empty<Texture2D>();
            PhysicalCache = PhysicalCaches.Count > 0 ? PhysicalCaches[0] : null;
            FeedbackRequests = feedbackRequests;
            FeedbackCounter = feedbackCounter;
            ShaderParams = shaderParams;
            MipOffsets = mipOffsets;
            LayerFallbacks = layerFallbacks ?? Array.Empty<Vector4>();
        }

        public int BindingIndex { get; }

        public int AllocationId { get; }

        public bool PrivateSpace { get; }

        public int SpaceId { get; }

        public string SpaceName { get; }

        public VTProducerHandle ProducerHandle { get; }

        public GraphicsBuffer PageTableBuffer { get; }

        public Texture2D PhysicalCache { get; }

        public IReadOnlyList<Texture2D> PhysicalCaches { get; }

        public ComputeBuffer FeedbackRequests { get; }

        public ComputeBuffer FeedbackCounter { get; }

        public VirtualTextureSpaceShaderParams ShaderParams { get; }

        public int[] MipOffsets { get; }

        public Vector4[] LayerFallbacks { get; }

        public bool HasFeedback => FeedbackRequests != null && FeedbackCounter != null;

        public bool IsValid => PageTableBuffer != null && PhysicalCache != null;

        internal VirtualTextureSpaceBinding WithBindingIndex(int bindingIndex)
        {
            return new VirtualTextureSpaceBinding(
                bindingIndex,
                AllocationId,
                PrivateSpace,
                SpaceId,
                SpaceName,
                ProducerHandle,
                PageTableBuffer,
                PhysicalCaches,
                FeedbackRequests,
                FeedbackCounter,
                ShaderParams,
                MipOffsets,
                LayerFallbacks);
        }
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

        internal int PhysicalPoolCount => m_Stats.PhysicalPoolCount;

        internal int PhysicalPoolResidentPageCount => m_Stats.PhysicalPoolResidentPageCount;

        internal int PhysicalPoolFreePageCount => m_Stats.PhysicalPoolFreePageCount;

        internal int PhysicalPoolLockedPageCount => m_Stats.PhysicalPoolLockedPageCount;

        internal int PhysicalPoolEvictedPageCount => m_Stats.PhysicalPoolEvictedPageCount;

        internal int PendingMipGapSum => m_Stats.PendingMipGapSum;

        internal int PendingMipGapMax => m_Stats.PendingMipGapMax;

        internal int PendingMipGapSampleCount => m_Stats.PendingMipGapSampleCount;

        internal float PendingMipGapAverage => m_Stats.PendingMipGapAverage;

        internal int PrefetchRequestCount => m_Stats.PrefetchRequestCount;

        internal int CpuProducedPageCount => m_Stats.CpuProducedPageCount;

        internal int GpuProducedPageCount => m_Stats.GpuProducedPageCount;

        internal int GpuDispatchCount => m_Stats.GpuDispatchCount;

        internal int StreamSaturatedRequestCount => m_Stats.StreamSaturatedRequestCount;

        internal float AdaptiveMipBias => m_Stats.AdaptiveMipBias;

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
        public static readonly int _VTPhysicalCache1 = Shader.PropertyToID(nameof(_VTPhysicalCache1));
        public static readonly int _VTPhysicalCache2 = Shader.PropertyToID(nameof(_VTPhysicalCache2));
        public static readonly int _VTPhysicalCache3 = Shader.PropertyToID(nameof(_VTPhysicalCache3));
        public static readonly int _VTFeedbackRequests = Shader.PropertyToID(nameof(_VTFeedbackRequests));
        public static readonly int _VTFeedbackCounter = Shader.PropertyToID(nameof(_VTFeedbackCounter));
        public static readonly int _VTFeedbackEnabled = Shader.PropertyToID(nameof(_VTFeedbackEnabled));
        public static readonly int _VTFeedbackViewParams = Shader.PropertyToID(nameof(_VTFeedbackViewParams));
        public static readonly int _VTSpaceParams = Shader.PropertyToID(nameof(_VTSpaceParams));
        public static readonly int _VTMipOffsets = Shader.PropertyToID(nameof(_VTMipOffsets));
        public static readonly int _VTLayerFallbacks = Shader.PropertyToID(nameof(_VTLayerFallbacks));
        public static readonly int _VTDebugMode = Shader.PropertyToID(nameof(_VTDebugMode));
        public static readonly int _VTFeedbackFrameIndex = Shader.PropertyToID(nameof(_VTFeedbackFrameIndex));
        public static readonly int _VTFeedbackSampleRate = Shader.PropertyToID(nameof(_VTFeedbackSampleRate));
        public static readonly int _VTAdaptiveMipBias = Shader.PropertyToID(nameof(_VTAdaptiveMipBias));

        public static readonly int[] PhysicalCaches =
        {
            _VTPhysicalCache,
            _VTPhysicalCache1,
            _VTPhysicalCache2,
            _VTPhysicalCache3,
        };
    }

    internal static class VirtualTextureSpaceUtility
    {
        internal const int PageTableEntryStride = sizeof(uint);
        internal const int MaxPageTableEntryCount = int.MaxValue / PageTableEntryStride;

        internal static int[] BuildMipOffsets(int virtualPageCountX, int virtualPageCountY, int mipCount)
        {
            GetTotalPageCount(virtualPageCountX, virtualPageCountY, mipCount);
            var offsets = new int[mipCount];
            long runningOffset = 0;
            for (int mip = 0; mip < mipCount; mip++)
            {
                offsets[mip] = (int)runningOffset;
                runningOffset += (long)GetPageCountX(virtualPageCountX, mip)
                                 * GetPageCountY(virtualPageCountY, mip);
            }

            return offsets;
        }

        internal static int GetTotalPageCount(int virtualPageCountX, int virtualPageCountY, int mipCount)
        {
            ValidatePageTableDimensions(virtualPageCountX, virtualPageCountY, mipCount);
            long total = 0;
            for (int mip = 0; mip < mipCount; mip++)
            {
                total += (long)GetPageCountX(virtualPageCountX, mip)
                         * GetPageCountY(virtualPageCountY, mip);
                if (total > MaxPageTableEntryCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(virtualPageCountX),
                        $"Virtual texture page table requires {total} entries, exceeding the "
                        + $"supported maximum of {MaxPageTableEntryCount} entries.");
                }
            }

            return (int)total;
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
            return ComputePhysicalUVW(desc, virtualUv, resolvedEntry, layerIndex: 0);
        }

        internal static Vector3 ComputePhysicalUVW(
            in VirtualTextureSpaceDesc desc,
            Vector2 virtualUv,
            in VirtualTexturePageTableEntry resolvedEntry,
            int layerIndex)
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
            int physicalSlice = resolvedEntry.PhysicalPageId * Mathf.Max(1, desc.StackDesc.LayerCount)
                                + Mathf.Clamp(layerIndex, 0, Mathf.Max(0, desc.StackDesc.LayerCount - 1));
            return new Vector3(
                texelCoord.x / physicalPageSize,
                texelCoord.y / physicalPageSize,
                physicalSlice);
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

        private static void ValidatePageTableDimensions(
            int virtualPageCountX,
            int virtualPageCountY,
            int mipCount)
        {
            if (virtualPageCountX <= 0
                || virtualPageCountX > VirtualTextureFeedbackProcessor.MaxPageCountPerDimension)
            {
                throw new ArgumentOutOfRangeException(nameof(virtualPageCountX));
            }

            if (virtualPageCountY <= 0
                || virtualPageCountY > VirtualTextureFeedbackProcessor.MaxPageCountPerDimension)
            {
                throw new ArgumentOutOfRangeException(nameof(virtualPageCountY));
            }

            if (mipCount <= 0 || mipCount > VirtualTextureFeedbackProcessor.MaxMipCount)
                throw new ArgumentOutOfRangeException(nameof(mipCount));
        }
    }
}
