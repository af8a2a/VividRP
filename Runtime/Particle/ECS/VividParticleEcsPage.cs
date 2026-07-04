using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VividRP.Runtime.Particle.ECS
{
    internal readonly struct VividParticlePage : IEquatable<VividParticlePage>
    {
        public static readonly VividParticlePage Invalid = new(-1);

        public VividParticlePage(int index)
        {
            Index = index;
        }

        public int Index { get; }

        public bool IsValid => Index >= 0;

        public bool Equals(VividParticlePage other)
        {
            return Index == other.Index;
        }

        public override bool Equals(object obj)
        {
            return obj is VividParticlePage other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Index;
        }
    }

    internal readonly struct VividParticlePageInfo
    {
        public VividParticlePageInfo(int pageIndex, int startIndex, int entryCount)
            : this(pageIndex, startIndex, entryCount, VividParticleSystemId.Invalid)
        {
        }

        public VividParticlePageInfo(
            int pageIndex,
            int startIndex,
            int entryCount,
            VividParticleSystemId systemId)
        {
            if (pageIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(pageIndex));

            if (startIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(startIndex));

            if (entryCount < 0 || entryCount > VividParticleEcsConstants.PageEntryCount)
                throw new ArgumentOutOfRangeException(nameof(entryCount));

            Page = new VividParticlePage(pageIndex);
            PageIndex = pageIndex;
            StartIndex = startIndex;
            EntryCount = entryCount;
            Capacity = VividParticleEcsConstants.PageEntryCount;
            SystemId = systemId;
        }

        public VividParticlePage Page { get; }

        public int PageIndex { get; }

        public int StartIndex { get; }

        public int EntryCount { get; }

        public int Capacity { get; }

        public VividParticleSystemId SystemId { get; }
    }

    internal sealed class VividParticleColumn<T> : IDisposable
        where T : struct
    {
        private NativeArray<T> m_Data;

        public bool isCreated => m_Data.IsCreated;

        public int length => m_Data.IsCreated ? m_Data.Length : 0;

        public NativeArray<T> data => m_Data;

        public T this[int index]
        {
            get => m_Data[index];
            set => m_Data[index] = value;
        }

        public void EnsureLength(int requestedLength)
        {
            if (requestedLength < 0)
                throw new ArgumentOutOfRangeException(nameof(requestedLength));

            if (length == requestedLength)
                return;

            NativeArray<T> newData = default;
            if (requestedLength > 0)
                newData = new NativeArray<T>(requestedLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            if (m_Data.IsCreated && newData.IsCreated)
            {
                int copyCount = math.min(m_Data.Length, newData.Length);
                if (copyCount > 0)
                    NativeArray<T>.Copy(m_Data, newData, copyCount);
            }

            Dispose();
            m_Data = newData;
        }

        public void Dispose()
        {
            if (m_Data.IsCreated)
                m_Data.Dispose();

            m_Data = default;
        }
    }

    internal sealed class VividParticleCommonColumns : IDisposable
    {
        private readonly VividParticleColumn<float3> m_Positions = new();
        private readonly VividParticleColumn<float3> m_Velocities = new();
        private readonly VividParticleColumn<float> m_StartLifetimes = new();
        private readonly VividParticleColumn<float> m_RemainingLifetimes = new();
        private readonly VividParticleColumn<float4> m_Colors = new();
        private readonly VividParticleColumn<float> m_Sizes = new();

        public bool isCreated => m_Positions.isCreated;

        public int capacity => isCreated ? m_Positions.length : 0;

        public VividParticleColumn<float3> positionColumn => m_Positions;

        public VividParticleColumn<float3> velocityColumn => m_Velocities;

        public VividParticleColumn<float> startLifetimeColumn => m_StartLifetimes;

        public VividParticleColumn<float> remainingLifetimeColumn => m_RemainingLifetimes;

        public VividParticleColumn<float4> colorColumn => m_Colors;

        public VividParticleColumn<float> sizeColumn => m_Sizes;

        public NativeArray<float3> positions => m_Positions.data;

        public NativeArray<float3> velocities => m_Velocities.data;

        public NativeArray<float> startLifetimes => m_StartLifetimes.data;

        public NativeArray<float> remainingLifetimes => m_RemainingLifetimes.data;

        public NativeArray<float4> colors => m_Colors.data;

        public NativeArray<float> sizes => m_Sizes.data;

        public void EnsurePageCapacity(int pageCount)
        {
            if (pageCount < 0)
                throw new ArgumentOutOfRangeException(nameof(pageCount));

            int length = pageCount * VividParticleEcsConstants.PageEntryCount;
            m_Positions.EnsureLength(length);
            m_Velocities.EnsureLength(length);
            m_StartLifetimes.EnsureLength(length);
            m_RemainingLifetimes.EnsureLength(length);
            m_Colors.EnsureLength(length);
            m_Sizes.EnsureLength(length);
        }

        public void SetParticle(
            int index,
            float3 position,
            float3 velocity,
            float startLifetime,
            float remainingLifetime,
            float size,
            float4 color)
        {
            m_Positions[index] = position;
            m_Velocities[index] = velocity;
            m_StartLifetimes[index] = startLifetime;
            m_RemainingLifetimes[index] = remainingLifetime;
            m_Colors[index] = color;
            m_Sizes[index] = size;
        }

        public void CopyParticle(int sourceIndex, int destinationIndex)
        {
            m_Positions[destinationIndex] = m_Positions[sourceIndex];
            m_Velocities[destinationIndex] = m_Velocities[sourceIndex];
            m_StartLifetimes[destinationIndex] = m_StartLifetimes[sourceIndex];
            m_RemainingLifetimes[destinationIndex] = m_RemainingLifetimes[sourceIndex];
            m_Colors[destinationIndex] = m_Colors[sourceIndex];
            m_Sizes[destinationIndex] = m_Sizes[sourceIndex];
        }

        public float3 GetPosition(int index)
        {
            return m_Positions[index];
        }

        public float3 GetVelocity(int index)
        {
            return m_Velocities[index];
        }

        public float4 GetColor(int index)
        {
            return m_Colors[index];
        }

        public void Dispose()
        {
            m_Positions.Dispose();
            m_Velocities.Dispose();
            m_StartLifetimes.Dispose();
            m_RemainingLifetimes.Dispose();
            m_Colors.Dispose();
            m_Sizes.Dispose();
        }
    }

    internal sealed class VividParticleArchetypeLine : IDisposable
    {
        private readonly VividParticleCommonColumns m_Common = new();
        private NativeArray<int> m_PageEntryCounts;
        private VividParticleSystemId m_SystemId = VividParticleSystemId.Invalid;
        private int m_MaxParticles;
        private int m_ActiveCount;

        public VividParticleCommonColumns common => m_Common;

        public VividParticleSystemId systemId
        {
            get => m_SystemId;
            set => m_SystemId = value;
        }

        public bool isCreated => m_PageEntryCounts.IsCreated && m_Common.isCreated;

        public int pageCount => m_PageEntryCounts.IsCreated ? m_PageEntryCounts.Length : 0;

        public int capacity => pageCount * VividParticleEcsConstants.PageEntryCount;

        public int maxParticles => m_MaxParticles;

        public int activeCount => math.clamp(m_ActiveCount, 0, math.min(capacity, m_MaxParticles));

        public void EnsureCapacity(int maxParticles)
        {
            int requestedMaxParticles = math.max(1, maxParticles);
            int requestedCapacity = VividParticleStorage.AlignToPage(requestedMaxParticles);
            int requestedPageCount = requestedCapacity / VividParticleEcsConstants.PageEntryCount;
            if (pageCount == requestedPageCount)
            {
                m_MaxParticles = requestedMaxParticles;
                SetActiveCount(math.min(activeCount, m_MaxParticles));
                return;
            }

            m_Common.EnsurePageCapacity(requestedPageCount);

            var newPageEntryCounts = new NativeArray<int>(
                requestedPageCount,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            DisposePageEntryCounts();
            m_PageEntryCounts = newPageEntryCounts;
            m_MaxParticles = requestedMaxParticles;
            SetActiveCount(math.min(activeCount, m_MaxParticles));
        }

        public void Clear()
        {
            SetActiveCount(0);
        }

        public bool Append(
            float3 position,
            float3 velocity,
            float startLifetime,
            float remainingLifetime,
            float size,
            float4 color,
            out int index)
        {
            index = -1;
            if (!isCreated || activeCount >= m_MaxParticles)
                return false;

            index = m_ActiveCount++;
            m_Common.SetParticle(index, position, velocity, startLifetime, remainingLifetime, size, color);
            SetPageEntryCountForIndex(index);
            return true;
        }

        public void SetActiveCount(int count)
        {
            m_ActiveCount = math.clamp(count, 0, math.min(capacity, m_MaxParticles));
            RebuildPageEntryCounts();
        }

        public VividParticlePageInfo GetPageInfo(int pageIndex)
        {
            if ((uint)pageIndex >= (uint)pageCount)
                throw new ArgumentOutOfRangeException(nameof(pageIndex));

            return new VividParticlePageInfo(
                pageIndex,
                pageIndex * VividParticleEcsConstants.PageEntryCount,
                m_PageEntryCounts[pageIndex],
                m_SystemId);
        }

        public VividParticlePageGroup CreatePageGroup(Allocator allocator)
        {
            int livePageCount = activeCount <= 0
                ? 0
                : (activeCount + VividParticleEcsConstants.PageEntryCount - 1) / VividParticleEcsConstants.PageEntryCount;

            var pages = new NativeArray<VividParticlePageInfo>(livePageCount, allocator, NativeArrayOptions.UninitializedMemory);
            for (int pageIndex = 0; pageIndex < livePageCount; pageIndex++)
                pages[pageIndex] = GetPageInfo(pageIndex);

            return new VividParticlePageGroup(pages);
        }

        public void Dispose()
        {
            m_Common.Dispose();
            DisposePageEntryCounts();
            m_MaxParticles = 0;
            m_ActiveCount = 0;
        }

        private void RebuildPageEntryCounts()
        {
            if (!m_PageEntryCounts.IsCreated)
                return;

            for (int pageIndex = 0; pageIndex < m_PageEntryCounts.Length; pageIndex++)
            {
                int start = pageIndex * VividParticleEcsConstants.PageEntryCount;
                m_PageEntryCounts[pageIndex] = math.clamp(
                    m_ActiveCount - start,
                    0,
                    VividParticleEcsConstants.PageEntryCount);
            }
        }

        private void SetPageEntryCountForIndex(int index)
        {
            int pageIndex = index / VividParticleEcsConstants.PageEntryCount;
            if ((uint)pageIndex >= (uint)m_PageEntryCounts.Length)
                return;

            int pageStart = pageIndex * VividParticleEcsConstants.PageEntryCount;
            m_PageEntryCounts[pageIndex] = math.clamp(
                m_ActiveCount - pageStart,
                0,
                VividParticleEcsConstants.PageEntryCount);
        }

        private void DisposePageEntryCounts()
        {
            if (m_PageEntryCounts.IsCreated)
                m_PageEntryCounts.Dispose();

            m_PageEntryCounts = default;
        }
    }

    internal struct VividParticlePageGroup : IDisposable
    {
        private NativeArray<VividParticlePageInfo> m_Pages;

        public VividParticlePageGroup(NativeArray<VividParticlePageInfo> pages)
        {
            m_Pages = pages;
        }

        public NativeArray<VividParticlePageInfo> pages => m_Pages;

        public int pageCount => m_Pages.IsCreated ? m_Pages.Length : 0;

        public VividParticlePageInfo this[int index] => m_Pages[index];

        public void Dispose()
        {
            if (m_Pages.IsCreated)
                m_Pages.Dispose();

            m_Pages = default;
        }
    }
}
