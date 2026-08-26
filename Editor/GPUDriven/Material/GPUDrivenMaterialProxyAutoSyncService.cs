using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    [InitializeOnLoad]
    internal static class GPUDrivenMaterialProxyAutoSyncService
    {
        private const double VirtualTextureRefreshDelaySeconds = 0.2;
        private const double ProxySaveDelaySeconds = 0.5;

        private static readonly Dictionary<EntityId, List<GPUDrivenMaterialProxy>> s_ProxiesByMaterialId = new();
        private static readonly Dictionary<EntityId, EntityId> s_MaterialIdByProxyId = new();
        private static readonly Dictionary<EntityId, MaterialTextureSignature> s_TextureSignaturesByMaterialId = new();
        private static readonly HashSet<string> s_TrackedMaterialAssetPaths = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_TrackedProxyAssetPaths = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<EntityId> s_PendingMaterialIds = new();
        private static readonly HashSet<EntityId> s_ForcedVirtualTextureCheckMaterialIds = new();
        private static readonly Dictionary<EntityId, GPUDrivenMaterialProxy> s_PendingVirtualTextureRefreshes = new();
        private static readonly Dictionary<EntityId, GPUDrivenMaterialProxy> s_PendingProxySaves = new();

        private static bool s_IndexRebuildRequested = true;
        private static bool s_RequeueAllSourceMaterialsOnIndexRebuild = true;
        private static double s_VirtualTextureRefreshTime = double.PositiveInfinity;
        private static double s_ProxySaveTime = double.PositiveInfinity;

        static GPUDrivenMaterialProxyAutoSyncService()
        {
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        internal static void HandleImportedAssets(string[] importedAssetPaths)
        {
            if (importedAssetPaths == null)
            {
                return;
            }

            for (int pathIndex = 0; pathIndex < importedAssetPaths.Length; pathIndex++)
            {
                string assetPath = importedAssetPaths[pathIndex];
                if (s_TrackedMaterialAssetPaths.Contains(assetPath))
                {
                    s_IndexRebuildRequested = true;
                }

                Type mainAssetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                if (mainAssetType != typeof(Material)
                    && mainAssetType != typeof(GPUDrivenMaterialProxy)
                    && !s_TrackedMaterialAssetPaths.Contains(assetPath)
                    && !s_TrackedProxyAssetPaths.Contains(assetPath))
                {
                    continue;
                }

                UnityEngine.Object[] importedAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                for (int assetIndex = 0; assetIndex < importedAssets.Length; assetIndex++)
                {
                    switch (importedAssets[assetIndex])
                    {
                        case Material material:
                            QueueMaterial(material, forceVirtualTextureCheck: true);
                            break;
                        case GPUDrivenMaterialProxy materialProxy:
                            IndexProxy(materialProxy, queueSourceMaterial: true);
                            break;
                    }
                }
            }
        }

        internal static void HandleRemovedAssets(string[] removedAssetPaths)
        {
            if (removedAssetPaths == null)
            {
                return;
            }

            for (int pathIndex = 0; pathIndex < removedAssetPaths.Length; pathIndex++)
            {
                string assetPath = removedAssetPaths[pathIndex];
                if (s_TrackedMaterialAssetPaths.Contains(assetPath)
                    || s_TrackedProxyAssetPaths.Contains(assetPath))
                {
                    s_IndexRebuildRequested = true;
                    return;
                }
            }
        }

        internal static void QueueMaterial(Material material)
        {
            QueueMaterial(material, forceVirtualTextureCheck: false);
        }

        private static void QueueMaterial(Material material, bool forceVirtualTextureCheck)
        {
            if (material != null)
            {
                EntityId materialId = material.GetEntityId();
                s_PendingMaterialIds.Add(materialId);
                if (forceVirtualTextureCheck)
                {
                    s_ForcedVirtualTextureCheckMaterialIds.Add(materialId);
                }
            }
        }

        internal static int SynchronizeMaterialNowForTests(
            Material material,
            GPUDrivenMaterialProxyTextureMode textureMode)
        {
            return SynchronizeMaterial(material, textureMode);
        }

        internal static void IndexProxyForTests(GPUDrivenMaterialProxy materialProxy)
        {
            IndexProxy(materialProxy, queueSourceMaterial: false);
        }

        internal static void ResetForTests(
            bool requestIndexRebuild = false,
            bool requeueAllSourceMaterials = false)
        {
            ClearIndex();
            s_PendingMaterialIds.Clear();
            s_ForcedVirtualTextureCheckMaterialIds.Clear();
            s_PendingVirtualTextureRefreshes.Clear();
            s_PendingProxySaves.Clear();
            s_IndexRebuildRequested = requestIndexRebuild;
            s_RequeueAllSourceMaterialsOnIndexRebuild =
                requestIndexRebuild && requeueAllSourceMaterials;
            s_VirtualTextureRefreshTime = double.PositiveInfinity;
            s_ProxySaveTime = double.PositiveInfinity;
        }

        internal static void FlushPendingProxySavesForTests()
        {
            ProcessPendingProxySaves();
        }

        internal static void RebuildIndexAndSynchronizeForTests(string searchFolder)
        {
            RebuildIndex(new[] { searchFolder });
            ProcessPendingMaterials();
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int eventIndex = 0; eventIndex < stream.length; eventIndex++)
            {
                if (stream.GetEventType(eventIndex) != ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    continue;
                }

                stream.GetChangeAssetObjectPropertiesEvent(eventIndex, out ChangeAssetObjectPropertiesEventArgs change);
                switch (EditorUtility.EntityIdToObject(change.entityId))
                {
                    case Material material:
                        QueueMaterial(material);
                        break;
                    case GPUDrivenMaterialProxy materialProxy:
                        IndexProxy(materialProxy, queueSourceMaterial: true);
                        break;
                }
            }
        }

        private static void OnEditorUpdate()
        {
            if (EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (s_IndexRebuildRequested)
            {
                RebuildIndex();
            }

            ProcessPendingMaterials();

            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime >= s_VirtualTextureRefreshTime)
            {
                ProcessPendingVirtualTextureRefreshes();
            }

            if (currentTime >= s_ProxySaveTime)
            {
                ProcessPendingProxySaves();
            }
        }

        private static void ProcessPendingMaterials()
        {
            if (s_PendingMaterialIds.Count == 0)
            {
                return;
            }

            var pendingMaterialIds = new List<EntityId>(s_PendingMaterialIds);
            s_PendingMaterialIds.Clear();
            GPUDrivenMaterialProxyTextureMode textureMode =
                GPUDrivenMaterialProxyEditorUtility.ResolveActiveTextureMode();

            for (int materialIndex = 0; materialIndex < pendingMaterialIds.Count; materialIndex++)
            {
                EntityId materialId = pendingMaterialIds[materialIndex];
                bool forceVirtualTextureCheck = s_ForcedVirtualTextureCheckMaterialIds.Remove(materialId);
                if (EditorUtility.EntityIdToObject(materialId) is Material material)
                {
                    SynchronizeMaterial(material, textureMode, forceVirtualTextureCheck);
                }
            }
        }

        private static int SynchronizeMaterial(
            Material material,
            GPUDrivenMaterialProxyTextureMode textureMode,
            bool forceVirtualTextureCheck = false)
        {
            if (material == null
                || !s_ProxiesByMaterialId.TryGetValue(material.GetEntityId(), out List<GPUDrivenMaterialProxy> proxies))
            {
                return 0;
            }

            EntityId materialId = material.GetEntityId();
            MaterialTextureSignature currentTextureSignature = MaterialTextureSignature.Create(material);
            bool sourceTexturesChanged = forceVirtualTextureCheck
                                         || !s_TextureSignaturesByMaterialId.TryGetValue(
                                             materialId,
                                             out MaterialTextureSignature previousTextureSignature)
                                         || !currentTextureSignature.Equals(previousTextureSignature);
            s_TextureSignaturesByMaterialId[materialId] = currentTextureSignature;

            int changedProxyCount = 0;
            var synchronizedProxyIds = new HashSet<EntityId>();
            for (int proxyIndex = proxies.Count - 1; proxyIndex >= 0; proxyIndex--)
            {
                GPUDrivenMaterialProxy materialProxy = proxies[proxyIndex];
                if (materialProxy == null)
                {
                    proxies.RemoveAt(proxyIndex);
                    continue;
                }

                EntityId proxyId = materialProxy.GetEntityId();
                if (!synchronizedProxyIds.Add(proxyId))
                {
                    continue;
                }

                GPUDrivenMaterialProxyTextureMode previousTextureMode = materialProxy.TextureMode;
                GPUDrivenMaterialProxySyncResult syncResult = materialProxy.SyncFromSourceMaterial(
                    material,
                    textureMode,
                    recordUndo: false,
                    saveAsset: false);
                if (!syncResult.Success)
                {
                    Debug.LogWarning(
                        $"[VividRP] Failed to automatically synchronize GPUDriven material proxy " +
                        $"'{materialProxy.name}': {syncResult.ErrorMessage}",
                        materialProxy);
                    continue;
                }

                if (syncResult.Changed)
                {
                    changedProxyCount++;
                    ScheduleProxySave(materialProxy);
                }

                if (textureMode == GPUDrivenMaterialProxyTextureMode.VirtualTexture
                    && (sourceTexturesChanged
                        || previousTextureMode != GPUDrivenMaterialProxyTextureMode.VirtualTexture
                        || (currentTextureSignature.HasAnyTexture && materialProxy.StreamedVirtualTexture == null)))
                {
                    ScheduleVirtualTextureRefresh(materialProxy);
                }
            }

            return changedProxyCount;
        }

        private static void ScheduleVirtualTextureRefresh(GPUDrivenMaterialProxy materialProxy)
        {
            s_PendingVirtualTextureRefreshes[materialProxy.GetEntityId()] = materialProxy;
            s_VirtualTextureRefreshTime = EditorApplication.timeSinceStartup + VirtualTextureRefreshDelaySeconds;
        }

        private static void ProcessPendingVirtualTextureRefreshes()
        {
            s_VirtualTextureRefreshTime = double.PositiveInfinity;
            if (s_PendingVirtualTextureRefreshes.Count == 0)
            {
                return;
            }

            var pendingProxies = new List<GPUDrivenMaterialProxy>(s_PendingVirtualTextureRefreshes.Values);
            s_PendingVirtualTextureRefreshes.Clear();
            for (int proxyIndex = 0; proxyIndex < pendingProxies.Count; proxyIndex++)
            {
                GPUDrivenMaterialProxy materialProxy = pendingProxies[proxyIndex];
                if (materialProxy == null
                    || materialProxy.TextureMode != GPUDrivenMaterialProxyTextureMode.VirtualTexture
                    || !AssetDatabase.Contains(materialProxy))
                {
                    continue;
                }

                if (!GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                        materialProxy,
                        out _,
                        out _,
                        out string errorMessage,
                        skipIfUpToDate: true))
                {
                    Debug.LogWarning(
                        $"[VividRP] Failed to automatically refresh streamed VT for " +
                        $"'{materialProxy.name}': {errorMessage}",
                        materialProxy);
                }
            }
        }

        private static void ScheduleProxySave(GPUDrivenMaterialProxy materialProxy)
        {
            s_PendingProxySaves[materialProxy.GetEntityId()] = materialProxy;
            s_ProxySaveTime = EditorApplication.timeSinceStartup + ProxySaveDelaySeconds;
        }

        private static void ProcessPendingProxySaves()
        {
            s_ProxySaveTime = double.PositiveInfinity;
            if (s_PendingProxySaves.Count == 0)
            {
                return;
            }

            var pendingProxies = new List<GPUDrivenMaterialProxy>(s_PendingProxySaves.Values);
            s_PendingProxySaves.Clear();
            for (int proxyIndex = 0; proxyIndex < pendingProxies.Count; proxyIndex++)
            {
                GPUDrivenMaterialProxy materialProxy = pendingProxies[proxyIndex];
                if (materialProxy != null && AssetDatabase.Contains(materialProxy))
                {
                    AssetDatabase.SaveAssetIfDirty(materialProxy);
                }
            }
        }

        private static void RebuildIndex()
        {
            RebuildIndex(null);
        }

        private static void RebuildIndex(string[] searchFolders)
        {
            s_IndexRebuildRequested = false;
            bool requeueSourceMaterials = s_RequeueAllSourceMaterialsOnIndexRebuild;
            s_RequeueAllSourceMaterialsOnIndexRebuild = false;
            ClearIndex();
            string filter = $"t:{nameof(GPUDrivenMaterialProxy)}";
            string[] proxyGuids = searchFolders is { Length: > 0 }
                ? AssetDatabase.FindAssets(filter, searchFolders)
                : AssetDatabase.FindAssets(filter);
            for (int guidIndex = 0; guidIndex < proxyGuids.Length; guidIndex++)
            {
                string proxyPath = AssetDatabase.GUIDToAssetPath(proxyGuids[guidIndex]);
                GPUDrivenMaterialProxy materialProxy =
                    AssetDatabase.LoadAssetAtPath<GPUDrivenMaterialProxy>(proxyPath);
                // The first rebuild after a domain reload reconstructs work that
                // was held only in memory. Ordinary import-driven rebuilds keep
                // their precise material queue instead of resyncing every asset.
                IndexProxy(materialProxy, requeueSourceMaterials);
            }
        }

        private static void IndexProxy(GPUDrivenMaterialProxy materialProxy, bool queueSourceMaterial)
        {
            if (materialProxy == null)
            {
                return;
            }

            EntityId proxyId = materialProxy.GetEntityId();
            EntityId previousMaterialId = EntityId.None;
            if (s_MaterialIdByProxyId.TryGetValue(proxyId, out EntityId indexedMaterialId))
            {
                previousMaterialId = indexedMaterialId;
                RemoveProxyFromMaterial(indexedMaterialId, proxyId);
            }

            string proxyPath = AssetDatabase.GetAssetPath(materialProxy);
            if (!string.IsNullOrEmpty(proxyPath))
            {
                s_TrackedProxyAssetPaths.Add(proxyPath);
            }

            Material sourceMaterial = materialProxy.SourceMaterial;
            if (sourceMaterial == null)
            {
                s_MaterialIdByProxyId.Remove(proxyId);
                return;
            }

            EntityId materialId = sourceMaterial.GetEntityId();
            if (!s_ProxiesByMaterialId.TryGetValue(materialId, out List<GPUDrivenMaterialProxy> proxies))
            {
                proxies = new List<GPUDrivenMaterialProxy>();
                s_ProxiesByMaterialId.Add(materialId, proxies);
            }

            proxies.Add(materialProxy);
            s_MaterialIdByProxyId[proxyId] = materialId;

            string materialPath = AssetDatabase.GetAssetPath(sourceMaterial);
            if (!string.IsNullOrEmpty(materialPath))
            {
                s_TrackedMaterialAssetPaths.Add(materialPath);
            }

            if (queueSourceMaterial && !previousMaterialId.Equals(materialId))
            {
                QueueMaterial(sourceMaterial);
            }
        }

        private static void RemoveProxyFromMaterial(EntityId materialId, EntityId proxyId)
        {
            if (!s_ProxiesByMaterialId.TryGetValue(materialId, out List<GPUDrivenMaterialProxy> proxies))
            {
                return;
            }

            for (int proxyIndex = proxies.Count - 1; proxyIndex >= 0; proxyIndex--)
            {
                GPUDrivenMaterialProxy indexedProxy = proxies[proxyIndex];
                if (indexedProxy == null || indexedProxy.GetEntityId().Equals(proxyId))
                {
                    proxies.RemoveAt(proxyIndex);
                }
            }

            if (proxies.Count == 0)
            {
                s_ProxiesByMaterialId.Remove(materialId);
                s_TextureSignaturesByMaterialId.Remove(materialId);
            }
        }

        private static void ClearIndex()
        {
            s_ProxiesByMaterialId.Clear();
            s_MaterialIdByProxyId.Clear();
            s_TextureSignaturesByMaterialId.Clear();
            s_TrackedMaterialAssetPaths.Clear();
            s_TrackedProxyAssetPaths.Clear();
        }

        private static void OnBeforeAssemblyReload()
        {
            ProcessPendingProxySaves();
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            EditorApplication.update -= OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        private readonly struct MaterialTextureSignature : IEquatable<MaterialTextureSignature>
        {
            private MaterialTextureSignature(
                EntityId baseMapId,
                EntityId bumpMapId,
                EntityId maskMapId,
                GPUDrivenMaterialMaskMode maskMode,
                TextureWrapMode addressSourceWrapMode)
            {
                BaseMapId = baseMapId;
                BumpMapId = bumpMapId;
                MaskMapId = maskMapId;
                MaskMode = maskMode;
                AddressSourceWrapMode = addressSourceWrapMode;
            }

            private EntityId BaseMapId { get; }

            private EntityId BumpMapId { get; }

            private EntityId MaskMapId { get; }

            private GPUDrivenMaterialMaskMode MaskMode { get; }

            private TextureWrapMode AddressSourceWrapMode { get; }

            internal bool HasAnyTexture => !BaseMapId.Equals(EntityId.None)
                                           || !BumpMapId.Equals(EntityId.None)
                                           || !MaskMapId.Equals(EntityId.None);

            internal static MaterialTextureSignature Create(Material material)
            {
                GPUDrivenMaterialProxySourceTextures textures =
                    GPUDrivenMaterialProxySyncUtility.ExtractSourceTextures(material);
                Texture2D addressSource = textures.BaseMap != null
                    ? textures.BaseMap
                    : textures.BumpMap != null
                        ? textures.BumpMap
                        : textures.MaskMap;
                return new MaterialTextureSignature(
                    GetObjectEntityId(textures.BaseMap),
                    GetObjectEntityId(textures.BumpMap),
                    GetObjectEntityId(textures.MaskMap),
                    textures.MaskMode,
                    addressSource != null ? addressSource.wrapMode : TextureWrapMode.Repeat);
            }

            public bool Equals(MaterialTextureSignature other)
            {
                return BaseMapId == other.BaseMapId
                       && BumpMapId == other.BumpMapId
                       && MaskMapId == other.MaskMapId
                       && MaskMode == other.MaskMode
                       && AddressSourceWrapMode == other.AddressSourceWrapMode;
            }

            private static EntityId GetObjectEntityId(UnityEngine.Object target)
            {
                return target != null ? target.GetEntityId() : EntityId.None;
            }
        }
    }

    internal sealed class GPUDrivenMaterialProxyAutoSyncPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            GPUDrivenMaterialProxyAutoSyncService.HandleImportedAssets(importedAssets);
            GPUDrivenMaterialProxyAutoSyncService.HandleImportedAssets(movedAssets);
            GPUDrivenMaterialProxyAutoSyncService.HandleRemovedAssets(deletedAssets);
            GPUDrivenMaterialProxyAutoSyncService.HandleRemovedAssets(movedFromAssetPaths);
        }
    }
}
