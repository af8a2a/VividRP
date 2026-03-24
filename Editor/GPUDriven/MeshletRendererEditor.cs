using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VividRP.Editor.GPUDriven.Meshlets;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.GPUDriven
{
    internal readonly struct MeshletRendererTakeOverRepairResult
    {
        public MeshletRendererTakeOverRepairResult(
            bool success,
            bool changed,
            string errorMessage,
            string[] createdMeshletAssetPaths,
            string[] createdMaterialProxyAssetPaths,
            string[] warnings)
        {
            Success = success;
            Changed = changed;
            ErrorMessage = errorMessage;
            CreatedMeshletAssetPaths = createdMeshletAssetPaths ?? Array.Empty<string>();
            CreatedMaterialProxyAssetPaths = createdMaterialProxyAssetPaths ?? Array.Empty<string>();
            Warnings = warnings ?? Array.Empty<string>();
        }

        public bool Success { get; }

        public bool Changed { get; }

        public string ErrorMessage { get; }

        public string[] CreatedMeshletAssetPaths { get; }

        public string[] CreatedMaterialProxyAssetPaths { get; }

        public string[] Warnings { get; }
    }

    internal readonly struct MeshletRendererSourceRendererDetachResult
    {
        public MeshletRendererSourceRendererDetachResult(
            bool success,
            bool changed,
            string errorMessage,
            string[] createdMeshletAssetPaths,
            string[] createdMaterialProxyAssetPaths,
            string[] warnings)
        {
            Success = success;
            Changed = changed;
            ErrorMessage = errorMessage;
            CreatedMeshletAssetPaths = createdMeshletAssetPaths ?? Array.Empty<string>();
            CreatedMaterialProxyAssetPaths = createdMaterialProxyAssetPaths ?? Array.Empty<string>();
            Warnings = warnings ?? Array.Empty<string>();
        }

        public bool Success { get; }

        public bool Changed { get; }

        public string ErrorMessage { get; }

        public string[] CreatedMeshletAssetPaths { get; }

        public string[] CreatedMaterialProxyAssetPaths { get; }

        public string[] Warnings { get; }
    }

    [CustomEditor(typeof(MeshletRenderer))]
    internal sealed class MeshletRendererEditor : UnityEditor.Editor
    {
        private SerializedProperty m_SourceMesh;
        private SerializedProperty m_SourceMaterials;
        private SerializedProperty m_SourceRenderingEnabled;
        private SerializedProperty m_ShadowCastingMode;
        private SerializedProperty m_ReceiveShadows;
        private SerializedProperty m_MotionVectorGenerationMode;
        private SerializedProperty m_RenderingLayerMask;
        private SerializedProperty m_MeshletCollections;
        private SerializedProperty m_MaterialProxies;
        private SerializedProperty m_TakeOverSourceRenderer;

        private void OnEnable()
        {
            m_SourceMesh = serializedObject.FindProperty("m_SourceMesh");
            m_SourceMaterials = serializedObject.FindProperty("m_SourceMaterials");
            m_SourceRenderingEnabled = serializedObject.FindProperty("m_SourceRenderingEnabled");
            m_ShadowCastingMode = serializedObject.FindProperty("m_ShadowCastingMode");
            m_ReceiveShadows = serializedObject.FindProperty("m_ReceiveShadows");
            m_MotionVectorGenerationMode = serializedObject.FindProperty("m_MotionVectorGenerationMode");
            m_RenderingLayerMask = serializedObject.FindProperty("m_RenderingLayerMask");
            m_MeshletCollections = serializedObject.FindProperty("m_MeshletCollections");
            m_MaterialProxies = serializedObject.FindProperty("m_MaterialProxies");
            m_TakeOverSourceRenderer = serializedObject.FindProperty("m_TakeOverSourceRenderer");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(m_SourceMesh);
            }

            EditorGUILayout.PropertyField(m_SourceMaterials, true);
            EditorGUILayout.PropertyField(m_SourceRenderingEnabled);
            EditorGUILayout.PropertyField(m_ShadowCastingMode);
            EditorGUILayout.PropertyField(m_ReceiveShadows);
            EditorGUILayout.PropertyField(m_MotionVectorGenerationMode);
            EditorGUILayout.PropertyField(m_RenderingLayerMask);
            EditorGUILayout.PropertyField(m_TakeOverSourceRenderer);
            EditorGUILayout.PropertyField(m_MeshletCollections, true);
            EditorGUILayout.PropertyField(m_MaterialProxies, true);

            serializedObject.ApplyModifiedProperties();

            var meshletRenderer = (MeshletRenderer) target;
            DrawStatus(meshletRenderer);
            DrawActions(meshletRenderer);
        }

        private static void DrawStatus(MeshletRenderer meshletRenderer)
        {
            if (meshletRenderer == null)
            {
                return;
            }

            if (meshletRenderer.sourceMesh == null)
            {
                EditorGUILayout.HelpBox(
                    "Source data has not been captured yet. Use 'Take Over And Remove MeshRenderer' to copy the attached MeshRenderer state into MeshletRenderer.",
                    MessageType.Info
                );
                return;
            }

            if (meshletRenderer.TryValidate(out string validationMessage))
            {
                EditorGUILayout.HelpBox(
                    $"Ready: '{meshletRenderer.sourceMesh.name}' with {meshletRenderer.subMeshCount} submesh binding(s).",
                    MessageType.Info
                );
                return;
            }

            EditorGUILayout.HelpBox(validationMessage, MessageType.Warning);

            if (meshletRenderer.sourceMesh != null && !MeshletRendererEditorUtility.IsPersistentMesh(meshletRenderer.sourceMesh))
            {
                EditorGUILayout.HelpBox(
                    "The source Mesh is not stored as an asset, so meshlet assets cannot be generated automatically.",
                    MessageType.None
                );
            }
        }

        private void DrawActions(MeshletRenderer meshletRenderer)
        {
            EditorGUILayout.Space();

            if (meshletRenderer.takeOverSourceRenderer && meshletRenderer.sourceMesh != null && !meshletRenderer.TryValidate(out _))
            {
                if (GUILayout.Button("Repair Takeover Bindings"))
                {
                    MeshletRendererTakeOverRepairResult repairResult =
                        MeshletRendererEditorUtility.RepairTakeOverBindings(meshletRenderer);

                    if (!repairResult.Success && !string.IsNullOrEmpty(repairResult.ErrorMessage))
                    {
                        Debug.LogWarning($"[VividRP] {repairResult.ErrorMessage}", meshletRenderer);
                    }

                    LogWarnings(meshletRenderer, repairResult.Warnings);
                    SelectLastCreatedAsset(repairResult.CreatedMaterialProxyAssetPaths, repairResult.CreatedMeshletAssetPaths);
                    serializedObject.Update();
                }

                EditorGUILayout.Space();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Normalize Stored Source"))
                {
                    Undo.RecordObject(meshletRenderer, "Normalize Meshlet Renderer Source");
                    meshletRenderer.RefreshSource();
                    EditorUtility.SetDirty(meshletRenderer);
                    serializedObject.Update();
                }

                using (new EditorGUI.DisabledScope(meshletRenderer.sourceMesh == null))
                {
                    if (GUILayout.Button("Find Meshlet Assets"))
                    {
                        AssignMeshletCollections(
                            meshletRenderer,
                            MeshletRendererEditorUtility.CollectMeshletCollections(meshletRenderer.sourceMesh),
                            "Bind Meshlet Assets"
                        );
                    }
                }
            }

            using (new EditorGUI.DisabledScope(meshletRenderer.sourceMesh == null || !MeshletRendererEditorUtility.IsPersistentMesh(meshletRenderer.sourceMesh)))
            {
                if (GUILayout.Button("Generate Missing Assets"))
                {
                    string[] createdAssets = MeshletRendererEditorUtility.GenerateMissingMeshletCollections(meshletRenderer.sourceMesh);
                    AssignMeshletCollections(
                        meshletRenderer,
                        MeshletRendererEditorUtility.CollectMeshletCollections(meshletRenderer.sourceMesh),
                        "Generate Meshlet Assets"
                    );

                    if (createdAssets.Length > 0)
                    {
                        Selection.activeObject = AssetDatabase.LoadAssetAtPath<VividMeshletCollectionAsset>(createdAssets[^1]);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(meshletRenderer.sourceMesh == null))
                {
                    if (GUILayout.Button("Create/Bind GPUDriven Proxies"))
                    {
                        GPUDrivenMaterialProxyBindingResult bindingResult =
                            GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);

                        if (!bindingResult.Success && !string.IsNullOrEmpty(bindingResult.ErrorMessage))
                        {
                            Debug.LogWarning($"[VividRP] {bindingResult.ErrorMessage}", meshletRenderer);
                        }

                        LogWarnings(meshletRenderer, bindingResult.Warnings);
                        serializedObject.Update();
                    }

                    if (GUILayout.Button("Sync Proxies From Source Materials"))
                    {
                        GPUDrivenMaterialProxySyncResult syncResult =
                            GPUDrivenMaterialProxyEditorUtility.SyncMaterialProxiesFromSourceMaterials(meshletRenderer);

                        if (!syncResult.Success && !string.IsNullOrEmpty(syncResult.ErrorMessage))
                        {
                            Debug.LogWarning($"[VividRP] {syncResult.ErrorMessage}", meshletRenderer);
                        }

                        LogWarnings(meshletRenderer, syncResult.Warnings);
                        serializedObject.Update();
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!MeshletRendererEditorUtility.HasAttachedMeshRenderer(meshletRenderer)))
            {
                if (GUILayout.Button("Take Over And Remove MeshRenderer"))
                {
                    MeshletRendererSourceRendererDetachResult detachResult =
                        MeshletRendererEditorUtility.TakeOverAndRemoveSourceMeshRenderer(meshletRenderer);

                    if (!detachResult.Success && !string.IsNullOrEmpty(detachResult.ErrorMessage))
                    {
                        Debug.LogWarning($"[VividRP] {detachResult.ErrorMessage}", meshletRenderer);
                    }

                    LogWarnings(meshletRenderer, detachResult.Warnings);
                    SelectLastCreatedAsset(detachResult.CreatedMaterialProxyAssetPaths, detachResult.CreatedMeshletAssetPaths);
                    serializedObject.Update();
                }
            }
        }

        private void AssignMeshletCollections(
            MeshletRenderer meshletRenderer,
            VividMeshletCollectionAsset[] meshletCollections,
            string undoLabel
        )
        {
            if (meshletRenderer == null)
            {
                return;
            }

            Undo.RecordObject(meshletRenderer, undoLabel);
            meshletRenderer.SetMeshletCollections(meshletCollections);
            EditorUtility.SetDirty(meshletRenderer);
            serializedObject.Update();
        }

        private static void LogWarnings(UnityEngine.Object context, string[] warnings)
        {
            if (warnings == null)
            {
                return;
            }

            for (int warningIndex = 0; warningIndex < warnings.Length; warningIndex++)
            {
                Debug.LogWarning($"[VividRP] {warnings[warningIndex]}", context);
            }
        }

        private static void SelectLastCreatedAsset(params string[][] assetPathGroups)
        {
            if (assetPathGroups == null)
            {
                return;
            }

            for (int groupIndex = assetPathGroups.Length - 1; groupIndex >= 0; groupIndex--)
            {
                string[] assetPaths = assetPathGroups[groupIndex];
                if (assetPaths == null || assetPaths.Length == 0)
                {
                    continue;
                }

                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPaths[^1]);
                if (asset != null)
                {
                    Selection.activeObject = asset;
                }

                return;
            }
        }
    }

    internal static class MeshletRendererEditorUtility
    {
        internal static MeshletRendererTakeOverRepairResult RepairTakeOverBindings(MeshletRenderer meshletRenderer)
        {
            if (meshletRenderer == null)
            {
                return new MeshletRendererTakeOverRepairResult(false, false, "MeshletRenderer is null.", null, null, null);
            }

            bool changed = RefreshSource(meshletRenderer);
            Mesh sourceMesh = meshletRenderer.sourceMesh;
            if (sourceMesh == null)
            {
                return new MeshletRendererTakeOverRepairResult(
                    false,
                    changed,
                    "MeshletRenderer source Mesh is not captured. Run 'Take Over And Remove MeshRenderer' first.",
                    null,
                    null,
                    null
                );
            }

            int expectedCount = meshletRenderer.subMeshCount;
            var warnings = new List<string>();
            var createdMeshletAssetPaths = new List<string>();
            var resolvedMeshletCollections = new VividMeshletCollectionAsset[expectedCount];
            for (int subMeshIndex = 0; subMeshIndex < expectedCount; subMeshIndex++)
            {
                resolvedMeshletCollections[subMeshIndex] = meshletRenderer.GetMeshletCollection(subMeshIndex);
            }

            FillMissingMeshletCollections(sourceMesh, resolvedMeshletCollections);

            if (HasMissingEntries(resolvedMeshletCollections))
            {
                if (IsPersistentMesh(sourceMesh))
                {
                    string[] generatedAssetPaths = GenerateMissingMeshletCollections(sourceMesh);
                    if (generatedAssetPaths.Length > 0)
                    {
                        createdMeshletAssetPaths.AddRange(generatedAssetPaths);
                        changed = true;
                    }

                    FillMissingMeshletCollections(sourceMesh, resolvedMeshletCollections);
                }
                else
                {
                    warnings.Add("Source Mesh is not stored as an asset, so missing meshlet assets cannot be generated automatically.");
                }
            }

            Undo.RecordObject(meshletRenderer, "Repair Meshlet Renderer Takeover Bindings");
            if (meshletRenderer.SetMeshletCollections(resolvedMeshletCollections))
            {
                changed = true;
                EditorUtility.SetDirty(meshletRenderer);
            }

            GPUDrivenMaterialProxyBindingResult bindingResult =
                GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);
            warnings.AddRange(bindingResult.Warnings);

            if (!bindingResult.Success)
            {
                return new MeshletRendererTakeOverRepairResult(
                    false,
                    changed,
                    bindingResult.ErrorMessage,
                    createdMeshletAssetPaths.ToArray(),
                    bindingResult.CreatedAssetPaths,
                    warnings.ToArray()
                );
            }

            if (bindingResult.CreatedAssetPaths.Length > 0)
            {
                changed = true;
            }

            GPUDrivenMaterialProxySyncResult syncResult =
                GPUDrivenMaterialProxyEditorUtility.SyncMaterialProxiesFromSourceMaterials(meshletRenderer);
            warnings.AddRange(syncResult.Warnings);

            if (!syncResult.Success)
            {
                return new MeshletRendererTakeOverRepairResult(
                    false,
                    changed || syncResult.Changed,
                    syncResult.ErrorMessage,
                    createdMeshletAssetPaths.ToArray(),
                    bindingResult.CreatedAssetPaths,
                    warnings.ToArray()
                );
            }

            changed |= syncResult.Changed;
            VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

            if (!meshletRenderer.TryValidate(out string validationMessage))
            {
                return new MeshletRendererTakeOverRepairResult(
                    false,
                    changed,
                    validationMessage,
                    createdMeshletAssetPaths.ToArray(),
                    bindingResult.CreatedAssetPaths,
                    warnings.ToArray()
                );
            }

            return new MeshletRendererTakeOverRepairResult(
                true,
                changed,
                string.Empty,
                createdMeshletAssetPaths.ToArray(),
                bindingResult.CreatedAssetPaths,
                warnings.ToArray()
            );
        }

        internal static MeshletRendererSourceRendererDetachResult TakeOverAndRemoveSourceMeshRenderer(MeshletRenderer meshletRenderer)
        {
            if (meshletRenderer == null)
            {
                return new MeshletRendererSourceRendererDetachResult(false, false, "MeshletRenderer is null.", null, null, null);
            }

            if (!TryGetAttachedMeshRenderer(meshletRenderer, out MeshRenderer meshRenderer))
            {
                return new MeshletRendererSourceRendererDetachResult(
                    false,
                    false,
                    "No MeshRenderer with a valid MeshFilter is attached to this GameObject.",
                    null,
                    null,
                    null
                );
            }

            Undo.RecordObject(meshletRenderer, "Capture Meshlet Renderer Source");
            bool changed = meshletRenderer.CaptureSourceFromRenderer(meshRenderer);
            changed |= meshletRenderer.SetTakeOverSourceRenderer(true);
            if (changed)
            {
                EditorUtility.SetDirty(meshletRenderer);
            }

            MeshletRendererTakeOverRepairResult repairResult = RepairTakeOverBindings(meshletRenderer);
            if (!repairResult.Success)
            {
                return new MeshletRendererSourceRendererDetachResult(
                    false,
                    changed || repairResult.Changed,
                    repairResult.ErrorMessage,
                    repairResult.CreatedMeshletAssetPaths,
                    repairResult.CreatedMaterialProxyAssetPaths,
                    repairResult.Warnings
                );
            }

            Undo.DestroyObjectImmediate(meshRenderer);
            RefreshSource(meshletRenderer);
            VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

            if (!meshletRenderer.TryValidate(out string validationMessage))
            {
                return new MeshletRendererSourceRendererDetachResult(
                    false,
                    true,
                    validationMessage,
                    repairResult.CreatedMeshletAssetPaths,
                    repairResult.CreatedMaterialProxyAssetPaths,
                    repairResult.Warnings
                );
            }

            return new MeshletRendererSourceRendererDetachResult(
                true,
                changed || repairResult.Changed,
                string.Empty,
                repairResult.CreatedMeshletAssetPaths,
                repairResult.CreatedMaterialProxyAssetPaths,
                repairResult.Warnings
            );
        }

        internal static VividMeshletCollectionAsset[] CollectMeshletCollections(Mesh mesh)
        {
            if (!TryGetMeshAssetKey(mesh, out string meshGuid, out long meshLocalFileId, out string meshName, out string folderPath))
            {
                return Array.Empty<VividMeshletCollectionAsset>();
            }

            int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            var meshletCollections = new VividMeshletCollectionAsset[subMeshCount];

            string[] assetGuids = AssetDatabase.FindAssets($"t:{nameof(VividMeshletCollectionAsset)}", new[] { folderPath });
            Array.Sort(assetGuids, StringComparer.Ordinal);
            foreach (string assetGuid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                VividMeshletCollectionAsset meshletCollection = AssetDatabase.LoadAssetAtPath<VividMeshletCollectionAsset>(assetPath);
                if (meshletCollection == null || !MatchesMesh(meshletCollection, meshGuid, meshLocalFileId, meshName))
                {
                    continue;
                }

                int subMeshIndex = meshletCollection.SourceSubmeshIndex;
                if (subMeshIndex < 0 || subMeshIndex >= meshletCollections.Length || meshletCollections[subMeshIndex] != null)
                {
                    continue;
                }

                meshletCollections[subMeshIndex] = meshletCollection;
            }

            return meshletCollections;
        }

        internal static string[] GenerateMissingMeshletCollections(Mesh mesh)
        {
            if (!TryGetMeshAssetKey(mesh, out _, out _, out _, out _))
            {
                return Array.Empty<string>();
            }

            VividMeshletCollectionAsset[] existingCollections = CollectMeshletCollections(mesh);
            int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            var createdAssetPaths = new List<string>(subMeshCount);

            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                if (existingCollections[subMeshIndex] != null)
                {
                    continue;
                }

                string assetPath = VividMeshletCollectionAssetImporter.CreateAssetForMesh(mesh, subMeshIndex);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    createdAssetPaths.Add(assetPath);
                }
            }

            return createdAssetPaths.ToArray();
        }

        internal static bool IsPersistentMesh(Mesh mesh)
        {
            return TryGetMeshAssetKey(mesh, out _, out _, out _, out _);
        }

        internal static bool HasAttachedMeshRenderer(MeshletRenderer meshletRenderer)
        {
            return TryGetAttachedMeshRenderer(meshletRenderer, out _);
        }

        private static bool RefreshSource(MeshletRenderer meshletRenderer)
        {
            Undo.RecordObject(meshletRenderer, "Normalize Meshlet Renderer Source");
            bool changed = meshletRenderer.RefreshSource();
            if (changed)
            {
                EditorUtility.SetDirty(meshletRenderer);
            }

            return changed;
        }

        private static void FillMissingMeshletCollections(
            Mesh sourceMesh,
            VividMeshletCollectionAsset[] resolvedMeshletCollections)
        {
            if (sourceMesh == null || resolvedMeshletCollections == null || resolvedMeshletCollections.Length == 0)
            {
                return;
            }

            VividMeshletCollectionAsset[] collectedMeshletCollections = CollectMeshletCollections(sourceMesh);
            int count = Mathf.Min(resolvedMeshletCollections.Length, collectedMeshletCollections.Length);
            for (int subMeshIndex = 0; subMeshIndex < count; subMeshIndex++)
            {
                if (resolvedMeshletCollections[subMeshIndex] == null && collectedMeshletCollections[subMeshIndex] != null)
                {
                    resolvedMeshletCollections[subMeshIndex] = collectedMeshletCollections[subMeshIndex];
                }
            }
        }

        private static bool HasMissingEntries<T>(T[] values)
            where T : UnityEngine.Object
        {
            if (values == null)
            {
                return true;
            }

            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetMeshAssetKey(
            Mesh mesh,
            out string meshGuid,
            out long meshLocalFileId,
            out string meshName,
            out string folderPath
        )
        {
            meshGuid = string.Empty;
            meshLocalFileId = 0L;
            meshName = string.Empty;
            folderPath = string.Empty;

            if (mesh == null)
            {
                return false;
            }

            string meshPath = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(meshPath))
            {
                return false;
            }

            meshName = mesh.name;
            folderPath = File.Exists(meshPath) ? Path.GetDirectoryName(meshPath) ?? "Assets" : meshPath;
            meshGuid = AssetDatabase.AssetPathToGUID(meshPath);

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string resolvedGuid, out long resolvedLocalFileId))
            {
                if (!string.IsNullOrEmpty(resolvedGuid))
                {
                    meshGuid = resolvedGuid;
                }

                meshLocalFileId = resolvedLocalFileId;
            }

            return !string.IsNullOrEmpty(meshGuid);
        }

        private static bool MatchesMesh(
            VividMeshletCollectionAsset meshletCollection,
            string meshGuid,
            long meshLocalFileId,
            string meshName
        )
        {
            if (meshletCollection == null || !string.Equals(meshletCollection.SourceMeshGUID, meshGuid, StringComparison.Ordinal))
            {
                return false;
            }

            if (meshLocalFileId != 0L && meshletCollection.SourceMeshLocalFileID != 0L)
            {
                return meshletCollection.SourceMeshLocalFileID == meshLocalFileId;
            }

            return string.Equals(meshletCollection.SourceMeshName, meshName, StringComparison.Ordinal);
        }

        private static bool TryGetAttachedMeshRenderer(MeshletRenderer meshletRenderer, out MeshRenderer meshRenderer)
        {
            meshRenderer = null;
            if (meshletRenderer == null)
            {
                return false;
            }

            if (!meshletRenderer.TryGetComponent(out meshRenderer))
            {
                meshRenderer = null;
                return false;
            }

            return MeshletRenderer.TryExtractMesh(meshRenderer, out _);
        }
    }
}
