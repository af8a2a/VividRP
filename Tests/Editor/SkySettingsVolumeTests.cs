using System.IO;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class SkySettingsVolumeTests
    {
        [Test]
        public void OnEnable_InitializesResolutionParameters_WithExpectedDefaults()
        {
            var volume = ScriptableObject.CreateInstance<SkySettingsVolume>();

            try
            {
                Assert.That(volume.generatedCubemapResolution, Is.Not.Null);
                Assert.That(volume.generatedCubemapResolution.value, Is.EqualTo(SkyGeneratedCubemapResolution.Resolution64));
                Assert.That(volume.specularPrefilterResolution, Is.Not.Null);
                Assert.That(volume.specularPrefilterResolution.value, Is.EqualTo(SkySpecularPrefilterResolution.Source));
                Assert.That(volume.specularPrefilterQuality, Is.Not.Null);
                Assert.That(volume.specularPrefilterQuality.value, Is.EqualTo(SkySpecularPrefilterQuality.PlatformDefault));
                Assert.That(SkySettingsVolume.GetGeneratedCubemapResolution(volume), Is.EqualTo(64));
                Assert.That(SkySettingsVolume.GetSpecularPrefilterResolution(volume), Is.EqualTo(0));
                Assert.That(SkySettingsVolume.GetSpecularPrefilterMaxSampleCount(volume), Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetGeneratedCubemapResolution_ClampsInvalidValues_ToMinimumSupportedSize()
        {
            var volume = ScriptableObject.CreateInstance<SkySettingsVolume>();

            try
            {
                volume.generatedCubemapResolution.value = (SkyGeneratedCubemapResolution)16;

                Assert.That(SkySettingsVolume.GetGeneratedCubemapResolution(volume), Is.EqualTo(32));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetResolutionHelpers_ReturnFallbackValues_WhenSettingsAreNull()
        {
            Assert.That(SkySettingsVolume.GetGeneratedCubemapResolution(), Is.EqualTo(64));
            Assert.That(SkySettingsVolume.GetSpecularPrefilterResolution(), Is.EqualTo(0));
            Assert.That(SkySettingsVolume.GetSpecularPrefilterMaxSampleCount(), Is.EqualTo(0));
        }

        [Test]
        public void Source_ReinitializesResolutionParameters_ForLegacyProfiles()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkySettingsVolume.cs"));

            Assert.That(source, Does.Contain("generatedCubemapResolution ??= new EnumParameter<SkyGeneratedCubemapResolution>(SkyGeneratedCubemapResolution.Resolution64);"));
            Assert.That(source, Does.Contain("specularPrefilterResolution ??= new EnumParameter<SkySpecularPrefilterResolution>(SkySpecularPrefilterResolution.Source);"));
            Assert.That(source, Does.Contain("specularPrefilterQuality ??= new EnumParameter<SkySpecularPrefilterQuality>(SkySpecularPrefilterQuality.PlatformDefault);"));
            Assert.That(source, Does.Contain("return Math.Max(32, resolution);"));
            Assert.That(source, Does.Contain("internal static int GetSpecularPrefilterMaxSampleCount(SkySettingsVolume settings = null)"));
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
