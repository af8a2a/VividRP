using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(VividVolumetricFogVolume))]
    internal sealed class VividVolumetricFogVolumeEditor : VolumeComponentEditor
    {
        private static readonly GUIContent s_StateLabel =
            EditorGUIUtility.TrTextContent("State", "Enables fog for this volume.");
        private static readonly GUIContent s_FogAttenuationDistanceLabel =
            EditorGUIUtility.TrTextContent("Fog Attenuation Distance", "Average distance, in meters, before light is scattered or absorbed.");
        private static readonly GUIContent s_MaxFogDistanceLabel =
            EditorGUIUtility.TrTextContent("Max Fog Distance", "Maximum distance affected by height fog.");
        private static readonly GUIContent s_GIDimmerLabel =
            EditorGUIUtility.TrTextContent("GI Dimmer", "Contribution multiplier for ambient probe lighting inside the volume.");
        private static readonly GUIContent s_VolumetricFogDistanceLabel =
            EditorGUIUtility.TrTextContent("Volumetric Fog Distance", "Maximum view distance covered by the volumetric buffer.");

        private SerializedDataParameter m_Enabled;
        private SerializedDataParameter m_MeanFreePath;
        private SerializedDataParameter m_BaseHeight;
        private SerializedDataParameter m_MaximumHeight;
        private SerializedDataParameter m_MaxFogDistance;
        private SerializedDataParameter m_ColorMode;
        private SerializedDataParameter m_Tint;
        private SerializedDataParameter m_MipFogNear;
        private SerializedDataParameter m_MipFogFar;
        private SerializedDataParameter m_MipFogMaxMip;
        private SerializedDataParameter m_VolumetricFog;
        private SerializedDataParameter m_Albedo;
        private SerializedDataParameter m_Anisotropy;
        private SerializedDataParameter m_GlobalLightProbeDimmer;
        private SerializedDataParameter m_DepthExtent;
        private SerializedDataParameter m_SliceDistributionUniformity;
        private SerializedDataParameter m_Tier;
        private SerializedDataParameter m_FogControlMode;
        private SerializedDataParameter m_VolumetricFogBudget;
        private SerializedDataParameter m_ResolutionDepthRatio;
        private SerializedDataParameter m_ScreenResolutionPercentage;
        private SerializedDataParameter m_VolumeSliceCount;
        private SerializedDataParameter m_DenoisingMode;
        private SerializedDataParameter m_DirectionalLightsOnly;
        private SerializedDataParameter m_VolumetricLightingDensityCutoff;
        private SerializedDataParameter m_MultipleScatteringIntensity;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<VividVolumetricFogVolume>(serializedObject);
            m_Enabled = Unpack(fetcher.Find(x => x.enabled));
            m_MeanFreePath = Unpack(fetcher.Find(x => x.meanFreePath));
            m_BaseHeight = Unpack(fetcher.Find(x => x.baseHeight));
            m_MaximumHeight = Unpack(fetcher.Find(x => x.maximumHeight));
            m_MaxFogDistance = Unpack(fetcher.Find(x => x.maxFogDistance));
            m_ColorMode = Unpack(fetcher.Find(x => x.colorMode));
            m_Tint = Unpack(fetcher.Find(x => x.tint));
            m_MipFogNear = Unpack(fetcher.Find(x => x.mipFogNear));
            m_MipFogFar = Unpack(fetcher.Find(x => x.mipFogFar));
            m_MipFogMaxMip = Unpack(fetcher.Find(x => x.mipFogMaxMip));
            m_VolumetricFog = Unpack(fetcher.Find(x => x.volumetricFog));
            m_Albedo = Unpack(fetcher.Find(x => x.albedo));
            m_Anisotropy = Unpack(fetcher.Find(x => x.anisotropy));
            m_GlobalLightProbeDimmer = Unpack(fetcher.Find(x => x.globalLightProbeDimmer));
            m_DepthExtent = Unpack(fetcher.Find(x => x.depthExtent));
            m_SliceDistributionUniformity = Unpack(fetcher.Find(x => x.sliceDistributionUniformity));
            m_Tier = Unpack(fetcher.Find(x => x.tier));
            m_FogControlMode = Unpack(fetcher.Find(x => x.fogControlMode));
            m_VolumetricFogBudget = Unpack(fetcher.Find(x => x.volumetricFogBudget));
            m_ResolutionDepthRatio = Unpack(fetcher.Find(x => x.resolutionDepthRatio));
            m_ScreenResolutionPercentage = Unpack(fetcher.Find(x => x.screenResolutionPercentage));
            m_VolumeSliceCount = Unpack(fetcher.Find(x => x.volumeSliceCount));
            m_DenoisingMode = Unpack(fetcher.Find(x => x.denoisingMode));
            m_DirectionalLightsOnly = Unpack(fetcher.Find(x => x.directionalLightsOnly));
            m_VolumetricLightingDensityCutoff = Unpack(fetcher.Find(x => x.volumetricLightingDensityCutoff));
            m_MultipleScatteringIntensity = Unpack(fetcher.Find(x => x.multipleScatteringIntensity));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Enabled, s_StateLabel);
            PropertyField(m_MeanFreePath, s_FogAttenuationDistanceLabel);
            PropertyField(m_BaseHeight);
            PropertyField(m_MaximumHeight);
            DrawHeightRangeWarning();
            PropertyField(m_MaxFogDistance, s_MaxFogDistanceLabel);
            PropertyField(m_ColorMode);
            using (new EditorGUI.DisabledScope(ShouldDisableColorModeSettings()))
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_Tint);
                PropertyField(m_MipFogNear);
                PropertyField(m_MipFogFar);
                PropertyField(m_MipFogMaxMip);
            }

            PropertyField(m_VolumetricFog);
            using (new EditorGUI.DisabledScope(ShouldDisableVolumetricSettings()))
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_Albedo);
                PropertyField(m_GlobalLightProbeDimmer, s_GIDimmerLabel);
                PropertyField(m_DepthExtent, s_VolumetricFogDistanceLabel);
                PropertyField(m_DenoisingMode);
                PropertyField(m_SliceDistributionUniformity);
                PropertyField(m_Tier);
                PropertyField(m_FogControlMode);
                using (new EditorGUI.IndentLevelScope())
                {
                    if (ShouldShowBalanceQualitySettings())
                    {
                        PropertyField(m_VolumetricFogBudget);
                        PropertyField(m_ResolutionDepthRatio);
                    }

                    if (ShouldShowManualQualitySettings())
                    {
                        PropertyField(m_ScreenResolutionPercentage);
                        PropertyField(m_VolumeSliceCount);
                    }
                }

                PropertyField(m_DirectionalLightsOnly);
                PropertyField(m_Anisotropy);
                PropertyField(m_VolumetricLightingDensityCutoff);
            }
            PropertyField(m_MultipleScatteringIntensity);
        }

        private bool ShouldDisableColorModeSettings()
        {
            return !m_ColorMode.value.hasMultipleDifferentValues
                && (VividFogColorMode)m_ColorMode.value.intValue == VividFogColorMode.SkyColor;
        }

        private bool ShouldDisableVolumetricSettings()
        {
            return !m_VolumetricFog.value.hasMultipleDifferentValues
                && !m_VolumetricFog.value.boolValue;
        }

        private bool ShouldShowBalanceQualitySettings()
        {
            return m_FogControlMode.value.hasMultipleDifferentValues
                || (VividVolumetricFogControlMode)m_FogControlMode.value.intValue == VividVolumetricFogControlMode.Balance;
        }

        private bool ShouldShowManualQualitySettings()
        {
            return m_FogControlMode.value.hasMultipleDifferentValues
                || (VividVolumetricFogControlMode)m_FogControlMode.value.intValue == VividVolumetricFogControlMode.Manual;
        }

        private void DrawHeightRangeWarning()
        {
            if (m_BaseHeight.value.hasMultipleDifferentValues || m_MaximumHeight.value.hasMultipleDifferentValues)
                return;

            if (m_MaximumHeight.value.floatValue > m_BaseHeight.value.floatValue)
                return;

            EditorGUILayout.HelpBox("Maximum Height is clamped above Base Height at runtime.", MessageType.Info);
        }
    }
}
