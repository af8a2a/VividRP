using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    public class PipelineResourceUpdater : AssetPostprocessor
    {
        private const string ContainerRelativePath = "Runtime/Resources/PipelineResources.asset";

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            // Delay to avoid asset database issues during domain reload
            EditorApplication.delayCall += UpdateDefaultContainerResources;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool relevant = false;
            foreach (var path in importedAssets)
            {
                if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".compute", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".exr", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                {
                    relevant = true;
                    break;
                }
            }

            if (relevant)
                UpdateDefaultContainerResources();
        }

        internal static int UpdateContainerResources(
            PipelineResourcesContainer container,
            bool recordUndo = false,
            bool logSummary = false)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            var entries = CollectEntries();

            if (recordUndo)
                Undo.RecordObject(container, "Recollect Pipeline Resources");

            container.Entries.Clear();
            container.Entries.AddRange(entries);
            EditorUtility.SetDirty(container);

            var assetPath = AssetDatabase.GetAssetPath(container);
            if (!string.IsNullOrEmpty(assetPath))
                AssetDatabase.SaveAssetIfDirty(container);

            if (logSummary)
            {
                if (string.IsNullOrEmpty(assetPath))
                    assetPath = container.name;

                Debug.Log($"[VividRP] Recollected {entries.Count} pipeline resources into {assetPath}.");
            }

            return entries.Count;
        }

        private static void UpdateDefaultContainerResources()
        {
            var containerPath = VividPackagePathUtility.GetPreferredAssetPath(ContainerRelativePath);
            Debug.Log(containerPath);
            var container = AssetDatabase.LoadAssetAtPath<PipelineResourcesContainer>(containerPath);
            if (container == null)
            {
                container = ScriptableObject.CreateInstance<PipelineResourcesContainer>();
                AssetDatabase.CreateAsset(container, containerPath);
            }

            UpdateContainerResources(container);
        }

        private static List<ResourceEntry> CollectEntries()
        {
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
                            ResourceName = attr.Path,
                            ResourceObject = asset,
                        });

                        if (asset == null)
                            Debug.LogWarning($"[VividRP] Could not resolve resource: {attr.Path} for {type.Name}.{field.Name}");
                    }
                }
            }

            entries.Sort((left, right) => string.CompareOrdinal(left?.ResourceName, right?.ResourceName));
            return entries;
        }

        private static UnityEngine.Object ResolveAsset(string relativePath, Type fieldType)
        {
            // Try with type-appropriate extensions first, then without extension
            var extensions = GetExtensionsForType(fieldType);

            foreach (var ext in extensions)
            {
                var asset = LoadFirstMatchingAsset(relativePath + ext, fieldType);
                if (asset != null)
                    return asset;
            }

            // Try the path as-is (already has extension)
            return LoadFirstMatchingAsset(relativePath, fieldType);
        }

        private static UnityEngine.Object LoadFirstMatchingAsset(string relativePath, Type fieldType)
        {
            var candidateAssetPaths = VividPackagePathUtility.GetCandidateAssetPaths(relativePath);
            for (var i = 0; i < candidateAssetPaths.Length; i++)
            {
                var asset = AssetDatabase.LoadAssetAtPath(candidateAssetPaths[i], fieldType);
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
