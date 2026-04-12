using System;
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
        private const int HistogramBucketCount = 64;
        private const string StatsPreviewShaderName = "Hidden/VividRP/Editor/Auto Exposure Stats";

        private static readonly int PreviewStateId = Shader.PropertyToID("_PreviewState");
        private static readonly int StatusFlagsId = Shader.PropertyToID("_StatusFlags");
        private static readonly int HistogramMarkersId = Shader.PropertyToID("_HistogramMarkers");
        private static readonly int GaugeMarkersId = Shader.PropertyToID("_GaugeMarkers");
        private static readonly int PercentMarkersId = Shader.PropertyToID("_PercentMarkers");
        private static readonly int HistogramSamplesId = Shader.PropertyToID("_HistogramSamples");

        private static readonly GUIContent s_EnableLabel = EditorGUIUtility.TrTextContent("Enable");
        private static readonly GUIContent s_PresetLabel = EditorGUIUtility.TrTextContent("Preset");
        private static readonly GUIContent s_ApplyPresetLabel = EditorGUIUtility.TrTextContent("Apply Preset");
        private static readonly GUIContent s_ModeLabel = EditorGUIUtility.TrTextContent("Mode");
        private static readonly GUIContent s_FixedExposureLabel = EditorGUIUtility.TrTextContent("Fixed Exposure");
        private static readonly GUIContent s_LimitMinLabel = EditorGUIUtility.TrTextContent("Limit Min");
        private static readonly GUIContent s_LimitMaxLabel = EditorGUIUtility.TrTextContent("Limit Max");
        private static readonly GUIContent s_CompensationLabel = EditorGUIUtility.TrTextContent("Compensation");
        private static readonly GUIContent s_CompensationCurveLabel = EditorGUIUtility.TrTextContent("Compensation Curve");
        private static readonly GUIContent s_CurveMapLabel = EditorGUIUtility.TrTextContent("Curve Map");
        private static readonly GUIContent s_MeteringModeLabel = EditorGUIUtility.TrTextContent("Metering Mode");
        private static readonly GUIContent s_AdaptationModeLabel = EditorGUIUtility.TrTextContent("Adaptation Mode");
        private static readonly GUIContent s_TargetMidGrayLabel = EditorGUIUtility.TrTextContent("Target Mid Gray");
        private static readonly GUIContent s_WeightTextureMaskLabel = EditorGUIUtility.TrTextContent("Weight Texture Mask");
        private static readonly GUIContent s_SpeedDarkToLightLabel = EditorGUIUtility.TrTextContent("Speed Dark to Light");
        private static readonly GUIContent s_SpeedLightToDarkLabel = EditorGUIUtility.TrTextContent("Speed Light to Dark");
        private static readonly GUIContent s_HistogramPercentagesLabel = EditorGUIUtility.TrTextContent("Histogram Percentages");
        private static readonly GUIContent s_HistogramPercentagesMinLabel = EditorGUIUtility.TrTextContent("Low Percent");
        private static readonly GUIContent s_HistogramPercentagesMaxLabel = EditorGUIUtility.TrTextContent("High Percent");
        private static readonly GUIContent s_HistogramEv100RangeLabel = EditorGUIUtility.TrTextContent("Histogram EV100 Range");

        private static GUIStyle s_StatsValueStyle;
        private static readonly AutoExposureCommonPreset[] s_PresetValues = (AutoExposureCommonPreset[])Enum.GetValues(typeof(AutoExposureCommonPreset));
        private static readonly GUIContent[] s_PresetOptions = BuildPresetOptions();

        private SerializedDataParameter m_Enabled;
        private SerializedDataParameter m_Mode;
        private SerializedDataParameter m_Percent;
        private SerializedDataParameter m_MinEV100;
        private SerializedDataParameter m_MaxEV100;
        private SerializedDataParameter m_SpeedUp;
        private SerializedDataParameter m_SpeedDown;
        private SerializedDataParameter m_ManualEV100;
        private SerializedDataParameter m_MeteringMode;
        private SerializedDataParameter m_AdaptationMode;
        private SerializedDataParameter m_TargetMidGray;
        private SerializedDataParameter m_ApplyPhysicalCameraExposure;
        private SerializedDataParameter m_ExposureCompensation;
        private SerializedDataParameter m_ExposureCompensationCurve;
        private SerializedDataParameter m_CurveMap;
        private SerializedDataParameter m_HistogramLogRange;
        private SerializedDataParameter m_MeterMask;

        private Rect m_StatsPreviewRect;
        private Material m_StatsPreviewMaterial;
        private RenderTexture m_StatsPreviewTexture;
        private readonly float[] m_HistogramPreviewSamples = new float[HistogramBucketCount];
        private AutoExposureCommonPreset m_SelectedPreset = AutoExposureCommonPreset.HistogramBalanced;

        public override bool hasAdditionalProperties => true;

        private static LightUnitSliderUIDrawer k_LightUnitSlider;

        private static GUIStyle StatsValueStyle => s_StatsValueStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontStyle = FontStyle.Bold
        };

        private readonly struct AutoExposureStatsPreviewData
        {
            public readonly bool usingLiveStats;
            public readonly bool hasLiveHistogram;
            public readonly bool active;
            public readonly bool enabled;
            public readonly AutoExposureExposureMode mode;
            public readonly bool applyPhysicalCameraExposure;
            public readonly bool hasPhysicalCameraPreview;
            public readonly bool meterMaskAssigned;
            public readonly bool hasValidHistory;
            public readonly float lowPercent;
            public readonly float highPercent;
            public readonly float minEV100;
            public readonly float maxEV100;
            public readonly Vector2 histogramEV100Range;
            public readonly float resolvedEV100;
            public readonly float resolvedAverageLuminance;
            public readonly float resolvedExposureScale;
            public readonly float targetExposureScale;
            public readonly float exposureCompensationSettingsStops;
            public readonly float exposureCompensationCurveStops;
            public readonly float exposureCompensationAllStops;
            public readonly float clampMinPosition;
            public readonly float clampMaxPosition;
            public readonly float averagePosition;
            public readonly float histogramWidth;
            public readonly float currentGaugePosition;
            public readonly float targetGaugePosition;
            public readonly float compensationGaugePosition;
            public readonly float evGaugePosition;
            public readonly string previewCameraName;
            public readonly int liveFrameIndex;

            public AutoExposureStatsPreviewData(
                bool usingLiveStats,
                bool hasLiveHistogram,
                bool active,
                bool enabled,
                AutoExposureExposureMode mode,
                bool applyPhysicalCameraExposure,
                bool hasPhysicalCameraPreview,
                bool meterMaskAssigned,
                bool hasValidHistory,
                float lowPercent,
                float highPercent,
                float minEV100,
                float maxEV100,
                Vector2 histogramEV100Range,
                float resolvedEV100,
                float resolvedAverageLuminance,
                float resolvedExposureScale,
                float targetExposureScale,
                float exposureCompensationSettingsStops,
                float exposureCompensationCurveStops,
                float exposureCompensationAllStops,
                float clampMinPosition,
                float clampMaxPosition,
                float averagePosition,
                float histogramWidth,
                float currentGaugePosition,
                float targetGaugePosition,
                float compensationGaugePosition,
                float evGaugePosition,
                string previewCameraName,
                int liveFrameIndex)
            {
                this.usingLiveStats = usingLiveStats;
                this.hasLiveHistogram = hasLiveHistogram;
                this.active = active;
                this.enabled = enabled;
                this.mode = mode;
                this.applyPhysicalCameraExposure = applyPhysicalCameraExposure;
                this.hasPhysicalCameraPreview = hasPhysicalCameraPreview;
                this.meterMaskAssigned = meterMaskAssigned;
                this.hasValidHistory = hasValidHistory;
                this.lowPercent = lowPercent;
                this.highPercent = highPercent;
                this.minEV100 = minEV100;
                this.maxEV100 = maxEV100;
                this.histogramEV100Range = histogramEV100Range;
                this.resolvedEV100 = resolvedEV100;
                this.resolvedAverageLuminance = resolvedAverageLuminance;
                this.resolvedExposureScale = resolvedExposureScale;
                this.targetExposureScale = targetExposureScale;
                this.exposureCompensationSettingsStops = exposureCompensationSettingsStops;
                this.exposureCompensationCurveStops = exposureCompensationCurveStops;
                this.exposureCompensationAllStops = exposureCompensationAllStops;
                this.clampMinPosition = clampMinPosition;
                this.clampMaxPosition = clampMaxPosition;
                this.averagePosition = averagePosition;
                this.histogramWidth = histogramWidth;
                this.currentGaugePosition = currentGaugePosition;
                this.targetGaugePosition = targetGaugePosition;
                this.compensationGaugePosition = compensationGaugePosition;
                this.evGaugePosition = evGaugePosition;
                this.previewCameraName = previewCameraName;
                this.liveFrameIndex = liveFrameIndex;
            }
        }

        public override void OnEnable()
        {
            var o = new PropertyFetcher<AutoExposure>(serializedObject);
            var exposureModeProperty = o.Find(x => x.exposureMode);

            m_Enabled = Unpack(o.Find(x => x.enabled));
            m_Mode = exposureModeProperty != null
                ? Unpack(exposureModeProperty)
                : Unpack(o.Find(x => x.mode));
            m_Percent = Unpack(o.Find(x => x.percent));
            m_MinEV100 = Unpack(o.Find(x => x.minEV100));
            m_MaxEV100 = Unpack(o.Find(x => x.maxEV100));
            m_SpeedUp = Unpack(o.Find(x => x.speedUp));
            m_SpeedDown = Unpack(o.Find(x => x.speedDown));
            m_ManualEV100 = Unpack(o.Find(x => x.manualEV100));
            m_MeteringMode = Unpack(o.Find(x => x.meteringMode));
            m_AdaptationMode = Unpack(o.Find(x => x.adaptationMode));
            m_TargetMidGray = Unpack(o.Find(x => x.targetMidGray));
            m_ApplyPhysicalCameraExposure = Unpack(o.Find(x => x.applyPhysicalCameraExposure));
            m_ExposureCompensation = Unpack(o.Find(x => x.exposureCompensation));
            m_ExposureCompensationCurve = Unpack(o.Find(x => x.exposureCompensationCurve));
            m_CurveMap = Unpack(o.Find(x => x.curveMap));
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
            DrawPresetControls();

            using (new EditorGUI.DisabledScope(!m_Enabled.value.boolValue))
            {
                PropertyField(m_Mode, s_ModeLabel);
                DrawHdrpModeInspector(ResolveSelectedExposureMode());

                EditorGUILayout.Space();
                DrawSectionHeader("Monitor");
                DrawStatsPreview();
            }
        }

        private void DrawPresetControls()
        {
            EditorGUILayout.Space();
            DrawSectionHeader("Presets");

            var selectedPresetIndex = ResolvePresetIndex(m_SelectedPreset);
            using (new EditorGUILayout.HorizontalScope())
            {
                var newPresetIndex = EditorGUILayout.Popup(s_PresetLabel, selectedPresetIndex, s_PresetOptions);
                if (newPresetIndex >= 0 && newPresetIndex < s_PresetValues.Length)
                    m_SelectedPreset = s_PresetValues[newPresetIndex];

                if (GUILayout.Button(s_ApplyPresetLabel, GUILayout.MaxWidth(108f)))
                    ApplySelectedPreset();
            }

            var preset = AutoExposureCommonPresets.Get(m_SelectedPreset);
            EditorGUILayout.HelpBox(preset.Description, MessageType.None);
        }

        private void DrawHdrpModeInspector(AutoExposureExposureMode mode)
        {
            DrawModeSupportInfo(mode);

            switch (mode)
            {
                case AutoExposureExposureMode.Fixed:
                    EditorGUILayout.Space();
                    DrawSectionHeader("Fixed");
                    DoExposurePropertyField(m_ManualEV100, s_FixedExposureLabel);
                    PropertyField(m_ExposureCompensation, s_CompensationLabel);
                    PropertyField(m_ExposureCompensationCurve, s_CompensationCurveLabel);
                    break;

                case AutoExposureExposureMode.UsePhysicalCamera:
                    EditorGUILayout.Space();
                    DrawSectionHeader("Physical Camera");
                    PropertyField(m_ExposureCompensation, s_CompensationLabel);
                    PropertyField(m_ExposureCompensationCurve, s_CompensationCurveLabel);
                    break;

                case AutoExposureExposureMode.CurveMapping:
                    DrawAutomaticInspector(drawHistogramControls: false);

                    EditorGUILayout.Space();
                    DrawSectionHeader("Curve Mapping");
                    PropertyField(m_CurveMap, s_CurveMapLabel);
                    break;

                case AutoExposureExposureMode.AutomaticHistogram:
                    DrawAutomaticInspector(drawHistogramControls: true);
                    break;

                default:
                    DrawAutomaticInspector(drawHistogramControls: false);
                    break;
            }
        }

        private void DrawAutomaticInspector(bool drawHistogramControls)
        {
            EditorGUILayout.Space();
            DrawSectionHeader("Metering");
            PropertyField(m_MeteringMode, s_MeteringModeLabel);

            if ((AutoExposureMeteringMode)m_MeteringMode.value.intValue == AutoExposureMeteringMode.MaskWeighted)
                PropertyField(m_MeterMask, s_WeightTextureMaskLabel);

            EditorGUILayout.Space();
            DrawSectionHeader(drawHistogramControls ? "Automatic Histogram" : "Automatic");
            DoExposurePropertyField(m_MinEV100, s_LimitMinLabel);
            DoExposurePropertyField(m_MaxEV100, s_LimitMaxLabel);
            PropertyField(m_TargetMidGray, s_TargetMidGrayLabel);
            PropertyField(m_ExposureCompensation, s_CompensationLabel);
            PropertyField(m_ExposureCompensationCurve, s_CompensationCurveLabel);

            if (drawHistogramControls)
            {
                DrawHistogramPercentages();

                EditorGUILayout.Space();
                DrawSectionHeader("Histogram");
                PropertyField(m_HistogramLogRange, s_HistogramEv100RangeLabel);
            }

            EditorGUILayout.Space();
            DrawSectionHeader("Adaptation");
            PropertyField(m_AdaptationMode, s_AdaptationModeLabel);

            if ((AutoExposureAdaptationMode)m_AdaptationMode.value.intValue == AutoExposureAdaptationMode.Progressive)
            {
                PropertyField(m_SpeedUp, s_SpeedDarkToLightLabel);
                PropertyField(m_SpeedDown, s_SpeedLightToDarkLabel);
            }
        }

        private void DrawModeSupportInfo(AutoExposureExposureMode mode)
        {
            if (mode == AutoExposureExposureMode.CurveMapping)
            {
                EditorGUILayout.HelpBox(
                    "Curve Mapping now uses HDRP-style curve remapping at runtime. The curve is baked into a runtime texture, and Limit Min/Max still define the final exposure clamp range.",
                    MessageType.Info);
                return;
            }

            if (mode == AutoExposureExposureMode.AutomaticHistogram)
            {
                EditorGUILayout.HelpBox(
                    "Automatic Histogram now runs through a dedicated HDRP histogram path. It no longer falls back to Unreal auto exposure or dispatches HDRP's KPrePass/KReduction average-luminance chain.",
                    MessageType.Info);
            }
        }

        private AutoExposureExposureMode ResolveSelectedExposureMode()
        {
            return Enum.IsDefined(typeof(AutoExposureExposureMode), m_Mode.value.intValue)
                ? (AutoExposureExposureMode)m_Mode.value.intValue
                : AutoExposureExposureMode.Automatic;
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

            var previewHeight = AutoExposureExposureModeUtility.UsesManualSettings(previewData.mode) ? 104f : 122f;
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

            if (!AutoExposureExposureModeUtility.UsesManualSettings(previewData.mode))
                DrawHistogramRangeLabels(previewData);

            DrawStatsSummary(previewData);
        }

        private AutoExposureStatsPreviewData BuildStatsPreviewData()
        {
            AutoExposureStatsReadbackBridge.TouchInspectorRequest();
            if (AutoExposureStatsReadbackBridge.TryGetLatestSnapshot(out var snapshot))
                return BuildLiveStatsPreviewData(snapshot);

            return BuildFallbackStatsPreviewData();
        }

        private AutoExposureStatsPreviewData BuildLiveStatsPreviewData(AutoExposureStatsReadbackSnapshot snapshot)
        {
            var settings = snapshot.settings;
            var mode = settings.exposureMode;
            var lowPercent = Mathf.Clamp01(settings.exposureLowPercent);
            var highPercent = Mathf.Clamp(Mathf.Max(lowPercent, settings.exposureHighPercent), 0f, 1f);

            var histogramEV100Range = ResolveHistogramEv100RangeFromSettings(settings);
            var minAverageLuminance = Mathf.Max(settings.minAverageLuminance, 1e-4f);
            var maxAverageLuminance = Mathf.Max(minAverageLuminance, settings.maxAverageLuminance);
            var minEV100 = ResolveEv100FromAverageSceneLuminance(minAverageLuminance);
            var maxEV100 = ResolveEv100FromAverageSceneLuminance(maxAverageLuminance);

            var fallbackAverageLuminance = AutoExposureExposureModeUtility.UsesManualSettings(mode)
                ? Mathf.Max(settings.manualAverageSceneLuminance, 1e-4f)
                : Mathf.Max(0.5f * (minAverageLuminance + maxAverageLuminance), 1e-4f);
            var fallbackExposureScale = AutoExposureExposureModeUtility.UsesManualSettings(mode)
                ? Mathf.Max(settings.fixedExposureScale, 1e-4f)
                : 1f;
            var exposureState = snapshot.hasExposureState
                ? snapshot.exposureState
                : new Vector4(fallbackExposureScale, fallbackExposureScale, fallbackAverageLuminance, Mathf.Max(settings.exposureCompensationAll, 1e-4f));

            var resolvedAverageLuminance = Mathf.Max(exposureState.z, 1e-4f);
            var resolvedEV100 = ResolveEv100FromAverageSceneLuminance(resolvedAverageLuminance);
            var currentExposureScale = Mathf.Max(exposureState.x, 1e-4f);
            var targetExposureScale = Mathf.Max(exposureState.y, 1e-4f);
            var compensationSettingsStops = ResolveCompensationStops(settings.exposureCompensationSettings);
            var compensationAllStops = ResolveCompensationStops(Mathf.Max(exposureState.w, 1e-4f));
            var compensationCurveStops = compensationAllStops - compensationSettingsStops;

            var clampMinPosition = ResolveHistogramPositionFromLuminance(
                minAverageLuminance,
                settings.histogramScale,
                settings.histogramBias,
                settings.luminanceMin);
            var clampMaxPosition = ResolveHistogramPositionFromLuminance(
                maxAverageLuminance,
                settings.histogramScale,
                settings.histogramBias,
                settings.luminanceMin);
            var averagePosition = ResolveHistogramPositionFromLuminance(
                resolvedAverageLuminance,
                settings.histogramScale,
                settings.histogramBias,
                settings.luminanceMin);
            var histogramWidth = Mathf.Max(Mathf.Abs(clampMaxPosition - clampMinPosition), 0.12f);

            PopulateHistogramSamplesFromSnapshot(snapshot, averagePosition, histogramWidth);

            return new AutoExposureStatsPreviewData(
                true,
                snapshot.hasHistogram,
                snapshot.exposureEnabled,
                settings.enabled,
                mode,
                AutoExposureExposureModeUtility.UsesPhysicalCamera(mode),
                AutoExposureExposureModeUtility.UsesPhysicalCamera(mode),
                settings.meterMask != null,
                snapshot.hasValidHistory,
                lowPercent,
                highPercent,
                minEV100,
                maxEV100,
                histogramEV100Range,
                resolvedEV100,
                resolvedAverageLuminance,
                currentExposureScale,
                targetExposureScale,
                compensationSettingsStops,
                compensationCurveStops,
                compensationAllStops,
                clampMinPosition,
                clampMaxPosition,
                averagePosition,
                histogramWidth,
                ResolveExposureGaugePosition(currentExposureScale),
                ResolveExposureGaugePosition(targetExposureScale),
                ResolveCompensationGaugePosition(compensationAllStops),
                ResolveEvGaugePosition(resolvedEV100),
                snapshot.cameraName,
                snapshot.frameIndex);
        }

        private AutoExposureStatsPreviewData BuildFallbackStatsPreviewData()
        {
            var autoExposure = (AutoExposure)target;
            var mode = autoExposure.ResolveExposureMode();
            var enabled = autoExposure.enabled.value;
            var active = autoExposure.IsActive();
            var applyPhysicalCameraExposure = AutoExposureExposureModeUtility.UsesPhysicalCamera(mode);
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

            var resolvedEV100 = AutoExposureExposureModeUtility.UsesManualSettings(mode)
                ? ResolveManualPreviewEV100(autoExposure, previewCamera, hasPhysicalCameraPreview)
                : 0.5f * (minEV100 + maxEV100);
            var resolvedAverageLuminance = AutoExposureSettingsResolver.ResolveAverageSceneLuminanceFromEV100(resolvedEV100);
            var compensationSettingsStops = autoExposure.exposureCompensation.value;
            var compensationCurveStops = AutoExposureSettingsResolver.ResolveExposureCompensationCurveStops(
                autoExposure.exposureCompensationCurve.value,
                resolvedEV100);
            var compensationSettingsLinear = AutoExposureSettingsResolver.ResolveExposureCompensation(compensationSettingsStops);
            var compensationAllStops = compensationSettingsStops + compensationCurveStops;
            var compensationAllLinear = AutoExposureSettingsResolver.ResolveExposureCompensationAll(
                compensationSettingsLinear,
                compensationCurveStops);
            var resolvedExposureScale = AutoExposureSettingsResolver.ResolveManualExposureScale(resolvedEV100, compensationAllLinear);

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

            PopulateFallbackHistogramSamples(averagePosition, histogramWidth, clampMinPosition, clampMaxPosition);

            return new AutoExposureStatsPreviewData(
                false,
                false,
                active,
                enabled,
                mode,
                applyPhysicalCameraExposure,
                hasPhysicalCameraPreview,
                autoExposure.meterMask.value != null,
                false,
                lowPercent,
                highPercent,
                minEV100,
                maxEV100,
                histogramEV100Range,
                resolvedEV100,
                resolvedAverageLuminance,
                resolvedExposureScale,
                resolvedExposureScale,
                compensationSettingsStops,
                compensationCurveStops,
                compensationAllStops,
                clampMinPosition,
                clampMaxPosition,
                averagePosition,
                histogramWidth,
                ResolveExposureGaugePosition(resolvedExposureScale),
                ResolveExposureGaugePosition(resolvedExposureScale),
                ResolveCompensationGaugePosition(compensationAllStops),
                ResolveEvGaugePosition(resolvedEV100),
                previewCamera != null ? previewCamera.name : string.Empty,
                0);
        }

        private void PopulateHistogramSamplesFromSnapshot(
            AutoExposureStatsReadbackSnapshot snapshot,
            float averagePosition,
            float histogramWidth)
        {
            if (!snapshot.hasHistogram || snapshot.histogram == null || snapshot.histogram.Length == 0)
            {
                PopulateFallbackHistogramSamples(averagePosition, histogramWidth, averagePosition - histogramWidth * 0.5f, averagePosition + histogramWidth * 0.5f);
                return;
            }

            uint histogramMax = 0;
            var histogramCount = Mathf.Min(snapshot.histogram.Length, m_HistogramPreviewSamples.Length);
            for (var i = 0; i < histogramCount; i++)
                histogramMax = snapshot.histogram[i] > histogramMax ? snapshot.histogram[i] : histogramMax;

            if (histogramMax == 0)
            {
                PopulateFallbackHistogramSamples(averagePosition, histogramWidth, averagePosition - histogramWidth * 0.5f, averagePosition + histogramWidth * 0.5f);
                return;
            }

            var inverseHistogramMax = 1f / histogramMax;
            for (var i = 0; i < histogramCount; i++)
                m_HistogramPreviewSamples[i] = Mathf.Pow(Mathf.Clamp01(snapshot.histogram[i] * inverseHistogramMax), 0.35f);

            for (var i = histogramCount; i < m_HistogramPreviewSamples.Length; i++)
                m_HistogramPreviewSamples[i] = 0f;
        }

        private void PopulateFallbackHistogramSamples(
            float averagePosition,
            float histogramWidth,
            float clampMinPosition,
            float clampMaxPosition)
        {
            var width = Mathf.Max(histogramWidth, 0.08f);
            var secondaryCenter = Mathf.Lerp(clampMinPosition, clampMaxPosition, 0.72f);
            for (var i = 0; i < m_HistogramPreviewSamples.Length; i++)
            {
                var position = i / (float)(m_HistogramPreviewSamples.Length - 1);
                var primary = Mathf.Exp(-Mathf.Pow((position - averagePosition) / width, 2f) * 2.6f);
                var secondary = Mathf.Exp(-Mathf.Pow((position - secondaryCenter) / Mathf.Max(width * 0.55f, 0.05f), 2f) * 2f);
                m_HistogramPreviewSamples[i] = Mathf.Clamp01(primary + secondary * 0.32f);
            }
        }

        private static Vector2 ResolveHistogramEv100RangeFromSettings(AutoExposureSettingsData settings)
        {
            var histogramScale = Mathf.Max(settings.histogramScale, 1e-4f);
            var logMin = -settings.histogramBias / histogramScale;
            var logMax = (1f - settings.histogramBias) / histogramScale;
            var log2LuminanceBias = Mathf.Log(AutoExposureSettingsResolver.ResolveLuminanceMaxFromLensAttenuation(), 2f);
            return new Vector2(logMin - log2LuminanceBias, logMax - log2LuminanceBias);
        }

        private static float ResolveEv100FromAverageSceneLuminance(float averageSceneLuminance)
        {
            return AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(Mathf.Max(averageSceneLuminance, 1e-4f));
        }

        private static float ResolveCompensationStops(float compensationLinear)
        {
            return Mathf.Log(Mathf.Max(compensationLinear, 1e-4f), 2f);
        }

        private void ConfigureStatsPreview(AutoExposureStatsPreviewData previewData)
        {
            if (m_StatsPreviewMaterial == null)
                return;

            m_StatsPreviewMaterial.SetVector(
                PreviewStateId,
                new Vector4(
                    GUI.enabled ? 1f : 0.45f,
                    AutoExposureExposureModeUtility.UsesManualSettings(previewData.mode) ? 1f : 0f,
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
            m_StatsPreviewMaterial.SetFloatArray(HistogramSamplesId, m_HistogramPreviewSamples);
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
                    "Source",
                    previewData.usingLiveStats ? $"Live GPU ({previewData.liveFrameIndex})" : "Inspector Preview");

                if (!AutoExposureExposureModeUtility.UsesManualSettings(previewData.mode))
                {
                    DrawMetricRow(
                        "Clamp EV100",
                        $"{FormatEv100(previewData.minEV100)} -> {FormatEv100(previewData.maxEV100)}",
                        "Percent Window",
                        $"{previewData.lowPercent * 100f:0.#}% -> {previewData.highPercent * 100f:0.#}%");
                    DrawMetricRow(
                        previewData.usingLiveStats ? "Live Avg" : "Preview Avg",
                        $"{previewData.resolvedAverageLuminance:0.###}",
                        "Exposure Scale",
                        $"{previewData.resolvedExposureScale:0.###} -> {previewData.targetExposureScale:0.###}");
                    DrawMetricRow(
                        "Comp Settings",
                        $"{previewData.exposureCompensationSettingsStops:+0.##;-0.##;0} EV",
                        "Comp Curve",
                        $"{previewData.exposureCompensationCurveStops:+0.##;-0.##;0} EV");
                    DrawMetricRow(
                        "Comp All",
                        $"{previewData.exposureCompensationAllStops:+0.##;-0.##;0} EV",
                        "History",
                        previewData.hasValidHistory ? "Buffered" : previewData.usingLiveStats ? "Warmup" : "Preview");
                    DrawMetricRow(
                        "Meter Mask",
                        previewData.meterMaskAssigned ? "Assigned" : "None",
                        previewData.usingLiveStats ? "Camera" : "Preview EV100",
                        previewData.usingLiveStats
                            ? (string.IsNullOrEmpty(previewData.previewCameraName) ? "None" : previewData.previewCameraName)
                            : FormatEv100(previewData.resolvedEV100));
                    DrawMetricRow(
                        "Histogram",
                        previewData.hasLiveHistogram ? "Live Readback" : "Preview Shape",
                        "Resolved EV100",
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
                        "Comp Settings",
                        $"{previewData.exposureCompensationSettingsStops:+0.##;-0.##;0} EV",
                        "Comp Curve",
                        $"{previewData.exposureCompensationCurveStops:+0.##;-0.##;0} EV");
                    DrawMetricRow(
                        "Comp All",
                        $"{previewData.exposureCompensationAllStops:+0.##;-0.##;0} EV",
                        "History",
                        previewData.hasValidHistory ? "Buffered" : previewData.usingLiveStats ? "Warmup" : "Preview");
                    DrawMetricRow(
                        "Physical Camera",
                        ResolvePhysicalPreviewLabel(previewData),
                        previewData.usingLiveStats ? "Live Camera" : "Preview Camera",
                        string.IsNullOrEmpty(previewData.previewCameraName) ? "None" : previewData.previewCameraName);
                }

                EditorGUILayout.LabelField(
                    previewData.usingLiveStats
                        ? "Live stats come from the editor-only GPU readback path on the latest rendered Game camera."
                        : "Waiting for editor-only GPU readback. Until a Game camera renders, the monitor uses inspector preview values.",
                    EditorStyles.miniLabel);
            }

            if (AutoExposureExposureModeUtility.UsesManualSettings(previewData.mode)
                && previewData.applyPhysicalCameraExposure
                && !previewData.hasPhysicalCameraPreview
                && !previewData.usingLiveStats)
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
            if (!AutoExposureExposureModeUtility.UsesManualSettings(autoExposure.ResolveExposureMode()))
                return autoExposure.manualEV100.value;

            if (!AutoExposureExposureModeUtility.UsesPhysicalCamera(autoExposure.ResolveExposureMode()) || !hasPhysicalCameraPreview)
                return autoExposure.manualEV100.value;

            return AutoExposureSettingsResolver.ResolvePhysicalCameraEV100(previewCamera);
        }

        private static Camera ResolvePreviewCamera()
        {
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
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

        private void ApplySelectedPreset()
        {
            var preset = AutoExposureCommonPresets.Get(m_SelectedPreset);

            foreach (var targetObject in targets)
            {
                if (targetObject is not AutoExposure autoExposure)
                    continue;

                Undo.RecordObject(autoExposure, $"Apply {preset.Name} Preset");
                preset.ApplyTo(autoExposure);
                EditorUtility.SetDirty(autoExposure);
                AssetDatabase.SaveAssetIfDirty(autoExposure);
            }

            serializedObject.Update();
            Repaint();
        }

        private static int ResolvePresetIndex(AutoExposureCommonPreset preset)
        {
            for (var i = 0; i < s_PresetValues.Length; i++)
            {
                if (s_PresetValues[i] == preset)
                    return i;
            }

            return 0;
        }

        private static GUIContent[] BuildPresetOptions()
        {
            var options = new GUIContent[s_PresetValues.Length];
            for (var i = 0; i < s_PresetValues.Length; i++)
            {
                var preset = AutoExposureCommonPresets.Get(s_PresetValues[i]);
                options[i] = EditorGUIUtility.TrTextContent(preset.Name, preset.Description);
            }

            return options;
        }

        private void DoExposurePropertyField(SerializedDataParameter exposureProperty)
        {
            DoExposurePropertyField(exposureProperty, EditorGUIUtility.TrTextContent(exposureProperty.displayName));
        }

        private void DoExposurePropertyField(SerializedDataParameter exposureProperty, GUIContent label)
        {
            using (var scope = new OverridablePropertyScope(exposureProperty, label, this))
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
