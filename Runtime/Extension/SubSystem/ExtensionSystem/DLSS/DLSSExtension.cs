//------------------------------------------------------------------------------
// DLSSExtension.cs - DLSS Extension for VividRP ExtensionSystem
//------------------------------------------------------------------------------
// Integrates NVIDIA DLSS (Deep Learning Super Sampling) into VividRP.
// Supports both DLSS-SR (Super Resolution) and DLSS-RR (Ray Reconstruction).
//
// Enable with scripting define: DLSS_PLUGIN_INTEGRATE
//------------------------------------------------------------------------------

#if DLSS_PLUGIN_INTEGRATE
using System;
#endif

namespace UnityEngine.Rendering.Universal
{
    using static DLSSSdk;

    /// <summary>
    /// DLSS Extension for VividRP ExtensionSystem.
    /// Handles initialization and capability detection for NVIDIA DLSS.
    /// </summary>
    public class DLSSExtension : IExtension
    {
#if DLSS_PLUGIN_INTEGRATE
        private bool m_Initialized = false;
        private bool m_SRSupported = false;
        private bool m_RRSupported = false;

        /// <summary>
        /// Check if DLSS-SR (Super Resolution) is supported
        /// </summary>
        public bool IsSRSupported => m_SRSupported;

        /// <summary>
        /// Check if DLSS-RR (Ray Reconstruction) is supported
        /// </summary>
        public bool IsRRSupported => m_RRSupported;

        /// <summary>
        /// Get the singleton instance from ExtensionSystem
        /// </summary>
        public static DLSSExtension Instance
        {
            get
            {
                if (ExtensionSystem.RegisteredExtensions.TryGetValue(HardwareExtension.DLSS, out var ext))
                    return ext as DLSSExtension;
                return null;
            }
        }
#endif

        public void Init()
        {
#if DLSS_PLUGIN_INTEGRATE
            Debug.Log("[DLSSExtension] Initializing DLSS...");
            Debug.Log($"[DLSSExtension] Graphics Device: {SystemInfo.graphicsDeviceName}");
            Debug.Log($"[DLSSExtension] Graphics Vendor: {SystemInfo.graphicsDeviceVendor}");

            // Check if NVIDIA GPU
            if (!SystemInfo.graphicsDeviceVendor.ToLowerInvariant().Contains("nvidia"))
            {
                Debug.Log("[DLSSExtension] Non-NVIDIA GPU detected. DLSS is not available.");
                m_Initialized = false;
                return;
            }

            // Check if D3D12
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                Debug.LogWarning("[DLSSExtension] DLSS requires Direct3D12. Current API: " + SystemInfo.graphicsDeviceType);
                m_Initialized = false;
                return;
            }

            try
            {
                // Initialize DLSS using new SDK (reference-counted)
                var result = DLSS_Init();
                m_Initialized = NVSDK_NGX_SUCCEED(result);

                if (!m_Initialized)
                {
                    Debug.LogWarning($"[DLSSExtension] DLSS initialization failed: {result}");
                    return;
                }

                // Query capabilities using new SDK
                m_SRSupported = DLSS_IsSuperSamplingAvailable();
                m_RRSupported = DLSS_IsRayReconstructionAvailable();

                Debug.Log($"[DLSSExtension] DLSS-SR Available: {m_SRSupported}");
                Debug.Log($"[DLSSExtension] DLSS-RR Available: {m_RRSupported}");

                Debug.Log("[DLSSExtension] DLSS initialized successfully!");
            }
            catch (DllNotFoundException e)
            {
                Debug.LogWarning($"[DLSSExtension] DLSS DLL not found: {e.Message}");
                Debug.LogWarning("[DLSSExtension] Make sure UnityDLSS.dll and nvngx_*.dll are in Assets/Plugins/x86_64/");
                m_Initialized = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DLSSExtension] DLSS initialization error: {e.Message}");
                m_Initialized = false;
            }
#else
            Debug.Log("[DLSSExtension] DLSS plugin not integrated. Define DLSS_PLUGIN_INTEGRATE to enable.");
#endif
        }

        public bool Support()
        {
#if DLSS_PLUGIN_INTEGRATE
            // Consider DLSS supported if either SR or RR is available
            return m_Initialized && (m_SRSupported || m_RRSupported);
#else
            return false;
#endif
        }

        public HardwareExtension GetExtension()
        {
            return HardwareExtension.DLSS;
        }

#if DLSS_PLUGIN_INTEGRATE
        /// <summary>
        /// Shutdown DLSS (call on application quit)
        /// </summary>
        public void Shutdown()
        {
            if (m_Initialized)
            {
                DLSS_Shutdown();
                m_Initialized = false;
                m_SRSupported = false;
                m_RRSupported = false;
                Debug.Log("[DLSSExtension] DLSS shutdown complete.");
            }
        }
#endif
    }
}
