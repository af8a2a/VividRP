using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VividRP.Runtime
{
    public static class PipelineResourceManager
    {
        private static PipelineResourcesContainer s_Container;
        private static readonly Dictionary<Type, object> s_Cache = new();
        private static readonly Dictionary<string, UnityEngine.Object> s_ResourceLookup = new(StringComparer.Ordinal);
        private static bool s_Initialized;

        #if UNITY_EDITOR
        [InitializeOnLoadMethod]
        #else
        [RuntimeInitializeOnLoadMethod]
        #endif
        public static void Initialize()
        {
            if (s_Initialized)
                return;

            s_Container = Resources.Load<PipelineResourcesContainer>("PipelineResources");
            if (s_Container == null)
            {
                Debug.LogWarning("[VividRP] PipelineResourcesContainer not found at Resources/PipelineResources. Resource fields will be null.");
            }
            else
            {
                BuildResourceLookup();
            }

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
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<ResourcePathAttribute>();
                if (attr == null)
                    continue;

                if (!s_ResourceLookup.TryGetValue(attr.Path, out var resourceObject) || resourceObject == null)
                    continue;

                if (!field.FieldType.IsInstanceOfType(resourceObject))
                {
                    Debug.LogWarning(
                        $"[VividRP] Resource '{attr.Path}' is assigned to {resourceObject.GetType().Name}, " +
                        $"but field {type.Name}.{field.Name} expects {field.FieldType.Name}.");
                    continue;
                }

                field.SetValue(instance, resourceObject);
            }

            return instance;
        }

        public static void Cleanup()
        {
            s_Cache.Clear();
            s_ResourceLookup.Clear();
            s_Container = null;
            s_Initialized = false;
        }

        private static void BuildResourceLookup()
        {
            s_ResourceLookup.Clear();

            if (s_Container?.Entries == null)
                return;

            foreach (var entry in s_Container.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.ResourceName))
                    continue;

                s_ResourceLookup[entry.ResourceName] = entry.ResourceObject;
            }
        }
    }
}
