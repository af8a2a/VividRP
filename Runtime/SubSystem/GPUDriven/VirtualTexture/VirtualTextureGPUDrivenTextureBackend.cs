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
        IGPUDrivenVirtualTextureBackend
    {
        internal const int PageSize = 128;
        internal const int BorderSize = 4;
        internal const int AtlasPageCount = 128;
        internal const int MaxAllocationPageCount = AtlasPageCount / 2;
        internal const string SpaceName = "VividGPUDriven.StaticMesh";

        private const int CachePageCount = 512;
        private const int MaxUploadsPerFrame = 16;
        private const int FeedbackCapacity = 65536;
        private const int ResourceLayerBitCount = 8;

        private readonly struct TextureSetKey : IEquatable<TextureSetKey>
        {
            internal TextureSetKey(
                Texture2D baseColor,
                Texture2D normal,
                Texture2D mask,
                GPUDrivenSurfaceAddressMode addressMode)
            {
                BaseColorId = baseColor != null ? baseColor.GetEntityId() : EntityId.None;
                NormalId = normal != null ? normal.GetEntityId() : EntityId.None;
                MaskId = mask != null ? mask.GetEntityId() : EntityId.None;
                AddressMode = addressMode;
            }

            private EntityId BaseColorId { get; }

            private EntityId NormalId { get; }

            private EntityId MaskId { get; }

            private GPUDrivenSurfaceAddressMode AddressMode { get; }

            public bool Equals(TextureSetKey other)
            {
                return BaseColorId == other.BaseColorId
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
                return HashCode.Combine(BaseColorId, NormalId, MaskId, AddressMode);
            }
        }

        private readonly Dictionary<TextureSetKey, VividSurfaceBindingData> m_Bindings = new();
        private readonly HashSet<EntityId> m_RegisteredTextureIds = new();
        private readonly HashSet<EntityId> m_UnsupportedTextureWarningIds = new();
        private readonly List<VirtualTexturePageCoord> m_PendingMipTailCoords = new();
        private readonly bool[,] m_AllocatedPages = new bool[AtlasPageCount, AtlasPageCount];
        private readonly GPUDrivenVirtualTextureProducer m_Producer;

        private uint m_BindingRevision = 1;
        private uint m_CreateResourceCallCountThisFrame;
        private int m_AllocatedPageCount;
        private int m_QueuedMipTailCount;
        private int m_ResidentMipTailCount;
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

        internal int AllocatedPageCount => m_AllocatedPageCount;

        internal int ResidentMipTailCount => m_ResidentMipTailCount;

        internal int QueuedMipTailCount => m_QueuedMipTailCount;

        public void PrepareFrame()
        {
            ThrowIfDisposed();
            if (!IsAvailable)
                return;

            for (int tailIndex = m_PendingMipTailCoords.Count - 1; tailIndex >= 0; tailIndex--)
            {
                if (VirtualTextureSystem.TryGetPageTableEntry(
                        VirtualTextureSpaceId,
                        m_PendingMipTailCoords[tailIndex],
                        out VirtualTexturePageTableEntry entry)
                    && entry.Resident
                    && entry.Locked)
                {
                    int lastTailIndex = m_PendingMipTailCoords.Count - 1;
                    m_PendingMipTailCoords[tailIndex] = m_PendingMipTailCoords[lastTailIndex];
                    m_PendingMipTailCoords.RemoveAt(lastTailIndex);
                    m_ResidentMipTailCount += 1;
                }
            }
        }

        public void ResetPerFrameStats()
        {
            ThrowIfDisposed();
            m_CreateResourceCallCountThisFrame = 0;
        }

        public VividSurfaceBindingData CreateSurfaceBinding(in GPUDrivenSurfaceTextureSet textures)
        {
            ThrowIfDisposed();
            if (!IsAvailable || m_Producer == null)
                return CreateEmptyBinding();

            Texture2D baseColor = ResolveTexture2D(textures.BaseColor);
            Texture2D normal = ResolveTexture2D(textures.Normal);
            Texture2D mask = ResolveTexture2D(textures.Mask);
            var key = new TextureSetKey(baseColor, normal, mask, textures.AddressMode);
            if (m_Bindings.TryGetValue(key, out VividSurfaceBindingData existingBinding))
                return existingBinding;

            if (baseColor == null && normal == null && mask == null)
            {
                VividSurfaceBindingData emptyBinding = CreateEmptyBinding();
                m_Bindings.Add(key, emptyBinding);
                return emptyBinding;
            }

            int allocationPageCount = ResolveAllocationPageCount(baseColor, normal, mask);
            if (!TryAllocatePageRegion(allocationPageCount, out RectInt pageRegion))
            {
                Debug.LogWarning(
                    $"[VividRP] GPUDriven VT atlas is full. Could not allocate a {allocationPageCount}x{allocationPageCount} page region.");
                VividSurfaceBindingData emptyBinding = CreateEmptyBinding();
                m_Bindings.Add(key, emptyBinding);
                return emptyBinding;
            }

            int maxMip = Mathf.RoundToInt(Mathf.Log(allocationPageCount, 2.0f));
            bool repeat = textures.AddressMode == GPUDrivenSurfaceAddressMode.Repeat;
            WarnAddressModeFallback(textures, baseColor, normal, mask);
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
                VividSurfaceBindingData fallbackBinding = CreateEmptyBinding();
                m_Bindings.Add(key, fallbackBinding);
                return fallbackBinding;
            }

            m_PendingMipTailCoords.Add(mipTailCoord);
            m_QueuedMipTailCount += 1;

            VividSurfaceBindingFlags flags = VividSurfaceBindingFlags.None;
            uint baseColorResource = CreateResource(baseColor, GPUDrivenVirtualTextureProducer.BaseColorLayerIndex, maxMip, VividSurfaceBindingFlags.BaseColor, ref flags);
            uint normalResource = CreateResource(normal, GPUDrivenVirtualTextureProducer.NormalLayerIndex, maxMip, VividSurfaceBindingFlags.Normal, ref flags);
            uint maskResource = CreateResource(mask, GPUDrivenVirtualTextureProducer.MaskLayerIndex, maxMip, VividSurfaceBindingFlags.Mask, ref flags);
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

            m_Bindings.Add(key, binding);
            m_CreateResourceCallCountThisFrame += 1;
            IncrementBindingRevision();
            return binding;
        }

        public GPUDrivenTextureBackendStats GetStats()
        {
            ThrowIfDisposed();
            return new GPUDrivenTextureBackendStats(
                poolCount: IsAvailable ? 1u : 0u,
                resourceCapacity: AtlasPageCount * AtlasPageCount,
                allocatedResourceCount: (uint) m_RegisteredTextureIds.Count,
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
            m_PendingMipTailCoords.Clear();
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
                || (copySupport & CopyTextureSupport.RTToTexture) == 0)
            {
                unavailableReason = "The active graphics device cannot copy a RenderTexture array into the VT Texture2DArray cache.";
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
            for (int y = 0; y + size <= AtlasPageCount; y += size)
            {
                for (int x = 0; x + size <= AtlasPageCount; x += size)
                {
                    if (!IsPageRegionFree(x, y, size))
                        continue;

                    MarkPageRegionAllocated(x, y, size);
                    region = new RectInt(x, y, size, size);
                    m_AllocatedPageCount += size * size;
                    return true;
                }
            }

            region = default;
            return false;
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
