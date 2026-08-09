using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
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
