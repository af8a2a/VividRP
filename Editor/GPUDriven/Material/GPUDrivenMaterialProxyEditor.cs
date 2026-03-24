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
                    GPUDrivenMaterialProxySyncResult syncResult = GPUDrivenMaterialProxySyncUtility.SyncFromSourceMaterial(materialProxy);
                    if (!syncResult.Success)
                    {
                        Debug.LogWarning($"[VividRP] Failed to sync GPUDriven material proxy '{materialProxy.name}': {syncResult.ErrorMessage}", materialProxy);
                    }

                    LogWarnings(materialProxy, syncResult.Warnings);
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

            string[] warnings = GPUDrivenMaterialProxySyncUtility.CollectUnsupportedWarnings(materialProxy.SourceMaterial);
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
