using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Profiling;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class RenderPassProfilerMarkers
    {
        private const string MarkerRoot = "VividRP.RenderPass";

        public RenderPassProfilerMarkers(string displayName, string graphName)
        {
            displayName = string.IsNullOrWhiteSpace(displayName)
                ? "Unknown"
                : displayName;
            graphName = string.IsNullOrWhiteSpace(graphName)
                ? displayName
                : graphName;

            Create = new ProfilerMarker($"{MarkerRoot}.Create/{displayName}");
            Initialize = new ProfilerMarker($"{MarkerRoot}.Initialize/{displayName}");
            Prepare = new ProfilerMarker($"{MarkerRoot}.Prepare/{displayName}");
            RecordGraph = new ProfilerMarker($"{MarkerRoot}.RecordGraph/{displayName}");
            Record = new ProfilerMarker($"{MarkerRoot}.Record/{displayName}");
            Dispose = new ProfilerMarker($"{MarkerRoot}.Dispose/{displayName}");
            CommandSampler = new ProfilingSampler($"{MarkerRoot}.Commands/{displayName}");
            GraphName = graphName;
        }

        public ProfilerMarker Create { get; }
        public ProfilerMarker Initialize { get; }
        public ProfilerMarker Prepare { get; }
        public ProfilerMarker RecordGraph { get; }
        public ProfilerMarker Record { get; }
        public ProfilerMarker Dispose { get; }
        public ProfilingSampler CommandSampler { get; }
        public string GraphName { get; }
    }

    internal static class RenderPassProfilingUtility
    {
        private readonly struct PassProfilerKey
        {
            public PassProfilerKey(IRenderPass pass, string displayName, int passIndex)
            {
                Pass = pass;
                DisplayName = displayName ?? string.Empty;
                PassIndex = passIndex;
            }

            public IRenderPass Pass { get; }
            public string DisplayName { get; }
            public int PassIndex { get; }
        }

        private sealed class PassProfilerKeyComparer : IEqualityComparer<PassProfilerKey>
        {
            public bool Equals(PassProfilerKey x, PassProfilerKey y)
            {
                return ReferenceEquals(x.Pass, y.Pass)
                    && x.PassIndex == y.PassIndex
                    && string.Equals(x.DisplayName, y.DisplayName, StringComparison.Ordinal);
            }

            public int GetHashCode(PassProfilerKey obj)
            {
                var passHash = obj.Pass != null ? RuntimeHelpers.GetHashCode(obj.Pass) : 0;
                return HashCode.Combine(
                    passHash,
                    obj.PassIndex,
                    StringComparer.Ordinal.GetHashCode(obj.DisplayName));
            }
        }

        private static readonly Dictionary<PassProfilerKey, RenderPassProfilerMarkers> s_Markers = new(new PassProfilerKeyComparer());

        public static readonly ProfilerMarker CompileMarker = new("VividRP.RenderPass.Compile");
        public static readonly ProfilerMarker PrepareFrameMarker = new("VividRP.RenderPass.PrepareFrame");
        public static readonly ProfilerMarker PrepareAllMarker = new("VividRP.RenderPass.PrepareAll");
        public static readonly ProfilerMarker RecordRenderGraphMarker = new("VividRP.RenderPass.RecordRenderGraph");
        public static readonly ProfilerMarker InjectedStpRecordGraphMarker = new("VividRP.RenderPass.RecordGraph/STP (Injected)");

        public static RenderPassProfilerMarkers GetMarkers(
            IRenderPass pass,
            string displayName = null,
            int passIndex = -1)
        {
            var key = new PassProfilerKey(pass, displayName, passIndex);
            if (s_Markers.TryGetValue(key, out var markers))
                return markers;

            var graphName = ResolveGraphName(pass, displayName);
            displayName = ResolveDisplayName(graphName, passIndex);
            markers = new RenderPassProfilerMarkers(displayName, graphName);
            s_Markers[key] = markers;
            return markers;
        }

        public static void Clear()
        {
            s_Markers.Clear();
        }

        private static string ResolveGraphName(IRenderPass pass, string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return pass != null ? pass.GetType().Name : "Unknown";

            return displayName;
        }

        private static string ResolveDisplayName(string graphName, int passIndex)
        {
            return passIndex >= 0
                ? $"{passIndex}:{graphName}"
                : graphName;
        }
    }
}
