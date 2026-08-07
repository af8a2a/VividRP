using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.VirtualTexture
{
    internal interface IGPUDrivenVirtualTextureRuntimeCapabilities
    {
        bool SupportsComputeShaders { get; }

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

        private const int CachePageCount = 512;
        private const int MaxUploadsPerFrame = 16;
        private const int FeedbackCapacity = 65536;
        private const int ResourceLayerBitCount = 8;

        private readonly struct TextureSetKey : IEquatable<TextureSetKey>
        {
            internal TextureSetKey(
                VividVirtualTextureAsset streamedAsset,
                Texture2D baseColor,
                Texture2D normal,
                Texture2D mask,
                GPUDrivenSurfaceAddressMode addressMode)
            {
                StreamedAssetId = streamedAsset != null ? streamedAsset.GetEntityId() : EntityId.None;
                ContentVersion = streamedAsset != null ? streamedAsset.ContentVersion : 0u;
                BaseColorId = streamedAsset == null && baseColor != null ? baseColor.GetEntityId() : EntityId.None;
                NormalId = streamedAsset == null && normal != null ? normal.GetEntityId() : EntityId.None;
                MaskId = streamedAsset == null && mask != null ? mask.GetEntityId() : EntityId.None;
                AddressMode = addressMode;
            }

            private EntityId StreamedAssetId { get; }

            private uint ContentVersion { get; }

            private EntityId BaseColorId { get; }

            private EntityId NormalId { get; }

            private EntityId MaskId { get; }

            private GPUDrivenSurfaceAddressMode AddressMode { get; }

            public bool Equals(TextureSetKey other)
            {
                return StreamedAssetId == other.StreamedAssetId
                       && ContentVersion == other.ContentVersion
                       && BaseColorId == other.BaseColorId
                       && NormalId == other.NormalId
                       && MaskId == other.MaskId
                       && AddressMode == other.AddressMode;
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
                    AddressMode);
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
            internal VirtualTexturePageCoord MipTailCoord;
            internal int MaxMip;
            internal uint LastTouchedUpdate;
            internal uint CreatedUpdate;
            internal bool HasAllocation;
            internal bool MipTailResident;
        }

        private readonly Dictionary<TextureSetKey, BindingEntry> m_Bindings = new();
        private readonly HashSet<EntityId> m_RegisteredTextureIds = new();
        private readonly HashSet<EntityId> m_UnsupportedTextureWarningIds = new();
        private readonly HashSet<EntityId> m_InvalidStreamedAssetWarningIds = new();
        private readonly List<BindingEntry> m_PendingMipTailEntries = new();
        private readonly List<BindingEntry> m_ReleaseEntries = new();
        private readonly List<VTPageRegion> m_ReleaseRegions = new();
        private readonly List<RectInt> m_ReleaseAtlasRegions = new();
        private readonly bool[,] m_AllocatedPages = new bool[AtlasPageCount, AtlasPageCount];
        private readonly GPUDrivenVirtualTextureProducer m_Producer;

        private uint m_BindingRevision = 1;
        private uint m_SurfaceBindingUpdate = 1;
        private uint m_CreateResourceCallCountThisFrame;
        private int m_AllocatedPageCount;
        private int m_AtlasAllocationFailureCount;
        private int m_QueuedMipTailCount;
        private int m_ResidentMipTailCount;
        private string m_LastAtlasAllocationFailureReason = string.Empty;
        private bool m_SurfaceBindingUpdateActive;
        private bool m_RetrySurfaceBindingUpdate;
        private bool m_IsDisposed;

        internal VirtualTextureGPUDrivenTextureBackend()
            : this(
                PipelineResourceManager.Get<VividRPCoreResources>()?.GPUDrivenVirtualTexturePageProducerCompute,
                GPUDrivenVirtualTextureRuntimeCapabilities.Instance)
        {
        }

        internal VirtualTextureGPUDrivenTextureBackend(
            ComputeShader pageProducerCompute,
            IGPUDrivenVirtualTextureRuntimeCapabilities capabilities)
        {
            VirtualTextureSpaceDesc = CreateSpaceDesc();

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

        internal VirtualTextureSpaceDesc VirtualTextureSpaceDesc { get; }

        internal int AtlasEntryCount => m_Producer?.EntryCount ?? 0;

        internal int StreamedAtlasEntryCount => m_Producer?.StreamedEntryCount ?? 0;

        internal int AllocatedPageCount => m_AllocatedPageCount;

        internal int AtlasAllocationFailureCount => m_AtlasAllocationFailureCount;

        internal string LastAtlasAllocationFailureReason => m_LastAtlasAllocationFailureReason;

        internal int LargestFreeAllocationPageCount => GetLargestFreeAllocationPageCount();

        internal int ResidentMipTailCount => m_ResidentMipTailCount;

        internal int QueuedMipTailCount => m_QueuedMipTailCount;

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

            for (int tailIndex = m_PendingMipTailEntries.Count - 1; tailIndex >= 0; tailIndex--)
            {
                BindingEntry bindingEntry = m_PendingMipTailEntries[tailIndex];
                if (VirtualTextureSystem.TryGetPageTableEntry(
                        VirtualTextureSpaceId,
                        bindingEntry.MipTailCoord,
                        out VirtualTexturePageTableEntry entry)
                    && entry.Resident
                    && entry.Locked)
                {
                    int lastTailIndex = m_PendingMipTailEntries.Count - 1;
                    m_PendingMipTailEntries[tailIndex] = m_PendingMipTailEntries[lastTailIndex];
                    m_PendingMipTailEntries.RemoveAt(lastTailIndex);
                    bindingEntry.MipTailResident = true;
                    m_ResidentMipTailCount += 1;
                }
            }
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
            var key = new TextureSetKey(streamedAsset, baseColor, normal, mask, addressMode);
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

            int allocationPageCount = streamedAsset != null
                ? streamedAsset.VirtualPageCountX
                : ResolveAllocationPageCount(baseColor, normal, mask);
            if (!TryAllocatePageRegion(allocationPageCount, out RectInt pageRegion))
            {
                m_AtlasAllocationFailureCount += 1;
                int largestFreeAllocation = GetLargestFreeAllocationPageCount();
                m_LastAtlasAllocationFailureReason =
                    $"GPUDriven VT atlas is full. Could not allocate a {allocationPageCount}x{allocationPageCount} page region. "
                    + $"Used {m_AllocatedPageCount}/{VirtualPageCapacity} virtual pages; "
                    + $"largest aligned free region is {largestFreeAllocation}x{largestFreeAllocation}.";
                Debug.LogWarning($"[VividRP] {m_LastAtlasAllocationFailureReason}");
                m_RetrySurfaceBindingUpdate = true;
                return CreateEmptyBinding();
            }

            int maxMip = streamedAsset != null
                ? streamedAsset.MipCount - 1
                : Mathf.RoundToInt(Mathf.Log(allocationPageCount, 2.0f));
            bool repeat = addressMode == GPUDrivenSurfaceAddressMode.Repeat;
            if (streamedAsset == null)
                WarnAddressModeFallback(textures, baseColor, normal, mask);
            if (streamedAsset != null)
                m_Producer.RegisterStreamedEntry(pageRegion, streamedAsset);
            else
                m_Producer.RegisterEntry(pageRegion, maxMip, baseColor, normal, mask, PageSize, repeat);

            var mipTailCoord = new VirtualTexturePageCoord(
                pageRegion.x >> maxMip,
                pageRegion.y >> maxMip,
                maxMip);
            bool mipTailQueued;
            try
            {
                mipTailQueued = VirtualTextureSystem.TryQueuePageResident(
                    VirtualTextureSpaceId,
                    mipTailCoord,
                    locked: true,
                    frameIndex: Time.frameCount);
            }
            catch (Exception exception)
            {
                mipTailQueued = false;
                Debug.LogWarning(
                    $"[VividRP] Failed to queue GPUDriven VT mip tail {mipTailCoord}: {exception.Message}");
            }

            if (!mipTailQueued)
            {
                m_Producer.UnregisterEntry(pageRegion);
                ReleasePageRegion(pageRegion);
                Debug.LogWarning(
                    $"[VividRP] GPUDriven VT mip tail {mipTailCoord} could not be queued. "
                    + "The material will use texture fallbacks to keep alpha and shadows deterministic.");
                m_RetrySurfaceBindingUpdate = true;
                return CreateEmptyBinding();
            }

            VividSurfaceBindingFlags flags = VividSurfaceBindingFlags.None;
            int contentLayerMask = streamedAsset != null ? streamedAsset.ContentLayerMask : 0;
            uint baseColorResource = streamedAsset != null
                ? CreateStreamedResource(contentLayerMask, 1, GPUDrivenVirtualTextureProducer.BaseColorLayerIndex, maxMip, VividSurfaceBindingFlags.BaseColor, ref flags)
                : CreateResource(baseColor, GPUDrivenVirtualTextureProducer.BaseColorLayerIndex, maxMip, VividSurfaceBindingFlags.BaseColor, ref flags);
            uint normalResource = streamedAsset != null
                ? CreateStreamedResource(contentLayerMask, 2, GPUDrivenVirtualTextureProducer.NormalLayerIndex, maxMip, VividSurfaceBindingFlags.Normal, ref flags)
                : CreateResource(normal, GPUDrivenVirtualTextureProducer.NormalLayerIndex, maxMip, VividSurfaceBindingFlags.Normal, ref flags);
            uint maskResource = streamedAsset != null
                ? CreateStreamedResource(contentLayerMask, 4, GPUDrivenVirtualTextureProducer.MaskLayerIndex, maxMip, VividSurfaceBindingFlags.Mask, ref flags)
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
                MipTailCoord = mipTailCoord,
                MaxMip = maxMip,
                HasAllocation = true,
            };
            TouchNewBindingEntry(bindingEntry);
            m_Bindings.Add(key, bindingEntry);
            m_PendingMipTailEntries.Add(bindingEntry);
            m_QueuedMipTailCount += 1;
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
                allocatedResourceCount: (uint) m_AllocatedPageCount,
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
            m_PendingMipTailEntries.Clear();
            m_ReleaseEntries.Clear();
            m_ReleaseRegions.Clear();
            m_ReleaseAtlasRegions.Clear();
            m_IsDisposed = true;
        }

        internal static uint PackResource(int layerIndex, int maxMip)
        {
            return ((uint) maxMip << ResourceLayerBitCount) | (uint) layerIndex;
        }

        private static VirtualTextureSpaceDesc CreateSpaceDesc()
        {
            var layers = new[]
            {
                new VTLayerDesc(
                    VTLayerSemantic.BaseColor,
                    GraphicsFormat.R8G8B8A8_SRGB,
                    true,
                    new Color32(255, 255, 255, 255)),
                new VTLayerDesc(
                    VTLayerSemantic.Normal,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    false,
                    // GPUDriven uses the existing DXT5nm-compatible W/Y unpack path.
                    new Color32(128, 128, 255, 128)),
                new VTLayerDesc(
                    VTLayerSemantic.Mask,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    false,
                    new Color32(255, 255, 255, 255)),
            };
            var stackDesc = new VTStackDesc(
                PageSize,
                BorderSize,
                CachePageCount,
                layers,
                MaxUploadsPerFrame,
                FeedbackCapacity,
                neighborPrefetchCount: 1);
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

        private VividVirtualTextureAsset ResolveStreamedAsset(VividVirtualTextureAsset asset)
        {
            if (asset == null)
                return null;

            VividVirtualTextureBuiltData builtData = asset.BuiltData;
            bool valid = builtData != null
                         && builtData.BuildProfile == VividVirtualTextureBuildProfile.GPUDrivenSurface
                         && builtData.PageSize == PageSize
                         && builtData.BorderSize == BorderSize
                         && builtData.VirtualPageCountX == builtData.VirtualPageCountY
                         && builtData.VirtualPageCountX > 0
                         && builtData.VirtualPageCountX <= MaxAllocationPageCount
                         && Mathf.IsPowerOfTwo(builtData.VirtualPageCountX)
                         && builtData.MipCount == Mathf.RoundToInt(Mathf.Log(builtData.VirtualPageCountX, 2.0f)) + 1
                         && builtData.MatchesStack(VirtualTextureSpaceDesc.StackDesc)
                         && (builtData.HasInlineRawData || builtData.HasStreamData);
            if (valid)
                return asset;

            EntityId assetId = asset.GetEntityId();
            if (m_InvalidStreamedAssetWarningIds.Add(assetId))
            {
                Debug.LogWarning(
                    $"[VividRP] Streamed VT asset '{asset.name}' is not a compatible GPUDrivenSurface build. "
                    + "The material will use its Texture2D fallback producer until the asset is rebuilt.",
                    asset);
            }

            return null;
        }

        private static int ResolveAllocationPageCount(Texture2D baseColor, Texture2D normal, Texture2D mask)
        {
            int maxDimension = 1;
            ResolveMaxDimension(baseColor, ref maxDimension);
            ResolveMaxDimension(normal, ref maxDimension);
            ResolveMaxDimension(mask, ref maxDimension);
            int requiredPages = Mathf.Max(1, Mathf.CeilToInt((float) maxDimension / PageSize));
            return Mathf.Min(MaxAllocationPageCount, Mathf.NextPowerOfTwo(requiredPages));
        }

        private static void ResolveMaxDimension(Texture2D texture, ref int maxDimension)
        {
            if (texture != null)
                maxDimension = Mathf.Max(maxDimension, texture.width, texture.height);
        }

        private bool TryAllocatePageRegion(int pageCount, out RectInt region)
        {
            int size = Mathf.Clamp(Mathf.NextPowerOfTwo(pageCount), 1, MaxAllocationPageCount);
            if (!TryFindFreePageRegion(size, out region))
                return false;

            MarkPageRegionAllocated(region.x, region.y, size);
            m_AllocatedPageCount += size * size;
            return true;
        }

        private bool TryFindFreePageRegion(int size, out RectInt region)
        {
            for (int y = 0; y + size <= AtlasPageCount; y += size)
            {
                for (int x = 0; x + size <= AtlasPageCount; x += size)
                {
                    if (!IsPageRegionFree(x, y, size))
                        continue;

                    region = new RectInt(x, y, size, size);
                    return true;
                }
            }

            region = default;
            return false;
        }

        private int GetLargestFreeAllocationPageCount()
        {
            for (int size = MaxAllocationPageCount; size >= 1; size >>= 1)
            {
                if (TryFindFreePageRegion(size, out _))
                    return size;
            }

            return 0;
        }

        private bool IsPageRegionFree(int startX, int startY, int size)
        {
            for (int y = startY; y < startY + size; y++)
            {
                for (int x = startX; x < startX + size; x++)
                {
                    if (m_AllocatedPages[x, y])
                        return false;
                }
            }

            return true;
        }

        private void ReleasePageRegion(RectInt region)
        {
            int releasedPageCount = 0;
            for (int y = region.yMin; y < region.yMax; y++)
            {
                for (int x = region.xMin; x < region.xMax; x++)
                {
                    if (!m_AllocatedPages[x, y])
                        continue;

                    m_AllocatedPages[x, y] = false;
                    releasedPageCount += 1;
                }
            }

            m_AllocatedPageCount = Mathf.Max(0, m_AllocatedPageCount - releasedPageCount);
        }

        private void MarkPageRegionAllocated(int startX, int startY, int size)
        {
            for (int y = startY; y < startY + size; y++)
            {
                for (int x = startX; x < startX + size; x++)
                    m_AllocatedPages[x, y] = true;
            }
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
            m_ReleaseAtlasRegions.Clear();
            for (int entryIndex = 0; entryIndex < bindingEntries.Count; entryIndex++)
            {
                BindingEntry bindingEntry = bindingEntries[entryIndex];
                if (bindingEntry == null || !m_Bindings.Remove(bindingEntry.Key))
                    continue;

                if (!bindingEntry.HasAllocation)
                    continue;

                releasedAllocation = true;
                m_ReleaseAtlasRegions.Add(bindingEntry.PageRegion);
                m_Producer?.UnregisterEntry(bindingEntry.PageRegion);
                if (!bindingEntry.MipTailResident)
                    m_PendingMipTailEntries.Remove(bindingEntry);
                else
                    m_ResidentMipTailCount = Mathf.Max(0, m_ResidentMipTailCount - 1);
                m_QueuedMipTailCount = Mathf.Max(0, m_QueuedMipTailCount - 1);

                for (int mip = 0; mip <= bindingEntry.MaxMip; mip++)
                    m_ReleaseRegions.Add(new VTPageRegion(mip, GetRegionAtMip(bindingEntry.PageRegion, mip)));
            }

            if (m_ReleaseRegions.Count > 0
                && VirtualTextureSpaceId > 0
                && VirtualTextureSystem.IsInitialized)
            {
                VirtualTextureSystem.FlushRegions(VirtualTextureSpaceId, m_ReleaseRegions);
            }

            for (int regionIndex = 0; regionIndex < m_ReleaseAtlasRegions.Count; regionIndex++)
                ReleasePageRegion(m_ReleaseAtlasRegions[regionIndex]);

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
