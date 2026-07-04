using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace VividRP.Runtime.ECS
{
    internal readonly struct VividEcsPage : IEquatable<VividEcsPage>
    {
        public static readonly VividEcsPage Invalid = new(-1, -1);

        public VividEcsPage(int archetypeLineId, int index)
        {
            ArchetypeLineId = archetypeLineId;
            Index = index;
        }

        public int ArchetypeLineId { get; }

        public int Index { get; }

        public bool IsValid => ArchetypeLineId >= 0 && Index >= 0;

        public bool Equals(VividEcsPage other)
        {
            return ArchetypeLineId == other.ArchetypeLineId && Index == other.Index;
        }

        public override bool Equals(object obj)
        {
            return obj is VividEcsPage other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (ArchetypeLineId * 397) ^ Index;
            }
        }
    }

    internal readonly struct VividEcsPageInfo
    {
        public VividEcsPageInfo(int archetypeLineId, int pageIndex, int startIndex, int entryCount)
            : this(archetypeLineId, pageIndex, startIndex, entryCount, VividEcsConstants.PageEntryCount)
        {
        }

        public VividEcsPageInfo(int archetypeLineId, int pageIndex, int startIndex, int entryCount, int reserveCount)
        {
            if (pageIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(pageIndex));

            if (startIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(startIndex));

            if (entryCount < 0 || entryCount > VividEcsConstants.PageEntryCount)
                throw new ArgumentOutOfRangeException(nameof(entryCount));

            if (reserveCount < entryCount || reserveCount > VividEcsConstants.PageEntryCount)
                throw new ArgumentOutOfRangeException(nameof(reserveCount));

            Page = new VividEcsPage(archetypeLineId, pageIndex);
            ArchetypeLineId = archetypeLineId;
            PageIndex = pageIndex;
            StartIndex = startIndex;
            EntryCount = entryCount;
            ReserveCount = reserveCount;
            Capacity = VividEcsConstants.PageEntryCount;
        }

        public VividEcsPage Page { get; }

        public int ArchetypeLineId { get; }

        public int PageIndex { get; }

        public int StartIndex { get; }

        public int EntryCount { get; }

        public int ReserveCount { get; }

        public int Capacity { get; }
    }

    internal struct VividEcsPageGroup : IDisposable
    {
        private NativeArray<VividEcsPageInfo> m_Pages;

        public VividEcsPageGroup(NativeArray<VividEcsPageInfo> pages)
        {
            m_Pages = pages;
        }

        public NativeArray<VividEcsPageInfo> pages => m_Pages;

        public int pageCount => m_Pages.IsCreated ? m_Pages.Length : 0;

        public VividEcsPageInfo this[int index] => m_Pages[index];

        public void Dispose()
        {
            if (m_Pages.IsCreated)
                m_Pages.Dispose();

            m_Pages = default;
        }
    }

    internal sealed class VividEcsArchetypeLine : IDisposable
    {
        private readonly Dictionary<VividEcsTypeIndex, IVividEcsColumn> m_Columns = new();
        private readonly Dictionary<VividEcsTypeIndex, object> m_SharedComponents = new();
        private readonly List<VividEcsTypeIndex> m_Types = new();
        private readonly List<int> m_EntityIds = new();
        private NativeArray<int> m_PageEntryCounts;
        private int m_MaxEntries;
        private int m_ActiveCount;

        public VividEcsArchetypeLine(int archetypeLineId, params VividEcsTypeIndex[] componentTypes)
        {
            ArchetypeLineId = archetypeLineId;
            AddComponentTypes(componentTypes);
        }

        public int ArchetypeLineId { get; }

        public bool isCreated => m_PageEntryCounts.IsCreated;

        public int pageCount => m_PageEntryCounts.IsCreated ? m_PageEntryCounts.Length : 0;

        public int capacity => pageCount * VividEcsConstants.PageEntryCount;

        public int maxEntries => m_MaxEntries;

        public int activeCount => math.clamp(m_ActiveCount, 0, math.min(capacity, m_MaxEntries));

        public IReadOnlyList<VividEcsTypeIndex> types => m_Types;

        public void EnsureCapacity(int maxEntries)
        {
            int requestedMaxEntries = math.max(1, maxEntries);
            int requestedCapacity = VividEcsConstants.AlignToPage(requestedMaxEntries);
            int requestedPageCount = requestedCapacity / VividEcsConstants.PageEntryCount;
            if (pageCount == requestedPageCount)
            {
                m_MaxEntries = requestedMaxEntries;
                SetActiveCount(math.min(activeCount, m_MaxEntries));
                return;
            }

            foreach (IVividEcsColumn column in m_Columns.Values)
                column.EnsureCapacity(requestedCapacity);

            var newPageEntryCounts = new NativeArray<int>(
                requestedPageCount,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            DisposePageEntryCounts();
            m_PageEntryCounts = newPageEntryCounts;
            m_MaxEntries = requestedMaxEntries;
            SetActiveCount(math.min(activeCount, m_MaxEntries));
        }

        public bool Contains(VividEcsTypeIndex typeIndex)
        {
            return m_Types.Contains(typeIndex);
        }

        public void AddComponentTypes(params VividEcsTypeIndex[] componentTypes)
        {
            if (componentTypes == null)
                return;

            for (int index = 0; index < componentTypes.Length; index++)
            {
                VividEcsTypeIndex typeIndex = componentTypes[index];
                if (!typeIndex.IsValid || m_Types.Contains(typeIndex))
                    continue;

                m_Types.Add(typeIndex);
                VividEcsTypeInfo typeInfo = VividEcsTypeManager.GetTypeInfo(typeIndex);
                if (typeInfo.IsShared || typeInfo.IsTag)
                    continue;

                IVividEcsColumn column = CreateColumn(typeInfo);
                column.EnsureCapacity(capacity);
                m_Columns.Add(typeIndex, column);
            }

            m_Types.Sort();
        }

        public void RemoveComponentTypes(params VividEcsTypeIndex[] componentTypes)
        {
            if (componentTypes == null)
                return;

            for (int index = 0; index < componentTypes.Length; index++)
            {
                VividEcsTypeIndex typeIndex = componentTypes[index];
                m_Types.Remove(typeIndex);
                m_SharedComponents.Remove(typeIndex);
                if (m_Columns.TryGetValue(typeIndex, out IVividEcsColumn column))
                {
                    column.Dispose();
                    m_Columns.Remove(typeIndex);
                }
            }
        }

        public TColumn GetColumn<TColumn>(VividEcsTypeIndex typeIndex)
            where TColumn : class, IVividEcsColumn
        {
            if (!m_Columns.TryGetValue(typeIndex, out IVividEcsColumn column))
                throw new InvalidOperationException($"Column {typeIndex} does not exist in archetype line {ArchetypeLineId}.");

            return column as TColumn
                ?? throw new InvalidOperationException($"Column {typeIndex} is not a {typeof(TColumn).Name}.");
        }

        public void SetSharedComponent<T>(T value)
            where T : struct, IVividEcsSharedComponentData
        {
            VividEcsTypeIndex typeIndex = VividEcsTypeManager.GetTypeIndex<T>();
            if (!typeIndex.IsValid)
                typeIndex = VividEcsTypeManager.RegisterShared<T>();

            if (!m_Types.Contains(typeIndex))
                m_Types.Add(typeIndex);

            m_SharedComponents[typeIndex] = value;
        }

        public bool TryGetSharedComponent<T>(out T value)
            where T : struct, IVividEcsSharedComponentData
        {
            VividEcsTypeIndex typeIndex = VividEcsTypeManager.GetTypeIndex<T>();
            if (typeIndex.IsValid && m_SharedComponents.TryGetValue(typeIndex, out object boxed) && boxed is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default;
            return false;
        }

        public void Clear()
        {
            m_ActiveCount = 0;
            m_EntityIds.Clear();
            RebuildPageEntryCounts();
        }

        public bool Append(out int index, int entityId = -1)
        {
            index = -1;
            if (!isCreated || activeCount >= m_MaxEntries)
                return false;

            index = m_ActiveCount++;
            if (index < m_EntityIds.Count)
                m_EntityIds[index] = entityId;
            else
                m_EntityIds.Add(entityId);

            SetPageEntryCountForIndex(index);
            return true;
        }

        public bool RemoveAtSwapBack(int index, out int movedEntityId, out int movedFromIndex)
        {
            movedEntityId = -1;
            movedFromIndex = -1;
            if ((uint)index >= (uint)activeCount)
                return false;

            int lastIndex = activeCount - 1;
            if (index != lastIndex)
            {
                foreach (IVividEcsColumn column in m_Columns.Values)
                    column.CopyEntry(lastIndex, index);

                movedFromIndex = lastIndex;
                movedEntityId = m_EntityIds[lastIndex];
                m_EntityIds[index] = movedEntityId;
            }

            foreach (IVividEcsColumn column in m_Columns.Values)
                column.ClearEntry(lastIndex);

            if (lastIndex < m_EntityIds.Count)
                m_EntityIds.RemoveAt(lastIndex);

            m_ActiveCount = lastIndex;
            RebuildPageEntryCounts();
            return true;
        }

        public int RemoveEntriesByPageKeepMask(int pageIndex, NativeArray<byte> keepMask)
        {
            if ((uint)pageIndex >= (uint)pageCount)
                throw new ArgumentOutOfRangeException(nameof(pageIndex));

            if (!keepMask.IsCreated)
                throw new ArgumentException("Keep mask must be created.", nameof(keepMask));

            int pageStart = pageIndex * VividEcsConstants.PageEntryCount;
            int pageEnd = math.min(pageStart + m_PageEntryCounts[pageIndex], activeCount);
            int removedCount = 0;
            for (int index = pageStart; index < pageEnd;)
            {
                int localIndex = index - pageStart;
                bool keep = localIndex < keepMask.Length && keepMask[localIndex] != 0;
                if (keep)
                {
                    index++;
                    continue;
                }

                RemoveAtSwapBack(index, out _, out _);
                removedCount++;
                pageEnd = math.min(pageStart + m_PageEntryCounts[pageIndex], activeCount);
            }

            return removedCount;
        }

        public void SetActiveCount(int count)
        {
            m_ActiveCount = math.clamp(count, 0, math.min(capacity, m_MaxEntries));
            while (m_EntityIds.Count > m_ActiveCount)
                m_EntityIds.RemoveAt(m_EntityIds.Count - 1);

            while (m_EntityIds.Count < m_ActiveCount)
                m_EntityIds.Add(-1);

            RebuildPageEntryCounts();
        }

        public int GetEntityId(int index)
        {
            return (uint)index < (uint)m_EntityIds.Count ? m_EntityIds[index] : -1;
        }

        public VividEcsPageInfo GetPageInfo(int pageIndex)
        {
            if ((uint)pageIndex >= (uint)pageCount)
                throw new ArgumentOutOfRangeException(nameof(pageIndex));

            return new VividEcsPageInfo(
                ArchetypeLineId,
                pageIndex,
                pageIndex * VividEcsConstants.PageEntryCount,
                m_PageEntryCounts[pageIndex]);
        }

        public VividEcsPageGroup CreatePageGroup(Allocator allocator)
        {
            int livePageCount = activeCount <= 0
                ? 0
                : (activeCount + VividEcsConstants.PageEntryCount - 1) / VividEcsConstants.PageEntryCount;

            var pages = new NativeArray<VividEcsPageInfo>(livePageCount, allocator, NativeArrayOptions.UninitializedMemory);
            for (int pageIndex = 0; pageIndex < livePageCount; pageIndex++)
                pages[pageIndex] = GetPageInfo(pageIndex);

            return new VividEcsPageGroup(pages);
        }

        public void Dispose()
        {
            foreach (IVividEcsColumn column in m_Columns.Values)
                column.Dispose();

            m_Columns.Clear();
            m_SharedComponents.Clear();
            m_Types.Clear();
            m_EntityIds.Clear();
            DisposePageEntryCounts();
            m_MaxEntries = 0;
            m_ActiveCount = 0;
        }

        private void RebuildPageEntryCounts()
        {
            if (!m_PageEntryCounts.IsCreated)
                return;

            for (int pageIndex = 0; pageIndex < m_PageEntryCounts.Length; pageIndex++)
            {
                int start = pageIndex * VividEcsConstants.PageEntryCount;
                m_PageEntryCounts[pageIndex] = math.clamp(
                    m_ActiveCount - start,
                    0,
                    VividEcsConstants.PageEntryCount);
            }
        }

        private void SetPageEntryCountForIndex(int index)
        {
            int pageIndex = index / VividEcsConstants.PageEntryCount;
            if ((uint)pageIndex >= (uint)m_PageEntryCounts.Length)
                return;

            int pageStart = pageIndex * VividEcsConstants.PageEntryCount;
            m_PageEntryCounts[pageIndex] = math.clamp(
                m_ActiveCount - pageStart,
                0,
                VividEcsConstants.PageEntryCount);
        }

        private void DisposePageEntryCounts()
        {
            if (m_PageEntryCounts.IsCreated)
                m_PageEntryCounts.Dispose();

            m_PageEntryCounts = default;
        }

        private static IVividEcsColumn CreateColumn(VividEcsTypeInfo typeInfo)
        {
            Type columnType;
            if (typeInfo.IsSoa)
                columnType = typeof(VividEcsSoaColumn<>).MakeGenericType(typeInfo.ManagedType);
            else if (typeInfo.IsBit)
                columnType = typeof(VividEcsBitColumn<>).MakeGenericType(typeInfo.ManagedType);
            else
                columnType = typeof(VividEcsComponentColumn<>).MakeGenericType(typeInfo.ManagedType);

            return (IVividEcsColumn)Activator.CreateInstance(columnType, typeInfo.TypeIndex);
        }
    }
}
