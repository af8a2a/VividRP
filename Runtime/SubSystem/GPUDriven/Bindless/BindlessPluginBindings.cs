using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven.Bindless
{
    public static class BindlessPluginBindings
    {
        private static bool s_HasAttemptedLoad;

        private const string DLLName =
#if (PLATFORM_IOS || PLATFORM_TVOS || PLATFORM_BRATWURST || PLATFORM_SWITCH) && !UNITY_EDITOR
            "__Internal";
#else
            "UnityBindless";
#endif

        [DllImport(DLLName)]
        private static extern uint WarmupPlugin();

        [DllImport(DLLName)]
        public static extern uint GetSRVDescriptorHeapCount();

        [DllImport(DLLName)]
        public static extern uint GetBindlessDescriptorStartIndex();

        [DllImport(DLLName)]
        public static extern uint GetBindlessDescriptorCount();

        [return: MarshalAs(UnmanagedType.I1)]
        [DllImport(DLLName)]
        public static extern bool CreateSRVDescriptor(IntPtr pTexture, uint index);

        [DllImport(DLLName)]
        public static extern uint IsPixLoaded();

        [DllImport(DLLName)]
        public static extern uint BeginPixCapture([MarshalAs(UnmanagedType.LPWStr)] string filename);

        [DllImport(DLLName)]
        public static extern uint EndPixCapture();

        [DllImport(DLLName)]
        public static extern void OpenPixCapture([MarshalAs(UnmanagedType.LPWStr)] string filename);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void WarmupOnLoad()
        {
            EnsureLoaded();
        }

        public static void EnsureLoaded()
        {
            if (s_HasAttemptedLoad)
            {
                return;
            }

            s_HasAttemptedLoad = true;

            try
            {
                WarmupPlugin();
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                             || exception is EntryPointNotFoundException
                                             || exception is BadImageFormatException)
            {
            }
        }
    }
}
