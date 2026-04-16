using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(CascadedShadowSettingsVolume))]
    internal sealed class CascadedShadowSettingsVolumeEditor : VolumeComponentEditor
    {
        private enum WorkingUnit
        {
            Metric,
            Percent
        }

        private static readonly GUIContent s_StateLabel =
            EditorGUIUtility.TrTextContent("State", "When enabled, VividRP renders cascaded shadow maps for the main directional light.");
        private static readonly GUIContent s_MaxShadowDistanceLabel =
            EditorGUIUtility.TrTextContent("Max Distance", "Maximum distance from the camera that receives cascaded directional shadows.");
        private static readonly GUIContent s_WorkingUnitLabel =
            EditorGUIUtility.TrTextContent("Working Unit", "Controls whether cascade splits are edited in meters or as a percentage of Max Distance.");
        private static readonly GUIContent s_CascadeCountLabel =
            EditorGUIUtility.TrTextContent("Cascade Count", "Number of directional shadow cascades.");
        private readonly GUIContent[] m_SplitLabels =
        {
            EditorGUIUtility.TrTextContent("Split 1"),
            EditorGUIUtility.TrTextContent("Split 2"),
            EditorGUIUtility.TrTextContent("Split 3")
        };

        private readonly string[] m_CascadeOrder =
        {
            "first",
            "second",
            "third"
        };

        private SerializedDataParameter m_EnableCSM;
        private SerializedDataParameter m_CascadeCount;
        private SerializedDataParameter m_MaxShadowDistance;
        private SerializedDataParameter m_CascadeSplit1;
        private SerializedDataParameter m_CascadeSplit2;
        private SerializedDataParameter m_CascadeSplit3;

        private EditorPrefBoolFlags<WorkingUnit> m_WorkingUnitState;

        public CascadedShadowSettingsVolumeEditor()
        {
            const string key = "VividRP:CascadedShadowSettingsVolumeEditor:WorkingUnit";
            m_WorkingUnitState = new EditorPrefBoolFlags<WorkingUnit>(key);
        }

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<CascadedShadowSettingsVolume>(serializedObject);
            m_EnableCSM = Unpack(fetcher.Find(x => x.enableCSM));
            m_CascadeCount = Unpack(fetcher.Find(x => x.cascadeCount));
            m_MaxShadowDistance = Unpack(fetcher.Find(x => x.maxShadowDistance));
            m_CascadeSplit1 = Unpack(fetcher.Find(x => x.cascadeSplit1));
            m_CascadeSplit2 = Unpack(fetcher.Find(x => x.cascadeSplit2));
            m_CascadeSplit3 = Unpack(fetcher.Find(x => x.cascadeSplit3));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_EnableCSM, s_StateLabel);
            PropertyField(m_MaxShadowDistance, s_MaxShadowDistanceLabel);

            DrawSectionHeader("Directional Light");
            DrawWorkingUnitField();

            EditorGUI.BeginChangeCheck();
            PropertyField(m_CascadeCount, s_CascadeCountLabel);
            if (EditorGUI.EndChangeCheck())
                NormalizeCascadeSplitOrdering();

            using (new EditorGUI.IndentLevelScope())
            {
                var splitParameters = GetCascadeSplitParameters();
                var activeSplitCount = Mathf.Max(0, GetCascadeCount() - 1);
                for (var i = 0; i < activeSplitCount; i++)
                    DrawCascadeSplitField(splitParameters, i, activeSplitCount);
            }

            DrawCascadePreview();

            DrawSectionHeader("Per Light");
            EditorGUILayout.HelpBox(
                "Screen Space Quality, Atlas Resolution, Depth Bias, Normal Bias, and Slope-Scale Depth Bias are configured on the shadow-casting directional light. When Screen Space Quality is set to Very High (PCSS), blocker and filter tuning also lives on that light.",
                MessageType.Info);
        }

        private void DrawWorkingUnitField()
        {
            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginChangeCheck();
            var selected = (WorkingUnit)EditorGUI.EnumPopup(rect, s_WorkingUnitLabel, m_WorkingUnitState.value);
            if (EditorGUI.EndChangeCheck())
                m_WorkingUnitState.value = selected;
        }

        private void DrawCascadeSplitField(SerializedDataParameter[] splitParameters, int splitIndex, int activeSplitCount)
        {
            var parameter = splitParameters[splitIndex];
            var label = m_SplitLabels[splitIndex];
            var title = EditorGUIUtility.TrTextContent(label.text, BuildSplitTooltip(splitIndex));

            using (var scope = new OverridablePropertyScope(parameter, title, this))
            {
                if (!scope.displayed)
                    return;

                var maxDisplayValue = GetSplitDisplayMaximum();
                var previousSplit = splitIndex == 0 ? 0.0f : splitParameters[splitIndex - 1].value.floatValue;
                var nextSplit = splitIndex + 1 < activeSplitCount
                    ? splitParameters[splitIndex + 1].value.floatValue
                    : 1.0f;

                var minDisplayValue = previousSplit * maxDisplayValue;
                var currentDisplayValue = parameter.value.floatValue * maxDisplayValue;
                var maxAllowedDisplayValue = nextSplit * maxDisplayValue;

                var oldMixedValue = EditorGUI.showMixedValue;
                EditorGUI.showMixedValue = parameter.value.hasMultipleDifferentValues;

                var rect = EditorGUILayout.GetControlRect();
                EditorGUI.BeginProperty(rect, title, parameter.value);
                EditorGUI.BeginChangeCheck();
                var displayValue = EditorGUI.Slider(rect, title, currentDisplayValue, minDisplayValue, maxAllowedDisplayValue);
                if (EditorGUI.EndChangeCheck())
                {
                    parameter.value.floatValue = Mathf.Clamp01(displayValue / Mathf.Max(maxDisplayValue, 1e-5f));
                    NormalizeCascadeSplitOrdering();
                }
                EditorGUI.EndProperty();

                EditorGUI.showMixedValue = oldMixedValue;
            }
        }

        private void DrawCascadePreview()
        {
            if (m_CascadeCount.value.hasMultipleDifferentValues || m_MaxShadowDistance.value.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox(
                    "Cascade preview is hidden while editing multiple volumes with different cascade settings.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            GUILayout.Label("Cascade splits", GUILayout.Height(EditorGUIUtility.singleLineHeight + 4));

            DrawShadowCascades(GetCascadeCount(), m_WorkingUnitState.value == WorkingUnit.Metric, GetMaxShadowDistance());
        }

        private void DrawShadowCascades(int cascadeCount, bool useMetric, float baseMetric)
        {
            var cascades = new ShadowCascadeGUI.Cascade[cascadeCount];
            var splitParameters = GetCascadeSplitParameters();

            float lastCascadePartitionSplit = 0.0f;
            for (var i = 0; i < cascadeCount - 1; ++i)
            {
                cascades[i] = new ShadowCascadeGUI.Cascade
                {
                    size = i == 0
                        ? splitParameters[i].value.floatValue
                        : splitParameters[i].value.floatValue - lastCascadePartitionSplit,
                    borderSize = 0.0f,
                    cascadeHandleState = splitParameters[i].overrideState.boolValue
                        ? ShadowCascadeGUI.HandleState.Enabled
                        : ShadowCascadeGUI.HandleState.Disabled,
                    borderHandleState = ShadowCascadeGUI.HandleState.Hidden,
                };

                lastCascadePartitionSplit = splitParameters[i].value.floatValue;
            }

            var lastCascade = cascadeCount - 1;
            cascades[lastCascade] = new ShadowCascadeGUI.Cascade
            {
                size = lastCascade == 0 ? 1.0f : 1.0f - splitParameters[lastCascade - 1].value.floatValue,
                borderSize = 0.0f,
                cascadeHandleState = ShadowCascadeGUI.HandleState.Hidden,
                borderHandleState = ShadowCascadeGUI.HandleState.Hidden,
            };

            EditorGUI.BeginChangeCheck();
            ShadowCascadeGUI.DrawCascades(ref cascades, useMetric, baseMetric);
            if (EditorGUI.EndChangeCheck())
            {
                float accumulatedSplit = 0.0f;
                for (var i = 0; i < cascadeCount - 1; ++i)
                {
                    accumulatedSplit += cascades[i].size;
                    splitParameters[i].value.floatValue = Mathf.Clamp01(accumulatedSplit);
                }

                NormalizeCascadeSplitOrdering();
            }
        }

        private void NormalizeCascadeSplitOrdering()
        {
            m_CascadeSplit1.value.floatValue = Mathf.Clamp01(m_CascadeSplit1.value.floatValue);
            m_CascadeSplit2.value.floatValue = Mathf.Max(m_CascadeSplit1.value.floatValue, Mathf.Clamp01(m_CascadeSplit2.value.floatValue));
            m_CascadeSplit3.value.floatValue = Mathf.Max(m_CascadeSplit2.value.floatValue, Mathf.Clamp01(m_CascadeSplit3.value.floatValue));
        }

        private int GetCascadeCount()
        {
            return Mathf.Clamp(m_CascadeCount.value.intValue, 1, CascadedShadowSettingsVolume.DefaultCascadeCount);
        }

        private float GetMaxShadowDistance()
        {
            return Mathf.Max(0.01f, m_MaxShadowDistance.value.floatValue);
        }

        private float GetSplitDisplayMaximum()
        {
            return m_WorkingUnitState.value == WorkingUnit.Metric ? GetMaxShadowDistance() : 100.0f;
        }

        private SerializedDataParameter[] GetCascadeSplitParameters()
        {
            return new[]
            {
                m_CascadeSplit1,
                m_CascadeSplit2,
                m_CascadeSplit3
            };
        }

        private string BuildSplitTooltip(int splitIndex)
        {
            var useMetric = m_WorkingUnitState.value == WorkingUnit.Metric;
            return useMetric
                ? $"Distance from the camera (in meters) to the {m_CascadeOrder[splitIndex]} cascade split."
                : $"Distance from the camera (as a percentage of Max Distance) to the {m_CascadeOrder[splitIndex]} cascade split.";
        }

        private static void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
