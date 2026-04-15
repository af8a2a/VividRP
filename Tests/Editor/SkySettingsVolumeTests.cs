using System.IO;
using NUnit.Framework;
using UnityEngine;
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

                volume.generatedCubemapQuality.value = SkyGeneratedCubemapQuality.High;
                Assert.That(SkySettingsVolume.GetGeneratedCubemapViewSampleCount(volume), Is.EqualTo(16));
                Assert.That(SkySettingsVolume.GetGeneratedCubemapLightSampleCount(volume), Is.EqualTo(8));
                Assert.That(SkySettingsVolume.GetGeneratedCubemapResolution(volume), Is.EqualTo(64));

                volume.generatedCubemapQuality.value = SkyGeneratedCubemapQuality.Ultra;
                Assert.That(SkySettingsVolume.GetGeneratedCubemapViewSampleCount(volume), Is.EqualTo(24));
                Assert.That(SkySettingsVolume.GetGeneratedCubemapLightSampleCount(volume), Is.EqualTo(12));
                Assert.That(SkySettingsVolume.GetGeneratedCubemapResolution(volume), Is.EqualTo(128));
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
        public void Source_ReinitializesPlanetAndQualityParameters_ForLegacyProfiles()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkySettingsVolume.cs"));

            Assert.That(source, Does.Contain("includeSunInBaking ??= new BoolParameter(false);"));
            Assert.That(source, Does.Contain("generatedCubemapQuality ??= new EnumParameter<SkyGeneratedCubemapQuality>(SkyGeneratedCubemapQuality.PlatformDefault);"));
            Assert.That(source, Does.Contain("renderingSpace ??= new EnumParameter<RenderingSpace>(RenderingSpace.World);"));
            Assert.That(source, Does.Contain("centerMode ??= new EnumParameter<PlanetMode>(PlanetMode.Automatic);"));
            Assert.That(source, Does.Contain("planetCenter ??= new Vector3Parameter(new Vector3(0.0f, -DefaultEarthRadius, 0.0f));"));
            Assert.That(source, Does.Contain("internal static bool GetIncludeSunInBaking(SkySettingsVolume settings = null)"));
            Assert.That(source, Does.Contain("internal readonly struct SkyPlanet"));
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
    }
}
