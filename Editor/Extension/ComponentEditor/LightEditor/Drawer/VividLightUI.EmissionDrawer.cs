using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
    partial class VividLightUI
    {
        static void DrawEmissionContent(VividSerializedLight serializedLight, Editor owner)
        {
            DrawLightIntensity(serializedLight, owner);

#if false
            serializedLight.settings.DrawIntensity();
#endif
            serializedLight.settings.DrawBounceIntensity();

            if (!serializedLight.settings.lightType.hasMultipleDifferentValues)
            {
                var lightType = serializedLight.settings.light.type;
                if (lightType != LightType.Directional)
                {
#if UNITY_2020_1_OR_NEWER
                    serializedLight.settings.DrawRange();
#else
                    serializedLight.settings.DrawRange(false);
#endif
                }
            }

            DrawLightCookieContent(serializedLight, owner);
        }


        static void DrawLightCookieContent(VividSerializedLight serializedLight, Editor owner)
        {
            var settings = serializedLight.settings;
            if (settings.lightType.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("Cannot multi edit light cookies from different light types.", MessageType.Info);
                return;
            }

            settings.DrawCookie();

            // Draw 2D cookie size for directional lights
            bool isDirectionalLight = settings.light.type == LightType.Directional;
            if (isDirectionalLight)
            {
                if (settings.cookie != null)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(serializedLight.lightCookieSizeProp, Styles.LightCookieSize);
                    EditorGUILayout.PropertyField(serializedLight.lightCookieOffsetProp, Styles.LightCookieOffset);
                    if (EditorGUI.EndChangeCheck())
                        Experimental.Lightmapping.SetLightDirty((UnityEngine.Light)serializedLight.serializedObject.targetObject);
                }
            }
        }
    }
}