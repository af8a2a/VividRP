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

    internal readonly struct VividEcsTileRange : IEquatable<VividEcsTileRange>
    {
        public static readonly VividEcsTileRange Invalid = new(-1, 0);

        public VividEcsTileRange(int startTile, int tileCount)
        {
            StartTile = startTile;
            TileCount = tileCount;
        }

        public int StartTile { get; }

        public int TileCount { get; }

        public int StartEntry => StartTile * VividEcsConstants.PageEntryCount;

        public int EntryCapacity => TileCount * VividEcsConstants.PageEntryCount;

        public bool IsValid => StartTile >= 0 && TileCount > 0;

        public bool Equals(VividEcsTileRange other)
        {
            return StartTile == other.StartTile && TileCount == other.TileCount;
        }

        public override bool Equals(object obj)
        {
            return obj is VividEcsTileRange other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (StartTile * 397) ^ TileCount;
            }
        }
    }

    internal sealed class VividEcsTileAllocator
    {
        private readonly List<VividEcsTileRange> m_FreeRanges = new();
        private int m_NextTile;
        private int m_LiveTileCount;

        public int liveTileCount => m_LiveTileCount;

        public int highWatermarkTileCount => m_NextTile;

        public int freeRangeCount => m_FreeRanges.Count;

        public VividEcsTileRange AllocateEntries(int entryCount)
        {
            int tileCount = Math.Max(
                1,
                (Math.Max(1, entryCount) + VividEcsConstants.PageEntryCount - 1) / VividEcsConstants.PageEntryCount);
            return AllocateTiles(tileCount);
        }

        public VividEcsTileRange AllocateTiles(int tileCount)
        {
            tileCount = Math.Max(1, tileCount);
            for (int index = 0; index < m_FreeRanges.Count; index++)
            {
                VividEcsTileRange range = m_FreeRanges[index];
                if (range.TileCount < tileCount)
                    continue;

                var allocated = new VividEcsTileRange(range.StartTile, tileCount);
                int remainingCount = range.TileCount - tileCount;
                if (remainingCount > 0)
                    m_FreeRanges[index] = new VividEcsTileRange(range.StartTile + tileCount, remainingCount);
                else
                    m_FreeRanges.RemoveAt(index);

                m_LiveTileCount += tileCount;
                return allocated;
            }

            var newRange = new VividEcsTileRange(m_NextTile, tileCount);
            m_NextTile += tileCount;
            m_LiveTileCount += tileCount;
            return newRange;
        }

        public void Free(VividEcsTileRange range)
        {
            if (!range.IsValid)
                return;

            m_LiveTileCount = Math.Max(0, m_LiveTileCount - range.TileCount);
            m_FreeRanges.Add(range);
            m_FreeRanges.Sort((left, right) => left.StartTile.CompareTo(right.StartTile));
            MergeFreeRanges();
        }

        public void Clear()
        {
            m_FreeRanges.Clear();
            m_NextTile = 0;
            m_LiveTileCount = 0;
        }

        private void MergeFreeRanges()
        {
            for (int index = 0; index < m_FreeRanges.Count - 1;)
            {
                VividEcsTileRange current = m_FreeRanges[index];
                VividEcsTileRange next = m_FreeRanges[index + 1];
                int currentEnd = current.StartTile + current.TileCount;
                if (currentEnd < next.StartTile)
                {
                    index++;
                    continue;
                }

                int mergedEnd = Math.Max(currentEnd, next.StartTile + next.TileCount);
                m_FreeRanges[index] = new VividEcsTileRange(current.StartTile, mergedEnd - current.StartTile);
                m_FreeRanges.RemoveAt(index + 1);
            }
        }
    }

    internal readonly struct VividEcsSharedComponentKey : IEquatable<VividEcsSharedComponentKey>
    {
        private readonly VividEcsTypeIndex m_Type0;
        private readonly object m_Value0;
        private readonly VividEcsTypeIndex[] m_Types;
        private readonly object[] m_Values;

        public VividEcsSharedComponentKey(VividEcsTypeIndex type, object value)
        {
            if (!type.IsValid)
            {
                m_Type0 = default;
                m_Value0 = null;
                m_Types = null;
                m_Values = null;
                Count = 0;
                Hash = 0;
                return;
            }

            m_Type0 = type;
            m_Value0 = value;
            m_Types = null;
            m_Values = null;
            Count = 1;
            Hash = ComputeHash(type, value);
        }

        public VividEcsSharedComponentKey(VividEcsTypeIndex[] types, object[] values)
        {
            int count = Math.Min(types?.Length ?? 0, values?.Length ?? 0);
            if (count == 0)
            {
                m_Type0 = default;
                m_Value0 = null;
                m_Types = null;
                m_Values = null;
                Count = 0;
                Hash = 0;
                return;
            }

            if (count == 1)
            {
                m_Type0 = types[0];
                m_Value0 = values[0];
                m_Types = null;
                m_Values = null;
                Count = 1;
                Hash = ComputeHash(m_Type0, m_Value0);
                return;
            }

            m_Type0 = default;
            m_Value0 = null;
            m_Types = types;
            m_Values = values;
            Count = count;
            Hash = ComputeHash(types, values, count);
        }

        public int Count { get; }

        public int Hash { get; }

        public bool Equals(VividEcsSharedComponentKey other)
        {
            if (Count != other.Count)
                return false;

            if (Count == 0)
                return true;

            if (Hash != other.Hash)
                return false;

            for (int index = 0; index < Count; index++)
            {
                if (GetTypeAt(index) != other.GetTypeAt(index)
                    || !Equals(GetValueAt(index), other.GetValueAt(index)))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is VividEcsSharedComponentKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Count == 0 ? 0 : Hash;
        }

        private VividEcsTypeIndex GetTypeAt(int index)
        {
            if (Count == 1)
                return m_Type0;

            return m_Types[index];
        }

        private object GetValueAt(int index)
        {
            if (Count == 1)
                return m_Value0;

            return m_Values[index];
        }

        private static int ComputeHash(VividEcsTypeIndex type, object value)
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 397) ^ type.GetHashCode();
                hash = (hash * 397) ^ (value?.GetHashCode() ?? 0);
                return hash;
            }
        }

        private static int ComputeHash(VividEcsTypeIndex[] types, object[] values, int count)
        {
            if (count == 0)
                return 0;

            if (count == 1)
                return ComputeHash(types[0], values[0]);

            unchecked
            {
                int hash = 17;
                for (int index = 0; index < count; index++)
                {
                    hash = (hash * 397) ^ types[index].GetHashCode();
                    hash = (hash * 397) ^ (values[index]?.GetHashCode() ?? 0);
                }

                return hash;
            }
        }
    }

    internal sealed class VividEcsArchetypeLineGroup
    {
        private readonly List<VividEcsArchetypeLine> m_Lines;

        public VividEcsArchetypeLineGroup(
            VividEcsSharedComponentKey sharedKey,
            List<VividEcsArchetypeLine> lines)
        {
            SharedKey = sharedKey;
            m_Lines = lines ?? new List<VividEcsArchetypeLine>();
        }

        public VividEcsSharedComponentKey SharedKey { get; }

        public IReadOnlyList<VividEcsArchetypeLine> lines => m_Lines;

        public int lineCount => m_Lines.Count;

        internal void Clear()
        {
            m_Lines.Clear();
        }

        internal void AddLine(VividEcsArchetypeLine line)
        {
            if (line != null)
                m_Lines.Add(line);
        }

        public int activeCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < m_Lines.Count; index++)
                    count += m_Lines[index].activeCount;

                return count;
            }
        }

        public VividEcsPageGroup CreatePageGroup(Allocator allocator)
        {
            int pageCount = 0;
            for (int lineIndex = 0; lineIndex < m_Lines.Count; lineIndex++)
            {
                VividEcsArchetypeLine line = m_Lines[lineIndex];
                if (line != null)
                    pageCount += GetLivePageCount(line.activeCount);
            }

            var pages = new NativeArray<VividEcsPageInfo>(pageCount, allocator, NativeArrayOptions.UninitializedMemory);
            int writeIndex = 0;
            for (int lineIndex = 0; lineIndex < m_Lines.Count; lineIndex++)
            {
                VividEcsArchetypeLine line = m_Lines[lineIndex];
                int linePageCount = line != null ? GetLivePageCount(line.activeCount) : 0;
                for (int pageIndex = 0; pageIndex < linePageCount; pageIndex++)
                    pages[writeIndex++] = line.GetPageInfo(pageIndex);
            }

            return new VividEcsPageGroup(pages);
        }

        private static int GetLivePageCount(int activeCount)
        {
            return activeCount <= 0
                ? 0
                : (activeCount + VividEcsConstants.PageEntryCount - 1) / VividEcsConstants.PageEntryCount;
        }
    }

    internal sealed class VividEcsArchetypeLine : IDisposable
    {
        private readonly Dictionary<VividEcsTypeIndex, IVividEcsColumn> m_Columns = new();
        private readonly Dictionary<VividEcsTypeIndex, object> m_SharedComponents = new();
        private readonly List<VividEcsTypeIndex> m_Types = new();
        private readonly List<int> m_EntityIds = new();
        private readonly VividEcsTileAllocator m_TileAllocator;
        private readonly Action<VividEcsTypeIndex, bool> m_OnQueryDataChanged;
        private NativeArray<int> m_PageEntryCounts;
        private VividEcsTileRange m_TileRange = VividEcsTileRange.Invalid;
        private int m_MaxEntries;
        private int m_ActiveCount;

        public VividEcsArchetypeLine(int archetypeLineId, params VividEcsTypeIndex[] componentTypes)
            : this(archetypeLineId, null, componentTypes)
        {
        }

        public VividEcsArchetypeLine(
            int archetypeLineId,
            VividEcsTileAllocator tileAllocator,
            params VividEcsTypeIndex[] componentTypes)
            : this(archetypeLineId, tileAllocator, null, componentTypes)
        {
        }

        internal VividEcsArchetypeLine(
            int archetypeLineId,
            VividEcsTileAllocator tileAllocator,
            Action<VividEcsTypeIndex, bool> onQueryDataChanged,
            params VividEcsTypeIndex[] componentTypes)
        {
            ArchetypeLineId = archetypeLineId;
            m_TileAllocator = tileAllocator;
            AddComponentTypes(componentTypes);
            m_OnQueryDataChanged = onQueryDataChanged;
        }

        public int ArchetypeLineId { get; }

        public bool isCreated => m_PageEntryCounts.IsCreated;

        public int pageCount => m_PageEntryCounts.IsCreated ? m_PageEntryCounts.Length : 0;

        public int capacity => pageCount * VividEcsConstants.PageEntryCount;

        public VividEcsTileRange tileRange => m_TileRange;

        public int maxEntries => m_MaxEntries;

        public int activeCount => math.clamp(m_ActiveCount, 0, math.min(capacity, m_MaxEntries));

        public IReadOnlyList<VividEcsTypeIndex> types => m_Types;

        public int sharedComponentCount => m_SharedComponents.Count;

        public void EnsureCapacity(int maxEntries)
        {
            int requestedMaxEntries = math.max(1, maxEntries);
            if (m_EntityIds.Capacity < requestedMaxEntries)
                m_EntityIds.Capacity = requestedMaxEntries;

            int requestedCapacity = VividEcsConstants.AlignToPage(requestedMaxEntries);
            int requestedPageCount = requestedCapacity / VividEcsConstants.PageEntryCount;
            if (pageCount == requestedPageCount)
            {
                m_MaxEntries = requestedMaxEntries;
                SetActiveCount(math.min(activeCount, m_MaxEntries));
                return;
            }

            VividEcsTileRange oldRange = m_TileRange;
            VividEcsTileRange newRange = m_TileAllocator != null
                ? m_TileAllocator.AllocateTiles(requestedPageCount)
                : new VividEcsTileRange(0, requestedPageCount);

            foreach (IVividEcsColumn column in m_Columns.Values)
                column.EnsureCapacity(requestedCapacity);

            var newPageEntryCounts = new NativeArray<int>(
                requestedPageCount,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            DisposePageEntryCounts();
            m_PageEntryCounts = newPageEntryCounts;
            m_TileRange = newRange;
            if (m_TileAllocator != null)
                m_TileAllocator.Free(oldRange);

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

            bool changed = false;
            for (int index = 0; index < componentTypes.Length; index++)
            {
                VividEcsTypeIndex typeIndex = componentTypes[index];
                if (!typeIndex.IsValid || m_Types.Contains(typeIndex))
                    continue;

                changed = true;
                m_Types.Add(typeIndex);
                VividEcsTypeInfo typeInfo = VividEcsTypeManager.GetTypeInfo(typeIndex);
                if (typeInfo.IsShared || typeInfo.IsTag)
                    continue;

                IVividEcsColumn column = CreateColumn(typeInfo);
                column.EnsureCapacity(capacity);
                m_Columns.Add(typeIndex, column);
            }

            m_Types.Sort();
            if (changed)
                m_OnQueryDataChanged?.Invoke(VividEcsTypeIndex.Invalid, false);
        }

        public void RemoveComponentTypes(params VividEcsTypeIndex[] componentTypes)
        {
            if (componentTypes == null)
                return;

            bool changed = false;
            for (int index = 0; index < componentTypes.Length; index++)
            {
                VividEcsTypeIndex typeIndex = componentTypes[index];
                if (!m_Types.Remove(typeIndex))
                    continue;

                changed = true;
                if (m_SharedComponents.Remove(typeIndex))
                    m_OnQueryDataChanged?.Invoke(typeIndex, true);
                if (m_Columns.TryGetValue(typeIndex, out IVividEcsColumn column))
                {
                    column.Dispose();
                    m_Columns.Remove(typeIndex);
                }
            }

            if (changed)
                m_OnQueryDataChanged?.Invoke(VividEcsTypeIndex.Invalid, false);
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

            bool addedType = !m_Types.Contains(typeIndex);
            bool valueChanged = !m_SharedComponents.TryGetValue(typeIndex, out object previousValue)
                || !Equals(previousValue, value);
            if (!addedType && !valueChanged)
                return;

            if (addedType)
                m_Types.Add(typeIndex);

            m_SharedComponents[typeIndex] = value;
            m_Types.Sort();
            if (addedType)
                m_OnQueryDataChanged?.Invoke(VividEcsTypeIndex.Invalid, false);
            m_OnQueryDataChanged?.Invoke(typeIndex, true);
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

        public bool TryGetSharedComponentBoxed(VividEcsTypeIndex typeIndex, out object value)
        {
            if (typeIndex.IsValid && m_SharedComponents.TryGetValue(typeIndex, out value))
                return true;

            value = null;
            return false;
        }

        public VividEcsSharedComponentKey GetSharedComponentKey()
        {
            if (m_SharedComponents.Count == 0)
                return default;

            if (m_SharedComponents.Count == 1)
            {
                foreach (KeyValuePair<VividEcsTypeIndex, object> pair in m_SharedComponents)
                    return new VividEcsSharedComponentKey(pair.Key, pair.Value);
            }

            var types = new VividEcsTypeIndex[m_SharedComponents.Count];
            var values = new object[m_SharedComponents.Count];
            int writeIndex = 0;
            for (int typeIndex = 0; typeIndex < m_Types.Count; typeIndex++)
            {
                VividEcsTypeIndex type = m_Types[typeIndex];
                if (!m_SharedComponents.TryGetValue(type, out object value))
                    continue;

                types[writeIndex] = type;
                values[writeIndex] = value;
                writeIndex++;
            }

            if (writeIndex == 0)
                return default;

            if (writeIndex == 1)
                return new VividEcsSharedComponentKey(types[0], values[0]);

            if (writeIndex == types.Length)
                return new VividEcsSharedComponentKey(types, values);

            Array.Resize(ref types, writeIndex);
            Array.Resize(ref values, writeIndex);
            return new VividEcsSharedComponentKey(types, values);
        }

        public VividEcsSharedComponentKey GetSharedComponentKey(params VividEcsTypeIndex[] sharedComponentTypes)
        {
            if (sharedComponentTypes == null || sharedComponentTypes.Length == 0)
                return GetSharedComponentKey();

            if (sharedComponentTypes.Length == 1)
                return GetSharedComponentKey(sharedComponentTypes[0]);

            var types = new VividEcsTypeIndex[sharedComponentTypes.Length];
            var values = new object[sharedComponentTypes.Length];
            int writeIndex = 0;
            for (int index = 0; index < sharedComponentTypes.Length; index++)
            {
                VividEcsTypeIndex type = sharedComponentTypes[index];
                if (!type.IsValid || !m_SharedComponents.TryGetValue(type, out object value))
                    continue;

                types[writeIndex] = type;
                values[writeIndex] = value;
                writeIndex++;
            }

            if (writeIndex == 0)
                return default;

            if (writeIndex == 1)
                return new VividEcsSharedComponentKey(types[0], values[0]);

            if (writeIndex == types.Length)
                return new VividEcsSharedComponentKey(types, values);

            Array.Resize(ref types, writeIndex);
            Array.Resize(ref values, writeIndex);
            return new VividEcsSharedComponentKey(types, values);
        }

        public VividEcsSharedComponentKey GetSharedComponentKey(VividEcsTypeIndex sharedComponentType)
        {
            return sharedComponentType.IsValid
                && m_SharedComponents.TryGetValue(sharedComponentType, out object value)
                    ? new VividEcsSharedComponentKey(sharedComponentType, value)
                    : default;
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

        public int AppendRange(int requestedCount, out int startIndex, int entityId = -1)
        {
            startIndex = -1;
            if (!isCreated || requestedCount <= 0)
                return 0;

            int count = math.min(requestedCount, m_MaxEntries - activeCount);
            if (count <= 0)
                return 0;

            startIndex = m_ActiveCount;
            int endIndex = startIndex + count;
            while (m_EntityIds.Count < endIndex)
                m_EntityIds.Add(entityId);

            for (int index = startIndex; index < endIndex; index++)
                m_EntityIds[index] = entityId;

            m_ActiveCount = endIndex;
            RebuildPageEntryCounts();
            return count;
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
            if (m_TileAllocator != null)
                m_TileAllocator.Free(m_TileRange);

            m_TileRange = VividEcsTileRange.Invalid;
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
