using UnityEditor;
using UnityEngine;
using VividRP.Runtime.Particle;

namespace VividRP.Editor
{
    [CustomEditor(typeof(VividParticleForceField))]
    [CanEditMultipleObjects]
    internal sealed class VividParticleForceFieldEditor : UnityEditor.Editor
    {
        private static readonly Color s_EndRangeColor = new(0.25f, 0.8f, 1.0f, 0.85f);
        private static readonly Color s_StartRangeColor = new(1.0f, 0.72f, 0.2f, 0.85f);
        private static readonly Color s_LengthColor = new(0.55f, 1.0f, 0.45f, 0.85f);

        private SerializedProperty m_Shape;
        private SerializedProperty m_StartRange;
        private SerializedProperty m_EndRange;
        private SerializedProperty m_Length;

        private void OnEnable()
        {
            m_Shape = serializedObject.FindProperty("m_Shape");
            m_StartRange = serializedObject.FindProperty("m_StartRange");
            m_EndRange = serializedObject.FindProperty("m_EndRange");
            m_Length = serializedObject.FindProperty("m_Length");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_Shape);
            EditorGUILayout.PropertyField(m_StartRange);
            EditorGUILayout.PropertyField(m_EndRange);
            if (m_Shape != null
                && !m_Shape.hasMultipleDifferentValues
                && (VividParticleForceFieldShape)m_Shape.enumValueIndex
                    == VividParticleForceFieldShape.Cylinder)
            {
                EditorGUILayout.PropertyField(m_Length);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_DirectionX"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_DirectionY"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_DirectionZ"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Gravity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_GravityFocus"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_RotationSpeed"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_RotationAttraction"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_RotationRandomness"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Drag"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_MultiplyDragByParticleSize"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_MultiplyDragByParticleVelocity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_VectorField"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_VectorFieldSpeed"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_VectorFieldAttraction"));
            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            if (target is not VividParticleForceField field)
                return;

            Transform fieldTransform = field.transform;
            using (new Handles.DrawingScope(fieldTransform.localToWorldMatrix))
            {
                EditorGUI.BeginChangeCheck();
                Handles.color = s_EndRangeColor;
                float endRange = DrawRangeHandle(field.shape, field.endRange, field.length);
                Handles.color = s_StartRangeColor;
                float startRange = DrawRangeHandle(field.shape, field.startRange, field.length);
                float length = field.shape == VividParticleForceFieldShape.Cylinder
                    ? DrawLengthHandle(field.length)
                    : field.length;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(field, "Edit Vivid Particle Force Field Bounds");
                    ResolveRanges(startRange, endRange, out startRange, out endRange);
                    field.startRange = startRange;
                    field.endRange = endRange;
                    field.length = length;
                    EditorUtility.SetDirty(field);
                }

                Handles.color = s_StartRangeColor;
                DrawWireShape(field.shape, field.startRange, field.length);
                Handles.color = s_EndRangeColor;
                DrawWireShape(field.shape, field.endRange, field.length);
            }
        }

        internal static void ResolveRanges(
            float startRange,
            float endRange,
            out float resolvedStartRange,
            out float resolvedEndRange)
        {
            resolvedStartRange = Mathf.Max(0.0f, startRange);
            resolvedEndRange = Mathf.Max(resolvedStartRange, endRange);
        }

        internal static float ResolveLength(float length)
        {
            return Mathf.Max(0.0f, length);
        }

        private static float DrawRangeHandle(
            VividParticleForceFieldShape shape,
            float range,
            float length)
        {
            if (shape == VividParticleForceFieldShape.Box)
            {
                Vector3 size = Vector3.one * range * 2.0f;
                size = Handles.ScaleHandle(size, Vector3.zero, Quaternion.identity, range);
                return Mathf.Max(0.0f, Mathf.Max(size.x, size.y, size.z) * 0.5f);
            }

            if (shape == VividParticleForceFieldShape.Cylinder)
            {
                return Mathf.Max(
                    0.0f,
                    Handles.RadiusHandle(Quaternion.Euler(90.0f, 0.0f, 0.0f), Vector3.zero, range));
            }

            return Mathf.Max(0.0f, Handles.RadiusHandle(Quaternion.identity, Vector3.zero, range));
        }

        private static float DrawLengthHandle(float length)
        {
            Handles.color = s_LengthColor;
            float halfLength = Mathf.Max(0.0f, length) * 0.5f;
            Vector3 worldHandlePosition = Handles.matrix.MultiplyPoint3x4(Vector3.up * halfLength);
            float handleSize = HandleUtility.GetHandleSize(worldHandlePosition) * 0.12f;
            Vector3 positive = Handles.Slider(
                Vector3.up * halfLength,
                Vector3.up,
                handleSize,
                Handles.ConeHandleCap,
                0.0f);
            Handles.DrawLine(Vector3.down * halfLength, Vector3.up * halfLength);
            return ResolveLength(Mathf.Max(0.0f, positive.y) * 2.0f);
        }

        private static void DrawWireShape(
            VividParticleForceFieldShape shape,
            float range,
            float length)
        {
            switch (shape)
            {
                case VividParticleForceFieldShape.Box:
                    Handles.DrawWireCube(Vector3.zero, Vector3.one * range * 2.0f);
                    break;
                case VividParticleForceFieldShape.Cylinder:
                    float halfLength = length * 0.5f;
                    Handles.DrawWireDisc(Vector3.up * halfLength, Vector3.up, range);
                    Handles.DrawWireDisc(Vector3.down * halfLength, Vector3.up, range);
                    break;
                case VividParticleForceFieldShape.Hemisphere:
                    Handles.DrawWireArc(Vector3.zero, Vector3.forward, Vector3.right, 180.0f, range);
                    Handles.DrawWireDisc(Vector3.zero, Vector3.up, range);
                    break;
                default:
                    Handles.DrawWireDisc(Vector3.zero, Vector3.up, range);
                    Handles.DrawWireDisc(Vector3.zero, Vector3.right, range);
                    Handles.DrawWireDisc(Vector3.zero, Vector3.forward, range);
                    break;
            }
        }
    }
}
