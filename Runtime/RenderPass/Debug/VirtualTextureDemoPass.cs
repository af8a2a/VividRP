using System;
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

        private readonly float[] m_SpaceParams = new float[VirtualTextureSpaceShaderParams.IntCount];
        private readonly float[] m_MipOffsets = new float[VirtualTextureFeedbackProcessor.MaxMipCount];

        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private VirtualTextureDebugMode m_ResolvedDebugMode;

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
            if (m_RenderList == null
                || !m_RenderList.IsValid
                || m_VirtualTextureFrameData == null
                || !m_VirtualTextureFrameData.TryGetPrimaryBinding(out VirtualTextureSpaceBinding binding)
                || !binding.IsValid)
            {
                return;
            }

            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                nativeCmd.SetRenderTarget(m_ColorTarget, m_DepthTarget);
                BindSpaceGlobals(nativeCmd, binding);

                bool hasFeedback = binding.HasFeedback;
                if (hasFeedback)
                {
                    nativeCmd.SetRandomWriteTarget(1, binding.FeedbackRequests, preserveCounterValue: false);
                    nativeCmd.SetRandomWriteTarget(2, binding.FeedbackCounter, preserveCounterValue: true);
                }

                nativeCmd.DrawRendererList(m_RenderList);

                if (hasFeedback)
                    nativeCmd.ClearRandomWriteTargets();
            }
        }

        public override void Dispose()
        {
            m_VirtualTextureFrameData = null;
        }

        private void BindSpaceGlobals(CommandBuffer cmd, in VirtualTextureSpaceBinding binding)
        {
            Array.Clear(m_SpaceParams, 0, m_SpaceParams.Length);
            Array.Clear(m_MipOffsets, 0, m_MipOffsets.Length);

            float[] shaderParams = binding.ShaderParams.ToFloatArray();
            for (int paramIndex = 0; paramIndex < shaderParams.Length && paramIndex < m_SpaceParams.Length; paramIndex++)
                m_SpaceParams[paramIndex] = shaderParams[paramIndex];

            int[] mipOffsets = binding.MipOffsets;
            if (mipOffsets != null)
            {
                for (int mipIndex = 0; mipIndex < mipOffsets.Length && mipIndex < m_MipOffsets.Length; mipIndex++)
                    m_MipOffsets[mipIndex] = mipOffsets[mipIndex];
            }

            cmd.SetGlobalBuffer(VirtualTextureShaderIDs._VTPageTable, binding.PageTableBuffer);
            cmd.SetGlobalTexture(VirtualTextureShaderIDs._VTPhysicalCache, binding.PhysicalCache);
            cmd.SetGlobalFloatArray(VirtualTextureShaderIDs._VTSpaceParams, m_SpaceParams);
            cmd.SetGlobalFloatArray(VirtualTextureShaderIDs._VTMipOffsets, m_MipOffsets);
            cmd.SetGlobalInt(VirtualTextureShaderIDs._VTFeedbackEnabled, binding.HasFeedback ? 1 : 0);
            cmd.SetGlobalInt(VirtualTextureShaderIDs._VTDebugMode, (int)m_ResolvedDebugMode);
        }
    }
}
