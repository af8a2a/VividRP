using System.Linq;
using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
    partial class VividLightUI
    {
        static partial class Styles
        {
            public static readonly GUIContent AreaWidth = EditorGUIUtility.TrTextContent("Width", "Controls the width in units of the area light.");
            public static readonly GUIContent AreaHeight = EditorGUIUtility.TrTextContent("Height", "Controls the height in units of the area light.");
            public static readonly GUIContent AreaRadius = EditorGUIUtility.TrTextContent("Radius", "Controls the radius in units of the disc area light.");
        }

        static void DrawAreaShapeContent(VividSerializedLight serializedLight, Editor owner)
        {
            int selectedShape = serializedLight.settings.isAreaLightType ? serializedLight.settings.lightType.intValue : 0;

            // Handle all lights that are not in the default set
            if (!Styles.LightTypeValues.Contains(serializedLight.settings.lightType.intValue))
            {
                if (serializedLight.settings.lightType.intValue == (int)LightType.Disc)
                {
                    selectedShape = (int)LightType.Disc;
                }
            }

            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginProperty(rect, Styles.AreaLightShapeContent, serializedLight.settings.lightType);
            EditorGUI.BeginChangeCheck();
            int shape = EditorGUI.IntPopup(rect, Styles.AreaLightShapeContent, selectedShape, Styles.AreaLightShapeTitles, Styles.AreaLightShapeValues);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(serializedLight.settings.light, "Adjust Light Shape");
                serializedLight.settings.lightType.intValue = shape;
            }

            EditorGUI.EndProperty();

            using (new EditorGUI.IndentLevelScope())
                DrawArea((LightType)selectedShape, serializedLight.settings);
            // serializedLight.settings.DrawArea();
        }

        static void DrawArea(LightType lightType, LightEditor.Settings settings)
        {
            bool changed = false;
            switch (lightType)
            {
                case LightType.Rectangle:
                    EditorGUILayout.PropertyField(settings.areaSizeX, Styles.AreaWidth);
                    EditorGUILayout.PropertyField(settings.areaSizeY, Styles.AreaHeight);
                    if (settings.areaSizeX.floatValue < 0.01f)
                    {
                        settings.areaSizeX.floatValue = 0.01f;
                        changed = true;
                    }

                    if (settings.areaSizeY.floatValue < 0.01f)
                    {
                        settings.areaSizeY.floatValue = 0.01f;
                        changed = true;
                    }

                    break;
                case LightType.Disc:
                    EditorGUILayout.PropertyField(settings.areaSizeX, Styles.AreaRadius);
                    if (settings.areaSizeX.floatValue < 0.01f)
                    {
                        settings.areaSizeX.floatValue = 0.01f;
                        changed = true;
                    }

                    break;
            }

            if (changed) settings.ApplyModifiedProperties();
        }
    }
}