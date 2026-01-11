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
        private bool m_NeedsDriverUpdate = false;

        /// <summary>
        /// Check if DLSS-SR (Super Resolution) is supported
        /// </summary>
        public bool IsSRSupported => m_SRSupported;

        /// <summary>
        /// Check if DLSS-RR (Ray Reconstruction) is supported
        /// </summary>
        public bool IsRRSupported => m_RRSupported;

        /// <summary>
        /// Check if driver update is needed for full DLSS support
        /// </summary>
        public bool NeedsDriverUpdate => m_NeedsDriverUpdate;

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
            // Debug.Log($"[DLSSExtension] {DLSSNative.DLSS_GetResultString(DLSSResult.Success)}");
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
                // Initialize DLSS through DLSSManager (sets internal s_Initialized flag)
                m_Initialized = DLSSManager.Initialize(
                    0Xffaacae,  // appId
                    Application.productName,
                    Application.unityVersion,
                    Application.persistentDataPath + "/DLSS"
                );

                if (!m_Initialized)
                {
                    Debug.LogWarning("[DLSSExtension] DLSS initialization failed");
                    return;
                }

                // Query capabilities using DLSSManager
                if (DLSSManager.TryGetCapabilities(out var caps))
                {
                    m_SRSupported = caps.IsSRAvailable;
                    m_RRSupported = caps.IsRRAvailable;
                    m_NeedsDriverUpdate = caps.NeedsDriverUpdate;

                    Debug.Log($"[DLSSExtension] DLSS-SR Available: {m_SRSupported}");
                    Debug.Log($"[DLSSExtension] DLSS-RR Available: {m_RRSupported}");

                    if (m_NeedsDriverUpdate)
                    {
                        Debug.LogWarning($"[DLSSExtension] Driver update recommended. Min version: {caps.minDriverVersionMajor}.{caps.minDriverVersionMinor}");
                    }
                }

                Debug.Log("[DLSSExtension] DLSS initialized successfully!");
            }
            catch (DllNotFoundException e)
            {
                Debug.LogWarning($"[DLSSExtension] DLSS DLL not found: {e.Message}");
                Debug.LogWarning("[DLSSExtension] Make sure UnityPlugin.dll and nvngx_*.dll are in Assets/Plugins/x86_64/");
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
        /// Get optimal render resolution for DLSS
        /// </summary>
        public bool TryGetOptimalSettings(
            DLSSMode mode,
            DLSSQuality quality,
            int outputWidth,
            int outputHeight,
            out DLSSOptimalSettings settings)
        {
            settings = default;

            if (!m_Initialized)
                return false;

            return DLSSManager.TryGetOptimalSettings(
                mode,
                quality,
                (uint)outputWidth,
                (uint)outputHeight,
                out settings
            );
        }

        /// <summary>
        /// Get DLSS memory statistics
        /// </summary>
        public bool TryGetStats(DLSSMode mode, out DLSSStats stats)
        {
            stats = default;

            if (!m_Initialized)
                return false;

            return DLSSNative.DLSS_GetStats(mode, out stats) == DLSSResult.Success;
        }

        /// <summary>
        /// Shutdown DLSS (call on application quit)
        /// </summary>
        public void Shutdown()
        {
            if (m_Initialized)
            {
                DLSSManager.DestroyAllContexts();
                DLSSManager.Shutdown();
                m_Initialized = false;
                Debug.Log("[DLSSExtension] DLSS shutdown complete.");
            }
        }
#endif
    }
}
