using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal readonly struct VividTextureId : IEquatable<VividTextureId>
    {
        private readonly EntityId m_EntityId;
        private readonly int m_TextureSize;

        internal VividTextureId(EntityId entityId, int textureSize)
        {
            m_EntityId = entityId;
            m_TextureSize = textureSize;
        }

        public bool Equals(VividTextureId other)
        {
            return m_EntityId.Equals(other.m_EntityId) && m_TextureSize == other.m_TextureSize;
        }

        public override bool Equals(object obj)
        {
            return obj is VividTextureId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(m_EntityId, m_TextureSize);
        }
    }

    internal sealed class VividAtlasAllocatorDynamic
    {
        private sealed class AtlasNodePool
        {
            internal AtlasNode[] Nodes;
            private short m_Next;
            private short m_FreelistHead;

            internal AtlasNodePool(short capacity)
            {
                Nodes = new AtlasNode[capacity];
                m_Next = 0;
                m_FreelistHead = -1;
            }

            internal void Clear()
            {
                m_Next = 0;
                m_FreelistHead = -1;
            }

            internal short CreateNode(short parent)
            {
                Debug.Assert(m_Next < Nodes.Length || m_FreelistHead != -1);

                if (m_FreelistHead != -1)
                {
                    var freelistHeadNext = Nodes[m_FreelistHead].FreelistNext;
                    Nodes[m_FreelistHead] = new AtlasNode(m_FreelistHead, parent);
                    var result = m_FreelistHead;
                    m_FreelistHead = freelistHeadNext;
                    return result;
                }

                Nodes[m_Next] = new AtlasNode(m_Next, parent);
                return m_Next++;
            }

            internal void FreeNode(short index)
            {
                Debug.Assert(index >= 0 && index < Nodes.Length);
                Nodes[index].FreelistNext = m_FreelistHead;
                m_FreelistHead = index;
            }
        }

        private struct AtlasNode
        {
            private const ushort IsOccupiedFlag = 1 << 0;

            internal short Self;
            internal short Parent;
            internal short LeftChild;
            internal short RightChild;
            internal short FreelistNext;
            internal ushort Flags;
            internal Vector4 Rect;

            internal AtlasNode(short self, short parent)
            {
                Self = self;
                Parent = parent;
                LeftChild = -1;
                RightChild = -1;
                FreelistNext = -1;
                Flags = 0;
                Rect = Vector4.zero;
            }

            internal bool IsOccupied => (Flags & IsOccupiedFlag) != 0;

            private bool IsLeaf => LeftChild == -1;

            internal short Allocate(AtlasNodePool pool, int width, int height)
            {
                if (Mathf.Min(width, height) < 1)
                {
                    Debug.Assert(false);
                    return -1;
                }

                if (!IsLeaf)
                {
                    var node = pool.Nodes[LeftChild].Allocate(pool, width, height);
                    return node != -1 ? node : pool.Nodes[RightChild].Allocate(pool, width, height);
                }

                if (IsOccupied || width > Rect.x || height > Rect.y)
                    return -1;

                LeftChild = pool.CreateNode(Self);
                RightChild = pool.CreateNode(Self);

                var deltaX = Rect.x - width;
                var deltaY = Rect.y - height;

                if (deltaX >= deltaY)
                {
                    pool.Nodes[LeftChild].Rect = new Vector4(width, Rect.y, Rect.z, Rect.w);
                    pool.Nodes[RightChild].Rect = new Vector4(deltaX, Rect.y, Rect.z + width, Rect.w);

                    if (deltaY < 1)
                    {
                        pool.Nodes[LeftChild].SetOccupied();
                        return LeftChild;
                    }
                }
                else
                {
                    pool.Nodes[LeftChild].Rect = new Vector4(Rect.x, height, Rect.z, Rect.w);
                    pool.Nodes[RightChild].Rect = new Vector4(Rect.x, deltaY, Rect.z, Rect.w + height);

                    if (deltaX < 1)
                    {
                        pool.Nodes[LeftChild].SetOccupied();
                        return LeftChild;
                    }
                }

                var allocated = pool.Nodes[LeftChild].Allocate(pool, width, height);
                if (allocated >= 0)
                    pool.Nodes[allocated].SetOccupied();
                return allocated;
            }

            internal void ReleaseChildren(AtlasNodePool pool)
            {
                if (IsLeaf)
                    return;

                pool.Nodes[LeftChild].ReleaseChildren(pool);
                pool.Nodes[RightChild].ReleaseChildren(pool);
                pool.FreeNode(LeftChild);
                pool.FreeNode(RightChild);
                LeftChild = -1;
                RightChild = -1;
            }

            internal void ReleaseAndMerge(AtlasNodePool pool)
            {
                var node = Self;
                do
                {
                    pool.Nodes[node].ReleaseChildren(pool);
                    pool.Nodes[node].ClearOccupied();
                    node = pool.Nodes[node].Parent;
                }
                while (node >= 0 && pool.Nodes[node].ShouldMerge(pool));
            }

            private bool ShouldMerge(AtlasNodePool pool)
            {
                return !IsLeaf
                    && pool.Nodes[LeftChild].IsLeaf
                    && !pool.Nodes[LeftChild].IsOccupied
                    && pool.Nodes[RightChild].IsLeaf
                    && !pool.Nodes[RightChild].IsOccupied;
            }

            private void SetOccupied()
            {
                Flags |= IsOccupiedFlag;
            }

            private void ClearOccupied()
            {
                Flags &= unchecked((ushort)~IsOccupiedFlag);
            }
        }

        private readonly int m_Width;
        private readonly int m_Height;
        private readonly AtlasNodePool m_Pool;
        private readonly Dictionary<VividTextureId, short> m_NodeFromId;
        private short m_Root;

        internal VividAtlasAllocatorDynamic(int width, int height, int capacityAllocations)
        {
            m_Width = width;
            m_Height = height;
            var capacityNodes = Mathf.Max(2, capacityAllocations * 2);
            Debug.Assert(capacityNodes < 1 << 15);
            m_Pool = new AtlasNodePool((short)capacityNodes);
            m_NodeFromId = new Dictionary<VividTextureId, short>(capacityAllocations);
            Reset();
        }

        internal bool Allocate(out Vector4 result, VividTextureId key, int width, int height)
        {
            var node = m_Pool.Nodes[m_Root].Allocate(m_Pool, width, height);
            if (node < 0)
            {
                result = Vector4.zero;
                return false;
            }

            result = m_Pool.Nodes[node].Rect;
            m_NodeFromId.Add(key, node);
            return true;
        }

        internal void Release(VividTextureId key)
        {
            if (!m_NodeFromId.TryGetValue(key, out var node))
                return;

            m_Pool.Nodes[node].ReleaseAndMerge(m_Pool);
            m_NodeFromId.Remove(key);
        }

        internal void Reset()
        {
            m_Pool.Clear();
            m_Root = m_Pool.CreateNode(-1);
            m_Pool.Nodes[m_Root].Rect = new Vector4(m_Width, m_Height, 0.0f, 0.0f);
            m_NodeFromId.Clear();
        }
    }

    internal sealed class VividTexture2DAtlasDynamic : IDisposable
    {
        private readonly int m_Width;
        private readonly int m_Height;
        private readonly VividAtlasAllocatorDynamic m_AtlasAllocator;
        private readonly Dictionary<VividTextureId, Vector4> m_AllocationCache;
        private RTHandle m_AtlasTexture;

        internal RTHandle AtlasTexture => m_AtlasTexture;

        internal VividTexture2DAtlasDynamic(int width, int height, int capacity, RTHandle atlasTexture)
        {
            m_Width = width;
            m_Height = height;
            m_AtlasTexture = atlasTexture;
            m_AtlasAllocator = new VividAtlasAllocatorDynamic(width, height, capacity);
            m_AllocationCache = new Dictionary<VividTextureId, Vector4>(capacity);
        }

        public void Dispose()
        {
            ResetAllocator();
            m_AtlasTexture = null;
        }

        internal void ResetAllocator()
        {
            m_AtlasAllocator.Reset();
            m_AllocationCache.Clear();
        }

        internal bool IsCached(out Vector4 scaleOffset, VividTextureId key)
        {
            return m_AllocationCache.TryGetValue(key, out scaleOffset);
        }

        internal bool EnsureTextureSlot(
            out bool isUploadNeeded,
            out Vector4 scaleOffset,
            VividTextureId key,
            int width,
            int height)
        {
            isUploadNeeded = false;
            if (m_AllocationCache.TryGetValue(key, out scaleOffset))
                return true;

            if (!m_AtlasAllocator.Allocate(out scaleOffset, key, width, height))
                return false;

            isUploadNeeded = true;
            scaleOffset.Scale(new Vector4(1.0f / m_Width, 1.0f / m_Height, 1.0f / m_Width, 1.0f / m_Height));
            m_AllocationCache.Add(key, scaleOffset);
            return true;
        }

        internal void ReleaseTextureSlot(VividTextureId key)
        {
            m_AtlasAllocator.Release(key);
            m_AllocationCache.Remove(key);
        }
    }
}
