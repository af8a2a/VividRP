using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven.Bindless
{
    public static class BindlessPluginBindings
    {
        private static bool s_IsLoaded;

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

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
#endif
        private static void WarmupAfterAssembliesLoaded()
        {
            EnsureLoaded();
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#else

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        private static void WarmupOnSubsystemRegistration()
        {
            EnsureLoaded();
        }

        
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
#endif
        private static void WarmupOnLoad()
        {
            EnsureLoaded();
        }

        public static void EnsureLoaded()
        {
            if (s_IsLoaded)
            {
                return;
            }

            try
            {
                WarmupPlugin();
                s_IsLoaded = true;
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                             || exception is EntryPointNotFoundException
                                             || exception is BadImageFormatException)
            {
            }
        }
    }
}
