using System;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class FidelityFXShadowDenoiser : IDisposable
    {
        ComputeShader m_PrepareShadowMask;
        ComputeShader m_TileClassification;
        ComputeShader m_FilterSoftShadows;


        public void Init(Camera camera)
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<DenoiserRuntimeShader>();

            m_PrepareShadowMask = runtimeShaders.fidelityFXPrepareShadowMask;
            m_TileClassification = runtimeShaders.fidelityFXTileClassification;
            m_FilterSoftShadows = runtimeShaders.fidelityFXFilterSoftShadows;
        }

        public void Dispose()
        {
        }

        class PassData
        {
            internal ComputeShader PrepareShadowMask;
            internal ComputeShader TileClassification;
            internal ComputeShader FilterSoftShadows;

            // Input textures
            internal TextureHandle depthTexture;
            internal TextureHandle velocityTexture;
            internal TextureHandle normalTexture;
            internal TextureHandle previousDepthTexture;
            internal TextureHandle raytracedShadowMask; // Texture2D<uint> at tile resolution
            internal TextureHandle previousHistoryTexture;
            internal TextureHandle previousMomentsTexture;

            internal TextureHandle debugTexture;
            internal TextureHandle debugTexture1;

            // PrepareShadowMask output
            internal BufferHandle TileData; // StructuredBuffer<uint> - one per tile

            // Transient textures
            internal TextureHandle prevReprojectionResults;
            internal TextureHandle transientBuffer;
            internal BufferHandle transientTileMetaData; // Buffer, not texture

            // Persistent history
            internal TextureHandle currReprojectionResults;
            internal TextureHandle persistMomentsTexture;

            // Output
            internal TextureHandle shadowTexture;

            internal int width, height;
            internal int tileWidth, tileHeight; // Tile dimensions

            // Constants
            internal float3 eye;
            internal int firstFrame;
            internal float4x4 projectionInverse;
            internal float4x4 reprojectionMatrix;
            internal float4x4 viewProjectionInverse;
            internal float depthSimilaritySigma;
        }

        // Shader property IDs
        static int gIn_Depth = Shader.PropertyToID("t2d_depth");
        static int gIn_Velocity = Shader.PropertyToID("t2d_velocity");
        static int gIn_Normal = Shader.PropertyToID("t2d_normal");
        static int gIn_PreviousDepth = Shader.PropertyToID("t2d_previousDepth");
        static int gIn_RaytracedShadowMask = Shader.PropertyToID("sb_raytracerResult");
        static int gIn_History = Shader.PropertyToID("t2d_history");
        static int gIn_PreviousMoments = Shader.PropertyToID("t2d_previousMoments");

        // PrepareShadowMask shader property IDs
        static int gIn_HitMaskResults = Shader.PropertyToID("t2d_hitMaskResults");
        static int gOut_ShadowMask = Shader.PropertyToID("rwsb_shadowMask");
        static int gPrepareShadowMask_PassData = Shader.PropertyToID("PassData");

        static int gOut_TileMetaData = Shader.PropertyToID("rwsb_tileMetaData");
        static int gOut_ReprojectionResults = Shader.PropertyToID("rwt2d_reprojectionResults");
        static int gOut_MomentsBuffer = Shader.PropertyToID("rwt2d_momentsBuffer");
        static int gOut_History = Shader.PropertyToID("rwt2d_history");
        static int gOut_Output = Shader.PropertyToID("rwt2d_output");

        // FilterSoftShadows shader property IDs
        static int gFilter_DepthBuffer = Shader.PropertyToID("t2d_DepthBuffer");
        static int gFilter_NormalBuffer = Shader.PropertyToID("t2d_NormalBuffer");
        static int gFilter_TileMetaData = Shader.PropertyToID("sb_tileMetaData");
        static int gFilter_Input = Shader.PropertyToID("rqt2d_input");
        static int gFilter_History = Shader.PropertyToID("rwt2d_history");
        static int gFilter_PassData = Shader.PropertyToID("cbPassData");

        static int FFX_DNSR_Shadows_Data = Shader.PropertyToID("FFX_DNSR_Shadows_Data");

        struct PrepareShadowMaskConstants
        {
            public int2 BufferDimensions;
        };

        struct TileClassificationConstants
        {
            public float3 Eye;
            public int FirstFrame;
            public int2 BufferDimensions;
            public float2 InvBufferDimensions;
            public float4x4 ProjectionInverse;
            public float4x4 ReprojectionMatrix;
            public float4x4 ViewProjectionInverse;
        }

        struct FilterSoftShadowsConstants
        {
            public float4x4 ProjectionInverse;
            public int2 BufferDimensions;
            public float2 InvBufferDimensions;
            public float DepthSimilaritySigma;
        }


        internal bool ReAllocatedShadowTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle prevFrameRT, out RTHandle currFrameRT)
        {
            static RTHandle HistoryCaptureBufferAllocatorFunction(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                frameIndex &= 1;

                return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                    enableRandomWrite: true, useDynamicScale: true,
                    name: string.Format("{0}_FidelityFX_ReprojectionResults{1}", viewName, frameIndex));
            }

            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.FidelityFXShadowResult);
            bool vaild = true;

            if (curTexture == null)
            {
                vaild = false;

                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.FidelityFXShadowResult);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.FidelityFXShadowResult,
                    HistoryCaptureBufferAllocatorFunction, GraphicsFormat.R16G16_SFloat, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.FidelityFXShadowResult);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.FidelityFXShadowResult);

            return vaild;
        }


        internal bool ReAllocatedShadowMomentTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle prevFrameRT, out RTHandle currFrameRT)
        {
            static RTHandle HistoryCaptureBufferAllocatorFunction(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                frameIndex &= 1;

                return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                    enableRandomWrite: true, useDynamicScale: true,
                    name: string.Format("{0}_ShadowMoment_{1}", viewName, frameIndex));
            }

            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.FidelityFXShadowMoment);
            bool vaild = true;

            if (curTexture == null)
            {
                vaild = false;

                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.FidelityFXShadowMoment);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.FidelityFXShadowMoment,
                    HistoryCaptureBufferAllocatorFunction, GraphicsFormat.B10G11R11_UFloatPack32, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.FidelityFXShadowMoment);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.FidelityFXShadowMoment);

            return vaild;
        }


        static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            // PrepareShadowMask pass
            if (data.PrepareShadowMask != null)
            {
                int prepareKernel = data.PrepareShadowMask.FindKernel("main");
                if (prepareKernel >= 0)
                {
                    // Set constant buffer
                    var prepareConstants = new PrepareShadowMaskConstants
                    {
                        BufferDimensions = new int2(data.width, data.height)
                    };
                    ConstantBuffer.Push(cmd, prepareConstants, data.PrepareShadowMask, gPrepareShadowMask_PassData);

                    // Set input texture (hit mask results from raytracing)
                    cmd.SetComputeTextureParam(data.PrepareShadowMask, prepareKernel, gIn_HitMaskResults, data.raytracedShadowMask);

                    // Set output buffer
                    cmd.SetComputeBufferParam(data.PrepareShadowMask, prepareKernel, gOut_ShadowMask, data.TileData);

                    cmd.SetComputeTextureParam(data.PrepareShadowMask, prepareKernel, "debugTexture", data.debugTexture1);
                    // Dispatch: The shader processes 4x4 tiles at once (gid *= 4 in the shader)
                    // So we dispatch (tileWidth/4, tileHeight/4, 1)
                    int dispatchX = CoreUtils.DivRoundUp(data.tileWidth, 4);
                    int dispatchY = CoreUtils.DivRoundUp(data.tileHeight, 4);
                    cmd.DispatchCompute(data.PrepareShadowMask, prepareKernel, dispatchX, dispatchY, 1);
                }
            }

            // TileClassification pass
            if (data.TileClassification != null)
            {
                int tileClassifyKernel = data.TileClassification.FindKernel("main");
                if (tileClassifyKernel >= 0)
                {
                    // Set constant buffer
                    var tileClassifyConstants = new TileClassificationConstants
                    {
                        Eye = data.eye,
                        FirstFrame = data.firstFrame,
                        BufferDimensions = new int2(data.width, data.height),
                        InvBufferDimensions = new float2(1.0f / data.width, 1.0f / data.height),
                        ProjectionInverse = data.projectionInverse,
                        ReprojectionMatrix = data.reprojectionMatrix,
                        ViewProjectionInverse = data.viewProjectionInverse
                    };
                    ConstantBuffer.Push(cmd, tileClassifyConstants, data.TileClassification, FFX_DNSR_Shadows_Data);

                    // Set input textures
                    cmd.SetComputeTextureParam(data.TileClassification, tileClassifyKernel, gIn_Depth, data.depthTexture);
                    cmd.SetComputeTextureParam(data.TileClassification, tileClassifyKernel, gIn_Velocity, data.velocityTexture);
                    cmd.SetComputeTextureParam(data.TileClassification, tileClassifyKernel, gIn_Normal, data.normalTexture);
                    cmd.SetComputeTextureParam(data.TileClassification, tileClassifyKernel, gIn_PreviousDepth, data.previousDepthTexture);

                    // Set history texture (reprojection results from previous frame)
                    // For first frame, use default texture; otherwise use previous frame's reprojection results
                    // Note: We need to store reprojection results from previous frame - for now using default
                    cmd.SetComputeTextureParam(data.TileClassification, tileClassifyKernel, gIn_History, data.prevReprojectionResults);

                    // Set shadow mask buffer (output from PrepareShadowMask)
                    cmd.SetComputeBufferParam(data.TileClassification, tileClassifyKernel, gIn_RaytracedShadowMask, data.TileData);

                    // Set previous moments texture (in space1)
                    cmd.SetComputeTextureParam(data.TileClassification, tileClassifyKernel, gIn_PreviousMoments, data.previousMomentsTexture);

                    cmd.SetComputeTextureParam(data.TileClassification, tileClassifyKernel, "debugTexture", data.debugTexture);

                    cmd.SetComputeTextureParam(data.TileClassification, tileClassifyKernel, "sb_raytracerResult_raw", data.raytracedShadowMask);
                    // Set output resources
                    cmd.SetComputeBufferParam(data.TileClassification, tileClassifyKernel, gOut_TileMetaData, data.transientTileMetaData);
                    cmd.SetComputeTextureParam(data.TileClassification, tileClassifyKernel, gOut_ReprojectionResults, data.currReprojectionResults);
                    // Moments buffer is written to the persistent texture (current frame)
                    cmd.SetComputeTextureParam(data.TileClassification, tileClassifyKernel, gOut_MomentsBuffer, data.persistMomentsTexture);

                    // Dispatch: 8x8 thread groups
                    int dispatchX = CoreUtils.DivRoundUp(data.width, 8);
                    int dispatchY = CoreUtils.DivRoundUp(data.height, 8);
                    cmd.DispatchCompute(data.TileClassification, tileClassifyKernel, dispatchX, dispatchY, 1);
                }
            }

            // FilterSoftShadows Pass 0 (step size = 1)
            if (data.FilterSoftShadows != null)
            {
                int pass0Kernel = data.FilterSoftShadows.FindKernel("Pass0");
                if (pass0Kernel >= 0)
                {
                    // Set constant buffer
                    var filterConstants = new FilterSoftShadowsConstants
                    {
                        ProjectionInverse = data.projectionInverse,
                        BufferDimensions = new int2(data.width, data.height),
                        InvBufferDimensions = new float2(1.0f / data.width, 1.0f / data.height),
                        DepthSimilaritySigma = data.depthSimilaritySigma
                    };
                    ConstantBuffer.Push(cmd, filterConstants, data.FilterSoftShadows, gFilter_PassData);
            
                    // Set input textures
                    cmd.SetComputeTextureParam(data.FilterSoftShadows, pass0Kernel, gFilter_DepthBuffer, data.depthTexture);
                    cmd.SetComputeTextureParam(data.FilterSoftShadows, pass0Kernel, gFilter_NormalBuffer, data.normalTexture);
                    cmd.SetComputeBufferParam(data.FilterSoftShadows, pass0Kernel, gFilter_TileMetaData, data.transientTileMetaData);
                    // Input is reprojection results from TileClassification 
                    cmd.SetComputeTextureParam(data.FilterSoftShadows, pass0Kernel, gFilter_Input, data.currReprojectionResults);
            
                    // Output to history buffer (read-write, space0) - same texture, different register
                    // In Unity, we bind the same texture handle to both registers
                    cmd.SetComputeTextureParam(data.FilterSoftShadows, pass0Kernel, gFilter_History, data.transientBuffer);
            
                    // Dispatch: 8x8 thread groups
                    int dispatchX = CoreUtils.DivRoundUp(data.width, 8);
                    int dispatchY = CoreUtils.DivRoundUp(data.height, 8);
                    cmd.DispatchCompute(data.FilterSoftShadows, pass0Kernel, dispatchX, dispatchY, 1);
                }
            }
            
            // FilterSoftShadows Pass 1 (step size = 2)
            if (data.FilterSoftShadows != null)
            {
                int pass1Kernel = data.FilterSoftShadows.FindKernel("Pass1");
                if (pass1Kernel >= 0)
                {
                    // Set constant buffer (same as Pass0)
                    var filterConstants = new FilterSoftShadowsConstants
                    {
                        ProjectionInverse = data.projectionInverse,
                        BufferDimensions = new int2(data.width, data.height),
                        InvBufferDimensions = new float2(1.0f / data.width, 1.0f / data.height),
                        DepthSimilaritySigma = data.depthSimilaritySigma
                    };
                    ConstantBuffer.Push(cmd, filterConstants, data.FilterSoftShadows, gFilter_PassData);
            
                    // Set input textures
                    cmd.SetComputeTextureParam(data.FilterSoftShadows, pass1Kernel, gFilter_DepthBuffer, data.depthTexture);
                    cmd.SetComputeTextureParam(data.FilterSoftShadows, pass1Kernel, gFilter_NormalBuffer, data.normalTexture);
                    cmd.SetComputeBufferParam(data.FilterSoftShadows, pass1Kernel, gFilter_TileMetaData, data.transientTileMetaData);
                    // Input is history from Pass0 (read-only, space1)
                    cmd.SetComputeTextureParam(data.FilterSoftShadows, pass1Kernel, gFilter_Input, data.transientBuffer);
            
                    // Output to history buffer (read-write, space0) - same texture, different register
                    cmd.SetComputeTextureParam(data.FilterSoftShadows, pass1Kernel, gFilter_History, data.currReprojectionResults);
            
                    // Dispatch: 8x8 thread groups
                    int dispatchX = CoreUtils.DivRoundUp(data.width, 8);
                    int dispatchY = CoreUtils.DivRoundUp(data.height, 8);
                    cmd.DispatchCompute(data.FilterSoftShadows, pass1Kernel, dispatchX, dispatchY, 1);
                }
            }
            
            // FilterSoftShadows Pass 2 (step size = 4)
            if (data.FilterSoftShadows != null)
            {
                int pass2Kernel = data.FilterSoftShadows.FindKernel("Pass2");
                if (pass2Kernel >= 0)
                {
                    // Set constant buffer (same as previous passes)
                    var filterConstants = new FilterSoftShadowsConstants
                    {
                        ProjectionInverse = data.projectionInverse,
                        BufferDimensions = new int2(data.width, data.height),
                        InvBufferDimensions = new float2(1.0f / data.width, 1.0f / data.height),
                        DepthSimilaritySigma = data.depthSimilaritySigma
                    };
                    ConstantBuffer.Push(cmd, filterConstants, data.FilterSoftShadows, gFilter_PassData);
            
                    // Set input textures
                    cmd.SetComputeTextureParam(data.FilterSoftShadows, pass2Kernel, gFilter_DepthBuffer, data.depthTexture);
                    cmd.SetComputeTextureParam(data.FilterSoftShadows, pass2Kernel, gFilter_NormalBuffer, data.normalTexture);
                    cmd.SetComputeBufferParam(data.FilterSoftShadows, pass2Kernel, gFilter_TileMetaData, data.transientTileMetaData);
                    // Input is history from Pass1 (read-only, space1)
                    cmd.SetComputeTextureParam(data.FilterSoftShadows, pass2Kernel, gFilter_Input, data.currReprojectionResults);
            
                    // Output to final shadow texture (with contrast remapping applied)
                    cmd.SetComputeTextureParam(data.FilterSoftShadows, pass2Kernel, gOut_Output, data.shadowTexture);
            
                    // Dispatch: 8x8 thread groups
                    int dispatchX = CoreUtils.DivRoundUp(data.width, 8);
                    int dispatchY = CoreUtils.DivRoundUp(data.height, 8);
                    cmd.DispatchCompute(data.FilterSoftShadows, pass2Kernel, dispatchX, dispatchY, 1);
                }
            }
        }

        /// <summary>
        /// FidelityFX Shadow Denoiser
        /// </summary>
        /// <param name="renderGraph"></param>
        /// <param name="frameData"></param>
        /// <param name="depthTexture"></param>
        /// <param name="velocityTexture"></param>
        /// <param name="normalTexture"></param>
        /// <param name="previousDepthTexture"></param>
        /// <param name="raytracedShadowMask">StructuredBuffer containing raytraced shadow results</param>
        /// <param name="shadowTexture">Output denoised shadow texture</param>
        /// <returns></returns>
        public TextureHandle Denoise(RenderGraph renderGraph,
            ContextContainer frameData,
            TextureHandle depthTexture,
            TextureHandle velocityTexture,
            TextureHandle normalTexture,
            TextureHandle raytracedShadowMask
        )
        {
            var result = TextureHandle.nullHandle;
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var historyRTSystem = cameraData.historyFrameRTSystem;

            var prevDepth = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.Depth);


            bool firstFrame = ReAllocatedShadowMomentTextureIfNeeded(historyRTSystem, out var prevMoment, out var currMoment);

            firstFrame &= ReAllocatedShadowTextureIfNeeded(historyRTSystem, out var prevResult, out var currResult);

            // Tile metadata buffer: (width/8) * (height/4) tiles, each tile = 1 uint
            int tileWidth = CoreUtils.DivRoundUp(cameraData.scaledWidth, 8);
            int tileHeight = CoreUtils.DivRoundUp(cameraData.scaledHeight, 4);

            using (var builder = renderGraph.AddComputePass<PassData>("FidelityFX Shadow Denoiser", out var data))
            {
                var cameraExt = cameraData.cameraExtension;

                data.PrepareShadowMask = m_PrepareShadowMask;
                data.TileClassification = m_TileClassification;
                data.FilterSoftShadows = m_FilterSoftShadows;

                data.depthTexture = depthTexture;
                data.velocityTexture = velocityTexture;
                data.normalTexture = normalTexture;
                data.previousDepthTexture = prevDepth is null ? renderGraph.defaultResources.blackTexture : renderGraph.ImportTexture(prevDepth);
                data.raytracedShadowMask = raytracedShadowMask;
                // data.previousHistoryTexture = previousHistoryTexture;
                // data.previousMomentsTexture = previousMomentsTexture;

                // Setup matrices
                data.eye = cameraExt.camera.transform.position;
                data.firstFrame = firstFrame ? 1 : 0;
                data.projectionInverse = cameraExt.gpuProjectionMatrix.inverse;
                data.reprojectionMatrix = cameraExt.gpuProjectionMatrix * cameraExt.previousViewMatrix * cameraExt.gpuViewProjectionMatrix.inverse;
                data.viewProjectionInverse = (cameraExt.gpuViewProjectionMatrix).inverse;
                data.depthSimilaritySigma = 0.01f; // TODO: Make this configurable

                // Create transient textures

                data.transientBuffer = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    format = GraphicsFormat.R16G16_SFloat,
                    name = "FidelityFX Temp",
                    enableRandomWrite = true,
                });

                int tileWidthTransient = CoreUtils.DivRoundUp(cameraData.scaledWidth, 8);
                int tileHeightTransient = CoreUtils.DivRoundUp(cameraData.scaledHeight, 4);
                data.tileWidth = tileWidthTransient;
                data.tileHeight = tileHeightTransient;

                // Create tile metadata buffer (one uint per tile)
                int totalTiles = tileWidthTransient * tileHeightTransient;
                data.transientTileMetaData = builder.CreateTransientBuffer(new BufferDesc(totalTiles, 4) // uint per tile
                {
                    name = "FidelityFX TileMetaData"
                });

                // Create shadow mask buffer for PrepareShadowMask pass
                // One uint per tile
                totalTiles = tileWidthTransient * tileHeightTransient;
                data.TileData = builder.CreateTransientBuffer(new BufferDesc(totalTiles, sizeof(int)) // uint per tile
                {
                    name = "FidelityFX Shadow Tile Data"
                });

                // Create output texture
                data.shadowTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight)
                {
                    format = GraphicsFormat.R8_UNorm,
                    enableRandomWrite = true,
                    name = "FidelityFX Shadow Output",
                });

                // Import persistent history
                // Get previous frame's reprojection results (if available)
                // For now, we'll use a default texture for first frame
                data.prevReprojectionResults = renderGraph.ImportTexture(prevResult);
                data.currReprojectionResults = renderGraph.ImportTexture(currResult);

                data.debugTexture = builder.CreateTransientTexture(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight)
                {
                    format = GraphicsFormat.R8G8B8A8_UNorm,
                    enableRandomWrite = true,
                    name = "FidelityFX Shadow Debug",
                });
                
                data.debugTexture1 = builder.CreateTransientTexture(new TextureDesc(tileWidth, tileHeight)
                {
                    format = GraphicsFormat.R32_UInt,
                    enableRandomWrite = true,
                    name = "FidelityFX Shadow Debug 1",
                });



                data.previousMomentsTexture = renderGraph.ImportTexture(prevMoment);
                data.persistMomentsTexture = renderGraph.ImportTexture(currMoment);


                data.width = cameraData.actualWidth;
                data.height = cameraData.actualHeight;

                // Use textures
                builder.UseTexture(data.depthTexture);
                builder.UseTexture(data.velocityTexture);
                builder.UseTexture(data.normalTexture);
                builder.UseTexture(data.previousDepthTexture);
                builder.UseTexture(data.raytracedShadowMask);
                // builder.UseTexture(data.previousHistoryTexture);
                builder.UseTexture(data.previousMomentsTexture);
                builder.UseTexture(data.persistMomentsTexture);
                builder.UseTexture(data.prevReprojectionResults, AccessFlags.ReadWrite);
                builder.UseTexture(data.currReprojectionResults, AccessFlags.ReadWrite);

                // Use PrepareShadowMask buffer
                builder.UseBuffer(data.TileData, AccessFlags.Write);

                // Use TileClassification resources
                builder.UseBuffer(data.transientTileMetaData, AccessFlags.Write);
                builder.UseTexture(data.persistMomentsTexture, AccessFlags.Write);

                // builder.UseTexture(data.previousHistoryTexture);

                // FilterSoftShadows will read-write transientReprojectionResults (Pass0 and Pass1)
                // and write to shadowTexture (Pass2)
                builder.UseTexture(data.shadowTexture, AccessFlags.Write);

                builder.AllowPassCulling(false);
                builder.SetRenderFunc<PassData>(ExecutePass);
                result = data.shadowTexture;
            }

            return result;
        }
    }
}