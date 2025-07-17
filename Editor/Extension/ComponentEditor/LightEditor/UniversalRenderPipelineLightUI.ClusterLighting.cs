using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    using CED = CoreEditorDrawer<UniversalRenderPipelineSerializedLight>;


    internal partial class UniversalRenderPipelineLightUI
    {
        static PiecewiseLightUnitSlider k_DirectionalLightUnitSlider = new PiecewiseLightUnitSlider(LightUnitSliderDescriptors.LuxDescriptor);
        static PiecewiseLightUnitSlider k_PunctualLightUnitSlider = new PiecewiseLightUnitSlider(LightUnitSliderDescriptors.LumenDescriptor);




        private static partial class Styles
        {
            public static readonly GUIContent AngularDiameter = EditorGUIUtility.TrTextContent("Angular Diameter",
                "Angular diameter of the emissive celestial body represented by the light as seen from the camera (in degrees). Used to render the sun/moon disk.");

            public static readonly GUIContent LightRadius = EditorGUIUtility.TrTextContent("Radius",
                "Sets the radius of the light source. This affects the falloff of diffuse lighting, the spread of the specular highlight, and the softness of Ray Traced shadows.");

            public static readonly GUIContent lightIntensity =
                EditorGUIUtility.TrTextContent("Intensity", "Sets the strength of the Light. Use the drop-down to select the light units to use.");
            
            
            public static readonly GUIContent contributionsHeader = EditorGUIUtility.TrTextContent("Contribution");

            public static readonly GUIContent BaseContribution = EditorGUIUtility.TrTextContent("Base Light", "Sets the contribution of the Base Light.");
        }
        

        static void DrawDirectionalLightIntensity(UniversalRenderPipelineSerializedLight serializedLight, Editor owner)
        {
            var lightUnitSlider = k_DirectionalLightUnitSlider;


            lightUnitSlider.SetSerializedObject(serializedLight.serializedObject);

            Rect lineRect = EditorGUILayout.GetControlRect();
            Rect labelRect = lineRect;
            labelRect.width = EditorGUIUtility.labelWidth;

            EditorGUI.LabelField(labelRect, Styles. lightIntensity);
            // Draw the light unit slider + icon + tooltip
            Rect lightUnitSliderRect = lineRect; // TODO: Move the value and unit rects to new line
            lightUnitSliderRect.x += EditorGUIUtility.labelWidth + 2;
            lightUnitSliderRect.width -= EditorGUIUtility.labelWidth + 2;

            float val = serializedLight.intensity.floatValue;
            EditorGUI.BeginChangeCheck();
            lightUnitSlider.Draw(lightUnitSliderRect, serializedLight.intensity, ref val);
            if (EditorGUI.EndChangeCheck())
            {
                serializedLight.intensity.floatValue = val;
            }
        }
        
        static void DrawContributionsContent(UniversalRenderPipelineSerializedLight serializedLight, Editor owner)
        {
            EditorGUILayout.Slider(serializedLight.baseContributionProp, 0f, 1f, Styles.BaseContribution);
        }

        
        static void DrawPuntualLightIntensity(UniversalRenderPipelineSerializedLight serializedLight, Editor owner)
        {
            var lightUnitSlider = k_PunctualLightUnitSlider;

            lightUnitSlider.SetSerializedObject(serializedLight.serializedObject);

            Rect lineRect = EditorGUILayout.GetControlRect();
            Rect labelRect = lineRect;
            labelRect.width = EditorGUIUtility.labelWidth;

            EditorGUI.LabelField(labelRect, Styles.lightIntensity);
            // Draw the light unit slider + icon + tooltip
            Rect lightUnitSliderRect = lineRect; // TODO: Move the value and unit rects to new line
            lightUnitSliderRect.x += EditorGUIUtility.labelWidth + 2;
            lightUnitSliderRect.width -= EditorGUIUtility.labelWidth + 2;

            float val = serializedLight.intensity.floatValue;
            float convertedVal = LightUtils.ConvertPunctualLightLuxToLumen(serializedLight.settings.light.type, SpotLightShape.Cone, val, false, serializedLight.settings.light.spotAngle, 1.0f, 1.0f);
            EditorGUI.BeginChangeCheck();
            lightUnitSlider.Draw(lightUnitSliderRect, serializedLight.intensity, ref convertedVal);
            if (EditorGUI.EndChangeCheck())
            {
                serializedLight.intensity.floatValue = LightUtils.ConvertPunctualLightLumenToLux(serializedLight.settings.light.type, convertedVal, val, false, 1.0f);
            }
        }


        static void DrawLightIntensity(UniversalRenderPipelineSerializedLight serializedLight, Editor owner)
        {
            if (serializedLight.settings.light.type == LightType.Directional)
            {
                DrawDirectionalLightIntensity(serializedLight, owner);
            }
            else
            {
                DrawPuntualLightIntensity(serializedLight, owner);
            }

            // Draw value field
            Rect valueRect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(valueRect, serializedLight.intensity, CoreEditorStyles.empty);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(serializedLight.settings.light, "Adjust Light Intensity");
                serializedLight.intensity.floatValue = Mathf.Max(serializedLight.intensity.floatValue, 0.0f);
            }
        }
        
        
        
        static void DrawDirectionalShapeContent(UniversalRenderPipelineSerializedLight serializedLight, Editor owner)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedLight.angularDiameter, Styles.AngularDiameter);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(serializedLight.settings.light, "Adjust Directional Light Shape");
                serializedLight.angularDiameter.floatValue = Mathf.Clamp(serializedLight.angularDiameter.floatValue, 0, 90);
            }
        }
        
        static void DrawPointShapeContent(UniversalRenderPipelineSerializedLight serializedLight, Editor owner)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedLight.shapeRadius, Styles.LightRadius);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(serializedLight.settings.light, "Adjust Point Light Shape");
                serializedLight.shapeRadius.floatValue = Mathf.Clamp(serializedLight.shapeRadius.floatValue, 0, 30);
            }
        }


    }
}