using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class RenderGraphHistoryRegistry
    {
        private sealed class HistoryEntry
        {
            public RTHandle Handle;
            public HistoryTargetSettings Settings;
            public bool HasValidData;
        }

        private struct HistoryTargetSettings
        {
            public int Width;
            public int Height;
            public int Slices;
            public DepthBits DepthBits;
            public GraphicsFormat ColorFormat;
            public TextureDimension Dimension;
            public FilterMode FilterMode;
            public TextureWrapMode WrapMode;
            public bool EnableRandomWrite;
            public bool UseMipMap;
            public bool AutoGenerateMips;
            public int AnisoLevel;
            public float MipMapBias;
            public MSAASamples MsaaSamples;
            public bool BindTextureMS;
            public bool UseDynamicScale;
            public bool UseDynamicScaleExplicit;
            public string Name;
        }

        private static readonly Dictionary<string, HistoryEntry> s_HistoryTextures = new(StringComparer.Ordinal);

        internal static void Clear()
        {
            foreach (var entry in s_HistoryTextures.Values)
            {
                entry?.Handle?.Release();
            }

            s_HistoryTextures.Clear();
        }

        internal static RTHandle GetOrCreateHistoryTarget(
            Camera camera,
            RenderGraphData graphAsset,
            int historyIndex,
            RenderGraphTextureDesc descriptor,
            CommandBuffer cmd = null)
        {
            return GetOrCreateHistoryTarget(
                camera,
                graphAsset,
                historyIndex.ToString(),
                descriptor,
                cmd);
        }

        internal static RTHandle GetOrCreateHistoryTarget(
            Camera camera,
            RenderGraphData graphAsset,
            string historyKey,
            RenderGraphTextureDesc descriptor,
            CommandBuffer cmd = null)
        {
            if (camera == null || graphAsset == null || string.IsNullOrEmpty(historyKey))
                return null;

            var settings = CreateSettings(descriptor, historyKey);
            var entry = GetOrCreateEntry(BuildScopedKey(camera, graphAsset, historyKey));
            var createdOrChanged = false;
            if (entry.Handle == null || !SettingsMatch(entry.Settings, settings))
            {
                entry.Handle?.Release();
                entry.Handle = RTHandles.Alloc(
                    Mathf.Max(1, settings.Width),
                    Mathf.Max(1, settings.Height),
                    slices: settings.Slices,
                    depthBufferBits: settings.DepthBits,
                    colorFormat: settings.ColorFormat,
                    filterMode: settings.FilterMode,
                    wrapMode: settings.WrapMode,
                    dimension: settings.Dimension,
                    enableRandomWrite: settings.EnableRandomWrite,
                    useMipMap: settings.UseMipMap,
                    autoGenerateMips: settings.AutoGenerateMips,
                    isShadowMap: false,
                    anisoLevel: settings.AnisoLevel,
                    mipMapBias: settings.MipMapBias,
                    msaaSamples: settings.MsaaSamples,
                    bindTextureMS: settings.BindTextureMS,
                    useDynamicScale: settings.UseDynamicScale,
                    useDynamicScaleExplicit: settings.UseDynamicScaleExplicit,
                    name: settings.Name);
                entry.Settings = settings;
                entry.HasValidData = false;
                createdOrChanged = true;
            }

            if (createdOrChanged && cmd != null)
            {
                var clearFlag = descriptor != null && descriptor.DepthBufferBits != DepthBits.None
                    ? ClearFlag.DepthStencil
                    : ClearFlag.Color;
                CoreUtils.SetRenderTarget(cmd, entry.Handle, clearFlag, descriptor?.ClearColor ?? Color.clear);
            }

            return entry.Handle;
        }

        internal static bool TryGetHistoryTarget(
            Camera camera,
            RenderGraphData graphAsset,
            int historyIndex,
            out RTHandle handle,
            out bool hasValidData)
        {
            return TryGetHistoryTarget(
                camera,
                graphAsset,
                historyIndex.ToString(),
                out handle,
                out hasValidData);
        }

        internal static bool TryGetHistoryTarget(
            Camera camera,
            RenderGraphData graphAsset,
            string historyKey,
            out RTHandle handle,
            out bool hasValidData)
        {
            handle = null;
            hasValidData = false;
            if (camera == null || graphAsset == null || string.IsNullOrEmpty(historyKey))
                return false;

            if (!s_HistoryTextures.TryGetValue(BuildScopedKey(camera, graphAsset, historyKey), out var entry) || entry == null)
                return false;

            handle = entry.Handle;
            hasValidData = entry.HasValidData;
            return handle != null;
        }

        internal static void MarkHistoryValid(Camera camera, RenderGraphData graphAsset, int historyIndex, bool valid = true)
        {
            MarkHistoryValid(
                camera,
                graphAsset,
                historyIndex.ToString(),
                valid);
        }

        internal static void MarkHistoryValid(Camera camera, RenderGraphData graphAsset, string historyKey, bool valid = true)
        {
            if (camera == null || graphAsset == null || string.IsNullOrEmpty(historyKey))
                return;

            if (!s_HistoryTextures.TryGetValue(BuildScopedKey(camera, graphAsset, historyKey), out var entry) || entry == null)
                return;

            entry.HasValidData = valid;
        }

        private static HistoryEntry GetOrCreateEntry(string key)
        {
            if (!s_HistoryTextures.TryGetValue(key, out var entry) || entry == null)
            {
                entry = new HistoryEntry();
                s_HistoryTextures[key] = entry;
            }

            return entry;
        }

        private static HistoryTargetSettings CreateSettings(RenderGraphTextureDesc descriptor, string historyKey)
        {
            return new HistoryTargetSettings
            {
                Width = descriptor?.Width ?? 1,
                Height = descriptor?.Height ?? 1,
                Slices = descriptor?.Slices ?? 1,
                DepthBits = descriptor?.DepthBufferBits ?? DepthBits.None,
                ColorFormat = descriptor?.ColorFormat ?? GraphicsFormat.R8G8B8A8_UNorm,
                Dimension = descriptor?.Dimension ?? TextureDimension.Tex2D,
                FilterMode = descriptor?.FilterMode ?? FilterMode.Bilinear,
                WrapMode = descriptor?.WrapMode ?? TextureWrapMode.Clamp,
                EnableRandomWrite = descriptor?.EnableRandomWrite ?? false,
                UseMipMap = descriptor?.UseMipMap ?? false,
                AutoGenerateMips = descriptor?.AutoGenerateMips ?? false,
                AnisoLevel = descriptor?.AnisoLevel ?? 1,
                MipMapBias = descriptor?.MipMapBias ?? 0f,
                MsaaSamples = descriptor?.MsaaSamples ?? MSAASamples.None,
                BindTextureMS = descriptor?.BindTextureMS ?? false,
                UseDynamicScale = descriptor?.UseDynamicScale ?? false,
                UseDynamicScaleExplicit = descriptor?.UseDynamicScaleExplicit ?? false,
                Name = string.IsNullOrEmpty(descriptor?.Name)
                    ? $"HistoryTexture_{historyKey}"
                    : $"{descriptor.Name} History"
            };
        }

        private static bool SettingsMatch(HistoryTargetSettings left, HistoryTargetSettings right)
        {
            return left.Width == right.Width
                && left.Height == right.Height
                && left.Slices == right.Slices
                && left.DepthBits == right.DepthBits
                && left.ColorFormat == right.ColorFormat
                && left.Dimension == right.Dimension
                && left.FilterMode == right.FilterMode
                && left.WrapMode == right.WrapMode
                && left.EnableRandomWrite == right.EnableRandomWrite
                && left.UseMipMap == right.UseMipMap
                && left.AutoGenerateMips == right.AutoGenerateMips
                && left.AnisoLevel == right.AnisoLevel
                && Mathf.Approximately(left.MipMapBias, right.MipMapBias)
                && left.MsaaSamples == right.MsaaSamples
                && left.BindTextureMS == right.BindTextureMS
                && left.UseDynamicScale == right.UseDynamicScale
                && left.UseDynamicScaleExplicit == right.UseDynamicScaleExplicit
                && string.Equals(left.Name, right.Name, StringComparison.Ordinal);
        }

        private static string BuildKey(Camera camera, RenderGraphData graphAsset, int historyIndex)
        {
            return BuildScopedKey(camera, graphAsset, historyIndex.ToString());
        }

        private static string BuildScopedKey(Camera camera, RenderGraphData graphAsset, string historyKey)
        {
            return $"{camera.GetEntityId()}|{graphAsset.GetEntityId()}|{historyKey}";
        }
    }

    internal static class RenderGraphBufferHistoryRegistry
    {
        private sealed class HistoryEntry
        {
            public GraphicsBuffer PreviousBuffer;
            public GraphicsBuffer CurrentBuffer;
            public HistoryBufferSettings Settings;
            public bool HasValidData;
        }

        private struct HistoryBufferSettings
        {
            public int Count;
            public int Stride;
            public GraphicsBuffer.Target Target;
            public string Name;
        }

        private static readonly Dictionary<string, HistoryEntry> s_HistoryBuffers = new(StringComparer.Ordinal);

        internal static void Clear()
        {
            foreach (var entry in s_HistoryBuffers.Values)
            {
                entry?.PreviousBuffer?.Dispose();
                entry?.CurrentBuffer?.Dispose();
            }

            s_HistoryBuffers.Clear();
        }

        internal static bool PrepareHistoryBuffers(
            Camera camera,
            RenderGraphData graphAsset,
            string historyKey,
            RenderGraphBufferDesc descriptor,
            out GraphicsBuffer previousBuffer,
            out GraphicsBuffer currentBuffer,
            out bool hasValidData)
        {
            previousBuffer = null;
            currentBuffer = null;
            hasValidData = false;

            if (camera == null || graphAsset == null || string.IsNullOrEmpty(historyKey))
                return false;

            var settings = CreateSettings(descriptor, historyKey);
            var entry = GetOrCreateEntry(BuildKey(camera, graphAsset, historyKey));
            if (entry.PreviousBuffer == null
                || entry.CurrentBuffer == null
                || !SettingsMatch(entry.Settings, settings))
            {
                entry.PreviousBuffer?.Dispose();
                entry.CurrentBuffer?.Dispose();
                entry.PreviousBuffer = CreateBuffer(settings, "Previous");
                entry.CurrentBuffer = CreateBuffer(settings, "Current");
                entry.Settings = settings;
                entry.HasValidData = false;
            }

            previousBuffer = entry.PreviousBuffer;
            currentBuffer = entry.CurrentBuffer;
            hasValidData = entry.HasValidData;
            return previousBuffer != null || currentBuffer != null;
        }

        internal static void FinalizeFrame(Camera camera, RenderGraphData graphAsset, string historyKey, bool valid = true)
        {
            if (camera == null || graphAsset == null || string.IsNullOrEmpty(historyKey))
                return;

            if (!s_HistoryBuffers.TryGetValue(BuildKey(camera, graphAsset, historyKey), out var entry) || entry == null)
                return;

            (entry.PreviousBuffer, entry.CurrentBuffer) = (entry.CurrentBuffer, entry.PreviousBuffer);
            entry.HasValidData = valid;
        }

        private static HistoryEntry GetOrCreateEntry(string key)
        {
            if (!s_HistoryBuffers.TryGetValue(key, out var entry) || entry == null)
            {
                entry = new HistoryEntry();
                s_HistoryBuffers[key] = entry;
            }

            return entry;
        }

        private static HistoryBufferSettings CreateSettings(RenderGraphBufferDesc descriptor, string historyKey)
        {
            return new HistoryBufferSettings
            {
                Count = Mathf.Max(1, descriptor?.Count ?? 1),
                Stride = Mathf.Max(1, descriptor?.Stride ?? 4),
                Target = descriptor?.Target ?? GraphicsBuffer.Target.Structured,
                Name = string.IsNullOrEmpty(descriptor?.Name)
                    ? $"HistoryBuffer_{historyKey}"
                    : descriptor.Name
            };
        }

        private static GraphicsBuffer CreateBuffer(HistoryBufferSettings settings, string suffix)
        {
            var buffer = new GraphicsBuffer(settings.Target, settings.Count, settings.Stride);
            buffer.name = $"{settings.Name} {suffix}";
            return buffer;
        }

        private static bool SettingsMatch(HistoryBufferSettings left, HistoryBufferSettings right)
        {
            return left.Count == right.Count
                && left.Stride == right.Stride
                && left.Target == right.Target
                && string.Equals(left.Name, right.Name, StringComparison.Ordinal);
        }

        private static string BuildKey(Camera camera, RenderGraphData graphAsset, string historyKey)
        {
            return $"{camera.GetEntityId()}|{graphAsset.GetEntityId()}|{historyKey}";
        }
    }
}
