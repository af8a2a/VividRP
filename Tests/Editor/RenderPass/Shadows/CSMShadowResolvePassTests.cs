using System;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class CSMShadowResolvePassTests
    {

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
            Assert.That(
                unreal.MaxRayDistance,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightBendSSSMaxRayDistance).Within(0.001f));
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
    }
}
