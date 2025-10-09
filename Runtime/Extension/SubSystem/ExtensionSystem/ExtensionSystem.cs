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
        Bindless
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
            extensions.Clear();
            supportedExtension.Clear();
        }
    }
}