using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven.VirtualTexture
{
    internal sealed class GPUDrivenVirtualTextureAtlasAllocator
    {
        private const int InitialNodeCapacity = 512;
        private const int InitialNodeSetCapacity = 128;
        private const int InitialAllocationCapacity = 16;

        internal readonly struct Allocation
        {
            private readonly GPUDrivenVirtualTextureAtlasAllocator m_Owner;

            internal Allocation(
                GPUDrivenVirtualTextureAtlasAllocator owner,
                int id,
                RectInt pageRegion,
                int maxMip)
            {
                m_Owner = owner;
                Id = id;
                PageRegion = pageRegion;
                MaxMip = maxMip;
            }

            internal int Id { get; }

            internal RectInt PageRegion { get; }

            internal int MaxMip { get; }

            internal bool BelongsTo(GPUDrivenVirtualTextureAtlasAllocator allocator)
            {
                return m_Owner == allocator;
            }
        }

        private enum NodeState : byte
        {
            None,
            Free,
            PartiallyFree,
            Allocated,
        }

        private struct Node
        {
            internal const int ChildCount = 4;

            internal int Index;
            internal int ParentIndex;
            internal int X;
            internal int Y;
            internal int Order;
            internal NodeState State;
            internal int OwnerAllocationId;
            private int m_ChildIndex0;
            private int m_ChildIndex1;
            private int m_ChildIndex2;
            private int m_ChildIndex3;

            internal bool HasChildren => m_ChildIndex0 >= 0;

            internal int GetChildIndex(int childOffset)
            {
                return childOffset switch
                {
                    0 => m_ChildIndex0,
                    1 => m_ChildIndex1,
                    2 => m_ChildIndex2,
                    3 => m_ChildIndex3,
                    _ => throw new ArgumentOutOfRangeException(nameof(childOffset)),
                };
            }

            internal void SetChildIndex(int childOffset, int childIndex)
            {
                switch (childOffset)
                {
                    case 0:
                        m_ChildIndex0 = childIndex;
                        break;
                    case 1:
                        m_ChildIndex1 = childIndex;
                        break;
                    case 2:
                        m_ChildIndex2 = childIndex;
                        break;
                    case 3:
                        m_ChildIndex3 = childIndex;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(childOffset));
                }
            }

            internal void Initialize(int index, int parentIndex, int x, int y, int order)
            {
                Index = index;
                ParentIndex = parentIndex;
                X = x;
                Y = y;
                Order = order;
                State = NodeState.None;
                OwnerAllocationId = 0;
                ClearChildren();
            }

            internal void ClearChildren()
            {
                m_ChildIndex0 = -1;
                m_ChildIndex1 = -1;
                m_ChildIndex2 = -1;
                m_ChildIndex3 = -1;
            }
        }

        private readonly struct NodeKey : IComparable<NodeKey>
        {
            internal NodeKey(uint mortonAddress, int nodeIndex)
            {
                MortonAddress = mortonAddress;
                NodeIndex = nodeIndex;
            }

            internal uint MortonAddress { get; }

            internal int NodeIndex { get; }

            public int CompareTo(NodeKey other)
            {
                int addressComparison = MortonAddress.CompareTo(other.MortonAddress);
                return addressComparison != 0
                    ? addressComparison
                    : NodeIndex.CompareTo(other.NodeIndex);
            }
        }

        private readonly struct Candidate
        {
            internal Candidate(int nodeIndex, int x, int y, uint mortonAddress)
            {
                NodeIndex = nodeIndex;
                X = x;
                Y = y;
                MortonAddress = mortonAddress;
            }

            internal int NodeIndex { get; }

            internal int X { get; }

            internal int Y { get; }

            internal uint MortonAddress { get; }
        }

        private readonly int m_MaxAllocationPageCount;
        private readonly int m_RootOrder;
        private readonly List<Node> m_Nodes = new(InitialNodeCapacity);
        private readonly Stack<int> m_RecycledNodeIndices = new();
        private readonly List<NodeKey>[] m_FreeNodesByOrder;
        private readonly List<NodeKey>[] m_PartiallyFreeNodesByOrder;
        private readonly Dictionary<int, Allocation> m_Allocations = new(InitialAllocationCapacity);
        private readonly HashSet<int> m_CoalesceCandidates = new();
        private readonly List<int> m_CoalesceCandidateList = new();
        private int m_NextAllocationId = 1;

        internal GPUDrivenVirtualTextureAtlasAllocator(
            int atlasPageCount,
            int maxAllocationPageCount)
        {
            if (!Mathf.IsPowerOfTwo(atlasPageCount))
                throw new ArgumentOutOfRangeException(nameof(atlasPageCount));
            if (!Mathf.IsPowerOfTwo(maxAllocationPageCount)
                || maxAllocationPageCount > atlasPageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAllocationPageCount));
            }

            m_MaxAllocationPageCount = maxAllocationPageCount;
            m_RootOrder = IntegerLog2(atlasPageCount);
            m_FreeNodesByOrder = CreateNodeSets(m_RootOrder + 1);
            m_PartiallyFreeNodesByOrder = CreateNodeSets(m_RootOrder + 1);

            int rootIndex = AcquireNode(-1, 0, 0, m_RootOrder);
            SetNodeFree(rootIndex);
        }

        internal int AllocatedPageCount { get; private set; }

        internal int AllocationCount => m_Allocations.Count;

        internal bool TryAllocate(int width, int height, out Allocation allocation)
        {
            allocation = default;
            if (!TryValidateDimensions(width, height, out int maxMip))
                return false;
            if (!TryFindCandidate(width, height, maxMip, out Candidate candidate))
                return false;

            int allocationId = m_NextAllocationId++;
            if (m_NextAllocationId <= 0)
                m_NextAllocationId = 1;

            allocation = new Allocation(
                this,
                allocationId,
                new RectInt(candidate.X, candidate.Y, width, height),
                maxMip);
            int allocatedNodeCount = MarkAllocated(
                candidate.NodeIndex,
                allocation.PageRegion,
                allocation.Id);
            if (allocatedNodeCount == 0)
                throw new InvalidOperationException("The VT atlas allocator produced an empty allocation.");

            m_Allocations.Add(allocation.Id, allocation);
            AllocatedPageCount += checked(width * height);
            return true;
        }

        internal bool CanAllocate(int width, int height)
        {
            return TryValidateDimensions(width, height, out int maxMip)
                   && TryFindCandidate(width, height, maxMip, out _);
        }

        internal int GetLargestFreeSquarePageCount()
        {
            for (int size = m_MaxAllocationPageCount; size >= 1; size >>= 1)
            {
                if (CanAllocate(size, size))
                    return size;
            }

            return 0;
        }

        internal bool Release(Allocation allocation)
        {
            if (!allocation.BelongsTo(this)
                || !m_Allocations.ContainsKey(allocation.Id))
            {
                return false;
            }

            m_CoalesceCandidates.Clear();
            int releasedNodeCount = ReleaseAllocatedNodes(0, allocation.PageRegion, allocation.Id);
            if (releasedNodeCount == 0)
                throw new InvalidOperationException("The VT atlas allocation owns no buddy blocks.");

            m_CoalesceCandidateList.Clear();
            m_CoalesceCandidateList.AddRange(m_CoalesceCandidates);
            m_CoalesceCandidateList.Sort(CompareNodeOrder);
            for (int candidateIndex = 0; candidateIndex < m_CoalesceCandidateList.Count; candidateIndex++)
                TryCoalesce(m_CoalesceCandidateList[candidateIndex]);

            m_Allocations.Remove(allocation.Id);
            AllocatedPageCount -= allocation.PageRegion.width * allocation.PageRegion.height;
            return true;
        }

        private static List<NodeKey>[] CreateNodeSets(int count)
        {
            var sets = new List<NodeKey>[count];
            for (int setIndex = 0; setIndex < sets.Length; setIndex++)
                sets[setIndex] = new List<NodeKey>(InitialNodeSetCapacity);
            return sets;
        }

        private bool TryValidateDimensions(int width, int height, out int maxMip)
        {
            maxMip = 0;
            if (width <= 0
                || height <= 0
                || width > m_MaxAllocationPageCount
                || height > m_MaxAllocationPageCount
                || !Mathf.IsPowerOfTwo(width)
                || !Mathf.IsPowerOfTwo(height))
            {
                return false;
            }

            maxMip = IntegerLog2(Mathf.Min(width, height));
            return true;
        }

        private bool TryFindCandidate(int width, int height, int maxMip, out Candidate candidate)
        {
            int maxOrder = IntegerLog2(Mathf.Max(width, height));
            bool found = false;
            candidate = default;

            for (int order = maxOrder; order <= m_RootOrder; order++)
            {
                if (m_FreeNodesByOrder[order].Count == 0)
                    continue;

                NodeKey key = m_FreeNodesByOrder[order][0];
                Node node = m_Nodes[key.NodeIndex];
                if (!found || key.MortonAddress < candidate.MortonAddress)
                {
                    candidate = new Candidate(node.Index, node.X, node.Y, key.MortonAddress);
                    found = true;
                }
            }

            int alignment = 1 << maxMip;
            foreach (NodeKey key in m_PartiallyFreeNodesByOrder[maxOrder])
            {
                if (found && key.MortonAddress >= candidate.MortonAddress)
                    break;

                Node node = m_Nodes[key.NodeIndex];
                int blockSize = 1 << node.Order;
                int maxX = node.X + blockSize - width;
                int maxY = node.Y + blockSize - height;
                for (int y = node.Y; y <= maxY; y += alignment)
                {
                    for (int x = node.X; x <= maxX; x += alignment)
                    {
                        uint mortonAddress = EncodeMorton(x, y);
                        if (found && mortonAddress >= candidate.MortonAddress)
                            continue;

                        var pageRegion = new RectInt(x, y, width, height);
                        if (!TestAllocation(node.Index, pageRegion))
                            continue;

                        candidate = new Candidate(node.Index, x, y, mortonAddress);
                        found = true;
                    }
                }
            }

            return found;
        }

        private bool TestAllocation(int nodeIndex, RectInt pageRegion)
        {
            Node node = m_Nodes[nodeIndex];
            if (!Overlaps(node, pageRegion))
                return true;
            if (node.State == NodeState.Free)
                return true;
            if (node.State == NodeState.Allocated)
                return false;
            if (node.State != NodeState.PartiallyFree || !node.HasChildren)
                throw new InvalidOperationException("The VT atlas quadtree contains an invalid node state.");
            if (Contains(pageRegion, node))
                return false;

            for (int childOffset = 0; childOffset < Node.ChildCount; childOffset++)
            {
                if (!TestAllocation(node.GetChildIndex(childOffset), pageRegion))
                    return false;
            }

            return true;
        }

        private int MarkAllocated(int nodeIndex, RectInt pageRegion, int allocationId)
        {
            Node node = m_Nodes[nodeIndex];
            if (!Overlaps(node, pageRegion))
                return 0;

            if (Contains(pageRegion, node))
            {
                if (node.State != NodeState.Free || node.HasChildren)
                    throw new InvalidOperationException("The VT atlas allocator attempted to claim a non-free buddy block.");

                RemoveFreeNode(nodeIndex);
                node.State = NodeState.Allocated;
                node.OwnerAllocationId = allocationId;
                m_Nodes[nodeIndex] = node;
                return 1;
            }

            if (node.Order <= 0)
                throw new InvalidOperationException("The VT atlas allocator could not subdivide an intersecting leaf block.");
            if (node.State == NodeState.Free)
            {
                Subdivide(nodeIndex);
                node = m_Nodes[nodeIndex];
            }
            else if (node.State != NodeState.PartiallyFree)
                throw new InvalidOperationException("The VT atlas allocator encountered an occupied intersecting block.");

            int allocatedNodeCount = 0;
            for (int childOffset = 0; childOffset < Node.ChildCount; childOffset++)
            {
                allocatedNodeCount += MarkAllocated(
                    node.GetChildIndex(childOffset),
                    pageRegion,
                    allocationId);
            }

            return allocatedNodeCount;
        }

        private int ReleaseAllocatedNodes(int nodeIndex, RectInt pageRegion, int allocationId)
        {
            Node node = m_Nodes[nodeIndex];
            if (!Overlaps(node, pageRegion))
                return 0;

            if (node.State == NodeState.Allocated)
            {
                if (node.OwnerAllocationId != allocationId)
                    throw new InvalidOperationException("The VT atlas allocation owns an invalid buddy block.");

                node.OwnerAllocationId = 0;
                node.State = NodeState.None;
                m_Nodes[nodeIndex] = node;
                SetNodeFree(nodeIndex);
                AddCoalesceAncestors(node.ParentIndex);
                return 1;
            }

            if (node.State != NodeState.PartiallyFree || !node.HasChildren)
                throw new InvalidOperationException("The VT atlas allocation contains an invalid buddy block.");

            int releasedNodeCount = 0;
            for (int childOffset = 0; childOffset < Node.ChildCount; childOffset++)
            {
                releasedNodeCount += ReleaseAllocatedNodes(
                    node.GetChildIndex(childOffset),
                    pageRegion,
                    allocationId);
            }

            return releasedNodeCount;
        }

        private void Subdivide(int nodeIndex)
        {
            Node node = m_Nodes[nodeIndex];
            if (node.State != NodeState.Free || node.HasChildren || node.Order <= 0)
                throw new InvalidOperationException("Only non-leaf free VT atlas blocks can be subdivided.");

            RemoveFreeNode(nodeIndex);
            node.State = NodeState.PartiallyFree;
            m_Nodes[nodeIndex] = node;
            AddPartiallyFreeNode(nodeIndex);

            int childOrder = node.Order - 1;
            int childSize = 1 << childOrder;
            for (int childOffset = 0; childOffset < Node.ChildCount; childOffset++)
            {
                int childX = node.X + ((childOffset & 1) != 0 ? childSize : 0);
                int childY = node.Y + ((childOffset & 2) != 0 ? childSize : 0);
                int childIndex = AcquireNode(nodeIndex, childX, childY, childOrder);
                node.SetChildIndex(childOffset, childIndex);
                SetNodeFree(childIndex);
            }
            m_Nodes[nodeIndex] = node;
        }

        private void AddCoalesceAncestors(int nodeIndex)
        {
            while (nodeIndex >= 0)
            {
                if (!m_CoalesceCandidates.Add(nodeIndex))
                    break;
                nodeIndex = m_Nodes[nodeIndex].ParentIndex;
            }
        }

        private int CompareNodeOrder(int leftIndex, int rightIndex)
        {
            int orderComparison = m_Nodes[leftIndex].Order.CompareTo(m_Nodes[rightIndex].Order);
            if (orderComparison != 0)
                return orderComparison;

            return EncodeMorton(m_Nodes[leftIndex].X, m_Nodes[leftIndex].Y)
                .CompareTo(EncodeMorton(m_Nodes[rightIndex].X, m_Nodes[rightIndex].Y));
        }

        private void TryCoalesce(int nodeIndex)
        {
            Node node = m_Nodes[nodeIndex];
            if (node.State != NodeState.PartiallyFree || !node.HasChildren)
                return;

            for (int childOffset = 0; childOffset < Node.ChildCount; childOffset++)
            {
                Node child = m_Nodes[node.GetChildIndex(childOffset)];
                if (child.State != NodeState.Free || child.HasChildren)
                    return;
            }

            RemovePartiallyFreeNode(nodeIndex);
            for (int childOffset = 0; childOffset < Node.ChildCount; childOffset++)
            {
                int childIndex = node.GetChildIndex(childOffset);
                RemoveFreeNode(childIndex);
                RecycleNode(childIndex);
            }

            node.ClearChildren();
            node.State = NodeState.None;
            m_Nodes[nodeIndex] = node;
            SetNodeFree(nodeIndex);
        }

        private int AcquireNode(int parentIndex, int x, int y, int order)
        {
            int nodeIndex;
            Node node;
            if (m_RecycledNodeIndices.Count > 0)
            {
                nodeIndex = m_RecycledNodeIndices.Pop();
                node = m_Nodes[nodeIndex];
            }
            else
            {
                nodeIndex = m_Nodes.Count;
                node = default;
                m_Nodes.Add(node);
            }

            node.Initialize(nodeIndex, parentIndex, x, y, order);
            m_Nodes[nodeIndex] = node;
            return nodeIndex;
        }

        private void RecycleNode(int nodeIndex)
        {
            Node node = m_Nodes[nodeIndex];
            node.Initialize(nodeIndex, -1, 0, 0, 0);
            m_Nodes[nodeIndex] = node;
            m_RecycledNodeIndices.Push(nodeIndex);
        }

        private void SetNodeFree(int nodeIndex)
        {
            Node node = m_Nodes[nodeIndex];
            if (node.State != NodeState.None || node.HasChildren || node.OwnerAllocationId != 0)
                throw new InvalidOperationException("The VT atlas allocator cannot add a non-empty node to a free list.");

            node.State = NodeState.Free;
            m_Nodes[nodeIndex] = node;
            AddNodeKey(m_FreeNodesByOrder[node.Order], CreateNodeKey(node));
        }

        private void RemoveFreeNode(int nodeIndex)
        {
            Node node = m_Nodes[nodeIndex];
            if (node.State != NodeState.Free
                || !RemoveNodeKey(m_FreeNodesByOrder[node.Order], CreateNodeKey(node)))
            {
                throw new InvalidOperationException("The VT atlas allocator free list is inconsistent.");
            }

            node.State = NodeState.None;
            m_Nodes[nodeIndex] = node;
        }

        private void AddPartiallyFreeNode(int nodeIndex)
        {
            Node node = m_Nodes[nodeIndex];
            if (node.State != NodeState.PartiallyFree)
                throw new InvalidOperationException("Only partially free VT atlas nodes can be indexed.");
            AddNodeKey(m_PartiallyFreeNodesByOrder[node.Order], CreateNodeKey(node));
        }

        private void RemovePartiallyFreeNode(int nodeIndex)
        {
            Node node = m_Nodes[nodeIndex];
            if (node.State != NodeState.PartiallyFree
                || !RemoveNodeKey(m_PartiallyFreeNodesByOrder[node.Order], CreateNodeKey(node)))
            {
                throw new InvalidOperationException("The VT atlas allocator partial list is inconsistent.");
            }

            node.State = NodeState.None;
            m_Nodes[nodeIndex] = node;
        }

        private static NodeKey CreateNodeKey(Node node)
        {
            return new NodeKey(EncodeMorton(node.X, node.Y), node.Index);
        }

        private static void AddNodeKey(List<NodeKey> nodeKeys, NodeKey nodeKey)
        {
            int keyIndex = nodeKeys.BinarySearch(nodeKey);
            if (keyIndex >= 0)
                throw new InvalidOperationException("The VT atlas allocator node index contains a duplicate key.");

            nodeKeys.Insert(~keyIndex, nodeKey);
        }

        private static bool RemoveNodeKey(List<NodeKey> nodeKeys, NodeKey nodeKey)
        {
            int keyIndex = nodeKeys.BinarySearch(nodeKey);
            if (keyIndex < 0)
                return false;

            nodeKeys.RemoveAt(keyIndex);
            return true;
        }

        private static bool Overlaps(Node node, RectInt pageRegion)
        {
            int nodeSize = 1 << node.Order;
            return pageRegion.xMax > node.X
                   && pageRegion.xMin < node.X + nodeSize
                   && pageRegion.yMax > node.Y
                   && pageRegion.yMin < node.Y + nodeSize;
        }

        private static bool Contains(RectInt pageRegion, Node node)
        {
            int nodeSize = 1 << node.Order;
            return node.X >= pageRegion.xMin
                   && node.X + nodeSize <= pageRegion.xMax
                   && node.Y >= pageRegion.yMin
                   && node.Y + nodeSize <= pageRegion.yMax;
        }

        private static int IntegerLog2(int value)
        {
            int result = 0;
            while ((value >>= 1) > 0)
                result += 1;
            return result;
        }

        private static uint EncodeMorton(int x, int y)
        {
            uint result = 0;
            uint valueX = (uint) x;
            uint valueY = (uint) y;
            for (int bit = 0; bit < 16; bit++)
            {
                result |= ((valueX >> bit) & 1u) << (bit * 2);
                result |= ((valueY >> bit) & 1u) << (bit * 2 + 1);
            }

            return result;
        }
    }
}
