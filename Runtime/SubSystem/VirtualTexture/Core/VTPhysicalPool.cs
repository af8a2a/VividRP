using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal readonly struct VTPhysicalPoolLayerDesc : IEquatable<VTPhysicalPoolLayerDesc>
    {
        internal VTPhysicalPoolLayerDesc(
            VTLayerSemantic semantic,
            int physicalGroup,
            GraphicsFormat graphicsFormat,
            bool sRGB)
        {
            Semantic = semantic;
            PhysicalGroup = Mathf.Max(0, physicalGroup);
            GraphicsFormat = graphicsFormat;
            StorageFormat = VTPhysicalPoolDesc.ResolveStorageFormat(graphicsFormat);
            SRGB = sRGB;
        }

        internal VTLayerSemantic Semantic { get; }

        internal int PhysicalGroup { get; }

        internal GraphicsFormat GraphicsFormat { get; }

        internal GraphicsFormat StorageFormat { get; }

        internal bool SRGB { get; }

        internal static VTPhysicalPoolLayerDesc FromLayer(in VTLayerDesc layer)
        {
            return new VTPhysicalPoolLayerDesc(
                layer.Semantic,
                layer.PhysicalGroup,
                layer.GraphicsFormat,
                layer.SRGB);
        }

        public bool Equals(VTPhysicalPoolLayerDesc other)
        {
            return Semantic == other.Semantic
                   && PhysicalGroup == other.PhysicalGroup
                   && GraphicsFormat == other.GraphicsFormat
                   && StorageFormat == other.StorageFormat
                   && SRGB == other.SRGB;
        }

        public override bool Equals(object obj)
        {
            return obj is VTPhysicalPoolLayerDesc other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Semantic, PhysicalGroup, GraphicsFormat, StorageFormat, SRGB);
        }
    }

    internal readonly struct VTPhysicalPoolDesc : IEquatable<VTPhysicalPoolDesc>
    {
        internal VTPhysicalPoolDesc(
            int pageSize,
            int borderSize,
            int pageCount,
            IReadOnlyList<VTLayerDesc> layers)
        {
            if (layers == null || layers.Count == 0)
                throw new ArgumentException("Physical pool must contain at least one layer.", nameof(layers));

            PageSize = pageSize;
            BorderSize = borderSize;
            PhysicalPageSize = pageSize + borderSize * 2;
            PageCount = pageCount;
            LayerCount = Mathf.Max(1, layers.Count);
            m_Layers = new VTPhysicalPoolLayerDesc[LayerCount];
            m_GroupLayerCounts = new int[VTStackDesc.MaxLayerCount];
            m_GroupStorageFormats = new GraphicsFormat[VTStackDesc.MaxLayerCount];
            m_LayerPhysicalLayerIndices = new int[LayerCount];
            int maxPhysicalGroup = 0;
            for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
            {
                m_Layers[layerIndex] = VTPhysicalPoolLayerDesc.FromLayer(layers[layerIndex]);
                if (m_Layers[layerIndex].PhysicalGroup >= VTStackDesc.MaxLayerCount)
                    throw new ArgumentOutOfRangeException(
                        nameof(layers),
                        $"Physical group index must be smaller than {VTStackDesc.MaxLayerCount}.");

                maxPhysicalGroup = Mathf.Max(maxPhysicalGroup, m_Layers[layerIndex].PhysicalGroup);
                GraphicsFormat groupFormat = m_GroupStorageFormats[m_Layers[layerIndex].PhysicalGroup];
                if (groupFormat == GraphicsFormat.None)
                {
                    m_GroupStorageFormats[m_Layers[layerIndex].PhysicalGroup] = m_Layers[layerIndex].StorageFormat;
                }
                else if (groupFormat != m_Layers[layerIndex].StorageFormat)
                {
                    throw new ArgumentException(
                        $"Physical group {m_Layers[layerIndex].PhysicalGroup} mixes layer storage formats. " +
                        "Use a separate physical group for layers with different formats.",
                        nameof(layers));
                }

                m_LayerPhysicalLayerIndices[layerIndex] = m_GroupLayerCounts[m_Layers[layerIndex].PhysicalGroup];
                m_GroupLayerCounts[m_Layers[layerIndex].PhysicalGroup] += 1;
            }

            GraphicsFormat = m_Layers[0].StorageFormat;
            PhysicalGroupCount = maxPhysicalGroup + 1;
            for (int groupIndex = 0; groupIndex < PhysicalGroupCount; groupIndex++)
            {
                if (m_GroupLayerCounts[groupIndex] <= 0)
                {
                    throw new ArgumentException(
                        "Physical group indices must be compact and start at zero.",
                        nameof(layers));
                }
            }

            LayerGroup = BuildLayerGroupKey(m_Layers);
        }

        private readonly VTPhysicalPoolLayerDesc[] m_Layers;
        private readonly int[] m_GroupLayerCounts;
        private readonly GraphicsFormat[] m_GroupStorageFormats;
        private readonly int[] m_LayerPhysicalLayerIndices;

        internal int PageSize { get; }

        internal int BorderSize { get; }

        internal int PhysicalPageSize { get; }

        internal int PageCount { get; }

        internal int LayerCount { get; }

        internal GraphicsFormat GraphicsFormat { get; }

        internal int PhysicalGroupCount { get; }

        internal string LayerGroup { get; }

        internal IReadOnlyList<VTPhysicalPoolLayerDesc> Layers => m_Layers ?? Array.Empty<VTPhysicalPoolLayerDesc>();

        internal int GetGroupLayerCount(int physicalGroup)
        {
            return m_GroupLayerCounts != null && physicalGroup >= 0 && physicalGroup < m_GroupLayerCounts.Length
                ? m_GroupLayerCounts[physicalGroup]
                : 0;
        }

        internal GraphicsFormat GetGroupStorageFormat(int physicalGroup)
        {
            return m_GroupStorageFormats != null
                   && physicalGroup >= 0
                   && physicalGroup < m_GroupStorageFormats.Length
                ? m_GroupStorageFormats[physicalGroup]
                : GraphicsFormat.None;
        }

        internal int GetLayerPhysicalGroup(int layerIndex)
        {
            if (m_Layers == null || layerIndex < 0 || layerIndex >= m_Layers.Length)
                return 0;

            return m_Layers[layerIndex].PhysicalGroup;
        }

        internal int GetLayerPhysicalLayerIndex(int layerIndex)
        {
            if (m_LayerPhysicalLayerIndices == null
                || layerIndex < 0
                || layerIndex >= m_LayerPhysicalLayerIndices.Length)
            {
                return 0;
            }

            return m_LayerPhysicalLayerIndices[layerIndex];
        }

        internal static VTPhysicalPoolDesc FromSpaceDesc(in VirtualTextureSpaceDesc desc)
        {
            return new VTPhysicalPoolDesc(
                desc.PageSize,
                desc.BorderSize,
                desc.CachePageCount,
                desc.StackDesc.Layers);
        }

        internal static GraphicsFormat ResolveStorageFormat(GraphicsFormat graphicsFormat)
        {
            return GraphicsFormatUtility.IsSRGBFormat(graphicsFormat)
                ? GraphicsFormatUtility.GetLinearFormat(graphicsFormat)
                : graphicsFormat;
        }

        public bool Equals(VTPhysicalPoolDesc other)
        {
            return PageSize == other.PageSize
                   && BorderSize == other.BorderSize
                   && PageCount == other.PageCount
                   && LayerCount == other.LayerCount
                   && GraphicsFormat == other.GraphicsFormat
                   && PhysicalGroupCount == other.PhysicalGroupCount
                   && LayersEqual(other)
                   && string.Equals(LayerGroup, other.LayerGroup, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is VTPhysicalPoolDesc other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(PageSize);
            hashCode.Add(BorderSize);
            hashCode.Add(PageCount);
            hashCode.Add(LayerCount);
            hashCode.Add(GraphicsFormat);
            hashCode.Add(PhysicalGroupCount);
            hashCode.Add(StringComparer.Ordinal.GetHashCode(LayerGroup ?? string.Empty));
            for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
                hashCode.Add(m_Layers[layerIndex]);

            return hashCode.ToHashCode();
        }

        private bool LayersEqual(in VTPhysicalPoolDesc other)
        {
            if (m_Layers == null || other.m_Layers == null)
                return m_Layers == other.m_Layers;

            if (m_Layers.Length != other.m_Layers.Length)
                return false;

            for (int layerIndex = 0; layerIndex < m_Layers.Length; layerIndex++)
            {
                if (!m_Layers[layerIndex].Equals(other.m_Layers[layerIndex]))
                    return false;
            }

            return true;
        }

        private static string BuildLayerGroupKey(IReadOnlyList<VTPhysicalPoolLayerDesc> layers)
        {
            if (layers == null || layers.Count == 0)
                return "Default";

            var keyBuilder = new System.Text.StringBuilder();
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                if (layerIndex > 0)
                    keyBuilder.Append('|');

                VTPhysicalPoolLayerDesc layer = layers[layerIndex];
                keyBuilder.Append((int)layer.Semantic);
                keyBuilder.Append(':');
                keyBuilder.Append(layer.PhysicalGroup);
                keyBuilder.Append(':');
                keyBuilder.Append((int)layer.GraphicsFormat);
                keyBuilder.Append(':');
                keyBuilder.Append(layer.SRGB ? 1 : 0);
            }

            return keyBuilder.ToString();
        }
    }

    internal readonly struct VTPhysicalPoolStats
    {
        internal VTPhysicalPoolStats(
            int poolCount,
            int residentPageCount,
            int freePageCount,
            int lockedPageCount,
            int evictedPageCount,
            long allocatedByteCount = 0,
            long residentByteCount = 0)
        {
            PoolCount = poolCount;
            ResidentPageCount = residentPageCount;
            FreePageCount = freePageCount;
            LockedPageCount = lockedPageCount;
            EvictedPageCount = evictedPageCount;
            AllocatedByteCount = Math.Max(0L, allocatedByteCount);
            ResidentByteCount = Math.Max(0L, residentByteCount);
        }

        internal int PoolCount { get; }

        internal int ResidentPageCount { get; }

        internal int FreePageCount { get; }

        internal int LockedPageCount { get; }

        internal int EvictedPageCount { get; }

        internal long AllocatedByteCount { get; }

        internal long ResidentByteCount { get; }
    }

    internal readonly struct VTPhysicalAtlasLayout : IEquatable<VTPhysicalAtlasLayout>
    {
        internal VTPhysicalAtlasLayout(int physicalPageSize, int tileCount, int maxTextureSize)
        {
            if (physicalPageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(physicalPageSize));
            if (tileCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(tileCount));
            if (maxTextureSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTextureSize));

            int maxTileCountPerDimension = maxTextureSize / physicalPageSize;
            if (maxTileCountPerDimension <= 0)
            {
                throw new InvalidOperationException(
                    $"VT physical page size {physicalPageSize} exceeds the active device's "
                    + $"maximum 2D texture size of {maxTextureSize}.");
            }

            int tileCountX = Mathf.CeilToInt(Mathf.Sqrt(tileCount));
            int tileCountY = (tileCount + tileCountX - 1) / tileCountX;
            if (tileCountX > maxTileCountPerDimension || tileCountY > maxTileCountPerDimension)
            {
                long atlasCapacity = (long)maxTileCountPerDimension * maxTileCountPerDimension;
                throw new InvalidOperationException(
                    $"VT physical cache requires {tileCount} atlas tiles, but the active device can fit at most "
                    + $"{atlasCapacity} {physicalPageSize}x{physicalPageSize} tiles in a "
                    + $"{maxTextureSize}x{maxTextureSize} 2D texture.");
            }

            PhysicalPageSize = physicalPageSize;
            TileCount = tileCount;
            TileCountX = tileCountX;
            TileCountY = tileCountY;
            Width = tileCountX * physicalPageSize;
            Height = tileCountY * physicalPageSize;
        }

        internal int PhysicalPageSize { get; }

        internal int TileCount { get; }

        internal int TileCountX { get; }

        internal int TileCountY { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal RectInt GetTileRect(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= TileCount)
                throw new ArgumentOutOfRangeException(nameof(tileIndex));

            return new RectInt(
                tileIndex % TileCountX * PhysicalPageSize,
                tileIndex / TileCountX * PhysicalPageSize,
                PhysicalPageSize,
                PhysicalPageSize);
        }

        public bool Equals(VTPhysicalAtlasLayout other)
        {
            return PhysicalPageSize == other.PhysicalPageSize
                   && TileCount == other.TileCount
                   && TileCountX == other.TileCountX
                   && TileCountY == other.TileCountY;
        }

        public override bool Equals(object obj)
        {
            return obj is VTPhysicalAtlasLayout other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(PhysicalPageSize, TileCount, TileCountX, TileCountY);
        }
    }

    internal readonly struct VTPhysicalPageIdentity : IEquatable<VTPhysicalPageIdentity>
    {
        internal VTPhysicalPageIdentity(
            VTProducerHandle producerHandle,
            string producerName,
            in VirtualTexturePageCoord pageCoord)
        {
            ProducerHandle = producerHandle;
            ProducerName = producerName;
            PageCoord = pageCoord;
        }

        internal VTProducerHandle ProducerHandle { get; }

        internal string ProducerName { get; }

        internal VirtualTexturePageCoord PageCoord { get; }

        public bool Equals(VTPhysicalPageIdentity other)
        {
            bool eitherHandleIsValid = ProducerHandle.IsValid || other.ProducerHandle.IsValid;
            bool sameProducer = eitherHandleIsValid
                ? ProducerHandle.IsValid
                  && other.ProducerHandle.IsValid
                  && ProducerHandle.Equals(other.ProducerHandle)
                : !string.IsNullOrEmpty(ProducerName)
                  && string.Equals(ProducerName, other.ProducerName, StringComparison.Ordinal);
            return sameProducer && PageCoord.Equals(other.PageCoord);
        }

        public override bool Equals(object obj)
        {
            return obj is VTPhysicalPageIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ProducerHandle.IsValid
                ? HashCode.Combine(ProducerHandle, PageCoord)
                : HashCode.Combine(ProducerName ?? string.Empty, PageCoord);
        }
    }

    internal interface IVTPhysicalPoolOwner
    {
        int SpaceId { get; }

        bool OnPhysicalPageInvalidated(int pageIndex, int generation);
    }

