using System.Linq;
using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
    partial class VividLightUI
    {
        static void DrawGeneralContent(VividSerializedLight serializedLight, Editor owner)
        {
            DrawGeneralContentInternal(serializedLight, owner, isInPreset: false);
        }

        static void DrawGeneralContentPreset(VividSerializedLight serializedLight, Editor owner)
        {
            DrawGeneralContentInternal(serializedLight, owner, isInPreset: true);
        }

        static void DrawGeneralContentInternal(VividSerializedLight serializedLight, Editor owner, bool isInPreset)
        {
            // To the user, we will only display it as a area light, but under the hood, we have Rectangle and Disc. This is not to confuse people
            // who still use our legacy light inspector.

            int selectedLightType = serializedLight.settings.lightType.intValue;

            // Handle all lights that are not in the default set
            if (!Styles.LightTypeValues.Contains(serializedLight.settings.lightType.intValue))
            {
                if (serializedLight.settings.lightType.intValue == (int)LightType.Disc)
                {
                    selectedLightType = (int)LightType.Rectangle;
                }
            }

            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginProperty(rect, Styles.Type, serializedLight.settings.lightType);
            EditorGUI.BeginChangeCheck();
            int type;
            if (Styles.LightTypeValues.Contains(selectedLightType))
            {
                // ^ The currently selected light type is supported in the
                // current pipeline.
                type = EditorGUI.IntPopup(rect, Styles.Type, selectedLightType, Styles.LightTypeTitles, Styles.LightTypeValues);
            }
            else
            {
                // ^ The currently selected light type is not supported in
                // the current pipeline. Add it to the dropdown, since it
                // would show up as a blank entry.
                string currentTitle = ((LightType)selectedLightType).ToString();
                GUIContent[] titles = Styles.LightTypeTitles.Append(EditorGUIUtility.TrTextContent(currentTitle)).ToArray();
                int[] values = Styles.LightTypeValues.Append(selectedLightType).ToArray();
                type = EditorGUI.IntPopup(rect, Styles.Type, selectedLightType, titles, values);
            }

            if (EditorGUI.EndChangeCheck())
            {
                s_SetGizmosDirty();
                serializedLight.settings.lightType.intValue = type;
            }

            EditorGUI.EndProperty();

            if (!Styles.LightTypeValues.Contains(type))
            {
                EditorGUILayout.HelpBox(
                    "This light type is not supported in the current active render pipeline. Change the light type or the active Render Pipeline to use this light.",
                    MessageType.Info
                );
            }

            Light light = serializedLight.settings.light;
            var lightType = light.type;
            if (LightType.Directional != lightType && light == RenderSettings.sun)
            {
                EditorGUILayout.HelpBox(Styles.SunSourceWarning.text, MessageType.Warning);
            }

            if (!serializedLight.settings.lightType.hasMultipleDifferentValues)
            {
                // using (new EditorGUI.DisabledScope(serializedLight.settings.isAreaLightType))
                    serializedLight.settings.DrawLightmapping();

                // if (serializedLight.settings.isAreaLightType && serializedLight.settings.lightmapping.intValue != (int)LightmapBakeType.Baked)
                // {
                //     serializedLight.settings.lightmapping.intValue = (int)LightmapBakeType.Baked;
                //     serializedLight.Apply();
                // }
            }
        }
    }
}