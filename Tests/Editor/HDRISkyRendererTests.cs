using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public class HDRISkyRendererTests
    {
        [Test]
        public void Update_PopulatesAmbientProbeBakeInputsForHDRI()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "HDRISkyRenderer.cs"));

            Assert.That(source, Does.Contain("public void Update(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd)"));
            Assert.That(source, Does.Contain("m_AmbientProbeBakingPass = m_Material.FindPass(\"HDRISkyBaking\");"));
            Assert.That(source, Does.Contain("SkySettingsVolume.GetGeneratedCubemapResolution(skySettings)"));
            Assert.That(source, Does.Contain("var generatedCubemapResolution = SkySettingsVolume.GetGeneratedCubemapResolution(VividVolumeManagerUtility.GetSkySettingsVolume());"));
            Assert.That(source, Does.Contain("NeedsAmbientProbeRebuild(skyHash, generatedCubemapResolution)"));
            Assert.That(source, Does.Contain("EnsureAmbientProbeCubemap(generatedCubemapResolution);"));
            Assert.That(source, Does.Contain("SkyCubemapBakingUtility.RenderSkyToCubemap("));
            Assert.That(source, Does.Contain("skyData.ambientProbeCubemap = useBakedAmbientProbe ? m_AmbientProbeCubemap : cubemap;"));
            Assert.That(source, Does.Contain("skyData.ambientProbeTint = useBakedAmbientProbe ? Color.white : skyData.tint;"));
            Assert.That(source, Does.Contain("skyData.ambientProbeExposure = useBakedAmbientProbe ? 0.0f : skyData.exposure;"));
            Assert.That(source, Does.Contain("skyData.ambientProbeRotation = useBakedAmbientProbe ? 0.0f : skyData.rotation;"));
            Assert.That(source, Does.Contain("skyData.ambientProbeHash = skyHash;"));
            Assert.That(source, Does.Not.Contain("m_AmbientProbeConvolution.RequestUpdate("));
            Assert.That(source, Does.Not.Contain("TryProjectCubemapToSH("));
        }

        [Test]
        public void SkyManager_DelegatesHdriConvolutionAndKeepsGpuOnlyFallbacks()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyManager.cs"));

            Assert.That(source, Does.Contain("RegisterRenderer(new HDRISkyRenderer(), resources);"));
            Assert.That(source, Does.Contain("renderer.Update(context, s_CachedSkyData, cmd);"));
            Assert.That(source, Does.Contain("if (skyData != null && skyData.ambientProbeCubemap != null)"));
            Assert.That(source, Does.Contain("skyData.ambientProbeCubemap,"));
            Assert.That(source, Does.Contain("skyData.ambientProbeTint,"));
            Assert.That(source, Does.Contain("skyData.ambientProbeExposure,"));
            Assert.That(source, Does.Contain("skyData.ambientProbeRotation,"));
            Assert.That(source, Does.Contain("skyData.ambientProbeHash);"));
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
