using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class SkySpecularCacheTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RTHandles.Initialize(1, 1);
        }

        [Test]
        public void Update_AllocatesRTHandle_WhenSourceIsNew()
        {
            var cache = new SkySpecularCache();
            var cubemap = new Cubemap(4, TextureFormat.RGBA32, false);

            try
            {
                cache.Update(cubemap, 17);

                Assert.That(cache.IsValid, Is.True);
                Assert.That(cache.Cubemap, Is.Not.Null);
                Assert.That(cache.SkyHash, Is.EqualTo(17));
            }
            finally
            {
                cache.Dispose();
                Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void Update_ReusesHandle_WhenSourceAndHashAreUnchanged()
        {
            var cache = new SkySpecularCache();
            var cubemap = new Cubemap(4, TextureFormat.RGBA32, false);

            try
            {
                cache.Update(cubemap, 23);
                var firstHandle = cache.Cubemap;

                cache.Update(cubemap, 23);

                Assert.That(cache.Cubemap, Is.SameAs(firstHandle));
                Assert.That(cache.SkyHash, Is.EqualTo(23));
            }
            finally
            {
                cache.Dispose();
                Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void Update_AllocatesRTHandle_WhenSourceIsRuntimeCubemapTexture()
        {
            var cache = new SkySpecularCache();
            var runtimeCubemap = new RenderTexture(8, 8, 0)
            {
                dimension = TextureDimension.Cube,
                volumeDepth = 6,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                useMipMap = true,
                autoGenerateMips = false,
                enableRandomWrite = true
            };

            try
            {
                runtimeCubemap.Create();
                cache.Update(runtimeCubemap, 29);

                Assert.That(cache.IsValid, Is.True);
                Assert.That(cache.Cubemap, Is.Not.Null);
                Assert.That(cache.SkyHash, Is.EqualTo(29));
            }
            finally
            {
                cache.Dispose();
                runtimeCubemap.Release();
                Object.DestroyImmediate(runtimeCubemap);
            }
        }

        [Test]
        public void Update_UsesFallbackHandle_WhenSourceIsNull()
        {
            var cache = new SkySpecularCache();

            try
            {
                cache.Update(null, 0);
                var fallbackHandle = cache.Cubemap;

                Assert.That(cache.IsValid, Is.True);
                Assert.That(fallbackHandle, Is.Not.Null);
                Assert.That(cache.SkyHash, Is.EqualTo(0));
            }
            finally
            {
                cache.Dispose();
            }
        }

        [Test]
        public void DeferredLightingPass_DelegatesSkyCubemapLifecycleToSkyManager()
        {
            var deferredSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "DeferredLightingPass.cs"));
            var skyManagerSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyManager.cs"));
            var cacheSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkySpecularCache.cs"));

            Assert.That(deferredSource, Does.Contain("SkyManager.ImportSpecularCubemap(m_SkyIBLCubemap, skyData);"));
            Assert.That(deferredSource, Does.Not.Contain("ImportedSkyCubemapState"));
            Assert.That(deferredSource, Does.Not.Contain("EnsureSkyIblCubemapImported("));
            Assert.That(deferredSource, Does.Not.Contain("ReleaseSkyIblCubemapState("));
            Assert.That(deferredSource, Does.Not.Contain("CreateFallbackSkyIBLCubemap("));
            Assert.That(deferredSource, Does.Contain("SkyManager.GetSpecularCubemapMaxMip(skyData)"));

            Assert.That(skyManagerSource, Does.Contain("private static readonly SkySpecularCache s_SpecularCache = new();"));
            Assert.That(skyManagerSource, Does.Contain("s_SpecularCache.Build(resources);"));
            Assert.That(skyManagerSource, Does.Contain("internal static RTHandle GetSpecularCubemapHandle()"));
            Assert.That(skyManagerSource, Does.Contain("internal static int GetSpecularCubemapMaxMip(VividSkyData skyData = null)"));
            Assert.That(skyManagerSource, Does.Contain("internal static void ImportSpecularCubemap(RenderGraphTexture texture, VividSkyData skyData = null)"));
            Assert.That(skyManagerSource, Does.Contain("UpdateSpecularCubemap(cmd, s_CachedSkyData);"));
            Assert.That(skyManagerSource, Does.Contain("UpdateSpecularCubemap(skyData);"));
            Assert.That(cacheSource, Does.Contain("private const string PrefilterKernelName = \"SkySpecularPrefilter\";"));
            Assert.That(cacheSource, Does.Contain("internal bool HasSource(Texture source)"));
            Assert.That(cacheSource, Does.Contain("internal int MaxMipLevel"));
            Assert.That(cacheSource, Does.Contain("m_ConvolutionCompute = resources?.SkyAmbientProbeConvolutionCompute;"));
            Assert.That(cacheSource, Does.Contain("m_FilteredCubemap = new RenderTexture(faceSize, faceSize, 0)"));
            Assert.That(cacheSource, Does.Contain("m_FilteredCubemapFaces = new RenderTexture(faceSize, faceSize, 0)"));
            Assert.That(cacheSource, Does.Contain("cmd.SetComputeTextureParam(m_ConvolutionCompute, m_PrefilterKernel, SkySpecularSourceCubemapId, source);"));
            Assert.That(cacheSource, Does.Contain("cmd.SetComputeTextureParam("));
            Assert.That(cacheSource, Does.Contain("SkySpecularMipOutputId"));
            Assert.That(cacheSource, Does.Contain("cmd.DispatchCompute("));
            Assert.That(cacheSource, Does.Contain("for (var mip = 0; mip < mipCount; mip++)"));
            Assert.That(cacheSource, Does.Contain("cmd.CopyTexture(m_FilteredCubemapFaces, face, mip, m_FilteredCubemap, face, mip);"));
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
