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
        private static readonly GUIContent s_ProfileLabel =
            EditorGUIUtility.TrTextContent("Profile");
        private static readonly GUIContent s_ProbeSpacingLabel =
            EditorGUIUtility.TrTextContent("Probe Spacing");
        private static readonly GUIContent s_ProbeNormalBiasLabel =
            EditorGUIUtility.TrTextContent("Probe Normal Bias");
        private static readonly GUIContent s_ProbeViewBiasLabel =
            EditorGUIUtility.TrTextContent("Probe View Bias");
        private static readonly GUIContent s_ProbeMaxRayDistanceLabel =
            EditorGUIUtility.TrTextContent("Probe Max Ray Distance");
        private static readonly GUIContent s_ProbeCountsLabel =
            EditorGUIUtility.TrTextContent("Probe Counts");
        private const string ProbePreviewInfo =
            "Select this DDGI volume in Scene view to preview probe placement with indirect-drawn spheres.";
        private const string SphereWarning =
            "Sphere DDGI volumes remain editor-visible in v1, but runtime DDGI registration only supports box volumes.";

        private static readonly Color SelectedGizmoColor = new(0.28f, 0.72f, 1.0f, 0.10f);
        private static readonly Color NonSelectedGizmoColor = new(0.28f, 0.72f, 1.0f, 0.05f);
        private static readonly Color SelectedBlendGizmoColor = new(1.0f, 0.76f, 0.30f, 0.9f);
        private static readonly Color NonSelectedBlendGizmoColor = new(1.0f, 0.76f, 0.30f, 0.65f);

        private SerializedBoundProxyShape m_SerializedBoundProxy;
        private SerializedProperty m_BlendDistance;
        private SerializedProperty m_Profile;
        private SerializedProperty m_ProbeSpacing;
        private SerializedProperty m_ProbeNormalBias;
        private SerializedProperty m_ProbeViewBias;
        private SerializedProperty m_ProbeMaxRayDistance;

        private void OnEnable()
        {
            SerializedProperty boundProxyProperty = serializedObject.FindProperty("m_BoundProxy");
            m_SerializedBoundProxy = boundProxyProperty != null
                ? new SerializedBoundProxyShape(boundProxyProperty)
                : null;
            m_BlendDistance = serializedObject.FindProperty("m_BlendDistance");
            m_Profile = serializedObject.FindProperty("m_Profile");
            m_ProbeSpacing = serializedObject.FindProperty("m_ProbeSpacing");
            m_ProbeNormalBias = serializedObject.FindProperty("m_ProbeNormalBias");
            m_ProbeViewBias = serializedObject.FindProperty("m_ProbeViewBias");
            m_ProbeMaxRayDistance = serializedObject.FindProperty("m_ProbeMaxRayDistance");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (m_SerializedBoundProxy == null
                || m_BlendDistance == null
                || m_Profile == null
                || m_ProbeSpacing == null
                || m_ProbeNormalBias == null
                || m_ProbeViewBias == null
                || m_ProbeMaxRayDistance == null)
            {
                EditorGUILayout.HelpBox("Unable to bind DDGI volume bound proxy data.", MessageType.Error);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.LabelField(s_BoundProxyLabel, EditorStyles.boldLabel);
            BoundProxyEditorUtility.DrawInspector(m_SerializedBoundProxy, showCenter: false);
            EditorGUILayout.PropertyField(m_BlendDistance, s_BlendDistanceLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Probe Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Profile, s_ProfileLabel);
            EditorGUILayout.PropertyField(m_ProbeSpacing, s_ProbeSpacingLabel);
            EditorGUILayout.PropertyField(m_ProbeNormalBias, s_ProbeNormalBiasLabel);
            EditorGUILayout.PropertyField(m_ProbeViewBias, s_ProbeViewBiasLabel);
            EditorGUILayout.PropertyField(m_ProbeMaxRayDistance, s_ProbeMaxRayDistanceLabel);

            if ((BoundProxyShapeType)m_SerializedBoundProxy.shape.intValue == BoundProxyShapeType.Sphere)
            {
                EditorGUILayout.HelpBox(SphereWarning, MessageType.Warning);
            }

            if (target is DDGIVolume ddgiVolume)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Vector3IntField(s_ProbeCountsLabel, ddgiVolume.ProbeCounts);
                }
            }

            EditorGUILayout.HelpBox(ProbePreviewInfo, MessageType.Info);

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
