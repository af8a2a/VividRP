//------------------------------------------------------------------------------
// DLSSRRDenoiser.cs - DLSS Ray Reconstruction Denoiser for VividRP
//------------------------------------------------------------------------------
// Simplified wrapper for DLSS-RR integration.
// Uses DLSSRayReconstruction directly with pre-prepared GBuffer inputs.
//
// Enable with scripting define: DLSS_PLUGIN_INTEGRATE
//------------------------------------------------------------------------------

#if DLSS_PLUGIN_INTEGRATE

using System;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// DLSS Ray Reconstruction (RR) denoiser for path tracing.
    /// Uses NVIDIA's AI-based denoiser with integrated upscaling.
    /// Expects DXR GBuffer to provide DLSS-RR native format directly.
    /// </summary>
    public class DLSSRRDenoiser : IDisposable
    {
        private DLSSRayReconstruction m_DlssRR;
        private bool m_Initialized;
        private int m_InputWidth;
        private int m_InputHeight;
        private int m_OutputWidth;
        private int m_OutputHeight;

        /// <summary>
        /// Settings for DLSS-RR denoiser
        /// </summary>
        public struct Settings
        {
            public DLSSQuality quality;
            public bool resetHistory;
            public float preExposure;
            public float exposureScale;
            public float frameTimeDeltaMs;
            public float sharpness;
            public bool autoExposure;
            public bool isHDR;

            public static Settings Default => new Settings
            {
                quality = DLSSQuality.Balanced,
                resetHistory = false,
                preExposure = 1.0f,
                exposureScale = 1.0f,
                frameTimeDeltaMs = 16.67f,
                sharpness = 0.0f,
                autoExposure = false,
                isHDR = true
            };
        }

        /// <summary>
        /// Check if DLSS-RR is available on the current system
        /// </summary>
        public static bool IsSupported => DLSSExtension.Instance?.IsRRSupported ?? false;

        /// <summary>
        /// Create a new DLSS-RR denoiser instance
        /// </summary>
        public DLSSRRDenoiser()
        {
            m_Initialized = false;
        }

        /// <summary>
        /// Initialize or update the DLSS-RR context for the given resolution
        /// </summary>
        public bool Initialize(int inputWidth, int inputHeight, int outputWidth, int outputHeight,
            DLSSQuality quality, bool isHDR = true, bool autoExposure = false)
        {
            if (!IsSupported)
            {
                Debug.LogError("[DLSSRRDenoiser] DLSS-RR not supported on this system");
                return false;
            }

            // Map user-facing quality to internal NGX value
            var ngxQuality = quality.ToNGXQuality();

            bool needsRecreate = !m_Initialized ||
                                 m_InputWidth != inputWidth ||
                                 m_InputHeight != inputHeight ||
                                 m_OutputWidth != outputWidth ||
                                 m_OutputHeight != outputHeight;

            if (!needsRecreate && m_DlssRR != null)
            {
                m_DlssRR.SetQuality(ngxQuality);
                return true;
            }

            // Dispose existing wrapper
            m_DlssRR?.Dispose();

            // Create feature flags
            var flags = NVSDK_NGX_DLSS_Feature_Flags.DepthInverted|NVSDK_NGX_DLSS_Feature_Flags.MVLowRes;
            if (isHDR)
                flags |= NVSDK_NGX_DLSS_Feature_Flags.IsHDR;
            if (autoExposure)
                flags |= NVSDK_NGX_DLSS_Feature_Flags.AutoExposure;

            // Create new wrapper - roughness packed in normals.w
            m_DlssRR = new DLSSRayReconstruction(
                flags,
                ngxQuality,
                DLSSRayReconstruction.DepthType.Hardware,
                DLSSRayReconstruction.RoughnessMode.PackedInNormalsW
            );

            m_InputWidth = inputWidth;
            m_InputHeight = inputHeight;
            m_OutputWidth = outputWidth;
            m_OutputHeight = outputHeight;
            m_Initialized = true;

            return true;
        }

        
        /// <summary>
        /// Execute DLSS-RR denoising with pre-prepared DXR GBuffer inputs.
        /// DXR GBuffer should output DLSS-RR native format:
        /// - DiffuseAlbedo: albedo * (1-metallic)
        /// - SpecularAlbedo: EnvBRDFApprox2(F0, roughness, NoV)
        /// - NormalRoughness: world normal + sqrt(alphaRoughness) in alpha
        /// </summary>
        public bool Execute(
            CommandBuffer cmd,
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            RenderTexture diffuseAlbedo,
            RenderTexture specularAlbedo,
            RenderTexture normalRoughness,
            RenderTexture diffuseHitDistance,
            RenderTexture specularHitDistance,
            Vector2 jitterOffset,
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            Settings settings)
        {
            if (!m_Initialized || m_DlssRR == null)
            {
                Debug.LogError("[DLSSRRDenoiser] Denoiser not initialized");
                return false;
            }

            if (colorInput == null || colorOutput == null || depth == null || motionVectors == null)
            {
                Debug.LogError("[DLSSRRDenoiser] Required textures are null");
                return false;
            }

            // Build GBuffer from pre-prepared DXR GBuffer outputs
            var gbuffer = new DLSSRRGBuffer
            {
                DiffuseAlbedo = diffuseAlbedo,
                SpecularAlbedo = specularAlbedo,
                Normals = normalRoughness,
                Roughness = null  // Packed in normals.w
            };

            // Build ray inputs - hit distances from path tracer output alpha channels
            var rayInputs = new DLSSRRRayInputs
            {
                DiffuseRayDirectionHitDistance = diffuseHitDistance,
                SpecularRayDirectionHitDistance = specularHitDistance
            };

            // Execute via DLSSRayReconstruction wrapper
            return m_DlssRR.Render(
                cmd,
                colorInput,
                colorOutput,
                depth,
                motionVectors,
                gbuffer,
                rayInputs,
                worldToView,
                viewToClip,
                jitterOffset.x * m_InputWidth,   // Convert to pixel space
                jitterOffset.y * m_InputHeight,
                -(float)m_InputWidth,   // Unity convention
                -(float)m_InputHeight,
                settings.resetHistory,
                settings.frameTimeDeltaMs
            );
        }

        /// <summary>
        /// Check if the denoiser is initialized and ready
        /// </summary>
        public bool IsReady => m_Initialized && m_DlssRR != null;

        /// <summary>
        /// Current input resolution
        /// </summary>
        public Vector2Int InputResolution => new Vector2Int(m_InputWidth, m_InputHeight);

        /// <summary>
        /// Current output resolution
        /// </summary>
        public Vector2Int OutputResolution => new Vector2Int(m_OutputWidth, m_OutputHeight);

        /// <summary>
        /// Dispose of the denoiser and release resources
        /// </summary>
        public void Dispose()
        {
            m_DlssRR?.Dispose();
            m_DlssRR = null;
            m_Initialized = false;
        }
    }
}

#endif
