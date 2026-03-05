using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class FullScreenPass : RasterPass
    {
        Material material;

        [RenderGraphResource(Access = AccessFlags.Write,AttachmentIndex = 0)]
        RenderGraphTexture texture = new RenderGraphTexture();

        public override void Record(RasterGraphContext context)
        {
            var cmd = context.cmd;
            Blitter.BlitTexture(cmd, Vector2.one, material, 0);
        }

        
        public override void Prepare(ContextContainer frameData)
        {
            var desc = texture.desc;
            var cameraData = frameData.Get<VividCameraData>();
            desc.Width = cameraData.actualWidth;
            desc.Height = cameraData.actualHeight;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();

            material = CoreUtils.CreateEngineMaterial(resources.FullScreenUVShader);
        }
    }
}