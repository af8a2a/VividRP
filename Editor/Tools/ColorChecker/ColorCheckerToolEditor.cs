using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CustomEditor(typeof(ColorCheckerTool))]
    internal sealed class ColorCheckerToolEditor : UnityEditor.Editor
    {
        private static readonly GUIContent s_ModeLabel = EditorGUIUtility.TrTextContent("Mode");
        private static readonly GUIContent s_FieldCountLabel = EditorGUIUtility.TrTextContent("Field Count");
        private static readonly GUIContent s_MaterialFieldsCountLabel = EditorGUIUtility.TrTextContent("Material Count");
        private static readonly GUIContent s_FieldsPerRowLabel = EditorGUIUtility.TrTextContent("Fields Per Row");
        private static readonly GUIContent s_FieldSizeLabel = EditorGUIUtility.TrTextContent("Field Size");
        private static readonly GUIContent s_FieldMarginLabel = EditorGUIUtility.TrTextContent("Field Margin");
        private static readonly GUIContent s_SphereModeLabel = EditorGUIUtility.TrTextContent("Sphere Mode");
        private static readonly GUIContent s_UnlitCompareLabel = EditorGUIUtility.TrTextContent("Compare To Unlit");
        private static readonly GUIContent s_AddGradientLabel = EditorGUIUtility.TrTextContent("Add Gradient");
        private static readonly GUIContent s_GradientALabel = EditorGUIUtility.TrTextContent("Gradient A");
        private static readonly GUIContent s_GradientBLabel = EditorGUIUtility.TrTextContent("Gradient B");
        private static readonly GUIContent s_GradientPowerLabel = EditorGUIUtility.TrTextContent("Gradient Power");
        private static readonly GUIContent s_UserTextureLabel = EditorGUIUtility.TrTextContent("Lit Texture");
        private static readonly GUIContent s_UserTextureRawLabel = EditorGUIUtility.TrTextContent("Raw Texture");
        private static readonly GUIContent s_TextureSliceLabel = EditorGUIUtility.TrTextContent("Slice");
        private static readonly GUIContent s_UnlitTextureExposureLabel = EditorGUIUtility.TrTextContent(
            "Raw Adapts To Exposure",
            "Disable when the raw texture already contains display-referred values.");
        private static readonly GUIContent s_ResetLabel = EditorGUIUtility.TrTextContent("Reset Palette");
        private static readonly GUIContent s_MoveToViewLabel = EditorGUIUtility.TrTextContent("Move To View");
        private static readonly GUIContent s_MetallicLabel = EditorGUIUtility.TrTextContent("Metallic");

        private SerializedProperty m_Mode;
        private SerializedProperty m_AddGradient;
        private SerializedProperty m_UnlitCompare;
        private SerializedProperty m_SphereMode;
        private SerializedProperty m_FieldCount;
        private SerializedProperty m_MaterialFieldsCount;
        private SerializedProperty m_FieldsPerRow;
        private SerializedProperty m_GridThickness;
        private SerializedProperty m_FieldSize;
        private SerializedProperty m_GradientPower;
        private SerializedProperty m_GradientA;
        private SerializedProperty m_GradientB;
        private SerializedProperty m_UserTexture;
        private SerializedProperty m_UserTextureRaw;
        private SerializedProperty m_TextureSlice;
        private SerializedProperty m_UnlitTextureExposure;
        private SerializedProperty m_CustomColors;
        private SerializedProperty m_CustomMaterials;
        private SerializedProperty m_IsMetalBools;

        private ColorCheckerTool tool => (ColorCheckerTool)target;

        private void OnEnable()
        {
            m_Mode = serializedObject.FindProperty("m_Mode");
            m_AddGradient = serializedObject.FindProperty("m_AddGradient");
            m_UnlitCompare = serializedObject.FindProperty("m_UnlitCompare");
            m_SphereMode = serializedObject.FindProperty("m_SphereMode");
            m_FieldCount = serializedObject.FindProperty("m_FieldCount");
            m_MaterialFieldsCount = serializedObject.FindProperty("m_MaterialFieldsCount");
            m_FieldsPerRow = serializedObject.FindProperty("m_FieldsPerRow");
            m_GridThickness = serializedObject.FindProperty("m_GridThickness");
            m_FieldSize = serializedObject.FindProperty("m_FieldSize");
            m_GradientPower = serializedObject.FindProperty("m_GradientPower");
            m_GradientA = serializedObject.FindProperty("m_GradientA");
            m_GradientB = serializedObject.FindProperty("m_GradientB");
            m_UserTexture = serializedObject.FindProperty("m_UserTexture");
            m_UserTextureRaw = serializedObject.FindProperty("m_UserTextureRaw");
            m_TextureSlice = serializedObject.FindProperty("m_TextureSlice");
            m_UnlitTextureExposure = serializedObject.FindProperty("m_UnlitTextureExposure");
            m_CustomColors = serializedObject.FindProperty("m_CustomColors");
            m_CustomMaterials = serializedObject.FindProperty("m_CustomMaterials");
            m_IsMetalBools = serializedObject.FindProperty("m_IsMetalBools");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(m_Mode, s_ModeLabel);
            var mode = (ColorCheckerTool.ColorCheckerMode)m_Mode.enumValueIndex;
            EditorGUILayout.HelpBox(GetInfoText(mode), MessageType.Info);

            DrawModeSettings(mode);
            DrawPalette(mode);
            DrawActions(mode);

            var changed = EditorGUI.EndChangeCheck();
            if (serializedObject.ApplyModifiedProperties() || changed)
                RefreshTargets();
        }

        private void DrawModeSettings(ColorCheckerTool.ColorCheckerMode mode)
        {
            switch (mode)
            {
                case ColorCheckerTool.ColorCheckerMode.Colors:
                    EditorGUILayout.PropertyField(m_FieldCount, s_FieldCountLabel);
                    EditorGUILayout.PropertyField(m_FieldsPerRow, s_FieldsPerRowLabel);
                    DrawProceduralCommonSettings(showSphereMode: true, showUnlitCompare: true, showGradient: true);
                    break;
                case ColorCheckerTool.ColorCheckerMode.Grayscale:
                    DrawProceduralCommonSettings(showSphereMode: true, showUnlitCompare: true, showGradient: true);
                    break;
                case ColorCheckerTool.ColorCheckerMode.MiddleGray:
                    DrawProceduralCommonSettings(showSphereMode: true, showUnlitCompare: true, showGradient: false);
                    break;
                case ColorCheckerTool.ColorCheckerMode.Reflection:
                    EditorGUILayout.PropertyField(m_FieldSize, s_FieldSizeLabel);
                    EditorGUILayout.PropertyField(m_GridThickness, s_FieldMarginLabel);
                    break;
                case ColorCheckerTool.ColorCheckerMode.SteppedLuminance:
                    EditorGUILayout.PropertyField(m_FieldSize, s_FieldSizeLabel);
                    EditorGUILayout.PropertyField(m_UnlitCompare, s_UnlitCompareLabel);
                    EditorGUILayout.PropertyField(m_AddGradient, s_AddGradientLabel);
                    DrawGradientSettings();
                    break;
                case ColorCheckerTool.ColorCheckerMode.Materials:
                    EditorGUILayout.PropertyField(m_MaterialFieldsCount, s_MaterialFieldsCountLabel);
                    EditorGUILayout.PropertyField(m_FieldSize, s_FieldSizeLabel);
                    EditorGUILayout.PropertyField(m_GridThickness, s_FieldMarginLabel);
                    break;
                case ColorCheckerTool.ColorCheckerMode.Texture:
                    EditorGUILayout.PropertyField(m_UserTexture, s_UserTextureLabel);
                    EditorGUILayout.PropertyField(m_UserTextureRaw, s_UserTextureRawLabel);
                    EditorGUILayout.PropertyField(m_TextureSlice, s_TextureSliceLabel);
                    EditorGUILayout.PropertyField(m_UnlitTextureExposure, s_UnlitTextureExposureLabel);
                    EditorGUILayout.PropertyField(m_FieldSize, s_FieldSizeLabel);
                    break;
            }
        }

        private void DrawProceduralCommonSettings(bool showSphereMode, bool showUnlitCompare, bool showGradient)
        {
            EditorGUILayout.PropertyField(m_FieldSize, s_FieldSizeLabel);
            EditorGUILayout.PropertyField(m_GridThickness, s_FieldMarginLabel);

            if (showSphereMode)
                EditorGUILayout.PropertyField(m_SphereMode, s_SphereModeLabel);

            if (showUnlitCompare)
                EditorGUILayout.PropertyField(m_UnlitCompare, s_UnlitCompareLabel);

            if (showGradient)
            {
                EditorGUILayout.PropertyField(m_AddGradient, s_AddGradientLabel);
                DrawGradientSettings();
            }
        }

        private void DrawGradientSettings()
        {
            using (new EditorGUI.DisabledScope(!m_AddGradient.boolValue))
            {
                EditorGUILayout.PropertyField(m_GradientA, s_GradientALabel);
                EditorGUILayout.PropertyField(m_GradientB, s_GradientBLabel);
                EditorGUILayout.PropertyField(m_GradientPower, s_GradientPowerLabel);
            }
        }

        private void DrawPalette(ColorCheckerTool.ColorCheckerMode mode)
        {
            switch (mode)
            {
                case ColorCheckerTool.ColorCheckerMode.Colors:
                    DrawEditableColorPalette(m_CustomColors, Mathf.Clamp(m_FieldCount.intValue, 1, ColorCheckerTool.MaxColorFields), Mathf.Max(1, m_FieldsPerRow.intValue));
                    break;
                case ColorCheckerTool.ColorCheckerMode.Grayscale:
                    DrawReadOnlyPalette(tool.crossPolarizedGrayscale, 6);
                    break;
                case ColorCheckerTool.ColorCheckerMode.MiddleGray:
                    DrawReadOnlyPalette(tool.middleGray, 1);
                    break;
                case ColorCheckerTool.ColorCheckerMode.SteppedLuminance:
                    DrawReadOnlyPalette(tool.steppedLuminance, 16);
                    break;
                case ColorCheckerTool.ColorCheckerMode.Materials:
                    DrawMaterialPalette();
                    break;
            }
        }

        private static void DrawReadOnlyPalette(Color32[] colors, int fieldsPerRow)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                for (var rowStart = 0; rowStart < colors.Length; rowStart += fieldsPerRow)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var rowEnd = Mathf.Min(rowStart + fieldsPerRow, colors.Length);
                        for (var index = rowStart; index < rowEnd; index++)
                            EditorGUILayout.ColorField(GUIContent.none, colors[index], false, false, false);
                    }
                }
            }
        }

        private static void DrawEditableColorPalette(SerializedProperty colors, int fieldCount, int fieldsPerRow)
        {
            if (colors == null || !colors.isArray)
                return;

            EditorGUILayout.LabelField("Color Fields", EditorStyles.boldLabel);
            for (var rowStart = 0; rowStart < fieldCount; rowStart += fieldsPerRow)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rowEnd = Mathf.Min(rowStart + fieldsPerRow, fieldCount);
                    for (var index = rowStart; index < rowEnd; index++)
                        DrawColorElement(colors.GetArrayElementAtIndex(index));
                }
            }
        }

        private void DrawMaterialPalette()
        {
            if (m_CustomMaterials == null || !m_CustomMaterials.isArray || m_IsMetalBools == null || !m_IsMetalBools.isArray)
                return;

            EditorGUILayout.LabelField("Material Fields", EditorStyles.boldLabel);
            var materialCount = Mathf.Clamp(m_MaterialFieldsCount.intValue, 1, ColorCheckerTool.MaxMaterialFields);
            for (var index = 0; index < materialCount; index++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawColorElement(m_CustomMaterials.GetArrayElementAtIndex(index));
                    var metallic = m_IsMetalBools.GetArrayElementAtIndex(index);
                    metallic.boolValue = EditorGUILayout.ToggleLeft(s_MetallicLabel, metallic.boolValue, GUILayout.Width(96f));
                }
            }
        }

        private static void DrawColorElement(SerializedProperty property)
        {
            var color = property.colorValue;
            var edited = EditorGUILayout.ColorField(
                GUIContent.none,
                new Color(color.r, color.g, color.b, 1f),
                false,
                false,
                false);
            property.colorValue = new Color(edited.r, edited.g, edited.b, color.a);
        }

        private void DrawActions(ColorCheckerTool.ColorCheckerMode mode)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(mode != ColorCheckerTool.ColorCheckerMode.Colors && mode != ColorCheckerTool.ColorCheckerMode.Materials))
                {
                    if (GUILayout.Button(s_ResetLabel))
                    {
                        foreach (var selectedTarget in targets)
                        {
                            var colorChecker = (ColorCheckerTool)selectedTarget;
                            Undo.RecordObject(colorChecker, "Reset Color Checker Palette");
                            colorChecker.ResetColors();
                            EditorUtility.SetDirty(colorChecker);
                        }
                    }
                }

                if (GUILayout.Button(s_MoveToViewLabel))
                    MoveToSceneView(tool);
            }
        }

        private void RefreshTargets()
        {
            foreach (var selectedTarget in targets)
            {
                var colorChecker = (ColorCheckerTool)selectedTarget;
                Undo.RecordObject(colorChecker, "Update Color Checker");
                colorChecker.ApplyMaterialMetalFlags();
                colorChecker.Refresh();
                EditorUtility.SetDirty(colorChecker);
            }
        }

        private static void MoveToSceneView(ColorCheckerTool colorChecker)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || colorChecker == null)
                return;

            var previousSelection = Selection.activeGameObject;
            Selection.activeGameObject = colorChecker.gameObject;
            sceneView.AlignWithView();
            sceneView.MoveToView(colorChecker.transform);
            Selection.activeGameObject = previousSelection;
        }

        private static string GetInfoText(ColorCheckerTool.ColorCheckerMode mode)
        {
            return mode switch
            {
                ColorCheckerTool.ColorCheckerMode.Colors =>
                    "Procedural color chart for color and lighting calibration. Color fields are customizable and persistent.",
                ColorCheckerTool.ColorCheckerMode.Grayscale =>
                    "Cross-polarized grayscale values for PBR lighting calibration without specular contribution.",
                ColorCheckerTool.ColorCheckerMode.MiddleGray =>
                    "Neutral 5 middle gray reference.",
                ColorCheckerTool.ColorCheckerMode.Reflection =>
                    "Metallic smooth sphere for checking local reflections.",
                ColorCheckerTool.ColorCheckerMode.SteppedLuminance =>
                    "Stepped luminance ramp for gamma calibration.",
                ColorCheckerTool.ColorCheckerMode.Materials =>
                    "Material palette where each row varies smoothness across six columns.",
                ColorCheckerTool.ColorCheckerMode.Texture =>
                    "External texture comparison mode. The slice separates lit and raw texture views.",
                _ => string.Empty,
            };
        }
    }

    internal static class ColorCheckerToolMenuItems
    {
        internal const string CreateColorCheckerMenuPath = "GameObject/Rendering/VividRP Color Checker Tool";

        [MenuItem(CreateColorCheckerMenuPath, priority = 13)]
        private static void CreateColorChecker(MenuCommand menuCommand)
        {
            CreateColorCheckerGameObject(menuCommand.context as GameObject);
        }

        internal static GameObject CreateColorCheckerGameObject(GameObject parent)
        {
            var gameObject = new GameObject("Color Checker")
            {
                tag = "EditorOnly",
                hideFlags = HideFlags.DontSaveInBuild,
            };
            GameObjectUtility.SetParentAndAlign(gameObject, parent);
            gameObject.AddComponent<ColorCheckerTool>();

            Undo.RegisterCreatedObjectUndo(gameObject, "Create Color Checker");
            Selection.activeGameObject = gameObject;

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                sceneView.AlignWithView();
                sceneView.MoveToView(gameObject.transform);
                gameObject.transform.eulerAngles = new Vector3(0f, gameObject.transform.eulerAngles.y, gameObject.transform.eulerAngles.z);
            }

            return gameObject;
        }
    }
}
