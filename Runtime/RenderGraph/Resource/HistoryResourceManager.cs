using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderGraph.Resource
{
    public class HistoryResourceManager
    {
        private struct HistoryEntry
        {
            public RTHandle Current;
            public RTHandle History;
            public TextureDesc Desc;
        }

        private readonly Dictionary<string, HistoryEntry> m_Entries = new Dictionary<string, HistoryEntry>();

        public RTHandle GetOrAllocate(string guid, TextureDesc desc)
        {
            if (m_Entries.TryGetValue(guid, out var entry))
            {
                if (entry.Desc.width == desc.width &&
                    entry.Desc.height == desc.height &&
                    entry.Desc.colorFormat == desc.colorFormat)
                {
                    return entry.Current;
                }

                // Descriptor changed — reallocate
                entry.Current?.Release();
                entry.History?.Release();
                m_Entries.Remove(guid);
            }

            var current = RTHandles.Alloc(
                desc.width, desc.height,
                colorFormat: desc.colorFormat,
                name: desc.name + "_Current");

            var history = RTHandles.Alloc(
                desc.width, desc.height,
                colorFormat: desc.colorFormat,
                name: desc.name + "_History");

            // Clear both to black on first allocation
            ClearRTHandle(current);
            ClearRTHandle(history);

            m_Entries[guid] = new HistoryEntry
            {
                Current = current,
                History = history,
                Desc = desc
            };

            return current;
        }

        public RTHandle GetCurrentHandle(string guid)
        {
            return m_Entries.TryGetValue(guid, out var entry) ? entry.Current : null;
        }

        public RTHandle GetHistoryHandle(string guid)
        {
            return m_Entries.TryGetValue(guid, out var entry) ? entry.History : null;
        }

        public void SwapBuffers()
        {
            var keys = new List<string>(m_Entries.Keys);
            foreach (var key in keys)
            {
                var entry = m_Entries[key];
                m_Entries[key] = new HistoryEntry
                {
                    Current = entry.History,
                    History = entry.Current,
                    Desc = entry.Desc
                };
            }
        }

        public void Release(string guid)
        {
            if (m_Entries.TryGetValue(guid, out var entry))
            {
                entry.Current?.Release();
                entry.History?.Release();
                m_Entries.Remove(guid);
            }
        }

        public void ReleaseAll()
        {
            foreach (var entry in m_Entries.Values)
            {
                entry.Current?.Release();
                entry.History?.Release();
            }
            m_Entries.Clear();
        }

        private static void ClearRTHandle(RTHandle handle)
        {
            if (handle?.rt == null) return;
            var prev = RenderTexture.active;
            RenderTexture.active = handle.rt;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = prev;
        }
    }
}
