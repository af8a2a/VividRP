using System.Reflection;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
            Assert.That(source, Does.Contain("m_Constants = SigmaSharedConstants.Compute("));
            Assert.That(source, Does.Contain("float stabilizationStrength = m_HasValidHistory ? sigmaSettings.StabilizationStrength : 0.0f;"));
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
        public void ResizePassOwned_PreservesExistingClearBufferState()
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R8_UNorm)
            };
            texture.desc.ClearBuffer = true;

            var resizeMethod = typeof(SIGMAShadowDenoisePass).GetMethod(
                "ResizePassOwned",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(resizeMethod, Is.Not.Null);

            resizeMethod.Invoke(null, new object[] { texture, 64, 32 });

            Assert.That(texture.desc.Width, Is.EqualTo(64));
            Assert.That(texture.desc.Height, Is.EqualTo(32));
            Assert.That(texture.desc.ClearBuffer, Is.True);
        }

        [Test]
        public void Constructor_ConfiguresDeterministicClears_ForShadowOutputs()
        {
            var pass = new SIGMAShadowDenoisePass();

            var denoisedShadowTexture = GetTextureField(pass, "m_DenoisedShadowTexture");
            var transientPenumbra = GetTextureField(pass, "m_TransientPenumbra");
            var transientShadow = GetTextureField(pass, "m_TransientShadow");
            var transientHistoryLength = GetTextureField(pass, "m_TransientHistoryLength");

            Assert.That(denoisedShadowTexture.desc.ClearBuffer, Is.True);
            Assert.That(denoisedShadowTexture.desc.ClearColor, Is.EqualTo(Color.white));
            Assert.That(transientPenumbra.desc.ClearBuffer, Is.True);
            Assert.That(transientPenumbra.desc.ClearColor, Is.EqualTo(Color.clear));
            Assert.That(transientShadow.desc.ClearBuffer, Is.True);
            Assert.That(transientShadow.desc.ClearColor, Is.EqualTo(Color.white));
            Assert.That(transientHistoryLength.desc.ClearBuffer, Is.True);
            Assert.That(transientHistoryLength.desc.ClearColor, Is.EqualTo(Color.clear));
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
        public void SIGMAShadowDenoisePass_ClearsCurrentHistoryTargets_BeforeTemporalStabilization()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sigma", "SIGMAShadowDenoisePass.cs"));

            Assert.That(source, Does.Contain("ClearTexture(cmd, m_HistoryShadowCurrent, Color.white);"));
            Assert.That(source, Does.Contain("ClearTexture(cmd, m_HistoryLengthCurrent, Color.clear);"));
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

        private static RenderGraphTexture GetTextureField(SIGMAShadowDenoisePass pass, string fieldName)
        {
            var field = typeof(SIGMAShadowDenoisePass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture)field.GetValue(pass);
        }
    }
}
