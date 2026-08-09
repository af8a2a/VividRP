using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyHdrpPortTests
    {

        [Test]
        public void RetiredHdrpFullscreenRenderChain_KeepsOnlyExpectedLegacyFilesRemoved()
        {
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyRendering.hlsl")), Is.True);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphericScattering", "AtmosphericScatteringSky.hlsl")), Is.False);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "GroundIrradiancePrecomputation.compute")), Is.True);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "InScatteredRadiancePrecomputation.compute")), Is.True);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "LightDefinition.cs.hlsl")), Is.False);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "CookieSampling.hlsl")), Is.False);
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
