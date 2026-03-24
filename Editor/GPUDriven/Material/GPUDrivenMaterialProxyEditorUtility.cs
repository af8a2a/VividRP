using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    internal readonly struct GPUDrivenMaterialProxyBindingResult
    {
        public GPUDrivenMaterialProxyBindingResult(bool success, string errorMessage, string[] createdAssetPaths, string[] warnings)
        {
            Success = success;
            ErrorMessage = errorMessage;
            CreatedAssetPaths = createdAssetPaths ?? Array.Empty<string>();
            Warnings = warnings ?? Array.Empty<string>();
        }

        public bool Success { get; }

        public string ErrorMessage { get; }

        public string[] CreatedAssetPaths { get; }

        public string[] Warnings { get; }
    }

    internal static class GPUDrivenMaterialProxyEditorUtility
    {
        public static GPUDrivenMaterialProxyBindingResult CreateOrBindMaterialProxies(MeshletRenderer meshletRenderer)
        {
            if (meshletRenderer == null)
            {
                return new GPUDrivenMaterialProxyBindingResult(false, "MeshletRenderer is null.", null, null);
            }

            RefreshSource(meshletRenderer, "Refresh Meshlet Renderer Source");

            Mesh sourceMesh = meshletRenderer.sourceMesh;
            if (sourceMesh == null)
            {
                return new GPUDrivenMaterialProxyBindingResult(
                    false,
                    "MeshletRenderer source Mesh is not resolved.",
                    null,
                    null
                );
            }

            int expectedCount = meshletRenderer.subMeshCount;
            var materialProxies = new GPUDrivenMaterialProxy[expectedCount];
            var createdAssetPaths = new List<string>();
            var warnings = new List<string>();
            for (int subMeshIndex = 0; subMeshIndex < expectedCount; subMeshIndex++)
            {
                materialProxies[subMeshIndex] = meshletRenderer.GetMaterialProxy(subMeshIndex);
            }

            for (int subMeshIndex = 0; subMeshIndex < expectedCount; subMeshIndex++)
            {
                Material sourceMaterial = meshletRenderer.GetSourceMaterial(subMeshIndex);
                if (sourceMaterial == null)
                {
                    warnings.Add($"Submesh {subMeshIndex} has no source Material, so no GPUDriven proxy was created.");
                    continue;
                }

                string proxyAssetPath = ResolveProxyAssetPath(sourceMaterial, sourceMesh, subMeshIndex);
                if (string.IsNullOrEmpty(proxyAssetPath))
                {
                    warnings.Add(
                        $"Could not determine asset path for GPUDriven material proxy on submesh {subMeshIndex}. Make the source Material or Mesh persistent first."
                    );
                    continue;
                }

                GPUDrivenMaterialProxy materialProxy = LoadOrCreateProxyAsset(
                    proxyAssetPath,
                    sourceMaterial,
                    createdAssetPaths,
                    out bool wasCreated
                );
                if (materialProxy == null)
                {
                    warnings.Add($"Failed to create GPUDriven material proxy asset at '{proxyAssetPath}'.");
                    continue;
                }

                if (materialProxy.SourceMaterial == null)
                {
                    materialProxy.SourceMaterial = sourceMaterial;
                    EditorUtility.SetDirty(materialProxy);
                    AssetDatabase.SaveAssetIfDirty(materialProxy);
                }

                materialProxies[subMeshIndex] = materialProxy;

                if (wasCreated)
                {
                    GPUDrivenMaterialProxySyncResult syncResult = GPUDrivenMaterialProxySyncUtility.SyncFromSourceMaterial(materialProxy, sourceMaterial);
                    if (!syncResult.Success && !string.IsNullOrEmpty(syncResult.ErrorMessage))
                    {
                        warnings.Add(syncResult.ErrorMessage);
                    }

                    warnings.AddRange(syncResult.Warnings);
                }
            }

            Undo.RecordObject(meshletRenderer, "Bind GPUDriven Material Proxies");
            bool changed = meshletRenderer.SetMaterialProxies(materialProxies);
            if (changed)
            {
                EditorUtility.SetDirty(meshletRenderer);
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);
            }

            return new GPUDrivenMaterialProxyBindingResult(true, string.Empty, createdAssetPaths.ToArray(), warnings.ToArray());
        }

        public static GPUDrivenMaterialProxySyncResult SyncMaterialProxiesFromSourceMaterials(MeshletRenderer meshletRenderer)
        {
            if (meshletRenderer == null)
            {
                return new GPUDrivenMaterialProxySyncResult(false, false, "MeshletRenderer is null.", null);
            }

            RefreshSource(meshletRenderer, "Refresh Meshlet Renderer Source");

            if (meshletRenderer.sourceMesh == null)
            {
                return new GPUDrivenMaterialProxySyncResult(false, false, "MeshletRenderer source Mesh is not resolved.", null);
            }

            var warnings = new List<string>();
            bool changed = false;

            for (int subMeshIndex = 0; subMeshIndex < meshletRenderer.subMeshCount; subMeshIndex++)
            {
                GPUDrivenMaterialProxy materialProxy = meshletRenderer.GetMaterialProxy(subMeshIndex);
                if (materialProxy == null)
                {
                    warnings.Add($"Submesh {subMeshIndex} is missing a GPUDriven material proxy.");
                    continue;
                }

                Material sourceMaterial = meshletRenderer.GetSourceMaterial(subMeshIndex);
                if (sourceMaterial == null)
                {
                    warnings.Add($"Submesh {subMeshIndex} has no source Material to synchronize from.");
                    continue;
                }

                GPUDrivenMaterialProxySyncResult syncResult = GPUDrivenMaterialProxySyncUtility.SyncFromSourceMaterial(materialProxy, sourceMaterial);
                if (!syncResult.Success)
                {
                    return new GPUDrivenMaterialProxySyncResult(false, changed, syncResult.ErrorMessage, warnings.ToArray());
                }

                changed |= syncResult.Changed;
                warnings.AddRange(syncResult.Warnings);
            }

            if (changed)
            {
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);
            }

            return new GPUDrivenMaterialProxySyncResult(true, changed, string.Empty, warnings.ToArray());
        }

        private static void RefreshSource(MeshletRenderer meshletRenderer, string undoLabel)
        {
            if (meshletRenderer == null)
            {
                return;
            }

            Undo.RecordObject(meshletRenderer, undoLabel);
            if (meshletRenderer.RefreshSource())
            {
                EditorUtility.SetDirty(meshletRenderer);
            }
        }

        internal static string ResolveProxyAssetPath(Material sourceMaterial, Mesh sourceMesh, int subMeshIndex)
        {
            string materialPath = sourceMaterial != null ? AssetDatabase.GetAssetPath(sourceMaterial) : string.Empty;
            if (sourceMaterial != null
                && !string.IsNullOrEmpty(materialPath)
                && string.Equals(Path.GetExtension(materialPath), ".mat", StringComparison.OrdinalIgnoreCase))
            {
                string directory = Path.GetDirectoryName(materialPath)?.Replace('\\', '/') ?? "Assets";
                return $"{directory}/{sourceMaterial.name}_GPUDriven.asset";
            }

            string meshPath = sourceMesh != null ? AssetDatabase.GetAssetPath(sourceMesh) : string.Empty;
            if (!string.IsNullOrEmpty(meshPath))
            {
                string directory = Path.GetDirectoryName(meshPath)?.Replace('\\', '/') ?? "Assets";
                return $"{directory}/{sourceMesh.name}_SubMesh{subMeshIndex}_GPUDriven.asset";
            }

            return string.Empty;
        }

        private static GPUDrivenMaterialProxy LoadOrCreateProxyAsset(
            string proxyAssetPath,
            Material sourceMaterial,
            ICollection<string> createdAssetPaths,
            out bool wasCreated
        )
        {
            GPUDrivenMaterialProxy existingProxy = AssetDatabase.LoadAssetAtPath<GPUDrivenMaterialProxy>(proxyAssetPath);
            if (existingProxy != null)
            {
                wasCreated = false;
                return existingProxy;
            }

            string resolvedPath = proxyAssetPath;
            if (AssetDatabase.LoadMainAssetAtPath(proxyAssetPath) != null)
            {
                resolvedPath = AssetDatabase.GenerateUniqueAssetPath(proxyAssetPath);
            }

            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            materialProxy.name = Path.GetFileNameWithoutExtension(resolvedPath);
            materialProxy.SourceMaterial = sourceMaterial;
            AssetDatabase.CreateAsset(materialProxy, resolvedPath);
            createdAssetPaths?.Add(resolvedPath);
            wasCreated = true;
            return materialProxy;
        }

        private static Material GetMaterialForSubMesh(Material[] sharedMaterials, int subMeshIndex)
        {
            if (sharedMaterials == null || sharedMaterials.Length == 0)
            {
                return null;
            }

            int materialIndex = Mathf.Clamp(subMeshIndex, 0, sharedMaterials.Length - 1);
            return sharedMaterials[materialIndex];
        }
    }
}
