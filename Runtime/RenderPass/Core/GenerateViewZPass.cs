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
            m_DepthTexture       = CreateInputTexture("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_LinearDepthTexture = CreateOutputTexture("LinearDepth", GraphicsFormat.R32_SFloat);
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
            ConfigureTexture(m_LinearDepthTexture, m_Width, m_Height);
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

        private static void ConfigureTexture(RenderGraphTexture tex, int width, int height)
        {
            if (tex?.desc == null) return;
            tex.desc.Width             = width;
            tex.desc.Height            = height;
            tex.desc.EnableRandomWrite = true;
        }

        private static RenderGraphTexture CreateInputTexture(string name, GraphicsFormat format,
            DepthBits depthBits = DepthBits.None)
        {
            var tex = new RenderGraphTexture
            {
                desc = format == GraphicsFormat.None
                    ? RenderGraphTextureDesc.CreateDepthTarget(1, 1, depthBits)
                    : RenderGraphTextureDesc.CreateColorTarget(1, 1, format)
            };
            tex.desc.Name        = name;
            tex.desc.ClearBuffer = false;
            return tex;
        }

        private static RenderGraphTexture CreateOutputTexture(string name, GraphicsFormat format)
        {
            var tex = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, format)
            };
            tex.desc.Name             = name;
            tex.desc.ClearBuffer      = false;
            tex.desc.EnableRandomWrite = true;
            return tex;
        }
    }
}
