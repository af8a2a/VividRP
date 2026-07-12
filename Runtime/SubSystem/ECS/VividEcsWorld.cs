using System;
using System.Collections.Generic;
using Unity.Collections;

namespace VividRP.Runtime.ECS
{
    internal readonly struct VividEcsEntity : IEquatable<VividEcsEntity>
    {
        public static readonly VividEcsEntity Invalid = new(-1, -1);

        public VividEcsEntity(int id, int version)
        {
            Id = id;
            Version = version;
        }

        public int Id { get; }

        public int Version { get; }

        public bool IsValid => Id >= 0 && Version >= 0;

        public bool Equals(VividEcsEntity other)
        {
            return Id == other.Id && Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            return obj is VividEcsEntity other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Id * 397) ^ Version;
            }
        }
    }

    internal interface IVividEcsSparseTable
    {
        int version { get; }

        bool Remove(int key);

        void Clear();
    }

    internal sealed class VividEcsSparseTable<T> : IVividEcsSparseTable
    {
        private const int EmptyIndex = -1;

        private readonly List<int> m_Sparse = new();
        private readonly List<int> m_DenseKeys = new();
        private readonly List<T> m_DenseValues = new();
        private int m_Version = 1;

        public int count => m_DenseKeys.Count;

        public int sparseCapacity => m_Sparse.Count;

        public int denseCapacity => m_DenseValues.Capacity;

        public int version => m_Version;

        public bool ContainsKey(int key)
        {
            return TryGetDenseIndex(key, out _);
        }

        public void Set(int key, T value)
        {
            if (key < 0)
                throw new ArgumentOutOfRangeException(nameof(key));

            EnsureSparseCapacity(key + 1);
            int denseIndex = m_Sparse[key];
            if (denseIndex >= 0)
            {
                m_DenseValues[denseIndex] = value;
                IncrementVersion();
                return;
            }

            m_Sparse[key] = m_DenseKeys.Count;
            m_DenseKeys.Add(key);
            m_DenseValues.Add(value);
            IncrementVersion();
        }

        public bool TryGetValue(int key, out T value)
        {
            if (TryGetDenseIndex(key, out int denseIndex))
            {
                value = m_DenseValues[denseIndex];
                return true;
            }

            value = default;
            return false;
        }

        public bool Remove(int key)
        {
            if (!TryGetDenseIndex(key, out int denseIndex))
                return false;

            int lastDenseIndex = m_DenseKeys.Count - 1;
            int lastKey = m_DenseKeys[lastDenseIndex];
            if (denseIndex != lastDenseIndex)
            {
                m_DenseKeys[denseIndex] = lastKey;
                m_DenseValues[denseIndex] = m_DenseValues[lastDenseIndex];
                m_Sparse[lastKey] = denseIndex;
            }

            m_DenseKeys.RemoveAt(lastDenseIndex);
            m_DenseValues.RemoveAt(lastDenseIndex);
            m_Sparse[key] = EmptyIndex;
            IncrementVersion();
            return true;
        }

        public int GetKeyAtDenseIndex(int denseIndex)
        {
            return m_DenseKeys[denseIndex];
        }

        public T GetValueAtDenseIndex(int denseIndex)
        {
            return m_DenseValues[denseIndex];
        }

        public void Clear()
        {
            if (m_DenseKeys.Count == 0)
                return;

            for (int index = 0; index < m_DenseKeys.Count; index++)
                m_Sparse[m_DenseKeys[index]] = EmptyIndex;

            m_DenseKeys.Clear();
            m_DenseValues.Clear();
            IncrementVersion();
        }

        private void IncrementVersion()
        {
            m_Version = m_Version == int.MaxValue ? 1 : m_Version + 1;
        }

        private bool TryGetDenseIndex(int key, out int denseIndex)
        {
            if ((uint)key >= (uint)m_Sparse.Count)
            {
                denseIndex = EmptyIndex;
                return false;
            }

            denseIndex = m_Sparse[key];
            return denseIndex >= 0
                && denseIndex < m_DenseKeys.Count
                && m_DenseKeys[denseIndex] == key;
        }

        private void EnsureSparseCapacity(int capacity)
        {
            while (m_Sparse.Count < capacity)
                m_Sparse.Add(EmptyIndex);
        }
    }

    internal sealed class VividEcsWorld : IDisposable
    {
        private readonly List<VividEcsArchetypeLine> m_Lines = new();
        private readonly VividEcsSparseTable<EntityRecord> m_Entities = new();
        private readonly Dictionary<Type, IVividEcsSparseTable> m_LineAttachments = new();
        private readonly VividEcsTileAllocator m_TileAllocator = new();
        private readonly Dictionary<VividEcsTypeIndex, int> m_SharedComponentVersions = new();
        private int m_NextLineId;
        private int m_NextEntityId;
        private int m_QueryStructureVersion = 1;
        private int m_AnySharedComponentVersion;

        public int archetypeLineCount => m_Lines.Count;

        public int entityCount => m_Entities.count;

        public VividEcsTileAllocator tileAllocator => m_TileAllocator;

        public VividEcsArchetypeLine CreateArchetypeLine(int maxEntries, params VividEcsTypeIndex[] componentTypes)
        {
            var line = new VividEcsArchetypeLine(
                m_NextLineId++,
                m_TileAllocator,
                OnLineQueryDataChanged,
                componentTypes);
            if (maxEntries > 0)
                line.EnsureCapacity(maxEntries);

            m_Lines.Add(line);
            IncrementQueryStructureVersion();
            return line;
        }

        public bool DestroyArchetypeLine(VividEcsArchetypeLine line)
        {
            if (line == null)
                return false;

            int index = m_Lines.IndexOf(line);
            if (index < 0)
                return false;

            for (int denseIndex = m_Entities.count - 1; denseIndex >= 0; denseIndex--)
            {
                EntityRecord record = m_Entities.GetValueAtDenseIndex(denseIndex);
                if (record.Line == line)
                    m_Entities.Remove(m_Entities.GetKeyAtDenseIndex(denseIndex));
            }

            m_Lines.RemoveAt(index);
            RemoveAllLineAttachments(line.ArchetypeLineId);
            line.Dispose();
            IncrementQueryStructureVersion();
            return true;
        }

        public VividEcsEntity CreateEntity(VividEcsArchetypeLine line)
        {
            if (line == null)
                throw new ArgumentNullException(nameof(line));

            int entityId = m_NextEntityId++;
            if (!line.Append(out int index, entityId))
                throw new InvalidOperationException("Archetype line is full.");

            var entity = new VividEcsEntity(entityId, 1);
            m_Entities.Set(entityId, new EntityRecord(entity.Version, line, index));
            return entity;
        }

        public bool DestroyEntity(VividEcsEntity entity)
        {
            if (!TryGetRecord(entity, out EntityRecord record))
                return false;

            if (record.Line.RemoveAtSwapBack(record.Index, out int movedEntityId, out _)
                && movedEntityId >= 0
                && m_Entities.TryGetValue(movedEntityId, out EntityRecord movedRecord))
            {
                movedRecord.Index = record.Index;
                m_Entities.Set(movedEntityId, movedRecord);
            }

            m_Entities.Remove(entity.Id);
            return true;
        }

        public bool Exists(VividEcsEntity entity)
        {
            return TryGetRecord(entity, out _);
        }

        public void AddComponentType(VividEcsArchetypeLine line, VividEcsTypeIndex typeIndex)
        {
            if (line == null)
                throw new ArgumentNullException(nameof(line));

            line.AddComponentTypes(typeIndex);
        }

        public void RemoveComponentType(VividEcsArchetypeLine line, VividEcsTypeIndex typeIndex)
        {
            if (line == null)
                throw new ArgumentNullException(nameof(line));

            line.RemoveComponentTypes(typeIndex);
        }

        public VividEcsQuery CreateQuery()
        {
            return new VividEcsQuery(this);
        }

        public void SetLineAttachment<T>(VividEcsArchetypeLine line, T value)
            where T : struct, IVividEcsLineAttachmentData
        {
            if (line == null)
                throw new ArgumentNullException(nameof(line));

            GetOrCreateLineAttachmentTable<T>().Set(line.ArchetypeLineId, value);
        }

        public bool TryGetLineAttachment<T>(VividEcsArchetypeLine line, out T value)
            where T : struct, IVividEcsLineAttachmentData
        {
            if (line != null
                && TryGetLineAttachmentTable(out VividEcsSparseTable<T> table)
                && table.TryGetValue(line.ArchetypeLineId, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        public bool RemoveLineAttachment<T>(VividEcsArchetypeLine line)
            where T : struct, IVividEcsLineAttachmentData
        {
            return line != null
                && TryGetLineAttachmentTable(out VividEcsSparseTable<T> table)
                && table.Remove(line.ArchetypeLineId);
        }

        public int GetLineAttachmentCount<T>()
            where T : struct, IVividEcsLineAttachmentData
        {
            return TryGetLineAttachmentTable(out VividEcsSparseTable<T> table)
                ? table.count
                : 0;
        }

        public int GetLineAttachmentVersion<T>()
            where T : struct, IVividEcsLineAttachmentData
        {
            return TryGetLineAttachmentTable(out VividEcsSparseTable<T> table)
                ? table.version
                : 0;
        }

        internal bool TryGetLineAttachment<T>(int lineId, out T value)
            where T : struct, IVividEcsLineAttachmentData
        {
            if (lineId >= 0
                && TryGetLineAttachmentTable(out VividEcsSparseTable<T> table)
                && table.TryGetValue(lineId, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        public VividEcsPageGroup CreatePageGroup(VividEcsQuery query, Allocator allocator)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            int pageCount = 0;
            int matchingLineCount = query.PrepareMatchingLines();
            for (int lineIndex = 0; lineIndex < matchingLineCount; lineIndex++)
            {
                VividEcsArchetypeLine line = query.GetMatchingLine(lineIndex);

                if (line != null)
                    pageCount += GetLivePageCount(line.activeCount);
            }

            var pages = new NativeArray<VividEcsPageInfo>(pageCount, allocator, NativeArrayOptions.UninitializedMemory);
            int writeIndex = 0;
            for (int lineIndex = 0; lineIndex < matchingLineCount; lineIndex++)
            {
                VividEcsArchetypeLine line = query.GetMatchingLine(lineIndex);

                int linePageCount = line != null ? GetLivePageCount(line.activeCount) : 0;
                for (int pageIndex = 0; pageIndex < linePageCount; pageIndex++)
                    pages[writeIndex++] = line.GetPageInfo(pageIndex);
            }

            return new VividEcsPageGroup(pages);
        }

        public List<VividEcsArchetypeLineGroup> CreateArchetypeLineGroups(VividEcsQuery query)
        {
            return CreateArchetypeLineGroups(query, Array.Empty<VividEcsTypeIndex>());
        }

        public List<VividEcsArchetypeLineGroup> CreateArchetypeLineGroups(
            VividEcsQuery query,
            params VividEcsTypeIndex[] sharedComponentTypes)
        {
            var result = new List<VividEcsArchetypeLineGroup>();
            CreateArchetypeLineGroups(query, result, sharedComponentTypes);
            return result;
        }

        public List<VividEcsArchetypeLineGroup> CreateArchetypeLineGroups(
            VividEcsQuery query,
            VividEcsTypeIndex sharedComponentType)
        {
            var result = new List<VividEcsArchetypeLineGroup>();
            CreateArchetypeLineGroups(query, result, sharedComponentType);
            return result;
        }

        public void CreateArchetypeLineGroups(
            VividEcsQuery query,
            List<VividEcsArchetypeLineGroup> result,
            params VividEcsTypeIndex[] sharedComponentTypes)
        {
            CreateArchetypeLineGroups(query, result, scratchGroups: null, sharedComponentTypes);
        }

        public void CreateArchetypeLineGroups(
            VividEcsQuery query,
            List<VividEcsArchetypeLineGroup> result,
            VividEcsTypeIndex sharedComponentType)
        {
            CreateArchetypeLineGroups(query, result, scratchGroups: null, sharedComponentType);
        }

        public void CreateArchetypeLineGroups(
            VividEcsQuery query,
            List<VividEcsArchetypeLineGroup> result,
            Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> scratchGroups,
            params VividEcsTypeIndex[] sharedComponentTypes)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            if (result == null)
                throw new ArgumentNullException(nameof(result));

            result.Clear();
            Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> groups =
                scratchGroups ?? new Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>>();
            CreateArchetypeLineGroupMap(query, groups, sharedComponentTypes);

            AddNonEmptyLineGroupsToResult(groups, result);
        }

        public void CreateArchetypeLineGroups(
            VividEcsQuery query,
            List<VividEcsArchetypeLineGroup> result,
            Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> scratchGroups,
            VividEcsTypeIndex sharedComponentType)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            if (result == null)
                throw new ArgumentNullException(nameof(result));

            result.Clear();
            Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> groups =
                scratchGroups ?? new Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>>();
            CreateArchetypeLineGroupMap(query, groups, sharedComponentType);

            AddNonEmptyLineGroupsToResult(groups, result);
        }

        public int CreateArchetypeLineGroupMap(
            VividEcsQuery query,
            Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> groups,
            params VividEcsTypeIndex[] sharedComponentTypes)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            if (groups == null)
                throw new ArgumentNullException(nameof(groups));

            ClearLineGroupMap(groups);

            int matchingLineCount = query.PrepareMatchingLines();
            for (int lineIndex = 0; lineIndex < matchingLineCount; lineIndex++)
            {
                VividEcsArchetypeLine line = query.GetMatchingLine(lineIndex);

                VividEcsSharedComponentKey key =
                    sharedComponentTypes != null && sharedComponentTypes.Length > 0
                        ? line.GetSharedComponentKey(sharedComponentTypes)
                        : line.GetSharedComponentKey();
                AddLineToGroupMap(groups, key, line);
            }

            return CountNonEmptyLineGroups(groups);
        }

        public int CreateArchetypeLineGroupMap(
            VividEcsQuery query,
            Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> groups,
            VividEcsTypeIndex sharedComponentType)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            if (groups == null)
                throw new ArgumentNullException(nameof(groups));

            ClearLineGroupMap(groups);

            int matchingLineCount = query.PrepareMatchingLines();
            for (int lineIndex = 0; lineIndex < matchingLineCount; lineIndex++)
            {
                VividEcsArchetypeLine line = query.GetMatchingLine(lineIndex);

                VividEcsSharedComponentKey key = line.GetSharedComponentKey(sharedComponentType);
                AddLineToGroupMap(groups, key, line);
            }

            return CountNonEmptyLineGroups(groups);
        }

        private static void ClearLineGroupMap(
            Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> groups)
        {
            foreach (List<VividEcsArchetypeLine> lines in groups.Values)
                lines.Clear();
        }

        private static void AddLineToGroupMap(
            Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> groups,
            VividEcsSharedComponentKey key,
            VividEcsArchetypeLine line)
        {
            if (!groups.TryGetValue(key, out List<VividEcsArchetypeLine> lines))
            {
                lines = new List<VividEcsArchetypeLine>();
                groups.Add(key, lines);
            }

            lines.Add(line);
        }

        private static void AddNonEmptyLineGroupsToResult(
            Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> groups,
            List<VividEcsArchetypeLineGroup> result)
        {
            foreach (KeyValuePair<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> pair in groups)
            {
                if (pair.Value.Count > 0)
                    result.Add(new VividEcsArchetypeLineGroup(pair.Key, pair.Value));
            }
        }

        private static int CountNonEmptyLineGroups(
            Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> groups)
        {
            int groupCount = 0;
            foreach (KeyValuePair<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> pair in groups)
            {
                if (pair.Value.Count > 0)
                    groupCount++;
            }

            return groupCount;
        }

        public void Dispose()
        {
            for (int index = 0; index < m_Lines.Count; index++)
                m_Lines[index].Dispose();

            m_Lines.Clear();
            m_Entities.Clear();
            foreach (IVividEcsSparseTable table in m_LineAttachments.Values)
                table.Clear();
            m_LineAttachments.Clear();
            m_TileAllocator.Clear();
            m_SharedComponentVersions.Clear();
            m_NextLineId = 0;
            m_NextEntityId = 0;
            IncrementQueryStructureVersion();
        }

        private VividEcsSparseTable<T> GetOrCreateLineAttachmentTable<T>()
            where T : struct, IVividEcsLineAttachmentData
        {
            Type type = typeof(T);
            if (m_LineAttachments.TryGetValue(type, out IVividEcsSparseTable existing))
                return (VividEcsSparseTable<T>)existing;

            var table = new VividEcsSparseTable<T>();
            m_LineAttachments.Add(type, table);
            return table;
        }

        private bool TryGetLineAttachmentTable<T>(out VividEcsSparseTable<T> table)
            where T : struct, IVividEcsLineAttachmentData
        {
            if (m_LineAttachments.TryGetValue(typeof(T), out IVividEcsSparseTable existing))
            {
                table = (VividEcsSparseTable<T>)existing;
                return true;
            }

            table = null;
            return false;
        }

        private void RemoveAllLineAttachments(int lineId)
        {
            foreach (IVividEcsSparseTable table in m_LineAttachments.Values)
                table.Remove(lineId);
        }

        internal IReadOnlyList<VividEcsArchetypeLine> querySourceLines => m_Lines;

        internal int queryStructureVersion => m_QueryStructureVersion;

        internal int anySharedComponentVersion => m_AnySharedComponentVersion;

        internal int GetSharedComponentVersion(VividEcsTypeIndex typeIndex)
        {
            return typeIndex.IsValid && m_SharedComponentVersions.TryGetValue(typeIndex, out int version)
                ? version
                : 0;
        }

        private void OnLineQueryDataChanged(VividEcsTypeIndex typeIndex, bool sharedValueOnly)
        {
            if (!sharedValueOnly)
            {
                IncrementQueryStructureVersion();
                return;
            }

            m_SharedComponentVersions.TryGetValue(typeIndex, out int version);
            m_SharedComponentVersions[typeIndex] = NextVersion(version);
            m_AnySharedComponentVersion = NextVersion(m_AnySharedComponentVersion);
        }

        private void IncrementQueryStructureVersion()
        {
            m_QueryStructureVersion = NextVersion(m_QueryStructureVersion);
        }

        private static int NextVersion(int version)
        {
            return version == int.MaxValue ? 1 : version + 1;
        }

        private bool TryGetRecord(VividEcsEntity entity, out EntityRecord record)
        {
            if (!entity.IsValid || !m_Entities.TryGetValue(entity.Id, out record) || record.Version != entity.Version)
            {
                record = default;
                return false;
            }

            return true;
        }

        private static int GetLivePageCount(int activeCount)
        {
            return activeCount <= 0
                ? 0
                : (activeCount + VividEcsConstants.PageEntryCount - 1) / VividEcsConstants.PageEntryCount;
        }

        private struct EntityRecord
        {
            public EntityRecord(int version, VividEcsArchetypeLine line, int index)
            {
                Version = version;
                Line = line;
                Index = index;
            }

            public int Version;
            public VividEcsArchetypeLine Line;
            public int Index;
        }
    }

    internal sealed class VividEcsQuery
    {
        private readonly VividEcsWorld m_World;
        private readonly IReadOnlyList<VividEcsArchetypeLine> m_SourceLines;
        private readonly List<VividEcsArchetypeLine> m_MatchingLines = new();
        private readonly List<VividEcsArchetypeLine> m_RebuildMatchingLines = new();
        private readonly List<VividEcsTypeIndex> m_All = new();
        private readonly List<VividEcsTypeIndex> m_Any = new();
        private readonly List<VividEcsTypeIndex> m_None = new();
        private readonly List<SharedFilter> m_SharedFilters = new();
        private readonly List<int> m_CachedSharedFilterVersions = new();
        private int m_DefinitionVersion = 1;
        private int m_CachedDefinitionVersion = -1;
        private int m_CachedStructureVersion = -1;
        private int m_CacheRebuildCount;
        private int m_CacheHitCount;
        private int m_LastSourceScanCount;
        private int m_MatchingLinesRevision;

        public VividEcsQuery(VividEcsWorld world)
        {
            m_World = world ?? throw new ArgumentNullException(nameof(world));
            m_SourceLines = world.querySourceLines;
        }

        public VividEcsQuery WithAll(params VividEcsTypeIndex[] types)
        {
            if (AddRange(m_All, types))
                InvalidateDefinition();
            return this;
        }

        public VividEcsQuery WithAny(params VividEcsTypeIndex[] types)
        {
            if (AddRange(m_Any, types))
                InvalidateDefinition();
            return this;
        }

        public VividEcsQuery WithNone(params VividEcsTypeIndex[] types)
        {
            if (AddRange(m_None, types))
                InvalidateDefinition();
            return this;
        }

        public VividEcsQuery WithShared<T>(T value)
            where T : struct, IVividEcsSharedComponentData
        {
            VividEcsTypeIndex typeIndex = VividEcsTypeManager.GetTypeIndex<T>();
            if (!typeIndex.IsValid)
                typeIndex = VividEcsTypeManager.RegisterShared<T>();

            m_SharedFilters.Add(new SharedFilter(typeIndex, value));
            InvalidateDefinition();
            return this;
        }

        public int MatchingLineCount()
        {
            return PrepareMatchingLines();
        }

        public int MatchingEntriesCount()
        {
            int count = 0;
            int matchingLineCount = PrepareMatchingLines();
            for (int index = 0; index < matchingLineCount; index++)
                count += m_MatchingLines[index].activeCount;

            return count;
        }

        public int cachedMatchingLineCount => m_MatchingLines.Count;

        internal IReadOnlyList<VividEcsArchetypeLine> cachedMatchingLines => m_MatchingLines;

        public int cacheRebuildCount => m_CacheRebuildCount;

        public int cacheHitCount => m_CacheHitCount;

        public int lastSourceScanCount => m_LastSourceScanCount;

        internal int cacheRevision => m_MatchingLinesRevision;

        internal int anySharedComponentVersion => m_World.anySharedComponentVersion;

        internal int GetSharedComponentVersion(VividEcsTypeIndex typeIndex)
        {
            return m_World.GetSharedComponentVersion(typeIndex);
        }

        public IEnumerable<VividEcsArchetypeLine> MatchLines()
        {
            int matchingLineCount = PrepareMatchingLines();
            for (int index = 0; index < matchingLineCount; index++)
                yield return m_MatchingLines[index];
        }

        internal int PrepareMatchingLines()
        {
            if (IsCacheValid())
            {
                m_CacheHitCount++;
                m_LastSourceScanCount = 0;
                return m_MatchingLines.Count;
            }

            m_RebuildMatchingLines.Clear();
            m_LastSourceScanCount = m_SourceLines.Count;
            for (int index = 0; index < m_SourceLines.Count; index++)
            {
                VividEcsArchetypeLine line = m_SourceLines[index];
                if (Matches(line))
                    m_RebuildMatchingLines.Add(line);
            }

            if (!HaveSameLines(m_MatchingLines, m_RebuildMatchingLines))
            {
                m_MatchingLines.Clear();
                m_MatchingLines.AddRange(m_RebuildMatchingLines);
                m_MatchingLinesRevision = m_MatchingLinesRevision == int.MaxValue
                    ? 1
                    : m_MatchingLinesRevision + 1;
            }
            m_RebuildMatchingLines.Clear();

            m_CachedDefinitionVersion = m_DefinitionVersion;
            m_CachedStructureVersion = m_World.queryStructureVersion;
            m_CachedSharedFilterVersions.Clear();
            for (int index = 0; index < m_SharedFilters.Count; index++)
            {
                m_CachedSharedFilterVersions.Add(
                    m_World.GetSharedComponentVersion(m_SharedFilters[index].TypeIndex));
            }

            m_CacheRebuildCount++;
            return m_MatchingLines.Count;
        }

        internal VividEcsArchetypeLine GetMatchingLine(int index)
        {
            return m_MatchingLines[index];
        }

        private bool Matches(VividEcsArchetypeLine line)
        {
            for (int index = 0; index < m_All.Count; index++)
            {
                if (!line.Contains(m_All[index]))
                    return false;
            }

            if (m_Any.Count > 0)
            {
                bool any = false;
                for (int index = 0; index < m_Any.Count; index++)
                    any |= line.Contains(m_Any[index]);

                if (!any)
                    return false;
            }

            for (int index = 0; index < m_None.Count; index++)
            {
                if (line.Contains(m_None[index]))
                    return false;
            }

            for (int index = 0; index < m_SharedFilters.Count; index++)
            {
                SharedFilter filter = m_SharedFilters[index];
                if (!line.TryGetSharedComponentBoxed(filter.TypeIndex, out object value)
                    || !Equals(value, filter.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsCacheValid()
        {
            if (m_CachedDefinitionVersion != m_DefinitionVersion
                || m_CachedStructureVersion != m_World.queryStructureVersion
                || m_CachedSharedFilterVersions.Count != m_SharedFilters.Count)
            {
                return false;
            }

            for (int index = 0; index < m_SharedFilters.Count; index++)
            {
                if (m_CachedSharedFilterVersions[index]
                    != m_World.GetSharedComponentVersion(m_SharedFilters[index].TypeIndex))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HaveSameLines(
            List<VividEcsArchetypeLine> left,
            List<VividEcsArchetypeLine> right)
        {
            if (left.Count != right.Count)
                return false;

            for (int index = 0; index < left.Count; index++)
            {
                if (!ReferenceEquals(left[index], right[index]))
                    return false;
            }

            return true;
        }

        private void InvalidateDefinition()
        {
            m_DefinitionVersion = m_DefinitionVersion == int.MaxValue ? 1 : m_DefinitionVersion + 1;
        }

        private static bool AddRange(List<VividEcsTypeIndex> target, VividEcsTypeIndex[] source)
        {
            if (source == null)
                return false;

            bool changed = false;
            for (int index = 0; index < source.Length; index++)
            {
                VividEcsTypeIndex type = source[index];
                if (type.IsValid && !target.Contains(type))
                {
                    target.Add(type);
                    changed = true;
                }
            }

            return changed;
        }

        private readonly struct SharedFilter
        {
            public SharedFilter(VividEcsTypeIndex typeIndex, object value)
            {
                TypeIndex = typeIndex;
                Value = value;
            }

            public readonly VividEcsTypeIndex TypeIndex;
            public readonly object Value;
        }
    }

    internal sealed class VividEcsArchetypeLineGroupCache
    {
        private readonly VividEcsQuery m_Query;
        private readonly VividEcsTypeIndex[] m_SharedComponentTypes;
        private readonly int[] m_CachedSharedComponentVersions;
        private readonly Dictionary<VividEcsSharedComponentKey, VividEcsArchetypeLineGroup> m_GroupLookup = new();
        private readonly List<VividEcsArchetypeLineGroup> m_ActiveGroups = new();
        private int m_CachedQueryRevision = -1;
        private int m_CachedAnySharedComponentVersion = -1;
        private int m_CacheRebuildCount;
        private int m_CacheHitCount;
        private int m_LastSourceLineScanCount;

        public VividEcsArchetypeLineGroupCache(
            VividEcsQuery query,
            VividEcsTypeIndex sharedComponentType)
            : this(query, new[] { sharedComponentType })
        {
        }

        public VividEcsArchetypeLineGroupCache(
            VividEcsQuery query,
            params VividEcsTypeIndex[] sharedComponentTypes)
        {
            m_Query = query ?? throw new ArgumentNullException(nameof(query));
            m_SharedComponentTypes = CopyValidDistinctTypes(sharedComponentTypes);
            m_CachedSharedComponentVersions = new int[m_SharedComponentTypes.Length];
            for (int index = 0; index < m_CachedSharedComponentVersions.Length; index++)
                m_CachedSharedComponentVersions[index] = -1;
        }

        public IReadOnlyList<VividEcsArchetypeLineGroup> groups => m_ActiveGroups;

        public int groupCount => m_ActiveGroups.Count;

        public int cacheRebuildCount => m_CacheRebuildCount;

        public int cacheHitCount => m_CacheHitCount;

        public int lastSourceLineScanCount => m_LastSourceLineScanCount;

        public bool Matches(VividEcsQuery query, VividEcsTypeIndex sharedComponentType)
        {
            return ReferenceEquals(m_Query, query)
                && m_SharedComponentTypes.Length == 1
                && m_SharedComponentTypes[0] == sharedComponentType;
        }

        public bool Prepare()
        {
            int matchingLineCount = m_Query.PrepareMatchingLines();
            if (IsCacheValid())
            {
                m_CacheHitCount++;
                m_LastSourceLineScanCount = 0;
                return false;
            }

            for (int index = 0; index < m_ActiveGroups.Count; index++)
                m_ActiveGroups[index].Clear();
            m_ActiveGroups.Clear();

            m_LastSourceLineScanCount = matchingLineCount;
            for (int lineIndex = 0; lineIndex < matchingLineCount; lineIndex++)
            {
                VividEcsArchetypeLine line = m_Query.GetMatchingLine(lineIndex);
                VividEcsSharedComponentKey key = GetSharedComponentKey(line);
                if (!m_GroupLookup.TryGetValue(key, out VividEcsArchetypeLineGroup group))
                {
                    group = new VividEcsArchetypeLineGroup(key, null);
                    m_GroupLookup.Add(key, group);
                }

                if (group.lineCount == 0)
                    m_ActiveGroups.Add(group);
                group.AddLine(line);
            }

            m_CachedQueryRevision = m_Query.cacheRevision;
            if (m_SharedComponentTypes.Length == 0)
            {
                m_CachedAnySharedComponentVersion = m_Query.anySharedComponentVersion;
            }
            else
            {
                for (int index = 0; index < m_SharedComponentTypes.Length; index++)
                {
                    m_CachedSharedComponentVersions[index] =
                        m_Query.GetSharedComponentVersion(m_SharedComponentTypes[index]);
                }
            }

            m_CacheRebuildCount++;
            return true;
        }

        private bool IsCacheValid()
        {
            if (m_CachedQueryRevision != m_Query.cacheRevision)
                return false;

            if (m_SharedComponentTypes.Length == 0)
                return m_CachedAnySharedComponentVersion == m_Query.anySharedComponentVersion;

            for (int index = 0; index < m_SharedComponentTypes.Length; index++)
            {
                if (m_CachedSharedComponentVersions[index]
                    != m_Query.GetSharedComponentVersion(m_SharedComponentTypes[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private VividEcsSharedComponentKey GetSharedComponentKey(VividEcsArchetypeLine line)
        {
            if (m_SharedComponentTypes.Length == 0)
                return line.GetSharedComponentKey();

            if (m_SharedComponentTypes.Length == 1)
                return line.GetSharedComponentKey(m_SharedComponentTypes[0]);

            return line.GetSharedComponentKey(m_SharedComponentTypes);
        }

        private static VividEcsTypeIndex[] CopyValidDistinctTypes(VividEcsTypeIndex[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<VividEcsTypeIndex>();

            var result = new List<VividEcsTypeIndex>(source.Length);
            for (int index = 0; index < source.Length; index++)
            {
                VividEcsTypeIndex typeIndex = source[index];
                if (typeIndex.IsValid && !result.Contains(typeIndex))
                    result.Add(typeIndex);
            }

            return result.ToArray();
        }
    }

    internal sealed class VividEcsEntityCommandBuffer : IDisposable
    {
        private readonly List<Action<VividEcsWorld>> m_Commands = new();
        private bool m_Disposed;

        public int commandCount => m_Commands.Count;

        public void CreateEntity(VividEcsArchetypeLine line)
        {
            ThrowIfDisposed();
            m_Commands.Add(world => world.CreateEntity(line));
        }

        public void DestroyEntity(VividEcsEntity entity)
        {
            ThrowIfDisposed();
            m_Commands.Add(world => world.DestroyEntity(entity));
        }

        public void AddComponentType(VividEcsArchetypeLine line, VividEcsTypeIndex typeIndex)
        {
            ThrowIfDisposed();
            m_Commands.Add(world => world.AddComponentType(line, typeIndex));
        }

        public void RemoveComponentType(VividEcsArchetypeLine line, VividEcsTypeIndex typeIndex)
        {
            ThrowIfDisposed();
            m_Commands.Add(world => world.RemoveComponentType(line, typeIndex));
        }

        public void Playback(VividEcsWorld world)
        {
            ThrowIfDisposed();
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            for (int index = 0; index < m_Commands.Count; index++)
                m_Commands[index](world);

            m_Commands.Clear();
        }

        public void Dispose()
        {
            m_Commands.Clear();
            m_Disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(VividEcsEntityCommandBuffer));
        }
    }

    internal readonly struct VividEcsLineGroupAttachment<T>
        where T : unmanaged, IVividEcsLineAttachmentData
    {
        public VividEcsLineGroupAttachment(int lineId, T value)
        {
            LineId = lineId;
            Value = value;
        }

        public int LineId { get; }

        public T Value { get; }
    }

    internal readonly struct VividEcsLineGroupAttachmentRange
    {
        public VividEcsLineGroupAttachmentRange(int groupIndex, int start, int count)
        {
            GroupIndex = groupIndex;
            Start = start;
            Count = count;
        }

        public int GroupIndex { get; }

        public int Start { get; }

        public int Count { get; }
    }

    internal sealed class VividEcsArchetypeLineGroupNativeAttachmentCache<T> : IDisposable
        where T : unmanaged, IVividEcsLineAttachmentData
    {
        private readonly VividEcsWorld m_World;
        private readonly VividEcsArchetypeLineGroupCache m_LineGroupCache;
        private NativeList<VividEcsLineGroupAttachment<T>> m_Attachments;
        private NativeList<VividEcsLineGroupAttachmentRange> m_Ranges;
        private int m_CachedLineGroupBuildCount = -1;
        private int m_CachedAttachmentVersion = -1;
        private int m_CacheRebuildCount;
        private int m_CacheHitCount;
        private int m_LastSourceLineScanCount;

        public VividEcsArchetypeLineGroupNativeAttachmentCache(
            VividEcsWorld world,
            VividEcsArchetypeLineGroupCache lineGroupCache,
            Allocator allocator = Allocator.Persistent)
        {
            m_World = world ?? throw new ArgumentNullException(nameof(world));
            m_LineGroupCache = lineGroupCache
                ?? throw new ArgumentNullException(nameof(lineGroupCache));
            m_Attachments = new NativeList<VividEcsLineGroupAttachment<T>>(allocator);
            m_Ranges = new NativeList<VividEcsLineGroupAttachmentRange>(allocator);
        }

        public NativeArray<VividEcsLineGroupAttachment<T>> attachments =>
            m_Attachments.IsCreated ? m_Attachments.AsArray() : default;

        public NativeArray<VividEcsLineGroupAttachmentRange> ranges =>
            m_Ranges.IsCreated ? m_Ranges.AsArray() : default;

        public int groupCount => m_Ranges.IsCreated ? m_Ranges.Length : 0;

        public int attachmentCount => m_Attachments.IsCreated ? m_Attachments.Length : 0;

        public int cacheRebuildCount => m_CacheRebuildCount;

        public int cacheHitCount => m_CacheHitCount;

        public int lastSourceLineScanCount => m_LastSourceLineScanCount;

        public bool Matches(
            VividEcsWorld world,
            VividEcsArchetypeLineGroupCache lineGroupCache)
        {
            return ReferenceEquals(m_World, world)
                && ReferenceEquals(m_LineGroupCache, lineGroupCache);
        }

        public bool Prepare()
        {
            bool groupsRebuilt = m_LineGroupCache.Prepare();
            return Prepare(groupsRebuilt);
        }

        public bool Prepare(bool groupsRebuilt)
        {
            int attachmentVersion = m_World.GetLineAttachmentVersion<T>();
            if (!groupsRebuilt
                && m_CachedLineGroupBuildCount == m_LineGroupCache.cacheRebuildCount
                && m_CachedAttachmentVersion == attachmentVersion)
            {
                m_CacheHitCount++;
                m_LastSourceLineScanCount = 0;
                return false;
            }

            m_Attachments.Clear();
            m_Ranges.Clear();
            m_LastSourceLineScanCount = 0;
            IReadOnlyList<VividEcsArchetypeLineGroup> groups = m_LineGroupCache.groups;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                VividEcsArchetypeLineGroup group = groups[groupIndex];
                int start = m_Attachments.Length;
                IReadOnlyList<VividEcsArchetypeLine> lines = group?.lines;
                int lineCount = lines?.Count ?? 0;
                m_LastSourceLineScanCount += lineCount;
                for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
                {
                    VividEcsArchetypeLine line = lines[lineIndex];
                    if (line != null && m_World.TryGetLineAttachment(line.ArchetypeLineId, out T value))
                    {
                        m_Attachments.Add(new VividEcsLineGroupAttachment<T>(
                            line.ArchetypeLineId,
                            value));
                    }
                }

                m_Ranges.Add(new VividEcsLineGroupAttachmentRange(
                    groupIndex,
                    start,
                    m_Attachments.Length - start));
            }

            m_CachedLineGroupBuildCount = m_LineGroupCache.cacheRebuildCount;
            m_CachedAttachmentVersion = attachmentVersion;
            m_CacheRebuildCount++;
            return true;
        }

        public void Dispose()
        {
            if (m_Attachments.IsCreated)
                m_Attachments.Dispose();
            if (m_Ranges.IsCreated)
                m_Ranges.Dispose();
            m_Attachments = default;
            m_Ranges = default;
        }
    }
}
