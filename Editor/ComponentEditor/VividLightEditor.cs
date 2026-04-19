using System;
using System.Reflection;
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
        private static readonly GUIContent s_CSMShadowLabel = EditorGUIUtility.TrTextContent("CSM Shadow");
        private static readonly GUIContent s_ScreenSpaceShadowQualityLabel = EditorGUIUtility.TrTextContent("Screen Space Quality", "Quality tier used by the screen-space CSM resolve for this directional light.");
        private static readonly GUIContent s_ShadowAtlasResolutionLabel = EditorGUIUtility.TrTextContent("Atlas Resolution", "Fixed resolution used for the full 2x2 CSM atlas rendered by this directional light.");
        private static readonly GUIContent s_DepthBiasLabel = EditorGUIUtility.TrTextContent("Depth Bias", "Constant depth bias applied while rendering cascaded shadow maps for this directional light.");
        private static readonly GUIContent s_NormalBiasLabel = EditorGUIUtility.TrTextContent("Normal Bias", "Normal-based bias applied while rendering and resolving cascaded shadow maps for this directional light.");
        private static readonly GUIContent s_SlopeBiasLabel = EditorGUIUtility.TrTextContent("Slope-Scale Depth Bias", "Slope-scale depth bias applied while rasterizing cascaded shadow maps for this directional light.");
        private static readonly GUIContent s_PCSSSettingsLabel = EditorGUIUtility.TrTextContent("PCSS");
        private static readonly GUIContent s_DirLightPCSSMaxPenumbraSizeLabel = EditorGUIUtility.TrTextContent("Max Penumbra Size", "Maximum size (in world space) of PCSS shadow penumbra limiting blur filter kernel size, larger kernels may require more samples to avoid quality degradation.");
        private static readonly GUIContent s_DirLightPCSSMaxSamplingDistanceLabel = EditorGUIUtility.TrTextContent("Max Sampling Distance", "Maximum distance (in world space) from the receiver PCSS shadow sampling occurs, lower to avoid light bleeding but may cause self-shadowing.");
        private static readonly GUIContent s_DirLightPCSSMinFilterSizeTexelsLabel = EditorGUIUtility.TrTextContent("Min Filter", "Minimum filter size (in shadowmap texels) to avoid aliasing close to the caster.");
        private static readonly GUIContent s_DirLightPCSSMinFilterMaxAngularDiameterLabel = EditorGUIUtility.TrTextContent("Min Filter Max Angular Diameter", "Maximum angular diameter to reach minimum filter size, lower to avoid self-shadowing but may cause light bleeding.");
        private static readonly GUIContent s_DirLightPCSSBlockerSearchAngularDiameterLabel = EditorGUIUtility.TrTextContent("Blocker Search Angular Diameter", "Angular diameter to use for blocker search, increase to avoid missing hidden close blockers but it may cause self-shadowing.");
        private static readonly GUIContent s_DirLightPCSSBlockerSamplingClumpExponentLabel = EditorGUIUtility.TrTextContent("Blocker Sampling Clump Exponent", "Affects how blocker search samples are distributed. Sample distance to center is elevated to this power.");
        private static readonly GUIContent s_DirLightPCSSBlockerSampleCountLabel = EditorGUIUtility.TrTextContent("Blocker Sample Count", "Controls the number of samples used to determine average blocker distance. Higher values reduce noise at additional cost.");
        private static readonly GUIContent s_DirLightPCSSFilterSampleCountLabel = EditorGUIUtility.TrTextContent("Filter Sample Count", "Controls the number of samples used to filter the penumbra. Higher values reduce noise at additional cost.");
        private static readonly GUIContent s_RayTracedShadowLabel = EditorGUIUtility.TrTextContent("Ray Traced Shadow");
        private static readonly GUIContent s_EnableRayTracedShadowLabel = EditorGUIUtility.TrTextContent("Enable");
        private static readonly GUIContent s_RayTracedShadowRayLengthLabel = EditorGUIUtility.TrTextContent("Ray Length");
        private static readonly GUIContent s_RayTracedShadowRayBiasLabel = EditorGUIUtility.TrTextContent("Ray Bias");
        private static readonly GUIContent s_RayTracedShadowDistantRayBiasLabel = EditorGUIUtility.TrTextContent("Distant Ray Bias");
        private static readonly GUIContent s_RayTracedShadowSunAngularDiameterLabel = EditorGUIUtility.TrTextContent("Sun Angular Diameter (Unused in MVP)");
        private static readonly GUIContent s_BarnDoorLabel = EditorGUIUtility.TrTextContent("Barn Door");
        private static readonly GUIContent s_BarnDoorAngleLabel = EditorGUIUtility.TrTextContent("Angle", "Angle in degrees of the rectangular area light barn doors.");
        private static readonly GUIContent s_BarnDoorLengthLabel = EditorGUIUtility.TrTextContent("Length", "Length of the rectangular area light barn door blades.");
        private static readonly GUIContent s_CelestialBodyLabel = EditorGUIUtility.TrTextContent("Celestial Body");
        private static readonly GUIContent s_InteractsWithSkyLabel = EditorGUIUtility.TrTextContent("Affect Physically Based Sky", "Check this option to make the light and the Physically Based sky affect one another.");
        private static readonly GUIContent s_AngularDiameterLabel = EditorGUIUtility.TrTextContent("Angular Diameter", "Angular diameter of the emissive celestial body represented by the light as seen from the camera (in degrees). Used to render the sun/moon disk and affects the sharpness of shadows.");
        private static readonly GUIContent s_DiameterMultiplierLabel = EditorGUIUtility.TrTextContent("Angular Diameter Multiplier", "Angular diameter used to render the celestial body in the sky without affecting the sharpness of shadows. This value is multiplied by the Angular Diameter set in the Shape section.");
        private static readonly GUIContent s_DiameterOverrideLabel = EditorGUIUtility.TrTextContent("Angular Diameter", "Angular diameter used to render the celestial body in the sky without affecting the sharpness of shadows.");
        private static readonly GUIContent s_ShadingSourceLabel = EditorGUIUtility.TrTextContent("Shading", "Specify the light source used for shading of the Celestial Body.\nIt can either emit it's own light, receive it from a Light in the scene, or using manual settings.");
        private static readonly GUIContent s_SunLightOverrideLabel = EditorGUIUtility.TrTextContent("Sun Light Override", "Specifiy the Directional Light that should illuminate this Celestial Body.\nIf not specified, VividRP will use the directional light in the scene with the highest intensity.");
        private static readonly GUIContent s_SunColorLabel = EditorGUIUtility.TrTextContent("Sun Color", "Color of the light source.");
        private static readonly GUIContent s_SunIntensityLabel = EditorGUIUtility.TrTextContent("Sun Intensity", "Intensity of the light source.");
        private static readonly GUIContent s_MoonPhaseLabel = EditorGUIUtility.TrTextContent("Phase", "Controls the area of the surface illuminated by the Sun.");
        private static readonly GUIContent s_MoonPhaseRotationLabel = EditorGUIUtility.TrTextContent("Phase Rotation", "Rotates the Light Source relatively to the Celestial Body.");
        private static readonly GUIContent s_EarthshineLabel = EditorGUIUtility.TrTextContent("Earthshine", "Intensity of the light reflected from the planet onto the Celestial Body.");
        private static readonly GUIContent s_FlareSizeLabel = EditorGUIUtility.TrTextContent("Flare Size", "Size of the flare around the celestial body (in degrees).");
        private static readonly GUIContent s_FlareTintLabel = EditorGUIUtility.TrTextContent("Flare Tint", "Tints the flare of the celestial body");
        private static readonly GUIContent s_FlareFalloffLabel = EditorGUIUtility.TrTextContent("Flare Falloff", "The falloff rate of flare intensity as the angle from the light increases.");
        private static readonly GUIContent s_FlareMultiplierLabel = EditorGUIUtility.TrTextContent("Flare Multiplier", "Multiplier applied on the flare intensity.");
        private static readonly GUIContent s_SurfaceColorLabel = EditorGUIUtility.TrTextContent("Surface Color", "Texture of the surface of the celestial body.");
        private static readonly GUIContent s_SurfaceTintLabel = EditorGUIUtility.TrTextContent("Tint");
        private static readonly GUIContent s_DistanceLabel = EditorGUIUtility.TrTextContent("Distance", "Distance from the camera (in meters) to the emissive celestial body represented by the light. This value is only used for sorting.");
        private static readonly string[] s_DiameterModeNames = { "Multiply", "Override" };
        private static readonly GUIContent[] s_ScreenSpaceShadowQualityOptionLabels =
        {
            EditorGUIUtility.TrTextContent("Low (PCF 3x3)"),
            EditorGUIUtility.TrTextContent("Medium (PCF 5x5)"),
            EditorGUIUtility.TrTextContent("High (PCF 7x7)"),
            EditorGUIUtility.TrTextContent("Very High (PCSS)")
        };

        private static readonly int[] s_ScreenSpaceShadowQualityOptionValues =
        {
            (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low,
            (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Medium,
            (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.High,
            (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh
        };
        private static readonly GUIContent[] s_ShadowAtlasResolutionOptionLabels =
        {
            EditorGUIUtility.TrTextContent("1024"),
            EditorGUIUtility.TrTextContent("2048"),
            EditorGUIUtility.TrTextContent("4096"),
            EditorGUIUtility.TrTextContent("8192")
        };

        private static readonly int[] s_ShadowAtlasResolutionOptionValues =
        {
            1024,
            2048,
            4096,
            8192
        };

        private const int DiameterModePopupWidth = 70;

        private VividSerializedLight m_SerializedLight;
        private static MethodInfo s_TextureMiniThumbnailMethod;
        private static bool s_TextureMiniThumbnailMethodResolved;

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
                case LightType.Directional:
                    EditorGUILayout.PropertyField(m_SerializedLight.angularDiameter, s_AngularDiameterLabel);
                    break;
                case LightType.Rectangle:
                    settings.DrawArea();
                    DrawAreaBarnDoorInspector();
                    break;
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
            DrawDirectionalShadowBiasInspector();
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

        private void DrawDirectionalShadowBiasInspector()
        {
            if (!ShouldShowDirectionalShadowBiasControls(m_SerializedLight))
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(s_CSMShadowLabel, EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                DrawDirectionalScreenSpaceShadowQualityField();
                DrawDirectionalPCSSFields();
                DrawDirectionalShadowAtlasResolutionField();
                EditorGUILayout.Slider(m_SerializedLight.depthBias, 0.0f, 10.0f, s_DepthBiasLabel);
                EditorGUILayout.Slider(m_SerializedLight.normalBias, 0.0f, 10.0f, s_NormalBiasLabel);
                EditorGUILayout.Slider(m_SerializedLight.slopeBias, 0.0f, 5.0f, s_SlopeBiasLabel);
            }
        }

        private void DrawDirectionalScreenSpaceShadowQualityField()
        {
            var property = m_SerializedLight.screenSpaceShadowQuality;
            var oldMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;

            EditorGUI.BeginChangeCheck();
            var quality = EditorGUILayout.IntPopup(
                s_ScreenSpaceShadowQualityLabel,
                property.intValue,
                s_ScreenSpaceShadowQualityOptionLabels,
                s_ScreenSpaceShadowQualityOptionValues);
            if (EditorGUI.EndChangeCheck())
                property.intValue = quality;

            EditorGUI.showMixedValue = oldMixedValue;
        }

        private void DrawAreaBarnDoorInspector()
        {
            if (!ShouldShowAreaBarnDoorControls(m_SerializedLight))
                return;

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField(s_BarnDoorLabel, EditorStyles.miniBoldLabel);
            EditorGUILayout.Slider(m_SerializedLight.barnDoorAngle, 0.0f, 90.0f, s_BarnDoorAngleLabel);
            EditorGUILayout.PropertyField(m_SerializedLight.barnDoorLength, s_BarnDoorLengthLabel);
        }

        private void DrawDirectionalPCSSFields()
        {
            if (!ShouldShowDirectionalPCSSControls(m_SerializedLight))
                return;

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField(s_PCSSSettingsLabel, EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(m_SerializedLight.dirLightPCSSMaxPenumbraSize, s_DirLightPCSSMaxPenumbraSizeLabel);
            EditorGUILayout.PropertyField(m_SerializedLight.dirLightPCSSMaxSamplingDistance, s_DirLightPCSSMaxSamplingDistanceLabel);
            EditorGUILayout.PropertyField(m_SerializedLight.dirLightPCSSMinFilterSizeTexels, s_DirLightPCSSMinFilterSizeTexelsLabel);
            EditorGUILayout.PropertyField(m_SerializedLight.dirLightPCSSMinFilterMaxAngularDiameter, s_DirLightPCSSMinFilterMaxAngularDiameterLabel);
            EditorGUILayout.PropertyField(m_SerializedLight.dirLightPCSSBlockerSearchAngularDiameter, s_DirLightPCSSBlockerSearchAngularDiameterLabel);
            EditorGUILayout.Slider(
                m_SerializedLight.dirLightPCSSBlockerSamplingClumpExponent,
                1.0f,
                6.0f,
                s_DirLightPCSSBlockerSamplingClumpExponentLabel);
            EditorGUILayout.PropertyField(m_SerializedLight.dirLightPCSSBlockerSampleCount, s_DirLightPCSSBlockerSampleCountLabel);
            EditorGUILayout.PropertyField(m_SerializedLight.dirLightPCSSFilterSampleCount, s_DirLightPCSSFilterSampleCountLabel);
        }

        private void DrawDirectionalShadowAtlasResolutionField()
        {
            var property = m_SerializedLight.shadowAtlasResolution;
            var oldMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;

            EditorGUI.BeginChangeCheck();
            var resolution = EditorGUILayout.IntPopup(
                s_ShadowAtlasResolutionLabel,
                property.intValue,
                s_ShadowAtlasResolutionOptionLabels,
                s_ShadowAtlasResolutionOptionValues);
            if (EditorGUI.EndChangeCheck())
                property.intValue = resolution;

            EditorGUI.showMixedValue = oldMixedValue;
        }

        internal static bool ShouldShowDirectionalPCSSControls(VividSerializedLight serializedLight)
        {
            return ShouldShowDirectionalShadowBiasControls(serializedLight)
                && serializedLight?.screenSpaceShadowQuality != null
                && (serializedLight.screenSpaceShadowQuality.hasMultipleDifferentValues
                    || serializedLight.screenSpaceShadowQuality.intValue == (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh);
        }

        internal static bool ShouldShowAreaBarnDoorControls(VividSerializedLight serializedLight)
        {
            return serializedLight != null
                && serializedLight.settings != null
                && !serializedLight.settings.lightType.hasMultipleDifferentValues
                && serializedLight.settings.light != null
                && serializedLight.settings.light.type == LightType.Rectangle;
        }

        internal static bool ShouldShowDirectionalShadowBiasControls(VividSerializedLight serializedLight)
        {
            return serializedLight != null
                && serializedLight.settings != null
                && !serializedLight.settings.lightType.hasMultipleDifferentValues
                && serializedLight.settings.light != null
                && serializedLight.settings.light.type == LightType.Directional;
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
            EditorGUILayout.LabelField(s_CelestialBodyLabel, EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedLight.interactsWithSky, s_InteractsWithSkyLabel);

                using (new EditorGUI.DisabledScope(
                           !m_SerializedLight.interactsWithSky.hasMultipleDifferentValues
                           && !m_SerializedLight.interactsWithSky.boolValue))
                {
                    DrawCelestialBodyAngularDiameterField();
                    EditorGUILayout.PropertyField(m_SerializedLight.distance, s_DistanceLabel);
                    DrawCelestialBodySurfaceColorField();
                    EditorGUILayout.PropertyField(m_SerializedLight.celestialBodyShadingSource, s_ShadingSourceLabel);
                    DrawCelestialBodyShadingFields();
                    EditorGUILayout.PropertyField(m_SerializedLight.flareSize, s_FlareSizeLabel);
                    EditorGUILayout.PropertyField(m_SerializedLight.flareFalloff, s_FlareFalloffLabel);
                    EditorGUILayout.PropertyField(m_SerializedLight.flareTint, s_FlareTintLabel);
                    EditorGUILayout.PropertyField(m_SerializedLight.flareMultiplier, s_FlareMultiplierLabel);
                }
            }
        }

        private void DrawCelestialBodyAngularDiameterField()
        {
            var rect = EditorGUILayout.GetControlRect();
            rect.xMax -= DiameterModePopupWidth + 2;

            var popupRect = rect;
            popupRect.x = rect.xMax + 2 - EditorGUI.indentLevel * 15;
            popupRect.width = DiameterModePopupWidth + EditorGUI.indentLevel * 15;

            var mode = m_SerializedLight.diameterMultiplierMode.boolValue ? 0 : 1;
            EditorGUI.showMixedValue = m_SerializedLight.diameterMultiplierMode.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            mode = EditorGUI.Popup(popupRect, mode, s_DiameterModeNames);
            if (EditorGUI.EndChangeCheck())
                m_SerializedLight.diameterMultiplierMode.boolValue = mode == 0;

            EditorGUI.showMixedValue = false;
            EditorGUI.BeginProperty(rect, GUIContent.none, m_SerializedLight.diameterMultiplierMode);
            if (m_SerializedLight.diameterMultiplierMode.hasMultipleDifferentValues
                || m_SerializedLight.diameterMultiplierMode.boolValue)
            {
                EditorGUI.PropertyField(rect, m_SerializedLight.diameterMultiplier, s_DiameterMultiplierLabel);
            }
            else
            {
                EditorGUI.PropertyField(rect, m_SerializedLight.diameterOverride, s_DiameterOverrideLabel);
            }

            EditorGUI.EndProperty();
        }

        private void DrawCelestialBodyShadingFields()
        {
            if (m_SerializedLight.celestialBodyShadingSource.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(m_SerializedLight.sunLightOverride, s_SunLightOverrideLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.sunColor, s_SunColorLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.sunIntensity, s_SunIntensityLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.moonPhase, s_MoonPhaseLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.moonPhaseRotation, s_MoonPhaseRotationLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.earthshine, s_EarthshineLabel);
                return;
            }

            var shadingSource = (VividAdditionalLightData.CelestialBodyShadingSource)
                m_SerializedLight.celestialBodyShadingSource.enumValueIndex;

            using (new EditorGUI.IndentLevelScope())
            {
                switch (shadingSource)
                {
                    case VividAdditionalLightData.CelestialBodyShadingSource.ReflectSunLight:
                        EditorGUILayout.PropertyField(m_SerializedLight.sunLightOverride, s_SunLightOverrideLabel);
                        DrawCelestialBodySunLightWarnings();
                        EditorGUILayout.PropertyField(m_SerializedLight.earthshine, s_EarthshineLabel);
                        break;
                    case VividAdditionalLightData.CelestialBodyShadingSource.Manual:
                        EditorGUILayout.PropertyField(m_SerializedLight.sunColor, s_SunColorLabel);
                        EditorGUILayout.PropertyField(m_SerializedLight.sunIntensity, s_SunIntensityLabel);
                        EditorGUILayout.PropertyField(m_SerializedLight.moonPhase, s_MoonPhaseLabel);
                        EditorGUILayout.PropertyField(m_SerializedLight.moonPhaseRotation, s_MoonPhaseRotationLabel);
                        EditorGUILayout.PropertyField(m_SerializedLight.earthshine, s_EarthshineLabel);
                        break;
                }
            }
        }

        private void DrawCelestialBodySunLightWarnings()
        {
            if (m_SerializedLight.sunLightOverride.objectReferenceValue == null)
                return;

            var referencedLight = m_SerializedLight.sunLightOverride.objectReferenceValue as Light;
            var currentLight = target as Light;
            if (referencedLight == null || currentLight == null)
                return;

            if (referencedLight == currentLight)
            {
                EditorGUILayout.HelpBox("The Celestial Body cannot receive lighting from itself.", MessageType.Warning);
            }
            else if (referencedLight.type != LightType.Directional)
            {
                EditorGUILayout.HelpBox("The Sun Light needs to be a directional light.", MessageType.Error);
            }
        }

        private void DrawCelestialBodySurfaceColorField()
        {
            var miniThumbnailMethod = GetTextureMiniThumbnailMethod();
            if (miniThumbnailMethod == null)
            {
                EditorGUILayout.PropertyField(m_SerializedLight.surfaceTexture, s_SurfaceColorLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.surfaceTint, s_SurfaceTintLabel);
                return;
            }

            var rect = EditorGUILayout.GetControlRect();
            miniThumbnailMethod.Invoke(null, new object[] { rect, m_SerializedLight.surfaceTexture, s_SurfaceColorLabel, typeof(Texture2D) });

            var oldIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var colorRect = new Rect(
                rect.x + EditorGUIUtility.labelWidth + 2,
                rect.y,
                rect.width - EditorGUIUtility.labelWidth - 2,
                rect.height);
            EditorGUI.BeginProperty(colorRect, s_SurfaceColorLabel, m_SerializedLight.surfaceTint);

            EditorGUI.BeginChangeCheck();
            var color = EditorGUI.ColorField(colorRect, m_SerializedLight.surfaceTint.colorValue);
            if (EditorGUI.EndChangeCheck())
                m_SerializedLight.surfaceTint.colorValue = color;

            EditorGUI.EndProperty();
            EditorGUI.indentLevel = oldIndentLevel;
        }

        private static MethodInfo GetTextureMiniThumbnailMethod()
        {
            if (s_TextureMiniThumbnailMethodResolved)
                return s_TextureMiniThumbnailMethod;

            var type = Type.GetType("UnityEditor.Rendering.TextureParameterHelper,Unity.RenderPipelines.Core.Editor");
            s_TextureMiniThumbnailMethod = type?.GetMethod("MiniThumbnail", BindingFlags.Static | BindingFlags.NonPublic);
            s_TextureMiniThumbnailMethodResolved = true;
            return s_TextureMiniThumbnailMethod;
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
