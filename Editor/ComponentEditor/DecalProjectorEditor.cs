using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.SubSystem.Decal;

namespace VividRP.Editor
{
    [CustomEditor(typeof(DecalProjector))]
    [CanEditMultipleObjects]
    internal sealed class DecalProjectorEditor : UnityEditor.Editor
    {
        private const float k_SelectedGizmoMinLod = 0.1f;
        private const float k_DetailedGizmoMinLod = 0.5f;
        private const float k_ProjectionArrowScale = 0.25f;
        private const float k_ProjectionPlaneLineThickness = 3.0f;

        private static readonly Color s_GizmoColor = new(0.1f, 0.1f, 0.1f, 0.01f);

        private SerializedProperty m_BoundProxy;
        private SerializedProperty m_BlendDistance;
        private SerializedProperty m_VirtualTextureAsset;
        private SerializedProperty m_DrawOrder;
        private SerializedProperty m_BaseColorTexture;
        private SerializedProperty m_NormalTexture;
        private SerializedProperty m_MetallicTexture;
        private SerializedProperty m_RoughnessTexture;
        private SerializedProperty m_BaseColor;
        private SerializedProperty m_Metallic;
        private SerializedProperty m_Roughness;

        private SerializedBoundProxyShape m_SerializedShape;

        private void OnEnable()
        {
            m_BoundProxy = serializedObject.FindProperty("m_BoundProxy");
            m_BlendDistance = serializedObject.FindProperty("m_BlendDistance");
            m_VirtualTextureAsset = serializedObject.FindProperty("m_VirtualTextureAsset");
            m_DrawOrder = serializedObject.FindProperty("m_DrawOrder");
            m_BaseColorTexture = serializedObject.FindProperty("m_BaseColorTexture");
            m_NormalTexture = serializedObject.FindProperty("m_NormalTexture");
            m_MetallicTexture = serializedObject.FindProperty("m_MetallicTexture");
            m_RoughnessTexture = serializedObject.FindProperty("m_RoughnessTexture");
            m_BaseColor = serializedObject.FindProperty("m_BaseColor");
            m_Metallic = serializedObject.FindProperty("m_Metallic");
            m_Roughness = serializedObject.FindProperty("m_Roughness");

            m_SerializedShape = new SerializedBoundProxyShape(m_BoundProxy);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Projection", EditorStyles.boldLabel);
            BoundProxyEditorUtility.DrawInspector(m_SerializedShape, showCenter: false);
            EditorGUILayout.PropertyField(m_BlendDistance);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Material", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_DrawOrder);
            EditorGUILayout.PropertyField(
                m_VirtualTextureAsset,
                new GUIContent(
                    "Virtual Texture Asset",
                    "Combined BaseColor, Normal, and packed Metallic/Occlusion/Smoothness VVT used by the Terrain Runtime Virtual Texture decal technique."));
            EditorGUILayout.PropertyField(m_BaseColor);
            EditorGUILayout.PropertyField(m_BaseColorTexture);
            EditorGUILayout.PropertyField(m_NormalTexture);
            EditorGUILayout.PropertyField(m_Metallic);
            EditorGUILayout.PropertyField(m_MetallicTexture);
            EditorGUILayout.PropertyField(m_Roughness);
            EditorGUILayout.PropertyField(m_RoughnessTexture);

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            var projector = (DecalProjector)target;
            if (projector == null)
                return;

            var so = new SerializedObject(target);
            var shapeProp = so.FindProperty("m_BoundProxy");
            var shape = new SerializedBoundProxyShape(shapeProp);

            BoundProxyEditorUtility.DrawSceneHandles(
                so,
                shape,
                projector.transform,
                undoLabel: "Edit Decal Projector Bounds");
        }
    }
}
