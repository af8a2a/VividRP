using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace VividRP.Runtime
{
    internal sealed class VividPerObjectRecordAllocator
    {
        internal const int ReservedBytes = VividPerObjectLayout.RecordAlignment;
        internal const int DefaultInitialCapacity = 64 * 1024;

        private readonly List<FreeRange> m_FreeRanges = new();
        private readonly int m_MaxCapacity;
        private byte[] m_Data;
        private int m_UsedBytes;

        internal VividPerObjectRecordAllocator(
            int initialCapacity = DefaultInitialCapacity,
            int maxCapacity = int.MaxValue & ~15)
        {
            if (initialCapacity < ReservedBytes)
                initialCapacity = ReservedBytes;

            initialCapacity = AlignUp(initialCapacity, VividPerObjectLayout.RecordAlignment);
            maxCapacity = AlignDown(maxCapacity, VividPerObjectLayout.RecordAlignment);
            if (maxCapacity < initialCapacity)
                throw new ArgumentOutOfRangeException(nameof(maxCapacity));

            m_MaxCapacity = maxCapacity;
            m_Data = new byte[initialCapacity];
            m_FreeRanges.Add(new FreeRange(ReservedBytes, initialCapacity - ReservedBytes));
            m_UsedBytes = ReservedBytes;
        }

        internal byte[] Data => m_Data;

        internal int Capacity => m_Data.Length;

        internal int UsedBytes => m_UsedBytes;

        internal int FreeBytes => Capacity - m_UsedBytes;

        internal int LargestFreeBlock
        {
            get
            {
                int largest = 0;
                for (int i = 0; i < m_FreeRanges.Count; i++)
                    largest = Math.Max(largest, m_FreeRanges[i].Length);
                return largest;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int Allocate(int size, out bool capacityChanged)
        {
            size = AlignUp(size, VividPerObjectLayout.RecordAlignment);
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            capacityChanged = false;
            int rangeIndex = FindBestFit(size);
            if (rangeIndex < 0)
            {
                Grow(size);
                capacityChanged = true;
                rangeIndex = FindBestFit(size);
            }

            if (rangeIndex < 0)
                throw new InvalidOperationException("Per-object record allocator failed after growing its storage.");

            FreeRange range = m_FreeRanges[rangeIndex];
            int address = range.Start;
            if (range.Length == size)
            {
                m_FreeRanges.RemoveAt(rangeIndex);
            }
            else
            {
                m_FreeRanges[rangeIndex] = new FreeRange(range.Start + size, range.Length - size);
            }

            m_UsedBytes += size;
            return address;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Free(int address, int size)
        {
            size = AlignUp(size, VividPerObjectLayout.RecordAlignment);
            if (address < ReservedBytes
                || (address & (VividPerObjectLayout.RecordAlignment - 1)) != 0
                || size <= 0
                || address > Capacity - size)
            {
                throw new ArgumentOutOfRangeException(nameof(address));
            }

            Array.Clear(m_Data, address, size);
            int insertionIndex = 0;
            while (insertionIndex < m_FreeRanges.Count && m_FreeRanges[insertionIndex].Start < address)
                insertionIndex++;

            if (insertionIndex > 0 && m_FreeRanges[insertionIndex - 1].End > address)
                throw new InvalidOperationException("Attempted to free an overlapping per-object buffer range.");
            if (insertionIndex < m_FreeRanges.Count && address + size > m_FreeRanges[insertionIndex].Start)
                throw new InvalidOperationException("Attempted to free an overlapping per-object buffer range.");

            m_FreeRanges.Insert(insertionIndex, new FreeRange(address, size));
            CoalesceAt(insertionIndex);
            m_UsedBytes -= size;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindBestFit(int size)
        {
            int bestIndex = -1;
            int bestLength = int.MaxValue;
            for (int i = 0; i < m_FreeRanges.Count; i++)
            {
                int length = m_FreeRanges[i].Length;
                if (length < size || length >= bestLength)
                    continue;

                bestIndex = i;
                bestLength = length;
                if (length == size)
                    break;
            }

            return bestIndex;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Grow(int requiredContiguousBytes)
        {
            int oldCapacity = Capacity;
            long minimumCapacity = (long)oldCapacity + requiredContiguousBytes;
            long newCapacity = oldCapacity;
            while (newCapacity < minimumCapacity && newCapacity < m_MaxCapacity)
                newCapacity = Math.Min((long)m_MaxCapacity, newCapacity * 2L);

            if (newCapacity < minimumCapacity || newCapacity > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Per-object buffer requires {minimumCapacity} bytes, exceeding the configured {m_MaxCapacity}-byte limit.");
            }

            Array.Resize(ref m_Data, (int)newCapacity);
            InsertFreeRange(new FreeRange(oldCapacity, (int)newCapacity - oldCapacity));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InsertFreeRange(FreeRange range)
        {
            int index = 0;
            while (index < m_FreeRanges.Count && m_FreeRanges[index].Start < range.Start)
                index++;
            m_FreeRanges.Insert(index, range);
            CoalesceAt(index);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CoalesceAt(int index)
        {
            if (index > 0 && m_FreeRanges[index - 1].End == m_FreeRanges[index].Start)
            {
                FreeRange previous = m_FreeRanges[index - 1];
                FreeRange current = m_FreeRanges[index];
                m_FreeRanges[index - 1] = new FreeRange(previous.Start, previous.Length + current.Length);
                m_FreeRanges.RemoveAt(index);
                index--;
            }

            if (index + 1 < m_FreeRanges.Count && m_FreeRanges[index].End == m_FreeRanges[index + 1].Start)
            {
                FreeRange current = m_FreeRanges[index];
                FreeRange next = m_FreeRanges[index + 1];
                m_FreeRanges[index] = new FreeRange(current.Start, current.Length + next.Length);
                m_FreeRanges.RemoveAt(index + 1);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int AlignUp(int value, int alignment)
        {
            checked
            {
                return (value + alignment - 1) & ~(alignment - 1);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int AlignDown(int value, int alignment)
        {
            return value & ~(alignment - 1);
        }

        private readonly struct FreeRange
        {
            internal FreeRange(int start, int length)
            {
                Start = start;
                Length = length;
            }

            internal int Start { get; }

            internal int Length { get; }

            internal int End => Start + Length;
        }
    }
}
