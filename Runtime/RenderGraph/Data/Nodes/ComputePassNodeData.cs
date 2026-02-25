using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Passes;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [RenderPass("Compute Pass", PassType.Compute)]
    public class ComputePassNodeData : RenderPassNodeData
    {
        public bool AsyncCapable;

        public override PassType Type => PassType.Compute;

        public ComputePassNodeData()
        {
            NodeName = "Compute Pass";
            AddPort("Input Texture", PortType.Texture, true, AccessFlags.Read);
            AddPort("Input Buffer", PortType.Buffer, true, AccessFlags.Read);
            AddPort("Output Texture", PortType.Texture, false, AccessFlags.ReadWrite);
            AddPort("Output Buffer", PortType.Buffer, false, AccessFlags.ReadWrite);
        }

        private class PassData { }

        public override void Record(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            PassExecutionContext context)
        {
            using var builder = renderGraph.AddComputePass<PassData>(
                NodeName, out _);

            foreach (var port in Ports)
            {
                if (!port.IsInput) continue;

                var slot = context.ResolveInput(port);
                if (!slot.IsValid) continue;

                if (slot.Type == ResourceType.Texture)
                    builder.UseTexture(slot.TextureHandle, port.Access);
                else if (slot.Type == ResourceType.Buffer)
                    builder.UseBuffer(slot.BufferHandle, port.Access);
            }

            builder.SetRenderFunc<PassData>((_, _) => { });
        }
    }
}
