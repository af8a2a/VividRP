using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public class HDRPTemporalAAPass
    {
        public static readonly int _BlitTexture = Shader.PropertyToID("_BlitTexture");
        public static readonly int _InputTexture = Shader.PropertyToID("_InputTexture");

        public static readonly int _TaaPostParameters = Shader.PropertyToID("_TaaPostParameters");
        public static readonly int _TaaPostParameters1 = Shader.PropertyToID("_TaaPostParameters1");
        public static readonly int _TaaHistorySize = Shader.PropertyToID("_TaaHistorySize");
        public static readonly int _TaaFilterWeights = Shader.PropertyToID("_TaaFilterWeights");
        public static readonly int _NeighbourOffsets = Shader.PropertyToID("_NeighbourOffsets");
        public static readonly int _TaauParameters = Shader.PropertyToID("_TaauParameters");
        public static readonly int _TaaScales = Shader.PropertyToID("_TaaScales");
        public static readonly int _CameraMotionVectorsTexture = Shader.PropertyToID("_CameraMotionVectorsTexture");
        public static readonly int _StencilMask = Shader.PropertyToID("_StencilMask");
        public static readonly int _StencilRef = Shader.PropertyToID("_StencilRef");
        public static readonly int _StencilCmp = Shader.PropertyToID("_StencilCmp");
        public static readonly int _InputHistoryTexture = Shader.PropertyToID("_InputHistoryTexture");
        public static readonly int _OutputHistoryTexture = Shader.PropertyToID("_OutputHistoryTexture");
        public static readonly int _InputVelocityMagnitudeHistory = Shader.PropertyToID("_InputVelocityMagnitudeHistory");
        public static readonly int _OutputVelocityMagnitudeHistory = Shader.PropertyToID("_OutputVelocityMagnitudeHistory");
        public static readonly int _DepthTexture = Shader.PropertyToID("_DepthTexture");
        public static readonly int _StencilTexture = Shader.PropertyToID("_StencilTexture");
        public static readonly int _TaaFrameInfo = Shader.PropertyToID("_TaaFrameInfo");
        public static readonly int _TaaJitterStrength = Shader.PropertyToID("_TaaJitterStrength");

        class TemporalAntiAliasingData
        {
            public Material temporalAAMaterial;
            public bool resetPostProcessingHistory;
            public Vector4 previousScreenSize;
            public Vector4 taaParameters;
            public Vector4 taaParameters1;
            public Vector4[] taaFilterWeights = new Vector4[2];
            public Vector4[] neighbourOffsets = new Vector4[4];
            public bool motionVectorRejection;
            public Vector4 taauParams;
            public Rect finalViewport;
            public Rect prevFinalViewport;
            public Vector4 taaScales;
            public Vector4 taaFrameInfo;
            public Vector4 taaJitterStrength;

            
            public bool runsTAAU;

            public TextureHandle source;
            public TextureHandle destination;
            public TextureHandle motionVecTexture;
            public TextureHandle depthBuffer;
            public TextureHandle stencilBuffer;
            public TextureHandle depthMipChain;
            public TextureHandle prevHistory;
            public TextureHandle nextHistory;
            public TextureHandle prevMVLen;
            public TextureHandle nextMVLen;
        }


        static readonly Vector2[] TAASampleOffsets = new Vector2[]
        {
            // center
            new Vector2(0.0f, 0.0f),

            // NeighbourOffsets
            new Vector2(0.0f, 1.0f),
            new Vector2(1.0f, 0.0f),
            new Vector2(-1.0f, 0.0f),
            new Vector2(0.0f, -1.0f),
            new Vector2(1.0f, 1.0f),
            new Vector2(1.0f, -1.0f),
            new Vector2(-1.0f, 1.0f),
            new Vector2(-1.0f, -1.0f)
        };

        float[] taaSampleWeights = new float[9];
        internal const float TAABaseBlendFactorMin = 0.6f;
        internal const float TAABaseBlendFactorMax = 0.95f;
        private Material m_TemporalAAMaterial;
        private UpscalingTechnique m_PrevUpscaleTechnique = UpscalingTechnique.Linear;
        static ProfilingSampler _profilingSampler = new ProfilingSampler("Temporal AA");

        void ComputeWeights(ref float centralWeight, ref Vector4[] filterWeights, Vector2 jitter)
        {
            float totalWeight = 0;
            for (int i = 0; i < 9; ++i)
            {
                float x = TAASampleOffsets[i].x + jitter.x;
                float y = TAASampleOffsets[i].y + jitter.y;
                float d = (x * x + y * y);

                taaSampleWeights[i] = Mathf.Exp((-0.5f / (0.22f)) * d);
                totalWeight += taaSampleWeights[i];
            }

            centralWeight = taaSampleWeights[0] / totalWeight;

            for (int i = 0; i < 8; ++i)
            {
                filterWeights[(i / 4)][(i % 4)] = taaSampleWeights[i + 1] / totalWeight;
            }
        }

        static void GetNeighbourOffsets(ref Vector4[] neighbourOffsets)
        {
            for (int i = 0; i < 16; ++i)
            {
                neighbourOffsets[(i / 4)][(i % 4)] = TAASampleOffsets[i / 2 + 1][i % 2];
            }
        }


        static RTHandle HistoryAccumulateTextureAllocator(Vector2Int viewport,GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {

            frameIndex &= 1;

            return rtHandleSystem.Alloc(viewport.x,viewport.y, colorFormat: graphicsFormat,
                filterMode: FilterMode.Point, enableRandomWrite: true,
                useDynamicScale:false,
                name: string.Format("{0}TAA History{1}", viewName, frameIndex));
        }

        static RTHandle VelocityMagnitudeHistoryTexturesAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;
            return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                filterMode: FilterMode.Point, enableRandomWrite: true,
                name: string.Format("{0}Velocity magnitude{1}", viewName, frameIndex));
        }


        void PrepareTAAPassData(RenderGraph renderGraph, IUnsafeRenderGraphBuilder builder, TemporalAntiAliasingData passData, UniversalCameraData cameraData,
            TextureHandle depthStencilBuffer, TextureHandle motionVectors, TextureHandle depthBufferMipChain, TextureHandle sourceTexture)
        {
            if (!m_TemporalAAMaterial)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<PostProcessingRuntimeShader>();

                m_TemporalAAMaterial = CoreUtils.CreateEngineMaterial(runtimeShader.taauShader);
            }


            passData.resetPostProcessingHistory = cameraData.resetHistory;

            cameraData.camera.TryGetComponent(out UniversalAdditionalCameraData additionalCameraData);

            float minAntiflicker = 0.0f;
            float maxAntiflicker = 3.5f;
            float motionRejectionMultiplier = Mathf.Lerp(0.0f, 250.0f,
                additionalCameraData.taaMotionVectorRejection * additionalCameraData.taaMotionVectorRejection * additionalCameraData.taaMotionVectorRejection);

            // The anti flicker becomes much more aggressive on higher values
            float temporalContrastForMaxAntiFlicker = 0.7f - Mathf.Lerp(0.0f, 0.3f, Mathf.SmoothStep(0.5f, 1.0f, additionalCameraData.taaAntiFlicker));

            bool TAAU = cameraData.IsTAAUEnabled();

            float antiFlickerLerpFactor = additionalCameraData.taaAntiFlicker;
            float historySharpening = additionalCameraData.taaHistorySharpening;

            if (cameraData.camera.cameraType == CameraType.SceneView)
            {
                // Force settings for scene view.
                historySharpening = 0.25f;
                antiFlickerLerpFactor = 0.7f;
            }

            float antiFlicker = Mathf.Lerp(minAntiflicker, maxAntiflicker, antiFlickerLerpFactor);
            const float historyContrastBlendStart = 0.51f;
            float historyContrastLerp = Mathf.Clamp01((antiFlickerLerpFactor - historyContrastBlendStart) / (1.0f - historyContrastBlendStart));

            passData.taaParameters = new Vector4(historySharpening, antiFlicker, motionRejectionMultiplier, temporalContrastForMaxAntiFlicker);

            // Precompute weights used for the Gaussian fitting of the Blackman-Harris filter.
            ComputeWeights(ref passData.taaParameters1.y, ref passData.taaFilterWeights, cameraData.jitter);
            GetNeighbourOffsets(ref passData.neighbourOffsets);

            // For post dof we can be a bit more agressive with the taa base blend factor, since most aliasing has already been taken care of in the first TAA pass.
            // The following MAD operation expands the range to a new minimum (and keeps max the same).
            const float postDofMin = 0.4f;
            const float scale = (TAABaseBlendFactorMax - postDofMin) / (TAABaseBlendFactorMax - TAABaseBlendFactorMin);
            const float offset = postDofMin - TAABaseBlendFactorMin * scale;
            float taaBaseBlendFactor = additionalCameraData.taaBaseBlendFactor;

            passData.taaParameters1.x = cameraData.camera.cameraType == CameraType.SceneView ? 0.2f : 1.0f - taaBaseBlendFactor;
            passData.taaParameters1.z = (int)StencilUsage.ExcludeFromTUAndAA;
            passData.taaParameters1.w = historyContrastLerp;
            passData.taaFrameInfo = new Vector4(additionalCameraData.taaSharpenMode == TAASharpenMode.LowQuality ? additionalCameraData.taaSharpenStrength : 0,
                0, cameraData.historyFrameRTSystem.historyFrameCount,
                additionalCameraData.upscalerTechnique is UpscalingTechnique.TAAU ? 1 : 0);
            passData.taaJitterStrength = cameraData.jitter;
            
            passData.temporalAAMaterial = m_TemporalAAMaterial;
            passData.temporalAAMaterial.shaderKeywords = null;

            if (cameraData.isAlphaOutputEnabled)
            {
                passData.temporalAAMaterial.EnableKeyword("ENABLE_ALPHA");
            }

            if (additionalCameraData.taaHistorySharpening == 0)
            {
                passData.temporalAAMaterial.EnableKeyword("FORCE_BILINEAR_HISTORY");
            }

            if (additionalCameraData.taaHistorySharpening != 0 && additionalCameraData.taaAntiHistoryRinging &&
                additionalCameraData.TAAQuality is TAAQualityLevel.High)
            {
                passData.temporalAAMaterial.EnableKeyword("ANTI_RINGING");
            }

            passData.motionVectorRejection = additionalCameraData.taaMotionVectorRejection > 0;
            if (passData.motionVectorRejection)
            {
                passData.temporalAAMaterial.EnableKeyword("ENABLE_MV_REJECTION");
            }

            if (historyContrastLerp > 0.0f)
            {
                passData.temporalAAMaterial.EnableKeyword("HISTORY_CONTRAST_ANTI_FLICKER");
            }

            passData.runsTAAU = TAAU;

            if (TAAU)
            {
                passData.temporalAAMaterial.EnableKeyword("TAA_UPSAMPLE");
            }
            else
            {
                switch (additionalCameraData.TAAQuality)
                {
                    case TAAQualityLevel.Low:
                        passData.temporalAAMaterial.EnableKeyword("LOW_QUALITY");
                        break;
                    case TAAQualityLevel.Medium:
                        passData.temporalAAMaterial.EnableKeyword("MEDIUM_QUALITY");
                        break;
                    case TAAQualityLevel.High:
                        passData.temporalAAMaterial.EnableKeyword("HIGH_QUALITY");
                        break;
                    default:
                        passData.temporalAAMaterial.EnableKeyword("MEDIUM_QUALITY");
                        break;
                }
            }

            if (TAAU)
            {
                passData.temporalAAMaterial.EnableKeyword("DIRECT_STENCIL_SAMPLE");
            }


            RTHandle prevHistory, nextHistory;
            bool validHistory = cameraData.historyFrameRTSystem.ReAllocatedAccumulateTextureIfNeeded(
                HistoryAccumulateTextureAllocator, new Vector2Int(cameraData.pixelWidth, cameraData.pixelHeight), RenderingUtilsExt.PickPostProcessingFormat(),
                HistoryFrameType.TemporalAntialiasing, out prevHistory,
                out nextHistory);


            Vector2Int prevViewPort = RTHandles.rtHandleProperties.previousViewportSize;
            passData.previousScreenSize = new Vector4(prevViewPort.x, prevViewPort.y, 1.0f / prevViewPort.x, 1.0f / prevViewPort.y);
            if (TAAU )
                passData.previousScreenSize = new Vector4(cameraData.pixelWidth, cameraData.pixelHeight,
                    1.0f / cameraData.pixelWidth,
                    1.0f / cameraData.pixelHeight);

            passData.source = sourceTexture;

            passData.depthBuffer = depthStencilBuffer;

            passData.motionVecTexture = motionVectors;
            passData.depthMipChain = depthBufferMipChain;
            passData.prevHistory = renderGraph.ImportTexture(prevHistory);

            passData.resetPostProcessingHistory = passData.resetPostProcessingHistory || !validHistory;

            builder.UseTexture(sourceTexture);
            builder.UseTexture(depthStencilBuffer);
            builder.UseTexture(motionVectors);
            builder.UseTexture(depthBufferMipChain);
            builder.UseTexture(renderGraph.ImportTexture(prevHistory));
            builder.UseTexture(passData.prevHistory, passData.resetPostProcessingHistory ? AccessFlags.Read : AccessFlags.ReadWrite);

            passData.nextHistory = renderGraph.ImportTexture(nextHistory);


            builder.UseTexture(renderGraph.ImportTexture(nextHistory), AccessFlags.Write);

            // Note: In case we run TAA for a second time (post-dof), we can use the same velocity history (and not write the output)
            RTHandle prevMVLen, nextMVLen;

            cameraData.historyFrameRTSystem.ReAllocatedAccumulateTextureIfNeeded(
                VelocityMagnitudeHistoryTexturesAllocator, GraphicsFormat.R16_SFloat, HistoryFrameType.TAAMotionVectorMagnitude, out prevMVLen,
                out nextMVLen);


            passData.prevMVLen = renderGraph.ImportTexture(prevMVLen);
            passData.nextMVLen = renderGraph.ImportTexture(nextMVLen);

            builder.UseTexture(passData.prevMVLen);
            builder.UseTexture(passData.nextMVLen, AccessFlags.Write);

            passData.destination = renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth, cameraData.pixelHeight)
            {
                name = "TAA Destination",
                format = cameraData.cameraTargetDescriptor.graphicsFormat,
                enableRandomWrite = true
            });

            builder.UseTexture(passData.destination, AccessFlags.Write);
            bool needToUseCurrFrameSizeForHistory = passData.resetPostProcessingHistory || m_PrevUpscaleTechnique != cameraData.upscalingTechnique;

            passData.prevFinalViewport = cameraData.pixelRect;

            var mainRTScales = RTHandles.CalculateRatioAgainstMaxSize(cameraData.actualWidth, cameraData.actualHeight);

            var historyRenderingViewport = TAAU
                ? new Vector2(passData.prevFinalViewport.width, passData.prevFinalViewport.height)
                : (needToUseCurrFrameSizeForHistory
                    ? RTHandles.rtHandleProperties.currentViewportSize
                    : RTHandles.rtHandleProperties.previousViewportSize);

            passData.finalViewport = cameraData.pixelRect;

            Vector4 scales = new Vector4(historyRenderingViewport.x / prevHistory.rt.width, historyRenderingViewport.y / prevHistory.rt.height, mainRTScales.x,
                mainRTScales.y);

            passData.taaScales = scales;

            var resScale = DynamicResolutionHandler.instance.GetCurrentScale();
            float stdDev = 0.4f;
            passData.taauParams = new Vector4(1.0f / (stdDev * stdDev), 1.0f / resScale, 0.5f / resScale, resScale);

            passData.stencilBuffer = depthStencilBuffer;


            builder.UseTexture(depthStencilBuffer);


            m_PrevUpscaleTechnique = cameraData.upscalingTechnique;
        }


        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            using (var builder = renderGraph.AddUnsafePass<TemporalAntiAliasingData>("Temporal Anti-Aliasing", out var passData, _profilingSampler))
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                if (cameraData.cameraType != CameraType.Game)
                {
                    return source;
                }
                var resourceData = frameData.Get<UniversalResourceData>();
                PrepareTAAPassData(renderGraph, builder, passData, cameraData, resourceData.cameraDepthTexture, resourceData.motionVectorColor,
                    resourceData.cameraDepthPyramidTexture, source);

                builder.AllowPassCulling(false);
                builder.SetRenderFunc<TemporalAntiAliasingData>((data, ctx) =>
                {
                    var source = data.source;
                    var nextMVLenTexture = data.nextMVLen;
                    var prevMVLenTexture = data.prevMVLen;
                    var prevHistory = (RTHandle)data.prevHistory;
                    var nextHistory = (RTHandle)data.nextHistory;

                    
                    int taaPass = data.temporalAAMaterial.FindPass("TAA");
                    int excludeTaaPass = data.temporalAAMaterial.FindPass("Excluded From TAA");
                    int taauPass = data.temporalAAMaterial.FindPass("TAAU");
                    int copyHistoryPass = data.temporalAAMaterial.FindPass("Copy History");


                    var material = data.temporalAAMaterial;
                    material.SetVector(_TaaScales, data.taaScales);


                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);

                    if (data.resetPostProcessingHistory)
                    {
                        // var historyMpb = ctx.renderGraphPool.GetTempMaterialPropertyBlock();
                        // historyMpb.SetTexture(_InputTexture, source);
                        // historyMpb.SetVector(_TaaScales, data.taaScales);
                       material.SetVector(_TaaScales, data.taaScales);

                        // Rect r = data.finalViewport;
                        
                        // Blitter.BlitCameraTexture(cmd, source, data.prevHistory, material, copyHistoryPass);
                        // Blitter.BlitCameraTexture(cmd, source, data.nextHistory, material, copyHistoryPass);

                        // Blitter.DrawFullScreen(cmd, r, material, data.prevHistory, historyMpb, copyHistoryPass);
                        // Blitter.DrawFullScreen(cmd, r, material, data.nextHistory, historyMpb, copyHistoryPass);
                        Blitter.BlitTexture(cmd, source, data.prevHistory, material, copyHistoryPass);
                        Blitter.BlitTexture(cmd, source, data.nextHistory, material, copyHistoryPass);

                        // Blitter.DrawFullScreen(cmd, r, material, data.prevHistory, null, copyHistoryPass);
                        // Blitter.DrawFullScreen(cmd, r, material, data.nextHistory, null, copyHistoryPass);

                    }


                    material.SetInt(_StencilMask, (int)StencilUsage.ExcludeFromTUAndAA);
                    material.SetInt(_StencilRef, (int)StencilUsage.ExcludeFromTUAndAA);
                    material.SetTexture(_CameraMotionVectorsTexture, data.motionVecTexture);
                    material.SetTexture(_InputTexture, source);
                    material.SetTexture(_InputHistoryTexture, data.prevHistory);
                    if (prevMVLenTexture.IsValid() && data.motionVectorRejection)
                    {
                        material.SetTexture(_InputVelocityMagnitudeHistory, prevMVLenTexture);
                    }

                    material.SetTexture(_DepthTexture, data.depthMipChain);

                    var taaHistorySize = data.previousScreenSize;

                    material.SetVector(_TaaPostParameters, data.taaParameters);
                    material.SetVector(_TaaPostParameters1, data.taaParameters1);
                    material.SetVector(_TaaHistorySize, taaHistorySize);
                    material.SetVectorArray(_TaaFilterWeights, data.taaFilterWeights);
                    material.SetVectorArray(_NeighbourOffsets, data.neighbourOffsets);

                    material.SetVector(_TaauParameters, data.taauParams);
                    // material.SetVector(_TaaScales, data.taaScales);
                    material.SetVector(_TaaFrameInfo, data.taaFrameInfo);
                    material.SetVector(_TaaJitterStrength, data.taaJitterStrength);

                    if (data.runsTAAU )
                    {
                        CoreUtils.SetRenderTarget(cmd, data.destination);
                    }
                    else
                    {
                        CoreUtils.SetRenderTarget(cmd, data.destination, data.depthBuffer);
                    }

                    cmd.SetRandomWriteTarget(1, data.nextHistory);
                    if (nextMVLenTexture.IsValid() && data.motionVectorRejection)
                    {
                        cmd.SetRandomWriteTarget(2, nextMVLenTexture);
                    }

                    Rect rect = data.finalViewport;
                    rect.x = 0;
                    rect.y = 0;
                    if (data.runsTAAU )
                    {
                        // material.SetTexture(_StencilTexture, data.stencilBuffer, RenderTextureSubElement.Stencil);
                        Blitter.BlitTexture(cmd, source, data.destination, material, taauPass);
                    }
                    else
                    {
                        cmd.SetViewport(rect);
                        cmd.DrawProcedural(Matrix4x4.identity, data.temporalAAMaterial, taaPass, MeshTopology.Triangles, 3, 1);
                        cmd.DrawProcedural(Matrix4x4.identity, data.temporalAAMaterial, excludeTaaPass, MeshTopology.Triangles, 3, 1);
                    }

                    cmd.ClearRandomWriteTargets();
                });
                return passData.destination;
            }
        }
    }
}