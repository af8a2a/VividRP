using Features.Shadow;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Shadows))]
    public class ShadowEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_Enable;


        SerializedDataParameter m_RayTracing;
        SerializedDataParameter m_DirShadowsRayLength;
        SerializedDataParameter m_sampleCount;
        SerializedDataParameter m_radius;
        SerializedDataParameter m_CharacterLayerMask;


        SerializedDataParameter m_ShadowsAlgo;
        SerializedDataParameter m_ShadowsIntensity;

        SerializedDataParameter m_MaxShadowDistance;
        SerializedDataParameter m_CascadeShadowSplitCount;
        SerializedDataParameter[] m_CascadeShadowSplits = new SerializedDataParameter[7];
        SerializedDataParameter m_CascadeBorder;


        SerializedDataParameter m_Penumbra;

        SerializedDataParameter m_ShadowScatterMode;
        SerializedDataParameter m_ShadowRampTex;
        SerializedDataParameter m_ScatterR;
        SerializedDataParameter m_ScatterG;
        SerializedDataParameter m_ScatterB;


        private enum Unit
        {
            Metric,
            Percent
        }

        EditorPrefBoolFlags<Unit> m_State;
        private int[] layers;
        private GUIContent[] displayedOptions;

        public ShadowEditor()
        {
            string Key = string.Format("{0}:{1}:UI_State", "URP", typeof(ShadowEditor).Name);
            m_State = new EditorPrefBoolFlags<Unit>(Key);
        }


        public override void OnEnable()
        {
            var o = new PropertyFetcher<Shadows>(serializedObject);

            m_Enable = Unpack(o.Find(x => x.enable));


            m_RayTracing = Unpack(o.Find(x => x.rayTracing));
            m_DirShadowsRayLength = Unpack(o.Find(x => x.dirShadowsRayLength));
            m_sampleCount= Unpack(o.Find(x => x.sampleCount));
            m_radius = Unpack(o.Find(x => x.radius));
            m_CharacterLayerMask = Unpack(o.Find(x => x.characterLayerMask));

            m_ShadowsAlgo = Unpack(o.Find(x => x.shadowAlgo));
            m_ShadowsIntensity = Unpack(o.Find(x => x.intensity));
            m_MaxShadowDistance = Unpack(o.Find(x => x.maxShadowDistance));
            m_CascadeShadowSplitCount = Unpack(o.Find(x => x.cascadeShadowSplitCount));
            m_CascadeShadowSplits[0] = Unpack(o.Find(x => x.cascadeShadowSplit0));
            m_CascadeShadowSplits[1] = Unpack(o.Find(x => x.cascadeShadowSplit1));
            m_CascadeShadowSplits[2] = Unpack(o.Find(x => x.cascadeShadowSplit2));
            m_CascadeShadowSplits[3] = Unpack(o.Find(x => x.cascadeShadowSplit3));
            m_CascadeShadowSplits[4] = Unpack(o.Find(x => x.cascadeShadowSplit4));
            m_CascadeShadowSplits[5] = Unpack(o.Find(x => x.cascadeShadowSplit5));
            m_CascadeShadowSplits[6] = Unpack(o.Find(x => x.cascadeShadowSplit6));

            m_CascadeBorder = Unpack(o.Find(x => x.cascadeBorder));
            m_Penumbra = Unpack(o.Find(x => x.penumbra));
            m_ShadowScatterMode = Unpack(o.Find(x => x.shadowScatterMode));
            m_ShadowRampTex = Unpack(o.Find(x => x.shadowRampTex));
            m_ScatterR = Unpack(o.Find(x => x.scatterR));
            m_ScatterG = Unpack(o.Find(x => x.scatterG));
            m_ScatterB = Unpack(o.Find(x => x.scatterB));
            // m_ShadowACES = Unpack(o.Find(x => x.ACES));
            // m_OcclusionPenumbra = Unpack(o.Find(x => x.occlusionPenumbra));

            layers = new int[InternalEditorUtility.layers.Length];
            displayedOptions = new GUIContent[InternalEditorUtility.layers.Length];
            for (int i = 0; i < InternalEditorUtility.layers.Length; i++)
            {
                layers[i] = LayerMask.NameToLayer(InternalEditorUtility.layers[i]);
                displayedOptions[i] = new GUIContent(InternalEditorUtility.layers[i]);
            }

            (serializedObject.targetObject as Shadows)?.InitNormalized(m_State.value == Unit.Percent);
        }


        public override void OnInspectorGUI()
        {
            PropertyField(m_Enable, EditorGUIUtility.TrTextContent("Shadow Enable"));
            PropertyField(m_RayTracing,EditorGUIUtility.TrTextContent("Raytracing Shadow"));

            bool rayTracingSettingsDisplayed = m_RayTracing.overrideState.boolValue
                                               && m_RayTracing.value.boolValue;


            // The rest of the ray tracing UI is only displayed if the asset supports ray tracing and the checkbox is checked.
            if (rayTracingSettingsDisplayed)
            {
                RayTracedShadowsGUI();
            }
            else
            {
                RasterShadowsGUI();
            }

        }


        void RayTracedShadowsGUI()
        {
            if (!SystemInfo.supportsRayTracing)
            {
                EditorGUILayout.HelpBox("Platform not support Raytracing", MessageType.Error, true);
            }
            else
            {
                PropertyField(m_DirShadowsRayLength);
                PropertyField(m_CharacterLayerMask);

                PropertyField(m_sampleCount);
                PropertyField(m_radius);
                EditorGUILayout.Space(10);

                PropertyField(m_ShadowsIntensity);
            }
        }

        void RasterShadowsGUI()
        {
            PropertyField(m_ShadowsAlgo, EditorGUIUtility.TrTextContent("Shadow Algo"));
            PropertyField(m_ShadowsIntensity, EditorGUIUtility.TrTextContent("Shadow Intensity"));


#if false
            PropertyField(m_MaxShadowDistance, EditorGUIUtility.TrTextContent("Max Distance"));

            Unit unit;

            using (new IndentLevelScope(9 + 14 + 3))
            {
                EditorGUIUtility.labelWidth -= 3;
                Rect shiftedRect = EditorGUILayout.GetControlRect();
                EditorGUI.BeginChangeCheck();
                unit = (Unit)EditorGUI.EnumPopup(shiftedRect,
                    EditorGUIUtility.TrTextContent("Working Unit", "Except Max Distance which will be still in meter"), m_State.value);
                if (EditorGUI.EndChangeCheck())
                {
                    m_State.value = unit;
                    (serializedObject.targetObject as Shadows)?.InitNormalized(m_State.value == Unit.Percent);
                }

                EditorGUIUtility.labelWidth += 3;
            }
            
            PropertyField(m_CascadeShadowSplitCount, EditorGUIUtility.TrTextContent("Cascade Count"));

            if (EditorGUI.EndChangeCheck())
            {
                for (int i = 1; i < m_CascadeShadowSplitCount.value.intValue - 1; i++)
                {
                    if (m_CascadeShadowSplits[i - 1].value.floatValue > m_CascadeShadowSplits[i].value.floatValue)
                        m_CascadeShadowSplits[i].value.floatValue = m_CascadeShadowSplits[i - 1].value.floatValue;
                }
            }

            if (!m_CascadeShadowSplitCount.value.hasMultipleDifferentValues)
            {
                int cascadeCount;
                int splitCount;

                using (new IndentLevelScope())
                {
                    cascadeCount = m_CascadeShadowSplitCount.value.intValue;
                    splitCount = cascadeCount - 1;
                    string[] cascadeOrder = { "first", "second", "third", "forth", "fifth", "sixth", "seventh" };

                    for (int i = 0; i < cascadeCount - 1; i++)
                    {
                        string tooltipOverride = (unit == Unit.Metric)
                            ? $"Distance from the Camera (in meters) to the {cascadeOrder[i]} cascade split."
                            : $"Distance from the Camera (as a percentage of Max Distance) to the {cascadeOrder[i]} cascade split.";
                        PropertyField(m_CascadeShadowSplits[i], EditorGUIUtility.TrTextContent(string.Format("Split {0}", i + 1), tooltipOverride));
                    }
                }

                var borderValue = m_CascadeBorder.value.floatValue;
                float baseMetric = m_MaxShadowDistance.value.floatValue;

                EditorGUI.BeginChangeCheck();
                if (unit == Unit.Metric)
                {
                    var lastCascadeSplitSize = splitCount == 0 ? baseMetric : (1.0f - m_CascadeShadowSplits[splitCount - 1].value.floatValue) * baseMetric;
                    var invLastCascadeSplitSize = lastCascadeSplitSize == 0 ? 0 : 1f / lastCascadeSplitSize;
                    float valueMetric = borderValue * lastCascadeSplitSize;
                    valueMetric = EditorGUILayout.Slider(EditorGUIUtility.TrTextContent("Last Border", "The distance of the last cascade."), valueMetric, 0f,
                        lastCascadeSplitSize, null);

                    borderValue = valueMetric * invLastCascadeSplitSize;
                }
                else
                {
                    float valueProcentage = borderValue * 100f;
                    valueProcentage = EditorGUILayout.Slider(EditorGUIUtility.TrTextContent("Last Border", "The distance of the last cascade."),
                        valueProcentage, 0f, 100f, null);

                    borderValue = valueProcentage * 0.01f;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    m_CascadeBorder.value.floatValue = borderValue;
                }

                EditorGUILayout.Space();

                GUILayout.Label("Cascade splits");

                DrawShadowCascades(cascadeCount, unit == Unit.Metric, m_MaxShadowDistance.value.floatValue);
            }
#endif
            GUILayout.Label("PCSS");
            PropertyField(m_Penumbra, EditorGUIUtility.TrTextContent("Penumbra"));

            GUILayout.Label("Shadow Scatter");
            PropertyField(m_ShadowScatterMode, EditorGUIUtility.TrTextContent("ShadowScatterMode"));
            PropertyField(m_ShadowRampTex, EditorGUIUtility.TrTextContent("ShadowRampTex"));
            // PropertyField(m_OcclusionPenumbra, EditorGUIUtility.TrTextContent("OcclusionPenumbra"));
            PropertyField(m_ScatterR, EditorGUIUtility.TrTextContent("ScatterR"));
            PropertyField(m_ScatterG, EditorGUIUtility.TrTextContent("ScatterG"));
            PropertyField(m_ScatterB, EditorGUIUtility.TrTextContent("ScatterB"));
        }


        #region Cascade Editor Extension

        //preserved for cascade extend...

        private void DrawShadowCascades(int cascadeCount, bool useMetric, float baseMetric)
        {
            var cascades = new ShadowCascadeGUI.Cascade[cascadeCount];

            float lastCascadePartitionSplit = 0;
            for (int i = 0; i < cascadeCount - 1; ++i)
            {
                cascades[i] = new ShadowCascadeGUI.Cascade()
                {
                    size = i == 0 ? m_CascadeShadowSplits[i].value.floatValue : m_CascadeShadowSplits[i].value.floatValue - lastCascadePartitionSplit,
                    borderSize = 0f,
                    cascadeHandleState = m_CascadeShadowSplits[i].overrideState.boolValue
                        ? ShadowCascadeGUI.HandleState.Enabled
                        : ShadowCascadeGUI.HandleState.Disabled,
                    borderHandleState = ShadowCascadeGUI.HandleState.Hidden,
                };
                lastCascadePartitionSplit = m_CascadeShadowSplits[i].value.floatValue;
            }

            var lastCascade = cascadeCount - 1;
            cascades[lastCascade] = new ShadowCascadeGUI.Cascade()
            {
                size = lastCascade == 0 ? 1.0f : 1 - m_CascadeShadowSplits[lastCascade - 1].value.floatValue, // Calculate the size of cascade
                borderSize = m_CascadeBorder.value.floatValue,
                cascadeHandleState = ShadowCascadeGUI.HandleState.Hidden,
                borderHandleState = ShadowCascadeGUI.HandleState.Enabled,
            };

            EditorGUI.BeginChangeCheck();
            ShadowCascadeGUI.DrawCascades(ref cascades, useMetric, baseMetric);
            if (EditorGUI.EndChangeCheck())
            {
                float lastCascadeSize = 0;
                for (int i = 0; i < cascadeCount - 1; ++i)
                {
                    m_CascadeShadowSplits[i].value.floatValue = lastCascadeSize + cascades[i].size;
                    lastCascadeSize = m_CascadeShadowSplits[i].value.floatValue;
                }
            }
        }

        [VolumeParameterDrawer(typeof(CascadePartitionSplitParameter))]
        sealed class CascadePartitionSplitParameterDrawer : VolumeParameterDrawer
        {
            public override bool OnGUI(SerializedDataParameter parameter, GUIContent title)
            {
                var value = parameter.value;

                if (value.propertyType != SerializedPropertyType.Float)
                    return false;

                var o = parameter.GetObjectRef<CascadePartitionSplitParameter>();
                float max = o.normalized ? 100f : o.representationDistance;
                float modifiableValue = value.floatValue * max;
                EditorGUI.BeginChangeCheck();

                var lineRect = EditorGUILayout.GetControlRect();
                EditorGUI.BeginProperty(lineRect, title, value);
                modifiableValue = EditorGUI.Slider(lineRect, title, modifiableValue, 0f, max);
                EditorGUI.EndProperty();
                if (EditorGUI.EndChangeCheck())
                {
                    modifiableValue /= max;
                    value.floatValue = Mathf.Clamp(modifiableValue, o.min, o.max);
                }

                return true;
            }
        }

        #endregion
    }
}