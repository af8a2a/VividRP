using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace UnityEngine.Rendering.Universal
{
    public enum HardwareExtension
    {
        PlaceHolder,
        ShaderExecutionReordering,
        Bindless,
        NvidiaRealtimeDenoiser,
        DLSS  // NVIDIA DLSS (Deep Learning Super Sampling) - SR and RR
    }


    public static class ExtensionSystem
    {
        private static Dictionary<HardwareExtension,IExtension> extensions =new Dictionary<HardwareExtension, IExtension>();

        private static HashSet<HardwareExtension> supportedExtension = new HashSet<HardwareExtension>();

        public static void Init()
        {
            var extensionPlugins = ExtensionFinder.GetAllExtensionInstances();




            foreach (var extension in extensionPlugins)
            {
                extensions.Add(extension.GetExtension(), extension);

                extension.Init();
                if (extension.Support())
                {
                    supportedExtension.Add(extension.GetExtension());
                }
            }
            
        }

        public static IReadOnlyDictionary<HardwareExtension, IExtension> RegisteredExtensions => extensions;

        public static IReadOnlyCollection<HardwareExtension> SupportedExtension => supportedExtension;


        public static void Clean()
        {
            foreach (var extension in extensions)
            {
                extension.Value.ShutDown();
            }
            extensions.Clear();
            supportedExtension.Clear();
        }
    }
}