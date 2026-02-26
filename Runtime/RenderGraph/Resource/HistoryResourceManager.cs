using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderGraph.Resource
{
    public class HistoryResourceManager
    {
        private struct TextureEntry
        {
            public RTHandle Current;
            public RTHandle History;
            public int Width;
            public int Height;
            public GraphicsFormat Format;
            public bool EnableRandomWrite;
        }

        private struct BufferEntry
        {
            public GraphicsBuffer Current;
            public GraphicsBuffer History;
            public int Count;
            public int Stride;
        }

        private readonly Dictionary<string, TextureEntry> m_TextureEntries = new();
        private readonly Dictionary<string, BufferEntry> m_BufferEntries = new();

        // --- Texture ---

        public RTHandle GetOrAllocate(string guid, TextureDesc desc, bool enableRandomWrite = false)
        {
            if (m_TextureEntries.TryGetValue(guid, out var entry))
            {
                if (entry.Width == desc.width &&
                    entry.Height == desc.height &&
                    entry.Format == desc.colorFormat &&
                    entry.EnableRandomWrite == enableRandomWrite)
                {
                    return entry.Current;
                }

                entry.Current?.Release();
                entry.History?.Release();
                m_TextureEntries.Remove(guid);
            }

            var current = RTHandles.Alloc(
                desc.width, desc.height,
                colorFormat: desc.colorFormat,
                enableRandomWrite: enableRandomWrite,
                name: desc.name + "_Current");

            var history = RTHandles.Alloc(
                desc.width, desc.height,
                colorFormat: desc.colorFormat,
                enableRandomWrite: enableRandomWrite,
                name: desc.name + "_History");

            ClearRTHandle(current);
            ClearRTHandle(history);

            m_TextureEntries[guid] = new TextureEntry
            {
                Current = current,
                History = history,
                Width = desc.width,
                Height = desc.height,
                Format = desc.colorFormat,
                EnableRandomWrite = enableRandomWrite
            };

            return current;
        }

        public RTHandle GetCurrentHandle(string guid)
        {
            return m_TextureEntries.TryGetValue(guid, out var entry) ? entry.Current : null;
        }

        public RTHandle GetHistoryHandle(string guid)
        {
            return m_TextureEntries.TryGetValue(guid, out var entry) ? entry.History : null;
        }

        // --- Buffer ---

        public GraphicsBuffer GetOrAllocateBuffer(string guid, int count, int stride)
        {
            if (m_BufferEntries.TryGetValue(guid, out var entry))
            {
                if (entry.Count == count && entry.Stride == stride)
                    return entry.Current;

                entry.Current?.Release();
                entry.History?.Release();
                m_BufferEntries.Remove(guid);
            }

            var current = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);
            var history = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);

            m_BufferEntries[guid] = new BufferEntry
            {
                Current = current,
                History = history,
                Count = count,
                Stride = stride
            };

            return current;
        }

        public GraphicsBuffer GetCurrentBufferHandle(string guid)
        {
            return m_BufferEntries.TryGetValue(guid, out var entry) ? entry.Current : null;
        }

        public GraphicsBuffer GetHistoryBufferHandle(string guid)
        {
            return m_BufferEntries.TryGetValue(guid, out var entry) ? entry.History : null;
        }

        // --- Swap ---

        public void SwapBuffers()
        {
            var texKeys = new List<string>(m_TextureEntries.Keys);
            foreach (var key in texKeys)
            {
                var e = m_TextureEntries[key];
                m_TextureEntries[key] = new TextureEntry
                {
                    Current = e.History,
                    History = e.Current,
                    Width = e.Width,
                    Height = e.Height,
                    Format = e.Format,
                    EnableRandomWrite = e.EnableRandomWrite
                };
            }

            var bufKeys = new List<string>(m_BufferEntries.Keys);
            foreach (var key in bufKeys)
            {
                var e = m_BufferEntries[key];
                m_BufferEntries[key] = new BufferEntry
                {
                    Current = e.History,
                    History = e.Current,
                    Count = e.Count,
                    Stride = e.Stride
                };
            }
        }

        // --- Release ---

        public void Release(string guid)
        {
            if (m_TextureEntries.TryGetValue(guid, out var texEntry))
            {
                texEntry.Current?.Release();
                texEntry.History?.Release();
                m_TextureEntries.Remove(guid);
            }

            if (m_BufferEntries.TryGetValue(guid, out var bufEntry))
            {
                bufEntry.Current?.Release();
                bufEntry.History?.Release();
                m_BufferEntries.Remove(guid);
            }
        }

        public void ReleaseAll()
        {
            foreach (var entry in m_TextureEntries.Values)
            {
                entry.Current?.Release();
                entry.History?.Release();
            }
            m_TextureEntries.Clear();

            foreach (var entry in m_BufferEntries.Values)
            {
                entry.Current?.Release();
                entry.History?.Release();
            }
            m_BufferEntries.Clear();
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
