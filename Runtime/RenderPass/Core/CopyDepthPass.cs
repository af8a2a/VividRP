using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class CopyDepthPass : RasterPass
    {
        private const string CopyDepthShaderName = "Hidden/VividRP/CopyDepth";

        [RenderGraphResource(Name = "DepthAttachment", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthAttachment;

        [RenderGraphResource(Name = "DepthTexture", Access = AccessFlags.Write, AttachmentIndex = 0)]
        private RenderGraphTexture m_DepthTexture;

        private Material m_Material;

        public CopyDepthPass()
        {
            m_DepthAttachment = RenderGraphTexture.CreateInput("DepthAttachment", GraphicsFormat.None, DepthBits.Depth32);
            m_DepthTexture = RenderGraphTexture.CreateColorTarget("DepthTexture", GraphicsFormat.R32_SFloat);
            m_DepthTexture.desc.ClearBuffer = false;
            m_DepthTexture.desc.FilterMode = FilterMode.Point;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources.CopyDepthShader;
            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{CopyDepthShaderName}' for {nameof(CopyDepthPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var sourceDescriptor = m_DepthAttachment?.desc;
            var hasExplicitSourceSize = HasExplicitSize(sourceDescriptor);
            var width = hasExplicitSourceSize
                ? Mathf.Max(1, sourceDescriptor.Width)
                : CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            var height = hasExplicitSourceSize
                ? Mathf.Max(1, sourceDescriptor.Height)
                : CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);

            if (sourceDescriptor != null && !hasExplicitSourceSize)
            {
                sourceDescriptor.Width = width;
                sourceDescriptor.Height = height;
            }

            ConfigureDepthTextureOutput(width, height);
        }

        public override void Record(RasterPassContext context)
        {
            if (m_Material == null || !m_DepthAttachment.innerHandle.IsValid() || !m_DepthTexture.innerHandle.IsValid())
                return;

            RTHandle sourceHandle = m_DepthAttachment.innerHandle;


            Blitter.BlitTexture(context.cmd, sourceHandle, Vector2.one, m_Material, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
        }

        private void ConfigureDepthTextureOutput(int width, int height)
        {
            if (m_DepthTexture?.desc == null)
                return;

            m_DepthTexture.desc.Width = width;
            m_DepthTexture.desc.Height = height;
            m_DepthTexture.desc.ColorFormat = GraphicsFormat.R32_SFloat;
            m_DepthTexture.desc.DepthBufferBits = DepthBits.None;
            m_DepthTexture.desc.MsaaSamples = MSAASamples.None;
            m_DepthTexture.desc.FilterMode = FilterMode.Point;
            m_DepthTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_DepthTexture.desc.ClearBuffer = false;
            m_DepthTexture.desc.UseMipMap = false;
            m_DepthTexture.desc.AutoGenerateMips = false;
            m_DepthTexture.desc.MipCount = 1;
            m_DepthTexture.desc.EnableRandomWrite = false;
            m_DepthTexture.desc.BindTextureMS = false;
            m_DepthTexture.desc.Name = "DepthTexture";

            if (m_DepthAttachment?.desc == null)
                return;

            m_DepthTexture.desc.Dimension = m_DepthAttachment.desc.Dimension;
            m_DepthTexture.desc.Slices = Mathf.Max(1, m_DepthAttachment.desc.Slices);
            m_DepthTexture.desc.UseDynamicScale = m_DepthAttachment.desc.UseDynamicScale;
            m_DepthTexture.desc.UseDynamicScaleExplicit = m_DepthAttachment.desc.UseDynamicScaleExplicit;
            m_DepthTexture.desc.ScaleFactor = m_DepthAttachment.desc.ScaleFactor;
        }

        private static bool HasExplicitSize(RenderGraphTextureDesc descriptor)
        {
            return descriptor != null
                && descriptor.Width > 0
                && descriptor.Height > 0
                && !(descriptor.Width == 1 && descriptor.Height == 1);
        }

    }
}
