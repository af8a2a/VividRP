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
    internal sealed partial class AutoExposureEditor : VolumeComponentEditor
    {
        private const int HistogramBucketCount = 64;
        private const string StatsPreviewShaderName = "Hidden/VividRP/Editor/Auto Exposure Stats";

        private static readonly int PreviewStateId = Shader.PropertyToID("_PreviewState");
        private static readonly int StatusFlagsId = Shader.PropertyToID("_StatusFlags");
        private static readonly int HistogramMarkersId = Shader.PropertyToID("_HistogramMarkers");
        private static readonly int GaugeMarkersId = Shader.PropertyToID("_GaugeMarkers");
        private static readonly int PercentMarkersId = Shader.PropertyToID("_PercentMarkers");
        private static readonly int HistogramLabelRangeId = Shader.PropertyToID("_HistogramLabelRange");
        private static readonly int HistogramExposureValuesId = Shader.PropertyToID("_HistogramExposureValues");
        private static readonly int HistogramPercentileBinsId = Shader.PropertyToID("_HistogramPercentileBins");
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
        private static readonly GUIContent s_TargetMidGrayLabel = EditorGUIUtility.TrTextContent("Target Mid Grey");
        private static readonly GUIContent s_WeightTextureMaskLabel = EditorGUIUtility.TrTextContent("Weight Texture Mask");
        private static readonly GUIContent s_ExposureMeteringMaskLabel =
            EditorGUIUtility.TrTextContent("Exposure Metering Mask");
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
            public readonly bool usesManualSettings;
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
            public readonly float resolvedPreExposure;
            public readonly float exposureCompensationSettingsStops;
            public readonly float exposureCompensationCurveStops;
            public readonly float exposureCompensationAllStops;
            public readonly float clampMinPosition;
            public readonly float clampMaxPosition;
            public readonly float averagePosition;
            public readonly float histogramWidth;
            public readonly float currentExposureEV100;
            public readonly float targetExposureEV100;
            public readonly float lowPercentileBin;
            public readonly float highPercentileBin;
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
                bool usesManualSettings,
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
                float resolvedPreExposure,
                float exposureCompensationSettingsStops,
                float exposureCompensationCurveStops,
                float exposureCompensationAllStops,
                float clampMinPosition,
                float clampMaxPosition,
                float averagePosition,
                float histogramWidth,
                float currentExposureEV100,
                float targetExposureEV100,
                float lowPercentileBin,
                float highPercentileBin,
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
                this.usesManualSettings = usesManualSettings;
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
                this.resolvedPreExposure = resolvedPreExposure;
                this.exposureCompensationSettingsStops = exposureCompensationSettingsStops;
                this.exposureCompensationCurveStops = exposureCompensationCurveStops;
                this.exposureCompensationAllStops = exposureCompensationAllStops;
                this.clampMinPosition = clampMinPosition;
                this.clampMaxPosition = clampMaxPosition;
                this.averagePosition = averagePosition;
                this.histogramWidth = histogramWidth;
                this.currentExposureEV100 = currentExposureEV100;
                this.targetExposureEV100 = targetExposureEV100;
                this.lowPercentileBin = lowPercentileBin;
                this.highPercentileBin = highPercentileBin;
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

            m_Enabled = Unpack(o.Find(x => x.enabled));
            InitializeUnrealProperties(o);
            InitializeHDRPProperties(o);

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
            var implementation = ResolveEditorImplementation();
            PropertyField(m_Enabled, s_EnableLabel);

            if (implementation == AutoExposureImplementationPath.Unreal)
                DrawPresetControls();

            using (new EditorGUI.DisabledScope(!m_Enabled.value.boolValue))
            {
                if (implementation == AutoExposureImplementationPath.HDRP)
                    DrawHDRPInspector();
                else
                    DrawUnrealInspector();

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

        private void DrawStatsPreview()
        {
            var previewData = BuildStatsPreviewData();

            if (m_StatsPreviewMaterial == null)
            {
                EditorGUILayout.HelpBox("Auto exposure stats preview shader is unavailable.", MessageType.Warning);
                return;
            }

            const float previewHeight = 132f;
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

            if (!previewData.usesManualSettings)
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
            var usesManualSettings = settings.mode == AutoExposureMode.Manual;
            var usesPhysicalCamera = usesManualSettings
                && settings.applyPhysicalCameraExposure;
            var lowPercent = Mathf.Clamp01(settings.exposureLowPercent);
            var highPercent = Mathf.Clamp(Mathf.Max(lowPercent, settings.exposureHighPercent), 0f, 1f);

            var histogramEV100Range = ResolveHistogramEv100RangeFromSettings(settings);
            var minAverageLuminance = Mathf.Max(settings.minAverageLuminance, 1e-4f);
            var maxAverageLuminance = Mathf.Max(minAverageLuminance, settings.maxAverageLuminance);
            var minEV100 = ResolveEv100FromAverageSceneLuminance(minAverageLuminance);
            var maxEV100 = ResolveEv100FromAverageSceneLuminance(maxAverageLuminance);

            var fallbackAverageLuminance = usesManualSettings
                ? Mathf.Max(settings.manualAverageSceneLuminance, 1e-4f)
                : Mathf.Max(0.5f * (minAverageLuminance + maxAverageLuminance), 1e-4f);
            var fallbackExposureScale = usesManualSettings
                ? Mathf.Max(settings.fixedExposureScale, 1e-4f)
                : 1f;
            var exposureState = snapshot.hasExposureState
                ? snapshot.exposureState
                : new Vector4(fallbackExposureScale, fallbackExposureScale, fallbackAverageLuminance, Mathf.Max(settings.exposureCompensationAll, 1e-4f));

            var resolvedAverageLuminance = Mathf.Max(exposureState.z, 1e-4f);
            var resolvedEV100 = ResolveEv100FromAverageSceneLuminance(resolvedAverageLuminance);
            var currentExposureScale = Mathf.Max(exposureState.x, 1e-4f);
            var targetExposureScale = Mathf.Max(exposureState.y, 1e-4f);
            var resolvedPreExposure = Mathf.Max(
                snapshot.hasPreExposureState
                    ? snapshot.preExposureState.x
                    : exposureState.x,
                1e-4f);
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
            var percentileBins = snapshot.hasHistogram && snapshot.histogram != null && snapshot.histogram.Length > 0
                ? ResolveHistogramPercentileBins(snapshot.histogram, lowPercent, highPercent)
                : ResolveHistogramPercentileBins(m_HistogramPreviewSamples, lowPercent, highPercent);

            return new AutoExposureStatsPreviewData(
                true,
                snapshot.hasHistogram,
                snapshot.exposureEnabled,
                settings.enabled,
                usesManualSettings,
                usesPhysicalCamera,
                usesPhysicalCamera,
                ResolvePreviewMeterMask(settings) != null,
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
                resolvedPreExposure,
                compensationSettingsStops,
                compensationCurveStops,
                compensationAllStops,
                clampMinPosition,
                clampMaxPosition,
                averagePosition,
                histogramWidth,
                ResolveExposureEV100FromScale(currentExposureScale),
                ResolveExposureEV100FromScale(targetExposureScale),
                percentileBins.x,
                percentileBins.y,
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
            var implementation = ResolveEditorImplementation();
            var usesHDRP = implementation == AutoExposureImplementationPath.HDRP;
            var hdrpExposureMode = usesHDRP
                ? autoExposure.ResolveExposureMode()
                : default;
            var usesManualSettings = usesHDRP
                ? AutoExposureExposureModeUtility.UsesManualSettings(hdrpExposureMode)
                : autoExposure.mode.value == AutoExposureMode.Manual;
            var enabled = autoExposure.enabled.value;
            var active = autoExposure.IsActive(implementation);
            var applyPhysicalCameraExposure = usesHDRP
                ? AutoExposureExposureModeUtility.UsesPhysicalCamera(hdrpExposureMode)
                : usesManualSettings
                    && autoExposure.applyPhysicalCameraExposure.value;
            var previewCamera = ResolvePreviewCamera();
            var hasPhysicalCameraPreview = applyPhysicalCameraExposure
                && previewCamera != null
                && previewCamera.usePhysicalProperties;

            var usesPercentiles = usesHDRP
                || autoExposure.mode.value == AutoExposureMode.Histogram;
            var histogramPercentages = usesHDRP
                ? autoExposure.histogramPercentages.value
                : usesPercentiles
                    ? autoExposure.percent.value
                    : new Vector2(0f, 100f);
            var percentileMinimum = usesHDRP || !usesPercentiles ? 0f : 1f;
            var percentileMaximum = usesHDRP || !usesPercentiles ? 100f : 99f;
            var lowPercent = Mathf.Clamp(
                    histogramPercentages.x,
                    percentileMinimum,
                    percentileMaximum)
                * 0.01f;
            var highPercent = Mathf.Clamp(
                    histogramPercentages.y,
                    percentileMinimum,
                    percentileMaximum)
                * 0.01f;
            highPercent = Mathf.Max(lowPercent, highPercent);

            var histogramEV100Range = usesHDRP
                ? new Vector2(autoExposure.limitMin.value, autoExposure.limitMax.value)
                : autoExposure.histogramLogRange.value;
            var histogramLogRange = AutoExposureSettingsResolver.ResolveHistogramLogRangeFromEV100(
                histogramEV100Range.x,
                histogramEV100Range.y);
            var histogramScaleBias = AutoExposureSettingsResolver.BuildHistogramScaleBias(
                histogramLogRange.x,
                histogramLogRange.y);
            var luminanceMin = !usesHDRP && autoExposure.mode.value == AutoExposureMode.Basic
                ? 1e-4f
                : Mathf.Pow(2f, histogramLogRange.x);

            var minEV100 = usesHDRP
                ? autoExposure.limitMin.value
                : autoExposure.minEV100.value;
            var maxEV100 = Mathf.Max(
                minEV100,
                usesHDRP
                    ? autoExposure.limitMax.value
                    : autoExposure.maxEV100.value);
            var minAverageLuminance = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(minEV100)
                * AutoExposureSettingsResolver.MiddleGrey;
            var maxAverageLuminance = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(maxEV100)
                * AutoExposureSettingsResolver.MiddleGrey;

            var resolvedEV100 = usesManualSettings
                ? ResolveManualPreviewEV100(
                    usesHDRP ? autoExposure.fixedExposure.value : autoExposure.manualEV100.value,
                    usesManualSettings,
                    applyPhysicalCameraExposure,
                    previewCamera,
                    hasPhysicalCameraPreview)
                : 0.5f * (minEV100 + maxEV100);
            var resolvedAverageLuminance = AutoExposureSettingsResolver.ResolveAverageSceneLuminanceFromEV100(resolvedEV100);
            var compensationSettingsStops = usesHDRP
                ? autoExposure.compensation.value
                : autoExposure.exposureCompensation.value;
            var compensationCurveStops = usesHDRP
                ? 0f
                : AutoExposureSettingsResolver.ResolveExposureCompensationCurveStops(
                    autoExposure.exposureCompensationCurve.value,
                    resolvedEV100);
            var compensationSettingsLinear = AutoExposureSettingsResolver.ResolveExposureCompensation(compensationSettingsStops);
            var compensationAllStops = compensationSettingsStops + compensationCurveStops;
            var compensationAllLinear = AutoExposureSettingsResolver.ResolveExposureCompensationAll(
                compensationSettingsLinear,
                compensationCurveStops);
            var resolvedExposureScale = AutoExposureSettingsResolver.ResolveManualExposureScale(resolvedEV100, compensationAllLinear);
            var resolvedPreExposure = resolvedExposureScale;

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
            var percentileBins = ResolveHistogramPercentileBins(m_HistogramPreviewSamples, lowPercent, highPercent);

            return new AutoExposureStatsPreviewData(
                false,
                false,
                active,
                enabled,
                usesManualSettings,
                applyPhysicalCameraExposure,
                hasPhysicalCameraPreview,
                usesHDRP
                    ? autoExposure.weightTextureMask.value != null
                    : autoExposure.exposureMeteringMask.value != null,
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
                resolvedPreExposure,
                compensationSettingsStops,
                compensationCurveStops,
                compensationAllStops,
                clampMinPosition,
                clampMaxPosition,
                averagePosition,
                histogramWidth,
                ResolveExposureEV100FromScale(resolvedExposureScale),
                ResolveExposureEV100FromScale(resolvedExposureScale),
                percentileBins.x,
                percentileBins.y,
                ResolveExposureGaugePosition(resolvedExposureScale),
                ResolveExposureGaugePosition(resolvedExposureScale),
                ResolveCompensationGaugePosition(compensationAllStops),
                ResolveEvGaugePosition(resolvedEV100),
                previewCamera != null ? previewCamera.name : string.Empty,
                0);
        }

        private static Texture ResolvePreviewMeterMask(
            in AutoExposureSettingsData settings)
        {
            return settings.implementation == AutoExposureImplementationPath.HDRP
                ? settings.hdrpWeightTextureMask
                : settings.unrealExposureMeteringMask;
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

        private static Vector2 ResolveHistogramPercentileBins(
            uint[] histogramSamples,
            float lowPercent,
            float highPercent)
        {
            if (histogramSamples == null || histogramSamples.Length == 0)
                return new Vector2(0f, HistogramBucketCount - 1);

            double histogramSum = 0;
            var histogramCount = Mathf.Min(histogramSamples.Length, HistogramBucketCount);
            for (var i = 0; i < histogramCount; i++)
                histogramSum += histogramSamples[i];

            if (histogramSum <= 0)
                return new Vector2(0f, HistogramBucketCount - 1);

            return ResolveHistogramPercentileBins(
                index => histogramSamples[index],
                histogramCount,
                histogramSum,
                lowPercent,
                highPercent);
        }

        private static Vector2 ResolveHistogramPercentileBins(
            float[] histogramSamples,
            float lowPercent,
            float highPercent)
        {
            if (histogramSamples == null || histogramSamples.Length == 0)
                return new Vector2(0f, HistogramBucketCount - 1);

            double histogramSum = 0;
            var histogramCount = Mathf.Min(histogramSamples.Length, HistogramBucketCount);
            for (var i = 0; i < histogramCount; i++)
                histogramSum += Mathf.Max(histogramSamples[i], 0f);

            if (histogramSum <= 0)
                return new Vector2(0f, HistogramBucketCount - 1);

            return ResolveHistogramPercentileBins(
                index => Mathf.Max(histogramSamples[index], 0f),
                histogramCount,
                histogramSum,
                lowPercent,
                highPercent);
        }

        private static Vector2 ResolveHistogramPercentileBins(
            Func<int, double> readSample,
            int histogramCount,
            double histogramSum,
            float lowPercent,
            float highPercent)
        {
            var lowThreshold = histogramSum * Mathf.Clamp01(lowPercent);
            var highThreshold = histogramSum * Mathf.Clamp01(Mathf.Max(lowPercent, highPercent));
            var lowBin = 0;
            var highBin = Mathf.Max(histogramCount - 1, 0);
            var foundLow = false;
            var foundHigh = false;
            double cumulative = 0;

            for (var i = 0; i < histogramCount; i++)
            {
                cumulative += readSample(i);

                if (!foundLow && cumulative >= lowThreshold)
                {
                    lowBin = i;
                    foundLow = true;
                }

                if (!foundHigh && cumulative >= highThreshold)
                {
                    highBin = i;
                    foundHigh = true;
                }
            }

            return new Vector2(lowBin, Mathf.Max(lowBin, highBin));
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

        private static float ResolveExposureEV100FromScale(float exposureScale)
        {
            return -Mathf.Log(Mathf.Max(exposureScale, 1e-4f), 2f);
        }

        private void ConfigureStatsPreview(AutoExposureStatsPreviewData previewData)
        {
            if (m_StatsPreviewMaterial == null)
                return;

            m_StatsPreviewMaterial.SetVector(
                PreviewStateId,
                new Vector4(
                    GUI.enabled ? 1f : 0.45f,
                    previewData.usesManualSettings ? 1f : 0f,
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
            m_StatsPreviewMaterial.SetVector(
                HistogramLabelRangeId,
                new Vector4(
                    previewData.minEV100,
                    Mathf.Max(previewData.maxEV100, previewData.minEV100 + 1e-4f),
                    previewData.histogramEV100Range.x,
                    previewData.histogramEV100Range.y));
            m_StatsPreviewMaterial.SetVector(
                HistogramExposureValuesId,
                new Vector4(
                    previewData.currentExposureEV100,
                    previewData.targetExposureEV100,
                    previewData.exposureCompensationAllStops,
                    previewData.resolvedEV100));
            m_StatsPreviewMaterial.SetVector(
                HistogramPercentileBinsId,
                new Vector4(
                    previewData.lowPercentileBin,
                    previewData.highPercentileBin,
                    previewData.hasLiveHistogram ? 1f : 0f,
                    previewData.usingLiveStats ? 1f : 0f));
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

                if (!previewData.usesManualSettings)
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
                        "Pre Buffer.x",
                        $"{previewData.resolvedPreExposure:0.###}",
                        "Comp Settings",
                        $"{previewData.exposureCompensationSettingsStops:+0.##;-0.##;0} EV");
                    DrawMetricRow(
                        "Comp Curve",
                        $"{previewData.exposureCompensationCurveStops:+0.##;-0.##;0} EV",
                        "Comp All",
                        $"{previewData.exposureCompensationAllStops:+0.##;-0.##;0} EV");
                    DrawMetricRow(
                        "Meter Mask",
                        previewData.meterMaskAssigned ? "Assigned" : "None",
                        "History",
                        previewData.hasValidHistory ? "Buffered" : previewData.usingLiveStats ? "Warmup" : "Preview");
                    DrawMetricRow(
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
                        "Pre Buffer.x",
                        $"{previewData.resolvedPreExposure:0.###}",
                        "Average Luminance",
                        $"{previewData.resolvedAverageLuminance:0.###}");
                    DrawMetricRow(
                        "Meter Mask",
                        previewData.meterMaskAssigned ? "Assigned" : "None",
                        "Comp Settings",
                        $"{previewData.exposureCompensationSettingsStops:+0.##;-0.##;0} EV");
                    DrawMetricRow(
                        "Comp Curve",
                        $"{previewData.exposureCompensationCurveStops:+0.##;-0.##;0} EV",
                        "Comp All",
                        $"{previewData.exposureCompensationAllStops:+0.##;-0.##;0} EV");
                    DrawMetricRow(
                        "History",
                        previewData.hasValidHistory ? "Buffered" : previewData.usingLiveStats ? "Warmup" : "Preview",
                        "Physical Camera",
                        ResolvePhysicalPreviewLabel(previewData));
                    DrawMetricRow(
                        previewData.usingLiveStats ? "Live Camera" : "Preview Camera",
                        string.IsNullOrEmpty(previewData.previewCameraName) ? "None" : previewData.previewCameraName);
                }

                EditorGUILayout.LabelField(
                    previewData.usingLiveStats
                        ? "Live stats come from the editor-only GPU readback path on the latest rendered Game camera."
                        : "Waiting for editor-only GPU readback. Until a Game camera renders, the monitor uses inspector preview values.",
                    EditorStyles.miniLabel);
            }

            if (previewData.usesManualSettings
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

        private static void DrawMetricRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawMetric(label, value);
                GUILayout.FlexibleSpace();
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
            float fixedExposure,
            bool usesManualSettings,
            bool usesPhysicalCamera,
            Camera previewCamera,
            bool hasPhysicalCameraPreview)
        {
            if (!usesManualSettings)
                return fixedExposure;

            if (!usesPhysicalCamera || !hasPhysicalCameraPreview)
            {
                return fixedExposure;
            }

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
