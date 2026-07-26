using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal sealed partial class AutoExposureEditor
    {
        private static readonly GUIContent s_CenterAroundTargetLabel =
            EditorGUIUtility.TrTextContent("Center Around Exposure Target");
        private static readonly GUIContent s_ProceduralCenterLabel =
            EditorGUIUtility.TrTextContent(
                "Center",
                "Sets the center of the procedural mask in normalized screen coordinates.");
        private static readonly GUIContent s_ProceduralOffsetLabel =
            EditorGUIUtility.TrTextContent(
                "Offset",
                "Sets the normalized offset from the camera exposure target.");
        private static readonly GUIContent s_ProceduralRadiusLabel =
            EditorGUIUtility.TrTextContent(
                "Radius",
                "Sets the horizontal and vertical radius as a fraction of the screen.");
        private static readonly GUIContent s_ProceduralSoftnessLabel =
            EditorGUIUtility.TrTextContent("Softness");
        private static readonly GUIContent s_MaskMinIntensityLabel =
            EditorGUIUtility.TrTextContent("Mask Min Intensity");
        private static readonly GUIContent s_MaskMaxIntensityLabel =
            EditorGUIUtility.TrTextContent("Mask Max Intensity");
        private static readonly GUIContent s_UseCurveRemappingLabel =
            EditorGUIUtility.TrTextContent("Use Curve Remapping");

        private SerializedDataParameter m_HDRPMode;
        private SerializedDataParameter m_HDRPMeteringMode;
        private SerializedDataParameter m_HDRPFixedExposure;
        private SerializedDataParameter m_HDRPCompensation;
        private SerializedDataParameter m_HDRPLimitMin;
        private SerializedDataParameter m_HDRPLimitMax;
        private SerializedDataParameter m_HDRPCurveMap;
        private SerializedDataParameter m_HDRPAdaptationMode;
        private SerializedDataParameter m_HDRPSpeedDarkToLight;
        private SerializedDataParameter m_HDRPSpeedLightToDark;
        private SerializedDataParameter m_HDRPWeightTextureMask;
        private SerializedDataParameter m_HDRPHistogramPercentages;
        private SerializedDataParameter m_HDRPHistogramUseCurveRemapping;
        private SerializedDataParameter m_HDRPTargetMidGray;
        private SerializedDataParameter m_HDRPCenterAroundExposureTarget;
        private SerializedDataParameter m_HDRPProceduralCenter;
        private SerializedDataParameter m_HDRPProceduralRadii;
        private SerializedDataParameter m_HDRPProceduralSoftness;
        private SerializedDataParameter m_HDRPMaskMinIntensity;
        private SerializedDataParameter m_HDRPMaskMaxIntensity;

        private void InitializeHDRPProperties(PropertyFetcher<AutoExposure> properties)
        {
            m_HDRPMode = Unpack(properties.Find(x => x.exposureMode));
            m_HDRPMeteringMode = Unpack(properties.Find(x => x.meteringMode));
            m_HDRPFixedExposure = Unpack(properties.Find(x => x.fixedExposure));
            m_HDRPCompensation = Unpack(properties.Find(x => x.compensation));
            m_HDRPLimitMin = Unpack(properties.Find(x => x.limitMin));
            m_HDRPLimitMax = Unpack(properties.Find(x => x.limitMax));
            m_HDRPCurveMap = Unpack(properties.Find(x => x.curveMap));
            m_HDRPAdaptationMode = Unpack(properties.Find(x => x.adaptationMode));
            m_HDRPSpeedDarkToLight = Unpack(
                properties.Find(x => x.adaptationSpeedDarkToLight));
            m_HDRPSpeedLightToDark = Unpack(
                properties.Find(x => x.adaptationSpeedLightToDark));
            m_HDRPWeightTextureMask = Unpack(properties.Find(x => x.weightTextureMask));
            m_HDRPHistogramPercentages = Unpack(
                properties.Find(x => x.histogramPercentages));
            m_HDRPHistogramUseCurveRemapping = Unpack(
                properties.Find(x => x.histogramUseCurveRemapping));
            m_HDRPTargetMidGray = Unpack(properties.Find(x => x.targetMidGray));
            m_HDRPCenterAroundExposureTarget = Unpack(
                properties.Find(x => x.centerAroundExposureTarget));
            m_HDRPProceduralCenter = Unpack(properties.Find(x => x.proceduralCenter));
            m_HDRPProceduralRadii = Unpack(properties.Find(x => x.proceduralRadii));
            m_HDRPProceduralSoftness = Unpack(properties.Find(x => x.proceduralSoftness));
            m_HDRPMaskMinIntensity = Unpack(properties.Find(x => x.maskMinIntensity));
            m_HDRPMaskMaxIntensity = Unpack(properties.Find(x => x.maskMaxIntensity));
        }

        private void DrawHDRPInspector()
        {
            PropertyField(m_HDRPMode, s_ModeLabel);
            var mode = ResolveSelectedHDRPExposureMode();

            if (mode == AutoExposureExposureMode.Fixed)
            {
                DoExposurePropertyField(m_HDRPFixedExposure, s_FixedExposureLabel);
                PropertyField(m_HDRPCompensation, s_CompensationLabel);
                return;
            }

            if (mode == AutoExposureExposureMode.UsePhysicalCamera)
            {
                PropertyField(m_HDRPCompensation, s_CompensationLabel);
                return;
            }

            PropertyField(m_HDRPMeteringMode, s_MeteringModeLabel);
            var meteringMode = (AutoExposureMeteringMode)m_HDRPMeteringMode.value.intValue;
            if (meteringMode == AutoExposureMeteringMode.MaskWeighted)
            {
                PropertyField(m_HDRPWeightTextureMask, s_WeightTextureMaskLabel);
            }
            else if (meteringMode == AutoExposureMeteringMode.ProceduralMask)
            {
                DrawHDRPProceduralMaskInspector();
            }

            if (mode == AutoExposureExposureMode.CurveMapping)
            {
                PropertyField(m_HDRPCurveMap, s_CurveMapLabel);
            }
            else if (!(mode == AutoExposureExposureMode.AutomaticHistogram
                       && m_HDRPHistogramUseCurveRemapping.value.boolValue))
            {
                DoExposurePropertyField(m_HDRPLimitMin, s_LimitMinLabel);
                DoExposurePropertyField(m_HDRPLimitMax, s_LimitMaxLabel);
            }

            PropertyField(m_HDRPCompensation, s_CompensationLabel);

            if (mode == AutoExposureExposureMode.AutomaticHistogram)
            {
                EditorGUILayout.Space();
                DrawSectionHeader("Histogram");
                PropertyField(m_HDRPHistogramPercentages, s_HistogramPercentagesLabel);
                PropertyField(
                    m_HDRPHistogramUseCurveRemapping,
                    s_UseCurveRemappingLabel);
                if (m_HDRPHistogramUseCurveRemapping.value.boolValue)
                    PropertyField(m_HDRPCurveMap, s_CurveMapLabel);
            }

            EditorGUILayout.Space();
            DrawSectionHeader("Adaptation");
            PropertyField(m_HDRPAdaptationMode, s_ModeLabel);
            if ((AutoExposureAdaptationMode)m_HDRPAdaptationMode.value.intValue
                == AutoExposureAdaptationMode.Progressive)
            {
                PropertyField(m_HDRPSpeedDarkToLight, s_SpeedDarkToLightLabel);
                PropertyField(m_HDRPSpeedLightToDark, s_SpeedLightToDarkLabel);
            }

            PropertyField(m_HDRPTargetMidGray, s_TargetMidGrayLabel);
        }

        private void DrawHDRPProceduralMaskInspector()
        {
            EditorGUILayout.Space();
            DrawSectionHeader("Procedural Mask");
            PropertyField(m_HDRPCenterAroundExposureTarget, s_CenterAroundTargetLabel);

            var center = m_HDRPProceduralCenter.value.vector2Value;
            if (m_HDRPCenterAroundExposureTarget.value.boolValue)
            {
                center.x = Mathf.Clamp(center.x, -0.5f, 0.5f);
                center.y = Mathf.Clamp(center.y, -0.5f, 0.5f);
                m_HDRPProceduralCenter.value.vector2Value = center;
                PropertyField(m_HDRPProceduralCenter, s_ProceduralOffsetLabel);
            }
            else
            {
                center.x = Mathf.Clamp01(center.x);
                center.y = Mathf.Clamp01(center.y);
                m_HDRPProceduralCenter.value.vector2Value = center;
                PropertyField(m_HDRPProceduralCenter, s_ProceduralCenterLabel);
            }

            var radii = m_HDRPProceduralRadii.value.vector2Value;
            radii.x = Mathf.Clamp01(radii.x);
            radii.y = Mathf.Clamp01(radii.y);
            m_HDRPProceduralRadii.value.vector2Value = radii;
            PropertyField(m_HDRPProceduralRadii, s_ProceduralRadiusLabel);
            PropertyField(m_HDRPProceduralSoftness, s_ProceduralSoftnessLabel);
            PropertyField(m_HDRPMaskMinIntensity, s_MaskMinIntensityLabel);
            PropertyField(m_HDRPMaskMaxIntensity, s_MaskMaxIntensityLabel);
            EditorGUILayout.Space();
        }

        private AutoExposureExposureMode ResolveSelectedHDRPExposureMode()
        {
            return Enum.IsDefined(
                typeof(AutoExposureExposureMode),
                m_HDRPMode.value.intValue)
                ? (AutoExposureExposureMode)m_HDRPMode.value.intValue
                : AutoExposureExposureMode.AutomaticHistogram;
        }
    }
}
