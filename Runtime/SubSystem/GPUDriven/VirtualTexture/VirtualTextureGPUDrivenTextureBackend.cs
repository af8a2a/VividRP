using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.VirtualTexture
{
    public enum GPUDrivenVirtualTexturePhysicalPoolQuality
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    internal readonly struct GPUDrivenVirtualTextureDescriptorProfile :
        IEquatable<GPUDrivenVirtualTextureDescriptorProfile>
    {
        internal GPUDrivenVirtualTextureDescriptorProfile(int cachePageCount)
        {
            if (cachePageCount <= 0
                || cachePageCount > VirtualTexturePageTableEntry.MaxPhysicalPageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(cachePageCount));
            }

            CachePageCount = cachePageCount;
        }

        internal int CachePageCount { get; }

        public bool Equals(GPUDrivenVirtualTextureDescriptorProfile other)
        {
            return CachePageCount == other.CachePageCount;
        }

        public override bool Equals(object obj)
        {
            return obj is GPUDrivenVirtualTextureDescriptorProfile other && Equals(other);
        }

        public override int GetHashCode()
        {
            return CachePageCount;
        }
    }

    internal interface IGPUDrivenVirtualTextureRuntimeCapabilities
    {
        bool SupportsComputeShaders { get; }

        int MaxTextureSize { get; }

        bool IsFormatSupported(GraphicsFormat format, GraphicsFormatUsage usage);

        CopyTextureSupport CopyTextureSupport { get; }
    }

    internal sealed class GPUDrivenVirtualTextureRuntimeCapabilities : IGPUDrivenVirtualTextureRuntimeCapabilities
    {
        internal static readonly GPUDrivenVirtualTextureRuntimeCapabilities Instance = new();

        private GPUDrivenVirtualTextureRuntimeCapabilities()
        {
        }

        public bool SupportsComputeShaders => SystemInfo.supportsComputeShaders;

        public int MaxTextureSize => SystemInfo.maxTextureSize;

        public CopyTextureSupport CopyTextureSupport => SystemInfo.copyTextureSupport;

        public bool IsFormatSupported(GraphicsFormat format, GraphicsFormatUsage usage)
        {
            return SystemInfo.IsFormatSupported(format, usage);
        }
    }

    internal sealed class VirtualTextureGPUDrivenTextureBackend :
        IGPUDrivenTextureBackend,
        IGPUDrivenTextureBindingLifecycle,
        IGPUDrivenVirtualTextureBackend
    {
        internal const int PageSize = 128;
        internal const int BorderSize = 4;
        internal const int AtlasPageCount = 256;
        internal const int MaxAllocationPageCount = 64;
        internal const int VirtualPageCapacity = AtlasPageCount * AtlasPageCount;
        internal const string SpaceName = "VividGPUDriven.StaticMesh";

        private const int LowCachePageCount = 256;
        private const int MediumCachePageCount = 512;
        private const int HighCachePageCount = 1024;
        private const int MaxUploadsPerFrame = 16;
        private const int FeedbackCapacity = 65536;
        private const int NeighborPrefetchCount = 1;
        private const int ResourceLayerBitCount = 8;

        private readonly struct TextureSetKey : IEquatable<TextureSetKey>
        {
            internal TextureSetKey(
                VividVirtualTextureAsset streamedAsset,
                Texture2D baseColor,
                Texture2D normal,
                Texture2D mask,
                GPUDrivenSurfaceAddressMode addressMode,
                GPUDrivenMaterialMaskMode maskMode)
            {
                StreamedAssetId = streamedAsset != null ? streamedAsset.GetEntityId() : EntityId.None;
                ContentVersion = streamedAsset != null ? streamedAsset.ContentVersion : 0u;
                BaseColorId = streamedAsset == null && baseColor != null ? baseColor.GetEntityId() : EntityId.None;
                NormalId = streamedAsset == null && normal != null ? normal.GetEntityId() : EntityId.None;
                MaskId = streamedAsset == null && mask != null ? mask.GetEntityId() : EntityId.None;
                AddressMode = addressMode;
                MaskMode = maskMode;
            }

            private EntityId StreamedAssetId { get; }

            private uint ContentVersion { get; }

            private EntityId BaseColorId { get; }

            private EntityId NormalId { get; }

            private EntityId MaskId { get; }

            private GPUDrivenSurfaceAddressMode AddressMode { get; }

            private GPUDrivenMaterialMaskMode MaskMode { get; }

            public bool Equals(TextureSetKey other)
            {
                return StreamedAssetId == other.StreamedAssetId
                       && ContentVersion == other.ContentVersion
                       && BaseColorId == other.BaseColorId
                       && NormalId == other.NormalId
                       && MaskId == other.MaskId
                       && AddressMode == other.AddressMode
                       && MaskMode == other.MaskMode;
            }

            public override bool Equals(object obj)
            {
                return obj is TextureSetKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    StreamedAssetId,
                    ContentVersion,
                    BaseColorId,
                    NormalId,
                    MaskId,
                    AddressMode,
                    MaskMode);
            }
        }

        private sealed class BindingEntry
        {
            internal TextureSetKey Key;
            internal VividSurfaceBindingData Binding;
            internal Texture2D BaseColor;
            internal Texture2D Normal;
            internal Texture2D Mask;
            internal VividVirtualTextureAsset StreamedAsset;
            internal RectInt PageRegion;
            internal GPUDrivenVirtualTextureAtlasAllocator.Allocation AtlasAllocation;
            internal VirtualTexturePageCoord[] MipTailCoords;
            internal int MaxMip;
            internal int ResidentMipTailPageCount;
            internal uint LastTouchedUpdate;
            internal uint CreatedUpdate;
            internal bool HasAllocation;
            internal bool MipTailResident;
        }

        private readonly Dictionary<TextureSetKey, BindingEntry> m_Bindings = new();
        private readonly HashSet<EntityId> m_RegisteredTextureIds = new();
        private readonly HashSet<EntityId> m_UnsupportedTextureWarningIds = new();
        private readonly HashSet<EntityId> m_InvalidStreamedAssetWarningIds = new();
        private readonly Dictionary<EntityId, uint> m_PermanentlyFailedStreamedAssets = new();
        private readonly HashSet<EntityId> m_IncompatibleScalarMaskWarningIds = new();
        private readonly List<BindingEntry> m_PendingMipTailEntries = new();
        private readonly List<BindingEntry> m_ReleaseEntries = new();
        private readonly List<VTPageRegion> m_ReleaseRegions = new();
        private readonly List<BindingEntry> m_ReleaseAllocationEntries = new();
        private readonly GPUDrivenVirtualTextureAtlasAllocator m_AtlasAllocator = new(
            AtlasPageCount,
            MaxAllocationPageCount);
        private readonly GPUDrivenVirtualTextureProducer m_Producer;

        private uint m_BindingRevision = 1;
        private uint m_SurfaceBindingUpdate = 1;
        private uint m_CreateResourceCallCountThisFrame;
        private int m_AtlasAllocationFailureCount;
        private int m_QueuedMipTailCount;
        private int m_ResidentMipTailCount;
        private int m_QueuedMipTailPageCount;
        private int m_ResidentMipTailPageCount;
        private string m_LastAtlasAllocationFailureReason = string.Empty;
        private bool m_SurfaceBindingUpdateActive;
        private bool m_RetrySurfaceBindingUpdate;
        private bool m_IsDisposed;

        internal VirtualTextureGPUDrivenTextureBackend()
            : this(
                PipelineResourceManager.Get<VividRPCoreResources>()?.GPUDrivenVirtualTexturePageProducerCompute,
                GPUDrivenVirtualTextureRuntimeCapabilities.Instance,
                ResolveDescriptorProfile(GPUDrivenVirtualTexturePhysicalPoolQuality.Medium))
        {
        }

        internal VirtualTextureGPUDrivenTextureBackend(
            GPUDrivenVirtualTextureDescriptorProfile descriptorProfile)
            : this(
                PipelineResourceManager.Get<VividRPCoreResources>()?.GPUDrivenVirtualTexturePageProducerCompute,
                GPUDrivenVirtualTextureRuntimeCapabilities.Instance,
                descriptorProfile)
        {
        }

        internal VirtualTextureGPUDrivenTextureBackend(
            ComputeShader pageProducerCompute,
            IGPUDrivenVirtualTextureRuntimeCapabilities capabilities)
            : this(
                pageProducerCompute,
                capabilities,
                ResolveDescriptorProfile(GPUDrivenVirtualTexturePhysicalPoolQuality.Medium))
        {
        }

        internal VirtualTextureGPUDrivenTextureBackend(
            ComputeShader pageProducerCompute,
            IGPUDrivenVirtualTextureRuntimeCapabilities capabilities,
            GPUDrivenVirtualTextureDescriptorProfile descriptorProfile)
        {
            DescriptorProfile = ResolveSupportedDescriptorProfile(
                descriptorProfile,
                capabilities?.MaxTextureSize ?? 0);
            VirtualTextureSpaceDesc = CreateSpaceDesc(DescriptorProfile);

            if (!DescriptorProfile.Equals(descriptorProfile))
            {
                Debug.LogWarning(
                    $"[VividRP] GPUDriven virtual texture physical pool was reduced from "
                    + $"{descriptorProfile.CachePageCount} to {DescriptorProfile.CachePageCount} pages "
                    + $"because the active device supports at most {capabilities.MaxTextureSize}x"
                    + $"{capabilities.MaxTextureSize} 2D textures.");
            }

            if (!TryValidateGpuPageProducer(pageProducerCompute, capabilities, out string unavailableReason))
            {
                UnavailableReason = unavailableReason;
                Debug.LogWarning($"[VividRP] GPUDriven virtual texture backend is unavailable: {unavailableReason}");
                return;
            }

            try
            {
                m_Producer = new GPUDrivenVirtualTextureProducer(
                    SpaceName,
                    VirtualTextureSpaceDesc,
                    pageProducerCompute);
                VTProducerHandle producerHandle = VirtualTextureSystem.RegisterProducer(VirtualTextureSpaceDesc, m_Producer);
                VTAllocatedVirtualTexture allocation;
                try
                {
                    allocation = VirtualTextureSystem.AllocateVirtualTexture(new VTAllocationDesc(
                        SpaceName,
                        VirtualTextureSpaceDesc,
                        producerHandle,
                        privateSpace: true));
                }
                catch
                {
                    VirtualTextureSystem.ReleaseProducer(producerHandle);
                    throw;
                }

                VirtualTextureSpaceId = allocation.SpaceId;
                VirtualTextureAllocationId = allocation.AllocationId;
                UnavailableReason = string.Empty;
            }
            catch (Exception exception)
            {
                VirtualTextureSpaceId = 0;
                VirtualTextureAllocationId = 0;
                UnavailableReason = exception.Message;
                Debug.LogWarning($"[VividRP] Failed to create the GPUDriven virtual texture backend: {exception.Message}");
            }
        }

        public string DisplayName => "Virtual Texture";

        public bool IsAvailable => !m_IsDisposed && VirtualTextureSpaceId > 0 && VirtualTextureAllocationId > 0;

        public string UnavailableReason { get; }

        public uint BindingRevision => m_BindingRevision;

        public int VirtualTextureSpaceId { get; }

        public int VirtualTextureAllocationId { get; }

        internal GPUDrivenVirtualTextureDescriptorProfile DescriptorProfile { get; }

        internal VirtualTextureSpaceDesc VirtualTextureSpaceDesc { get; }

        internal int AtlasEntryCount => m_Producer?.EntryCount ?? 0;

        internal int StreamedAtlasEntryCount => m_Producer?.StreamedEntryCount ?? 0;

        internal int AllocatedPageCount => m_AtlasAllocator.AllocatedPageCount;

        internal int AtlasAllocationFailureCount => m_AtlasAllocationFailureCount;

        internal string LastAtlasAllocationFailureReason => m_LastAtlasAllocationFailureReason;

        internal int LargestFreeAllocationPageCount => m_AtlasAllocator.GetLargestFreeSquarePageCount();

        internal int ResidentMipTailCount => m_ResidentMipTailCount;

        internal int QueuedMipTailCount => m_QueuedMipTailCount;

        internal int ResidentMipTailPageCount => m_ResidentMipTailPageCount;

        internal int QueuedMipTailPageCount => m_QueuedMipTailPageCount;

        public void PrepareFrame()
        {
            ThrowIfDisposed();
            if (!IsAvailable)
                return;

            if (m_RetrySurfaceBindingUpdate)
            {
                m_RetrySurfaceBindingUpdate = false;
                IncrementBindingRevision();
            }

            m_ReleaseEntries.Clear();
            for (int tailIndex = m_PendingMipTailEntries.Count - 1; tailIndex >= 0; tailIndex--)
            {
                BindingEntry bindingEntry = m_PendingMipTailEntries[tailIndex];
                int residentPageCount = 0;
                bool tailFailed = m_Producer.HasPermanentStreamFailure(bindingEntry.PageRegion);
                for (int pageIndex = 0; pageIndex < bindingEntry.MipTailCoords.Length; pageIndex++)
                {
                    if (VirtualTextureSystem.TryGetPageTableEntry(
                            VirtualTextureSpaceId,
                            bindingEntry.MipTailCoords[pageIndex],
                            out VirtualTexturePageTableEntry entry)
                        && entry.Resident
                        && entry.Locked)
                    {
                        residentPageCount += 1;
                    }
                    else if (!entry.PendingUpload)
                    {
                        tailFailed = true;
                    }
                }

                int residentPageDelta = residentPageCount - bindingEntry.ResidentMipTailPageCount;
                bindingEntry.ResidentMipTailPageCount = residentPageCount;
                m_ResidentMipTailPageCount = Mathf.Max(0, m_ResidentMipTailPageCount + residentPageDelta);
                if (tailFailed)
                {
                    int failedTailLastIndex = m_PendingMipTailEntries.Count - 1;
                    m_PendingMipTailEntries[tailIndex] = m_PendingMipTailEntries[failedTailLastIndex];
                    m_PendingMipTailEntries.RemoveAt(failedTailLastIndex);
                    if (bindingEntry.StreamedAsset != null)
                    {
                        m_PermanentlyFailedStreamedAssets[bindingEntry.StreamedAsset.GetEntityId()] =
                            bindingEntry.StreamedAsset.ContentVersion;
                    }
                    m_ReleaseEntries.Add(bindingEntry);
                    m_RetrySurfaceBindingUpdate = true;
                    continue;
                }

                if (residentPageCount != bindingEntry.MipTailCoords.Length)
                    continue;

                int lastTailIndex = m_PendingMipTailEntries.Count - 1;
                m_PendingMipTailEntries[tailIndex] = m_PendingMipTailEntries[lastTailIndex];
                m_PendingMipTailEntries.RemoveAt(lastTailIndex);
                bindingEntry.MipTailResident = true;
                m_ResidentMipTailCount += 1;
            }

            if (m_ReleaseEntries.Count > 0)
                ReleaseBindingEntries(m_ReleaseEntries);
            m_ReleaseEntries.Clear();
        }

        public void ResetPerFrameStats()
        {
            ThrowIfDisposed();
            m_CreateResourceCallCountThisFrame = 0;
        }

        public void BeginSurfaceBindingUpdate()
        {
            ThrowIfDisposed();
            if (m_SurfaceBindingUpdateActive)
                throw new InvalidOperationException("A GPUDriven VT surface binding update is already active.");

            unchecked
            {
                m_SurfaceBindingUpdate += 1;
                if (m_SurfaceBindingUpdate == 0)
                    m_SurfaceBindingUpdate = 1;
            }

            m_SurfaceBindingUpdateActive = true;
        }

        public void EndSurfaceBindingUpdate()
        {
            ThrowIfDisposed();
            if (!m_SurfaceBindingUpdateActive)
                return;

            m_ReleaseEntries.Clear();
            foreach (BindingEntry bindingEntry in m_Bindings.Values)
            {
                if (bindingEntry.LastTouchedUpdate != m_SurfaceBindingUpdate)
                    m_ReleaseEntries.Add(bindingEntry);
            }

            m_SurfaceBindingUpdateActive = false;
            ReleaseBindingEntries(m_ReleaseEntries);
            m_ReleaseEntries.Clear();
        }

        public void CancelSurfaceBindingUpdate()
        {
            if (m_IsDisposed || !m_SurfaceBindingUpdateActive)
                return;

            m_ReleaseEntries.Clear();
            foreach (BindingEntry bindingEntry in m_Bindings.Values)
            {
                if (bindingEntry.CreatedUpdate == m_SurfaceBindingUpdate)
                    m_ReleaseEntries.Add(bindingEntry);
            }

            m_SurfaceBindingUpdateActive = false;
            ReleaseBindingEntries(m_ReleaseEntries);
            m_ReleaseEntries.Clear();
        }

        public VividSurfaceBindingData CreateSurfaceBinding(in GPUDrivenSurfaceTextureSet textures)
        {
            ThrowIfDisposed();
            if (!IsAvailable || m_Producer == null)
                return CreateEmptyBinding();

            VividVirtualTextureAsset streamedAsset = ResolveStreamedAsset(textures.StreamedVirtualTexture);
            Texture2D baseColor = streamedAsset == null ? ResolveTexture2D(textures.BaseColor) : null;
            Texture2D normal = streamedAsset == null ? ResolveTexture2D(textures.Normal) : null;
            Texture2D mask = streamedAsset == null ? ResolveTexture2D(textures.Mask) : null;
            GPUDrivenSurfaceAddressMode addressMode = streamedAsset != null
                ? streamedAsset.AddressMode == VividVirtualTextureAddressMode.Clamp
                    ? GPUDrivenSurfaceAddressMode.Clamp
                    : GPUDrivenSurfaceAddressMode.Repeat
                : textures.AddressMode;
            var key = new TextureSetKey(streamedAsset, baseColor, normal, mask, addressMode, textures.MaskMode);
            if (m_Bindings.TryGetValue(key, out BindingEntry existingEntry))
            {
                TouchBindingEntry(existingEntry);
                return existingEntry.Binding;
            }

            if (streamedAsset == null && baseColor == null && normal == null && mask == null)
            {
                var emptyEntry = new BindingEntry
                {
                    Key = key,
                    Binding = CreateEmptyBinding(),
                };
                TouchNewBindingEntry(emptyEntry);
                m_Bindings.Add(key, emptyEntry);
                return emptyEntry.Binding;
            }

            if (streamedAsset == null && (baseColor != null || normal != null || mask != null))
            {
                WarnLegacyTextureFallback(baseColor, normal, mask);
                var fallbackEntry = new BindingEntry
                {
                    Key = key,
                    Binding = CreateEmptyBinding(),
                };
                TouchNewBindingEntry(fallbackEntry);
                m_Bindings.Add(key, fallbackEntry);
                return fallbackEntry.Binding;
            }

            Vector2Int allocationPageCounts = streamedAsset != null
                ? new Vector2Int(streamedAsset.VirtualPageCountX, streamedAsset.VirtualPageCountY)
                : ResolveAllocationPageCounts(baseColor, normal, mask);
            if (!m_AtlasAllocator.TryAllocate(
                    allocationPageCounts.x,
                    allocationPageCounts.y,
                    out GPUDrivenVirtualTextureAtlasAllocator.Allocation atlasAllocation))
            {
                m_AtlasAllocationFailureCount += 1;
                int largestFreeAllocation = m_AtlasAllocator.GetLargestFreeSquarePageCount();
                m_LastAtlasAllocationFailureReason =
                    $"GPUDriven VT atlas is full. Could not allocate a {allocationPageCounts.x}x{allocationPageCounts.y} page region. "
                    + $"Used {m_AtlasAllocator.AllocatedPageCount}/{VirtualPageCapacity} virtual pages; "
                    + $"largest aligned free region is {largestFreeAllocation}x{largestFreeAllocation}.";
                Debug.LogWarning($"[VividRP] {m_LastAtlasAllocationFailureReason}");
                m_RetrySurfaceBindingUpdate = true;
                return CreateEmptyBinding();
            }

            RectInt pageRegion = atlasAllocation.PageRegion;
            int maxMip = atlasAllocation.MaxMip;
            bool repeat = addressMode == GPUDrivenSurfaceAddressMode.Repeat;
            if (streamedAsset == null)
                WarnAddressModeFallback(textures, baseColor, normal, mask);
            try
            {
                if (streamedAsset != null)
                    m_Producer.RegisterStreamedEntry(pageRegion, streamedAsset);
                else
                    m_Producer.RegisterEntry(pageRegion, maxMip, baseColor, normal, mask, PageSize, repeat);
            }
            catch
            {
                m_AtlasAllocator.Release(atlasAllocation);
                throw;
            }

            RectInt mipTailRegion = GetRegionAtMip(pageRegion, maxMip);
            VirtualTexturePageCoord[] mipTailCoords = CreatePageCoords(mipTailRegion, maxMip);
            int queuedMipTailPageCount = 0;
            VirtualTexturePageCoord failedMipTailCoord = default;
            string mipTailFailureReason = string.Empty;
            for (int pageIndex = 0; pageIndex < mipTailCoords.Length; pageIndex++)
            {
                failedMipTailCoord = mipTailCoords[pageIndex];
                try
                {
                    if (!VirtualTextureSystem.TryQueuePageResident(
                            VirtualTextureSpaceId,
                            failedMipTailCoord,
                            locked: true,
                            frameIndex: Time.frameCount))
                    {
                        break;
                    }
                }
                catch (Exception exception)
                {
                    mipTailFailureReason = exception.Message;
                    break;
                }

                queuedMipTailPageCount += 1;
            }

            if (queuedMipTailPageCount != mipTailCoords.Length)
            {
                if (queuedMipTailPageCount > 0)
                    VirtualTextureSystem.FlushRegion(VirtualTextureSpaceId, maxMip, mipTailRegion);
                m_Producer.UnregisterEntry(pageRegion);
                m_AtlasAllocator.Release(atlasAllocation);
                if (!string.IsNullOrWhiteSpace(mipTailFailureReason))
                {
                    Debug.LogWarning(
                        $"[VividRP] Failed to queue GPUDriven VT mip tail {failedMipTailCoord}: {mipTailFailureReason}");
                }
                Debug.LogWarning(
                    $"[VividRP] GPUDriven VT mip tail {failedMipTailCoord} could not be queued. "
                    + "The material will use texture fallbacks to keep alpha and shadows deterministic.");
                m_RetrySurfaceBindingUpdate = true;
                return CreateEmptyBinding();
            }

            VividSurfaceBindingFlags flags = VividSurfaceBindingFlags.None;
            int contentLayerMask = streamedAsset != null ? streamedAsset.ContentLayerMask : 0;
            if (streamedAsset != null
                && IsSingleChannelMask(streamedAsset)
                && textures.MaskMode != GPUDrivenMaterialMaskMode.Roughness)
            {
                contentLayerMask &= ~4;
                EntityId assetId = streamedAsset.GetEntityId();
                if (m_IncompatibleScalarMaskWarningIds.Add(assetId))
                {
                    Debug.LogWarning(
                        $"[VividRP] Streamed VT '{streamedAsset.name}' stores its mask as BC4 SingleChannelR, "
                        + $"but material mask mode is {textures.MaskMode}. The mask resource is disabled and material constants are used.",
                        streamedAsset);
                }
            }
            uint baseColorResource = streamedAsset != null
                ? CreateStreamedResource(contentLayerMask, 1, GPUDrivenVirtualTextureProducer.BaseColorLayerIndex, maxMip, VividSurfaceBindingFlags.BaseColor, ref flags)
                : CreateResource(baseColor, GPUDrivenVirtualTextureProducer.BaseColorLayerIndex, maxMip, VividSurfaceBindingFlags.BaseColor, ref flags);
            uint normalResource = streamedAsset != null
                ? CreateStreamedResource(contentLayerMask, 2, GPUDrivenVirtualTextureProducer.NormalLayerIndex, maxMip, VividSurfaceBindingFlags.Normal, ref flags)
                : CreateResource(normal, GPUDrivenVirtualTextureProducer.NormalLayerIndex, maxMip, VividSurfaceBindingFlags.Normal, ref flags);
            uint maskResource = streamedAsset != null
                ? CreateStreamedResource(
                    contentLayerMask,
                    4,
                    IsSingleChannelMask(streamedAsset)
                        ? GPUDrivenVirtualTextureProducer.ScalarMaskLayerIndex
                        : GPUDrivenVirtualTextureProducer.MaskLayerIndex,
                    maxMip,
                    VividSurfaceBindingFlags.Mask,
                    ref flags)
                : CreateResource(mask, GPUDrivenVirtualTextureProducer.MaskLayerIndex, maxMip, VividSurfaceBindingFlags.Mask, ref flags);
            if (streamedAsset != null)
                m_RegisteredTextureIds.Add(streamedAsset.GetEntityId());
            float inverseAtlasPageCount = 1.0f / AtlasPageCount;
            float addressScaleSign = repeat ? 1.0f : -1.0f;

            var binding = new VividSurfaceBindingData
            {
                BaseColorResource = baseColorResource,
                NormalResource = normalResource,
                MaskResource = maskResource,
                Flags = flags,
                UVScaleBias = new float4(
                    addressScaleSign * pageRegion.width * inverseAtlasPageCount,
                    addressScaleSign * pageRegion.height * inverseAtlasPageCount,
                    pageRegion.x * inverseAtlasPageCount,
                    pageRegion.y * inverseAtlasPageCount),
            };

            var bindingEntry = new BindingEntry
            {
                Key = key,
                Binding = binding,
                BaseColor = streamedAsset == null ? baseColor : null,
                Normal = streamedAsset == null ? normal : null,
                Mask = streamedAsset == null ? mask : null,
                StreamedAsset = streamedAsset,
                PageRegion = pageRegion,
                AtlasAllocation = atlasAllocation,
                MipTailCoords = mipTailCoords,
                MaxMip = maxMip,
                HasAllocation = true,
            };
            TouchNewBindingEntry(bindingEntry);
            m_Bindings.Add(key, bindingEntry);
            m_PendingMipTailEntries.Add(bindingEntry);
            m_QueuedMipTailCount += 1;
            m_QueuedMipTailPageCount += mipTailCoords.Length;
            m_CreateResourceCallCountThisFrame += 1;
            IncrementBindingRevision();
            return binding;
        }

        public GPUDrivenTextureBackendStats GetStats()
        {
            ThrowIfDisposed();
            return new GPUDrivenTextureBackendStats(
                poolCount: IsAvailable ? 1u : 0u,
                resourceCapacity: VirtualPageCapacity,
                allocatedResourceCount: (uint) m_AtlasAllocator.AllocatedPageCount,
                createResourceCallCountThisFrame: m_CreateResourceCallCountThisFrame,
                registeredResourceCount: m_RegisteredTextureIds.Count);
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            if (VirtualTextureSpaceId > 0 && VirtualTextureSystem.IsInitialized)
                VirtualTextureSystem.UnregisterAddressSpace(VirtualTextureSpaceId);

            m_Bindings.Clear();
            m_RegisteredTextureIds.Clear();
            m_UnsupportedTextureWarningIds.Clear();
            m_InvalidStreamedAssetWarningIds.Clear();
            m_PermanentlyFailedStreamedAssets.Clear();
            m_IncompatibleScalarMaskWarningIds.Clear();
            m_PendingMipTailEntries.Clear();
            m_ReleaseEntries.Clear();
            m_ReleaseRegions.Clear();
            m_ReleaseAllocationEntries.Clear();
            m_IsDisposed = true;
        }

        internal static uint PackResource(int layerIndex, int maxMip)
        {
            return ((uint) maxMip << ResourceLayerBitCount) | (uint) layerIndex;
        }

        internal static bool IsCompatibleStreamedAsset(
            VividVirtualTextureAsset asset,
            out string validationMessage)
        {
            return IsCompatibleStreamedAsset(
                asset,
                CreateSpaceDesc(
                    ResolveDescriptorProfile(GPUDrivenVirtualTexturePhysicalPoolQuality.Medium)).StackDesc,
                out validationMessage);
        }

        public bool CanUseStreamedVirtualTexture(VividVirtualTextureAsset asset)
        {
            return IsCompatibleStreamedAsset(asset, out _);
        }

        private static bool IsCompatibleStreamedAsset(
            VividVirtualTextureAsset asset,
            VTStackDesc expectedStackDesc,
            out string validationMessage)
        {
            if (asset == null)
            {
                validationMessage = "Assign a streamed virtual texture asset.";
                return false;
            }

            VividVirtualTextureBuiltData builtData = asset.BuiltData;
            if (builtData == null)
            {
                validationMessage = $"Streamed VT asset '{asset.name}' has no built data.";
                return false;
            }

            bool valid = builtData.BuildProfile == VividVirtualTextureBuildProfile.GPUDrivenSurface
                         && builtData.PageSize == PageSize
                         && builtData.BorderSize == BorderSize
                         && builtData.VirtualPageCountX > 0
                         && builtData.VirtualPageCountY > 0
                         && builtData.VirtualPageCountX <= MaxAllocationPageCount
                         && builtData.VirtualPageCountY <= MaxAllocationPageCount
                         && Mathf.IsPowerOfTwo(builtData.VirtualPageCountX)
                         && Mathf.IsPowerOfTwo(builtData.VirtualPageCountY)
                         && builtData.MipCount == ComputeMaxMip(
                             builtData.VirtualPageCountX,
                             builtData.VirtualPageCountY) + 1
                         && builtData.MatchesStack(expectedStackDesc)
                         && (builtData.HasInlineRawData || builtData.HasStreamData);
            if (valid)
            {
                validationMessage = string.Empty;
                return true;
            }

            validationMessage =
                $"Streamed VT asset '{asset.name}' is not a compatible GPUDrivenSurface build (128 texel pages, 4 texel borders, power-of-two dimensions, and the GPUDriven BCn stack are required).";
            return false;
        }

        internal static GPUDrivenVirtualTextureDescriptorProfile ResolveDescriptorProfile(
            GPUDrivenVirtualTexturePhysicalPoolQuality quality)
        {
            int cachePageCount = quality switch
            {
                GPUDrivenVirtualTexturePhysicalPoolQuality.Low => LowCachePageCount,
                GPUDrivenVirtualTexturePhysicalPoolQuality.High => HighCachePageCount,
                _ => MediumCachePageCount,
            };
            return new GPUDrivenVirtualTextureDescriptorProfile(cachePageCount);
        }

        internal static GPUDrivenVirtualTextureDescriptorProfile ResolveSupportedDescriptorProfile(
            in GPUDrivenVirtualTextureDescriptorProfile requestedProfile,
            int maxTextureSize)
        {
            if (maxTextureSize <= 0)
                return requestedProfile;

            int physicalPageSize = PageSize + BorderSize * 2;
            int maxTilesPerDimension = maxTextureSize / physicalPageSize;
            long maxPageCount = (long)maxTilesPerDimension * maxTilesPerDimension;
            if (requestedProfile.CachePageCount <= maxPageCount)
                return requestedProfile;

            if (MediumCachePageCount <= maxPageCount)
            {
                return ResolveDescriptorProfile(
                    GPUDrivenVirtualTexturePhysicalPoolQuality.Medium);
            }

            if (LowCachePageCount <= maxPageCount)
            {
                return ResolveDescriptorProfile(
                    GPUDrivenVirtualTexturePhysicalPoolQuality.Low);
            }

            // There is no supported quality below Low. Keep the requested descriptor so
            // normal backend construction reports the precise atlas capability failure.
            return requestedProfile;
        }

        private static VirtualTextureSpaceDesc CreateSpaceDesc(
            in GPUDrivenVirtualTextureDescriptorProfile descriptorProfile)
        {
            var layers = new[]
            {
                new VTLayerDesc(
                    VTLayerSemantic.BaseColor,
                    GraphicsFormat.RGBA_BC7_SRGB,
                    true,
                    new Color32(255, 255, 255, 255),
                    physicalGroup: 0,
                    VTLayerDataEncoding.RGBA),
                new VTLayerDesc(
                    VTLayerSemantic.Normal,
                    GraphicsFormat.RG_BC5_UNorm,
                    false,
                    new Color32(128, 128, 255, 128),
                    physicalGroup: 1,
                    VTLayerDataEncoding.NormalRG),
                new VTLayerDesc(
                    VTLayerSemantic.Mask,
                    GraphicsFormat.RGBA_BC7_UNorm,
                    false,
                    new Color32(255, 255, 255, 255),
                    physicalGroup: 2,
                    VTLayerDataEncoding.RGBA),
                new VTLayerDesc(
                    VTLayerSemantic.Height,
                    GraphicsFormat.R_BC4_UNorm,
                    false,
                    new Color32(255, 255, 255, 255),
                    physicalGroup: 3,
                    VTLayerDataEncoding.SingleChannelR),
            };
            var stackDesc = new VTStackDesc(
                PageSize,
                BorderSize,
                descriptorProfile.CachePageCount,
                layers,
                MaxUploadsPerFrame,
                FeedbackCapacity,
                NeighborPrefetchCount);
            int mipCount = Mathf.RoundToInt(Mathf.Log(AtlasPageCount, 2.0f)) + 1;
            return new VirtualTextureSpaceDesc(
                SpaceName,
                AtlasPageCount,
                AtlasPageCount,
                mipCount,
                stackDesc);
        }

        private static bool TryValidateGpuPageProducer(
            ComputeShader pageProducerCompute,
            IGPUDrivenVirtualTextureRuntimeCapabilities capabilities,
            out string unavailableReason)
        {
            if (pageProducerCompute == null)
            {
                unavailableReason = "GPUDriven VT page producer compute shader resource is missing.";
                return false;
            }

            if (capabilities == null)
            {
                unavailableReason = "GPUDriven VT runtime capabilities are unavailable.";
                return false;
            }

            if (!capabilities.SupportsComputeShaders)
            {
                unavailableReason = "The active graphics device does not support compute shaders.";
                return false;
            }

            if (!pageProducerCompute.HasKernel("CS"))
            {
                unavailableReason = "GPUDriven VT page producer compute shader is missing the 'CS' kernel.";
                return false;
            }

            GraphicsFormat storageFormat = VTPhysicalPoolDesc.ResolveStorageFormat(GraphicsFormat.R8G8B8A8_SRGB);
            if (!capabilities.IsFormatSupported(storageFormat, GraphicsFormatUsage.LoadStore))
            {
                unavailableReason = $"The active graphics device does not support {storageFormat} UAV load/store.";
                return false;
            }

            GraphicsFormat[] compressedStorageFormats =
            {
                GraphicsFormat.RGBA_BC7_UNorm,
                GraphicsFormat.RG_BC5_UNorm,
                GraphicsFormat.R_BC4_UNorm,
            };
            for (int formatIndex = 0; formatIndex < compressedStorageFormats.Length; formatIndex++)
            {
                GraphicsFormat format = compressedStorageFormats[formatIndex];
                if (capabilities.IsFormatSupported(format, GraphicsFormatUsage.Sample))
                    continue;

                unavailableReason =
                    $"The active graphics device cannot sample the GPUDriven VT physical cache format {format}.";
                return false;
            }

            CopyTextureSupport copySupport = capabilities.CopyTextureSupport;
            if ((copySupport & CopyTextureSupport.Basic) == 0
                || (copySupport & CopyTextureSupport.RTToTexture) == 0
                || (copySupport & CopyTextureSupport.DifferentTypes) == 0)
            {
                unavailableReason =
                    "The active graphics device cannot copy RenderTexture array slices into the VT 2D tile atlas.";
                return false;
            }

            unavailableReason = string.Empty;
            return true;
        }

        private Texture2D ResolveTexture2D(Texture texture)
        {
            if (texture == null)
                return null;
            if (texture is Texture2D texture2D)
                return texture2D;

            EntityId textureId = texture.GetEntityId();
            if (m_UnsupportedTextureWarningIds.Add(textureId))
            {
                Debug.LogWarning(
                    $"[VividRP] GPUDriven VT supports Texture2D sources only. Texture '{texture.name}' ({texture.GetType().Name}) was skipped.",
                    texture);
            }

            return null;
        }

        private void WarnLegacyTextureFallback(Texture2D baseColor, Texture2D normal, Texture2D mask)
        {
            Texture2D texture = baseColor ?? normal ?? mask;
            if (texture == null || !m_UnsupportedTextureWarningIds.Add(texture.GetEntityId()))
                return;

            Debug.LogWarning(
                "[VividRP] The GPUDriven VT physical cache now stores GPU-ready BCn pages. "
                + "Texture2D runtime page encoding is disabled; assign a DesktopBCn streamed VT asset instead. "
                + "Material constants are used as the fallback.",
                texture);
        }

        private VividVirtualTextureAsset ResolveStreamedAsset(VividVirtualTextureAsset asset)
        {
            if (asset == null)
                return null;

            EntityId assetId = asset.GetEntityId();
            if (m_PermanentlyFailedStreamedAssets.TryGetValue(assetId, out uint failedContentVersion))
            {
                if (failedContentVersion == asset.ContentVersion)
                    return null;
                m_PermanentlyFailedStreamedAssets.Remove(assetId);
            }

            if (IsCompatibleStreamedAsset(asset, VirtualTextureSpaceDesc.StackDesc, out _))
                return asset;

            if (m_InvalidStreamedAssetWarningIds.Add(assetId))
            {
                Debug.LogWarning(
                    $"[VividRP] Streamed VT asset '{asset.name}' is not a compatible GPUDrivenSurface build. "
                    + "The material will use its Texture2D fallback producer until the asset is rebuilt.",
                    asset);
            }

            return null;
        }

        private static bool IsSingleChannelMask(VividVirtualTextureAsset asset)
        {
            VividVirtualTextureBuiltData builtData = asset?.BuiltData;
            if (builtData == null)
                return false;

            return builtData.MaskStorage == VividVirtualTextureMaskStorage.SingleChannelR;
        }

        private static Vector2Int ResolveAllocationPageCounts(
            Texture2D baseColor,
            Texture2D normal,
            Texture2D mask)
        {
            int maxWidth = 1;
            int maxHeight = 1;
            ResolveMaxDimensions(baseColor, ref maxWidth, ref maxHeight);
            ResolveMaxDimensions(normal, ref maxWidth, ref maxHeight);
            ResolveMaxDimensions(mask, ref maxWidth, ref maxHeight);
            int requiredPagesX = Mathf.Max(1, Mathf.CeilToInt((float) maxWidth / PageSize));
            int requiredPagesY = Mathf.Max(1, Mathf.CeilToInt((float) maxHeight / PageSize));
            return new Vector2Int(
                Mathf.Min(MaxAllocationPageCount, Mathf.NextPowerOfTwo(requiredPagesX)),
                Mathf.Min(MaxAllocationPageCount, Mathf.NextPowerOfTwo(requiredPagesY)));
        }

        private static void ResolveMaxDimensions(
            Texture2D texture,
            ref int maxWidth,
            ref int maxHeight)
        {
            if (texture == null)
                return;

            maxWidth = Mathf.Max(maxWidth, texture.width);
            maxHeight = Mathf.Max(maxHeight, texture.height);
        }

        private void TouchBindingEntry(BindingEntry bindingEntry)
        {
            if (m_SurfaceBindingUpdateActive)
                bindingEntry.LastTouchedUpdate = m_SurfaceBindingUpdate;
        }

        private void TouchNewBindingEntry(BindingEntry bindingEntry)
        {
            if (!m_SurfaceBindingUpdateActive)
                return;

            bindingEntry.LastTouchedUpdate = m_SurfaceBindingUpdate;
            bindingEntry.CreatedUpdate = m_SurfaceBindingUpdate;
        }

        private void ReleaseBindingEntries(IReadOnlyList<BindingEntry> bindingEntries)
        {
            if (bindingEntries == null || bindingEntries.Count == 0)
                return;

            bool releasedAllocation = false;
            m_ReleaseRegions.Clear();
            m_ReleaseAllocationEntries.Clear();
            for (int entryIndex = 0; entryIndex < bindingEntries.Count; entryIndex++)
            {
                BindingEntry bindingEntry = bindingEntries[entryIndex];
                if (bindingEntry == null || !m_Bindings.Remove(bindingEntry.Key))
                    continue;

                if (!bindingEntry.HasAllocation)
                    continue;

                releasedAllocation = true;
                m_ReleaseAllocationEntries.Add(bindingEntry);
                if (!bindingEntry.MipTailResident)
                    m_PendingMipTailEntries.Remove(bindingEntry);
                else
                    m_ResidentMipTailCount = Mathf.Max(0, m_ResidentMipTailCount - 1);
                m_QueuedMipTailCount = Mathf.Max(0, m_QueuedMipTailCount - 1);
                m_QueuedMipTailPageCount = Mathf.Max(
                    0,
                    m_QueuedMipTailPageCount - bindingEntry.MipTailCoords.Length);
                m_ResidentMipTailPageCount = Mathf.Max(
                    0,
                    m_ResidentMipTailPageCount - bindingEntry.ResidentMipTailPageCount);

                for (int mip = 0; mip <= bindingEntry.MaxMip; mip++)
                    m_ReleaseRegions.Add(new VTPageRegion(mip, GetRegionAtMip(bindingEntry.PageRegion, mip)));
            }

            if (m_ReleaseRegions.Count > 0
                && VirtualTextureSpaceId > 0
                && VirtualTextureSystem.IsInitialized)
            {
                VirtualTextureSystem.FlushRegions(VirtualTextureSpaceId, m_ReleaseRegions);
            }

            for (int entryIndex = 0; entryIndex < m_ReleaseAllocationEntries.Count; entryIndex++)
            {
                BindingEntry bindingEntry = m_ReleaseAllocationEntries[entryIndex];
                m_Producer?.UnregisterEntry(bindingEntry.PageRegion);
                m_AtlasAllocator.Release(bindingEntry.AtlasAllocation);
            }

            RebuildRegisteredTextureIds();
            if (releasedAllocation)
                IncrementBindingRevision();
        }

        private void RebuildRegisteredTextureIds()
        {
            m_RegisteredTextureIds.Clear();
            foreach (BindingEntry bindingEntry in m_Bindings.Values)
            {
                RegisterTextureId(bindingEntry.BaseColor);
                RegisterTextureId(bindingEntry.Normal);
                RegisterTextureId(bindingEntry.Mask);
                if (bindingEntry.StreamedAsset != null)
                    m_RegisteredTextureIds.Add(bindingEntry.StreamedAsset.GetEntityId());
            }
        }

        private void RegisterTextureId(Texture2D texture)
        {
            if (texture != null)
                m_RegisteredTextureIds.Add(texture.GetEntityId());
        }

        private static RectInt GetRegionAtMip(RectInt pageRegion, int mip)
        {
            int xMin = pageRegion.xMin >> mip;
            int yMin = pageRegion.yMin >> mip;
            int xMax = ((pageRegion.xMax - 1) >> mip) + 1;
            int yMax = ((pageRegion.yMax - 1) >> mip) + 1;
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private static VirtualTexturePageCoord[] CreatePageCoords(RectInt pageRegion, int mip)
        {
            var coords = new VirtualTexturePageCoord[pageRegion.width * pageRegion.height];
            int pageIndex = 0;
            for (int y = pageRegion.yMin; y < pageRegion.yMax; y++)
            {
                for (int x = pageRegion.xMin; x < pageRegion.xMax; x++)
                    coords[pageIndex++] = new VirtualTexturePageCoord(x, y, mip);
            }

            return coords;
        }

        private static int ComputeMaxMip(int pageCountX, int pageCountY)
        {
            int minPageCount = Mathf.Max(1, Mathf.Min(pageCountX, pageCountY));
            int maxMip = 0;
            while ((minPageCount >>= 1) > 0)
                maxMip += 1;
            return maxMip;
        }

        private uint CreateResource(
            Texture2D texture,
            int layerIndex,
            int maxMip,
            VividSurfaceBindingFlags flag,
            ref VividSurfaceBindingFlags flags)
        {
            if (texture == null)
                return VividSurfaceBindingData.InvalidResource;

            flags |= flag;
            m_RegisteredTextureIds.Add(texture.GetEntityId());
            return PackResource(layerIndex, maxMip);
        }

        private static uint CreateStreamedResource(
            int contentLayerMask,
            int layerBit,
            int layerIndex,
            int maxMip,
            VividSurfaceBindingFlags flag,
            ref VividSurfaceBindingFlags flags)
        {
            if ((contentLayerMask & layerBit) == 0)
                return VividSurfaceBindingData.InvalidResource;

            flags |= flag;
            return PackResource(layerIndex, maxMip);
        }

        private static void WarnAddressModeFallback(
            in GPUDrivenSurfaceTextureSet textures,
            Texture2D baseColor,
            Texture2D normal,
            Texture2D mask)
        {
            string materialTextures = $"Base='{baseColor?.name ?? "None"}', Normal='{normal?.name ?? "None"}', Mask='{mask?.name ?? "None"}'";
            if (textures.HasUnsupportedAddressMode)
            {
                Debug.LogWarning(
                    $"[VividRP] GPUDriven surfaces support Repeat and Clamp address modes. "
                    + $"Mirror modes fall back to Repeat ({materialTextures}).");
            }

            if (textures.HasMixedAddressModes)
            {
                Debug.LogWarning(
                    $"[VividRP] GPUDriven surface layers must share one address mode. "
                    + $"Using {textures.AddressMode} from the first available layer ({materialTextures}).");
            }
        }

        private static VividSurfaceBindingData CreateEmptyBinding()
        {
            return new VividSurfaceBindingData
            {
                BaseColorResource = VividSurfaceBindingData.InvalidResource,
                NormalResource = VividSurfaceBindingData.InvalidResource,
                MaskResource = VividSurfaceBindingData.InvalidResource,
                Flags = VividSurfaceBindingFlags.None,
                UVScaleBias = new float4(1.0f, 1.0f, 0.0f, 0.0f),
            };
        }

        private void IncrementBindingRevision()
        {
            unchecked
            {
                m_BindingRevision += 1;
                if (m_BindingRevision == 0)
                    m_BindingRevision = 1;
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
                throw new ObjectDisposedException(nameof(VirtualTextureGPUDrivenTextureBackend));
        }
    }
}
