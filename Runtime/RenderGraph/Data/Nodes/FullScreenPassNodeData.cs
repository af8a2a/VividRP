using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
            AddPort("Output Texture", PortType.Texture, false, AccessFlags.ReadWrite);
        }

        private class PassData
        {
            public TextureHandle Output;
        }

        public override void Record(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            PassExecutionContext context)
        {
            var desc = new TextureDesc(Screen.width, Screen.height)
            {
                colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                clearBuffer = true,
                clearColor = Color.clear,
                name = "FullScreenPassOutput"
            };
            var outputTex = renderGraph.CreateTexture(desc);

            // Store the created texture in the context so the executor can propagate it
            context.StoreOutput(Ports[0].Id, ResourceSlot.FromTexture(outputTex));

            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                NodeName, out var passData);

            passData.Output = outputTex;
            builder.SetRenderAttachment(outputTex, 0);

            builder.SetRenderFunc<PassData>((data, rasterGraphContext) =>
            {
                // Draw a full-screen triangle outputting UV coordinates
                Blitter.BlitTexture(rasterGraphContext.cmd,
                    new Vector4(1f, 1f, 0f, 0f), 0f, false);
            });
        }
    }
}
