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
            // Check if DLSSManager is initialized (via DLSSExtension)
            if (!DLSSManager.IsInitialized)
                return false;

            // Query DLSS-SR capability
            if (DLSSManager.TryGetCapabilities(out var caps))
            {
                return caps.IsSRAvailable;
            }

            return false;
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

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private static HashSet<uint> s_ActiveViewIds = new HashSet<uint>();
#endif

        private DLSSPass()
        {
            // DLSSManager initialization handled by DLSSExtension
        }

        // Profiling sampler IDs
        private enum DLSSProfileId
        {
            UpdateContext,
            Execute
        }

        private static class DLSSProfilingSamplers
        {
            public static readonly ProfilingSampler UpdateContext = new ProfilingSampler(nameof(DLSSProfileId.UpdateContext));
            public static readonly ProfilingSampler Execute = new ProfilingSampler(nameof(DLSSProfileId.Execute));
        }

        // Map Unity NVIDIA quality to VividRP quality
        private static DLSSQuality MapQuality(DLSSQuality unityQuality)
        {
            switch (unityQuality)
            {
                case DLSSQuality.MaxQuality: return DLSSQuality.MaxQuality;
                case DLSSQuality.Balanced: return DLSSQuality.Balanced;
                case DLSSQuality.MaxPerformance: return DLSSQuality.MaxPerformance;
                case DLSSQuality.UltraPerformance: return DLSSQuality.UltraPerformance;
                case DLSSQuality.DLAA: return DLSSQuality.DLAA;
                default: return DLSSQuality.Balanced;
            }
        }

        // Map DLSSPreset uint to VividRP preset
        private static DLSSSRPreset MapPreset(uint preset)
        {
            // Mapping based on NVIDIA.DLSSPreset enum values (0-13)
            if (preset >= 0 && preset <= 13)
                return (DLSSSRPreset)preset;
            Debug.LogWarning($"[DLSSPass] Unknown DLSS Preset value {preset}, using default.");
            return DLSSSRPreset.Default;
        }

        private static bool IsOptimalSettingsValid(in DLSSOptimalSettings settings)
        {
            return settings.maxRenderHeight >= settings.minRenderHeight
                   && settings.maxRenderWidth >= settings.minRenderWidth
                   && settings.maxRenderWidth != 0
                   && settings.maxRenderHeight != 0
                   && settings.minRenderWidth != 0
                   && settings.minRenderHeight != 0;
        }

        //--------------------------------------------------------------------------
        // DLSSViewContext - Replaces ViewState
        //--------------------------------------------------------------------------
        private class DLSSViewContext
        {
            private uint m_ViewId;
            private bool m_ContextCreated;
            private bool m_UseAutomaticSettings;

            // Last known context parameters (for change detection)
            private struct ContextParams
            {
                public DLSSQuality quality;
                public uint inputWidth;
                public uint inputHeight;
                public uint outputWidth;
                public uint outputHeight;
                public uint presetQuality;
                public uint presetBalanced;
                public uint presetPerformance;
                public uint presetUltraPerformance;
                public uint presetDLAA;

                public bool Equals(in ContextParams other)
                {
                    return quality == other.quality &&
                           inputWidth == other.inputWidth &&
                           inputHeight == other.inputHeight &&
                           outputWidth == other.outputWidth &&
                           outputHeight == other.outputHeight &&
                           presetQuality == other.presetQuality &&
                           presetBalanced == other.presetBalanced &&
                           presetPerformance == other.presetPerformance &&
                           presetUltraPerformance == other.presetUltraPerformance &&
                           presetDLAA == other.presetDLAA;
                }
            }

            private ContextParams m_CurrentParams;

            // Optimal settings for DRS
            private DLSSQuality m_OptimalSettingsQuality;
            private Rect m_OptimalSettingsViewport;
            private DLSSOptimalSettings m_OptimalSettings;

            public bool useAutomaticSettings => m_UseAutomaticSettings;
            public DLSSOptimalSettings optimalSettings => m_OptimalSettings;
            public Rect optimalSettingsViewport => m_OptimalSettingsViewport;
            public DLSSQuality optimalSettingsQuality => m_OptimalSettingsQuality;

            public DLSSViewContext(uint viewId)
            {
                m_ViewId = viewId;
                m_ContextCreated = false;
                m_UseAutomaticSettings = false;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                // Track viewId for debugging
                if (s_ActiveViewIds.Contains(viewId))
                {
                    Debug.LogWarning($"[DLSSPass] ViewId {viewId} already in use!");
                }
                s_ActiveViewIds.Add(viewId);
#endif
            }

            public void RequestUseAutomaticSettings(bool enable, DLSSQuality quality, Rect viewport, in DLSSOptimalSettings settings)
            {
                m_UseAutomaticSettings = enable;
                m_OptimalSettingsQuality = quality;
                m_OptimalSettingsViewport = viewport;
                m_OptimalSettings = settings;
            }

            public void ClearAutomaticSettings()
            {
                m_UseAutomaticSettings = false;
            }

            public void UpdateContext(
                DLSSQuality unityQuality,
                uint inputWidth, uint inputHeight,
                uint outputWidth, uint outputHeight,
                CommandBuffer cmdBuffer)
            {
                //default
                var newParams = new ContextParams
                {
                    quality = unityQuality,
                    inputWidth = inputWidth,
                    inputHeight = inputHeight,
                    outputWidth = outputWidth,
                    outputHeight = outputHeight,
                    presetQuality = (uint)DLSSSRPreset.J,
                    presetBalanced = (uint)DLSSSRPreset.Default,
                    presetPerformance = (uint)DLSSSRPreset.M,
                    presetUltraPerformance = (uint)DLSSSRPreset.L,
                    presetDLAA = (uint)DLSSSRPreset.K
                };

                // Check if context needs recreation
                bool needsRecreate = !m_ContextCreated || !m_CurrentParams.Equals(newParams);

                if (!needsRecreate)
                    return;

                using (new ProfilingScope(cmdBuffer, DLSSProfilingSamplers.UpdateContext))
                {
                    // Destroy existing context if any
                    if (DLSSManager.HasContext(m_ViewId))
                    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        Debug.Log($"[DLSSPass] Destroying context for viewId {m_ViewId}");
#endif
                        DLSSManager.DestroyContext(m_ViewId);
                    }

                    m_ContextCreated = false;

                    // Create new context with DLSSNative directly to support custom presets
                    // Note: Do NOT set MVJittered - motion vectors are calculated using GetProjectionMatrixNoJitter()
                    // and do not contain jitter offset
                    var flags = DLSSFeatureFlags.IsHDR | DLSSFeatureFlags.DepthInverted;

                    var createParams = DLSSContextCreateParams.CreateSR(
                        newParams.quality,
                        newParams.inputWidth,
                        newParams.inputHeight,
                        newParams.outputWidth,
                        newParams.outputHeight,
                        flags
                    );
                    
                    var result = DLSSNative.DLSS_CreateContext(m_ViewId, ref createParams);
                    if (result == DLSSResult.Success)
                    {
                        m_ContextCreated = true;
                        m_CurrentParams = newParams;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        Debug.Log($"[DLSSPass] Created SR context for viewId {m_ViewId} ({inputWidth}x{inputHeight} -> {outputWidth}x{outputHeight}, quality: {unityQuality})");
#endif
                    }
                    else
                    {
                        Debug.LogError($"[DLSSPass] Failed to create DLSS-SR context for view {m_ViewId}: {result}");
                    }
                }
            }

            public void Execute(
                RenderTexture source,
                RenderTexture depth,
                RenderTexture motionVectors,
                RenderTexture biasColorMask,
                RenderTexture output,
                float jitterX,
                float jitterY,
                uint inputWidth,
                uint inputHeight,
                float preExposure,
                bool reset,
                CommandBuffer cmdBuffer)
            {
                if (!m_ContextCreated)
                {
                    Debug.LogWarning($"[DLSSPass] Context not created for view {m_ViewId}");
                    return;
                }

                using (new ProfilingScope(cmdBuffer, DLSSProfilingSamplers.Execute))
                {
                    // Ensure textures are created before getting native pointers
                    if (!source.IsCreated()) source.Create();
                    if (!output.IsCreated()) output.Create();
                    if (!depth.IsCreated()) depth.Create();
                    if (!motionVectors.IsCreated()) motionVectors.Create();

                    // Get native pointers
                    IntPtr sourcePtr = source.GetNativeTexturePtr();
                    IntPtr outputPtr = output.GetNativeTexturePtr();
                    IntPtr depthPtr = depth.GetNativeTexturePtr();
                    IntPtr motionVectorsPtr = motionVectors.GetNativeTexturePtr();
                    IntPtr biasColorMaskPtr = (biasColorMask != null && biasColorMask.IsCreated())
                        ? biasColorMask.GetNativeTexturePtr()
                        : IntPtr.Zero;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    // Validate texture pointers
                    Debug.Assert(sourcePtr != IntPtr.Zero, "[DLSSPass] Color input texture not created");
                    Debug.Assert(outputPtr != IntPtr.Zero, "[DLSSPass] Color output texture not created");
                    Debug.Assert(depthPtr != IntPtr.Zero, "[DLSSPass] Depth texture not created");
                    Debug.Assert(motionVectorsPtr != IntPtr.Zero, "[DLSSPass] Motion vectors texture not created");
                    Debug.Assert(inputWidth > 0 && inputHeight > 0, "[DLSSPass] Invalid input resolution");
#endif
                    // Build execute parameters
                    // In DLSS X-jitter should go left-to-right, Y-jitter should go top-to-bottom.
                    var executeParams = DLSSExecuteParams.CreateSR(
                        sourcePtr,
                        outputPtr,
                        depthPtr,
                        motionVectorsPtr,
                        jitterX,
                        jitterY,
                        -(float)inputWidth,  // mvScaleX (negative for Unity's convention)
                        -(float)inputHeight, // mvScaleY
                        inputWidth,
                        inputHeight,
                        reset,
                        IntPtr.Zero, // No exposure texture
                        biasColorMaskPtr
                    );

                    // Set additional common params
                    executeParams.common.preExposure = preExposure;
                    executeParams.common.invertYAxis = 1;
                    executeParams.common.invertXAxis = 0;

                    // Execute via command buffer for proper GPU synchronization
                    DLSSManager.ExecuteOnCommandBuffer(cmdBuffer, m_ViewId, ref executeParams);
                }
            }

            public void Cleanup()
            {
                if (DLSSManager.HasContext(m_ViewId))
                {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    Debug.Log($"[DLSSPass] Cleaning up context for viewId {m_ViewId}");
#endif
                    DLSSManager.DestroyContext(m_ViewId);
                    m_ContextCreated = false;
                }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                s_ActiveViewIds.Remove(m_ViewId);
#endif
            }
        }

        //--------------------------------------------------------------------------
        // DLSSCameraState - Replaces DLSSCamera
        //--------------------------------------------------------------------------
        private class DLSSCameraState
        {
            private List<DLSSViewContext> m_ViewContexts = null;
            private PerformDynamicRes m_ScaleDelegate = null;
            private int m_CameraInstanceId;

            public PerformDynamicRes ScaleDelegate => m_ScaleDelegate;
            public List<DLSSViewContext> ViewContexts => m_ViewContexts;

            public DLSSCameraState(int cameraInstanceId)
            {
                m_CameraInstanceId = cameraInstanceId;
                m_ScaleDelegate = ScaleFn;
            }

            public void ClearAutomaticSettings()
            {
                if (m_ViewContexts == null)
                    return;
                foreach (var ctx in m_ViewContexts)
                    ctx.ClearAutomaticSettings();
            }

            private float ScaleFn()
            {
                if (m_ViewContexts == null || m_ViewContexts.Count == 0)
                    return 100.0f;

                var viewContext = m_ViewContexts[0];
                if (!viewContext.useAutomaticSettings)
                    return 100.0f;

                var optimalSettings = viewContext.optimalSettings;
                var targetViewport = viewContext.optimalSettingsViewport;
                float suggestedPercentageX = (float)optimalSettings.optimalRenderWidth / targetViewport.width;
                float suggestedPercentageY = (float)optimalSettings.optimalRenderHeight / targetViewport.height;
                return Mathf.Min(suggestedPercentageX, suggestedPercentageY) * 100.0f;
            }

            // ViewId assignment: cameraInstanceId (upper 16 bits) + viewIndex (lower 16 bits)
            // Ensures unique viewIds across cameras and XR views
            private uint GetViewId(int viewIndex)
            {
                return (uint)((m_CameraInstanceId & 0xFFFF) << 16) | (uint)(viewIndex & 0xFFFF);
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
                    if (m_ViewContexts != null)
                    {
                        foreach (var ctx in m_ViewContexts)
                            ctx.Cleanup();
                        ListPool<DLSSViewContext>.Release(m_ViewContexts);
                    }

                    m_ViewContexts = ListPool<DLSSViewContext>.Get();
                    for (int viewIdx = 0; viewIdx < cameraViewCount; ++viewIdx)
                    {
                        uint viewId = GetViewId(viewIdx);
                        m_ViewContexts.Add(new DLSSViewContext(viewId));
                    }
                }

                // Helper to run DLSS for a single view
                void RunDLSSForView(DLSSViewContext viewContext, in UpscalerResources.ViewResources viewResources)
                {
                    // Update context (creates/recreates if needed)
                    viewContext.UpdateContext(
                        quality,
                        inputWidth, inputHeight,
                        outputWidth, outputHeight,
                        cmdBuffer
                    );

                    // Execute DLSS
                    viewContext.Execute(
                        viewResources.source as RenderTexture,
                        viewResources.depth as RenderTexture,
                        viewResources.motionVectors as RenderTexture,
                        viewResources.biasColorMask as RenderTexture,
                        viewResources.output as RenderTexture,
                        jitterX,
                        jitterY,
                        inputWidth,
                        inputHeight,
                        preExposure,
                        resetHistory,
                        cmdBuffer
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
                    ctx.Cleanup();

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
            if (dlssCameraState.ViewContexts == null || dlssCameraState.ViewContexts.Count == 0)
                return;

            // Get current quality from first view context
            var firstView = dlssCameraState.ViewContexts[0];

            Rect finalViewport = xrPass != null && xrPass.enabled
                ? xrPass.GetViewport()
                : new Rect(camera.pixelRect.x, camera.pixelRect.y, camera.pixelWidth, camera.pixelHeight);

            // Determine quality - if context exists, use current quality; otherwise use default
            DLSSQuality queryQuality = firstView.optimalSettingsQuality != 0
                ? firstView.optimalSettingsQuality
                : DLSSQuality.Balanced;

            // Query optimal settings using DLSSManager
            if (DLSSManager.TryGetOptimalSettings(
                    DLSSMode.SuperResolution,
                    queryQuality,
                    (uint)finalViewport.width,
                    (uint)finalViewport.height,
                    out var optimalSettings))
            {
                foreach (var view in dlssCameraState.ViewContexts)
                {
                    if (view == null)
                        continue;

                    view.RequestUseAutomaticSettings(enableAutomaticSettings, queryQuality, finalViewport, optimalSettings);
                }

                if (enableAutomaticSettings && IsOptimalSettingsValid(optimalSettings))
                {
                    dynamicResolutionSettings.maxPercentage = Mathf.Min(
                        (float)optimalSettings.maxRenderWidth / finalViewport.width,
                        (float)optimalSettings.maxRenderHeight / finalViewport.height) * 100.0f;
                    dynamicResolutionSettings.minPercentage = Mathf.Max(
                        (float)optimalSettings.minRenderWidth / finalViewport.width,
                        (float)optimalSettings.minRenderHeight / finalViewport.height) * 100.0f;
                    DynamicResolutionHandler.SetSystemDynamicResScaler(dlssCameraState.ScaleDelegate,
                        DynamicResScalePolicyType.ReturnsPercentage);
                    DynamicResolutionHandler.SetActiveDynamicScalerSlot(DynamicResScalerSlot.System);
                }
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

#endif

        #endregion
    }
}
