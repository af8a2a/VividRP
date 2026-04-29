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
        private static readonly GUIContent s_VolumeHeader = EditorGUIUtility.TrTextContent("Volume");
        private static readonly GUIContent s_MaskTextureHeader = EditorGUIUtility.TrTextContent("Mask Texture");
        private static readonly GUIContent s_MaskMaterialHeader = EditorGUIUtility.TrTextContent("Mask Material");
        private static readonly GUIContent s_AlbedoLabel =
            EditorGUIUtility.TrTextContent("Single Scattering Albedo", "The color this fog scatters light to.");
        private static readonly GUIContent s_MeanFreePathLabel =
            EditorGUIUtility.TrTextContent("Fog Distance", "Determines how far you can see through the fog in meters.");
        private static readonly GUIContent s_MaskModeLabel =
            EditorGUIUtility.TrTextContent("Mask Mode", "Texture mode uses a 3D texture as density mask. Material mode uses a Fog Volume material.");
        private static readonly GUIContent s_BlendingModeLabel =
            EditorGUIUtility.TrTextContent("Blending Mode", "Determines how this fog volume blends with other fog volumes in the scene.");
        private static readonly GUIContent s_PriorityLabel =
            EditorGUIUtility.TrTextContent("Priority", "Rendering priority for overlapping local volumetric fog volumes.");
        private static readonly GUIContent s_SizeLabel =
            EditorGUIUtility.TrTextContent("Size", "Size of the local volumetric fog volume.");
        private static readonly GUIContent s_BlendDistanceLabel =
            EditorGUIUtility.TrTextContent("Blend Distance", "Interior distance from each face where the fog fades in completely.");
        private static readonly GUIContent s_PositiveFadeLabel =
            EditorGUIUtility.TrTextContent("Positive Blend", "Blend distance along the positive local X, Y and Z faces.");
        private static readonly GUIContent s_NegativeFadeLabel =
            EditorGUIUtility.TrTextContent("Negative Blend", "Blend distance along the negative local X, Y and Z faces.");
        private static readonly GUIContent s_InvertFadeLabel =
            EditorGUIUtility.TrTextContent("Invert Blend", "Inverts the face blend so the edge is denser than the center.");
        private static readonly GUIContent s_FalloffModeLabel =
            EditorGUIUtility.TrTextContent("Falloff Mode", "Controls the falloff curve used by the blend distance.");
        private static readonly GUIContent s_DistanceFadeStartLabel =
            EditorGUIUtility.TrTextContent("Distance Fade Start", "Distance from the camera where this local volumetric fog starts to fade out.");
        private static readonly GUIContent s_DistanceFadeEndLabel =
            EditorGUIUtility.TrTextContent("Distance Fade End", "Distance from the camera where this local volumetric fog is fully faded out.");
        private static readonly GUIContent s_TextureLabel =
            EditorGUIUtility.TrTextContent("Texture", "3D texture used as the density mask.");
        private static readonly GUIContent s_TextureScrollLabel =
            EditorGUIUtility.TrTextContent("Scroll Speed", "Speed at which the density mask scrolls on each local axis.");
        private static readonly GUIContent s_TextureTileLabel =
            EditorGUIUtility.TrTextContent("Tiling", "Tiling of the density mask on each local axis.");
        private static readonly GUIContent s_TextureOffsetLabel =
            EditorGUIUtility.TrTextContent("Offset", "Offset of the density mask on each local axis.");
        private static readonly GUIContent s_MaterialMaskLabel =
            EditorGUIUtility.TrTextContent("Material", "Material used to mask color and density. It must contain a FogVolumeVoxelize pass.");
        private const string InvalidMaterialMessage = "Material not compatible. Please use a material with a FogVolumeVoxelize pass.";

        private SerializedProperty m_BoundProxy;
        private SerializedProperty m_Parameters;
        private SerializedProperty m_Albedo;
        private SerializedProperty m_MeanFreePath;
        private SerializedProperty m_BlendingMode;
        private SerializedProperty m_Priority;
        private SerializedProperty m_Anisotropy;
        private SerializedProperty m_MaskMode;
        private SerializedProperty m_VolumeMask;
        private SerializedProperty m_MaterialMask;
        private SerializedProperty m_TextureScrollingSpeed;
        private SerializedProperty m_TextureTiling;
        private SerializedProperty m_TextureOffset;
        private SerializedProperty m_PositiveFade;
        private SerializedProperty m_NegativeFade;
        private SerializedProperty m_InvertFade;
        private SerializedProperty m_DistanceFadeStart;
        private SerializedProperty m_DistanceFadeEnd;
        private SerializedProperty m_FalloffMode;
        private SerializedBoundProxyShape m_SerializedShape;
        private UnityEditor.Editor m_MaterialEditor;
        private static bool s_ShowVolume = true;
        private static bool s_ShowMaskTexture = true;
        private static bool s_ShowMaskMaterial = true;

        private void OnEnable()
        {
            m_BoundProxy = serializedObject.FindProperty("m_BoundProxy");
            m_Parameters = serializedObject.FindProperty("m_Parameters");
            m_Albedo = FindParameter("albedo");
            m_MeanFreePath = FindParameter("meanFreePath");
            m_BlendingMode = FindParameter("blendingMode");
            m_Priority = FindParameter("priority");
            m_Anisotropy = FindParameter("anisotropy");
            m_MaskMode = FindParameter("maskMode");
            m_VolumeMask = FindParameter("volumeMask");
            m_MaterialMask = FindParameter("materialMask");
            m_TextureScrollingSpeed = FindParameter("textureScrollingSpeed");
            m_TextureTiling = FindParameter("textureTiling");
            m_TextureOffset = FindParameter("textureOffset");
            m_PositiveFade = FindParameter("positiveFade");
            m_NegativeFade = FindParameter("negativeFade");
            m_InvertFade = FindParameter("invertFade");
            m_DistanceFadeStart = FindParameter("distanceFadeStart");
            m_DistanceFadeEnd = FindParameter("distanceFadeEnd");
            m_FalloffMode = FindParameter("falloffMode");
            m_SerializedShape = new SerializedBoundProxyShape(m_BoundProxy);
        }

        private void OnDisable()
        {
            if (m_MaterialEditor == null)
                return;

            DestroyImmediate(m_MaterialEditor);
            m_MaterialEditor = null;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ForceBoxShape(m_SerializedShape);

            DrawPrimarySettings();
            EditorGUILayout.Space();
            DrawVolumeSettings();
            DrawMaskSettings();

            serializedObject.ApplyModifiedProperties();
            DrawMaterialInspector();
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

        private void DrawPrimarySettings()
        {
            if (ShouldDrawTextureModeSettings())
            {
                DrawProperty(m_Albedo, s_AlbedoLabel);
                DrawProperty(m_MeanFreePath, s_MeanFreePathLabel);
            }

            DrawProperty(m_MaskMode, s_MaskModeLabel);
            if (ShouldDrawTextureModeSettings())
                DrawProperty(m_BlendingMode, s_BlendingModeLabel);
            DrawProperty(m_Priority, s_PriorityLabel);
            DrawProperty(m_Anisotropy);
        }

        private void DrawVolumeSettings()
        {
            s_ShowVolume = EditorGUILayout.Foldout(s_ShowVolume, s_VolumeHeader, true);
            if (!s_ShowVolume)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedShape.center);
                EditorGUILayout.PropertyField(m_SerializedShape.size, s_SizeLabel);
                DrawProperty(m_PositiveFade, s_PositiveFadeLabel);
                DrawProperty(m_NegativeFade, s_NegativeFadeLabel);
                EditorGUILayout.LabelField(s_BlendDistanceLabel, EditorStyles.miniLabel);
                DrawProperty(m_FalloffMode, s_FalloffModeLabel);
                DrawProperty(m_InvertFade, s_InvertFadeLabel);
                DrawProperty(m_DistanceFadeStart, s_DistanceFadeStartLabel);
                DrawProperty(m_DistanceFadeEnd, s_DistanceFadeEndLabel);
            }
        }

        private void DrawMaskSettings()
        {
            if (ShouldDrawTextureModeSettings())
                DrawMaskTextureSettings();

            if (ShouldDrawMaterialModeSettings())
                DrawMaskMaterialSettings();
        }

        private void DrawMaskTextureSettings()
        {
            s_ShowMaskTexture = EditorGUILayout.Foldout(s_ShowMaskTexture, s_MaskTextureHeader, true);
            if (!s_ShowMaskTexture)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                DrawProperty(m_VolumeMask, s_TextureLabel);
                DrawProperty(m_TextureScrollingSpeed, s_TextureScrollLabel);
                DrawProperty(m_TextureTiling, s_TextureTileLabel);
                DrawProperty(m_TextureOffset, s_TextureOffsetLabel);
            }
        }

        private void DrawMaskMaterialSettings()
        {
            s_ShowMaskMaterial = EditorGUILayout.Foldout(s_ShowMaskMaterial, s_MaskMaterialHeader, true);
            if (!s_ShowMaskMaterial)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                DrawProperty(m_MaterialMask, s_MaterialMaskLabel);
                if (m_MaterialMask != null && m_MaterialMask.objectReferenceValue != null && !IsMaterialMaskCompatible())
                    EditorGUILayout.HelpBox(InvalidMaterialMessage, MessageType.Error);
            }
        }

        private void DrawMaterialInspector()
        {
            if (!ShouldDrawMaterialModeSettings() || m_MaterialMask == null || m_MaterialMask.objectReferenceValue is not Material material)
                return;

            if (m_MaterialEditor == null || m_MaterialEditor.target != material)
                UnityEditor.Editor.CreateCachedEditor(material, typeof(MaterialEditor), ref m_MaterialEditor);

            using (new EditorGUI.DisabledScope((material.hideFlags & HideFlags.NotEditable) != 0))
            {
                m_MaterialEditor.DrawHeader();
                m_MaterialEditor.OnInspectorGUI();
            }
        }

        private SerializedProperty FindParameter(string propertyName)
        {
            return m_Parameters?.FindPropertyRelative(propertyName);
        }

        private static void DrawProperty(SerializedProperty property, GUIContent label = null)
        {
            if (property == null)
                return;

            if (label == null)
                EditorGUILayout.PropertyField(property);
            else
                EditorGUILayout.PropertyField(property, label);
        }

        private bool ShouldDrawTextureModeSettings()
        {
            return m_MaskMode == null
                || m_MaskMode.hasMultipleDifferentValues
                || (VividLocalVolumetricFogMaskMode)m_MaskMode.intValue == VividLocalVolumetricFogMaskMode.Texture;
        }

        private bool ShouldDrawMaterialModeSettings()
        {
            return m_MaskMode != null
                && (m_MaskMode.hasMultipleDifferentValues
                    || (VividLocalVolumetricFogMaskMode)m_MaskMode.intValue == VividLocalVolumetricFogMaskMode.Material);
        }

        private bool IsMaterialMaskCompatible()
        {
            return m_MaterialMask.objectReferenceValue is Material material
                && material.FindPass("FogVolumeVoxelize") >= 0;
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
