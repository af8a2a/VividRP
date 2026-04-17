using NUnit.Framework;
using UnityEngine;
using System.IO;
using System.Reflection;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyVolumeTests
    {
        [Test]
        public void Constructor_UsesHdrpAlignedDefaultModel()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                Assert.That(volume.type.value, Is.EqualTo(PhysicallyBasedSkyModel.EarthAdvanced));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void Source_UsesHdrpAlignedMenuAndEarthConstants()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSkyVolume.cs"));

            Assert.That(source, Does.Contain("[VolumeComponentMenu(\"Sky/Physically Based Sky\")]"));
            Assert.That(source, Does.Contain("public EnumParameter<PhysicallyBasedSkyModel> type = new(PhysicallyBasedSkyModel.EarthAdvanced);"));
            Assert.That(source, Does.Contain("private static readonly float DefaultAerosolMaximumAltitude = LayerDepthFromScaleHeight(DefaultAerosolScaleHeight);"));
            Assert.That(source, Does.Contain("private const float DefaultOzoneMinimumAltitude = 20.0f * 1000.0f;"));
            Assert.That(source, Does.Contain("private const float DefaultOzoneLayerWidth = 20.0f * 1000.0f;"));
            Assert.That(source, Does.Contain("public FloatParameter exposure = new(0.0f);"));
            Assert.That(source, Does.Contain("protected override void OnEnable()"));
            Assert.That(source, Does.Contain("private bool m_ExposureDefaultsMigrated;"));
            Assert.That(source, Does.Contain("return SkyIntensityUtility.GetExposureMultiplier(exposure.value);"));
            Assert.That(source, Does.Contain(": DefaultOzoneLayerWidth;"));
            Assert.That(source, Does.Contain(": DefaultOzoneMinimumAltitude;"));
        }

        [Test]
        public void SharedFields_UseHdrpStyleTooltips()
        {
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.type));
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.atmosphericScattering));
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.renderingMode));
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.material));
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.airDensityR));
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.aerosolDensity));
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.ozoneDensityDimmer));
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.groundTint));
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.horizonTint));
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.planetRadius));
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.exposure));
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.renderSunDisk));
            AssertFieldHasTooltip(nameof(PhysicallyBasedSkyVolume.enableHeightFog));
        }

        [Test]
        public void IsActive_ReturnsTrue_WhenAtmosphereContainsScattering()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                volume.airMaximumAltitude.value = 10000.0f;
                volume.airDensityR.value = 0.25f;

                Assert.That(volume.IsActive(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetAirExtinctionCoefficient_UsesCustomDensity_WhenTypeIsCustom()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                volume.type.value = PhysicallyBasedSkyModel.Custom;
                volume.airMaximumAltitude.value = 12000.0f;
                volume.airDensityR.value = 0.5f;
                volume.airDensityG.value = 0.25f;
                volume.airDensityB.value = 0.125f;

                var expectedScaleHeight = PhysicallyBasedSkyVolume.ScaleHeightFromLayerDepth(12000.0f);
                var extinction = volume.GetAirExtinctionCoefficient();

                Assert.That(extinction.x, Is.EqualTo(PhysicallyBasedSkyVolume.ExtinctionFromZenithOpacityAndScaleHeight(0.5f, expectedScaleHeight)).Within(1e-5f));
                Assert.That(extinction.y, Is.EqualTo(PhysicallyBasedSkyVolume.ExtinctionFromZenithOpacityAndScaleHeight(0.25f, expectedScaleHeight)).Within(1e-5f));
                Assert.That(extinction.z, Is.EqualTo(PhysicallyBasedSkyVolume.ExtinctionFromZenithOpacityAndScaleHeight(0.125f, expectedScaleHeight)).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetMaximumAltitude_UsesAdvancedAerosolAltitude_WhenModelIsEarthAdvanced()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                volume.type.value = PhysicallyBasedSkyModel.EarthAdvanced;
                volume.aerosolMaximumAltitude.value = 24000.0f;

                Assert.That(volume.GetMaximumAltitude(), Is.EqualTo(24000.0f));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetOzoneLayerHelpers_ReturnEarthDefaults_WhenModelIsNotCustom()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                volume.type.value = PhysicallyBasedSkyModel.EarthAdvanced;
                volume.ozoneMinimumAltitude.value = 5000.0f;
                volume.ozoneLayerWidth.value = 7000.0f;

                Assert.That(volume.GetOzoneLayerMinimumAltitude(), Is.EqualTo(20000.0f));
                Assert.That(volume.GetOzoneLayerWidth(), Is.EqualTo(20000.0f));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void IsHeightFogActive_ReturnsTrue_WhenEnabledWithPositiveDensityAndDistance()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                volume.enableHeightFog.value = true;
                volume.fogDensity.value = 0.05f;
                volume.fogMaxDistance.value = 1500.0f;

                Assert.That(volume.IsHeightFogActive(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetPrecomputationHashCode_DoesNotChange_WhenArtisticOverridesChange()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                var initialHash = volume.GetPrecomputationHashCode();
                volume.horizonTint.value = Color.red;
                volume.colorSaturation.value = 0.25f;
                volume.alphaMultiplier.value = 0.5f;
                volume.spaceRotation.value = new Vector3(15.0f, 30.0f, 45.0f);

                Assert.That(volume.GetPrecomputationHashCode(), Is.EqualTo(initialHash));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void ExposureField_IsVisible_BecauseConcreteSkyVolumesOwnExposureControls()
        {
            var field = typeof(PhysicallyBasedSkyVolume).GetField(nameof(PhysicallyBasedSkyVolume.exposure));

            Assert.That(field, Is.Not.Null);
            Assert.That(field.GetCustomAttribute<HideInInspector>(), Is.Null);
        }

        [Test]
        public void GetHashCode_Changes_WhenExposureChanges()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                var initialHash = volume.GetHashCode();
                volume.exposure.value = 4.0f;

                Assert.That(volume.GetHashCode(), Is.Not.EqualTo(initialHash));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetHashCode_Changes_WhenFogSettingsChange()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                var initialHash = volume.GetHashCode();
                volume.enableHeightFog.value = true;
                volume.fogDensity.value = 0.125f;

                Assert.That(volume.GetHashCode(), Is.Not.EqualTo(initialHash));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetHashCode_Changes_WhenArtisticOverridesChange()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                var initialHash = volume.GetHashCode();
                volume.horizonTint.value = new Color(0.5f, 0.75f, 1.0f);

                Assert.That(volume.GetHashCode(), Is.Not.EqualTo(initialHash));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }

        private static void AssertFieldHasTooltip(string fieldName)
        {
            var field = typeof(PhysicallyBasedSkyVolume).GetField(fieldName);

            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' to exist.");
            Assert.That(field!.GetCustomAttribute<TooltipAttribute>(), Is.Not.Null, $"Expected field '{fieldName}' to expose a Tooltip attribute.");
        }
    }
}
