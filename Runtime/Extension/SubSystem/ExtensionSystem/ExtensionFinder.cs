using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine.Rendering.Universal
{
    public class ExtensionFinder
    {
        public static List<IExtension> GetAllExtensionInstances()
        {
            var interfaceType = typeof(IExtension);

            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a =>
                {
                    try
                    {
                        return a.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        return ex.Types.Where(t => t != null);
                    }
                })
                .Where(t => t != null &&
                            !t.IsAbstract &&
                            interfaceType.IsAssignableFrom(t) &&
                            t.GetConstructor(Type.EmptyTypes) != null) // 确保有无参构造函数
                .Select(t => (IExtension)Activator.CreateInstance(t))
                .ToList();
        }
    }
}