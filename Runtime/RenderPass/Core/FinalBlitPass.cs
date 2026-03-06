using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class FinalBlitPass : UnsafePass
    {
        private static readonly Vector4 s_DefaultScaleBias = new(1f, 1f, 0f, 0f);

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source = new();

        Material m_Material;
        private RenderTargetIdentifier m_CameraBackBufferTarget;
        private Rect m_Viewport;

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData.camera;

            m_CameraBackBufferTarget = camera != null && camera.targetTexture != null
                ? new RenderTargetIdentifier(camera.targetTexture)
                : BuiltinRenderTextureType.CameraTarget;

            var width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0 || height <= 0)
            {
                width = Screen.width;
                height = Screen.height;
            }

            m_Viewport = new Rect(0f, 0f, width, height);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();

            m_Material = CoreUtils.CreateEngineMaterial(resources.BlitShader);
        }
        

        public override void Record(UnsafeGraphContext context)
        {
            var cmd = context.cmd;
            var unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);

            cmd.SetRenderTarget(m_CameraBackBufferTarget);
            cmd.SetViewport(m_Viewport);

            Blitter.BlitTexture(unsafeCmd, source.innerHandle,Vector2.one, m_Material,0);
        }

        public override void Dispose()
        {
        }
    }
}
