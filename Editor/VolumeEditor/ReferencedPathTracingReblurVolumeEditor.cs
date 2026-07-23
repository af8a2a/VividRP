using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(ReferencedPathTracingReblurVolume))]
    internal sealed class ReferencedPathTracingReblurVolumeEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_Enabled;
        private SerializedDataParameter m_MaxAccumulatedFrameNum;
        private SerializedDataParameter m_MaxFastAccumulatedFrameNum;
        private SerializedDataParameter m_HistoryFixFrameNum;
        private SerializedDataParameter m_HistoryFixBasePixelStride;
        private SerializedDataParameter m_FastHistoryClampingSigmaScale;
        private SerializedDataParameter m_DiffusePrepassBlurRadius;
        private SerializedDataParameter m_SpecularPrepassBlurRadius;
        private SerializedDataParameter m_MinBlurRadius;
        private SerializedDataParameter m_MaxBlurRadius;
        private SerializedDataParameter m_LobeAngleFraction;
        private SerializedDataParameter m_RoughnessFraction;
        private SerializedDataParameter m_PlaneDistanceSensitivity;
        private SerializedDataParameter m_MinHitDistanceWeight;
        private SerializedDataParameter m_FireflySuppressorMinRelativeScale;
        private SerializedDataParameter m_EnableAntiFirefly;
        private SerializedDataParameter m_UsePrepassOnlyForSpecularMotionEstimation;
        private SerializedDataParameter m_AntilagLuminanceSigmaScale;
        private SerializedDataParameter m_AntilagLuminanceSensitivity;
        private SerializedDataParameter m_ResponsiveAccumulationRoughnessThreshold;
        private SerializedDataParameter m_ResponsiveAccumulationMinFrameNum;
        private SerializedDataParameter m_HitDistanceA;
        private SerializedDataParameter m_HitDistanceB;
        private SerializedDataParameter m_HitDistanceC;
        private SerializedDataParameter m_HitDistanceD;
        private SerializedDataParameter m_ReturnHistoryLengthInsteadOfOcclusion;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<ReferencedPathTracingReblurVolume>(serializedObject);
            m_Enabled = Unpack(fetcher.Find(x => x.enabled));
            m_MaxAccumulatedFrameNum = Unpack(fetcher.Find(x => x.maxAccumulatedFrameNum));
            m_MaxFastAccumulatedFrameNum = Unpack(fetcher.Find(x => x.maxFastAccumulatedFrameNum));
            m_HistoryFixFrameNum = Unpack(fetcher.Find(x => x.historyFixFrameNum));
            m_HistoryFixBasePixelStride = Unpack(fetcher.Find(x => x.historyFixBasePixelStride));
            m_FastHistoryClampingSigmaScale =
                Unpack(fetcher.Find(x => x.fastHistoryClampingSigmaScale));
            m_DiffusePrepassBlurRadius = Unpack(fetcher.Find(x => x.diffusePrepassBlurRadius));
            m_SpecularPrepassBlurRadius = Unpack(fetcher.Find(x => x.specularPrepassBlurRadius));
            m_MinBlurRadius = Unpack(fetcher.Find(x => x.minBlurRadius));
            m_MaxBlurRadius = Unpack(fetcher.Find(x => x.maxBlurRadius));
            m_LobeAngleFraction = Unpack(fetcher.Find(x => x.lobeAngleFraction));
            m_RoughnessFraction = Unpack(fetcher.Find(x => x.roughnessFraction));
            m_PlaneDistanceSensitivity = Unpack(fetcher.Find(x => x.planeDistanceSensitivity));
            m_MinHitDistanceWeight = Unpack(fetcher.Find(x => x.minHitDistanceWeight));
            m_FireflySuppressorMinRelativeScale =
                Unpack(fetcher.Find(x => x.fireflySuppressorMinRelativeScale));
            m_EnableAntiFirefly = Unpack(fetcher.Find(x => x.enableAntiFirefly));
            m_UsePrepassOnlyForSpecularMotionEstimation =
                Unpack(fetcher.Find(x => x.usePrepassOnlyForSpecularMotionEstimation));
            m_AntilagLuminanceSigmaScale =
                Unpack(fetcher.Find(x => x.antilagLuminanceSigmaScale));
            m_AntilagLuminanceSensitivity =
                Unpack(fetcher.Find(x => x.antilagLuminanceSensitivity));
            m_ResponsiveAccumulationRoughnessThreshold =
                Unpack(fetcher.Find(x => x.responsiveAccumulationRoughnessThreshold));
            m_ResponsiveAccumulationMinFrameNum =
                Unpack(fetcher.Find(x => x.responsiveAccumulationMinFrameNum));
            m_HitDistanceA = Unpack(fetcher.Find(x => x.hitDistanceA));
            m_HitDistanceB = Unpack(fetcher.Find(x => x.hitDistanceB));
            m_HitDistanceC = Unpack(fetcher.Find(x => x.hitDistanceC));
            m_HitDistanceD = Unpack(fetcher.Find(x => x.hitDistanceD));
            m_ReturnHistoryLengthInsteadOfOcclusion =
                Unpack(fetcher.Find(x => x.returnHistoryLengthInsteadOfOcclusion));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Enabled);

            DrawSectionHeader("Accumulation");
            PropertyField(m_MaxAccumulatedFrameNum);
            PropertyField(m_MaxFastAccumulatedFrameNum);
            PropertyField(m_HistoryFixFrameNum);
            PropertyField(m_HistoryFixBasePixelStride);
            PropertyField(m_FastHistoryClampingSigmaScale);
            PropertyField(m_ResponsiveAccumulationRoughnessThreshold);
            PropertyField(m_ResponsiveAccumulationMinFrameNum);

            DrawSectionHeader("Anti-lag");
            PropertyField(m_AntilagLuminanceSigmaScale);
            PropertyField(m_AntilagLuminanceSensitivity);

            DrawSectionHeader("Spatial filtering");
            PropertyField(m_DiffusePrepassBlurRadius);
            PropertyField(m_SpecularPrepassBlurRadius);
            PropertyField(m_MinBlurRadius);
            PropertyField(m_MaxBlurRadius);
            PropertyField(m_LobeAngleFraction);
            PropertyField(m_RoughnessFraction);
            PropertyField(m_PlaneDistanceSensitivity);
            PropertyField(m_MinHitDistanceWeight);
            PropertyField(m_UsePrepassOnlyForSpecularMotionEstimation);

            DrawSectionHeader("Firefly suppression");
            PropertyField(m_FireflySuppressorMinRelativeScale);
            PropertyField(m_EnableAntiFirefly);

            DrawSectionHeader("Hit-distance normalization");
            PropertyField(m_HitDistanceA);
            PropertyField(m_HitDistanceB);
            PropertyField(m_HitDistanceC);
            PropertyField(m_HitDistanceD);

            DrawSectionHeader("Debug");
            PropertyField(m_ReturnHistoryLengthInsteadOfOcclusion);
            EditorGUILayout.HelpBox(
                "Checkerboard, hit-distance reconstruction and temporal stabilization remain disabled in the current full-resolution integration.",
                MessageType.Info);
        }

        private static void DrawSectionHeader(string text)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }
    }
}
