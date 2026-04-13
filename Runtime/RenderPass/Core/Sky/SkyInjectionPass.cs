using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class SkyInjectionPass : UnsafePass
    {
        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTarget;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "SkyViewLUT", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SkyViewLUT;

        [RenderGraphResource(Name = "DirectionalShadowTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DirectionalShadowTexture;

        private bool m_ShouldInject;

        public SkyInjectionPass()
        {
            profilingSampler = new ProfilingSampler(nameof(SkyInjectionPass));

            m_ColorTarget = RenderGraphTexture.CreateInput("SkyColor", GraphicsFormat.R8G8B8A8_SRGB);
            m_DepthTexture = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_SkyViewLUT = RenderGraphTexture.CreateInput("SkyViewLUT", GraphicsFormat.R16G16B16A16_SFloat);
            m_DirectionalShadowTexture = RenderGraphTexture.CreateInput("DirectionalShadowTexture", GraphicsFormat.R16_SFloat);
        }

        public override void Create()
        {
            SkyManager.Initialize();
        }

        public override void Dispose()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData?.GetOrCreate<VividCameraData>();
            ConfigureRenderTargets(cameraData);

            m_ShouldInject = SkyManager.PrepareSkyInjection(
                frameData,
                m_ColorTarget,
                m_DepthTexture,
                m_SkyViewLUT,
                m_DirectionalShadowTexture);
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (!m_ShouldInject)
                return;

            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(cmd, profilingSampler))
            {
                SkyManager.RenderSkyInjection(cmd);
            }
        }

        private void ConfigureRenderTargets(VividCameraData cameraData)
        {
            var width = cameraData?.actualWidth > 0 ? cameraData.actualWidth : cameraData?.pixelWidth ?? 0;
            var height = cameraData?.actualHeight > 0 ? cameraData.actualHeight : cameraData?.pixelHeight ?? 0;

            if (width <= 0)
                width = Mathf.Max(1, Screen.width);

            if (height <= 0)
                height = Mathf.Max(1, Screen.height);

            m_ColorTarget.Resize(width, height);
            m_DepthTexture.Resize(width, height);
        }
    }
}
