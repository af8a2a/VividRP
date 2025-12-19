using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class SIGMADenoiser : IDisposable
    {
        ComputeShader m_ClassifyTiles;
        ComputeShader m_SmoothTiles;
        ComputeShader m_ShadowCopy;
        ComputeShader m_ShadowBlur;
        ComputeShader m_ShadowPostBlur;
        ComputeShader m_ShadowTemporalStabilization;

        ComputeShader m_ShadowSplitScreen;

        RTHandle _HistoryLength;
        RTHandle _History;

        IntPtr NRDContext;

        public void Init(Camera camera)
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<DenoiserRuntimeShader>();

            m_ClassifyTiles = runtimeShaders.shadowClassifyTiles;
            m_SmoothTiles = runtimeShaders.shadowSmoothTiles;
            m_ShadowCopy = runtimeShaders.shadowCopy;
            m_ShadowBlur = runtimeShaders.shadowBlur;
            m_ShadowPostBlur = runtimeShaders.shadowPostBlur;
            m_ShadowTemporalStabilization = runtimeShaders.shadowTemporalStabilization;
            m_ShadowSplitScreen = runtimeShaders.shadowSplitScreen;

            NRDContext = NRDInitlizer.NRD_GetContext();
        }


        public void Dispose()
        {
            NRDInitlizer.NRD_ReleaseContext(NRDContext);
            _HistoryLength?.Release();
            _History?.Release();
        }


        class PassData
        {
            internal ComputeShader ClassifyTiles;
            internal ComputeShader SmoothTiles;
            internal ComputeShader ShadowCopy;
            internal ComputeShader ShadowBlur;
            internal ComputeShader ShadowPostBlur;
            internal ComputeShader ShadowTemporalStabilization;
            internal ComputeShader ShadowSplitScreen;


            internal TextureHandle motionTexture;
            internal TextureHandle gBufferNormalRoughnessTexture;
            internal TextureHandle viewZTexture;


            internal TextureHandle ShadowTransientTexture_0;
            internal TextureHandle ShadowTransientTexture_1;
            internal TextureHandle ShadowTransientTexture_2;
            internal TextureHandle ShadowTransientTexture_3;


            internal TextureHandle TransientSigmaHistory;
            internal TextureHandle TransientSigmaHistoryLength;

            //write to history
            internal TextureHandle PersistSigmaHistory;
            internal TextureHandle PersistSigmaHistoryLength;


            //will be output
            internal TextureHandle PenumbraTexture;


            internal int width, height;

            internal SigmaSharedConstants SigmaSharedConstants;

            internal SigmaSettings Settings;

            //internal resource
            internal TextureHandle TileTexture;
            internal TextureHandle SmoothTileTexture;


            //debug
            internal float SplitScreen;

            //out
            internal TextureHandle ShadowTexture;
        }


        static int gIn_ViewZ = Shader.PropertyToID("gIn_ViewZ");
        static int gIn_Penumbra = Shader.PropertyToID("gIn_Penumbra");
        static int gIn_Tiles = Shader.PropertyToID("gIn_Tiles");
        static int gOut_Tiles = Shader.PropertyToID("gOut_Tiles");
        static int gIn_Normal_Roughness = Shader.PropertyToID("gIn_Normal_Roughness");
        static int gOut_Penumbra = Shader.PropertyToID("gOut_Penumbra");
        static int gIn_Shadow_Translucency = Shader.PropertyToID("gIn_Shadow_Translucency");
        static int gOut_Shadow_Translucency = Shader.PropertyToID("gOut_Shadow_Translucency");
        static int gIn_Mv = Shader.PropertyToID("gIn_Mv");

        static int gIn_History = Shader.PropertyToID("gIn_History");
        static int gIn_HistoryLength = Shader.PropertyToID("gIn_HistoryLength");
        static int gOut_History = Shader.PropertyToID("gOut_History");
        static int gOut_HistoryLength = Shader.PropertyToID("gOut_HistoryLength");


        static int SIGMA_ClassifyTilesConstants = Shader.PropertyToID("SIGMA_ClassifyTilesConstants");
        static int SIGMA_SmoothTilesConstants = Shader.PropertyToID("SIGMA_SmoothTilesConstants");
        static int SIGMA_BlurConstants = Shader.PropertyToID("SIGMA_BlurConstants");
        static int SIGMA_TemporalStabilizationConstants = Shader.PropertyToID("SIGMA_TemporalStabilizationConstants");
        static int SIGMA_SplitScreenConstants = Shader.PropertyToID("SIGMA_SplitScreenConstants");

        static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;
            var cs = data.ClassifyTiles;
            var kernel = 0;

            var tx = CoreUtils.DivRoundUp(data.width, 16);
            var ty = CoreUtils.DivRoundUp(data.height, 16);

            {
                ConstantBuffer.Push(cmd, data.SigmaSharedConstants, cs, SIGMA_ClassifyTilesConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.viewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Penumbra, data.PenumbraTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Tiles, data.TileTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }


            tx = CoreUtils.DivRoundUp(tx, 16);
            ty = CoreUtils.DivRoundUp(ty, 16);

            cs = data.SmoothTiles;
            {
                ConstantBuffer.Push(cmd, data.SigmaSharedConstants, cs, SIGMA_SmoothTilesConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Tiles, data.SmoothTileTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            tx = CoreUtils.DivRoundUp(data.width, 8);
            ty = CoreUtils.DivRoundUp(data.height, 16);


            cs = data.ShadowCopy;
            {
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.SmoothTileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_History, data.PersistSigmaHistory);
                cmd.SetComputeTextureParam(cs, kernel, gIn_HistoryLength, data.PersistSigmaHistoryLength);
                cmd.SetComputeTextureParam(cs, kernel, gOut_History, data.TransientSigmaHistory);
                cmd.SetComputeTextureParam(cs, kernel, gOut_HistoryLength, data.TransientSigmaHistoryLength);
                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }


            cs = data.ShadowBlur;
            {
                ConstantBuffer.Push(cmd, data.SigmaSharedConstants, cs, SIGMA_BlurConstants);

                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.viewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.gBufferNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.SmoothTileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Penumbra, data.PenumbraTexture);

                cmd.SetComputeTextureParam(cs, kernel, gOut_Penumbra, data.ShadowTransientTexture_0);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Shadow_Translucency, data.ShadowTransientTexture_1);
                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }


            cs = data.ShadowPostBlur;
            ConstantBuffer.Push(cmd, data.SigmaSharedConstants, cs, SIGMA_BlurConstants);

            {
                bool isStabilizationEnabled = data.Settings.maxStabilizedFrameNum > 0; 

                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.viewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.gBufferNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.SmoothTileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Penumbra, data.ShadowTransientTexture_0);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Shadow_Translucency, data.ShadowTransientTexture_1);


                cmd.SetComputeTextureParam(cs, kernel, gOut_Penumbra, data.ShadowTransientTexture_2);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Shadow_Translucency, isStabilizationEnabled ? data.ShadowTransientTexture_3 : data.ShadowTexture);
                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }


            if (data.Settings.maxStabilizedFrameNum > 0 )
            {
                cs = data.ShadowTemporalStabilization;
                ConstantBuffer.Push(cmd, data.SigmaSharedConstants, cs, SIGMA_TemporalStabilizationConstants);
                {
                    // Inputs
                    cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.viewZTexture);
                    cmd.SetComputeTextureParam(cs, kernel, gIn_Mv, data.motionTexture);

                    cmd.SetComputeTextureParam(cs, kernel, gIn_Penumbra, data.ShadowTransientTexture_2);
                    cmd.SetComputeTextureParam(cs, kernel, gIn_Shadow_Translucency, data.ShadowTransientTexture_3);


                    cmd.SetComputeTextureParam(cs, kernel, gIn_History, data.TransientSigmaHistory);
                    cmd.SetComputeTextureParam(cs, kernel, gIn_HistoryLength, data.TransientSigmaHistoryLength);

                    cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.SmoothTileTexture);


                    // Outputs
                    cmd.SetComputeTextureParam(cs, kernel, gOut_Shadow_Translucency, data.ShadowTexture);
                    cmd.SetComputeTextureParam(cs, kernel, gOut_HistoryLength, data.PersistSigmaHistoryLength);
                    cmd.SetComputeTextureParam(cs, kernel, gOut_History, data.PersistSigmaHistory);

                    // Shaders
                    cmd.DispatchCompute(cs, kernel, tx, ty, 1);
                }
            }

            if (data.SplitScreen > 0)
            {
                cs = data.ShadowSplitScreen;
                ConstantBuffer.Push(cmd, data.SigmaSharedConstants, cs, SIGMA_SplitScreenConstants);

                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.viewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Penumbra, data.PenumbraTexture);

                cmd.SetComputeTextureParam(cs, kernel, gOut_Shadow_Translucency, data.ShadowTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
                
            }
        }

        static uint GetMaxAccumulatedFrameNum(float accumulationTime, float fps)
        {
            return (uint)(accumulationTime * fps + 0.5f);
        }


        /// <summary>
        /// NRD SIGMA Denoise
        /// </summary>
        /// <param name="renderGraph"></param>
        /// <param name="cameraData"></param>
        /// <param name="motionTexture"></param>
        /// <param name="gBufferNormalRoughnessTexture"></param>
        /// <param name="viewZTexture"></param>
        /// <param name="UnfilteredPenumbraTexture"></param>
        /// <param name="UnfilteredTranslucencyTexture">Not support yet</param>
        /// <param name="ShadowTexture">out</param>
        /// <returns></returns>
        public TextureHandle Denoise(RenderGraph renderGraph,
            ContextContainer frameData,
            TextureHandle motionTexture,
            TextureHandle gBufferNormalRoughnessTexture,
            TextureHandle viewZTexture,
            TextureHandle UnfilteredPenumbraTexture,
            //not support yet
            TextureHandle UnfilteredTranslucencyTexture
        )
        {
            var result = TextureHandle.nullHandle;
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            var shadowSetting = VolumeManager.instance.stack.GetComponent<Shadows>();

            RenderingUtils.ReAllocateHandleIfNeeded(ref _HistoryLength,
                new RenderTextureDescriptor(cameraData.scaledWidth, cameraData.scaledHeight, GraphicsFormat.R32_UInt, 0)
                {
                    enableRandomWrite = true
                });

            RenderingUtils.ReAllocateHandleIfNeeded(ref _History,
                new RenderTextureDescriptor(cameraData.scaledWidth, cameraData.scaledHeight, GraphicsFormat.R8G8B8A8_UNorm, 0)
                {
                    enableRandomWrite = true
                });

            using (var builder = renderGraph.AddComputePass<PassData>("NRD SIGMA Denoiser", out var data))
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
                commonSettings.splitScreen = shadowSetting.splitScreen.value;


                NRDInitlizer.NRD_SetCommonSettings(NRDContext, ref commonSettings);

                data.Settings = SigmaSettings.Default();

                if (shadowSetting.adaptiveAccumulation.value)
                {
                    //at least 30 fps...
                    var fps = Mathf.Min(1f / Mathf.Min(Time.deltaTime, 0.033f), 121.0f);
                    const float sigmaDefaultAccumulationTime = 0.084f; // sec

                    var maxSigmaStabilizedFrames = GetMaxAccumulatedFrameNum(sigmaDefaultAccumulationTime, fps);
                    const uint sigmaMaxHistoryFrameNum = 7;
                    data.Settings.maxStabilizedFrameNum = (uint)Mathf.Min(maxSigmaStabilizedFrames, sigmaMaxHistoryFrameNum);
                }
                else
                {
                    data.Settings.maxStabilizedFrameNum = (uint)shadowSetting.maxStabilizedFrameNum.value;
                }

                data.Settings.maxStabilizedFrameNum = (uint)shadowSetting.maxStabilizedFrameNum.value;
                data.Settings.lightDirection = (-lightData.visibleLights[lightData.mainLightIndex].GetForward()).Pack();

                data.SigmaSharedConstants = new SigmaSharedConstants();
                NRDInitlizer.NRD_SetupSigmaConstBuffer(NRDContext, ref commonSettings, ref data.Settings, ref data.SigmaSharedConstants);


                data.ClassifyTiles = m_ClassifyTiles;
                data.SmoothTiles = m_SmoothTiles;
                data.ShadowCopy = m_ShadowCopy;
                data.ShadowBlur = m_ShadowBlur;
                data.ShadowPostBlur = m_ShadowPostBlur;
                data.ShadowTemporalStabilization = m_ShadowTemporalStabilization;
                data.ShadowSplitScreen = m_ShadowSplitScreen;

                data.motionTexture = motionTexture;
                data.gBufferNormalRoughnessTexture = gBufferNormalRoughnessTexture;
                data.viewZTexture = viewZTexture;
                data.PenumbraTexture = UnfilteredPenumbraTexture;
                data.ShadowTransientTexture_0 = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    format = GraphicsFormat.R8G8B8A8_UNorm,
                    name = "ShadowTransientTexture_0",
                    enableRandomWrite = true,
                });

                data.ShadowTransientTexture_1 = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    //todo:
                    format = GraphicsFormat.R8G8B8A8_UNorm,
                    name = "ShadowTransientTexture_1",
                    enableRandomWrite = true,
                });

                data.ShadowTransientTexture_2 = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    //todo:
                    format = GraphicsFormat.R8G8B8A8_UNorm,
                    name = "ShadowTransientTexture_2",
                    enableRandomWrite = true,
                });

                data.ShadowTransientTexture_3 = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    //todo:
                    format = GraphicsFormat.R8_UNorm,
                    name = "ShadowTransientTexture_3",
                    enableRandomWrite = true,
                });


                data.ShadowTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight)
                {
                    format = GraphicsFormat.R8_UNorm,
                    enableRandomWrite = true,
                    name = "NRD-SIGMA Output",
                });

                data.TileTexture = builder.CreateTransientTexture(new TextureDesc(
                    CoreUtils.DivRoundUp(cameraData.actualWidth, 16),
                    CoreUtils.DivRoundUp(cameraData.actualHeight, 16))
                {
                    format = GraphicsFormat.R8G8B8A8_UNorm,
                    name = "NRD-SIGMA TileTexture",
                    enableRandomWrite = true
                });

                data.SmoothTileTexture = builder.CreateTransientTexture(new TextureDesc(
                    CoreUtils.DivRoundUp(cameraData.actualWidth, 16),
                    CoreUtils.DivRoundUp(cameraData.actualHeight, 16))
                {
                    format = GraphicsFormat.R8G8B8A8_UNorm,
                    name = "NRD-SIGMA SmoothTileTexture",
                    enableRandomWrite = true
                });


                data.PersistSigmaHistory = renderGraph.ImportTexture(_History);
                data.PersistSigmaHistoryLength = renderGraph.ImportTexture(_HistoryLength);

                data.TransientSigmaHistory = builder.CreateTransientTexture(data.PersistSigmaHistory);
                data.TransientSigmaHistoryLength = builder.CreateTransientTexture(data.PersistSigmaHistoryLength);


                data.width = cameraData.actualWidth;
                data.height = cameraData.actualHeight;
                data.SplitScreen = shadowSetting.splitScreen.value;

                builder.UseTexture(data.motionTexture);
                builder.UseTexture(data.gBufferNormalRoughnessTexture);
                builder.UseTexture(data.viewZTexture);
                builder.UseTexture(data.PenumbraTexture);

                builder.UseTexture(data.PersistSigmaHistory, AccessFlags.ReadWrite);
                builder.UseTexture(data.PersistSigmaHistoryLength, AccessFlags.ReadWrite);

                builder.UseTexture(data.TileTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.SmoothTileTexture, AccessFlags.ReadWrite);


                builder.UseTexture(data.ShadowTexture, AccessFlags.Write);

                builder.AllowPassCulling(false);
                builder.SetRenderFunc<PassData>(ExecutePass);
                result = data.ShadowTexture;
            }

            return result;
        }
    }
}