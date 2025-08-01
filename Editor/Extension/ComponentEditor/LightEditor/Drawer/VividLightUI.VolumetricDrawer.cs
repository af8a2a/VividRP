using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
    partial class VividLightUI
    {
        static partial class Styles
        {
            public static readonly GUIContent volumetricEnable = EditorGUIUtility.TrTextContent("Enable", "When enabled, this Light uses Volumetrics.");

            public static readonly GUIContent volumetricDimmer =
                EditorGUIUtility.TrTextContent("Multiplier", "Controls the intensity of the scattered Volumetric lighting.");

            public static readonly GUIContent volumetricFadeDistance = EditorGUIUtility.TrTextContent("Fade Distance",
                "Sets the distance from the camera at which light smoothly fades out from contributing to volumetric lighting.");
        }

        public static readonly GUIContent Volumetric = EditorGUIUtility.TrTextContent("Volumetric");

        static void DrawVolumetric(VividSerializedLight serializedLight, Editor owner)
        {
            LightType lightType = serializedLight.settings.lightType.GetEnumValue<LightType>();

            // Right now the only supported area light type in path tracing is rectangle lights.
            // Modify this if this changes to add new area light shapees.
            // if (lightType == LightType.Rectangle)
            // {
            //     EditorGUILayout.HelpBox(s_Styles.areaLightVolumetricsWarning.text, MessageType.Warning);
            // }

            EditorGUILayout.PropertyField(serializedLight.affectsVolumetric, Styles.volumetricEnable);
            {
                using (new EditorGUI.DisabledScope(!serializedLight.affectsVolumetric.boolValue))
                    EditorGUILayout.PropertyField(serializedLight.volumetricDimmer, Styles.volumetricDimmer);
                // EditorGUILayout.Slider(serialized.volumetricShadowDimmer, 0.0f, 1.0f, s_Styles.volumetricShadowDimmer);
                if (lightType != LightType.Directional)
                {
                    EditorGUILayout.PropertyField(serializedLight.volumetricFadeDistance, Styles.volumetricFadeDistance);
                }
                
            }
        }
    }
}