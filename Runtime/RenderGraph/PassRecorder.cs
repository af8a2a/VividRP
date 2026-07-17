using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    /// <summary>
    /// Automatically records a RenderGraph pass from a PassResource.
    /// Creates resources, calls builder methods, and invokes the pass's Record().
    /// </summary>
    public static partial class PassRecorder
    {
        private const BindingFlags BuilderMethodFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo s_AccelerationStructureResourceHandleField =
            typeof(RayTracingAccelerationStructureHandle).GetField("handle", BuilderMethodFlags);

        private readonly struct ImportedPassHandle : IEquatable<ImportedPassHandle>
        {
            private ImportedPassHandle(TextureHandle textureHandle, BufferHandle bufferHandle, bool isBuffer, AccessFlags access)
            {
                TextureHandle = textureHandle;
                BufferHandle = bufferHandle;
                IsBuffer = isBuffer;
                Access = access;
            }

            public static ImportedPassHandle Texture(TextureHandle handle, AccessFlags access)
            {
                return new ImportedPassHandle(handle, default, false, access);
            }

            public static ImportedPassHandle Buffer(BufferHandle handle, AccessFlags access)
            {
                return new ImportedPassHandle(default, handle, true, access);
            }

            public TextureHandle TextureHandle { get; }

            public BufferHandle BufferHandle { get; }

            public bool IsBuffer { get; }

            public AccessFlags Access { get; }

            public bool IsValid => IsBuffer ? BufferHandle.IsValid() : TextureHandle.IsValid();

            public bool Equals(ImportedPassHandle other)
            {
                return TextureHandle.Equals(other.TextureHandle)
                    && BufferHandle.Equals(other.BufferHandle)
                    && IsBuffer == other.IsBuffer
                    && Access == other.Access;
            }

            public override bool Equals(object obj)
            {
                return obj is ImportedPassHandle other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(TextureHandle, BufferHandle, IsBuffer, Access);
            }
        }

        private class ComputePassData
        {
            public ComputePass Pass;
            public RenderPassProfilerMarkers Markers;
        }

        private class RasterPassData
        {
            public RasterPass Pass;
            public RenderPassProfilerMarkers Markers;
        }

        private class UnsafePassData
        {
            public UnsafePass Pass;
            public RenderPassProfilerMarkers Markers;
        }

        private static readonly BaseRenderFunc<ComputePassData, ComputeGraphContext> s_ComputeRenderFunc =
            ExecuteComputePass;
        private static readonly BaseRenderFunc<RasterPassData, RasterGraphContext> s_RasterRenderFunc =
            ExecuteRasterPass;
        private static readonly BaseRenderFunc<UnsafePassData, UnsafeGraphContext> s_UnsafeRenderFunc =
            ExecuteUnsafePass;

        /// <summary>
        /// Records a compute pass. Creates resources from PassResource, sets up builder calls,
        /// and wires the render func to call pass.Record().
        /// </summary>
        internal static void RecordComputePass(
            RenderGraph renderGraph,
            ComputePass pass,
            PassResource resource,
            RenderGraphPassDefinition passDefinition,
            bool enableAsyncCompute,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache,
            string passName = null)
        {
            var markers = GetPassMarkers(pass, passName);
            using var recordGraphScope = markers.RecordGraph.Auto();
            using (markers.RecordGraphBuild.Auto())
            {
                using var builder = renderGraph.AddComputePass<ComputePassData>(
                    markers.GraphName, out var passData);

                passData.Pass = pass;
                passData.Markers = markers;

                using (markers.RecordGraphSetupResources.Auto())
                {
                    SetupComputeResources(
                        renderGraph,
                        builder,
                        resource,
                        textureCache,
                        bufferCache,
                        renderListCache,
                        accelerationStructureCache);
                }

                using (markers.RecordGraphSetupImportedHandles.Auto())
                {
                    SetupImportedHandles(builder, pass);
                }

                using (markers.RecordGraphConfigureBuilder.Auto())
                {
                    ConfigureGlobalStateModification(builder, pass);
                    if (ShouldEnableAsyncCompute(enableAsyncCompute, pass, passDefinition))
                        builder.EnableAsyncCompute(true);
                }

                using (markers.RecordGraphSetRenderFunc.Auto())
                {
                    builder.SetRenderFunc<ComputePassData>(s_ComputeRenderFunc);
                }
            }
        }

        /// <summary>
        /// Records a raster pass. Creates resources, sets up attachments and builder calls,
        /// and wires the render func to call pass.Record().
        /// </summary>
        internal static void RecordRasterPass(
            RenderGraph renderGraph,
            RasterPass pass,
            PassResource resource,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache,
            string passName = null)
        {
            var markers = GetPassMarkers(pass, passName);
            using var recordGraphScope = markers.RecordGraph.Auto();
            using (markers.RecordGraphBuild.Auto())
            {
                using var builder = renderGraph.AddRasterRenderPass<RasterPassData>(
                    markers.GraphName, out var passData);

                passData.Pass = pass;
                passData.Markers = markers;

                using (markers.RecordGraphSetupResources.Auto())
                {
                    SetupRasterResources(
                        renderGraph,
                        builder,
                        resource,
                        textureCache,
                        bufferCache,
                        renderListCache,
                        accelerationStructureCache);
                }

                using (markers.RecordGraphSetupImportedHandles.Auto())
                {
                    SetupImportedHandles(builder, pass);
                }

                using (markers.RecordGraphConfigureBuilder.Auto())
                {
                    ConfigureGlobalStateModification(builder, pass);
                }

                using (markers.RecordGraphSetRenderFunc.Auto())
                {
                    builder.SetRenderFunc<RasterPassData>(s_RasterRenderFunc);
                }
            }
        }

        /// <summary>
        /// Records an unsafe pass. Creates resources from PassResource, sets up builder calls,
        /// and wires the render func to call pass.Record().
        /// </summary>
        internal static void RecordUnsafePass(
            RenderGraph renderGraph,
            UnsafePass pass,
            PassResource resource,
            RenderGraphPassDefinition passDefinition,
            bool enableAsyncCompute,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache,
            string passName = null)
        {
            var markers = GetPassMarkers(pass, passName);
            using var recordGraphScope = markers.RecordGraph.Auto();
            using (markers.RecordGraphBuild.Auto())
            {
                using var builder = renderGraph.AddUnsafePass<UnsafePassData>(
                    markers.GraphName, out var passData);

                passData.Pass = pass;
                passData.Markers = markers;

                using (markers.RecordGraphSetupResources.Auto())
                {
                    SetupUnsafeResources(
                        renderGraph,
                        builder,
                        resource,
                        textureCache,
                        bufferCache,
                        renderListCache,
                        accelerationStructureCache);
                }

                using (markers.RecordGraphSetupImportedHandles.Auto())
                {
                    SetupImportedHandles(builder, pass);
                }

                using (markers.RecordGraphConfigureBuilder.Auto())
                {
                    ConfigureGlobalStateModification(builder, pass);
                    if (ShouldEnableAsyncCompute(enableAsyncCompute, pass, passDefinition))
                        builder.EnableAsyncCompute(true);
                }

                using (markers.RecordGraphSetRenderFunc.Auto())
                {
                    builder.SetRenderFunc<UnsafePassData>(s_UnsafeRenderFunc);
                }
            }
        }

        private static void ExecuteComputePass(ComputePassData data, ComputeGraphContext ctx)
        {
            using var recordScope = data.Markers.Record.Auto();
            data.Pass.Record(new ComputePassContext(ctx, s_FrameData));
        }

        private static void ExecuteRasterPass(RasterPassData data, RasterGraphContext ctx)
        {
            using var recordScope = data.Markers.Record.Auto();
            data.Pass.Record(new RasterPassContext(ctx, s_FrameData));
        }

        private static void ExecuteUnsafePass(UnsafePassData data, UnsafeGraphContext ctx)
        {
            var passContext = new UnsafePassContext(ctx, s_FrameData);
            using var recordScope = data.Markers.Record.Auto();
            data.Pass.Record(passContext);
        }

        internal static bool ShouldEnableAsyncCompute(
            bool enableAsyncCompute,
            IRenderPass pass,
            RenderGraphPassDefinition passDefinition)
        {
            return enableAsyncCompute
                && pass != null
                && passDefinition != null
                && passDefinition.EnableAsyncCompute
                && SystemInfo.supportsAsyncCompute
                && RenderGraphPassExecutionUtility.SupportsAsyncCompute(pass.GetType());
        }

        private static void SetupImportedHandles(IBaseRenderGraphBuilder builder, IRenderPass pass)
        {
            if (pass is IBlueNoiseConsumerPass)
                BlueNoise.Instance?.RegisterPassResources(pass);

            if (builder == null
                || pass == null
                || !s_PassImportedHandles.TryGetValue(pass, out var importedHandles)
                || importedHandles == null)
            {
                return;
            }

            foreach (var importedHandle in importedHandles)
            {
                if (!importedHandle.IsValid)
                    continue;

                if (importedHandle.IsBuffer)
                    builder.UseBuffer(importedHandle.BufferHandle, importedHandle.Access);
                else
                    builder.UseTexture(importedHandle.TextureHandle, importedHandle.Access);
            }
        }

        private static void ConfigureGlobalStateModification(IBaseRenderGraphBuilder builder, IRenderPass pass)
        {
            if (builder == null || pass is not IAllowGlobalStateModificationPass)
                return;

            builder.AllowGlobalStateModification(true);
        }

        internal static TextureHandle GetOrCreateTextureHandle(
            RenderGraph renderGraph,
            RenderGraphTexture texture,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache)
        {
            if (textureCache.TryGetValue(texture, out var handle))
                return handle;

            // Check if this texture has an imported RTHandle
            if (texture != null && s_ImportedRTHandles.TryGetValue(texture, out var rtHandle))
            {
                handle = renderGraph.ImportTexture(rtHandle);
                texture.SetImportedHandle(handle);
                textureCache.Add(texture, handle);
                return handle;
            }

            if (texture != null && texture.HasImportedHandle)
            {
                handle = texture.innerHandle;
                textureCache.Add(texture, handle);
                return handle;
            }

            handle = renderGraph.CreateTexture(texture.desc);
            textureCache.Add(texture, handle);
            return handle;
        }

        internal static BufferHandle GetOrCreateBufferHandle(
            RenderGraph renderGraph,
            RenderGraphBuffer buffer,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache)
        {
            if (bufferCache.TryGetValue(buffer, out var handle))
                return handle;

            if (buffer != null && buffer.HasImportedBuffer)
            {
                handle = renderGraph.ImportBuffer(buffer.ImportedGraphicsBuffer);
                bufferCache.Add(buffer, handle);
                return handle;
            }

            handle = renderGraph.CreateBuffer(buffer.desc);
            bufferCache.Add(buffer, handle);
            return handle;
        }

        private static TextureHandle CreateTransientTextureHandle(
            IBaseRenderGraphBuilder builder,
            RenderGraphTexture texture)
        {
            if (builder == null || texture == null)
                return default;

            if (s_ImportedRTHandles.ContainsKey(texture) || texture.HasImportedHandle)
            {
                Debug.LogWarning(
                    $"[VividRP] Transient texture '{texture.desc?.Name ?? "<Unnamed>"}' ignores imported texture handles.");
            }

            var handle = builder.CreateTransientTexture(texture.desc);
            texture.innerHandle = handle;
            return handle;
        }

        private static BufferHandle CreateTransientBufferHandle(
            IBaseRenderGraphBuilder builder,
            RenderGraphBuffer buffer)
        {
            if (builder == null || buffer == null)
                return default;

            if (buffer.HasImportedBuffer)
            {
                Debug.LogWarning(
                    $"[VividRP] Transient buffer '{buffer.desc?.Name ?? "<Unnamed>"}' ignores imported graphics buffers.");
            }

            var handle = builder.CreateTransientBuffer(buffer.desc);
            buffer.innerHandle = handle;
            return handle;
        }

        private static RendererListHandle GetOrCreateRenderListHandle(
            RenderGraph renderGraph,
            RenderGraphRenderList renderList,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache)
        {
            if (renderList == null)
                return default;

            if (renderListCache.TryGetValue(renderList, out var handle))
                return handle;

            var camera = s_FrameData.GetOrCreate<VividCameraData>().camera;
            if (camera == null)
                return default;

            var renderingData = s_FrameData.GetOrCreate<VividRenderingData>();
            var desc = renderList.desc?.CreateRendererListDesc(renderingData.cullingResults, camera) ?? default;
            if (!desc.IsValid())
                return default;

            handle = renderGraph.CreateRendererList(desc);
            renderListCache.Add(renderList, handle);
            return handle;
        }

        private static RayTracingAccelerationStructureHandle GetOrCreateAccelerationStructureHandle(
            RenderGraph renderGraph,
            RenderGraphAccelerationStructure accelerationStructure,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache)
        {
            if (accelerationStructure == null)
                return default;

            if (accelerationStructureCache.TryGetValue(accelerationStructure, out var handle))
                return handle;

            if (!accelerationStructure.HasAccelerationStructure)
            {
                if (!SystemInfo.supportsRayTracing)
                    return default;

                accelerationStructure.EnsureCreated();
            }

            var nativeAccelerationStructure = (RayTracingAccelerationStructure)accelerationStructure;
            if (nativeAccelerationStructure == null)
                return default;

            var name = accelerationStructure.desc?.Name;
            handle = renderGraph.ImportRayTracingAccelerationStructure(nativeAccelerationStructure, name);
            accelerationStructure.innerHandle = handle;
            accelerationStructureCache.Add(accelerationStructure, handle);
            return handle;
        }

        private static void SetupRenderLists(
            RenderGraph renderGraph,
            IBaseRenderGraphBuilder builder,
            PassResource resource,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache)
        {
            using var setupRenderListsScope = RenderPassProfilingUtility.RecordGraphSetupRenderListsMarker.Auto();

            var usesRendererList = false;

            foreach (var entry in resource.RenderLists)
            {
                var renderList = entry.RenderList;
                if (renderList == null)
                    continue;

                var handle = GetOrCreateRenderListHandle(renderGraph, renderList, renderListCache);
                renderList.innerHandle = handle;

                if (!handle.IsValid())
                    continue;

                builder.UseRendererList(handle);
                usesRendererList = true;
            }

            if (usesRendererList)
                builder.UseAllGlobalTextures(true);
        }

        private static void SetupAccelerationStructures(
            RenderGraph renderGraph,
            IBaseRenderGraphBuilder builder,
            PassResource resource,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache)
        {
            using var setupAccelerationStructuresScope = RenderPassProfilingUtility.RecordGraphSetupAccelerationStructuresMarker.Auto();

            foreach (var entry in resource.AccelerationStructures)
            {
                var accelerationStructure = entry.AccelerationStructure;
                if (accelerationStructure == null)
                    continue;

                var handle = GetOrCreateAccelerationStructureHandle(
                    renderGraph,
                    accelerationStructure,
                    accelerationStructureCache);
                if (!handle.IsValid())
                    continue;

                UseAccelerationStructure(builder, handle, entry.Access);
            }
        }

        /// <summary>
        /// Sets up resources for compute and unsafe passes using IBaseRenderGraphBuilder.
        /// </summary>
        private static void SetupUnsafeResources(
            RenderGraph renderGraph,
            IUnsafeRenderGraphBuilder builder,
            PassResource resource,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache)
        {
            using (RenderPassProfilingUtility.RecordGraphSetupTexturesMarker.Auto())
            {
                foreach (var entry in resource.Textures)
                {
                    var texture = entry.Texture;
                    if (texture == null) continue;

                    if (entry.IsTransient)
                    {
                        CreateTransientTextureHandle(builder, texture);
                        continue;
                    }

                    // Read-only textures are expected to be imported or created externally.
                    // Create a placeholder — the pipeline will replace this with the actual handle.
                    var handle = GetOrCreateTextureHandle(renderGraph, texture, textureCache);
                    texture.innerHandle = handle;
                    builder.UseTexture(handle, entry.Access);
                }
            }

            using (RenderPassProfilingUtility.RecordGraphSetupBuffersMarker.Auto())
            {
                foreach (var entry in resource.Buffers)
                {
                    var buffer = entry.Buffer;
                    if (buffer == null) continue;

                    if (entry.IsTransient)
                    {
                        CreateTransientBufferHandle(builder, buffer);
                        continue;
                    }

                    var handle = GetOrCreateBufferHandle(renderGraph, buffer, bufferCache);
                    buffer.innerHandle = handle;
                    builder.UseBuffer(handle, entry.Access);
                }
            }

            SetupRenderLists(renderGraph, builder, resource, renderListCache);
            SetupAccelerationStructures(renderGraph, builder, resource, accelerationStructureCache);

            builder.AllowPassCulling(false);
        }

        /// <summary>
        /// Sets up resources for compute and unsafe passes using IBaseRenderGraphBuilder.
        /// </summary>
        private static void SetupComputeResources(
            RenderGraph renderGraph,
            IComputeRenderGraphBuilder builder,
            PassResource resource,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache)
        {
            using (RenderPassProfilingUtility.RecordGraphSetupTexturesMarker.Auto())
            {
                foreach (var entry in resource.Textures)
                {
                    var texture = entry.Texture;
                    if (texture == null) continue;

                    if (entry.IsTransient)
                    {
                        CreateTransientTextureHandle(builder, texture);
                        continue;
                    }

                    // Read-only textures are expected to be imported or created externally.
                    // Create a placeholder — the pipeline will replace this with the actual handle.
                    var handle = GetOrCreateTextureHandle(renderGraph, texture, textureCache);
                    texture.innerHandle = handle;
                    builder.UseTexture(handle, entry.Access);
                }
            }

            using (RenderPassProfilingUtility.RecordGraphSetupBuffersMarker.Auto())
            {
                foreach (var entry in resource.Buffers)
                {
                    var buffer = entry.Buffer;
                    if (buffer == null) continue;

                    if (entry.IsTransient)
                    {
                        CreateTransientBufferHandle(builder, buffer);
                        continue;
                    }

                    var handle = GetOrCreateBufferHandle(renderGraph, buffer, bufferCache);
                    buffer.innerHandle = handle;
                    builder.UseBuffer(handle, entry.Access);
                }
            }

            SetupRenderLists(renderGraph, builder, resource, renderListCache);
            SetupAccelerationStructures(renderGraph, builder, resource, accelerationStructureCache);

            builder.AllowPassCulling(false);
        }

        /// <summary>
        /// Sets up resources for raster passes. Handles color/depth attachments
        /// in addition to regular texture/buffer usage.
        /// </summary>
        private static void SetupRasterResources(
            RenderGraph renderGraph,
            IRasterRenderGraphBuilder builder,
            PassResource resource,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache)
        {
            using (RenderPassProfilingUtility.RecordGraphSetupTexturesMarker.Auto())
            {
                foreach (var entry in resource.Textures)
                {
                    var texture = entry.Texture;
                    if (texture == null) continue;

                    var handle = entry.IsTransient
                        ? CreateTransientTextureHandle(builder, texture)
                        : GetOrCreateTextureHandle(renderGraph, texture, textureCache);
                    texture.innerHandle = handle;

                    if (entry.InputAttachmentIndex >= 0)
                    {
                        builder.SetInputAttachment(handle, entry.InputAttachmentIndex, entry.Access);
                    }
                    else if (entry.IsDepthAttachment)
                    {
                        builder.SetRenderAttachmentDepth(handle, entry.Access);
                    }
                    else if (entry.AttachmentIndex >= 0)
                    {
                        builder.SetRenderAttachment(handle, entry.AttachmentIndex, entry.Access);
                    }
                    else
                    {
                        if (!entry.IsTransient)
                            builder.UseTexture(handle, entry.Access);
                    }
                }
            }

            using (RenderPassProfilingUtility.RecordGraphSetupBuffersMarker.Auto())
            {
                foreach (var entry in resource.Buffers)
                {
                    var buffer = entry.Buffer;
                    if (buffer == null) continue;

                    if (entry.IsTransient)
                    {
                        CreateTransientBufferHandle(builder, buffer);
                        continue;
                    }

                    var handle = GetOrCreateBufferHandle(renderGraph, buffer, bufferCache);
                    buffer.innerHandle = handle;
                    builder.UseBuffer(handle, entry.Access);
                }
            }

            SetupRenderLists(renderGraph, builder, resource, renderListCache);
            SetupAccelerationStructures(renderGraph, builder, resource, accelerationStructureCache);

            builder.AllowPassCulling(false);
        }

        private static void UseAccelerationStructure(
            IBaseRenderGraphBuilder builder,
            RayTracingAccelerationStructureHandle handle,
            AccessFlags access)
        {
            if (builder == null || !handle.IsValid())
                return;

            InvokeAccelerationStructureBuilderMethod(builder, handle, access);
        }

        private static void InvokeAccelerationStructureBuilderMethod(
            IBaseRenderGraphBuilder builder,
            RayTracingAccelerationStructureHandle handle,
            AccessFlags access)
        {
            if (s_AccelerationStructureResourceHandleField == null)
            {
                Debug.LogError("[VividRP] RenderGraph acceleration-structure handle field was not found.");
                return;
            }

            var method = builder.GetType().GetMethod("UseResource", BuilderMethodFlags);
            if (method == null)
            {
                Debug.LogError(
                    $"[VividRP] RenderGraph builder method 'UseResource' was not found on '{builder.GetType().FullName}'.");
                return;
            }

            var resourceHandle = s_AccelerationStructureResourceHandleField.GetValue(handle);
            var builderAccess = access;

            if ((builderAccess & AccessFlags.Write) != 0
                && (builderAccess & AccessFlags.Read) == 0)
            {
                builderAccess |= AccessFlags.Discard;
            }

            method.Invoke(builder, new[] { resourceHandle, (object)builderAccess });
        }
    }
}
