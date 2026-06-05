using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

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
            Assert.That(source, Does.Contain("private static readonly int CSMBendLightCoordinateId = Shader.PropertyToID(\"_CSMBendLightCoordinate\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMBendWaveOffsetId = Shader.PropertyToID(\"_CSMBendWaveOffset\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMBendDepthTextureSizeId = Shader.PropertyToID(\"_CSMBendDepthTextureSize\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMBendSurfaceThicknessId = Shader.PropertyToID(\"_CSMBendSurfaceThickness\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMBendBilinearThresholdId = Shader.PropertyToID(\"_CSMBendBilinearThreshold\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMBendShadowContrastId = Shader.PropertyToID(\"_CSMBendShadowContrast\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMBendIgnoreEdgePixelsId = Shader.PropertyToID(\"_CSMBendIgnoreEdgePixels\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMBendUsePrecisionOffsetId = Shader.PropertyToID(\"_CSMBendUsePrecisionOffset\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMBendBilinearSamplingOffsetModeId = Shader.PropertyToID(\"_CSMBendBilinearSamplingOffsetMode\");"));
            Assert.That(source, Does.Not.Contain("private static readonly int CSMDepthBiasId = Shader.PropertyToID(\"_CSMDepthBias\");"));
            Assert.That(source, Does.Contain("m_ShadowQuality = (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low;"));
            Assert.That(source, Does.Contain("m_BendQualitySettings = ResolveBendQualitySettings(m_ShadowQuality);"));
            Assert.That(source, Does.Contain("CoreUtils.DivRoundUp(width, ThreadGroupSizeX);"));
            Assert.That(source, Does.Contain("TryResolveMainDirectionalLight(lightData, out _, out var additionalLightData)"));
            Assert.That(source, Does.Contain("m_ShadowQuality = (int)additionalLightData.screenSpaceShadowQuality;"));
            Assert.That(source, Does.Contain("m_LightAngularDiameter = Mathf.Max(additionalLightData.angularDiameter, 0.0f);"));
            Assert.That(source, Does.Contain("m_PCSSBlockerSampleCount = additionalLightData.dirLightPCSSBlockerSampleCount;"));
            Assert.That(source, Does.Contain("m_PCSSFilterSampleCount = additionalLightData.dirLightPCSSFilterSampleCount;"));
            Assert.That(source, Does.Contain("m_PCSSMaxPenumbraSize = additionalLightData.dirLightPCSSMaxPenumbraSize;"));
            Assert.That(source, Does.Contain("additionalLightData.dirLightBendSSSSurfaceThickness,"));
            Assert.That(source, Does.Contain("additionalLightData.dirLightBendSSSBilinearThreshold,"));
            Assert.That(source, Does.Contain("additionalLightData.dirLightBendSSSShadowContrast,"));
            Assert.That(source, Does.Contain("additionalLightData.dirLightBendSSSIgnoreEdgePixels,"));
            Assert.That(source, Does.Contain("additionalLightData.dirLightBendSSSUsePrecisionOffset,"));
            Assert.That(source, Does.Contain("additionalLightData.dirLightBendSSSBilinearSamplingOffsetMode);"));
            Assert.That(source, Does.Contain("m_CascadeWorldTexelSizes[i] = shadowData.cascadeWorldTexelSizes[i];"));
            Assert.That(source, Does.Contain("m_CascadeBorders[i] = shadowData.cascadeBorders[i];"));
            Assert.That(source, Does.Contain("m_FrameIndex = Time.frameCount;"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ResolveCompute, CSMShadowQualityId, ResolveCSMFilteringQuality(m_ShadowQuality));"));
            Assert.That(source, Does.Contain("cmd.SetComputeFloatParam(m_ResolveCompute, CSMLightAngularDiameterId, m_LightAngularDiameter);"));
            Assert.That(source, Does.Contain("cmd.SetComputeVectorParam(m_ResolveCompute, CSMCascadeWorldTexelSizesId, m_CascadeWorldTexelSizes);"));
            Assert.That(source, Does.Contain("cmd.SetComputeVectorParam(m_ResolveCompute, CSMCascadeBordersId, m_CascadeBorders);"));
            Assert.That(source, Does.Not.Contain("cmd.SetComputeFloatParam(m_ResolveCompute, CSMDepthBiasId, m_DepthBias);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ResolveCompute, CSMFrameIndexId, m_FrameIndex);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ResolveCompute, CSMPCSSBlockerSampleCountId, m_PCSSBlockerSampleCount);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ResolveCompute, CSMPCSSFilterSampleCountId, m_PCSSFilterSampleCount);"));
            Assert.That(source, Does.Contain("cmd.SetComputeFloatParam(m_ResolveCompute, CSMPCSSMaxPenumbraSizeId, m_PCSSMaxPenumbraSize);"));
            Assert.That(source, Does.Contain("cmd.SetComputeFloatParam(m_ResolveCompute, CSMPCSSBlockerSamplingClumpExponentId, m_PCSSBlockerSamplingClumpExponent);"));
            Assert.That(source, Does.Contain("cmd.SetComputeVectorParam(m_ResolveCompute, CSMBendLightCoordinateId, m_BendDispatchList.LightCoordinate);"));
            Assert.That(source, Does.Contain("cmd.SetComputeFloatParam(m_ResolveCompute, CSMBendShadowContrastId, m_BendQualitySettings.ShadowContrast);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ResolveCompute, CSMBendIgnoreEdgePixelsId, m_BendQualitySettings.IgnoreEdgePixels ? 1 : 0);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ResolveCompute, CSMBendUsePrecisionOffsetId, m_BendQualitySettings.UsePrecisionOffset ? 1 : 0);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ResolveCompute, CSMBendBilinearSamplingOffsetModeId, m_BendQualitySettings.BilinearSamplingOffsetMode ? 1 : 0);"));
        }

        [Test]
        public void CSMShadowResolvePass_SplitsVividTilePCSSAndUnrealBendQualityPaths()
        {
            var source = File.ReadAllText(GetPassSourcePath());

            Assert.That(source, Does.Contain("private const int ScreenSpaceShadowTileSize = 16;"));
            Assert.That(source, Does.Contain("private const int IndirectDispatchArgsElementCount = 3;"));
            Assert.That(source, Does.Contain("private const int BendWaveSize = 64;"));
            Assert.That(source, Does.Contain("private const int BendMaxDispatchCount = 8;"));
            Assert.That(source, Does.Contain("private const string ClearTilesKernelName = \"CSMShadowClearTiles\";"));
            Assert.That(source, Does.Contain("private const string ClassifyTilesKernelName = \"CSMShadowClassifyTiles\";"));
            Assert.That(source, Does.Contain("private const string ResolveTilesKernelName = \"CSMShadowResolveTiles\";"));
            Assert.That(source, Does.Contain("private const string CopyFilterSourceKernelName = \"CSMShadowCopyFilterSource\";"));
            Assert.That(source, Does.Contain("private const string BilateralFilterHKernelName = \"CSMShadowBilateralFilterH\";"));
            Assert.That(source, Does.Contain("private const string BilateralFilterVKernelName = \"CSMShadowBilateralFilterV\";"));
            Assert.That(source, Does.Contain("private const string BendCompositeLowKernelName = \"CSMShadowBendCompositeLow\";"));
            Assert.That(source, Does.Contain("private const string BendCompositeMediumKernelName = \"CSMShadowBendCompositeMedium\";"));
            Assert.That(source, Does.Contain("private const string BendCompositeHighKernelName = \"CSMShadowBendCompositeHigh\";"));
            Assert.That(source, Does.Contain("private const string BendCompositeVeryHighKernelName = \"CSMShadowBendCompositeVeryHigh\";"));
            Assert.That(source, Does.Contain("private static readonly int CSMShadowTileListId = Shader.PropertyToID(\"_CSMShadowTileList\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMShadowDispatchIndirectArgsId = Shader.PropertyToID(\"_CSMShadowDispatchIndirectArgs\");"));
            Assert.That(source, Does.Contain("[RenderGraphResource(Name = \"CSMShadowTileList\", Access = AccessFlags.ReadWrite)]"));
            Assert.That(source, Does.Contain("[RenderGraphResource(Name = \"CSMShadowDispatchIndirectArgs\", Access = AccessFlags.ReadWrite)]"));
            Assert.That(source, Does.Contain("[RenderGraphResource(Name = \"CSMShadowFilterTexture\", Access = AccessFlags.ReadWrite)]"));
            Assert.That(source, Does.Contain("m_EnableBilateralDenoise = csmSettings != null && csmSettings.screenSpaceShadowDenoise.value;"));
            Assert.That(source, Does.Contain("m_EnableTiledResolve = IsVividTiledPCSSQuality(m_ShadowQuality)"));
            Assert.That(source, Does.Contain("if (IsUnrealScreenSpaceShadowQuality(m_ShadowQuality))"));
            Assert.That(source, Does.Contain("RecordTiledScreenSpaceResolve(cmd);"));
            Assert.That(source, Does.Contain("RecordBendScreenSpaceContactShadow(cmd);"));
            Assert.That(source, Does.Contain("BuildBendDispatchList("));
            Assert.That(source, Does.Contain("m_EnableBendComposite = m_BendDispatchList.DispatchCount > 0"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ResolveCompute, m_ClearTilesKernel, 1, 1, 1);"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ResolveCompute, m_ClassifyTilesKernel, m_TileCountX, m_TileCountY, 1);"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ResolveCompute, m_ResolveTilesKernel, m_DispatchIndirectArgsBuffer, 0);"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ResolveCompute, m_CopyFilterSourceKernel, m_DispatchGroupCountX, m_DispatchGroupCountY, 1);"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ResolveCompute, m_BilateralFilterHKernel, m_DispatchIndirectArgsBuffer, 0);"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ResolveCompute, m_BilateralFilterVKernel, m_DispatchIndirectArgsBuffer, 0);"));
            Assert.That(source, Does.Contain("dispatch.WaveCount.x,"));
            Assert.That(source, Does.Contain("dispatch.WaveCount.y,"));
            Assert.That(source, Does.Contain("dispatch.WaveCount.z);"));
            Assert.That(source, Does.Contain("m_CopyFilterSourceKernel >= 0"));
            Assert.That(source, Does.Contain("return shader != null && shader.HasKernel(kernelName) ? shader.FindKernel(kernelName) : -1;"));
        }

        [Test]
        public void BuildBendDispatchList_ReturnsDispatches_ForOnScreenDirectionalLight()
        {
            var dispatchList = CSMShadowResolvePass.BuildBendDispatchList(
                new Vector4(0.0f, 0.0f, 0.5f, 1.0f),
                new Vector2Int(256, 128),
                Vector2Int.zero,
                new Vector2Int(256, 128));

            Assert.That(dispatchList.LightCoordinate.x, Is.EqualTo(128.0f).Within(0.001f));
            Assert.That(dispatchList.LightCoordinate.y, Is.EqualTo(64.0f).Within(0.001f));
            Assert.That(dispatchList.LightCoordinate.z, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(dispatchList.LightCoordinate.w, Is.EqualTo(1.0f).Within(0.001f));
            AssertValidBendDispatchList(dispatchList);
            Assert.That(dispatchList.DispatchCount, Is.EqualTo(6));
            AssertBendDispatch(dispatchList.Dispatches[0], new Vector3Int(64, 2, 2), new Vector2Int(-128, 0));
            AssertBendDispatch(dispatchList.Dispatches[1], new Vector3Int(64, 2, 1), new Vector2Int(0, 64));
            AssertBendDispatch(dispatchList.Dispatches[2], new Vector3Int(64, 1, 2), new Vector2Int(128, 64));
            AssertBendDispatch(dispatchList.Dispatches[3], new Vector3Int(64, 2, 1), new Vector2Int(-64, -64));
            AssertBendDispatch(dispatchList.Dispatches[4], new Vector3Int(64, 1, 2), new Vector2Int(-128, -128));
            AssertBendDispatch(dispatchList.Dispatches[5], new Vector3Int(64, 2, 2), new Vector2Int(64, -64));
        }

        [Test]
        public void BuildBendDispatchList_ClampsLightW_ForDirectionalClipCoordinate()
        {
            var dispatchList = CSMShadowResolvePass.BuildBendDispatchList(
                new Vector4(0.0f, 0.0f, 0.5f, 0.0f),
                new Vector2Int(128, 128),
                Vector2Int.zero,
                new Vector2Int(128, 128));

            AssertFinite(dispatchList.LightCoordinate);
            Assert.That(dispatchList.LightCoordinate.x, Is.EqualTo(64.0f).Within(0.001f));
            Assert.That(dispatchList.LightCoordinate.y, Is.EqualTo(64.0f).Within(0.001f));
            Assert.That(dispatchList.LightCoordinate.z, Is.EqualTo(0.0f).Within(0.001f));
            Assert.That(dispatchList.LightCoordinate.w, Is.EqualTo(-1.0f).Within(0.001f));
            AssertValidBendDispatchList(dispatchList);
            Assert.That(dispatchList.DispatchCount, Is.EqualTo(4));
            AssertBendDispatch(dispatchList.Dispatches[0], new Vector3Int(64, 1, 2), new Vector2Int(-64, 0));
            AssertBendDispatch(dispatchList.Dispatches[1], new Vector3Int(64, 2, 1), new Vector2Int(0, 64));
            AssertBendDispatch(dispatchList.Dispatches[2], new Vector3Int(64, 2, 1), new Vector2Int(-64, -64));
            AssertBendDispatch(dispatchList.Dispatches[3], new Vector3Int(64, 1, 2), new Vector2Int(64, -64));
        }

        [Test]
        public void BuildBendDispatchList_ReturnsDispatches_ForOffScreenLightCoordinate()
        {
            var dispatchList = CSMShadowResolvePass.BuildBendDispatchList(
                new Vector4(4.0f, 0.0f, 0.5f, 1.0f),
                new Vector2Int(128, 128),
                Vector2Int.zero,
                new Vector2Int(128, 128));

            AssertFinite(dispatchList.LightCoordinate);
            Assert.That(dispatchList.LightCoordinate.x, Is.GreaterThan(128.0f));
            AssertValidBendDispatchList(dispatchList);
            Assert.That(dispatchList.DispatchCount, Is.EqualTo(2));
            AssertBendDispatch(dispatchList.Dispatches[0], new Vector3Int(64, 2, 2), new Vector2Int(-320, 0));
            AssertBendDispatch(dispatchList.Dispatches[1], new Vector3Int(64, 3, 2), new Vector2Int(-320, -128));
        }

        [Test]
        public void BuildBendDispatchList_ReusesProvidedStorage_WithoutAllocating()
        {
            var dispatches = new CSMShadowResolvePass.BendDispatchData[8];

            CSMShadowResolvePass.BuildBendDispatchList(
                dispatches,
                new Vector4(0.0f, 0.0f, 0.5f, 1.0f),
                new Vector2Int(256, 128),
                Vector2Int.zero,
                new Vector2Int(256, 128));
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var dispatchList = CSMShadowResolvePass.BuildBendDispatchList(
                dispatches,
                new Vector4(0.0f, 0.0f, 0.5f, 1.0f),
                new Vector2Int(256, 128),
                Vector2Int.zero,
                new Vector2Int(256, 128));
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(allocatedBytes, Is.EqualTo(0));
            Assert.That(dispatchList.Dispatches, Is.SameAs(dispatches));
            AssertValidBendDispatchList(dispatchList);
        }

        [Test]
        public void ResolveBendQualitySettings_MapsUnrealToHighestBendTier()
        {
            var low = CSMShadowResolvePass.ResolveBendQualitySettings(
                (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low);
            var medium = CSMShadowResolvePass.ResolveBendQualitySettings(
                (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Medium);
            var high = CSMShadowResolvePass.ResolveBendQualitySettings(
                (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.High);
            var veryHigh = CSMShadowResolvePass.ResolveBendQualitySettings(
                (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh);
            var unreal = CSMShadowResolvePass.ResolveBendQualitySettings(
                (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Unreal);

            Assert.That(low.SurfaceThickness, Is.GreaterThan(medium.SurfaceThickness));
            Assert.That(medium.SurfaceThickness, Is.GreaterThan(high.SurfaceThickness));
            Assert.That(high.SurfaceThickness, Is.EqualTo(veryHigh.SurfaceThickness).Within(0.001f));
            Assert.That(veryHigh.SurfaceThickness, Is.GreaterThan(unreal.SurfaceThickness));
            Assert.That(low.BilinearThreshold, Is.GreaterThan(unreal.BilinearThreshold));
            Assert.That(low.ShadowContrast, Is.LessThan(medium.ShadowContrast));
            Assert.That(medium.ShadowContrast, Is.LessThanOrEqualTo(high.ShadowContrast));
            Assert.That(high.ShadowContrast, Is.EqualTo(veryHigh.ShadowContrast).Within(0.001f));
            Assert.That(veryHigh.ShadowContrast, Is.EqualTo(unreal.ShadowContrast).Within(0.001f));
            Assert.That(unreal.IgnoreEdgePixels, Is.EqualTo(VividAdditionalLightData.DefaultDirLightBendSSSIgnoreEdgePixels));
            Assert.That(unreal.UsePrecisionOffset, Is.EqualTo(VividAdditionalLightData.DefaultDirLightBendSSSUsePrecisionOffset));
            Assert.That(
                unreal.BilinearSamplingOffsetMode,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightBendSSSBilinearSamplingOffsetMode));
        }

        [Test]
        public void QualityHelpers_SplitVeryHighTilePCSSFromUnrealBend()
        {
            var veryHigh = (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh;
            var unreal = (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Unreal;

            Assert.That(CSMShadowResolvePass.IsVividTiledPCSSQuality(veryHigh), Is.True);
            Assert.That(CSMShadowResolvePass.IsUnrealScreenSpaceShadowQuality(veryHigh), Is.False);
            Assert.That(CSMShadowResolvePass.IsVividTiledPCSSQuality(unreal), Is.False);
            Assert.That(CSMShadowResolvePass.IsUnrealScreenSpaceShadowQuality(unreal), Is.True);
            Assert.That(
                CSMShadowResolvePass.ResolveCSMFilteringQuality(unreal),
                Is.EqualTo((int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.High));
            Assert.That(
                CSMShadowResolvePass.ResolveCSMFilteringQuality(veryHigh),
                Is.EqualTo((int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh));
        }

        [Test]
        public void CreateBendDepthTextureSize_ClampsInvalidDimensions()
        {
            var textureSize = CSMShadowResolvePass.CreateBendDepthTextureSize(0, -5);

            Assert.That(textureSize.x, Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(textureSize.y, Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(textureSize.z, Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(textureSize.w, Is.EqualTo(1.0f).Within(0.001f));
        }

        private static void AssertValidBendDispatchList(CSMShadowResolvePass.BendDispatchList dispatchList)
        {
            Assert.That(dispatchList.DispatchCount, Is.GreaterThan(0));
            Assert.That(dispatchList.DispatchCount, Is.LessThanOrEqualTo(8));
            Assert.That(dispatchList.Dispatches, Is.Not.Null);
            Assert.That(dispatchList.Dispatches.Length, Is.GreaterThanOrEqualTo(dispatchList.DispatchCount));

            for (var i = 0; i < dispatchList.DispatchCount; i++)
            {
                var dispatch = dispatchList.Dispatches[i];

                Assert.That(dispatch.WaveCount.x, Is.GreaterThan(0));
                Assert.That(dispatch.WaveCount.y, Is.GreaterThan(0));
                Assert.That(dispatch.WaveCount.z, Is.GreaterThan(0));
                Assert.That(dispatch.WaveOffset.x % 64, Is.EqualTo(0));
                Assert.That(dispatch.WaveOffset.y % 64, Is.EqualTo(0));
            }
        }

        private static void AssertBendDispatch(
            CSMShadowResolvePass.BendDispatchData dispatch,
            Vector3Int expectedWaveCount,
            Vector2Int expectedWaveOffset)
        {
            Assert.That(dispatch.WaveCount, Is.EqualTo(expectedWaveCount));
            Assert.That(dispatch.WaveOffset, Is.EqualTo(expectedWaveOffset));
        }

        private static void AssertFinite(Vector4 value)
        {
            Assert.That(float.IsNaN(value.x) || float.IsInfinity(value.x), Is.False);
            Assert.That(float.IsNaN(value.y) || float.IsInfinity(value.y), Is.False);
            Assert.That(float.IsNaN(value.z) || float.IsInfinity(value.z), Is.False);
            Assert.That(float.IsNaN(value.w) || float.IsInfinity(value.w), Is.False);
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
                Path.Combine(projectRoot, "Packages", "Custom_URP"),
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
