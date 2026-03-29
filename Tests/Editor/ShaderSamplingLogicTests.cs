using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class ShaderSamplingLogicTests
    {
        [Test]
        public void StandardLitGBufferPass_UsesKeywordGuards_ForOptionalTextureSampling()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "StandardLitGBufferPass.hlsl"));

            Assert.That(source, Does.Contain("#if defined(_ALPHATEST_ON)"));
            Assert.That(source, Does.Contain("#if defined(_OPACITYMAP)"));
            Assert.That(source, Does.Contain("#if defined(_NORMALMAP)"));
            Assert.That(source, Does.Contain("#if defined(_METALLICSPECGLOSSMAP)"));
            Assert.That(source, Does.Contain("#if defined(_ROUGHNESSMAP)"));
            Assert.That(source, Does.Contain("defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)"));
            Assert.That(source, Does.Contain("#if defined(_OCCLUSIONMAP)"));
            Assert.That(source, Does.Contain("#if defined(_EMISSION)"));
            Assert.That(source, Does.Contain("#if defined(_CLEARCOAT)"));
            Assert.That(source, Does.Not.Contain("#if _NORMALMAP"));
        }

        [Test]
        public void SimpleDeferredLitPass_UsesLoadBasedSampling_ForGBufferAndDepth()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "SimpleDeferredLitPass.hlsl"));

            Assert.That(source, Does.Contain("uint2 pixelCoord = GetPixelCoord(input);"));
            Assert.That(source, Does.Contain("LOAD_TEXTURE2D_X(_GBuffer0, pixelCoord)"));
            Assert.That(source, Does.Contain("LOAD_TEXTURE2D_X(_GBuffer1, pixelCoord)"));
            Assert.That(source, Does.Contain("LOAD_TEXTURE2D_X(_GBuffer2, pixelCoord)"));
            Assert.That(source, Does.Contain("LOAD_TEXTURE2D_X(_GBuffer3, pixelCoord)"));
            Assert.That(source, Does.Contain("LOAD_TEXTURE2D_X(_GBuffer4, pixelCoord)"));
            Assert.That(source, Does.Contain("LOAD_TEXTURE2D_X(_DepthTexture, pixelCoord).r"));
            Assert.That(source, Does.Not.Contain("SAMPLE_TEXTURE2D_X(_GBuffer0"));
            Assert.That(source, Does.Not.Contain("SAMPLE_TEXTURE2D_X(_DepthTexture"));
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
