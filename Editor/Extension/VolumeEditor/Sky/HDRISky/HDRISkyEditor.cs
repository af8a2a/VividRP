using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(HDRISkySetting))]
    sealed class HDRISkyEditor : SkySettingsEditor
    {
        SerializedDataParameter m_HDRISky;
        //SerializedDataParameter m_UpperHemisphereLuxValue;
        //SerializedDataParameter m_UpperHemisphereLuxColor;

        public override void OnEnable()
        {
            base.OnEnable();

            // HDRI sky does not have control over sun display.
            m_CommonUIElementsMask = 0xFFFFFFFF & ~(uint)(SkySettingsUIElement.IncludeSunInBaking);


            m_EnableLuxIntensityMode = true;
            var o = new PropertyFetcher<HDRISkySetting>(serializedObject);

            m_HDRISky = Unpack(o.Find(x => x.hdriSky));
            //m_UpperHemisphereLuxValue = Unpack(o.Find(x => x.upperHemisphereLuxValue));
            //m_UpperHemisphereLuxColor = Unpack(o.Find(x => x.upperHemisphereLuxColor));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_HDRISky);

            base.CommonSkySettingsGUI();
        }
    }
}
