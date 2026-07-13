using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        public int LastSeenUpdate;
        public int IsDetached;
        public int DieWithParticles;
        public float Lifetime;
        public float DetachTime;
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
        private const int FreeCountStateIndex = 0;
        private const int AllocatedCountStateIndex = 1;
        private const int AllocatorLockStateIndex = 2;
        internal const int AllocatorStateCount = 3;

        [NativeDisableParallelForRestriction]
        public NativeArray<VividParticleTrailTileHeader> Headers;
        [NativeDisableParallelForRestriction]
        public NativeArray<float3> Positions;
        [NativeDisableParallelForRestriction]
        public NativeArray<float> Times;
        [NativeDisableParallelForRestriction]
        public NativeArray<float> CumulativeLengths;
        [NativeDisableParallelForRestriction]
        public NativeArray<int> FreeIndices;
        [NativeDisableParallelForRestriction]
        public NativeArray<int> AllocatorState;
        public int ControlPointCount;

        public bool IsCreated => Headers.IsCreated
            && Positions.IsCreated
            && Times.IsCreated
            && CumulativeLengths.IsCreated
            && FreeIndices.IsCreated
            && AllocatorState.IsCreated
            && AllocatorState.Length >= AllocatorStateCount
            && ControlPointCount > 1;

        public int FreeCount => IsCreated ? math.max(0, AllocatorState[FreeCountStateIndex]) : 0;

        public int AllocatedCount => IsCreated
            ? math.max(0, AllocatorState[AllocatedCountStateIndex])
            : 0;

        public unsafe bool TryAllocate(uint randomSeed, out VividParticleTrailHandle handle)
        {
            handle = VividParticleTrailHandle.Invalid;
            if (!IsCreated)
                return false;

            int* state = (int*)AllocatorState.GetUnsafePtr();
            AcquireAllocatorLock(state);
            int freeCount = state[FreeCountStateIndex];
            if (freeCount <= 0)
            {
                ReleaseAllocatorLock(state);
                return false;
            }

            int tileIndex = FreeIndices[freeCount - 1];
            state[FreeCountStateIndex] = freeCount - 1;
            state[AllocatedCountStateIndex]++;
            VividParticleTrailTileHeader header = Headers[tileIndex];
            header.Generation = math.max(1, header.Generation);
            header.IsAllocated = 1;
            header.PointCount = 0;
            header.HeadIndex = 0;
            header.TailIndex = 0;
            header.TotalLength = 0.0f;
            header.RandomSeed = randomSeed == 0u ? 1u : randomSeed;
            header.LastSeenUpdate = 0;
            header.IsDetached = 0;
            header.DieWithParticles = 1;
            header.Lifetime = 0.0f;
            header.DetachTime = 0.0f;
            Headers[tileIndex] = header;
            ClearTileData(tileIndex);
            handle = new VividParticleTrailHandle(tileIndex, header.Generation);
            ReleaseAllocatorLock(state);
            return true;
        }

        public unsafe bool Free(VividParticleTrailHandle handle)
        {
            if (!IsCreated || !handle.IsValid || (uint)handle.Index >= (uint)Headers.Length)
                return false;

            int* state = (int*)AllocatorState.GetUnsafePtr();
            AcquireAllocatorLock(state);
            VividParticleTrailTileHeader header = Headers[handle.Index];
            if (header.IsAllocated == 0 || header.Generation != handle.Generation)
            {
                ReleaseAllocatorLock(state);
                return false;
            }

            header.IsAllocated = 0;
            header.PointCount = 0;
            header.HeadIndex = 0;
            header.TailIndex = 0;
            header.TotalLength = 0.0f;
            header.RandomSeed = 0u;
            header.LastSeenUpdate = 0;
            header.IsDetached = 0;
            header.DieWithParticles = 1;
            header.Lifetime = 0.0f;
            header.DetachTime = 0.0f;
            header.Generation = NextGeneration(header.Generation);
            Headers[handle.Index] = header;
            int freeCount = math.clamp(state[FreeCountStateIndex], 0, FreeIndices.Length - 1);
            FreeIndices[freeCount] = handle.Index;
            state[FreeCountStateIndex] = freeCount + 1;
            state[AllocatedCountStateIndex] = math.max(
                0,
                state[AllocatedCountStateIndex] - 1);
            ReleaseAllocatorLock(state);
            return true;
        }

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

        private void ClearTileData(int tileIndex)
        {
            int start = tileIndex * ControlPointCount;
            int end = start + ControlPointCount;
            for (int index = start; index < end; index++)
            {
                Positions[index] = float3.zero;
                Times[index] = 0.0f;
                CumulativeLengths[index] = 0.0f;
            }
        }

        private static int NextGeneration(int generation)
        {
            return generation == int.MaxValue ? 1 : math.max(1, generation + 1);
        }

        private static unsafe void AcquireAllocatorLock(int* state)
        {
            while (Interlocked.CompareExchange(
                ref state[AllocatorLockStateIndex],
                1,
                0) != 0)
            {
            }
        }

        private static unsafe void ReleaseAllocatorLock(int* state)
        {
            Volatile.Write(ref state[AllocatorLockStateIndex], 0);
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
        private NativeArray<int> m_AllocatorState;

        public VividParticleTrailTable(int controlPointCount = DefaultControlPointCount)
        {
            m_ControlPointCount = math.max(2, controlPointCount);
        }

        public int controlPointCount => m_ControlPointCount;

        public int tileCapacity => m_Headers.IsCreated ? m_Headers.Length : 0;

        public int allocatedCount => m_AllocatorState.IsCreated
            ? m_AllocatorState[1]
            : 0;

        public int freeCount => m_AllocatorState.IsCreated
            ? m_AllocatorState[0]
            : 0;

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
            m_FreeIndices.ResizeUninitialized(nextCapacity);
            int freeCount = m_AllocatorState[0];
            for (int tileIndex = nextCapacity - 1; tileIndex >= previousCapacity; tileIndex--)
            {
                m_Headers[tileIndex] = new VividParticleTrailTileHeader
                {
                    Generation = 1,
                };
                m_FreeIndices[freeCount++] = tileIndex;
            }
            m_AllocatorState[0] = freeCount;
        }

        public bool Allocate(uint randomSeed, out VividParticleTrailHandle handle)
        {
            handle = VividParticleTrailHandle.Invalid;
            if (!m_AllocatorState.IsCreated || freeCount == 0)
                EnsureCapacity(math.max(8, tileCapacity * 2));
            return GetView().TryAllocate(randomSeed, out handle);
        }

        public bool Free(VividParticleTrailHandle handle)
        {
            return GetView().Free(handle);
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
                FreeIndices = m_FreeIndices.IsCreated ? m_FreeIndices.AsArray() : default,
                AllocatorState = m_AllocatorState,
                ControlPointCount = m_ControlPointCount,
            };
        }

        public void Clear()
        {
            if (!m_Headers.IsCreated)
                return;

            int freeCount = 0;
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
                header.LastSeenUpdate = 0;
                header.IsDetached = 0;
                header.DieWithParticles = 1;
                header.Lifetime = 0.0f;
                header.DetachTime = 0.0f;
                m_Headers[tileIndex] = header;
                m_FreeIndices[freeCount++] = tileIndex;
            }
            m_AllocatorState[0] = freeCount;
            m_AllocatorState[1] = 0;
            m_AllocatorState[2] = 0;
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
            if (m_AllocatorState.IsCreated)
                m_AllocatorState.Dispose();
            m_Headers = default;
            m_Positions = default;
            m_Times = default;
            m_CumulativeLengths = default;
            m_FreeIndices = default;
            m_AllocatorState = default;
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
            m_AllocatorState = new NativeArray<int>(
                VividParticleTrailTableView.AllocatorStateCount,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
        }

        private static int NextGeneration(int generation)
        {
            return generation == int.MaxValue ? 1 : math.max(1, generation + 1);
        }
    }
}
