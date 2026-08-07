using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VirtualTextureFeedbackPass : UnsafePass, IAllowGlobalStateModificationPass, IRenderGraphSideEffectPass
    {
        internal const string VirtualTextureGBufferShaderTagName = "VividVTGBuffer";
        internal const string VirtualTextureGPUDrivenDecalGBufferShaderTagName = "VividVTGBufferGPUDrivenDecal";

        private static readonly string[] s_DefaultShaderTagNames =
        {
            VirtualTextureGBufferShaderTagName,
        };

        private static readonly string[] s_GPUDrivenDecalShaderTagNames =
        {
            VirtualTextureGPUDrivenDecalGBufferShaderTagName,
        };

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(
            Name = "GBuffer0",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 0)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(
            Name = "GBuffer1",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 1)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(
            Name = "GBuffer2",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 2)]
        private RenderGraphTexture m_GBuffer2;

        [RenderGraphResource(
            Name = "GBuffer3",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 3)]
        private RenderGraphTexture m_GBuffer3;

        [RenderGraphResource(
            Name = "GBuffer4",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 4)]
        private RenderGraphTexture m_GBuffer4;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_GBufferDepth;

        [SerializeField, Min(1f)]
        private float m_FeedbackSampleRate = 4f;

        private readonly RenderGraphTexture m_DefaultGBuffer0;
        private readonly RenderGraphTexture m_DefaultGBuffer1;
        private readonly RenderGraphTexture m_DefaultGBuffer2;
        private readonly RenderGraphTexture m_DefaultGBuffer3;
        private readonly RenderGraphTexture m_DefaultGBuffer4;
        private readonly RenderGraphTexture m_DefaultGBufferDepth;
        private readonly RenderTargetIdentifier[] m_GBufferColorTargets = new RenderTargetIdentifier[5];
        private readonly float[] m_SpaceParams = new float[VirtualTextureSpaceShaderParams.IntCount];
        private readonly float[] m_MipOffsets = new float[VirtualTextureFeedbackProcessor.MaxMipCount];
        private readonly Vector4[] m_LayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];

        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private int m_FrameIndex;

        public VirtualTextureFeedbackPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VirtualTextureFeedbackPass));
            m_RenderList = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateOpaque(VirtualTextureGBufferShaderTagName),
            };
            m_RenderList.desc.RendererConfiguration = PerObjectData.Lightmaps;

            m_GBuffer0 = RenderGraphTexture.CreateColorTarget("GBuffer0", GraphicsFormat.R8G8B8A8_SRGB);
            m_GBuffer1 = RenderGraphTexture.CreateColorTarget("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_GBuffer2 = RenderGraphTexture.CreateColorTarget("GBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer3 = RenderGraphTexture.CreateColorTarget("GBuffer3", GraphicsFormat.B10G11R11_UFloatPack32);
            m_GBuffer3.desc.EnableRandomWrite = true;
            m_GBuffer4 = RenderGraphTexture.CreateColorTarget("GBuffer4", GraphicsFormat.R16G16B16A16_SFloat);
            m_GBufferDepth = RenderGraphTexture.CreateDepthTarget("GBufferDepth");

            m_DefaultGBuffer0 = m_GBuffer0;
            m_DefaultGBuffer1 = m_GBuffer1;
            m_DefaultGBuffer2 = m_GBuffer2;
            m_DefaultGBuffer3 = m_GBuffer3;
            m_DefaultGBuffer4 = m_GBuffer4;
            m_DefaultGBufferDepth = m_GBufferDepth;

            ConfigurePassOwnedTargets(width: 1, height: 1);
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_VirtualTextureFrameData = frameData?.GetOrCreate<VividVirtualTextureFrameData>();
            VividCameraData cameraData = frameData?.GetOrCreate<VividCameraData>();
            m_FrameIndex = cameraData != null && cameraData.frameIndex >= 0
                ? cameraData.frameIndex
                : Time.frameCount;

            int width = cameraData != null
                ? CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width)
                : Mathf.Max(1, Screen.width);
            int height = cameraData != null
                ? CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height)
                : Mathf.Max(1, Screen.height);

            ConfigurePassOwnedTargets(width, height);
            UpdateRenderListShaderTags(frameData);
        }

        public override void Record(UnsafePassContext context)
        {
            if (!AreTargetsValid()
                || m_RenderList == null
                || !m_RenderList.IsValid
                || m_VirtualTextureFrameData == null
                || !m_VirtualTextureFrameData.TryGetDefaultBinding(out VirtualTextureSpaceBinding binding)
                || !binding.IsValid)
            {
                return;
            }

            var nativeCmd = context.GetNativeCommandBuffer();
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                BindGBufferTargets(nativeCmd);
                VirtualTextureFeedbackBindingUtility.BindSpaceGlobals(
                    nativeCmd,
                    binding,
                    m_SpaceParams,
                    m_MipOffsets,
                    m_LayerFallbacks,
                    m_FrameIndex,
                    Mathf.RoundToInt(m_FeedbackSampleRate),
                    m_VirtualTextureFrameData.AdaptiveMipBias,
                    VirtualTextureDebugMode.None);

                bool hasFeedback = VirtualTextureFeedbackBindingUtility.BindFeedbackTargets(nativeCmd, binding);
                nativeCmd.DrawRendererList(m_RenderList);

                if (hasFeedback)
                    nativeCmd.ClearRandomWriteTargets();
            }
        }

        public override void Dispose()
        {
            m_VirtualTextureFrameData = null;
            m_FrameIndex = 0;
        }

        internal static Vector4 BuildFeedbackViewParamsForTesting(int feedbackSampleRate, int frameIndex)
        {
            return VirtualTextureFeedbackBindingUtility.BuildFeedbackViewParams(feedbackSampleRate, frameIndex);
        }

        private void UpdateRenderListShaderTags(ContextContainer frameData)
        {
            if (m_RenderList?.desc == null)
                return;

            m_RenderList.desc.ShaderTagNames = GBufferPass.ShouldUseGPUDrivenDecalShaderTag(frameData)
                ? s_GPUDrivenDecalShaderTagNames
                : s_DefaultShaderTagNames;
        }

        private void ConfigurePassOwnedTargets(int width, int height)
        {
            ConfigurePassOwnedTarget(m_GBuffer0, m_DefaultGBuffer0, width, height, GraphicsFormat.R8G8B8A8_SRGB, false, "GBuffer0");
            ConfigurePassOwnedTarget(m_GBuffer1, m_DefaultGBuffer1, width, height, GraphicsFormat.A2B10G10R10_UNormPack32, false, "GBuffer1");
            ConfigurePassOwnedTarget(m_GBuffer2, m_DefaultGBuffer2, width, height, GraphicsFormat.R8G8B8A8_UNorm, false, "GBuffer2");
            ConfigurePassOwnedTarget(m_GBuffer3, m_DefaultGBuffer3, width, height, GraphicsFormat.B10G11R11_UFloatPack32, true, "GBuffer3");
            ConfigurePassOwnedTarget(m_GBuffer4, m_DefaultGBuffer4, width, height, GraphicsFormat.R16G16B16A16_SFloat, false, "GBuffer4");
            ConfigurePassOwnedDepth(m_GBufferDepth, m_DefaultGBufferDepth, width, height, "GBufferDepth");
        }

        private static void ConfigurePassOwnedTarget(
            RenderGraphTexture texture,
            RenderGraphTexture defaultTexture,
            int width,
            int height,
            GraphicsFormat format,
            bool enableRandomWrite,
            string name)
        {
            if (!ReferenceEquals(texture, defaultTexture) || texture?.desc == null)
                return;

            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.ColorFormat = format;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.ClearBuffer = false;
            texture.desc.EnableRandomWrite = enableRandomWrite;
            texture.desc.Name = name;
        }

        private static void ConfigurePassOwnedDepth(
            RenderGraphTexture texture,
            RenderGraphTexture defaultTexture,
            int width,
            int height,
            string name)
        {
            if (!ReferenceEquals(texture, defaultTexture) || texture?.desc == null)
                return;

            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.ColorFormat = GraphicsFormat.None;
            texture.desc.DepthBufferBits = DepthBits.Depth32;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.ClearBuffer = false;
            texture.desc.Name = name;
        }

        private bool AreTargetsValid()
        {
            return m_GBuffer0?.IsValid() == true
                && m_GBuffer1?.IsValid() == true
                && m_GBuffer2?.IsValid() == true
                && m_GBuffer3?.IsValid() == true
                && m_GBuffer4?.IsValid() == true
                && m_GBufferDepth?.IsValid() == true;
        }

        private void BindGBufferTargets(CommandBuffer cmd)
        {
            m_GBufferColorTargets[0] = m_GBuffer0;
            m_GBufferColorTargets[1] = m_GBuffer1;
            m_GBufferColorTargets[2] = m_GBuffer2;
            m_GBufferColorTargets[3] = m_GBuffer3;
            m_GBufferColorTargets[4] = m_GBuffer4;
            cmd.SetRenderTarget(m_GBufferColorTargets, m_GBufferDepth);
        }
    }
}
