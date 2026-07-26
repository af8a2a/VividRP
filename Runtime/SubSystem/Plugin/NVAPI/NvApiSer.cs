using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.Plugin
{
    /// <summary>
    /// Managed entry point for the vendored Unity_NVAPI native plugin.
    /// </summary>
    public static class NvApiSer
    {
        private const string DllName = "Unity_NVAPI";

        /// <summary>
        /// Configures NVAPI Shader Execution Reordering for the active D3D12 device.
        /// </summary>
        /// <remarks>
        /// Call this before the first ray-tracing dispatch that uses the matching
        /// <c>NV_SHADER_EXTN_SLOT</c>.
        /// </remarks>
        public static bool TryInitializeShaderExecutionReordering(
            uint shaderExtensionUavSlot,
            out string failureReason)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (IntPtr.Size != 8)
            {
                failureReason = "Unity_NVAPI is only built for Windows x86_64.";
                return false;
            }

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                failureReason = "Shader Execution Reordering requires Direct3D 12.";
                return false;
            }

            try
            {
                if (!NvAPI_IsShaderExecutionReorderingAPISupported())
                {
                    failureReason =
                        "The installed NVIDIA driver does not expose the Shader Execution Reordering API.";
                    return false;
                }

                if (!NvAPI_IsShaderExecutionReorderingSupportedByGPU())
                {
                    failureReason =
                        "The active NVIDIA GPU does not support Shader Execution Reordering.";
                    return false;
                }

                if (!NvAPI_SetNvShaderExtnSlot(shaderExtensionUavSlot))
                {
                    failureReason =
                        $"NVAPI could not reserve UAV slot u{shaderExtensionUavSlot} for shader extensions.";
                    return false;
                }

                failureReason = null;
                return true;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException
                || exception is EntryPointNotFoundException
                || exception is BadImageFormatException)
            {
                failureReason =
                    $"Unity_NVAPI could not be loaded ({exception.GetType().Name}).";
                return false;
            }
#else
            failureReason =
                "Shader Execution Reordering is only available in Windows Editor and Windows Standalone builds.";
            return false;
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [return: MarshalAs(UnmanagedType.I1)]
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool
            NvAPI_IsShaderExecutionReorderingAPISupported();

        [return: MarshalAs(UnmanagedType.I1)]
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool
            NvAPI_IsShaderExecutionReorderingSupportedByGPU();

        [return: MarshalAs(UnmanagedType.I1)]
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool NvAPI_SetNvShaderExtnSlot(uint uavSlot);
#endif
    }
}
