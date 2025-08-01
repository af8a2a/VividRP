using UnityEngine;
using UnityEditorInternal;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(LocalVolumetricFog))]
    class LocalVolumetricFogEditor : Editor
    {
        internal const EditMode.SceneViewEditMode k_EditShape = EditMode.SceneViewEditMode.ReflectionProbeBox;
        internal const EditMode.SceneViewEditMode k_EditBlend = EditMode.SceneViewEditMode.GridBox;

        Editor m_MaterialEditor;

        static HierarchicalBox s_ShapeBox;
        static HierarchicalBox s_BlendBox;

        SerializedLocalVolumetricFog m_SerializedLocalVolumetricFog;

        private void OnEnable()
        {
            m_SerializedLocalVolumetricFog = new SerializedLocalVolumetricFog(serializedObject);

            if (s_ShapeBox == null || s_ShapeBox.Equals(null))
            {
                s_ShapeBox = new HierarchicalBox(LocalVolumetricFogUI.Styles.k_GizmoColorBase,
                    LocalVolumetricFogUI.Styles.k_BaseHandlesColor);
                s_ShapeBox.monoHandle = false;
            }

            if (s_BlendBox == null || s_BlendBox.Equals(null))
            {
                s_BlendBox = new HierarchicalBox(LocalVolumetricFogUI.Styles.k_GizmoColorBase,
                    LocalVolumetricFogUI.Styles.k_BaseHandlesColor);
            }
        }

        private void OnDisable()
        {
            CoreUtils.Destroy(m_MaterialEditor);
        }
        
        public override void OnInspectorGUI() {
            serializedObject.Update();

            LocalVolumetricFogUI.Inspector.Draw(m_SerializedLocalVolumetricFog, this);

            m_SerializedLocalVolumetricFog.Apply();

            if ((LocalVolumetricFogMode)m_SerializedLocalVolumetricFog.fogMode.intValue == LocalVolumetricFogMode.Material
                && m_SerializedLocalVolumetricFog.fogMaterial.objectReferenceValue is Material mat) {
                if (m_MaterialEditor == null || m_MaterialEditor.target != mat) {
                    Editor.CreateCachedEditor(mat, typeof(MaterialEditor), ref m_MaterialEditor);
                }

                using (new EditorGUI.DisabledScope((mat.hideFlags & HideFlags.NotEditable) != 0)) {
                    m_MaterialEditor.DrawHeader();
                    m_MaterialEditor.OnInspectorGUI();
                }
            }
        }


        static Vector3 CenterBlendLocalPosition(LocalVolumetricFog localVolumetricFog)
        {
            return Vector3.zero;
        }

        static Vector3 BlendSize(LocalVolumetricFog localVolumetricFog)
        {
            Vector3 size = localVolumetricFog.parameters.size;
            return size - localVolumetricFog.parameters.m_EditorUniformFade * 2f * Vector3.one;
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        static void DrawGizmosSelected(LocalVolumetricFog fog, GizmoType gizmoType)
        {
            if (s_BlendBox == null || s_BlendBox.Equals(null)
                                   || s_ShapeBox == null || s_ShapeBox.Equals(null)) return;

            using (new Handles.DrawingScope(Matrix4x4.TRS(fog.transform.position, fog.transform.rotation, Vector3.one)))
            {
                s_BlendBox.center = CenterBlendLocalPosition(fog);
                s_BlendBox.size = BlendSize(fog);
                Color baseColor = fog.parameters.albedo;
                baseColor.a = 8 / 255f;
                s_BlendBox.baseColor = baseColor;
                s_BlendBox.DrawHull(EditMode.editMode == k_EditBlend);

                s_ShapeBox.center = Vector3.zero;
                s_ShapeBox.size = fog.parameters.size;
                s_ShapeBox.DrawHull(EditMode.editMode == k_EditShape);
            }
        }

        private void OnSceneGUI()
        {
            LocalVolumetricFog fog = target as LocalVolumetricFog;
            var trans = fog.transform;
            switch (EditMode.editMode)
            {
                case k_EditBlend:
                    using (new Handles.DrawingScope(Matrix4x4.TRS(trans.position, trans.rotation, Vector3.one)))
                    {
                        s_ShapeBox.center = Vector3.zero;
                        s_ShapeBox.size = fog.parameters.size;

                        Color baseColor = fog.parameters.albedo;
                        baseColor.a = 8 / 255f;
                        s_BlendBox.baseColor = baseColor;
                        s_BlendBox.center = CenterBlendLocalPosition(fog);
                        s_BlendBox.size = BlendSize(fog);
                        EditorGUI.BeginChangeCheck();
                        s_BlendBox.DrawHandle();
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(fog, L10n.Tr("Change Local Volumetric Fog Blend"));

                            float uniformDistance = (s_ShapeBox.size.x - s_BlendBox.size.x) * 0.5f;
                            float max = Mathf.Min(s_ShapeBox.size.x, s_ShapeBox.size.y, s_ShapeBox.size.z) * 0.5f;
                            fog.parameters.m_EditorUniformFade = Mathf.Clamp(uniformDistance, 0f, max);
                        }
                    }

                    break;
                case k_EditShape:
                    using (new Handles.DrawingScope(Matrix4x4.TRS(Vector3.zero, trans.rotation, Vector3.one)))
                    {
                        s_ShapeBox.center = Quaternion.Inverse(trans.rotation) * trans.position;
                        s_ShapeBox.size = fog.parameters.size;

                        EditorGUI.BeginChangeCheck();
                        s_ShapeBox.DrawHandle();
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObjects(new Object[] { fog, trans }, L10n.Tr("Change Local Volumetric Fog Bounding Box"));

                            Vector3 newSize = s_ShapeBox.size;
                            fog.parameters.size = newSize;

                            float max = Mathf.Min(newSize.x, newSize.y, newSize.z) * 0.5f;
                            float newUniformFade = Mathf.Clamp(fog.parameters.m_EditorUniformFade, 0f, max);
                            fog.parameters.m_EditorUniformFade = newUniformFade;

                            fog.parameters.positiveFade = fog.parameters.negativeFade = new Vector3(
                                1.0f - (newSize.x > 0.00000001 ? (newSize.x - newUniformFade) / newSize.x : 0f),
                                1.0f - (newSize.y > 0.00000001 ? (newSize.y - newUniformFade) / newSize.y : 0f),
                                1.0f - (newSize.z > 0.00000001 ? (newSize.z - newUniformFade) / newSize.z : 0f));

                            Vector3 delta = fog.transform.rotation * s_ShapeBox.center - fog.transform.position;
                            fog.transform.position += delta;
                        }
                    }

                    break;
            }
        }
    }
}