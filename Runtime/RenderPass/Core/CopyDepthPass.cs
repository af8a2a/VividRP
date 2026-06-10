using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class CopyDepthPass : ComputePass
    {
        private const int ThreadGroupSize = 8;

        private static readonly int InputDepthId = Shader.PropertyToID("_InputDepth");
        private static readonly int DepthMipChainId = Shader.PropertyToID("_DepthMipChain");
        private static readonly int DstOffsetAndSizeId = Shader.PropertyToID("_DstOffsetAndSize");

        [RenderGraphResource(Name = "DepthAttachment", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthAttachment;

        [RenderGraphResource(Name = "DepthTexture", Access = AccessFlags.Write)]
        private RenderGraphTexture m_DepthTexture;

        private readonly int[] m_DstOffsetAndSize = new int[4];

        private ComputeShader m_ComputeShader;
        private int m_CopyDepthKernel = -1;
        private int m_Width = 1;
        private int m_Height = 1;

        public CopyDepthPass()
        {
            m_DepthAttachment = RenderGraphTexture.CreateInput("DepthAttachment", GraphicsFormat.None, DepthBits.Depth32);
            m_DepthTexture = RenderGraphTexture.CreateColorTarget("DepthTexture", GraphicsFormat.R32_SFloat);
            m_DepthTexture.desc.ClearBuffer = false;
            m_DepthTexture.desc.FilterMode = FilterMode.Point;
            m_DepthTexture.desc.EnableRandomWrite = true;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ComputeShader = resources?.HDRPHZBCompute;
            if (m_ComputeShader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find HDRP HZB compute shader for {nameof(CopyDepthPass)}.");
                return;
            }

            try
            {
                m_CopyDepthKernel = m_ComputeShader.FindKernel("KCopyDepthToAtlas");
            }
            catch (System.ArgumentException)
            {
                Debug.LogWarning($"[VividRP] HDRPHZB.compute is missing KCopyDepthToAtlas. {nameof(CopyDepthPass)} will be skipped.");
                m_ComputeShader = null;
                m_CopyDepthKernel = -1;
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var sourceDescriptor = m_DepthAttachment?.desc;
            var hasExplicitSourceSize = RenderGraphTextureDescUtility.HasExplicitSize(sourceDescriptor);
            var width = hasExplicitSourceSize
                ? Mathf.Max(1, sourceDescriptor.Width)
                : CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            var height = hasExplicitSourceSize
                ? Mathf.Max(1, sourceDescriptor.Height)
                : CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);
            m_Width = width;
            m_Height = height;

            if (sourceDescriptor != null && !hasExplicitSourceSize)
            {
                sourceDescriptor.Width = width;
                sourceDescriptor.Height = height;
            }

            ConfigureDepthTextureOutput(width, height);
        }

        public override void Record(ComputePassContext context)
        {
            if (m_ComputeShader == null
                || m_CopyDepthKernel < 0
                || !m_DepthAttachment.innerHandle.IsValid()
                || !m_DepthTexture.innerHandle.IsValid())
                return;

            var cmd = context.cmd;
            m_DstOffsetAndSize[0] = 0;
            m_DstOffsetAndSize[1] = 0;
            m_DstOffsetAndSize[2] = m_Width;
            m_DstOffsetAndSize[3] = m_Height;

            cmd.SetComputeIntParams(m_ComputeShader, DstOffsetAndSizeId, m_DstOffsetAndSize);
            cmd.SetComputeTextureParam(m_ComputeShader, m_CopyDepthKernel, InputDepthId, m_DepthAttachment.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_CopyDepthKernel, DepthMipChainId, m_DepthTexture.innerHandle);
            cmd.DispatchCompute(
                m_ComputeShader,
                m_CopyDepthKernel,
                CoreUtils.DivRoundUp(m_Width, ThreadGroupSize),
                CoreUtils.DivRoundUp(m_Height, ThreadGroupSize),
                1);
        }

        public override void Dispose()
        {
            m_ComputeShader = null;
            m_CopyDepthKernel = -1;
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
            m_DepthTexture.desc.EnableRandomWrite = true;
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

    }
}
