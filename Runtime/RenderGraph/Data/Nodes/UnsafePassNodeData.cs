using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Passes;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [RenderPass("Unsafe Pass", PassType.Unsafe)]
    public class UnsafePassNodeData : RenderPassNodeData
    {
        public override PassType Type => PassType.Unsafe;

        public UnsafePassNodeData()
        {
            NodeName = "Unsafe Pass";
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
            using var builder = renderGraph.AddUnsafePass<PassData>(
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
