using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Profiling;

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
            RecordGraphPrepareRenderGraph = new ProfilerMarker($"{MarkerRoot}.RecordGraph.PrepareRenderGraph/{displayName}");
            RecordGraphResolveResources = new ProfilerMarker($"{MarkerRoot}.RecordGraph.ResolveResources/{displayName}");
            RecordGraphInactiveBypass = new ProfilerMarker($"{MarkerRoot}.RecordGraph.InactiveBypass/{displayName}");
            RecordGraphBuild = new ProfilerMarker($"{MarkerRoot}.RecordGraph.Build/{displayName}");
            RecordGraphSetupResources = new ProfilerMarker($"{MarkerRoot}.RecordGraph.SetupResources/{displayName}");
            RecordGraphSetupImportedHandles = new ProfilerMarker($"{MarkerRoot}.RecordGraph.SetupImportedHandles/{displayName}");
            RecordGraphConfigureBuilder = new ProfilerMarker($"{MarkerRoot}.RecordGraph.ConfigureBuilder/{displayName}");
            RecordGraphSetRenderFunc = new ProfilerMarker($"{MarkerRoot}.RecordGraph.SetRenderFunc/{displayName}");
            Record = new ProfilerMarker($"{MarkerRoot}.Record/{displayName}");
            Dispose = new ProfilerMarker($"{MarkerRoot}.Dispose/{displayName}");
            GraphName = graphName;
        }

        public ProfilerMarker Create { get; }
        public ProfilerMarker Initialize { get; }
        public ProfilerMarker Prepare { get; }
        public ProfilerMarker RecordGraph { get; }
        public ProfilerMarker RecordGraphPrepareRenderGraph { get; }
        public ProfilerMarker RecordGraphResolveResources { get; }
        public ProfilerMarker RecordGraphInactiveBypass { get; }
        public ProfilerMarker RecordGraphBuild { get; }
        public ProfilerMarker RecordGraphSetupResources { get; }
        public ProfilerMarker RecordGraphSetupImportedHandles { get; }
        public ProfilerMarker RecordGraphConfigureBuilder { get; }
        public ProfilerMarker RecordGraphSetRenderFunc { get; }
        public ProfilerMarker Record { get; }
        public ProfilerMarker Dispose { get; }
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
        public static readonly ProfilerMarker InitializeContextMarker = new("VividRP.RenderPass.InitializeContext");
        public static readonly ProfilerMarker InitializeContextClearFrameCachesMarker = new("VividRP.RenderPass.InitializeContext/ClearFrameCaches");
        public static readonly ProfilerMarker InitializeContextResolveFrameDataMarker = new("VividRP.RenderPass.InitializeContext/ResolveFrameData");
        public static readonly ProfilerMarker InitializeContextResolveFrameIndexMarker = new("VividRP.RenderPass.InitializeContext/ResolveFrameIndex");
        public static readonly ProfilerMarker InitializeContextResolveAdditionalCameraDataMarker = new("VividRP.RenderPass.InitializeContext/ResolveAdditionalCameraData");
        public static readonly ProfilerMarker InitializeContextAntialiasingResolveMarker = new("VividRP.RenderPass.InitializeContext/Antialiasing.Resolve");
        public static readonly ProfilerMarker InitializeContextAntialiasingJitterMarker = new("VividRP.RenderPass.InitializeContext/Antialiasing.ApplyJitter");
        public static readonly ProfilerMarker InitializeContextUpdateCameraMatricesMarker = new("VividRP.RenderPass.InitializeContext/UpdateCameraMatrices");
        public static readonly ProfilerMarker InitializeContextPopulateCameraDataMarker = new("VividRP.RenderPass.InitializeContext/PopulateCameraData");
        public static readonly ProfilerMarker InitializeContextPopulateRenderingDataMarker = new("VividRP.RenderPass.InitializeContext/PopulateRenderingData");
        public static readonly ProfilerMarker InitializeContextLightDataUpdateMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update");
        public static readonly ProfilerMarker InitializeContextSceneLightCompleteMarker = new("VividRP.RenderPass.InitializeContext/VividSceneLightSystem.Complete");
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
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables");
        public static readonly ProfilerMarker PrepareFrameContextSetShaderGlobalsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SetShaderGlobals");
        public static readonly ProfilerMarker PrepareFrameContextAdaptiveProbeVolumeMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.AdaptiveProbeVolume");
        public static readonly ProfilerMarker PrepareFrameContextSubsystemPreRenderMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender");
        public static readonly ProfilerMarker PrepareFrameSubsystemAutoExposureMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VividAutoExposureSystem");
        public static readonly ProfilerMarker PrepareFrameSubsystemLTCAreaLightMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/LTCAreaLightSystem");
        public static readonly ProfilerMarker PrepareFrameSubsystemDecalMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/DecalSystem");
        public static readonly ProfilerMarker PrepareFrameSubsystemDecalKickMarker = new("VividRP.PlayerLoop.PreLateUpdate/DecalSystem.Kick");
        public static readonly ProfilerMarker PrepareFrameSubsystemDecalCullScheduleMarker = new("VividRP.BeginCameraRendering/DecalSystem.CullSchedule");
        public static readonly ProfilerMarker PrepareFrameSubsystemDecalCompleteMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/DecalSystem/Complete");
        public static readonly ProfilerMarker PrepareFrameSubsystemSceneLightKickMarker = new("VividRP.PlayerLoop.PreLateUpdate/VividSceneLightSystem.Kick");
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
        public static readonly ProfilerMarker PrepareFrameSubsystemSkyFrameDataMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/SkyManager/FrameData");
        public static readonly ProfilerMarker PrepareFrameSubsystemSkyActiveRendererMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/SkyManager/ActiveRenderer");
        public static readonly ProfilerMarker PrepareFrameSubsystemSkyBuildContextMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/SkyManager/BuildContext");
        public static readonly ProfilerMarker PrepareFrameSubsystemSkyRendererUpdateMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/SkyManager/RendererUpdate");
        public static readonly ProfilerMarker PrepareFrameSubsystemSkyEnvironmentMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/SkyManager/Environment");
        public static readonly ProfilerMarker PrepareFrameSubsystemSkyEnvironmentSpecularMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/SkyManager/Environment/SpecularCubemap");
        public static readonly ProfilerMarker PrepareFrameSubsystemSkyEnvironmentDiffuseMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/SkyManager/Environment/DiffuseAmbientProbe");
        public static readonly ProfilerMarker PrepareFrameSubsystemSkyEnvironmentGlobalsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/SkyManager/Environment/GlobalTexture");
        public static readonly ProfilerMarker PrepareFrameSubsystemSkyCopyToFrameMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/SkyManager/CopyToFrame");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem");
        public static readonly ProfilerMarker PrepareAllMarker = new("VividRP.RenderPass.PrepareAll");
        public static readonly ProfilerMarker RecordRenderGraphMarker = new("VividRP.RenderPass.RecordRenderGraph");
        public static readonly ProfilerMarker RecordRenderGraphEnsureCompiledMarker = new("VividRP.RenderPass.RecordRenderGraph/EnsureCompiled");
        public static readonly ProfilerMarker RecordRenderGraphSetCurrentGraphMarker = new("VividRP.RenderPass.RecordRenderGraph/SetCurrentGraph");
        public static readonly ProfilerMarker RecordRenderGraphImportBlueNoiseMarker = new("VividRP.RenderPass.RecordRenderGraph/ImportBlueNoise");
        public static readonly ProfilerMarker RecordRenderGraphAllocatePassActiveStatesMarker = new("VividRP.RenderPass.RecordRenderGraph/AllocatePassActiveStates");
        public static readonly ProfilerMarker RecordRenderGraphPrepareAllResolveResourcesMarker = new("VividRP.RenderPass.PrepareAll/ResolveResources");
        public static readonly ProfilerMarker RecordRenderGraphPrepareAllEvaluateActiveMarker = new("VividRP.RenderPass.PrepareAll/EvaluateActive");
        public static readonly ProfilerMarker RecordRenderGraphPrepareAllPrepareActiveMarker = new("VividRP.RenderPass.PrepareAll/PrepareActive");
        public static readonly ProfilerMarker RecordRenderGraphPrepareAllApplyInactiveBypassMarker = new("VividRP.RenderPass.PrepareAll/ApplyInactiveBypassDescriptors");
        public static readonly ProfilerMarker RecordRenderGraphPrepareHistoryImportsMarker = new("VividRP.RenderPass.RecordRenderGraph/PrepareHistoryImports");
        public static readonly ProfilerMarker RecordRenderGraphClearResourceCachesMarker = new("VividRP.RenderPass.RecordRenderGraph/ClearResourceCaches");
        public static readonly ProfilerMarker RecordRenderGraphRecordPassesMarker = new("VividRP.RenderPass.RecordRenderGraph/RecordPasses");
        public static readonly ProfilerMarker RecordRenderGraphRecordGizmosMarker = new("VividRP.RenderPass.RecordRenderGraph/RecordGizmos");
        public static readonly ProfilerMarker RecordGraphSetupTexturesMarker = new("VividRP.RenderPass.RecordGraph.SetupResources/Textures");
        public static readonly ProfilerMarker RecordGraphSetupBuffersMarker = new("VividRP.RenderPass.RecordGraph.SetupResources/Buffers");
        public static readonly ProfilerMarker RecordGraphSetupRenderListsMarker = new("VividRP.RenderPass.RecordGraph.SetupResources/RenderLists");
        public static readonly ProfilerMarker RecordGraphSetupAccelerationStructuresMarker = new("VividRP.RenderPass.RecordGraph.SetupResources/AccelerationStructures");
        public static readonly ProfilerMarker CommitFrameMarker = new("VividRP.RenderPass.CommitFrame");
        public static readonly ProfilerMarker CommitFrameTextureHistoriesMarker = new("VividRP.RenderPass.CommitFrame/TextureHistories");
        public static readonly ProfilerMarker CommitFrameBufferHistoriesMarker = new("VividRP.RenderPass.CommitFrame/BufferHistories");
        public static readonly ProfilerMarker CommitFrameClearHistoryImportsMarker = new("VividRP.RenderPass.CommitFrame/ClearHistoryImports");
        public static readonly ProfilerMarker CommitFrameClearCodeManagedHistoryMarker = new("VividRP.RenderPass.CommitFrame/ClearCodeManagedHistory");
        public static readonly ProfilerMarker AllocHistoryTextureForPassMarker = new("VividRP.RenderPass.History.AllocTextureForPass");
        public static readonly ProfilerMarker AllocHistoryBufferForPassMarker = new("VividRP.RenderPass.History.AllocBufferForPass");

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
