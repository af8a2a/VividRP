using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven.Bindless
{
    public sealed class BindlessTextureContainer : IDisposable
    {
        public const uint InvalidTextureIndex = uint.MaxValue;

        private readonly IBindlessTextureDescriptorAllocator m_Allocator;
        private readonly Dictionary<int, BindlessTextureInfo> m_TextureInfos = new();
        private readonly Dictionary<int, Texture> m_DirtyTextures = new();
        private readonly HashSet<int> m_DestroyedTextureIds = new();

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
            m_DirtyTextures.Clear();
            m_DestroyedTextureIds.Clear();
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

            return TryGetOrCreateIndex(texture, texture.GetInstanceID(), out index);
        }

        public bool TryGetExistingIndex(Texture texture, out uint index)
        {
            ThrowIfDisposed();

            if (texture != null && m_TextureInfos.TryGetValue(texture.GetInstanceID(), out BindlessTextureInfo info))
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

            m_DirtyTextures[texture.GetInstanceID()] = texture;
        }

        public void MarkTextureDestroyed(int instanceId)
        {
            ThrowIfDisposed();
            m_DestroyedTextureIds.Add(instanceId);
        }

        public void PreRender()
        {
            ThrowIfDisposed();
            UpdateDirtyTextures();
            UpdateDestroyedTextures();
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
            if (m_DirtyTextures.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<int, Texture> pair in m_DirtyTextures)
            {
                if (!m_TextureInfos.TryGetValue(pair.Key, out BindlessTextureInfo info))
                {
                    continue;
                }

                Texture texture = pair.Value;
                if (texture == null)
                {
                    continue;
                }

                IntPtr nativeTexturePtr = texture.GetNativeTexturePtr();
                if (nativeTexturePtr == info.NativeTexturePtr)
                {
                    continue;
                }

                if (!m_Allocator.TryCreateTextureDescriptor(texture, info.Index))
                {
                    continue;
                }

                m_TextureInfos[pair.Key] = new BindlessTextureInfo(info.Index, nativeTexturePtr);
            }

            m_DirtyTextures.Clear();
        }

        private void UpdateDestroyedTextures()
        {
            if (m_DestroyedTextureIds.Count == 0)
            {
                return;
            }

            Texture fallbackTexture = GetEffectiveTexture(null);
            if (fallbackTexture == null)
            {
                m_DestroyedTextureIds.Clear();
                return;
            }

            IntPtr fallbackNativeTexturePtr = fallbackTexture.GetNativeTexturePtr();
            foreach (int instanceId in m_DestroyedTextureIds)
            {
                if (!m_TextureInfos.TryGetValue(instanceId, out BindlessTextureInfo info))
                {
                    continue;
                }

                if (!m_Allocator.TryCreateTextureDescriptor(fallbackTexture, info.Index))
                {
                    continue;
                }

                m_TextureInfos[instanceId] = new BindlessTextureInfo(info.Index, fallbackNativeTexturePtr);
            }

            m_DestroyedTextureIds.Clear();
        }

        private uint RemainingDescriptorCount()
        {
            return m_Allocator.DescriptorCapacity - m_AllocatedDescriptorCount;
        }

        private static Texture GetEffectiveTexture(Texture texture)
        {
            return texture != null ? texture : Texture2D.whiteTexture;
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
