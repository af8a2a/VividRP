//------------------------------------------------------------------------------
// DLSSRayReconstruction.cs - DLSS Ray Reconstruction Implementation
//------------------------------------------------------------------------------
// Simplified wrapper for DLSS-RR integration following the reference pattern.
// Manages DLSS-RR feature lifecycle and execution via CommandBuffer.
//------------------------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    using static DLSSSdk;

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
        private int m_dlssHandle = DLSS_INVALID_FEATURE_HANDLE;
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

        // RR-specific NGX parameter names
        private const string NVSDK_NGX_Parameter_DLSS_Denoise_Mode = "DLSS.Denoise.Mode";
        private const string NVSDK_NGX_Parameter_DLSS_Depth_Type = "DLSS.Depth.Type";
        private const string NVSDK_NGX_Parameter_DLSS_Roughness_Mode = "DLSS.Roughness.Mode";

        // GBuffer parameters
        private const string NVSDK_NGX_Parameter_DiffuseAlbedo = "DiffuseAlbedo";
        private const string NVSDK_NGX_Parameter_SpecularAlbedo = "SpecularAlbedo";
        private const string NVSDK_NGX_Parameter_Normals = "Normals";
        private const string NVSDK_NGX_Parameter_Roughness = "Roughness";
        private const string NVSDK_NGX_Parameter_Emissive = "Emissive";

        // Ray data parameters
        private const string NVSDK_NGX_Parameter_DiffuseRayDirection = "DiffuseRayDirection";
        private const string NVSDK_NGX_Parameter_DiffuseHitDistance = "DiffuseHitDistance";
        private const string NVSDK_NGX_Parameter_SpecularRayDirection = "SpecularRayDirection";
        private const string NVSDK_NGX_Parameter_SpecularHitDistance = "SpecularHitDistance";
        private const string NVSDK_NGX_Parameter_DiffuseRayDirectionHitDistance = "DiffuseRayDirectionHitDistance";
        private const string NVSDK_NGX_Parameter_SpecularRayDirectionHitDistance = "SpecularRayDirectionHitDistance";

        // Matrix parameters
        private const string NVSDK_NGX_Parameter_WorldToViewMatrix = "WorldToViewMatrix";
        private const string NVSDK_NGX_Parameter_ViewToClipMatrix = "ViewToClipMatrix";

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
            DLSS_Init();

            m_featureFlags = featureFlags;
            m_qualityValue = qualityValue;
            m_depthType = depthType;
            m_roughnessMode = roughnessMode;
        }

        /// <summary>
        /// Check if DLSS-RR is supported on the current system.
        /// </summary>
        public bool IsSupported => DLSS_IsRayReconstructionAvailable();

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
        /// <param name="jitterX">Jitter X in render pixels</param>
        /// <param name="jitterY">Jitter Y in render pixels</param>
        /// <param name="mvScaleX">Motion vector scale X</param>
        /// <param name="mvScaleY">Motion vector scale Y</param>
        /// <param name="reset">Reset temporal history</param>
        /// <param name="frameTimeDeltaMs">Frame time delta in milliseconds</param>
        /// <returns>True if successful</returns>
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
            float jitterX,
            float jitterY,
            float mvScaleX,
            float mvScaleY,
            bool reset = false,
            float frameTimeDeltaMs = 0.0f)
        {
            if (!IsSupported)
            {
                Debug.LogError("[DLSSRayReconstruction] DLSS-RR is not supported");
                return false;
            }

            // Validate required inputs
            if (!ValidateInputs(colorInput, colorOutput, depth, motionVectors, gbuffer, rayInputs))
            {
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
                gbuffer, rayInputs,
                worldToView, viewToClip,
                jitterX, jitterY, mvScaleX, mvScaleY,
                reset, frameTimeDeltaMs);

            // Execute
            DLSS_EvaluateFeature(cmd, m_dlssHandle, m_dlssParameters);
            return true;
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

        private bool Initialize(CommandBuffer cmd)
        {
            if (m_initialized)
                return true;

            // Allocate parameters
            var result = DLSS_AllocateParameters_D3D12(out m_dlssParameters);
            if (NVSDK_NGX_FAILED(result))
            {
                Debug.LogError($"[DLSSRayReconstruction] Failed to allocate parameters: {result}");
                return false;
            }

            // Set creation parameters
            DLSS_Parameter_SetUI(m_dlssParameters, NVSDK_NGX_Parameter_CreationNodeMask, 1);
            DLSS_Parameter_SetUI(m_dlssParameters, NVSDK_NGX_Parameter_VisibilityNodeMask, 1);
            DLSS_Parameter_SetUI(m_dlssParameters, NVSDK_NGX_Parameter_Width, m_inputWidth);
            DLSS_Parameter_SetUI(m_dlssParameters, NVSDK_NGX_Parameter_Height, m_inputHeight);
            DLSS_Parameter_SetUI(m_dlssParameters, NVSDK_NGX_Parameter_OutWidth, m_outputWidth);
            DLSS_Parameter_SetUI(m_dlssParameters, NVSDK_NGX_Parameter_OutHeight, m_outputHeight);
            DLSS_Parameter_SetI(m_dlssParameters, NVSDK_NGX_Parameter_PerfQualityValue, (int)m_qualityValue);
            DLSS_Parameter_SetI(m_dlssParameters, NVSDK_NGX_Parameter_DLSS_Feature_Create_Flags, (int)m_featureFlags);
            DLSS_Parameter_SetI(m_dlssParameters, NVSDK_NGX_Parameter_DLSS_Enable_Output_Subrects, 0);

            // RR-specific creation parameters
            DLSS_Parameter_SetI(m_dlssParameters, NVSDK_NGX_Parameter_DLSS_Denoise_Mode, (int)m_denoiseMode);
            DLSS_Parameter_SetI(m_dlssParameters, NVSDK_NGX_Parameter_DLSS_Depth_Type, (int)m_depthType);
            DLSS_Parameter_SetI(m_dlssParameters, NVSDK_NGX_Parameter_DLSS_Roughness_Mode, (int)m_roughnessMode);

            // Create feature
            m_dlssHandle = DLSS_CreateFeature(cmd, NVSDK_NGX_Feature.NVSDK_NGX_Feature_RayReconstruction, m_dlssParameters);
            if (m_dlssHandle == DLSS_INVALID_FEATURE_HANDLE)
            {
                Debug.LogError("[DLSSRayReconstruction] Failed to create DLSS-RR feature");
                DLSS_DestroyParameters_D3D12(m_dlssParameters);
                m_dlssParameters = IntPtr.Zero;
                return false;
            }

            m_initialized = true;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log($"[DLSSRayReconstruction] Initialized: {m_inputWidth}x{m_inputHeight} -> {m_outputWidth}x{m_outputHeight}, Quality={m_qualityValue}");
#endif
            return true;
        }

        private void SetupEvalParams(
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            DLSSRRGBuffer gbuffer,
            DLSSRRRayInputs rayInputs,
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            float jitterX,
            float jitterY,
            float mvScaleX,
            float mvScaleY,
            bool reset,
            float frameTimeDeltaMs)
        {
            // Common textures
            DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_Color, colorInput);
            DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_Output, colorOutput);
            DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_Depth, depth);
            DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_MotionVectors, motionVectors);

            // GBuffer
            DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_DiffuseAlbedo, gbuffer.DiffuseAlbedo);
            DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_SpecularAlbedo, gbuffer.SpecularAlbedo);
            DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_Normals, gbuffer.Normals);

            if (gbuffer.Roughness != null)
            {
                DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_Roughness, gbuffer.Roughness);
            }
            if (gbuffer.Emissive != null)
            {
                DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_Emissive, gbuffer.Emissive);
            }

            // Ray inputs (prefer separate direction/distance if available)
            if (rayInputs.DiffuseRayDirection != null)
            {
                DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_DiffuseRayDirection, rayInputs.DiffuseRayDirection);
                DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_DiffuseHitDistance, rayInputs.DiffuseHitDistance);
            }
            else
            {
                DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_DiffuseRayDirectionHitDistance, rayInputs.DiffuseRayDirectionHitDistance);
            }

            if (rayInputs.SpecularRayDirection != null)
            {
                DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_SpecularRayDirection, rayInputs.SpecularRayDirection);
                DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_SpecularHitDistance, rayInputs.SpecularHitDistance);
            }
            else
            {
                DLSS_Parameter_SetD3d12RenderTexture(m_dlssParameters, NVSDK_NGX_Parameter_SpecularRayDirectionHitDistance, rayInputs.SpecularRayDirectionHitDistance);
            }

            // Matrices (required for RR)
            DLSS_Parameter_SetMatrix4x4(m_dlssParameters, NVSDK_NGX_Parameter_WorldToViewMatrix, worldToView);
            DLSS_Parameter_SetMatrix4x4(m_dlssParameters, NVSDK_NGX_Parameter_ViewToClipMatrix, viewToClip);

            // Jitter
            DLSS_Parameter_SetF(m_dlssParameters, NVSDK_NGX_Parameter_Jitter_Offset_X, jitterX);
            DLSS_Parameter_SetF(m_dlssParameters, NVSDK_NGX_Parameter_Jitter_Offset_Y, jitterY);

            // Motion vector scale
            DLSS_Parameter_SetF(m_dlssParameters, NVSDK_NGX_Parameter_MV_Scale_X, mvScaleX == 0 ? 1.0f : mvScaleX);
            DLSS_Parameter_SetF(m_dlssParameters, NVSDK_NGX_Parameter_MV_Scale_Y, mvScaleY == 0 ? 1.0f : mvScaleY);

            // Reset flag
            DLSS_Parameter_SetI(m_dlssParameters, NVSDK_NGX_Parameter_Reset, reset ? 1 : 0);

            // Render subrect dimensions
            DLSS_Parameter_SetUI(m_dlssParameters, NVSDK_NGX_Parameter_DLSS_Render_Subrect_Dimensions_Width, m_inputWidth);
            DLSS_Parameter_SetUI(m_dlssParameters, NVSDK_NGX_Parameter_DLSS_Render_Subrect_Dimensions_Height, m_inputHeight);

            // Frame time delta
            DLSS_Parameter_SetF(m_dlssParameters, NVSDK_NGX_Parameter_FrameTimeDeltaInMsec, frameTimeDeltaMs);

            // Exposure defaults
            DLSS_Parameter_SetF(m_dlssParameters, NVSDK_NGX_Parameter_DLSS_Pre_Exposure, 1.0f);
            DLSS_Parameter_SetF(m_dlssParameters, NVSDK_NGX_Parameter_DLSS_Exposure_Scale, 1.0f);

            // Y-axis inversion for Unity
            DLSS_Parameter_SetI(m_dlssParameters, NVSDK_NGX_Parameter_DLSS_Indicator_Invert_Y_Axis, 1);
            DLSS_Parameter_SetI(m_dlssParameters, NVSDK_NGX_Parameter_DLSS_Indicator_Invert_X_Axis, 0);
        }

        private void DisposeResources(CommandBuffer cmd)
        {
            if (m_initialized)
            {
                if (m_dlssHandle != DLSS_INVALID_FEATURE_HANDLE)
                {
                    DLSS_DestroyFeature(cmd, m_dlssHandle);
                    m_dlssHandle = DLSS_INVALID_FEATURE_HANDLE;
                }

                if (m_dlssParameters != IntPtr.Zero)
                {
                    DLSS_DestroyParameters_D3D12(m_dlssParameters);
                    m_dlssParameters = IntPtr.Zero;
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
                using (var cmd = new CommandBuffer())
                {
                    cmd.name = "DLSS-RR Cleanup";
                    DisposeResources(cmd);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
            }

            DLSS_Shutdown();
            m_disposed = true;
        }

        ~DLSSRayReconstruction()
        {
            Dispose(false);
        }
    }
}
