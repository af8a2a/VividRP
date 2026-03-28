using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class SIGMAShadowDenoisePassTests
    {
        [Test]
        public void SIGMAShadowDenoisePass_ClassifyStage_WritesTileTextureBeforeSmoothStage()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sigma", "SIGMAShadowDenoisePass.cs"));

            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ClassifyTiles, kernel, gOut_Tiles,   m_TileTexture.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_SmoothTiles, kernel, gIn_Tiles,  m_TileTexture.innerHandle);"));
        }

        [Test]
        public void SIGMAShadowDenoisePass_KeepsDebugTileTexture_ForFrameDebuggerInspection()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sigma", "SIGMAShadowDenoisePass.cs"));

            Assert.That(source, Does.Contain("RenderingUtils.ReAllocateHandleIfNeeded(ref debugTexture, desc, name: \"NRD-SIGMA TileTexture\");"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ClassifyTiles, kernel, gOut_Tiles,   debugTexture);"));
            Assert.That(source, Does.Contain("debugTexture?.Release();"));
        }

        [Test]
        public void SIGMAShadowDenoisePass_UsesPackedNormalRoughnessGBufferInput()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sigma", "SIGMAShadowDenoisePass.cs"));

            Assert.That(source, Does.Contain("RenderGraphTexture.CreateInput(\"GBuffer1\",      GraphicsFormat.A2B10G10R10_UNormPack32);"));
        }

        [Test]
        public void LegacyDirectionalRayTracedShadowTemporalDenoiseResource_IsRemoved()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "Utility", "PipelineResource", "VividResources.cs"));

            Assert.That(source, Does.Not.Contain("DirectionalRayTracedShadowDenoiseCompute"));
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
