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
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "HDRI", "HDRISkyRenderer.cs"));

            Assert.That(source, Does.Contain("public void Update(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd, int skyHash, bool forceRebuild)"));
            Assert.That(source, Does.Contain("m_AmbientProbeBakingPass = m_Material.FindPass(\"HDRISkyBaking\");"));
            Assert.That(source, Does.Contain("HDRISkyRenderer.RebuildAmbientProbe (MissingTexture)"));
            Assert.That(source, Does.Contain("EnsureAmbientProbeCubemap(generatedCubemapResolution);"));
            Assert.That(source, Does.Contain("SkyCubemapBakingUtility.RenderSkyToCubemap("));
            Assert.That(source, Does.Contain("skyData.ambientProbeCubemap = useBakedAmbientProbe ? m_AmbientProbeCubemap : cubemap;"));
            Assert.That(source, Does.Contain("skyData.ambientProbeHash = skyHash;"));
        }

        [Test]
        public void Renderer_ExposesRendererDrivenSkyInjectionHooks()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "HDRI", "HDRISkyRenderer.cs"));
            var interfaceSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "ISkyRenderer.cs"));

            Assert.That(interfaceSource, Does.Contain("void PrepareSkyRendering("));
            Assert.That(interfaceSource, Does.Contain("void RenderSky(CommandBuffer cmd);"));
            Assert.That(source, Does.Contain("public void PrepareSkyRendering("));
            Assert.That(source, Does.Contain("public void RenderSky(CommandBuffer cmd)"));
            Assert.That(source, Does.Contain("cmd.SetRenderTarget(m_ColorTarget, m_DepthTexture);"));
            Assert.That(source, Does.Contain("properties.SetMatrix(PixelCoordToViewDirWSId, m_PixelCoordToViewDirMatrix);"));
            Assert.That(source, Does.Contain("CoreUtils.DrawFullScreen(cmd, m_Material, properties, 0);"));
            Assert.That(source, Does.Contain("private static void GetSkyParameters(float exposure, float rotation, out float intensity, out float phi)"));
            Assert.That(source, Does.Not.Contain("HDRISkyPass.GetParameters("));
        }

        [Test]
        public void SkyManager_DelegatesHdriConvolutionAndSkyInjection()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyManager.cs"));

            Assert.That(source, Does.Contain("RegisterRenderer(new HDRISkyRenderer(), resources);"));
            Assert.That(source, Does.Contain("renderer.Update(context, s_CachedSkyData, cmd, skyHash, forceRebuild);"));
            Assert.That(source, Does.Contain("internal static bool PrepareSkyInjection("));
            Assert.That(source, Does.Contain("s_ActiveRenderer.PrepareSkyRendering("));
            Assert.That(source, Does.Contain("internal static void RenderSkyInjection(CommandBuffer cmd)"));
            Assert.That(source, Does.Contain("s_PendingSkyRenderer.RenderSky(cmd);"));
            Assert.That(source, Does.Contain("var useDefaultAmbientProbe = skyData == null || skyData.ambientProbeCubemap == null;"));
            Assert.That(source, Does.Contain("s_AmbientProbeConvolution.BindGlobalBuffer(cmd, useDefaultAmbientProbe);"));
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
