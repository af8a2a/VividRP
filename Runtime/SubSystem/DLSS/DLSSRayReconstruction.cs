#if DLSS_PLUGIN_INTEGRATE

//------------------------------------------------------------------------------
// DLSSRayReconstruction.cs - DLSS Ray Reconstruction Implementation
//------------------------------------------------------------------------------
// Simplified wrapper for DLSS-RR integration following the reference pattern.
// Manages DLSS-RR feature lifecycle and execution via CommandBuffer.
//------------------------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    /// <summary>
    /// GBuffer input configuration for DLSS-RR.
    /// </summary>
    public class DLSSRRGBuffer
    {
        public RenderTexture DiffuseAlbedo;     // Required: Diffuse albedo
        public RenderTexture SpecularAlbedo;    // Required: Specular albedo (F0)
        public RenderTexture Normals;           // Required: World-space normals
        public RenderTexture Roughness;         // Optional if packed in normals.w
        public RenderTexture Emissive;          // Optional
    }

    /// <summary>
    /// Ray tracing inputs for DLSS-RR.
    /// Provide either separate direction+distance OR combined direction+distance textures.
    /// </summary>
    public class DLSSRRRayInputs
    {
        // Separate diffuse ray data
        public RenderTexture DiffuseRayDirection;   // RGB: normalized direction
        public RenderTexture DiffuseHitDistance;    // R: hit distance

        // Separate specular ray data
        public RenderTexture SpecularRayDirection;  // RGB: normalized direction
        public RenderTexture SpecularHitDistance;   // R: hit distance

        // Alternative: Combined direction+distance (RGBA)
        public RenderTexture DiffuseRayDirectionHitDistance;
        public RenderTexture SpecularRayDirectionHitDistance;

        /// <summary>
        /// Check if valid diffuse ray data is provided.
        /// </summary>
        public bool HasValidDiffuseRays =>
            (DiffuseRayDirection != null && DiffuseHitDistance != null) ||
            DiffuseRayDirectionHitDistance != null;

        /// <summary>
        /// Check if valid specular ray data is provided.
        /// </summary>
        public bool HasValidSpecularRays =>
            (SpecularRayDirection != null && SpecularHitDistance != null) ||
            SpecularRayDirectionHitDistance != null;
    }

    /// <summary>
    /// DLSS Ray Reconstruction render pass implementation.
    /// Manages DLSS-RR feature lifecycle and execution.
    /// </summary>
    public class DLSSRayReconstruction : IDisposable
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
        /// Denoise mode for RR.
        /// </summary>
        public enum DenoiseMode : int
        {
            Off = 0,
            DLUnified = 1  // Required for DLSS-RR
        }

        /// <summary>
        /// Depth type for RR.
        /// </summary>
        public enum DepthType : int
        {
            Linear = 0,
            Hardware = 1
        }

        /// <summary>
        /// Roughness packing mode.
        /// </summary>
        public enum RoughnessMode : int
        {
            Unpacked = 0,           // Separate roughness texture
            PackedInNormalsW = 1    // Roughness in normals.w
        }

        private DenoiseMode m_denoiseMode = DenoiseMode.DLUnified;
        private DepthType m_depthType = DepthType.Hardware;
        private RoughnessMode m_roughnessMode = RoughnessMode.Unpacked;

        /// <summary>
        /// Create a new DLSS-RR instance.
        /// </summary>
        /// <param name="featureFlags">Feature creation flags</param>
        /// <param name="qualityValue">Quality/performance preset</param>
        /// <param name="depthType">Depth buffer type</param>
        /// <param name="roughnessMode">Roughness packing mode</param>
        public DLSSRayReconstruction(
            NVSDK_NGX_DLSS_Feature_Flags featureFlags = NVSDK_NGX_DLSS_Feature_Flags.None,
            NVSDK_NGX_PerfQuality_Value qualityValue = NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_Balanced,
            DepthType depthType = DepthType.Hardware,
            RoughnessMode roughnessMode = RoughnessMode.Unpacked)
        {
            m_featureFlags = featureFlags;
            m_qualityValue = qualityValue;
            m_depthType = depthType;
            m_roughnessMode = roughnessMode;
        }

        /// <summary>
        /// Check if DLSS-RR is supported on the current system.
        /// </summary>
        public bool IsSupported => Extension?.IsRRSupported ?? false;

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
        /// Execute DLSS-RR.
        /// </summary>
        /// <param name="cmd">Command buffer</param>
        /// <param name="colorInput">Noisy color input (render resolution)</param>
        /// <param name="colorOutput">Denoised+upscaled output (display resolution)</param>
        /// <param name="depth">Depth buffer</param>
        /// <param name="motionVectors">Motion vectors</param>
        /// <param name="gbuffer">GBuffer inputs (albedo, normals, etc.)</param>
        /// <param name="rayInputs">Ray tracing inputs (direction, hit distance)</param>
        /// <param name="worldToView">World to view matrix</param>
        /// <param name="viewToClip">View to clip (projection) matrix</param>
        /// <param name="jitterOffset">Jitter offset explicitly converted to input/render pixels</param>
        /// <param name="motionVectorEncoding">Units and direction encoded in the motion-vector texture</param>
        /// <param name="exposure">Explicit per-frame pre-exposure, exposure scale, and optional final-exposure texture</param>
        /// <param name="reset">Reset temporal history</param>
        /// <param name="frameTimeDeltaMs">Frame time delta in milliseconds</param>
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
            DLSSRRGBuffer gbuffer,
            DLSSRRRayInputs rayInputs,
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            DLSSJitterOffset jitterOffset,
            DLSSMotionVectorEncoding motionVectorEncoding,
            DLSSExposure exposure,
            bool reset = false,
            float frameTimeDeltaMs = 0.0f)
        {
            if (!IsSupported || Extension == null)
            {
                Debug.LogError("[DLSSRayReconstruction] DLSS-RR is not supported");
                RecordFallback(cmd, colorInput, colorOutput);
                return false;
            }

            // Validate required inputs
            if (!ValidateInputs(colorInput, colorOutput, depth, motionVectors, gbuffer, rayInputs))
            {
                RecordFallback(cmd, colorInput, colorOutput);
                return false;
            }

            if (!exposure.TryValidate(out string exposureError))
            {
                Debug.LogError($"[DLSSRayReconstruction] Invalid exposure contract: {exposureError}");
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

            // Execute
            if (Extension.EvaluateRayReconstructionFeature(
                    cmd,
                    m_dlssHandle,
                    colorInput,
                    colorOutput,
                    depth,
                    motionVectors,
                    exposure,
                    gbuffer.DiffuseAlbedo,
                    gbuffer.SpecularAlbedo,
                    gbuffer.Normals,
                    gbuffer.Roughness,
                    gbuffer.Emissive,
                    rayInputs.DiffuseRayDirection,
                    rayInputs.DiffuseHitDistance,
                    rayInputs.DiffuseRayDirectionHitDistance,
                    rayInputs.SpecularRayDirection,
                    rayInputs.SpecularHitDistance,
                    rayInputs.SpecularRayDirectionHitDistance,
                    worldToView,
                    viewToClip,
                    jitterPixels.x,
                    jitterPixels.y,
                    motionVectorScale.x,
                    motionVectorScale.y,
                    reset,
                    m_inputWidth,
                    m_inputHeight,
                    frameTimeDeltaMs))
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

        private bool ValidateInputs(
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            DLSSRRGBuffer gbuffer,
            DLSSRRRayInputs rayInputs)
        {
            if (colorInput == null || colorOutput == null || depth == null || motionVectors == null)
            {
                Debug.LogError("[DLSSRayReconstruction] Required common textures are null");
                return false;
            }

            if (gbuffer == null)
            {
                Debug.LogError("[DLSSRayReconstruction] GBuffer is null");
                return false;
            }

            if (gbuffer.DiffuseAlbedo == null || gbuffer.SpecularAlbedo == null || gbuffer.Normals == null)
            {
                Debug.LogError("[DLSSRayReconstruction] Required GBuffer textures (DiffuseAlbedo, SpecularAlbedo, Normals) are null");
                return false;
            }

            if (rayInputs == null)
            {
                Debug.LogError("[DLSSRayReconstruction] Ray inputs are null");
                return false;
            }

            if (!rayInputs.HasValidDiffuseRays || !rayInputs.HasValidSpecularRays)
            {
                Debug.LogError("[DLSSRayReconstruction] Invalid ray inputs - need either (Direction + HitDistance) or (DirectionHitDistance) for both diffuse and specular");
                return false;
            }

            return true;
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
                Debug.LogError("[DLSSRayReconstruction] DLSSExtension not available");
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
                        Debug.Log($"[DLSSRayReconstruction] Initialized: {m_inputWidth}x{m_inputHeight} -> {m_outputWidth}x{m_outputHeight}, Quality={m_qualityValue}");
#endif
                        return true;

                    case DLSSFeatureStatus.Failed:
                        Debug.LogError($"[DLSSRayReconstruction] Native DLSS-RR creation failed: {createResult}");
                        DiscardFailedInitialization(ext, true);
                        return false;

                    default:
                        Debug.LogError("[DLSSRayReconstruction] Native DLSS-RR feature handle became invalid");
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
                Debug.LogError($"[DLSSRayReconstruction] Failed to allocate parameters: {result}");
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

            // RR-specific creation parameters
            ext.SetParameterI(parameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Denoise_Mode, (int)m_denoiseMode);
            ext.SetParameterI(parameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Depth_Type, (int)m_depthType);
            ext.SetParameterI(parameters, DLSSExtension.NVSDK_NGX_Parameter_DLSS_Roughness_Mode, (int)m_roughnessMode);

            // Create feature
            m_dlssHandle = ext.CreateFeature(
                cmd,
                NVSDK_NGX_Feature.NVSDK_NGX_Feature_RayReconstruction,
                parameters);
            if (m_dlssHandle == DLSSExtension.DLSS_INVALID_FEATURE_HANDLE)
            {
                Debug.LogError("[DLSSRayReconstruction] Failed to create DLSS-RR feature");
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
                using (var cmd = new CommandBuffer())
                {
                    cmd.name = "DLSS-RR Cleanup";
                    DisposeResources(cmd);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
            }

            m_disposed = true;
        }

        ~DLSSRayReconstruction()
        {
            Dispose(false);
        }
#else
        public enum DenoiseMode : int { Off = 0, DLUnified = 1 }
        public enum DepthType : int { Linear = 0, Hardware = 1 }
        public enum RoughnessMode : int { Unpacked = 0, PackedInNormalsW = 1 }

        public DLSSRayReconstruction(
            NVSDK_NGX_DLSS_Feature_Flags featureFlags = NVSDK_NGX_DLSS_Feature_Flags.None,
            NVSDK_NGX_PerfQuality_Value qualityValue = NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_Balanced,
            DepthType depthType = DepthType.Hardware,
            RoughnessMode roughnessMode = RoughnessMode.Unpacked)
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
            DLSSRRGBuffer gbuffer,
            DLSSRRRayInputs rayInputs,
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            DLSSJitterOffset jitterOffset,
            DLSSMotionVectorEncoding motionVectorEncoding,
            DLSSExposure exposure,
            bool reset = false,
            float frameTimeDeltaMs = 0.0f)
        {
            return false;
        }

        public void Dispose() { }
#endif
    }
}

#endif
