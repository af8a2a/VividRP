using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace VividRP.Runtime
{
    public static partial class PassRecorder
    {
        private static readonly List<IRenderPass> s_RenderPasses = new();
        private static readonly ContextContainer s_FrameData = new();
        private static readonly Dictionary<IRenderPass, PassResource> s_PassResources = new();

        private static RenderGraphData s_CurrentGraphAsset;
        private static long s_CurrentImportVersion;
        private static bool s_IsCompiled;

        internal static void InitializeContext(ScriptableRenderContext context, Camera camera)
        {
            var renderingData = s_FrameData.GetOrCreate<VividRenderingData>();
            var cameraData = s_FrameData.GetOrCreate<VividCameraData>();
            cameraData.camera = camera;
            cameraData.pixelWidth = camera.pixelWidth;
            cameraData.pixelHeight = camera.pixelHeight;
            cameraData.actualWidth = camera.scaledPixelWidth;
            cameraData.actualHeight = camera.scaledPixelHeight;
            renderingData.context = context;
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
            RenderGraphPreviewRegistry.Clear();
            s_CurrentGraphAsset = null;
            s_CurrentImportVersion = 0;
            s_IsCompiled = false;
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
                var buffers = CreateRuntimeBuffers(graphAsset);
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

                    ApplyResourceBindings(pass, passType, passDef, textures, buffers);

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
                s_PassResources[pass] = pass.Initialize();
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
            RenderGraphBuffer[] buffers)
        {
            if (passDef.ResourceBindings == null || passDef.ResourceBindings.Count == 0)
                return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var binding in passDef.ResourceBindings)
            {
                if (binding == null || string.IsNullOrEmpty(binding.FieldName))
                    continue;

                var field = passType.GetField(binding.FieldName, flags);
                if (field == null)
                    continue;

                if (binding.SourceKind == RenderGraphPassBindingSourceKind.PassField)
                    continue;

                switch (binding.ResourceKind)
                {
                    case RenderGraphResourceKind.Texture:
                        if (binding.ResourceIndex >= 0 && binding.ResourceIndex < textures.Length &&
                            field.FieldType == typeof(RenderGraphTexture))
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

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var binding in passDef.ResourceBindings)
            {
                if (binding == null || binding.SourceKind != RenderGraphPassBindingSourceKind.PassField || string.IsNullOrEmpty(binding.FieldName))
                    continue;

                var field = passType.GetField(binding.FieldName, flags);
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

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var sourceField = sourcePassType.GetField(fieldName, flags);
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

        private static void RecordTexturePreviewPasses(
            RenderGraph renderGraph,
            IRenderPass pass,
            PassResource resources)
        {
            if (renderGraph == null || pass == null || resources?.Textures == null || resources.Textures.Length == 0)
                return;

            var passType = pass.GetType();
            foreach (var entry in resources.Textures)
            {
                if (!ShouldRecordTexturePreview(entry))
                    continue;

                var source = entry.Texture.innerHandle;
                if (!source.IsValid())
                    continue;

                var sourceInfo = renderGraph.GetRenderTargetInfo(source);
                if (!CanPreviewTexture(sourceInfo))
                    continue;

                var previewTarget = RenderGraphPreviewRegistry.GetOrCreatePreviewTarget(
                    passType,
                    entry.Field.Name,
                    sourceInfo,
                    entry.Texture.desc);
                if (previewTarget == null)
                    continue;

                var destination = renderGraph.ImportTexture(previewTarget);
                if (!destination.IsValid() || !renderGraph.CanAddCopyPass(source, destination))
                    continue;

                renderGraph.AddCopyPass(source, destination, $"{passType.Name}.{entry.Field.Name} Preview");
            }
        }

        private static bool ShouldRecordTexturePreview(PassResourceEntry entry)
        {
            return entry != null
                && entry.Texture != null
                && entry.Field != null
                && !string.IsNullOrEmpty(entry.Field.Name)
                && (entry.Access & AccessFlags.Write) != 0
                && !entry.IsDepthAttachment;
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

            foreach (var pass in s_RenderPasses)
            {
                pass.Prepare(s_FrameData);
            }

            var textureCache = new Dictionary<RenderGraphTexture, TextureHandle>();
            var bufferCache = new Dictionary<RenderGraphBuffer, BufferHandle>();

            foreach (var pass in s_RenderPasses)
            {
                if (!s_PassResources.TryGetValue(pass, out var resources))
                    resources = pass.Initialize();

                if (pass is ComputePass computePass)
                {
                    RecordComputePass(renderGraph, computePass, resources, textureCache, bufferCache);
                    RecordTexturePreviewPasses(renderGraph, computePass, resources);
                }
                else if (pass is RasterPass rasterPass)
                {
                    RecordRasterPass(renderGraph, rasterPass, resources, textureCache, bufferCache);
                    RecordTexturePreviewPasses(renderGraph, rasterPass, resources);
                }
                else if (pass is UnsafePass unsafePass)
                {
                    RecordUnsafePass(renderGraph, unsafePass, resources, textureCache, bufferCache);
                    RecordTexturePreviewPasses(renderGraph, unsafePass, resources);
                }
            }
        }
    }
}

