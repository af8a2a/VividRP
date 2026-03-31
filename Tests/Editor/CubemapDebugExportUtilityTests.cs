using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class CubemapDebugExportUtilityTests
    {
        [Test]
        public void Source_ExportsSelectedCubemapFacesAndRestoresReadableState()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "PipelineResource", "CubemapDebugExportUtility.cs"));

            Assert.That(source, Does.Contain("Assets/VividRP/Export Selected Cubemap Faces"));
            Assert.That(source, Does.Contain("cubemap.GetPixels(face, mip)"));
            Assert.That(source, Does.Contain("Texture2D.EXRFlags.OutputAsFloat"));
            Assert.That(source, Does.Contain("summary.txt"));
            Assert.That(source, Does.Contain("currentImporter.isReadable = originalReadable;"));
            Assert.That(source, Does.Contain("EditorUtility.RevealInFinder(exportDirectory);"));
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
