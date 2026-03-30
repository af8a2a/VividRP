using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public class HDRISkyRendererTests
    {
        [Test]
        public void Update_RequestsGpuAmbientProbeConvolutionForHDRI()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "HDRISkyRenderer.cs"));

            Assert.That(source, Does.Contain("public void Update(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd)"));
            Assert.That(source, Does.Contain("m_AmbientProbeConvolution.RequestUpdate("));
            Assert.That(source, Does.Contain("skyData.hasDiffuseSH = false;"));
            Assert.That(source, Does.Contain("skyData.diffuseSH = default;"));
            Assert.That(source, Does.Not.Contain("TryProjectCubemapToSH("));
        }

        [Test]
        public void SkyManager_DelegatesHdriConvolutionAndKeepsGpuOnlyFallbacks()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyManager.cs"));

            Assert.That(source, Does.Contain("RegisterRenderer(new HDRISkyRenderer(s_AmbientProbeConvolution), resources);"));
            Assert.That(source, Does.Contain("renderer.Update(context, s_CachedSkyData, cmd);"));
            Assert.That(source, Does.Contain("skyData.activeSkyType != SkyType.HDRI && s_AmbientProbeConvolution.IsSupported"));
            Assert.That(source, Does.Contain("s_AmbientProbeConvolution.RequestUpdate("));
            Assert.That(source, Does.Contain("s_AmbientProbeConvolution.BindGlobalBuffer(cmd, true);"));
            Assert.That(source, Does.Not.Contain("SkyDiffuseSHUtility.TryProjectCubemapToSH("));
            Assert.That(source, Does.Not.Contain("UploadProbe("));
            Assert.That(source, Does.Not.Contain("UploadRenderSettingsAmbientProbe("));
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
