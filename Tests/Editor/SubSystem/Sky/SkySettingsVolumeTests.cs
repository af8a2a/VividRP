using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class SkySettingsVolumeTests
    {
        [Test]
        public void GetGeneratedCubemapHelpers_ReturnExpectedPresetValues()
        {
            var volume = ScriptableObject.CreateInstance<SkySettingsVolume>();

            try
            {
                volume.generatedCubemapQuality.value = SkyGeneratedCubemapQuality.Low;
                Assert.That(SkySettingsVolume.GetGeneratedCubemapViewSampleCount(volume), Is.EqualTo(8));
                Assert.That(SkySettingsVolume.GetGeneratedCubemapLightSampleCount(volume), Is.EqualTo(4));
                Assert.That(SkySettingsVolume.GetGeneratedCubemapResolution(volume), Is.EqualTo(16));
                Assert.That(SkySettingsVolume.GetSkyTextureResolution(volume), Is.EqualTo(1024));
                Assert.That(SkySettingsVolume.GetSkyReflectionResolution(volume), Is.EqualTo(128));

                volume.generatedCubemapQuality.value = SkyGeneratedCubemapQuality.High;
                Assert.That(SkySettingsVolume.GetGeneratedCubemapViewSampleCount(volume), Is.EqualTo(16));
                Assert.That(SkySettingsVolume.GetGeneratedCubemapLightSampleCount(volume), Is.EqualTo(8));
                Assert.That(SkySettingsVolume.GetGeneratedCubemapResolution(volume), Is.EqualTo(64));
                Assert.That(SkySettingsVolume.GetSkyTextureResolution(volume), Is.EqualTo(1024));
                Assert.That(SkySettingsVolume.GetSkyReflectionResolution(volume), Is.EqualTo(512));

                volume.generatedCubemapQuality.value = SkyGeneratedCubemapQuality.Ultra;
                Assert.That(SkySettingsVolume.GetGeneratedCubemapViewSampleCount(volume), Is.EqualTo(24));
                Assert.That(SkySettingsVolume.GetGeneratedCubemapLightSampleCount(volume), Is.EqualTo(12));
                Assert.That(SkySettingsVolume.GetGeneratedCubemapResolution(volume), Is.EqualTo(128));
                Assert.That(SkySettingsVolume.GetSkyTextureResolution(volume), Is.EqualTo(1024));
                Assert.That(SkySettingsVolume.GetSkyReflectionResolution(volume), Is.EqualTo(1024));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetGeneratedCubemapHelpers_ReturnFallbackValues_WhenSettingsAreNull()
        {
            Assert.That(SkySettingsVolume.GetGeneratedCubemapViewSampleCount(), Is.EqualTo(12));
            Assert.That(SkySettingsVolume.GetGeneratedCubemapLightSampleCount(), Is.EqualTo(6));
            Assert.That(SkySettingsVolume.GetGeneratedCubemapResolution(), Is.EqualTo(32));
            Assert.That(SkySettingsVolume.GetSkyTextureResolution(), Is.EqualTo(1024));
            Assert.That(SkySettingsVolume.GetSkyReflectionResolution(), Is.EqualTo(256));
            Assert.That(SkySettingsVolume.GetIncludeSunInBaking(), Is.False);
        }

        [Test]
        public void GetIncludeSunInBaking_ReturnsConfiguredValue()
        {
            var volume = ScriptableObject.CreateInstance<SkySettingsVolume>();

            try
            {
                Assert.That(SkySettingsVolume.GetIncludeSunInBaking(volume), Is.False);

                volume.includeSunInBaking.value = true;

                Assert.That(SkySettingsVolume.GetIncludeSunInBaking(volume), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void SkyIntensityUtility_UsesHdrpAlignedIntensityModes()
        {
            Assert.That(
                SkyIntensityUtility.GetIntensityFromSettings(SkyIntensityMode.Exposure, 2.0f, 3.0f, 10.0f, 500.0f),
                Is.EqualTo(ColorUtils.ConvertEV100ToExposure(-2.0f)).Within(1e-6f));
            Assert.That(
                SkyIntensityUtility.GetIntensityFromSettings(SkyIntensityMode.Multiplier, 2.0f, 3.0f, 10.0f, 500.0f),
                Is.EqualTo(3.0f));
            Assert.That(
                SkyIntensityUtility.GetIntensityFromSettings(SkyIntensityMode.Lux, 2.0f, 3.0f, 10.0f, 500.0f),
                Is.EqualTo(50.0f));
        }

        [Test]
        public void SkyPlanet_UsesAutomaticWorldCenter_WhenRenderingInWorldSpace()
        {
            var skyVolume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();
            var settings = ScriptableObject.CreateInstance<SkySettingsVolume>();

            try
            {
                skyVolume.planetRadius.value = 1000.0f;
                settings.renderingSpace.value = RenderingSpace.World;
                settings.centerMode.value = PlanetMode.Automatic;

                var planet = SkyPlanet.Resolve(skyVolume, settings, new Vector3(10.0f, 20.0f, 30.0f));

                Assert.That(planet.radius, Is.EqualTo(1000.0f));
                Assert.That(planet.renderingSpace, Is.EqualTo(RenderingSpace.World));
                Assert.That(planet.center, Is.EqualTo(new Vector3(0.0f, -1000.0f, 0.0f)));
                Assert.That(planet.GetPlanetCenterRadius(), Is.EqualTo(new Vector4(0.0f, -1000.0f, 0.0f, 1000.0f)));
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(skyVolume);
            }
        }

        [Test]
        public void SkyPlanet_AnchorsCenterToCamera_WhenRenderingInCameraSpace()
        {
            var skyVolume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();
            var settings = ScriptableObject.CreateInstance<SkySettingsVolume>();

            try
            {
                skyVolume.planetRadius.value = 1000.0f;
                settings.renderingSpace.value = RenderingSpace.Camera;

                var cameraPosition = new Vector3(10.0f, 20.0f, 30.0f);
                var planet = SkyPlanet.Resolve(skyVolume, settings, cameraPosition);
                var planetUpAltitude = planet.GetPlanetUpAltitude(cameraPosition);

                Assert.That(planet.center, Is.EqualTo(new Vector3(10.0f, -980.0f, 30.0f)));
                Assert.That(planetUpAltitude, Is.EqualTo(new Vector4(0.0f, 1.0f, 0.0f, 0.0f)));
                Assert.That(planet.GetCameraPositionPS(cameraPosition), Is.EqualTo(new Vector3(0.0f, 1001.0f, 0.0f)));
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(skyVolume);
            }
        }

        [Test]
        public void SkyPlanet_UsesManualPlanetCenter_WhenConfigured()
        {
            var skyVolume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();
            var settings = ScriptableObject.CreateInstance<SkySettingsVolume>();

            try
            {
                skyVolume.planetRadius.value = 1000.0f;
                settings.renderingSpace.value = RenderingSpace.World;
                settings.centerMode.value = PlanetMode.Manual;
                settings.planetCenter.value = new Vector3(100.0f, -900.0f, 50.0f);

                var cameraPosition = new Vector3(100.0f, 120.0f, 50.0f);
                var planet = SkyPlanet.Resolve(skyVolume, settings, cameraPosition);

                Assert.That(planet.center, Is.EqualTo(new Vector3(100.0f, -900.0f, 50.0f)));
                Assert.That(planet.GetCameraPositionPS(cameraPosition), Is.EqualTo(new Vector3(0.0f, 1020.0f, 0.0f)));
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(skyVolume);
            }
        }
    }
}
