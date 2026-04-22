using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class ShadowClassifyPass : ComputePass
    {
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const string KernelName = "ShadowClassify";

        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int ShadowClassifyMaskId = Shader.PropertyToID("_ShadowClassifyMask");
        private static readonly int LightDirectionWSId = Shader.PropertyToID("_LightDirectionWS");
        private static readonly int NormalFacingThresholdId = Shader.PropertyToID("_NormalFacingThreshold");
        private static readonly int OutputWidthId = Shader.PropertyToID("_OutputWidth");
        private static readonly int OutputHeightId = Shader.PropertyToID("_OutputHeight");

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "ShadowClassifyMask", Access = AccessFlags.Write)]
        private RenderGraphTexture m_ShadowClassifyMask;

        private ComputeShader m_ClassifyCompute;
        private int m_Kernel = -1;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private Vector4 m_LightDirectionWS = new Vector4(0f, 1f, 0f, 0f);

        private const float DefaultNormalFacingThreshold = 0.0f;

        public ShadowClassifyPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ShadowClassifyPass));
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_ShadowClassifyMask = RenderGraphTexture.CreateOutput("ShadowClassifyMask", GraphicsFormat.R8_UNorm);
            m_ShadowClassifyMask.desc.ClearBuffer = true;
            m_ShadowClassifyMask.desc.ClearColor = Color.clear;
            m_ShadowClassifyMask.desc.FilterMode = FilterMode.Point;
            m_ShadowClassifyMask.desc.WrapMode = TextureWrapMode.Clamp;
        }

        public override void Create()
        {
            m_ClassifyCompute =
                PipelineResourceManager.Get<VividRPCoreResources>()?.ShadowClassifyCompute;

            if (m_ClassifyCompute == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find compute shader resource for {nameof(ShadowClassifyPass)}.");
                return;
            }

            m_Kernel = m_ClassifyCompute.FindKernel(KernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            ConfigureMaskTexture(cameraData.actualWidth, cameraData.actualHeight);

            m_DispatchGroupCountX = CoreUtils.DivRoundUp(cameraData.actualWidth, ThreadGroupSizeX);
            m_DispatchGroupCountY = CoreUtils.DivRoundUp(cameraData.actualHeight, ThreadGroupSizeY);

            var lightData = frameData.GetOrCreate<VividLightData>();
            if (lightData != null && lightData.hasMainDirectionalLight)
            {
                var dir = lightData.mainDirectionalLight.directionWS;
                m_LightDirectionWS = new Vector4(dir.x, dir.y, dir.z, 0f);
            }
            else
            {
                m_LightDirectionWS = new Vector4(0f, 1f, 0f, 0f);
            }
        }

        public override void Record(ComputePassContext context)
        {
            if (m_ClassifyCompute == null || m_Kernel < 0)
                return;

            if (!m_DepthTexture.innerHandle.IsValid()
                || !m_GBuffer1.innerHandle.IsValid()
                || !m_ShadowClassifyMask.innerHandle.IsValid())
                return;

            var cmd = context.cmd;

            cmd.SetComputeTextureParam(m_ClassifyCompute, m_Kernel,
                DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ClassifyCompute, m_Kernel,
                GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_ClassifyCompute, m_Kernel,
                ShadowClassifyMaskId, m_ShadowClassifyMask.innerHandle);

            cmd.SetComputeVectorParam(m_ClassifyCompute, LightDirectionWSId, m_LightDirectionWS);
            cmd.SetComputeFloatParam(m_ClassifyCompute, NormalFacingThresholdId, DefaultNormalFacingThreshold);
            cmd.SetComputeIntParam(m_ClassifyCompute, OutputWidthId, m_ShadowClassifyMask.desc.Width);
            cmd.SetComputeIntParam(m_ClassifyCompute, OutputHeightId, m_ShadowClassifyMask.desc.Height);

            cmd.DispatchCompute(m_ClassifyCompute, m_Kernel,
                m_DispatchGroupCountX, m_DispatchGroupCountY, 1);
        }

        public override void Dispose()
        {
            m_ClassifyCompute = null;
            m_Kernel = -1;
            m_DispatchGroupCountX = 1;
            m_DispatchGroupCountY = 1;
            m_LightDirectionWS = new Vector4(0f, 1f, 0f, 0f);
        }

        private void ConfigureMaskTexture(int width, int height)
        {
            m_ShadowClassifyMask?.Resize(width, height);
        }

    }
}
