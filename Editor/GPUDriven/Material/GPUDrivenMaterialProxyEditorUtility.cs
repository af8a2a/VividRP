using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;

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
        internal static GPUDrivenMaterialProxyTextureMode ResolveActiveTextureMode()
        {
            return VividGPUDrivenSystem.ResolveConfiguredTextureBackendMode(
                       VividRenderPipelineAsset.GetActiveAsset())
                   == GPUDrivenTextureBackendMode.VirtualTexture
                ? GPUDrivenMaterialProxyTextureMode.VirtualTexture
                : GPUDrivenMaterialProxyTextureMode.Bindless;
        }

        public static GPUDrivenMaterialProxyBindingResult CreateOrBindMaterialProxy(
            MeshletRenderer meshletRenderer,
            int subMeshIndex
        )
        {
            if (meshletRenderer == null)
            {
                return new GPUDrivenMaterialProxyBindingResult(false, "MeshletRenderer is null.", null, null);
            }

            RefreshSource(meshletRenderer, "Normalize Meshlet Renderer Source");

            Mesh sourceMesh = meshletRenderer.sourceMesh;
            if (sourceMesh == null)
            {
                return new GPUDrivenMaterialProxyBindingResult(
                    false,
                    "MeshletRenderer source Mesh is not captured. Run the takeover flow first.",
                    null,
                    null
                );
            }

            if (subMeshIndex < 0 || subMeshIndex >= meshletRenderer.subMeshCount)
            {
                return new GPUDrivenMaterialProxyBindingResult(
                    false,
                    $"Submesh index {subMeshIndex} is out of range.",
                    null,
                    null
                );
            }

            Material sourceMaterial = meshletRenderer.GetSourceMaterial(subMeshIndex);
            if (sourceMaterial == null)
            {
                return new GPUDrivenMaterialProxyBindingResult(
                    false,
                    $"Submesh {subMeshIndex} has no source Material to bind.",
                    null,
                    null
                );
            }

            string proxyAssetPath = ResolveProxyAssetPath(sourceMaterial, sourceMesh, subMeshIndex);
            if (string.IsNullOrEmpty(proxyAssetPath))
            {
                return new GPUDrivenMaterialProxyBindingResult(
                    false,
                    $"Could not determine asset path for GPUDriven material proxy on submesh {subMeshIndex}.",
                    null,
                    null
                );
            }

            var createdAssetPaths = new List<string>();
            var warnings = new List<string>();
            GPUDrivenMaterialProxy materialProxy = LoadOrCreateProxyAsset(
                proxyAssetPath,
                sourceMaterial,
                createdAssetPaths,
                out bool wasCreated
            );
            if (materialProxy == null)
            {
                return new GPUDrivenMaterialProxyBindingResult(
                    false,
                    $"Failed to create GPUDriven material proxy asset at '{proxyAssetPath}'.",
                    createdAssetPaths.ToArray(),
                    warnings.ToArray()
                );
            }

            if (materialProxy.SourceMaterial != sourceMaterial)
            {
                Undo.RecordObject(materialProxy, "Bind GPUDriven Material Proxy");
                materialProxy.SourceMaterial = sourceMaterial;
                EditorUtility.SetDirty(materialProxy);
                AssetDatabase.SaveAssetIfDirty(materialProxy);
            }

            var materialProxies = new GPUDrivenMaterialProxy[meshletRenderer.subMeshCount];
            for (int index = 0; index < materialProxies.Length; index++)
            {
                materialProxies[index] = meshletRenderer.GetMaterialProxy(index);
            }

            materialProxies[subMeshIndex] = materialProxy;

            Undo.RecordObject(meshletRenderer, "Bind GPUDriven Material Proxy");
            bool changed = meshletRenderer.SetMaterialProxies(materialProxies);
            if (changed)
            {
                EditorUtility.SetDirty(meshletRenderer);
            }

            GPUDrivenMaterialProxyTextureMode textureMode = ResolveActiveTextureMode();
            GPUDrivenMaterialProxySyncResult syncResult = materialProxy.SyncFromSourceMaterial(
                sourceMaterial,
                textureMode);
            if (!syncResult.Success && !string.IsNullOrEmpty(syncResult.ErrorMessage))
            {
                warnings.Add(syncResult.ErrorMessage);
            }

            warnings.AddRange(syncResult.Warnings);

            if (syncResult.Success && textureMode == GPUDrivenMaterialProxyTextureMode.VirtualTexture)
            {
                if (!BuildOrRefreshStreamedVirtualTexture(materialProxy, out string streamedAssetPath, out bool streamedAssetCreated, out string streamError))
                {
                    warnings.Add(streamError);
                }
                else if (streamedAssetCreated && !string.IsNullOrEmpty(streamedAssetPath))
                {
                    createdAssetPaths.Add(streamedAssetPath);
                }
            }

            if (changed || wasCreated || syncResult.Changed)
            {
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);
            }

            return new GPUDrivenMaterialProxyBindingResult(true, string.Empty, createdAssetPaths.ToArray(), warnings.ToArray());
        }

        public static GPUDrivenMaterialProxyBindingResult CreateOrBindMaterialProxies(
            MeshletRenderer meshletRenderer,
            bool skipStreamedVirtualTextureRebuildIfUpToDate = false
        )
        {
            if (meshletRenderer == null)
            {
                return new GPUDrivenMaterialProxyBindingResult(false, "MeshletRenderer is null.", null, null);
            }

            RefreshSource(meshletRenderer, "Normalize Meshlet Renderer Source");

            Mesh sourceMesh = meshletRenderer.sourceMesh;
            if (sourceMesh == null)
            {
                return new GPUDrivenMaterialProxyBindingResult(
                    false,
                    "MeshletRenderer source Mesh is not captured. Run the takeover flow first.",
                    null,
                    null
                );
            }

            int expectedCount = meshletRenderer.subMeshCount;
            GPUDrivenMaterialProxyTextureMode textureMode = ResolveActiveTextureMode();
            var materialProxies = new GPUDrivenMaterialProxy[expectedCount];
            var createdAssetPaths = new List<string>();
            var warnings = new List<string>();
            bool proxyDataChanged = false;
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

                materialProxies[subMeshIndex] = materialProxy;

                if (wasCreated
                    || materialProxy.SourceMaterial != sourceMaterial
                    || materialProxy.TextureMode != textureMode)
                {
                    GPUDrivenMaterialProxySyncResult syncResult = materialProxy.SyncFromSourceMaterial(
                        sourceMaterial,
                        textureMode);
                    if (!syncResult.Success && !string.IsNullOrEmpty(syncResult.ErrorMessage))
                    {
                        warnings.Add(syncResult.ErrorMessage);
                    }

                    warnings.AddRange(syncResult.Warnings);
                    proxyDataChanged |= syncResult.Changed;
                }

                if (textureMode == GPUDrivenMaterialProxyTextureMode.VirtualTexture)
                {
                    if (!BuildOrRefreshStreamedVirtualTexture(
                            materialProxy,
                            out string streamedAssetPath,
                            out bool streamedAssetCreated,
                            out string streamError,
                            skipStreamedVirtualTextureRebuildIfUpToDate))
                    {
                        warnings.Add(streamError);
                    }
                    else if (streamedAssetCreated && !string.IsNullOrEmpty(streamedAssetPath))
                    {
                        createdAssetPaths.Add(streamedAssetPath);
                    }
                }
            }

            Undo.RecordObject(meshletRenderer, "Bind GPUDriven Material Proxies");
            bool changed = meshletRenderer.SetMaterialProxies(materialProxies);
            if (changed || proxyDataChanged)
            {
                EditorUtility.SetDirty(meshletRenderer);
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);
            }

            return new GPUDrivenMaterialProxyBindingResult(true, string.Empty, createdAssetPaths.ToArray(), warnings.ToArray());
        }

        public static GPUDrivenMaterialProxySyncResult SyncMaterialProxyFromSourceMaterial(
            MeshletRenderer meshletRenderer,
            int subMeshIndex
        )
        {
            if (meshletRenderer == null)
            {
                return new GPUDrivenMaterialProxySyncResult(false, false, "MeshletRenderer is null.", null);
            }

            RefreshSource(meshletRenderer, "Normalize Meshlet Renderer Source");

            if (meshletRenderer.sourceMesh == null)
            {
                return new GPUDrivenMaterialProxySyncResult(
                    false,
                    false,
                    "MeshletRenderer source Mesh is not captured. Run the takeover flow first.",
                    null
                );
            }

            if (subMeshIndex < 0 || subMeshIndex >= meshletRenderer.subMeshCount)
            {
                return new GPUDrivenMaterialProxySyncResult(
                    false,
                    false,
                    $"Submesh index {subMeshIndex} is out of range.",
                    null
                );
            }

            GPUDrivenMaterialProxy materialProxy = meshletRenderer.GetMaterialProxy(subMeshIndex);
            if (materialProxy == null)
            {
                return new GPUDrivenMaterialProxySyncResult(
                    false,
                    false,
                    $"Submesh {subMeshIndex} is missing a GPUDriven material proxy.",
                    null
                );
            }

            Material sourceMaterial = meshletRenderer.GetSourceMaterial(subMeshIndex);
            if (sourceMaterial == null)
            {
                return new GPUDrivenMaterialProxySyncResult(
                    false,
                    false,
                    $"Submesh {subMeshIndex} has no source Material to synchronize from.",
                    null
                );
            }

            GPUDrivenMaterialProxyTextureMode textureMode = ResolveActiveTextureMode();
            GPUDrivenMaterialProxySyncResult syncResult = materialProxy.SyncFromSourceMaterial(
                sourceMaterial,
                textureMode);
            var warnings = new List<string>(syncResult.Warnings);
            uint revisionAfterSync = materialProxy.Revision;
            if (syncResult.Success
                && textureMode == GPUDrivenMaterialProxyTextureMode.VirtualTexture
                && !BuildOrRefreshStreamedVirtualTexture(materialProxy, out _, out _, out string streamError))
            {
                warnings.Add(streamError);
            }

            bool changed = syncResult.Changed || materialProxy.Revision != revisionAfterSync;
            if (syncResult.Success && changed)
            {
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);
            }

            return new GPUDrivenMaterialProxySyncResult(
                syncResult.Success,
                changed,
                syncResult.ErrorMessage,
                warnings.ToArray());
        }

        public static GPUDrivenMaterialProxySyncResult SyncMaterialProxiesFromSourceMaterials(
            MeshletRenderer meshletRenderer,
            bool skipStreamedVirtualTextureRebuildIfUpToDate = false
        )
        {
            if (meshletRenderer == null)
            {
                return new GPUDrivenMaterialProxySyncResult(false, false, "MeshletRenderer is null.", null);
            }

            RefreshSource(meshletRenderer, "Normalize Meshlet Renderer Source");

            if (meshletRenderer.sourceMesh == null)
            {
                return new GPUDrivenMaterialProxySyncResult(
                    false,
                    false,
                    "MeshletRenderer source Mesh is not captured. Run the takeover flow first.",
                    null
                );
            }

            var warnings = new List<string>();
            bool changed = false;
            GPUDrivenMaterialProxyTextureMode textureMode = ResolveActiveTextureMode();

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

                GPUDrivenMaterialProxySyncResult syncResult = materialProxy.SyncFromSourceMaterial(
                    sourceMaterial,
                    textureMode);
                if (!syncResult.Success)
                {
                    return new GPUDrivenMaterialProxySyncResult(false, changed, syncResult.ErrorMessage, warnings.ToArray());
                }

                changed |= syncResult.Changed;
                warnings.AddRange(syncResult.Warnings);
                uint revisionAfterSync = materialProxy.Revision;
                if (textureMode == GPUDrivenMaterialProxyTextureMode.VirtualTexture
                    && !BuildOrRefreshStreamedVirtualTexture(
                        materialProxy,
                        out _,
                        out _,
                        out string streamError,
                        skipStreamedVirtualTextureRebuildIfUpToDate))
                    warnings.Add(streamError);
                changed |= materialProxy.Revision != revisionAfterSync;
            }

            if (changed)
            {
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);
            }

            return new GPUDrivenMaterialProxySyncResult(true, changed, string.Empty, warnings.ToArray());
        }

        internal static bool BuildOrRefreshStreamedVirtualTexture(
            GPUDrivenMaterialProxy materialProxy,
            out string assetPath,
            out bool wasCreated,
            out string errorMessage,
            bool skipIfUpToDate = false)
        {
            assetPath = string.Empty;
            wasCreated = false;
            errorMessage = string.Empty;
            if (materialProxy == null)
            {
                errorMessage = "GPUDriven material proxy is null.";
                return false;
            }

            GPUDrivenMaterialProxySourceTextures sourceTextures = ResolveSourceTextures(
                materialProxy,
                out VividVirtualTextureAddressMode addressMode);
            if (!sourceTextures.HasAnyTexture)
            {
                if (materialProxy.TextureMode != GPUDrivenMaterialProxyTextureMode.VirtualTexture
                    || materialProxy.StreamedVirtualTexture != null
                    || materialProxy.BaseMap != null
                    || materialProxy.BumpMap != null
                    || materialProxy.MaskMap != null)
                {
                    Undo.RecordObject(materialProxy, "Use GPUDriven Virtual Texture Payload");
                    materialProxy.TextureMode = GPUDrivenMaterialProxyTextureMode.VirtualTexture;
                    materialProxy.StreamedVirtualTexture = null;
                    EditorUtility.SetDirty(materialProxy);
                    AssetDatabase.SaveAssetIfDirty(materialProxy);
                }

                return true;
            }

            string proxyAssetPath = AssetDatabase.GetAssetPath(materialProxy);
            if (string.IsNullOrWhiteSpace(proxyAssetPath))
            {
                errorMessage = "Save the GPUDriven material proxy as an asset before building its streamed VT data.";
                return false;
            }

            string directory = Path.GetDirectoryName(proxyAssetPath)?.Replace('\\', '/') ?? "Assets";
            string assetName = Path.GetFileNameWithoutExtension(proxyAssetPath) + "_Surface." + VividVirtualTextureAssetImporter.Extension;
            assetPath = Path.Combine(directory, assetName).Replace('\\', '/');

            if (!BuildOrRefreshStreamedVirtualTexture(
                    assetPath,
                    sourceTextures.BaseMap,
                    sourceTextures.BumpMap,
                    sourceTextures.MaskMap,
                    materialProxy.MaskMode,
                    addressMode,
                    out VividVirtualTextureAsset streamedAsset,
                    out wasCreated,
                    out errorMessage,
                    skipIfUpToDate))
            {
                return false;
            }

            if (materialProxy.StreamedVirtualTexture != streamedAsset
                || materialProxy.TextureMode != GPUDrivenMaterialProxyTextureMode.VirtualTexture)
            {
                Undo.RecordObject(materialProxy, "Bind GPUDriven Streamed VT Asset");
                materialProxy.StreamedVirtualTexture = streamedAsset;
                EditorUtility.SetDirty(materialProxy);
                AssetDatabase.SaveAssetIfDirty(materialProxy);
            }

            return true;
        }

        internal static bool BuildOrRefreshStreamedVirtualTexture(
            string assetPath,
            Texture2D baseMap,
            Texture2D normalMap,
            Texture2D maskMap,
            GPUDrivenMaterialMaskMode maskMode,
            VividVirtualTextureAddressMode addressMode,
            out VividVirtualTextureAsset streamedAsset,
            out bool wasCreated,
            out string errorMessage,
            bool skipIfUpToDate = false)
        {
            streamedAsset = null;
            wasCreated = false;
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                errorMessage = "A streamed VT asset path is required.";
                return false;
            }

            if (baseMap == null && normalMap == null && maskMap == null)
            {
                errorMessage = "A streamed VT asset requires at least one source texture.";
                return false;
            }

            try
            {
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                {
                    File.WriteAllText(assetPath, VividVirtualTextureAssetImporter.Version3Marker);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                    wasCreated = true;
                }
                else if (!string.Equals(
                             File.ReadAllText(assetPath).Trim(),
                             VividVirtualTextureAssetImporter.Version3Marker,
                             StringComparison.Ordinal))
                {
                    // Early streamed GPUDriven assets were created as empty importer source files.
                    // Rebuild them as schema-v3 assets so DesktopBCn/Zstd settings are honored.
                    File.WriteAllText(assetPath, VividVirtualTextureAssetImporter.Version3Marker);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                }

                if (AssetImporter.GetAtPath(assetPath) is not VividVirtualTextureAssetImporter importer)
                {
                    errorMessage = $"'{assetPath}' is not a Vivid virtual texture asset.";
                    return false;
                }

                VividVirtualTextureMaskStorage maskStorage = maskMode == GPUDrivenMaterialMaskMode.Roughness
                    ? VividVirtualTextureMaskStorage.SingleChannelR
                    : VividVirtualTextureMaskStorage.PackedRGBA;
                GPUDrivenVirtualTexturePhysicalPoolQuality poolQuality =
                    VirtualTextureGPUDrivenTextureBackend.ResolveActivePhysicalPoolQuality();
                int borderSize = VirtualTextureGPUDrivenTextureBackend
                    .ResolveDescriptorProfile(poolQuality)
                    .BorderSize;
                bool importerChanged = importer.SourceTexture != baseMap
                                       || importer.NormalTexture != normalMap
                                       || importer.MaskTexture != maskMap
                                       || importer.BuildProfile != VividVirtualTextureBuildProfile.GPUDrivenSurface
                                       || importer.AddressMode != addressMode
                                       || importer.StorageProfile != VividVirtualTextureStorageProfile.DesktopBCn
                                       || importer.StreamCompression != VividVirtualTextureStreamCompression.Zstd
                                       || importer.MaskStorage != maskStorage
                                       || importer.BCQuality != VividVirtualTextureBCQuality.Normal
                                       || importer.ZstdLevel != 3
                                       || importer.ChunkTargetKiB != 256
                                       || importer.PageSize != 128
                                       || importer.BorderSize != borderSize
                                       || importer.MipCount != 0
                                       || importer.FallbackColor != Color.white
                                       || importer.NormalFallbackColor != new Color(0.5f, 0.5f, 1.0f, 0.5f)
                                       || importer.MaskFallbackColor != Color.white;

                streamedAsset = AssetDatabase.LoadAssetAtPath<VividVirtualTextureAsset>(assetPath);
                if (skipIfUpToDate
                    && !importerChanged
                    && !EditorUtility.IsDirty(importer)
                    && IsReusableSourceTexture(baseMap)
                    && IsReusableSourceTexture(normalMap)
                    && IsReusableSourceTexture(maskMap)
                    && IsReusableStreamedVirtualTexture(streamedAsset))
                {
                    return true;
                }

                if (importerChanged)
                {
                    Undo.RecordObject(importer, "Build GPUDriven Streamed VT Asset");
                    importer.SourceTexture = baseMap;
                    importer.NormalTexture = normalMap;
                    importer.MaskTexture = maskMap;
                    importer.BuildProfile = VividVirtualTextureBuildProfile.GPUDrivenSurface;
                    importer.AddressMode = addressMode;
                    importer.StorageProfile = VividVirtualTextureStorageProfile.DesktopBCn;
                    importer.StreamCompression = VividVirtualTextureStreamCompression.Zstd;
                    importer.MaskStorage = maskStorage;
                    importer.BCQuality = VividVirtualTextureBCQuality.Normal;
                    importer.ZstdLevel = 3;
                    importer.ChunkTargetKiB = 256;
                    importer.PageSize = 128;
                    importer.BorderSize = borderSize;
                    importer.MipCount = 0;
                    importer.FallbackColor = Color.white;
                    importer.NormalFallbackColor = new Color(0.5f, 0.5f, 1.0f, 0.5f);
                    importer.MaskFallbackColor = Color.white;
                    EditorUtility.SetDirty(importer);
                }

                importer.SaveAndReimport();

                streamedAsset = AssetDatabase.LoadAssetAtPath<VividVirtualTextureAsset>(assetPath);
                if (streamedAsset == null || streamedAsset.BuiltData == null)
                {
                    errorMessage = $"Failed to import GPUDriven streamed VT asset '{assetPath}'.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                errorMessage = $"Failed to build GPUDriven streamed VT asset: {exception.Message}";
                return false;
            }
        }

        private static GPUDrivenMaterialProxySourceTextures ResolveSourceTextures(
            GPUDrivenMaterialProxy materialProxy,
            out VividVirtualTextureAddressMode addressMode)
        {
            GPUDrivenMaterialProxySourceTextures sourceTextures;
            if (materialProxy.TextureMode == GPUDrivenMaterialProxyTextureMode.Bindless)
            {
                sourceTextures = new GPUDrivenMaterialProxySourceTextures(
                    materialProxy.BaseMap,
                    materialProxy.BumpMap,
                    materialProxy.MaskMap,
                    materialProxy.MaskMode);
                if (!sourceTextures.HasAnyTexture && materialProxy.SourceMaterial != null)
                {
                    sourceTextures = GPUDrivenMaterialProxySyncUtility.ExtractSourceTextures(
                        materialProxy.SourceMaterial);
                }
            }
            else if (materialProxy.SourceMaterial != null)
            {
                sourceTextures = GPUDrivenMaterialProxySyncUtility.ExtractSourceTextures(
                    materialProxy.SourceMaterial);
            }
            else
            {
                sourceTextures = new GPUDrivenMaterialProxySourceTextures(
                    materialProxy.BaseMap,
                    materialProxy.BumpMap,
                    materialProxy.MaskMap,
                    materialProxy.MaskMode);
            }

            VividVirtualTextureAsset streamedAsset = materialProxy.StreamedVirtualTexture;
            if (!sourceTextures.HasAnyTexture
                && materialProxy.SourceMaterial == null
                && streamedAsset != null
                && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(streamedAsset))
                    is VividVirtualTextureAssetImporter importer)
            {
                sourceTextures = new GPUDrivenMaterialProxySourceTextures(
                    importer.SourceTexture,
                    importer.NormalTexture,
                    importer.MaskTexture,
                    materialProxy.MaskMode);
                addressMode = importer.AddressMode;
                return sourceTextures;
            }

            Texture texture = sourceTextures.BaseMap != null
                ? sourceTextures.BaseMap
                : sourceTextures.BumpMap != null
                    ? sourceTextures.BumpMap
                    : sourceTextures.MaskMap;
            addressMode = texture != null && texture.wrapMode == TextureWrapMode.Clamp
                ? VividVirtualTextureAddressMode.Clamp
                : VividVirtualTextureAddressMode.Repeat;
            return sourceTextures;
        }

        private static bool IsReusableStreamedVirtualTexture(VividVirtualTextureAsset streamedAsset)
        {
            if (!VirtualTextureGPUDrivenTextureBackend.IsCompatibleStreamedAsset(
                    streamedAsset,
                    VirtualTextureGPUDrivenTextureBackend.ResolveActivePhysicalPoolQuality(),
                    out _))
            {
                return false;
            }

            VividVirtualTextureBuiltData builtData = streamedAsset.BuiltData;
            if (builtData == null
                || !builtData.HasStreamData
                || builtData.ContainerSchemaVersion != VividVirtualTextureBuiltData.CurrentContainerSchemaVersion
                || string.IsNullOrWhiteSpace(builtData.StreamDataPath)
                || !File.Exists(builtData.StreamDataPath))
            {
                return false;
            }

            return VividVirtualTextureAssetProducer.ValidateContainerHeader(
                builtData.StreamDataPath,
                builtData);
        }

        private static bool IsReusableSourceTexture(Texture2D texture)
        {
            return texture == null || (AssetDatabase.Contains(texture) && !EditorUtility.IsDirty(texture));
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

    }
}
