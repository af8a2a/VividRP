using UnityEditor;
using UnityEditor.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal sealed partial class AutoExposureEditor
    {
        private SerializedDataParameter m_Mode;
        private SerializedDataParameter m_Percent;
        private SerializedDataParameter m_MinEV100;
        private SerializedDataParameter m_MaxEV100;
        private SerializedDataParameter m_SpeedUp;
        private SerializedDataParameter m_SpeedDown;
        private SerializedDataParameter m_ManualEV100;
        private SerializedDataParameter m_ApplyPhysicalCameraExposure;
        private SerializedDataParameter m_ExposureCompensation;
        private SerializedDataParameter m_ExposureCompensationCurve;
        private SerializedDataParameter m_ExposureMeteringMask;
        private SerializedDataParameter m_HistogramLogRange;

        private void InitializeUnrealProperties(PropertyFetcher<AutoExposure> properties)
        {
            m_Mode = Unpack(properties.Find(x => x.mode));
            m_Percent = Unpack(properties.Find(x => x.percent));
            m_MinEV100 = Unpack(properties.Find(x => x.minEV100));
            m_MaxEV100 = Unpack(properties.Find(x => x.maxEV100));
            m_SpeedUp = Unpack(properties.Find(x => x.speedUp));
            m_SpeedDown = Unpack(properties.Find(x => x.speedDown));
            m_ManualEV100 = Unpack(properties.Find(x => x.manualEV100));
            m_ApplyPhysicalCameraExposure = Unpack(
                properties.Find(x => x.applyPhysicalCameraExposure));
            m_ExposureCompensation = Unpack(
                properties.Find(x => x.exposureCompensation));
            m_ExposureCompensationCurve = Unpack(
                properties.Find(x => x.exposureCompensationCurve));
            m_ExposureMeteringMask = Unpack(
                properties.Find(x => x.exposureMeteringMask));
            m_HistogramLogRange = Unpack(properties.Find(x => x.histogramLogRange));
        }

        private void DrawUnrealInspector()
        {
            PropertyField(m_Mode, s_ModeLabel);
            var mode = (AutoExposureMode)m_Mode.value.intValue;

            if (mode == AutoExposureMode.Manual)
            {
                EditorGUILayout.Space();
                DrawSectionHeader("Manual");
                DoExposurePropertyField(m_ManualEV100, s_FixedExposureLabel);
                PropertyField(m_ApplyPhysicalCameraExposure);
                PropertyField(m_ExposureCompensation, s_CompensationLabel);
                PropertyField(m_ExposureCompensationCurve, s_CompensationCurveLabel);
                return;
            }

            PropertyField(m_ExposureMeteringMask, s_ExposureMeteringMaskLabel);

            EditorGUILayout.Space();
            DrawSectionHeader(mode == AutoExposureMode.Basic ? "Basic" : "Histogram");
            if (mode == AutoExposureMode.Histogram)
                PropertyField(m_Percent, s_HistogramPercentagesLabel);
            DoExposurePropertyField(m_MinEV100, s_LimitMinLabel);
            DoExposurePropertyField(m_MaxEV100, s_LimitMaxLabel);
            PropertyField(m_HistogramLogRange, s_HistogramEv100RangeLabel);
            PropertyField(m_ExposureCompensation, s_CompensationLabel);
            PropertyField(m_ExposureCompensationCurve, s_CompensationCurveLabel);

            EditorGUILayout.Space();
            DrawSectionHeader("Adaptation");
            PropertyField(m_SpeedUp, s_SpeedDarkToLightLabel);
            PropertyField(m_SpeedDown, s_SpeedLightToDarkLabel);
        }

        private static AutoExposureImplementationPath ResolveEditorImplementation()
        {
            return AutoExposureImplementationUtility.ResolveImplementation(
                VividRenderPipelineAsset.GetActiveAsset());
        }
    }
}
