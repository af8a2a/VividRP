using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime.SubSystem.Decal;

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
        IGPUDrivenVirtualTextureBackend,
        IGPUDrivenTerrainRuntimeVirtualTextureBackend
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
        private const int TerrainRuntimeVirtualTexturePageBudget = 4;
        private const int InitialSurfaceBindingCapacity = 16;

        private static readonly VTStackDesc s_CompatibleStreamedStackDesc = CreateSpaceDesc(
            ResolveDescriptorProfile(GPUDrivenVirtualTexturePhysicalPoolQuality.Medium)).StackDesc;
        private static readonly int s_TerrainRVTRecordsId = Shader.PropertyToID("_VividTerrainRVTRecords");
        private static readonly int s_TerrainRVTLevelsId = Shader.PropertyToID("_VividTerrainRVTLevels");
        private static readonly int s_TerrainRVTRecordCountId = Shader.PropertyToID("_VividTerrainRVTRecordCount");
        private static readonly int s_TerrainRVTEnabledId = Shader.PropertyToID("_VividTerrainRVTEnabled");

        internal readonly struct TextureSetKey : IEquatable<TextureSetKey>
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

        internal struct BindingEntry
        {
            internal VividSurfaceBindingData Binding;
            internal Texture2D BaseColor;
            internal Texture2D Normal;
            internal Texture2D Mask;
            internal VividVirtualTextureAsset StreamedAsset;
            internal RectInt PageRegion;
            internal GPUDrivenVirtualTextureAtlasAllocator.Allocation AtlasAllocation;
            internal RectInt MipTailRegion;
            internal int MaxMip;
            internal int ResidentMipTailPageCount;
            internal uint LastTouchedUpdate;
            internal uint CreatedUpdate;
            internal bool HasAllocation;
            internal bool MipTailResident;
        }

        internal sealed class ExternalSurfaceBindingLease : IDisposable
        {
            private VirtualTextureGPUDrivenTextureBackend m_Owner;
            private readonly TextureSetKey m_Key;

            internal ExternalSurfaceBindingLease(
                VirtualTextureGPUDrivenTextureBackend owner,
                in TextureSetKey key,
                in BindingEntry entry)
            {
                m_Owner = owner;
                m_Key = key;
                Binding = entry.Binding;
                PageRegion = entry.PageRegion;
                MaxMip = entry.MaxMip;
            }

            internal VividSurfaceBindingData Binding { get; }
            internal RectInt PageRegion { get; }
            internal int MaxMip { get; }

            public void Dispose()
            {
                VirtualTextureGPUDrivenTextureBackend owner = m_Owner;
                if (owner == null)
                    return;

                m_Owner = null;
                owner.ReleaseExternalSurfaceBinding(m_Key);
            }
        }

        private sealed class TerrainRVTRegistration
        {
            internal VividTerrain Terrain;
            internal VividTerrainData TerrainData;
            internal EntityId EntityId;
            internal uint Revision;
            internal uint RecordIndex;
            internal uint LastTouchedUpdate;
            internal uint CreatedUpdate;
        }

        private sealed class TerrainRVTCameraState
        {
            internal Camera Camera;
            internal EntityId CameraId;
            internal readonly Dictionary<EntityId, TerrainRuntimeVirtualTextureClipmap> Clipmaps = new();
            internal int LastScheduledFrame = -1;
        }

        private readonly Dictionary<TextureSetKey, BindingEntry> m_Bindings =
            new(InitialSurfaceBindingCapacity);
        private readonly Dictionary<TextureSetKey, int> m_ExternalBindingRefCounts =
            new(InitialSurfaceBindingCapacity);
        private readonly HashSet<EntityId> m_RegisteredTextureIds = new(InitialSurfaceBindingCapacity);
        private readonly HashSet<EntityId> m_UnsupportedTextureWarningIds = new();
        private readonly HashSet<EntityId> m_InvalidStreamedAssetWarningIds = new();
        private readonly Dictionary<EntityId, uint> m_PermanentlyFailedStreamedAssets = new();
        private readonly HashSet<EntityId> m_IncompatibleScalarMaskWarningIds = new();
        private readonly HashSet<EntityId> m_InvalidTerrainDecalWarningIds = new();
        private readonly HashSet<EntityId> m_InvalidTerrainDecalDataWarningIds = new();
        private readonly List<TextureSetKey> m_PendingMipTailEntries = new(InitialSurfaceBindingCapacity);
        private readonly List<TextureSetKey> m_ReleaseEntries = new(InitialSurfaceBindingCapacity);
        private readonly List<VTPageRegion> m_ReleaseRegions = new();
        private readonly List<BindingEntry> m_ReleaseAllocationEntries = new(InitialSurfaceBindingCapacity);
        private readonly Dictionary<EntityId, TerrainRVTRegistration> m_TerrainRVTRegistrations = new();
        private readonly List<TerrainRVTRegistration> m_TerrainRVTRecords = new();
        private readonly Dictionary<EntityId, TerrainRVTCameraState> m_TerrainRVTCameraStates = new();
        private readonly Dictionary<TerrainRuntimeVirtualTextureClipmap, GPUDrivenVirtualTextureAtlasAllocator.Allocation[]>
            m_TerrainRVTAllocations = new();
        private readonly List<TerrainRVTRegistration> m_TerrainRVTReleaseEntries = new();
        private readonly List<TerrainRVTCameraState> m_TerrainRVTCameraReleaseEntries = new();
        private readonly List<TerrainRuntimeVirtualTextureClipmap> m_TerrainRVTClipmapReleaseEntries = new();
        private readonly List<TerrainRuntimeVirtualTextureClipmap.PageCandidate> m_TerrainRVTCandidates = new();
        private readonly List<VTPageRegion> m_TerrainRVTFlushRegions = new();
        private readonly ComputeShader m_TerrainRVTPageProducerCompute;
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
        private readonly bool m_TerrainRVTRequested;
        private TerrainRVTCameraState m_CurrentTerrainRVTCameraState;
        private GraphicsBuffer m_TerrainRVTRecordBuffer;
        private GraphicsBuffer m_TerrainRVTLevelBuffer;
        private TerrainRuntimeVirtualTextureRecordGPUData[] m_TerrainRVTRecordUpload =
            Array.Empty<TerrainRuntimeVirtualTextureRecordGPUData>();
        private TerrainRuntimeVirtualTextureLevelGPUData[] m_TerrainRVTLevelUpload =
            Array.Empty<TerrainRuntimeVirtualTextureLevelGPUData>();
        private bool m_IsDisposed;
        private bool m_TerrainDecalConfigurationWarningIssued;

        internal VirtualTextureGPUDrivenTextureBackend()
            : this(
                PipelineResourceManager.Get<VividRPCoreResources>()?.GPUDrivenVirtualTexturePageProducerCompute,
                GPUDrivenVirtualTextureRuntimeCapabilities.Instance,
                ResolveDescriptorProfile(GPUDrivenVirtualTexturePhysicalPoolQuality.Medium),
                VividRenderPipelineAsset.GetActiveAsset()?.EnableTerrainRuntimeVirtualTexture == true)
        {
        }

        internal VirtualTextureGPUDrivenTextureBackend(
            GPUDrivenVirtualTextureDescriptorProfile descriptorProfile)
            : this(
                PipelineResourceManager.Get<VividRPCoreResources>()?.GPUDrivenVirtualTexturePageProducerCompute,
                GPUDrivenVirtualTextureRuntimeCapabilities.Instance,
                descriptorProfile,
                VividRenderPipelineAsset.GetActiveAsset()?.EnableTerrainRuntimeVirtualTexture == true)
        {
        }

        internal VirtualTextureGPUDrivenTextureBackend(
            GPUDrivenVirtualTextureDescriptorProfile descriptorProfile,
            bool enableTerrainRuntimeVirtualTexture)
            : this(
                PipelineResourceManager.Get<VividRPCoreResources>()?.GPUDrivenVirtualTexturePageProducerCompute,
                GPUDrivenVirtualTextureRuntimeCapabilities.Instance,
                descriptorProfile,
                enableTerrainRuntimeVirtualTexture)
        {
        }

        internal VirtualTextureGPUDrivenTextureBackend(
            ComputeShader pageProducerCompute,
            IGPUDrivenVirtualTextureRuntimeCapabilities capabilities)
            : this(
                pageProducerCompute,
                capabilities,
                ResolveDescriptorProfile(GPUDrivenVirtualTexturePhysicalPoolQuality.Medium),
                false)
        {
        }

        internal VirtualTextureGPUDrivenTextureBackend(
            ComputeShader pageProducerCompute,
            IGPUDrivenVirtualTextureRuntimeCapabilities capabilities,
            GPUDrivenVirtualTextureDescriptorProfile descriptorProfile)
            : this(pageProducerCompute, capabilities, descriptorProfile, false)
        {
        }

        internal VirtualTextureGPUDrivenTextureBackend(
            ComputeShader pageProducerCompute,
            IGPUDrivenVirtualTextureRuntimeCapabilities capabilities,
            GPUDrivenVirtualTextureDescriptorProfile descriptorProfile,
            bool enableTerrainRuntimeVirtualTexture)
        {
            m_TerrainRVTRequested = enableTerrainRuntimeVirtualTexture;
            m_TerrainRVTPageProducerCompute = PipelineResourceManager
                .Get<VividRPCoreResources>()
                ?.TerrainRuntimeVirtualTexturePageProducerCompute;
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

        public bool TerrainRuntimeVirtualTextureEnabled =>
            m_TerrainRVTRequested
            && IsAvailable
            && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
            && m_TerrainRVTPageProducerCompute != null
            && m_TerrainRVTPageProducerCompute.HasKernel("CS")
            && VTRuntimeBlockCompressor.IsAvailable(out _)
            && SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_UInt, GraphicsFormatUsage.LoadStore)
            && SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_UInt, GraphicsFormatUsage.LoadStore);

        internal bool TerrainRuntimeVirtualTextureRequested => m_TerrainRVTRequested;

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

        internal int TerrainRuntimeVirtualTextureRecordCount => m_TerrainRVTRegistrations.Count;

        public void PrepareFrame()
        {
            ThrowIfDisposed();
            if (!IsAvailable)
                return;

            PurgeInactiveTerrainRVTCameras();

            if (m_RetrySurfaceBindingUpdate)
            {
                m_RetrySurfaceBindingUpdate = false;
                IncrementBindingRevision();
            }

            m_ReleaseEntries.Clear();
            for (int tailIndex = m_PendingMipTailEntries.Count - 1; tailIndex >= 0; tailIndex--)
            {
                TextureSetKey bindingKey = m_PendingMipTailEntries[tailIndex];
                BindingEntry bindingEntry = m_Bindings[bindingKey];
                int residentPageCount = 0;
                bool tailFailed = m_Producer.HasPermanentStreamFailure(bindingEntry.PageRegion);
                int mipTailPageCount = GetPageCount(bindingEntry.MipTailRegion);
                for (int pageIndex = 0; pageIndex < mipTailPageCount; pageIndex++)
                {
                    if (VirtualTextureSystem.TryGetPageTableEntry(
                            VirtualTextureSpaceId,
                            GetPageCoord(bindingEntry.MipTailRegion, bindingEntry.MaxMip, pageIndex),
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
                m_Bindings[bindingKey] = bindingEntry;
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
                    m_ReleaseEntries.Add(bindingKey);
                    m_RetrySurfaceBindingUpdate = true;
                    continue;
                }

                if (residentPageCount != mipTailPageCount)
                    continue;

                int lastTailIndex = m_PendingMipTailEntries.Count - 1;
                m_PendingMipTailEntries[tailIndex] = m_PendingMipTailEntries[lastTailIndex];
                m_PendingMipTailEntries.RemoveAt(lastTailIndex);
                bindingEntry.MipTailResident = true;
                m_Bindings[bindingKey] = bindingEntry;
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

        public bool TryGetOrCreateTerrainRuntimeVirtualTexture(
            VividTerrain terrain,
            VividTerrainData terrainData,
            uint revision,
            out uint recordIndex)
        {
            recordIndex = VividSurfaceBindingData.InvalidResource;
            if (!CanCreateTerrainRuntimeVirtualTexture(terrain, terrainData))
                return false;

            EntityId terrainId = terrain.GetEntityId();
            if (m_TerrainRVTRegistrations.TryGetValue(
                    terrainId,
                    out TerrainRVTRegistration existingRegistration))
            {
                if (existingRegistration.Revision == revision)
                {
                    TouchTerrainRVT(existingRegistration);
                    recordIndex = existingRegistration.RecordIndex;
                    return true;
                }

                ReleaseTerrainRVT(existingRegistration);
            }

            int recordSlot = FindTerrainRVTRecordSlot();
            var registration = new TerrainRVTRegistration
            {
                Terrain = terrain,
                TerrainData = terrainData,
                EntityId = terrainId,
                Revision = revision,
                RecordIndex = (uint)recordSlot,
            };
            TouchNewTerrainRVT(registration);
            m_TerrainRVTRecords[recordSlot] = registration;
            m_TerrainRVTRegistrations.Add(terrainId, registration);
            IncrementBindingRevision();
            recordIndex = registration.RecordIndex;
            return true;
        }

        private bool TryCreateTerrainRVTClipmap(
            TerrainRVTCameraState cameraState,
            TerrainRVTRegistration registration)
        {
            if (cameraState.Clipmaps.ContainsKey(registration.EntityId))
                return true;

            var allocations = new GPUDrivenVirtualTextureAtlasAllocator.Allocation[
                TerrainRuntimeVirtualTextureClipmap.LevelCount];
            var pageRegions = new RectInt[TerrainRuntimeVirtualTextureClipmap.LevelCount];
            int allocatedLevelCount = 0;
            TerrainRuntimeVirtualTextureClipmap clipmap = null;
            try
            {
                for (int levelIndex = 0;
                     levelIndex < TerrainRuntimeVirtualTextureClipmap.LevelCount;
                     levelIndex++)
                {
                    if (!m_AtlasAllocator.TryAllocate(
                            TerrainRuntimeVirtualTextureClipmap.WindowPageCount,
                            TerrainRuntimeVirtualTextureClipmap.WindowPageCount,
                            out allocations[levelIndex]))
                    {
                        return false;
                    }

                    pageRegions[levelIndex] = allocations[levelIndex].PageRegion;
                    allocatedLevelCount += 1;
                }

                clipmap = new TerrainRuntimeVirtualTextureClipmap(
                    registration.Terrain,
                    registration.TerrainData,
                    this,
                    m_TerrainRVTPageProducerCompute,
                    pageRegions,
                    registration.Revision)
                {
                    RecordIndex = registration.RecordIndex,
                };
                int registeredLevelCount = 0;
                try
                {
                    for (int levelIndex = 0; levelIndex < clipmap.Levels.Length; levelIndex++)
                    {
                        m_Producer.RegisterRuntimeEntry(pageRegions[levelIndex], clipmap.Levels[levelIndex]);
                        registeredLevelCount += 1;
                    }
                }
                catch
                {
                    for (int levelIndex = 0; levelIndex < registeredLevelCount; levelIndex++)
                        m_Producer.UnregisterEntry(pageRegions[levelIndex]);
                    clipmap.Dispose();
                    throw;
                }

                cameraState.Clipmaps.Add(registration.EntityId, clipmap);
                m_TerrainRVTAllocations.Add(clipmap, allocations);
                return true;
            }
            finally
            {
                if (clipmap == null || !cameraState.Clipmaps.ContainsKey(registration.EntityId))
                {
                    for (int levelIndex = 0; levelIndex < allocatedLevelCount; levelIndex++)
                        m_AtlasAllocator.Release(allocations[levelIndex]);
                }
            }
        }

        public void UpdateTerrainRuntimeVirtualTextures(Camera renderingCamera, int frameIndex)
        {
            m_CurrentTerrainRVTCameraState = null;
            bool terrainDecalsEnabled = ResolveTerrainDecalTechniqueEnabled();
            if (!TerrainRuntimeVirtualTextureEnabled
                || ResolveTerrainRVTCamera(renderingCamera) == null
                || m_TerrainRVTRegistrations.Count == 0)
            {
                return;
            }

            TerrainRVTCameraState cameraState = GetOrCreateTerrainRVTCameraState(renderingCamera);
            m_CurrentTerrainRVTCameraState = cameraState;
            foreach (TerrainRVTRegistration registration in m_TerrainRVTRegistrations.Values)
                TryCreateTerrainRVTClipmap(cameraState, registration);

            int resolvedFrameIndex = frameIndex >= 0 ? frameIndex : Time.frameCount;
            if (cameraState.LastScheduledFrame == resolvedFrameIndex)
                return;

            cameraState.LastScheduledFrame = resolvedFrameIndex;
            TerrainVirtualTextureDecalSnapshot decalSnapshot =
                DecalSystem.GetTerrainVirtualTextureSnapshot();
            m_TerrainRVTFlushRegions.Clear();
            foreach (TerrainRuntimeVirtualTextureClipmap clipmap in cameraState.Clipmaps.Values)
            {
                Material terrainMaterial = clipmap.TerrainData.SourceMaterial;
                bool materialReceivesDecals = terrainMaterial != null
                                               && terrainMaterial.HasProperty("_SupportDecals")
                                               && terrainMaterial.GetFloat("_SupportDecals") > 0.5f;
                bool receiveVirtualTextureDecals = terrainDecalsEnabled && materialReceivesDecals;
                if (receiveVirtualTextureDecals
                    && !clipmap.TerrainData.TryValidateRuntimeDecalProjection(out string reason)
                    && m_InvalidTerrainDecalDataWarningIds.Add(clipmap.EntityId))
                {
                    Debug.LogWarning(
                        $"[VividRP] Terrain RVT decals are disabled for '{clipmap.Terrain.name}': {reason}",
                        clipmap.Terrain);
                }
                clipmap.QueueDecalSnapshot(
                    decalSnapshot,
                    receiveVirtualTextureDecals,
                    materialReceivesDecals);
                for (int levelIndex = 0; levelIndex < clipmap.Levels.Length; levelIndex++)
                    clipmap.Levels[levelIndex].RetireResidentPages(VirtualTextureSpaceId);
                clipmap.TryApplyPendingDecalSnapshot();
                clipmap.UpdateCamera(renderingCamera.transform.position, m_TerrainRVTFlushRegions);
            }
            if (m_TerrainRVTFlushRegions.Count > 0)
                VirtualTextureSystem.FlushRegions(VirtualTextureSpaceId, m_TerrainRVTFlushRegions);

            m_TerrainRVTCandidates.Clear();
            foreach (TerrainRuntimeVirtualTextureClipmap clipmap in cameraState.Clipmaps.Values)
            {
                for (int levelIndex = 0; levelIndex < clipmap.Levels.Length; levelIndex++)
                    clipmap.Levels[levelIndex].GatherCandidates(m_TerrainRVTCandidates);
            }
            m_TerrainRVTCandidates.Sort(CompareTerrainRVTCandidates);
            int scheduledPageCount = 0;
            for (int candidateIndex = 0;
                 candidateIndex < m_TerrainRVTCandidates.Count
                 && scheduledPageCount < TerrainRuntimeVirtualTexturePageBudget;
                 candidateIndex++)
            {
                TerrainRuntimeVirtualTextureClipmap.PageCandidate candidate =
                    m_TerrainRVTCandidates[candidateIndex];
                if (candidate.Level.TryApproveAndQueue(
                        VirtualTextureSpaceId,
                        resolvedFrameIndex,
                        candidate.CellIndex))
                {
                    scheduledPageCount += 1;
                }
            }
        }

        private bool ResolveTerrainDecalTechniqueEnabled()
        {
            VividRenderPipelineAsset asset = VividRenderPipelineAsset.GetActiveAsset();
            if (asset == null
                || !asset.EnableGPUDrivenDecal
                || asset.DecalTechnique != VividDecalTechnique.TerrainRuntimeVirtualTexture)
            {
                return false;
            }

            if (!asset.TryValidateTerrainRuntimeVirtualTextureDecals(out _))
                return false;

            bool valid = TerrainRuntimeVirtualTextureEnabled;
            if (!valid && !m_TerrainDecalConfigurationWarningIssued)
            {
                m_TerrainDecalConfigurationWarningIssued = true;
                Debug.LogWarning(
                    "[VividRP] Terrain Runtime Virtual Texture decals are disabled: "
                    + "the active Terrain RVT backend does not expose the required DX12 compute capabilities. "
                    + "The renderer will not fall back to Clustered Bindless decals.",
                    asset);
            }
            return valid;
        }

        internal void WarnInvalidTerrainDecal(DecalProjector projector, string reason)
        {
            if (projector == null || !m_InvalidTerrainDecalWarningIds.Add(projector.GetEntityId()))
                return;

            Debug.LogWarning(
                $"[VividRP] Decal Projector '{projector.name}' was skipped by the Terrain RVT decal backend: {reason}",
                projector);
        }

        internal bool HasPermanentStreamFailure(RectInt pageRegion)
        {
            return m_Producer?.HasPermanentStreamFailure(pageRegion) == true;
        }

        public void BindTerrainRuntimeVirtualTextureGlobals(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException(nameof(cmd));

            TerrainRVTCameraState cameraState = m_CurrentTerrainRVTCameraState;
            int recordCount = cameraState != null && cameraState.Clipmaps.Count > 0
                ? m_TerrainRVTRecords.Count
                : 0;
            if (recordCount > 0)
            {
                // Record slots stay terrain-stable while their contents are selected per camera.
                // Queue the upload with this camera's rendering commands to preserve submission order.
                UploadTerrainRVTBuffers(cmd, cameraState);
            }
            cmd.SetGlobalInt(s_TerrainRVTRecordCountId, recordCount);
            cmd.SetGlobalInt(s_TerrainRVTEnabledId, recordCount > 0 ? 1 : 0);
            if (recordCount > 0 && m_TerrainRVTRecordBuffer != null)
                cmd.SetGlobalBuffer(s_TerrainRVTRecordsId, m_TerrainRVTRecordBuffer);
            if (recordCount > 0 && m_TerrainRVTLevelBuffer != null)
                cmd.SetGlobalBuffer(s_TerrainRVTLevelsId, m_TerrainRVTLevelBuffer);
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
            foreach (KeyValuePair<TextureSetKey, BindingEntry> bindingPair in m_Bindings)
            {
                if (bindingPair.Value.LastTouchedUpdate != m_SurfaceBindingUpdate
                    && !m_ExternalBindingRefCounts.ContainsKey(bindingPair.Key))
                    m_ReleaseEntries.Add(bindingPair.Key);
            }

            m_SurfaceBindingUpdateActive = false;
            ReleaseBindingEntries(m_ReleaseEntries);
            m_ReleaseEntries.Clear();

            m_TerrainRVTReleaseEntries.Clear();
            foreach (TerrainRVTRegistration registration in m_TerrainRVTRegistrations.Values)
            {
                if (registration.LastTouchedUpdate != m_SurfaceBindingUpdate)
                    m_TerrainRVTReleaseEntries.Add(registration);
            }
            for (int registrationIndex = 0;
                 registrationIndex < m_TerrainRVTReleaseEntries.Count;
                 registrationIndex++)
            {
                ReleaseTerrainRVT(m_TerrainRVTReleaseEntries[registrationIndex]);
            }
            m_TerrainRVTReleaseEntries.Clear();
        }

        public void CancelSurfaceBindingUpdate()
        {
            if (m_IsDisposed || !m_SurfaceBindingUpdateActive)
                return;

            m_ReleaseEntries.Clear();
            foreach (KeyValuePair<TextureSetKey, BindingEntry> bindingPair in m_Bindings)
            {
                if (bindingPair.Value.CreatedUpdate == m_SurfaceBindingUpdate
                    && !m_ExternalBindingRefCounts.ContainsKey(bindingPair.Key))
                    m_ReleaseEntries.Add(bindingPair.Key);
            }

            m_SurfaceBindingUpdateActive = false;
            ReleaseBindingEntries(m_ReleaseEntries);
            m_ReleaseEntries.Clear();

            m_TerrainRVTReleaseEntries.Clear();
            foreach (TerrainRVTRegistration registration in m_TerrainRVTRegistrations.Values)
            {
                if (registration.CreatedUpdate == m_SurfaceBindingUpdate)
                    m_TerrainRVTReleaseEntries.Add(registration);
            }
            for (int registrationIndex = 0;
                 registrationIndex < m_TerrainRVTReleaseEntries.Count;
                 registrationIndex++)
            {
                ReleaseTerrainRVT(m_TerrainRVTReleaseEntries[registrationIndex]);
            }
            m_TerrainRVTReleaseEntries.Clear();
        }

        public VividSurfaceBindingData CreateSurfaceBinding(in GPUDrivenSurfaceTextureSet textures)
        {
            return CreateSurfaceBinding(textures, allowRuntimeTextureProducer: false);
        }

        private VividSurfaceBindingData CreateSurfaceBinding(
            in GPUDrivenSurfaceTextureSet textures,
            bool allowRuntimeTextureProducer)
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
                if (allowRuntimeTextureProducer
                    && streamedAsset == null
                    && !existingEntry.HasAllocation
                    && (baseColor != null || normal != null || mask != null))
                {
                    m_Bindings.Remove(key);
                }
                else
                {
                    TouchBindingEntry(ref existingEntry);
                    m_Bindings[key] = existingEntry;
                    return existingEntry.Binding;
                }
            }

            if (streamedAsset == null && baseColor == null && normal == null && mask == null)
            {
                var emptyEntry = new BindingEntry
                {
                    Binding = CreateEmptyBinding(),
                };
                TouchNewBindingEntry(ref emptyEntry);
                m_Bindings.Add(key, emptyEntry);
                return emptyEntry.Binding;
            }

            if (streamedAsset == null
                && !allowRuntimeTextureProducer
                && (baseColor != null || normal != null || mask != null))
            {
                WarnLegacyTextureFallback(baseColor, normal, mask);
                var fallbackEntry = new BindingEntry
                {
                    Binding = CreateEmptyBinding(),
                };
                TouchNewBindingEntry(ref fallbackEntry);
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
            int mipTailPageCount = GetPageCount(mipTailRegion);
            int queuedMipTailPageCount = 0;
            VirtualTexturePageCoord failedMipTailCoord = default;
            string mipTailFailureReason = string.Empty;
            for (int pageIndex = 0; pageIndex < mipTailPageCount; pageIndex++)
            {
                failedMipTailCoord = GetPageCoord(mipTailRegion, maxMip, pageIndex);
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

            if (queuedMipTailPageCount != mipTailPageCount)
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
                Binding = binding,
                BaseColor = streamedAsset == null ? baseColor : null,
                Normal = streamedAsset == null ? normal : null,
                Mask = streamedAsset == null ? mask : null,
                StreamedAsset = streamedAsset,
                PageRegion = pageRegion,
                AtlasAllocation = atlasAllocation,
                MipTailRegion = mipTailRegion,
                MaxMip = maxMip,
                HasAllocation = true,
            };
            TouchNewBindingEntry(ref bindingEntry);
            m_Bindings.Add(key, bindingEntry);
            m_PendingMipTailEntries.Add(key);
            m_QueuedMipTailCount += 1;
            m_QueuedMipTailPageCount += mipTailPageCount;
            m_CreateResourceCallCountThisFrame += 1;
            IncrementBindingRevision();
            return binding;
        }

        internal bool TryAcquireExternalSurfaceBinding(
            VividVirtualTextureAsset asset,
            out ExternalSurfaceBindingLease lease,
            out string reason)
        {
            lease = null;
            if (!IsAvailable)
            {
                reason = UnavailableReason;
                return false;
            }
            if (!IsCompatibleStreamedAsset(asset, VirtualTextureSpaceDesc.StackDesc, out reason))
                return false;

            var textures = new GPUDrivenSurfaceTextureSet(
                asset,
                null,
                null,
                null,
                GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness);
            VividSurfaceBindingData binding = CreateSurfaceBinding(textures);
            var key = new TextureSetKey(
                asset,
                null,
                null,
                null,
                asset.AddressMode == VividVirtualTextureAddressMode.Clamp
                    ? GPUDrivenSurfaceAddressMode.Clamp
                    : GPUDrivenSurfaceAddressMode.Repeat,
                GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness);
            bool hasEntry = m_Bindings.TryGetValue(key, out BindingEntry entry);
            if (!hasEntry
                || !entry.HasAllocation
                || (binding.Flags & VividSurfaceBindingFlags.BaseColor) == 0)
            {
                reason = $"Streamed VT asset '{asset.name}' must contain a BaseColor layer with alpha.";
                if (hasEntry
                    && !m_SurfaceBindingUpdateActive
                    && !m_ExternalBindingRefCounts.ContainsKey(key)
                    && entry.LastTouchedUpdate != m_SurfaceBindingUpdate)
                {
                    m_ReleaseEntries.Clear();
                    m_ReleaseEntries.Add(key);
                    ReleaseBindingEntries(m_ReleaseEntries);
                    m_ReleaseEntries.Clear();
                }
                return false;
            }

            m_ExternalBindingRefCounts.TryGetValue(key, out int refCount);
            m_ExternalBindingRefCounts[key] = refCount + 1;
            lease = new ExternalSurfaceBindingLease(this, key, entry);
            reason = string.Empty;
            return true;
        }

        internal bool TryAcquireExternalSurfaceBinding(
            in GPUDrivenSurfaceTextureSet textures,
            out ExternalSurfaceBindingLease lease,
            out string reason)
        {
            lease = null;
            if (!IsAvailable)
            {
                reason = UnavailableReason;
                return false;
            }

            VividVirtualTextureAsset streamedAsset = ResolveStreamedAsset(textures.StreamedVirtualTexture);
            Texture2D baseColor = streamedAsset == null ? ResolveTexture2D(textures.BaseColor) : null;
            Texture2D normal = streamedAsset == null ? ResolveTexture2D(textures.Normal) : null;
            Texture2D mask = streamedAsset == null ? ResolveTexture2D(textures.Mask) : null;
            if (streamedAsset == null && baseColor == null && normal == null && mask == null)
            {
                reason = "The external VT texture set does not contain a supported Texture2D.";
                return false;
            }

            GPUDrivenSurfaceAddressMode addressMode = streamedAsset != null
                ? streamedAsset.AddressMode == VividVirtualTextureAddressMode.Clamp
                    ? GPUDrivenSurfaceAddressMode.Clamp
                    : GPUDrivenSurfaceAddressMode.Repeat
                : textures.AddressMode;
            var key = new TextureSetKey(
                streamedAsset,
                baseColor,
                normal,
                mask,
                addressMode,
                textures.MaskMode);
            if (streamedAsset == null && !VTRuntimeBlockCompressor.IsAvailable(out reason))
                return false;

            VividSurfaceBindingData binding = CreateSurfaceBinding(
                textures,
                allowRuntimeTextureProducer: true);
            bool hasEntry = m_Bindings.TryGetValue(key, out BindingEntry entry);
            if (!hasEntry
                || !entry.HasAllocation
                || binding.Flags == VividSurfaceBindingFlags.None)
            {
                reason = "The external VT texture set could not allocate a valid surface binding.";
                if (hasEntry
                    && !m_SurfaceBindingUpdateActive
                    && !m_ExternalBindingRefCounts.ContainsKey(key)
                    && entry.LastTouchedUpdate != m_SurfaceBindingUpdate)
                {
                    m_ReleaseEntries.Clear();
                    m_ReleaseEntries.Add(key);
                    ReleaseBindingEntries(m_ReleaseEntries);
                    m_ReleaseEntries.Clear();
                }
                return false;
            }

            m_ExternalBindingRefCounts.TryGetValue(key, out int refCount);
            m_ExternalBindingRefCounts[key] = refCount + 1;
            lease = new ExternalSurfaceBindingLease(this, key, entry);
            reason = string.Empty;
            return true;
        }

        private void ReleaseExternalSurfaceBinding(in TextureSetKey key)
        {
            if (m_IsDisposed
                || !m_ExternalBindingRefCounts.TryGetValue(key, out int refCount))
            {
                return;
            }

            if (refCount <= 1)
            {
                m_ExternalBindingRefCounts.Remove(key);
                if (!m_SurfaceBindingUpdateActive
                    && m_Bindings.TryGetValue(key, out BindingEntry entry)
                    && entry.LastTouchedUpdate != m_SurfaceBindingUpdate)
                {
                    m_ReleaseEntries.Clear();
                    m_ReleaseEntries.Add(key);
                    ReleaseBindingEntries(m_ReleaseEntries);
                    m_ReleaseEntries.Clear();
                }
            }
            else
                m_ExternalBindingRefCounts[key] = refCount - 1;
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

            foreach (TerrainRVTCameraState cameraState in m_TerrainRVTCameraStates.Values)
            {
                foreach (TerrainRuntimeVirtualTextureClipmap clipmap in cameraState.Clipmaps.Values)
                    clipmap.Dispose();
                cameraState.Clipmaps.Clear();
            }
            m_TerrainRVTCameraStates.Clear();
            m_TerrainRVTRegistrations.Clear();
            m_TerrainRVTAllocations.Clear();
            m_TerrainRVTRecords.Clear();
            m_TerrainRVTReleaseEntries.Clear();
            m_TerrainRVTCameraReleaseEntries.Clear();
            m_TerrainRVTClipmapReleaseEntries.Clear();
            m_TerrainRVTCandidates.Clear();
            m_TerrainRVTFlushRegions.Clear();
            m_TerrainRVTRecordBuffer?.Dispose();
            m_TerrainRVTRecordBuffer = null;
            m_TerrainRVTLevelBuffer?.Dispose();
            m_TerrainRVTLevelBuffer = null;
            m_TerrainRVTRecordUpload = Array.Empty<TerrainRuntimeVirtualTextureRecordGPUData>();
            m_TerrainRVTLevelUpload = Array.Empty<TerrainRuntimeVirtualTextureLevelGPUData>();
            m_CurrentTerrainRVTCameraState = null;
            m_Bindings.Clear();
            m_ExternalBindingRefCounts.Clear();
            m_RegisteredTextureIds.Clear();
            m_UnsupportedTextureWarningIds.Clear();
            m_InvalidStreamedAssetWarningIds.Clear();
            m_PermanentlyFailedStreamedAssets.Clear();
            m_IncompatibleScalarMaskWarningIds.Clear();
            m_InvalidTerrainDecalWarningIds.Clear();
            m_InvalidTerrainDecalDataWarningIds.Clear();
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
                s_CompatibleStreamedStackDesc,
                out validationMessage);
        }

        public bool CanUseStreamedVirtualTexture(VividVirtualTextureAsset asset)
        {
            return IsCompatibleStreamedAsset(asset, VirtualTextureSpaceDesc.StackDesc, out _);
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
                $"Streamed VT asset '{asset.name}' is not a compatible GPUDrivenSurface build. "
                + $"Actual: profile={builtData.BuildProfile}, storage={builtData.StorageProfile}, "
                + $"page={builtData.PageSize}, border={builtData.BorderSize}, "
                + $"pages={builtData.VirtualPageCountX}x{builtData.VirtualPageCountY}, "
                + $"mips={builtData.MipCount}, layers={builtData.LayerCount}. "
                + "Required: GPUDrivenSurface, DesktopBCn, 128 texel pages, 4 texel borders, "
                + "power-of-two dimensions, automatic full mip chain, and the GPUDriven four-layer stack.";
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

        private bool CanCreateTerrainRuntimeVirtualTexture(
            VividTerrain terrain,
            VividTerrainData terrainData)
        {
            if (!TerrainRuntimeVirtualTextureEnabled
                || terrain == null
                || terrainData == null
                || terrain.Data != terrainData
                || terrainData.SupportedSurfaceLayerCount < 2
                || terrainData.SupportedSurfaceLayerCount > VividTerrainData.MaximumSurfaceLayerCount
                || !terrainData.HasCompleteControlMapData
                || !CanUseStreamedVirtualTexture(terrainData.CompositeVirtualTexture))
            {
                return false;
            }

            for (int controlIndex = 0; controlIndex < terrainData.RequiredControlMapCount; controlIndex++)
            {
                if (terrainData.ControlMaps[controlIndex] == null)
                    return false;
            }
            return true;
        }

        private static int CompareTerrainRVTCandidates(
            TerrainRuntimeVirtualTextureClipmap.PageCandidate left,
            TerrainRuntimeVirtualTextureClipmap.PageCandidate right)
        {
            int levelComparison = left.Level.LevelIndex.CompareTo(right.Level.LevelIndex);
            if (levelComparison != 0)
                return levelComparison;

            int feedbackComparison = right.FeedbackHitCount.CompareTo(left.FeedbackHitCount);
            return feedbackComparison != 0
                ? feedbackComparison
                : left.DistanceSquared.CompareTo(right.DistanceSquared);
        }

        private int FindTerrainRVTRecordSlot()
        {
            for (int recordIndex = 0; recordIndex < m_TerrainRVTRecords.Count; recordIndex++)
            {
                if (m_TerrainRVTRecords[recordIndex] == null)
                    return recordIndex;
            }

            m_TerrainRVTRecords.Add(null);
            return m_TerrainRVTRecords.Count - 1;
        }

        internal static Camera ResolveTerrainRVTCamera(Camera renderingCamera)
        {
            return renderingCamera != null && IsTerrainRVTCameraType(renderingCamera.cameraType)
                ? renderingCamera
                : null;
        }

        internal static bool IsTerrainRVTCameraType(CameraType cameraType)
        {
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }

        internal static bool ShouldKeepTerrainRVTCameraState(
            CameraType cameraType,
            bool isActiveAndEnabled)
        {
            return IsTerrainRVTCameraType(cameraType)
                   && (cameraType != CameraType.Game || isActiveAndEnabled);
        }

        private TerrainRVTCameraState GetOrCreateTerrainRVTCameraState(Camera camera)
        {
            EntityId cameraId = camera.GetEntityId();
            if (m_TerrainRVTCameraStates.TryGetValue(cameraId, out TerrainRVTCameraState cameraState))
            {
                if (ReferenceEquals(cameraState.Camera, camera))
                    return cameraState;
                ReleaseTerrainRVTCameraState(cameraState);
            }

            cameraState = new TerrainRVTCameraState
            {
                Camera = camera,
                CameraId = cameraId,
            };
            m_TerrainRVTCameraStates.Add(cameraId, cameraState);
            return cameraState;
        }

        private void PurgeInactiveTerrainRVTCameras()
        {
            m_TerrainRVTCameraReleaseEntries.Clear();
            foreach (TerrainRVTCameraState cameraState in m_TerrainRVTCameraStates.Values)
            {
                if (cameraState.Camera == null
                    // Unity renders hidden SceneView cameras while their Camera component is disabled.
                    || !ShouldKeepTerrainRVTCameraState(
                        cameraState.Camera.cameraType,
                        cameraState.Camera.isActiveAndEnabled))
                {
                    m_TerrainRVTCameraReleaseEntries.Add(cameraState);
                }
            }
            for (int cameraIndex = 0;
                 cameraIndex < m_TerrainRVTCameraReleaseEntries.Count;
                 cameraIndex++)
            {
                ReleaseTerrainRVTCameraState(m_TerrainRVTCameraReleaseEntries[cameraIndex]);
            }
            m_TerrainRVTCameraReleaseEntries.Clear();
        }

        private void ReleaseTerrainRVTCameraState(TerrainRVTCameraState cameraState)
        {
            if (cameraState == null || !m_TerrainRVTCameraStates.Remove(cameraState.CameraId))
                return;

            if (ReferenceEquals(m_CurrentTerrainRVTCameraState, cameraState))
                m_CurrentTerrainRVTCameraState = null;

            m_TerrainRVTClipmapReleaseEntries.Clear();
            m_TerrainRVTClipmapReleaseEntries.AddRange(cameraState.Clipmaps.Values);
            for (int clipmapIndex = 0;
                 clipmapIndex < m_TerrainRVTClipmapReleaseEntries.Count;
                 clipmapIndex++)
            {
                ReleaseTerrainRVTClipmap(
                    cameraState,
                    m_TerrainRVTClipmapReleaseEntries[clipmapIndex]);
            }
            m_TerrainRVTClipmapReleaseEntries.Clear();
        }

        private void TouchTerrainRVT(TerrainRVTRegistration registration)
        {
            if (m_SurfaceBindingUpdateActive)
                registration.LastTouchedUpdate = m_SurfaceBindingUpdate;
        }

        private void TouchNewTerrainRVT(TerrainRVTRegistration registration)
        {
            if (!m_SurfaceBindingUpdateActive)
                return;

            registration.LastTouchedUpdate = m_SurfaceBindingUpdate;
            registration.CreatedUpdate = m_SurfaceBindingUpdate;
        }

        private void ReleaseTerrainRVT(TerrainRVTRegistration registration)
        {
            if (registration == null || !m_TerrainRVTRegistrations.Remove(registration.EntityId))
                return;

            foreach (TerrainRVTCameraState cameraState in m_TerrainRVTCameraStates.Values)
            {
                if (cameraState.Clipmaps.TryGetValue(
                        registration.EntityId,
                        out TerrainRuntimeVirtualTextureClipmap clipmap))
                {
                    ReleaseTerrainRVTClipmap(cameraState, clipmap);
                }
            }

            int recordIndex = (int)registration.RecordIndex;
            if (recordIndex >= 0
                && recordIndex < m_TerrainRVTRecords.Count
                && ReferenceEquals(m_TerrainRVTRecords[recordIndex], registration))
            {
                m_TerrainRVTRecords[recordIndex] = null;
            }
            IncrementBindingRevision();
        }

        private void ReleaseTerrainRVTClipmap(
            TerrainRVTCameraState cameraState,
            TerrainRuntimeVirtualTextureClipmap clipmap)
        {
            if (clipmap == null || !cameraState.Clipmaps.Remove(clipmap.EntityId))
                return;

            m_TerrainRVTFlushRegions.Clear();
            for (int levelIndex = 0; levelIndex < clipmap.Levels.Length; levelIndex++)
            {
                TerrainRuntimeVirtualTextureClipmap.Level level = clipmap.Levels[levelIndex];
                m_TerrainRVTFlushRegions.Add(new VTPageRegion(0, level.PageRegion));
                m_Producer.UnregisterEntry(level.PageRegion);
            }
            VirtualTextureSystem.FlushRegions(VirtualTextureSpaceId, m_TerrainRVTFlushRegions);

            if (m_TerrainRVTAllocations.Remove(
                    clipmap,
                    out GPUDrivenVirtualTextureAtlasAllocator.Allocation[] allocations))
            {
                for (int allocationIndex = 0; allocationIndex < allocations.Length; allocationIndex++)
                    m_AtlasAllocator.Release(allocations[allocationIndex]);
            }

            clipmap.Dispose();
        }

        private void UploadTerrainRVTBuffers(CommandBuffer cmd, TerrainRVTCameraState cameraState)
        {
            int recordCount = Mathf.Max(1, m_TerrainRVTRecords.Count);
            int levelCount = recordCount * TerrainRuntimeVirtualTextureClipmap.LevelCount;
            EnsureTerrainRVTBuffer(
                ref m_TerrainRVTRecordBuffer,
                recordCount,
                Marshal.SizeOf<TerrainRuntimeVirtualTextureRecordGPUData>(),
                "VividTerrainRVTRecords");
            EnsureTerrainRVTBuffer(
                ref m_TerrainRVTLevelBuffer,
                levelCount,
                Marshal.SizeOf<TerrainRuntimeVirtualTextureLevelGPUData>(),
                "VividTerrainRVTLevels");

            if (m_TerrainRVTRecordUpload.Length < recordCount)
                m_TerrainRVTRecordUpload = new TerrainRuntimeVirtualTextureRecordGPUData[recordCount];
            if (m_TerrainRVTLevelUpload.Length < levelCount)
                m_TerrainRVTLevelUpload = new TerrainRuntimeVirtualTextureLevelGPUData[levelCount];
            Array.Clear(m_TerrainRVTRecordUpload, 0, recordCount);
            Array.Clear(m_TerrainRVTLevelUpload, 0, levelCount);
            for (int recordIndex = 0; recordIndex < m_TerrainRVTRecords.Count; recordIndex++)
            {
                TerrainRVTRegistration registration = m_TerrainRVTRecords[recordIndex];
                if (registration == null
                    || !cameraState.Clipmaps.TryGetValue(
                        registration.EntityId,
                        out TerrainRuntimeVirtualTextureClipmap clipmap))
                {
                    continue;
                }

                int levelStartIndex = recordIndex * TerrainRuntimeVirtualTextureClipmap.LevelCount;
                m_TerrainRVTRecordUpload[recordIndex] = clipmap.CreateGPUData((uint)levelStartIndex);
                for (int levelIndex = 0; levelIndex < clipmap.Levels.Length; levelIndex++)
                {
                    m_TerrainRVTLevelUpload[levelStartIndex + levelIndex] =
                        clipmap.Levels[levelIndex].CreateGPUData();
                }
            }

            cmd.SetBufferData(m_TerrainRVTRecordBuffer, m_TerrainRVTRecordUpload, 0, 0, recordCount);
            cmd.SetBufferData(m_TerrainRVTLevelBuffer, m_TerrainRVTLevelUpload, 0, 0, levelCount);
        }

        private static void EnsureTerrainRVTBuffer(
            ref GraphicsBuffer buffer,
            int count,
            int stride,
            string name)
        {
            if (buffer != null && buffer.count >= count && buffer.stride == stride)
                return;

            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, count), stride)
            {
                name = name,
            };
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

        private void TouchBindingEntry(ref BindingEntry bindingEntry)
        {
            if (m_SurfaceBindingUpdateActive)
                bindingEntry.LastTouchedUpdate = m_SurfaceBindingUpdate;
        }

        private void TouchNewBindingEntry(ref BindingEntry bindingEntry)
        {
            if (!m_SurfaceBindingUpdateActive)
                return;

            bindingEntry.LastTouchedUpdate = m_SurfaceBindingUpdate;
            bindingEntry.CreatedUpdate = m_SurfaceBindingUpdate;
        }

        private void ReleaseBindingEntries(IReadOnlyList<TextureSetKey> bindingKeys)
        {
            if (bindingKeys == null || bindingKeys.Count == 0)
                return;

            bool releasedAllocation = false;
            m_ReleaseRegions.Clear();
            m_ReleaseAllocationEntries.Clear();
            for (int entryIndex = 0; entryIndex < bindingKeys.Count; entryIndex++)
            {
                TextureSetKey bindingKey = bindingKeys[entryIndex];
                if (!m_Bindings.TryGetValue(bindingKey, out BindingEntry bindingEntry)
                    || !m_Bindings.Remove(bindingKey))
                    continue;

                if (!bindingEntry.HasAllocation)
                    continue;

                releasedAllocation = true;
                m_ReleaseAllocationEntries.Add(bindingEntry);
                if (!bindingEntry.MipTailResident)
                    m_PendingMipTailEntries.Remove(bindingKey);
                else
                    m_ResidentMipTailCount = Mathf.Max(0, m_ResidentMipTailCount - 1);
                m_QueuedMipTailCount = Mathf.Max(0, m_QueuedMipTailCount - 1);
                m_QueuedMipTailPageCount = Mathf.Max(
                    0,
                    m_QueuedMipTailPageCount - GetPageCount(bindingEntry.MipTailRegion));
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

        private static int GetPageCount(RectInt pageRegion)
        {
            return pageRegion.width * pageRegion.height;
        }

        private static VirtualTexturePageCoord GetPageCoord(RectInt pageRegion, int mip, int pageIndex)
        {
            return new VirtualTexturePageCoord(
                pageRegion.xMin + pageIndex % pageRegion.width,
                pageRegion.yMin + pageIndex / pageRegion.width,
                mip);
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
