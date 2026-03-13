using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using VividRP.Runtime;

namespace VividRP.Runtime
{
    public static partial class PassRecorder
    {
        private static readonly List<IRenderPass> s_RenderPasses = new();
        private static readonly ContextContainer s_FrameData = new();
        private static readonly Dictionary<IRenderPass, PassResource> s_PassResources = new();
        private static readonly Dictionary<IRenderPass, Dictionary<string, AccessFlags>> s_PassResourceAccessOverrides = new();
        private static RenderGraphTexture[] s_HistoryPreviousTextures = Array.Empty<RenderGraphTexture>();
        private static RenderGraphTexture[] s_HistoryCurrentTextures = Array.Empty<RenderGraphTexture>();
        private static readonly Dictionary<RenderGraphTexture, RTHandle> s_ImportedRTHandles = new();
        private static readonly Dictionary<IRenderPass, List<RTHandle>> s_PassImportedHandles = new();
        private static RenderGraph s_CurrentRenderGraph;

        private static RenderGraphData s_CurrentGraphAsset;
        private static long s_CurrentImportVersion;
        private static bool s_IsCompiled;

        internal static void InitializeContext(ScriptableRenderContext context, Camera camera, CullingResults cullingResults)
        {
            var renderingData = s_FrameData.GetOrCreate<VividRenderingData>();
            var cameraData = s_FrameData.GetOrCreate<VividCameraData>();
            var lightData = s_FrameData.GetOrCreate<VividLightData>();
            var additionalCameraData = camera.GetComponent<VividAdditionalCameraData>();
            if (additionalCameraData == null && camera.cameraType == CameraType.Game)
                additionalCameraData = camera.GetVividAdditionalCameraData();
            if (additionalCameraData != null)
                additionalCameraData.UpdateCameraMatrices(camera.targetTexture != null);

            cameraData.camera = camera;
            cameraData.additionalData = additionalCameraData;
            cameraData.renderType = additionalCameraData != null ? additionalCameraData.renderType : VividCameraRenderType.Base;
            cameraData.clearDepth = additionalCameraData == null || additionalCameraData.clearDepth;
            cameraData.pixelWidth = camera.pixelWidth;
            cameraData.pixelHeight = camera.pixelHeight;
            cameraData.pixelRect = camera.pixelRect;
            cameraData.actualWidth = camera.scaledPixelWidth;
            cameraData.actualHeight = camera.scaledPixelHeight;
            renderingData.cullingResults = cullingResults;
            renderingData.context = context;
            lightData.Update(cullingResults);
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

            s_RenderPasses.Clear();
            s_PassResources.Clear();
            s_PassResourceAccessOverrides.Clear();
            s_HistoryPreviousTextures = Array.Empty<RenderGraphTexture>();
            s_HistoryCurrentTextures = Array.Empty<RenderGraphTexture>();
            s_ImportedRTHandles.Clear();
            s_PassImportedHandles.Clear();
            s_CurrentRenderGraph = null;
            RenderGraphHistoryRegistry.Clear();
            RenderGraphPreviewRegistry.Clear();
            s_CurrentGraphAsset = null;
            s_CurrentImportVersion = 0;
            s_IsCompiled = false;
        }

        internal static void PrepareFrame(RenderGraphData graphAsset, CommandBuffer cmdBuffer)
        {
            EnsureCompiled(graphAsset);
            PrepareHistoryTargets(graphAsset, cmdBuffer);
            ClearImportedTextures();
        }

        /// <summary>
        /// Imports an external RTHandle for a specific pass during Prepare().
        /// Returns a TextureHandle that can be assigned to pass member variables.
        /// </summary>
        internal static TextureHandle ImportTextureForPass(IRenderPass pass, RTHandle rtHandle)
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
                handles = new List<RTHandle>();
                s_PassImportedHandles[pass] = handles;
            }
            handles.Add(rtHandle);

