using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public class SkyDiffuseSHUtilityTests
    {
        [Test]
        public void SkyDiffuseSHUtility_RemovesCpuProjectionEntryPoint()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyDiffuseSHUtility.cs"));

            Assert.That(source, Does.Not.Contain("TryProjectCubemapToSH("));
            Assert.That(source, Does.Contain("internal static readonly CubemapFace[] ValidCubemapFaces"));
            Assert.That(source, Does.Contain("internal static Vector3 GetDirectionForCubemapFace"));
        }

        [Test]
        public void SkyDiffuseSHUtility_KeepsCubemapFaceDirectionHelpers()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyDiffuseSHUtility.cs"));

            Assert.That(source, Does.Contain("CubemapFace.PositiveX => new Vector3(1.0f, -v, -u)"));
            Assert.That(source, Does.Contain("return direction.normalized;"));
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
