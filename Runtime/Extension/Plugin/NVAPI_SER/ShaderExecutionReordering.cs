using System.Runtime.InteropServices;

namespace UnityEngine.Rendering.Universal
{
    public class ShaderExecutionReordering : IExtension
    {
        
        [DllImport("NVAPIPlugin")]
        public static extern bool NvAPI_IsShaderExecutionReorderingAPISupported();

        [DllImport("NVAPIPlugin")]
        public static extern bool NvAPI_IsShaderExecutionReorderingSupportedByGPU();

        [DllImport("NVAPIPlugin")]
        public static extern bool NvAPI_SetNvShaderExtnSlot(uint uavSlot);

        private bool useHWSER = false;

        public void Init()
        {
            
            Debug.Log("Graphics Device Name: " + SystemInfo.graphicsDeviceName);
            Debug.Log("Graphics Device Vendor: " + SystemInfo.graphicsDeviceVendor);
            // 显卡版本（驱动支持的 API）
            Debug.Log("Graphics Device Version: " + SystemInfo.graphicsDeviceVersion);

            if (NvAPI_IsShaderExecutionReorderingAPISupported())
                Debug.Log("Shader Execution Reordering (SER) NV API is supported!");
            else
                Debug.Log(
                    "Shader Execution Reordering (SER) NVAPI is NOT supported! The SER NVAPI is supported on all raytracing-capable NVIDIA GPUs starting with R520 drivers.");

            if (NvAPI_IsShaderExecutionReorderingSupportedByGPU())
                Debug.Log("Shader Execution Reordering (SER) is supported by the GPU!");
            else
                Debug.Log($"Shader Execution Reordering (SER) is NOT supported by the GPU {SystemInfo.graphicsDeviceName}! Thread reordering (NvReorderThread) in HLSL will be ignored.");

            useHWSER = NvAPI_IsShaderExecutionReorderingAPISupported() && NvAPI_IsShaderExecutionReorderingSupportedByGPU();
        }

        public bool Support()
        {
            return useHWSER;
        }

        public HardwareExtension GetExtension()
        {
            return HardwareExtension.ShaderExecutionReordering;
        }
    }
}