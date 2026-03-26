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
        private readonly Dictionary<int, BindlessTextureInfo> m_TextureInfos = new();
        private readonly List<Texture> m_PotentiallyDirtyTextures = new(InitialDirtyTextureCapacity);
        private readonly List<int> m_PotentiallyDirtyTextureInstanceIds = new(InitialDirtyTextureCapacity);
        private readonly List<int> m_PotentiallyDestroyedTextureInstanceIds = new(InitialDirtyTextureCapacity);

        private uint m_AllocatedDescriptorCount;
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

        public string UnavailableReason => m_Allocator.UnavailableReason;

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            m_TextureInfos.Clear();
            m_PotentiallyDirtyTextures.Clear();
            m_PotentiallyDirtyTextureInstanceIds.Clear();
            m_PotentiallyDestroyedTextureInstanceIds.Clear();
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

            startIndex = m_Allocator.DescriptorStartIndex + m_Allocator.DescriptorCapacity - (m_AllocatedDescriptorCount + count);
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

        public void MarkTextureDestroyed(int instanceId)
        {
            ThrowIfDisposed();
            AddPotentialDestroyedDirtyTexture(instanceId);
        }

        public void PreRender()
        {
            ThrowIfDisposed();
            UpdateDirtyTextures();
        }

        internal void AddPotentialDirtyTextureRange(NativeArray<EntityId> textureInstanceIds, List<Object> textures)
        {
            ThrowIfDisposed();

            if (textureInstanceIds.Length == 0 || textures == null || textures.Count == 0)
            {
                return;
            }

            int count = Math.Min(textureInstanceIds.Length, textures.Count);
            for (int index = 0; index < count; index++)
            {
                if (textures[index] is Texture texture)
                {
                    AddPotentialDirtyTexture(ToTrackedTextureId(textureInstanceIds[index]), texture);
                }
            }
        }

        internal void AddPotentialDestroyedDirtyTextureRange(NativeArray<EntityId> textureInstanceIds)
        {
            ThrowIfDisposed();

            for (int index = 0; index < textureInstanceIds.Length; index++)
            {
                AddPotentialDestroyedDirtyTexture(ToTrackedTextureId(textureInstanceIds[index]));
            }
        }

        private bool TryGetOrCreateIndex(Texture texture, int instanceId, out uint index)
        {
            if (!m_Allocator.IsAvailable)
            {
                index = InvalidTextureIndex;
                return false;
            }

            Texture effectiveTexture = GetEffectiveTexture(texture);
            if (effectiveTexture == null)
            {
                index = InvalidTextureIndex;
                return false;
            }

            IntPtr nativeTexturePtr = effectiveTexture.GetNativeTexturePtr();
            bool hasExistingInfo = m_TextureInfos.TryGetValue(instanceId, out BindlessTextureInfo info);
            if (hasExistingInfo)
            {
                if (info.NativeTexturePtr == nativeTexturePtr)
                {
                    index = info.Index;
                    return true;
                }

                index = info.Index;
            }
            else
            {
                if (!TryGetNextDescriptorIndex(out index))
                {
                    return false;
                }
            }

            if (!m_Allocator.TryCreateTextureDescriptor(effectiveTexture, index))
            {
                index = InvalidTextureIndex;
                return false;
            }

            if (!hasExistingInfo)
            {
                m_AllocatedDescriptorCount++;
            }

            m_TextureInfos[instanceId] = new BindlessTextureInfo(index, nativeTexturePtr);
            return true;
        }

        private bool TryGetNextDescriptorIndex(out uint index)
        {
            if (!m_Allocator.IsAvailable || m_AllocatedDescriptorCount >= m_Allocator.DescriptorCapacity)
            {
                index = InvalidTextureIndex;
                return false;
            }

            index = m_Allocator.DescriptorStartIndex + m_Allocator.DescriptorCapacity - 1 - m_AllocatedDescriptorCount;
            return true;
        }

        private void UpdateDirtyTextures()
        {
            if (m_PotentiallyDirtyTextureInstanceIds.Count > 0)
            {
                int dirtyTextureCount = Math.Min(m_PotentiallyDirtyTextureInstanceIds.Count, m_PotentiallyDirtyTextures.Count);
                for (int index = 0; index < dirtyTextureCount; index++)
                {
                    int instanceId = m_PotentiallyDirtyTextureInstanceIds[index];
                    if (!m_TextureInfos.TryGetValue(instanceId, out _))
                    {
                        continue;
                    }

                    Texture texture = m_PotentiallyDirtyTextures[index];
                    if (texture == null)
                    {
                        continue;
                    }

                    TryGetOrCreateIndex(texture, instanceId, out _);
                }

                m_PotentiallyDirtyTextureInstanceIds.Clear();
                m_PotentiallyDirtyTextures.Clear();
            }

            if (m_PotentiallyDestroyedTextureInstanceIds.Count == 0)
            {
                return;
            }

            for (int index = 0; index < m_PotentiallyDestroyedTextureInstanceIds.Count; index++)
            {
                int instanceId = m_PotentiallyDestroyedTextureInstanceIds[index];
                if (!m_TextureInfos.TryGetValue(instanceId, out BindlessTextureInfo info))
                {
                    continue;
                }

                TryGetOrCreateIndex(null, instanceId, out _);
            }

            m_PotentiallyDestroyedTextureInstanceIds.Clear();
        }

        private uint RemainingDescriptorCount()
        {
            return m_Allocator.DescriptorCapacity - m_AllocatedDescriptorCount;
        }

        private static Texture GetEffectiveTexture(Texture texture)
        {
            return texture != null ? texture : Texture2D.whiteTexture;
        }

        private static int GetTrackedTextureId(Texture texture)
        {
            return texture != null
                ? ToTrackedTextureId(texture.GetEntityId())
                : 0;
        }

        private static int ToTrackedTextureId(EntityId entityId)
        {
            return unchecked((int) EntityId.ToULong(entityId));
        }

        private void AddPotentialDirtyTexture(int instanceId, Texture texture)
        {
            if (texture == null)
            {
                return;
            }

            m_PotentiallyDirtyTextureInstanceIds.Add(instanceId);
            m_PotentiallyDirtyTextures.Add(texture);
        }

        private void AddPotentialDestroyedDirtyTexture(int instanceId)
        {
            m_PotentiallyDestroyedTextureInstanceIds.Add(instanceId);
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(nameof(BindlessTextureContainer));
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
    }
}
