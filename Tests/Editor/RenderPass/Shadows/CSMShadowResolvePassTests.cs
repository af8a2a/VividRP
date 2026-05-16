using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class CSMShadowResolvePassTests
    {
        [Test]
        public void CSMShadowResolvePass_UploadsPerLightScreenSpaceShadowQuality()
        {
            var source = File.ReadAllText(GetPassSourcePath());

            Assert.That(source, Does.Contain("private static readonly int CSMShadowQualityId = Shader.PropertyToID(\"_CSMShadowQuality\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMLightAngularDiameterId = Shader.PropertyToID(\"_CSMLightAngularDiameter\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMFrameIndexId = Shader.PropertyToID(\"_CSMFrameIndex\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMPCSSBlockerSampleCountId = Shader.PropertyToID(\"_CSMPCSSBlockerSampleCount\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMPCSSFilterSampleCountId = Shader.PropertyToID(\"_CSMPCSSFilterSampleCount\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMPCSSMaxPenumbraSizeId = Shader.PropertyToID(\"_CSMPCSSMaxPenumbraSize\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMCascadeBordersId = Shader.PropertyToID(\"_CSMCascadeBorders\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMCascadeWorldTexelSizesId = Shader.PropertyToID(\"_CSMCascadeWorldTexelSizes\");"));
            Assert.That(source, Does.Not.Contain("private static readonly int CSMDepthBiasId = Shader.PropertyToID(\"_CSMDepthBias\");"));
            Assert.That(source, Does.Contain("m_ShadowQuality = (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low;"));
            Assert.That(source, Does.Contain("CoreUtils.DivRoundUp(width, ThreadGroupSizeX);"));
            Assert.That(source, Does.Contain("TryResolveMainDirectionalLight(lightData, out _, out var additionalLightData)"));
            Assert.That(source, Does.Contain("m_ShadowQuality = (int)additionalLightData.screenSpaceShadowQuality;"));
            Assert.That(source, Does.Contain("m_LightAngularDiameter = Mathf.Max(additionalLightData.resolvedAngularDiameter, 0.0f);"));
            Assert.That(source, Does.Contain("m_PCSSBlockerSampleCount = additionalLightData.dirLightPCSSBlockerSampleCount;"));
            Assert.That(source, Does.Contain("m_PCSSFilterSampleCount = additionalLightData.dirLightPCSSFilterSampleCount;"));
            Assert.That(source, Does.Contain("m_PCSSMaxPenumbraSize = additionalLightData.dirLightPCSSMaxPenumbraSize;"));
            Assert.That(source, Does.Contain("m_CascadeWorldTexelSizes[i] = shadowData.cascadeWorldTexelSizes[i];"));
            Assert.That(source, Does.Contain("m_CascadeBorders[i] = shadowData.cascadeBorders[i];"));
            Assert.That(source, Does.Contain("m_FrameIndex = Time.frameCount;"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ResolveCompute, CSMShadowQualityId, m_ShadowQuality);"));
            Assert.That(source, Does.Contain("cmd.SetComputeFloatParam(m_ResolveCompute, CSMLightAngularDiameterId, m_LightAngularDiameter);"));
            Assert.That(source, Does.Contain("cmd.SetComputeVectorParam(m_ResolveCompute, CSMCascadeWorldTexelSizesId, m_CascadeWorldTexelSizes);"));
            Assert.That(source, Does.Contain("cmd.SetComputeVectorParam(m_ResolveCompute, CSMCascadeBordersId, m_CascadeBorders);"));
            Assert.That(source, Does.Not.Contain("cmd.SetComputeFloatParam(m_ResolveCompute, CSMDepthBiasId, m_DepthBias);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ResolveCompute, CSMFrameIndexId, m_FrameIndex);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ResolveCompute, CSMPCSSBlockerSampleCountId, m_PCSSBlockerSampleCount);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ResolveCompute, CSMPCSSFilterSampleCountId, m_PCSSFilterSampleCount);"));
            Assert.That(source, Does.Contain("cmd.SetComputeFloatParam(m_ResolveCompute, CSMPCSSMaxPenumbraSizeId, m_PCSSMaxPenumbraSize);"));
            Assert.That(source, Does.Contain("cmd.SetComputeFloatParam(m_ResolveCompute, CSMPCSSBlockerSamplingClumpExponentId, m_PCSSBlockerSamplingClumpExponent);"));
        }

        [Test]
        public void CSMShadowResolvePass_UsesLegacyTileResolvePath_ForVeryHighScreenSpaceShadows()
        {
            var source = File.ReadAllText(GetPassSourcePath());

            Assert.That(source, Does.Contain("private const int ScreenSpaceShadowTileSize = 16;"));
            Assert.That(source, Does.Contain("private const int IndirectDispatchArgsElementCount = 3;"));
            Assert.That(source, Does.Contain("private const string ClearTilesKernelName = \"CSMShadowClearTiles\";"));
            Assert.That(source, Does.Contain("private const string ClassifyTilesKernelName = \"CSMShadowClassifyTiles\";"));
            Assert.That(source, Does.Contain("private const string ResolveTilesKernelName = \"CSMShadowResolveTiles\";"));
            Assert.That(source, Does.Contain("private const string CopyFilterSourceKernelName = \"CSMShadowCopyFilterSource\";"));
            Assert.That(source, Does.Contain("private const string BilateralFilterHKernelName = \"CSMShadowBilateralFilterH\";"));
            Assert.That(source, Does.Contain("private const string BilateralFilterVKernelName = \"CSMShadowBilateralFilterV\";"));
            Assert.That(source, Does.Contain("private static readonly int CSMShadowTileListId = Shader.PropertyToID(\"_CSMShadowTileList\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMShadowDispatchIndirectArgsId = Shader.PropertyToID(\"_CSMShadowDispatchIndirectArgs\");"));
            Assert.That(source, Does.Contain("[RenderGraphResource(Name = \"CSMShadowTileList\", Access = AccessFlags.ReadWrite)]"));
            Assert.That(source, Does.Contain("[RenderGraphResource(Name = \"CSMShadowDispatchIndirectArgs\", Access = AccessFlags.ReadWrite)]"));
            Assert.That(source, Does.Contain("[RenderGraphResource(Name = \"CSMShadowFilterTexture\", Access = AccessFlags.ReadWrite)]"));
            Assert.That(source, Does.Contain("m_EnableBilateralDenoise = csmSettings != null && csmSettings.screenSpaceShadowDenoise.value;"));
            Assert.That(source, Does.Contain("m_EnableTiledResolve = m_ShadowQuality == (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh"));
            Assert.That(source, Does.Contain("RecordTiledScreenSpaceResolve(cmd);"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ResolveCompute, m_ClearTilesKernel, 1, 1, 1);"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ResolveCompute, m_ClassifyTilesKernel, m_TileCountX, m_TileCountY, 1);"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ResolveCompute, m_ResolveTilesKernel, m_DispatchIndirectArgsBuffer, 0);"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ResolveCompute, m_CopyFilterSourceKernel, m_DispatchGroupCountX, m_DispatchGroupCountY, 1);"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ResolveCompute, m_BilateralFilterHKernel, m_DispatchIndirectArgsBuffer, 0);"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ResolveCompute, m_BilateralFilterVKernel, m_DispatchIndirectArgsBuffer, 0);"));
            Assert.That(source, Does.Contain("m_CopyFilterSourceKernel >= 0"));
            Assert.That(source, Does.Contain("return shader != null && shader.HasKernel(kernelName) ? shader.FindKernel(kernelName) : -1;"));
        }

        private static string GetPassSourcePath()
        {
            var passPath = GetPackageFilePath("Runtime", "RenderPass", "Core", "CSMShadowResolvePass.cs");

            Assert.That(File.Exists(passPath), Is.True, $"Expected pass source at '{passPath}'.");
            return passPath;
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
