using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VirtualTextureDemoPass : UnsafePass, IAllowGlobalStateModificationPass
    {
        internal const string VirtualTextureShaderTagName = "VividVT";

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTarget;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthTarget;

        [SerializeField]
        private VirtualTextureDebugMode m_DefaultDebugMode = VirtualTextureDebugMode.None;

        [SerializeField, Min(1)]
        private int m_FeedbackSampleRate = 4;

        private readonly float[] m_SpaceParams = new float[VirtualTextureSpaceShaderParams.IntCount];
        private readonly float[] m_MipOffsets = new float[VirtualTextureFeedbackProcessor.MaxMipCount];
        private readonly Vector4[] m_LayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];

        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private VirtualTextureDebugMode m_ResolvedDebugMode;
        private int m_FrameIndex;
        private bool m_ShouldSkipExecution;

        public VirtualTextureDemoPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VirtualTextureDemoPass));
            m_RenderList = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateOpaque(VirtualTextureShaderTagName),
            };
            m_ColorTarget = RenderGraphTexture.CreateColorTarget("Color", GraphicsFormat.R8G8B8A8_UNorm);
            m_ColorTarget.desc.ClearBuffer = false;
            m_DepthTarget = RenderGraphTexture.CreateDepthTarget("Depth", DepthBits.Depth32);
            m_DepthTarget.desc.ClearBuffer = false;
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_VirtualTextureFrameData = frameData?.GetOrCreate<VividVirtualTextureFrameData>();
            VividRenderingDebugSettingsData debugSettings = VividRenderingDebugDisplaySettings.Data;
            m_ResolvedDebugMode = debugSettings != null
                ? debugSettings.virtualTextureDebugMode
                : m_DefaultDebugMode;

            VividCameraData cameraData = frameData?.GetOrCreate<VividCameraData>();
            m_FrameIndex = cameraData != null && cameraData.frameIndex >= 0
                ? cameraData.frameIndex
                : Time.frameCount;
            m_ShouldSkipExecution = DebugPassCameraUtility.ShouldSkipExecution(cameraData);
            int width = cameraData != null
                ? Mathf.Max(1, cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth)
                : Mathf.Max(1, Screen.width);
            int height = cameraData != null
                ? Mathf.Max(1, cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight)
                : Mathf.Max(1, Screen.height);

            if (m_ColorTarget?.desc != null)
            {
                m_ColorTarget.desc.Width = width;
                m_ColorTarget.desc.Height = height;
            }

            if (m_DepthTarget?.desc != null)
            {
                m_DepthTarget.desc.Width = width;
                m_DepthTarget.desc.Height = height;
            }
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_ShouldSkipExecution)
                return;

            if (m_RenderList == null
                || !m_RenderList.IsValid
                || m_VirtualTextureFrameData == null
                || !m_VirtualTextureFrameData.TryGetDefaultBinding(out VirtualTextureSpaceBinding binding)
                || !binding.IsValid)
            {
                return;
            }

            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                nativeCmd.SetRenderTarget(m_ColorTarget, m_DepthTarget);
                VirtualTextureFeedbackBindingUtility.BindSpaceGlobals(
                    nativeCmd,
                    binding,
                    m_SpaceParams,
                    m_MipOffsets,
                    m_LayerFallbacks,
                    m_FrameIndex,
                    m_FeedbackSampleRate,
                    m_VirtualTextureFrameData.AdaptiveMipBias,
                    m_ResolvedDebugMode);

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
            m_ShouldSkipExecution = false;
        }
    }
}
