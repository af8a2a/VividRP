using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class ReflectionProbeAtlasTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RTHandles.Initialize(1, 1);
        }

        [Test]
        public void ResolveDimensions_ReturnsPackedAsymmetricResolution()
        {
            var dimensions = VividReflectionProbeAtlasSettings.ResolveDimensions(
                VividReflectionProbeAtlasResolution.Resolution8192x4096);

            Assert.That(dimensions, Is.EqualTo(new Vector2Int(8192, 4096)));
        }

        [Test]
        public void GetReflectionProbeSizeInAtlas_MatchesHdrpCubeSizingRule()
        {
            Assert.That(VividReflectionProbeTextureCache.GetReflectionProbeSizeInAtlas(16), Is.EqualTo(128));
            Assert.That(VividReflectionProbeTextureCache.GetReflectionProbeSizeInAtlas(128), Is.EqualTo(512));
            Assert.That(VividReflectionProbeTextureCache.GetReflectionProbeSizeInAtlas(512), Is.EqualTo(1024));
        }

        [Test]
        public void ReflectionProbeData_StrideIncludesAtlasFields()
        {
            Assert.That(VividLightData.ReflectionProbeData.Stride, Is.EqualTo(128));
        }

        [Test]
        public void BSDFFilterSourceSize_ReservesHdrpConvolutionMipRange()
        {
            Assert.That(VividReflectionProbeTextureCache.GetBSDFFilterSourceSize(16), Is.EqualTo(64));
            Assert.That(VividReflectionProbeTextureCache.GetBSDFFilterSourceSize(64), Is.EqualTo(64));
            Assert.That(VividReflectionProbeTextureCache.GetBSDFFilterSourceSize(128), Is.EqualTo(128));
        }

        [Test]
        public void BSDFFilteredSourceMipLevel_ClampsToConvolutionMipRange()
        {
            Assert.That(VividReflectionProbeTextureCache.GetBSDFFilteredSourceMipLevel(-1), Is.Zero);
            Assert.That(VividReflectionProbeTextureCache.GetBSDFFilteredSourceMipLevel(3), Is.EqualTo(3));
            Assert.That(VividReflectionProbeTextureCache.GetBSDFFilteredSourceMipLevel(6), Is.EqualTo(6));
            Assert.That(VividReflectionProbeTextureCache.GetBSDFFilteredSourceMipLevel(12), Is.EqualTo(6));
        }

        [Test]
        public void GetAtlasSamplingMipCount_UsesBsdfFilteredMipCount()
        {
            var cache = new VividReflectionProbeTextureCache(
                null,
                512,
                512,
                GraphicsFormat.R16G16B16A16_SFloat,
                true,
                3);

            try
            {
                Assert.That(cache.GetAtlasMipCount(), Is.GreaterThan(VividReflectionProbeTextureCache.ConvolutionMipCount));
                Assert.That(cache.GetAtlasSamplingMipCount(), Is.EqualTo(VividReflectionProbeTextureCache.ConvolutionMipCount));
            }
            finally
            {
                cache.Dispose();
            }
        }

        [Test]
        public void ClusteredLightingData_DoesNotCarryReflectionAtlasState_WhenAtlasIsGlobal()
        {
            Assert.That(typeof(VividClusteredLightingData).GetField("reflectionAtlas"), Is.Null);
            Assert.That(typeof(VividClusteredLightingData).GetField("reflectionAtlasCubeData"), Is.Null);
            Assert.That(typeof(VividClusteredLightingData).GetField("reflectionAtlasMipCount"), Is.Null);
            Assert.That(typeof(VividClusteredLightingData).GetField("reflectionAtlasSliceCount"), Is.Null);
        }

        [Test]
        public void FetchCubeReflectionProbe_AllocatesCachedSlotAndResetsFetchIndex()
        {
            var probeTexture = CreateTemporaryCubeRenderTexture(32);
            var cmd = CommandBufferPool.Get("ReflectionProbeAtlasTests");
            var cache = new VividReflectionProbeTextureCache(
                null,
                512,
                512,
                GraphicsFormat.R16G16B16A16_SFloat,
                true,
                3);

            try
            {
                cache.NewRender();
                cache.NewFrame();

                var firstScaleOffset = cache.FetchCubeReflectionProbe(cmd, probeTexture, out var firstFetchIndex);
                var secondScaleOffset = cache.FetchCubeReflectionProbe(cmd, probeTexture, out var secondFetchIndex);

                Assert.That(firstFetchIndex, Is.EqualTo(0));
                Assert.That(secondFetchIndex, Is.EqualTo(1));
                Assert.That(firstScaleOffset.x, Is.GreaterThan(0.0f));
                Assert.That(firstScaleOffset.y, Is.GreaterThan(0.0f));
                Assert.That(secondScaleOffset, Is.EqualTo(firstScaleOffset));

                cache.NewFrame();
                var nextFrameScaleOffset = cache.FetchCubeReflectionProbe(cmd, probeTexture, out var nextFrameFetchIndex);

                Assert.That(nextFrameFetchIndex, Is.EqualTo(0));
                Assert.That(nextFrameScaleOffset, Is.EqualTo(firstScaleOffset));

                cache.Clear(cmd);
                cache.NewFrame();
                var clearedScaleOffset = cache.FetchCubeReflectionProbe(cmd, probeTexture, out var clearedFetchIndex);

                Assert.That(clearedFetchIndex, Is.EqualTo(0));
                Assert.That(clearedScaleOffset.x, Is.GreaterThan(0.0f));
                Assert.That(clearedScaleOffset.y, Is.GreaterThan(0.0f));
            }
            finally
            {
                cmd.Clear();
                cache.Dispose();
                CommandBufferPool.Release(cmd);
                probeTexture.Release();
                Object.DestroyImmediate(probeTexture);
            }
        }

        [Test]
        public void ApproxCacheSize_UsesGraphicsFormatBlockSize()
        {
            var bytes = VividReflectionProbeTextureCache.GetApproxCacheSizeInBytes(
                1,
                64,
                64,
                GraphicsFormat.R16G16B16A16_SFloat);

            Assert.That(bytes, Is.GreaterThan(64 * 64));
        }

        private static RenderTexture CreateTemporaryCubeRenderTexture(int size)
        {
            var descriptor = new RenderTextureDescriptor(size, size, GraphicsFormat.R16G16B16A16_SFloat, 0)
            {
                autoGenerateMips = false,
                dimension = TextureDimension.Cube,
                msaaSamples = 1,
                useMipMap = true,
                volumeDepth = 6
            };
            var texture = new RenderTexture(descriptor)
            {
                filterMode = FilterMode.Trilinear,
                name = "ReflectionProbeAtlasTests_Cube"
            };
            texture.Create();
            return texture;
        }
    }
}
