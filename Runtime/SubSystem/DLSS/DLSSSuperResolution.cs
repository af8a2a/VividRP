#if DLSS_PLUGIN_INTEGRATE

//------------------------------------------------------------------------------
// DLSSSuperResolution.cs - DLSS Super Resolution Implementation
//------------------------------------------------------------------------------
// Simplified wrapper for DLSS-SR integration following the reference pattern.
// Manages feature lifecycle and execution via CommandBuffer.
//------------------------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    /// <summary>
    /// DLSS Super Resolution render pass implementation.
    /// Manages DLSS-SR feature lifecycle and execution.
    /// </summary>
    public class DLSSSuperResolution : IDisposable
    {
#if DLSS_PLUGIN_INTEGRATE
        private int m_dlssHandle = DLSSExtension.DLSS_INVALID_FEATURE_HANDLE;
        private bool m_initialized = false;
        private bool m_createFailed = false;
        private bool m_disposed = false;

        // Create params tracking for recreation
        private uint m_inputWidth;
        private uint m_inputHeight;
        private uint m_outputWidth;
        private uint m_outputHeight;
        private NVSDK_NGX_PerfQuality_Value m_qualityValue;
        private NVSDK_NGX_DLSS_Feature_Flags m_featureFlags;
        private bool m_createParamsChanged = false;

        // Cached extension reference
        private DLSSExtension m_Extension;

        private DLSSExtension Extension
        {
            get
            {
                if (m_Extension == null)
                    m_Extension = DLSSExtension.Instance;
                return m_Extension;
            }
        }

        /// <summary>
        /// Create a new DLSS-SR instance.
        /// </summary>
        /// <param name="featureFlags">Feature creation flags (HDR, MV format, depth format, etc.)</param>
        /// <param name="qualityValue">Quality/performance preset</param>
        public DLSSSuperResolution(
            NVSDK_NGX_DLSS_Feature_Flags featureFlags = NVSDK_NGX_DLSS_Feature_Flags.None,
            NVSDK_NGX_PerfQuality_Value qualityValue = NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_Balanced)
        {
            m_featureFlags = featureFlags;
            m_qualityValue = qualityValue;
        }

        /// <summary>
        /// Check if DLSS-SR is supported on the current system.
        /// </summary>
        public bool IsSupported => Extension?.IsSRSupported ?? false;

        /// <summary>
        /// Set the quality/performance preset.
        /// </summary>
        public void SetQuality(NVSDK_NGX_PerfQuality_Value quality)
        {
            if (m_qualityValue != quality)
            {
                m_qualityValue = quality;
                m_createParamsChanged = true;
            }
        }

        /// <summary>
        /// Set the feature creation flags.
        /// </summary>
        public void SetFeatureFlags(NVSDK_NGX_DLSS_Feature_Flags flags)
        {
            if (m_featureFlags != flags)
            {
                m_featureFlags = flags;
                m_createParamsChanged = true;
            }
        }

        /// <summary>
        /// Execute DLSS-SR.
        /// </summary>
        /// <param name="cmd">Command buffer to record commands into</param>
        /// <param name="colorInput">Input color texture (render resolution)</param>
        /// <param name="colorOutput">Output color texture (display resolution)</param>
        /// <param name="depth">Depth buffer</param>
        /// <param name="motionVectors">Motion vectors</param>
        /// <param name="jitterOffset">Jitter offset explicitly converted to input/render pixels</param>
        /// <param name="motionVectorEncoding">Units and direction encoded in the motion-vector texture</param>
        /// <param name="exposure">Explicit per-frame pre-exposure, exposure scale, and optional final-exposure texture</param>
        /// <param name="reset">Reset temporal history (e.g., on scene change)</param>
        /// <param name="biasColorMask">Optional bias color mask</param>
        /// <returns>
        /// True only when the native feature is ready and evaluation was queued.
        /// Returns false while asynchronous creation is pending or after it fails.
        /// </returns>
        public bool Render(
            CommandBuffer cmd,
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            DLSSJitterOffset jitterOffset,
            DLSSMotionVectorEncoding motionVectorEncoding,
            DLSSExposure exposure,
            bool reset = false,
            RenderTexture biasColorMask = null)
        {
            if (!IsSupported || Extension == null)
            {
                Debug.LogError("[DLSSSuperResolution] DLSS-SR is not supported");
                RecordFallback(cmd, colorInput, colorOutput);
                return false;
            }

            if (colorInput == null || colorOutput == null || depth == null || motionVectors == null)
            {
                Debug.LogError("[DLSSSuperResolution] Required textures are null");
                return false;
            }

            if (!exposure.TryValidate(out string exposureError))
            {
                Debug.LogError($"[DLSSSuperResolution] Invalid exposure contract: {exposureError}");
                RecordFallback(cmd, colorInput, colorOutput);
                return false;
            }

            // Check if we need to recreate the feature
            uint inputW = (uint)colorInput.width;
            uint inputH = (uint)colorInput.height;
            uint outputW = (uint)colorOutput.width;
            uint outputH = (uint)colorOutput.height;
            Vector2 jitterPixels = jitterOffset.RenderPixels;
            Vector2 motionVectorScale = motionVectorEncoding.GetNGXPixelScale(
                colorInput.width,
                colorInput.height);

            if (m_inputWidth != inputW || m_inputHeight != inputH ||
                m_outputWidth != outputW || m_outputHeight != outputH)
            {
                m_inputWidth = inputW;
                m_inputHeight = inputH;
                m_outputWidth = outputW;
                m_outputHeight = outputH;
                m_createParamsChanged = true;
            }

            // Recreate feature if params changed
            if (m_createParamsChanged)
            {
                if (!DisposeResources(cmd))
                {
                    RecordFallback(cmd, colorInput, colorOutput);
                    return false;
                }
                m_createParamsChanged = false;
                m_createFailed = false;
            }

            // Queue creation on the first call, then wait for the render-thread
            // result before publishing an evaluation command.
            if (!EnsureInitialized(cmd))
            {
                RecordFallback(cmd, colorInput, colorOutput);
                return false;
            }

            if (Extension.EvaluateSuperResolutionFeature(
                    cmd,
                    m_dlssHandle,
                    colorInput,
                    colorOutput,
                    depth,
                    motionVectors,
                    jitterPixels.x,
                    jitterPixels.y,
                    motionVectorScale.x,
                    motionVectorScale.y,
                    reset,
                    m_inputWidth,
                    m_inputHeight,
                    exposure,
                    biasColorMask))
            {
                return true;
            }

            RecordFallback(cmd, colorInput, colorOutput);
            return false;
        }

        private static void RecordFallback(
            CommandBuffer cmd,
            RenderTexture colorInput,
            RenderTexture colorOutput)
        {
            if (cmd != null && colorInput != null && colorOutput != null)
            {
                cmd.Blit(colorInput, colorOutput);
            }
        }

        private bool EnsureInitialized(CommandBuffer cmd)
        {
            if (m_initialized)
                return true;

            if (m_createFailed)
                return false;

            var ext = Extension;
            if (ext == null)
            {
                Debug.LogError("[DLSSSuperResolution] DLSSExtension not available");
                return false;
            }

            if (m_dlssHandle != DLSSExtension.DLSS_INVALID_FEATURE_HANDLE)
            {
                var status = ext.GetFeatureStatus(m_dlssHandle, out var createResult);
                switch (status)
                {
                    case DLSSFeatureStatus.Pending:
                        return false;

                    case DLSSFeatureStatus.Ready:
                        m_initialized = true;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        Debug.Log($"[DLSSSuperResolution] Initialized: {m_inputWidth}x{m_inputHeight} -> {m_outputWidth}x{m_outputHeight}, Quality={m_qualityValue}");
#endif
                        return true;

                    case DLSSFeatureStatus.Failed:
                        Debug.LogError($"[DLSSSuperResolution] Native DLSS-SR creation failed: {createResult}");
                        DiscardFailedInitialization(ext, true);
                        return false;

                    default:
                        Debug.LogError("[DLSSSuperResolution] Native DLSS-SR feature handle became invalid");
                        DiscardFailedInitialization(ext, false);
                        return false;
                }
            }

            if (!BeginInitialize(cmd))
            {
                m_createFailed = true;
            }

            // Creation executes asynchronously on the render thread. Evaluation
            // starts on a later call only after the status becomes Ready.
            return false;
        }

        private bool BeginInitialize(CommandBuffer cmd)
        {
            var ext = Extension;

            // Allocate parameters
            var result = ext.AllocateParameters(out IntPtr parameters);
            if (DLSSExtension.NVSDK_NGX_FAILED(result))
            {
                Debug.LogError($"[DLSSSuperResolution] Failed to allocate parameters: {result}");
                return false;
            }

            // Set creation parameters
            ext.SetParameterUI(parameters, DLSSExtension.NVSDK_NGX_Parameter_CreationNodeMask, 1);
            ext.SetParameterUI(parameters, DLSSExtension.NVSDK_NGX_Parameter_VisibilityNodeMask, 1);
            ext.SetParameterUI(parameters, DLSSExtension.NVSDK_NGX_Parameter_Width, m_inputWidth);
            ext.SetParameterUI(parameters, DLSSExtension.NVSDK_NGX_Parameter_Height, m_inputHeight);
            ext.SetParameterUI(parameters, DLSSExtension.NVSDK_NGX_Parameter_OutWidth, m_outputWidth);
            ext.SetParameterUI(parameters, DLSSExtension.NVSDK_NGX_Parameter_OutHeight, m_outputHeight);
            ext.SetParameterI(parameters, DLSSExtension.NVSDK_NGX_Parameter_PerfQualityValue, (int)m_qualityValue);
            ext.SetParameterI(parameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Feature_Create_Flags, (int)m_featureFlags);
            ext.SetParameterI(parameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Enable_Output_Subrects, 0);

            // Create feature
            m_dlssHandle = ext.CreateFeature(
                cmd,
                NVSDK_NGX_Feature.NVSDK_NGX_Feature_SuperSampling,
                parameters);
            if (m_dlssHandle == DLSSExtension.DLSS_INVALID_FEATURE_HANDLE)
            {
                Debug.LogError("[DLSSSuperResolution] Failed to create DLSS-SR feature");
                return false;
            }

            return true;
        }

        private void DiscardFailedInitialization(DLSSExtension ext, bool releaseHandle)
        {
            if (releaseHandle)
            {
                ext.ReleaseFeatureHandle(m_dlssHandle);
            }

            m_dlssHandle = DLSSExtension.DLSS_INVALID_FEATURE_HANDLE;

            m_initialized = false;
            m_createFailed = true;
        }

        private bool DisposeResources(CommandBuffer cmd)
        {
            var ext = Extension;
            if (ext == null)
            {
                return m_dlssHandle == DLSSExtension.DLSS_INVALID_FEATURE_HANDLE;
            }

            if (m_dlssHandle != DLSSExtension.DLSS_INVALID_FEATURE_HANDLE)
            {
                var status = ext.GetFeatureStatus(m_dlssHandle, out _);
                if (status == DLSSFeatureStatus.Pending ||
                    status == DLSSFeatureStatus.Ready)
                {
                    if (!ext.DestroyFeature(cmd, m_dlssHandle))
                    {
                        return false;
                    }
                }
                else
                {
                    if (status == DLSSFeatureStatus.Failed)
                    {
                        ext.ReleaseFeatureHandle(m_dlssHandle);
                    }

                }

                m_dlssHandle = DLSSExtension.DLSS_INVALID_FEATURE_HANDLE;
            }
            m_initialized = false;
            return true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (m_disposed)
                return;

            if (disposing)
            {
                // Create a temporary command buffer for cleanup
                using (var cmd = new CommandBuffer())
                {
                    cmd.name = "DLSS-SR Cleanup";
                    DisposeResources(cmd);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
            }

            m_disposed = true;
        }

        ~DLSSSuperResolution()
        {
            Dispose(false);
        }
#else
        public DLSSSuperResolution(
            NVSDK_NGX_DLSS_Feature_Flags featureFlags = NVSDK_NGX_DLSS_Feature_Flags.None,
            NVSDK_NGX_PerfQuality_Value qualityValue = NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_Balanced)
        {
        }

        public bool IsSupported => false;

        public void SetQuality(NVSDK_NGX_PerfQuality_Value quality) { }

        public void SetFeatureFlags(NVSDK_NGX_DLSS_Feature_Flags flags) { }

        public bool Render(
            CommandBuffer cmd,
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            DLSSJitterOffset jitterOffset,
            DLSSMotionVectorEncoding motionVectorEncoding,
            DLSSExposure exposure,
            bool reset = false,
            RenderTexture biasColorMask = null)
        {
            return false;
        }

        public void Dispose() { }
#endif
    }
}

#endif
