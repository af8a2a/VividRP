using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CustomEditor(typeof(VividLocalVolumetricFog))]
    [CanEditMultipleObjects]
    internal sealed class VividLocalVolumetricFogEditor : UnityEditor.Editor
    {
        private static readonly Color s_GizmoColor = new(0.23f, 0.73f, 0.67f, 0.08f);

        private SerializedProperty m_BoundProxy;
        private SerializedProperty m_Parameters;
        private SerializedBoundProxyShape m_SerializedShape;

        private void OnEnable()
        {
            m_BoundProxy = serializedObject.FindProperty("m_BoundProxy");
            m_Parameters = serializedObject.FindProperty("m_Parameters");
            m_SerializedShape = new SerializedBoundProxyShape(m_BoundProxy);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ForceBoxShape(m_SerializedShape);

            EditorGUILayout.LabelField("Bounds", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_SerializedShape.center);
            EditorGUILayout.PropertyField(m_SerializedShape.size);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fog", EditorStyles.boldLabel);
            DrawParameters();

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            var fog = (VividLocalVolumetricFog)target;
            if (fog == null)
                return;

            var so = new SerializedObject(target);
            var shapeProp = so.FindProperty("m_BoundProxy");
            var shape = new SerializedBoundProxyShape(shapeProp);
            so.Update();
            ForceBoxShape(shape);
            so.ApplyModifiedProperties();

            BoundProxyEditorUtility.DrawSceneHandles(
                so,
                shape,
                fog.transform,
                undoLabel: "Edit Local Volumetric Fog Bounds",
                allowCenterHandle: true);
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void DrawGizmosSelected(VividLocalVolumetricFog fog, GizmoType gizmoType)
        {
            BoundProxyEditorUtility.DrawGizmo(fog.transform, fog.BoundProxyShape, filled: true, s_GizmoColor);
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawGizmosNonSelected(VividLocalVolumetricFog fog, GizmoType gizmoType)
        {
            BoundProxyEditorUtility.DrawGizmo(fog.transform, fog.BoundProxyShape, filled: false, s_GizmoColor * 0.5f);
        }

        private void DrawParameters()
        {
            DrawParameter("albedo");
            DrawParameter("meanFreePath");
            DrawParameter("blendingMode");
            DrawParameter("priority");
            DrawParameter("anisotropy");
            DrawParameter("maskMode");
            DrawParameter("volumeMask");
            DrawParameter("materialMask");
            DrawParameter("textureScrollingSpeed");
            DrawParameter("textureTiling");
            DrawParameter("textureOffset");
            DrawParameter("positiveFade");
            DrawParameter("negativeFade");
            DrawParameter("invertFade");
            DrawParameter("distanceFadeStart");
            DrawParameter("distanceFadeEnd");
            DrawParameter("falloffMode");
        }

        private void DrawParameter(string propertyName)
        {
            SerializedProperty property = m_Parameters.FindPropertyRelative(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property);
            }
        }

        private static void ForceBoxShape(SerializedBoundProxyShape shape)
        {
            if (shape == null)
                return;

            shape.shape.intValue = (int)BoundProxyShapeType.Box;
            shape.radius.floatValue = 0.0f;
        }
    }
}
