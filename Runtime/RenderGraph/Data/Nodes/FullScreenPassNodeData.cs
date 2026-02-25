using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Passes;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [RenderPass("Full Screen Pass", PassType.Raster)]
    public class FullScreenPassNodeData : RenderPassNodeData
    {
        public override PassType Type => PassType.Raster;

        public FullScreenPassNodeData()
        {
            NodeName = "Full Screen Pass";
            AddPort("Input Texture", PortType.Texture, true, AccessFlags.Read);
            AddPort("Output Texture", PortType.Texture, false, AccessFlags.ReadWrite);
        }

        private class PassData { }

        public override void Record(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            PassExecutionContext context)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                NodeName, out _);

            foreach (var port in Ports)
            {
                if (!port.IsInput) continue;

                var slot = context.ResolveInput(port);
                if (!slot.IsValid) continue;

                builder.UseTexture(slot.TextureHandle, port.Access);
            }

            builder.SetRenderFunc<PassData>((_, _) => { });
        }
    }
}