            return handle;
        }

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

        /// <summary>
        /// Clears all imported textures at the start of each frame.
        /// </summary>
        private static void ClearImportedTextures()
        {
            s_ImportedRTHandles.Clear();
            s_PassImportedHandles.Clear();
            s_CurrentRenderGraph = null;
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

            if (graphAsset == null || graphAsset.Passes == null || graphAsset.Passes.Count == 0)
            {
                // Fallback graph (keeps the pipeline running without an authored asset).
                s_RenderPasses.Add(new FullScreenPass());
            }
            else
            {
                var textures = CreateRuntimeTextures(graphAsset);
                CreateRuntimeHistoryTextures(graphAsset, out s_HistoryPreviousTextures, out s_HistoryCurrentTextures);
                var buffers = CreateRuntimeBuffers(graphAsset);
                var renderLists = CreateRuntimeRenderLists(graphAsset);
                var indexedPasses = new IRenderPass[graphAsset.Passes.Count];
                var indexedPassTypes = new Type[graphAsset.Passes.Count];

                for (var passIndex = 0; passIndex < graphAsset.Passes.Count; passIndex++)
                {
                    var passDef = graphAsset.Passes[passIndex];
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

                    ApplyResourceBindings(
                        pass,
                        passType,
                        passDef,
                        textures,
                        s_HistoryPreviousTextures,
                        s_HistoryCurrentTextures,
                        buffers,
                        renderLists);

                    var accessOverrides = BuildResourceAccessOverrides(passType, passDef);
                    if (accessOverrides != null && accessOverrides.Count > 0)
                        s_PassResourceAccessOverrides[pass] = accessOverrides;

                    indexedPasses[passIndex] = pass;
                    indexedPassTypes[passIndex] = passType;
                    s_RenderPasses.Add(pass);
                }

                for (var passIndex = 0; passIndex < graphAsset.Passes.Count; passIndex++)
                {
                    var pass = indexedPasses[passIndex];
                    var passType = indexedPassTypes[passIndex];
                    if (pass == null || passType == null)
                        continue;

                    ApplyPassFieldBindings(
                        passIndex,
                        pass,
                        passType,
                        graphAsset.Passes,
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
            RenderGraphRenderList[] renderLists)
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
            if (cmdBuffer == null || graphAsset?.HistoryTextureDescriptors == null || graphAsset.HistoryTextureDescriptors.Count == 0)
                return;

            var camera = s_FrameData.GetOrCreate<VividCameraData>().camera;
            if (camera == null)
                return;

            for (var i = 0; i < graphAsset.HistoryTextureDescriptors.Count; i++)
            {
                RenderGraphHistoryRegistry.GetOrCreateHistoryTarget(camera, graphAsset, i, graphAsset.HistoryTextureDescriptors[i], cmdBuffer);
            }
        }

        private static void PrepareFrameHistoryTextures(RenderGraph renderGraph, RenderGraphData graphAsset)
        {
            if (renderGraph == null)
                return;

            foreach (var texture in s_HistoryPreviousTextures)
            {
                texture?.ClearImportedHandle();
            }

            foreach (var texture in s_HistoryCurrentTextures)
            {
                texture?.ClearImportedHandle();
            }

            if (graphAsset?.HistoryTextureDescriptors == null || graphAsset.HistoryTextureDescriptors.Count == 0)
                return;

            var camera = s_FrameData.GetOrCreate<VividCameraData>().camera;
            if (camera == null)
                return;

            for (var i = 0; i < graphAsset.HistoryTextureDescriptors.Count && i < s_HistoryPreviousTextures.Length; i++)
            {
                if (!RenderGraphHistoryRegistry.TryGetHistoryTarget(camera, graphAsset, i, out var target, out _)
                    || target == null)
                {
                    continue;
                }

                s_HistoryPreviousTextures[i].SetImportedHandle(renderGraph.ImportTexture(target));
            }
        }

        private static void RecordHistoryUpdatePasses(RenderGraph renderGraph, RenderGraphData graphAsset)
        {
            if (renderGraph == null || graphAsset?.HistoryTextureDescriptors == null || graphAsset.HistoryTextureDescriptors.Count == 0)
                return;

            var camera = s_FrameData.GetOrCreate<VividCameraData>().camera;
            if (camera == null)
                return;

            for (var i = 0; i < graphAsset.HistoryTextureDescriptors.Count && i < s_HistoryCurrentTextures.Length; i++)
            {
                var currentTexture = s_HistoryCurrentTextures[i];
                if (currentTexture == null || !currentTexture.innerHandle.IsValid() || !ShouldPersistHistoryTexture(currentTexture))
                    continue;

                var target = RenderGraphHistoryRegistry.GetOrCreateHistoryTarget(camera, graphAsset, i, graphAsset.HistoryTextureDescriptors[i]);
                if (target == null)
                    continue;

                var destination = renderGraph.ImportTexture(target);
                if (!destination.IsValid() || !renderGraph.CanAddCopyPass(currentTexture.innerHandle, destination))
                    continue;

                var historyName = graphAsset.HistoryTextureDescriptors[i]?.Name;
                if (string.IsNullOrEmpty(historyName))
                    historyName = $"History_{i}";

                renderGraph.AddCopyPass(currentTexture.innerHandle, destination, $"{historyName} Persist");
                RenderGraphHistoryRegistry.MarkHistoryValid(camera, graphAsset, i);
            }
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
                || (entry.Access & AccessFlags.Write) == 0
                || entry.IsDepthAttachment)
            {
                return false;
            }

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

        public static void RecordRenderGraph(RenderGraph renderGraph, ScriptableRenderContext context, RenderGraphData graphAsset)
        {
            EnsureCompiled(graphAsset);
            PrepareFrameHistoryTextures(renderGraph, graphAsset);

            s_CurrentRenderGraph = renderGraph;

            foreach (var pass in s_RenderPasses)
            {
                pass.Prepare(s_FrameData);
            }

            s_CurrentRenderGraph = null;

            var textureCache = new Dictionary<RenderGraphTexture, TextureHandle>();
            var bufferCache = new Dictionary<RenderGraphBuffer, BufferHandle>();
            var renderListCache = new Dictionary<RenderGraphRenderList, RendererListHandle>();
            var shouldRecordPreviews = RenderGraphPreviewRegistry.IsAvailable;

            var passDefinitions = graphAsset?.Passes;
            for (var passIndex = 0; passIndex < s_RenderPasses.Count; passIndex++)
            {
                var pass = s_RenderPasses[passIndex];
                var resources = GetCurrentPassResources(pass);
                var passDefinition = passDefinitions != null && passIndex < passDefinitions.Count
                    ? passDefinitions[passIndex]
                    : null;

                if (pass is ComputePass computePass)
                {
                    RecordComputePass(renderGraph, computePass, resources, textureCache, bufferCache, renderListCache);
                    if (shouldRecordPreviews)
                        RecordTexturePreviewPasses(renderGraph, computePass, resources, passDefinition);
                }
                else if (pass is RasterPass rasterPass)
                {
                    RecordRasterPass(renderGraph, rasterPass, resources, textureCache, bufferCache, renderListCache);
                    if (shouldRecordPreviews)
                        RecordTexturePreviewPasses(renderGraph, rasterPass, resources, passDefinition);
                }
                else if (pass is UnsafePass unsafePass)
                {
                    RecordUnsafePass(renderGraph, unsafePass, resources, textureCache, bufferCache, renderListCache);
                    if (shouldRecordPreviews)
                        RecordTexturePreviewPasses(renderGraph, unsafePass, resources, passDefinition);
                }
            }

            RecordHistoryUpdatePasses(renderGraph, graphAsset);
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
    }
}
