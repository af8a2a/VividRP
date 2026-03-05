using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

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

                foreach (var passDef in graphAsset.Passes)
                {
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

                    s_RenderPasses.Add(pass);
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
                }
                else if (pass is RasterPass rasterPass)
                {
                    RecordRasterPass(renderGraph, rasterPass, resources, textureCache, bufferCache);
                }
                else if (pass is UnsafePass unsafePass)
                {
                    RecordUnsafePass(renderGraph, unsafePass, resources, textureCache, bufferCache);
                }
            }
        }
    }
}
