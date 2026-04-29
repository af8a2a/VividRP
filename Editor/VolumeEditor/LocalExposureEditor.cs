using UnityEditor;
using UnityEditor.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(LocalExposure))]
    internal sealed class LocalExposureEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_Enabled;
        private SerializedDataParameter m_HighlightContrastScale;
        private SerializedDataParameter m_ShadowContrastScale;
        private SerializedDataParameter m_HighlightContrastCurve;
        private SerializedDataParameter m_ShadowContrastCurve;
        private SerializedDataParameter m_DetailStrength;
        private SerializedDataParameter m_BlurredLuminanceBlend;
        private SerializedDataParameter m_BlurredLuminanceKernelSizePercent;
        private SerializedDataParameter m_HighlightThreshold;
        private SerializedDataParameter m_ShadowThreshold;
        private SerializedDataParameter m_HighlightThresholdStrength;
        private SerializedDataParameter m_ShadowThresholdStrength;
        private SerializedDataParameter m_MiddleGreyBias;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<LocalExposure>(serializedObject);
            m_Enabled = Unpack(o.Find(x => x.enabled));
            m_HighlightContrastScale = Unpack(o.Find(x => x.highlightContrastScale));
            m_ShadowContrastScale = Unpack(o.Find(x => x.shadowContrastScale));
            m_HighlightContrastCurve = Unpack(o.Find(x => x.highlightContrastCurve));
            m_ShadowContrastCurve = Unpack(o.Find(x => x.shadowContrastCurve));
            m_DetailStrength = Unpack(o.Find(x => x.detailStrength));
            m_BlurredLuminanceBlend = Unpack(o.Find(x => x.blurredLuminanceBlend));
            m_BlurredLuminanceKernelSizePercent = Unpack(o.Find(x => x.blurredLuminanceKernelSizePercent));
            m_HighlightThreshold = Unpack(o.Find(x => x.highlightThreshold));
            m_ShadowThreshold = Unpack(o.Find(x => x.shadowThreshold));
            m_HighlightThresholdStrength = Unpack(o.Find(x => x.highlightThresholdStrength));
            m_ShadowThresholdStrength = Unpack(o.Find(x => x.shadowThresholdStrength));
            m_MiddleGreyBias = Unpack(o.Find(x => x.middleGreyBias));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Enabled);

            using (new EditorGUI.DisabledScope(!m_Enabled.value.boolValue))
            {
                PropertyField(m_HighlightContrastScale);
                PropertyField(m_ShadowContrastScale);
                PropertyField(m_HighlightContrastCurve);
                PropertyField(m_ShadowContrastCurve);
                PropertyField(m_DetailStrength);
                PropertyField(m_BlurredLuminanceBlend);
                PropertyField(m_BlurredLuminanceKernelSizePercent);
                PropertyField(m_HighlightThreshold);
                PropertyField(m_ShadowThreshold);
                PropertyField(m_HighlightThresholdStrength);
                PropertyField(m_ShadowThresholdStrength);
                PropertyField(m_MiddleGreyBias);
            }
        }
    }
}
