using System;
using UnityEngine;
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

            SetupComputeResources(renderGraph, builder, resource);

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
            string passName = null)
        {
            using var builder = renderGraph.AddUnsafePass<UnsafePassData>(
                passName ?? pass.GetType().Name, out var passData);

            passData.Pass = pass;

            SetupUnsafeResources(renderGraph, builder, resource);

            builder.SetRenderFunc<UnsafePassData>(static (data, ctx) => { data.Pass.Record(ctx); });
        }


        /// <summary>
        /// Sets up resources for compute and unsafe passes using IBaseRenderGraphBuilder.
        /// </summary>
        private static void SetupUnsafeResources(
            RenderGraph renderGraph,
            IUnsafeRenderGraphBuilder builder,
            PassResource resource)
        {
            foreach (var entry in resource.Textures)
            {
                var texture = entry.Texture;
                if (texture == null) continue;

                // Read-only textures are expected to be imported or created externally.
                // Create a placeholder — the pipeline will replace this with the actual handle.
                var actualDesc = texture.desc;
                texture.innerHandle = renderGraph.CreateTexture(actualDesc);
                builder.UseTexture(texture.innerHandle, entry.Access);
            }

            foreach (var entry in resource.Buffers)
            {
                var buffer = entry.Buffer;
                if (buffer == null) continue;
                buffer.innerHandle = renderGraph.CreateBuffer(buffer.desc);
                builder.UseBuffer(buffer.innerHandle, entry.Access);
            }
            builder.AllowPassCulling(false);

        }


        /// <summary>
        /// Sets up resources for compute and unsafe passes using IBaseRenderGraphBuilder.
        /// </summary>
        private static void SetupComputeResources(
            RenderGraph renderGraph,
            IComputeRenderGraphBuilder builder,
            PassResource resource)
        {
            foreach (var entry in resource.Textures)
            {
                var texture = entry.Texture;
                if (texture == null) continue;

                // Read-only textures are expected to be imported or created externally.
                // Create a placeholder — the pipeline will replace this with the actual handle.
                var actualDesc = texture.desc;
                texture.innerHandle = renderGraph.CreateTexture(actualDesc);
                builder.UseTexture(texture.innerHandle, entry.Access);
            }

            foreach (var entry in resource.Buffers)
            {
                var buffer = entry.Buffer;
                if (buffer == null) continue;
                buffer.innerHandle = renderGraph.CreateBuffer(buffer.desc);
                builder.UseBuffer(buffer.innerHandle, entry.Access);
            }
            builder.AllowPassCulling(false);
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
                var texture = entry.Texture;
                if (texture == null) continue;

                texture.innerHandle = renderGraph.CreateTexture(texture.desc);

                if (entry.IsDepthAttachment)
                {
                    builder.SetRenderAttachmentDepth(texture.innerHandle, entry.Access);
                }
                else if (entry.AttachmentIndex >= 0)
                {
                    builder.SetRenderAttachment(texture.innerHandle, entry.AttachmentIndex, entry.Access);
                }
                else
                {
                    builder.UseTexture(texture.innerHandle, entry.Access);
                }
            }

            foreach (var entry in resource.Buffers)
            {
                var buffer = entry.Buffer;
                if (buffer == null) continue;

                buffer.innerHandle = renderGraph.CreateBuffer(buffer.desc);
                builder.UseBuffer(buffer.innerHandle, entry.Access);
            }
            builder.AllowPassCulling(false);

        }
    }
}