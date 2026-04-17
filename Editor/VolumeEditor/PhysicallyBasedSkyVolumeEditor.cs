using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(PhysicallyBasedSkyVolume))]
    internal sealed class PhysicallyBasedSkyVolumeEditor : VolumeComponentEditor
    {
        private static readonly GUIContent[] s_ModelTypeOptions =
        {
            EditorGUIUtility.TrTextContent("Earth (Simple)"),
            EditorGUIUtility.TrTextContent("Earth (Advanced)"),
            EditorGUIUtility.TrTextContent("Custom Planet")
        };

        private static readonly int[] s_ModelTypeValues =
        {
            (int)PhysicallyBasedSkyModel.EarthSimple,
            (int)PhysicallyBasedSkyModel.EarthAdvanced,
            (int)PhysicallyBasedSkyModel.Custom
        };

        private static readonly GUIContent s_MaterialLabel =
            EditorGUIUtility.TrTextContent("Material", "Sets a custom material that is reserved for HDRP-style physically based sky authoring.");
        private static readonly GUIContent s_ExposureCompensationLabel =
            EditorGUIUtility.TrTextContent("Exposure Compensation", "Sets the exposure compensation of the sky in EV.");

        private SerializedDataParameter m_Type;
        private SerializedDataParameter m_AtmosphericScattering;
        private SerializedDataParameter m_RenderingMode;
        private SerializedDataParameter m_Material;
        private SerializedDataParameter m_PlanetRadius;
        private SerializedDataParameter m_PlanetRotation;
        private SerializedDataParameter m_GroundColorTexture;
        private SerializedDataParameter m_GroundTint;
        private SerializedDataParameter m_GroundEmissionTexture;
        private SerializedDataParameter m_GroundEmissionMultiplier;
        private SerializedDataParameter m_SpaceRotation;
        private SerializedDataParameter m_SpaceEmissionTexture;
        private SerializedDataParameter m_SpaceEmissionMultiplier;
        private SerializedDataParameter m_AirMaximumAltitude;
        private SerializedDataParameter m_AirDensityR;
        private SerializedDataParameter m_AirDensityG;
        private SerializedDataParameter m_AirDensityB;
        private SerializedDataParameter m_AirTint;
        private SerializedDataParameter m_AerosolMaximumAltitude;
        private SerializedDataParameter m_AerosolDensity;
        private SerializedDataParameter m_AerosolTint;
        private SerializedDataParameter m_AerosolAnisotropy;
        private SerializedDataParameter m_OzoneDensityDimmer;
        private SerializedDataParameter m_OzoneMinimumAltitude;
        private SerializedDataParameter m_OzoneLayerWidth;
        private SerializedDataParameter m_ColorSaturation;
        private SerializedDataParameter m_AlphaSaturation;
        private SerializedDataParameter m_AlphaMultiplier;
        private SerializedDataParameter m_Exposure;
        private SerializedDataParameter m_HorizonTint;
        private SerializedDataParameter m_ZenithTint;
        private SerializedDataParameter m_HorizonZenithShift;
        private SerializedDataParameter m_RenderSunDisk;
        private SerializedDataParameter m_SunDiskSize;
        private SerializedDataParameter m_EnableHeightFog;
        private SerializedDataParameter m_FogBaseHeight;
        private SerializedDataParameter m_FogDensity;
        private SerializedDataParameter m_FogMaxDistance;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<PhysicallyBasedSkyVolume>(serializedObject);
            m_Type = Unpack(fetcher.Find(x => x.type));
            m_AtmosphericScattering = Unpack(fetcher.Find(x => x.atmosphericScattering));
            m_RenderingMode = Unpack(fetcher.Find(x => x.renderingMode));
            m_Material = Unpack(fetcher.Find(x => x.material));
            m_PlanetRadius = Unpack(fetcher.Find(x => x.planetRadius));
            m_PlanetRotation = Unpack(fetcher.Find(x => x.planetRotation));
            m_GroundColorTexture = Unpack(fetcher.Find(x => x.groundColorTexture));
            m_GroundTint = Unpack(fetcher.Find(x => x.groundTint));
            m_GroundEmissionTexture = Unpack(fetcher.Find(x => x.groundEmissionTexture));
            m_GroundEmissionMultiplier = Unpack(fetcher.Find(x => x.groundEmissionMultiplier));
            m_SpaceRotation = Unpack(fetcher.Find(x => x.spaceRotation));
            m_SpaceEmissionTexture = Unpack(fetcher.Find(x => x.spaceEmissionTexture));
            m_SpaceEmissionMultiplier = Unpack(fetcher.Find(x => x.spaceEmissionMultiplier));
            m_AirMaximumAltitude = Unpack(fetcher.Find(x => x.airMaximumAltitude));
            m_AirDensityR = Unpack(fetcher.Find(x => x.airDensityR));
            m_AirDensityG = Unpack(fetcher.Find(x => x.airDensityG));
            m_AirDensityB = Unpack(fetcher.Find(x => x.airDensityB));
            m_AirTint = Unpack(fetcher.Find(x => x.airTint));
            m_AerosolMaximumAltitude = Unpack(fetcher.Find(x => x.aerosolMaximumAltitude));
            m_AerosolDensity = Unpack(fetcher.Find(x => x.aerosolDensity));
            m_AerosolTint = Unpack(fetcher.Find(x => x.aerosolTint));
            m_AerosolAnisotropy = Unpack(fetcher.Find(x => x.aerosolAnisotropy));
            m_OzoneDensityDimmer = Unpack(fetcher.Find(x => x.ozoneDensityDimmer));
            m_OzoneMinimumAltitude = Unpack(fetcher.Find(x => x.ozoneMinimumAltitude));
            m_OzoneLayerWidth = Unpack(fetcher.Find(x => x.ozoneLayerWidth));
            m_Exposure = Unpack(fetcher.Find(x => x.exposure));
            m_ColorSaturation = Unpack(fetcher.Find(x => x.colorSaturation));
            m_AlphaSaturation = Unpack(fetcher.Find(x => x.alphaSaturation));
            m_AlphaMultiplier = Unpack(fetcher.Find(x => x.alphaMultiplier));
            m_HorizonTint = Unpack(fetcher.Find(x => x.horizonTint));
            m_ZenithTint = Unpack(fetcher.Find(x => x.zenithTint));
            m_HorizonZenithShift = Unpack(fetcher.Find(x => x.horizonZenithShift));
            m_RenderSunDisk = Unpack(fetcher.Find(x => x.renderSunDisk));
            m_SunDiskSize = Unpack(fetcher.Find(x => x.sunDiskSize));
            m_EnableHeightFog = Unpack(fetcher.Find(x => x.enableHeightFog));
            m_FogBaseHeight = Unpack(fetcher.Find(x => x.fogBaseHeight));
            m_FogDensity = Unpack(fetcher.Find(x => x.fogDensity));
            m_FogMaxDistance = Unpack(fetcher.Find(x => x.fogMaxDistance));
        }

        public override void OnInspectorGUI()
        {
            var hasCustomMaterial = HasCustomMaterial();
            var isSimpleEarth = IsSimpleEarth();
            var isCustomPlanet = IsCustomPlanet();

            DrawSectionHeader("Model");
            DrawModelTypeField();
            PropertyField(m_AtmosphericScattering);

            DrawSectionHeader("Planet and Space");
            PropertyField(m_RenderingMode);
            if (hasCustomMaterial)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    PropertyField(m_Material, s_MaterialLabel);
                }
            }

            DrawSectionHeader("Planet");
            PropertyField(m_PlanetRadius);
            if (!isSimpleEarth && !hasCustomMaterial)
                PropertyField(m_PlanetRotation);

            if (!isSimpleEarth && !hasCustomMaterial)
                PropertyField(m_GroundColorTexture);

            PropertyField(m_GroundTint);

            if (!isSimpleEarth && !hasCustomMaterial)
            {
                PropertyField(m_GroundEmissionTexture);
                PropertyField(m_GroundEmissionMultiplier);
            }

            if (!isSimpleEarth && !hasCustomMaterial)
            {
                DrawSectionHeader("Space");
                PropertyField(m_SpaceRotation);
                PropertyField(m_SpaceEmissionTexture);
                PropertyField(m_SpaceEmissionMultiplier);
            }

            if (isCustomPlanet)
            {
                DrawSectionHeader("Air");
                PropertyField(m_AirMaximumAltitude);
                PropertyField(m_AirDensityR);
                PropertyField(m_AirDensityG);
                PropertyField(m_AirDensityB);
                PropertyField(m_AirTint);
            }

            DrawSectionHeader("Aerosols");
            PropertyField(m_AerosolDensity);
            PropertyField(m_AerosolTint);
            PropertyField(m_AerosolAnisotropy);
            if (!isSimpleEarth)
                PropertyField(m_AerosolMaximumAltitude);

            if (!isSimpleEarth)
            {
                DrawSectionHeader("Ozone");
                PropertyField(m_OzoneDensityDimmer);
                if (isCustomPlanet)
                {
                    PropertyField(m_OzoneMinimumAltitude);
                    PropertyField(m_OzoneLayerWidth);
                }
            }

            DrawSectionHeader("Artistic Overrides");
            PropertyField(m_ColorSaturation);
            PropertyField(m_AlphaSaturation);
            PropertyField(m_AlphaMultiplier);
            PropertyField(m_HorizonTint);
            PropertyField(m_HorizonZenithShift);
            PropertyField(m_ZenithTint);

            DrawSectionHeader("Sky");
            PropertyField(m_Exposure, s_ExposureCompensationLabel);

            DrawSectionHeader("Vivid Extensions");
            PropertyField(m_RenderSunDisk);
            if (ShouldShowSunDiskSize())
                PropertyField(m_SunDiskSize);

            DrawSectionHeader("Height Fog");
            PropertyField(m_EnableHeightFog);
            if (ShouldShowHeightFogSettings())
            {
                PropertyField(m_FogBaseHeight);
                PropertyField(m_FogDensity);
                PropertyField(m_FogMaxDistance);
            }
        }

        private void DrawModelTypeField()
        {
            var title = EditorGUIUtility.TrTextContent(m_Type.displayName, m_Type.GetAttribute<TooltipAttribute>()?.tooltip);
            using (var scope = new OverridablePropertyScope(m_Type, title, this))
            {
                if (!scope.displayed)
                    return;

                var rect = EditorGUILayout.GetControlRect();
                EditorGUI.BeginProperty(rect, title, m_Type.value);

                EditorGUI.BeginChangeCheck();
                var selectedValue = EditorGUI.IntPopup(rect, title, m_Type.value.intValue, s_ModelTypeOptions, s_ModelTypeValues);
                if (EditorGUI.EndChangeCheck())
                    m_Type.value.intValue = selectedValue;

                EditorGUI.EndProperty();
            }
        }

        private bool IsSimpleEarth()
        {
            return !m_Type.value.hasMultipleDifferentValues
                && (PhysicallyBasedSkyModel)m_Type.value.intValue == PhysicallyBasedSkyModel.EarthSimple;
        }

        private bool IsCustomPlanet()
        {
            return !m_Type.value.hasMultipleDifferentValues
                && (PhysicallyBasedSkyModel)m_Type.value.intValue == PhysicallyBasedSkyModel.Custom;
        }

        private bool HasCustomMaterial()
        {
            return m_RenderingMode.value.hasMultipleDifferentValues
                || (PhysicallyBasedSkyRenderingMode)m_RenderingMode.value.intValue == PhysicallyBasedSkyRenderingMode.Material;
        }

        private bool ShouldShowSunDiskSize()
        {
            return m_RenderSunDisk.value.hasMultipleDifferentValues || m_RenderSunDisk.value.boolValue;
        }

        private bool ShouldShowHeightFogSettings()
        {
            return m_EnableHeightFog.value.hasMultipleDifferentValues || m_EnableHeightFog.value.boolValue;
        }

        private static void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
