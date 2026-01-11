//------------------------------------------------------------------------------
// DLSSSuperResolution.cs - DLSS Super Resolution Implementation
//------------------------------------------------------------------------------
// Simplified wrapper for DLSS-SR integration following the reference pattern.
// Manages feature lifecycle and execution via CommandBuffer.
//------------------------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// DLSS Super Resolution render pass implementation.
    /// Manages DLSS-SR feature lifecycle and execution.
    /// </summary>
    public class DLSSSuperResolution : IDisposable
    {
#if DLSS_PLUGIN_INTEGRATE
        private int m_dlssHandle = DLSSExtension.DLSS_INVALID_FEATURE_HANDLE;
        private IntPtr m_dlssParameters = IntPtr.Zero;
        private bool m_initialized = false;
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
        /// <param name="jitterX">Jitter offset X in render pixels</param>
        /// <param name="jitterY">Jitter offset Y in render pixels</param>
        /// <param name="mvScaleX">Motion vector scale X (typically render width with sign for direction)</param>
        /// <param name="mvScaleY">Motion vector scale Y (typically render height with sign for direction)</param>
        /// <param name="reset">Reset temporal history (e.g., on scene change)</param>
        /// <param name="preExposure">Pre-exposure value (default 1.0)</param>
        /// <param name="exposureTexture">Optional 1x1 exposure texture</param>
        /// <param name="biasColorMask">Optional bias color mask</param>
        /// <returns>True if execution was successful</returns>
        public bool Render(
            CommandBuffer cmd,
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            float jitterX,
            float jitterY,
            float mvScaleX,
            float mvScaleY,
            bool reset = false,
            float preExposure = 1.0f,
            RenderTexture exposureTexture = null,
            RenderTexture biasColorMask = null)
        {
            if (!IsSupported || Extension == null)
            {
                Debug.LogError("[DLSSSuperResolution] DLSS-SR is not supported");
                return false;
            }

            if (colorInput == null || colorOutput == null || depth == null || motionVectors == null)
            {
                Debug.LogError("[DLSSSuperResolution] Required textures are null");
                return false;
            }

            // Check if we need to recreate the feature
            uint inputW = (uint)colorInput.width;
            uint inputH = (uint)colorInput.height;
            uint outputW = (uint)colorOutput.width;
            uint outputH = (uint)colorOutput.height;

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
                DisposeResources(cmd);
                m_createParamsChanged = false;
            }

            // Initialize if needed
            if (!m_initialized)
            {
                if (!Initialize(cmd))
                {
                    return false;
                }
            }

            // Set evaluation parameters
            SetupEvalParams(
                colorInput, colorOutput, depth, motionVectors,
                jitterX, jitterY, mvScaleX, mvScaleY,
                reset, preExposure, exposureTexture, biasColorMask);

            // Execute
            Extension.EvaluateFeature(cmd, m_dlssHandle, m_dlssParameters);
            return true;
        }

        private bool Initialize(CommandBuffer cmd)
        {
            if (m_initialized)
                return true;

            var ext = Extension;
            if (ext == null)
            {
                Debug.LogError("[DLSSSuperResolution] DLSSExtension not available");
                return false;
            }

            // Allocate parameters
            var result = ext.AllocateParameters(out m_dlssParameters);
            if (DLSSExtension.NVSDK_NGX_FAILED(result))
            {
                Debug.LogError($"[DLSSSuperResolution] Failed to allocate parameters: {result}");
                return false;
            }

            // Set creation parameters
            ext.SetParameterUI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_CreationNodeMask, 1);
            ext.SetParameterUI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_VisibilityNodeMask, 1);
            ext.SetParameterUI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_Width, m_inputWidth);
            ext.SetParameterUI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_Height, m_inputHeight);
            ext.SetParameterUI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_OutWidth, m_outputWidth);
            ext.SetParameterUI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_OutHeight, m_outputHeight);
            ext.SetParameterI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_PerfQualityValue, (int)m_qualityValue);
            ext.SetParameterI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Feature_Create_Flags, (int)m_featureFlags);
            ext.SetParameterI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Enable_Output_Subrects, 0);

            // Create feature
            m_dlssHandle = ext.CreateFeature(cmd, NVSDK_NGX_Feature.NVSDK_NGX_Feature_SuperSampling, m_dlssParameters);
            if (m_dlssHandle == DLSSExtension.DLSS_INVALID_FEATURE_HANDLE)
            {
                Debug.LogError("[DLSSSuperResolution] Failed to create DLSS-SR feature");
                ext.DestroyParameters(m_dlssParameters);
                m_dlssParameters = IntPtr.Zero;
                return false;
            }

            m_initialized = true;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log($"[DLSSSuperResolution] Initialized: {m_inputWidth}x{m_inputHeight} -> {m_outputWidth}x{m_outputHeight}, Quality={m_qualityValue}");
