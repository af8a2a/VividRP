using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    //migrate from HDRP Diffuse Denoiser
    class SpatialDenoiser
    {
        // Resources used for the de-noiser
        ComputeShader m_DiffuseDenoiser;

        // Runtime Initialization data
        bool m_DenoiserInitialized;
        RTHandle m_OwnenScrambledTexture;
        GraphicsBuffer m_PointDistribution;

        // Kernels that may be required
        int m_BilateralFilterSingleKernel;
        int m_BilateralFilterColorKernel;
        int m_GatherSingleKernel;
        int m_GatherColorKernel;



        #region ShaderID
        private static readonly int _PointDistribution = Shader.PropertyToID("_PointDistribution");
        private static readonly int _PointDistributionRW = Shader.PropertyToID("_PointDistributionRW");
        private static readonly int _OwenScrambledRGTexture = Shader.PropertyToID("_OwenScrambledRGTexture");
        private static readonly int _DenoiserFilterRadius = Shader.PropertyToID("_DenoiserFilterRadius");
        private static readonly int _DenoiseInputTexture = Shader.PropertyToID("_DenoiseInputTexture");
        private static readonly int _DepthTexture = Shader.PropertyToID("_DepthTexture");
        private static readonly int _NormalBufferTexture = Shader.PropertyToID("_NormalBufferTexture");
        private static readonly int _DenoiseOutputTextureRW = Shader.PropertyToID("_DenoiseOutputTextureRW");
        private static readonly int _HalfResolutionFilter = Shader.PropertyToID("_HalfResolutionFilter");
        private static readonly int _PixelSpreadAngleTangent = Shader.PropertyToID("_PixelSpreadAngleTangent");
        private static readonly int _DenoiserResolutionMultiplierVals = Shader.PropertyToID("_DenoiserResolutionMultiplierVals");
        private static readonly int _JitterFramePeriod = Shader.PropertyToID("_JitterFramePeriod");


        

        #endregion
        
        
        
        public void Init()
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<DenoiserRuntimeShader>();
            var blueNoise = RuntimeTextureSystem.instance;

            // Keep track of the resources
            m_DiffuseDenoiser = runtimeShaders.SpatialDenoiserCS;

            // Grab all the kernels we'll eventually need
            m_BilateralFilterSingleKernel = m_DiffuseDenoiser.FindKernel("BilateralFilterSingle");
            m_BilateralFilterColorKernel = m_DiffuseDenoiser.FindKernel("BilateralFilterColor");
            m_GatherSingleKernel = m_DiffuseDenoiser.FindKernel("GatherSingle");
            m_GatherColorKernel = m_DiffuseDenoiser.FindKernel("GatherColor");

            // Data required for the online initialization
            m_DenoiserInitialized = false;
            m_OwnenScrambledTexture = blueNoise.owenScrambledRGBATex;
            m_PointDistribution = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16 * 4, 2 * sizeof(float));
        }
        
        
        
        
        

        public void Release()
        {
            CoreUtils.SafeRelease(m_PointDistribution);
        }

        class DiffuseDenoiserPassData
        {
            // Camera parameters
            public int texWidth;
            public int texHeight;
            public int viewCount;

            // Denoising parameters
            public bool needInit;
            public float pixelSpreadTangent;
            public float kernelSize;
            public bool halfResolutionFilter;
            public bool jitterFilter;
            public int frameIndex;
            public float resolutionMultiplier;

            // Kernels
            public int bilateralFilterKernel;
            public int gatherKernel;

            // Other parameters
            public BufferHandle pointDistribution;
            public ComputeShader diffuseDenoiserCS;

            public TextureHandle owenScrambledTexture;
            public TextureHandle depthStencilBuffer;
            public TextureHandle normalBuffer;
            public TextureHandle noisyBuffer;
            public TextureHandle intermediateBuffer;
            public TextureHandle outputBuffer;
        }

        internal struct DiffuseDenoiserParameters
        {
            public bool singleChannel;
            public float kernelSize;
            public bool halfResolutionFilter;
            public bool jitterFilter;
            public float resolutionMultiplier;
        }

        public TextureHandle Denoise(RenderGraph renderGraph, UniversalCameraData cameraData, DiffuseDenoiserParameters denoiserParams,
            TextureHandle noisyBuffer, TextureHandle depthBuffer, TextureHandle normalBuffer, TextureHandle outputBuffer)
        {
            using (var builder = renderGraph.AddComputePass<DiffuseDenoiserPassData>("Spatial Denoiser", out var passData))
            {
                var camera = cameraData.camera;
                var histroyRT = HistoryFrameRTSystem.GetOrCreate(camera);


                builder.AllowPassCulling(false);
                // Initialization data
                passData.needInit = !m_DenoiserInitialized;
                m_DenoiserInitialized = true;
                
                passData.owenScrambledTexture = renderGraph.ImportTexture(m_OwnenScrambledTexture);
                
                // Camera parameters
                passData.texWidth = (int)Mathf.Floor(cameraData.scaledWidth / denoiserParams.resolutionMultiplier);
                passData.texHeight = (int)Mathf.Floor(cameraData.scaledHeight / denoiserParams.resolutionMultiplier);
                passData.viewCount = 1;
                
                // Parameters
                passData.pixelSpreadTangent = RenderingUtilsExt.GetPixelSpreadTangent(cameraData.camera.fieldOfView, passData.texWidth, passData.texHeight);
                passData.kernelSize = denoiserParams.kernelSize;
                passData.halfResolutionFilter = denoiserParams.halfResolutionFilter;
                passData.jitterFilter = denoiserParams.jitterFilter;
                passData.frameIndex = histroyRT.historyFrameCount;
                passData.resolutionMultiplier = denoiserParams.resolutionMultiplier;
                
                // Kernels
                passData.bilateralFilterKernel = denoiserParams.singleChannel ? m_BilateralFilterSingleKernel : m_BilateralFilterColorKernel;
                passData.gatherKernel = denoiserParams.singleChannel ? m_GatherSingleKernel : m_GatherColorKernel;
                
                // Other parameters
                passData.diffuseDenoiserCS = m_DiffuseDenoiser;
                
                passData.pointDistribution = (renderGraph.ImportBuffer(m_PointDistribution));
                passData.depthStencilBuffer = (depthBuffer);
                passData.normalBuffer = (normalBuffer);
                passData.noisyBuffer = (noisyBuffer);
                
                
                builder.UseBuffer(passData.pointDistribution);
                
                builder.UseTexture(passData.depthStencilBuffer);
                builder.UseTexture(passData.normalBuffer);
                builder.UseTexture(passData.noisyBuffer);
                builder.UseTexture(passData.owenScrambledTexture);
                
                passData.intermediateBuffer = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                    { format = GraphicsFormat.B10G11R11_UFloatPack32, enableRandomWrite = true, name = "DiffuseDenoiserIntermediate" });
                passData.outputBuffer = (outputBuffer);
                
                builder.UseTexture(passData.outputBuffer, AccessFlags.ReadWrite);
                builder.SetRenderFunc((DiffuseDenoiserPassData data, ComputeGraphContext ctx) =>
                {
                    // Generate the point distribution if needed (this is only ran once)
                    if (data.needInit)
                    {
                        int m_GeneratePointDistributionKernel = data.diffuseDenoiserCS.FindKernel("GeneratePointDistribution");
                        ctx.cmd.SetComputeTextureParam(data.diffuseDenoiserCS, m_GeneratePointDistributionKernel, _OwenScrambledRGTexture,
                            data.owenScrambledTexture);
                        ctx.cmd.SetComputeBufferParam(data.diffuseDenoiserCS, m_GeneratePointDistributionKernel, _PointDistributionRW,
                            data.pointDistribution);
                        ctx.cmd.DispatchCompute(data.diffuseDenoiserCS, m_GeneratePointDistributionKernel, 1, 1, 1);
                    }
                    
                    // Evaluate the dispatch parameters
                    int areaTileSize = 8;
                    int numTilesX = (data.texWidth + (areaTileSize - 1)) / areaTileSize;
                    int numTilesY = (data.texHeight + (areaTileSize - 1)) / areaTileSize;
                    
                    // Request the intermediate buffers that we need
                    ctx.cmd.SetComputeFloatParam(data.diffuseDenoiserCS, _DenoiserFilterRadius, data.kernelSize);
                    ctx.cmd.SetComputeBufferParam(data.diffuseDenoiserCS, data.bilateralFilterKernel, _PointDistribution, data.pointDistribution);
                    ctx.cmd.SetComputeTextureParam(data.diffuseDenoiserCS, data.bilateralFilterKernel, _DenoiseInputTexture, data.noisyBuffer);
                    ctx.cmd.SetComputeTextureParam(data.diffuseDenoiserCS, data.bilateralFilterKernel, _DepthTexture, data.depthStencilBuffer);

                    ctx.cmd.SetComputeTextureParam(data.diffuseDenoiserCS, data.bilateralFilterKernel, _NormalBufferTexture, data.normalBuffer);
                    ctx.cmd.SetComputeTextureParam(data.diffuseDenoiserCS, data.bilateralFilterKernel, _DenoiseOutputTextureRW,
                        data.halfResolutionFilter ? data.intermediateBuffer : data.outputBuffer);
                    ctx.cmd.SetComputeIntParam(data.diffuseDenoiserCS, _HalfResolutionFilter, data.halfResolutionFilter ? 1 : 0);
                    ctx.cmd.SetComputeFloatParam(data.diffuseDenoiserCS, _PixelSpreadAngleTangent, data.pixelSpreadTangent);
                    ctx.cmd.SetComputeVectorParam(data.diffuseDenoiserCS, _DenoiserResolutionMultiplierVals,
                        new Vector4(data.resolutionMultiplier, 1.0f / data.resolutionMultiplier, 0.0f, 0.0f));
                    if (data.jitterFilter)
                        ctx.cmd.SetComputeIntParam(data.diffuseDenoiserCS, _JitterFramePeriod, (data.frameIndex % 4));
                    else
                        ctx.cmd.SetComputeIntParam(data.diffuseDenoiserCS, _JitterFramePeriod, -1);
                    
                    ctx.cmd.DispatchCompute(data.diffuseDenoiserCS, data.bilateralFilterKernel, numTilesX, numTilesY, data.viewCount);
                    
                    if (data.halfResolutionFilter)
                    {
                        ctx.cmd.SetComputeTextureParam(data.diffuseDenoiserCS, data.gatherKernel, _DenoiseInputTexture, data.intermediateBuffer);
                        ctx.cmd.SetComputeTextureParam(data.diffuseDenoiserCS, data.gatherKernel, _DepthTexture, data.depthStencilBuffer);
                        ctx.cmd.SetComputeTextureParam(data.diffuseDenoiserCS, data.gatherKernel, _DenoiseOutputTextureRW, data.outputBuffer);
                        ctx.cmd.DispatchCompute(data.diffuseDenoiserCS, data.gatherKernel, numTilesX, numTilesY, data.viewCount);
                    }
                });
                return passData.outputBuffer;
            }
        }
    }
}