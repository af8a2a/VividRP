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
    [CustomEditor(typeof(MeshletRenderer))]
    internal sealed class MeshletRendererEditor : UnityEditor.Editor
    {
        private SerializedProperty m_SourceRenderer;
        private SerializedProperty m_SourceMesh;
        private SerializedProperty m_MeshletCollections;

        private void OnEnable()
        {
            m_SourceRenderer = serializedObject.FindProperty("m_SourceRenderer");
            m_SourceMesh = serializedObject.FindProperty("m_SourceMesh");
            m_MeshletCollections = serializedObject.FindProperty("m_MeshletCollections");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_SourceRenderer);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(m_SourceMesh);
            }

            EditorGUILayout.PropertyField(m_MeshletCollections, true);

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

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Source"))
                {
                    Undo.RecordObject(meshletRenderer, "Refresh Meshlet Renderer Source");
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
    }

    internal static class MeshletRendererEditorUtility
    {
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
    }
}
