using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    partial class NSFluidEvaluator
    {
        static int _NormalTextureRW = Shader.PropertyToID("_NormalTextureRW");


        private ComputeShader m_GenerateNormal;

        class GenerateNormalPassData
        {
            public int Resolution;

            public ComputeShader GenerateNormal;
            public TextureHandle VelocityTexture;
            public TextureHandle NormalTexture;
        }

        public TextureHandle GenerateNormalTexture(RenderGraph renderGraph, TextureHandle velocityTexture)
        {
            if (!m_GenerateNormal)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<FluidRuntimeShader>();
                m_GenerateNormal = runtimeShader.generateNormal;
            }

            TextureHandle result = TextureHandle.nullHandle;


            using (var builder = renderGraph.AddComputePass<GenerateNormalPassData>("Fluid Velocity Generate Normal", out var passData))
            {
                var desc = renderGraph.GetTextureDesc(velocityTexture);
                passData.VelocityTexture = velocityTexture;
                passData.NormalTexture = renderGraph.CreateTexture(new TextureDesc(desc.width, desc.height)
                {
                    format = GraphicsFormat.R16G16_SFloat,
                    enableRandomWrite = true
                });
                passData.Resolution = desc.width;
                passData.GenerateNormal = m_GenerateNormal;
        

                builder.UseTexture(passData.VelocityTexture);
                builder.UseTexture(passData.NormalTexture, AccessFlags.Write);

                builder.AllowPassCulling(false);
                // builder.EnableAsyncCompute(true);


                builder.SetRenderFunc<GenerateNormalPassData>((data, ctx) =>
                {
                    var cmd = ctx.cmd;

                    var cs = data.GenerateNormal;
                    var kernel = 0;


                    cmd.SetComputeTextureParam(cs, kernel, _VelocityTexture, data.VelocityTexture);
                    cmd.SetComputeTextureParam(cs, kernel, _NormalTextureRW, data.NormalTexture);
                    cmd.SetComputeFloatParam(cs, _SimulationResolution, passData.Resolution);

                    var threadCount = CoreUtils.DivRoundUp((int)passData.Resolution, 8);
                    cmd.DispatchCompute(cs, kernel, threadCount, threadCount, 1);
                });
                result = passData.NormalTexture;
            }

            return result;
        }
    }
}