#if VT_DEBUG
    internal enum VTPageRequestKind : byte
    {
        Unknown = 0,
        Bootstrap = 1,
        Locked = 2,
        Demand = 3,
        Refinement = 4,
        Neighbor = 5,
    }

    internal readonly struct VTPageRequestDebugInfo
    {
        internal VTPageRequestDebugInfo(
            VTPageRequestKind requestKind,
            in VirtualTexturePageCoord sourceCoord,
            in VirtualTexturePageCoord effectiveCoord,
            int mipGap,
            long weightedScore)
        {
            RequestKind = requestKind;
            SourceCoord = sourceCoord;
            EffectiveCoord = effectiveCoord;
            MipGap = mipGap;
            WeightedScore = weightedScore;
        }

        internal VTPageRequestKind RequestKind { get; }

        internal VirtualTexturePageCoord SourceCoord { get; }

        internal VirtualTexturePageCoord EffectiveCoord { get; }

        internal int MipGap { get; }

        internal long WeightedScore { get; }
    }
#endif

    internal sealed class VTPhysicalPool : IDisposable
    {
        internal const int AsyncCommitEvictionProtectionFrames = 3;
        internal const int FeedbackEvictionProtectionFrames = 8;

        private struct PhysicalPageBinding
        {
            public IVTPhysicalPoolOwner Owner;
            public int SpaceId;
            public int VirtualPageIndex;
            public bool Locked;
        }

        private struct PhysicalPageSlotState
        {
            public IVTPhysicalPoolOwner Owner;
            public int SpaceId;
            public int VirtualPageIndex;
            public int VirtualPageMip;
            public int Generation;
            public int LastAllocationFrame;
            public int LastAsyncCommitFrame;
            public VirtualTextureViewId AffinityViewId;
            public int LastAffinityFrame;
            public VTPhysicalPageIdentity Identity;
            public bool Resident;
            public bool PendingUpload;
            public bool Locked;
#if VT_DEBUG
            public VTPageRequestDebugInfo RequestDebugInfo;
#endif
        }

        private readonly PhysicalPageSlotState[] m_Slots;
        private readonly Stack<int> m_FreePhysicalPages;
        private readonly LinkedList<int> m_LruPhysicalPages = new();
        private readonly LinkedListNode<int>[] m_LruNodes;
        private readonly int[] m_LastLruTouchFrames;
        private readonly int[] m_NextPhysicalPageWithSameIdentity;
        private readonly Dictionary<VTPhysicalPageIdentity, int> m_PhysicalPageLookup;
        private readonly List<PhysicalPageBinding>[] m_Bindings;
        private readonly Texture2D[] m_Textures;
        private readonly VTPhysicalAtlasLayout[] m_AtlasLayouts;
        private readonly string m_PoolName;
        private readonly long m_AllocatedByteCount;
        private readonly long m_BytesPerPhysicalPage;
#if VT_DEBUG
        private readonly string m_DebugName;
#endif

        private int m_NextGeneration;
        private int m_RefCount;
        private int m_EvictedPageCount;

        internal VTPhysicalPool(string name, in VTPhysicalPoolDesc desc)
        {
            Desc = desc;
            string poolName = string.IsNullOrWhiteSpace(name) ? "Shared" : name;
            m_PoolName = poolName;
#if VT_DEBUG
            m_DebugName = poolName;
#endif
            m_Slots = new PhysicalPageSlotState[Mathf.Max(1, desc.PageCount)];
            for (int slotIndex = 0; slotIndex < m_Slots.Length; slotIndex++)
            {
                m_Slots[slotIndex].VirtualPageIndex = -1;
                m_Slots[slotIndex].LastAsyncCommitFrame = -1;
                m_Slots[slotIndex].AffinityViewId = VirtualTextureViewId.Invalid;
                m_Slots[slotIndex].LastAffinityFrame = -1;
            }

            m_LruNodes = new LinkedListNode<int>[m_Slots.Length];
            m_LastLruTouchFrames = new int[m_Slots.Length];
            m_NextPhysicalPageWithSameIdentity = new int[m_Slots.Length];
            m_PhysicalPageLookup = new Dictionary<VTPhysicalPageIdentity, int>(m_Slots.Length);
            m_Bindings = new List<PhysicalPageBinding>[m_Slots.Length];
            for (int slotIndex = 0; slotIndex < m_Bindings.Length; slotIndex++)
            {
                m_LastLruTouchFrames[slotIndex] = int.MinValue;
                m_NextPhysicalPageWithSameIdentity[slotIndex] = -1;
                m_Bindings[slotIndex] = new List<PhysicalPageBinding>(1);
            }

            m_FreePhysicalPages = new Stack<int>(m_Slots.Length);
            for (int slotIndex = m_Slots.Length - 1; slotIndex >= 0; slotIndex--)
                m_FreePhysicalPages.Push(slotIndex);

            m_Textures = new Texture2D[Mathf.Max(1, desc.PhysicalGroupCount)];
            m_AtlasLayouts = new VTPhysicalAtlasLayout[m_Textures.Length];
            long allocatedByteCount = 0;
            long bytesPerPhysicalPage = 0;
            try
            {
                for (int groupIndex = 0; groupIndex < m_Textures.Length; groupIndex++)
                {
                    GraphicsFormat storageFormat = desc.GetGroupStorageFormat(groupIndex);
                    CopyTextureSupport requiredCopySupport =
                        CopyTextureSupport.Basic | CopyTextureSupport.DifferentTypes;
                    if (GraphicsFormatUtility.IsCompressedFormat(storageFormat)
                        && (!SystemInfo.IsFormatSupported(storageFormat, GraphicsFormatUsage.Sample)
                            || (SystemInfo.copyTextureSupport & requiredCopySupport) != requiredCopySupport))
                    {
                        throw new InvalidOperationException(
                            $"The active graphics device cannot sample and CopyTexture the compressed VT format "
                            + $"{storageFormat} used by physical group {groupIndex}.");
                    }

                    int groupLayerCount = Mathf.Max(1, desc.GetGroupLayerCount(groupIndex));
                    int tileCount = checked(m_Slots.Length * groupLayerCount);
                    m_AtlasLayouts[groupIndex] = new VTPhysicalAtlasLayout(
                        desc.PhysicalPageSize,
                        tileCount,
                        SystemInfo.maxTextureSize);
                    m_Textures[groupIndex] = CreatePhysicalTexture(
                        poolName,
                        desc,
                        groupIndex,
                        m_AtlasLayouts[groupIndex]);
                    allocatedByteCount = checked(
                        allocatedByteCount
                        + GetTextureByteCount(
                            storageFormat,
                            m_AtlasLayouts[groupIndex].Width,
                            m_AtlasLayouts[groupIndex].Height));
                    bytesPerPhysicalPage = checked(
                        bytesPerPhysicalPage
                        + GetTextureByteCount(
                            storageFormat,
                            desc.PhysicalPageSize,
                            desc.PhysicalPageSize)
                        * groupLayerCount);
                }
            }
            catch
            {
                DestroyTextures(m_Textures);
                throw;
            }

            m_AllocatedByteCount = allocatedByteCount;
            m_BytesPerPhysicalPage = bytesPerPhysicalPage;
        }

        internal VTPhysicalPoolDesc Desc { get; }

        internal Texture2D Texture => GetTextureForGroup(0);

        internal IReadOnlyList<Texture2D> Textures => m_Textures ?? Array.Empty<Texture2D>();

        internal Texture2D GetTextureForGroup(int physicalGroup)
        {
            if (m_Textures == null || physicalGroup < 0 || physicalGroup >= m_Textures.Length)
                return null;

            return m_Textures[physicalGroup];
        }

        internal VTPhysicalAtlasLayout GetAtlasLayoutForGroup(int physicalGroup)
        {
            if (m_AtlasLayouts == null || physicalGroup < 0 || physicalGroup >= m_AtlasLayouts.Length)
                throw new ArgumentOutOfRangeException(nameof(physicalGroup));

            return m_AtlasLayouts[physicalGroup];
        }

        internal RectInt GetPhysicalTileRect(int physicalGroup, int physicalPageId, int physicalLayerIndex)
        {
            if (physicalPageId < 0 || physicalPageId >= m_Slots.Length)
                throw new ArgumentOutOfRangeException(nameof(physicalPageId));

            int groupLayerCount = Mathf.Max(1, GetGroupLayerCount(physicalGroup));
            if (physicalLayerIndex < 0 || physicalLayerIndex >= groupLayerCount)
                throw new ArgumentOutOfRangeException(nameof(physicalLayerIndex));

            int tileIndex = checked(physicalPageId * groupLayerCount + physicalLayerIndex);
            return GetAtlasLayoutForGroup(physicalGroup).GetTileRect(tileIndex);
        }

        internal int GetGroupLayerCount(int physicalGroup)
        {
            return Desc.GetGroupLayerCount(physicalGroup);
        }

        internal int GetLayerPhysicalGroup(int layerIndex)
        {
            return Desc.GetLayerPhysicalGroup(layerIndex);
        }

        internal int GetLayerPhysicalLayerIndex(int layerIndex)
        {
            return Desc.GetLayerPhysicalLayerIndex(layerIndex);
        }

        internal int RefCount => m_RefCount;

        internal int FreePageCount => m_FreePhysicalPages.Count;

        internal int ResidentPageCount
        {
            get
            {
                int count = 0;
                for (int pageIndex = 0; pageIndex < m_Slots.Length; pageIndex++)
                {
                    if (IsOccupied(m_Slots[pageIndex]) && m_Slots[pageIndex].Resident)
                        count += 1;
                }

                return count;
            }
        }

        internal int LockedPageCount
        {
            get
            {
                int count = 0;
                for (int pageIndex = 0; pageIndex < m_Slots.Length; pageIndex++)
                {
                    if (IsOccupied(m_Slots[pageIndex]) && m_Slots[pageIndex].Locked)
                        count += 1;
                }

                return count;
            }
        }

        internal int EvictedPageCount => m_EvictedPageCount;

        internal void ResetRuntimeState()
        {
            m_EvictedPageCount = 0;
            RecreatePhysicalTextures();
        }

        internal long AllocatedByteCount => m_AllocatedByteCount;

        internal long ResidentByteCount => checked((long)ResidentPageCount * m_BytesPerPhysicalPage);

        internal void AddRef()
        {
            m_RefCount += 1;
        }

        internal int ReleaseRef()
        {
            m_RefCount = Mathf.Max(0, m_RefCount - 1);
            return m_RefCount;
        }

        internal bool TryAllocatePage(
            IVTPhysicalPoolOwner owner,
            VTProducerHandle producerHandle,
            string producerName,
            int pageIndex,
            int pageMip,
            in VirtualTexturePageCoord pageCoord,
            VirtualTextureViewId activeViewId,
            VirtualTextureViewId allocationViewId,
            bool updateAffinity,
            int frameIndex,
            bool locked,
            bool pendingUpload,
#if VT_DEBUG
            in VTPageRequestDebugInfo requestDebugInfo,
#endif
            out int physicalPageId,
            out int generation,
            out bool evicted)
        {
            physicalPageId = -1;
            generation = 0;
            evicted = false;
#if VT_DEBUG
            bool allocatedFromFreeList = false;
            PhysicalPageSlotState replacedSlotState = default;
            int replacedBindingCount = 0;
#endif
            if (owner == null)
                return false;

            if (m_FreePhysicalPages.Count > 0)
            {
                physicalPageId = m_FreePhysicalPages.Pop();
#if VT_DEBUG
                allocatedFromFreeList = true;
#endif
            }
            else
            {
                physicalPageId = FindEvictionCandidate(frameIndex, activeViewId);
                if (physicalPageId < 0)
                    return false;

#if VT_DEBUG
                replacedSlotState = m_Slots[physicalPageId];
                replacedBindingCount = m_Bindings[physicalPageId].Count;
                LogPageReplacementBegin(
                    physicalPageId,
                    in replacedSlotState,
                    replacedBindingCount,
                    owner,
                    producerHandle,
                    producerName,
                    pageIndex,
                    pageMip,
                    in pageCoord,
                    activeViewId,
                    allocationViewId,
                    updateAffinity,
                    frameIndex,
                    locked,
                    pendingUpload,
                    in requestDebugInfo);
#endif
                evicted = EvictPhysicalPageForReuse(physicalPageId, frameIndex);
            }

            generation = ++m_NextGeneration;
            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            slotState.Owner = owner;
            slotState.SpaceId = owner.SpaceId;
            slotState.VirtualPageIndex = pageIndex;
            slotState.VirtualPageMip = pageMip;
            slotState.Generation = generation;
            slotState.LastAllocationFrame = frameIndex;
            slotState.LastAsyncCommitFrame = -1;
            slotState.Identity = new VTPhysicalPageIdentity(producerHandle, producerName, pageCoord);
            slotState.Resident = !pendingUpload;
            slotState.PendingUpload = pendingUpload;
            slotState.Locked = locked;
#if VT_DEBUG
            slotState.RequestDebugInfo = requestDebugInfo;
#endif
            slotState.AffinityViewId = VirtualTextureViewId.Invalid;
            slotState.LastAffinityFrame = -1;
            m_Slots[physicalPageId] = slotState;
            AddPhysicalPageLookup(physicalPageId, slotState.Identity);
            m_Bindings[physicalPageId].Clear();
            AddBinding(physicalPageId, owner, pageIndex, locked);
            Touch(physicalPageId, allocationViewId, frameIndex, updateAffinity);
#if VT_DEBUG
            if (evicted)
            {
                PhysicalPageSlotState committedSlotState = m_Slots[physicalPageId];
                LogPageReplacementCommit(
                    physicalPageId,
                    in replacedSlotState,
                    replacedBindingCount,
                    in committedSlotState,
                    frameIndex);
            }
            else if (allocatedFromFreeList)
            {
                PhysicalPageSlotState reservedSlotState = m_Slots[physicalPageId];
                LogPageFillReserve(
                    physicalPageId,
                    in reservedSlotState,
                    allocationViewId,
                    frameIndex);
            }

            if (!pendingUpload)
            {
                PhysicalPageSlotState residentSlotState = m_Slots[physicalPageId];
                LogPageResidentCommit(
                    physicalPageId,
                    in residentSlotState,
                    frameIndex,
                    commitFrameIndex: -1,
                    wasPendingUpload: false,
                    wasResident: false);
            }
#endif
            return true;
        }

        internal bool TryAttachResidentPage(
            IVTPhysicalPoolOwner owner,
            VTProducerHandle producerHandle,
            string producerName,
            int pageIndex,
            in VirtualTexturePageCoord pageCoord,
            VirtualTextureViewId viewId,
            int frameIndex,
            bool locked,
            out int physicalPageId,
            out int generation)
        {
            physicalPageId = -1;
            generation = 0;
            if (owner == null)
                return false;

            if (!TryFindPhysicalPage(producerHandle, producerName, pageCoord, out physicalPageId, out generation))
                return false;

            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            if (!slotState.Resident || slotState.PendingUpload)
            {
                physicalPageId = -1;
                generation = 0;
                return false;
            }

            AddBinding(physicalPageId, owner, pageIndex, locked);
            Touch(physicalPageId, viewId, frameIndex, HasViewAffinity(viewId));
            return true;
        }

        internal bool TryFindPhysicalPage(
            VTProducerHandle producerHandle,
            string producerName,
            in VirtualTexturePageCoord pageCoord,
            out int physicalPageId,
            out int generation)
        {
            var identity = new VTPhysicalPageIdentity(producerHandle, producerName, pageCoord);
            if (m_PhysicalPageLookup.TryGetValue(identity, out int slotIndex))
            {
                PhysicalPageSlotState slotState = m_Slots[slotIndex];
                if (IsOccupied(slotState) && slotState.Identity.Equals(identity))
                {
                    physicalPageId = slotIndex;
                    generation = slotState.Generation;
                    return true;
                }
            }

            physicalPageId = -1;
            generation = 0;
            return false;
        }

        internal bool TryCommitPage(
            int physicalPageId,
            int generation,
            int commitFrameIndex = -1)
        {
            if (!TryGetSlot(physicalPageId, generation, out PhysicalPageSlotState slotState))
                return false;

#if VT_DEBUG
            bool wasPendingUpload = slotState.PendingUpload;
            bool wasResident = slotState.Resident;
            int allocationFrameIndex = slotState.LastAllocationFrame;
#endif
            slotState.PendingUpload = false;
            slotState.Resident = true;
            if (commitFrameIndex >= 0)
            {
                slotState.LastAllocationFrame = Mathf.Max(
                    slotState.LastAllocationFrame,
                    commitFrameIndex);
                slotState.LastAsyncCommitFrame = commitFrameIndex;
            }
            m_Slots[physicalPageId] = slotState;
#if VT_DEBUG
            LogPageResidentCommit(
                physicalPageId,
                in slotState,
                allocationFrameIndex,
                commitFrameIndex,
                wasPendingUpload,
                wasResident);
#endif
            return true;
        }

        internal bool TrySetLocked(
            int physicalPageId,
            int generation,
            IVTPhysicalPoolOwner owner,
            int pageIndex,
            bool locked)
        {
            if (!TryGetSlot(physicalPageId, generation, out PhysicalPageSlotState slotState))
                return false;

            if (!TrySetBindingLocked(physicalPageId, owner, pageIndex, locked))
                return false;

            slotState.Locked = IsAnyBindingLocked(physicalPageId);
            m_Slots[physicalPageId] = slotState;
            return true;
        }

        internal void Touch(
            int physicalPageId,
            VirtualTextureViewId viewId,
            int frameIndex,
            bool updateAffinity)
        {
            if (physicalPageId < 0 || physicalPageId >= m_Slots.Length)
                return;

            if (updateAffinity && HasViewAffinity(viewId))
            {
                PhysicalPageSlotState slotState = m_Slots[physicalPageId];
                slotState.AffinityViewId = viewId;
                slotState.LastAffinityFrame = frameIndex;
                m_Slots[physicalPageId] = slotState;
            }

            if (m_LastLruTouchFrames[physicalPageId] == frameIndex)
                return;

            m_LastLruTouchFrames[physicalPageId] = frameIndex;

            LinkedListNode<int> node = m_LruNodes[physicalPageId];
            if (node == null)
            {
                node = new LinkedListNode<int>(physicalPageId);
                m_LruNodes[physicalPageId] = node;
                m_LruPhysicalPages.AddLast(node);
                return;
            }

            if (node.List != null && node != m_LruPhysicalPages.Last)
            {
                m_LruPhysicalPages.Remove(node);
                m_LruPhysicalPages.AddLast(node);
            }
            else if (node.List == null)
            {
                m_LruPhysicalPages.AddLast(node);
            }
        }

        internal int FlushProducer(VTProducerHandle producerHandle, string producerName)
        {
            int flushedCount = 0;
            for (int slotIndex = m_Slots.Length - 1; slotIndex >= 0; slotIndex--)
            {
                PhysicalPageSlotState slotState = m_Slots[slotIndex];
                if (!IsOccupied(slotState) || !IsSameProducer(slotState.Identity, producerHandle, producerName))
                    continue;

                FlushPhysicalPage(slotIndex);
                flushedCount += 1;
            }

            return flushedCount;
        }

        internal int FlushRegion(
            int spaceId,
            int mip,
            RectInt pageRegion)
        {
            int flushedCount = 0;
            for (int slotIndex = m_Slots.Length - 1; slotIndex >= 0; slotIndex--)
            {
                PhysicalPageSlotState slotState = m_Slots[slotIndex];
                if (!IsOccupied(slotState)
                    || !HasBindingForSpace(slotIndex, spaceId)
                    || slotState.Identity.PageCoord.Mip != mip
                    || !pageRegion.Contains(new Vector2Int(slotState.Identity.PageCoord.X, slotState.Identity.PageCoord.Y)))
                {
                    continue;
                }

                flushedCount += FlushBindings(
                    slotIndex,
                    binding => binding.SpaceId == spaceId);
            }

            return flushedCount;
        }

        internal int FlushOwner(IVTPhysicalPoolOwner owner)
        {
            if (owner == null)
                return 0;

            int flushedCount = 0;
            for (int slotIndex = m_Slots.Length - 1; slotIndex >= 0; slotIndex--)
            {
                flushedCount += FlushBindings(
                    slotIndex,
                    binding => ReferenceEquals(binding.Owner, owner));
            }

            return flushedCount;
        }

        public void Dispose()
        {
            m_LruPhysicalPages.Clear();
            m_PhysicalPageLookup.Clear();
            m_FreePhysicalPages.Clear();
            for (int slotIndex = 0; slotIndex < m_Bindings.Length; slotIndex++)
                m_Bindings[slotIndex].Clear();

            if (m_Textures == null)
                return;

            DestroyTextures(m_Textures);
        }

        private static void DestroyTextures(IReadOnlyList<Texture2D> textures)
        {
            if (textures == null)
                return;

            for (int textureIndex = 0; textureIndex < textures.Count; textureIndex++)
            {
                if (textures[textureIndex] != null)
                    CoreUtils.Destroy(textures[textureIndex]);
            }
        }

        private void RecreatePhysicalTextures()
        {
            var replacements = new Texture2D[m_Textures.Length];
            try
            {
                for (int physicalGroup = 0; physicalGroup < replacements.Length; physicalGroup++)
                {
                    replacements[physicalGroup] = CreatePhysicalTexture(
                        m_PoolName,
                        Desc,
                        physicalGroup,
                        m_AtlasLayouts[physicalGroup]);
                }
            }
            catch
            {
                DestroyTextures(replacements);
                throw;
            }

            for (int physicalGroup = 0; physicalGroup < replacements.Length; physicalGroup++)
            {
                Texture2D previous = m_Textures[physicalGroup];
                m_Textures[physicalGroup] = replacements[physicalGroup];
                if (previous != null)
                    CoreUtils.Destroy(previous);
            }
        }

        private static Texture2D CreatePhysicalTexture(
            string poolName,
            in VTPhysicalPoolDesc desc,
            int physicalGroup,
            in VTPhysicalAtlasLayout layout)
        {
            GraphicsFormat storageFormat = desc.GetGroupStorageFormat(physicalGroup);
            if (storageFormat == GraphicsFormat.None)
                storageFormat = desc.GraphicsFormat;

            var texture = new Texture2D(
                layout.Width,
                layout.Height,
                storageFormat,
                TextureCreationFlags.None)
            {
                name = physicalGroup == 0
                    ? $"VividVT_{poolName}_PhysicalAtlas"
                    : $"VividVT_{poolName}_PhysicalAtlas_Group{physicalGroup}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            NativeArray<byte> rawTextureData = texture.GetRawTextureData<byte>();
            unsafe
            {
                UnsafeUtility.MemClear(rawTextureData.GetUnsafePtr(), rawTextureData.Length);
            }
            texture.Apply(false, true);
            return texture;
        }

        private bool TryGetSlot(int physicalPageId, int generation, out PhysicalPageSlotState slotState)
        {
            slotState = default;
            if (physicalPageId < 0 || physicalPageId >= m_Slots.Length)
                return false;

            slotState = m_Slots[physicalPageId];
            return IsOccupied(slotState) && slotState.Generation == generation;
        }

        private bool EvictPhysicalPageForReuse(int physicalPageId, int frameIndex)
        {
            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            if (!IsOccupied(slotState))
                return false;

#if VT_DEBUG
            InvalidateBindingsForReplacement(physicalPageId, frameIndex);
#else
            InvalidateBindings(physicalPageId);
#endif
            ClearPhysicalPage(physicalPageId, releaseToFreeList: false);
            m_EvictedPageCount += 1;
            return true;
        }

        private void FlushPhysicalPage(int physicalPageId)
        {
            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            if (!IsOccupied(slotState))
                return;

            InvalidateBindings(physicalPageId);
            ClearPhysicalPage(physicalPageId, releaseToFreeList: true);
        }

        private void AddBinding(
            int physicalPageId,
            IVTPhysicalPoolOwner owner,
            int pageIndex,
            bool locked)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                PhysicalPageBinding binding = bindings[bindingIndex];
                if (!ReferenceEquals(binding.Owner, owner) || binding.VirtualPageIndex != pageIndex)
                    continue;

                binding.Locked |= locked;
                bindings[bindingIndex] = binding;
                if (locked)
                    SetSlotLocked(physicalPageId, true);
                return;
            }

            bindings.Add(new PhysicalPageBinding
            {
                Owner = owner,
                SpaceId = owner.SpaceId,
                VirtualPageIndex = pageIndex,
                Locked = locked,
            });

            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            if (slotState.Owner == null)
            {
                slotState.Owner = owner;
                slotState.SpaceId = owner.SpaceId;
                slotState.VirtualPageIndex = pageIndex;
            }

            if (locked)
                slotState.Locked = true;

            m_Slots[physicalPageId] = slotState;
        }

        private bool TrySetBindingLocked(
            int physicalPageId,
            IVTPhysicalPoolOwner owner,
            int pageIndex,
            bool locked)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                PhysicalPageBinding binding = bindings[bindingIndex];
                if (!ReferenceEquals(binding.Owner, owner) || binding.VirtualPageIndex != pageIndex)
                    continue;

                binding.Locked = locked;
                bindings[bindingIndex] = binding;
                return true;
            }

            return false;
        }

        private bool IsAnyBindingLocked(int physicalPageId)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                if (bindings[bindingIndex].Locked)
                    return true;
            }

            return false;
        }

        private bool HasBindingForSpace(int physicalPageId, int spaceId)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                if (bindings[bindingIndex].SpaceId == spaceId)
                    return true;
            }

            return false;
        }

        private void SetSlotLocked(int physicalPageId, bool locked)
        {
            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            slotState.Locked = locked;
            m_Slots[physicalPageId] = slotState;
        }

        private int FlushBindings(
            int physicalPageId,
            Predicate<PhysicalPageBinding> predicate)
        {
            if (predicate == null || physicalPageId < 0 || physicalPageId >= m_Bindings.Length)
                return 0;

            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            if (bindings.Count == 0)
                return 0;

            int flushedCount = 0;
            int generation = m_Slots[physicalPageId].Generation;
            for (int bindingIndex = bindings.Count - 1; bindingIndex >= 0; bindingIndex--)
            {
                PhysicalPageBinding binding = bindings[bindingIndex];
                if (!predicate(binding))
                    continue;

                binding.Owner?.OnPhysicalPageInvalidated(binding.VirtualPageIndex, generation);
                bindings.RemoveAt(bindingIndex);
                flushedCount += 1;
            }

            if (flushedCount <= 0)
                return 0;

            if (bindings.Count == 0)
                ClearPhysicalPage(physicalPageId, releaseToFreeList: true);
            else
                PromotePrimaryBinding(physicalPageId);

            return flushedCount;
        }

        private void InvalidateBindings(int physicalPageId)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            int generation = m_Slots[physicalPageId].Generation;
            for (int bindingIndex = bindings.Count - 1; bindingIndex >= 0; bindingIndex--)
            {
                PhysicalPageBinding binding = bindings[bindingIndex];
                binding.Owner?.OnPhysicalPageInvalidated(binding.VirtualPageIndex, generation);
            }
        }

