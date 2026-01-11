using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    internal class DLSSPass
    {
        #region public members, general engine code

        public struct Parameters
        {
            public bool resetHistory;
            public float preExposure;
            public UniversalCameraData cameraData;
        }

        public static bool SetupFeature()
        {
#if DLSS_PLUGIN_INTEGRATE
            // Check DLSS-SR capability via DLSSExtension
            var ext = DLSSExtension.Instance;
            return ext != null && ext.IsSRSupported;
#else
            return false;
#endif
        }

        public static DLSSPass Create()
        {
            DLSSPass dlssPass = null;

#if DLSS_PLUGIN_INTEGRATE
            if (!SetupFeature())
                return null;

            dlssPass = new DLSSPass();
#endif
            return dlssPass;
        }

        public void BeginFrame(UniversalCameraData cameraData)
        {
#if DLSS_PLUGIN_INTEGRATE
            InternalBeginFrame(cameraData);
#endif
        }

        public void SetupDRSScaling(bool enableAutomaticSettings, Camera camera, in UniversalAdditionalCameraData additionalCameraData, XRPass xrPass,
            ref GlobalDynamicResolutionSettings dynamicResolutionSettings)
        {
#if DLSS_PLUGIN_INTEGRATE
            InternalSetupDRSScaling(enableAutomaticSettings, camera, additionalCameraData, xrPass, ref dynamicResolutionSettings);
#endif
        }

        public void Render(
            DLSSPass.Parameters parameters,
            UpscalerResources.CameraResources resources,
            CommandBuffer cmdBuffer)
        {
#if DLSS_PLUGIN_INTEGRATE
            InternalRender(parameters, resources, cmdBuffer);
#endif
        }

        #endregion

        #region private members, VividRP DLSS implementation

#if DLSS_PLUGIN_INTEGRATE
        private UpscalerCameras m_CameraStates = new UpscalerCameras();
        private CommandBuffer m_CommandBuffer = new CommandBuffer();

        private DLSSPass()
        {
            // DLSS initialization handled by SetupFeature via DLSS_Init
        }

        // Profiling sampler IDs
        private enum DLSSProfileId
        {
            Render
        }

        private static class DLSSProfilingSamplers
        {
            public static readonly ProfilingSampler Render = new ProfilingSampler("DLSS-SR Render");
        }

        //--------------------------------------------------------------------------
        // DLSSViewContext - Uses DLSSSuperResolution wrapper
        //--------------------------------------------------------------------------
        private class DLSSViewContext : IDisposable
        {
            private DLSSSuperResolution m_DlssSR;
            private NVSDK_NGX_PerfQuality_Value m_CurrentQuality;
            private bool m_Disposed = false;

            public DLSSViewContext()
            {
                // Feature flags: HDR, DepthInverted (reversed-Z in Unity)
                // Note: Do NOT set MVJittered - motion vectors use GetProjectionMatrixNoJitter()
                var flags = NVSDK_NGX_DLSS_Feature_Flags.IsHDR | NVSDK_NGX_DLSS_Feature_Flags.DepthInverted;
                m_DlssSR = new DLSSSuperResolution(flags, NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_Balanced);
                m_CurrentQuality = NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_Balanced;
            }

            public void SetQuality(NVSDK_NGX_PerfQuality_Value quality)
            {
                if (m_CurrentQuality != quality)
                {
                    m_CurrentQuality = quality;
                    m_DlssSR.SetQuality(quality);
                }
            }

            public void Execute(
                CommandBuffer cmdBuffer,
                RenderTexture source,
                RenderTexture depth,
                RenderTexture motionVectors,
                RenderTexture output,
                float jitterX,
                float jitterY,
                float mvScaleX,
                float mvScaleY,
                float preExposure,
                bool reset,
                RenderTexture biasColorMask = null)
            {
                // Ensure textures are created
                if (!source.IsCreated()) source.Create();
                if (!output.IsCreated()) output.Create();
                if (!depth.IsCreated()) depth.Create();
                if (!motionVectors.IsCreated()) motionVectors.Create();

                m_DlssSR.Render(
                    cmdBuffer,
                    source,
                    output,
                    depth,
                    motionVectors,
                    jitterX,
                    jitterY,
                    mvScaleX,
                    mvScaleY,
                    reset,
                    preExposure,
                    null, // exposureTexture
                    biasColorMask
                );
            }

            public void Dispose()
            {
                if (!m_Disposed)
                {
                    m_DlssSR?.Dispose();
                    m_DlssSR = null;
                    m_Disposed = true;
                }
            }
        }

        //--------------------------------------------------------------------------
        // DLSSCameraState - Camera-level state management
        //--------------------------------------------------------------------------
        private class DLSSCameraState
        {
            private List<DLSSViewContext> m_ViewContexts = null;
            private PerformDynamicRes m_ScaleDelegate = null;
            private int m_CameraInstanceId;

            // DRS tracking
            private bool m_UseAutomaticSettings;
            private float m_OptimalScalePercent = 100.0f;

            public PerformDynamicRes ScaleDelegate => m_ScaleDelegate;
            public List<DLSSViewContext> ViewContexts => m_ViewContexts;

            public DLSSCameraState(int cameraInstanceId)
            {
                m_CameraInstanceId = cameraInstanceId;
                m_ScaleDelegate = ScaleFn;
            }

            public void SetOptimalScale(bool useAutoSettings, float scalePercent)
            {
                m_UseAutomaticSettings = useAutoSettings;
                m_OptimalScalePercent = scalePercent;
            }

            public void ClearAutomaticSettings()
            {
                m_UseAutomaticSettings = false;
                m_OptimalScalePercent = 100.0f;
            }

            private float ScaleFn()
            {
                return m_UseAutomaticSettings ? m_OptimalScalePercent : 100.0f;
            }

            public void Execute(
                UniversalCameraData cameraData,
                DLSSQuality quality,
                float preExposure,
                bool resetHistory,
                uint inputWidth,
                uint inputHeight,
                uint outputWidth,
                uint outputHeight,
                float jitterX,
                float jitterY,
                in UpscalerResources.CameraResources camResources,
                CommandBuffer cmdBuffer)
            {
                int cameraViewCount = 1;
                int activeViewId = 0;
                if (cameraData.xr.enabled)
                {
                    cameraViewCount = cameraData.xr.singlePassEnabled ? cameraData.xr.viewCount : 2;
                    activeViewId = cameraData.xr.multipassId;
                }

                // Ensure view contexts exist
                if (m_ViewContexts == null || m_ViewContexts.Count != cameraViewCount)
                {
                    Cleanup();

                    m_ViewContexts = ListPool<DLSSViewContext>.Get();
                    for (int viewIdx = 0; viewIdx < cameraViewCount; ++viewIdx)
                    {
                        m_ViewContexts.Add(new DLSSViewContext());
                    }
                }

                // Map quality and update all view contexts
                var ngxQuality = quality.ToNGXQuality();
                foreach (var ctx in m_ViewContexts)
                {
                    ctx.SetQuality(ngxQuality);
                }

                // Motion vector scale (negative for Unity's convention)
                float mvScaleX = -(float)inputWidth;
                float mvScaleY = -(float)inputHeight;

                // Helper to run DLSS for a single view
                void RunDLSSForView(DLSSViewContext viewContext, in UpscalerResources.ViewResources viewResources)
                {
                    viewContext.Execute(
                        cmdBuffer,
                        viewResources.source as RenderTexture,
                        viewResources.depth as RenderTexture,
                        viewResources.motionVectors as RenderTexture,
                        viewResources.output as RenderTexture,
                        jitterX,
                        jitterY,
                        mvScaleX,
                        mvScaleY,
                        preExposure,
                        resetHistory,
                        viewResources.biasColorMask as RenderTexture
                    );
                }

                // XR single-pass handling (copy array textures to individual views)
                if (camResources.copyToViews)
                {
                    Assertions.Assert.IsTrue(cameraData.xr.enabled && cameraData.xr.singlePassEnabled,
                        "XR must be enabled for tmp copying to views to occur");

                    // Copy array texture slices to temporary 2D textures for each view
                    for (int viewIdx = 0; viewIdx < m_ViewContexts.Count; ++viewIdx)
                    {
                        var tmpResources = viewIdx == 0 ? camResources.tmpView0 : camResources.tmpView1;

                        cmdBuffer.CopyTexture(camResources.resources.source, viewIdx, tmpResources.source, 0);
                        cmdBuffer.CopyTexture(camResources.resources.depth, viewIdx, tmpResources.depth, 0);
                        cmdBuffer.CopyTexture(camResources.resources.motionVectors, viewIdx, tmpResources.motionVectors, 0);

                        if (camResources.resources.biasColorMask != null)
                            cmdBuffer.CopyTexture(camResources.resources.biasColorMask, viewIdx, tmpResources.biasColorMask, 0);
                    }

                    // Execute DLSS for each view with temporary textures
                    for (int viewIdx = 0; viewIdx < m_ViewContexts.Count; ++viewIdx)
                    {
                        var tmpResources = viewIdx == 0 ? camResources.tmpView0 : camResources.tmpView1;
                        RunDLSSForView(m_ViewContexts[viewIdx], tmpResources);
                        cmdBuffer.CopyTexture(tmpResources.output, 0, camResources.resources.output, viewIdx);
                    }
                }
                else
                {
                    // Single view or XR multipass
                    RunDLSSForView(m_ViewContexts[activeViewId], camResources.resources);
                }
            }

            public void Cleanup()
            {
                if (m_ViewContexts == null)
                    return;

                foreach (var ctx in m_ViewContexts)
                    ctx.Dispose();

                ListPool<DLSSViewContext>.Release(m_ViewContexts);
                m_ViewContexts = null;
            }
        }

        //--------------------------------------------------------------------------
        // Camera State Management
        //--------------------------------------------------------------------------
        private void CleanupCameraStates()
        {
            Dictionary<int, UpscalerCameras.State> cameras = m_CameraStates.cameras;
            m_CommandBuffer.Clear();
            foreach (var kv in cameras)
            {
                var cameraState = kv.Value;
                if (!m_CameraStates.HasCameraStateExpired(cameraState) || cameraState.data == null)
                    continue;

                var dlssCameraState = cameraState.data as DLSSCameraState;
                dlssCameraState.Cleanup();
                cameraState.data = null;
            }

            Graphics.ExecuteCommandBuffer(m_CommandBuffer);
            m_CameraStates.CleanupCameraStates();
        }

        //--------------------------------------------------------------------------
        // Internal Methods
        //--------------------------------------------------------------------------
        private void InternalBeginFrame(UniversalCameraData cameraData)
        {
            m_CameraStates.ProcessExpiredCameras();

            UpscalerCameras.State cameraState = m_CameraStates.GetState(cameraData.camera);
            var dlssCameraState = cameraState != null ? cameraState.data as DLSSCameraState : null;

            bool dlssActive = cameraData.IsDLSSEnabled();

            if (cameraState == null && dlssActive)
            {
                dlssCameraState = new DLSSCameraState(cameraData.camera.GetInstanceID());
                cameraState = m_CameraStates.CreateState(cameraData.camera);
                cameraState.data = dlssCameraState;
            }
            else if (cameraState != null && !dlssActive)
            {
                m_CameraStates.InvalidateState(cameraState);
            }

            if (cameraState != null)
                m_CameraStates.TagUsed(cameraState);

            CleanupCameraStates();
            m_CameraStates.NextFrame();
        }

        private void InternalSetupDRSScaling(bool enableAutomaticSettings, Camera camera, in UniversalAdditionalCameraData additionalCameraData, XRPass xrPass,
            ref GlobalDynamicResolutionSettings dynamicResolutionSettings)
        {
            UpscalerCameras.State cameraState = m_CameraStates.GetState(camera);
            if (cameraState == null)
                return;

            var dlssCameraState = cameraState.data as DLSSCameraState;
            if (dlssCameraState == null)
                return;

            // For now, use simple scale calculation based on quality preset
            // The new SDK doesn't expose optimal settings query directly
            // You can implement custom DRS logic here if needed
            if (enableAutomaticSettings)
            {
                dlssCameraState.SetOptimalScale(true, DLSSConstants.DEFAULT_DRS_SCALE_PERCENT);
                DynamicResolutionHandler.SetSystemDynamicResScaler(dlssCameraState.ScaleDelegate,
                    DynamicResScalePolicyType.ReturnsPercentage);
                DynamicResolutionHandler.SetActiveDynamicScalerSlot(DynamicResScalerSlot.System);
            }
            else
            {
                dlssCameraState.ClearAutomaticSettings();
            }
        }

        private void InternalRender(in DLSSPass.Parameters parameters, UpscalerResources.CameraResources resources, CommandBuffer cmdBuffer)
        {
            UpscalerCameras.State cameraState = m_CameraStates.GetState(parameters.cameraData.camera);
            if (cameraState == null)
                return;

            DLSSCameraState dlssCameraState = cameraState.data as DLSSCameraState;

            using (new ProfilingScope(cmdBuffer, DLSSProfilingSamplers.Render))
            {
                dlssCameraState.Execute(
                    parameters.cameraData,
                    parameters.cameraData.dlssQuality,
                    parameters.preExposure,
                    parameters.resetHistory,
                    (uint)parameters.cameraData.actualWidth,
                    (uint)parameters.cameraData.actualHeight,
                    (uint)parameters.cameraData.pixelWidth,
                    (uint)parameters.cameraData.pixelHeight,
                    // DLSS expects jitter in pixel space (±0.5 pixels), not NDC
                    // Jitter from VividCameraExtension is in NDC, need to scale to pixels
                    -parameters.cameraData.jitter.x * parameters.cameraData.actualWidth,
                    -parameters.cameraData.jitter.y * parameters.cameraData.actualHeight,
                    resources,
                    cmdBuffer
                );
            }
        }

#endif

        #endregion
    }
}