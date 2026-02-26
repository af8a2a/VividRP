using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.Utility.PipelineResource;

namespace VividRP.Editor.PipelineResource
{
    public class PipelineResourceUpdater : AssetPostprocessor
    {
        private const string ContainerPath = "Packages/com.af8a2a.vividrp/Runtime/Resources/PipelineResources.asset";
        private const string PackageRuntimeRoot = "Packages/com.af8a2a.vividrp/Runtime/";

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            // Delay to avoid asset database issues during domain reload
            EditorApplication.delayCall += UpdateAllResources;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool relevant = false;
            foreach (var path in importedAssets)
            {
                if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".compute", StringComparison.OrdinalIgnoreCase))
                {
                    relevant = true;
                    break;
                }
            }

            if (relevant)
                UpdateAllResources();
        }

        private static void UpdateAllResources()
        {
            var container = AssetDatabase.LoadAssetAtPath<PipelineResourcesContainer>(ContainerPath);
            if (container == null)
            {
                container = ScriptableObject.CreateInstance<PipelineResourcesContainer>();
                AssetDatabase.CreateAsset(container, ContainerPath);
            }

            var entries = new List<ResourceEntry>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }

                if (types == null) continue;

                foreach (var type in types)
                {
                    if (type == null) continue;
                    if (type.GetCustomAttribute<PipelineResourceAttribute>() == null) continue;

                    var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var field in fields)
                    {
                        var attr = field.GetCustomAttribute<ResourcePathAttribute>();
                        if (attr == null) continue;

                        var asset = ResolveAsset(attr.Path, field.FieldType);
                        entries.Add(new ResourceEntry
                        {
                            TypeName = type.FullName,
                            FieldName = field.Name,
                            Asset = asset
                        });

                        if (asset == null)
                            Debug.LogWarning($"[VividRP] Could not resolve resource: {attr.Path} for {type.Name}.{field.Name}");
                    }
                }
            }

            container.Entries.Clear();
            container.Entries.AddRange(entries);
            EditorUtility.SetDirty(container);
            AssetDatabase.SaveAssetIfDirty(container);
        }

        private static UnityEngine.Object ResolveAsset(string relativePath, Type fieldType)
        {
            // Try with type-appropriate extensions first, then without extension
            var extensions = GetExtensionsForType(fieldType);

            foreach (var ext in extensions)
            {
                var fullPath = PackageRuntimeRoot + relativePath + ext;
                var asset = AssetDatabase.LoadAssetAtPath(fullPath, fieldType);
                if (asset != null)
                    return asset;
            }

            // Try the path as-is (already has extension)
            {
                var fullPath = PackageRuntimeRoot + relativePath;
                var asset = AssetDatabase.LoadAssetAtPath(fullPath, fieldType);
                if (asset != null)
                    return asset;
            }

            return null;
        }

        private static string[] GetExtensionsForType(Type fieldType)
        {
            if (fieldType == typeof(Shader))
                return new[] { ".shader", ".shadergraph" };
            if (fieldType == typeof(ComputeShader))
                return new[] { ".compute" };
            if (fieldType == typeof(Texture2D))
                return new[] { ".png", ".tga", ".exr", ".asset" };
            if (fieldType == typeof(Material))
                return new[] { ".mat" };

            return new[] { ".asset" };
        }
    }
}
