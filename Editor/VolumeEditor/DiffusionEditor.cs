using UnityEditor;
using UnityEditor.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Diffusion))]
    internal sealed class DiffusionEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_Enabled;
        private SerializedDataParameter m_Mode;
        private SerializedDataParameter m_Multiply;
        private SerializedDataParameter m_BlurScale;
        private SerializedDataParameter m_Filter;
        private SerializedDataParameter m_Intensity;
        private SerializedDataParameter m_BlurIntensity;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<Diffusion>(serializedObject);
            m_Enabled = Unpack(o.Find(x => x.enabled));
            m_Mode = Unpack(o.Find(x => x.mode));
            m_Multiply = Unpack(o.Find(x => x.multiply));
            m_BlurScale = Unpack(o.Find(x => x.blurScale));
            m_Filter = Unpack(o.Find(x => x.filter));
            m_Intensity = Unpack(o.Find(x => x.intensity));
            m_BlurIntensity = Unpack(o.Find(x => x.blurIntensity));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Enabled);

            using (new EditorGUI.DisabledScope(!m_Enabled.value.boolValue))
            {
                PropertyField(m_Mode);
                PropertyField(m_BlurScale);
                PropertyField(m_BlurIntensity);

                var mode = (DiffusionMode)m_Mode.value.intValue;
                if (mode == DiffusionMode.Max)
                {
                    PropertyField(m_Intensity);
                }
                else
                {
                    PropertyField(m_Multiply);
                    PropertyField(m_Filter);
                }
            }
        }
    }
}
