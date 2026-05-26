using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal readonly struct VTPhysicalPoolDesc : IEquatable<VTPhysicalPoolDesc>
    {
        internal VTPhysicalPoolDesc(
            int pageSize,
            int borderSize,
            int pageCount,
            GraphicsFormat graphicsFormat,
            string layerGroup)
        {
            PageSize = pageSize;
            BorderSize = borderSize;
            PhysicalPageSize = pageSize + borderSize * 2;
            PageCount = pageCount;
            GraphicsFormat = graphicsFormat;
            LayerGroup = string.IsNullOrWhiteSpace(layerGroup) ? "Default" : layerGroup;
        }

        internal int PageSize { get; }

        internal int BorderSize { get; }

        internal int PhysicalPageSize { get; }

        internal int PageCount { get; }

        internal GraphicsFormat GraphicsFormat { get; }

        internal string LayerGroup { get; }

        internal static VTPhysicalPoolDesc FromSpaceDesc(in VirtualTextureSpaceDesc desc)
        {
            return new VTPhysicalPoolDesc(
                desc.PageSize,
                desc.BorderSize,
                desc.CachePageCount,
                desc.GraphicsFormat,
                "Default");
        }

        public bool Equals(VTPhysicalPoolDesc other)
        {
            return PageSize == other.PageSize
                   && BorderSize == other.BorderSize
                   && PageCount == other.PageCount
                   && GraphicsFormat == other.GraphicsFormat
                   && string.Equals(LayerGroup, other.LayerGroup, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is VTPhysicalPoolDesc other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(PageSize, BorderSize, PageCount, GraphicsFormat, LayerGroup);
        }
    }

    internal readonly struct VTPhysicalPoolStats
    {
        internal VTPhysicalPoolStats(
            int poolCount,
            int residentPageCount,
            int freePageCount,
            int lockedPageCount,
            int evictedPageCount)
        {
            PoolCount = poolCount;
            ResidentPageCount = residentPageCount;
            FreePageCount = freePageCount;
            LockedPageCount = lockedPageCount;
            EvictedPageCount = evictedPageCount;
        }

        internal int PoolCount { get; }

        internal int ResidentPageCount { get; }

        internal int FreePageCount { get; }

        internal int LockedPageCount { get; }

        internal int EvictedPageCount { get; }
    }

    internal readonly struct VTPhysicalPageIdentity : IEquatable<VTPhysicalPageIdentity>
    {
        internal VTPhysicalPageIdentity(VTProducer producer, in VirtualTexturePageCoord pageCoord)
        {
            Producer = producer;
            ProducerName = producer?.Name;
            PageCoord = pageCoord;
        }

        internal VTProducer Producer { get; }

        internal string ProducerName { get; }

        internal VirtualTexturePageCoord PageCoord { get; }

        public bool Equals(VTPhysicalPageIdentity other)
        {
            bool sameProducer = ReferenceEquals(Producer, other.Producer)
                                || (!string.IsNullOrEmpty(ProducerName)
                                    && string.Equals(ProducerName, other.ProducerName, StringComparison.Ordinal));
            return sameProducer && PageCoord.Equals(other.PageCoord);
        }

        public override bool Equals(object obj)
        {
            return obj is VTPhysicalPageIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ProducerName ?? string.Empty, PageCoord);
        }
    }

    internal interface IVTPhysicalPoolOwner
    {
        int SpaceId { get; }

        bool OnPhysicalPageInvalidated(int pageIndex, int generation);
    }

    internal sealed class VTPhysicalPool : IDisposable
    {
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
            public VirtualTextureViewId AffinityViewId;
            public int LastAffinityFrame;
            public VTPhysicalPageIdentity Identity;
            public bool Resident;
            public bool PendingUpload;
            public bool Locked;
        }

        private readonly PhysicalPageSlotState[] m_Slots;
        private readonly Stack<int> m_FreePhysicalPages;
        private readonly LinkedList<int> m_LruPhysicalPages = new();
        private readonly LinkedListNode<int>[] m_LruNodes;
        private readonly List<PhysicalPageBinding>[] m_Bindings;
        private readonly Texture2DArray m_Texture;

        private int m_NextGeneration;
        private int m_RefCount;
        private int m_EvictedPageCount;

        internal VTPhysicalPool(string name, in VTPhysicalPoolDesc desc)
        {
            Desc = desc;
            string poolName = string.IsNullOrWhiteSpace(name) ? "Shared" : name;
            m_Slots = new PhysicalPageSlotState[Mathf.Max(1, desc.PageCount)];
            for (int slotIndex = 0; slotIndex < m_Slots.Length; slotIndex++)
            {
                m_Slots[slotIndex].VirtualPageIndex = -1;
                m_Slots[slotIndex].AffinityViewId = VirtualTextureViewId.Invalid;
                m_Slots[slotIndex].LastAffinityFrame = -1;
            }

            m_LruNodes = new LinkedListNode<int>[m_Slots.Length];
            m_Bindings = new List<PhysicalPageBinding>[m_Slots.Length];
            for (int slotIndex = 0; slotIndex < m_Bindings.Length; slotIndex++)
                m_Bindings[slotIndex] = new List<PhysicalPageBinding>(1);

            m_FreePhysicalPages = new Stack<int>(m_Slots.Length);
            for (int slotIndex = m_Slots.Length - 1; slotIndex >= 0; slotIndex--)
                m_FreePhysicalPages.Push(slotIndex);

            m_Texture = new Texture2DArray(
                desc.PhysicalPageSize,
                desc.PhysicalPageSize,
                m_Slots.Length,
                desc.GraphicsFormat,
                TextureCreationFlags.None)
            {
                name = $"VividVT_{poolName}_PhysicalPool",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            m_Texture.Apply(false, false);
        }

        internal VTPhysicalPoolDesc Desc { get; }

        internal Texture2DArray Texture => m_Texture;

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
            VTProducer producer,
            int pageIndex,
            int pageMip,
            in VirtualTexturePageCoord pageCoord,
            VirtualTextureViewId activeViewId,
            VirtualTextureViewId allocationViewId,
            bool updateAffinity,
            int frameIndex,
            bool locked,
            bool pendingUpload,
            out int physicalPageId,
            out int generation,
            out bool evicted)
        {
            physicalPageId = -1;
            generation = 0;
            evicted = false;
            if (owner == null)
                return false;

            if (m_FreePhysicalPages.Count > 0)
            {
                physicalPageId = m_FreePhysicalPages.Pop();
            }
            else
            {
                physicalPageId = FindEvictionCandidate(frameIndex, activeViewId);
                if (physicalPageId < 0)
                    return false;

                evicted = EvictPhysicalPageForReuse(physicalPageId);
            }

            generation = ++m_NextGeneration;
            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            slotState.Owner = owner;
            slotState.SpaceId = owner.SpaceId;
            slotState.VirtualPageIndex = pageIndex;
            slotState.VirtualPageMip = pageMip;
            slotState.Generation = generation;
            slotState.LastAllocationFrame = frameIndex;
            slotState.Identity = new VTPhysicalPageIdentity(producer, pageCoord);
            slotState.Resident = !pendingUpload;
            slotState.PendingUpload = pendingUpload;
            slotState.Locked = locked;
            slotState.AffinityViewId = VirtualTextureViewId.Invalid;
            slotState.LastAffinityFrame = -1;
            m_Slots[physicalPageId] = slotState;
            m_Bindings[physicalPageId].Clear();
            AddBinding(physicalPageId, owner, pageIndex, locked);
            Touch(physicalPageId, allocationViewId, frameIndex, updateAffinity);
            return true;
        }

        internal bool TryAttachResidentPage(
            IVTPhysicalPoolOwner owner,
            VTProducer producer,
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

            if (!TryFindPhysicalPage(producer, pageCoord, out physicalPageId, out generation))
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
            VTProducer producer,
            in VirtualTexturePageCoord pageCoord,
            out int physicalPageId,
            out int generation)
        {
            var identity = new VTPhysicalPageIdentity(producer, pageCoord);
            for (int slotIndex = 0; slotIndex < m_Slots.Length; slotIndex++)
            {
                PhysicalPageSlotState slotState = m_Slots[slotIndex];
                if (!IsOccupied(slotState) || !slotState.Identity.Equals(identity))
                    continue;

                physicalPageId = slotIndex;
                generation = slotState.Generation;
                return true;
            }

            physicalPageId = -1;
            generation = 0;
            return false;
        }

        internal bool TryCommitPage(int physicalPageId, int generation)
        {
            if (!TryGetSlot(physicalPageId, generation, out PhysicalPageSlotState slotState))
                return false;

            slotState.PendingUpload = false;
            slotState.Resident = true;
            m_Slots[physicalPageId] = slotState;
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

        internal int FlushProducer(VTProducer producer)
        {
            int flushedCount = 0;
            for (int slotIndex = m_Slots.Length - 1; slotIndex >= 0; slotIndex--)
            {
                PhysicalPageSlotState slotState = m_Slots[slotIndex];
                if (!IsOccupied(slotState) || !IsSameProducer(slotState.Identity.Producer, producer))
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
            m_FreePhysicalPages.Clear();
            for (int slotIndex = 0; slotIndex < m_Bindings.Length; slotIndex++)
                m_Bindings[slotIndex].Clear();

            if (m_Texture != null)
                CoreUtils.Destroy(m_Texture);
        }

        private bool TryGetSlot(int physicalPageId, int generation, out PhysicalPageSlotState slotState)
        {
            slotState = default;
            if (physicalPageId < 0 || physicalPageId >= m_Slots.Length)
                return false;

            slotState = m_Slots[physicalPageId];
            return IsOccupied(slotState) && slotState.Generation == generation;
        }

        private bool EvictPhysicalPageForReuse(int physicalPageId)
        {
            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            if (!IsOccupied(slotState))
                return false;

            InvalidateBindings(physicalPageId);
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
            slotState.Owner = null;
            slotState.SpaceId = 0;
            slotState.VirtualPageIndex = -1;
            slotState.VirtualPageMip = 0;
            slotState.Generation = 0;
            slotState.LastAllocationFrame = -1;
            slotState.AffinityViewId = VirtualTextureViewId.Invalid;
            slotState.LastAffinityFrame = -1;
            slotState.Identity = default;
            slotState.Resident = false;
            slotState.PendingUpload = false;
            slotState.Locked = false;
            m_Slots[physicalPageId] = slotState;
            m_Bindings[physicalPageId].Clear();

            if (!releaseToFreeList)
                return;

            LinkedListNode<int> node = m_LruNodes[physicalPageId];
            if (node?.List != null)
                m_LruPhysicalPages.Remove(node);

            m_FreePhysicalPages.Push(physicalPageId);
        }

        private int FindEvictionCandidate(int frameIndex, VirtualTextureViewId activeViewId)
        {
            int candidatePhysicalPageId = -1;
            int candidateMip = int.MaxValue;
            int fallbackPhysicalPageId = -1;
            int fallbackMip = int.MaxValue;

            LinkedListNode<int> node = m_LruPhysicalPages.First;
            while (node != null)
            {
                int physicalPageId = node.Value;
                if (!CanEvict(physicalPageId, frameIndex))
                {
                    node = node.Next;
                    continue;
                }

                int pageMip = m_Slots[physicalPageId].VirtualPageMip;
                if (fallbackPhysicalPageId < 0 || pageMip < fallbackMip)
                {
                    fallbackPhysicalPageId = physicalPageId;
                    fallbackMip = pageMip;
                }

                if (IsProtectedByActiveViewAffinity(physicalPageId, activeViewId))
                {
                    node = node.Next;
                    continue;
                }

                if (candidatePhysicalPageId < 0 || pageMip < candidateMip)
                {
                    candidatePhysicalPageId = physicalPageId;
                    candidateMip = pageMip;
                    if (candidateMip == 0)
                        break;
                }

                node = node.Next;
            }

            return candidatePhysicalPageId >= 0 ? candidatePhysicalPageId : fallbackPhysicalPageId;
        }

        private bool CanEvict(int physicalPageId, int frameIndex)
        {
            if (physicalPageId < 0 || physicalPageId >= m_Slots.Length)
                return false;

            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            return IsOccupied(slotState)
                   && slotState.LastAllocationFrame != frameIndex
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

        private static bool IsSameProducer(VTProducer left, VTProducer right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left == null || right == null)
                return false;

            return string.Equals(left.Name, right.Name, StringComparison.Ordinal);
        }
    }
}
