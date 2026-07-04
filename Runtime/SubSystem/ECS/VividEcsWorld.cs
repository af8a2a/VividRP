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

    internal sealed class VividEcsWorld : IDisposable
    {
        private readonly List<VividEcsArchetypeLine> m_Lines = new();
        private readonly Dictionary<int, EntityRecord> m_Entities = new();
        private int m_NextLineId;
        private int m_NextEntityId;

        public int archetypeLineCount => m_Lines.Count;

        public int entityCount => m_Entities.Count;

        public VividEcsArchetypeLine CreateArchetypeLine(int maxEntries, params VividEcsTypeIndex[] componentTypes)
        {
            var line = new VividEcsArchetypeLine(m_NextLineId++, componentTypes);
            line.EnsureCapacity(maxEntries);
            m_Lines.Add(line);
            return line;
        }

        public VividEcsEntity CreateEntity(VividEcsArchetypeLine line)
        {
            if (line == null)
                throw new ArgumentNullException(nameof(line));

            int entityId = m_NextEntityId++;
            if (!line.Append(out int index, entityId))
                throw new InvalidOperationException("Archetype line is full.");

            var entity = new VividEcsEntity(entityId, 1);
            m_Entities.Add(entityId, new EntityRecord(entity.Version, line, index));
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
                m_Entities[movedEntityId] = movedRecord;
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
            return new VividEcsQuery(m_Lines);
        }

        public VividEcsPageGroup CreatePageGroup(VividEcsQuery query, Allocator allocator)
        {
            List<VividEcsPageInfo> pageInfos = new();
            foreach (VividEcsArchetypeLine line in query.MatchLines())
            {
                using VividEcsPageGroup group = line.CreatePageGroup(Allocator.Temp);
                for (int index = 0; index < group.pageCount; index++)
                    pageInfos.Add(group[index]);
            }

            var pages = new NativeArray<VividEcsPageInfo>(pageInfos.Count, allocator, NativeArrayOptions.UninitializedMemory);
            for (int index = 0; index < pageInfos.Count; index++)
                pages[index] = pageInfos[index];

            return new VividEcsPageGroup(pages);
        }

        public void Dispose()
        {
            for (int index = 0; index < m_Lines.Count; index++)
                m_Lines[index].Dispose();

            m_Lines.Clear();
            m_Entities.Clear();
            m_NextLineId = 0;
            m_NextEntityId = 0;
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
        private readonly IReadOnlyList<VividEcsArchetypeLine> m_Lines;
        private readonly List<VividEcsTypeIndex> m_All = new();
        private readonly List<VividEcsTypeIndex> m_Any = new();
        private readonly List<VividEcsTypeIndex> m_None = new();

        public VividEcsQuery(IReadOnlyList<VividEcsArchetypeLine> lines)
        {
            m_Lines = lines ?? throw new ArgumentNullException(nameof(lines));
        }

        public VividEcsQuery WithAll(params VividEcsTypeIndex[] types)
        {
            AddRange(m_All, types);
            return this;
        }

        public VividEcsQuery WithAny(params VividEcsTypeIndex[] types)
        {
            AddRange(m_Any, types);
            return this;
        }

        public VividEcsQuery WithNone(params VividEcsTypeIndex[] types)
        {
            AddRange(m_None, types);
            return this;
        }

        public int MatchingLineCount()
        {
            int count = 0;
            foreach (VividEcsArchetypeLine _ in MatchLines())
                count++;

            return count;
        }

        public int MatchingEntriesCount()
        {
            int count = 0;
            foreach (VividEcsArchetypeLine line in MatchLines())
                count += line.activeCount;

            return count;
        }

        public IEnumerable<VividEcsArchetypeLine> MatchLines()
        {
            for (int index = 0; index < m_Lines.Count; index++)
            {
                VividEcsArchetypeLine line = m_Lines[index];
                if (Matches(line))
                    yield return line;
            }
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

            return true;
        }

        private static void AddRange(List<VividEcsTypeIndex> target, VividEcsTypeIndex[] source)
        {
            if (source == null)
                return;

            for (int index = 0; index < source.Length; index++)
            {
                VividEcsTypeIndex type = source[index];
                if (type.IsValid && !target.Contains(type))
                    target.Add(type);
            }
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
}
