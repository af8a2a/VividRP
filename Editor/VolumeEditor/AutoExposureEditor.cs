using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{

    [CanEditMultipleObjects]
    [CustomEditor(typeof(AutoExposure))]
    internal sealed class AutoExposureEditor : VolumeComponentEditor
    {
        private static readonly GUIContent s_EnableLabel = EditorGUIUtility.TrTextContent("Enable");
        private static readonly GUIContent s_ModeLabel = EditorGUIUtility.TrTextContent("Mode");
        private static readonly GUIContent s_UsePhysicalCameraLabel = EditorGUIUtility.TrTextContent("Use Physical Camera");
        private static readonly GUIContent s_FixedExposureLabel = EditorGUIUtility.TrTextContent("Fixed Exposure");
        private static readonly GUIContent s_CompensationLabel = EditorGUIUtility.TrTextContent("Compensation");
        private static readonly GUIContent s_WeightTextureMaskLabel = EditorGUIUtility.TrTextContent("Weight Texture Mask");
        private static readonly GUIContent s_LimitMinLabel = EditorGUIUtility.TrTextContent("Limit Min");
        private static readonly GUIContent s_LimitMaxLabel = EditorGUIUtility.TrTextContent("Limit Max");
        private static readonly GUIContent s_SpeedDarkToLightLabel = EditorGUIUtility.TrTextContent("Speed Dark to Light");
        private static readonly GUIContent s_SpeedLightToDarkLabel = EditorGUIUtility.TrTextContent("Speed Light to Dark");
        private static readonly GUIContent s_HistogramPercentagesLabel = EditorGUIUtility.TrTextContent("Histogram Percentages");
        private static readonly GUIContent s_HistogramPercentagesMinLabel = EditorGUIUtility.TrTextContent("Low Percent");
        private static readonly GUIContent s_HistogramPercentagesMaxLabel = EditorGUIUtility.TrTextContent("High Percent");
        private static readonly GUIContent s_HistogramEv100RangeLabel = EditorGUIUtility.TrTextContent("Histogram EV100 Range");

        private SerializedDataParameter m_Enabled;
        private SerializedDataParameter m_Mode;
        private SerializedDataParameter m_LowPercent;
        private SerializedDataParameter m_HighPercent;
        private SerializedDataParameter m_MinEV100;
        private SerializedDataParameter m_MaxEV100;
        private SerializedDataParameter m_SpeedUp;
        private SerializedDataParameter m_SpeedDown;
        private SerializedDataParameter m_ManualEV100;
        private SerializedDataParameter m_ApplyPhysicalCameraExposure;
        private SerializedDataParameter m_ExposureCompensation;
        private SerializedDataParameter m_HistogramLogRange;
        private SerializedDataParameter m_MeterMask;

        public override bool hasAdditionalProperties => true;

        private static LightUnitSliderUIDrawer k_LightUnitSlider;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<AutoExposure>(serializedObject);
            m_Enabled = Unpack(o.Find(x => x.enabled));
            m_Mode = Unpack(o.Find(x => x.mode));
            m_LowPercent = Unpack(o.Find(x => x.lowPercent));
            m_HighPercent = Unpack(o.Find(x => x.highPercent));
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
            }
        }

        private void DrawHistogramPercentages()
        {
            EditorGUILayout.LabelField(s_HistogramPercentagesLabel, EditorStyles.miniLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_LowPercent, s_HistogramPercentagesMinLabel);
                PropertyField(m_HighPercent, s_HistogramPercentagesMaxLabel);
            }
        }

        private static void DrawSectionHeader(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
        
        
        
        // TODO: See if this can be refactored into a custom VolumeParameterDrawer
        void DoExposurePropertyField(SerializedDataParameter exposureProperty)
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

                    // GUIContent.none disables horizontal scrolling, use TrTextContent and adjust the rect to make it work.
                    lineRect.x -= EditorGUIUtility.labelWidth + 2;
                    lineRect.y += EditorGUIUtility.standardVerticalSpacing;
                    lineRect.width += EditorGUIUtility.labelWidth + 2;
                    EditorGUI.PropertyField(lineRect, exposureProperty.value, EditorGUIUtility.TrTextContent(" "));
                }
            }
        }

    }
}
