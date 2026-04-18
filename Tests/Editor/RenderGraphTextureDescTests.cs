using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class RenderGraphTextureDescTests
    {
        [Test]
        public void RenderGraphTextureDesc_PreservesShadowMapFlag_WhenConvertingToAndFromTextureDesc()
        {
            var source = File.ReadAllText(GetSourcePath());

            Assert.That(source, Does.Contain("public bool IsShadowMap = false;"));
            Assert.That(source, Does.Contain("isShadowMap = IsShadowMap,"));
            Assert.That(source, Does.Contain("IsShadowMap = desc.isShadowMap,"));
        }

        private static string GetSourcePath()
        {
            var sourcePath = GetPackageFilePath("Runtime", "RenderGraph", "Resource", "RenderGraphTexture.cs");

            Assert.That(File.Exists(sourcePath), Is.True, $"Expected source at '{sourcePath}'.");
            return sourcePath;
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
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
