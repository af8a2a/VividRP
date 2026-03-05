using System;
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
            string passName = null)
        {
            using var builder = renderGraph.AddComputePass<ComputePassData>(
                passName ?? pass.GetType().Name, out var passData);

            passData.Pass = pass;

            SetupResources(renderGraph, builder, resource);

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
            string passName = null)
        {
            using var builder = renderGraph.AddRasterRenderPass<RasterPassData>(
                passName ?? pass.GetType().Name, out var passData);

            passData.Pass = pass;

            SetupRasterResources(renderGraph, builder, resource);

            builder.SetRenderFunc<RasterPassData>(static (data, ctx) => { data.Pass.Record(ctx); });
        }

        /// <summary>
        /// Records an unsafe pass. Creates resources from PassResource, sets up builder calls,
        /// and wires the render func to call pass.Record().
        /// </summary>
        static void RecordUnsafePass(
            RenderGraph renderGraph,
            UnsafePass pass,
            PassResource resource,
            string passName = null)
        {
            using var builder = renderGraph.AddUnsafePass<UnsafePassData>(
                passName ?? pass.GetType().Name, out var passData);

            passData.Pass = pass;

            SetupResources(renderGraph, builder, resource);

            builder.SetRenderFunc<UnsafePassData>(static (data, ctx) => { data.Pass.Record(ctx); });
        }

        /// <summary>
        /// Sets up resources for compute and unsafe passes using IBaseRenderGraphBuilder.
        /// </summary>
        private static void SetupResources(
            RenderGraph renderGraph,
            IBaseRenderGraphBuilder builder,
            PassResource resource)
        {
            foreach (var entry in resource.Textures)
            {
                var desc = entry.TextureDesc;
                if (desc == null) continue;

                if (entry.Access == AccessFlags.Read)
                {
                    // Read-only textures are expected to be imported or created externally.
                    // Create a placeholder — the pipeline will replace this with the actual handle.
                    var handle = renderGraph.CreateTexture(desc.ToTextureDesc());
                    builder.UseTexture(handle, AccessFlags.Read);
                }
                else
                {
                    var handle = renderGraph.CreateTexture(desc.ToTextureDesc());
                    builder.UseTexture(handle, entry.Access);
                }
            }

            foreach (var entry in resource.Buffers)
            {
                var desc = entry.BufferDesc;
                if (desc == null) continue;

                var handle = renderGraph.CreateBuffer(desc.ToBufferDesc());
                builder.UseBuffer(handle, entry.Access);
            }
        }

        /// <summary>
        /// Sets up resources for raster passes. Handles color/depth attachments
        /// in addition to regular texture/buffer usage.
        /// </summary>
        private static void SetupRasterResources(
            RenderGraph renderGraph,
            IRasterRenderGraphBuilder builder,
            PassResource resource)
        {
            foreach (var entry in resource.Textures)
            {
                var desc = entry.TextureDesc;
                if (desc == null) continue;

                var handle = renderGraph.CreateTexture(desc.ToTextureDesc());

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
                var desc = entry.BufferDesc;
                if (desc == null) continue;

                var handle = renderGraph.CreateBuffer(desc.ToBufferDesc());
                builder.UseBuffer(handle, entry.Access);
            }
        }
    }
}