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

        int m_TemporalAccumulationSingleKernel;
        int m_TemporalAccumulationColorKernel;
        int m_CopyHistoryKernel;
        int m_ValidateHistoryKernel;


        private static int ColorBuffer = Shader.PropertyToID("ColorBuffer");
        private static int HistoryBuffer = Shader.PropertyToID("HistoryBuffer");
        private static int MotionBuffer = Shader.PropertyToID("MotionBuffer");
        private static int DepthBuffer = Shader.PropertyToID("DepthBuffer");
        private static int HistoryDepth = Shader.PropertyToID("HistoryDepth");

        private static int _DepthTexture = Shader.PropertyToID("_DepthTexture");
        private static int _NormalBufferTexture = Shader.PropertyToID("_NormalBufferTexture");

        private static int _HistoryDepthTexture = Shader.PropertyToID("_HistoryDepthTexture");
        private static int _HistoryNormalTexture = Shader.PropertyToID("_HistoryNormalTexture");
        private static int _CameraMotionVectorsTexture = Shader.PropertyToID("_CameraMotionVectorsTexture");
        private static int _PixelSpreadAngleTangent = Shader.PropertyToID("_PixelSpreadAngleTangent");
        private static int _ValidationBufferRW = Shader.PropertyToID("_ValidationBufferRW");

        private static int _DenoiseInputTexture = Shader.PropertyToID("_DenoiseInputTexture");
        private static int _HistoryBuffer = Shader.PropertyToID("_HistoryBuffer");
        private static int _ValidationBuffer = Shader.PropertyToID("_ValidationBuffer");
        private static int _VelocityBuffer = Shader.PropertyToID("_VelocityBuffer");
        private static int _AccumulationOutputTextureRW = Shader.PropertyToID("_AccumulationOutputTextureRW");
        private static int _DenoiseOutputTextureRW = Shader.PropertyToID("_DenoiseOutputTextureRW");
        private static int _DenoiserResolutionMultiplierVals = Shader.PropertyToID("_DenoiserResolutionMultiplierVals");
        private static int _ReceiverMotionRejection = Shader.PropertyToID("_ReceiverMotionRejection");
        private static int _OccluderMotionRejection = Shader.PropertyToID("_OccluderMotionRejection");
        private static  int _HistorySizeAndScale = Shader.PropertyToID("_HistorySizeAndScale");
        private static  int _HistoryValidity = Shader.PropertyToID("_HistoryValidity");

        
        public void Init()
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<DenoiserRuntimeShader>();


            // Keep track of the resources
            m_TemporalFilter = runtimeShaders.temporalFilterCS;

            m_ValidateHistoryKernel = m_TemporalFilter.FindKernel("ValidateHistory");
            m_TemporalAccumulationSingleKernel = m_TemporalFilter.FindKernel("TemporalAccumulationSingle");
            m_TemporalAccumulationColorKernel = m_TemporalFilter.FindKernel("TemporalAccumulationColor");
            m_CopyHistoryKernel = m_TemporalFilter.FindKernel("CopyHistory");

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
            public float historyValidity;
            public float pixelSpreadTangent;
            public bool occluderMotionRejection;
            public bool receiverMotionRejection;
            public float resolutionMultiplier;
            public float historyResolutionMultiplier;

            // Kernels
            public int temporalAccKernel;
            public int copyHistoryKernel;

            // Other parameters
            public ComputeShader temporalDenoiserCS;

            public TextureHandle depthStencilBuffer;
            public TextureHandle normalBuffer;
            public TextureHandle motionVectorBuffer;
            public TextureHandle velocityBuffer;
            public TextureHandle noisyBuffer;
            public TextureHandle validationBuffer;
            public TextureHandle historyBuffer;
            public TextureHandle outputBuffer;


            public VarianceEstimater VarianceEstimater;
            public VarianceEstimater.VarianceEstimaterParameter VarianceEstimaterParameter;
        }

        class HistoryValidityPassData
        {
            // Camera parameters
            public int texWidth;
            public int texHeight;
            public Vector4 historySizeAndScale;

            // Denoising parameters
            public float pixelSpreadTangent;

            // Kernels
            public int validateHistoryKernel;

            // Other parameters
            public ComputeShader temporalFilterCS;

            public TextureHandle depthStencilBuffer;
            public TextureHandle normalBuffer;
            public TextureHandle motionVectorBuffer;
            public TextureHandle historyDepthTexture;
            public TextureHandle historyNormalTexture;
            public TextureHandle validationBuffer;
            public TextureHandle debugTexture;
        }

        
        static RTHandle HistoryValidityBufferAllocatorFunction(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_ValidationTexture{1}", viewName, frameIndex));
        }

        
        // Function that evaluates the history validation Buffer
        public TextureHandle HistoryValidity(RenderGraph renderGraph, UniversalCameraData cameraData,
            TextureHandle normalBuffer,
            TextureHandle motionVectorBuffer,
            TextureHandle depthBuffer)
        {
            var camHistoryRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);

            using (var builder = renderGraph.AddComputePass<HistoryValidityPassData>("History Validity Evaluation", out var passData))
            {
                builder.AllowPassCulling(false);
                passData.texWidth = cameraData.scaledWidth;
                passData.texHeight = cameraData.scaledHeight;

                // Denoising parameters
                passData.pixelSpreadTangent =
                    RenderingUtilsExt.GetPixelSpreadTangent(cameraData.camera.fieldOfView, cameraData.scaledWidth, cameraData.scaledHeight);

                // Kernels
                passData.validateHistoryKernel = m_ValidateHistoryKernel;

                // Other parameters
                passData.temporalFilterCS = m_TemporalFilter;

                // Input Buffers
                passData.depthStencilBuffer = depthBuffer;
                passData.normalBuffer = normalBuffer;
                passData.motionVectorBuffer = motionVectorBuffer;
                passData.debugTexture = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R32G32B32A32_SFloat
                });

                // Grab and import the history buffers
                var historyDepth = camHistoryRTSystem.GetCurrentFrameRT(HistoryFrameType.Depth);
                var historyNormal = camHistoryRTSystem.GetCurrentFrameRT(HistoryFrameType.PrevNormalRoughness);
                passData.historyDepthTexture = renderGraph.ImportTexture(historyDepth);
                passData.historyNormalTexture = renderGraph.ImportTexture(historyNormal);

                passData.historySizeAndScale = (historyDepth != null && historyNormal != null)
                    ? RenderingUtilsExt.EvaluateRayTracingHistorySizeAndScale(historyDepth)
                    : Vector4.one;

                // Output buffers
                

                if (camHistoryRTSystem.GetCurrentFrameRT(HistoryFrameType.HistoryValidity) == null)
                {
                    camHistoryRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.HistoryValidity);
                    camHistoryRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.HistoryValidity, HistoryValidityBufferAllocatorFunction,
                        GraphicsFormat.R8_UInt, 1);
                }

                var historyValidityRT = camHistoryRTSystem.GetCurrentFrameRT(HistoryFrameType.HistoryValidity);
                passData.validationBuffer = renderGraph.ImportTexture(historyValidityRT);


                builder.UseTexture(passData.depthStencilBuffer);
                builder.UseTexture(passData.normalBuffer);
                builder.UseTexture(passData.motionVectorBuffer);
                builder.UseTexture(passData.historyDepthTexture);
                builder.UseTexture(passData.historyNormalTexture);


                builder.UseTexture(passData.validationBuffer, AccessFlags.ReadWrite);

                builder.SetRenderFunc((HistoryValidityPassData data, ComputeGraphContext ctx) =>
                {
                    // If we do not have a depth and normal history buffers, we can skip right away

                    // Evaluate the dispatch parameters
                    int areaTileSize = 8;
                    int numTilesX =RenderingUtilsExt.DivRoundUp(data.texWidth,areaTileSize) ;
                    int numTilesY =RenderingUtilsExt.DivRoundUp(data.texHeight,areaTileSize);

                    // First of all we need to validate the history to know where we can or cannot use the history signal
                    // Bind the input buffers
                    ctx.cmd.SetComputeTextureParam(data.temporalFilterCS, data.validateHistoryKernel, _DepthTexture, data.depthStencilBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalFilterCS, data.validateHistoryKernel, _HistoryDepthTexture,
                        data.historyDepthTexture);
                    ctx.cmd.SetComputeTextureParam(data.temporalFilterCS, data.validateHistoryKernel, _NormalBufferTexture, data.normalBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalFilterCS, data.validateHistoryKernel, _HistoryNormalTexture,
                        data.historyNormalTexture);
                    ctx.cmd.SetComputeTextureParam(data.temporalFilterCS, data.validateHistoryKernel, _CameraMotionVectorsTexture,
                        data.motionVectorBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalFilterCS, data.validateHistoryKernel,"_DebugTexture",
                        data.debugTexture);

                    // Bind the constants
                    ctx.cmd.SetComputeFloatParam(data.temporalFilterCS, _PixelSpreadAngleTangent, data.pixelSpreadTangent);
                    ctx.cmd.SetComputeVectorParam(data.temporalFilterCS, _HistorySizeAndScale, data.historySizeAndScale);
                    ctx.cmd.SetComputeFloatParam(data.temporalFilterCS, _HistoryValidity, 1);
                    ctx.cmd.SetComputeVectorParam(data.temporalFilterCS, _HistorySizeAndScale, data.historySizeAndScale);

                    // Bind the output buffer
                    ctx.cmd.SetComputeTextureParam(data.temporalFilterCS, data.validateHistoryKernel, _ValidationBufferRW, data.validationBuffer);

                    // Evaluate the validity
                    ctx.cmd.DispatchCompute(data.temporalFilterCS, data.validateHistoryKernel, numTilesX, numTilesY, 1);
                });
                return passData.validationBuffer;
            }
        }

        internal struct TemporalFilterParameters
        {
            public bool singleChannel;
            public float historyValidity;
            public bool occluderMotionRejection;
            public bool receiverMotionRejection;
            public bool exposureControl;
            public float resolutionMultiplier;
            public float historyResolutionMultiplier;
        }


        public TextureHandle Denoise(RenderGraph renderGraph, UniversalCameraData cameraData,
            TemporalFilterParameters filterParams,
            TextureHandle noisyBuffer,
            TextureHandle velocityBuffer,
            TextureHandle historyBuffer,
            TextureHandle depthBuffer,
            TextureHandle normalBuffer,
            TextureHandle motionVectorBuffer,
            TextureHandle historyValidationBuffer
        )
        {
            using (var builder = renderGraph.AddComputePass<TemporalDenoiserPassData>("Temporal Denoiser", out var passData))
            {
                var camera = cameraData.camera;
                // var histroyRT = HistoryFrameRTSystem.GetOrCreate(camera);


                builder.AllowPassCulling(false);
                // Initialization data
                m_DenoiserInitialized = true;


                // Camera parameters
                passData.texWidth = (int)Mathf.Floor((float)cameraData.scaledWidth * filterParams.resolutionMultiplier);
                passData.texHeight = (int)Mathf.Floor((float)cameraData.scaledHeight * filterParams.resolutionMultiplier);


                passData.pixelSpreadTangent = RenderingUtilsExt.GetPixelSpreadTangent(cameraData.camera.fieldOfView, passData.texWidth, passData.texHeight);
                passData.historyValidity = filterParams.historyValidity;
                passData.receiverMotionRejection = filterParams.receiverMotionRejection;
                passData.occluderMotionRejection = filterParams.occluderMotionRejection;
                passData.resolutionMultiplier = filterParams.resolutionMultiplier;
                passData.historyResolutionMultiplier = filterParams.historyResolutionMultiplier;


                passData.temporalAccKernel = filterParams.singleChannel ? m_TemporalAccumulationSingleKernel : m_TemporalAccumulationColorKernel;
                passData.copyHistoryKernel = m_CopyHistoryKernel;


                // Other parameters
                passData.temporalDenoiserCS = m_TemporalFilter;


                // Prepass Buffers
                passData.depthStencilBuffer = depthBuffer;
                passData.normalBuffer = normalBuffer;
                passData.motionVectorBuffer = motionVectorBuffer;

                // Effect buffers
                passData.velocityBuffer = velocityBuffer;
                passData.noisyBuffer = noisyBuffer;
                passData.validationBuffer = historyValidationBuffer;


                // History buffer
                passData.historyBuffer = (historyBuffer);

                // Output buffers
                passData.outputBuffer = (renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    format =  GraphicsFormat.R16G16B16A16_SFloat ,
                    enableRandomWrite = true, name = "Temporal Filter Output"
                }));


                var meanBuffer =
                    builder.CreateTransientBuffer(new BufferDesc(passData.texWidth * passData.texHeight, sizeof(float), GraphicsBuffer.Target.Structured));
                var squareBuffer =
                    builder.CreateTransientBuffer(new BufferDesc(passData.texWidth * passData.texHeight, sizeof(float), GraphicsBuffer.Target.Structured));
                var resultBuffer = builder.CreateTransientBuffer(new BufferDesc(1, sizeof(float), GraphicsBuffer.Target.Structured));


                passData.VarianceEstimater = VarianceEstimater.instance;
                passData.VarianceEstimaterParameter = new VarianceEstimater.VarianceEstimaterParameter()
                {
                    width = passData.texWidth,
                    height = passData.texHeight,
                    colorBuffer = noisyBuffer,
                    meanBuffer = meanBuffer,
                    squareBuffer = squareBuffer,
                    resultBuffer = resultBuffer,
                };


                builder.UseTexture(passData.depthStencilBuffer);
                builder.UseTexture(passData.normalBuffer);
                builder.UseTexture(passData.motionVectorBuffer);
                builder.UseTexture(passData.velocityBuffer);
                builder.UseTexture(passData.noisyBuffer);
                builder.UseTexture(passData.validationBuffer);


                builder.UseTexture(passData.historyBuffer, AccessFlags.ReadWrite);
                builder.UseTexture(passData.outputBuffer, AccessFlags.ReadWrite);

                builder.SetRenderFunc((TemporalDenoiserPassData data, ComputeGraphContext ctx) =>
                {
                     // data.VarianceEstimater.Estimate(ctx.cmd,passData.VarianceEstimaterParameter);
                    
                    
                    // Evaluate the dispatch parameters
                    int numTilesX = RenderingUtilsExt.DivRoundUp(data.texWidth, 8);
                    int numTilesY = RenderingUtilsExt.DivRoundUp(data.texHeight, 8);

                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalAccKernel, _DenoiseInputTexture, data.noisyBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalAccKernel, _HistoryBuffer, data.historyBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalAccKernel, _DepthTexture, data.depthStencilBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalAccKernel, _ValidationBuffer, data.validationBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalAccKernel, _VelocityBuffer, data.velocityBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalAccKernel, _CameraMotionVectorsTexture, data.motionVectorBuffer);
                    ctx.cmd.SetComputeFloatParam(data.temporalDenoiserCS, data.temporalAccKernel, data.historyValidity);
                    ctx.cmd.SetComputeIntParam(data.temporalDenoiserCS, _ReceiverMotionRejection, data.receiverMotionRejection ? 1 : 0);
                    ctx.cmd.SetComputeIntParam(data.temporalDenoiserCS, _OccluderMotionRejection, data.occluderMotionRejection ? 1 : 0);

                    ctx.cmd.SetComputeVectorParam(data.temporalDenoiserCS, _DenoiserResolutionMultiplierVals,
                        new Vector4(data.resolutionMultiplier, 1.0f / data.resolutionMultiplier, data.historyResolutionMultiplier,
                            1.0f / data.historyResolutionMultiplier));

                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.temporalAccKernel, _AccumulationOutputTextureRW, data.outputBuffer);

                    ctx.cmd.DispatchCompute(data.temporalDenoiserCS, data.temporalAccKernel, numTilesX, numTilesY, 1);
                    
                    // Make sure to copy the new-accumulated signal in our history buffer
                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.copyHistoryKernel, _DenoiseInputTexture, data.outputBuffer);
                    ctx.cmd.SetComputeTextureParam(data.temporalDenoiserCS, data.copyHistoryKernel, _DenoiseOutputTextureRW, data.historyBuffer);
                    ctx.cmd.DispatchCompute(data.temporalDenoiserCS, data.copyHistoryKernel, numTilesX, numTilesY, 1);

                });
                return passData.outputBuffer;
            }
        }
    }
}