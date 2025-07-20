using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public partial class ExposurePass
    {
        class DynamicExposureData
        {
            public ComputeShader exposureCS;
            public ComputeShader histogramExposureCS;
            public int exposurePreparationKernel;
            public int exposureReductionKernel;

            public TextureHandle textureMeteringMask;
            public TextureHandle exposureCurve;
            public TextureHandle blackTexture;

            public UniversalCameraData camera;
            public Vector2Int viewportSize;

            public ComputeBuffer histogramBuffer;

            public ExposureMode exposureMode;
            public bool histogramUsesCurve;
            public bool histogramOutputDebugData;

            public int[] exposureVariants;
            public Vector4 exposureParams;
            public Vector4 exposureParams2;
            public Vector4 proceduralMaskParams;
            public Vector4 proceduralMaskParams2;
            public Vector4 histogramExposureParams;
            public Vector4 adaptationParams;

            public TextureHandle source;
            public TextureHandle prevExposure;
            public TextureHandle nextExposure;
            public TextureHandle exposureDebugData;
            public TextureHandle tmpTarget1024;
            public TextureHandle tmpTarget32;
        }

        class ApplyExposureData
        {
            public ComputeShader applyExposureCS;
            public int applyExposureKernel;
            public int width;
            public int height;
            public int viewCount;

            public TextureHandle source;
            public TextureHandle destination;
            public TextureHandle prevExposure;
        }


        ExposureSetting m_Exposure;

        // Exposure data
        const int k_ExposureCurvePrecision = 128;
        const int k_HistogramBins = 128; // Important! If this changes, need to change HistogramExposure.compute
        const int k_DebugImageHistogramBins = 256; // Important! If this changes, need to change HistogramExposure.compute
        const int k_SizeOfHDRXYMapping = 512;
        readonly Color[] m_ExposureCurveColorArray = new Color[k_ExposureCurvePrecision];
        readonly int[] m_ExposureVariants = new int[4];

        Texture2D m_ExposureCurveTexture;

        RTHandle m_ExposureCurveTextureRT;
        RTHandle m_EmptyExposureTexture; // RGHalf
        RTHandle m_DebugExposureData;
        ComputeBuffer m_HistogramBuffer;
        ComputeBuffer m_DebugImageHistogramBuffer;
        readonly int[] m_EmptyHistogram = new int[k_HistogramBins];
        readonly int[] m_EmptyDebugImageHistogram = new int[k_DebugImageHistogramBins * 4];

        partial class Profiling
        {
            public static ProfilingSampler DynamicExposure = new ProfilingSampler(nameof(DynamicExposure));
            public static ProfilingSampler ApplyExposure = new ProfilingSampler(nameof(ApplyExposure));
        }

        void PrepareExposureCurveData(out float min, out float max)
        {
            var curve = m_Exposure.curveMap.value;
            var minCurve = m_Exposure.limitMinCurveMap.value;
            var maxCurve = m_Exposure.limitMaxCurveMap.value;

            if (m_ExposureCurveTexture == null)
            {
                m_ExposureCurveTexture = new Texture2D(k_ExposureCurvePrecision, 1, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.None)
                {
                    name = "Exposure Curve",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            bool minCurveHasPoints = minCurve.length > 0;
            bool maxCurveHasPoints = maxCurve.length > 0;
            float defaultMin = -100.0f;
            float defaultMax = 100.0f;

            var pixels = m_ExposureCurveColorArray;

            // Fail safe in case the curve is deleted / has 0 point
            if (curve == null || curve.length == 0)
            {
                min = 0f;
                max = 0f;

                for (int i = 0; i < k_ExposureCurvePrecision; i++)
                    pixels[i] = Color.clear;
            }
            else
            {
                min = curve[0].time;
                max = curve[curve.length - 1].time;
                float step = (max - min) / (k_ExposureCurvePrecision - 1f);

                for (int i = 0; i < k_ExposureCurvePrecision; i++)
                {
                    float currTime = min + step * i;
                    pixels[i] = new Color(curve.Evaluate(currTime),
                        minCurveHasPoints ? minCurve.Evaluate(currTime) : defaultMin,
                        maxCurveHasPoints ? maxCurve.Evaluate(currTime) : defaultMax,
                        0f);
                }
            }

            m_ExposureCurveTexture.SetPixels(pixels);
            m_ExposureCurveTexture.Apply();
            m_ExposureCurveTextureRT = RTHandles.Alloc(m_ExposureCurveTexture);
        }

        void ComputeProceduralMeteringParams(UniversalCameraData camera, out Vector4 proceduralParams1, out Vector4 proceduralParams2)
        {
            Vector2 proceduralCenter = m_Exposure.proceduralCenter.value;
            // if (camera.exposureTarget != null && m_Exposure.centerAroundExposureTarget.value)
            // {
            //     var transform = camera.exposureTarget.transform;
            //     // Transform in screen space
            //     Vector3 targetLocation = transform.position;
            //     if (ShaderConfig.s_CameraRelativeRendering != 0)
            //     {
            //         targetLocation -= camera.camera.transform.position;
            //     }
            //     var ndcLoc = camera.mainViewConstants.viewProjMatrix * (targetLocation);
            //     ndcLoc.x /= ndcLoc.w;
            //     ndcLoc.y /= ndcLoc.w;
            //
            //     Vector2 targetUV = new Vector2(ndcLoc.x, ndcLoc.y) * 0.5f + new Vector2(0.5f, 0.5f);
            //     targetUV.y = 1.0f - targetUV.y;
            //
            //     proceduralCenter += targetUV;
            // }

            proceduralCenter.x = Mathf.Clamp01(proceduralCenter.x);
            proceduralCenter.y = Mathf.Clamp01(proceduralCenter.y);

            proceduralCenter.x *= camera.scaledWidth;
            proceduralCenter.y *= camera.scaledHeight;

            float screenDiagonal = 0.5f * (camera.scaledWidth + camera.scaledHeight);

            proceduralParams1 = new Vector4(proceduralCenter.x, proceduralCenter.y,
                m_Exposure.proceduralRadii.value.x * camera.scaledWidth,
                m_Exposure.proceduralRadii.value.y * camera.scaledHeight);

            proceduralParams2 = new Vector4(1.0f / m_Exposure.proceduralSoftness.value, LightUnitUtils.Ev100ToNits(m_Exposure.maskMinIntensity.value),
                LightUnitUtils.Ev100ToNits(m_Exposure.maskMaxIntensity.value), 0.0f);
        }


        static void ValidateComputeBuffer(ref ComputeBuffer cb, int size, int stride, ComputeBufferType type = ComputeBufferType.Default)
        {
            if (cb == null || cb.count < size)
            {
                CoreUtils.SafeRelease(cb);
                cb = new ComputeBuffer(size, stride, type);
            }
        }


        void GrabExposureRequiredTextures(UniversalCameraData cameraData, out RTHandle prevExposure, out RTHandle nextExposure)
        {
            var historyRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);
            ReAllocatedExposureTexturesIfNeeded(historyRTSystem, out var prev, out var curr);


            prevExposure = prev;
            nextExposure = curr;
            if (historyRTSystem.historyFrameCount <= 1)
            {
                // For Dynamic Exposure, we need to undo the pre-exposure from the color buffer to calculate the correct one
                // When we reset history we must setup neutral value
                prevExposure = m_EmptyExposureTexture; // Use neutral texture
            }
        }


        void PrepareExposurePassData(RenderGraph renderGraph, IComputeRenderGraphBuilder builder, UniversalCameraData cameraData, TextureHandle source,
            DynamicExposureData passData)
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ExposureRuntimeShader>();


            passData.exposureCS = runtimeShaders.exposureCS;
            passData.histogramExposureCS = runtimeShaders.histogramExposureCS;
            passData.histogramExposureCS.shaderKeywords = null;

            passData.camera = cameraData;
            passData.viewportSize = new Vector2Int(cameraData.scaledWidth, cameraData.scaledHeight);

            // Setup variants
            var adaptationMode = m_Exposure.adaptationMode.value;

            // if (!hdCamera.animateMaterials || hdCamera.resetPostProcessingHistory)
            //     adaptationMode = AdaptationMode.Fixed;

            passData.exposureVariants = m_ExposureVariants;
            passData.exposureVariants[0] = 1; // (int)exposureSettings.luminanceSource.value;
            passData.exposureVariants[1] = (int)m_Exposure.meteringMode.value;
            passData.exposureVariants[2] = (int)adaptationMode;
            passData.exposureVariants[3] = 0;

            bool useTextureMask = m_Exposure.meteringMode.value == MeteringMode.MaskWeighted && m_Exposure.weightTextureMask.value != null;
            passData.textureMeteringMask = useTextureMask
                ? renderGraph.ImportTexture(RTHandles.Alloc(m_Exposure.weightTextureMask.value))
                : renderGraph.defaultResources.whiteTexture;

            ComputeProceduralMeteringParams(cameraData, out passData.proceduralMaskParams, out passData.proceduralMaskParams2);

            bool isHistogramBased = m_Exposure.mode.value == ExposureMode.AutomaticHistogram;
            bool needsCurve = (isHistogramBased && m_Exposure.histogramUseCurveRemapping.value) || m_Exposure.mode.value == ExposureMode.CurveMapping;

            passData.histogramUsesCurve = m_Exposure.histogramUseCurveRemapping.value;

            // When recording with accumulation, unity_DeltaTime is adjusted to account for the subframes.
            // To match the ganeview's exposure adaptation when recording, we adjust similarly the speed.
            float speedMultiplier = 1.0f;
            passData.adaptationParams = new Vector4(m_Exposure.adaptationSpeedLightToDark.value * speedMultiplier,
                m_Exposure.adaptationSpeedDarkToLight.value * speedMultiplier, 0.0f, 0.0f);

            passData.exposureMode = m_Exposure.mode.value;

            float limitMax = m_Exposure.limitMax.value;
            float limitMin = m_Exposure.limitMin.value;

            float curveMin = 0.0f;
            float curveMax = 0.0f;
            if (needsCurve)
            {
                PrepareExposureCurveData(out curveMin, out curveMax);
                limitMin = curveMin;
                limitMax = curveMax;
            }

            passData.exposureParams = new Vector4(m_Exposure.compensation.value, limitMin, limitMax, 0f);
            passData.exposureParams2 = new Vector4(curveMin, curveMax, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant);

            passData.exposureCurve = renderGraph.ImportTexture(m_ExposureCurveTextureRT);

            if (isHistogramBased)
            {
                ValidateComputeBuffer(ref m_HistogramBuffer, k_HistogramBins, sizeof(uint));
                m_HistogramBuffer.SetData(m_EmptyHistogram); // Clear the histogram

                Vector2 histogramFraction = m_Exposure.histogramPercentages.value / 100.0f;
                float evRange = limitMax - limitMin;
                float histScale = 1.0f / Mathf.Max(1e-5f, evRange);
                float histBias = -limitMin * histScale;
                passData.histogramExposureParams = new Vector4(histScale, histBias, histogramFraction.x, histogramFraction.y);

                passData.histogramBuffer = m_HistogramBuffer;
                // passData.histogramOutputDebugData =
                //     m_CurrentDebugDisplaySettings.data.lightingDebugSettings.exposureDebugMode == ExposureDebugMode.HistogramView;
                // if (passData.histogramOutputDebugData)
                // {
                //     passData.histogramExposureCS.EnableKeyword("OUTPUT_DEBUG_DATA");
                // }

                passData.exposurePreparationKernel = passData.histogramExposureCS.FindKernel("KHistogramGen");
                passData.exposureReductionKernel = passData.histogramExposureCS.FindKernel("KHistogramReduce");
            }
            else
            {
                passData.exposurePreparationKernel = passData.exposureCS.FindKernel("KPrePass");
                passData.exposureReductionKernel = passData.exposureCS.FindKernel("KReduction");
            }

            GrabExposureRequiredTextures(cameraData, out var prevExposure, out var nextExposure);

            passData.source = source;
            passData.prevExposure = renderGraph.ImportTexture(prevExposure);
            passData.nextExposure = renderGraph.ImportTexture(nextExposure);
            passData.blackTexture = renderGraph.defaultResources.blackTexture;

            builder.UseTexture(passData.blackTexture);
            builder.UseTexture(passData.exposureCurve);
            builder.UseTexture(passData.source);
            builder.UseTexture(passData.prevExposure);
            builder.UseTexture(passData.nextExposure);
        }


        static void DoHistogramBasedExposure(DynamicExposureData data, ComputeCommandBuffer cmd)
        {
            var cs = data.histogramExposureCS;
            int kernel;

            cmd.SetComputeVectorParam(cs, ShaderIDs._ProceduralMaskParams, data.proceduralMaskParams);
            cmd.SetComputeVectorParam(cs, ShaderIDs._ProceduralMaskParams2, data.proceduralMaskParams2);

            cmd.SetComputeVectorParam(cs, ShaderIDs._HistogramExposureParams, data.histogramExposureParams);

            // Generate histogram.
            kernel = data.exposurePreparationKernel;
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._PreviousExposureTexture, data.prevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._SourceTexture, data.source);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._ExposureWeightMask, data.textureMeteringMask);

            cmd.SetComputeIntParams(cs, ShaderIDs._Variants, data.exposureVariants);

            cmd.SetComputeBufferParam(cs, kernel, ShaderIDs._HistogramBuffer, data.histogramBuffer);

            int threadGroupSizeX = 16;
            int threadGroupSizeY = 8;
            int dispatchSizeX = RenderingUtilsExt.DivRoundUp(data.viewportSize.x / 2, threadGroupSizeX);
            int dispatchSizeY = RenderingUtilsExt.DivRoundUp(data.viewportSize.y / 2, threadGroupSizeY);

            cmd.DispatchCompute(cs, kernel, dispatchSizeX, dispatchSizeY, 1);

            // Now read the histogram
            kernel = data.exposureReductionKernel;
            cmd.SetComputeVectorParam(cs, ShaderIDs._ExposureParams, data.exposureParams);
            cmd.SetComputeVectorParam(cs, ShaderIDs._ExposureParams2, data.exposureParams2);
            cmd.SetComputeVectorParam(cs, ShaderIDs._AdaptationParams, data.adaptationParams);
            cmd.SetComputeBufferParam(cs, kernel, ShaderIDs._HistogramBuffer, data.histogramBuffer);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._PreviousExposureTexture, data.prevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._OutputTexture, data.nextExposure);

            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._ExposureCurveTexture, data.exposureCurve);
            data.exposureVariants[3] = 0;
            if (data.histogramUsesCurve)
            {
                data.exposureVariants[3] = 2;
            }

            cmd.SetComputeIntParams(cs, ShaderIDs._Variants, data.exposureVariants);

            if (data.histogramOutputDebugData)
            {
                cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._ExposureDebugTexture, data.exposureDebugData);
            }

            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }

        static void DoDynamicExposure(DynamicExposureData data, ComputeCommandBuffer cmd)
        {
            var cs = data.exposureCS;
            int kernel;

            kernel = data.exposurePreparationKernel;
            cmd.SetComputeIntParams(cs, ShaderIDs._Variants, data.exposureVariants);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._PreviousExposureTexture, data.prevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._SourceTexture, data.source);
            cmd.SetComputeVectorParam(cs, ShaderIDs._ExposureParams2, data.exposureParams2);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._ExposureWeightMask, data.textureMeteringMask);

            cmd.SetComputeVectorParam(cs, ShaderIDs._ProceduralMaskParams, data.proceduralMaskParams);
            cmd.SetComputeVectorParam(cs, ShaderIDs._ProceduralMaskParams2, data.proceduralMaskParams2);

            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._OutputTexture, data.tmpTarget1024);
            cmd.DispatchCompute(cs, kernel, 1024 / 8, 1024 / 8, 1);

            // Reduction: 1st pass (1024 -> 32)
            kernel = data.exposureReductionKernel;
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._PreviousExposureTexture, data.prevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._ExposureCurveTexture, data.blackTexture);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._InputTexture, data.tmpTarget1024);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._OutputTexture, data.tmpTarget32);
            cmd.DispatchCompute(cs, kernel, 32, 32, 1);

            cmd.SetComputeVectorParam(cs, ShaderIDs._ExposureParams, data.exposureParams);

            // Reduction: 2nd pass (32 -> 1) + evaluate exposure
            if (data.exposureMode == ExposureMode.Automatic)
            {
                data.exposureVariants[3] = 1;
            }
            else if (data.exposureMode == ExposureMode.CurveMapping)
            {
                cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._ExposureCurveTexture, data.exposureCurve);
                data.exposureVariants[3] = 2;
            }

            cmd.SetComputeVectorParam(cs, ShaderIDs._AdaptationParams, data.adaptationParams);
            cmd.SetComputeIntParams(cs, ShaderIDs._Variants, data.exposureVariants);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._PreviousExposureTexture, data.prevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._InputTexture, data.tmpTarget32);
            cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._OutputTexture, data.nextExposure);
            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }


        void DynamicExposurePass(RenderGraph renderGraph, ContextContainer frameData) //, HDCamera hdCamera, TextureHandle source)
        {
            // Dynamic exposure - will be applied in the next frame
            // Not considered as a post-process so it's not affected by its enabled state

            var resourceData = frameData.Get<UniversalResourceData>();

            var source = resourceData.cameraColor;

            TextureHandle exposureForImmediateApplication = TextureHandle.nullHandle;
            // if (!IsExposureFixed(hdCamera) && hdCamera.exposureControlFS)
            {
                using (var builder = renderGraph.AddComputePass<DynamicExposureData>("Dynamic Exposure", out var passData, Profiling.DynamicExposure))
                {
                    var cameraData = frameData.Get<UniversalCameraData>();
                    PrepareExposurePassData(renderGraph, builder, cameraData, source, passData);

                    builder.AllowPassCulling(false);
                    if (m_Exposure.mode.value == ExposureMode.AutomaticHistogram)
                    {
                        passData.exposureDebugData = renderGraph.ImportTexture(m_DebugExposureData);
                        builder.UseTexture(passData.exposureDebugData, AccessFlags.Write);
                        builder.SetRenderFunc<DynamicExposureData>((data, ctx) => { DoHistogramBasedExposure(data, ctx.cmd); });
                        exposureForImmediateApplication = passData.nextExposure;
                    }
                    else
                    {
                        passData.tmpTarget1024 = builder.CreateTransientTexture(new TextureDesc(1024, 1024, false, false)
                            { format = GraphicsFormat.R16G16_SFloat, enableRandomWrite = true, name = "Average Luminance Temp 1024" });
                        passData.tmpTarget32 = builder.CreateTransientTexture(new TextureDesc(32, 32, false, false)
                            { format = GraphicsFormat.R16G16_SFloat, enableRandomWrite = true, name = "Average Luminance Temp 32" });

                        builder.SetRenderFunc<DynamicExposureData>((data, ctx) => { DoDynamicExposure(data, ctx.cmd); });
                        exposureForImmediateApplication = passData.nextExposure;
                    }
                }

                // if (hdCamera.resetPostProcessingHistory)
                {
                    using (var builder = renderGraph.AddComputePass<ApplyExposureData>("Apply Exposure", out var passData,
                               Profiling.ApplyExposure))
                    {
                        var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ExposureRuntimeShader>();
                        var cameraData = frameData.Get<UniversalCameraData>();

                        passData.applyExposureCS = runtimeShaders.applyExposureCS;
                        passData.applyExposureCS.shaderKeywords = null;
                        passData.applyExposureKernel = passData.applyExposureCS.FindKernel("KMain");

                        // if (PostProcessEnableAlpha(hdCamera))
                        //     passData.applyExposureCS.EnableKeyword("ENABLE_ALPHA");

                        passData.width = cameraData.scaledWidth;
                        passData.height = cameraData.scaledHeight;
                        passData.viewCount = 1;
                        passData.source = source;

                        passData.prevExposure = exposureForImmediateApplication;
                        builder.AllowPassCulling(false);

                        TextureHandle dest = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                        {
                            name = "Apply Exposure Destination",
                            format = GraphicsFormat.B10G11R11_UFloatPack32,
                            enableRandomWrite = true
                        });
                        passData.destination = dest;


                        builder.UseTexture(passData.source);
                        builder.UseTexture(passData.prevExposure);
                        builder.UseTexture(passData.destination, AccessFlags.Write);

                        builder.SetRenderFunc<ApplyExposureData>((data, ctx) =>
                        {
                            ctx.cmd.SetComputeTextureParam(data.applyExposureCS, data.applyExposureKernel, ShaderIDs._ExposureTexture, data.prevExposure);
                            ctx.cmd.SetComputeTextureParam(data.applyExposureCS, data.applyExposureKernel, ShaderIDs._InputTexture, data.source);
                            ctx.cmd.SetComputeTextureParam(data.applyExposureCS, data.applyExposureKernel, ShaderIDs._OutputTexture, data.destination);
                            ctx.cmd.DispatchCompute(data.applyExposureCS, data.applyExposureKernel, (data.width + 7) / 8, (data.height + 7) / 8,
                                data.viewCount);
                        });

                        resourceData.cameraColor = passData.destination;
                    }
                }
            }
        }
    }
}