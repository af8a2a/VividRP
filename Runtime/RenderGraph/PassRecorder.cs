using System;
using System.Collections.Generic;
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
        private class ComputePassData
        {
            public ComputePass Pass;
        }

        private class RasterPassData
        {
            public RasterPass Pass;
        }

        private class UnsafePassData
        {
            public UnsafePass Pass;
        }

        /// <summary>
        /// Records a compute pass. Creates resources from PassResource, sets up builder calls,
        /// and wires the render func to call pass.Record().
        /// </summary>
        static void RecordComputePass(
            RenderGraph renderGraph,
            ComputePass pass,
            PassResource resource,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            string passName = null)
        {
            using var builder = renderGraph.AddComputePass<ComputePassData>(
                passName ?? pass.GetType().Name, out var passData);

            passData.Pass = pass;

            SetupComputeResources(renderGraph, builder, resource, textureCache, bufferCache, renderListCache);

            builder.SetRenderFunc<ComputePassData>(static (data, ctx) => { data.Pass.Record(ctx); });
        }

        /// <summary>
        /// Records a raster pass. Creates resources, sets up attachments and builder calls,
        /// and wires the render func to call pass.Record().
        /// </summary>
        static void RecordRasterPass(
            RenderGraph renderGraph,
            RasterPass pass,
            PassResource resource,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            string passName = null)
        {
            using var builder = renderGraph.AddRasterRenderPass<RasterPassData>(
                passName ?? pass.GetType().Name, out var passData);

            passData.Pass = pass;

            SetupRasterResources(renderGraph, builder, resource, textureCache, bufferCache, renderListCache);

            builder.SetRenderFunc<RasterPassData>(static (data, ctx) =>
            {
                data.Pass.Record(ctx);
            });
        }

        /// <summary>
        /// Records an unsafe pass. Creates resources from PassResource, sets up builder calls,
        /// and wires the render func to call pass.Record().
        /// </summary>
        static void RecordUnsafePass(
            RenderGraph renderGraph,
            UnsafePass pass,
            PassResource resource,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            string passName = null)
        {
            using var builder = renderGraph.AddUnsafePass<UnsafePassData>(
                passName ?? pass.GetType().Name, out var passData);

            passData.Pass = pass;

            SetupUnsafeResources(renderGraph, builder, resource, textureCache, bufferCache, renderListCache);

            builder.SetRenderFunc<UnsafePassData>(static (data, ctx) => { data.Pass.Record(ctx); });
        }

        private static TextureHandle GetOrCreateTextureHandle(
            RenderGraph renderGraph,
            RenderGraphTexture texture,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache)
        {
            if (textureCache.TryGetValue(texture, out var handle))
                return handle;

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

        private static BufferHandle GetOrCreateBufferHandle(
            RenderGraph renderGraph,
            RenderGraphBuffer buffer,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache)
        {
            if (bufferCache.TryGetValue(buffer, out var handle))
                return handle;

            handle = renderGraph.CreateBuffer(buffer.desc);
            bufferCache.Add(buffer, handle);
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

        private static void SetupRenderLists(
            RenderGraph renderGraph,
            IBaseRenderGraphBuilder builder,
            PassResource resource,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache)
        {
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

        /// <summary>
        /// Sets up resources for compute and unsafe passes using IBaseRenderGraphBuilder.
        /// </summary>
        private static void SetupUnsafeResources(
            RenderGraph renderGraph,
            IUnsafeRenderGraphBuilder builder,
            PassResource resource,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache)
        {
            foreach (var entry in resource.Textures)
            {
                var texture = entry.Texture;
                if (texture == null) continue;

                // Read-only textures are expected to be imported or created externally.
                // Create a placeholder — the pipeline will replace this with the actual handle.
                var handle = GetOrCreateTextureHandle(renderGraph, texture, textureCache);
                texture.innerHandle = handle;
                builder.UseTexture(handle, entry.Access);
            }

            foreach (var entry in resource.Buffers)
            {
                var buffer = entry.Buffer;
                if (buffer == null) continue;

                var handle = GetOrCreateBufferHandle(renderGraph, buffer, bufferCache);
                buffer.innerHandle = handle;
                builder.UseBuffer(handle, entry.Access);
            }

            SetupRenderLists(renderGraph, builder, resource, renderListCache);

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
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache)
        {
            foreach (var entry in resource.Textures)
            {
                var texture = entry.Texture;
                if (texture == null) continue;

                // Read-only textures are expected to be imported or created externally.
                // Create a placeholder — the pipeline will replace this with the actual handle.
                var handle = GetOrCreateTextureHandle(renderGraph, texture, textureCache);
                texture.innerHandle = handle;
                builder.UseTexture(handle, entry.Access);
            }

            foreach (var entry in resource.Buffers)
            {
                var buffer = entry.Buffer;
                if (buffer == null) continue;

                var handle = GetOrCreateBufferHandle(renderGraph, buffer, bufferCache);
                buffer.innerHandle = handle;
                builder.UseBuffer(handle, entry.Access);
            }

            SetupRenderLists(renderGraph, builder, resource, renderListCache);

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
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache)
        {
            foreach (var entry in resource.Textures)
            {
                var texture = entry.Texture;
                if (texture == null) continue;

                var handle = GetOrCreateTextureHandle(renderGraph, texture, textureCache);
                texture.innerHandle = handle;

                if (entry.IsDepthAttachment)
                {
                    builder.SetRenderAttachmentDepth(handle, entry.Access);
                }
                else if (entry.AttachmentIndex >= 0)
                {
                    builder.SetRenderAttachment(handle, entry.AttachmentIndex, entry.Access);
                }
                else
                {
                    builder.UseTexture(handle, entry.Access);
                }
            }

            foreach (var entry in resource.Buffers)
            {
                var buffer = entry.Buffer;
                if (buffer == null) continue;

                var handle = GetOrCreateBufferHandle(renderGraph, buffer, bufferCache);
                buffer.innerHandle = handle;
                builder.UseBuffer(handle, entry.Access);
            }

            SetupRenderLists(renderGraph, builder, resource, renderListCache);

            builder.AllowPassCulling(false);
        }
    }
}
