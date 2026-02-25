using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VividRP.Runtime.Utility
{
    public static class VividResourceManager
    {
        private static readonly Dictionary<string, UnityEngine.Object> s_LoadedResources = new Dictionary<string, UnityEngine.Object>();
        private static bool s_Initialized;

        public static void Initialize()
        {
            if (s_Initialized)
                return;

            LoadAnnotatedResources(typeof(VividResourceManager).Assembly);
            s_Initialized = true;
        }

        public static T Get<T>(string key) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            Initialize();

            if (s_LoadedResources.TryGetValue(key, out var cached))
                return cached as T;

            var loaded = LoadResource(typeof(T), key);
            if (loaded != null)
                s_LoadedResources[key] = loaded;

            return loaded as T;
        }

        private static void LoadAnnotatedResources(Assembly assembly)
        {
            var types = GetTypesSafely(assembly);
            foreach (var type in types)
            {
                if (type == null)
                    continue;

                var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();
                    if (resourcePath == null)
                        continue;

                    try
                    {
                        LoadAndAssignField(field, resourcePath);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"[VividRP] Failed to load resource for field '{field.DeclaringType?.FullName}.{field.Name}': {exception.Message}");
                    }
                }
            }
        }

        private static void LoadAndAssignField(FieldInfo field, ResourcePathAttribute resourcePath)
        {
            if (!ValidateField(field, resourcePath))
                return;

            var loaded = GetOrLoadCached(field.FieldType, resourcePath.Path);
            if (loaded == null)
            {
                if (resourcePath.Required)
                {
                    Debug.LogError(
                        $"[VividRP] Failed to load required resource '{resourcePath.Path}' for field '{field.DeclaringType?.FullName}.{field.Name}'.");
                }
                else
                {
                    Debug.LogWarning(
                        $"[VividRP] Optional resource '{resourcePath.Path}' was not found for field '{field.DeclaringType?.FullName}.{field.Name}'.");
                }

                return;
            }

            field.SetValue(null, loaded);
        }

        private static UnityEngine.Object GetOrLoadCached(Type type, string key)
        {
            if (s_LoadedResources.TryGetValue(key, out var cached))
                return type.IsInstanceOfType(cached) ? cached : null;

            var loaded = LoadResource(type, key);
            if (loaded != null)
                s_LoadedResources[key] = loaded;

            return loaded;
        }

        private static UnityEngine.Object LoadResource(Type type, string path)
        {
            if (type == typeof(Shader))
                return Shader.Find(path);

            return Resources.Load(path, type);
        }

        private static bool ValidateField(FieldInfo field, ResourcePathAttribute resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath.Path))
            {
                Debug.LogError(
                    $"[VividRP] ResourcePath on field '{field.DeclaringType?.FullName}.{field.Name}' cannot be empty.");
                return false;
            }

            if (!field.IsStatic)
            {
                Debug.LogError(
                    $"[VividRP] ResourcePath can only be used on static fields. Invalid field '{field.DeclaringType?.FullName}.{field.Name}'.");
                return false;
            }

            if (field.IsInitOnly || field.IsLiteral)
            {
                Debug.LogError(
                    $"[VividRP] ResourcePath field '{field.DeclaringType?.FullName}.{field.Name}' must be writable.");
                return false;
            }

            if (!typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
            {
                Debug.LogError(
                    $"[VividRP] ResourcePath field '{field.DeclaringType?.FullName}.{field.Name}' must derive from UnityEngine.Object.");
                return false;
            }

            return true;
        }

        private static Type[] GetTypesSafely(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types ?? Array.Empty<Type>();
            }
        }
    }
}
