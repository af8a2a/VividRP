using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class GenerateViewZPass : ComputePass//, IAsyncComputeSupportedPass
    {
        private static readonly int DepthTextureId      = Shader.PropertyToID("_DepthTexture");
        private static readonly int LinearDepthOutputId = Shader.PropertyToID("_LinearDepthOutput");

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "LinearDepth", Access = AccessFlags.Write)]
        private RenderGraphTexture m_LinearDepthTexture;

        private ComputeShader m_Shader;
        private int m_Width;
        private int m_Height;

        public GenerateViewZPass()
        {
            profilingSampler = new ProfilingSampler(nameof(GenerateViewZPass));
            m_DepthTexture       = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_LinearDepthTexture = RenderGraphTexture.CreateOutput("LinearDepth", GraphicsFormat.R32_SFloat);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (resources == null) return;
            m_Shader = resources.GenerateViewZCompute;
        }

        public override void Dispose()
        {
            
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_Width  = cameraData.actualWidth;
            m_Height = cameraData.actualHeight;
            m_LinearDepthTexture.desc.ClearBuffer = false;
            m_LinearDepthTexture.Resize(m_Width, m_Height);
            if (m_LinearDepthTexture.desc != null)
                m_LinearDepthTexture.desc.EnableRandomWrite = true;
        }

        public override void Record(ComputeGraphContext context)
        {
            
            if (m_Shader == null) return;
            if (m_DepthTexture == null || !m_DepthTexture.innerHandle.IsValid()) return;
            if (m_LinearDepthTexture == null || !m_LinearDepthTexture.innerHandle.IsValid()) return;

            var cmd = context.cmd;
            cmd.SetComputeTextureParam(m_Shader, 0, DepthTextureId,      m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_Shader, 0, LinearDepthOutputId, m_LinearDepthTexture.innerHandle);
            cmd.DispatchCompute(m_Shader, 0,
                CoreUtils.DivRoundUp(m_Width,  8),
                CoreUtils.DivRoundUp(m_Height, 8), 1);
        }
    }
}
