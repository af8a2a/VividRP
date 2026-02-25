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
            public TextureHandle Destination;
            public bool IsSceneViewCamera;
            public Rect PixelRect;
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
            var camera = context.Camera;
            bool isSceneView = camera.cameraType == CameraType.SceneView;

            // Scene view renders to a target texture whose size may differ from camera.pixelWidth/Height.
            // Use the actual target texture dimensions to avoid attachment size mismatch.
            int width, height;
            if (camera.targetTexture != null)
            {
                width = camera.targetTexture.width;
                height = camera.targetTexture.height;
            }
            else
            {
                width = camera.pixelWidth;
                height = camera.pixelHeight;
            }

            var importInfo = new RenderTargetInfo
            {
                width = width,
                height = height,
                volumeDepth = 1,
                msaaSamples = 1,
                format = GraphicsFormat.R8G8B8A8_SRGB
            };

            var backBuffer = renderGraph.ImportBackbuffer(BuiltinRenderTextureType.CameraTarget, importInfo);

            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                NodeName, out var passData);

            passData.Material = GetBlitMaterial();
            passData.Destination = backBuffer;
            passData.IsSceneViewCamera = isSceneView;
            passData.PixelRect = camera.pixelRect;

            // Scene view: use Write so depth/stencil is preserved for gizmos
            // Game view: use WriteAll since we're doing a full-screen blit
            builder.SetRenderAttachment(backBuffer, 0,
                isSceneView ? AccessFlags.Write : AccessFlags.WriteAll);

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

            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc<PassData>((data, rasterGraphContext) =>
            {
                if (data.Material == null) return;

                var cmd = rasterGraphContext.cmd;

                // Disable wireframe so the blit quad doesn't render as wireframe in scene view
                cmd.SetWireframe(false);

                // Game view: set viewport to camera pixel rect
                // Scene view: skip viewport setup (scene view manages its own viewport)
                if (!data.IsSceneViewCamera)
                    cmd.SetViewport(data.PixelRect);

                data.Material.SetTexture(s_BlitTextureId, data.Source);
                cmd.DrawProcedural(
                    Matrix4x4.identity, data.Material, 0,
                    MeshTopology.Triangles, 3);
            });
        }
    }
}
