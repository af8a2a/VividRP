using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class HdrpDeferredLitLightingTests
    {
        [Test]
        public void HdrpLitLightingInclude_ContainsHdrpInspiredDirectLightingBuildingBlocks()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "HdrpLitLighting.hlsl"));

            Assert.That(source, Does.Contain("VividDisneyDiffuse"));
            Assert.That(source, Does.Contain("VividDV_SmithJointGGX"));
            Assert.That(source, Does.Contain("VividD_Charlie"));
            Assert.That(source, Does.Contain("BuildVividHdrpLitBSDFData"));
            Assert.That(source, Does.Contain("EvaluateVividHdrpLitDirectLight"));
            Assert.That(source, Does.Contain("EvaluateDirectionalLight"));
            Assert.That(source, Does.Contain("EvaluatePunctualLight"));
        }

        [Test]
        public void DeferredLightingPasses_UseSharedHdrpLitLightingInclude()
        {
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "SimpleDeferredLitPass.hlsl")),
                Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/HdrpLitLighting.hlsl\""));

            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirectPass.hlsl")),
                Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/HdrpLitLighting.hlsl\""));

            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLighting.compute")),
                Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/HdrpLitLighting.hlsl\""));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var fullPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                Path.Combine(relativeParts)));

            Assert.That(File.Exists(fullPath), Is.True, $"Expected source file at '{fullPath}'.");
            return fullPath;
        }
    }
}
