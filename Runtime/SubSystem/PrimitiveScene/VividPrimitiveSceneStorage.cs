using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace VividRP.Runtime.PrimitiveScene
{
    internal readonly struct VividPrimitiveDirtyRange
    {
        internal VividPrimitiveDirtyRange(int start, int count)
        {
            Start = start;
            Count = count;
        }

        internal int Start { get; }

        internal int Count { get; }
    }

    internal sealed class VividPrimitiveGpuTable<T>
        where T : struct
    {
        private const int TargetDirtyPageBytes = 4 * 1024;

        private readonly List<T> m_Data = new();
        private readonly HashSet<int> m_DirtyPages = new();
        private readonly List<int> m_SortedDirtyPages = new();
        private readonly int m_RecordsPerPage;

        internal VividPrimitiveGpuTable()
        {
            int stride = UnsafeUtility.SizeOf<T>();
            m_RecordsPerPage = Mathf.Max(1, TargetDirtyPageBytes / Mathf.Max(1, stride));
        }

        internal int Count => m_Data.Count;

        internal int DirtyPageCount => m_DirtyPages.Count;

        internal List<T> Data => m_Data;

        internal T this[int index] => m_Data[index];

        internal void Set(int index, in T value)
        {
            EnsureCount(index + 1);
            m_Data[index] = value;
            MarkDirty(index, 1);
        }

        internal bool SetIfChanged(int index, in T value)
        {
            EnsureCount(index + 1);
            if (EqualityComparer<T>.Default.Equals(m_Data[index], value))
                return false;

            m_Data[index] = value;
            MarkDirty(index, 1);
            return true;
        }

        internal void Resize(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            int previousCount = m_Data.Count;
            if (count == previousCount)
                return;

            if (count < previousCount)
            {
                m_Data.RemoveRange(count, previousCount - count);
                int remainingPageCount = (count + m_RecordsPerPage - 1) / m_RecordsPerPage;
                m_DirtyPages.RemoveWhere(page => page >= remainingPageCount);
                m_SortedDirtyPages.Clear();
                return;
            }

            int addedCount = count - previousCount;
            if (m_Data.Capacity < count)
                m_Data.Capacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));
            for (int index = 0; index < addedCount; index++)
                m_Data.Add(default);
            MarkDirty(previousCount, addedCount);
        }

        internal void MarkAllDirty()
        {
            MarkDirty(0, m_Data.Count);
        }

        internal void MarkDirty(int start, int count)
        {
            if (count <= 0)
                return;
            if (start < 0 || start > m_Data.Count - count)
                throw new ArgumentOutOfRangeException(nameof(start));

            int firstPage = start / m_RecordsPerPage;
            int lastPage = (start + count - 1) / m_RecordsPerPage;
            for (int page = firstPage; page <= lastPage; page++)
                m_DirtyPages.Add(page);
        }

        internal void CollectDirtyRanges(List<VividPrimitiveDirtyRange> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();
            if (m_DirtyPages.Count == 0 || m_Data.Count == 0)
                return;

            m_SortedDirtyPages.Clear();
            foreach (int page in m_DirtyPages)
                m_SortedDirtyPages.Add(page);
            m_SortedDirtyPages.Sort();

            int rangeFirstPage = m_SortedDirtyPages[0];
            int rangeLastPage = rangeFirstPage;
            for (int index = 1; index < m_SortedDirtyPages.Count; index++)
            {
                int page = m_SortedDirtyPages[index];
                if (page == rangeLastPage + 1)
                {
                    rangeLastPage = page;
                    continue;
                }

                AddRange(destination, rangeFirstPage, rangeLastPage);
                rangeFirstPage = page;
                rangeLastPage = page;
            }
            AddRange(destination, rangeFirstPage, rangeLastPage);
        }

        internal void ClearDirtyPages()
        {
            m_DirtyPages.Clear();
            m_SortedDirtyPages.Clear();
        }

        private void EnsureCount(int count)
        {
            if (count > m_Data.Count)
                Resize(count);
        }

        private void AddRange(List<VividPrimitiveDirtyRange> destination, int firstPage, int lastPage)
        {
            int start = firstPage * m_RecordsPerPage;
            int end = Mathf.Min(m_Data.Count, (lastPage + 1) * m_RecordsPerPage);
            if (end > start)
                destination.Add(new VividPrimitiveDirtyRange(start, end - start));
        }
    }

    internal sealed class VividVersionedSlotAllocator
    {
        private readonly List<uint> m_Generations = new();
        private readonly List<bool> m_Allocated = new();
        private readonly Stack<int> m_FreeSlots = new();

        internal int ActiveCount { get; private set; }

        internal int SlotCount => m_Generations.Count;

        internal int FreeCount => m_FreeSlots.Count;

        internal int Allocate(out uint generation)
        {
            int slot;
            if (m_FreeSlots.Count > 0)
            {
                slot = m_FreeSlots.Pop();
                generation = m_Generations[slot];
                m_Allocated[slot] = true;
            }
            else
            {
                slot = m_Generations.Count;
                generation = 1u;
                m_Generations.Add(generation);
                m_Allocated.Add(true);
            }

            ActiveCount++;
            return slot;
        }

        internal bool Free(int slot, uint generation, out uint nextGeneration)
        {
            nextGeneration = 0u;
            if (!IsValid(slot, generation))
                return false;

            nextGeneration = NextGeneration(generation);
            m_Generations[slot] = nextGeneration;
            m_Allocated[slot] = false;
            m_FreeSlots.Push(slot);
            ActiveCount--;
            return true;
        }

        internal bool IsValid(int slot, uint generation)
        {
            return generation != 0u
                && (uint) slot < (uint) m_Generations.Count
                && m_Allocated[slot]
                && m_Generations[slot] == generation;
        }

        internal bool IsAllocated(int slot)
        {
            return (uint) slot < (uint) m_Allocated.Count && m_Allocated[slot];
        }

        internal uint GetGeneration(int slot)
        {
            return (uint) slot < (uint) m_Generations.Count ? m_Generations[slot] : 0u;
        }

        internal static uint NextGeneration(uint generation)
        {
            return generation == uint.MaxValue ? 1u : generation + 1u;
        }
    }

    internal sealed class VividPrimitiveSectionRangeAllocator
    {
        private readonly List<FreeRange> m_FreeRanges = new();

        internal int HighWaterMark { get; private set; }

        internal int Allocate(int count)
        {
            if (count <= 0)
                return 0;

            int bestIndex = -1;
            int bestCount = int.MaxValue;
            for (int index = 0; index < m_FreeRanges.Count; index++)
            {
                int freeCount = m_FreeRanges[index].Count;
                if (freeCount < count || freeCount >= bestCount)
                    continue;
                bestIndex = index;
                bestCount = freeCount;
            }

            if (bestIndex < 0)
            {
                int start = HighWaterMark;
                HighWaterMark = checked(HighWaterMark + count);
                return start;
            }

            FreeRange range = m_FreeRanges[bestIndex];
            int allocatedStart = range.Start;
            if (range.Count == count)
                m_FreeRanges.RemoveAt(bestIndex);
            else
                m_FreeRanges[bestIndex] = new FreeRange(range.Start + count, range.Count - count);
            return allocatedStart;
        }

        internal void Free(int start, int count)
        {
            if (count <= 0)
                return;
            if (start < 0 || start > HighWaterMark - count)
                throw new ArgumentOutOfRangeException(nameof(start));

            int insertionIndex = 0;
            while (insertionIndex < m_FreeRanges.Count && m_FreeRanges[insertionIndex].Start < start)
                insertionIndex++;

            if (insertionIndex > 0 && m_FreeRanges[insertionIndex - 1].End > start)
                throw new InvalidOperationException("Attempted to free an overlapping PrimitiveScene section range.");
            if (insertionIndex < m_FreeRanges.Count && start + count > m_FreeRanges[insertionIndex].Start)
                throw new InvalidOperationException("Attempted to free an overlapping PrimitiveScene section range.");

            m_FreeRanges.Insert(insertionIndex, new FreeRange(start, count));
            CoalesceAt(insertionIndex);
        }

        private void CoalesceAt(int index)
        {
            if (index > 0 && m_FreeRanges[index - 1].End == m_FreeRanges[index].Start)
            {
                FreeRange previous = m_FreeRanges[index - 1];
                FreeRange current = m_FreeRanges[index];
                m_FreeRanges[index - 1] = new FreeRange(previous.Start, previous.Count + current.Count);
                m_FreeRanges.RemoveAt(index);
                index--;
            }

            if (index + 1 < m_FreeRanges.Count && m_FreeRanges[index].End == m_FreeRanges[index + 1].Start)
            {
                FreeRange current = m_FreeRanges[index];
                FreeRange next = m_FreeRanges[index + 1];
                m_FreeRanges[index] = new FreeRange(current.Start, current.Count + next.Count);
                m_FreeRanges.RemoveAt(index + 1);
            }
        }

        private readonly struct FreeRange
        {
            internal FreeRange(int start, int count)
            {
                Start = start;
                Count = count;
            }

            internal int Start { get; }

            internal int Count { get; }

            internal int End => Start + Count;
        }
    }
}
