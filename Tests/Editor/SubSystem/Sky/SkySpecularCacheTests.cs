using System.IO;
using NUnit.Framework;

namespace VividRP.Editor.Tests
{
    public class SkySpecularCacheTests
    {
        [Test]
        public void SkySpecularCache_UsesManualGgxConvolutionInsteadOfGenerateMips()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkySpecularCache.cs"));

            Assert.That(source, Does.Contain("private readonly SkyCubemapGGXConvolution m_GgxConvolution = new();"));
            Assert.That(source, Does.Contain("if (TryConvolveCubemap(cmd, source))"));
            Assert.That(source, Does.Contain("m_ConvolvedCubemap = new RenderTexture(source.width, source.height, 0)"));
            Assert.That(source, Does.Not.Contain("cmd.GenerateMips"));
        }

        [Test]
        public void SkyCubemapGGXConvolution_PortsHdrpStyleCubemapFilteringLoop()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyCubemapGGXConvolution.cs"));

            Assert.That(source, Does.Contain("internal const string GGXConvolutionShaderName = \"Hidden/VividRP/Sky/GGXConvolve\";"));
            Assert.That(source, Does.Contain("SkyCubemapGGXConvolution.RenderCubemapGGXConvolution"));
            Assert.That(source, Does.Contain("RenderCubemapLevel(cmd, source, target, 0, CopyMipZeroPassIndex);"));
            Assert.That(source, Does.Contain("RenderCubemapLevel(cmd, source, target, mipLevel, GgxConvolutionPassIndex);"));
            Assert.That(source, Does.Contain("CoreUtils.DrawFullScreen(cmd, m_ConvolutionMaterial, m_PropertyBlock, passIndex);"));
            Assert.That(source, Does.Not.Contain("cmd.CopyTexture(source, faceIndex, 0, target, faceIndex, 0);"));
            Assert.That(source, Does.Not.Contain("TryCopyCubemapMipZero"));
            Assert.That(source, Does.Contain("GetIBLRuntimeFilterSampleCount"));
            Assert.That(source, Does.Contain("BuildGgxIblSampleDataTexture()"));
        }

        [Test]
        public void GGXConvolveShader_UsesIntegrateLdWithPrecomputedSamples()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "GGXConvolve.shader"));

            Assert.That(source, Does.Contain("Shader \"Hidden/VividRP/Sky/GGXConvolve\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl\""));
            Assert.That(source, Does.Contain("TEXTURE2D(_GgxIblSamples);"));
            Assert.That(source, Does.Contain("GetIBLRuntimeFilterSampleCount((uint)_Level)"));
            Assert.That(source, Does.Contain("IntegrateLD("));
            Assert.That(source, Does.Not.Contain("USE_MIS"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp"),
                Path.Combine(projectRoot, "Packages", "Custom_URP")
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