#endif
            return true;
        }

        private void SetupEvalParams(
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            float jitterX,
            float jitterY,
            float mvScaleX,
            float mvScaleY,
            bool reset,
            float preExposure,
            RenderTexture exposureTexture,
            RenderTexture biasColorMask)
        {
            var ext = Extension;

            // Input textures
            ext.SetParameterRenderTexture(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_Color, colorInput);
            ext.SetParameterRenderTexture(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_Output, colorOutput);
            ext.SetParameterRenderTexture(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_Depth, depth);
            ext.SetParameterRenderTexture(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_MotionVectors, motionVectors);

            // Optional textures
            if (exposureTexture != null)
            {
                ext.SetParameterRenderTexture(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_ExposureTexture, exposureTexture);
            }
            if (biasColorMask != null)
            {
                ext.SetParameterRenderTexture(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Input_Bias_Current_Color_Mask, biasColorMask);
            }

            // Jitter in pixel space
            ext.SetParameterF(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_Jitter_Offset_X, jitterX);
            ext.SetParameterF(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_Jitter_Offset_Y, jitterY);

            // Motion vector scale
            ext.SetParameterF(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_MV_Scale_X, mvScaleX);
            ext.SetParameterF(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_MV_Scale_Y, mvScaleY);

            // Reset flag
            ext.SetParameterI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_Reset, reset ? 1 : 0);

            // Render subrect dimensions
            ext.SetParameterUI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Render_Subrect_Dimensions_Width, m_inputWidth);
            ext.SetParameterUI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Render_Subrect_Dimensions_Height, m_inputHeight);

            // Exposure
            ext.SetParameterF(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Pre_Exposure, preExposure);
            ext.SetParameterF(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Exposure_Scale, 1.0f);

            // Y-axis inversion for Unity
            ext.SetParameterI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Indicator_Invert_Y_Axis, 1);
            ext.SetParameterI(m_dlssParameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Indicator_Invert_X_Axis, 0);
        }

        private void DisposeResources(CommandBuffer cmd)
        {
            if (m_initialized)
            {
                var ext = Extension;
                if (ext != null)
                {
                    if (m_dlssHandle != DLSSExtension.DLSS_INVALID_FEATURE_HANDLE)
                    {
                        ext.DestroyFeature(cmd, m_dlssHandle);
                        m_dlssHandle = DLSSExtension.DLSS_INVALID_FEATURE_HANDLE;
                    }

                    if (m_dlssParameters != IntPtr.Zero)
                    {
                        ext.DestroyParameters(m_dlssParameters);
                        m_dlssParameters = IntPtr.Zero;
                    }
                }

                m_initialized = false;
            }
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
            float jitterX,
            float jitterY,
            float mvScaleX,
            float mvScaleY,
            bool reset = false,
            float preExposure = 1.0f,
            RenderTexture exposureTexture = null,
            RenderTexture biasColorMask = null)
        {
            return false;
        }

        public void Dispose() { }
#endif
    }
}
