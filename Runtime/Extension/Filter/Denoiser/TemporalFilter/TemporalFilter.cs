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
        private static int MotionBuffer = Shader.PropertyToID("MotionBuffer");
        private static int DepthBuffer = Shader.PropertyToID("DepthBuffer");
        private static int HistoryDepth = Shader.PropertyToID("HistoryDepth");

        
        public void Init()
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<DenoiserRuntimeShader>();


            // Keep track of the resources
            m_TemporalFilter = runtimeShaders.temporalFilterCS;

            // Grab all the kernels we'll eventually need
            m_TemporalFilterKernel = m_TemporalFilter.FindKernel("TemporalDenoise");

            // Data required for the online initialization
            m_DenoiserInitialized = false;

            HistoryBufferCaptureManager.instance.AcquireHistoryPasses();
        }

        public void Release()
        {
            HistoryBufferCaptureManager.instance.ReleaseHistoryPasses();
        }

        class TemporalDenoiserPassData
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
            public TextureHandle motionBuffer;
            public TextureHandle depthBuffer;
            public TextureHandle historyDepthBuffer;

        }





        public TextureHandle Denoise(RenderGraph renderGraph, UniversalCameraData cameraData,
            TextureHandle noisyBuffer,
            TextureHandle historyBuffer,
            TextureHandle historyDepthBuffer,
            TextureHandle motionBuffer,
            TextureHandle depthBuffer
        )
        {
            using (var builder = renderGraph.AddComputePass<TemporalDenoiserPassData>("Temporal Denoiser", out var passData))
            {
                var camera = cameraData.camera;
                // var histroyRT = HistoryFrameRTSystem.GetOrCreate(camera);


                builder.AllowPassCulling(false);
                // Initialization data
                passData.needInit = !m_DenoiserInitialized;
                m_DenoiserInitialized = true;


                // Camera parameters
                passData.texWidth = cameraData.scaledWidth;
                passData.texHeight = cameraData.scaledHeight;
                passData.temporalFilterKernel = m_TemporalFilterKernel;

                // Other parameters
                passData.temporalDenoiserCS = m_TemporalFilter;

                passData.noisyBuffer = noisyBuffer;
                passData.motionBuffer = motionBuffer;
                passData.historyBuffer = historyBuffer;
                passData.depthBuffer = depthBuffer;
                passData.historyDepthBuffer = historyDepthBuffer;

                builder.UseTexture(passData.noisyBuffer, AccessFlags.ReadWrite);
                builder.UseTexture(passData.historyBuffer, AccessFlags.ReadWrite);
                builder.UseTexture(passData.motionBuffer);
                builder.UseTexture(passData.depthBuffer);
                builder.UseTexture(passData.historyDepthBuffer);

                builder.SetRenderFunc((TemporalDenoiserPassData data, ComputeGraphContext ctx) =>
                {
                    // Evaluate the dispatch parameters
                    int numTilesX = RenderingUtilsExt.DivRoundUp(data.texWidth, 16);
                    int numTilesY = RenderingUtilsExt.DivRoundUp(data.texHeight, 16);

                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalFilterKernel, ColorBuffer, data.noisyBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalFilterKernel, HistoryBuffer, data.historyBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalFilterKernel, MotionBuffer, data.motionBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalFilterKernel, HistoryDepth, data.historyDepthBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalFilterKernel, DepthBuffer, data.depthBuffer);

                    ctx.cmd.DispatchCompute(data.temporalDenoiserCS, data.temporalFilterKernel, numTilesX, numTilesY, 1);
                });
                return passData.noisyBuffer;
            }
        }
    }
}