using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VividRP.Runtime.Utility.PipelineResource
{
    public static class PipelineResourceManager
    {
        private static PipelineResourcesContainer s_Container;
        private static readonly Dictionary<Type, object> s_Cache = new();
        private static bool s_Initialized;

        public static void Initialize()
        {
            if (s_Initialized)
                return;

            s_Container = Resources.Load<PipelineResourcesContainer>("PipelineResources");
            if (s_Container == null)
                Debug.LogWarning("[VividRP] PipelineResourcesContainer not found at Resources/PipelineResources. Resource fields will be null.");

            s_Initialized = true;
        }

        public static T Get<T>() where T : class, new()
        {
            if (!s_Initialized)
                Initialize();

            var type = typeof(T);
            if (s_Cache.TryGetValue(type, out var cached))
                return (T)cached;

            var instance = BuildInstance<T>();
            s_Cache[type] = instance;
            return instance;
        }

        private static T BuildInstance<T>() where T : class, new()
        {
            var instance = new T();
            if (s_Container == null)
                return instance;

            var type = typeof(T);
            var typeName = type.FullName;
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<ResourcePathAttribute>();
                if (attr == null)
                    continue;

                foreach (var entry in s_Container.Entries)
                {
                    if (entry.TypeName == typeName && entry.FieldName == field.Name)
                    {
                        field.SetValue(instance, entry.Asset);
                        break;
                    }
                }
            }

            return instance;
        }

        public static void Cleanup()
        {
            s_Cache.Clear();
            s_Container = null;
            s_Initialized = false;
        }
    }
}
