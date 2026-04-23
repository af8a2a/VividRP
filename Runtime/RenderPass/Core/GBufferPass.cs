using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class GBufferPass : RasterPass
    {
        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(
            Name = "GBuffer0",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(
            Name = "GBuffer1",
            Access = AccessFlags.Write,
            AttachmentIndex = 1,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(
            Name = "GBuffer2",
            Access = AccessFlags.Write,
            AttachmentIndex = 2,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer2;

        [RenderGraphResource(
            Name = "GBuffer3",
            Access = AccessFlags.Write,
            AttachmentIndex = 3,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer3;

        [RenderGraphResource(
            Name = "GBuffer4",
            Access = AccessFlags.Write,
            AttachmentIndex = 4,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer4;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_GBufferDepth;

        public GBufferPass()
        {
            m_RenderList = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateOpaque("VividGBuffer")
            };
            m_RenderList.desc.RendererConfiguration = PerObjectData.Lightmaps;

            m_GBuffer0 = RenderGraphTexture.CreateColorTarget("GBuffer0", GraphicsFormat.R8G8B8A8_SRGB);
            m_GBuffer1 = RenderGraphTexture.CreateColorTarget("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_GBuffer2 = RenderGraphTexture.CreateColorTarget("GBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer3 = RenderGraphTexture.CreateColorTarget("GBuffer3", GraphicsFormat.B10G11R11_UFloatPack32);
            m_GBuffer3.desc.EnableRandomWrite = true;
            m_GBuffer4 = RenderGraphTexture.CreateColorTarget("GBuffer4", GraphicsFormat.R16G16B16A16_SFloat);
            m_GBufferDepth = RenderGraphTexture.CreateDepthTarget("GBufferDepth");
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0)
                width = Mathf.Max(1, Screen.width);

            if (height <= 0)
                height = Mathf.Max(1, Screen.height);

            m_GBuffer0.Resize(width, height);
            m_GBuffer1.Resize(width, height);
            m_GBuffer2.Resize(width, height);
            m_GBuffer3.Resize(width, height);
            m_GBuffer4.Resize(width, height);
            m_GBufferDepth.Resize(width, height);
        }

        public override void Record(RasterPassContext context)
        {
            if (m_RenderList == null || !m_RenderList.IsValid)
                return;

            context.cmd.DrawRendererList(m_RenderList);
        }

        public override void Dispose()
        {
        }
    }
}
