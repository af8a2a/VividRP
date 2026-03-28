using System.IO;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core.Sigma;

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
        public void ResolveSettings_UsesSampleDefaults_WhenVolumeHasNoOverrides()
        {
            var settings = SIGMAShadowDenoisePass.ResolveSettings(null);

            Assert.That(settings.DenoisingRange, Is.EqualTo(RayTracingSettingsVolume.DefaultSigmaDenoisingRange));
            Assert.That(
                settings.PlaneDistanceSensitivity,
                Is.EqualTo(RayTracingSettingsVolume.DefaultSigmaPlaneDistanceSensitivity));
            Assert.That(
                settings.MaxStabilizedFrameNum,
                Is.EqualTo((uint)RayTracingSettingsVolume.DefaultSigmaMaxStabilizedFrameNum));
        }

        [Test]
        public void ResolveSettings_UsesVolumeOverrides_WhenOverrideStateEnabled()
        {
            var volume = ScriptableObject.CreateInstance<RayTracingSettingsVolume>();

            try
            {
                volume.active = true;
                volume.sigmaDenoisingRange.overrideState = true;
                volume.sigmaDenoisingRange.value = 2048.0f;
                volume.sigmaPlaneDistanceSensitivity.overrideState = true;
                volume.sigmaPlaneDistanceSensitivity.value = 0.15f;
                volume.sigmaMaxStabilizedFrameNum.overrideState = true;
                volume.sigmaMaxStabilizedFrameNum.value = 3;

                var settings = SIGMAShadowDenoisePass.ResolveSettings(volume);

                Assert.That(settings.DenoisingRange, Is.EqualTo(2048.0f));
                Assert.That(settings.PlaneDistanceSensitivity, Is.EqualTo(0.15f));
                Assert.That(settings.MaxStabilizedFrameNum, Is.EqualTo(3u));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void SIGMAShadowDenoisePass_ReadsSigmaSettingsFromVolume()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sigma", "SIGMAShadowDenoisePass.cs"));

            Assert.That(source, Does.Contain("var sigmaSettings = ResolveSettings(VividVolumeManagerUtility.GetRayTracingSettingsVolume());"));
            Assert.That(source, Does.Contain("m_MaxStabilizedFrameNum = sigmaSettings.MaxStabilizedFrameNum;"));
        }

        [Test]
        public void SIGMAShadowDenoisePass_RunsTemporalBootstrap_WhenSigmaHistoryIsConfigured()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sigma", "SIGMAShadowDenoisePass.cs"));

            Assert.That(source, Does.Contain("bool useTemporalStabilization = m_MaxStabilizedFrameNum > 0;"));
            Assert.That(source, Does.Not.Contain("bool useTemporalStabilization = m_HasValidHistory && m_MaxStabilizedFrameNum > 0;"));
        }

        [Test]
        public void SIGMAShadowDenoisePass_ClearsTransientHistoryTextures_ForTemporalBootstrap()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sigma", "SIGMAShadowDenoisePass.cs"));

            Assert.That(
                source,
                Does.Contain("CreatePassOwnedTexture(\"SIGMA_TransientHistory\",       1, 1, GraphicsFormat.R8_UNorm, clearBuffer: true);"));
            Assert.That(
                source,
                Does.Contain("CreatePassOwnedTexture(\"SIGMA_TransientHistoryLength\", 1, 1, GraphicsFormat.R32_UInt, clearBuffer: true);"));
        }

        [Test]
        public void SIGMAShadowDenoisePass_DoesNotKeepLegacyDebugTileTexture()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sigma", "SIGMAShadowDenoisePass.cs"));

            Assert.That(source, Does.Not.Contain("debugTexture"));
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
