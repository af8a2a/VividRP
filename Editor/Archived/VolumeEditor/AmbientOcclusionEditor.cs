using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(AmbientOcclusion))]
    internal sealed class AmbientOcclusionEditor : VolumeComponentEditor
    {
        private static readonly GUIContent s_Enabled = EditorGUIUtility.TrTextContent(
            "Enabled",
            "Enables screen-space ambient occlusion.");
        private static readonly GUIContent s_Implementation =
            EditorGUIUtility.TrTextContent(
                "Implementation",
                "Selects the ambient-occlusion algorithm.");
        private static readonly GUIContent s_QualityLevel =
            EditorGUIUtility.TrTextContent(
                "Quality Level",
                "GTAO supports levels 0-3. FidelityFX CACAO supports levels 0-4.");
        private static readonly GUIContent s_Radius = EditorGUIUtility.TrTextContent(
            "Radius",
            "Ambient-occlusion radius in view-space units.");
        private static readonly GUIContent s_DenoisePasses =
            EditorGUIUtility.TrTextContent(
                "Denoise Passes",
                "Number of GTAO edge-aware denoise passes.");
        private static readonly GUIContent s_FalloffRange =
            EditorGUIUtility.TrTextContent(
                "Falloff Range",
                "Controls the GTAO distance range where occlusion fades out.");
        private static readonly GUIContent s_FinalValuePower =
            EditorGUIUtility.TrTextContent(
                "Final Value Power",
                "Shapes the final GTAO visibility value.");
        private static readonly GUIContent s_Downsampled =
            EditorGUIUtility.TrTextContent(
                "Downsampled",
                "Runs CACAO at reduced internal resolution and enables bilateral upsampling.");
        private static readonly GUIContent s_ShadowMultiplier =
            EditorGUIUtility.TrTextContent(
                "Shadow Multiplier",
                "Linear CACAO effect-strength multiplier.");
        private static readonly GUIContent s_ShadowPower =
            EditorGUIUtility.TrTextContent(
                "Shadow Power",
                "Power applied to the CACAO occlusion term.");
        private static readonly GUIContent s_ShadowClamp =
            EditorGUIUtility.TrTextContent(
                "Shadow Clamp",
                "Maximum CACAO occlusion before filtering.");
        private static readonly GUIContent s_HorizonAngleThreshold =
            EditorGUIUtility.TrTextContent(
                "Horizon Angle Threshold",
                "Reduces CACAO self-occlusion on shallow slopes.");
        private static readonly GUIContent s_FadeOutFrom =
            EditorGUIUtility.TrTextContent(
                "Fade Out From",
                "View-space distance where CACAO starts fading out.");
        private static readonly GUIContent s_FadeOutTo =
            EditorGUIUtility.TrTextContent(
                "Fade Out To",
                "View-space distance where CACAO is fully faded out.");
        private static readonly GUIContent s_AdaptiveQualityLimit =
            EditorGUIUtility.TrTextContent(
                "Adaptive Quality Limit",
                "Extra adaptive sample budget used by CACAO quality level 4.");
        private static readonly GUIContent s_BlurPasses =
            EditorGUIUtility.TrTextContent(
                "Blur Passes",
                "Number of CACAO edge-sensitive blur passes.");
        private static readonly GUIContent s_Sharpness =
            EditorGUIUtility.TrTextContent(
                "Sharpness",
                "Controls CACAO edge preservation during filtering.");
        private static readonly GUIContent s_DetailShadowStrength =
            EditorGUIUtility.TrTextContent(
                "Detail Shadow Strength",
                "Strength of CACAO high-frequency detail occlusion.");
        private static readonly GUIContent s_BilateralSigmaSquared =
            EditorGUIUtility.TrTextContent(
                "Bilateral Sigma Squared",
                "Gaussian sigma squared used by the CACAO bilateral upsampler.");
        private static readonly GUIContent s_BilateralSimilarityDistanceSigma =
            EditorGUIUtility.TrTextContent(
                "Bilateral Similarity Sigma",
                "Depth-similarity sigma used by the CACAO bilateral upsampler.");

        private SerializedDataParameter m_Enabled;
        private SerializedDataParameter m_Implementation;
        private SerializedDataParameter m_QualityLevel;
        private SerializedDataParameter m_DenoisePasses;
        private SerializedDataParameter m_Radius;
        private SerializedDataParameter m_FalloffRange;
        private SerializedDataParameter m_FinalValuePower;
        private SerializedDataParameter m_CacaoDownsampled;
        private SerializedDataParameter m_CacaoShadowMultiplier;
        private SerializedDataParameter m_CacaoShadowPower;
        private SerializedDataParameter m_CacaoShadowClamp;
        private SerializedDataParameter m_CacaoHorizonAngleThreshold;
        private SerializedDataParameter m_CacaoFadeOutFrom;
        private SerializedDataParameter m_CacaoFadeOutTo;
        private SerializedDataParameter m_CacaoAdaptiveQualityLimit;
        private SerializedDataParameter m_CacaoBlurPasses;
        private SerializedDataParameter m_CacaoSharpness;
        private SerializedDataParameter m_CacaoDetailShadowStrength;
        private SerializedDataParameter m_CacaoBilateralSigmaSquared;
        private SerializedDataParameter m_CacaoBilateralSimilarityDistanceSigma;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<AmbientOcclusion>(serializedObject);
            m_Enabled = Unpack(fetcher.Find(x => x.enabled));
            m_Implementation = Unpack(fetcher.Find(x => x.implementation));
            m_QualityLevel = Unpack(fetcher.Find(x => x.qualityLevel));
            m_DenoisePasses = Unpack(fetcher.Find(x => x.denoisePasses));
            m_Radius = Unpack(fetcher.Find(x => x.radius));
            m_FalloffRange = Unpack(fetcher.Find(x => x.falloffRange));
            m_FinalValuePower = Unpack(fetcher.Find(x => x.finalValuePower));
            m_CacaoDownsampled = Unpack(fetcher.Find(x => x.cacaoDownsampled));
            m_CacaoShadowMultiplier = Unpack(
                fetcher.Find(x => x.cacaoShadowMultiplier));
            m_CacaoShadowPower = Unpack(fetcher.Find(x => x.cacaoShadowPower));
            m_CacaoShadowClamp = Unpack(fetcher.Find(x => x.cacaoShadowClamp));
            m_CacaoHorizonAngleThreshold = Unpack(
                fetcher.Find(x => x.cacaoHorizonAngleThreshold));
            m_CacaoFadeOutFrom = Unpack(fetcher.Find(x => x.cacaoFadeOutFrom));
            m_CacaoFadeOutTo = Unpack(fetcher.Find(x => x.cacaoFadeOutTo));
            m_CacaoAdaptiveQualityLimit = Unpack(
                fetcher.Find(x => x.cacaoAdaptiveQualityLimit));
            m_CacaoBlurPasses = Unpack(fetcher.Find(x => x.cacaoBlurPasses));
            m_CacaoSharpness = Unpack(fetcher.Find(x => x.cacaoSharpness));
            m_CacaoDetailShadowStrength = Unpack(
                fetcher.Find(x => x.cacaoDetailShadowStrength));
            m_CacaoBilateralSigmaSquared = Unpack(
                fetcher.Find(x => x.cacaoBilateralSigmaSquared));
            m_CacaoBilateralSimilarityDistanceSigma = Unpack(
                fetcher.Find(x => x.cacaoBilateralSimilarityDistanceSigma));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Enabled, s_Enabled);
            PropertyField(m_Implementation, s_Implementation);
            PropertyField(m_Radius, s_Radius);

            if (m_Implementation.value.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox(
                    "Select a common implementation to edit implementation-specific settings.",
                    MessageType.Info);
                return;
            }

            if (
                (AmbientOcclusionImplementation)m_Implementation.value.intValue
                == AmbientOcclusionImplementation.FidelityFXCACAO)
            {
                DrawCacaoPanel();
                return;
            }

            DrawGtaoPanel();
        }

        private void DrawGtaoPanel()
        {
            DrawSectionHeader("XeGTAO");
            DrawQualityLevel(maximum: 3);
            PropertyField(m_DenoisePasses, s_DenoisePasses);
            PropertyField(m_FalloffRange, s_FalloffRange);
            PropertyField(m_FinalValuePower, s_FinalValuePower);
        }

        private void DrawCacaoPanel()
        {
            DrawSectionHeader("FidelityFX CACAO");
            DrawQualityLevel(maximum: 4);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sampling", EditorStyles.boldLabel);
            PropertyField(m_CacaoDownsampled, s_Downsampled);
            PropertyField(m_CacaoAdaptiveQualityLimit, s_AdaptiveQualityLimit);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Occlusion", EditorStyles.boldLabel);
            PropertyField(m_CacaoShadowMultiplier, s_ShadowMultiplier);
            PropertyField(m_CacaoShadowPower, s_ShadowPower);
            PropertyField(m_CacaoShadowClamp, s_ShadowClamp);
            PropertyField(m_CacaoHorizonAngleThreshold, s_HorizonAngleThreshold);
            PropertyField(m_CacaoDetailShadowStrength, s_DetailShadowStrength);
            PropertyField(m_CacaoFadeOutFrom, s_FadeOutFrom);
            PropertyField(m_CacaoFadeOutTo, s_FadeOutTo);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Filtering", EditorStyles.boldLabel);
            PropertyField(m_CacaoBlurPasses, s_BlurPasses);
            PropertyField(m_CacaoSharpness, s_Sharpness);
            PropertyField(m_CacaoBilateralSigmaSquared, s_BilateralSigmaSquared);
            PropertyField(
                m_CacaoBilateralSimilarityDistanceSigma,
                s_BilateralSimilarityDistanceSigma);
        }

        private void DrawQualityLevel(int maximum)
        {
            using (
                var scope = new OverridablePropertyScope(
                    m_QualityLevel,
                    s_QualityLevel,
                    this))
            {
                if (!scope.displayed)
                    return;

                var oldMixedValue = EditorGUI.showMixedValue;
                EditorGUI.showMixedValue =
                    m_QualityLevel.value.hasMultipleDifferentValues;

                var rect = EditorGUILayout.GetControlRect();
                EditorGUI.BeginProperty(rect, s_QualityLevel, m_QualityLevel.value);
                EditorGUI.BeginChangeCheck();
                int value = EditorGUI.IntSlider(
                    rect,
                    s_QualityLevel,
                    Mathf.Clamp(m_QualityLevel.value.intValue, 0, maximum),
                    0,
                    maximum);
                if (EditorGUI.EndChangeCheck())
                    m_QualityLevel.value.intValue = value;

                EditorGUI.EndProperty();
                EditorGUI.showMixedValue = oldMixedValue;
            }
        }

        private static void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
