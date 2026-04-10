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
        private static readonly GUIContent s_RayTracedShadowLabel = EditorGUIUtility.TrTextContent("Ray Traced Shadow");
        private static readonly GUIContent s_EnableRayTracedShadowLabel = EditorGUIUtility.TrTextContent("Enable");
        private static readonly GUIContent s_RayTracedShadowRayLengthLabel = EditorGUIUtility.TrTextContent("Ray Length");
        private static readonly GUIContent s_RayTracedShadowRayBiasLabel = EditorGUIUtility.TrTextContent("Ray Bias");
        private static readonly GUIContent s_RayTracedShadowDistantRayBiasLabel = EditorGUIUtility.TrTextContent("Distant Ray Bias");
        private static readonly GUIContent s_RayTracedShadowSunAngularDiameterLabel = EditorGUIUtility.TrTextContent("Sun Angular Diameter (Unused in MVP)");
        private static readonly GUIContent s_PhysicallyBasedSkyLabel = EditorGUIUtility.TrTextContent("Physically Based Sky");
        private static readonly GUIContent s_InteractsWithSkyLabel = EditorGUIUtility.TrTextContent("Interacts With Sky");
        private static readonly GUIContent s_AngularDiameterLabel = EditorGUIUtility.TrTextContent("Angular Diameter");
        private static readonly GUIContent s_DiameterMultiplierModeLabel = EditorGUIUtility.TrTextContent("Use Diameter Multiplier");
        private static readonly GUIContent s_DiameterMultiplierLabel = EditorGUIUtility.TrTextContent("Diameter Multiplier");
        private static readonly GUIContent s_DiameterOverrideLabel = EditorGUIUtility.TrTextContent("Diameter Override");
        private static readonly GUIContent s_ShadingSourceLabel = EditorGUIUtility.TrTextContent("Shading Source");
        private static readonly GUIContent s_SunLightOverrideLabel = EditorGUIUtility.TrTextContent("Sun Light Override");
        private static readonly GUIContent s_SunColorLabel = EditorGUIUtility.TrTextContent("Sun Color");
        private static readonly GUIContent s_SunIntensityLabel = EditorGUIUtility.TrTextContent("Sun Intensity");
        private static readonly GUIContent s_MoonPhaseLabel = EditorGUIUtility.TrTextContent("Moon Phase");
        private static readonly GUIContent s_MoonPhaseRotationLabel = EditorGUIUtility.TrTextContent("Moon Phase Rotation");
        private static readonly GUIContent s_EarthshineLabel = EditorGUIUtility.TrTextContent("Earthshine");
        private static readonly GUIContent s_FlareSizeLabel = EditorGUIUtility.TrTextContent("Flare Size");
        private static readonly GUIContent s_FlareTintLabel = EditorGUIUtility.TrTextContent("Flare Tint");
        private static readonly GUIContent s_FlareFalloffLabel = EditorGUIUtility.TrTextContent("Flare Falloff");
        private static readonly GUIContent s_FlareMultiplierLabel = EditorGUIUtility.TrTextContent("Flare Multiplier");
        private static readonly GUIContent s_SurfaceTextureLabel = EditorGUIUtility.TrTextContent("Surface Texture");
        private static readonly GUIContent s_SurfaceTintLabel = EditorGUIUtility.TrTextContent("Surface Tint");
        private static readonly GUIContent s_DistanceLabel = EditorGUIUtility.TrTextContent("Distance");

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
                    var oldSpotAngle = settings.spotAngle.floatValue;
                    EditorGUI.BeginChangeCheck();
                    settings.DrawInnerAndOuterSpotAngle();
                    if (EditorGUI.EndChangeCheck())
                        VividLightIntensityUnitUtility.PreserveSpotLightLumenIntensity(settings, oldSpotAngle);
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

            DrawPhysicallyBasedSkyInspector();

            if (!ShouldShowDirectionalRayTracedShadowControls(m_SerializedLight))
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(s_RayTracedShadowLabel, EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedLight.enableRayTracedShadow, s_EnableRayTracedShadowLabel);

                if (!ShouldExpandDirectionalRayTracedShadowControls(m_SerializedLight))
                    return;

                EditorGUILayout.PropertyField(m_SerializedLight.rayTracedShadowRayLength, s_RayTracedShadowRayLengthLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.rayTracedShadowRayBias, s_RayTracedShadowRayBiasLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.rayTracedShadowDistantRayBias, s_RayTracedShadowDistantRayBiasLabel);
                EditorGUILayout.PropertyField(
                    m_SerializedLight.rayTracedShadowSunAngularDiameter,
                    s_RayTracedShadowSunAngularDiameterLabel);
                EditorGUILayout.HelpBox(
                    "Current hard-shadow MVP stores Sun Angular Diameter for a future soft-shadow path and does not sample it yet.",
                    MessageType.Info);
            }
        }

        internal static bool ShouldShowDirectionalRayTracedShadowControls(VividSerializedLight serializedLight)
        {
            return serializedLight != null
                && serializedLight.settings != null
                && !serializedLight.settings.lightType.hasMultipleDifferentValues
                && serializedLight.settings.light != null
                && serializedLight.settings.light.type == LightType.Directional;
        }

        internal static bool ShouldExpandDirectionalRayTracedShadowControls(VividSerializedLight serializedLight)
        {
            return serializedLight?.enableRayTracedShadow != null
                && (serializedLight.enableRayTracedShadow.hasMultipleDifferentValues
                    || serializedLight.enableRayTracedShadow.boolValue);
        }

        private void DrawPhysicallyBasedSkyInspector()
        {
            if (!ShouldShowDirectionalPhysicallyBasedSkyControls(m_SerializedLight))
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(s_PhysicallyBasedSkyLabel, EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedLight.interactsWithSky, s_InteractsWithSkyLabel);

                using (new EditorGUI.DisabledScope(
                           !m_SerializedLight.interactsWithSky.hasMultipleDifferentValues
                           && !m_SerializedLight.interactsWithSky.boolValue))
                {
                    EditorGUILayout.PropertyField(m_SerializedLight.angularDiameter, s_AngularDiameterLabel);
                    EditorGUILayout.PropertyField(m_SerializedLight.diameterMultiplierMode, s_DiameterMultiplierModeLabel);

                    if (m_SerializedLight.diameterMultiplierMode.hasMultipleDifferentValues
                        || m_SerializedLight.diameterMultiplierMode.boolValue)
                    {
                        EditorGUILayout.PropertyField(m_SerializedLight.diameterMultiplier, s_DiameterMultiplierLabel);
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(m_SerializedLight.diameterOverride, s_DiameterOverrideLabel);
                    }

                    EditorGUILayout.PropertyField(m_SerializedLight.distance, s_DistanceLabel);
                    EditorGUILayout.PropertyField(m_SerializedLight.celestialBodyShadingSource, s_ShadingSourceLabel);

                    if (m_SerializedLight.celestialBodyShadingSource.hasMultipleDifferentValues)
                    {
                        EditorGUILayout.PropertyField(m_SerializedLight.sunLightOverride, s_SunLightOverrideLabel);
                        EditorGUILayout.PropertyField(m_SerializedLight.sunColor, s_SunColorLabel);
                        EditorGUILayout.PropertyField(m_SerializedLight.sunIntensity, s_SunIntensityLabel);
                        EditorGUILayout.PropertyField(m_SerializedLight.moonPhase, s_MoonPhaseLabel);
                        EditorGUILayout.PropertyField(m_SerializedLight.moonPhaseRotation, s_MoonPhaseRotationLabel);
                    }
                    else
                    {
                        var shadingSource = (VividAdditionalLightData.CelestialBodyShadingSource)
                            m_SerializedLight.celestialBodyShadingSource.enumValueIndex;

                        switch (shadingSource)
                        {
                            case VividAdditionalLightData.CelestialBodyShadingSource.ReflectSunLight:
                                EditorGUILayout.PropertyField(m_SerializedLight.sunLightOverride, s_SunLightOverrideLabel);
                                break;
                            case VividAdditionalLightData.CelestialBodyShadingSource.Manual:
                                EditorGUILayout.PropertyField(m_SerializedLight.sunColor, s_SunColorLabel);
                                EditorGUILayout.PropertyField(m_SerializedLight.sunIntensity, s_SunIntensityLabel);
                                EditorGUILayout.PropertyField(m_SerializedLight.moonPhase, s_MoonPhaseLabel);
                                EditorGUILayout.PropertyField(m_SerializedLight.moonPhaseRotation, s_MoonPhaseRotationLabel);
                                break;
                        }
                    }

                    EditorGUILayout.PropertyField(m_SerializedLight.surfaceTexture, s_SurfaceTextureLabel);
                    EditorGUILayout.PropertyField(m_SerializedLight.surfaceTint, s_SurfaceTintLabel);
                    EditorGUILayout.PropertyField(m_SerializedLight.earthshine, s_EarthshineLabel);
                    EditorGUILayout.PropertyField(m_SerializedLight.flareSize, s_FlareSizeLabel);
                    EditorGUILayout.PropertyField(m_SerializedLight.flareTint, s_FlareTintLabel);
                    EditorGUILayout.PropertyField(m_SerializedLight.flareFalloff, s_FlareFalloffLabel);
                    EditorGUILayout.PropertyField(m_SerializedLight.flareMultiplier, s_FlareMultiplierLabel);
                }
            }
        }

        internal static bool ShouldShowDirectionalPhysicallyBasedSkyControls(VividSerializedLight serializedLight)
        {
            return serializedLight != null
                && serializedLight.settings != null
                && !serializedLight.settings.lightType.hasMultipleDifferentValues
                && serializedLight.settings.light != null
                && serializedLight.settings.light.type == LightType.Directional;
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

                VividLightIntensityUnitUtility.InitializeDefaultLightUnit(light);
                return;
            }

            if (component is VividAdditionalLightData additionalLightData)
                Initialize(additionalLightData);
        }
    }

    internal static class VividLightIntensityUnitUtility
    {
        internal static void InitializeDefaultLightUnit(Light light)
        {
            if (light == null)
                return;

            if (light.type != LightType.Point && light.type != LightType.Spot)
                return;

            if (light.lightUnit == LightUnit.Lumen)
                return;

            Undo.RecordObject(light, "Initialize Vivid Light Intensity Unit");
            light.lightUnit = LightUnit.Lumen;
            EditorUtility.SetDirty(light);
        }

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

        internal static void PreserveSpotLightLumenIntensity(LightEditor.Settings settings, float oldSpotAngle)
        {
            if (settings == null || settings.lightType.hasMultipleDifferentValues)
                return;

            if (settings.light.type != LightType.Spot)
                return;

            if (settings.lightUnit.hasMultipleDifferentValues || settings.lightUnit.GetEnumValue<LightUnit>() != LightUnit.Lumen)
                return;

            if (settings.enableSpotReflector.hasMultipleDifferentValues)
                return;

            var newSpotAngle = settings.spotAngle.floatValue;
            if (Mathf.Approximately(oldSpotAngle, newSpotAngle))
                return;

            var oldSolidAngle = LightUnitUtils.GetSolidAngle(
                LightType.Spot,
                settings.enableSpotReflector.boolValue,
                oldSpotAngle,
                1.0f);
            var oldLumen = LightUnitUtils.CandelaToLumen(settings.intensity.floatValue, oldSolidAngle);
            var newSolidAngle = LightUnitUtils.GetSolidAngle(
                LightType.Spot,
                settings.enableSpotReflector.boolValue,
                newSpotAngle,
                1.0f);
            settings.intensity.floatValue = LightUnitUtils.LumenToCandela(oldLumen, newSolidAngle);
        }
    }
}
