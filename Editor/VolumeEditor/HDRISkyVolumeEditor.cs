using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(HDRISkyVolume))]
    internal sealed class HDRISkyVolumeEditor : VolumeComponentEditor
    {
        private static readonly GUIContent s_IntensityModeLabel =
            EditorGUIUtility.TrTextContent("Intensity Mode", "Specifies the intensity mode used for the sky.");
        private static readonly GUIContent s_ExposureLabel =
            EditorGUIUtility.TrTextContent("Exposure", "Sets the exposure of the sky in EV.");
        private static readonly GUIContent s_MultiplierLabel =
            EditorGUIUtility.TrTextContent("Multiplier", "Sets the intensity multiplier for the sky.");
        private static readonly GUIContent s_DesiredLuxValueLabel =
            EditorGUIUtility.TrTextContent("Desired Lux Value", "Sets the absolute intensity (in Lux) of the current HDR texture set in HDRI Sky.");
        private static readonly GUIContent s_RotationLabel =
            EditorGUIUtility.TrTextContent("Rotation", "Controls the rotation of the sky along the Y axis.");

        private static readonly GUIContent[] s_IntensityModeOptions =
        {
            new("Exposure"),
            new("Multiplier"),
            new("Lux")
        };

        private static readonly int[] s_IntensityModeValues =
        {
            (int)SkyIntensityMode.Exposure,
            (int)SkyIntensityMode.Multiplier,
            (int)SkyIntensityMode.Lux
        };

        private SerializedDataParameter m_SkyCubemap;
        private SerializedDataParameter m_SkyIntensityMode;
        private SerializedDataParameter m_Exposure;
        private SerializedDataParameter m_Multiplier;
        private SerializedDataParameter m_DesiredLuxValue;
        private SerializedDataParameter m_UpperHemisphereLuxValue;
        private SerializedDataParameter m_Rotation;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<HDRISkyVolume>(serializedObject);
            m_SkyCubemap = Unpack(fetcher.Find(x => x.skyCubemap));
            m_SkyIntensityMode = Unpack(fetcher.Find(x => x.skyIntensityMode));
            m_Exposure = Unpack(fetcher.Find(x => x.exposure));
            m_Multiplier = Unpack(fetcher.Find(x => x.multiplier));
            m_DesiredLuxValue = Unpack(fetcher.Find(x => x.desiredLuxValue));
            m_UpperHemisphereLuxValue = Unpack(fetcher.Find(x => x.upperHemisphereLuxValue));
            m_Rotation = Unpack(fetcher.Find(x => x.rotation));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_SkyCubemap);
            DrawIntensityMode();

            PropertyField(m_Rotation, s_RotationLabel);
        }

        private void DrawIntensityMode()
        {
            using (var scope = new OverridablePropertyScope(m_SkyIntensityMode, s_IntensityModeLabel, this))
            {
                if (!scope.displayed)
                    return;

                var rect = EditorGUILayout.GetControlRect();
                EditorGUI.BeginProperty(rect, s_IntensityModeLabel, m_SkyIntensityMode.value);
                EditorGUI.BeginChangeCheck();
                var selected = EditorGUI.IntPopup(rect, s_IntensityModeLabel, m_SkyIntensityMode.value.intValue, s_IntensityModeOptions, s_IntensityModeValues);
                if (EditorGUI.EndChangeCheck())
                    m_SkyIntensityMode.value.intValue = selected;
                EditorGUI.EndProperty();
            }

            using (new EditorGUI.IndentLevelScope())
            {
                var mode = (SkyIntensityMode)m_SkyIntensityMode.value.intValue;
                switch (mode)
                {
                    case SkyIntensityMode.Exposure:
                        PropertyField(m_Exposure, s_ExposureLabel);
                        break;
                    case SkyIntensityMode.Multiplier:
                        PropertyField(m_Multiplier, s_MultiplierLabel);
                        break;
                    case SkyIntensityMode.Lux:
                        PropertyField(m_DesiredLuxValue, s_DesiredLuxValueLabel);
                        var luxValue = m_UpperHemisphereLuxValue.value.floatValue;
                        var desiredLux = m_DesiredLuxValue.value.floatValue;
                        EditorGUILayout.HelpBox(
                            $"Upper hemisphere lux value: {luxValue:F1}\nAbsolute multiplier: {desiredLux / Mathf.Max(luxValue, 1e-5f):F4}",
                            MessageType.Info);
                        break;
                }
            }
        }
    }
}