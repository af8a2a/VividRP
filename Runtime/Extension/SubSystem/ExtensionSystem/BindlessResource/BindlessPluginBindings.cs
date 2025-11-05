using System;
using System.Runtime.InteropServices;

namespace UnityEngine.Rendering.Universal
{
    public class Bindless : IExtension
    {
        private const string DLLName =
#if (PLATFORM_IOS || PLATFORM_TVOS || PLATFORM_BRATWURST || PLATFORM_SWITCH) && !UNITY_EDITOR
            "__Internal";
#else
            "UnityBindless";
#endif

        [DllImport(DLLName)]
        public static extern uint GetSRVDescriptorHeapCount();

        [DllImport(DLLName)]
        public static extern bool CreateSRVDescriptor(IntPtr pTexture, uint index);
        
        
        [DllImport(DLLName)]
        public static extern bool CreateUAVDescriptor(IntPtr pTexture, uint index);


        [DllImport(DLLName)]
        public static extern bool CheckBindlessSupport();


        public void Init()
        {
        }

        public bool Support()
        {

            return CheckBindlessSupport();
        }

        public HardwareExtension GetExtension()
        {
            return HardwareExtension.Bindless;
        }
    }
}