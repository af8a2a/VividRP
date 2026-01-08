//------------------------------------------------------------------------------
// DLSSRRDenoiser.cs - DLSS Ray Reconstruction Denoiser for VividRP
//------------------------------------------------------------------------------
// Provides DLSS-RR based denoising for path tracing.
//
// Enable with scripting define: DLSS_PLUGIN_INTEGRATE
//------------------------------------------------------------------------------

#if DLSS_PLUGIN_INTEGRATE

using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using DLSS;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// DLSS Ray Reconstruction (RR) denoiser for path tracing.
    /// Uses NVIDIA's AI-based denoiser with integrated upscaling.
    /// </summary>
    public class DLSSRRDenoiser : IDisposable
    {
        private bool m_Initialized;
        private uint m_ViewId;
        private DLSSDimensions m_InputResolution;
        private DLSSDimensions m_OutputResolution;
        private DLSSQuality m_Quality;
        private bool m_ContextCreated;

        /// <summary>
        /// Settings for DLSS-RR denoiser
        /// </summary>
        public struct Settings
        {
            public DLSSQuality quality;
            public bool resetHistory;
            public float preExposure;
            public float frameTimeDeltaMs;

            public static Settings Default => new Settings
            {
                quality = DLSSQuality.Balanced,
                resetHistory = false,
                preExposure = 1.0f,
                frameTimeDeltaMs = 16.67f  // ~60fps
            };
        }

        /// <summary>
        /// Check if DLSS-RR is available on the current system
        /// </summary>
        public static bool IsSupported
        {
            get
            {
                if (!DLSSManager.IsInitialized)
                    return false;

                if (DLSSManager.TryGetCapabilities(out var caps))
                    return caps.IsRRAvailable;

                return false;
            }
        }

        /// <summary>
        /// Initialize DLSS system (call once at startup)
        /// </summary>
        public static bool InitializeDLSS(string projectId = null, string logPath = null)
        {
            if (DLSSManager.IsInitialized)
                return true;

            return DLSSManager.Initialize(
                appId: 0,
                projectId: projectId ?? Application.productName,
                engineVersion: Application.unityVersion,
                logPath: logPath ?? Application.persistentDataPath + "/DLSS"
            );
        }

        /// <summary>
        /// Shutdown DLSS system (call on application quit)
        /// </summary>
        public static void ShutdownDLSS()
        {
            DLSSManager.Shutdown();
        }

        /// <summary>
        /// Create a new DLSS-RR denoiser instance
        /// </summary>
        public DLSSRRDenoiser(uint viewId)
        {
            m_ViewId = viewId;
            m_Initialized = false;
            m_ContextCreated = false;
        }

        /// <summary>
        /// Initialize or update the DLSS-RR context for the given resolution
        /// </summary>
        public bool Initialize(int inputWidth, int inputHeight, int outputWidth, int outputHeight, DLSSQuality quality)
        {
            if (!DLSSManager.IsInitialized)
            {
                Debug.LogError("[DLSSRRDenoiser] DLSS not initialized. Call DLSSRRDenoiser.InitializeDLSS() first.");
                return false;
            }

            var newInputRes = new DLSSDimensions((uint)inputWidth, (uint)inputHeight);
            var newOutputRes = new DLSSDimensions((uint)outputWidth, (uint)outputHeight);

            // Check if we need to recreate the context
            bool needsRecreate = !m_ContextCreated ||
                                 m_InputResolution.width != newInputRes.width ||
                                 m_InputResolution.height != newInputRes.height ||
                                 m_OutputResolution.width != newOutputRes.width ||
                                 m_OutputResolution.height != newOutputRes.height ||
                                 m_Quality != quality;

            if (!needsRecreate)
                return true;

            // Destroy existing context if any
            if (m_ContextCreated)
            {
                DLSSManager.DestroyContext(m_ViewId);
                m_ContextCreated = false;
            }

            // Create new context
            var flags = DLSSFeatureFlags.DepthInverted  // Unity uses reversed-Z
                      | DLSSFeatureFlags.MVLowRes        // Motion vectors at render resolution
                      | DLSSFeatureFlags.IsHDR;          // HDR input

            var createParams = new DLSSContextCreateParams
            {
                mode = DLSSMode.RayReconstruction,
                quality = quality,
                inputResolution = newInputRes,
                outputResolution = newOutputRes,
                featureFlags = (uint)flags,
                denoiseMode = DLSSDenoiseMode.DLUnified,
                depthType = DLSSDepthType.Hardware,
                roughnessMode = DLSSRoughnessMode.Unpacked,

                // RR presets (use E for latest transformer with DoF support)
                presetRR_DLAA = DLSSRRPreset.E,
                presetRR_Quality = DLSSRRPreset.E,
                presetRR_Balanced = DLSSRRPreset.E,
                presetRR_Performance = DLSSRRPreset.E,
                presetRR_UltraPerformance = DLSSRRPreset.E,
                presetRR_UltraQuality = DLSSRRPreset.E
            };

            var result = DLSSNative.DLSS_CreateContext(m_ViewId, ref createParams);
            if (result != DLSSResult.Success)
            {
                Debug.LogError($"[DLSSRRDenoiser] Failed to create DLSS-RR context: {DLSSManager.GetResultString(result)}");
                return false;
            }

            m_InputResolution = newInputRes;
            m_OutputResolution = newOutputRes;
            m_Quality = quality;
            m_ContextCreated = true;
            m_Initialized = true;

            return true;
        }

        /// <summary>
        /// Get optimal render resolution for DLSS-RR
        /// </summary>
        public static bool TryGetOptimalRenderSize(DLSSQuality quality, int outputWidth, int outputHeight, out Vector2Int renderSize)
        {
            renderSize = new Vector2Int(outputWidth, outputHeight);

            if (!DLSSManager.IsInitialized)
                return false;

            if (DLSSManager.TryGetOptimalSettings(
                DLSSMode.RayReconstruction,
                quality,
                (uint)outputWidth,
                (uint)outputHeight,
                out var settings))
            {
                renderSize = settings.OptimalRenderSize;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Execute DLSS-RR denoising
        /// </summary>
        public bool Execute(
            CommandBuffer cmd,
            // Common textures
            RTHandle colorInput,        // Noisy diffuse + specular combined
            RTHandle colorOutput,       // Denoised output
            RTHandle depth,
            RTHandle motionVectors,
            // GBuffer
            RTHandle diffuseAlbedo,
            RTHandle specularAlbedo,
            RTHandle normals,
            RTHandle roughness,
            // Ray data
            RTHandle diffuseHitDistance,
            RTHandle specularHitDistance,
            // Per-frame parameters
            Vector2 jitterOffset,
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            Settings settings)
        {
            if (!m_Initialized || !m_ContextCreated)
            {
                Debug.LogError("[DLSSRRDenoiser] Denoiser not initialized");
                return false;
            }

            var executeParams = new DLSSExecuteParams
            {
                mode = DLSSMode.RayReconstruction,

                textures = new DLSSCommonTextures
                {
                    colorInput = colorInput.rt.GetNativeTexturePtr(),
                    colorOutput = colorOutput.rt.GetNativeTexturePtr(),
                    depth = depth.rt.GetNativeTexturePtr(),
                    motionVectors = motionVectors.rt.GetNativeTexturePtr()
                },

                common = new DLSSCommonParams
                {
                    jitterOffsetX = jitterOffset.x,
                    jitterOffsetY = jitterOffset.y,
                    mvScaleX = m_InputResolution.width,
                    mvScaleY = m_InputResolution.height,
                    renderSubrectDimensions = m_InputResolution,
                    reset = settings.resetHistory ? (byte)1 : (byte)0,
                    preExposure = settings.preExposure,
                    exposureScale = 1.0f
                },

                rrParams = new DLSSRRParams
                {
                    gbuffer = new DLSSRRGBufferTextures
                    {
                        diffuseAlbedo = diffuseAlbedo.rt.GetNativeTexturePtr(),
                        specularAlbedo = specularAlbedo.rt.GetNativeTexturePtr(),
                        normals = normals.rt.GetNativeTexturePtr(),
                        roughness = roughness != null ? roughness.rt.GetNativeTexturePtr() : IntPtr.Zero
                    },

                    rays = new DLSSRRRayTextures
                    {
                        // DLSS-RR can work with just hit distances (ray directions are optional)
                        diffuseHitDistance = diffuseHitDistance.rt.GetNativeTexturePtr(),
                        specularHitDistance = specularHitDistance.rt.GetNativeTexturePtr(),
                        diffuseRayDirection = IntPtr.Zero,  // Optional
                        specularRayDirection = IntPtr.Zero  // Optional
                    },

                    worldToViewMatrix = worldToView,
                    viewToClipMatrix = viewToClip,
                    frameTimeDeltaMs = settings.frameTimeDeltaMs
                }
            };

            var result = DLSSNative.DLSS_Execute(m_ViewId, ref executeParams);
            if (result != DLSSResult.Success)
            {
                Debug.LogError($"[DLSSRRDenoiser] DLSS-RR execute failed: {DLSSManager.GetResultString(result)}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Dispose of the denoiser and release resources
        /// </summary>
        public void Dispose()
        {
            if (m_ContextCreated)
            {
                DLSSManager.DestroyContext(m_ViewId);
                m_ContextCreated = false;
            }
            m_Initialized = false;
        }

        /// <summary>
        /// Check if the denoiser is initialized and ready
        /// </summary>
        public bool IsReady => m_Initialized && m_ContextCreated;

        /// <summary>
        /// Current view ID
        /// </summary>
        public uint ViewId => m_ViewId;

        /// <summary>
        /// Current input resolution
        /// </summary>
        public Vector2Int InputResolution => new Vector2Int((int)m_InputResolution.width, (int)m_InputResolution.height);

        /// <summary>
        /// Current output resolution
        /// </summary>
        public Vector2Int OutputResolution => new Vector2Int((int)m_OutputResolution.width, (int)m_OutputResolution.height);
    }
}

#endif