#if VT_DEBUG
        private void InvalidateBindingsForReplacement(int physicalPageId, int frameIndex)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            int generation = m_Slots[physicalPageId].Generation;
            for (int bindingIndex = bindings.Count - 1; bindingIndex >= 0; bindingIndex--)
            {
                PhysicalPageBinding binding = bindings[bindingIndex];
                bool invalidated = binding.Owner?.OnPhysicalPageInvalidated(
                    binding.VirtualPageIndex,
                    generation) ?? false;
                string ownerType = binding.Owner != null
                    ? binding.Owner.GetType().Name
                    : "<null>";
                string message =
                    $"[VividRP][VT_DEBUG][PageReplaceInvalidate] pool={m_DebugName} frame={frameIndex} "
                    + $"slot={physicalPageId} generation={generation} "
                    + $"binding={bindingIndex + 1}/{bindings.Count} space={binding.SpaceId} "
                    + $"pageIndex={binding.VirtualPageIndex} locked={binding.Locked} "
                    + $"owner={ownerType} accepted={invalidated}";
                if (invalidated)
                    Debug.Log(message);
                else
                    Debug.LogWarning(message);
            }
        }

        private void LogPageReplacementBegin(
            int physicalPageId,
            in PhysicalPageSlotState oldSlot,
            int oldBindingCount,
            IVTPhysicalPoolOwner newOwner,
            VTProducerHandle newProducerHandle,
            string newProducerName,
            int newPageIndex,
            int newPageMip,
            in VirtualTexturePageCoord newPageCoord,
            VirtualTextureViewId activeViewId,
            VirtualTextureViewId allocationViewId,
            bool updateAffinity,
            int frameIndex,
            bool newLocked,
            bool newPendingUpload,
            in VTPageRequestDebugInfo requestDebugInfo)
        {
            Debug.Log(
                $"[VividRP][VT_DEBUG][PageReplaceBegin] pool={m_DebugName} frame={frameIndex} slot={physicalPageId} "
                + $"old=(space:{oldSlot.SpaceId},pageIndex:{oldSlot.VirtualPageIndex},mip:{oldSlot.VirtualPageMip},"
                + $"coord:{oldSlot.Identity.PageCoord},producer:{FormatProducer(oldSlot.Identity.ProducerHandle, oldSlot.Identity.ProducerName)},"
                + $"generation:{oldSlot.Generation},resident:{oldSlot.Resident},pending:{oldSlot.PendingUpload},"
                + $"locked:{oldSlot.Locked},allocatedFrame:{oldSlot.LastAllocationFrame},"
                + $"asyncCommitFrame:{oldSlot.LastAsyncCommitFrame},lastTouchFrame:{m_LastLruTouchFrames[physicalPageId]},"
                + $"affinity:{oldSlot.AffinityViewId},affinityFrame:{oldSlot.LastAffinityFrame},bindings:{oldBindingCount}) "
                + $"new=(space:{newOwner.SpaceId},pageIndex:{newPageIndex},mip:{newPageMip},coord:{newPageCoord},"
                + $"producer:{FormatProducer(newProducerHandle, newProducerName)},locked:{newLocked},pending:{newPendingUpload}) "
                + $"activeView={activeViewId} allocationView={allocationViewId} updateAffinity={updateAffinity} "
                + $"{FormatRequestDebug(in requestDebugInfo)} "
                + $"evictionCountBefore={m_EvictedPageCount}");
        }

        private void LogPageReplacementCommit(
            int physicalPageId,
            in PhysicalPageSlotState oldSlot,
            int oldBindingCount,
            in PhysicalPageSlotState newSlot,
            int frameIndex)
        {
            Debug.Log(
                $"[VividRP][VT_DEBUG][PageReplaceCommit] pool={m_DebugName} frame={frameIndex} slot={physicalPageId} "
                + $"old=(space:{oldSlot.SpaceId},pageIndex:{oldSlot.VirtualPageIndex},mip:{oldSlot.VirtualPageMip},"
                + $"coord:{oldSlot.Identity.PageCoord},generation:{oldSlot.Generation},bindings:{oldBindingCount}) "
                + $"new=(space:{newSlot.SpaceId},pageIndex:{newSlot.VirtualPageIndex},mip:{newSlot.VirtualPageMip},"
                + $"coord:{newSlot.Identity.PageCoord},producer:{FormatProducer(newSlot.Identity.ProducerHandle, newSlot.Identity.ProducerName)},"
                + $"generation:{newSlot.Generation},resident:{newSlot.Resident},pending:{newSlot.PendingUpload},"
                + $"locked:{newSlot.Locked},lastTouchFrame:{m_LastLruTouchFrames[physicalPageId]},"
                + $"affinity:{newSlot.AffinityViewId},affinityFrame:{newSlot.LastAffinityFrame}) "
                + $"{FormatRequestDebug(in newSlot.RequestDebugInfo)} "
                + $"evictionCountAfter={m_EvictedPageCount}");
        }

        private void LogPageFillReserve(
            int physicalPageId,
            in PhysicalPageSlotState slot,
            VirtualTextureViewId allocationViewId,
            int frameIndex)
        {
            Debug.Log(
                $"[VividRP][VT_DEBUG][PageFillReserve] pool={m_DebugName} frame={frameIndex} slot={physicalPageId} "
                + $"space={slot.SpaceId} pageIndex={slot.VirtualPageIndex} mip={slot.VirtualPageMip} "
                + $"coord={slot.Identity.PageCoord} producer={FormatProducer(slot.Identity.ProducerHandle, slot.Identity.ProducerName)} "
                + $"generation={slot.Generation} resident={slot.Resident} pending={slot.PendingUpload} locked={slot.Locked} "
                + $"allocationView={allocationViewId} affinity={slot.AffinityViewId} affinityFrame={slot.LastAffinityFrame} "
                + $"lastTouchFrame={m_LastLruTouchFrames[physicalPageId]} {FormatRequestDebug(in slot.RequestDebugInfo)} "
                + $"freePagesAfter={m_FreePhysicalPages.Count}");
        }

        private void LogPageResidentCommit(
            int physicalPageId,
            in PhysicalPageSlotState slot,
            int allocationFrameIndex,
            int commitFrameIndex,
            bool wasPendingUpload,
            bool wasResident)
        {
            int resolvedCommitFrameIndex = commitFrameIndex >= 0
                ? commitFrameIndex
                : allocationFrameIndex;
            int latencyFrames = resolvedCommitFrameIndex >= 0 && allocationFrameIndex >= 0
                ? Mathf.Max(0, resolvedCommitFrameIndex - allocationFrameIndex)
                : -1;
            Debug.Log(
                $"[VividRP][VT_DEBUG][PageResidentCommit] pool={m_DebugName} frame={resolvedCommitFrameIndex} slot={physicalPageId} "
                + $"space={slot.SpaceId} pageIndex={slot.VirtualPageIndex} mip={slot.VirtualPageMip} "
                + $"coord={slot.Identity.PageCoord} producer={FormatProducer(slot.Identity.ProducerHandle, slot.Identity.ProducerName)} "
                + $"generation={slot.Generation} allocationFrame={allocationFrameIndex} latencyFrames={latencyFrames} "
                + $"commitPath={(commitFrameIndex >= 0 ? "async" : "immediate")} "
                + $"wasPending={wasPendingUpload} wasResident={wasResident} resident={slot.Resident} pending={slot.PendingUpload} "
                + $"locked={slot.Locked} lastTouchFrame={m_LastLruTouchFrames[physicalPageId]} "
                + $"affinity={slot.AffinityViewId} affinityFrame={slot.LastAffinityFrame} "
                + FormatRequestDebug(in slot.RequestDebugInfo));
        }

        private static string FormatRequestDebug(in VTPageRequestDebugInfo debugInfo)
        {
            string requestKind = debugInfo.RequestKind switch
            {
                VTPageRequestKind.Bootstrap => "bootstrap",
                VTPageRequestKind.Locked => "locked",
                VTPageRequestKind.Demand => "demand",
                VTPageRequestKind.Refinement => "refinement",
                VTPageRequestKind.Neighbor => "neighbor",
                _ => "unknown",
            };
            return $"requestKind={requestKind} sourceCoord={debugInfo.SourceCoord} "
                   + $"effectiveCoord={debugInfo.EffectiveCoord} mipGap={debugInfo.MipGap} "
                   + $"weightedScore={debugInfo.WeightedScore}";
        }

        private static string FormatProducer(VTProducerHandle producerHandle, string producerName)
        {
            string name = string.IsNullOrEmpty(producerName) ? "<unnamed>" : producerName;
            return $"{producerHandle}/{name}";
        }
