using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Runtime
{
    public static partial class PassRecorder
    {
        private static readonly List<IRenderPass> s_RenderPasses = new();
        private static readonly ContextContainer s_FrameData = new();
        private static readonly Dictionary<IRenderPass, PassResource> s_PassResources = new();
        private static readonly Dictionary<IRenderPass, int> s_PassIndices = new();
        private static readonly Dictionary<IRenderPass, Vector2Int> s_PassRenderSizes = new();
        private static readonly Dictionary<IRenderPass, Dictionary<string, AccessFlags>> s_PassResourceAccessOverrides = new();
        private static readonly Dictionary<RenderGraphTexture, RTHandle> s_ImportedRTHandles = new();
        private static readonly Dictionary<IRenderPass, List<ImportedPassHandle>> s_PassImportedHandles = new();
        private static readonly HashSet<RenderGraphTexture> s_FrameImportedTextures = new();
        private static readonly HashSet<RenderGraphBuffer> s_FrameImportedBuffers = new();
        private static readonly Dictionary<RTHandle, TextureHandle> s_TextureImportCache = new(16);
        private static readonly Dictionary<GraphicsBuffer, BufferHandle> s_BufferImportCache = new(16);
        private static readonly Dictionary<RenderGraphTexture, TextureHandle> s_RecordGraphTextureCache = new(64);
        private static readonly Dictionary<RenderGraphBuffer, BufferHandle> s_RecordGraphBufferCache = new(16);
        private static readonly Dictionary<RenderGraphRenderList, RendererListHandle> s_RecordGraphRenderListCache = new(16);
        private static readonly Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> s_RecordGraphAccelerationStructureCache = new(8);
        private static bool[] s_PassActiveStates = Array.Empty<bool>();
        private static RenderGraph s_CurrentRenderGraph;
        private static int s_InactiveBypassVersion;

        private static RenderGraphData s_CurrentGraphAsset;
        private static List<RenderGraphPassDefinition> s_RuntimePassDefinitions = new();
        private static long s_CurrentImportVersion;
        private static bool s_IsCompiled;
        private static bool s_RenderedPreImageEffectGizmosInGraph;
        private static bool s_HasCascadedShadowCasterPass;
        private static bool s_HasMeshletShadowPass;
        private static int s_EditModeFrameIndex;

        internal static bool UsesExperimentalMeshShaderRasterization
        {
            get
            {
                foreach (IRenderPass pass in s_RenderPasses)
                {
                    if (pass is VisibilityBufferPass visibilityBufferPass
                        && visibilityBufferPass.RasterizationPath
                        == VisibilityBufferRasterizationPath.ExperimentalMeshShader)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

#if UNITY_EDITOR
        private sealed class RenderGizmosPassData
        {
            public RendererListHandle GizmoRendererList;
            public Texture ExposureTexture;
            public TextureHandle Color;
            public TextureHandle Depth;
            public bool HasDepth;
        }

        private static readonly BaseRenderFunc<RenderGizmosPassData, UnsafeGraphContext> s_RenderGizmosRenderFunc =
            ExecuteRenderGizmosPass;
#endif
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void InitializeContext(
            ScriptableRenderContext context,
            Camera camera,
            CullingResults cullingResults,
            RenderGraphData graphAsset,
            int pipelineFrameIndex = -1)
        {
            using var initializeContextScope = RenderPassProfilingUtility.InitializeContextMarker.Auto();

            using (RenderPassProfilingUtility.InitializeContextClearFrameCachesMarker.Auto())
            {
                ColorGradingSettingsResolver.ClearFrameCache(s_FrameData);
                VividColorPyramidRuntimeUtility.ClearFrameCache(s_FrameData);
                VividScreenSpaceReflectionRuntimeUtility.ClearFrameCache(s_FrameData);
            }

            VividRenderingData renderingData;
            VividCameraData cameraData;
            VividAntialiasingData antialiasingData;
            VividGPUDrivenFrameData gpuDrivenFrameData;
            VividGPUDrivenDecalData gpuDrivenDecalData;
            VividLightData lightData;
            VividShadowData shadowData;
            using (RenderPassProfilingUtility.InitializeContextResolveFrameDataMarker.Auto())
            {
                renderingData = s_FrameData.GetOrCreate<VividRenderingData>();
                cameraData = s_FrameData.GetOrCreate<VividCameraData>();
                antialiasingData = s_FrameData.GetOrCreate<VividAntialiasingData>();
                gpuDrivenFrameData = s_FrameData.GetOrCreate<VividGPUDrivenFrameData>();
                gpuDrivenDecalData = s_FrameData.GetOrCreate<VividGPUDrivenDecalData>();
                lightData = s_FrameData.GetOrCreate<VividLightData>();
                shadowData = s_FrameData.GetOrCreate<VividShadowData>();
            }

            int frameIndex;
            using (RenderPassProfilingUtility.InitializeContextResolveFrameIndexMarker.Auto())
            {
                frameIndex = pipelineFrameIndex >= 0
                    ? pipelineFrameIndex
                    : ResolveFrameIndex();
            }

            VividAdditionalCameraData additionalCameraData;
            using (RenderPassProfilingUtility.InitializeContextResolveAdditionalCameraDataMarker.Auto())
            {
                additionalCameraData = camera.GetComponent<VividAdditionalCameraData>();
                if (additionalCameraData == null && camera.cameraType == CameraType.Game)
                    additionalCameraData = camera.GetVividAdditionalCameraData();
            }

            using (RenderPassProfilingUtility.InitializeContextAntialiasingResolveMarker.Auto())
            {
                bool hasAntialiasingPass;
                using (RenderPassProfilingUtility.InitializeContextAntialiasingResolveHasPassMarker.Auto())
                {
                    hasAntialiasingPass = HasAntialiasingPass(graphAsset);
                }

                using (RenderPassProfilingUtility.InitializeContextAntialiasingResolveDataMarker.Auto())
                {
                    VividAntialiasingRuntimeUtility.Resolve(
                        camera,
                        additionalCameraData,
                        hasAntialiasingPass,
                        antialiasingData);
                }
            }

            using (RenderPassProfilingUtility.InitializeContextAntialiasingJitterMarker.Auto())
            {
                VividAntialiasingRuntimeUtility.ApplyJitter(camera, additionalCameraData, antialiasingData, frameIndex);
            }

            using (RenderPassProfilingUtility.InitializeContextUpdateCameraMatricesMarker.Auto())
            {
                if (additionalCameraData != null)
                    additionalCameraData.UpdateCameraMatrices(true);
            }

            using (RenderPassProfilingUtility.InitializeContextPopulateCameraDataMarker.Auto())
            {
                cameraData.SetCamera(camera);
                cameraData.additionalData = additionalCameraData;
                cameraData.renderType = additionalCameraData != null ? additionalCameraData.renderType : VividCameraRenderType.Base;
                cameraData.clearDepth = additionalCameraData == null || additionalCameraData.clearDepth;
                cameraData.pixelWidth = camera.pixelWidth;
                cameraData.pixelHeight = camera.pixelHeight;
                cameraData.pixelRect = camera.pixelRect;
                cameraData.CacheCameraFrameProperties(camera);
                var actualSize = ResolveActualCameraSize(camera, antialiasingData);
                cameraData.actualWidth = actualSize.x;
                cameraData.actualHeight = actualSize.y;
                cameraData.frameIndex = frameIndex;
                cameraData.hdrOutputAllowed = VividHDROutputUtility.HDROutputAllowedForCamera(camera);
                cameraData.hdrOutputActive = VividHDROutputUtility.HDROutputActiveForCamera(camera);
                cameraData.hdrDisplayInformation = cameraData.hdrOutputActive
                    ? VividHDROutputUtility.HDRDisplayInformationForCamera(camera)
                    : VividHDROutputUtility.DefaultHDRDisplayInformation;
                cameraData.hdrDisplayColorGamut = cameraData.hdrOutputActive
                    ? VividHDROutputUtility.HDRDisplayColorGamutForCamera(camera)
                    : ColorGamut.sRGB;
            }

            using (RenderPassProfilingUtility.InitializeContextPopulateRenderingDataMarker.Auto())
            {
                renderingData.cullingResults = cullingResults;
                renderingData.context = context;
                gpuDrivenFrameData.Reset();
                gpuDrivenDecalData.Reset();
            }

            using (RenderPassProfilingUtility.InitializeContextLightDataUpdateMarker.Auto())
            {
                lightData.Update(cullingResults, cameraData.viewMatrix);
            }

            using (RenderPassProfilingUtility.InitializeContextShadowDataUpdateMarker.Auto())
            {
                shadowData.Update(cullingResults, lightData, cameraData);
            }

            using (RenderPassProfilingUtility.InitializeContextShadowCasterCullingMarker.Auto())
            {
                // Unity schedules renderer shadow-culling jobs here. Start them before subsystem
                // and pass preparation, but skip Meshlet-only graphs that never consume the results.
                if (s_HasCascadedShadowCasterPass)
                    shadowData.ScheduleShadowCasterCulling(context, cullingResults);
            }
        }

        private static int ResolveFrameIndex()
        {
            if (Application.isPlaying)
                return Time.frameCount;

            unchecked
            {
                s_EditModeFrameIndex++;
            }

            if (s_EditModeFrameIndex < 0)
                s_EditModeFrameIndex = 1;

            return s_EditModeFrameIndex;
        }

        private static Vector2Int ResolveActualCameraSize(Camera camera, VividAntialiasingData antialiasingData)
        {
            if (camera == null)
                return Vector2Int.one;

            if (antialiasingData != null
                && antialiasingData.hasAntialiasingPass
                && antialiasingData.renderSize.x > 0
                && antialiasingData.renderSize.y > 0)
            {
                return antialiasingData.renderSize;
            }

            var width = camera.scaledPixelWidth > 0 ? camera.scaledPixelWidth : camera.pixelWidth;
            var height = camera.scaledPixelHeight > 0 ? camera.scaledPixelHeight : camera.pixelHeight;
            width = Mathf.Max(1, width > 0 ? width : Screen.width);
            height = Mathf.Max(1, height > 0 ? height : Screen.height);
            return new Vector2Int(width, height);
        }

        internal static void SetGPUDrivenFrameData(
            GraphicsBuffer visibleMeshletRenderRequestsBuffer,
            GraphicsBuffer visibleMeshletIndirectDrawArgsBuffer,
            PrimitiveScene.VividPrimitiveDrawSet primitiveDrawSet = null,
            PrimitiveScene.VividPrimitiveDrawSet primitiveShadowDrawSet = null)
        {
            var gpuDrivenFrameData = s_FrameData.GetOrCreate<VividGPUDrivenFrameData>();
            gpuDrivenFrameData.visibleMeshletRenderRequestsBuffer = visibleMeshletRenderRequestsBuffer;
            gpuDrivenFrameData.visibleMeshletIndirectDrawArgsBuffer = visibleMeshletIndirectDrawArgsBuffer;
            gpuDrivenFrameData.primitiveDrawSet = primitiveDrawSet;
            gpuDrivenFrameData.primitiveShadowDrawSet = primitiveShadowDrawSet;
            gpuDrivenFrameData.ResetOcclusion();
        }

        internal static void SetGPUDrivenOcclusionFrameData(
            bool enabled,
            bool historyValid,
            GPUDriven.VividGPUDrivenCullingBuffers buffers,
            bool observationMode = false,
            GPUDriven.VividGPUDrivenOcclusionCullingParameters observationRetestParameters = default)
        {
            var gpuDrivenFrameData = s_FrameData.GetOrCreate<VividGPUDrivenFrameData>();
            gpuDrivenFrameData.ResetOcclusion();
            if (!enabled || buffers == null || !buffers.SupportsOcclusion)
                return;

            gpuDrivenFrameData.occlusionCullingEnabled = true;
            gpuDrivenFrameData.occlusionHistoryValid = historyValid;
            gpuDrivenFrameData.occlusionObservationMode = observationMode;
            gpuDrivenFrameData.occludedMeshletRenderRequestsBuffer = buffers.OccludedMeshletRenderRequestsBuffer;
            gpuDrivenFrameData.occludedMeshletRenderRequestCounterBuffer = buffers.OccludedMeshletRenderRequestCounterBuffer;
            gpuDrivenFrameData.occludedMeshletIndirectDispatchArgsBuffer = buffers.OccludedMeshletIndirectDispatchArgsBuffer;
            gpuDrivenFrameData.recoveredMeshletRenderRequestsBuffer = buffers.RecoveredMeshletRenderRequestsBuffer;
            gpuDrivenFrameData.recoveredRendererListMeshletCountsBuffer = buffers.RecoveredRendererListMeshletCountsBuffer;
            gpuDrivenFrameData.recoveredMeshletIndirectDrawArgsBuffer = buffers.RecoveredMeshletIndirectDrawArgsBuffer;
            gpuDrivenFrameData.observationRetestParameters = observationRetestParameters;
        }

        internal static ContextContainer GetFrameData()
        {
            return s_FrameData;
        }

        private static int ResolvePassIndex(IRenderPass pass)
        {
            return pass != null && s_PassIndices.TryGetValue(pass, out var passIndex)
                ? passIndex
                : -1;
        }

        private static RenderPassProfilerMarkers GetPassMarkers(IRenderPass pass, string displayName = null)
        {
            var passIndex = ResolvePassIndex(pass);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                var passDefinition = GetRuntimePassDefinition(s_RuntimePassDefinitions, passIndex);
                displayName = passDefinition?.PassName;
            }

            return RenderPassProfilingUtility.GetMarkers(pass, displayName, passIndex);
        }

        private static void CreateRenderPass(IRenderPass pass, string displayName = null)
        {
            if (pass == null)
                return;

            var markers = GetPassMarkers(pass, displayName);
            using (markers.Create.Auto())
            {
                pass.Create();
            }
        }

        private static void PrepareRenderPass(IRenderPass pass, string displayName = null)
        {
            if (pass == null)
                return;

            var markers = GetPassMarkers(pass, displayName);
            var cameraData = s_FrameData.GetOrCreate<VividCameraData>();
            var renderSize = new Vector2Int(
                CameraDimensionUtility.ResolveCameraDimension(
                    cameraData.actualWidth,
                    cameraData.pixelWidth,
                    Screen.width),
                CameraDimensionUtility.ResolveCameraDimension(
                    cameraData.actualHeight,
                    cameraData.pixelHeight,
                    Screen.height));

            if (!s_PassRenderSizes.TryGetValue(pass, out var previousRenderSize)
                || previousRenderSize != renderSize)
            {
                using (markers.Resize.Auto())
                {
                    pass.Resize(renderSize.x, renderSize.y);
                }

                s_PassRenderSizes[pass] = renderSize;
            }

            using (markers.Prepare.Auto())
            {
                pass.Prepare(s_FrameData);
            }
        }

        private static void PrepareRenderGraphPass(IRenderPass pass)
        {
            if (pass is IRenderGraphPreparePass preparePass)
                preparePass.PrepareRenderGraph(s_FrameData);
        }

        private static void DisposeRenderPass(IRenderPass pass, string displayName = null)
        {
            if (pass == null)
                return;

            var markers = GetPassMarkers(pass, displayName);
            using (markers.Dispose.Auto())
            {
                pass.Dispose();
            }
        }

        public static void Dispose()
        {
            foreach (var pass in s_RenderPasses)
            {
                try
                {
                    DisposeRenderPass(pass);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            DisposeAccelerationStructures();
            s_RenderPasses.Clear();
            s_PassResources.Clear();
            s_PassIndices.Clear();
            s_PassRenderSizes.Clear();
            s_PassResourceAccessOverrides.Clear();
            s_PassActiveStates = Array.Empty<bool>();
            ClearFrameImportedHandles();
            ClearImportedTextures();
            s_PassImportedHandles.Clear();
            ClearRecordGraphResourceCaches();
            VividAntialiasingRuntimeUtility.Clear();
            if (s_FrameData.Contains<VividLightData>())
                s_FrameData.Get<VividLightData>().ReleaseLightGridNativeResources();
            FrameContextSystem.Clear();
            VividRayTracingAccelerationStructureStatsRegistry.Clear();
            s_EditModeFrameIndex = 0;
            s_CurrentGraphAsset = null;
            s_RuntimePassDefinitions.Clear();
            s_CurrentImportVersion = 0;
            s_IsCompiled = false;
            s_HasCascadedShadowCasterPass = false;
            s_HasMeshletShadowPass = false;
            RenderPassProfilingUtility.Clear();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void PrepareFrame(RenderGraphData graphAsset, CommandBuffer cmdBuffer)
        {
            using var prepareFrameScope = RenderPassProfilingUtility.PrepareFrameMarker.Auto();
            using (RenderPassProfilingUtility.PrepareFrameEnsureCompiledMarker.Auto())
            {
                EnsureCompiled(graphAsset);
            }

            using (RenderPassProfilingUtility.PrepareFrameClearImportedHandlesMarker.Auto())
            {
                ClearFrameImportedHandles();
            }

            s_RenderedPreImageEffectGizmosInGraph = false;

            // Advance temporal state and set all shader globals before any pass executes.
            using (RenderPassProfilingUtility.PrepareFrameContextUpdateMarker.Auto())
            {
                FrameContextSystem.Update(s_FrameData, cmdBuffer);
            }

            using (RenderPassProfilingUtility.PrepareFrameClearImportedTexturesMarker.Auto())
            {
                ClearImportedTextures();
            }
        }

        internal static void AbortFrame()
        {
            VirtualTextureSystem.AbortPageTableUpdates();
            ClearImportedTextures();
            ClearFrameImportedHandles();
            s_RenderedPreImageEffectGizmosInGraph = false;
        }

        /// <summary>
        /// Imports an external RTHandle for a specific pass during Prepare().
        /// Returns a TextureHandle that can be assigned to pass member variables.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TextureHandle ImportTextureForPass(IRenderPass pass, RTHandle rtHandle, AccessFlags access = AccessFlags.Read)
        {
            var handle = ImportTextureHandle(rtHandle);
            RegisterImportedTextureForPass(pass, handle, access);
            return handle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TextureHandle ImportTextureHandle(RTHandle rtHandle)
        {
            if (s_CurrentRenderGraph == null)
            {
                Debug.LogWarning("[VividRP] Cannot import texture: RenderGraph is not active. Call Import() only during Prepare().");
                return default;
            }

            if (rtHandle == null)
            {
                Debug.LogWarning("[VividRP] Cannot import texture: RTHandle is null");
                return default;
            }

            if (!s_TextureImportCache.TryGetValue(rtHandle, out var handle))
            {
                handle = s_CurrentRenderGraph.ImportTexture(rtHandle);
                s_TextureImportCache.Add(rtHandle, handle);
            }

            return handle;
        }

        internal static void BindImportedTexture(RenderGraphTexture texture, TextureHandle handle)
        {
            if (texture == null || !handle.IsValid())
                return;

            texture.SetImportedHandle(handle);
            s_FrameImportedTextures.Add(texture);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static BufferHandle ImportBufferForPass(
            IRenderPass pass,
            GraphicsBuffer graphicsBuffer,
            AccessFlags access = AccessFlags.Read)
        {
            var handle = ImportBufferHandle(graphicsBuffer);
            RegisterImportedBufferForPass(pass, handle, access);
            return handle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static BufferHandle ImportBufferHandle(GraphicsBuffer graphicsBuffer)
        {
            if (s_CurrentRenderGraph == null)
            {
                Debug.LogWarning("[VividRP] Cannot import buffer: RenderGraph is not active. Call Import() only during Prepare().");
                return default;
            }

            if (graphicsBuffer == null)
            {
                Debug.LogWarning("[VividRP] Cannot import buffer: GraphicsBuffer is null");
                return default;
            }

            if (!s_BufferImportCache.TryGetValue(graphicsBuffer, out var handle))
            {
                handle = s_CurrentRenderGraph.ImportBuffer(graphicsBuffer);
                s_BufferImportCache.Add(graphicsBuffer, handle);
            }

            return handle;
        }

        internal static void BindImportedBuffer(RenderGraphBuffer buffer, BufferHandle handle)
        {
            if (buffer == null || !handle.IsValid())
                return;

            buffer.SetImportedHandle(handle);
            s_FrameImportedBuffers.Add(buffer);
        }

        internal static bool IsPassTextureImportActive => s_CurrentRenderGraph != null;

        internal static void RegisterImportedTextureForPass(
            IRenderPass pass,
            TextureHandle handle,
            AccessFlags access = AccessFlags.Read)
        {
            if (pass == null || !handle.IsValid())
                return;

            var importedHandle = ImportedPassHandle.Texture(handle, access);
            RegisterImportedHandleForPass(pass, importedHandle);
        }

        internal static void RegisterImportedBufferForPass(
            IRenderPass pass,
            BufferHandle handle,
            AccessFlags access = AccessFlags.Read)
        {
            if (pass == null || !handle.IsValid())
                return;

            var importedHandle = ImportedPassHandle.Buffer(handle, access);
            RegisterImportedHandleForPass(pass, importedHandle);
        }

        private static void RegisterImportedHandleForPass(IRenderPass pass, ImportedPassHandle importedHandle)
        {
            if (!s_PassImportedHandles.TryGetValue(pass, out var handles))
            {
                handles = new List<ImportedPassHandle>(32);
                s_PassImportedHandles[pass] = handles;
            }

            if (!handles.Contains(importedHandle))
                handles.Add(importedHandle);
        }

        /// <summary>
        /// Imports an external RTHandle into a RenderGraphTexture for use in passes.
        /// The imported texture will be available for the current frame only.
        /// Call this before RecordRenderGraph() to make the external resource available to passes.
        /// </summary>
        /// <param name="texture">The RenderGraphTexture to import into</param>
        /// <param name="rtHandle">The external RTHandle to import</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ImportTexture(RenderGraphTexture texture, RTHandle rtHandle)
        {
            if (texture == null)
            {
                Debug.LogWarning("[VividRP] Cannot import texture: RenderGraphTexture is null");
                return;
            }

            if (rtHandle == null)
            {
                Debug.LogWarning("[VividRP] Cannot import texture: RTHandle is null");
                return;
            }

            s_ImportedRTHandles[texture] = rtHandle;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void CommitFrame()
        {
            using var commitFrameScope = RenderPassProfilingUtility.CommitFrameMarker.Auto();
            VirtualTextureSystem.CommitPageTableUpdates();
            using (RenderPassProfilingUtility.CommitFrameClearImportedHandlesMarker.Auto())
            {
                ClearFrameImportedHandles();
            }
        }

        internal static bool HasRenderGizmoPrePostProcessBoundary(RenderGraphData graphAsset)
        {
            EnsureCompiled(graphAsset);
            return HasRenderGizmoPrePostProcessBoundary(s_RenderPasses);
        }

        internal static bool HasAntialiasingPass(RenderGraphData graphAsset)
        {
            EnsureCompiled(graphAsset);
            return HasAntialiasingPass(s_RenderPasses);
        }

        internal static bool HasCascadedShadowCasterPass => s_HasCascadedShadowCasterPass;

        internal static bool HasMeshletShadowPass => s_HasMeshletShadowPass;

        internal static void RegisterCascadedShadowCasterPass()
        {
            s_HasCascadedShadowCasterPass = true;
        }

        internal static void RegisterMeshletShadowPass()
        {
            s_HasMeshletShadowPass = true;
        }

        internal static bool HasRenderGizmoPrePostProcessBoundary(IReadOnlyList<IRenderPass> renderPasses)
        {
            if (renderPasses == null)
                return false;

            for (var index = 0; index < renderPasses.Count; index++)
            {
                var renderPass = renderPasses[index];
                if (renderPass is IRenderGizmoPrePostProcessBoundaryPass)
                    return true;
            }

            return false;
        }

        internal static bool HasAntialiasingPass(IReadOnlyList<IRenderPass> renderPasses)
        {
            if (renderPasses == null)
                return false;

            for (var index = 0; index < renderPasses.Count; index++)
            {
                var renderPass = renderPasses[index];
                if (renderPass is AntialiasingPass)
                    return true;
            }

            return false;
        }

        internal static bool ShouldRenderPreImageEffectGizmosOutsideRenderGraph(RenderGraphData graphAsset)
        {
            return ShouldRenderPreImageEffectGizmosOutsideRenderGraph(
                HasRenderGizmoPrePostProcessBoundary(graphAsset),
                s_RenderedPreImageEffectGizmosInGraph);
        }

        internal static bool ShouldRenderPreImageEffectGizmosOutsideRenderGraph(
            bool hasRenderGizmoPrePostProcessBoundary,
            bool renderedPreImageEffectGizmosInGraph)
        {
            return !hasRenderGizmoPrePostProcessBoundary || !renderedPreImageEffectGizmosInGraph;
        }

        /// <summary>
        /// Clears all imported textures at the start of each frame.
        /// </summary>
        private static void ClearImportedTextures()
        {
            foreach (var importedTexture in s_ImportedRTHandles.Keys)
            {
                importedTexture?.ClearImportedHandle();
            }

            s_ImportedRTHandles.Clear();
            foreach (var handles in s_PassImportedHandles.Values)
                handles?.Clear();
            s_TextureImportCache.Clear();
            s_BufferImportCache.Clear();
            s_CurrentRenderGraph = null;
        }

        private static void ClearFrameImportedHandles()
        {
            foreach (var texture in s_FrameImportedTextures)
            {
                texture?.ClearImportedHandle();
            }
            foreach (var buffer in s_FrameImportedBuffers)
            {
                buffer?.ClearImportedHandle();
            }

            s_FrameImportedTextures.Clear();
            s_FrameImportedBuffers.Clear();
        }

        private static void EnsureCompiled(RenderGraphData graphAsset)
        {
            if (s_IsCompiled && s_CurrentGraphAsset == graphAsset)
            {
                if (graphAsset == null || graphAsset.ImportVersion == s_CurrentImportVersion)
                    return;
            }

            Compile(graphAsset);
        }

        private static void Compile(RenderGraphData graphAsset)
        {
            using var compileScope = RenderPassProfilingUtility.CompileMarker.Auto();
            Dispose();

            if (graphAsset == null)
            {
                // Fallback graph (keeps the pipeline running without an authored asset).
                var fallbackPass = new FullScreenPass();
                s_PassIndices[fallbackPass] = 0;
                s_RenderPasses.Add(fallbackPass);
                s_RuntimePassDefinitions.Add(null);
            }
            else
            {
                
                var passDefinitions = graphAsset.Passes ?? new List<RenderGraphPassDefinition>();
                var textures = CreateRuntimeTextures(graphAsset);
                var buffers = CreateRuntimeBuffers(graphAsset);
                var renderLists = CreateRuntimeRenderLists(graphAsset);
                var accelerationStructures = CreateRuntimeAccelerationStructures(graphAsset);
                var indexedPasses = new IRenderPass[passDefinitions.Count];
                var indexedPassTypes = new Type[passDefinitions.Count];

                for (var passIndex = 0; passIndex < passDefinitions.Count; passIndex++)
                {
                    var passDef = passDefinitions[passIndex];
                    if (string.IsNullOrEmpty(passDef?.PassType))
                        continue;

                    var passType = ResolveType(passDef.PassType);
                    if (passType == null)
                    {
                        Debug.LogWarning($"[VividRP] Could not resolve pass type: {passDef.PassType}");
                        continue;
                    }

                    if (!typeof(IRenderPass).IsAssignableFrom(passType))
                    {
                        Debug.LogWarning($"[VividRP] Pass type does not implement IRenderPass: {passType.FullName}");
                        continue;
                    }

                    IRenderPass pass;
                    try
                    {
                        pass = (IRenderPass)Activator.CreateInstance(passType);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        continue;
                    }

                    RenderGraphPassFloatParameterUtility.ApplyFloatParameters(pass, passType, passDef?.FloatParameters);
                    RenderGraphPassEnumParameterUtility.ApplyEnumParameters(pass, passType, passDef?.EnumParameters);
                    RenderGraphPassRenderListDescParameterUtility.ApplyParameters(
                        pass,
                        passType,
                        passDef?.RenderListDescParameters);

                    ApplyResourceBindings(
                        pass,
                        passType,
                        passDef,
                        textures,
                        buffers,
                        renderLists,
                        accelerationStructures);

                    var accessOverrides = BuildResourceAccessOverrides(passType, passDef);
                    if (accessOverrides != null && accessOverrides.Count > 0)
                        s_PassResourceAccessOverrides[pass] = accessOverrides;

                    indexedPasses[passIndex] = pass;
                    indexedPassTypes[passIndex] = passType;
                    s_PassIndices[pass] = s_RenderPasses.Count;
                    s_RenderPasses.Add(pass);
                    s_RuntimePassDefinitions.Add(passDef);
                }

                for (var passIndex = 0; passIndex < passDefinitions.Count; passIndex++)
                {
                    var pass = indexedPasses[passIndex];
                    var passType = indexedPassTypes[passIndex];
                    if (pass == null || passType == null)
                        continue;

                    ApplyPassFieldBindings(
                        passIndex,
                        pass,
                        passType,
                        passDefinitions,
                        indexedPasses,
                        indexedPassTypes);
                }
            }

            foreach (var pass in s_RenderPasses)
            {
                CreateRenderPass(pass);
                GetCurrentPassResources(pass);
            }

            GetPassActiveStatesBuffer(s_RenderPasses.Count);

            s_CurrentGraphAsset = graphAsset;
            s_CurrentImportVersion = graphAsset != null ? graphAsset.ImportVersion : 0;
            s_IsCompiled = true;
        }

        internal static bool[] GetPassActiveStatesBuffer(int passCount)
        {
            if (s_PassActiveStates.Length < passCount)
                s_PassActiveStates = new bool[passCount];

            return s_PassActiveStates;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RenderGraphTexture[] CreateRuntimeTextures(RenderGraphData graphAsset)
        {
            if (graphAsset.TextureDescriptors == null || graphAsset.TextureDescriptors.Count == 0)
                return Array.Empty<RenderGraphTexture>();

            var textures = new RenderGraphTexture[graphAsset.TextureDescriptors.Count];
            for (var i = 0; i < textures.Length; i++)
            {
                var texture = new RenderGraphTexture();
                var desc = graphAsset.TextureDescriptors[i];
                texture.desc = desc != null ? desc.Clone() : new RenderGraphTextureDesc();
                textures[i] = texture;
            }

            return textures;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RenderGraphBuffer[] CreateRuntimeBuffers(RenderGraphData graphAsset)
        {
            if (graphAsset.BufferDescriptors == null || graphAsset.BufferDescriptors.Count == 0)
                return Array.Empty<RenderGraphBuffer>();

            var buffers = new RenderGraphBuffer[graphAsset.BufferDescriptors.Count];
            for (var i = 0; i < buffers.Length; i++)
            {
                var buffer = new RenderGraphBuffer();
                var desc = graphAsset.BufferDescriptors[i];
                buffer.desc = desc != null ? desc.Clone() : new RenderGraphBufferDesc();
                buffers[i] = buffer;
            }

            return buffers;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RenderGraphRenderList[] CreateRuntimeRenderLists(RenderGraphData graphAsset)
        {
            if (graphAsset.RenderListDescriptors == null || graphAsset.RenderListDescriptors.Count == 0)
                return Array.Empty<RenderGraphRenderList>();

            var renderLists = new RenderGraphRenderList[graphAsset.RenderListDescriptors.Count];
            for (var i = 0; i < renderLists.Length; i++)
            {
                var renderList = new RenderGraphRenderList();
                var desc = graphAsset.RenderListDescriptors[i];
                renderList.desc = desc != null ? desc.Clone() : new RenderGraphRenderListDesc();
                renderLists[i] = renderList;
            }

            return renderLists;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RenderGraphAccelerationStructure[] CreateRuntimeAccelerationStructures(RenderGraphData graphAsset)
        {
            if (graphAsset.AccelerationStructureDescriptors == null || graphAsset.AccelerationStructureDescriptors.Count == 0)
                return Array.Empty<RenderGraphAccelerationStructure>();

            var accelerationStructures = new RenderGraphAccelerationStructure[graphAsset.AccelerationStructureDescriptors.Count];
            for (var i = 0; i < accelerationStructures.Length; i++)
            {
                var accelerationStructure = new RenderGraphAccelerationStructure();
                var desc = graphAsset.AccelerationStructureDescriptors[i];
                accelerationStructure.desc = desc != null
                    ? desc.Clone()
                    : new RenderGraphAccelerationStructureDesc();
                accelerationStructures[i] = accelerationStructure;
            }

            return accelerationStructures;
        }

        private static Type ResolveType(string assemblyQualifiedOrFullName)
        {
            var type = Type.GetType(assemblyQualifiedOrFullName, throwOnError: false);
            if (type != null)
                return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(assemblyQualifiedOrFullName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplyResourceBindings(
            IRenderPass pass,
            Type passType,
            RenderGraphPassDefinition passDef,
            RenderGraphTexture[] textures,
            RenderGraphBuffer[] buffers,
            RenderGraphRenderList[] renderLists,
            RenderGraphAccelerationStructure[] accelerationStructures)
        {
            if (passDef.ResourceBindings == null || passDef.ResourceBindings.Count == 0)
                return;

            foreach (var binding in passDef.ResourceBindings)
            {
                if (binding == null || string.IsNullOrEmpty(binding.FieldName))
                    continue;

                var field = passType.GetInstanceField(binding.FieldName);
                if (field == null)
                    continue;

                if (field.IsDeclaredTransientResourceField())
                {
                    LogSkippedTransientResourceBinding(passType, binding.FieldName);
                    continue;
                }

                if (binding.SourceKind == RenderGraphPassBindingSourceKind.PassField)
                    continue;

                switch (binding.ResourceKind)
                {
                    case RenderGraphResourceKind.Texture:
                        if (field.FieldType != typeof(RenderGraphTexture))
                            break;

                        if (binding.ResourceIndex >= 0 && binding.ResourceIndex < textures.Length)
                        {
                            field.SetValue(pass, textures[binding.ResourceIndex]);
                        }
                        break;
                    case RenderGraphResourceKind.Buffer:
                        if (binding.ResourceIndex >= 0 && binding.ResourceIndex < buffers.Length &&
                            field.FieldType == typeof(RenderGraphBuffer))
                        {
                            field.SetValue(pass, buffers[binding.ResourceIndex]);
                        }
                        break;
                    case RenderGraphResourceKind.RenderList:
                        if (binding.ResourceIndex >= 0 && binding.ResourceIndex < renderLists.Length &&
                            field.FieldType == typeof(RenderGraphRenderList))
                        {
                            field.SetValue(pass, renderLists[binding.ResourceIndex]);
                        }
                        break;
                    case RenderGraphResourceKind.AccelerationStructure:
                        if (binding.ResourceIndex >= 0 && binding.ResourceIndex < accelerationStructures.Length &&
                            field.FieldType == typeof(RenderGraphAccelerationStructure))
                        {
                            field.SetValue(pass, accelerationStructures[binding.ResourceIndex]);
                        }
                        break;
                }
            }
        }

        private static void ApplyPassFieldBindings(
            int passIndex,
            IRenderPass pass,
            Type passType,
            IReadOnlyList<RenderGraphPassDefinition> passDefinitions,
            IReadOnlyList<IRenderPass> indexedPasses,
            IReadOnlyList<Type> indexedPassTypes)
        {
            var passDef = passDefinitions[passIndex];
            if (passDef?.ResourceBindings == null || passDef.ResourceBindings.Count == 0)
                return;

            foreach (var binding in passDef.ResourceBindings)
            {
                if (binding == null || binding.SourceKind != RenderGraphPassBindingSourceKind.PassField || string.IsNullOrEmpty(binding.FieldName))
                    continue;

                var field = passType.GetInstanceField(binding.FieldName);
                if (field == null)
                    continue;

                if (field.IsDeclaredTransientResourceField())
                {
                    LogSkippedTransientResourceBinding(passType, binding.FieldName);
                    continue;
                }

                var sourcePassType = binding.SourcePassIndex >= 0 && binding.SourcePassIndex < indexedPassTypes.Count
                    ? indexedPassTypes[binding.SourcePassIndex]
                    : null;
                var sourceField = sourcePassType.GetInstanceField(binding.SourceFieldName);
                if (sourceField.IsDeclaredTransientResourceField())
                {
                    LogSkippedTransientResourceBinding(sourcePassType, binding.SourceFieldName);
                    continue;
                }

                var sharedResource = ResolvePassFieldValue(
                    binding.SourcePassIndex,
                    binding.SourceFieldName,
                    indexedPasses,
                    indexedPassTypes,
                    passDefinitions,
                    new HashSet<string>(StringComparer.Ordinal));

                if (sharedResource != null && field.FieldType.IsInstanceOfType(sharedResource))
                {
                    field.SetValue(pass, sharedResource);
                }
            }
        }

        private static Dictionary<string, AccessFlags> BuildResourceAccessOverrides(Type passType, RenderGraphPassDefinition passDef)
        {
            if (passType == null || passDef?.ResourceBindings == null || passDef.ResourceBindings.Count == 0)
                return null;

            Dictionary<string, AccessFlags> accessOverrides = null;

            foreach (var binding in passDef.ResourceBindings)
            {
                if (binding == null
                    || string.IsNullOrEmpty(binding.FieldName))
                {
                    continue;
                }

                var field = passType.GetInstanceField(binding.FieldName);
                var attr = field?.GetCustomAttribute<RenderGraphResource>();
                if (attr == null)
                    continue;

                if (field.IsDeclaredTransientResourceField())
                    continue;

                var effectiveAccess = ShouldPreserveWriteOnlyColorAttachmentAccess(passType, binding, attr)
                    ? attr.Access
                    : RenderGraphPassBindingUtility.ResolveEffectiveAccess(binding, attr.Access);
                if (effectiveAccess == attr.Access)
                    continue;

                accessOverrides ??= new Dictionary<string, AccessFlags>(StringComparer.Ordinal);
                accessOverrides[field.Name] = effectiveAccess;
            }

            return accessOverrides;
        }

        private static bool ShouldPreserveWriteOnlyColorAttachmentAccess(
            Type passType,
            RenderGraphPassResourceBinding binding,
            RenderGraphResource attr)
        {
            return passType != null
                   && typeof(RasterPass).IsAssignableFrom(passType)
                   && binding != null
                   && RenderGraphPassBindingUtility.UsesInputConnection(binding.ConnectionKind)
                   && attr != null
                   && attr.AllowWriteOnlyInput
                   && attr.AttachmentIndex >= 0
                   && !attr.IsDepthAttachment
                   && (attr.Access & AccessFlags.Write) != 0
                   && (attr.Access & AccessFlags.Read) == 0;
        }

        private static object ResolvePassFieldValue(
            int passIndex,
            string fieldName,
            IReadOnlyList<IRenderPass> indexedPasses,
            IReadOnlyList<Type> indexedPassTypes,
            IReadOnlyList<RenderGraphPassDefinition> passDefinitions,
            ISet<string> visited)
        {
            if (passIndex < 0 || passIndex >= indexedPasses.Count || string.IsNullOrEmpty(fieldName))
                return null;

            var visitKey = $"{passIndex}:{fieldName}";
            if (!visited.Add(visitKey))
                return null;

            var sourcePass = indexedPasses[passIndex];
            var sourcePassType = indexedPassTypes[passIndex];
            if (sourcePass == null || sourcePassType == null)
                return null;

            var sourceField = sourcePassType.GetInstanceField(fieldName);
            if (sourceField == null)
                return null;

            if (sourceField.IsDeclaredTransientResourceField())
                return null;

            var sourceValue = sourceField.GetValue(sourcePass);
            var sourcePassDef = passDefinitions[passIndex];
            var sourceBinding = sourcePassDef?.ResourceBindings?.Find(binding => binding != null && binding.FieldName == fieldName);
            if (sourceBinding?.SourceKind == RenderGraphPassBindingSourceKind.PassField)
            {
                var resolvedValue = ResolvePassFieldValue(
                    sourceBinding.SourcePassIndex,
                    sourceBinding.SourceFieldName,
                    indexedPasses,
                    indexedPassTypes,
                    passDefinitions,
                    visited);
                if (resolvedValue != null && sourceField.FieldType.IsInstanceOfType(resolvedValue))
                {
                    sourceField.SetValue(sourcePass, resolvedValue);
                    sourceValue = resolvedValue;
                }
            }

            return sourceValue;
        }

        private static void LogSkippedTransientResourceBinding(Type passType, string fieldName)
        {
            Debug.LogWarning(
                $"[VividRP] Skipping legacy RenderGraph binding for transient field '{fieldName}' on '{passType?.FullName ?? "<Unknown Pass>"}'.");
        }
#if UNITY_EDITOR
        private static void RecordRenderGizmosPass(
            RenderGraph renderGraph,
            GizmoSubset gizmoSubset,
            RenderGraphTexture colorTexture,
            RenderGraphTexture depthTexture,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache)
        {
            var camera = s_FrameData.GetOrCreate<VividCameraData>().camera;
            if (renderGraph == null || !VividRenderPipeline.CanRenderGizmos(camera) || colorTexture == null)
                return;

            if (!UnityEditor.Handles.ShouldRenderGizmos())
                return;

            using var builder = renderGraph.AddUnsafePass<RenderGizmosPassData>(
                gizmoSubset == GizmoSubset.PreImageEffects ? "PrePostprocessGizmos" : "Gizmos",
                out var passData);

            passData.Color = GetOrCreateTextureHandle(renderGraph, colorTexture, textureCache);
            passData.HasDepth = depthTexture != null;
            if (passData.HasDepth)
                passData.Depth = GetOrCreateTextureHandle(renderGraph, depthTexture, textureCache);
            passData.GizmoRendererList = renderGraph.CreateGizmoRendererList(camera, gizmoSubset);
            passData.ExposureTexture = GetGizmoExposureTexture();

            builder.UseTexture(passData.Color, AccessFlags.Write);
            if (passData.HasDepth)
                builder.UseTexture(passData.Depth, AccessFlags.ReadWrite);
            builder.UseRendererList(passData.GizmoRendererList);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<RenderGizmosPassData>(s_RenderGizmosRenderFunc);
        }

        private static void ExecuteRenderGizmosPass(RenderGizmosPassData data, UnsafeGraphContext context)
        {
            Gizmos.exposure = data.ExposureTexture;
            if (data.HasDepth)
                context.cmd.SetRenderTarget(data.Color, data.Depth);
            else
                context.cmd.SetRenderTarget(data.Color);
            context.cmd.DrawRendererList(data.GizmoRendererList);
        }

        private static Texture GetGizmoExposureTexture()
        {
            var exposureData = s_FrameData.Get<VividExposureData>();
            return exposureData.currentExposureTexture;
        }

        private static bool TryGetPreImageEffectGizmoTargets(
            int boundaryPassIndex,
            PassResource boundaryResources,
            out RenderGraphTexture colorTexture,
            out RenderGraphTexture depthTexture)
        {
            colorTexture = GetPreImageEffectGizmoColorTexture(boundaryResources);
            depthTexture = GetPreImageEffectGizmoDepthTexture(boundaryPassIndex);
            return colorTexture != null;
        }

        private static RenderGraphTexture GetPreImageEffectGizmoColorTexture(PassResource boundaryResources)
        {
            if (boundaryResources?.Textures == null)
                return null;

            foreach (var entry in boundaryResources.Textures)
            {
                if (entry?.Texture == null || entry.IsDepthAttachment)
                    continue;

                if (string.Equals(entry.Name, "source", StringComparison.OrdinalIgnoreCase))
                    return entry.Texture;
            }

            foreach (var entry in boundaryResources.Textures)
            {
                if (entry?.Texture == null || entry.IsDepthAttachment)
                    continue;

                return entry.Texture;
            }

            return null;
        }

        private static RenderGraphTexture GetPreImageEffectGizmoDepthTexture(int boundaryPassIndex)
        {
            for (var passIndex = boundaryPassIndex - 1; passIndex >= 0; passIndex--)
            {
                var resources = GetCurrentPassResources(s_RenderPasses[passIndex]);
                if (resources?.Textures == null)
                    continue;

                foreach (var entry in resources.Textures)
                {
                    if (entry?.Texture == null || !entry.IsDepthAttachment)
                        continue;

                    if (entry.Texture.desc != null && entry.Texture.desc.DepthBufferBits == DepthBits.None)
                        continue;

                    return entry.Texture;
                }
            }

            return null;
        }
#endif

        public static void RecordRenderGraph(
            RenderGraph renderGraph,
            ScriptableRenderContext context,
            RenderGraphData graphAsset,
            bool enableAsyncCompute = true)
        {
            using var recordRenderGraphScope = RenderPassProfilingUtility.RecordRenderGraphMarker.Auto();
            using (RenderPassProfilingUtility.RecordRenderGraphEnsureCompiledMarker.Auto())
            {
                EnsureCompiled(graphAsset);
            }

            using (RenderPassProfilingUtility.RecordRenderGraphSetCurrentGraphMarker.Auto())
            {
                s_TextureImportCache.Clear();
                s_BufferImportCache.Clear();
                s_CurrentRenderGraph = renderGraph;
            }

            using (RenderPassProfilingUtility.RecordRenderGraphImportBlueNoiseMarker.Auto())
            {
                BlueNoise.Instance?.ImportResources(renderGraph);
            }

            var passDefinitions = s_RuntimePassDefinitions;
            bool[] passActiveStates;
            using (RenderPassProfilingUtility.RecordRenderGraphAllocatePassActiveStatesMarker.Auto())
            {
                passActiveStates = GetPassActiveStatesBuffer(s_RenderPasses.Count);
            }

            using (RenderPassProfilingUtility.PrepareAllMarker.Auto())
            {
                for (var passIndex = 0; passIndex < s_RenderPasses.Count; passIndex++)
                {
                    var pass = s_RenderPasses[passIndex];
                    var markers = GetPassMarkers(pass);
                    PassResource resources;
                    using (RenderPassProfilingUtility.RecordRenderGraphPrepareAllResolveResourcesMarker.Auto())
                    using (markers.RecordGraphResolveResources.Auto())
                    {
                        resources = GetCurrentPassResources(pass);
                    }

                    var passDefinition = GetRuntimePassDefinition(passDefinitions, passIndex);
                    bool isActive;
                    using (RenderPassProfilingUtility.RecordRenderGraphPrepareAllEvaluateActiveMarker.Auto())
                    {
                        isActive = IsRenderPassActive(pass);
                    }

                    passActiveStates[passIndex] = isActive;

                    if (isActive)
                    {
                        using (RenderPassProfilingUtility.RecordRenderGraphPrepareAllPrepareActiveMarker.Auto())
                        {
                            PrepareRenderPass(pass);
                        }

                        using (RenderPassProfilingUtility.RecordRenderGraphPrepareAllResolveResourcesMarker.Auto())
                        using (markers.RecordGraphResolveResources.Auto())
                        {
                            GetCurrentPassResources(pass);
                        }
                    }
                    else
                    {
                        using (RenderPassProfilingUtility.RecordRenderGraphPrepareAllApplyInactiveBypassMarker.Auto())
                        using (markers.RecordGraphInactiveBypass.Auto())
                        {
                            ApplyInactivePassBypassDescriptors(pass, resources, passDefinition);
                        }
                    }
                }

            }

            VirtualTextureSystem.RecordPageTableUpdates(renderGraph);

            s_CurrentRenderGraph = null;

            using (RenderPassProfilingUtility.RecordRenderGraphClearResourceCachesMarker.Auto())
            {
                ClearRecordGraphResourceCaches();
            }

            var textureCache = s_RecordGraphTextureCache;
            var bufferCache = s_RecordGraphBufferCache;
            var renderListCache = s_RecordGraphRenderListCache;
            var accelerationStructureCache = s_RecordGraphAccelerationStructureCache;
            var recordedPreImageEffectGizmos = false;

            using (RenderPassProfilingUtility.RecordRenderGraphRecordPassesMarker.Auto())
            {
                for (var passIndex = 0; passIndex < s_RenderPasses.Count; passIndex++)
                {
                    var pass = s_RenderPasses[passIndex];
                    var markers = GetPassMarkers(pass);
                    using (markers.RecordGraphPrepareRenderGraph.Auto())
                    {
                        PrepareRenderGraphPass(pass);
                    }

#if UNITY_EDITOR
                    if (!recordedPreImageEffectGizmos && pass is IRenderGizmoPrePostProcessBoundaryPass)
                    {
                        var boundaryResources = GetCurrentPassResources(pass);
                        var camera = s_FrameData.GetOrCreate<VividCameraData>().camera;
                        if (VividRenderPipeline.ShouldRenderPreImageEffectGizmosInRenderGraph(camera)
                            && TryGetPreImageEffectGizmoTargets(
                                passIndex,
                                boundaryResources,
                                out var gizmoColorTexture,
                                out var gizmoDepthTexture))
                        {
                            using (RenderPassProfilingUtility.RecordRenderGraphRecordGizmosMarker.Auto())
                            {
                                RecordRenderGizmosPass(
                                    renderGraph,
                                    GizmoSubset.PreImageEffects,
                                    gizmoColorTexture,
                                    gizmoDepthTexture,
                                    textureCache);
                            }

                            s_RenderedPreImageEffectGizmosInGraph = true;
                        }

                        recordedPreImageEffectGizmos = true;
                    }
#endif
                    PassResource resources;
                    using (markers.RecordGraphResolveResources.Auto())
                    {
                        resources = GetCurrentPassResources(pass);
                    }

                    var passDefinition = GetRuntimePassDefinition(passDefinitions, passIndex);

                    if (passIndex < passActiveStates.Length && !passActiveStates[passIndex])
                    {
                        using (markers.RecordGraphInactiveBypass.Auto())
                        {
                            ApplyInactivePassBypassHandles(
                                renderGraph,
                                pass,
                                resources,
                                passDefinition,
                                textureCache,
                                bufferCache);
                        }

                        continue;
                    }

                    if (pass is IRenderGraphRecordingPass graphRecordingPass)
                    {
                        using (markers.RecordGraph.Auto())
                        {
                            graphRecordingPass.RecordGraph(new RenderGraphRecordingContext(
                                renderGraph,
                                s_FrameData,
                                passDefinition,
                                enableAsyncCompute,
                                textureCache,
                                bufferCache,
                                renderListCache,
                                accelerationStructureCache));
                        }
                    }
                    else if (pass is ComputePass computePass)
                    {
                        RecordComputePass(
                            renderGraph,
                            computePass,
                            resources,
                            passDefinition,
                            enableAsyncCompute,
                            textureCache,
                            bufferCache,
                            renderListCache,
                            accelerationStructureCache);
                    }
                    else if (pass is RasterPass rasterPass)
                    {
                        RecordRasterPass(
                            renderGraph,
                            rasterPass,
                            resources,
                            textureCache,
                            bufferCache,
                            renderListCache,
                            accelerationStructureCache);
                    }
                    else if (pass is UnsafePass unsafePass)
                    {
                        RecordUnsafePass(
                            renderGraph,
                            unsafePass,
                            resources,
                            passDefinition,
                            enableAsyncCompute,
                            textureCache,
                            bufferCache,
                            renderListCache,
                            accelerationStructureCache);
                    }
                }
            }

            using (RenderPassProfilingUtility.RecordRenderGraphClearResourceCachesMarker.Auto())
            {
                ClearRecordGraphResourceCaches();
            }
        }

        private static void ClearRecordGraphResourceCaches()
        {
            s_TextureImportCache.Clear();
            s_BufferImportCache.Clear();
            s_RecordGraphTextureCache.Clear();
            s_RecordGraphBufferCache.Clear();
            s_RecordGraphRenderListCache.Clear();
            s_RecordGraphAccelerationStructureCache.Clear();
        }

        private static RenderGraphPassDefinition GetRuntimePassDefinition(
            IReadOnlyList<RenderGraphPassDefinition> passDefinitions,
            int passIndex)
        {
            return passDefinitions != null && passIndex >= 0 && passIndex < passDefinitions.Count
                ? passDefinitions[passIndex]
                : null;
        }

        private static bool IsRenderPassActive(IRenderPass pass)
        {
            return pass == null || pass.IsActive(s_FrameData);
        }

        private static void ApplyInactivePassBypassDescriptors(
            IRenderPass pass,
            PassResource resources,
            RenderGraphPassDefinition passDefinition)
        {
            if (pass == null || resources?.BypassRules == null || resources.BypassRules.Length == 0)
                return;

            foreach (var rule in resources.BypassRules)
            {
                if (rule == null)
                    continue;

                switch (rule.ResourceType)
                {
                    case PassResourceType.Texture:
                        CopyBypassTextureDescriptor(pass, rule);
                        break;
                    case PassResourceType.Buffer:
                        CopyBypassBufferDescriptor(pass, rule);
                        break;
                }
            }
        }

        private static void CopyBypassTextureDescriptor(IRenderPass pass, PassBypassRule rule)
        {
            var sourceTexture = rule.SourceEntry?.Texture ?? rule.SourceField?.GetValue(pass) as RenderGraphTexture;
            var outputTexture = rule.OutputEntry?.Texture ?? rule.OutputField?.GetValue(pass) as RenderGraphTexture;
            var sourceDescriptor = sourceTexture?.desc;
            if (sourceDescriptor == null || outputTexture == null)
                return;

            outputTexture.desc ??= new RenderGraphTextureDesc();
            sourceDescriptor.Copy(outputTexture.desc);
        }

        private static void CopyBypassBufferDescriptor(IRenderPass pass, PassBypassRule rule)
        {
            var sourceBuffer = rule.SourceEntry?.Buffer ?? rule.SourceField?.GetValue(pass) as RenderGraphBuffer;
            var outputBuffer = rule.OutputEntry?.Buffer ?? rule.OutputField?.GetValue(pass) as RenderGraphBuffer;
            var sourceDescriptor = sourceBuffer?.desc;
            if (sourceDescriptor == null || outputBuffer == null)
                return;

            outputBuffer.desc = sourceDescriptor.Clone();
        }

        internal static void ApplyInactivePassBypassHandles(
            RenderGraph renderGraph,
            IRenderPass pass,
            PassResource resources,
            RenderGraphPassDefinition passDefinition,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache)
        {
            var bypassVersion = NextInactiveBypassVersion();

            if (pass != null && resources?.BypassRules != null)
            {
                var bypassRules = resources.BypassRules;
                for (var i = 0; i < bypassRules.Length; i++)
                {
                    var rule = bypassRules[i];
                    if (rule == null)
                        continue;

                    if (TryApplyInactivePassBypassHandle(
                            renderGraph,
                            pass,
                            rule,
                            textureCache,
                            bufferCache))
                    {
                        rule.AppliedBypassVersion = bypassVersion;
                    }
                }
            }

            ClearInactivePassUnbypassedOutputs(resources, bypassVersion);
        }

        private static int NextInactiveBypassVersion()
        {
            unchecked
            {
                s_InactiveBypassVersion++;
                if (s_InactiveBypassVersion == 0)
                    s_InactiveBypassVersion = 1;
            }

            return s_InactiveBypassVersion;
        }

        private static bool TryApplyInactivePassBypassHandle(
            RenderGraph renderGraph,
            IRenderPass pass,
            PassBypassRule rule,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache)
        {
            if (renderGraph == null || pass == null || rule == null)
                return false;

            switch (rule.ResourceType)
            {
                case PassResourceType.Texture:
                {
                    var sourceTexture = rule.SourceEntry?.Texture ?? rule.SourceField?.GetValue(pass) as RenderGraphTexture;
                    var outputTexture = rule.OutputEntry?.Texture ?? rule.OutputField?.GetValue(pass) as RenderGraphTexture;
                    if (sourceTexture == null || outputTexture == null)
                        return false;

                    var sourceHandle = GetOrCreateTextureHandle(renderGraph, sourceTexture, textureCache);
                    if (!sourceHandle.IsValid())
                        return false;

                    outputTexture.ClearImportedHandle();
                    outputTexture.innerHandle = sourceHandle;
                    textureCache[outputTexture] = sourceHandle;
                    return true;
                }
                case PassResourceType.Buffer:
                {
                    var sourceBuffer = rule.SourceEntry?.Buffer ?? rule.SourceField?.GetValue(pass) as RenderGraphBuffer;
                    var outputBuffer = rule.OutputEntry?.Buffer ?? rule.OutputField?.GetValue(pass) as RenderGraphBuffer;
                    if (sourceBuffer == null || outputBuffer == null)
                        return false;

                    var sourceHandle = GetOrCreateBufferHandle(renderGraph, sourceBuffer, bufferCache);
                    if (!sourceHandle.IsValid())
                        return false;

                    outputBuffer.ClearImportedHandle();
                    outputBuffer.innerHandle = sourceHandle;
                    bufferCache[outputBuffer] = sourceHandle;
                    return true;
                }
                default:
                    return false;
            }
        }

        private static void ClearInactivePassUnbypassedOutputs(
            PassResource resources,
            int bypassVersion)
        {
            if (resources == null)
                return;

            var bypassRules = resources.BypassRules;
            ClearInactivePassUnbypassedTextures(resources.Textures, bypassRules, bypassVersion);
            ClearInactivePassUnbypassedBuffers(resources.Buffers, bypassRules, bypassVersion);
            ClearInactivePassUnbypassedRenderLists(resources.RenderLists, bypassRules, bypassVersion);
            ClearInactivePassUnbypassedAccelerationStructures(resources.AccelerationStructures, bypassRules, bypassVersion);
        }

        private static bool ShouldClearInactivePassOutput(
            PassResourceEntry entry,
            PassBypassRule[] bypassRules,
            int bypassVersion)
        {
            if (entry == null)
                return false;

            if ((entry.Access & AccessFlags.Write) == 0 || (entry.Access & AccessFlags.Read) != 0)
                return false;

            return !WasInactivePassOutputBypassed(entry, bypassRules, bypassVersion);
        }

        private static bool WasInactivePassOutputBypassed(
            PassResourceEntry entry,
            PassBypassRule[] bypassRules,
            int bypassVersion)
        {
            if (entry == null || bypassRules == null)
                return false;

            for (var i = 0; i < bypassRules.Length; i++)
            {
                var rule = bypassRules[i];
                if (rule == null || rule.AppliedBypassVersion != bypassVersion)
                    continue;

                if (ReferenceEquals(rule.OutputEntry, entry))
                    return true;

                var field = entry.Field;
                if (field != null
                    && (ReferenceEquals(rule.OutputField, field)
                        || string.Equals(rule.OutputFieldName, field.Name, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ClearInactivePassUnbypassedTextures(
            PassResourceEntry[] entries,
            PassBypassRule[] bypassRules,
            int bypassVersion)
        {
            if (entries == null)
                return;

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (!ShouldClearInactivePassOutput(entry, bypassRules, bypassVersion))
                    continue;

                entry.Texture?.ClearImportedHandle();
            }
        }

        private static void ClearInactivePassUnbypassedBuffers(
            PassResourceEntry[] entries,
            PassBypassRule[] bypassRules,
            int bypassVersion)
        {
            if (entries == null)
                return;

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (!ShouldClearInactivePassOutput(entry, bypassRules, bypassVersion) || entry.Buffer == null)
                    continue;

                entry.Buffer.ClearImportedHandle();
            }
        }

        private static void ClearInactivePassUnbypassedRenderLists(
            PassResourceEntry[] entries,
            PassBypassRule[] bypassRules,
            int bypassVersion)
        {
            if (entries == null)
                return;

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (!ShouldClearInactivePassOutput(entry, bypassRules, bypassVersion) || entry.RenderList == null)
                    continue;

                entry.RenderList.innerHandle = default;
            }
        }

        private static void ClearInactivePassUnbypassedAccelerationStructures(
            PassResourceEntry[] entries,
            PassBypassRule[] bypassRules,
            int bypassVersion)
        {
            if (entries == null)
                return;

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (!ShouldClearInactivePassOutput(entry, bypassRules, bypassVersion) || entry.AccelerationStructure == null)
                    continue;

                entry.AccelerationStructure.innerHandle = default;
            }
        }

        private static PassResource GetCurrentPassResources(IRenderPass pass, string displayName = null)
        {
            var needsRefresh = pass is IDynamicPassResourceLayout dynamicLayoutPass
                               && dynamicLayoutPass.IsPassResourceLayoutDirty;

            var hasResources = s_PassResources.TryGetValue(pass, out var resources);
            if (hasResources
                && needsRefresh
                && pass is IStablePassResourceLayout
                && pass.TryRefresh(resources))
            {
                ApplyResourceAccessOverrides(pass, resources);

                if (pass is IDynamicPassResourceLayout refreshedStableLayoutPass)
                    refreshedStableLayoutPass.ClearPassResourceLayoutDirty();

                return resources;
            }

            if (!hasResources || needsRefresh)
            {
                var markers = GetPassMarkers(pass, displayName);
                using (markers.Initialize.Auto())
                {
                    resources = pass.Initialize();
                }

                ApplyResourceAccessOverrides(pass, resources);
                s_PassResources[pass] = resources;

                if (pass is IDynamicPassResourceLayout refreshedDynamicLayoutPass)
                    refreshedDynamicLayoutPass.ClearPassResourceLayoutDirty();
            }

            return resources;
        }

        private static void ApplyResourceAccessOverrides(IRenderPass pass, PassResource resources)
        {
            if (pass == null
                || resources == null
                || !s_PassResourceAccessOverrides.TryGetValue(pass, out var accessOverrides)
                || accessOverrides == null
                || accessOverrides.Count == 0)
            {
                return;
            }

            foreach (var entry in resources.AllEntries)
            {
                var fieldName = entry?.Field?.Name;
                if (string.IsNullOrEmpty(fieldName))
                    continue;

                if (accessOverrides.TryGetValue(fieldName, out var access))
                    entry.Access = access;
            }
        }

        private static void DisposeAccelerationStructures()
        {
            if (s_PassResources.Count == 0)
                return;

            var disposedAccelerationStructures = new HashSet<RenderGraphAccelerationStructure>();
            foreach (var resources in s_PassResources.Values)
            {
                if (resources?.AccelerationStructures == null)
                    continue;

                foreach (var entry in resources.AccelerationStructures)
                {
                    var accelerationStructure = entry?.AccelerationStructure;
                    if (accelerationStructure != null && disposedAccelerationStructures.Add(accelerationStructure))
                        accelerationStructure.Dispose();
                }
            }
        }
    }
}
