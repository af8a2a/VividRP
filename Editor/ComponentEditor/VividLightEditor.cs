using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CustomEditor(typeof(Light))]
    [SupportedOnRenderPipeline(typeof(VividRenderPipelineAsset))]
    [CanEditMultipleObjects]
    internal sealed class VividLightEditor : LightEditor
    {
        private static readonly GUIContent s_VividSettingsLabel = EditorGUIUtility.TrTextContent("VividRP");
        private static readonly GUIContent s_UsePipelineSettingsLabel = EditorGUIUtility.TrTextContent("Use Pipeline Settings");
        private static readonly GUIContent s_CustomShadowLayersLabel = EditorGUIUtility.TrTextContent("Custom Shadow Layers");
        private static readonly GUIContent s_ShadowRenderingLayersLabel = EditorGUIUtility.TrTextContent("Shadow Rendering Layers");

        private VividSerializedLight m_SerializedLight;

        protected override void OnEnable()
        {
            m_SerializedLight = new VividSerializedLight(serializedObject, settings);
            Undo.undoRedoPerformed += RebuildSerializedState;
        }

        protected void OnDisable()
        {
            Undo.undoRedoPerformed -= RebuildSerializedState;
        }

        public override void OnInspectorGUI()
        {
            m_SerializedLight.Update();
            DrawBuiltInLightInspector();
            DrawVividInspector();
            m_SerializedLight.Apply();
            NormalizeSelectedLightIntensityUnits();
        }

        private void RebuildSerializedState()
        {
            m_SerializedLight = new VividSerializedLight(serializedObject, settings);
        }

        private void DrawBuiltInLightInspector()
        {
            settings.DrawLightType();
            settings.DrawLightmapping();
            LightUI.DrawColor(m_SerializedLight, this);
            LightUI.DrawIntensity(m_SerializedLight, this);
            LightUI.DrawIntensityModifiers(m_SerializedLight);

            DrawShapeInspector();
            DrawEmissionInspector();
            DrawRenderingInspector();
            DrawShadowsInspector();
        }

        private void DrawShapeInspector()
        {
            if (settings.lightType.hasMultipleDifferentValues)
                return;

            switch (settings.light.type)
            {
                case LightType.Spot:
                    settings.DrawInnerAndOuterSpotAngle();
                    break;
                case LightType.Rectangle:
                case LightType.Disc:
                case LightType.Tube:
                    settings.DrawArea();
                    break;
            }
        }

        private void DrawEmissionInspector()
        {
            settings.DrawBounceIntensity();

            if (!settings.lightType.hasMultipleDifferentValues && settings.light.type != LightType.Directional)
                settings.DrawRange();

            settings.DrawCookie();
            if (!settings.lightType.hasMultipleDifferentValues
                && settings.light.type == LightType.Directional
                && !settings.cookieProp.hasMultipleDifferentValues
                && settings.cookie != null)
            {
                settings.DrawCookieSize();
            }
        }

        private void DrawRenderingInspector()
        {
            settings.DrawRenderMode();
            settings.DrawCullingMask();
            settings.DrawRenderingLayerMask();
            settings.DrawHalo();
            settings.DrawFlare();
        }

        private void DrawShadowsInspector()
        {
            settings.DrawShadowsType();

            if (settings.lightType.hasMultipleDifferentValues || settings.shadowsType.hasMultipleDifferentValues)
                return;

            if (settings.light.shadows == LightShadows.None)
                return;

            if (settings.isBakedOrMixed)
            {
                switch (settings.light.type)
                {
                    case LightType.Point:
                    case LightType.Spot:
                        settings.DrawShapeRadius();
                        break;
                    case LightType.Directional:
                        settings.DrawBakedShadowAngle();
                        break;
                }
            }

            settings.DrawRuntimeShadow();
        }

        private void DrawVividInspector()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(s_VividSettingsLabel, EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedLight.usePipelineSettings, s_UsePipelineSettingsLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.customShadowLayers, s_CustomShadowLayersLabel);

                using (new EditorGUI.DisabledScope(!m_SerializedLight.customShadowLayers.boolValue && !m_SerializedLight.customShadowLayers.hasMultipleDifferentValues))
                {
                    EditorGUILayout.PropertyField(m_SerializedLight.shadowRenderingLayers, s_ShadowRenderingLayersLabel);
                }
            }
        }

        private void NormalizeSelectedLightIntensityUnits()
        {
            foreach (var targetObject in targets)
            {
                if (targetObject is not Light light)
                    continue;

                VividLightIntensityUnitUtility.NormalizeUnsupportedLightUnit(light);
            }
        }
    }

    [CustomEditor(typeof(VividAdditionalLightData))]
    [CanEditMultipleObjects]
    internal sealed class VividAdditionalLightDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Managed by the Light inspector.", MessageType.None);
        }
    }

    [InitializeOnLoad]
    internal static class VividAdditionalLightDataEditorUtility
    {
        static VividAdditionalLightDataEditorUtility()
        {
            ObjectFactory.componentWasAdded -= OnComponentWasAdded;
            ObjectFactory.componentWasAdded += OnComponentWasAdded;
        }

        internal static void Initialize(VividAdditionalLightData additionalData)
        {
            if (additionalData == null)
                return;

            if ((additionalData.hideFlags & HideFlags.HideInInspector) != 0)
                return;

            Undo.RecordObject(additionalData, "Hide Vivid Additional Light Data");
            additionalData.hideFlags |= HideFlags.HideInInspector;
            EditorUtility.SetDirty(additionalData);
        }

        private static void OnComponentWasAdded(Component component)
        {
            if (component is Light light)
            {
                if (!light.TryGetComponent<VividAdditionalLightData>(out var additionalData))
                {
                    additionalData = Undo.AddComponent<VividAdditionalLightData>(light.gameObject);
                    Initialize(additionalData);
                }

                return;
            }

            if (component is VividAdditionalLightData additionalLightData)
                Initialize(additionalLightData);
        }
    }

    internal static class VividLightIntensityUnitUtility
    {
        internal static void NormalizeUnsupportedLightUnit(Light light)
        {
            if (light == null)
                return;

            var lightType = light.type;
            var lightUnit = light.lightUnit;
            if (LightUnitUtils.IsLightUnitSupported(lightType, lightUnit))
                return;

            Undo.RecordObject(light, "Normalize Vivid Light Intensity Unit");
            light.lightUnit = LightUnitUtils.GetNativeLightUnit(lightType);
            if (lightType == LightType.Directional || lightType == LightType.Box)
                light.luxAtDistance = 1.0f;

            EditorUtility.SetDirty(light);
        }
    }
}
