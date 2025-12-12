using System;
using System.Runtime.InteropServices;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Use to init NRD dispatch parameter
    /// is very dirty to configure the NRD constBuffer...
    /// </summary>
    public class NRDInitlizer : IExtension
    {
        private const string DLLName =
#if (PLATFORM_IOS || PLATFORM_TVOS || PLATFORM_BRATWURST || PLATFORM_SWITCH) && !UNITY_EDITOR
            "__Internal";
#else
            "NRDUnityPlugin";
#endif


        [DllImport(DLLName)]
        public static extern int NRD_Test();

        [DllImport(DLLName)]
        public static extern IntPtr NRD_GetContext();

        [DllImport(DLLName)]
        public static extern void NRD_ReleaseContext(IntPtr ctx);


        [DllImport(DLLName)]
        public static extern NRDResult NRD_SetCommonSettings(
            IntPtr nrdContext,
            ref NRDCommonSettings commonSettings
        );

        [DllImport(DLLName)]
        public static extern bool NRD_SetupSigmaConstBuffer(
            IntPtr nrdContext,
            ref NRDCommonSettings commonSettings,
            ref SigmaSettings sigmaSettings,
            //out constbuffer
            ref SigmaSharedConstants data
        );

        
        [DllImport(DLLName)]
        public static extern bool NRD_SetupReblurConstBuffer(
            IntPtr nrdContext,
            ref NRDCommonSettings commonSettings,
            ref ReblurSettings sigmaSettings,
            //out constbuffer
            ref ReblurSharedConstants data
        );


        public void Init()
        {
        }

        public bool Support()
        {
            Debug.Log($"NRDInitlizer support:{NRD_Test() > 0}");
            return true;
        }

        public HardwareExtension GetExtension()
        {
            return HardwareExtension.NvidiaRealtimeDenoiser;
        }
    }
}