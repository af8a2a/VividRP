using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Passes;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [RenderPass("Final Blit", PassType.Raster)]
    public class FinalBlitPassNodeData : RenderPassNodeData
    {
        public override PassType Type => PassType.Raster;

        public FinalBlitPassNodeData()
        {
            NodeName = "Final Blit";
            AddPort("Input Texture", PortType.Texture, true, AccessFlags.Read);
        }

        private class PassData { }

        public override void Record(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            PassExecutionContext context)
        {
            var backBuffer = renderGraph.ImportBackbuffer(BuiltinRenderTextureType.CameraTarget);

            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                NodeName, out _);

            builder.SetRenderAttachment(backBuffer, 0);

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
