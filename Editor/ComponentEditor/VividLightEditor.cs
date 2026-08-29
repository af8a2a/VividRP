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
        [Flags]
        private enum Expandable
        {
            General = 1 << 0,
            Shape = 1 << 1,
            Emission = 1 << 2,
            Rendering = 1 << 3,
            Shadows = 1 << 4,
            Vivid = 1 << 5,
            CSMShadow = 1 << 6,
            PCSS = 1 << 7,
            BarnDoor = 1 << 8,
            Volumetric = 1 << 9,
            CelestialBody = 1 << 10,
            RayTracedShadow = 1 << 11,
            TimeOfDay = 1 << 12,
            BendSSS = 1 << 13,
        }

        internal enum SpotLightShape
        {
            Cone,
            Box
        }

        internal enum GeneralLightType
        {
            Spot,
            Directional,
            Point,
            Rectangle,
            Disc,
            Tube
        }

        private const Expandable DefaultExpandedState =
            Expandable.General
            | Expandable.Shape
            | Expandable.Emission
            | Expandable.Rendering
            | Expandable.Shadows
            | Expandable.Vivid
            | Expandable.CSMShadow
            | Expandable.PCSS
            | Expandable.BarnDoor
            | Expandable.Volumetric
            | Expandable.CelestialBody
            | Expandable.RayTracedShadow
            | Expandable.TimeOfDay
            | Expandable.BendSSS;

        private static ExpandedState<Expandable, VividLightEditor> s_ExpandedState;

        private static readonly GUIContent s_GeneralLabel = EditorGUIUtility.TrTextContent("General");
        private static readonly GUIContent s_ShapeLabel = EditorGUIUtility.TrTextContent("Shape");
        private static readonly GUIContent s_EmissionLabel = EditorGUIUtility.TrTextContent("Emission");
        private static readonly GUIContent s_RenderingLabel = EditorGUIUtility.TrTextContent("Rendering");
        private static readonly GUIContent s_ShadowsLabel = EditorGUIUtility.TrTextContent("Shadows");
        private static readonly GUIContent s_VividSettingsLabel = EditorGUIUtility.TrTextContent("VividRP");
        private static readonly GUIContent s_ExpandAllLabel = EditorGUIUtility.TrTextContent("Expand All");
        private static readonly GUIContent s_CollapseAllLabel = EditorGUIUtility.TrTextContent("Collapse All");
        private static readonly GUIContent s_UsePipelineSettingsLabel = EditorGUIUtility.TrTextContent("Use Pipeline Settings");
        private static readonly GUIContent s_CustomShadowLayersLabel = EditorGUIUtility.TrTextContent("Custom Shadow Layers");
        private static readonly GUIContent s_ShadowRenderingLayersLabel = EditorGUIUtility.TrTextContent("Shadow Rendering Layers");
        private static readonly GUIContent s_LightTypeLabel = EditorGUIUtility.TrTextContent("Type");
        private static readonly GUIContent s_LightRadiusLabel = EditorGUIUtility.TrTextContent("Radius", "Sets the radius of the light source. This affects the falloff of diffuse lighting, the spread of the specular highlight, and the softness of Ray Traced shadows.");
        private static readonly GUIContent s_SpotLightShapeLabel = EditorGUIUtility.TrTextContent("Shape", "Sets the shape of the spot light.");
        private static readonly GUIContent s_BoxShapeWidthLabel = EditorGUIUtility.TrTextContent("Width", "Sets the width of the box spot light.");
        private static readonly GUIContent s_BoxShapeHeightLabel = EditorGUIUtility.TrTextContent("Height", "Sets the height of the box spot light.");
        private static readonly Color s_BoxSpotGizmoColor = new(1.0f, 0.84f, 0.22f, 0.45f);
        private static readonly Color s_BoxSpotGizmoBehindColor = new(1.0f, 0.84f, 0.22f, 0.14f);
        private const float k_MinBoxSpotLightHandleValue = 0.0001f;
        private static readonly GUIContent s_CSMShadowLabel = EditorGUIUtility.TrTextContent("CSM Shadow");
        private static readonly GUIContent s_ScreenSpaceShadowQualityLabel = EditorGUIUtility.TrTextContent("Screen Space Quality", "Quality tier used by the screen-space CSM resolve for this directional light.");
        private static readonly GUIContent s_ShadowMapResolutionLabel = EditorGUIUtility.TrTextContent("Cascade Resolution", "Width and height of each cascade slice in the CSM texture array.");
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
        private static readonly GUIContent s_BendSSSSettingsLabel = EditorGUIUtility.TrTextContent("Bend SSS");
        private static readonly GUIContent s_DirLightBendSSSMaxRayDistanceLabel = EditorGUIUtility.TrTextContent("Max Ray Distance", "Maximum Bend screen-space shadow trace distance in world units. A world-space limit keeps contact-shadow extent stable as camera distance changes.");
        private static readonly GUIContent s_DirLightBendSSSSurfaceThicknessLabel = EditorGUIUtility.TrTextContent("Surface Thickness", "Assumed screen-space caster thickness as a percentage of non-linear depth remaining to the far plane.");
        private static readonly GUIContent s_DirLightBendSSSBilinearThresholdLabel = EditorGUIUtility.TrTextContent("Bilinear Threshold", "Depth-difference threshold used to stop interpolation across detected edges.");
        private static readonly GUIContent s_DirLightBendSSSShadowContrastLabel = EditorGUIUtility.TrTextContent("Shadow Contrast", "Contrast boost applied to Bend screen-space shadow samples. Values greater than one darken contact transitions.");
        private static readonly GUIContent s_DirLightBendSSSIgnoreEdgePixelsLabel = EditorGUIUtility.TrTextContent("Ignore Edge Pixels", "Prevents detected edge pixels from casting Bend screen-space shadows.");
        private static readonly GUIContent s_DirLightBendSSSUsePrecisionOffsetLabel = EditorGUIUtility.TrTextContent("Precision Offset", "Applies Bend's small depth precision offset before tracing.");
        private static readonly GUIContent s_DirLightBendSSSBilinearSamplingOffsetModeLabel = EditorGUIUtility.TrTextContent("Bilinear Offset Mode", "Uses Bend's alternate bilinear sampling mode that offsets samples to the shared wavefront ray.");
        private static readonly GUIContent s_RayTracedShadowLabel = EditorGUIUtility.TrTextContent("Ray Traced Shadow");
        private static readonly GUIContent s_EnableRayTracedShadowLabel = EditorGUIUtility.TrTextContent("Enable");
        private static readonly GUIContent s_RayTracedShadowRayLengthLabel = EditorGUIUtility.TrTextContent("Ray Length");
        private static readonly GUIContent s_RayTracedShadowRayBiasLabel = EditorGUIUtility.TrTextContent("Ray Bias");
        private static readonly GUIContent s_RayTracedShadowDistantRayBiasLabel = EditorGUIUtility.TrTextContent("Distant Ray Bias");
        private static readonly GUIContent s_BarnDoorLabel = EditorGUIUtility.TrTextContent("Barn Door");
        private static readonly GUIContent s_BarnDoorAngleLabel = EditorGUIUtility.TrTextContent("Angle", "Angle in degrees of the rectangular area light barn doors.");
        private static readonly GUIContent s_BarnDoorLengthLabel = EditorGUIUtility.TrTextContent("Length", "Length of the rectangular area light barn door blades.");
        private static readonly GUIContent s_VolumetricLabel = EditorGUIUtility.TrTextContent("Volumetrics");
        private static readonly GUIContent s_AffectsVolumetricLabel = EditorGUIUtility.TrTextContent("Affect Volumetric");
        private static readonly GUIContent s_VolumetricDimmerLabel = EditorGUIUtility.TrTextContent("Dimmer", "Controls how much this light contributes to volumetric fog.");
        private static readonly GUIContent s_VolumetricFadeDistanceLabel = EditorGUIUtility.TrTextContent("Fade Distance", "Distance from the camera at which this local light stops contributing to volumetric fog.");
        private static readonly GUIContent s_VolumetricShadowDimmerLabel = EditorGUIUtility.TrTextContent("Shadow Dimmer", "Controls how strongly this light's shadows affect volumetric fog.");
        private static readonly GUIContent s_TimeOfDayLabel = EditorGUIUtility.TrTextContent("Time of Day");
        private static readonly GUIContent s_EnableTimeOfDayLabel = EditorGUIUtility.TrTextContent("Enable Time of Day");
        private static readonly GUIContent s_TimeOfDayValueLabel = EditorGUIUtility.TrTextContent("Time", "Controls the directional light azimuth and Lux intensity over a 24 hour day.");
        private static readonly GUIContent s_CelestialBodyLabel = EditorGUIUtility.TrTextContent("Celestial Body");
        private static readonly GUIContent s_InteractsWithSkyLabel = EditorGUIUtility.TrTextContent("Affect Physically Based Sky", "Check this option to make the light and the Physically Based sky affect one another.");
        private static readonly GUIContent s_AngularDiameterLabel = EditorGUIUtility.TrTextContent("Angular Diameter", "Angular diameter of the emissive celestial body represented by the light as seen from the camera (in degrees). Used to render the sun/moon disk and affects the sharpness of shadows.");
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
        private static readonly GUIContent[] s_ScreenSpaceShadowQualityOptionLabels =
        {
            EditorGUIUtility.TrTextContent("Low (PCF 3x3)"),
            EditorGUIUtility.TrTextContent("Medium (PCF 5x5)"),
            EditorGUIUtility.TrTextContent("High (PCF 7x7)"),
            EditorGUIUtility.TrTextContent("Very High (VividRP PCSS)"),
            EditorGUIUtility.TrTextContent("Very High (Unreal SSS)")
        };

        private static readonly int[] s_ScreenSpaceShadowQualityOptionValues =
        {
            (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low,
            (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Medium,
            (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.High,
            (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh,
            (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Unreal
        };
        private static readonly GUIContent[] s_ShadowMapResolutionOptionLabels =
        {
            EditorGUIUtility.TrTextContent("512"),
            EditorGUIUtility.TrTextContent("1024"),
            EditorGUIUtility.TrTextContent("2048"),
            EditorGUIUtility.TrTextContent("4096")
        };

        private static readonly int[] s_ShadowMapResolutionOptionValues =
        {
            512,
            1024,
            2048,
            4096
        };
        private static readonly GUIContent[] s_GeneralLightTypeOptionLabels =
        {
            EditorGUIUtility.TrTextContent("Spot"),
            EditorGUIUtility.TrTextContent("Directional"),
            EditorGUIUtility.TrTextContent("Point"),
            EditorGUIUtility.TrTextContent("Rectangle"),
            EditorGUIUtility.TrTextContent("Disc"),
            EditorGUIUtility.TrTextContent("Tube")
        };

        private static readonly int[] s_GeneralLightTypeOptionValues =
        {
            (int)GeneralLightType.Spot,
            (int)GeneralLightType.Directional,
            (int)GeneralLightType.Point,
            (int)GeneralLightType.Rectangle,
            (int)GeneralLightType.Disc,
            (int)GeneralLightType.Tube
        };

        private VividSerializedLight m_SerializedLight;
        private bool m_ShouldApplyTimeOfDay;
        private static MethodInfo s_TextureMiniThumbnailMethod;
        private static bool s_TextureMiniThumbnailMethodResolved;
        private static readonly Func<GUIContent, bool, bool, bool> s_DrawSubHeaderFoldout =
            CoreEditorUtils.DrawSubHeaderFoldout;

        protected override void OnEnable()
        {
            EnsureExpandedState();
            m_SerializedLight = new VividSerializedLight(serializedObject, settings);
            Undo.undoRedoPerformed += RebuildSerializedState;
        }

        protected void OnDisable()
        {
            Undo.undoRedoPerformed -= RebuildSerializedState;
        }

        protected override void OnSceneGUI()
        {
            if (target is not Light light)
                return;

            if (light.type != LightType.Box)
            {
                base.OnSceneGUI();
                return;
            }

            DrawBoxSpotLightSceneHandle(light);
        }

        public override void OnInspectorGUI()
        {
            m_SerializedLight.Update();
            m_ShouldApplyTimeOfDay = false;
            NormalizeSerializedLightIntensityUnit(applyImmediately: true);
            DrawBuiltInLightInspector();
            DrawVividInspector();
            m_SerializedLight.Apply();
            if (m_ShouldApplyTimeOfDay)
                ApplyTimeOfDayToSelectedLights();

            NormalizeSelectedLightIntensityUnits();
        }

        private void RebuildSerializedState()
        {
            m_SerializedLight = new VividSerializedLight(serializedObject, settings);
        }

        private void DrawBuiltInLightInspector()
        {
            if (DrawLightFoldout(Expandable.General, s_GeneralLabel))
                DrawGeneralInspector();

            if (DrawLightFoldout(Expandable.Shape, s_ShapeLabel))
                DrawShapeInspector();

            if (DrawLightFoldout(Expandable.Emission, s_EmissionLabel))
                DrawEmissionInspector();

            if (DrawLightFoldout(Expandable.Rendering, s_RenderingLabel))
                DrawRenderingInspector();

            if (DrawLightFoldout(Expandable.Shadows, s_ShadowsLabel))
                DrawShadowsInspector();
        }

        private void DrawGeneralInspector()
        {
            DrawGeneralLightTypeInspector();
            settings.DrawLightmapping();
        }

        private void DrawGeneralLightTypeInspector()
        {
            var currentLightType = settings.lightType.hasMultipleDifferentValues
                ? LightType.Spot
                : settings.lightType.GetEnumValue<LightType>();
            var generalLightType = GetGeneralLightType(currentLightType);
            var oldMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = settings.lightType.hasMultipleDifferentValues;

            EditorGUI.BeginChangeCheck();
            var selectedLightType = (GeneralLightType)EditorGUILayout.IntPopup(
                s_LightTypeLabel,
                (int)generalLightType,
                s_GeneralLightTypeOptionLabels,
                s_GeneralLightTypeOptionValues);
            EditorGUI.showMixedValue = oldMixedValue;

            if (!EditorGUI.EndChangeCheck())
                return;

            settings.lightType.SetEnumValue(GetLightTypeForGeneralLightType(selectedLightType));
            NormalizeSerializedLightIntensityUnit(applyImmediately: true, forceApply: true);
        }

        private void DrawEmissionHeaderFields()
        {
            LightUI.DrawColor(m_SerializedLight, this);
            if (ShouldDrawBoxSpotLightIntensity(settings))
                DrawBoxSpotLightIntensityField();
            else
                LightUI.DrawIntensity(m_SerializedLight, this);

            if (ShouldDrawCoreLightIntensityModifiers(settings))
                LightUI.DrawIntensityModifiers(m_SerializedLight);
        }

        private void DrawBoxSpotLightIntensityField()
        {
            if (VividLightIntensityUnitUtility.NormalizeBoxSpotLightUnit(settings))
            {
                settings.ApplyModifiedProperties();
                settings.Update();
                m_SerializedLight.Update();
            }

            LightUI.DrawIntensity(m_SerializedLight, this);
        }

        private void DrawShapeInspector()
        {
            if (settings.lightType.hasMultipleDifferentValues)
                return;

            switch (settings.lightType.GetEnumValue<LightType>())
            {
                case LightType.Point:
                    DrawPunctualShapeRadiusInspector();
                    break;
                case LightType.Spot:
                case LightType.Box:
                    DrawSpotShapeInspector();
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
            DrawEmissionHeaderFields();
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
                    case LightType.Directional:
                        settings.DrawBakedShadowAngle();
                        break;
                }
            }

            settings.DrawRuntimeShadow();
            DrawDirectionalShadowBiasInspector();
        }

        private void DrawPunctualShapeRadiusInspector()
        {
            if (!ShouldShowPunctualShapeRadiusControls(m_SerializedLight))
                return;

            EditorGUILayout.PropertyField(settings.shapeRadius, s_LightRadiusLabel);
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawBoxSpotLightGizmo(Light light, GizmoType gizmoType)
        {
            if ((gizmoType & (GizmoType.Selected | GizmoType.Active)) != 0)
                return;

            if (!ShouldDrawBoxSpotLightGizmo(light))
                return;

            var colorScale = light.enabled && light.gameObject.activeInHierarchy ? 1.0f : 0.35f;
            var wireColor = s_BoxSpotGizmoColor * colorScale;
            var behindColor = s_BoxSpotGizmoBehindColor * colorScale;
            var previousColor = Handles.color;
            var previousZTest = Handles.zTest;

            using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
            {
                Handles.zTest = CompareFunction.Greater;
                Handles.color = behindColor;
                DrawBoxSpotLightGizmoWireframe(light.areaSize.x, light.areaSize.y, light.range);

                Handles.zTest = CompareFunction.LessEqual;
                Handles.color = wireColor;
                DrawBoxSpotLightGizmoWireframe(light.areaSize.x, light.areaSize.y, light.range);
            }

            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        private static void DrawBoxSpotLightSceneHandle(Light light)
        {
            if (!ShouldDrawBoxSpotLightGizmo(light))
                return;

            var wireframeColor = light.enabled ? LightEditor.kGizmoLight : LightEditor.kGizmoDisabledLight;
            var wireframeColorBehind = GetBoxSpotLightBehindObjectWireframeColor(wireframeColor);
            var handleColor = GetBoxSpotLightHandleColor(wireframeColor);
            var handleColorBehind = GetBoxSpotLightHandleColor(wireframeColorBehind);
            var handleValues = new Vector3(light.areaSize.x, light.areaSize.y, light.range);
            var previousColor = Handles.color;
            var previousZTest = Handles.zTest;

            using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
            {
                Handles.zTest = CompareFunction.Greater;
                Handles.color = wireframeColorBehind;
                DrawBoxSpotLightGizmoWireframe(handleValues.x, handleValues.y, handleValues.z);

                Handles.zTest = CompareFunction.LessEqual;
                Handles.color = wireframeColor;
                DrawBoxSpotLightGizmoWireframe(handleValues.x, handleValues.y, handleValues.z);

                EditorGUI.BeginChangeCheck();
                Handles.zTest = CompareFunction.Greater;
                Handles.color = handleColorBehind;
                handleValues = DrawBoxSpotLightHandleSliders(handleValues);

                Handles.zTest = CompareFunction.LessEqual;
                Handles.color = handleColor;
                handleValues = DrawBoxSpotLightHandleSliders(handleValues);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(light, "Adjust Box Spot Light");
                    ApplyBoxSpotLightHandleValues(light, handleValues);
                }
            }

            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        internal static bool ShouldDrawBoxSpotLightGizmo(Light light)
        {
            return light != null
                && light.transform != null
                && light.type == LightType.Box;
        }

        internal static bool ShouldDrawBoxSpotLightIntensity(LightEditor.Settings settings)
        {
            if (settings == null)
                return false;

            if (settings.lightType.hasMultipleDifferentValues)
                return false;

            if (settings.light != null && settings.light.type == LightType.Box)
                return true;

            return settings.lightType.GetEnumValue<LightType>() == LightType.Box;
        }

        internal static bool ShouldDrawCoreLightIntensityModifiers(LightEditor.Settings settings)
        {
            return !ShouldDrawBoxSpotLightIntensity(settings);
        }

        internal static Vector3[] GetBoxSpotLightGizmoLocalCorners(float width, float height, float range)
        {
            var values = SanitizeBoxSpotLightHandleValues(new Vector3(width, height, range));
            var halfWidth = values.x * 0.5f;
            var halfHeight = values.y * 0.5f;
            var maxRange = values.z;
            var sizeX = new Vector3(halfWidth, 0.0f, 0.0f);
            var sizeY = new Vector3(0.0f, halfHeight, 0.0f);
            var nearCenter = Vector3.zero;
            var farCenter = new Vector3(0.0f, 0.0f, maxRange);

            return new[]
            {
                nearCenter + sizeX + sizeY,
                nearCenter - sizeX + sizeY,
                nearCenter - sizeX - sizeY,
                nearCenter + sizeX - sizeY,
                farCenter + sizeX + sizeY,
                farCenter - sizeX + sizeY,
                farCenter - sizeX - sizeY,
                farCenter + sizeX - sizeY
            };
        }

        internal static Vector3 SanitizeBoxSpotLightHandleValues(Vector3 widthHeightRange)
        {
            return new Vector3(
                Mathf.Max(widthHeightRange.x, k_MinBoxSpotLightHandleValue),
                Mathf.Max(widthHeightRange.y, k_MinBoxSpotLightHandleValue),
                Mathf.Max(widthHeightRange.z, k_MinBoxSpotLightHandleValue));
        }

        internal static void ApplyBoxSpotLightHandleValues(Light light, Vector3 widthHeightRange)
        {
            if (light == null)
                return;

            var sanitizedValues = SanitizeBoxSpotLightHandleValues(widthHeightRange);
            light.areaSize = new Vector2(sanitizedValues.x, sanitizedValues.y);
            light.range = sanitizedValues.z;

            if (light.TryGetComponent<VividAdditionalLightData>(out var additionalData))
                additionalData.NotifyLightDataChanged();

            EditorUtility.SetDirty(light);
        }

        private static Vector3 DrawBoxSpotLightHandleSliders(Vector3 widthHeightRange)
        {
            var sanitizedValues = SanitizeBoxSpotLightHandleValues(widthHeightRange);
            var halfWidth = sanitizedValues.x * 0.5f;
            var halfHeight = sanitizedValues.y * 0.5f;
            var range = SliderLineHandle(Vector3.zero, Vector3.forward, sanitizedValues.z);
            var farEnd = new Vector3(0.0f, 0.0f, Mathf.Max(range, k_MinBoxSpotLightHandleValue));

            EditorGUI.BeginChangeCheck();
            halfWidth = SliderLineHandle(farEnd, Vector3.right, halfWidth);
            halfWidth = SliderLineHandle(farEnd, Vector3.left, halfWidth);
            if (EditorGUI.EndChangeCheck())
                halfWidth = Mathf.Max(halfWidth, k_MinBoxSpotLightHandleValue * 0.5f);

            EditorGUI.BeginChangeCheck();
            halfHeight = SliderLineHandle(farEnd, Vector3.up, halfHeight);
            halfHeight = SliderLineHandle(farEnd, Vector3.down, halfHeight);
            if (EditorGUI.EndChangeCheck())
                halfHeight = Mathf.Max(halfHeight, k_MinBoxSpotLightHandleValue * 0.5f);

            return SanitizeBoxSpotLightHandleValues(new Vector3(halfWidth * 2.0f, halfHeight * 2.0f, range));
        }

        private static float SliderLineHandle(Vector3 position, Vector3 direction, float value)
        {
            var id = GUIUtility.GetControlID(FocusType.Passive);
            var handlePosition = position + direction * value;
            var handleSize = HandleUtility.GetHandleSize(handlePosition) * 0.03f;
            var guiChanged = GUI.changed;

            GUI.changed = false;
            handlePosition = Handles.Slider(id, handlePosition, direction, handleSize, Handles.DotHandleCap, 0.0f);
            if (GUI.changed)
                value = Vector3.Dot(handlePosition - position, direction);

            GUI.changed |= guiChanged;
            return value;
        }

        private static Color GetBoxSpotLightHandleColor(Color wireframeColor)
        {
            var color = wireframeColor;
            color.a = Mathf.Clamp01(color.a * 2.0f);
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        }

        private static Color GetBoxSpotLightBehindObjectWireframeColor(Color wireframeColor)
        {
            var color = wireframeColor;
            color.a = 0.2f;
            return color;
        }

        private static void DrawBoxSpotLightGizmoWireframe(float width, float height, float range)
        {
            var corners = GetBoxSpotLightGizmoLocalCorners(width, height, range);

            Handles.DrawLine(corners[0], corners[1]);
            Handles.DrawLine(corners[1], corners[2]);
            Handles.DrawLine(corners[2], corners[3]);
            Handles.DrawLine(corners[3], corners[0]);

            Handles.DrawLine(corners[4], corners[5]);
            Handles.DrawLine(corners[5], corners[6]);
            Handles.DrawLine(corners[6], corners[7]);
            Handles.DrawLine(corners[7], corners[4]);

            Handles.DrawLine(corners[0], corners[4]);
            Handles.DrawLine(corners[1], corners[5]);
            Handles.DrawLine(corners[2], corners[6]);
            Handles.DrawLine(corners[3], corners[7]);
        }

        private void DrawSpotShapeInspector()
        {
            if (!ShouldShowSpotShapeControls(m_SerializedLight))
                return;

            var shape = GetSpotLightShape(settings.lightType.GetEnumValue<LightType>());

            EditorGUI.BeginChangeCheck();
            shape = (SpotLightShape)EditorGUILayout.EnumPopup(s_SpotLightShapeLabel, shape);
            if (EditorGUI.EndChangeCheck())
            {
                settings.lightType.SetEnumValue(GetLightTypeForSpotLightShape(shape));
                NormalizeSerializedLightIntensityUnit(applyImmediately: true, forceApply: true);
            }

            using (new EditorGUI.IndentLevelScope())
            {
                switch (shape)
                {
                    case SpotLightShape.Cone:
                        DrawSpotConeShapeInspector();
                        break;
                    case SpotLightShape.Box:
                        DrawProjectorBoxShapeInspector();
                        break;
                }
            }
        }

        private void DrawSpotConeShapeInspector()
        {
            var oldSpotAngle = settings.spotAngle.floatValue;
            EditorGUI.BeginChangeCheck();
            settings.DrawInnerAndOuterSpotAngle();
            if (EditorGUI.EndChangeCheck())
                VividLightIntensityUnitUtility.PreserveSpotLightLumenIntensity(settings, oldSpotAngle);

            EditorGUILayout.PropertyField(settings.shapeRadius, s_LightRadiusLabel);
        }

        private void DrawProjectorBoxShapeInspector()
        {
            EditorGUILayout.PropertyField(settings.areaSizeX, s_BoxShapeWidthLabel);
            EditorGUILayout.PropertyField(settings.areaSizeY, s_BoxShapeHeightLabel);
            ClampProjectorBoxSize(settings.areaSizeX);
            ClampProjectorBoxSize(settings.areaSizeY);
        }

        private static void ClampProjectorBoxSize(SerializedProperty property)
        {
            if (property == null || property.hasMultipleDifferentValues)
                return;

            var clampedValue = Mathf.Max(k_MinBoxSpotLightHandleValue, property.floatValue);
            if (!Mathf.Approximately(property.floatValue, clampedValue))
                property.floatValue = clampedValue;
        }

        private void DrawVividInspector()
        {
            if (DrawLightFoldout(Expandable.Vivid, s_VividSettingsLabel))
                DrawVividGeneralInspector();

            DrawVolumetricInspector();
            DrawTimeOfDayInspector();
            DrawPhysicallyBasedSkyInspector();
            DrawRayTracedShadowInspector();
        }

        private void DrawVividGeneralInspector()
        {
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

        private void DrawTimeOfDayInspector()
        {
            if (!ShouldShowDirectionalTimeOfDayControls(m_SerializedLight))
                return;

            if (!DrawLightFoldout(Expandable.TimeOfDay, s_TimeOfDayLabel))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(m_SerializedLight.enableTimeOfDay, s_EnableTimeOfDayLabel);

                if (m_SerializedLight.enableTimeOfDay.hasMultipleDifferentValues
                    || m_SerializedLight.enableTimeOfDay.boolValue)
                {
                    EditorGUILayout.Slider(m_SerializedLight.timeOfDay, 0.0f, 24.0f, s_TimeOfDayValueLabel);
                }

                if (EditorGUI.EndChangeCheck())
                    m_ShouldApplyTimeOfDay = true;
            }
        }

        private void DrawDirectionalShadowBiasInspector()
        {
            if (!ShouldShowDirectionalShadowBiasControls(m_SerializedLight))
                return;

            if (!DrawLightSubFoldout(Expandable.CSMShadow, s_CSMShadowLabel))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                DrawDirectionalScreenSpaceShadowQualityField();
                DrawDirectionalPCSSFields();
                DrawDirectionalBendSSSFields();
                DrawDirectionalShadowMapResolutionField();
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

            if (!DrawLightSubFoldout(Expandable.BarnDoor, s_BarnDoorLabel))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.Slider(m_SerializedLight.barnDoorAngle, 0.0f, 90.0f, s_BarnDoorAngleLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.barnDoorLength, s_BarnDoorLengthLabel);
            }
        }

        private void DrawVolumetricInspector()
        {
            if (!DrawLightFoldout(Expandable.Volumetric, s_VolumetricLabel))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedLight.affectsVolumetric, s_AffectsVolumetricLabel);

                using (new EditorGUI.DisabledScope(
                           !m_SerializedLight.affectsVolumetric.hasMultipleDifferentValues
                           && !m_SerializedLight.affectsVolumetric.boolValue))
                {
                    EditorGUILayout.Slider(m_SerializedLight.volumetricDimmer, 0.0f, 16.0f, s_VolumetricDimmerLabel);
                    EditorGUILayout.Slider(m_SerializedLight.volumetricShadowDimmer, 0.0f, 1.0f, s_VolumetricShadowDimmerLabel);

                    if (ShouldShowVolumetricFadeDistanceControls(m_SerializedLight))
                        EditorGUILayout.PropertyField(m_SerializedLight.volumetricFadeDistance, s_VolumetricFadeDistanceLabel);
                }
            }
        }

        private void DrawDirectionalPCSSFields()
        {
            if (!ShouldShowDirectionalPCSSControls(m_SerializedLight))
                return;

            if (!DrawLightSubFoldout(Expandable.PCSS, s_PCSSSettingsLabel))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
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
        }

        private void DrawDirectionalBendSSSFields()
        {
            if (!ShouldShowDirectionalBendSSSControls(m_SerializedLight))
                return;

            if (!DrawLightSubFoldout(Expandable.BendSSS, s_BendSSSSettingsLabel))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.Slider(
                    m_SerializedLight.dirLightBendSSSMaxRayDistance,
                    VividAdditionalLightData.MinDirLightBendSSSMaxRayDistance,
                    VividAdditionalLightData.MaxDirLightBendSSSMaxRayDistance,
                    s_DirLightBendSSSMaxRayDistanceLabel);
                EditorGUILayout.Slider(
                    m_SerializedLight.dirLightBendSSSSurfaceThickness,
                    VividAdditionalLightData.MinDirLightBendSSSSurfaceThickness,
                    VividAdditionalLightData.MaxDirLightBendSSSSurfaceThickness,
                    s_DirLightBendSSSSurfaceThicknessLabel);
                EditorGUILayout.Slider(
                    m_SerializedLight.dirLightBendSSSBilinearThreshold,
                    VividAdditionalLightData.MinDirLightBendSSSBilinearThreshold,
                    VividAdditionalLightData.MaxDirLightBendSSSBilinearThreshold,
                    s_DirLightBendSSSBilinearThresholdLabel);
                EditorGUILayout.Slider(
                    m_SerializedLight.dirLightBendSSSShadowContrast,
                    VividAdditionalLightData.MinDirLightBendSSSShadowContrast,
                    VividAdditionalLightData.MaxDirLightBendSSSShadowContrast,
                    s_DirLightBendSSSShadowContrastLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.dirLightBendSSSIgnoreEdgePixels, s_DirLightBendSSSIgnoreEdgePixelsLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.dirLightBendSSSUsePrecisionOffset, s_DirLightBendSSSUsePrecisionOffsetLabel);
                EditorGUILayout.PropertyField(
                    m_SerializedLight.dirLightBendSSSBilinearSamplingOffsetMode,
                    s_DirLightBendSSSBilinearSamplingOffsetModeLabel);
            }
        }

        private void DrawDirectionalShadowMapResolutionField()
        {
            var property = m_SerializedLight.shadowMapResolution;
            var oldMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;

            EditorGUI.BeginChangeCheck();
            var resolution = EditorGUILayout.IntPopup(
                s_ShadowMapResolutionLabel,
                property.intValue,
                s_ShadowMapResolutionOptionLabels,
                s_ShadowMapResolutionOptionValues);
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

        internal static bool ShouldShowDirectionalBendSSSControls(VividSerializedLight serializedLight)
        {
            return ShouldShowDirectionalShadowBiasControls(serializedLight)
                && serializedLight?.screenSpaceShadowQuality != null
                && (serializedLight.screenSpaceShadowQuality.hasMultipleDifferentValues
                    || serializedLight.screenSpaceShadowQuality.intValue == (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Unreal);
        }

        internal static bool ShouldShowAreaBarnDoorControls(VividSerializedLight serializedLight)
        {
            return serializedLight != null
                && serializedLight.settings != null
                && !serializedLight.settings.lightType.hasMultipleDifferentValues
                && serializedLight.settings.light != null
                && serializedLight.settings.light.type == LightType.Rectangle;
        }

        internal static bool ShouldShowPunctualShapeRadiusControls(VividSerializedLight serializedLight)
        {
            if (serializedLight == null
                || serializedLight.settings == null
                || serializedLight.settings.lightType.hasMultipleDifferentValues
                || serializedLight.settings.light == null)
            {
                return false;
            }

            var lightType = serializedLight.settings.lightType.GetEnumValue<LightType>();
            return lightType == LightType.Point || lightType == LightType.Spot;
        }

        internal static bool ShouldShowSpotShapeControls(VividSerializedLight serializedLight)
        {
            return serializedLight != null
                && serializedLight.settings != null
                && !serializedLight.settings.lightType.hasMultipleDifferentValues
                && serializedLight.settings.light != null
                && IsSpotShapeLightType(serializedLight.settings.lightType.GetEnumValue<LightType>());
        }

        internal static SpotLightShape GetSpotLightShape(LightType lightType)
        {
            return lightType == LightType.Box ? SpotLightShape.Box : SpotLightShape.Cone;
        }

        internal static LightType GetLightTypeForSpotLightShape(SpotLightShape shape)
        {
            return shape == SpotLightShape.Box ? LightType.Box : LightType.Spot;
        }

        internal static GeneralLightType GetGeneralLightType(LightType lightType)
        {
            return lightType switch
            {
                LightType.Directional => GeneralLightType.Directional,
                LightType.Point => GeneralLightType.Point,
                LightType.Rectangle => GeneralLightType.Rectangle,
                LightType.Disc => GeneralLightType.Disc,
                LightType.Tube => GeneralLightType.Tube,
                _ => GeneralLightType.Spot,
            };
        }

        internal static LightType GetLightTypeForGeneralLightType(GeneralLightType lightType)
        {
            return lightType switch
            {
                GeneralLightType.Directional => LightType.Directional,
                GeneralLightType.Point => LightType.Point,
                GeneralLightType.Rectangle => LightType.Rectangle,
                GeneralLightType.Disc => LightType.Disc,
                GeneralLightType.Tube => LightType.Tube,
                _ => LightType.Spot,
            };
        }

        private static bool IsSpotShapeLightType(LightType lightType)
        {
            return lightType == LightType.Spot || lightType == LightType.Box;
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

        internal static bool ShouldShowDirectionalTimeOfDayControls(VividSerializedLight serializedLight)
        {
            return serializedLight != null
                && serializedLight.settings != null
                && !serializedLight.settings.lightType.hasMultipleDifferentValues
                && serializedLight.settings.light != null
                && serializedLight.settings.light.type == LightType.Directional;
        }

        internal static bool ShouldShowVolumetricFadeDistanceControls(VividSerializedLight serializedLight)
        {
            return serializedLight != null
                && serializedLight.settings != null
                && !serializedLight.settings.lightType.hasMultipleDifferentValues
                && serializedLight.settings.light != null
                && serializedLight.settings.light.type != LightType.Directional;
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

            if (!DrawLightFoldout(Expandable.CelestialBody, s_CelestialBodyLabel))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedLight.interactsWithSky, s_InteractsWithSkyLabel);

                using (new EditorGUI.DisabledScope(
                           !m_SerializedLight.interactsWithSky.hasMultipleDifferentValues
                           && !m_SerializedLight.interactsWithSky.boolValue))
                {
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

        private void DrawRayTracedShadowInspector()
        {
            if (!ShouldShowDirectionalRayTracedShadowControls(m_SerializedLight))
                return;

            if (!DrawLightFoldout(Expandable.RayTracedShadow, s_RayTracedShadowLabel))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedLight.enableRayTracedShadow, s_EnableRayTracedShadowLabel);

                if (!ShouldExpandDirectionalRayTracedShadowControls(m_SerializedLight))
                    return;

                EditorGUILayout.PropertyField(m_SerializedLight.rayTracedShadowRayLength, s_RayTracedShadowRayLengthLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.rayTracedShadowRayBias, s_RayTracedShadowRayBiasLabel);
                EditorGUILayout.PropertyField(m_SerializedLight.rayTracedShadowDistantRayBias, s_RayTracedShadowDistantRayBiasLabel);
            }
        }

        private static bool DrawLightFoldout(Expandable section, GUIContent label)
        {
            EnsureExpandedState();
            CoreEditorUtils.DrawSplitter();
            var wasExpanded = s_ExpandedState[section];
            var isExpanded = CoreEditorUtils.DrawHeaderFoldout(
                label,
                wasExpanded,
                customMenuContextAction: PopulateExpansionMenu);

            if (isExpanded != wasExpanded)
                s_ExpandedState[section] = isExpanded;

            return isExpanded;
        }

        private static bool DrawLightSubFoldout(Expandable section, GUIContent label)
        {
            EnsureExpandedState();
            var wasExpanded = s_ExpandedState[section];
            var isExpanded = s_DrawSubHeaderFoldout(label, wasExpanded, false);

            if (isExpanded != wasExpanded)
                s_ExpandedState[section] = isExpanded;

            return isExpanded;
        }

        private static void PopulateExpansionMenu(GenericMenu menu)
        {
            menu.AddItem(s_ExpandAllLabel, false, ExpandAllFoldouts);
            menu.AddItem(s_CollapseAllLabel, false, CollapseAllFoldouts);
        }

        private static void EnsureExpandedState()
        {
            s_ExpandedState ??= new ExpandedState<Expandable, VividLightEditor>(DefaultExpandedState, "VividRP");
        }

        private static void ExpandAllFoldouts()
        {
            EnsureExpandedState();
            s_ExpandedState.ExpandAll();
        }

        private static void CollapseAllFoldouts()
        {
            EnsureExpandedState();
            s_ExpandedState.CollapseAll();
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

        private bool NormalizeSerializedLightIntensityUnit(bool applyImmediately, bool forceApply = false)
        {
            var changed = VividLightIntensityUnitUtility.NormalizeUnsupportedLightUnit(settings);
            if ((changed || forceApply) && applyImmediately)
            {
                settings.ApplyModifiedProperties();
                settings.Update();
            }

            return changed;
        }

        private void ApplyTimeOfDayToSelectedLights()
        {
            if (m_SerializedLight?.lightsAdditionalData == null)
                return;

            foreach (var additionalData in m_SerializedLight.lightsAdditionalData)
            {
                if (additionalData == null || !additionalData.enableTimeOfDay)
                    continue;

                var targetLight = additionalData.light;
                if (targetLight == null)
                    continue;

                Undo.RecordObject(targetLight, "Apply Time of Day");
                Undo.RecordObject(targetLight.transform, "Apply Time of Day");
                additionalData.ApplyTimeOfDayToLight();
                additionalData.NotifyLightDataChanged();
                EditorUtility.SetDirty(targetLight);
                EditorUtility.SetDirty(targetLight.transform);
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

        internal static bool NormalizeUnsupportedLightUnit(LightEditor.Settings settings)
        {
            if (settings == null || settings.lightType.hasMultipleDifferentValues)
                return false;

            if (settings.lightUnit.hasMultipleDifferentValues)
                return false;

            var lightType = settings.lightType.GetEnumValue<LightType>();
            var lightUnit = settings.lightUnit.GetEnumValue<LightUnit>();
            if (LightUnitUtils.IsLightUnitSupported(lightType, lightUnit))
                return false;

            settings.lightUnit.SetEnumValue(LightUnitUtils.GetNativeLightUnit(lightType));
            if (lightType == LightType.Directional || lightType == LightType.Box)
                settings.luxAtDistance.floatValue = 1.0f;

            return true;
        }

        internal static bool NormalizeBoxSpotLightUnit(LightEditor.Settings settings)
        {
            if (settings == null)
                return false;

            var isBoxSpotLight = settings.light != null && settings.light.type == LightType.Box;
            if (!settings.lightType.hasMultipleDifferentValues)
                isBoxSpotLight |= settings.lightType.GetEnumValue<LightType>() == LightType.Box;

            if (!isBoxSpotLight)
                return false;

            var changed = false;
            if (settings.lightUnit.hasMultipleDifferentValues
                || settings.lightUnit.GetEnumValue<LightUnit>() != LightUnit.Lux)
            {
                settings.lightUnit.SetEnumValue(LightUnit.Lux);
                changed = true;
            }

            if (settings.luxAtDistance.hasMultipleDifferentValues
                || !Mathf.Approximately(settings.luxAtDistance.floatValue, 1.0f))
            {
                settings.luxAtDistance.floatValue = 1.0f;
                changed = true;
            }

            return changed;
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
