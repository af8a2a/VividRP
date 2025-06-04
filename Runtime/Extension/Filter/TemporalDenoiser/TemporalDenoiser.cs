using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class TemporalDenoiser
    {
        ComputeShader TemporalDenoiserCS;
        int TemporalDenoiserKernel;



        public TemporalDenoiser()
        {
            TemporalDenoiserCS = Resources.Load<ComputeShader>("TemporalDenoise");
            TemporalDenoiserKernel = TemporalDenoiserCS.FindKernel("TemporalDenoise");
        }

        #region CS

        public class TemporalAntiAliasingCSData
        {
            public ComputeShader TemporalAntiAliasingShader;
            public int TemporalAntiAliasingKernel;
            public Vector2 Resolution;
            public TextureHandle motionTexture;
            public TextureHandle depthTexture;
            public TextureHandle currentHistory;
            public TextureHandle inputTexture;
            public TextureHandle denoiseOutput;
        }


        public TextureHandle DoColorTemporalDenoiseCS(RenderGraph renderGraph,
            Camera camera,
            TextureHandle motionVectors,
            TextureHandle depthTexture,
            TextureHandle inputTexture,
            TextureHandle prevHistory,
            TextureHandle currHistory,
            TemporalDenoiserSetting setting
        )
        {
            using var builder =
                renderGraph.AddComputePass<TemporalAntiAliasingCSData>("Temporal Denoise CS", out var passData);
            passData.Resolution = new Vector2(camera.pixelWidth, camera.pixelHeight);
            passData.TemporalAntiAliasingShader = TemporalDenoiserCS;
            passData.TemporalAntiAliasingKernel = TemporalDenoiserKernel;
            passData.motionTexture = motionVectors;
            passData.inputTexture = inputTexture;
            passData.depthTexture = depthTexture;
            passData.currentHistory = prevHistory;
            passData.denoiseOutput = currHistory;
            //
            builder.AllowGlobalStateModification(true);
            builder.UseTexture(passData.motionTexture);
            builder.UseTexture(passData.depthTexture);
            builder.UseTexture(passData.currentHistory);
            builder.UseTexture(passData.inputTexture, AccessFlags.ReadWrite);
            builder.UseTexture(passData.denoiseOutput, AccessFlags.ReadWrite);

            builder.SetRenderFunc(
                (TemporalAntiAliasingCSData data, ComputeGraphContext ctx) =>
                {
                    const int groupSizeX = 16;
                    const int groupSizeY = 16;
                    int threadGroupX = RenderingUtilsExt.DivRoundUp((int)data.Resolution.x, groupSizeX);
                    int threadGroupY = RenderingUtilsExt.DivRoundUp((int)data.Resolution.y, groupSizeY);
                    var cmd = ctx.cmd;
                    cmd.SetComputeTextureParam(data.TemporalAntiAliasingShader, data.TemporalAntiAliasingKernel,
                        "ColorBuffer", data.inputTexture);
                    cmd.SetComputeTextureParam(data.TemporalAntiAliasingShader, data.TemporalAntiAliasingKernel,
                        "DepthBuffer", data.depthTexture);
                    cmd.SetComputeTextureParam(data.TemporalAntiAliasingShader, data.TemporalAntiAliasingKernel,
                        "VelocityBuffer", data.motionTexture);
                    cmd.SetComputeTextureParam(data.TemporalAntiAliasingShader, data.TemporalAntiAliasingKernel,
                        "HistoryBuffer", data.currentHistory);
                    cmd.SetComputeTextureParam(data.TemporalAntiAliasingShader, data.TemporalAntiAliasingKernel,
                        "OutputBuffer", data.denoiseOutput);

                    cmd.SetComputeVectorParam(data.TemporalAntiAliasingShader, "TAA_BlendParameter",
                        new Vector4(0.97f, 0.9f, 6000, 1));
                    cmd.SetComputeVectorParam(data.TemporalAntiAliasingShader, "TAAJitter",
                        TemporalUtils.GenerateRandomOffset() / data.Resolution);

                    cmd.SetKeyword(data.TemporalAntiAliasingShader,
                        new LocalKeyword(data.TemporalAntiAliasingShader, "HDROutput"), camera.allowHDR);


                    cmd.DispatchCompute(data.TemporalAntiAliasingShader, data.TemporalAntiAliasingKernel, threadGroupX,
                        threadGroupY, 1);
                    
                });


            return passData.denoiseOutput;
        }

        #endregion
        
    }
}