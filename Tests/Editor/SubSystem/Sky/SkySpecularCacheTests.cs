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
            Assert.That(
                source,
                Does.Contain(
                    "if (TryConvolveCubemap(cmd, source, targetResolution))"));
            Assert.That(source, Does.Contain("m_ConvolvedCubemap = new RenderTexture(targetResolution, targetResolution, 0)"));
            Assert.That(source, Does.Not.Contain("cmd.GenerateMips"));
        }

        [Test]
        public void SkyTextureContentHash_IsStableAndTracksTextureShape()
        {
            var first = new UnityEngine.Cubemap(
                4,
                UnityEngine.TextureFormat.RGBAHalf,
                true);
            var second = new UnityEngine.Cubemap(
                8,
                UnityEngine.TextureFormat.RGBAHalf,
                true);

            try
            {
                var firstHash = VividRP.Runtime.SkyManager.GetSkyTextureContentHash(first);
                var unchangedHash =
                    VividRP.Runtime.SkyManager.GetSkyTextureContentHash(first);
                var secondHash =
                    VividRP.Runtime.SkyManager.GetSkyTextureContentHash(second);

                Assert.That(unchangedHash, Is.EqualTo(firstHash));
                Assert.That(secondHash, Is.Not.EqualTo(firstHash));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(first);
            }
        }

        [Test]
        public void SkyCubemapGGXConvolution_PortsHdrpStyleCubemapFilteringLoop()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyCubemapGGXConvolution.cs"));

            Assert.That(source, Does.Contain("internal const string GGXConvolutionShaderName = \"Hidden/VividRP/Sky/GGXConvolve\";"));
            Assert.That(source, Does.Contain("SkyCubemapGGXConvolution.RenderCubemapGGXConvolution"));
            Assert.That(source, Does.Contain("RenderCubemapLevel(cmd, target, 0, CopyMipZeroPassIndex);"));
            Assert.That(source, Does.Contain("RenderCubemapLevel(cmd, target, mipLevel, GgxConvolutionPassIndex);"));
            Assert.That(source, Does.Contain("CoreUtils.DrawFullScreen(cmd, m_ConvolutionMaterial, m_PropertyBlock, passIndex);"));
            Assert.That(source, Does.Not.Contain("cmd.CopyTexture(source, faceIndex, 0, target, faceIndex, 0);"));
            Assert.That(source, Does.Not.Contain("TryCopyCubemapMipZero"));
            Assert.That(source, Does.Contain("GetIBLRuntimeFilterSampleCount"));
            Assert.That(source, Does.Contain("BuildGgxIblSampleDataTexture()"));
            Assert.That(source, Does.Contain("SkyShaderCompilationUtility.EnsureMaterialPassReady("));
            Assert.That(source, Does.Contain("GgxConvolutionPassIndex)"));
            Assert.That(source, Does.Contain("CopyMipZeroPassIndex);"));
        }

        [Test]
        public void SkyCubemapGGXConvolution_UsesTargetMipDimensionsForFaceProjection()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyCubemapGGXConvolution.cs"));

            Assert.That(source, Does.Contain("GetConvolutionMipLevel(target)"));
            Assert.That(source, Does.Contain("var faceSize = Mathf.Max(1, target.width >> mipLevel);"));
            Assert.That(source, Does.Not.Contain("var faceSize = Mathf.Max(1, source.width >> mipLevel);"));
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
