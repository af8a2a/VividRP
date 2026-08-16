using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Experimental.Material
{
    public sealed class ExperimentalClosureBufferPass : UnsafePass
    {
        internal const string ShaderTagName = "ExperimentalClosureBuffer";

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer0",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer0;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer1",
            Access = AccessFlags.Write,
            AttachmentIndex = 1,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer1;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer2",
            Access = AccessFlags.Write,
            AttachmentIndex = 2,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer2;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer3",
            Access = AccessFlags.Write,
            AttachmentIndex = 3,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer3;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer4",
            Access = AccessFlags.Write,
            AttachmentIndex = 4,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer4;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer5",
            Access = AccessFlags.Write,
            AttachmentIndex = 5,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer5;

        [RenderGraphResource(
            Name = "Depth",
            Access = AccessFlags.Read,
            IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthTexture;

        private readonly RenderTargetIdentifier[] m_ColorTargets =
            new RenderTargetIdentifier[6];

        public ExperimentalClosureBufferPass()
        {
            m_RenderList = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateOpaque(ShaderTagName)
            };
            m_RenderList.desc.RendererConfiguration = PerObjectData.Lightmaps;

            m_ClosureBuffer0 = RenderGraphTexture.CreateColorTarget(
                "ExperimentalClosureBuffer0",
                GraphicsFormat.R8G8B8A8_SRGB);
            m_ClosureBuffer1 = RenderGraphTexture.CreateColorTarget(
                "ExperimentalClosureBuffer1",
                GraphicsFormat.A2B10G10R10_UNormPack32);
            m_ClosureBuffer2 = RenderGraphTexture.CreateColorTarget(
                "ExperimentalClosureBuffer2",
                GraphicsFormat.R8G8B8A8_UNorm);
            m_ClosureBuffer3 = RenderGraphTexture.CreateColorTarget(
                "ExperimentalClosureBuffer3",
                GraphicsFormat.R8G8B8A8_UNorm);
            m_ClosureBuffer4 = RenderGraphTexture.CreateColorTarget(
                "ExperimentalClosureBuffer4",
                GraphicsFormat.B10G11R11_UFloatPack32);
            m_ClosureBuffer5 = RenderGraphTexture.CreateColorTarget(
                "ExperimentalClosureBuffer5",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_DepthTexture = RenderGraphTexture.CreateInput(
                "Depth",
                GraphicsFormat.None,
                DepthBits.Depth32);
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var width = cameraData.actualWidth > 0
                ? cameraData.actualWidth
                : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0
                ? cameraData.actualHeight
                : cameraData.pixelHeight;

            if (width <= 0)
                width = Mathf.Max(1, Screen.width);
            if (height <= 0)
                height = Mathf.Max(1, Screen.height);

            m_ClosureBuffer0.Resize(width, height);
            m_ClosureBuffer1.Resize(width, height);
            m_ClosureBuffer2.Resize(width, height);
            m_ClosureBuffer3.Resize(width, height);
            m_ClosureBuffer4.Resize(width, height);
            m_ClosureBuffer5.Resize(width, height);
            m_DepthTexture.Resize(width, height);
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_RenderList == null || !m_RenderList.IsValid)
                return;

            var cmd = context.GetNativeCommandBuffer();
            m_ColorTargets[0] = m_ClosureBuffer0;
            m_ColorTargets[1] = m_ClosureBuffer1;
            m_ColorTargets[2] = m_ClosureBuffer2;
            m_ColorTargets[3] = m_ClosureBuffer3;
            m_ColorTargets[4] = m_ClosureBuffer4;
            m_ColorTargets[5] = m_ClosureBuffer5;
            cmd.SetRenderTarget(m_ColorTargets, m_DepthTexture);
            cmd.ClearRenderTarget(
                clearDepth: false,
                clearColor: true,
                Color.clear);
            cmd.DrawRendererList(m_RenderList);
        }

        public override void Dispose()
        {
        }
    }
}
