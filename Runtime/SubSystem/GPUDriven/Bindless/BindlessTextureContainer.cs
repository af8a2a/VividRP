using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VividRP.Runtime.GPUDriven.Bindless
{
    public sealed class BindlessTextureContainer : IDisposable
    {
        public const uint InvalidTextureIndex = uint.MaxValue;
        private const int InitialDirtyTextureCapacity = 16;

        private readonly IBindlessTextureDescriptorAllocator m_Allocator;
        private readonly Dictionary<EntityId, BindlessTextureInfo> m_TextureInfos = new();
        private readonly Stack<uint> m_FreeDescriptorIndices = new();
        private readonly List<Texture> m_PotentiallyDirtyTextures = new(InitialDirtyTextureCapacity);
        private readonly List<EntityId> m_PotentiallyDirtyTextureIds = new(InitialDirtyTextureCapacity);
        private readonly List<EntityId> m_PotentiallyDestroyedTextureIds = new(InitialDirtyTextureCapacity);
        private readonly List<RetiredDescriptorSlot> m_RetiredDescriptorSlots = new();

        private uint m_AllocatedDescriptorCount;
        private uint m_LinearAllocatedDescriptorCount;
        private uint m_TextureBindingRevision;
        private bool m_IsDisposed;

        public BindlessTextureContainer()
            : this(new NativeBindlessTextureDescriptorAllocator())
        {
        }

        public BindlessTextureContainer(IBindlessTextureDescriptorAllocator allocator)
        {
            m_Allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        }

        public bool IsAvailable => m_Allocator.IsAvailable;

        public uint DescriptorHeapCount => m_Allocator.DescriptorHeapCount;

        public uint DescriptorStartIndex => m_Allocator.DescriptorStartIndex;

        public uint DescriptorCapacity => m_Allocator.DescriptorCapacity;

        public uint AllocatedDescriptorCount => m_AllocatedDescriptorCount;

        public int RegisteredTextureCount => m_TextureInfos.Count;

        public uint TextureBindingRevision => m_TextureBindingRevision;

        public uint CreateSRVDescriptorCallCountThisFrame => m_Allocator.CreateSRVDescriptorCallCountThisFrame;

        public string UnavailableReason => m_Allocator.UnavailableReason;

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            m_TextureInfos.Clear();
            m_FreeDescriptorIndices.Clear();
            m_PotentiallyDirtyTextures.Clear();
            m_PotentiallyDirtyTextureIds.Clear();
            m_PotentiallyDestroyedTextureIds.Clear();
            m_RetiredDescriptorSlots.Clear();
            m_AllocatedDescriptorCount = 0;
            m_LinearAllocatedDescriptorCount = 0;
            m_TextureBindingRevision = 0;
            m_IsDisposed = true;
        }

        public bool TryGetOrCreateIndex(Texture texture, out uint index)
        {
            ThrowIfDisposed();

            if (texture == null)
            {
                index = InvalidTextureIndex;
                return false;
            }

            return TryGetOrCreateIndex(texture, GetTrackedTextureId(texture), out index);
        }

        public bool TryGetExistingIndex(Texture texture, out uint index)
        {
            ThrowIfDisposed();

            if (texture != null && m_TextureInfos.TryGetValue(GetTrackedTextureId(texture), out BindlessTextureInfo info))
            {
                index = info.Index;
                return true;
            }

            index = InvalidTextureIndex;
            return false;
        }

        public bool TryAllocateRange(uint count, out uint startIndex)
        {
            ThrowIfDisposed();

            if (!m_Allocator.IsAvailable || count == 0 || count > RemainingDescriptorCount())
            {
                startIndex = InvalidTextureIndex;
                return false;
            }

            uint remainingLinearDescriptorCount = RemainingLinearDescriptorCount();
            if (count > remainingLinearDescriptorCount)
            {
                startIndex = InvalidTextureIndex;
                return false;
            }

            startIndex = m_Allocator.DescriptorStartIndex + m_Allocator.DescriptorCapacity - (m_LinearAllocatedDescriptorCount + count);
            m_LinearAllocatedDescriptorCount += count;
            m_AllocatedDescriptorCount += count;
            return true;
        }

        public void MarkTextureDirty(Texture texture)
        {
            ThrowIfDisposed();

            if (texture == null)
            {
                return;
            }

            AddPotentialDirtyTexture(GetTrackedTextureId(texture), texture);
        }

        public void MarkTextureDestroyed(EntityId textureId)
        {
            ThrowIfDisposed();
            AddPotentialDestroyedDirtyTexture(textureId);
        }

        public void PreRender()
        {
            ThrowIfDisposed();
            RecycleRetiredDescriptorSlots();
            UpdateDirtyTextures();
        }

        public void ResetPerFrameStats()
        {
            ThrowIfDisposed();
            m_Allocator.ResetPerFrameStats();
        }

        internal void AddPotentialDirtyTextureRange(NativeArray<EntityId> textureIds, List<Object> textures)
        {
            ThrowIfDisposed();

            if (textureIds.Length == 0 || textures == null || textures.Count == 0)
            {
                return;
            }

            int count = Math.Min(textureIds.Length, textures.Count);
            for (int index = 0; index < count; index++)
            {
                if (textures[index] is Texture texture)
                {
                    AddPotentialDirtyTexture(textureIds[index], texture);
                }
            }
        }

        internal void AddPotentialDestroyedDirtyTextureRange(NativeArray<EntityId> textureIds)
        {
            ThrowIfDisposed();

            for (int index = 0; index < textureIds.Length; index++)
            {
                AddPotentialDestroyedDirtyTexture(textureIds[index]);
            }
        }

        private bool TryGetOrCreateIndex(Texture texture, EntityId textureId, out uint index)
        {
            if (!m_Allocator.IsAvailable)
            {
                index = InvalidTextureIndex;
                return false;
            }

            if (texture == null)
            {
                index = InvalidTextureIndex;
                return false;
            }

            IntPtr nativeTexturePtr = texture.GetNativeTexturePtr();
            bool hasExistingInfo = m_TextureInfos.TryGetValue(textureId, out BindlessTextureInfo info);
            if (hasExistingInfo)
            {
                if (info.NativeTexturePtr == nativeTexturePtr)
                {
                    index = info.Index;
                    return true;
                }
            }

            if (!TryCreateTextureDescriptorAtNextAvailableIndex(texture, out index))
            {
                return false;
            }

            if (hasExistingInfo)
            {
                RetireDescriptorIndex(info.Index);
                IncrementTextureBindingRevision();
            }

            m_TextureInfos[textureId] = new BindlessTextureInfo(index, nativeTexturePtr);
            return true;
        }

        private bool TryCreateTextureDescriptorAtNextAvailableIndex(Texture texture, out uint index)
        {
            RecycleRetiredDescriptorSlots();

            if (m_FreeDescriptorIndices.Count > 0)
            {
                uint recycledIndex = m_FreeDescriptorIndices.Peek();
                if (!m_Allocator.TryCreateTextureDescriptor(texture, recycledIndex))
                {
                    index = InvalidTextureIndex;
                    return false;
                }

                m_FreeDescriptorIndices.Pop();
                m_AllocatedDescriptorCount++;
                index = recycledIndex;
                return true;
            }

            if (!m_Allocator.IsAvailable || m_LinearAllocatedDescriptorCount >= m_Allocator.DescriptorCapacity)
            {
                index = InvalidTextureIndex;
                return false;
            }

            uint newIndex = m_Allocator.DescriptorStartIndex + m_Allocator.DescriptorCapacity - 1 - m_LinearAllocatedDescriptorCount;
            if (!m_Allocator.TryCreateTextureDescriptor(texture, newIndex))
            {
                index = InvalidTextureIndex;
                return false;
            }

            m_LinearAllocatedDescriptorCount++;
            m_AllocatedDescriptorCount++;
            index = newIndex;
            return true;
        }

        private void UpdateDirtyTextures()
        {
            if (m_PotentiallyDirtyTextureIds.Count > 0)
            {
                int dirtyTextureCount = Math.Min(m_PotentiallyDirtyTextureIds.Count, m_PotentiallyDirtyTextures.Count);
                for (int index = 0; index < dirtyTextureCount; index++)
                {
                    EntityId textureId = m_PotentiallyDirtyTextureIds[index];
                    if (!m_TextureInfos.TryGetValue(textureId, out _))
                    {
                        continue;
                    }

                    Texture texture = m_PotentiallyDirtyTextures[index];
                    if (texture == null)
                    {
                        continue;
                    }

                    TryGetOrCreateIndex(texture, textureId, out _);
                }

                m_PotentiallyDirtyTextureIds.Clear();
                m_PotentiallyDirtyTextures.Clear();
            }

            if (m_PotentiallyDestroyedTextureIds.Count == 0)
            {
                return;
            }

            for (int index = 0; index < m_PotentiallyDestroyedTextureIds.Count; index++)
            {
                EntityId textureId = m_PotentiallyDestroyedTextureIds[index];
                RetireTrackedTexture(textureId);
            }

            m_PotentiallyDestroyedTextureIds.Clear();
        }

        private void RetireTrackedTexture(EntityId textureId)
        {
            if (!m_TextureInfos.TryGetValue(textureId, out BindlessTextureInfo info))
            {
                return;
            }

            RetireDescriptorIndex(info.Index);
            m_TextureInfos.Remove(textureId);
            IncrementTextureBindingRevision();
        }

        private void RetireDescriptorIndex(uint index)
        {
            ulong retireFenceValue = Math.Max(m_Allocator.PendingFrameFenceValue, m_Allocator.CompletedFrameFenceValue);
            if (retireFenceValue == 0ul)
            {
                retireFenceValue = 1ul;
            }

            m_RetiredDescriptorSlots.Add(new RetiredDescriptorSlot(index, retireFenceValue));
        }

        private void RecycleRetiredDescriptorSlots()
        {
            if (m_RetiredDescriptorSlots.Count == 0 || !m_Allocator.IsAvailable)
            {
                return;
            }

            ulong completedFrameFenceValue = m_Allocator.CompletedFrameFenceValue;
            for (int index = m_RetiredDescriptorSlots.Count - 1; index >= 0; index--)
            {
                RetiredDescriptorSlot retiredSlot = m_RetiredDescriptorSlots[index];
                if (completedFrameFenceValue < retiredSlot.RetireFenceValue)
                {
                    continue;
                }

                m_FreeDescriptorIndices.Push(retiredSlot.Index);
                m_RetiredDescriptorSlots.RemoveAt(index);
                if (m_AllocatedDescriptorCount > 0)
                {
                    m_AllocatedDescriptorCount--;
                }
            }
        }

        private uint RemainingDescriptorCount()
        {
            return m_Allocator.DescriptorCapacity - m_AllocatedDescriptorCount;
        }

        private uint RemainingLinearDescriptorCount()
        {
            return m_Allocator.DescriptorCapacity - m_LinearAllocatedDescriptorCount;
        }

        private static EntityId GetTrackedTextureId(Texture texture)
        {
            return texture != null ? texture.GetEntityId() : EntityId.None;
        }

        private void AddPotentialDirtyTexture(EntityId textureId, Texture texture)
        {
            if (texture == null)
            {
                return;
            }

            m_PotentiallyDirtyTextureIds.Add(textureId);
            m_PotentiallyDirtyTextures.Add(texture);
        }

        private void AddPotentialDestroyedDirtyTexture(EntityId textureId)
        {
            m_PotentiallyDestroyedTextureIds.Add(textureId);
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(nameof(BindlessTextureContainer));
            }
        }

        private void IncrementTextureBindingRevision()
        {
            m_TextureBindingRevision++;
            if (m_TextureBindingRevision == 0)
            {
                m_TextureBindingRevision = 1;
            }
        }

        private readonly struct BindlessTextureInfo
        {
            public BindlessTextureInfo(uint index, IntPtr nativeTexturePtr)
            {
                Index = index;
                NativeTexturePtr = nativeTexturePtr;
            }

            public uint Index { get; }

            public IntPtr NativeTexturePtr { get; }
        }

        private readonly struct RetiredDescriptorSlot
        {
            public RetiredDescriptorSlot(uint index, ulong retireFenceValue)
            {
                Index = index;
                RetireFenceValue = retireFenceValue;
            }

            public uint Index { get; }

            public ulong RetireFenceValue { get; }
        }
    }
}
