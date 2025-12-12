using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class AmbientOcclusionDenoiser
    {
        ComputeShader m_ClassifyTiles;
        ComputeShader m_HitDistReconstruction;
        ComputeShader m_TemporalAccumulation;
        ComputeShader m_Blur;
        ComputeShader m_PostBlur;

        ComputeShader m_HistoryFix;

        ComputeShader m_SplitScreen;


        IntPtr NRDContext;

        public void Init(Camera camera)
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<DenoiserRuntimeShader>();

            m_ClassifyTiles = runtimeShaders.REBLURClassifyTiles;
            m_HitDistReconstruction = runtimeShaders.REBLURHitDistReconstruction;
            m_TemporalAccumulation = runtimeShaders.REBLURTemporalAccumulation;
            m_HistoryFix = runtimeShaders.REBLURHistoryFix;
            m_Blur = runtimeShaders.REBLURBlur;
            m_PostBlur = runtimeShaders.REBLURPostBlur;
            m_SplitScreen = runtimeShaders.REBLURSplitScreen;

            NRDContext = NRDInitlizer.NRD_GetContext();
        }


        public void Dispose()
        {
            NRDInitlizer.NRD_ReleaseContext(NRDContext);
        }

        #region Util

        internal bool ReAllocatedInternalDataTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle currFrameRT)
        {
            static RTHandle HistoryCaptureBufferAllocatorFunction(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                frameIndex &= 1;

                return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                    enableRandomWrite: true, useDynamicScale: true,
                    name: string.Format("{0}_NRD_REBLUR_InternalData{1}", viewName, frameIndex));
            }

            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURPrevInternalData);
            bool vaild = true;

            if (curTexture == null)
            {
                vaild = false;

                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.REBLURPrevInternalData);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.REBLURPrevInternalData,
                    HistoryCaptureBufferAllocatorFunction, GraphicsFormat.R8_UNorm, 1);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURPrevInternalData);
            return vaild;
        }


        internal bool ReAllocatedAODiffuseTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle prevFrameRT)
        {
            static RTHandle HistoryCaptureBufferAllocatorFunction(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                frameIndex &= 1;

                return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                    enableRandomWrite: true, useDynamicScale: true,
                    name: string.Format("{0}_NRD_REBLUR_AODiffuse{1}", viewName, frameIndex));
            }

            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURAmbientOcclusionDiffuse);
            bool vaild = true;

            if (curTexture == null)
            {
                vaild = false;

                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.REBLURAmbientOcclusionDiffuse);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.REBLURAmbientOcclusionDiffuse,
                    HistoryCaptureBufferAllocatorFunction, GraphicsFormat.R8_UNorm, 2);
            }

            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.REBLURAmbientOcclusionDiffuse);
            return vaild;
        }

        internal bool ReAllocatedDiffuseFastHistoryTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle prevFrameRT, out RTHandle currFrameRT)
        {
            static RTHandle HistoryCaptureBufferAllocatorFunction(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                frameIndex &= 1;

                return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                    enableRandomWrite: true, useDynamicScale: true,
                    name: string.Format("{0}_NRD_REBLUR_DiffuseFast{1}", viewName, frameIndex));
            }

            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURAmbientOcclusionDiffuseFast);
            bool vaild = true;

            if (curTexture == null)
            {
                vaild = false;

                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.REBLURAmbientOcclusionDiffuseFast);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.REBLURAmbientOcclusionDiffuseFast,
                    HistoryCaptureBufferAllocatorFunction, GraphicsFormat.R8_UNorm, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURAmbientOcclusionDiffuseFast);
            prevFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURAmbientOcclusionDiffuse);
            return vaild;
        }
        
        #endregion


        class PassData
        {
            internal ComputeShader ClassifyTiles;
            internal ComputeShader HitDistReconstruction;
            internal ComputeShader TemporalAccumulation;
            internal ComputeShader HistoryFix;
            internal ComputeShader Blur;

            internal ComputeShader PostBlur;
            internal ComputeShader SplitScreen;


            internal TextureHandle MotionTexture;
            internal TextureHandle CurrNormalRoughnessTexture;
            internal TextureHandle PrevNormalRoughnessTexture;
            internal TextureHandle CurrViewZTexture;
            internal TextureHandle PrevViewZTexture;
            internal TextureHandle DummyTexture;


            internal TextureHandle PrevInternalDataTexture;
            internal TextureHandle PrevAODiffuseFastTexture;
            internal TextureHandle CurrAODiffuseFastTexture;
            internal TextureHandle PrevAODiffuseTexture;
            // internal TextureHandle CurrAODiffuseTexture;

            
            //internal resource
            internal TextureHandle TileTexture;


            //transisent
            internal TextureHandle TempTexture1;
            internal TextureHandle TempTexture2;
            internal TextureHandle TempDataTexture;

            internal TextureHandle DebugTexture;


            internal TextureHandle UnfilteredAOTexture;

            internal TextureHandle OutputAOTexture;


            internal TextureHandle InternalDataTexture;


            internal int width, height;

            internal ReblurSharedConstants ReblurSharedConstants;

            internal ReblurSettings Settings;



        }


        static int gIn_ViewZ = Shader.PropertyToID("gIn_ViewZ");
        static int gIn_Tiles = Shader.PropertyToID("gIn_Tiles");
        static int gOut_Tiles = Shader.PropertyToID("gOut_Tiles");
        static int gIn_Normal_Roughness = Shader.PropertyToID("gIn_Normal_Roughness");
        static int gIn_Mv = Shader.PropertyToID("gIn_Mv");
        static int gIn_Diff = Shader.PropertyToID("gIn_Diff");
        static int gOut_Diff = Shader.PropertyToID("gOut_Diff");
        static int gPrev_ViewZ = Shader.PropertyToID("gPrev_ViewZ");
        static int gPrev_Normal_Roughness = Shader.PropertyToID("gPrev_Normal_Roughness");
        static int gIn_DisocclusionThresholdMix = Shader.PropertyToID("gIn_DisocclusionThresholdMix");
        static int gIn_DiffConfidence = Shader.PropertyToID("gIn_DiffConfidence");
        static int gHistory_Diff = Shader.PropertyToID("gHistory_Diff");
        static int gHistory_DiffFast = Shader.PropertyToID("gHistory_DiffFast");
        static int gPrev_InternalData = Shader.PropertyToID("gPrev_InternalData");
        static int REBLUR_ClassifyTilesConstants = Shader.PropertyToID("REBLUR_ClassifyTilesConstants");
        static int REBLUR_HitDistReconstructionConstants = Shader.PropertyToID("REBLUR_HitDistReconstructionConstants");
        static int REBLUR_TemporalAccumulationConstants = Shader.PropertyToID("REBLUR_TemporalAccumulationConstants");
        static int gOut_Normal_Roughness = Shader.PropertyToID("gOut_Normal_Roughness");
        static int REBLUR_HistoryFixConstants = Shader.PropertyToID("REBLUR_HistoryFixConstants");
        static int gOut_ViewZ = Shader.PropertyToID("gOut_ViewZ");
        static int gOut_InternalData = Shader.PropertyToID("gOut_InternalData");
        static int REBLUR_BlurConstants = Shader.PropertyToID("REBLUR_BlurConstants");
        static int REBLUR_SplitScreenConstants = Shader.PropertyToID("REBLUR_SplitScreenConstants");
        static int REBLUR_PostBlurConstants = Shader.PropertyToID("REBLUR_PostBlurConstants");
        static int gOut_DiffCopy = Shader.PropertyToID("gOut_DiffCopy");
        static int gIn_DiffFast = Shader.PropertyToID("gIn_DiffFast");

        static int gOut_DiffFast = Shader.PropertyToID("gOut_DiffFast");
        static int gOut_Data1 = Shader.PropertyToID("gOut_Data1");
        static int gIn_Data1 = Shader.PropertyToID("gIn_Data1");

        static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;
            var cs = data.ClassifyTiles;
            var kernel = 0;
            
            

            //spec for AO denoise

            var tx = CoreUtils.DivRoundUp(data.width, 16);
            var ty = CoreUtils.DivRoundUp(data.height, 16);
            // CLASSIFY_TILES
            {
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_ClassifyTilesConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Tiles, data.TileTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }


            //HITDIST_RECONSTRUCTION

            tx = CoreUtils.DivRoundUp(data.width, 8);
            ty = CoreUtils.DivRoundUp(data.width, 16);
            CoreUtils.SetKeyword(cmd, "_NRD_OCCLUSION", true);
            
            // cs = data.HitDistReconstruction;
            // {
            //     bool is5X5 = data.Settings.hitDistanceReconstructionMode == HitDistanceReconstructionMode.AREA_5X5;
            //
            //     // CoreUtils.SetKeyword(cmd, "_MODE_5X5", true);
            //     ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_HitDistReconstructionConstants);
            //     cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
            //     cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
            //     cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
            //     cmd.SetComputeTextureParam(cs, kernel, gIn_Diff, data.AOTexture);
            //
            //     cmd.SetComputeTextureParam(cs, kernel, gOut_Diff, data.TempTexture1);
            //
            //     cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            // }
            //

            cs = data.TemporalAccumulation;
            {
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_TemporalAccumulationConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Mv, data.MotionTexture);
                cmd.SetComputeTextureParam(cs, kernel, gPrev_ViewZ, data.PrevViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gPrev_Normal_Roughness, data.PrevNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gPrev_InternalData, data.PrevInternalDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_DisocclusionThresholdMix, data.DummyTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_DiffConfidence, data.DummyTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Diff, data.UnfilteredAOTexture);
                cmd.SetComputeTextureParam(cs, kernel, gHistory_Diff, data.PrevAODiffuseTexture);
                cmd.SetComputeTextureParam(cs, kernel, gHistory_DiffFast, data.PrevAODiffuseTexture);


                cmd.SetComputeTextureParam(cs, kernel, gOut_Data1, data.TempDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Diff, data.TempTexture2);
                cmd.SetComputeTextureParam(cs, kernel, gOut_DiffFast, data.CurrAODiffuseFastTexture);


                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }


            cs = data.HistoryFix;
            {
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_HistoryFixConstants);

                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Data1, data.TempDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Diff, data.TempTexture2);
                cmd.SetComputeTextureParam(cs, kernel, gIn_DiffFast, data.CurrAODiffuseFastTexture);


                cmd.SetComputeTextureParam(cs, kernel, gOut_Diff, data.TempTexture1);
                cmd.SetComputeTextureParam(cs, kernel, gOut_DiffFast, data.PrevAODiffuseTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            
            
            cs = data.Blur;
            {
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_BlurConstants);

                
                
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Data1, data.TempDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Diff, data.TempTexture1);

                    
                cmd.SetComputeTextureParam(cs, kernel, gOut_ViewZ, data.PrevViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Diff, data.TempTexture2);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }


            cs = data.PostBlur;
            
            {
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_PostBlurConstants);
            
                // Inputs
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Data1, data.TempDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.PrevViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Diff, data.TempTexture2);
            
            
                // Outputs
                cmd.SetComputeTextureParam(cs, kernel, gOut_Normal_Roughness, data.PrevNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Diff, data.OutputAOTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_InternalData, data.InternalDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_DiffCopy, data.PrevAODiffuseTexture);
            
                
                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            
            }



            cs = data.SplitScreen;

            if (data.ReblurSharedConstants.gSplitScreen > 0)
            {
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_SplitScreenConstants);

                // Inputs
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Diff, data.UnfilteredAOTexture);

                cmd.SetComputeTextureParam(cs, kernel, gOut_Diff, data.OutputAOTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }
        }


        /// <summary>
        /// NRD SIGMA Denoise
        /// </summary>
        /// <param name="renderGraph"></param>
        /// <param name="frameData"></param>
        /// <param name="motionTexture"></param>
        /// <param name="gBufferNormalRoughnessTexture"></param>
        /// <param name="viewZTexture"></param>
        /// <param name="UnfilteredPenumbraTexture"></param>
        /// <param name="UnfilteredTranslucencyTexture">Not support yet</param>
        /// <param name="ShadowTexture">out</param>
        /// <param name="aoTexture"></param>
        /// <returns></returns>
        public TextureHandle Denoise(RenderGraph renderGraph,
            ContextContainer frameData,
            TextureHandle motionTexture,
            TextureHandle gBufferNormalRoughnessTexture,
            TextureHandle viewZTexture,
            TextureHandle aoTexture
        )
        {
            var result = TextureHandle.nullHandle;
            var cameraData = frameData.Get<UniversalCameraData>();

            var aoSetting = VolumeManager.instance.stack.GetComponent<AmbientOcclusion>();

            var historyRT = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);

            using (var builder = renderGraph.AddComputePass<PassData>("NRD REBLUR DiffuseOcclusion Denoiser", out var data))
            {
                NRDCommonSettings commonSettings = NRDCommonSettings.Default();


                var cameraExt = cameraData.cameraExtension;

                var view = cameraExt.previousViewMatrix;
                var viewPrev = cameraExt.previousViewMatrix;

                var gpuProj = cameraExt.gpuProjectionMatrix;
                var gpuProjPrev = cameraExt.previousGPUProjectionMatrix;


                commonSettings.viewToClipMatrix = gpuProj.Pack();
                commonSettings.viewToClipMatrixPrev = gpuProjPrev.Pack();

                commonSettings.worldToViewMatrix = view.Pack();
                commonSettings.worldToViewMatrixPrev = viewPrev.Pack();

                // default worldPrevToWorldMatrix = identity
                commonSettings.worldPrevToWorldMatrix = new float[16]
                {
                    1f, 0f, 0f, 0f,
                    0f, 1f, 0f, 0f,
                    0f, 0f, 1f, 0f,
                    0f, 0f, 0f, 1f
                };
                commonSettings.cameraJitter = cameraExt.jitter.Pack();
                commonSettings.cameraJitterPrev = cameraExt.previousJitter.Pack();
                commonSettings.resourceSize = new[] { (ushort)cameraData.scaledWidth, (ushort)cameraData.scaledHeight };

                //todo:consider render scale change
                commonSettings.resourceSizePrev = new[] { (ushort)cameraData.scaledWidth, (ushort)cameraData.scaledHeight };
                commonSettings.rectSize = new[] { (ushort)cameraData.scaledWidth, (ushort)cameraData.scaledHeight };
                commonSettings.rectSizePrev = new[] { (ushort)cameraData.scaledWidth, (ushort)cameraData.scaledHeight };
                commonSettings.frameIndex = (uint)Time.frameCount;
                commonSettings.timeDeltaBetweenFrames = Time.deltaTime;
                commonSettings.denoisingRange = cameraData.camera.farClipPlane;
                commonSettings.accumulationMode = AccumulationMode.MAX_NUM;
                commonSettings.splitScreen = aoSetting.splitScreen.value;

                NRDInitlizer.NRD_SetCommonSettings(NRDContext, ref commonSettings);

                data.Settings = ReblurSettings.Default();
                data.Settings.minBlurRadius = aoSetting.NRDBlurMinRadius.value;
                data.Settings.maxBlurRadius = aoSetting.NRDBlurMaxRadius.value;

                data.ReblurSharedConstants = new();
                NRDInitlizer.NRD_SetupReblurConstBuffer(NRDContext, ref commonSettings, ref data.Settings, ref data.ReblurSharedConstants);


                data.ClassifyTiles = m_ClassifyTiles;
                data.HitDistReconstruction = m_HitDistReconstruction;
                data.TemporalAccumulation = m_TemporalAccumulation;
                data.HistoryFix = m_HistoryFix;
                data.Blur = m_Blur;
                data.PostBlur = m_PostBlur;
                data.SplitScreen = m_SplitScreen;

                data.MotionTexture = motionTexture;
                data.CurrNormalRoughnessTexture = gBufferNormalRoughnessTexture;
                data.CurrViewZTexture = viewZTexture;
                data.PrevViewZTexture = renderGraph.ImportTexture(historyRT.GetPreviousFrameRT(HistoryFrameType.ViewZ));

                data.PrevNormalRoughnessTexture = renderGraph.ImportTexture(historyRT.GetCurrentFrameRT(HistoryFrameType.PrevNormalRoughness));

                ReAllocatedInternalDataTextureIfNeeded(historyRT, out var reblurInternalData);
                data.PrevInternalDataTexture = renderGraph.ImportTexture(reblurInternalData);

                ReAllocatedAODiffuseTextureIfNeeded(historyRT, out var prevAODiffuse);

                ReAllocatedDiffuseFastHistoryTextureIfNeeded(historyRT, out var diffuseFastPrev, out var diffuseFastCurr);

                data.UnfilteredAOTexture = aoTexture;
                data.PrevAODiffuseTexture = renderGraph.ImportTexture(prevAODiffuse);
                data.PrevAODiffuseFastTexture = renderGraph.ImportTexture(diffuseFastPrev);
                data.CurrAODiffuseFastTexture = renderGraph.ImportTexture(diffuseFastCurr);

                
                data.TempDataTexture = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    format = GraphicsFormat.R8_UNorm,
                    name = "TempData Texture",
                    enableRandomWrite = true,
                });

                data.DebugTexture = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    format = GraphicsFormat.R8_UNorm,
                    name = "Debug Texture",
                    enableRandomWrite = true,
                });

                data.TempTexture1 = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    format = GraphicsFormat.R8_UNorm,
                    name = "TempTexture1",
                    enableRandomWrite = true,
                });
                data.TempTexture2 = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    format = GraphicsFormat.R8_UNorm,
                    name = "TempTexture2",
                    enableRandomWrite = true,
                });

                data.OutputAOTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    format = GraphicsFormat.R8_UNorm,
                    name = "Denoised AO",
                    enableRandomWrite = true,
                });

                data.TileTexture = builder.CreateTransientTexture(new TextureDesc(
                    CoreUtils.DivRoundUp(cameraData.actualWidth, 16),
                    CoreUtils.DivRoundUp(cameraData.actualHeight, 16))
                {
                    format = GraphicsFormat.R8_UNorm,
                    name = "NRD-SIGMA TileTexture",
                    enableRandomWrite = true
                });


                data.InternalDataTexture = builder.CreateTransientTexture(new TextureDesc(
                    cameraData.actualWidth,
                    cameraData.actualHeight)
                {
                    format = GraphicsFormat.R8_UNorm,
                    name = "REBLUR Transient Data",
                    enableRandomWrite = true
                });


                data.DummyTexture = renderGraph.defaultResources.blackTexture;


                data.width = cameraData.actualWidth;
                data.height = cameraData.actualHeight;
                // data.SplitScreen = shadowSetting.splitScreen.value;

                builder.UseTexture(data.PrevNormalRoughnessTexture);
                builder.UseTexture(data.PrevAODiffuseFastTexture);

                builder.UseTexture(data.PrevAODiffuseTexture);
                // builder.UseTexture(data.CurrAODiffuseTexture, AccessFlags.ReadWrite);

                builder.UseTexture(data.DummyTexture);

                builder.UseTexture(data.DummyTexture);
                builder.UseTexture(data.PrevInternalDataTexture);
                builder.UseTexture(data.MotionTexture);
                builder.UseTexture(data.CurrNormalRoughnessTexture);
                builder.UseTexture(data.CurrViewZTexture);
                builder.UseTexture(data.PrevViewZTexture);

                builder.UseTexture(data.UnfilteredAOTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.CurrAODiffuseFastTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.OutputAOTexture, AccessFlags.ReadWrite);

                builder.UseTexture(data.TileTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc<PassData>(ExecutePass);
                result = data.OutputAOTexture;
            }

            return result;
        }
    }
}