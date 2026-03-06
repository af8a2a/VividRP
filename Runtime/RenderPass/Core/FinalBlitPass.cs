using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class FinalBlitPass : UnsafePass
    {
        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source = new();

        private Material m_Material;
        private RenderTargetIdentifier m_CameraBackBufferTarget;
        private TextureUVOrigin m_CameraBackBufferTextureUVOrigin;
        private Rect m_Viewport;

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData.camera;
            var hasTargetTexture = camera != null && camera.targetTexture != null;
            var cameraType = camera != null ? camera.cameraType : CameraType.Game;

            m_CameraBackBufferTarget = hasTargetTexture
                ? new RenderTargetIdentifier(camera.targetTexture)
                : BuiltinRenderTextureType.CameraTarget;
            m_CameraBackBufferTextureUVOrigin = GetCameraBackBufferTextureUVOrigin(cameraType, hasTargetTexture);

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
            RTHandle sourceHandle = source.innerHandle;
            var scale = Vector2.one;

            if (sourceHandle != null && sourceHandle.useScaling)
            {
                scale.x = sourceHandle.rtHandleProperties.rtHandleScale.x;
                scale.y = sourceHandle.rtHandleProperties.rtHandleScale.y;
            }

            var sourceTextureUVOrigin = context.GetTextureUVOrigin(source.innerHandle);
            var scaleBias = GetFinalBlitScaleBias(scale, sourceTextureUVOrigin, m_CameraBackBufferTextureUVOrigin);

            cmd.SetRenderTarget(m_CameraBackBufferTarget);
            cmd.SetViewport(m_Viewport);

            Blitter.BlitTexture(unsafeCmd, sourceHandle, scaleBias, m_Material, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
        }

        private static TextureUVOrigin GetCameraBackBufferTextureUVOrigin(CameraType cameraType, bool hasTargetTexture)
        {
            var useActualBackbufferOrientation = cameraType != CameraType.SceneView
                && cameraType != CameraType.Preview
                && !hasTargetTexture;

            if (!useActualBackbufferOrientation)
                return TextureUVOrigin.BottomLeft;

            return SystemInfo.graphicsUVStartsAtTop ? TextureUVOrigin.TopLeft : TextureUVOrigin.BottomLeft;
        }

        private static Vector4 GetFinalBlitScaleBias(
            Vector2 scale,
            TextureUVOrigin sourceTextureUVOrigin,
            TextureUVOrigin destinationTextureUVOrigin)
        {
            var yFlip = sourceTextureUVOrigin != destinationTextureUVOrigin;
            return yFlip
                ? new Vector4(scale.x, -scale.y, 0f, scale.y)
                : new Vector4(scale.x, scale.y, 0f, 0f);
        }
    }
}
