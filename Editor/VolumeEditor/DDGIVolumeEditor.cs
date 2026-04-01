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
        private static readonly GUIContent s_BlendDistanceLabel =
            EditorGUIUtility.TrTextContent("Blend Distance");

        private static readonly Color SelectedGizmoColor = new(0.28f, 0.72f, 1.0f, 0.10f);
        private static readonly Color NonSelectedGizmoColor = new(0.28f, 0.72f, 1.0f, 0.05f);
        private static readonly Color SelectedBlendGizmoColor = new(1.0f, 0.76f, 0.30f, 0.9f);
        private static readonly Color NonSelectedBlendGizmoColor = new(1.0f, 0.76f, 0.30f, 0.65f);

        private SerializedBoundProxyShape m_SerializedBoundProxy;
        private SerializedProperty m_BlendDistance;

        private void OnEnable()
        {
            SerializedProperty boundProxyProperty = serializedObject.FindProperty("m_BoundProxy");
            m_SerializedBoundProxy = boundProxyProperty != null
                ? new SerializedBoundProxyShape(boundProxyProperty)
                : null;
            m_BlendDistance = serializedObject.FindProperty("m_BlendDistance");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (m_SerializedBoundProxy == null || m_BlendDistance == null)
            {
                EditorGUILayout.HelpBox("Unable to bind DDGI volume bound proxy data.", MessageType.Error);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.LabelField(s_BoundProxyLabel, EditorStyles.boldLabel);
            BoundProxyEditorUtility.DrawInspector(m_SerializedBoundProxy, showCenter: false);
            EditorGUILayout.PropertyField(m_BlendDistance, s_BlendDistanceLabel);

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            if (target is not DDGIVolume ddgiVolume)
            {
                return;
            }

            if (ddgiVolume.BlendDistance > 0.0f)
            {
                BoundProxyEditorUtility.DrawGizmo(
                    ddgiVolume.transform,
                    ddgiVolume.BlendInnerBoundProxyShape,
                    filled: false,
                    baseColor: SelectedBlendGizmoColor,
                    applyOwnerRotation: false);
            }

            if (!BoundProxyEditorUtility.TryDrawSceneHandles(
                    ddgiVolume.BoundProxyShape,
                    ddgiVolume.transform,
                    out BoundProxyShape updatedShape,
                    allowCenterHandle: false,
                    applyOwnerRotation: false))
            {
                return;
            }

            Undo.RecordObject(ddgiVolume, "Edit DDGI Volume Bound Proxy");
            ddgiVolume.SetBoundProxyShape(updatedShape);
            PrefabUtility.RecordPrefabInstancePropertyModifications(ddgiVolume);
            EditorUtility.SetDirty(ddgiVolume);
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
            if (ddgiVolume.BlendDistance > 0.0f)
            {
                Color blendColor = filled ? SelectedBlendGizmoColor : NonSelectedBlendGizmoColor;
                BoundProxyEditorUtility.DrawGizmo(
                    ddgiVolume.transform,
                    ddgiVolume.BlendInnerBoundProxyShape,
                    filled: false,
                    baseColor: blendColor,
                    applyOwnerRotation: false);
            }

            BoundProxyEditorUtility.DrawGizmo(
                ddgiVolume.transform,
                ddgiVolume.BoundProxyShape,
                filled,
                baseColor,
                applyOwnerRotation: false);
        }
    }
}
