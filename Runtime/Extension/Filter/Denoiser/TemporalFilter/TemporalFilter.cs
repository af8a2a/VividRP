using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    class TemporalFilter
    {
        // Resources used for the de-noiser
        ComputeShader m_TemporalFilter;

        // Runtime Initialization data
        bool m_DenoiserInitialized;

        int m_TemporalFilterKernel;


        private static int ColorBuffer = Shader.PropertyToID("ColorBuffer");
        private static int HistoryBuffer = Shader.PropertyToID("HistoryBuffer");


        public void Init()
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<DenoiserRuntimeShader>();


            // Keep track of the resources
            m_TemporalFilter = runtimeShaders.temporalFilterCS;

            // Grab all the kernels we'll eventually need
            m_TemporalFilterKernel = m_TemporalFilter.FindKernel("TemporalDenoise");
            // Data required for the online initialization
            m_DenoiserInitialized = false;
        }

        public void Release()
        {
        }

        class DiffuseDenoiserPassData
        {
            // Camera parameters
            public int texWidth;
            public int texHeight;

            // Denoising parameters
            public bool needInit;

            // Kernels
            public int temporalFilterKernel;


            public ComputeShader temporalDenoiserCS;

            public TextureHandle noisyBuffer;
            public TextureHandle historyBuffer;
        }


        public TextureHandle Denoise(RenderGraph renderGraph, UniversalCameraData cameraData, TextureHandle noisyBuffer, TextureHandle historyBuffer)
        {
            using (var builder = renderGraph.AddComputePass<DiffuseDenoiserPassData>("Temporal Denoiser", out var passData))
            {
                var camera = cameraData.camera;
                // var histroyRT = HistoryFrameRTSystem.GetOrCreate(camera);


                builder.AllowPassCulling(false);
                // Initialization data
                passData.needInit = !m_DenoiserInitialized;
                m_DenoiserInitialized = true;


                // Camera parameters
                passData.texWidth = RenderingUtilsExt.DivRoundUp(cameraData.scaledWidth, 16);
                passData.texHeight = RenderingUtilsExt.DivRoundUp(cameraData.scaledHeight, 16);
                passData.temporalFilterKernel = m_TemporalFilterKernel;

                // Other parameters
                passData.temporalDenoiserCS = m_TemporalFilter;


                passData.noisyBuffer = noisyBuffer;

                passData.historyBuffer = historyBuffer;

                builder.UseTexture(passData.noisyBuffer, AccessFlags.ReadWrite);
                builder.UseTexture(passData.historyBuffer);

                builder.SetRenderFunc((DiffuseDenoiserPassData data, ComputeGraphContext ctx) =>
                {
                    // Generate the point distribution if needed (this is only ran once)
                    if (data.needInit)
                    {
                    }

                    // Evaluate the dispatch parameters
                    int areaTileSize = 8;
                    int numTilesX = (data.texWidth + (areaTileSize - 1)) / areaTileSize;
                    int numTilesY = (data.texHeight + (areaTileSize - 1)) / areaTileSize;

                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalFilterKernel, ColorBuffer, data.noisyBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalFilterKernel, HistoryBuffer, data.historyBuffer);

                    ctx.cmd.DispatchCompute(data.temporalDenoiserCS, data.temporalFilterKernel, numTilesX, numTilesY, 1);
                });
                return passData.noisyBuffer;
            }
        }
    }
}