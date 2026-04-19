using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Runtime
{
    public static partial class PassRecorder
    {
        private sealed class TextureHistoryFrameBinding
        {
            public string Key;
            public RenderGraphTexture PreviousTexture;
            public RenderGraphTexture CurrentTexture;
            public RTHandle PreviousHandle;
            public RTHandle CurrentHandle;
            public RenderGraphTextureDesc Descriptor;
        }

        private sealed class CodeManagedBufferHistoryRequest
        {
            public string Key;
            public RenderGraphBuffer CurrentBuffer;
        }

        private static readonly List<IRenderPass> s_RenderPasses = new();
        private static readonly ContextContainer s_FrameData = new();
        private static readonly Dictionary<IRenderPass, PassResource> s_PassResources = new();
        private static readonly Dictionary<IRenderPass, int> s_PassIndices = new();
        private static readonly Dictionary<IRenderPass, Dictionary<string, AccessFlags>> s_PassResourceAccessOverrides = new();
        private static RenderGraphTexture[] s_HistoryPreviousTextures = Array.Empty<RenderGraphTexture>();
        private static RenderGraphTexture[] s_HistoryCurrentTextures = Array.Empty<RenderGraphTexture>();
        private static readonly Dictionary<RenderGraphTexture, RTHandle> s_ImportedRTHandles = new();
        private static readonly Dictionary<IRenderPass, List<ImportedPassTexture>> s_PassImportedHandles = new();
        private static readonly Dictionary<string, TextureHistoryFrameBinding> s_TextureHistoryFrameBindings = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, CodeManagedBufferHistoryRequest> s_CodeManagedBufferHistoryRequests = new(StringComparer.Ordinal);
        private static readonly HashSet<RenderGraphTexture> s_HistoryImportedTextures = new();
        private static readonly HashSet<RenderGraphBuffer> s_CodeManagedHistoryImportedBuffers = new();
        private static RenderGraph s_CurrentRenderGraph;

        private static RenderGraphData s_CurrentGraphAsset;
        private static List<RenderGraphPassDefinition> s_RuntimePassDefinitions = new();
        private static long s_CurrentImportVersion;
        private static bool s_IsCompiled;
        private static bool s_RenderedPreImageEffectGizmosInGraph;
        private static CMAA2Pass s_InjectedCmaa2Pass;

#if UNITY_EDITOR
        private sealed class RenderGizmosPassData
        {
            public RendererListHandle GizmoRendererList;
            public Texture ExposureTexture;
            public TextureHandle Color;
            public TextureHandle Depth;
            public bool HasDepth;
        }
#endif

        internal static void InitializeContext(ScriptableRenderContext context, Camera camera, CullingResults cullingResults)
        {
            var renderingData = s_FrameData.GetOrCreate<VividRenderingData>();
            var cameraData = s_FrameData.GetOrCreate<VividCameraData>();
            var gpuDrivenFrameData = s_FrameData.GetOrCreate<VividGPUDrivenFrameData>();
            var lightData = s_FrameData.GetOrCreate<VividLightData>();
            var additionalCameraData = camera.GetComponent<VividAdditionalCameraData>();
            if (additionalCameraData == null && camera.cameraType == CameraType.Game)
                additionalCameraData = camera.GetVividAdditionalCameraData();

            ApplyTAAJitter(camera, additionalCameraData);

            if (additionalCameraData != null)
                additionalCameraData.UpdateCameraMatrices(true);

            cameraData.camera = camera;
            cameraData.additionalData = additionalCameraData;
            cameraData.renderType = additionalCameraData != null ? additionalCameraData.renderType : VividCameraRenderType.Base;
            cameraData.clearDepth = additionalCameraData == null || additionalCameraData.clearDepth;
            cameraData.pixelWidth = camera.pixelWidth;
            cameraData.pixelHeight = camera.pixelHeight;
            cameraData.pixelRect = camera.pixelRect;
            cameraData.actualWidth = camera.scaledPixelWidth;
            cameraData.actualHeight = camera.scaledPixelHeight;
            cameraData.frameIndex = Time.frameCount;
            renderingData.cullingResults = cullingResults;
            renderingData.context = context;
            gpuDrivenFrameData.Reset();
            lightData.Update(cullingResults);
        }

        private static void ApplyTAAJitter(Camera camera, VividAdditionalCameraData additionalCameraData)
        {
            if (camera == null)
                return;

            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
                return;

            var nonJitteredProj = CameraProjectionMatrixUtility.GetNonJitteredProjectionMatrix(camera);

            var taaSettings = TAASettings.FromCamera(additionalCameraData);
            if (!taaSettings.Enabled)
            {
                CameraProjectionMatrixUtility.SetProjectionMatrices(camera, nonJitteredProj, nonJitteredProj);
                return;
            }

            var pixelWidth = camera.pixelWidth;
            var pixelHeight = camera.pixelHeight;
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                CameraProjectionMatrixUtility.SetProjectionMatrices(camera, nonJitteredProj, nonJitteredProj);
                return;
            }

            var jitter = HaltonJitter.Get(Time.frameCount, taaSettings.SampleCount);
            jitter *= taaSettings.JitterSpread;

            var jitterX = jitter.x * 2.0f / pixelWidth;
            var jitterY = jitter.y * 2.0f / pixelHeight;
            var jitterMatrix = Matrix4x4.identity;
            jitterMatrix.m03 = jitterX;
            jitterMatrix.m13 = jitterY;

            CameraProjectionMatrixUtility.SetProjectionMatrices(camera, jitterMatrix * nonJitteredProj, nonJitteredProj);
        }

        internal static void SetGPUDrivenFrameData(
            GraphicsBuffer visibleMeshletRenderRequestsBuffer,
            GraphicsBuffer visibleMeshletIndirectDrawArgsBuffer)
        {
            var gpuDrivenFrameData = s_FrameData.GetOrCreate<VividGPUDrivenFrameData>();
            gpuDrivenFrameData.visibleMeshletRenderRequestsBuffer = visibleMeshletRenderRequestsBuffer;
            gpuDrivenFrameData.visibleMeshletIndirectDrawArgsBuffer = visibleMeshletIndirectDrawArgsBuffer;
        }

        internal static ContextContainer GetFrameData()
        {
            return s_FrameData;
        }

        public static void Dispose()
        {
            foreach (var pass in s_RenderPasses)
            {
                try
                {
                    pass.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (s_InjectedCmaa2Pass != null)
            {
                try
                {
                    s_InjectedCmaa2Pass.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                s_InjectedCmaa2Pass = null;
            }

            DisposeAccelerationStructures();
            s_RenderPasses.Clear();
            s_PassResources.Clear();
            s_PassIndices.Clear();
            s_PassResourceAccessOverrides.Clear();
            ClearHistoryImportedHandles();
            s_HistoryPreviousTextures = Array.Empty<RenderGraphTexture>();
            s_HistoryCurrentTextures = Array.Empty<RenderGraphTexture>();
            ClearCodeManagedHistoryFrameState();
            ClearImportedTextures();
            RenderGraphHistoryRegistry.Clear();
            RenderGraphBufferHistoryRegistry.Clear();
            RenderGraphPreviewRegistry.Clear();
            FrameContextSystem.Clear();
            VividRayTracingAccelerationStructureStatsRegistry.Clear();
            s_CurrentGraphAsset = null;
            s_RuntimePassDefinitions.Clear();
            s_CurrentImportVersion = 0;
            s_IsCompiled = false;
        }

        internal static void PrepareFrame(RenderGraphData graphAsset, CommandBuffer cmdBuffer)
        {
            EnsureCompiled(graphAsset);
            ClearHistoryImportedHandles();
            ClearCodeManagedHistoryFrameState();
            s_RenderedPreImageEffectGizmosInGraph = false;

            // Advance temporal state and set all shader globals before any pass executes.
            FrameContextSystem.Update(s_FrameData, cmdBuffer);

            PrepareHistoryTargets(graphAsset, cmdBuffer);
            ClearImportedTextures();
        }

        internal static void AbortFrame()
        {
            ClearImportedTextures();
            ClearHistoryImportedHandles();
            ClearCodeManagedHistoryFrameState();
            s_RenderedPreImageEffectGizmosInGraph = false;
        }

        /// <summary>
        /// Imports an external RTHandle for a specific pass during Prepare().
        /// Returns a TextureHandle that can be assigned to pass member variables.
        /// </summary>
        internal static TextureHandle ImportTextureForPass(IRenderPass pass, RTHandle rtHandle, AccessFlags access = AccessFlags.Read)
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

            var handle = s_CurrentRenderGraph.ImportTexture(rtHandle);

            if (!s_PassImportedHandles.TryGetValue(pass, out var handles))
            {
                handles = new List<ImportedPassTexture>();
                s_PassImportedHandles[pass] = handles;
            }

            var importedTexture = new ImportedPassTexture(handle, access);
            if (!handles.Contains(importedTexture))
                handles.Add(importedTexture);

            return handle;
        }

        internal static bool IsPassTextureImportActive => s_CurrentRenderGraph != null;

        /// <summary>
        /// Imports an external RTHandle into a RenderGraphTexture for use in passes.
        /// The imported texture will be available for the current frame only.
        /// Call this before RecordRenderGraph() to make the external resource available to passes.
        /// </summary>
        /// <param name="texture">The RenderGraphTexture to import into</param>
        /// <param name="rtHandle">The external RTHandle to import</param>
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

        internal static bool AllocHistoryTextureForPass(
            IRenderPass pass,
            string key,
            RenderGraphTexture previous,
            RenderGraphTexture current,
            RenderGraphTextureDesc desc)
        {
            if (!TryResolvePassHistoryContext(pass, key, out var historyKey, out var camera, out var graphAsset))
                return false;

            var descriptor = CloneTextureDescriptor(desc ?? current?.desc ?? previous?.desc);
            if (descriptor == null)
            {
                Debug.LogWarning("[VividRP] Cannot allocate history texture without a descriptor.");
                return false;
            }

            if (previous != null)
            {
                previous.desc = CloneTextureDescriptor(descriptor);
            }

            if (current != null)
            {
                current.desc = CloneTextureDescriptor(descriptor);
            }

            var hasHistoryTextures = RenderGraphHistoryRegistry.AcquireHistoryTextures(
                camera,
                graphAsset,
                historyKey,
                descriptor,
                out var previousHandle,
                out var currentHandle,
                out var hasValidData);

            RegisterTextureHistoryBinding(
                historyKey,
                previous,
                current,
                previousHandle,
                currentHandle,
                descriptor);

            return hasHistoryTextures && hasValidData;
        }

        internal static void CommitFrame(RenderGraphData graphAsset)
        {
            CommitTextureHistories(graphAsset);
            FinalizeCodeManagedBufferHistories(graphAsset);
            ClearHistoryImportedHandles();
            ClearCodeManagedHistoryFrameState();
        }

        internal static bool HasRenderGizmoPrePostProcessBoundary(RenderGraphData graphAsset)
        {
            EnsureCompiled(graphAsset);
            return HasRenderGizmoPrePostProcessBoundary(s_RenderPasses);
        }

        internal static bool HasRenderGizmoPrePostProcessBoundary(IReadOnlyList<IRenderPass> renderPasses)
        {
            if (renderPasses == null)
                return false;

            foreach (var renderPass in renderPasses)
            {
                if (renderPass is IRenderGizmoPrePostProcessBoundaryPass)
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

        private static bool ShouldInjectCmaa2Pass()
        {
            var additionalData = s_FrameData.GetOrCreate<VividCameraData>().additionalData;
            return additionalData != null && additionalData.antialiasing == VividAntialiasingMode.CMAA2;
        }

        private static CMAA2Pass GetOrCreateInjectedCmaa2Pass()
        {
            if (s_InjectedCmaa2Pass != null)
                return s_InjectedCmaa2Pass;

            s_InjectedCmaa2Pass = new CMAA2Pass();
            s_InjectedCmaa2Pass.Create();
            GetCurrentPassResources(s_InjectedCmaa2Pass);
            return s_InjectedCmaa2Pass;
        }

        private static void RecordInjectedCmaa2Pass(
            RenderGraph renderGraph,
            CMAA2Pass cmaa2Pass,
            FinalBlitPass finalBlitPass,
            bool enableAsyncCompute,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache)
        {
            if (renderGraph == null || cmaa2Pass == null || finalBlitPass == null)
                return;

            var sourceTexture = finalBlitPass.GetSourceTexture();
            if (sourceTexture == null || sourceTexture.innerHandle.IsValid() != true)
                return;

            cmaa2Pass.SetInput(sourceTexture);
            var cmaa2Resources = GetCurrentPassResources(cmaa2Pass);
            RecordComputePass(
                renderGraph,
                cmaa2Pass,
                cmaa2Resources,
                null,
                enableAsyncCompute,
                textureCache,
                bufferCache,
                renderListCache,
                accelerationStructureCache,
                "CMAA2Pass (Injected)");
            finalBlitPass.SetSourceTexture(cmaa2Pass.GetOutputTexture());
        }

        private static void RestoreInjectedFinalBlitSources()
        {
            foreach (var pass in s_RenderPasses)
            {
                if (pass is FinalBlitPass finalBlitPass)
                    finalBlitPass.RestoreSourceTexture();
            }
        }

        private static void RegisterTextureHistoryBinding(
            string historyKey,
            RenderGraphTexture previousTexture,
            RenderGraphTexture currentTexture,
            RTHandle previousHandle,
            RTHandle currentHandle,
            RenderGraphTextureDesc descriptor)
        {
            if (string.IsNullOrEmpty(historyKey))
                return;

            if (previousTexture == null && currentTexture == null)
                return;

            s_TextureHistoryFrameBindings[historyKey] = new TextureHistoryFrameBinding
            {
                Key = historyKey,
                PreviousTexture = previousTexture,
                CurrentTexture = currentTexture,
                PreviousHandle = previousHandle,
                CurrentHandle = currentHandle,
                Descriptor = CloneTextureDescriptor(descriptor),
            };
        }

        internal static bool AllocHistoryBufferForPass(
            IRenderPass pass,
            string key,
            RenderGraphBuffer previous,
            RenderGraphBuffer current,
            RenderGraphBufferDesc desc)
        {
            if (!TryResolvePassHistoryContext(pass, key, out var historyKey, out var camera, out var graphAsset))
                return false;

            var descriptor = CloneBufferDescriptor(desc ?? current?.desc ?? previous?.desc);
            if (descriptor == null)
            {
                Debug.LogWarning("[VividRP] Cannot allocate history buffer without a descriptor.");
                return false;
            }

            if (!RenderGraphBufferHistoryRegistry.PrepareHistoryBuffers(
                    camera,
                    graphAsset,
                    historyKey,
                    descriptor,
                    out var previousBuffer,
                    out var currentBuffer,
                    out var hasValidHistory))
            {
                return false;
            }

            if (previous != null)
            {
                previous.desc = CloneBufferDescriptor(descriptor);
                previous.SetImportedBuffer(previousBuffer);
                s_CodeManagedHistoryImportedBuffers.Add(previous);
            }

            if (current != null)
            {
                current.desc = CloneBufferDescriptor(descriptor);
                current.SetImportedBuffer(currentBuffer);
                s_CodeManagedHistoryImportedBuffers.Add(current);
                s_CodeManagedBufferHistoryRequests[historyKey] = new CodeManagedBufferHistoryRequest
                {
                    Key = historyKey,
                    CurrentBuffer = current,
                };
            }

            return hasValidHistory;
        }

        internal static string BuildPassHistoryKey(IRenderPass pass, string key)
        {
            if (pass == null || string.IsNullOrWhiteSpace(key))
                return null;

            if (!s_PassIndices.TryGetValue(pass, out var passIndex))
            {
                passIndex = s_RenderPasses.IndexOf(pass);
                if (passIndex < 0)
                    return null;

                s_PassIndices[pass] = passIndex;
            }

            return $"{passIndex}:{key}";
        }

        private static bool TryResolvePassHistoryContext(
            IRenderPass pass,
            string key,
            out string historyKey,
            out Camera camera,
            out RenderGraphData graphAsset)
        {
            historyKey = BuildPassHistoryKey(pass, key);
            camera = s_FrameData.GetOrCreate<VividCameraData>().camera;
            graphAsset = s_CurrentGraphAsset;

            if (string.IsNullOrEmpty(historyKey) || camera == null || graphAsset == null)
            {
                if (string.IsNullOrWhiteSpace(key))
                    Debug.LogWarning("[VividRP] Cannot allocate history resource with an empty key.");
                else if (camera == null || graphAsset == null)
                    Debug.LogWarning("[VividRP] Cannot allocate history resource before PassRecorder has an active camera and graph asset.");
                else
                    Debug.LogWarning("[VividRP] Cannot resolve the pass history scope for the requested resource.");

                return false;
            }

            return true;
        }

        private static RenderGraphTextureDesc CloneTextureDescriptor(RenderGraphTextureDesc descriptor)
        {
            return descriptor?.Clone();
        }

        private static RenderGraphBufferDesc CloneBufferDescriptor(RenderGraphBufferDesc descriptor)
        {
            return descriptor?.Clone();
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
            s_PassImportedHandles.Clear();
            s_CurrentRenderGraph = null;
        }

        private static void ClearHistoryImportedHandles()
        {
            foreach (var texture in s_HistoryImportedTextures)
            {
                texture?.ClearImportedHandle();
            }

            s_HistoryImportedTextures.Clear();
            s_TextureHistoryFrameBindings.Clear();
        }

        private static void ClearCodeManagedHistoryFrameState()
        {
            foreach (var buffer in s_CodeManagedHistoryImportedBuffers)
            {
                buffer?.ClearImportedBuffer();
            }

            s_CodeManagedHistoryImportedBuffers.Clear();
            s_CodeManagedBufferHistoryRequests.Clear();
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
            Dispose();

            if (graphAsset == null)
            {
                // Fallback graph (keeps the pipeline running without an authored asset).
                var fallbackPass = new FullScreenPass();
                s_PassIndices[fallbackPass] = 0;
                s_RenderPasses.Add(fallbackPass);
            }
            else
            {
                
                var passDefinitions = graphAsset.Passes ?? new List<RenderGraphPassDefinition>();
                var textures = CreateRuntimeTextures(graphAsset);
                CreateRuntimeHistoryTextures(graphAsset, out s_HistoryPreviousTextures, out s_HistoryCurrentTextures);
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

                    ApplyResourceBindings(
                        pass,
                        passType,
                        passDef,
                        textures,
                        s_HistoryPreviousTextures,
                        s_HistoryCurrentTextures,
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
                pass.Create();
                GetCurrentPassResources(pass);
            }

            s_CurrentGraphAsset = graphAsset;
            s_CurrentImportVersion = graphAsset != null ? graphAsset.ImportVersion : 0;
            s_IsCompiled = true;
        }

        
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

        private static void CreateRuntimeHistoryTextures(
            RenderGraphData graphAsset,
            out RenderGraphTexture[] previousTextures,
            out RenderGraphTexture[] currentTextures)
        {
            if (graphAsset?.HistoryTextureDescriptors == null || graphAsset.HistoryTextureDescriptors.Count == 0)
            {
                previousTextures = Array.Empty<RenderGraphTexture>();
                currentTextures = Array.Empty<RenderGraphTexture>();
                return;
            }

            var count = graphAsset.HistoryTextureDescriptors.Count;
            previousTextures = new RenderGraphTexture[count];
            currentTextures = new RenderGraphTexture[count];
            for (var i = 0; i < count; i++)
            {
                var descriptor = graphAsset.HistoryTextureDescriptors[i];
                previousTextures[i] = new RenderGraphTexture
                {
                    desc = descriptor != null ? descriptor.Clone() : new RenderGraphTextureDesc(),
                };
                currentTextures[i] = new RenderGraphTexture
                {
                    desc = descriptor != null ? descriptor.Clone() : new RenderGraphTextureDesc(),
                };
            }
        }

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

        private static void ApplyResourceBindings(
            IRenderPass pass,
            Type passType,
            RenderGraphPassDefinition passDef,
            RenderGraphTexture[] textures,
            RenderGraphTexture[] historyPreviousTextures,
            RenderGraphTexture[] historyCurrentTextures,
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

                var field = RenderGraphPassReflectionUtility.GetInstanceField(passType, binding.FieldName);
                if (field == null)
                    continue;

                if (binding.SourceKind == RenderGraphPassBindingSourceKind.PassField)
                    continue;

                switch (binding.ResourceKind)
                {
                    case RenderGraphResourceKind.Texture:
                        if (field.FieldType != typeof(RenderGraphTexture))
                            break;

                        var textureArray = binding.ResourceBindingVariant switch
                        {
                            RenderGraphResourceBindingVariant.HistoryPrevious => historyPreviousTextures,
                            RenderGraphResourceBindingVariant.HistoryCurrent => historyCurrentTextures,
                            _ => textures,
                        };

                        if (binding.ResourceIndex >= 0 && binding.ResourceIndex < textureArray.Length)
                        {
                            field.SetValue(pass, textureArray[binding.ResourceIndex]);
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

                var field = RenderGraphPassReflectionUtility.GetInstanceField(passType, binding.FieldName);
                if (field == null)
                    continue;

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

                var field = RenderGraphPassReflectionUtility.GetInstanceField(passType, binding.FieldName);
                var attr = field?.GetCustomAttribute<RenderGraphResource>();
                if (attr == null)
                    continue;

                var effectiveAccess = RenderGraphPassBindingUtility.ResolveEffectiveAccess(binding, attr.Access);
                if (effectiveAccess == attr.Access)
                    continue;

                accessOverrides ??= new Dictionary<string, AccessFlags>(StringComparer.Ordinal);
                accessOverrides[field.Name] = effectiveAccess;
            }

            return accessOverrides;
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

            var sourceField = RenderGraphPassReflectionUtility.GetInstanceField(sourcePassType, fieldName);
            if (sourceField == null)
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

        private static void PrepareHistoryTargets(RenderGraphData graphAsset, CommandBuffer cmdBuffer)
        {
            if (graphAsset?.HistoryTextureDescriptors == null || graphAsset.HistoryTextureDescriptors.Count == 0)
                return;

            var camera = s_FrameData.GetOrCreate<VividCameraData>().camera;
            if (camera == null)
                return;

            for (var i = 0; i < graphAsset.HistoryTextureDescriptors.Count; i++)
            {
                var descriptor = graphAsset.HistoryTextureDescriptors[i];
                var previousTexture = i < s_HistoryPreviousTextures.Length ? s_HistoryPreviousTextures[i] : null;
                var currentTexture = i < s_HistoryCurrentTextures.Length ? s_HistoryCurrentTextures[i] : null;
                if (previousTexture == null && currentTexture == null)
                    continue;

                if (previousTexture != null)
                    previousTexture.desc = CloneTextureDescriptor(descriptor) ?? new RenderGraphTextureDesc();

                if (currentTexture != null)
                    currentTexture.desc = CloneTextureDescriptor(descriptor) ?? new RenderGraphTextureDesc();

                if (!RenderGraphHistoryRegistry.AcquireHistoryTextures(
                        camera,
                        graphAsset,
                        i,
                        descriptor,
                        out var previousHandle,
                        out var currentHandle,
                        out _,
                        cmdBuffer))
                {
                    continue;
                }

                RegisterTextureHistoryBinding(
                    i.ToString(),
                    previousTexture,
                    currentTexture,
                    previousHandle,
                    currentHandle,
                    descriptor);
            }
        }

        private static void PreparePendingHistoryTextureImports(RenderGraph renderGraph)
        {
            if (renderGraph == null || s_TextureHistoryFrameBindings.Count == 0)
                return;

            var importedHandles = new Dictionary<RTHandle, TextureHandle>();

            foreach (var binding in s_TextureHistoryFrameBindings.Values)
            {
                ImportHistoryTexture(renderGraph, binding?.PreviousTexture, binding?.PreviousHandle, importedHandles);
                ImportHistoryTexture(renderGraph, binding?.CurrentTexture, binding?.CurrentHandle, importedHandles);
            }
        }

        private static void ImportHistoryTexture(
            RenderGraph renderGraph,
            RenderGraphTexture texture,
            RTHandle rtHandle,
            IDictionary<RTHandle, TextureHandle> importedHandles)
        {
            if (renderGraph == null || texture == null || rtHandle == null)
                return;

            if (!importedHandles.TryGetValue(rtHandle, out var importedHandle))
            {
                importedHandle = renderGraph.ImportTexture(rtHandle);
                importedHandles.Add(rtHandle, importedHandle);
            }

            texture.SetImportedHandle(importedHandle);
            s_HistoryImportedTextures.Add(texture);
        }

        private static bool ShouldPersistHistoryTexture(RenderGraphTexture texture)
        {
            foreach (var resources in s_PassResources.Values)
            {
                if (resources?.Textures == null)
                    continue;

                foreach (var entry in resources.Textures)
                {
                    if (ReferenceEquals(entry?.Texture, texture) && (entry.Access & AccessFlags.Write) != 0)
                        return true;
                }
            }

            return false;
        }

        private static void CommitTextureHistories(RenderGraphData graphAsset)
        {
            if (graphAsset == null || s_TextureHistoryFrameBindings.Count == 0)
                return;

            var camera = s_FrameData.GetOrCreate<VividCameraData>().camera;
            if (camera == null)
                return;

            foreach (var binding in s_TextureHistoryFrameBindings.Values)
            {
                var currentTexture = binding?.CurrentTexture;
                if (currentTexture == null || !ShouldPersistHistoryTexture(currentTexture))
                {
                    continue;
                }

                RenderGraphHistoryRegistry.CommitHistory(camera, graphAsset, binding.Key);
            }
        }

        private static bool ShouldPersistHistoryBuffer(RenderGraphBuffer buffer)
        {
            foreach (var resources in s_PassResources.Values)
            {
                if (resources?.Buffers == null)
                    continue;

                foreach (var entry in resources.Buffers)
                {
                    if (ReferenceEquals(entry?.Buffer, buffer) && (entry.Access & AccessFlags.Write) != 0)
                        return true;
                }
            }

            return false;
        }

        private static void FinalizeCodeManagedBufferHistories(RenderGraphData graphAsset)
        {
            if (graphAsset == null || s_CodeManagedBufferHistoryRequests.Count == 0)
                return;

            var camera = s_FrameData.GetOrCreate<VividCameraData>().camera;
            if (camera == null)
                return;

            foreach (var request in s_CodeManagedBufferHistoryRequests.Values)
            {
                if (request?.CurrentBuffer == null || !ShouldPersistHistoryBuffer(request.CurrentBuffer))
                    continue;

                RenderGraphBufferHistoryRegistry.FinalizeFrame(camera, graphAsset, request.Key);
            }
        }

        private static void RecordTexturePreviewPasses(
            RenderGraph renderGraph,
            IRenderPass pass,
            PassResource resources,
            RenderGraphPassDefinition passDefinition)
        {
            if (!RenderGraphPreviewRegistry.IsAvailable
                || renderGraph == null
                || pass == null
                || resources?.Textures == null
                || resources.Textures.Length == 0)
                return;

            var passType = pass.GetType();
            foreach (var entry in resources.Textures)
            {
                if (!ShouldRecordTexturePreview(passDefinition, entry))
                    continue;

                var source = entry.Texture.innerHandle;
                if (!source.IsValid())
                    continue;

                var sourceInfo = renderGraph.GetRenderTargetInfo(source);
                if (!CanPreviewTexture(sourceInfo))
                    continue;

                var previewFieldName = !string.IsNullOrEmpty(entry.Name)
                    ? entry.Name
                    : entry.Field?.Name;

                if (string.IsNullOrEmpty(previewFieldName))
                    continue;

                var previewTarget = RenderGraphPreviewRegistry.GetOrCreatePreviewTarget(
                    passType,
                    previewFieldName,
                    sourceInfo,
                    entry.Texture.desc);
                if (previewTarget == null)
                    continue;

                var destination = renderGraph.ImportTexture(previewTarget);
                if (!destination.IsValid() || !renderGraph.CanAddCopyPass(source, destination))
                    continue;

                renderGraph.AddCopyPass(source, destination, $"{passType.Name}.{previewFieldName} Preview");
            }
        }

        internal static bool ShouldRecordTexturePreview(RenderGraphPassDefinition passDefinition, PassResourceEntry entry)
        {
            if (!RenderGraphPreviewRegistry.IsAvailable
                || passDefinition?.PreviewTextureFields == null
                || passDefinition.PreviewTextureFields.Count == 0
                || entry?.Texture == null
                || entry.IsDepthAttachment)
            {
                return false;
            }

            // PreviewTextureFields is the authoritative opt-in — check it regardless of access flags.
            // Debug export ports add read-only resources to PreviewTextureFields explicitly,
            // so the Write-only guard is not applied here.
            var previewKey = entry.Name;
            if (!string.IsNullOrEmpty(previewKey) && passDefinition.PreviewTextureFields.Contains(previewKey))
                return true;

            var legacyFieldName = entry.Field?.Name;
            return !string.IsNullOrEmpty(legacyFieldName)
                && passDefinition.PreviewTextureFields.Contains(legacyFieldName);
        }

        private static bool CanPreviewTexture(in RenderTargetInfo sourceInfo)
        {
            return sourceInfo.width > 0
                && sourceInfo.height > 0
                && sourceInfo.format != GraphicsFormat.None
                && !GraphicsFormatUtility.IsDepthFormat(sourceInfo.format);
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
            builder.SetRenderFunc<RenderGizmosPassData>(static (data, context) =>
            {
                Gizmos.exposure = data.ExposureTexture;
                if (data.HasDepth)
                    context.cmd.SetRenderTarget(data.Color, data.Depth);
                else
                    context.cmd.SetRenderTarget(data.Color);
                context.cmd.DrawRendererList(data.GizmoRendererList);
            });
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
            EnsureCompiled(graphAsset);
            RestoreInjectedFinalBlitSources();
            var injectedCmaa2Pass = ShouldInjectCmaa2Pass()
                ? GetOrCreateInjectedCmaa2Pass()
                : null;

            s_CurrentRenderGraph = renderGraph;
            BlueNoise.Instance?.ImportResources(renderGraph);

            foreach (var pass in s_RenderPasses)
            {
                pass.Prepare(s_FrameData);
            }

            if (injectedCmaa2Pass != null)
                injectedCmaa2Pass.Prepare(s_FrameData);

            PreparePendingHistoryTextureImports(renderGraph);
            s_CurrentRenderGraph = null;

            var textureCache = new Dictionary<RenderGraphTexture, TextureHandle>();
            var bufferCache = new Dictionary<RenderGraphBuffer, BufferHandle>();
            var renderListCache = new Dictionary<RenderGraphRenderList, RendererListHandle>();
            var accelerationStructureCache = new Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle>();
            var shouldRecordPreviews = RenderGraphPreviewRegistry.IsAvailable;
            var recordedPreImageEffectGizmos = false;

            var passDefinitions = s_RuntimePassDefinitions;
            for (var passIndex = 0; passIndex < s_RenderPasses.Count; passIndex++)
            {
                var pass = s_RenderPasses[passIndex];
#if UNITY_EDITOR
                if (!recordedPreImageEffectGizmos && pass is IRenderGizmoPrePostProcessBoundaryPass)
                {
                    var boundaryResources = GetCurrentPassResources(pass);
                    if (TryGetPreImageEffectGizmoTargets(
                            passIndex,
                            boundaryResources,
                            out var gizmoColorTexture,
                            out var gizmoDepthTexture))
                    {
                        RecordRenderGizmosPass(
                            renderGraph,
                            GizmoSubset.PreImageEffects,
                            gizmoColorTexture,
                            gizmoDepthTexture,
                            textureCache);
                        s_RenderedPreImageEffectGizmosInGraph = true;
                    }
                    recordedPreImageEffectGizmos = true;
                }
#endif
                if (injectedCmaa2Pass != null && pass is FinalBlitPass finalBlitPass)
                {
                    RecordInjectedCmaa2Pass(
                        renderGraph,
                        injectedCmaa2Pass,
                        finalBlitPass,
                        enableAsyncCompute,
                        textureCache,
                        bufferCache,
                        renderListCache,
                        accelerationStructureCache);
                }

                var resources = GetCurrentPassResources(pass);
                var passDefinition = passDefinitions != null && passIndex < passDefinitions.Count
                    ? passDefinitions[passIndex]
                    : null;

                if (pass is ComputePass computePass)
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
                    if (shouldRecordPreviews)
                        RecordTexturePreviewPasses(renderGraph, computePass, resources, passDefinition);
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
                    if (shouldRecordPreviews)
                        RecordTexturePreviewPasses(renderGraph, rasterPass, resources, passDefinition);
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
                    if (shouldRecordPreviews)
                        RecordTexturePreviewPasses(renderGraph, unsafePass, resources, passDefinition);
                }
            }
        }

        private static PassResource GetCurrentPassResources(IRenderPass pass)
        {
            var needsRefresh = pass is IDynamicPassResourceLayout dynamicLayoutPass
                               && dynamicLayoutPass.IsPassResourceLayoutDirty;

            if (!s_PassResources.TryGetValue(pass, out var resources) || needsRefresh)
            {
                resources = pass.Initialize();
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
