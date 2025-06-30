using Unity.Collections;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class ScreenSpaceAccumulationPass : ScriptableRenderPass
    {
        const string m_ProfilerTag = "Path Tracing Accumulation";

        // Curren Sample
        public int sample = 0;

        // Maximum Sample
        public int maximumSample = 64;

        public ScreenSpacePathTracing ssptVolume;

        private Material m_PathTracingMaterial;

        public RTHandle m_AccumulateColorHandle;
        private RTHandle m_AccumulateHistoryHandle;
        private RTHandle m_HistoryEmissionHandle;
        private RTHandle m_AccumulateSampleHandle;
        private RTHandle m_AccumulateHistorySampleHandle;
        private RTHandle m_HistoryDepthHandle;

        public ScreenSpacePathTracingFeature.SpatialDenoise m_SpatialDenoise;
        public ScreenSpacePathTracingFeature.Accumulation m_Accumulation;
        public bool m_ProgressBar;

        // Reset the offline accumulation when scene has changed.
        // This is not perfect because we cannot detect per mesh changes or per light changes.
        private Matrix4x4 prevCamWorldMatrix;
        private Matrix4x4 prevCamHClipMatrix;
        private NativeArray<VisibleLight> prevLightsList;
        private NativeArray<VisibleReflectionProbe> prevProbesList;

        private Matrix4x4 prevCamInvVPMatrix;
        private Vector3 prevCameraPositionWS;

#if UNITY_EDITOR
        private bool prevPlayState;
#endif


        private static readonly int _MaxSample = Shader.PropertyToID("_MaxSample");
        private static readonly int _Sample = Shader.PropertyToID("_Sample");
        private static readonly int _MaxSteps = Shader.PropertyToID("_MaxSteps");
        private static readonly int _StepSize = Shader.PropertyToID("_StepSize");
        private static readonly int _MaxBounce = Shader.PropertyToID("_MaxBounce");
        private static readonly int _RayCount = Shader.PropertyToID("_RayCount");
        private static readonly int _TemporalIntensity = Shader.PropertyToID("_TemporalIntensity");
        private static readonly int _MaxBrightness = Shader.PropertyToID("_MaxBrightness");
        private static readonly int _IsProbeCamera = Shader.PropertyToID("_IsProbeCamera");
        private static readonly int _BackDepthEnabled = Shader.PropertyToID("_BackDepthEnabled");
        private static readonly int _IsAccumulationPaused = Shader.PropertyToID("_IsAccumulationPaused");
        private static readonly int _PrevInvViewProjMatrix = Shader.PropertyToID("_PrevInvViewProjMatrix");
        private static readonly int _PrevCameraPositionWS = Shader.PropertyToID("_PrevCameraPositionWS");
        private static readonly int _PixelSpreadAngleTangent = Shader.PropertyToID("_PixelSpreadAngleTangent");
        private static readonly int _FrameIndex = Shader.PropertyToID("_FrameIndex");

        private static readonly int _PathTracingEmissionTexture = Shader.PropertyToID("_PathTracingEmissionTexture");
        private static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int _CameraDepthAttachment = Shader.PropertyToID("_CameraDepthAttachment");
        private static readonly int _CameraBackDepthTexture = Shader.PropertyToID("_CameraBackDepthTexture");
        private static readonly int _CameraBackNormalsTexture = Shader.PropertyToID("_CameraBackNormalsTexture");

        private static readonly int _PathTracingAccumulationTexture =
            Shader.PropertyToID("_PathTracingAccumulationTexture");

        private static readonly int _PathTracingHistoryTexture = Shader.PropertyToID("_PathTracingHistoryTexture");

        private static readonly int _PathTracingHistoryEmissionTexture =
            Shader.PropertyToID("_PathTracingHistoryEmissionTexture");

        private static readonly int _PathTracingSampleTexture = Shader.PropertyToID("_PathTracingSampleTexture");

        private static readonly int _PathTracingHistorySampleTexture =
            Shader.PropertyToID("_PathTracingHistorySampleTexture");

        private static readonly int _PathTracingHistoryDepthTexture =
            Shader.PropertyToID("_PathTracingHistoryDepthTexture");

        private static readonly int _TransparentGBuffer0 = Shader.PropertyToID("_TransparentGBuffer0");
        private static readonly int _TransparentGBuffer1 = Shader.PropertyToID("_TransparentGBuffer1");
        private static readonly int _TransparentGBuffer2 = Shader.PropertyToID("_TransparentGBuffer2");
        private static readonly int _GBuffer0 = Shader.PropertyToID("_GBuffer0");
        private static readonly int _GBuffer1 = Shader.PropertyToID("_GBuffer1");
        private static readonly int _GBuffer2 = Shader.PropertyToID("_GBuffer2");


        public ScreenSpaceAccumulationPass(Material pathTracingMaterial, bool progressBar)
        {
            profilingSampler = new ProfilingSampler(m_ProfilerTag);
            m_PathTracingMaterial = pathTracingMaterial;
            m_ProgressBar = progressBar;
        }

        // This class stores the data needed by the pass, passed as parameter to the delegate function that executes the pass
        private class PassData
        {
            internal Material pathTracingMaterial;

            internal bool progressBar;
            internal ScreenSpacePathTracingFeature.Accumulation accumulationMode;
            internal ScreenSpacePathTracingFeature.SpatialDenoise spatialDenoiseMode;

            internal TextureHandle cameraColorTargetHandle;
            internal TextureHandle cameraDepthTargetHandle;

            internal TextureHandle accumulateColorHandle;
            internal TextureHandle accumulateHistoryHandle;
            internal TextureHandle historyEmissionHandle;
            internal TextureHandle accumulateSampleHandle;
            internal TextureHandle accumulateHistorySampleHandle;
            internal TextureHandle historyDepthHandle;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            // Load & Store actions are important to support acculumation.
            if (data.accumulationMode == ScreenSpacePathTracingFeature.Accumulation.Camera)
            {
                cmd.SetRenderTarget(
                    data.accumulateColorHandle,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store,
                    data.accumulateColorHandle,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.DontCare);

                Blitter.BlitCameraTexture(cmd, data.cameraColorTargetHandle, data.accumulateColorHandle,
                    data.pathTracingMaterial, pass: 3);

                if (data.progressBar)
                    Blitter.BlitCameraTexture(cmd, data.accumulateColorHandle, data.cameraColorTargetHandle,
                        data.pathTracingMaterial, pass: 4);
                else
                    Blitter.BlitCameraTexture(cmd, data.accumulateColorHandle, data.cameraColorTargetHandle);
            }
            else if (data.accumulationMode == ScreenSpacePathTracingFeature.Accumulation.PerObject ||
                     data.accumulationMode == ScreenSpacePathTracingFeature.Accumulation.PerObjectBlur)
            {
                // Load & Store actions are important to support acculumation.
                cmd.SetRenderTarget(
                    data.accumulateColorHandle,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store,
                    data.accumulateColorHandle,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.DontCare);

                // [Spatial Denoise]
                if (data.accumulationMode == ScreenSpacePathTracingFeature.Accumulation.PerObjectBlur)
                {
                    for (int i = 0; i < ((int)data.spatialDenoiseMode); i++)
                    {
                        Blitter.BlitCameraTexture(cmd, data.cameraColorTargetHandle, data.accumulateColorHandle,
                            data.pathTracingMaterial, pass: 5);
                        Blitter.BlitCameraTexture(cmd, data.accumulateColorHandle, data.cameraColorTargetHandle,
                            data.pathTracingMaterial, pass: 5);
                    }

                    Blitter.BlitCameraTexture(cmd, data.cameraColorTargetHandle, data.accumulateColorHandle,
                        data.pathTracingMaterial, pass: 5);
                }
                else
                    Blitter.BlitCameraTexture(cmd, data.cameraColorTargetHandle, data.accumulateColorHandle);

                cmd.SetRenderTarget(
                    data.accumulateSampleHandle,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store,
                    data.accumulateSampleHandle,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.DontCare);

                RenderTargetIdentifier[] rTHandles = new RenderTargetIdentifier[2];
                rTHandles[0] = data.cameraColorTargetHandle;
                rTHandles[1] = data.accumulateSampleHandle;
                // RT-1: accumulated results
                // RT-2: accumulated sample count
                cmd.SetRenderTarget(rTHandles, data.accumulateSampleHandle);
                Blitter.BlitTexture(cmd, data.accumulateColorHandle, new Vector4(1.0f, 1.0f, 0.0f, 0.0f),
                    data.pathTracingMaterial, pass: 1);

                cmd.SetRenderTarget(
                    data.accumulateHistoryHandle,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store,
                    data.accumulateHistoryHandle,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.DontCare);
                // Copy history emission color
                Blitter.BlitCameraTexture(cmd, data.historyEmissionHandle, data.historyEmissionHandle,
                    data.pathTracingMaterial, pass: 6);
                // Copy history color
                Blitter.BlitCameraTexture(cmd, data.cameraColorTargetHandle, data.accumulateHistoryHandle);
                // Copy history sample count
                cmd.SetRenderTarget(
                    data.accumulateHistorySampleHandle,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store,
                    data.accumulateHistorySampleHandle,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.DontCare);

                Blitter.BlitCameraTexture(cmd, data.accumulateSampleHandle, data.accumulateHistorySampleHandle);
                // Copy history depth
                Blitter.BlitCameraTexture(cmd, data.historyDepthHandle, data.historyDepthHandle,
                    data.pathTracingMaterial, pass: 2);
            }
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // add an unsafe render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData))
            {
                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;

                if (m_Accumulation == ScreenSpacePathTracingFeature.Accumulation.Camera)
                {
                    Matrix4x4 camWorldMatrix = cameraData.camera.cameraToWorldMatrix;
                    Matrix4x4 camHClipMatrix = cameraData.camera.projectionMatrix;

                    bool haveMatrices = prevCamWorldMatrix != null && prevCamHClipMatrix != null;
                    if (haveMatrices && prevCamWorldMatrix == camWorldMatrix && prevCamHClipMatrix == camHClipMatrix)
                    {
                        prevCamWorldMatrix = camWorldMatrix;
                        prevCamHClipMatrix = camHClipMatrix;
                    }
                    else
                    {
                        sample = 0;
                        prevCamWorldMatrix = camWorldMatrix;
                        prevCamHClipMatrix = camHClipMatrix;
                    }

                    bool lightsNoUpdate = prevLightsList != null && prevLightsList == lightData.visibleLights;
                    bool probesNoUpdate = prevProbesList != null &&
                                          prevProbesList == universalRenderingData.cullResults.visibleReflectionProbes;
                    if (!lightsNoUpdate || !probesNoUpdate)
                    {
                        sample = 0;
                    }

                    prevLightsList = lightData.visibleLights;
                    prevProbesList = universalRenderingData.cullResults.visibleReflectionProbes;

                    m_PathTracingMaterial.SetFloat(_Sample, sample);

                    // If the HDR precision is set to 64 Bits, the maximum sample can be 512.
                    GraphicsFormat currentGraphicsFormat = cameraData.cameraTargetDescriptor.graphicsFormat;
                    int maxSample = currentGraphicsFormat == GraphicsFormat.B10G11R11_UFloatPack32 ? 64 : maximumSample;
                    m_PathTracingMaterial.SetFloat(_MaxSample, maxSample);
                    bool isPaused = false;
#if UNITY_EDITOR
                    if (prevPlayState != UnityEditor.EditorApplication.isPlaying)
                    {
                        sample = 0;
                    }

                    prevPlayState = UnityEditor.EditorApplication.isPlaying;
                    isPaused = UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused;
#endif
                    m_PathTracingMaterial.SetFloat(_IsAccumulationPaused, isPaused ? 1.0f : 0.0f);
                    if (sample < maxSample && !isPaused)
                        sample++;
                }
                else
                {
                    sample = 0;
                }

                passData.pathTracingMaterial = m_PathTracingMaterial;
                passData.progressBar = m_ProgressBar;
                passData.accumulationMode = m_Accumulation;
                passData.spatialDenoiseMode = m_SpatialDenoise;

                passData.cameraColorTargetHandle = resourceData.activeColorTexture;
                builder.UseTexture(passData.cameraColorTargetHandle, AccessFlags.ReadWrite);

                passData.cameraDepthTargetHandle = resourceData.activeDepthTexture;
                builder.UseTexture(passData.cameraDepthTargetHandle, AccessFlags.Write);

                TextureHandle accumulateHistoryHandle;
                TextureHandle historyEmissionHandle;
                TextureHandle accumulateSampleHandle;
                TextureHandle accumulateHistorySampleHandle;
                TextureHandle historyDepthHandle;

                // We decide to directly allocate RTHandles because these textures are stored across frames, which means they cannot be reused in other passes.
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_AccumulateColorHandle, desc, FilterMode.Point,
                    TextureWrapMode.Clamp, name: "_PathTracingAccumulationTexture");
                m_PathTracingMaterial.SetTexture(_PathTracingAccumulationTexture, m_AccumulateColorHandle);
                TextureHandle accumulateColorHandle = renderGraph.ImportTexture(m_AccumulateColorHandle);
                passData.accumulateColorHandle = accumulateColorHandle;

                builder.UseTexture(accumulateColorHandle, AccessFlags.ReadWrite);

                if (m_Accumulation == ScreenSpacePathTracingFeature.Accumulation.PerObject ||
                    m_Accumulation == ScreenSpacePathTracingFeature.Accumulation.PerObjectBlur)
                {
                    // [Temporal Accumulation]
                    var camera = cameraData.camera;
                    if (prevCamInvVPMatrix != null)
                        m_PathTracingMaterial.SetMatrix(_PrevInvViewProjMatrix, prevCamInvVPMatrix);
                    else
                        m_PathTracingMaterial.SetMatrix(_PrevInvViewProjMatrix,
                            camera.previousViewProjectionMatrix.inverse);

                    if (prevCameraPositionWS != null)
                        m_PathTracingMaterial.SetVector(_PrevCameraPositionWS, prevCameraPositionWS);
                    else
                        m_PathTracingMaterial.SetVector(_PrevCameraPositionWS, camera.transform.position);

                    prevCamInvVPMatrix = (GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, true) *
                                          cameraData.GetViewMatrix())
                        .inverse; // (cameraData.GetGPUProjectionMatrix() * cameraData.GetViewMatrix()).inverse;
                    prevCameraPositionWS = camera.transform.position;

                    m_PathTracingMaterial.SetFloat(_PixelSpreadAngleTangent,
                        Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) * 2.0f /
                        Mathf.Min(camera.scaledPixelWidth, camera.scaledPixelHeight));

                    RenderingUtils.ReAllocateHandleIfNeeded(ref m_AccumulateHistoryHandle, desc, FilterMode.Point,
                        TextureWrapMode.Clamp, name: "_PathTracingHistoryTexture");
                    RenderingUtils.ReAllocateHandleIfNeeded(ref m_HistoryEmissionHandle, desc, FilterMode.Point,
                        TextureWrapMode.Clamp, name: "_PathTracingHistoryEmissionTexture");
                    m_PathTracingMaterial.SetTexture(_PathTracingHistoryTexture, m_AccumulateHistoryHandle);
                    m_PathTracingMaterial.SetTexture(_PathTracingHistoryEmissionTexture, m_HistoryEmissionHandle);
                    accumulateHistoryHandle = renderGraph.ImportTexture(m_AccumulateHistoryHandle);
                    historyEmissionHandle = renderGraph.ImportTexture(m_HistoryEmissionHandle);

                    desc.colorFormat = RenderTextureFormat.RHalf;
                    RenderingUtils.ReAllocateHandleIfNeeded(ref m_AccumulateSampleHandle, desc, FilterMode.Point,
                        TextureWrapMode.Clamp, name: "_PathTracingSampleTexture");
                    RenderingUtils.ReAllocateHandleIfNeeded(ref m_AccumulateHistorySampleHandle, desc, FilterMode.Point,
                        TextureWrapMode.Clamp, name: "_PathTracingHistorySampleTexture");
                    m_PathTracingMaterial.SetTexture(_PathTracingSampleTexture, m_AccumulateSampleHandle);
                    m_PathTracingMaterial.SetTexture(_PathTracingHistorySampleTexture, m_AccumulateHistorySampleHandle);
                    accumulateSampleHandle = renderGraph.ImportTexture(m_AccumulateSampleHandle);
                    accumulateHistorySampleHandle = renderGraph.ImportTexture(m_AccumulateHistorySampleHandle);

                    desc.colorFormat = RenderTextureFormat.RFloat;
                    RenderingUtils.ReAllocateHandleIfNeeded(ref m_HistoryDepthHandle, desc, FilterMode.Point,
                        TextureWrapMode.Clamp, name: "_PathTracingHistoryDepthTexture");
                    m_PathTracingMaterial.SetTexture(_PathTracingHistoryDepthTexture, m_HistoryDepthHandle);
                    historyDepthHandle = renderGraph.ImportTexture(m_HistoryDepthHandle);

                    passData.accumulateHistoryHandle = accumulateHistoryHandle;
                    passData.historyEmissionHandle = historyEmissionHandle;
                    passData.accumulateSampleHandle = accumulateSampleHandle;
                    passData.accumulateHistorySampleHandle = accumulateHistorySampleHandle;
                    passData.historyDepthHandle = historyDepthHandle;

                    ConfigureInput(ScriptableRenderPassInput.Motion);

                    builder.UseTexture(accumulateHistoryHandle, AccessFlags.ReadWrite);
                    builder.UseTexture(historyEmissionHandle, AccessFlags.ReadWrite);
                    builder.UseTexture(accumulateSampleHandle, AccessFlags.ReadWrite);
                    builder.UseTexture(accumulateHistorySampleHandle, AccessFlags.ReadWrite);
                    builder.UseTexture(historyDepthHandle, AccessFlags.ReadWrite);
                    builder.UseTexture(resourceData.motionVectorColor, AccessFlags.Read);

                    builder.SetGlobalTextureAfterPass(accumulateHistoryHandle, _PathTracingHistoryTexture);
                    builder.SetGlobalTextureAfterPass(historyEmissionHandle, _PathTracingHistoryEmissionTexture);
                    builder.SetGlobalTextureAfterPass(accumulateSampleHandle, _PathTracingSampleTexture);
                    builder.SetGlobalTextureAfterPass(accumulateHistorySampleHandle, _PathTracingHistorySampleTexture);
                    builder.SetGlobalTextureAfterPass(historyDepthHandle, _PathTracingHistoryDepthTexture);
                }

                builder.SetGlobalTextureAfterPass(accumulateColorHandle, _PathTracingAccumulationTexture);

                // We disable culling for this pass for the demonstrative purpose of this sample, as normally this pass would be culled,
                // since the destination texture is not used anywhere else
                //builder.AllowGlobalStateModification(true);
                //builder.AllowPassCulling(false);

                // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }

        public void Dispose()
        {
            if (m_Accumulation != ScreenSpacePathTracingFeature.Accumulation.None)
                m_AccumulateColorHandle?.Release();
            if (m_Accumulation == ScreenSpacePathTracingFeature.Accumulation.PerObject || m_Accumulation == ScreenSpacePathTracingFeature.Accumulation.PerObjectBlur)
            {
                m_AccumulateHistoryHandle?.Release();
                m_HistoryEmissionHandle?.Release();
                m_AccumulateSampleHandle?.Release();
                m_HistoryDepthHandle?.Release();
            }
        }
    }
}