using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(DDGIVolume))]
    internal sealed class DDGIVolumeEditor : UnityEditor.Editor
    {
        private static readonly GUIContent s_BoundProxyLabel =
            EditorGUIUtility.TrTextContent("Bound Proxy");

        private static readonly Color SelectedGizmoColor = new(0.28f, 0.72f, 1.0f, 0.10f);
        private static readonly Color NonSelectedGizmoColor = new(0.28f, 0.72f, 1.0f, 0.05f);

        private SerializedBoundProxyShape m_SerializedBoundProxy;

        private void OnEnable()
        {
            SerializedProperty boundProxyProperty = serializedObject.FindProperty("m_BoundProxy");
            m_SerializedBoundProxy = boundProxyProperty != null
                ? new SerializedBoundProxyShape(boundProxyProperty)
                : null;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (m_SerializedBoundProxy == null)
            {
                EditorGUILayout.HelpBox("Unable to bind DDGI volume bound proxy data.", MessageType.Error);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.LabelField(s_BoundProxyLabel, EditorStyles.boldLabel);
            BoundProxyEditorUtility.DrawInspector(m_SerializedBoundProxy);

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            if (target is not DDGIVolume ddgiVolume || m_SerializedBoundProxy == null)
            {
                return;
            }

            BoundProxyEditorUtility.DrawSceneHandles(
                serializedObject,
                m_SerializedBoundProxy,
                ddgiVolume.transform,
                "Edit DDGI Volume Bound Proxy",
                allowCenterHandle: false);
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.InSelectionHierarchy | GizmoType.Pickable)]
        private static void DrawDDGIVolumeGizmo(DDGIVolume ddgiVolume, GizmoType gizmoType)
        {
            if (ddgiVolume == null || !ddgiVolume.isActiveAndEnabled)
            {
                return;
            }

            bool filled = (gizmoType & (GizmoType.Selected | GizmoType.InSelectionHierarchy)) != 0;
            Color baseColor = filled ? SelectedGizmoColor : NonSelectedGizmoColor;
            BoundProxyEditorUtility.DrawGizmo(
                ddgiVolume.transform,
                ddgiVolume.BoundProxyShape,
                filled,
                baseColor);
        }
    }
}
