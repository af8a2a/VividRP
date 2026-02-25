using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Passes;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [RenderPass("Raster Pass", PassType.Raster)]
    public class RasterPassNodeData : RenderPassNodeData
    {
        public int ColorAttachmentCount = 1;
        public bool HasDepth = true;
        public AccessFlags DefaultAccess = AccessFlags.ReadWrite;

        public override PassType Type => PassType.Raster;

        public RasterPassNodeData()
        {
            NodeName = "Raster Pass";
            AddPort("Color In", PortType.Texture, true, AccessFlags.Read);
            AddPort("Depth In", PortType.Texture, true, AccessFlags.Read);
            AddPort("Color Out", PortType.Texture, false, AccessFlags.ReadWrite);
            AddPort("Depth Out", PortType.Texture, false, AccessFlags.ReadWrite);
        }

        private class PassData { }

        public override void Record(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            PassExecutionContext context)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                NodeName, out _);

            int colorIndex = 0;
            foreach (var port in Ports)
            {
                if (!port.IsInput || port.Type != PortType.Texture) continue;

                var slot = context.ResolveInput(port);
                if (!slot.IsValid) continue;

                if (port.DisplayName.Contains("Depth"))
                    builder.SetRenderAttachmentDepth(slot.TextureHandle);
                else
                    builder.SetRenderAttachment(slot.TextureHandle, colorIndex++);
            }

            builder.SetRenderFunc<PassData>((_, _) => { });
        }
    }
}
