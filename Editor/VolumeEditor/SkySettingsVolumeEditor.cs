using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(SkySettingsVolume))]
    internal sealed class SkySettingsVolumeEditor : VolumeComponentEditor
    {
        private static readonly GUIContent s_SkyTypeLabel =
            EditorGUIUtility.TrTextContent("Sky Type", "Specifies the type of sky this Volume uses.");
        private static readonly GUIContent s_UpdateModeLabel =
            EditorGUIUtility.TrTextContent("Update Mode", "Specifies when VividRP updates the sky environment.");
        private static readonly GUIContent s_UpdatePeriodLabel =
            EditorGUIUtility.TrTextContent("Update Period", "Sets the period, in seconds, between realtime sky updates.");
        private static readonly GUIContent s_IncludeSunInBakingLabel =
            EditorGUIUtility.TrTextContent("Include Sun In Baking", "When enabled, VividRP uses the Sun Disk in baked lighting.");
        private static readonly GUIContent s_GeneratedCubemapQualityLabel =
            EditorGUIUtility.TrTextContent("Generated Cubemap Quality", "Controls the quality of generated runtime sky cubemaps and ambient probe bakes.");
        private static readonly GUIContent s_RenderingSpaceLabel =
            EditorGUIUtility.TrTextContent("Rendering Space", "Controls whether the sky is evaluated in camera-relative or world space.");
        private static readonly GUIContent s_CenterModeLabel =
            EditorGUIUtility.TrTextContent("Center", "The center is used when defining where the planet surface is. In automatic mode, the center is derived from the active planet radius.");
        private static readonly GUIContent s_PlanetCenterLabel =
            EditorGUIUtility.TrTextContent("Position", "Sets the world-space position of the planet center.");

        private static GUIContent[] s_SkyTypeOptions;
        private static int[] s_SkyTypeValues;

        private SerializedDataParameter m_SkyType;
        private SerializedDataParameter m_UpdateMode;
        private SerializedDataParameter m_UpdatePeriod;
        private SerializedDataParameter m_IncludeSunInBaking;
        private SerializedDataParameter m_GeneratedCubemapQuality;
        private SerializedDataParameter m_RenderingSpace;
        private SerializedDataParameter m_CenterMode;
        private SerializedDataParameter m_PlanetCenter;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<SkySettingsVolume>(serializedObject);
            m_SkyType = Unpack(fetcher.Find(x => x.skyType));
            m_UpdateMode = Unpack(fetcher.Find(x => x.updateMode));
            m_UpdatePeriod = Unpack(fetcher.Find(x => x.updatePeriod));
            m_IncludeSunInBaking = Unpack(fetcher.Find(x => x.includeSunInBaking));
            m_GeneratedCubemapQuality = Unpack(fetcher.Find(x => x.generatedCubemapQuality));
            m_RenderingSpace = Unpack(fetcher.Find(x => x.renderingSpace));
            m_CenterMode = Unpack(fetcher.Find(x => x.centerMode));
            m_PlanetCenter = Unpack(fetcher.Find(x => x.planetCenter));
        }

        public override void OnInspectorGUI()
        {
            UpdateSkyTypePopupData();
            DrawSkyTypeField();
            DrawUpdateSettings();
            PropertyField(m_IncludeSunInBaking, s_IncludeSunInBakingLabel);

            DrawSectionHeader("Planet");
            PropertyField(m_RenderingSpace, s_RenderingSpaceLabel);
            DrawPlanetCenterSettings();

            DrawSectionHeader("Vivid Extensions");
            PropertyField(m_GeneratedCubemapQuality, s_GeneratedCubemapQualityLabel);
        }

        private void DrawSkyTypeField()
        {
            using (var scope = new OverridablePropertyScope(m_SkyType, s_SkyTypeLabel, this))
            {
                if (!scope.displayed)
                    return;

                var rect = EditorGUILayout.GetControlRect();
                EditorGUI.BeginProperty(rect, s_SkyTypeLabel, m_SkyType.value);
                EditorGUI.BeginChangeCheck();
                var selectedValue = EditorGUI.IntPopup(rect, s_SkyTypeLabel, m_SkyType.value.intValue, s_SkyTypeOptions, s_SkyTypeValues);
                if (EditorGUI.EndChangeCheck())
                    m_SkyType.value.intValue = selectedValue;
                EditorGUI.EndProperty();
            }
        }

        private void DrawUpdateSettings()
        {
            PropertyField(m_UpdateMode, s_UpdateModeLabel);
            if (!m_UpdateMode.value.hasMultipleDifferentValues
                && m_UpdateMode.value.intValue == (int)SkyUpdateMode.Realtime)
            {
                using (new EditorGUI.IndentLevelScope())
                    PropertyField(m_UpdatePeriod, s_UpdatePeriodLabel);
            }
        }

        private void DrawPlanetCenterSettings()
        {
            if (m_RenderingSpace.value.intValue == (int)RenderingSpace.World && BeginAdditionalPropertiesScope())
            {
                PropertyField(m_CenterMode, s_CenterModeLabel);

                if (m_CenterMode.value.intValue == (int)PlanetMode.Manual)
                {
                    using (new EditorGUI.IndentLevelScope())
                        PropertyField(m_PlanetCenter, s_PlanetCenterLabel);
                }

                EndAdditionalPropertiesScope();
            }
        }

        private static void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void UpdateSkyTypePopupData()
        {
            if (s_SkyTypeOptions != null && s_SkyTypeValues != null)
                return;

            var skyTypeOptions = new List<GUIContent>();
            var skyTypeValues = new List<int>();

            foreach (SkyType skyType in Enum.GetValues(typeof(SkyType)))
            {
                skyTypeOptions.Add(new GUIContent(GetSkyTypeDisplayName(skyType)));
                skyTypeValues.Add((int)skyType);
            }

            s_SkyTypeOptions = skyTypeOptions.ToArray();
            s_SkyTypeValues = skyTypeValues.ToArray();
        }

        private static string GetSkyTypeDisplayName(SkyType skyType)
        {
            return skyType switch
            {
                SkyType.None => "None",
                SkyType.HDRI => "HDRI Sky",
                SkyType.PhysicallyBased => "Physically Based Sky",
                _ => ObjectNames.NicifyVariableName(skyType.ToString())
            };
        }
    }
}
