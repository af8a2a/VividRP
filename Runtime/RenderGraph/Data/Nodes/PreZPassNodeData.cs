using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Passes;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [RenderPass("PreZ Pass", PassType.Raster)]
    public class PreZPassNodeData : RenderPassNodeData
    {
        public override PassType Type => PassType.Raster;

        public LayerMask RenderingLayerMask = -1;

        private static readonly ShaderTagId s_DepthOnly = new("DepthOnly");
        private static readonly ShaderTagId s_SRPDefaultUnlit = new("SRPDefaultUnlit");

        public PreZPassNodeData()
        {
            NodeName = "PreZ Pass";
            AddPort("Depth In", PortType.Texture, true, AccessFlags.Write);
            AddPort("Depth Out", PortType.Texture, false, AccessFlags.ReadWrite);
        }

        private class PassData
        {
            public RendererListHandle RendererList;
        }

        public override void Record(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            PassExecutionContext context)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                NodeName, out var passData);

            // Resolve depth input and bind as depth attachment
            foreach (var port in Ports)
            {
                if (!port.IsInput || port.Type != PortType.Texture) continue;

                var slot = context.ResolveInput(port);
                if (!slot.IsValid) continue;

                builder.SetRenderAttachmentDepth(slot.TextureHandle);
            }

            // Build RendererList for opaque depth-only geometry
            var sortingSettings = new SortingSettings(context.Camera)
            {
                criteria = SortingCriteria.CommonOpaque
            };

            var drawingSettings = new DrawingSettings(s_DepthOnly, sortingSettings);
            drawingSettings.SetShaderPassName(1, s_SRPDefaultUnlit);

            var filteringSettings = new FilteringSettings(
                RenderQueueRange.opaque,
                RenderingLayerMask);

            var rendererListParams = new RendererListParams(
                context.CullingResults, drawingSettings, filteringSettings);

            passData.RendererList = renderGraph.CreateRendererList(rendererListParams);
            builder.UseRendererList(passData.RendererList);

            builder.SetRenderFunc<PassData>((data, rasterContext) =>
            {
                rasterContext.cmd.DrawRendererList(data.RendererList);
            });
        }
    }
}
