using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VividRP.Runtime.Particle.Trail
{
    internal readonly struct VividParticleTrailHandle : IEquatable<VividParticleTrailHandle>
    {
        public static readonly VividParticleTrailHandle Invalid = new(-1, 0);

        public VividParticleTrailHandle(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        public int Index { get; }

        public int Generation { get; }

        public bool IsValid => Index >= 0 && Generation > 0;

        public bool Equals(VividParticleTrailHandle other)
        {
            return Index == other.Index && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is VividParticleTrailHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Index * 397) ^ Generation;
            }
        }

        public static bool operator ==(VividParticleTrailHandle left, VividParticleTrailHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VividParticleTrailHandle left, VividParticleTrailHandle right)
        {
            return !left.Equals(right);
        }
    }

    internal struct VividParticleTrailTileHeader
    {
        public int Generation;
        public int IsAllocated;
        public int PointCount;
        public int HeadIndex;
        public int TailIndex;
        public float TotalLength;
        public uint RandomSeed;
    }

    internal readonly struct VividParticleTrailBounds
    {
        public VividParticleTrailBounds(float3 minimum, float3 maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
            IsValid = 1;
        }

        public float3 Minimum { get; }

        public float3 Maximum { get; }

        public int IsValid { get; }

        public float3 Center => (Minimum + Maximum) * 0.5f;

        public float3 Extents => (Maximum - Minimum) * 0.5f;
    }

    internal struct VividParticleTrailTableView
    {
        public NativeArray<VividParticleTrailTileHeader> Headers;
        public NativeArray<float3> Positions;
        public NativeArray<float> Times;
        public NativeArray<float> CumulativeLengths;
        public int ControlPointCount;

        public bool IsCreated => Headers.IsCreated
            && Positions.IsCreated
            && Times.IsCreated
            && CumulativeLengths.IsCreated
            && ControlPointCount > 1;

        public bool IsValid(VividParticleTrailHandle handle)
        {
            if (!IsCreated || !handle.IsValid || (uint)handle.Index >= (uint)Headers.Length)
                return false;

            VividParticleTrailTileHeader header = Headers[handle.Index];
            return header.IsAllocated != 0 && header.Generation == handle.Generation;
        }

        public bool AppendControlPoint(
            VividParticleTrailHandle handle,
            float3 position,
            float time,
            float minimumVertexDistance)
        {
            if (!IsValid(handle))
                return false;

            VividParticleTrailTileHeader header = Headers[handle.Index];
            int baseIndex = handle.Index * ControlPointCount;
            if (header.PointCount <= 0)
            {
                header.PointCount = 1;
                header.HeadIndex = 0;
                header.TailIndex = 0;
                header.TotalLength = 0.0f;
                Positions[baseIndex] = position;
                Times[baseIndex] = time;
                CumulativeLengths[baseIndex] = 0.0f;
                Headers[handle.Index] = header;
                return true;
            }

            int headDataIndex = baseIndex + header.HeadIndex;
            float distance = math.distance(Positions[headDataIndex], position);
            float clampedMinimumDistance = math.max(0.0f, minimumVertexDistance);
            if (distance < clampedMinimumDistance)
            {
                Positions[headDataIndex] = position;
                Times[headDataIndex] = time;
                if (header.PointCount > 1)
                {
                    int previousIndex = PreviousRingIndex(header.HeadIndex, ControlPointCount);
                    int previousDataIndex = baseIndex + previousIndex;
                    CumulativeLengths[headDataIndex] = CumulativeLengths[previousDataIndex]
                        + math.distance(Positions[previousDataIndex], position);
                }
                header.TotalLength = math.max(
                    0.0f,
                    CumulativeLengths[headDataIndex]
                    - CumulativeLengths[baseIndex + header.TailIndex]);
                Headers[handle.Index] = header;
                return false;
            }

            int nextHeadIndex = NextRingIndex(header.HeadIndex, ControlPointCount);
            int nextHeadDataIndex = baseIndex + nextHeadIndex;
            Positions[nextHeadDataIndex] = position;
            Times[nextHeadDataIndex] = time;
            CumulativeLengths[nextHeadDataIndex] = CumulativeLengths[headDataIndex] + distance;
            header.HeadIndex = nextHeadIndex;
            if (header.PointCount < ControlPointCount)
            {
                header.PointCount++;
            }
            else
            {
                header.TailIndex = NextRingIndex(header.TailIndex, ControlPointCount);
            }

            header.TotalLength = math.max(
                0.0f,
                CumulativeLengths[nextHeadDataIndex]
                - CumulativeLengths[baseIndex + header.TailIndex]);
            Headers[handle.Index] = header;
            return true;
        }

        public int PruneExpiredControlPoints(
            VividParticleTrailHandle handle,
            float currentTime,
            float lifetime)
        {
            if (!IsValid(handle))
                return 0;

            VividParticleTrailTileHeader header = Headers[handle.Index];
            int removedCount = 0;
            int baseIndex = handle.Index * ControlPointCount;
            float clampedLifetime = math.max(0.0f, lifetime);
            while (header.PointCount > 1
                && currentTime - Times[baseIndex + header.TailIndex] > clampedLifetime)
            {
                header.TailIndex = NextRingIndex(header.TailIndex, ControlPointCount);
                header.PointCount--;
                removedCount++;
            }

            header.TotalLength = header.PointCount > 1
                ? math.max(
                    0.0f,
                    CumulativeLengths[baseIndex + header.HeadIndex]
                    - CumulativeLengths[baseIndex + header.TailIndex])
                : 0.0f;
            Headers[handle.Index] = header;
            return removedCount;
        }

        public bool TryGetControlPoint(
            VividParticleTrailHandle handle,
            int ordinalFromTail,
            out float3 position,
            out float time,
            out float lengthFromTail)
        {
            position = default;
            time = 0.0f;
            lengthFromTail = 0.0f;
            if (!IsValid(handle))
                return false;

            VividParticleTrailTileHeader header = Headers[handle.Index];
            if ((uint)ordinalFromTail >= (uint)header.PointCount)
                return false;

            int ringIndex = (header.TailIndex + ordinalFromTail) % ControlPointCount;
            int baseIndex = handle.Index * ControlPointCount;
            int dataIndex = baseIndex + ringIndex;
            position = Positions[dataIndex];
            time = Times[dataIndex];
            lengthFromTail = math.max(
                0.0f,
                CumulativeLengths[dataIndex]
                - CumulativeLengths[baseIndex + header.TailIndex]);
            return true;
        }

        public VividParticleTrailBounds CalculateBounds(
            VividParticleTrailHandle handle,
            float halfWidth = 0.0f)
        {
            if (!IsValid(handle))
                return default;

            VividParticleTrailTileHeader header = Headers[handle.Index];
            if (header.PointCount <= 0)
                return default;

            int baseIndex = handle.Index * ControlPointCount;
            float3 minimum = new float3(float.MaxValue);
            float3 maximum = new float3(float.MinValue);
            for (int ordinal = 0; ordinal < header.PointCount; ordinal++)
            {
                int ringIndex = (header.TailIndex + ordinal) % ControlPointCount;
                float3 position = Positions[baseIndex + ringIndex];
                minimum = math.min(minimum, position);
                maximum = math.max(maximum, position);
            }

            float3 width = new float3(math.max(0.0f, halfWidth));
            return new VividParticleTrailBounds(minimum - width, maximum + width);
        }

        private static int NextRingIndex(int index, int count)
        {
            index++;
            return index == count ? 0 : index;
        }

        private static int PreviousRingIndex(int index, int count)
        {
            return index == 0 ? count - 1 : index - 1;
        }
    }

    internal sealed class VividParticleTrailTable : IDisposable
    {
        public const int DefaultControlPointCount = 32;

        private readonly int m_ControlPointCount;
        private NativeList<VividParticleTrailTileHeader> m_Headers;
        private NativeList<float3> m_Positions;
        private NativeList<float> m_Times;
        private NativeList<float> m_CumulativeLengths;
        private NativeList<int> m_FreeIndices;
        private int m_AllocatedCount;

        public VividParticleTrailTable(int controlPointCount = DefaultControlPointCount)
        {
            m_ControlPointCount = math.max(2, controlPointCount);
        }

        public int controlPointCount => m_ControlPointCount;

        public int tileCapacity => m_Headers.IsCreated ? m_Headers.Length : 0;

        public int allocatedCount => m_AllocatedCount;

        public int freeCount => m_FreeIndices.IsCreated ? m_FreeIndices.Length : 0;

        public bool isCreated => m_Headers.IsCreated;

        public void EnsureCapacity(int requestedTileCapacity)
        {
            int requested = math.max(0, requestedTileCapacity);
            if (requested <= tileCapacity)
                return;

            EnsureCreated();
            int previousCapacity = tileCapacity;
            int nextCapacity = math.max(8, math.ceilpow2(requested));
            m_Headers.Resize(nextCapacity, NativeArrayOptions.ClearMemory);
            m_Positions.Resize(nextCapacity * m_ControlPointCount, NativeArrayOptions.ClearMemory);
            m_Times.Resize(nextCapacity * m_ControlPointCount, NativeArrayOptions.ClearMemory);
            m_CumulativeLengths.Resize(
                nextCapacity * m_ControlPointCount,
                NativeArrayOptions.ClearMemory);
            for (int tileIndex = nextCapacity - 1; tileIndex >= previousCapacity; tileIndex--)
            {
                m_Headers[tileIndex] = new VividParticleTrailTileHeader
                {
                    Generation = 1,
                };
                m_FreeIndices.Add(tileIndex);
            }
        }

        public bool Allocate(uint randomSeed, out VividParticleTrailHandle handle)
        {
            handle = VividParticleTrailHandle.Invalid;
            if (!m_FreeIndices.IsCreated || m_FreeIndices.Length == 0)
                EnsureCapacity(math.max(8, tileCapacity * 2));
            if (m_FreeIndices.Length == 0)
                return false;

            int lastFreeIndex = m_FreeIndices.Length - 1;
            int tileIndex = m_FreeIndices[lastFreeIndex];
            m_FreeIndices.RemoveAt(lastFreeIndex);
            VividParticleTrailTileHeader header = m_Headers[tileIndex];
            header.Generation = math.max(1, header.Generation);
            header.IsAllocated = 1;
            header.PointCount = 0;
            header.HeadIndex = 0;
            header.TailIndex = 0;
            header.TotalLength = 0.0f;
            header.RandomSeed = randomSeed == 0u ? 1u : randomSeed;
            m_Headers[tileIndex] = header;
            ClearTileData(tileIndex);
            m_AllocatedCount++;
            handle = new VividParticleTrailHandle(tileIndex, header.Generation);
            return true;
        }

        public bool Free(VividParticleTrailHandle handle)
        {
            if (!GetView().IsValid(handle))
                return false;

            VividParticleTrailTileHeader header = m_Headers[handle.Index];
            header.IsAllocated = 0;
            header.PointCount = 0;
            header.HeadIndex = 0;
            header.TailIndex = 0;
            header.TotalLength = 0.0f;
            header.RandomSeed = 0u;
            header.Generation = NextGeneration(header.Generation);
            m_Headers[handle.Index] = header;
            m_FreeIndices.Add(handle.Index);
            m_AllocatedCount = math.max(0, m_AllocatedCount - 1);
            return true;
        }

        public VividParticleTrailTableView GetView()
        {
            return new VividParticleTrailTableView
            {
                Headers = m_Headers.IsCreated ? m_Headers.AsArray() : default,
                Positions = m_Positions.IsCreated ? m_Positions.AsArray() : default,
                Times = m_Times.IsCreated ? m_Times.AsArray() : default,
                CumulativeLengths = m_CumulativeLengths.IsCreated
                    ? m_CumulativeLengths.AsArray()
                    : default,
                ControlPointCount = m_ControlPointCount,
            };
        }

        public void Clear()
        {
            if (!m_Headers.IsCreated)
                return;

            m_FreeIndices.Clear();
            for (int tileIndex = m_Headers.Length - 1; tileIndex >= 0; tileIndex--)
            {
                VividParticleTrailTileHeader header = m_Headers[tileIndex];
                if (header.IsAllocated != 0)
                    header.Generation = NextGeneration(header.Generation);
                header.IsAllocated = 0;
                header.PointCount = 0;
                header.HeadIndex = 0;
                header.TailIndex = 0;
                header.TotalLength = 0.0f;
                header.RandomSeed = 0u;
                m_Headers[tileIndex] = header;
                m_FreeIndices.Add(tileIndex);
            }
            m_AllocatedCount = 0;
        }

        public void Dispose()
        {
            if (m_Headers.IsCreated)
                m_Headers.Dispose();
            if (m_Positions.IsCreated)
                m_Positions.Dispose();
            if (m_Times.IsCreated)
                m_Times.Dispose();
            if (m_CumulativeLengths.IsCreated)
                m_CumulativeLengths.Dispose();
            if (m_FreeIndices.IsCreated)
                m_FreeIndices.Dispose();
            m_Headers = default;
            m_Positions = default;
            m_Times = default;
            m_CumulativeLengths = default;
            m_FreeIndices = default;
            m_AllocatedCount = 0;
        }

        private void EnsureCreated()
        {
            if (m_Headers.IsCreated)
                return;

            m_Headers = new NativeList<VividParticleTrailTileHeader>(8, Allocator.Persistent);
            m_Positions = new NativeList<float3>(8 * m_ControlPointCount, Allocator.Persistent);
            m_Times = new NativeList<float>(8 * m_ControlPointCount, Allocator.Persistent);
            m_CumulativeLengths = new NativeList<float>(
                8 * m_ControlPointCount,
                Allocator.Persistent);
            m_FreeIndices = new NativeList<int>(8, Allocator.Persistent);
        }

        private void ClearTileData(int tileIndex)
        {
            int start = tileIndex * m_ControlPointCount;
            int end = start + m_ControlPointCount;
            for (int index = start; index < end; index++)
            {
                m_Positions[index] = float3.zero;
                m_Times[index] = 0.0f;
                m_CumulativeLengths[index] = 0.0f;
            }
        }

        private static int NextGeneration(int generation)
        {
            return generation == int.MaxValue ? 1 : math.max(1, generation + 1);
        }
    }
}
