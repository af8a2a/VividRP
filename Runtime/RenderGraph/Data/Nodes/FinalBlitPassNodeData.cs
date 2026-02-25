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
    [RenderPass("Final Blit", PassType.Raster)]
    public class FinalBlitPassNodeData : RenderPassNodeData
    {
        public override PassType Type => PassType.Raster;

        private static Material s_BlitMaterial;
        private static readonly int s_BlitTextureId = Shader.PropertyToID("_BlitTexture");

        public FinalBlitPassNodeData()
        {
            NodeName = "Final Blit";
            AddPort("Input Texture", PortType.Texture, true, AccessFlags.Read);
        }

        private class PassData
        {
            public Material Material;
            public TextureHandle Source;
        }

        private static Material GetBlitMaterial()
        {
            if (s_BlitMaterial == null)
            {
                var shader = Shader.Find("Hidden/VividRP/Blit");
                if (shader == null)
                {
                    Debug.LogError("[VividRP] Could not find shader Hidden/VividRP/Blit");
                    return null;
                }
                s_BlitMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return s_BlitMaterial;
        }

        public override void Record(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            PassExecutionContext context)
        {
            var importInfo = new RenderTargetInfo
            {
                width = Screen.width,
                height = Screen.height,
                volumeDepth = 1,
                msaaSamples = 1,
                format = GraphicsFormat.R8G8B8A8_SRGB
            };

            var backBuffer = renderGraph.ImportBackbuffer(BuiltinRenderTextureType.CameraTarget, importInfo);

            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                NodeName, out var passData);

            passData.Material = GetBlitMaterial();
            builder.SetRenderAttachment(backBuffer, 0);

            foreach (var port in Ports)
            {
                if (!port.IsInput) continue;

                var slot = context.ResolveInput(port);
                if (!slot.IsValid) continue;

                if (slot.Type == ResourceType.Texture)
                {
                    passData.Source = slot.TextureHandle;
                    builder.UseTexture(slot.TextureHandle, port.Access);
                }
            }

            builder.SetRenderFunc<PassData>((data, rasterGraphContext) =>
            {
                if (data.Material != null)
                {
                    data.Material.SetTexture(s_BlitTextureId, data.Source);
                    rasterGraphContext.cmd.DrawProcedural(
                        Matrix4x4.identity, data.Material, 0,
                        MeshTopology.Triangles, 3);
                }
            });
        }
    }
}
