using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Standard RenderGraph pass that rasterizes refractive transparent backfaces
    /// (LightMode = "VividTransparentBackface") into an RGBA16F texture packing
    /// (octNormalWS.xy, linearViewZ, validFlag) using a transient Depth32 buffer.
    /// Gives ForwardRefractionPass exact exit thickness and exit normals for double-sided
    /// Snell refraction and Beer-Lambert volume absorption on arbitrary meshes.
    /// </summary>
    public sealed class TransparentBackfacePass : UnsafePass, IAllowGlobalStateModificationPass
    {
        public const string BackfaceShaderTagName = "VividTransparentBackface";

        [RenderGraphResource(
            Name = "RenderList",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(
            Name = "BackfaceData",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_BackfaceData;

        [RenderGraphResource(Name = "BackfaceDepth", Access = AccessFlags.Write, IsDepthAttachment = true)]
        [TransientResource]
        private RenderGraphTexture m_BackfaceDepth;

        public TransparentBackfacePass()
        {
            profilingSampler = new ProfilingSampler(nameof(TransparentBackfacePass));
            m_RenderList = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateTransparent(BackfaceShaderTagName)
            };

            m_BackfaceData = RenderGraphTexture.CreateColorTarget("BackfaceData", GraphicsFormat.R16G16B16A16_SFloat);
            m_BackfaceData.desc.ClearBuffer = true;
            m_BackfaceData.desc.ClearColor = Color.clear;
            m_BackfaceData.desc.FilterMode = FilterMode.Point;
            m_BackfaceData.desc.WrapMode = TextureWrapMode.Clamp;

            m_BackfaceDepth = RenderGraphTexture.CreateDepthTarget("BackfaceDepth", DepthBits.Depth32);
            m_BackfaceDepth.desc.ClearBuffer = true;
        }

        public override void Create()
        {
        }

        public override void Dispose()
        {
        }

        public override bool IsActive(ContextContainer frameData)
        {
            return ForwardRefractionPass.EnableRefraction && ForwardRefractionPass.EnableBackfaceRefraction;
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            int width = CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualWidth ?? 0, cameraData?.pixelWidth ?? 0, Screen.width);
            int height = CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualHeight ?? 0, cameraData?.pixelHeight ?? 0, Screen.height);

            m_BackfaceData.Resize(width, height);
            m_BackfaceData.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            m_BackfaceData.desc.ClearBuffer = true;
            m_BackfaceData.desc.ClearColor = Color.clear;
            m_BackfaceData.desc.FilterMode = FilterMode.Point;
            m_BackfaceData.desc.WrapMode = TextureWrapMode.Clamp;

            m_BackfaceDepth.Resize(width, height);
            m_BackfaceDepth.desc.ClearBuffer = true;
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_BackfaceData == null
                || !m_BackfaceData.innerHandle.IsValid()
                || m_BackfaceDepth == null
                || !m_BackfaceDepth.innerHandle.IsValid())
            {
                return;
            }

            var nativeCmd = context.GetNativeCommandBuffer();
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                CoreUtils.SetRenderTarget(
                    nativeCmd,
                    m_BackfaceData.innerHandle,
                    m_BackfaceDepth.innerHandle,
                    ClearFlag.All,
                    Color.clear);

                if (m_RenderList != null && m_RenderList.IsValid)
                    nativeCmd.DrawRendererList(m_RenderList);
            }
        }
    }
}
