using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    /// <summary>
    /// Stable identifier for a same-frame RenderGraph blackboard resource slot.
    /// </summary>
    public readonly struct FrameResourceSlotId : IEquatable<FrameResourceSlotId>
    {
        private FrameResourceSlotId(int value, string name)
        {
            Value = value;
            Name = name;
        }

        public int Value { get; }

        public string Name { get; }

        public static FrameResourceSlotId Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Frame resource slot names cannot be empty.", nameof(name));

            return new FrameResourceSlotId(Shader.PropertyToID(name), name);
        }

        public bool Equals(FrameResourceSlotId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is FrameResourceSlotId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Name ?? string.Empty;
        }

        public static bool operator ==(FrameResourceSlotId left, FrameResourceSlotId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(FrameResourceSlotId left, FrameResourceSlotId right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Built-in same-frame blackboard slots shared across RenderGraph passes without wiring explicit .vrdg ports.
    /// </summary>
    public static class FrameResourceSlots
    {
        public const string SubstrateClosureBufferName = "Vivid.Frame.SubstrateClosureBuffer";
        public const string LumenBentNormalName = "Vivid.Frame.LumenBentNormal";
        public const string LumenRoughSpecularName = "Vivid.Frame.LumenRoughSpecular";
        public const string LumenSpecularReflectionName = "Vivid.Frame.LumenSpecularReflection";
        public const string LumenDebugOutputName = "Vivid.Frame.LumenDebugOutput";
        public const string SceneColorPrePostProcessName = "Vivid.Frame.SceneColorPrePostProcess";
        public const string ClusteredLightGridName = "Vivid.Frame.ClusteredLightGrid";
        public const string VirtualShadowMapLocalMaskName = "Vivid.Frame.VirtualShadowMapLocalMask";

        public static readonly FrameResourceSlotId SubstrateClosureBuffer =
            FrameResourceSlotId.Create(SubstrateClosureBufferName);

        public static readonly FrameResourceSlotId LumenBentNormal =
            FrameResourceSlotId.Create(LumenBentNormalName);

        public static readonly FrameResourceSlotId LumenRoughSpecular =
            FrameResourceSlotId.Create(LumenRoughSpecularName);

        public static readonly FrameResourceSlotId LumenSpecularReflection =
            FrameResourceSlotId.Create(LumenSpecularReflectionName);

        public static readonly FrameResourceSlotId LumenDebugOutput =
            FrameResourceSlotId.Create(LumenDebugOutputName);

        public static readonly FrameResourceSlotId SceneColorPrePostProcess =
            FrameResourceSlotId.Create(SceneColorPrePostProcessName);
    }

    /// <summary>
    /// Zero-allocation context passed to <see cref="IFrameTextureExtractor"/> during RenderGraph recording.
    /// </summary>
    public readonly struct FrameResourceExtractionContext
    {
        private readonly RenderGraph m_RenderGraph;
        private readonly Dictionary<RenderGraphTexture, TextureHandle> m_TextureCache;

        internal FrameResourceExtractionContext(
            RenderGraph renderGraph,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache)
        {
            m_RenderGraph = renderGraph;
            m_TextureCache = textureCache;
        }

        public TextureHandle GetOrCreateTextureHandle(RenderGraphTexture texture)
        {
            if (m_RenderGraph == null || texture == null || m_TextureCache == null)
                return default;

            return PassRecorder.GetOrCreateTextureHandle(m_RenderGraph, texture, m_TextureCache);
        }
    }

    /// <summary>
    /// Receives a one-shot RenderGraph recording callback when a target boundary slot becomes available.
    /// Implement directly on a reusable frame ContextItem to avoid per-frame delegate allocations.
    /// </summary>
    public interface IFrameTextureExtractor
    {
        bool TryExtractTexture(
            RenderGraph renderGraph,
            FrameResourceSlotId slot,
            RenderGraphTexture sourceTexture,
            TextureHandle sourceHandle,
            in FrameResourceExtractionContext context);
    }

    /// <summary>
    /// Per-frame RenderGraph blackboard holding CoreRP-managed TextureHandle and BufferHandle
    /// slots shared across passes in the same frame, plus boundary extraction hooks.
    /// Cleared automatically between frames.
    /// </summary>
    public sealed class FrameResourceTable : ContextItem
    {
        private struct TextureExtractorEntry
        {
            public FrameResourceSlotId Slot;
            public IFrameTextureExtractor Extractor;
            public bool Consumed;
        }

        private readonly Dictionary<FrameResourceSlotId, TextureHandle> m_Textures = new(16);
        private readonly Dictionary<FrameResourceSlotId, BufferHandle> m_Buffers = new(16);
        private readonly List<TextureExtractorEntry> m_TextureExtractors = new(4);
        private int m_PendingExtractorCount;

        public int TextureCount => m_Textures.Count;

        public int BufferCount => m_Buffers.Count;

        public bool HasPendingTextureExtractors => m_PendingExtractorCount > 0;

        public override void Reset()
        {
            m_Textures.Clear();
            m_Buffers.Clear();
            m_TextureExtractors.Clear();
            m_PendingExtractorCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FrameResourceTable GetOrCreate(ContextContainer frameData)
        {
            return frameData?.GetOrCreate<FrameResourceTable>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGet(ContextContainer frameData, out FrameResourceTable table)
        {
            if (frameData != null && frameData.Contains<FrameResourceTable>())
            {
                table = frameData.Get<FrameResourceTable>();
                return table != null;
            }

            table = null;
            return false;
        }

        public void PublishTexture(FrameResourceSlotId id, TextureHandle handle)
        {
            if (handle.IsValid())
                m_Textures[id] = handle;
            else
                m_Textures.Remove(id);
        }

        public void PublishBuffer(FrameResourceSlotId id, BufferHandle handle)
        {
            if (handle.IsValid())
                m_Buffers[id] = handle;
            else
                m_Buffers.Remove(id);
        }

        public bool TryGetTexture(FrameResourceSlotId id, out TextureHandle handle)
        {
            if (m_Textures.TryGetValue(id, out handle) && handle.IsValid())
                return true;

            handle = default;
            return false;
        }

        public bool TryGetBuffer(FrameResourceSlotId id, out BufferHandle handle)
        {
            if (m_Buffers.TryGetValue(id, out handle) && handle.IsValid())
                return true;

            handle = default;
            return false;
        }

        public TextureHandle CreateAndPublishTextureForPass(
            IRenderPass pass,
            FrameResourceSlotId id,
            in TextureDesc desc,
            AccessFlags access = AccessFlags.ReadWrite)
        {
            var handle = PassRecorder.CreateTextureForPass(pass, in desc, access);
            PublishTexture(id, handle);
            return handle;
        }

        public BufferHandle CreateAndPublishBufferForPass(
            IRenderPass pass,
            FrameResourceSlotId id,
            in BufferDesc desc,
            AccessFlags access = AccessFlags.ReadWrite)
        {
            var handle = PassRecorder.CreateBufferForPass(pass, in desc, access);
            PublishBuffer(id, handle);
            return handle;
        }

        public bool UseTextureSlotForPass(
            IRenderPass pass,
            FrameResourceSlotId id,
            AccessFlags access,
            out TextureHandle handle)
        {
            if (!TryGetTexture(id, out handle))
                return false;

            PassRecorder.RegisterImportedTextureForPass(pass, handle, access);
            return true;
        }

        public bool UseBufferSlotForPass(
            IRenderPass pass,
            FrameResourceSlotId id,
            AccessFlags access,
            out BufferHandle handle)
        {
            if (!TryGetBuffer(id, out handle))
                return false;

            PassRecorder.RegisterImportedBufferForPass(pass, handle, access);
            return true;
        }

        public void RegisterTextureExtractor(FrameResourceSlotId slot, IFrameTextureExtractor extractor)
        {
            if (extractor == null)
                return;

            for (var i = 0; i < m_TextureExtractors.Count; i++)
            {
                var existing = m_TextureExtractors[i];
                if (existing.Slot == slot && ReferenceEquals(existing.Extractor, extractor))
                {
                    if (existing.Consumed)
                    {
                        existing.Consumed = false;
                        m_TextureExtractors[i] = existing;
                        m_PendingExtractorCount++;
                    }
                    return;
                }
            }

            m_TextureExtractors.Add(new TextureExtractorEntry
            {
                Slot = slot,
                Extractor = extractor,
                Consumed = false,
            });
            m_PendingExtractorCount++;
        }

        internal int TryRunTextureExtractors(
            FrameResourceSlotId slot,
            RenderGraph renderGraph,
            RenderGraphTexture sourceTexture,
            TextureHandle sourceHandle,
            in FrameResourceExtractionContext context)
        {
            if (m_PendingExtractorCount <= 0 || renderGraph == null || !sourceHandle.IsValid())
                return 0;

            var extractedCount = 0;
            for (var i = 0; i < m_TextureExtractors.Count; i++)
            {
                var entry = m_TextureExtractors[i];
                if (entry.Consumed || entry.Slot != slot || entry.Extractor == null)
                    continue;

                if (entry.Extractor.TryExtractTexture(renderGraph, slot, sourceTexture, sourceHandle, in context))
                {
                    entry.Consumed = true;
                    m_TextureExtractors[i] = entry;
                    m_PendingExtractorCount--;
                    extractedCount++;
                }
            }

            return extractedCount;
        }
    }
}
