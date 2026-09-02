using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Editor;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class CascadedShadowSettingsVolumeTests
    {
        [Test]
        public void GetCascadeBorderRatios_ConvertsInterCascadeRangesToSquaredDistanceFade()
        {
            var volume = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();

            try
            {
                volume.cascadeCount.value = 4;
                volume.cascadeSplit1.value = 2.0f / 70.0f;
                volume.cascadeSplit2.value = 6.0f / 70.0f;
                volume.cascadeSplit3.value = 22.0f / 70.0f;
                volume.cascadeBorder1.value = 0.26794f / 2.0f;
                volume.cascadeBorder2.value = 1.17174f / 4.0f;
                volume.cascadeBorder3.value = 3.35086f / 16.0f;
                volume.cascadeBorder4.value = 0.0f;

                var borders = volume.GetCascadeBorderRatios();

                Assert.That(borders.x, Is.EqualTo(ConvertInterCascadeBorder(volume.cascadeBorder1.value, 0.0f, volume.cascadeSplit1.value)).Within(1e-6f));
                Assert.That(borders.y, Is.EqualTo(ConvertInterCascadeBorder(volume.cascadeBorder2.value, volume.cascadeSplit1.value, volume.cascadeSplit2.value)).Within(1e-6f));
                Assert.That(borders.z, Is.EqualTo(ConvertInterCascadeBorder(volume.cascadeBorder3.value, volume.cascadeSplit2.value, volume.cascadeSplit3.value)).Within(1e-6f));
                Assert.That(borders.w, Is.EqualTo(ConvertInterCascadeBorder(volume.cascadeBorder4.value, volume.cascadeSplit3.value, 1.0f)).Within(1e-6f));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        private static float ConvertInterCascadeBorder(float interCascadeBorder, float previousCascadeRelativeRange, float cascadeRelativeRange)
        {
            float rangeBorder = cascadeRelativeRange > 0.0f
                ? (cascadeRelativeRange - previousCascadeRelativeRange) * interCascadeBorder / cascadeRelativeRange
                : 0.0f;

            return 1.0f - (1.0f - rangeBorder) * (1.0f - rangeBorder);
        }

        [Test]
        public void CascadedShadowSettingsVolume_ExposesScreenSpaceShadowDenoiseToggle()
        {
            var volume = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();

            try
            {
                Assert.That(volume.screenSpaceShadowDenoise, Is.Not.Null);
                Assert.That(volume.screenSpaceShadowDenoise.value, Is.False);

                volume.screenSpaceShadowDenoise.value = true;

                Assert.That(volume.screenSpaceShadowDenoise.value, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void CascadedShadowSettingsVolume_VirtualShadowMapPrototypeDefaultsOff()
        {
            var volume = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();

            try
            {
                Assert.That(volume.enableVirtualShadowMapPrototype, Is.Not.Null);
                Assert.That(volume.enableVirtualShadowMapPrototype.value, Is.False);

                volume.enableVirtualShadowMapPrototype.value = true;

                Assert.That(volume.enableVirtualShadowMapPrototype.value, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [TestCase(GraphicsDeviceType.Direct3D12, true, true, true, true)]
        [TestCase(GraphicsDeviceType.Vulkan, true, true, true, true)]
        [TestCase(GraphicsDeviceType.Direct3D11, true, true, true, false)]
        [TestCase(GraphicsDeviceType.Direct3D12, false, true, true, false)]
        [TestCase(GraphicsDeviceType.Direct3D12, true, false, true, false)]
        [TestCase(GraphicsDeviceType.Direct3D12, true, true, false, false)]
        public void VirtualShadowMapPrototypeSupport_RequiresTargetPlatformCapabilities(
            GraphicsDeviceType deviceType,
            bool usesReversedZBuffer,
            bool supportsComputeShaders,
            bool supportsR32UIntRenderAndLoadStore,
            bool expected)
        {
            Assert.That(
                VirtualShadowMapPrototypeRuntime.IsSupported(
                    deviceType,
                    usesReversedZBuffer,
                    supportsComputeShaders,
                    supportsR32UIntRenderAndLoadStore),
                Is.EqualTo(expected));
        }

        [TestCase(false, true, true, true, false)]
        [TestCase(true, false, false, true, false)]
        [TestCase(true, true, false, true, true)]
        [TestCase(true, false, true, false, true)]
        [TestCase(true, true, true, false, false)]
        public void VirtualShadowMapPrototypePreparation_AcceptsEitherCasterBackend(
            bool prototypeEnabled,
            bool hasUnityShadowCasters,
            bool hasMeshletShadowCasters,
            bool unityCastersCompatible,
            bool expected)
        {
            Assert.That(
                CSMShadowPass.ShouldPrepareVirtualShadowMapPrototype(
                    prototypeEnabled,
                    hasUnityShadowCasters,
                    hasMeshletShadowCasters,
                    unityCastersCompatible),
                Is.EqualTo(expected));
        }

        [Test]
        public void VirtualShadowMapPrototypePreparation_StableSelectionAllocatesZeroBytes()
        {
            const int iterationCount = 4096;
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                CSMShadowPass.ShouldPrepareVirtualShadowMapPrototype(
                    prototypeEnabled: true,
                    hasUnityShadowCasters: true,
                    hasMeshletShadowCasters: false,
                    unityCastersCompatible: true);
            }

            int enabledCount = 0;
            long allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                if (CSMShadowPass.ShouldPrepareVirtualShadowMapPrototype(
                        prototypeEnabled: true,
                        hasUnityShadowCasters: true,
                        hasMeshletShadowCasters: false,
                        unityCastersCompatible: true))
                {
                    enabledCount++;
                }
            }
            long allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread()
                - allocatedBefore;

            Assert.That(enabledCount, Is.EqualTo(iterationCount));
            Assert.That(allocatedBytes, Is.Zero);
        }

        [TestCase("VividRP/Material/StandardLit")]
        [TestCase("VividRP/Material/StandardLayeredLit")]
        [TestCase("VividRP/Experimental/Material/StandardLit")]
        [TestCase("VividRP/Material/Unlit")]
        [TestCase("VividRP/Terrain/TerrainLit")]
        [TestCase("Hidden/VividRP/TerrainLit_Basemap")]
        public void VirtualShadowMapUnityCasterCompatibility_AcceptsMarkedShadowCaster(
            string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, shaderName);
            var material = new Material(shader);
            try
            {
                Assert.That(
                    VirtualShadowMapUnityCasterCompatibility.TryValidateMaterial(
                        material,
                        out Shader unsupportedShader,
                        out string unsupportedPassName),
                    Is.True,
                    $"Unexpected unsupported pass '{unsupportedPassName}' on '{unsupportedShader}'.");

                int shadowCasterPass = material.FindPass("ShadowCaster");
                Assert.That(shadowCasterPass, Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    shader.FindPassTagValue(
                        shadowCasterPass,
                        new ShaderTagId(
                            VirtualShadowMapUnityCasterCompatibility.CapabilityTagName)),
                    Is.EqualTo(new ShaderTagId(
                        VirtualShadowMapUnityCasterCompatibility.CapabilityTagValue)));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void VirtualShadowMapUnityCasterCompatibility_RejectsUnmarkedShadowCaster()
        {
            Shader shader = Shader.Find("Hidden/VividRP/Tests/PerObjectBuffer");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            try
            {
                Assert.That(
                    VirtualShadowMapUnityCasterCompatibility.TryValidateMaterial(
                        material,
                        out Shader unsupportedShader,
                        out string unsupportedPassName),
                    Is.False);
                Assert.That(unsupportedShader, Is.SameAs(shader));
                Assert.That(unsupportedPassName, Is.EqualTo("ShadowCaster"));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void VirtualShadowMapUnityCasterCompatibility_ReportsExactRendererMaterialSlot()
        {
            Shader shader = Shader.Find("Hidden/VividRP/Tests/PerObjectBuffer");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            var gameObject = new GameObject("UnsupportedVSMCaster");
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;

            try
            {
                Assert.That(
                    VirtualShadowMapUnityCasterCompatibility.TryValidateRenderer(
                        renderer,
                        activeOnly: true,
                        out VirtualShadowMapUnityCasterFailure failure),
                    Is.False);
                Assert.That(failure.Caster, Is.SameAs(renderer));
                Assert.That(failure.Material, Is.SameAs(material));
                Assert.That(failure.Shader, Is.SameAs(shader));
                Assert.That(failure.MaterialSlot, Is.Zero);
                Assert.That(failure.PassName, Is.EqualTo("ShadowCaster"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void VirtualShadowMapBuildValidation_RejectsInactivePotentialCaster()
        {
            Shader shader = Shader.Find("Hidden/VividRP/Tests/PerObjectBuffer");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            var gameObject = new GameObject("InactiveUnsupportedVSMCaster");
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            gameObject.SetActive(false);

            try
            {
                Assert.That(
                    VirtualShadowMapUnityCasterCompatibility.TryValidateRenderer(
                        renderer,
                        activeOnly: true,
                        out _),
                    Is.True);
                Assert.That(
                    VirtualShadowMapUnityCasterCompatibility.TryValidateRenderer(
                        renderer,
                        activeOnly: false,
                        out VirtualShadowMapUnityCasterFailure failure),
                    Is.False);
                Assert.That(failure.Caster, Is.SameAs(renderer));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void VirtualShadowMapBuildValidation_RequiresSceneVolumeOverride()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            CascadedShadowSettingsVolume settings =
                profile.Add<CascadedShadowSettingsVolume>(overrides: false);
            settings.enableVirtualShadowMapPrototype.value = true;

            try
            {
                Assert.That(
                    VirtualShadowMapSceneBuildValidator.ProfileEnablesVirtualShadowMap(
                        profile,
                        requireOverride: false),
                    Is.True);
                Assert.That(
                    VirtualShadowMapSceneBuildValidator.ProfileEnablesVirtualShadowMap(
                        profile,
                        requireOverride: true),
                    Is.False);

                settings.enableVirtualShadowMapPrototype.overrideState = true;

                Assert.That(
                    VirtualShadowMapSceneBuildValidator.ProfileEnablesVirtualShadowMap(
                        profile,
                        requireOverride: true),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void VirtualShadowMapUnityCasterCompatibility_StableReadinessAllocatesZeroBytes()
        {
            const int warmupCount = 16;
            const int iterationCount = 256;
            for (int iteration = 0; iteration < warmupCount; iteration++)
                VirtualShadowMapUnityCasterCompatibility.IsReady();

            int readyCount = 0;
            long allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                if (VirtualShadowMapUnityCasterCompatibility.IsReady())
                    readyCount++;
            }
            long allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread()
                - allocatedBefore;

            Assert.That(readyCount, Is.InRange(0, iterationCount));
            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void VirtualShadowMapPrototypePageTable_PacksFourCascadesIntoTwoByTwoPool()
        {
            int pagesPerAxis = VirtualShadowMapPrototypeRuntime.CalculatePagesPerAxis(2048);
            uint[] pageTable = VirtualShadowMapPrototypeRuntime.BuildFullyResidentPageTable(
                pagesPerAxis,
                4);

            Assert.That(pagesPerAxis, Is.EqualTo(16));
            Assert.That(pageTable, Has.Length.EqualTo(1024));
            Assert.That(pageTable[0], Is.EqualTo(1u));
            Assert.That(pageTable[256], Is.EqualTo(17u));
            Assert.That(pageTable[512], Is.EqualTo(513u));
            Assert.That(pageTable[1023], Is.EqualTo(1024u));
        }

        [Test]
        public void VirtualShadowMapPrototypeResources_AllocateFourCascadePhysicalPool()
        {
            Assume.That(
                VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(),
                Is.True);

            try
            {
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.EnsureResources(512, 4),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.PhysicalPage.rt.width,
                    Is.EqualTo(1024));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.PhysicalPage.rt.height,
                    Is.EqualTo(1024));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RasterDepth.rt.volumeDepth,
                    Is.EqualTo(4));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.PageTableEntryCount,
                    Is.EqualTo(64));
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.ReleaseResources();
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeCache_ReusesMatchingStableState()
        {
            VirtualShadowMapPrototypeCacheKey key = CreateCacheKey();
            int initialHitCount = VirtualShadowMapPrototypeRuntime.CacheHitCount;
            int initialRefreshCount = VirtualShadowMapPrototypeRuntime.CacheRefreshCount;

            try
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresCacheRefresh(key),
                    Is.True);

                VirtualShadowMapPrototypeRuntime.CommitCache(key);

                Assert.That(VirtualShadowMapPrototypeRuntime.IsCacheValid, Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresCacheRefresh(key),
                    Is.False);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.TryUseCachedPages(key),
                    Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.LastFrameUsedCache, Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.CacheHitCount,
                    Is.EqualTo(initialHitCount + 1));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.CacheRefreshCount,
                    Is.EqualTo(initialRefreshCount + 1));
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeCache_InvalidatesChangedSceneCameraAndCascade()
        {
            VirtualShadowMapPrototypeCacheKey key = CreateCacheKey();

            try
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
                VirtualShadowMapPrototypeRuntime.CommitCache(key);

                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresCacheRefresh(
                        CreateCacheKey(rendererInstanceRevision: 8u)),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresCacheRefresh(
                        CreateCacheKey(gpuDrivenShadowRevision: 4u)),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresCacheRefresh(
                        CreateCacheKey(cameraEntityId: 43ul)),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresCacheRefresh(
                        CreateCacheKey(hasUnityShadowCasters: true)),
                    Is.True);

                Matrix4x4 movedCascade = Matrix4x4.identity;
                movedCascade.m03 = 1.0f / 2048.0f;
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresCacheRefresh(
                        CreateCacheKey(cascade0: movedCascade)),
                    Is.True);
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeCache_RejectsNonFiniteState()
        {
            Matrix4x4 invalidCascade = Matrix4x4.identity;
            invalidCascade.m00 = float.NaN;
            VirtualShadowMapPrototypeCacheKey key = CreateCacheKey(
                cascade0: invalidCascade);

            try
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();

                Assert.That(key.IsValid, Is.False);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresCacheRefresh(key),
                    Is.True);

                VirtualShadowMapPrototypeRuntime.CommitCache(key);

                Assert.That(VirtualShadowMapPrototypeRuntime.IsCacheValid, Is.False);
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeCacheKey_AcceptsUnityOnlyCasters()
        {
            VirtualShadowMapPrototypeCacheKey key = CreateCacheKey(
                primitiveSceneToken: 0u,
                hasUnityShadowCasters: true,
                hasMeshletShadowCasters: false);

            Assert.That(key.IsValid, Is.True);
        }

        private static VirtualShadowMapPrototypeCacheKey CreateCacheKey(
            ulong cameraEntityId = 42ul,
            uint rendererInstanceRevision = 7u,
            uint gpuDrivenShadowRevision = 3u,
            uint primitiveSceneToken = 1u,
            bool hasUnityShadowCasters = false,
            bool hasMeshletShadowCasters = true,
            Matrix4x4? cascade0 = null)
        {
            return new VirtualShadowMapPrototypeCacheKey(
                cameraEntityId,
                primitiveSceneToken: primitiveSceneToken,
                primitiveSceneRevision: 2u,
                gpuDrivenShadowRevision: gpuDrivenShadowRevision,
                rendererStructureRevision: 4u,
                rendererResourceRevision: 5u,
                rendererInstanceRevision: rendererInstanceRevision,
                textureBindingRevision: 11u,
                hasUnityShadowCasters: hasUnityShadowCasters,
                hasMeshletShadowCasters: hasMeshletShadowCasters,
                cascadeCount: 4,
                virtualResolution: 2048,
                forcedMeshLODNodeDepth: 0,
                meshLODErrorThreshold: 1.0f,
                slopeScaleDepthBias: 2.0f,
                shadowCasterState: Vector4.zero,
                cascade0: cascade0 ?? Matrix4x4.identity,
                cascade1: Matrix4x4.identity,
                cascade2: Matrix4x4.identity,
                cascade3: Matrix4x4.identity);
        }
    }
}
