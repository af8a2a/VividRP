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
        public static readonly ProfilerMarker PrepareFrameEnsureCompiledMarker = new("VividRP.RenderPass.PrepareFrame/EnsureCompiled");
        public static readonly ProfilerMarker PrepareFrameClearHistoryImportsMarker = new("VividRP.RenderPass.PrepareFrame/ClearHistoryImports");
        public static readonly ProfilerMarker PrepareFrameClearCodeManagedHistoryMarker = new("VividRP.RenderPass.PrepareFrame/ClearCodeManagedHistory");
        public static readonly ProfilerMarker PrepareFrameContextUpdateMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.Update");
        public static readonly ProfilerMarker PrepareFramePrepareHistoryTargetsMarker = new("VividRP.RenderPass.PrepareFrame/PrepareHistoryTargets");
        public static readonly ProfilerMarker PrepareFrameClearImportedTexturesMarker = new("VividRP.RenderPass.PrepareFrame/ClearImportedTextures");
        public static readonly ProfilerMarker PrepareFrameContextPurgeDestroyedCamerasMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.PurgeDestroyedCameras");
        public static readonly ProfilerMarker PrepareFrameContextResolveDataMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.ResolveData");
        public static readonly ProfilerMarker PrepareFrameContextAdvanceTemporalMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.AdvanceTemporal");
        public static readonly ProfilerMarker PrepareFrameContextPopulateTemporalMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.PopulateTemporal");
        public static readonly ProfilerMarker PrepareFrameContextAutoExposureMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.AutoExposure");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables");
        public static readonly ProfilerMarker PrepareFrameContextSetShaderGlobalsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SetShaderGlobals");
        public static readonly ProfilerMarker PrepareFrameContextAdaptiveProbeVolumeMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.AdaptiveProbeVolume");
        public static readonly ProfilerMarker PrepareFrameContextSubsystemPreRenderMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender");
        public static readonly ProfilerMarker PrepareFrameSubsystemLTCAreaLightMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/LTCAreaLightSystem");
        public static readonly ProfilerMarker PrepareFrameSubsystemDecalMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/DecalSystem");
        public static readonly ProfilerMarker PrepareFrameSubsystemDecalKickMarker = new("VividRP.PlayerLoop.PreLateUpdate/DecalSystem.Kick");
        public static readonly ProfilerMarker PrepareFrameSubsystemDecalCompleteMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/DecalSystem/Complete");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenResolveAssetMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/ResolveAsset");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCameraDataMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/CameraData");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenPrepareFrameMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/PrepareFrame");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenApplySettingsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/ApplySettings");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenResolveResourcesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/ResolveResources");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenBindGlobalsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/BindGlobals");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenSetFrameDataMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/SetFrameData");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenReportStatsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/ReportStats");
        public static readonly ProfilerMarker PrepareFrameSubsystemSkyMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/SkyManager");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem");
        public static readonly ProfilerMarker PrepareAllMarker = new("VividRP.RenderPass.PrepareAll");
        public static readonly ProfilerMarker RecordRenderGraphMarker = new("VividRP.RenderPass.RecordRenderGraph");

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
