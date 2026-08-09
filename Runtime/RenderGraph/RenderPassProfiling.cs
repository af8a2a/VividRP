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
            Resize = new ProfilerMarker($"{MarkerRoot}.Resize/{displayName}");
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
            Record = new ProfilingSampler($"{MarkerRoot}.Record/{displayName}");
            Dispose = new ProfilerMarker($"{MarkerRoot}.Dispose/{displayName}");
            DisplayName = displayName;
            GraphName = graphName;
        }

        public ProfilerMarker Create { get; }
        public ProfilerMarker Initialize { get; }
        public ProfilerMarker Resize { get; }
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
        public ProfilingSampler Record { get; }
        public ProfilerMarker Dispose { get; }
        public string DisplayName { get; }
        public string GraphName { get; }

        internal void Release()
        {
            Record?.Dispose();
        }
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
        public static readonly ProfilerMarker InitializeContextAntialiasingResolveHasPassMarker = new("VividRP.RenderPass.InitializeContext/Antialiasing.Resolve/HasPass");
        public static readonly ProfilerMarker InitializeContextAntialiasingResolveDataMarker = new("VividRP.RenderPass.InitializeContext/Antialiasing.Resolve/Data");
        public static readonly ProfilerMarker InitializeContextAntialiasingJitterMarker = new("VividRP.RenderPass.InitializeContext/Antialiasing.ApplyJitter");
        public static readonly ProfilerMarker InitializeContextUpdateCameraMatricesMarker = new("VividRP.RenderPass.InitializeContext/UpdateCameraMatrices");
        public static readonly ProfilerMarker InitializeContextPopulateCameraDataMarker = new("VividRP.RenderPass.InitializeContext/PopulateCameraData");
        public static readonly ProfilerMarker InitializeContextPopulateRenderingDataMarker = new("VividRP.RenderPass.InitializeContext/PopulateRenderingData");
        public static readonly ProfilerMarker InitializeContextLightDataUpdateMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update");
        public static readonly ProfilerMarker InitializeContextShadowDataUpdateMarker = new("VividRP.RenderPass.InitializeContext/ShadowData.Update");
        public static readonly ProfilerMarker InitializeContextLightDataDrainMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/Drain");
        public static readonly ProfilerMarker InitializeContextLightDataInputsMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/Inputs");
        public static readonly ProfilerMarker InitializeContextLightDataVisibleLightsMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/VisibleLights");
        public static readonly ProfilerMarker InitializeContextLightDataSceneLightCompleteMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/VisibleLights/SceneLightComplete");
        public static readonly ProfilerMarker InitializeContextLightDataEnsureBuffersMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/VisibleLights/EnsureBuffers");
        public static readonly ProfilerMarker InitializeContextLightDataCollectVisibleMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/VisibleLights/CollectVisible");
        public static readonly ProfilerMarker InitializeContextLightDataCollectSceneMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/VisibleLights/CollectScene");
        public static readonly ProfilerMarker InitializeContextLightDataDirectionalMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/VisibleLights/Directional");
        public static readonly ProfilerMarker InitializeContextLightDataScheduleMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/VisibleLights/ScheduleJobs");
        public static readonly ProfilerMarker InitializeContextLightDataReflectionProbesMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/ReflectionProbes");
        public static readonly ProfilerMarker InitializeContextLightDataReflectionProbeEnsureCapacityMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/ReflectionProbes/EnsureCapacity");
        public static readonly ProfilerMarker InitializeContextLightDataReflectionProbeBuildMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/ReflectionProbes/Build");
        public static readonly ProfilerMarker InitializeContextLightDataReflectionProbeBuildSpatialMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/ReflectionProbes/Build/Spatial");
        public static readonly ProfilerMarker InitializeContextLightDataReflectionProbeBuildBaseDataMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/ReflectionProbes/Build/BaseData");
        public static readonly ProfilerMarker InitializeContextLightDataReflectionProbeAdditionalDataMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/ReflectionProbes/Build/AdditionalData");
        public static readonly ProfilerMarker InitializeContextLightDataReflectionProbeApplyAdditionalDataMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/ReflectionProbes/Build/ApplyAdditionalData");
        public static readonly ProfilerMarker InitializeContextLightDataReflectionProbeApplyAdditionalDataSyncMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/ReflectionProbes/Build/ApplyAdditionalData/Sync");
        public static readonly ProfilerMarker InitializeContextLightDataReflectionProbeApplyAdditionalDataValuesMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/ReflectionProbes/Build/ApplyAdditionalData/Values");
        public static readonly ProfilerMarker InitializeContextLightDataReflectionProbePackResultMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/ReflectionProbes/Build/PackResult");
        public static readonly ProfilerMarker InitializeContextLightDataReflectionProbeStoreMarker = new("VividRP.RenderPass.InitializeContext/LightData.Update/ReflectionProbes/Build/Store");
        public static readonly ProfilerMarker InitializeContextSceneLightCompleteMarker = new("VividRP.RenderPass.InitializeContext/VividSceneLightSystem.Complete");
        public static readonly ProfilerMarker PrepareFrameMarker = new("VividRP.RenderPass.PrepareFrame");
        public static readonly ProfilerMarker PrepareFrameEnsureCompiledMarker = new("VividRP.RenderPass.PrepareFrame/EnsureCompiled");
        public static readonly ProfilerMarker PrepareFrameClearImportedHandlesMarker = new("VividRP.RenderPass.PrepareFrame/ClearImportedHandles");
        public static readonly ProfilerMarker PrepareFrameContextUpdateMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.Update");
        public static readonly ProfilerMarker PrepareFrameClearImportedTexturesMarker = new("VividRP.RenderPass.PrepareFrame/ClearImportedTextures");
        public static readonly ProfilerMarker PrepareFrameContextPurgeDestroyedCamerasMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.PurgeDestroyedCameras");
        public static readonly ProfilerMarker PrepareFrameContextResolveDataMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.ResolveData");
        public static readonly ProfilerMarker PrepareFrameContextAdvanceTemporalMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.AdvanceTemporal");
        public static readonly ProfilerMarker PrepareFrameContextPopulateTemporalMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.PopulateTemporal");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesDepthTextureModeMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables/DepthTextureMode");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesDimensionsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables/Dimensions");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesMatricesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables/Matrices");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesTemporalMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables/Temporal");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesFrustumPlanesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables/FrustumPlanes");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesPackMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables/Pack");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesPackCameraMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables/Pack/Camera");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesPackScreenMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables/Pack/Screen");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesPackRtHandleScaleMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables/Pack/RtHandleScale");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesPackMipBiasMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables/Pack/MipBias");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesPackMatricesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables/Pack/Matrices");
        public static readonly ProfilerMarker PrepareFrameContextBuildShaderVariablesPackResultMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables/Pack/Result");
        public static readonly ProfilerMarker PrepareFrameContextSetShaderGlobalsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SetShaderGlobals");
        public static readonly ProfilerMarker PrepareFrameContextSetShaderGlobalsBuildMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SetShaderGlobals/Build");
        public static readonly ProfilerMarker PrepareFrameContextSetShaderGlobalsStoreMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SetShaderGlobals/Store");
        public static readonly ProfilerMarker PrepareFrameContextSetShaderGlobalsPushConstantBufferMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SetShaderGlobals/PushConstantBuffer");
        public static readonly ProfilerMarker PrepareFrameContextSetShaderGlobalsVectorArraysMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SetShaderGlobals/VectorArrays");
        public static readonly ProfilerMarker PrepareFrameContextSetShaderGlobalsBlueNoiseMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SetShaderGlobals/BlueNoise");
        public static readonly ProfilerMarker PrepareFrameContextAdaptiveProbeVolumeMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.AdaptiveProbeVolume");
        public static readonly ProfilerMarker PrepareFrameContextSubsystemPreRenderMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender");
        public static readonly ProfilerMarker PrepareFrameSubsystemPerObjectBufferMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VividPerObjectBufferSystem");
        public static readonly ProfilerMarker PrepareFrameSubsystemAutoExposureMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VividAutoExposureSystem");
        public static readonly ProfilerMarker PrepareFrameSubsystemLTCAreaLightMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/LTCAreaLightSystem");
        public static readonly ProfilerMarker PrepareFrameSubsystemPreIntegratedFGDMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VividPreIntegratedFGDSystem");
        public static readonly ProfilerMarker PrepareFrameSubsystemDecalMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/DecalSystem");
        public static readonly ProfilerMarker PrepareFrameSubsystemDecalKickMarker = new("VividRP.PlayerLoop.PreLateUpdate/DecalSystem.Kick");
        public static readonly ProfilerMarker PrepareFrameSubsystemDecalCullScheduleMarker = new("VividRP.RenderPipeline.RenderCamera.DecalSystem.CullSchedule");
        public static readonly ProfilerMarker PrepareFrameSubsystemDecalCompleteMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/DecalSystem/Complete");
        public static readonly ProfilerMarker PrepareFrameSubsystemSceneLightKickMarker = new("VividRP.PlayerLoop.PreLateUpdate/VividSceneLightSystem.Kick");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenResolveAssetMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/ResolveAsset");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCameraDataMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/CameraData");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenPrepareFrameMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/PrepareFrame");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenPrepareFrameResetStatsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/PrepareFrame/ResetStats");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenPrepareFrameTextureBackendMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/PrepareFrame/TextureBackend");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/PrepareFrame/BuildSceneData");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataCollectReferencesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/PrepareFrame/BuildSceneData/CollectReferences");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataDetectChangesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/PrepareFrame/BuildSceneData/DetectChanges");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataClearSceneMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/PrepareFrame/BuildSceneData/ClearSceneData");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataAppendRenderersMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/PrepareFrame/BuildSceneData/AppendRenderers");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataInstanceDiffMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/PrepareFrame/BuildSceneData/InstanceDiff");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataSwapReferencesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/PrepareFrame/BuildSceneData/SwapReferences");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenPrepareFrameUploadBuffersMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/PrepareFrame/UploadBuffers");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenApplySettingsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/ApplySettings");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenResolveResourcesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/ResolveResources");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullBuildContextMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull/BuildContext");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullFrustumPlanesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull/BuildContext/FrustumPlanes");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullDispatchMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull/Dispatch");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullDispatchEnsureCapacityMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull/Dispatch/EnsureCapacity");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullDispatchResetBuffersMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull/Dispatch/ResetBuffers");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullDispatchEnsureKernelsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull/Dispatch/EnsureKernels");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullDispatchUploadContextsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull/Dispatch/UploadContexts");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullDispatchInstanceCullingMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull/Dispatch/InstanceCulling");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullDispatchMeshletListBuildMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull/Dispatch/MeshletListBuild");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullDispatchFixupDrawArgsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull/Dispatch/FixupDrawArgs");
        public static readonly ProfilerMarker PrepareFrameSubsystemGPUDrivenCullDispatchMeshletCullingMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/GPUDrivenSystem/Cull/Dispatch/MeshletCulling");
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
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFrameSetupMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/FrameSetup");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackReadbackMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/CollectReadbacks");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackReadbackPollMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/CollectReadbacks/Poll");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackReadbackCollectBatchesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/CollectReadbacks/CollectBatches");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackReadbackStatsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/ReadbackStats");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackAggregateMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/Aggregate");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackAggregateAccumulateMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/Aggregate/Accumulate");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackAggregateDecodeMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/Aggregate/Decode");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackAggregateSortMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/Aggregate/Sort");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackCountActiveViewMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/CountActiveView");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackGroupBySpaceMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/GroupBySpace");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackPrefetchBiasMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/PrefetchBias");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureTransitionsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Transitions");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureTransitionsCollectMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Transitions/CollectAndSortSpaces");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureTransitionsStartMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Transitions/StartQueued");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureTransitionsAdvanceMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Transitions/AdvancePhases");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsBeginFrameMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/BeginFrame");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCommitCompletedMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CommitCompleted");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyBudgetMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/Budget");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyBudgetPoolStatsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/Budget/PhysicalPoolStats");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyBudgetAssignMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/Budget/AssignPerSpace");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyDemandPassMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/DemandPass");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyPrefetchPassMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/PrefetchPass");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyProcessRequestsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/ProcessRequests");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyDemandMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/ProcessRequests/Demand");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyPrefetchMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/ProcessRequests/Prefetch");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyClassificationMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/ProcessRequests/Request/Classification");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyClassificationPrepareMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/ProcessRequests/Request/Classification/PrepareInputs");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyClassificationRunInlineMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/ProcessRequests/Request/Classification/RunInline");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyClassificationScheduleMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/ProcessRequests/Request/Classification/ScheduleAndComplete");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyApplyMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/ProcessRequests/Request/Apply");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyResidentTouchMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/ProcessRequests/Request/ResidentTouch");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyPendingPriorityMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/ProcessRequests/Request/PendingPriority");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyAttachLookupMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/ProcessRequests/Request/AttachLookup");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureResidencyAllocateEvictMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Residency/ProcessRequests/Request/AllocateEvict");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCollectPendingMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CollectPending");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCollectPendingRetireMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CollectPending/RetireProducerRequests");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCollectPendingGatherTasksMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CollectPending/GatherProducerTasks");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCollectPendingInFlightMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CollectPending/InFlightChecks");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCollectPendingOrderMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CollectPending/OrderRequests");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCollectPendingRequestPageMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CollectPending/RequestPageData");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCollectPendingProducePageMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CollectPending/ProducePageData");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCollectPendingEnqueueMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CollectPending/Enqueue");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCollectPendingBuildSpaceOrderMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CollectPending/BuildSpaceOrder");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCollectPendingGatherCandidatesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CollectPending/GatherCandidates");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCollectPendingSortCandidatesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CollectPending/SortCandidates");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsCollectPendingScheduleCandidatesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/CollectPending/ScheduleCandidates");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStreamBeginFrameMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Stream/BeginFrame");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStreamPollReadBatchesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Stream/BeginFrame/PollReadBatches");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStreamReplaceBackendMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Stream/BeginFrame/ReplaceBackend");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStreamPollDecodeTasksMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Stream/BeginFrame/PollDecodeTasks");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStreamTrimCacheMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Stream/BeginFrame/TrimCache");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStreamSubmitReadsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Stream/SubmitReads");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStreamSubmitReadsSortMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Stream/SubmitReads/Sort");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStreamSubmitReadsBuildBatchesMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Stream/SubmitReads/BuildBatches");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStreamSubmitReadsCreateIOMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Stream/SubmitReads/CreateIOBatch");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStreamStartDecodeMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Stream/StartDecode");
        public static readonly ProfilerMarker VirtualTextureStreamDecodeMarker = new("VividRP.VirtualTexture.Stream/Decode");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsFinalizeMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Finalize");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsFinalizePrepareMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Finalize/Prepare");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsFinalizeSortMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Finalize/Sort");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsFinalizeScheduleMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Finalize/ScheduleBatches");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsFinalizeRenderPayloadsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Finalize/ScheduleBatches/RenderPayloads");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsFinalizeRecordGpuMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Finalize/ScheduleBatches/RenderPayloads/GPU/RecordDispatches");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsFinalizeWriteCpuStagingMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Finalize/ScheduleBatches/RenderPayloads/CPU/WriteStaging");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsFinalizeApplyStagingMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Finalize/ScheduleBatches/CPU/ApplyStaging");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsFinalizeCopyToCacheMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Finalize/ScheduleBatches/CopyToCache");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureUploadsFinalizeSubmitMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Uploads/Finalize/ScheduleBatches/Submit");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTexturePageTableMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/PageTable");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTexturePageTableRebuildMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/PageTable/Rebuild");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTexturePageTableRefreshMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/PageTable/RefreshBuffer");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureCleanupMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Cleanup");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/PrepareTargets");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsEnsureCapacityMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/PrepareTargets/EnsureCapacity");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsPollMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/PrepareTargets/Poll");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsScheduleReadbackMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/PrepareTargets/ScheduleReadback");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsResetCounterMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Feedback/PrepareTargets/ResetCounter");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureBindingsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Bindings");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStatsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Stats");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStatsPhysicalPoolsMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Stats/PhysicalPools");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStatsAdaptiveMipBiasMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Stats/AdaptiveMipBias");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStatsReportGlobalMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Stats/ReportGlobal");
        public static readonly ProfilerMarker PrepareFrameSubsystemVirtualTextureStatsReportViewMarker = new("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender/VirtualTextureSystem/Stats/ReportView");
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
        public static readonly ProfilerMarker RecordRenderGraphClearResourceCachesMarker = new("VividRP.RenderPass.RecordRenderGraph/ClearResourceCaches");
        public static readonly ProfilerMarker RecordRenderGraphRecordPassesMarker = new("VividRP.RenderPass.RecordRenderGraph/RecordPasses");
        public static readonly ProfilerMarker RecordRenderGraphRecordGizmosMarker = new("VividRP.RenderPass.RecordRenderGraph/RecordGizmos");
        public static readonly ProfilerMarker RecordGraphSetupTexturesMarker = new("VividRP.RenderPass.RecordGraph.SetupResources/Textures");
        public static readonly ProfilerMarker RecordGraphSetupBuffersMarker = new("VividRP.RenderPass.RecordGraph.SetupResources/Buffers");
        public static readonly ProfilerMarker RecordGraphSetupRenderListsMarker = new("VividRP.RenderPass.RecordGraph.SetupResources/RenderLists");
        public static readonly ProfilerMarker RecordGraphSetupAccelerationStructuresMarker = new("VividRP.RenderPass.RecordGraph.SetupResources/AccelerationStructures");
        public static readonly ProfilerMarker CommitFrameMarker = new("VividRP.RenderPass.CommitFrame");
        public static readonly ProfilerMarker CommitFrameClearImportedHandlesMarker = new("VividRP.RenderPass.CommitFrame/ClearImportedHandles");

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
            foreach (var markers in s_Markers.Values)
                markers.Release();

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
