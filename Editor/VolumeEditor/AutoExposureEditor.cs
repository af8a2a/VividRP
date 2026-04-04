using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(AutoExposure))]
    internal sealed class AutoExposureEditor : VolumeComponentEditor
    {
        private const string StatsPreviewShaderName = "Hidden/VividRP/Editor/Auto Exposure Stats";

        private static readonly int PreviewStateId = Shader.PropertyToID("_PreviewState");
        private static readonly int StatusFlagsId = Shader.PropertyToID("_StatusFlags");
        private static readonly int HistogramMarkersId = Shader.PropertyToID("_HistogramMarkers");
        private static readonly int GaugeMarkersId = Shader.PropertyToID("_GaugeMarkers");
        private static readonly int PercentMarkersId = Shader.PropertyToID("_PercentMarkers");

        private static readonly GUIContent s_EnableLabel = EditorGUIUtility.TrTextContent("Enable");
        private static readonly GUIContent s_ModeLabel = EditorGUIUtility.TrTextContent("Mode");
        private static readonly GUIContent s_UsePhysicalCameraLabel = EditorGUIUtility.TrTextContent("Use Physical Camera");
        private static readonly GUIContent s_FixedExposureLabel = EditorGUIUtility.TrTextContent("Fixed Exposure");
        private static readonly GUIContent s_CompensationLabel = EditorGUIUtility.TrTextContent("Compensation");
        private static readonly GUIContent s_WeightTextureMaskLabel = EditorGUIUtility.TrTextContent("Weight Texture Mask");
        private static readonly GUIContent s_SpeedDarkToLightLabel = EditorGUIUtility.TrTextContent("Speed Dark to Light");
        private static readonly GUIContent s_SpeedLightToDarkLabel = EditorGUIUtility.TrTextContent("Speed Light to Dark");
        private static readonly GUIContent s_HistogramPercentagesLabel = EditorGUIUtility.TrTextContent("Histogram Percentages");
        private static readonly GUIContent s_HistogramPercentagesMinLabel = EditorGUIUtility.TrTextContent("Low Percent");
        private static readonly GUIContent s_HistogramPercentagesMaxLabel = EditorGUIUtility.TrTextContent("High Percent");
        private static readonly GUIContent s_HistogramEv100RangeLabel = EditorGUIUtility.TrTextContent("Histogram EV100 Range");

        private static GUIStyle s_StatsValueStyle;

        private SerializedDataParameter m_Enabled;
        private SerializedDataParameter m_Mode;
        private SerializedDataParameter m_Percent;
        private SerializedDataParameter m_MinEV100;
        private SerializedDataParameter m_MaxEV100;
        private SerializedDataParameter m_SpeedUp;
        private SerializedDataParameter m_SpeedDown;
        private SerializedDataParameter m_ManualEV100;
        private SerializedDataParameter m_ApplyPhysicalCameraExposure;
        private SerializedDataParameter m_ExposureCompensation;
        private SerializedDataParameter m_HistogramLogRange;
        private SerializedDataParameter m_MeterMask;

        private Rect m_StatsPreviewRect;
        private Material m_StatsPreviewMaterial;
        private RenderTexture m_StatsPreviewTexture;

        public override bool hasAdditionalProperties => true;

        private static LightUnitSliderUIDrawer k_LightUnitSlider;

        private static GUIStyle StatsValueStyle => s_StatsValueStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontStyle = FontStyle.Bold
        };

        private readonly struct AutoExposureStatsPreviewData
        {
            public readonly bool active;
            public readonly bool enabled;
            public readonly AutoExposureMode mode;
            public readonly bool applyPhysicalCameraExposure;
            public readonly bool hasPhysicalCameraPreview;
            public readonly bool meterMaskAssigned;
            public readonly float lowPercent;
            public readonly float highPercent;
            public readonly float minEV100;
            public readonly float maxEV100;
            public readonly Vector2 histogramEV100Range;
            public readonly float resolvedEV100;
            public readonly float resolvedAverageLuminance;
            public readonly float resolvedExposureScale;
            public readonly float exposureCompensationStops;
            public readonly float clampMinPosition;
            public readonly float clampMaxPosition;
            public readonly float averagePosition;
            public readonly float histogramWidth;
            public readonly float currentGaugePosition;
            public readonly float targetGaugePosition;
            public readonly float compensationGaugePosition;
            public readonly float evGaugePosition;
            public readonly string previewCameraName;

            public AutoExposureStatsPreviewData(
                bool active,
                bool enabled,
                AutoExposureMode mode,
                bool applyPhysicalCameraExposure,
                bool hasPhysicalCameraPreview,
                bool meterMaskAssigned,
                float lowPercent,
                float highPercent,
                float minEV100,
                float maxEV100,
                Vector2 histogramEV100Range,
                float resolvedEV100,
                float resolvedAverageLuminance,
                float resolvedExposureScale,
                float exposureCompensationStops,
                float clampMinPosition,
                float clampMaxPosition,
                float averagePosition,
                float histogramWidth,
                float currentGaugePosition,
                float targetGaugePosition,
                float compensationGaugePosition,
                float evGaugePosition,
                string previewCameraName)
            {
                this.active = active;
                this.enabled = enabled;
                this.mode = mode;
                this.applyPhysicalCameraExposure = applyPhysicalCameraExposure;
                this.hasPhysicalCameraPreview = hasPhysicalCameraPreview;
                this.meterMaskAssigned = meterMaskAssigned;
                this.lowPercent = lowPercent;
                this.highPercent = highPercent;
                this.minEV100 = minEV100;
                this.maxEV100 = maxEV100;
                this.histogramEV100Range = histogramEV100Range;
                this.resolvedEV100 = resolvedEV100;
                this.resolvedAverageLuminance = resolvedAverageLuminance;
                this.resolvedExposureScale = resolvedExposureScale;
                this.exposureCompensationStops = exposureCompensationStops;
                this.clampMinPosition = clampMinPosition;
                this.clampMaxPosition = clampMaxPosition;
                this.averagePosition = averagePosition;
                this.histogramWidth = histogramWidth;
                this.currentGaugePosition = currentGaugePosition;
                this.targetGaugePosition = targetGaugePosition;
                this.compensationGaugePosition = compensationGaugePosition;
                this.evGaugePosition = evGaugePosition;
                this.previewCameraName = previewCameraName;
            }
        }

        public override void OnEnable()
        {
            var o = new PropertyFetcher<AutoExposure>(serializedObject);
            m_Enabled = Unpack(o.Find(x => x.enabled));
            m_Mode = Unpack(o.Find(x => x.mode));
            m_Percent = Unpack(o.Find(x => x.percent));
            m_MinEV100 = Unpack(o.Find(x => x.minEV100));
            m_MaxEV100 = Unpack(o.Find(x => x.maxEV100));
            m_SpeedUp = Unpack(o.Find(x => x.speedUp));
            m_SpeedDown = Unpack(o.Find(x => x.speedDown));
            m_ManualEV100 = Unpack(o.Find(x => x.manualEV100));
            m_ApplyPhysicalCameraExposure = Unpack(o.Find(x => x.applyPhysicalCameraExposure));
            m_ExposureCompensation = Unpack(o.Find(x => x.exposureCompensation));
            m_HistogramLogRange = Unpack(o.Find(x => x.histogramLogRange));
            m_MeterMask = Unpack(o.Find(x => x.meterMask));

            k_LightUnitSlider = new LightUnitSliderUIDrawer();

            var shader = Shader.Find(StatsPreviewShaderName);
            if (shader != null)
                m_StatsPreviewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public override void OnDisable()
        {
            CoreUtils.Destroy(m_StatsPreviewMaterial);
            m_StatsPreviewMaterial = null;

            CoreUtils.Destroy(m_StatsPreviewTexture);
            m_StatsPreviewTexture = null;
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Enabled, s_EnableLabel);

            using (new EditorGUI.DisabledScope(!m_Enabled.value.boolValue))
            {
                PropertyField(m_Mode, s_ModeLabel);

                var mode = (AutoExposureMode)m_Mode.value.intValue;
                if (mode == AutoExposureMode.Manual)
                {
                    EditorGUILayout.Space();
                    DrawSectionHeader(m_ApplyPhysicalCameraExposure.value.boolValue ? "Physical Camera" : "Fixed");
                    PropertyField(m_ApplyPhysicalCameraExposure, s_UsePhysicalCameraLabel);
                    if (!m_ApplyPhysicalCameraExposure.value.boolValue)
                        PropertyField(m_ManualEV100, s_FixedExposureLabel);

                    PropertyField(m_ExposureCompensation, s_CompensationLabel);
                }
                else
                {
                    EditorGUILayout.Space();
                    DrawSectionHeader("Metering");
                    PropertyField(m_MeterMask, s_WeightTextureMaskLabel);

                    EditorGUILayout.Space();
                    DrawSectionHeader("Automatic Histogram");
                    DrawHistogramPercentages();
                    DoExposurePropertyField(m_MinEV100);
                    DoExposurePropertyField(m_MaxEV100);

                    PropertyField(m_ExposureCompensation, s_CompensationLabel);

                    EditorGUILayout.Space();
                    DrawSectionHeader("Adaptation");
                    PropertyField(m_SpeedUp, s_SpeedDarkToLightLabel);
                    PropertyField(m_SpeedDown, s_SpeedLightToDarkLabel);

                    EditorGUILayout.Space();
                    DrawSectionHeader("Histogram");
                    PropertyField(m_HistogramLogRange, s_HistogramEv100RangeLabel);
                }

                EditorGUILayout.Space();
                DrawSectionHeader("Monitor");
                DrawStatsPreview();
            }
        }

        private void DrawHistogramPercentages()
        {
            EditorGUILayout.LabelField(s_HistogramPercentagesLabel, EditorStyles.miniLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_Percent);
            }
        }

        private void DrawStatsPreview()
        {
            var previewData = BuildStatsPreviewData();

            if (m_StatsPreviewMaterial == null)
            {
                EditorGUILayout.HelpBox("Auto exposure stats preview shader is unavailable.", MessageType.Warning);
                return;
            }

            var previewHeight = previewData.mode == AutoExposureMode.Histogram ? 122f : 104f;
            m_StatsPreviewRect = GUILayoutUtility.GetRect(128f, previewHeight);
            m_StatsPreviewRect.xMin += EditorGUI.indentLevel * 15f;

            if (Event.current.type == EventType.Repaint)
            {
                ConfigureStatsPreview(previewData);
                CheckStatsPreviewTexture(Mathf.CeilToInt(m_StatsPreviewRect.width), Mathf.CeilToInt(m_StatsPreviewRect.height));

                var oldRenderTarget = RenderTexture.active;
                Graphics.Blit(null, m_StatsPreviewTexture, m_StatsPreviewMaterial);
                RenderTexture.active = oldRenderTarget;

                GUI.DrawTexture(m_StatsPreviewRect, m_StatsPreviewTexture);
            }

            Handles.DrawSolidRectangleWithOutline(
                m_StatsPreviewRect,
                Color.clear,
                EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.18f) : new Color(0f, 0f, 0f, 0.18f));

            if (previewData.mode == AutoExposureMode.Histogram)
                DrawHistogramRangeLabels(previewData);

            DrawStatsSummary(previewData);
        }

        private AutoExposureStatsPreviewData BuildStatsPreviewData()
        {
            var autoExposure = (AutoExposure)target;
            var mode = autoExposure.mode.value;
            var enabled = autoExposure.enabled.value;
            var active = autoExposure.IsActive();
            var applyPhysicalCameraExposure = autoExposure.applyPhysicalCameraExposure.value;
            var previewCamera = ResolvePreviewCamera();
            var hasPhysicalCameraPreview = applyPhysicalCameraExposure
                && previewCamera != null
                && previewCamera.usePhysicalProperties;

            var lowPercent = Mathf.Clamp(autoExposure.percent.min, 1f, 99f) * 0.01f;
            var highPercent = Mathf.Clamp(autoExposure.percent.max, 1f, 99f) * 0.01f;
            highPercent = Mathf.Max(lowPercent, highPercent);

            var histogramEV100Range = autoExposure.histogramLogRange.value;
            var histogramLogRange = AutoExposureSettingsResolver.ResolveHistogramLogRangeFromEV100(
                histogramEV100Range.x,
                histogramEV100Range.y);
            var histogramScaleBias = AutoExposureSettingsResolver.BuildHistogramScaleBias(
                histogramLogRange.x,
                histogramLogRange.y);
            var luminanceMin = Mathf.Pow(2f, histogramLogRange.x);

            var minEV100 = autoExposure.minEV100.value;
            var maxEV100 = Mathf.Max(minEV100, autoExposure.maxEV100.value);
            var minAverageLuminance = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(minEV100)
                * AutoExposureSettingsResolver.MiddleGrey;
            var maxAverageLuminance = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(maxEV100)
                * AutoExposureSettingsResolver.MiddleGrey;

            var resolvedEV100 = mode == AutoExposureMode.Manual
                ? ResolveManualPreviewEV100(autoExposure, previewCamera, hasPhysicalCameraPreview)
                : 0.5f * (minEV100 + maxEV100);
            var resolvedAverageLuminance = AutoExposureSettingsResolver.ResolveAverageSceneLuminanceFromEV100(resolvedEV100);
            var compensationLinear = AutoExposureSettingsResolver.ResolveExposureCompensation(autoExposure.exposureCompensation.value);
            var resolvedExposureScale = AutoExposureSettingsResolver.ResolveManualExposureScale(resolvedEV100, compensationLinear);

            var clampMinPosition = ResolveHistogramPositionFromLuminance(
                minAverageLuminance,
                histogramScaleBias.x,
                histogramScaleBias.y,
                luminanceMin);
            var clampMaxPosition = ResolveHistogramPositionFromLuminance(
                maxAverageLuminance,
                histogramScaleBias.x,
                histogramScaleBias.y,
                luminanceMin);
            var averagePosition = ResolveHistogramPositionFromLuminance(
                resolvedAverageLuminance,
                histogramScaleBias.x,
                histogramScaleBias.y,
                luminanceMin);
            var histogramWidth = Mathf.Max(Mathf.Abs(clampMaxPosition - clampMinPosition), 0.12f);

            return new AutoExposureStatsPreviewData(
                active,
                enabled,
                mode,
                applyPhysicalCameraExposure,
                hasPhysicalCameraPreview,
                autoExposure.meterMask.value != null,
                lowPercent,
                highPercent,
                minEV100,
                maxEV100,
                histogramEV100Range,
                resolvedEV100,
                resolvedAverageLuminance,
                resolvedExposureScale,
                autoExposure.exposureCompensation.value,
                clampMinPosition,
                clampMaxPosition,
                averagePosition,
                histogramWidth,
                ResolveExposureGaugePosition(resolvedExposureScale),
                ResolveExposureGaugePosition(resolvedExposureScale),
                ResolveCompensationGaugePosition(autoExposure.exposureCompensation.value),
                ResolveEvGaugePosition(resolvedEV100),
                previewCamera != null ? previewCamera.name : string.Empty);
        }

        private void ConfigureStatsPreview(AutoExposureStatsPreviewData previewData)
        {
            if (m_StatsPreviewMaterial == null)
                return;

            m_StatsPreviewMaterial.SetVector(
                PreviewStateId,
                new Vector4(
                    GUI.enabled ? 1f : 0.45f,
                    previewData.mode == AutoExposureMode.Manual ? 1f : 0f,
                    EditorGUIUtility.isProSkin ? 1f : 0f,
                    0f));
            m_StatsPreviewMaterial.SetVector(
                StatusFlagsId,
                new Vector4(
                    previewData.active ? 1f : 0f,
                    previewData.applyPhysicalCameraExposure ? 1f : 0f,
                    previewData.hasPhysicalCameraPreview ? 1f : 0f,
                    previewData.meterMaskAssigned ? 1f : 0f));
            m_StatsPreviewMaterial.SetVector(
                HistogramMarkersId,
                new Vector4(
                    previewData.clampMinPosition,
                    previewData.clampMaxPosition,
                    previewData.averagePosition,
                    previewData.histogramWidth));
            m_StatsPreviewMaterial.SetVector(
                GaugeMarkersId,
                new Vector4(
                    previewData.currentGaugePosition,
                    previewData.targetGaugePosition,
                    previewData.compensationGaugePosition,
                    previewData.evGaugePosition));
            m_StatsPreviewMaterial.SetVector(
                PercentMarkersId,
                new Vector4(
                    previewData.lowPercent,
                    previewData.highPercent,
                    previewData.enabled ? 1f : 0f,
                    0f));
        }

        private void DrawHistogramRangeLabels(AutoExposureStatsPreviewData previewData)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    $"Histogram Min {FormatEv100(previewData.histogramEV100Range.x)}",
                    EditorStyles.miniLabel,
                    GUILayout.MaxWidth(160f));
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    $"Histogram Max {FormatEv100(previewData.histogramEV100Range.y)}",
                    EditorStyles.miniLabel,
                    GUILayout.MaxWidth(160f));
            }
        }

        private void DrawStatsSummary(AutoExposureStatsPreviewData previewData)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawMetricRow(
                    "State",
                    previewData.active ? "Active" : previewData.enabled ? "Configured" : "Disabled",
                    "Compensation",
                    $"{previewData.exposureCompensationStops:+0.##;-0.##;0} EV");

                if (previewData.mode == AutoExposureMode.Histogram)
                {
                    DrawMetricRow(
                        "Clamp EV100",
                        $"{FormatEv100(previewData.minEV100)} -> {FormatEv100(previewData.maxEV100)}",
                        "Percent Window",
                        $"{previewData.lowPercent * 100f:0.#}% -> {previewData.highPercent * 100f:0.#}%");
                    DrawMetricRow(
                        "Preview Avg",
                        $"{previewData.resolvedAverageLuminance:0.###}",
                        "Exposure Scale",
                        $"{previewData.resolvedExposureScale:0.###}");
                    DrawMetricRow(
                        "Meter Mask",
                        previewData.meterMaskAssigned ? "Assigned" : "None",
                        "Preview EV100",
                        FormatEv100(previewData.resolvedEV100));
                }
                else
                {
                    DrawMetricRow(
                        "Manual EV100",
                        FormatEv100(previewData.resolvedEV100),
                        "Exposure Scale",
                        $"{previewData.resolvedExposureScale:0.###}");
                    DrawMetricRow(
                        "Average Luminance",
                        $"{previewData.resolvedAverageLuminance:0.###}",
                        "Meter Mask",
                        previewData.meterMaskAssigned ? "Assigned" : "None");
                    DrawMetricRow(
                        "Physical Camera",
                        ResolvePhysicalPreviewLabel(previewData),
                        "Preview Camera",
                        string.IsNullOrEmpty(previewData.previewCameraName) ? "None" : previewData.previewCameraName);
                }

                EditorGUILayout.LabelField(
                    "Inspector monitor is parameter-driven. Use Overlay Debug > Auto Exposure for live scene histogram.",
                    EditorStyles.miniLabel);
            }

            if (previewData.mode == AutoExposureMode.Manual
                && previewData.applyPhysicalCameraExposure
                && !previewData.hasPhysicalCameraPreview)
            {
                EditorGUILayout.HelpBox(
                    "Physical Camera preview falls back to Manual EV100 until an enabled Game camera with physical properties is available.",
                    MessageType.Info);
            }
        }

        private static void DrawMetricRow(string leftLabel, string leftValue, string rightLabel, string rightValue)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawMetric(leftLabel, leftValue);
                GUILayout.Space(10f);
                DrawMetric(rightLabel, rightValue);
            }
        }

        private static void DrawMetric(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandWidth(true)))
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniLabel, GUILayout.Width(106f));
                EditorGUILayout.LabelField(value, StatsValueStyle, GUILayout.MinWidth(72f));
            }
        }

        private static string ResolvePhysicalPreviewLabel(AutoExposureStatsPreviewData previewData)
        {
            if (!previewData.applyPhysicalCameraExposure)
                return "Off";

            return previewData.hasPhysicalCameraPreview ? "Bound" : "Fallback";
        }

        private static float ResolveManualPreviewEV100(
            AutoExposure autoExposure,
            Camera previewCamera,
            bool hasPhysicalCameraPreview)
        {
            if (autoExposure.mode.value != AutoExposureMode.Manual)
                return autoExposure.manualEV100.value;

            if (!autoExposure.applyPhysicalCameraExposure.value || !hasPhysicalCameraPreview)
                return autoExposure.manualEV100.value;

            return AutoExposureSettingsResolver.ResolvePhysicalCameraEV100(previewCamera);
        }

        private static Camera ResolvePreviewCamera()
        {
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
            Camera fallback = null;

            foreach (var camera in cameras)
            {
                if (camera == null || camera.cameraType != CameraType.Game)
                    continue;

                if (camera.enabled)
                    return camera;

                fallback ??= camera;
            }

            return fallback;
        }

        private static float ResolveHistogramPositionFromLuminance(
            float luminance,
            float histogramScale,
            float histogramBias,
            float luminanceMin)
        {
            var resolvedLuminance = Mathf.Max(luminance, Mathf.Max(luminanceMin, 1e-4f));
            return Mathf.Clamp01(Mathf.Log(resolvedLuminance, 2f) * histogramScale + histogramBias);
        }

        private static float ResolveExposureGaugePosition(float exposureScale)
        {
            const float gaugeLogRange = 12f;
            var logExposureScale = Mathf.Log(Mathf.Max(exposureScale, 1e-4f), 2f);
            return Mathf.Clamp01(0.5f + logExposureScale / gaugeLogRange);
        }

        private static float ResolveCompensationGaugePosition(float compensationStops)
        {
            return Mathf.Clamp01(0.5f + compensationStops / 12f);
        }

        private static float ResolveEvGaugePosition(float ev100)
        {
            return Mathf.Clamp01(0.5f + ev100 / 24f);
        }

        private static string FormatEv100(float value)
        {
            return value.ToString("0.##");
        }

        private void CheckStatsPreviewTexture(int width, int height)
        {
            if (m_StatsPreviewTexture != null
                && m_StatsPreviewTexture.IsCreated()
                && m_StatsPreviewTexture.width == width
                && m_StatsPreviewTexture.height == height)
            {
                return;
            }

            CoreUtils.Destroy(m_StatsPreviewTexture);
            m_StatsPreviewTexture = new RenderTexture(width, height, 0, GraphicsFormat.R8G8B8A8_SRGB)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static void DrawSectionHeader(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private void DoExposurePropertyField(SerializedDataParameter exposureProperty)
        {
            using (var scope = new OverridablePropertyScope(exposureProperty, exposureProperty.displayName, this))
            {
                if (!scope.displayed)
                    return;

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(scope.label);

                    var xOffset = EditorGUIUtility.labelWidth + 2;

                    var lineRect = EditorGUILayout.GetControlRect();
                    lineRect.x += xOffset;
                    lineRect.width -= xOffset;

                    var sliderRect = lineRect;
                    sliderRect.y -= EditorGUIUtility.singleLineHeight;
                    k_LightUnitSlider.SetSerializedObject(serializedObject);
                    k_LightUnitSlider.DrawExposureSlider(exposureProperty.value, sliderRect);

                    lineRect.x -= EditorGUIUtility.labelWidth + 2;
                    lineRect.y += EditorGUIUtility.standardVerticalSpacing;
                    lineRect.width += EditorGUIUtility.labelWidth + 2;
                    EditorGUI.PropertyField(lineRect, exposureProperty.value, EditorGUIUtility.TrTextContent(" "));
                }
            }
        }
    }
}