#endif

        private void PromotePrimaryBinding(int physicalPageId)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            if (bindings.Count == 0)
                return;

            PhysicalPageBinding primary = bindings[0];
            bool locked = false;
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                locked |= bindings[bindingIndex].Locked;

            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            slotState.Owner = primary.Owner;
            slotState.SpaceId = primary.SpaceId;
            slotState.VirtualPageIndex = primary.VirtualPageIndex;
            slotState.Locked = locked;
            m_Slots[physicalPageId] = slotState;
        }

        private void ClearPhysicalPage(int physicalPageId, bool releaseToFreeList)
        {
            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            RemovePhysicalPageLookup(physicalPageId, slotState.Identity);
            slotState.Owner = null;
            slotState.SpaceId = 0;
            slotState.VirtualPageIndex = -1;
            slotState.VirtualPageMip = 0;
            slotState.Generation = 0;
            slotState.LastAllocationFrame = -1;
            slotState.LastAsyncCommitFrame = -1;
            slotState.AffinityViewId = VirtualTextureViewId.Invalid;
            slotState.LastAffinityFrame = -1;
            slotState.Identity = default;
            slotState.Resident = false;
            slotState.PendingUpload = false;
            slotState.Locked = false;
#if VT_DEBUG
            slotState.RequestDebugInfo = default;
#endif
            m_Slots[physicalPageId] = slotState;
            m_LastLruTouchFrames[physicalPageId] = int.MinValue;
            m_Bindings[physicalPageId].Clear();

            if (!releaseToFreeList)
                return;

            LinkedListNode<int> node = m_LruNodes[physicalPageId];
            if (node?.List != null)
                m_LruPhysicalPages.Remove(node);

            m_FreePhysicalPages.Push(physicalPageId);
        }

        private static long GetTextureByteCount(GraphicsFormat format, int width, int height)
        {
            long blockWidth = Math.Max(1u, GraphicsFormatUtility.GetBlockWidth(format));
            long blockHeight = Math.Max(1u, GraphicsFormatUtility.GetBlockHeight(format));
            long blockSize = Math.Max(1u, GraphicsFormatUtility.GetBlockSize(format));
            long blocksX = (Math.Max(1, width) + blockWidth - 1) / blockWidth;
            long blocksY = (Math.Max(1, height) + blockHeight - 1) / blockHeight;
            return checked(blocksX * blocksY * blockSize);
        }

        private void AddPhysicalPageLookup(int physicalPageId, in VTPhysicalPageIdentity identity)
        {
            m_NextPhysicalPageWithSameIdentity[physicalPageId] = -1;
            if (!m_PhysicalPageLookup.TryGetValue(identity, out int firstPhysicalPageId))
            {
                m_PhysicalPageLookup.Add(identity, physicalPageId);
                return;
            }

            if (physicalPageId < firstPhysicalPageId)
            {
                m_NextPhysicalPageWithSameIdentity[physicalPageId] = firstPhysicalPageId;
                m_PhysicalPageLookup[identity] = physicalPageId;
                return;
            }

            int previousPhysicalPageId = firstPhysicalPageId;
            int nextPhysicalPageId = m_NextPhysicalPageWithSameIdentity[previousPhysicalPageId];
            while (nextPhysicalPageId >= 0 && nextPhysicalPageId < physicalPageId)
            {
                previousPhysicalPageId = nextPhysicalPageId;
                nextPhysicalPageId = m_NextPhysicalPageWithSameIdentity[previousPhysicalPageId];
            }

            m_NextPhysicalPageWithSameIdentity[physicalPageId] = nextPhysicalPageId;
            m_NextPhysicalPageWithSameIdentity[previousPhysicalPageId] = physicalPageId;
        }

        private void RemovePhysicalPageLookup(int physicalPageId, in VTPhysicalPageIdentity identity)
        {
            if (!m_PhysicalPageLookup.TryGetValue(identity, out int firstPhysicalPageId))
                return;

            int nextPhysicalPageId = m_NextPhysicalPageWithSameIdentity[physicalPageId];
            if (firstPhysicalPageId == physicalPageId)
            {
                if (nextPhysicalPageId >= 0)
                    m_PhysicalPageLookup[identity] = nextPhysicalPageId;
                else
                    m_PhysicalPageLookup.Remove(identity);

                m_NextPhysicalPageWithSameIdentity[physicalPageId] = -1;
                return;
            }

            int previousPhysicalPageId = firstPhysicalPageId;
            while (previousPhysicalPageId >= 0)
            {
                if (m_NextPhysicalPageWithSameIdentity[previousPhysicalPageId] == physicalPageId)
                {
                    m_NextPhysicalPageWithSameIdentity[previousPhysicalPageId] = nextPhysicalPageId;
                    break;
                }

                previousPhysicalPageId = m_NextPhysicalPageWithSameIdentity[previousPhysicalPageId];
            }

            m_NextPhysicalPageWithSameIdentity[physicalPageId] = -1;
        }

        private int FindEvictionCandidate(int frameIndex, VirtualTextureViewId activeViewId)
        {
            int candidatePhysicalPageId = -1;
            int fallbackPhysicalPageId = -1;

            LinkedListNode<int> node = m_LruPhysicalPages.First;
            while (node != null)
            {
                int physicalPageId = node.Value;
                if (!CanEvict(physicalPageId, frameIndex))
                {
                    node = node.Next;
                    continue;
                }

                if (IsBetterEvictionCandidate(physicalPageId, fallbackPhysicalPageId))
                    fallbackPhysicalPageId = physicalPageId;

                if (IsProtectedByActiveViewAffinity(physicalPageId, activeViewId))
                {
                    node = node.Next;
                    continue;
                }

                if (IsBetterEvictionCandidate(physicalPageId, candidatePhysicalPageId))
                    candidatePhysicalPageId = physicalPageId;

                node = node.Next;
            }

            return candidatePhysicalPageId >= 0 ? candidatePhysicalPageId : fallbackPhysicalPageId;
        }

        private bool IsBetterEvictionCandidate(int physicalPageId, int currentPhysicalPageId)
        {
            if (currentPhysicalPageId < 0)
                return true;

            // Match the runtime VT age key: age is authoritative and mip only
            // differentiates pages observed in the same frame.
            int lastTouchFrame = m_LastLruTouchFrames[physicalPageId];
            int currentLastTouchFrame = m_LastLruTouchFrames[currentPhysicalPageId];
            if (lastTouchFrame != currentLastTouchFrame)
                return lastTouchFrame < currentLastTouchFrame;

            int pageMip = m_Slots[physicalPageId].VirtualPageMip;
            int currentPageMip = m_Slots[currentPhysicalPageId].VirtualPageMip;
            if (pageMip != currentPageMip)
                return pageMip < currentPageMip;

            return physicalPageId < currentPhysicalPageId;
        }

        private bool CanEvict(int physicalPageId, int frameIndex)
        {
            if (physicalPageId < 0 || physicalPageId >= m_Slots.Length)
                return false;

            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            int lastTouchFrame = m_LastLruTouchFrames[physicalPageId];
            bool outsideFeedbackProtectionWindow = lastTouchFrame == int.MinValue
                                                   || frameIndex < lastTouchFrame
                                                   || frameIndex - lastTouchFrame
                                                   >= FeedbackEvictionProtectionFrames;
            return IsOccupied(slotState)
                   && slotState.LastAllocationFrame != frameIndex
                   && outsideFeedbackProtectionWindow
                   && (slotState.LastAsyncCommitFrame < 0
                       || frameIndex - slotState.LastAsyncCommitFrame
                       >= AsyncCommitEvictionProtectionFrames)
                   && !slotState.PendingUpload
                   && !slotState.Locked;
        }

        private bool IsProtectedByActiveViewAffinity(
            int physicalPageId,
            VirtualTextureViewId activeViewId)
        {
            if ((!activeViewId.IsValid && !activeViewId.IsCameraTypeOnly)
                || physicalPageId < 0
                || physicalPageId >= m_Slots.Length)
            {
                return false;
            }

            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            if (slotState.LastAffinityFrame < 0)
                return false;

            return activeViewId.IsValid
                ? slotState.AffinityViewId.Equals(activeViewId)
                : slotState.AffinityViewId.CameraType == activeViewId.CameraType;
        }

        private static bool IsOccupied(in PhysicalPageSlotState slotState)
        {
            return slotState.Owner != null && slotState.VirtualPageIndex >= 0;
        }

        private static bool HasViewAffinity(VirtualTextureViewId viewId)
        {
            return viewId.IsValid || viewId.IsCameraTypeOnly;
        }

        private static bool IsSameProducer(
            in VTPhysicalPageIdentity identity,
            VTProducerHandle producerHandle,
            string producerName)
        {
            if (producerHandle.IsValid
                && identity.ProducerHandle.IsValid
                && identity.ProducerHandle.Equals(producerHandle))
            {
                return true;
            }

            if (string.IsNullOrEmpty(identity.ProducerName) || string.IsNullOrEmpty(producerName))
                return false;

            return string.Equals(identity.ProducerName, producerName, StringComparison.Ordinal);
        }
    }
}
