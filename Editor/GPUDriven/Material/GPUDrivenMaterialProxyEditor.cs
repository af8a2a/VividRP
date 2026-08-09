using UnityEditor;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    [CustomEditor(typeof(GPUDrivenMaterialProxy))]
    internal sealed class GPUDrivenMaterialProxyEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, "m_Script", "m_Revision");
            serializedObject.ApplyModifiedProperties();

            var materialProxy = (GPUDrivenMaterialProxy) target;

            DrawWarnings(materialProxy);

            using (new EditorGUI.DisabledScope(materialProxy.SourceMaterial == null))
            {
                if (GUILayout.Button("Sync From Source Material"))
                {
                    GPUDrivenMaterialProxySyncResult syncResult = materialProxy.SyncFromSourceMaterial();
                    if (!syncResult.Success)
                    {
                        Debug.LogWarning($"[VividRP] Failed to sync GPUDriven material proxy '{materialProxy.name}': {syncResult.ErrorMessage}", materialProxy);
                    }
                    else if (AssetDatabase.Contains(materialProxy)
                             && !GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                                 materialProxy,
                                 out _,
                                 out _,
                                 out string streamError))
                    {
                        Debug.LogWarning(
                            $"[VividRP] Failed to refresh streamed VT for '{materialProxy.name}': {streamError}",
                            materialProxy);
                    }

                    LogWarnings(materialProxy, syncResult.Warnings);
                }
            }

            using (new EditorGUI.DisabledScope(
                       EditorApplication.isCompiling
                       || (materialProxy.BaseMap == null && materialProxy.BumpMap == null && materialProxy.MaskMap == null)))
            {
                if (GUILayout.Button("Build / Refresh Streamed VT Asset"))
                {
                    if (!GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                            materialProxy,
                            out string assetPath,
                            out _,
                            out string errorMessage))
                    {
                        Debug.LogWarning(
                            $"[VividRP] Failed to build streamed VT for '{materialProxy.name}': {errorMessage}",
                            materialProxy);
                    }
                    else if (!string.IsNullOrEmpty(assetPath))
                    {
                        Debug.Log($"[VividRP] Built GPUDriven streamed VT asset '{assetPath}'.", materialProxy);
                    }
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LongField("Revision", materialProxy.Revision);
            }
        }

        private static void DrawWarnings(GPUDrivenMaterialProxy materialProxy)
        {
            if (materialProxy == null)
            {
                return;
            }

            if (materialProxy.SourceMaterial == null)
            {
                EditorGUILayout.HelpBox("Assign a source Material to enable one-click synchronization.", MessageType.Info);
                return;
            }

            string[] warnings = materialProxy.SourceMaterial.CollectUnsupportedWarnings();
            for (int warningIndex = 0; warningIndex < warnings.Length; warningIndex++)
            {
                EditorGUILayout.HelpBox(warnings[warningIndex], MessageType.Warning);
            }
        }

        private static void LogWarnings(GPUDrivenMaterialProxy materialProxy, string[] warnings)
        {
            if (warnings == null)
            {
                return;
            }

            for (int warningIndex = 0; warningIndex < warnings.Length; warningIndex++)
            {
                Debug.LogWarning($"[VividRP] {warnings[warningIndex]}", materialProxy);
            }
        }
    }
}
