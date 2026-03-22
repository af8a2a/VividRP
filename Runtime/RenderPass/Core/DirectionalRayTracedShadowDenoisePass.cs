using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class DirectionalRayTracedShadowDenoisePass : ComputePass
    {
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const string KernelName = "ShadowTemporalAccumulation";
        private const string HistoryShadowKey = "HistoryShadow";

        private static readonly int RawShadowTextureId = Shader.PropertyToID("_RawShadowTexture");
        private static readonly int HistoryShadowTextureId = Shader.PropertyToID("_HistoryShadowTexture");
        private static readonly int MotionVectorTextureId = Shader.PropertyToID("_MotionVectorTexture");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int DenoisedShadowTextureId = Shader.PropertyToID("_DenoisedShadowTexture");
        private static readonly int InvViewProjectionMatrixId = Shader.PropertyToID("_InvViewProjectionMatrix");
        private static readonly int HasValidHistoryId = Shader.PropertyToID("_HasValidHistory");
        private static readonly int TemporalBlendMinId = Shader.PropertyToID("_TemporalBlendMin");
        private static readonly int TemporalBlendMaxId = Shader.PropertyToID("_TemporalBlendMax");
        private static readonly int DepthRejectionThresholdId = Shader.PropertyToID("_DepthRejectionThreshold");
        private static readonly int NormalRejectionThresholdId = Shader.PropertyToID("_NormalRejectionThreshold");

        [RenderGraphResource(Name = "RawShadow", Access = AccessFlags.Read)]
        private RenderGraphTexture m_RawShadowTexture;

        [RenderGraphResource(Name = "MotionVectors", Access = AccessFlags.Read)]
        private RenderGraphTexture m_MotionVectorTexture;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(
            Name = "HistoryShadow",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_HistoryShadowTexture;

        [RenderGraphResource(Name = "DenoisedShadow", Access = AccessFlags.Write)]
        private RenderGraphTexture m_DenoisedShadowTexture;

        private ComputeShader m_DenoiseCompute;
        private int m_Kernel = -1;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private Matrix4x4 m_InvViewProjectionMatrix = Matrix4x4.identity;
        private bool m_HasValidHistory;

        private const float DefaultTemporalBlendMin = 0.05f;
        private const float DefaultTemporalBlendMax = 1.0f;
        private const float DefaultDepthRejectionThreshold = 1.0f;
        private const float DefaultNormalRejectionThreshold = 0.9f;

        public DirectionalRayTracedShadowDenoisePass()
        {
            profilingSampler = new ProfilingSampler(nameof(DirectionalRayTracedShadowDenoisePass));
            m_RawShadowTexture = RenderGraphTexture.CreateInput("RawShadow", GraphicsFormat.R16_SFloat);
            m_MotionVectorTexture = RenderGraphTexture.CreateInput("MotionVectors", GraphicsFormat.R16G16_SFloat);
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.R16G16_SFloat);
            m_HistoryShadowTexture = RenderGraphTexture.CreateInput("HistoryShadow", GraphicsFormat.R16_SFloat);
            m_DenoisedShadowTexture = RenderGraphTexture.CreateOutput("DenoisedShadow", GraphicsFormat.R16_SFloat);
            m_DenoisedShadowTexture.desc.ClearBuffer = true;
            m_DenoisedShadowTexture.desc.ClearColor = new Color(65504f, 0f, 0f, 0f);
            m_DenoisedShadowTexture.desc.FilterMode = FilterMode.Bilinear;
            m_DenoisedShadowTexture.desc.WrapMode = TextureWrapMode.Clamp;
        }

        public override void Create()
        {
            m_DenoiseCompute =
                PipelineResourceManager.Get<VividRPCoreResources>()?.DirectionalRayTracedShadowDenoiseCompute;

            if (m_DenoiseCompute == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find compute shader resource for {nameof(DirectionalRayTracedShadowDenoisePass)}.");
                return;
            }

            m_Kernel = m_DenoiseCompute.FindKernel(KernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            ConfigureOutputTexture(cameraData.actualWidth, cameraData.actualHeight);
            m_HasValidHistory = AllocHistoryTexture(
                HistoryShadowKey,
                m_HistoryShadowTexture,
                m_DenoisedShadowTexture,
                m_DenoisedShadowTexture?.desc);
            m_InvViewProjectionMatrix =
                DirectionalRayTracedShadowPass.ResolveInvViewProjectionMatrix(cameraData);

            m_DispatchGroupCountX = CoreUtils.DivRoundUp(cameraData.actualWidth, ThreadGroupSizeX);
            m_DispatchGroupCountY = CoreUtils.DivRoundUp(cameraData.actualHeight, ThreadGroupSizeY);
        }

        public override void Record(ComputeGraphContext context)
        {
            if (m_DenoiseCompute == null || m_Kernel < 0)
                return;

            if (!m_RawShadowTexture.innerHandle.IsValid()
                || !m_DenoisedShadowTexture.innerHandle.IsValid())
                return;

            var cmd = context.cmd;

            cmd.SetComputeTextureParam(m_DenoiseCompute, m_Kernel,
                RawShadowTextureId, m_RawShadowTexture.innerHandle);
            cmd.SetComputeTextureParam(m_DenoiseCompute, m_Kernel,
                DenoisedShadowTextureId, m_DenoisedShadowTexture.innerHandle);
            cmd.SetComputeTextureParam(m_DenoiseCompute, m_Kernel,
                DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_DenoiseCompute, m_Kernel,
                GBuffer1Id, m_GBuffer1.innerHandle);

            if (m_MotionVectorTexture != null && m_MotionVectorTexture.innerHandle.IsValid())
            {
                cmd.SetComputeTextureParam(m_DenoiseCompute, m_Kernel,
                    MotionVectorTextureId, m_MotionVectorTexture.innerHandle);
            }

            if (m_HasValidHistory)
            {
                cmd.SetComputeTextureParam(m_DenoiseCompute, m_Kernel,
                    HistoryShadowTextureId, m_HistoryShadowTexture.innerHandle);
            }

            cmd.SetComputeIntParam(m_DenoiseCompute, HasValidHistoryId, m_HasValidHistory ? 1 : 0);
            cmd.SetComputeFloatParam(m_DenoiseCompute, TemporalBlendMinId, DefaultTemporalBlendMin);
            cmd.SetComputeFloatParam(m_DenoiseCompute, TemporalBlendMaxId, DefaultTemporalBlendMax);
            cmd.SetComputeFloatParam(m_DenoiseCompute, DepthRejectionThresholdId, DefaultDepthRejectionThreshold);
            cmd.SetComputeFloatParam(m_DenoiseCompute, NormalRejectionThresholdId, DefaultNormalRejectionThreshold);
            cmd.SetComputeMatrixParam(m_DenoiseCompute, InvViewProjectionMatrixId, m_InvViewProjectionMatrix);

            cmd.DispatchCompute(m_DenoiseCompute, m_Kernel,
                m_DispatchGroupCountX, m_DispatchGroupCountY, 1);
        }

        public override void Dispose()
        {
            m_DenoiseCompute = null;
            m_Kernel = -1;
            m_DispatchGroupCountX = 1;
            m_DispatchGroupCountY = 1;
            m_InvViewProjectionMatrix = Matrix4x4.identity;
            m_HasValidHistory = false;
        }

        private void ConfigureOutputTexture(int width, int height)
        {
            if (m_DenoisedShadowTexture?.desc == null)
                return;

            m_DenoisedShadowTexture.Resize(width, height);
            m_DenoisedShadowTexture.desc.ColorFormat = GraphicsFormat.R16_SFloat;
            m_DenoisedShadowTexture.desc.FilterMode = FilterMode.Bilinear;
            m_DenoisedShadowTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_DenoisedShadowTexture.desc.ClearBuffer = true;
            m_DenoisedShadowTexture.desc.ClearColor = Color.white;
            m_DenoisedShadowTexture.desc.EnableRandomWrite = true;
        }

    }
}
