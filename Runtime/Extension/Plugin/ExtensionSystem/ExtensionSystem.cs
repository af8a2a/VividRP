using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace UnityEngine.Rendering.Universal
{
    public enum HardwareExtension
    {
        PlaceHolder,
        ShaderExecutionReordering,
    }


    public static class ExtensionSystem
    {
        private static Dictionary<HardwareExtension,IExtension> extensions =new Dictionary<HardwareExtension, IExtension>();

        private static HashSet<HardwareExtension> supportedExtension = new HashSet<HardwareExtension>();

        public static void Init()
        {
            var shaderExecutionReordering = new ShaderExecutionReordering();

            extensions.Add(HardwareExtension.ShaderExecutionReordering, shaderExecutionReordering);


            foreach (var (_, extension) in extensions)
            {
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