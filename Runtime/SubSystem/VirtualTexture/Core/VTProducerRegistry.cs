using System;
using System.Collections.Generic;

namespace VividRP.Runtime
{
    public readonly struct VTProducerHandle : IEquatable<VTProducerHandle>
    {
        public static readonly VTProducerHandle Invalid = new(0);

        internal VTProducerHandle(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value > 0;

        public bool Equals(VTProducerHandle other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is VTProducerHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return IsValid ? Value.ToString() : "Invalid";
        }

        public static bool operator ==(VTProducerHandle left, VTProducerHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VTProducerHandle left, VTProducerHandle right)
        {
            return !left.Equals(right);
        }
    }

    internal readonly struct VTRegisteredProducer
    {
        internal VTRegisteredProducer(
            VTProducerHandle handle,
            VTProducerDesc desc,
            VTProducer producer,
            IVTPageProducer pageProducer)
        {
            Handle = handle;
            Desc = desc;
            Producer = producer;
            PageProducer = pageProducer;
        }

        internal VTProducerHandle Handle { get; }

        internal VTProducerDesc Desc { get; }

        internal VTProducer Producer { get; }

        internal IVTPageProducer PageProducer { get; }

        internal string Name => Desc.Name;
    }

    internal sealed class VTProducerRegistry : IDisposable
    {
        private sealed class Entry
        {
            public VTProducerHandle Handle;
            public VTProducerDesc Desc;
            public VTProducer Producer;
            public IVTPageProducer PageProducer;
            public int RefCount;
        }

        private readonly Dictionary<VTProducerHandle, Entry> m_Entries = new();
        private int m_NextHandleValue = 1;

        internal VTProducerHandle Register(in VirtualTextureSpaceDesc spaceDesc, VTProducer producer)
        {
            VTProducer resolvedProducer = ResolveStoredProducer(producer);
            VTProducerDesc producerDesc = VTProducerDesc.FromSpaceDesc(resolvedProducer.Name, spaceDesc);

            foreach (KeyValuePair<VTProducerHandle, Entry> pair in m_Entries)
            {
                Entry entry = pair.Value;
                if (!entry.Desc.Equals(producerDesc) || !IsSameProducer(entry.Producer, resolvedProducer))
                    continue;

                entry.RefCount += 1;
                return entry.Handle;
            }

            VTProducerHandle handle = new(m_NextHandleValue++);
            IVTPageProducer pageProducer = VTRuntimeProducerUtility.Resolve(resolvedProducer, spaceDesc);
            m_Entries.Add(handle, new Entry
            {
                Handle = handle,
                Desc = producerDesc,
                Producer = resolvedProducer,
                PageProducer = pageProducer,
                RefCount = 1,
            });

            return handle;
        }

        internal bool TryGet(VTProducerHandle handle, out VTRegisteredProducer producer)
        {
            if (m_Entries.TryGetValue(handle, out Entry entry))
            {
                producer = new VTRegisteredProducer(
                    entry.Handle,
                    entry.Desc,
                    entry.Producer,
                    entry.PageProducer);
                return true;
            }

            producer = default;
            return false;
        }

        internal bool IsSameProducer(VTProducerHandle handle, VTProducer producer)
        {
            return m_Entries.TryGetValue(handle, out Entry entry)
                   && IsSameProducer(entry.Producer, ResolveStoredProducer(producer));
        }

        internal bool TryGetProducerName(VTProducerHandle handle, out string producerName)
        {
            if (m_Entries.TryGetValue(handle, out Entry entry))
            {
                producerName = entry.Desc.Name;
                return true;
            }

            producerName = null;
            return false;
        }

        internal void Release(VTProducerHandle handle)
        {
            if (!m_Entries.TryGetValue(handle, out Entry entry))
                return;

            entry.RefCount = Math.Max(0, entry.RefCount - 1);
            if (entry.RefCount > 0)
                return;

            if (entry.PageProducer is IDisposable disposableProducer)
                disposableProducer.Dispose();

            m_Entries.Remove(handle);
        }

        public void Dispose()
        {
            foreach (KeyValuePair<VTProducerHandle, Entry> pair in m_Entries)
            {
                if (pair.Value.PageProducer is IDisposable disposableProducer)
                    disposableProducer.Dispose();
            }

            m_Entries.Clear();
            m_NextHandleValue = 1;
        }

        private static VTProducer ResolveStoredProducer(VTProducer producer)
        {
            return producer ?? VTNullProducer.Instance;
        }

        private static bool IsSameProducer(VTProducer left, VTProducer right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left == null || right == null)
                return false;

            return string.Equals(left.Name, right.Name, StringComparison.Ordinal);
        }
    }
}
