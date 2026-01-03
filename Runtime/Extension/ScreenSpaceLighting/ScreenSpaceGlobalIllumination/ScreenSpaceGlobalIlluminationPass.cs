using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public class ScreenSpaceGlobalIlluminationPass : ScriptableRenderPass
    {
        // #region ShaderID
        //
        // public static readonly int _RayMarchingThicknessScale = Shader.PropertyToID("_RayMarchingThicknessScale");
        // public static readonly int _RayMarchingThicknessBias = Shader.PropertyToID("_RayMarchingThicknessBias");
        // public static readonly int _RayMarchingSteps = Shader.PropertyToID("_RayMarchingSteps");
        // public static readonly int _RayMarchingReflectSky = Shader.PropertyToID("_RayMarchingReflectSky");
        // public static readonly int _RayMarchingFallbackHierarchy = Shader.PropertyToID("_RayMarchingFallbackHierarchy");
        // public static readonly int _RayMarchingLowResPercentageInv = Shader.PropertyToID("_RayMarchingLowResPercentageInv");
        // public static readonly int _RayMarchingLowResPercentage = Shader.PropertyToID("_RayMarchingLowResPercentage");
        // public static readonly int _SSGILayerMask = Shader.PropertyToID("_SSGILayerMask");
        // public static readonly int _NormalBufferTexture = Shader.PropertyToID("_NormalBufferTexture");
        // public static readonly int _ShaderVariablesRaytracing = Shader.PropertyToID("_ShaderVariablesRaytracing");
        // public static readonly int _IndirectDiffuseFrameIndex = Shader.PropertyToID("_IndirectDiffuseFrameIndex");
        // public static readonly int _DepthTexture = Shader.PropertyToID("_DepthTexture");
        // public static readonly int _IndirectDiffuseHitPointTextureRW = Shader.PropertyToID("_IndirectDiffuseHitPointTextureRW");
        // public static readonly int _CameraMotionVectorsTexture = Shader.PropertyToID("_CameraMotionVectorsTexture");
        // public static readonly int _IndirectDiffuseHitPointTexture = Shader.PropertyToID("_IndirectDiffuseHitPointTexture");
        // public static readonly int _ColorPyramidTexture = Shader.PropertyToID("_ColorPyramidTexture");
        // public static readonly int _IndirectDiffuseTextureRW = Shader.PropertyToID("_IndirectDiffuseTextureRW");
        // public static readonly int _ShaderVariablesBilateralUpsample = Shader.PropertyToID("_ShaderVariablesBilateralUpsample");
        // public static readonly int _HistoryDepthTexture = Shader.PropertyToID("_HistoryDepthTexture");
        // public static readonly int _LowResolutionTexture = Shader.PropertyToID("_LowResolutionTexture");
        // public static readonly int _HalfScreenSize = Shader.PropertyToID("_HalfScreenSize");
        // public static readonly int _OutputUpscaledTexture = Shader.PropertyToID("_OutputUpscaledTexture");
        // public static readonly int _GBuffer0 = Shader.PropertyToID("_GBuffer0");
        // public static readonly int _GBuffer1 = Shader.PropertyToID("_GBuffer1");
        // public static readonly int _IndirectDiffuseTexture = Shader.PropertyToID("_IndirectDiffuseTexture");
        // public static readonly int _SceneColor = Shader.PropertyToID("_SceneColor");
        // public static readonly int _OuputSceneColor = Shader.PropertyToID("_OuputSceneColor");
        //
        // // public static readonly int _IndirectDiffuseTextureRW = Shader.PropertyToID("_IndirectDiffuseTextureRW");
        // // public static readonly int _HistoryDepthTexture = Shader.PropertyToID("_HistoryDepthTexture");
        //
        // #endregion
        //
        //
        // int m_TraceGlobalIlluminationKernel;
        // int m_TraceGlobalIlluminationHalfKernel;
        // int m_ReprojectGlobalIlluminationKernel;
        // int m_ReprojectGlobalIlluminationHalfKernel;
        // int m_BilateralUpSampleColorKernelHalf;
        // int m_BilateralUpSampleColorKernel;
        // int m_CombineGIKernel;
        //
        // void InitScreenSpaceGlobalIllumination()
        // {
        //     var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ScreenSpaceGlobalIlluminationRuntimeShader>();
        //
        //     // Grab the sets of shaders that we'll be using
        //     ComputeShader ssGICS = runtimeShaders.screenSpaceGlobalIlluminationCS;
        //     ComputeShader bilateralUpsampleCS = runtimeShaders.bilateralUpsampleCS;
        //
        //     // Grab the set of kernels that we shall be using
        //     m_TraceGlobalIlluminationKernel = ssGICS.FindKernel("TraceGlobalIllumination");
        //     m_TraceGlobalIlluminationHalfKernel = ssGICS.FindKernel("TraceGlobalIlluminationHalf");
        //     m_ReprojectGlobalIlluminationKernel = ssGICS.FindKernel("ReprojectGlobalIllumination");
        //     m_ReprojectGlobalIlluminationHalfKernel = ssGICS.FindKernel("ReprojectGlobalIlluminationHalf");
        //     m_BilateralUpSampleColorKernelHalf = bilateralUpsampleCS.FindKernel("BilateralUpSampleColorHalf");
        //     m_BilateralUpSampleColorKernel = bilateralUpsampleCS.FindKernel("BilateralUpSampleColor");
        //
        //     m_CombineGIKernel = runtimeShaders.combineGICS.FindKernel("CombineGI");
        // }
        //
        // #region Tracing
        //
        // class TraceSSGIPassData
        // {
        //     // Camera parameters
        //     public int texWidth;
        //     public int texHeight;
        //     public int viewCount;
        //     public Vector4 halfScreenSize;
        //
        //     // Generation parameters
        //     public float nearClipPlane;
        //     public float farClipPlane;
        //     public bool fullResolutionSS;
        //     public float thickness;
        //     public int raySteps;
        //     public int frameIndex;
        //     public int rayMiss;
        //     public UnityEngine.RenderingLayerMask apvLayerMask;
        //     public float lowResPercentage;
        //
        //     // Compute Shader
        //     public ComputeShader ssGICS;
        //     public int traceKernel;
        //     public int projectKernel;
        //
        //     // Other parameters
        //     public RuntimeTextureSystem.DitheredTextureHandleSet ditheredTextureSet;
        //     public ShaderVariablesRaytracing shaderVariablesRayTracingCB;
        //
        //     // Prepass buffers
        //     public BufferHandle lightList;
        //     public TextureHandle depthTexture;
        //     public TextureHandle stencilBuffer;
        //     public TextureHandle normalBuffer;
        //     public TextureHandle motionVectorsBuffer;
        //
        //     // History buffers
        //     public TextureHandle colorPyramid;
        //     public TextureHandle historyDepth;
        //
        //     // Intermediate buffers
        //     public TextureHandle hitPointBuffer;
        //
        //     // Input signal buffers
        //     public TextureHandle outputBuffer;
        // }
        //
        //
        // TextureHandle TraceSSGI(RenderGraph renderGraph, UniversalCameraData cameraData, GlobalIllumination giSettings, TextureHandle depthPyramid,
        //     TextureHandle normalBuffer, /*TextureHandle stencilBuffer,*/ TextureHandle motionVectorsBuffer) //, BufferHandle lightList)
        // {
        //     using (var builder = renderGraph.AddComputePass<TraceSSGIPassData>("Trace SSGI", out var passData))
        //     {
        //         builder.EnableAsyncCompute(false);
        //
        //         var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ScreenSpaceGlobalIlluminationRuntimeShader>();
        //
        //
        //         if (giSettings.fullResolutionSS.value)
        //         {
        //             passData.lowResPercentage = 1.0f;
        //             passData.texWidth = cameraData.scaledWidth;
        //             passData.texHeight = cameraData.scaledHeight;
        //         }
        //         else
        //         {
        //             float ssgiLowResMultiplier = 0.5f;
        //             passData.lowResPercentage = ssgiLowResMultiplier;
        //             passData.texWidth = (int)Mathf.Floor(cameraData.scaledWidth * ssgiLowResMultiplier);
        //             passData.texHeight = (int)Mathf.Floor(cameraData.scaledHeight * ssgiLowResMultiplier);
        //         }
        //
        //
        //         passData.viewCount = 1;
        //
        //         var historyFrameRTSystem = cameraData.historyFrameRTSystem;
        //         // Set the generation parameters
        //         passData.nearClipPlane = cameraData.camera.nearClipPlane;
        //         passData.farClipPlane = cameraData.camera.farClipPlane;
        //         passData.fullResolutionSS = true;
        //         passData.thickness = giSettings.depthBufferThickness.value;
        //         passData.raySteps = giSettings.maxRaySteps;
        //         passData.frameIndex = historyFrameRTSystem.historyFrameCount % 16;
        //         passData.rayMiss = (int)giSettings.rayMiss.value;
        //         passData.apvLayerMask = giSettings.adaptiveProbeVolumesLayerMask.value;
        //
        //         // Grab the right kernel
        //         passData.ssGICS = runtimeShaders.screenSpaceGlobalIlluminationCS;
        //         passData.traceKernel = giSettings.fullResolutionSS.value ? m_TraceGlobalIlluminationKernel : m_TraceGlobalIlluminationHalfKernel;
        //         passData.projectKernel = giSettings.fullResolutionSS.value ? m_ReprojectGlobalIlluminationKernel : m_ReprojectGlobalIlluminationHalfKernel;
        //
        //         var blueNoise = RuntimeTextureSystem.instance;
        //         var rayTracingSettings = VolumeManager.instance.stack.GetComponent<RayTracingSettings>();
        //
        //         passData.ditheredTextureSet = blueNoise.DitheredTextureSet8SPP().RenderGraphImport(renderGraph);
        //         passData.shaderVariablesRayTracingCB =  RayTracingSystem.instance.GetShaderVariablesRaytracingCB(cameraData);
        //
        //
        //         // passData.lightList = (lightList);
        //         passData.depthTexture = (depthPyramid);
        //         passData.normalBuffer = (normalBuffer);
        //         // passData.stencilBuffer = (stencilBuffer);
        //         passData.motionVectorsBuffer = (motionVectorsBuffer);
        //
        //
        //         // builder.UseBuffer(passData.lightList);
        //         builder.UseTexture(passData.depthTexture);
        //         builder.UseTexture(passData.normalBuffer);
        //         // builder.UseTexture(passData.stencilBuffer);
        //         builder.UseTexture(passData.motionVectorsBuffer);
        //
        //
        //         // History buffers
        //         var colorPyramid = historyFrameRTSystem.GetPreviousFrameRT(HistoryFrameType.ColorBufferMipChain);
        //         passData.colorPyramid = colorPyramid != null
        //             ? (renderGraph.ImportTexture(colorPyramid))
        //             : renderGraph.defaultResources.blackTexture;
        //         var historyDepth = historyFrameRTSystem.GetCurrentFrameRT(HistoryFrameType.Depth);
        //         passData.historyDepth = historyDepth != null
        //             ? (renderGraph.ImportTexture(historyDepth))
        //             : renderGraph.defaultResources.blackTexture;
        //
        //         builder.UseTexture(passData.colorPyramid);
        //         builder.UseTexture(passData.historyDepth);
        //
        //
        //         // Temporary textures
        //         passData.hitPointBuffer = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth,cameraData.scaledHeight)
        //             { format = GraphicsFormat.R16G16_SFloat, enableRandomWrite = true, name = "SSGI Hit Point" });
        //
        //         // Output textures
        //         passData.outputBuffer = (renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth,cameraData.scaledHeight)
        //             { format = GraphicsFormat.B10G11R11_UFloatPack32, enableRandomWrite = true, name = "SSGI Color" }));
        //         builder.UseTexture(passData.outputBuffer, AccessFlags.Write);
        //
        //
        //         passData.ditheredTextureSet.Use(builder);
        //
        //         builder.AllowGlobalStateModification(true);
        //         builder.SetRenderFunc<TraceSSGIPassData>((data, ctx) =>
        //         {
        //             int ssgiTileSize = 8;
        //             int numTilesXHR = (data.texWidth + (ssgiTileSize - 1)) / ssgiTileSize;
        //             int numTilesYHR = (data.texHeight + (ssgiTileSize - 1)) / ssgiTileSize;
        //
        //             // Inject all the input scalars
        //             float n = data.nearClipPlane;
        //             float f = data.farClipPlane;
        //             float thicknessScale = 1.0f / (1.0f + data.thickness);
        //             float thicknessBias = -n / (f - n) * (data.thickness * thicknessScale);
        //             ctx.cmd.SetComputeFloatParam(data.ssGICS, _RayMarchingThicknessScale, thicknessScale);
        //             ctx.cmd.SetComputeFloatParam(data.ssGICS, _RayMarchingThicknessBias, thicknessBias);
        //             ctx.cmd.SetComputeIntParam(data.ssGICS, _RayMarchingSteps, data.raySteps);
        //             ctx.cmd.SetComputeIntParam(data.ssGICS, _RayMarchingReflectSky, 1);
        //             ctx.cmd.SetComputeIntParam(data.ssGICS, _IndirectDiffuseFrameIndex, data.frameIndex);
        //
        //             // Inject the ray-tracing sampling data
        //
        //             RuntimeTextureSystem.BindDitheredTextureSet(ctx.cmd, data.ditheredTextureSet);
        //             // Inject all the input textures/buffers
        //             ctx.cmd.SetComputeTextureParam(data.ssGICS, data.traceKernel, _DepthTexture, data.depthTexture);
        //             ctx.cmd.SetComputeTextureParam(data.ssGICS, data.traceKernel, _NormalBufferTexture, data.normalBuffer);
        //             ctx.cmd.SetComputeTextureParam(data.ssGICS, data.traceKernel, _IndirectDiffuseHitPointTextureRW, data.hitPointBuffer);
        //             // ctx.cmd.SetComputeBufferParam(data.ssGICS, data.traceKernel, g_vLightListTile, data.lightList);
        //             ctx.cmd.SetComputeFloatParam(data.ssGICS, _RayMarchingLowResPercentageInv, 1.0f / data.lowResPercentage);
        //             ctx.cmd.SetComputeFloatParam(data.ssGICS, _SSGILayerMask, (uint)data.apvLayerMask);
        //
        //             // Do the ray marching
        //             ctx.cmd.DispatchCompute(data.ssGICS, data.traceKernel, numTilesXHR, numTilesYHR, data.viewCount);
        //
        //             // Update global constant buffer.
        //             // This should probably be a shader specific uniform instead of reusing the global constant buffer one since it's the only one updated here.
        //             ConstantBuffer.PushGlobal(ctx.cmd, data.shaderVariablesRayTracingCB, _ShaderVariablesRaytracing);
        //
        //             // Inject all the input scalars
        //             // ctx.cmd.SetComputeIntParam(data.ssGICS, HDShaderIDs._ObjectMotionStencilBit, (int)StencilUsage.ObjectMotionVector);
        //             ctx.cmd.SetComputeIntParam(data.ssGICS, _RayMarchingFallbackHierarchy, data.rayMiss);
        //             ctx.cmd.SetComputeFloatParam(data.ssGICS, _RayMarchingLowResPercentageInv, 1.0f / data.lowResPercentage);
        //
        //             // Bind all the input buffers
        //             ctx.cmd.SetComputeTextureParam(data.ssGICS, data.projectKernel, _DepthTexture, data.depthTexture);
        //             // ctx.cmd.SetComputeTextureParam(data.ssGICS, data.projectKernel, _StencilTexture, data.stencilBuffer, 0,
        //             //     RenderTextureSubElement.Stencil);
        //             ctx.cmd.SetComputeTextureParam(data.ssGICS, data.projectKernel, _NormalBufferTexture, data.normalBuffer);
        //             ctx.cmd.SetComputeTextureParam(data.ssGICS, data.projectKernel, _CameraMotionVectorsTexture, data.motionVectorsBuffer);
        //             ctx.cmd.SetComputeTextureParam(data.ssGICS, data.projectKernel, _IndirectDiffuseHitPointTexture, data.hitPointBuffer);
        //             ctx.cmd.SetComputeTextureParam(data.ssGICS, data.projectKernel, _ColorPyramidTexture, data.colorPyramid);
        //             ctx.cmd.SetComputeTextureParam(data.ssGICS, data.projectKernel, _HistoryDepthTexture, data.historyDepth);
        //             // ctx.cmd.SetComputeBufferParam(data.ssGICS, data.projectKernel, HDShaderIDs.g_vLightListTile, data.lightList);
        //
        //             // Bind the output texture
        //             ctx.cmd.SetComputeTextureParam(data.ssGICS, data.projectKernel, _IndirectDiffuseTextureRW, data.outputBuffer);
        //
        //             // Do the re-projection
        //             ctx.cmd.DispatchCompute(data.ssGICS, data.projectKernel, numTilesXHR, numTilesYHR, data.viewCount);
        //         });
        //
        //         return passData.outputBuffer;
        //     }
        // }
        //
        // #endregion
        //
        // #region Denoise
        //
        // static RTHandle IndirectDiffuseHistoryBufferAllocatorFunction(string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        // {
        //     return rtHandleSystem.Alloc(Vector2.one, colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
        //         enableRandomWrite: true, useMipMap: false, autoGenerateMips: false,
        //         name: string.Format("{0}_IndirectDiffuseHistoryBuffer{1}", viewName, frameIndex));
        // }
        //
        // static RTHandle RequestIndirectDiffuseHistoryTextureHF(UniversalCameraData cameraData)
        // {
        //     return cameraData.historyFrameRTSystem.GetCurrentFrameRT(HistoryFrameType.RaytracedIndirectDiffuseHF)
        //            ?? cameraData.historyFrameRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.RaytracedIndirectDiffuseHF,
        //                IndirectDiffuseHistoryBufferAllocatorFunction, 1);
        // }
        //
        // static RTHandle RequestIndirectDiffuseHistoryTextureLF(UniversalCameraData cameraData)
        // {
        //     return cameraData.historyFrameRTSystem.GetCurrentFrameRT(HistoryFrameType.RaytracedIndirectDiffuseLF)
        //            ?? cameraData.historyFrameRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.RaytracedIndirectDiffuseLF,
        //                IndirectDiffuseHistoryBufferAllocatorFunction, 1);
        // }
        //
        //
        // TextureHandle DenoiseSSGI(RenderGraph renderGraph, UniversalCameraData cameraData, TextureHandle rtGIBuffer, TextureHandle depthPyramid,
        //     TextureHandle normalBuffer,
        //     TextureHandle motionVectorBuffer, TextureHandle historyValidationTexture)
        // {
        //     var giSettings = VolumeManager.instance.stack.GetComponent<GlobalIllumination>();
        //
        //     var fullResolution = giSettings.fullResolutionSS.value;
        //     if (giSettings.denoiseSS)
        //     {
        //         // Evaluate the history's validity
        //         Vector2 resolutionPercentages = fullResolution ? Vector2.one : new Vector2(0.5f, 0.5f);
        //         var denoiseSystem = DenoiseSystem.GetOrCreate(cameraData.camera);
        //         var temporalFilter = denoiseSystem.temporalDenoiser;
        //         var diffuseDenoiser = denoiseSystem.spatialDenoiser;
        //
        //         // Run the temporal denoiser
        //         TextureHandle historyBufferHF = renderGraph.ImportTexture(RequestIndirectDiffuseHistoryTextureHF(cameraData));
        //         TemporalFilter.TemporalFilterParameters filterParams;
        //         filterParams.singleChannel = false;
        //         filterParams.historyValidity = 1;
        //         filterParams.occluderMotionRejection = false;
        //         filterParams.receiverMotionRejection = false;
        //         filterParams.exposureControl = true;
        //         filterParams.resolutionMultiplier = resolutionPercentages.x;
        //         filterParams.historyResolutionMultiplier = resolutionPercentages.y;
        //         TextureHandle denoisedRTGI = temporalFilter.Denoise(renderGraph, cameraData, filterParams,
        //             rtGIBuffer,
        //             renderGraph.defaultResources.blackTexture, historyBufferHF, depthPyramid, normalBuffer, motionVectorBuffer, historyValidationTexture);
        //
        //         // Apply the diffuse denoiser
        //         SpatialDenoiser.DiffuseDenoiserParameters ddParams;
        //         ddParams.singleChannel = false;
        //         ddParams.kernelSize = giSettings.denoiserRadiusSS;
        //         ddParams.halfResolutionFilter = giSettings.halfResolutionDenoiserSS;
        //         ddParams.jitterFilter = giSettings.secondDenoiserPassSS;
        //         ddParams.resolutionMultiplier = resolutionPercentages.x;
        //         rtGIBuffer = diffuseDenoiser.Denoise(renderGraph, cameraData, ddParams, denoisedRTGI, depthPyramid, normalBuffer, rtGIBuffer);
        //
        //         // If the second pass is requested, do it otherwise blit
        //         if (giSettings.secondDenoiserPassSS)
        //         {
        //             // Run the temporal filter
        //             TextureHandle historyBufferLF = renderGraph.ImportTexture(RequestIndirectDiffuseHistoryTextureLF(cameraData));
        //             filterParams.singleChannel = false;
        //             filterParams.historyValidity = 1;
        //             filterParams.occluderMotionRejection = false;
        //             filterParams.receiverMotionRejection = false;
        //             filterParams.exposureControl = true;
        //             filterParams.resolutionMultiplier = resolutionPercentages.x;
        //             filterParams.historyResolutionMultiplier = resolutionPercentages.y;
        //             denoisedRTGI = temporalFilter.Denoise(renderGraph, cameraData, filterParams, rtGIBuffer, renderGraph.defaultResources.blackTexture,
        //                 historyBufferLF, depthPyramid, normalBuffer, motionVectorBuffer, historyValidationTexture);
        //
        //             // Apply the diffuse filter
        //             ddParams.singleChannel = false;
        //             ddParams.kernelSize = giSettings.denoiserRadiusSS * 0.5f;
        //             ddParams.halfResolutionFilter = giSettings.halfResolutionDenoiserSS;
        //             ddParams.jitterFilter = false;
        //             ddParams.resolutionMultiplier = resolutionPercentages.x;
        //             rtGIBuffer = diffuseDenoiser.Denoise(renderGraph, cameraData, ddParams, denoisedRTGI, depthPyramid, normalBuffer, rtGIBuffer);
        //
        //             // Propagate the history validity for the second buffer
        //             // PropagateIndirectDiffuseHistoryValidity1(cameraData, fullResolution, false);
        //         }
        //
        //         // Propagate the history validity for the first buffer
        //         // PropagateIndirectDiffuseHistoryValidity0(cameraData, fullResolution, false);
        //
        //         return rtGIBuffer;
        //     }
        //     else
        //         return rtGIBuffer;
        // }
        //
        // #endregion
        //
        // #region Upscale
        //
        // class UpscaleSSGIPassData
        // {
        //     // Camera parameters
        //     public int texWidth;
        //     public int texHeight;
        //     public int viewCount;
        //     public Vector4 halfScreenSize;
        //     public float lowResPercentage;
        //     public ShaderVariablesBilateralUpsample shaderVariablesBilateralUpsampleCB;
        //
        //     // Compute Shader
        //     public ComputeShader bilateralUpsampleCS;
        //     public int upscaleKernel;
        //
        //     public TextureHandle depthTexture;
        //     public TextureHandle inputBuffer;
        //     public TextureHandle outputBuffer;
        // }
        //
        // TextureHandle UpscaleSSGI(RenderGraph renderGraph, UniversalCameraData cameraData,
        //     TextureHandle depthPyramid, TextureHandle inputBuffer)
        // {
        //     using (var builder = renderGraph.AddComputePass<UpscaleSSGIPassData>("Upscale SSGI", out var passData))
        //     {
        //         var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ScreenSpaceGlobalIlluminationRuntimeShader>();
        //         builder.EnableAsyncCompute(false);
        //
        //         // Set the camera parameters
        //         passData.texWidth = cameraData.scaledWidth;
        //         passData.texHeight = cameraData.scaledHeight;
        //         passData.viewCount = 1;
        //
        //         float lowResPercentage = 0.5f;
        //         passData.shaderVariablesBilateralUpsampleCB._HalfScreenSize = new Vector4(passData.texWidth / lowResPercentage,
        //             passData.texHeight / lowResPercentage, 1.0f / (passData.texWidth * lowResPercentage), 1.0f / (passData.texHeight * lowResPercentage));
        //         passData.lowResPercentage = lowResPercentage;
        //         unsafe
        //         {
        //             for (int i = 0; i < 16; ++i)
        //                 passData.shaderVariablesBilateralUpsampleCB._DistanceBasedWeights[i] = BilateralUpsample.distanceBasedWeights_2x2[i];
        //
        //             for (int i = 0; i < 32; ++i)
        //                 passData.shaderVariablesBilateralUpsampleCB._TapOffsets[i] = BilateralUpsample.tapOffsets_2x2[i];
        //         }
        //
        //         // Setup lowres bounds.
        //         int lowResWidth = (int)Mathf.Floor(passData.texWidth * passData.lowResPercentage);
        //         int lowResHeight = (int)Mathf.Floor(passData.texHeight * passData.lowResPercentage);
        //         passData.halfScreenSize.Set(lowResWidth, lowResHeight, 1.0f / lowResWidth, 1.0f / lowResHeight);
        //
        //         // Grab the right kernel
        //         passData.bilateralUpsampleCS = runtimeShaders.bilateralUpsampleCS;
        //         passData.upscaleKernel = passData.lowResPercentage == 0.5f ? m_BilateralUpSampleColorKernelHalf : m_BilateralUpSampleColorKernel;
        //
        //         passData.depthTexture = depthPyramid;
        //         passData.inputBuffer = inputBuffer;
        //
        //
        //         passData.outputBuffer = renderGraph.CreateTexture(new TextureDesc(Vector2.one, true)
        //             { format = GraphicsFormat.B10G11R11_UFloatPack32, enableRandomWrite = true, name = "SSGI Final" });
        //
        //         builder.UseTexture(passData.depthTexture);
        //         builder.UseTexture(passData.inputBuffer);
        //
        //         builder.UseTexture(passData.outputBuffer, AccessFlags.Write);
        //         builder.AllowGlobalStateModification(true);
        //         builder.SetRenderFunc((UpscaleSSGIPassData data, ComputeGraphContext ctx) =>
        //         {
        //             // Re-evaluate the dispatch parameters (we are evaluating the upsample in full resolution)
        //             int ssgiTileSize = 8;
        //             int numTilesXHR = (data.texWidth + (ssgiTileSize - 1)) / ssgiTileSize;
        //             int numTilesYHR = (data.texHeight + (ssgiTileSize - 1)) / ssgiTileSize;
        //
        //             ConstantBuffer.PushGlobal(ctx.cmd, data.shaderVariablesBilateralUpsampleCB, _ShaderVariablesBilateralUpsample);
        //
        //             ctx.cmd.SetComputeFloatParam(data.bilateralUpsampleCS, _RayMarchingLowResPercentage, data.lowResPercentage);
        //
        //             // Inject all the input buffers
        //             ctx.cmd.SetComputeTextureParam(data.bilateralUpsampleCS, data.upscaleKernel, _DepthTexture, data.depthTexture);
        //             ctx.cmd.SetComputeTextureParam(data.bilateralUpsampleCS, data.upscaleKernel, _LowResolutionTexture, data.inputBuffer);
        //             ctx.cmd.SetComputeVectorParam(data.bilateralUpsampleCS, _HalfScreenSize, data.halfScreenSize);
        //
        //             // Inject the output textures
        //             ctx.cmd.SetComputeTextureParam(data.bilateralUpsampleCS, data.upscaleKernel, _OutputUpscaledTexture, data.outputBuffer);
        //
        //             // Upscale the buffer to full resolution
        //             ctx.cmd.DispatchCompute(data.bilateralUpsampleCS, data.upscaleKernel, numTilesXHR, numTilesYHR, data.viewCount);
        //         });
        //         return passData.outputBuffer;
        //     }
        // }
        //
        // #endregion
        //
        // #region Combine
        //
        // class CombinePassData
        // {
        //     public int texWidth;
        //     public int texHeight;
        //     public int viewCount;
        //
        //     public ComputeShader combineGICS;
        //     public int combineKernel;
        //
        //     public TextureHandle indirectDiffuseTexture;
        //     public TextureHandle depthTexture;
        //     public TextureHandle Gbuffer0;
        //     public TextureHandle Gbuffer1;
        //     public TextureHandle sceneColor;
        //     public TextureHandle outputBuffer;
        //
        //     public float indirectDiffuseLightingMultiplier;
        // }
        //
        // //
        // TextureHandle CombineSSGI(RenderGraph renderGraph, UniversalCameraData cameraData,
        //     TextureHandle sceneColor,
        //     TextureHandle sceneDepth,
        //     TextureHandle[] gbuffers,
        //     TextureHandle indirectDiffuseTexture)
        // {
        //     using (var builder = renderGraph.AddComputePass<CombinePassData>("Combine SSGI", out var passData))
        //     {
        //         var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ScreenSpaceGlobalIlluminationRuntimeShader>();
        //         builder.EnableAsyncCompute(false);
        //
        //         // Set the camera parameters
        //         passData.texWidth = cameraData.scaledWidth;
        //         passData.texHeight = cameraData.scaledHeight;
        //         passData.viewCount = 1;
        //
        //         passData.combineGICS = runtimeShaders.combineGICS;
        //         passData.combineKernel = m_CombineGIKernel;
        //
        //         passData.sceneColor = sceneColor;
        //         passData.indirectDiffuseTexture = indirectDiffuseTexture;
        //         passData.indirectDiffuseLightingMultiplier = 1;
        //
        //
        //         passData.depthTexture = sceneDepth;
        //         passData.Gbuffer0 = gbuffers[0];
        //         passData.Gbuffer1 = gbuffers[1];
        //
        //         passData.outputBuffer = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
        //         {
        //             // clearBuffer = true,
        //             enableRandomWrite = true,
        //             name = "SSGI Final Color",
        //             format = UniversalRenderPipeline.asset.colorFormat,
        //         });
        //
        //         builder.UseTexture(passData.Gbuffer0);
        //         builder.UseTexture(passData.Gbuffer1);
        //         builder.UseTexture(passData.sceneColor);
        //         builder.UseTexture(passData.depthTexture);
        //         builder.UseTexture(passData.indirectDiffuseTexture);
        //         builder.UseTexture(passData.outputBuffer, AccessFlags.Write);
        //
        //         builder.SetRenderFunc<CombinePassData>((data, ctx) =>
        //         {
        //             int ssgiTileSize = 8;
        //             int numTilesXHR = (data.texWidth + (ssgiTileSize - 1)) / ssgiTileSize;
        //             int numTilesYHR = (data.texHeight + (ssgiTileSize - 1)) / ssgiTileSize;
        //
        //             var cs = data.combineGICS;
        //             var kernel = data.combineKernel;
        //             // Inject all the input buffers
        //             ctx.cmd.SetComputeTextureParam(cs, kernel, _DepthTexture, data.depthTexture);
        //             ctx.cmd.SetComputeTextureParam(cs, kernel, _GBuffer0, data.Gbuffer0);
        //             ctx.cmd.SetComputeTextureParam(cs, kernel, _GBuffer1, data.Gbuffer1);
        //             ctx.cmd.SetComputeTextureParam(cs, kernel, _SceneColor, data.sceneColor);
        //             ctx.cmd.SetComputeTextureParam(cs, kernel, _IndirectDiffuseTexture, data.indirectDiffuseTexture);
        //
        //
        //             ctx.cmd.SetComputeTextureParam(cs, kernel, _OuputSceneColor, data.outputBuffer);
        //             // Upscale the buffer to full resolution
        //             ctx.cmd.DispatchCompute(cs, kernel, numTilesXHR, numTilesYHR, data.viewCount);
        //         });
        //         return passData.outputBuffer;
        //     }
        // }
        //
        // #endregion
        //
        // public void Setup()
        // {
        //     InitScreenSpaceGlobalIllumination();
        //     ConfigureInput(ScriptableRenderPassInput.Motion);
        // }
        //
        // public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        // {
        //     var depthPyramid = MipGenerator.RenderMinDepthPyramidHDRP(renderGraph, frameData);
        //
        //     var resourceData = frameData.Get<UniversalResourceData>();
        //     var cameraData = frameData.Get<UniversalCameraData>();
        //
        //     var giSetting = VolumeManager.instance.stack.GetComponent<GlobalIllumination>();
        //     if (!giSetting.enable.value)
        //     {
        //         return;
        //     }
        //
        //     var colorBuffer = TraceSSGI(renderGraph, cameraData, giSetting, depthPyramid, resourceData.gBuffer[2], resourceData.motionVectorColor);
        //
        //     var denoisedSSGI = DenoiseSSGI(renderGraph, cameraData, colorBuffer,
        //         depthPyramid, resourceData.gBuffer[2], resourceData.motionVectorColor,
        //         renderGraph.ImportTexture(cameraData.historyFrameRTSystem.GetCurrentFrameRT(HistoryFrameType.HistoryValidity)));
        //
        //     if (!giSetting.fullResolutionSS.value)
        //         colorBuffer = UpscaleSSGI(renderGraph, cameraData, depthPyramid, denoisedSSGI);
        //
        //     resourceData.cameraColor = CombineSSGI(renderGraph, cameraData, resourceData.cameraColor, resourceData.cameraDepthTexture,
        //         resourceData.gBuffer, colorBuffer);
        // }
    }
}