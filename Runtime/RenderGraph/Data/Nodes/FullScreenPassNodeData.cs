using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Passes;
using VividRP.Runtime.RenderGraph.Resource;
using VividRP.Runtime.Utility;

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
            AddPort("Output Texture", PortType.Texture, false, AccessFlags.Write);
        }

        private class PassData
        {
            public Material Material;
        }

        private static Material GetFullScreenMaterial()
        {
            if (s_FullScreenMaterial == null)
            {
                var shader = VividResources.FullScreenUVShader;
                if (shader == null)
                {
                    Debug.LogError("[VividRP] Could not find shader Hidden/VividRP/FullScreenUV in VividResources.");
                    return null;
                }
                s_FullScreenMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return s_FullScreenMaterial;
        }
        
        
        internal static Vector4 GetFinalBlitScaleBias(in RasterGraphContext renderGraphContext, in TextureHandle source, in TextureHandle destination)
        {
            RTHandle srcRTHandle = source;
            Vector2 scale = srcRTHandle is { useScaling: true } ? new Vector2(srcRTHandle.rtHandleProperties.rtHandleScale.x, srcRTHandle.rtHandleProperties.rtHandleScale.y) : Vector2.one;
            var yflip = renderGraphContext.GetTextureUVOrigin(in source) != renderGraphContext.GetTextureUVOrigin(in destination);
            Vector4 scaleBias = yflip ? new Vector4(scale.x, -scale.y, 0, scale.y) : new Vector4(scale.x, scale.y, 0, 0);

            return scaleBias;
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
