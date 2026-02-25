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

        private static Material s_FullScreenMaterial;

        public FullScreenPassNodeData()
        {
            NodeName = "Full Screen Pass";
            AddPort("Output Texture", PortType.Texture, false, AccessFlags.ReadWrite);
        }

        private class PassData
        {
            public Material Material;
        }

        private static Material GetFullScreenMaterial()
        {
            if (s_FullScreenMaterial == null)
            {
                var shader = Shader.Find("Hidden/VividRP/FullScreenUV");
                if (shader == null)
                {
                    Debug.LogError("[VividRP] Could not find shader Hidden/VividRP/FullScreenUV");
                    return null;
                }
                s_FullScreenMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return s_FullScreenMaterial;
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

            context.StoreOutput(Ports[0].Id, ResourceSlot.FromTexture(outputTex));

            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                NodeName, out var passData);

            passData.Material = GetFullScreenMaterial();
            builder.SetRenderAttachment(outputTex, 0);

            builder.SetRenderFunc<PassData>((data, rasterGraphContext) =>
            {
                if (data.Material != null)
                    rasterGraphContext.cmd.DrawProcedural(
                        Matrix4x4.identity, data.Material, 0,
                        MeshTopology.Triangles, 3);
            });
        }
    }
}
