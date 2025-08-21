using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(MobileBloom))]
    sealed class MobileBloomEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Mode;
        SerializedDataParameter m_Threshold;
        SerializedDataParameter m_Intensity;
        SerializedDataParameter m_LumRangeScale;
        SerializedDataParameter m_PreFilterScale;
        SerializedDataParameter m_BlurCompositeWeight;
        SerializedDataParameter m_Scatter;
        SerializedDataParameter m_Clamp;
        SerializedDataParameter m_Tint;
        SerializedDataParameter m_HighQualityFiltering;
        SerializedDataParameter m_Downsample;
        SerializedDataParameter m_MaxIterations;
        SerializedDataParameter m_DirtTexture;
        SerializedDataParameter m_DirtIntensity;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<MobileBloom>(serializedObject);

            m_Mode = Unpack(o.Find(x => x.mode));
            m_Threshold = Unpack(o.Find(x => x.threshold));
            m_Intensity = Unpack(o.Find(x => x.intensity));
            m_LumRangeScale = Unpack(o.Find(x => x.lumRangeScale));
            m_PreFilterScale = Unpack(o.Find(x => x.preFilterScale));
            m_BlurCompositeWeight = Unpack(o.Find(x => x.blurCompositeWeight));
            m_Scatter = Unpack(o.Find(x => x.scatter));
            m_Clamp = Unpack(o.Find(x => x.clamp));
            m_Tint = Unpack(o.Find(x => x.tint));
            m_HighQualityFiltering = Unpack(o.Find(x => x.highQualityFiltering));
            m_Downsample = Unpack(o.Find(x => x.downscale));
            m_MaxIterations = Unpack(o.Find(x => x.maxIterations));
            m_DirtTexture = Unpack(o.Find(x => x.dirtTexture));
            m_DirtIntensity = Unpack(o.Find(x => x.dirtIntensity));
        }

        public override void OnInspectorGUI()
        {
            if (m_Mode.value.intValue == (int)BloomMode.Moblie)
            {
                PropertyField(m_Mode);
                PropertyField(m_Threshold);
                PropertyField(m_Tint);
                PropertyField(m_LumRangeScale);
                PropertyField(m_PreFilterScale);
                PropertyField(m_BlurCompositeWeight);
                PropertyField(m_Intensity);
            }
            else if (m_Mode.value.intValue == (int)BloomMode.URP)
            {
                PropertyField(m_Mode);
                PropertyField(m_Threshold);
                PropertyField(m_Intensity);
                PropertyField(m_Scatter);
                PropertyField(m_Tint);
                PropertyField(m_Clamp);
                PropertyField(m_HighQualityFiltering);

                PropertyField(m_Downsample);
                PropertyField(m_MaxIterations);

                PropertyField(m_DirtTexture);
                PropertyField(m_DirtIntensity);
            }
            else
            {
                PropertyField(m_Mode);
            }
        }
    }
}