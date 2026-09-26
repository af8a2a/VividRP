using VividRP.Runtime.VirtualShadowMap;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Editor;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime.PrimitiveScene;

namespace VividRP.Editor.Tests
{
    public sealed class CascadedShadowSettingsVolumeTests
    {
        private static readonly VividGPUCullingContext[] s_CacheCullingContexts =
            CreateCacheCullingContexts();

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

        [Test]
        public void VSMQuality_DefaultsToHardReferenceAndClampsTransitionWidth()
        {
            var volume = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            try
            {
                Assert.That(volume.virtualShadowMapPCF.value, Is.False);
                Assert.That(volume.virtualShadowMapStochasticFiltering.value, Is.False);
                Assert.That(volume.virtualShadowMapTransition.value, Is.EqualTo(0.2f));
                volume.virtualShadowMapTransition.value = 1;
                Assert.That(volume.virtualShadowMapTransition.value, Is.EqualTo(0.5f));
                volume.virtualShadowMapTransition.value = -1;
                Assert.That(volume.virtualShadowMapTransition.value, Is.Zero);
            }
            finally { Object.DestroyImmediate(volume); }
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
                VSMShadowPass.ShouldPrepareVirtualShadowMapPrototype(
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
                VSMShadowPass.ShouldPrepareVirtualShadowMapPrototype(
                    prototypeEnabled: true,
                    hasUnityShadowCasters: true,
                    hasMeshletShadowCasters: false,
                    unityCastersCompatible: true);
            }

            int enabledCount = 0;
            long allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                if (VSMShadowPass.ShouldPrepareVirtualShadowMapPrototype(
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

        [TestCase("VividRP/Material/StandardLit", true)]
        [TestCase("VividRP/Material/StandardLayeredLit", true)]
        [TestCase("VividRP/Experimental/Material/StandardLit", true)]
        [TestCase("VividRP/Material/Unlit", true)]
        [TestCase("Hidden/VividRP/Tests/PerObjectBuffer", false)]
        public void UnityDynamicCache_RequiresRigidRendererAndBoundsContract(string shaderName, bool expected)
        {
            var go = new GameObject("Bounded Unity shadow caster");
            var material = new Material(Shader.Find(shaderName));
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                Assert.That(VirtualShadowMapUnityCasterCompatibility.HasConservativeBounds(renderer), Is.EqualTo(expected));
                Object.DestroyImmediate(renderer);
                var skinned = go.AddComponent<SkinnedMeshRenderer>();
                skinned.sharedMaterial = material;
                Assert.That(VirtualShadowMapUnityCasterCompatibility.HasConservativeBounds(skinned), Is.False);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(material); }
        }

        [Test]
        public void UnityDynamicCache_BoundsContractCheckAllocatesZeroBytesAfterWarmup()
        {
            var go = new GameObject("Bounded Unity shadow caster");
            var material = new Material(Shader.Find("VividRP/Material/StandardLit"));
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                for (int i = 0; i < 32; i++)
                    VirtualShadowMapUnityCasterCompatibility.HasConservativeBounds(renderer);
                int bounded = 0;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++)
                    if (VirtualShadowMapUnityCasterCompatibility.HasConservativeBounds(renderer)) bounded++;
                long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(bounded, Is.EqualTo(256));
                Assert.That(bytes, Is.Zero);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(material); }
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

        [TestCase(0, 2048, 2048)]
        [TestCase(4097, 512, 4224)]
        [TestCase(16384, 512, 16384)]
        [TestCase(99, 512, 128)]
        public void VSMResolution_IsIndependentAndPageAligned(int requested, int csm, int expected)
        {
            Assert.That(VirtualShadowMapProjectionSet.ResolveResolution(requested, csm), Is.EqualTo(expected));
        }

        [Test]
        public void VSMProjectionABI_AndAllocatorAreNotLimitedToFourCSMCascades()
        {
            Assert.That(Marshal.SizeOf<VirtualShadowMapProjection>(), Is.EqualTo(160));
            Assert.That(Marshal.OffsetOf<VirtualShadowMapProjection>("Parameters").ToInt32(), Is.EqualTo(144));
            Assert.That(VirtualShadowMapPrototypeRuntime.BuildUnmappedPageTable(8, 7).Length,
                Is.EqualTo(8 * 8 * 7));
            Assert.That(VirtualShadowMapPrototypeRuntime.CalculatePhysicalPageCapacity(1, 7), Is.EqualTo(7));
        }

        [TestCase(true, 128)]
        [TestCase(false, 128)]
        [TestCase(true, 2048)]
        [TestCase(false, 2048)]
        public void VSMRasterProjection_PreservesVirtualPixelCentersAndDepth(bool top, int tileSize)
        {
            const int resolution = 16384;
            for (int originY = 0; originY < resolution; originY += tileSize)
            for (int originX = 0; originX < resolution; originX += tileSize)
            {
                Matrix4x4 transform = VirtualShadowMapProjectionSet.RasterTransform(
                    resolution, tileSize, originX, originY, top);
                foreach (float local in new[] { 0.5f, tileSize * 0.5f, tileSize - 0.5f })
                {
                    float x = (originX + local) / resolution * 2 - 1;
                    float y = (originY + local) / resolution * 2 - 1;
                    Vector4 clip = new Vector4(x, top ? -y : y, 0.37f, 1);
                    Vector4 raster = transform * clip;
                    Assert.That((raster.x + 1) * 0.5f * tileSize, Is.EqualTo(local).Within(0.002f));
                    Assert.That(((top ? -raster.y : raster.y) + 1) * 0.5f * tileSize,
                        Is.EqualTo(local).Within(0.002f));
                    Assert.That(raster.z, Is.EqualTo(clip.z));
                    Assert.That(raster.w, Is.EqualTo(clip.w));
                }
            }
        }

        [Test]
        public void VSMClipmapLayoutAndResourceReuse_AllocateZeroAfterWarmup()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            using var command = new CommandBuffer();
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
            Assert.That(compute, Is.Not.Null);
            int receiverParametersId = Shader.PropertyToID("_VSMReceiverParameters");
            var quality = new Vector4(1, 1, 1, 0);
            var layout = new VirtualShadowMapClipmapLayout();
            var bounds = new Bounds(Vector3.zero, Vector3.one * 100);
            layout.Update(Vector3.zero, Quaternion.identity, bounds, 150, 16384, 2, 1, 1, 2);
            try
            {
                for (int i = 0; i < 32; i++)
                {
                    VirtualShadowMapPrototypeRuntime.EnsureResources(16384, layout.Count);
                    layout.Update(Vector3.zero, Quaternion.identity, bounds, 150, 16384, 2, 1, 1, 2);
                    VirtualShadowMapPrototypeRuntime.Projections.PrepareClipmaps(layout);
                    VirtualShadowMapPrototypeRuntime.Projections.CommitRecordedLayout();
                    VirtualShadowMapPrototypeRuntime.Projections.Upload(command);
                    command.SetComputeVectorParam(compute, receiverParametersId, quality);
                    command.Clear();
                }
                var raster = VirtualShadowMapPrototypeRuntime.RasterDepth;
                var unityRaster = VirtualShadowMapPrototypeRuntime.UnityRasterDepth;
                var projectionBuffer = VirtualShadowMapPrototypeRuntime.Projections.Buffer;
                long before = global::System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++)
                {
                    VirtualShadowMapPrototypeRuntime.EnsureResources(16384, layout.Count);
                    layout.Update(Vector3.zero, Quaternion.identity, bounds, 150, 16384, 2, 1, 1, 2);
                    VirtualShadowMapPrototypeRuntime.Projections.PrepareClipmaps(layout);
                    VirtualShadowMapPrototypeRuntime.Projections.CommitRecordedLayout();
                    VirtualShadowMapPrototypeRuntime.Projections.GetRasterMatrix(0, 16384, 2048, 4096, 2048, true);
                    VirtualShadowMapPrototypeRuntime.Projections.Upload(command);
                    command.SetComputeVectorParam(compute, receiverParametersId, quality);
                    command.Clear();
                }
                long allocated = global::System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
                Assert.That(VirtualShadowMapPrototypeRuntime.RasterDepth, Is.SameAs(raster));
                Assert.That(VirtualShadowMapPrototypeRuntime.UnityRasterDepth, Is.SameAs(unityRaster));
                Assert.That(VirtualShadowMapPrototypeRuntime.Projections.Buffer, Is.SameAs(projectionBuffer));
                Assert.That(raster.rt.width, Is.EqualTo(128));
                Assert.That(raster.rt.height, Is.EqualTo(128));
                Assert.That(raster.rt.volumeDepth, Is.EqualTo(256));
                Assert.That(unityRaster.rt.width, Is.EqualTo(2048));
                Assert.That(unityRaster.rt.volumeDepth, Is.EqualTo(1));
                Assert.That(VirtualShadowMapPrototypeRuntime.PageTableEntryCount, Is.EqualTo(16384 * layout.Count));
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.ReleaseResources();
            }
        }

        [Test]
        public void VirtualShadowMapPrototypePageTable_StartsUnmapped()
        {
            int pagesPerAxis = VirtualShadowMapPrototypeRuntime.CalculatePagesPerAxis(2048);
            uint[] pageTable = VirtualShadowMapPrototypeRuntime.BuildUnmappedPageTable(
                pagesPerAxis,
                4);

            Assert.That(pagesPerAxis, Is.EqualTo(16));
            Assert.That(pageTable, Has.Length.EqualTo(1024));
            Assert.That(pageTable, Has.All.EqualTo(0u));
            Assert.That(
                VirtualShadowMapPrototypeRuntime.CalculatePhysicalPageCapacity(
                    pagesPerAxis,
                    4),
                Is.EqualTo(VirtualShadowMapPrototypeRuntime.DefaultPhysicalPageCount));
        }

        [Test]
        public void PageBudgetResize_PreservesPressureButLayoutChangeResetsIt()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            try
            {
                VirtualShadowMapPrototypeRuntime.EnsureResources(512, 4, 32);
                var pressure = VirtualShadowMapPrototypeRuntime.PagePressure;
                var data = new[] { new uint4(1, 2, 3, 4), new uint4(5, 6, 7, 8), new uint4(9, 10, 11, 12) };
                pressure.SetData(data);
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(42ul, 10);
                VirtualShadowMapPrototypeRuntime.EnsureResources(512, 4, 64);
                Assert.That(VirtualShadowMapPrototypeRuntime.PagePressure, Is.SameAs(pressure));
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(42ul, 11), Is.False);
                var actual = new uint4[3];
                pressure.GetData(actual);
                Assert.That(actual, Is.EqualTo(data));
                VirtualShadowMapPrototypeRuntime.EnsureResources(1024, 4, 64);
                Assert.That(VirtualShadowMapPrototypeRuntime.PagePressure, Is.Not.SameAs(pressure));
                VirtualShadowMapPrototypeRuntime.PagePressure.GetData(actual);
                Assert.That(actual, Is.EqualTo(new uint4[3]));
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(42ul, 11), Is.True);
            }
            finally { VirtualShadowMapPrototypeRuntime.ReleaseResources(); }
        }

        private static void DispatchVSMAllocation(ComputeShader shader, int kernel, int pageCount,
            GraphicsBuffer metadata, uint[] demand)
        {
            using var requestFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pageCount, sizeof(uint));
            requestFlags.SetData(demand);
            using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                CoreUtils.DivRoundUp(pageCount, 32), sizeof(uint));
            using var pressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint) * 4);
            pressure.SetData(new uint4[3]);
            int prepare = shader.FindKernel("VSMPrototypePrepareAllocation");
            shader.SetBuffer(prepare, "_VSMPrototypePageMetadata", metadata);
            shader.SetBuffer(prepare, "_VSMPageRequestFlags", requestFlags);
            shader.SetBuffer(kernel, "_VSMPageRequestFlags", requestFlags);
            shader.SetBuffer(prepare, "_VSMAllocationRequests", requests);
            shader.Dispatch(prepare, CoreUtils.DivRoundUp(requests.count, 64), 1, 1);
            shader.SetBuffer(kernel, "_VSMAllocationRequests", requests);
            shader.SetBuffer(kernel, "_VSMPagePressureRW", pressure);
            shader.Dispatch(kernel, 1, 1, 1);
        }

        [Test]
        public void VirtualShadowMapPrototypeAllocator_AllocatesRequestedPagesDeterministically()
        {
            Assume.That(
                VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(),
                Is.True);

            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
            Assert.That(shader, Is.Not.Null);
            using var receiverMasks = new VirtualShadowMapReceiverMaskTestBuffers(shader);
            int kernel = shader.FindKernel("VSMPrototypeAllocatePages");
            var pageTableData = new uint[8];
            var metadataData = new TestPageMetadata[8];
            var demand = new uint[8];
            demand[1] = 1u;
            metadataData[1].LastRequestedFrame = 7u;
            demand[3] = 1u;
            metadataData[3].LastRequestedFrame = 7u;
            demand[7] = 1u;
            metadataData[7].LastRequestedFrame = 7u;
            var ownerData = new uint[2];
            var counterData = new uint[4];

            using var pageTable = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                pageTableData.Length,
                sizeof(uint));
            using var metadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                metadataData.Length,
                Marshal.SizeOf<TestPageMetadata>());
            using var owners = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                ownerData.Length,
                sizeof(uint));
            using var counters = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                counterData.Length,
                sizeof(uint));
            pageTable.SetData(pageTableData);
            metadata.SetData(metadataData);
            owners.SetData(ownerData);
            counters.SetData(counterData);

            shader.SetInt("_VSMPrototypePageTableEntryCount", pageTableData.Length);
            shader.SetInt("_VSMProjectionCount", 1);
            shader.SetInt("_VSMPrototypePhysicalPageCapacity", ownerData.Length);
            shader.SetInt("_VSMPrototypeFeedbackFrameIndex", 7);
            shader.SetBuffer(kernel, "_VSMPrototypeWritablePageTable", pageTable);
            shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", metadata);
            shader.SetBuffer(kernel, "_VSMPrototypePhysicalPageOwners", owners);
            shader.SetBuffer(kernel, "_VSMPrototypeAllocatorCounters", counters);
            DispatchVSMAllocation(shader, kernel, pageTableData.Length, metadata, demand);

            pageTable.GetData(pageTableData);
            metadata.GetData(metadataData);
            owners.GetData(ownerData);
            counters.GetData(counterData);

            Assert.That(pageTableData[1], Is.EqualTo(1u));
            Assert.That(pageTableData[3], Is.EqualTo(2u));
            Assert.That(pageTableData[7], Is.Zero);
            Assert.That(ownerData, Is.EqualTo(new uint[] { 2u, 4u }));
            Assert.That(counterData, Is.EqualTo(new uint[] { 2u, 3u, 2u, 1u }));
            Assert.That(metadataData[1].Flags & 1u, Is.Zero);
            Assert.That(metadataData[1].Flags & 2u, Is.EqualTo(2u));
            Assert.That(metadataData[3].EncodedPhysicalPage, Is.EqualTo(2u));
            Assert.That(metadataData[7].Flags, Is.Zero);
            Assert.That(metadataData[7].LastRequestedFrame, Is.EqualTo(7u));
        }

        [TestCase(16, 7)]
        [TestCase(128, 65)]
        [TestCase(4096, 1024)]
        public void VirtualShadowMapPrototypeAllocator_ProtectsCoarseRequestsAcrossCommitBatches(
            int pagesPerLevel, int capacity)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            ComputeShader source = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
            Assert.That(source, Is.Not.Null);
            ComputeShader shader = Object.Instantiate(source);
            using var receiverMasks = new VirtualShadowMapReceiverMaskTestBuffers(shader);
            try
            {
                int pageCount = pagesPerLevel * 2;
                const uint requestedPrimary = 1u | 512u;
                const uint allocated = 2u;
                var pageData = new uint[pageCount];
                var metaData = new TestPageMetadata[pageCount];
                var demand = new uint[pageCount];
                var ownerData = new uint[capacity];
                var counterData = new uint[4];
                // All slots belong to requested fine pages. Coarse requests of
                // the same role must evict them before processing the fine level.
                for (int slot = 0; slot < capacity; slot++)
                {
                    pageData[slot] = (uint)slot + 1u;
                    ownerData[slot] = (uint)slot + 1u;
                    metaData[slot].Flags = allocated;
                    demand[slot] = requestedPrimary;
                    metaData[slot].EncodedPhysicalPage = (uint)slot + 1u;
                    metaData[slot].LastRequestedFrame = 7u;
                }
                for (int page = pagesPerLevel; page < pageCount; page++)
                {
                    demand[page] = requestedPrimary;
                    metaData[page].LastRequestedFrame = 7u;
                }
                using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pageCount, sizeof(uint));
                using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pageCount, Marshal.SizeOf<TestPageMetadata>());
                using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
                using var counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, sizeof(uint));
                table.SetData(pageData);
                metadata.SetData(metaData);
                owners.SetData(ownerData);
                int kernel = shader.FindKernel("VSMPrototypeAllocatePages");
                shader.SetInt("_VSMPrototypePageTableEntryCount", pageCount);
                shader.SetInt("_VSMProjectionCount", 2);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", capacity);
                shader.SetInt("_VSMPrototypeFeedbackFrameIndex", 7);
                shader.SetBuffer(kernel, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(kernel, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(kernel, "_VSMPrototypeAllocatorCounters", counters);
                DispatchVSMAllocation(shader, kernel, pageCount, metadata, demand);
                table.GetData(pageData);
                metadata.GetData(metaData);
                owners.GetData(ownerData);
                counters.GetData(counterData);
                Assert.That(counterData, Is.EqualTo(new uint[]
                {
                    (uint)capacity, (uint)(capacity + pagesPerLevel), (uint)capacity, (uint)pagesPerLevel
                }));
                for (int page = 0; page < pageCount; page++)
                {
                    uint expected = page >= pagesPerLevel && page < pagesPerLevel + capacity
                        ? (uint)(page - pagesPerLevel + 1) : 0u;
                    Assert.That(pageData[page], Is.EqualTo(expected), $"Page {page}");
                    Assert.That(metaData[page].EncodedPhysicalPage, Is.EqualTo(expected));
                    Assert.That(metaData[page].Flags & requestedPrimary, Is.Zero);
                    Assert.That(metaData[page].Flags & allocated, Is.EqualTo(expected == 0u ? 0u : allocated));
                }
                for (int slot = 0; slot < capacity; slot++)
                    Assert.That(ownerData[slot], Is.EqualTo((uint)(pagesPerLevel + slot + 1)));
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeAllocator_RecyclesUnrequestedPagesAcrossFrames()
        {
            Assume.That(
                VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(),
                Is.True);

            ComputeShader source = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
            Assert.That(source, Is.Not.Null);
            ComputeShader shader = Object.Instantiate(source);
            using var receiverMasks = new VirtualShadowMapReceiverMaskTestBuffers(shader);
            try
            {
                const uint requested = 1u;
                const uint allocatedDirty = 2u | 4u | 16u | 32u | (1u << 15);
                const uint allocatedCached = 2u | 8u | 16u | 32u;
                var frames = new (uint Requests, uint Owner0, uint Owner1,
                    uint NewPages, uint Overflow)[]
                {
                    (0x08, 4, 0, 1, 0), // Fill unused slots before evicting.
                    (0x88, 4, 8, 1, 0),
                    (0x80, 4, 8, 0, 0), // Keep unrequested cache without pressure.
                    (0x82, 2, 8, 1, 0), // Protect a later-index resident request.
                    (0x04, 3, 8, 1, 0), // Equal ages: lowest physical slot wins.
                    (0x08, 3, 4, 1, 0), // LRU wins over physical slot order.
                    (0x05, 3, 1, 1, 0),
                    (0x1d, 3, 1, 0, 2), // Protect requests already processed.
                    (0x18, 4, 5, 2, 0), // Recover overflow; protect new allocations.
                    (0x00, 4, 5, 0, 0),
                    (0x80, 8, 5, 1, 0),
                    (0x02, 8, 2, 1, 0), // Revisit an evicted virtual page.
                    (0x82, 8, 2, 0, 0),
                };
                var pageTableData = new uint[8];
                var previousPageTable = new uint[8];
                var metadataData = new TestPageMetadata[8];
                var demand = new uint[8];
                var ownerData = new uint[2];
                var counterData = new uint[4];
                using var pageTable = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, pageTableData.Length, sizeof(uint));
                using var metadata = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, metadataData.Length,
                    Marshal.SizeOf<TestPageMetadata>());
                using var owners = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, ownerData.Length, sizeof(uint));
                using var counters = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, counterData.Length, sizeof(uint));
                pageTable.SetData(pageTableData);
                owners.SetData(ownerData);
                counters.SetData(counterData);
                int allocateKernel = shader.FindKernel("VSMPrototypeAllocatePages");
                shader.SetInt("_VSMProjectionCount", 1);
                int finalizeKernel = shader.FindKernel("VSMPrototypeFinalizeDirtyPages");
                shader.SetInt("_VSMPrototypePageTableEntryCount", pageTableData.Length);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", ownerData.Length);
                shader.SetBuffer(allocateKernel, "_VSMPrototypeWritablePageTable", pageTable);
                shader.SetBuffer(allocateKernel, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(allocateKernel, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(allocateKernel, "_VSMPrototypeAllocatorCounters", counters);
                shader.SetBuffer(finalizeKernel, "_VSMPrototypePageMetadata", metadata);

                for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
                {
                    var frame = frames[frameIndex];
                    uint feedbackFrame = (uint)frameIndex + 6u;
                    uint requestCount = 0u;
                    System.Array.Copy(pageTableData, previousPageTable, pageTableData.Length);
                    // Old request age must neither allocate nor prevent eviction.
                    System.Array.Clear(demand, 0, demand.Length);
                    metadataData[6].LastRequestedFrame = feedbackFrame - 1u;
                    for (int page = 0; page < metadataData.Length; page++)
                    {
                        if ((frame.Requests & (1u << page)) == 0u)
                            continue;
                        demand[page] = requested;
                        requestCount++;
                    }

                    metadata.SetData(metadataData);
                    shader.SetInt("_VSMPrototypeFeedbackFrameIndex", (int)feedbackFrame);
                    DispatchVSMAllocation(shader, allocateKernel, pageTableData.Length, metadata, demand);
                    pageTable.GetData(pageTableData);
                    metadata.GetData(metadataData);
                    owners.GetData(ownerData);
                    counters.GetData(counterData);
                    Assert.That(ownerData, Is.EqualTo(new[] { frame.Owner0, frame.Owner1 }));
                    Assert.That(counterData, Is.EqualTo(new[]
                    {
                        frame.Owner1 == 0u ? 1u : 2u, requestCount,
                        frame.NewPages, frame.Overflow,
                    }));
                    for (int page = 0; page < pageTableData.Length; page++)
                    {
                        uint mapping = pageTableData[page];
                        Assert.That(metadataData[page].EncodedPhysicalPage, Is.EqualTo(mapping));
                        bool wasRequested = (frame.Requests & (1u << page)) != 0u;
                        bool evicted = previousPageTable[page] != 0u && mapping == 0u;
                        bool overflow = wasRequested && mapping == 0u;
                        uint snapshot = metadataData[page].Reserved | demand[page];
                        Assert.That((snapshot & 1u) != 0u, Is.EqualTo(wasRequested));
                        Assert.That((snapshot & 64u) != 0u, Is.EqualTo(evicted));
                        Assert.That((snapshot & 128u) != 0u, Is.EqualTo(overflow));
                        if (mapping == 0u)
                        {
                            Assert.That(metadataData[page].Flags, Is.Zero);
                            continue;
                        }

                        Assert.That(ownerData[mapping - 1u], Is.EqualTo((uint)page + 1u));
                        Assert.That(metadataData[page].Flags, Is.EqualTo(
                            previousPageTable[page] == 0u ? allocatedDirty : allocatedCached));
                        if ((frame.Requests & (1u << page)) != 0u)
                            Assert.That(metadataData[page].LastRequestedFrame, Is.EqualTo(feedbackFrame));
                    }

                    shader.Dispatch(finalizeKernel, 1, 1, 1);
                    metadata.GetData(metadataData);
                    for (int page = 0; page < pageTableData.Length; page++)
                    {
                        bool refreshed = pageTableData[page] != 0u && previousPageTable[page] == 0u;
                        bool reused = pageTableData[page] != 0u && previousPageTable[page] != 0u;
                        Assert.That((metadataData[page].Reserved & 4u) != 0u, Is.EqualTo(refreshed));
                        Assert.That((metadataData[page].Reserved & 8u) != 0u, Is.EqualTo(reused));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }

        [TestCase(0.7509f, 0.7511f, 0.1f, 0.2f, 0x0008u)]
        [TestCase(0.1f, 0.2f, 0.7509f, 0.7511f, 0x1000u)]
        [TestCase(0.7499f, 0.7501f, 0.7499f, 0.7501f, 0xcc00u)]
        [TestCase(0.25f, 0.25f, 0.1f, 0.2f, 0x0002u)]
        [TestCase(1.0f, 1.0f, 0.1f, 0.2f, 0x0008u)]
        [TestCase(-0.1f, 0.0001f, 0.1f, 0.2f, 0x0001u)]
        [TestCase(0.2499f, 0.24999f, 0.1f, 0.2f, 0x0001u)]
        [TestCase(0.2509f, 0.2511f, 0.1f, 0.2f, 0x0002u)]
        [TestCase(0.5009f, 0.5011f, 0.1f, 0.2f, 0x0004u)]
        public void VirtualShadowMapPrototypeInvalidation_MatchesRasterPageBoundaries(
            float minX, float maxX, float minY, float maxY, uint dirtyPageMask)
        {
            Assume.That(
                VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(),
                Is.True);

            ComputeShader source = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
            Assert.That(source, Is.Not.Null);
            ComputeShader shader = Object.Instantiate(source);
            using var receiverMasks = new VirtualShadowMapReceiverMaskTestBuffers(shader);
            try
            {
                for (int pool = 0; pool < 2; pool++)
                {
                    const uint allocatedCached = (1u << 1) | (1u << 3);
                    uint allocatedDirty = pool == 0 ? (1u << 1) | (1u << 2) : allocatedCached | (1u << 15);
                    var metadataData = new TestPageMetadata[16];
                    for (int pageIndex = 0; pageIndex < metadataData.Length; pageIndex++)
                    {
                        metadataData[pageIndex].Flags = allocatedCached;
                        metadataData[pageIndex].EncodedPhysicalPage = (uint)pageIndex + 1u;
                        metadataData[pageIndex].Reserved = allocatedCached;
                    }
                    var boundsData = new[]
                    {
                        new VividStaticShadowInvalidationBounds
                        {
                            BoundsMin = new float4(minX, minY, -1.0f, 0.0f),
                            BoundsMax = new float4(maxX, maxY, 1.0f, 0.0f),
                        },
                    };
                    using var metadata = new GraphicsBuffer(
                        GraphicsBuffer.Target.Structured, metadataData.Length,
                        Marshal.SizeOf<TestPageMetadata>());
                    using var bounds = new GraphicsBuffer(
                        GraphicsBuffer.Target.Structured, boundsData.Length,
                        Marshal.SizeOf<VividStaticShadowInvalidationBounds>());
                    metadata.SetData(metadataData);
                    bounds.SetData(boundsData);

                    int kernel = shader.FindKernel(pool == 0 ? "VSMPrototypeInvalidateStaticPages" : "VSMPrototypeInvalidateDynamicPages");
                    shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", metadata);
                    shader.SetBuffer(kernel, "_VSMPrototypeStaticInvalidationBounds", bounds);
                    shader.SetInt("_VSMPrototypeStaticInvalidationBoundsCount", 1);
                    shader.SetInt("_VSMProjectionCount", 1);
                    shader.SetInt("_VSMPrototypeVirtualResolution", 512);
                    shader.SetInt("_VSMPrototypePageSize", 128);
                    shader.SetInt("_VSMPrototypePagesPerAxis", 4);
                    shader.SetInt("_VSMPrototypePageTableEntryCount", metadataData.Length);
                    using var projections = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 160);
                    projections.SetData(new[]
                    {
                        new VirtualShadowMapProjection { WorldToShadow = Matrix4x4.identity }
                    });
                    shader.SetBuffer(kernel, "_VSMProjections", projections);
                    shader.Dispatch(kernel, 1, 1, 1);
                    metadata.GetData(metadataData);

                    for (int pageIndex = 0; pageIndex < metadataData.Length; pageIndex++)
                    {
                        bool dirty = (dirtyPageMask & (1u << pageIndex)) != 0u;
                        Assert.That(metadataData[pageIndex].Flags,
                            Is.EqualTo(dirty ? allocatedDirty : allocatedCached));
                        Assert.That(metadataData[pageIndex].EncodedPhysicalPage,
                            Is.EqualTo((uint)pageIndex + 1u));
                    }
                    int finalize = shader.FindKernel("VSMPrototypeFinalizeDirtyPages");
                    shader.SetBuffer(finalize, "_VSMPrototypePageMetadata", metadata);
                    shader.Dispatch(finalize, 1, 1, 1);
                    metadata.GetData(metadataData);
                    for (int pageIndex = 0; pageIndex < metadataData.Length; pageIndex++)
                    {
                        bool dirty = (dirtyPageMask & (1u << pageIndex)) != 0u;
                        Assert.That((metadataData[pageIndex].Reserved & 4u) != 0u, Is.EqualTo(dirty));
                        Assert.That((metadataData[pageIndex].Reserved & 8u) != 0u, Is.EqualTo(!dirty));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }

        [TestCase(1, 1)]
        [TestCase(65, 7)]
        [TestCase(1024, 64)]
        [TestCase(65, 7, true)]
        [TestCase(65, 7, false, true)]
        public void PageUpdateBudget_PrioritizesCoarsePagesAndConverges(int capacity, int budget, bool remap = false, bool unlimitedAfterFirst = false)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute"));
            using var receiverMasks = new VirtualShadowMapReceiverMaskTestBuffers(shader);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * 2, 16);
            using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4);
            using var work = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * 2, 4);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, 6, 4);
            try
            {
                const uint dirty = 4u | (1u << 15), deferred = 1u << 17, known = (1u << 14) | (1u << 16);
                var data = new uint4[capacity * 2]; var owner = new uint[capacity];
                for (int slot = 0; slot < capacity; slot++)
                {
                    int page = slot / 2 + (slot % 2) * capacity;
                    owner[slot] = (uint)page + 1;
                    data[page] = new uint4(2u | dirty | known, (uint)slot + 1, 0, 0);
                }
                owners.SetData(owner);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", capacity);
                shader.SetInt("_VSMPrototypePageTableEntryCount", capacity * 2);
                shader.SetInt("_VSMProjectionCount", 2);
                shader.SetInt("_VSMPrototypePageSize", 128);
                shader.SetInt("_VSMPageUpdateBudget", budget);
                shader.SetInt("_VSMPageOccupancySkipDisabled", 0);
                using var requestFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, metadata.count, 4);
                var demands = new uint[metadata.count];
                requestFlags.SetData(demands);
                int build = shader.FindKernel("VSMBuildPageWorkLists");
                shader.SetBuffer(build, "_VSMPageRequestFlags", requestFlags);
                shader.SetBuffer(build, "_VSMPrototypePageMetadata", metadata);
                using var pageTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, metadata.count, 4);
                pageTable.SetData(new uint[metadata.count]);
                shader.SetBuffer(build, "_VSMPrototypePageTable", pageTable);
                shader.SetBuffer(build, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(build, "_VSMPageWorkListRW", work);
                shader.SetBuffer(build, "_VSMPageWorkDispatchArgsRW", args);
                int finalize = shader.FindKernel("VSMPrototypeFinalizeDirtyPages");
                shader.SetBuffer(finalize, "_VSMPrototypePageMetadata", metadata);
                var dispatch = new uint[6]; var entries = new uint[capacity * 2];
                var expected = new System.Collections.Generic.List<int>(capacity);
                int completed = 0;
                for (int frame = 0; frame <= (capacity + budget - 1) / budget + 1; frame++)
                {
                    expected.Clear();
                    System.Array.Clear(demands, 0, demands.Length);
                    if (remap && frame == 1)
                    {
                        // A deferred physical slot changes virtual owner before
                        // the next work list. No queued old identity may survive.
                        int oldPage = (int)owner[0] - 1, newPage = capacity * 2 - 1;
                        data[newPage] = data[oldPage]; data[oldPage] = default;
                        owner[0] = (uint)newPage + 1; owners.SetData(owner);
                    }
                    int currentBudget = unlimitedAfterFirst && frame > 0 ? 0 : budget;
                    int limit = currentBudget == 0 ? capacity : currentBudget;
                    shader.SetInt("_VSMPageUpdateBudget", currentBudget);
                    for (int slot = 0; slot < capacity; slot++)
                    {
                        int page = (int)owner[slot] - 1;
                        data[page].z = (uint)frame;
                        // One initially unrequested dirty page must wait, even
                        // when there is spare budget, then resume on new demand.
                        demands[page] = frame == 0 && slot == 0 ? 0u : 1u;
                        if ((data[page].x & dirty) != 0 && demands[page] != 0) expected.Add(slot);
                    }
                    expected.Sort((a, b) => {
                        int level = ((owner[b] - 1) / (uint)capacity).CompareTo((owner[a] - 1) / (uint)capacity);
                        return level != 0 ? level : ((a + frame) % capacity).CompareTo((b + frame) % capacity);
                    });
                    if (expected.Count > limit) expected.RemoveRange(limit, expected.Count - limit);
                    metadata.SetData(data);
                    requestFlags.SetData(demands);
                    shader.SetInt("_VSMPrototypeFeedbackFrameIndex", frame);
                    shader.Dispatch(build, 1, 1, 1);
                    args.GetData(dispatch); work.GetData(entries); metadata.GetData(data);
                    Assert.That(dispatch[2], Is.EqualTo(expected.Count));
                    Assert.That(dispatch[3], Is.EqualTo(expected.Count));
                    var clearSlots = new System.Collections.Generic.HashSet<int>();
                    var occupancySlots = new System.Collections.Generic.HashSet<int>();
                    for (int i = 0; i < expected.Count; i++)
                    {
                        Assert.That(clearSlots.Add((int)entries[i]), Is.True);
                        Assert.That(occupancySlots.Add((int)entries[capacity + i]), Is.True);
                    }
                    CollectionAssert.AreEquivalent(expected, clearSlots);
                    CollectionAssert.AreEquivalent(expected, occupancySlots);
                    // No drawing is needed for this metadata test: the selected
                    // pages represent completed empty geometry before finalize.
                    shader.Dispatch(finalize, (capacity * 2 + 63) / 64, 1, 1);
                    metadata.GetData(data);
                    foreach (int slot in expected)
                    {
                        Assert.That(data[owner[slot] - 1].x & (dirty | deferred | 8u), Is.EqualTo(8u));
                        completed++;
                    }
                    for (int slot = 0; slot < capacity; slot++)
                    {
                        uint flags = data[owner[slot] - 1].x;
                        if ((flags & dirty) != 0) Assert.That(flags & (dirty | deferred), Is.EqualTo(dirty | deferred));
                    }
                }
                Assert.That(completed, Is.EqualTo(capacity));
                Assert.That(dispatch[2] + dispatch[3], Is.Zero);
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(0, 1, 1, 2)] // Readable terminal: nearest parent and transition before far parents.
        [TestCase(0, 8, 2, 7)] // Maintenance reserves one slot without taking the whole budget.
        [TestCase(1, 1, 5, 7)] // Static dirty terminal.
        [TestCase(2, 1, 5, 7)] // Dynamic dirty terminal.
        [TestCase(3, 1, 4, 7)] // Deferred terminal.
        [TestCase(4, 1, 4, 7)] // Not allocated.
        [TestCase(5, 1, 4, 7)] // Missing page table entry.
        [TestCase(6, 1, 4, 7)] // Table and metadata disagree.
        [TestCase(7, 1, 4, 7)] // Physical owner is gone.
        [TestCase(8, 1, 4, 7)] // Metadata points to another slot.
        [TestCase(9, 1, 4, 7)] // Another terminal request has no physical slot.
        [TestCase(10, 1, 4, 7)] // No explicit terminal demand: conservative old order.
        [TestCase(11, 1, 1, 2)] // Completed empty terminal is a valid fallback.
        [TestCase(12, 1, 4, 7)] // Out-of-range physical slot.
        public void PageUpdateBudget_RequiresReadableTerminalBeforePrioritizingDetail(int fault, int frame, int first, int second)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute"));
            using var receiverMasks = new VirtualShadowMapReceiverMaskTestBuffers(shader);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 12, 16);
            using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 4);
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 12, 4);
            using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 12, 4);
            using var work = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16, 4);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, 6, 4);
            try
            {
                const uint dirty = 4u, deferred = 1u << 17, known = (1u << 14) | (1u << 16);
                var data = new uint4[12]; var mapping = new uint[12]; var demands = new uint[12];
                uint[] owner = { 1, 3, 5, 7, 9, 11, 2, 10 };
                uint[] roles = { 512, 2048, 1024, 0, 0, 256, 512, 0 };
                for (int slot = 0; slot < 8; slot++)
                {
                    int page = (int)owner[slot] - 1;
                    mapping[page] = (uint)slot + 1;
                    data[page] = new uint4(2u | known | (slot == 5 ? 0u : dirty), mapping[page], 0, 0);
                    demands[page] = 1u | roles[slot];
                }
                switch (fault)
                {
                    case 1: data[10].x |= dirty; break;
                    case 2: data[10].x |= 1u << 15; break;
                    case 3: data[10].x |= deferred; break;
                    case 4: data[10].x &= ~2u; break;
                    case 5: mapping[10] = 0; break;
                    case 6: mapping[10] = 7; break;
                    case 7: owner[5] = 0; break;
                    case 8: data[10].y = 7; break;
                    case 9: demands[11] = 1u | 256u; break;
                    case 10: demands[10] = 1; break;
                    case 11: data[10].x |= (1u << 12) | (1u << 13); break;
                    case 12: mapping[10] = data[10].y = 9; break;
                }
                metadata.SetData(data); owners.SetData(owner); table.SetData(mapping); requests.SetData(demands);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 8);
                shader.SetInt("_VSMPrototypePageTableEntryCount", 12);
                shader.SetInt("_VSMProjectionCount", 6);
                shader.SetInt("_VSMPrototypePageSize", 128);
                shader.SetInt("_VSMPageUpdateBudget", 2);
                shader.SetInt("_VSMPageOccupancySkipDisabled", 0);
                shader.SetInt("_VSMPrototypeFeedbackFrameIndex", frame);
                int build = shader.FindKernel("VSMBuildPageWorkLists");
                shader.SetBuffer(build, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(build, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(build, "_VSMPrototypePageTable", table);
                shader.SetBuffer(build, "_VSMPageRequestFlags", requests);
                shader.SetBuffer(build, "_VSMPageWorkListRW", work);
                shader.SetBuffer(build, "_VSMPageWorkDispatchArgsRW", args);
                shader.Dispatch(build, 1, 1, 1);
                var dispatch = new uint[6]; var entries = new uint[16]; var actualRequests = new uint[12];
                args.GetData(dispatch); work.GetData(entries); metadata.GetData(data); requests.GetData(actualRequests);
                Assert.That(dispatch[2], Is.EqualTo(2));
                CollectionAssert.AreEquivalent(new[] { (uint)first, (uint)second }, new[] { entries[0], entries[1] });
                CollectionAssert.AreEqual(demands, actualRequests);
                for (int slot = 0; slot < 8; slot++)
                {
                    if (owner[slot] == 0) continue;
                    uint flags = data[owner[slot] - 1].x;
                    if ((flags & (dirty | (1u << 15))) != 0)
                        Assert.That((flags & deferred) == 0, Is.EqualTo(slot == first || slot == second));
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [Test]
        public void PageUpdateBudget_FullParentChainProgressesUnderContinuousEssentialInvalidation()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute"));
            using var receiverMasks = new VirtualShadowMapReceiverMaskTestBuffers(shader);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 12, 16);
            using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 4);
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 12, 4);
            using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 12, 4);
            using var work = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16, 4);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, 6, 4);
            try
            {
                var data = new uint4[12]; var mapping = new uint[12]; var demands = new uint[12];
                uint[] owner = { 1, 3, 5, 7, 9, 11, 2, 10 };
                uint[] roles = { 512, 2048, 1024, 0, 0, 256, 512, 0 };
                for (int slot = 0; slot < 8; slot++)
                {
                    int page = (int)owner[slot] - 1;
                    mapping[page] = (uint)slot + 1;
                    data[page] = new uint4(slot == 5 ? 10u : 6u, mapping[page], 0, 0);
                    demands[page] = 1u | roles[slot];
                }
                owners.SetData(owner); table.SetData(mapping); requests.SetData(demands);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 8);
                shader.SetInt("_VSMPrototypePageTableEntryCount", 12);
                shader.SetInt("_VSMProjectionCount", 6);
                shader.SetInt("_VSMPrototypePageSize", 128);
                shader.SetInt("_VSMPageUpdateBudget", 2);
                int build = shader.FindKernel("VSMBuildPageWorkLists"), finalize = shader.FindKernel("VSMPrototypeFinalizeDirtyPages");
                shader.SetBuffer(build, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(build, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(build, "_VSMPrototypePageTable", table);
                shader.SetBuffer(build, "_VSMPageRequestFlags", requests);
                shader.SetBuffer(build, "_VSMPageWorkListRW", work);
                shader.SetBuffer(build, "_VSMPageWorkDispatchArgsRW", args);
                shader.SetBuffer(finalize, "_VSMPrototypePageMetadata", metadata);
                var dispatch = new uint[6]; var actualRequests = new uint[12];
                for (int frame = 1; frame <= 24; frame++)
                {
                    foreach (int page in new[] { 0, 1, 2, 4 }) data[page].x |= 4u;
                    metadata.SetData(data);
                    shader.SetInt("_VSMPrototypeFeedbackFrameIndex", frame);
                    shader.Dispatch(build, 1, 1, 1);
                    args.GetData(dispatch);
                    Assert.That(dispatch[2], Is.EqualTo(2));
                    // Selected pages represent completed empty caster output.
                    shader.Dispatch(finalize, 1, 1, 1);
                    metadata.GetData(data);
                }
                foreach (int page in new[] { 6, 8, 9 })
                    Assert.That(data[page].x & (4u | (1u << 17) | 8u), Is.EqualTo(8u));
                requests.GetData(actualRequests);
                CollectionAssert.AreEqual(demands, actualRequests);
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [Test]
        public void PageUpdateBudget_DefaultAndStableReadsPreserveZeroManagedAllocation()
        {
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            try
            {
                Assert.That(settings.virtualShadowMapPageUpdateBudget.value, Is.EqualTo(64));
                settings.virtualShadowMapPageUpdateBudget.value = -1;
                Assert.That(settings.virtualShadowMapPageUpdateBudget.value, Is.Zero);
                settings.virtualShadowMapPageUpdateBudget.value = 2048;
                Assert.That(settings.virtualShadowMapPageUpdateBudget.value, Is.EqualTo(1024));
                int sum = 0;
                for (int i = 0; i < 1024; i++) sum += settings.virtualShadowMapPageUpdateBudget.value;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1024; i++) sum += settings.virtualShadowMapPageUpdateBudget.value;
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
                Assert.That(sum, Is.EqualTo(2 * 1024 * 1024));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [TestCase(1)]
        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(1024)]
        public void PageWorkLists_IncludeDirtyAndUnknownPagesAndOverwriteZeroWork(int capacity)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute"));
            using var receiverMasks = new VirtualShadowMapReceiverMaskTestBuffers(shader);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 16);
            using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4);
            using var work = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * 2 + 1, 4);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, 7, 4);
            try
            {
                using var requestFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, metadata.count, 4);
                var demands = new uint[metadata.count];
                requestFlags.SetData(demands);
                int build = shader.FindKernel("VSMBuildPageWorkLists");
                shader.SetBuffer(build, "_VSMPageRequestFlags", requestFlags);
                shader.SetBuffer(build, "_VSMPrototypePageMetadata", metadata);
                using var pageTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, metadata.count, 4);
                pageTable.SetData(new uint[metadata.count]);
                shader.SetBuffer(build, "_VSMPrototypePageTable", pageTable);
                shader.SetBuffer(build, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(build, "_VSMPageWorkListRW", work);
                shader.SetBuffer(build, "_VSMPageWorkDispatchArgsRW", args);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", capacity);
                shader.SetInt("_VSMPrototypePageSize", 128);
                const uint known = (1u << 14) | (1u << 16);
                var data = new uint4[capacity]; var owner = new uint[capacity];
                var entries = new uint[capacity * 2 + 1]; var dispatch = new uint[7];
                entries[capacity * 2] = dispatch[6] = 0x12345678;
                work.SetData(entries); args.SetData(dispatch);
                // Reassign each physical slot to a different virtual owner; last
                // iteration removes all owners after a full list was produced.
                for (int pattern = 0; pattern < 4; pattern++)
                {
                    var clear = new System.Collections.Generic.HashSet<uint>();
                    var reduce = new System.Collections.Generic.HashSet<uint>();
                    for (int slot = 0; slot < capacity; slot++)
                    {
                        int page = capacity - 1 - slot;
                        uint flags = known | 10u;
                        if (pattern < 2)
                        {
                            if (slot % 5 == 0) flags |= 4u;
                            if (slot % 5 == 1) flags |= 1u << 15;
                            if (slot % 5 == 2) flags &= ~(1u << 14);
                            if (slot % 5 == 3) flags &= ~(1u << 16);
                        }
                        data[page] = new uint4(flags, (uint)slot + 1, 1, 8);
                        owner[slot] = pattern == 3 || (pattern == 0 && slot % 7 == 0) ? 0u : (uint)page + 1;
                        if (owner[slot] == 0) continue;
                        bool dirty = (flags & (4u | (1u << 15))) != 0;
                        if (dirty) clear.Add((uint)slot);
                        if (dirty || (flags & known) != known || pattern == 2) reduce.Add((uint)slot);
                    }
                    metadata.SetData(data); owners.SetData(owner);
                    shader.SetInt("_VSMPageOccupancySkipDisabled", pattern == 2 ? 1 : 0);
                    shader.Dispatch(build, 1, 1, 1);
                    args.GetData(dispatch); work.GetData(entries);
                    Assert.That(dispatch, Is.EqualTo(new uint[] { 16, 16, (uint)clear.Count, (uint)reduce.Count, 1, 1, 0x12345678 }));
                    for (int i = 0; i < dispatch[2]; i++) Assert.That(clear.Remove(entries[i]), Is.True);
                    for (int i = 0; i < dispatch[3]; i++) Assert.That(reduce.Remove(entries[capacity + i]), Is.True);
                    Assert.That(clear.Count + reduce.Count, Is.Zero);
                    Assert.That(entries[capacity * 2], Is.EqualTo(0x12345678u));
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void DynamicPageLifecycle_PreservesAllHiddenLayersOutsideEachDirtyPool(bool indirect, bool deferred)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute"));
            using var receiverMasks = new VirtualShadowMapReceiverMaskTestBuffers(shader);
            var upload = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"));
            var descriptor = new RenderTextureDescriptor(16, 16)
            {
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt,
                depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None,
                enableRandomWrite = true, msaaSamples = 1, dimension = TextureDimension.Tex2DArray, volumeDepth = 16,
            };
            var staticPool = new RenderTexture(descriptor);
            var dynamicPool = new RenderTexture(descriptor);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16, 16);
            using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16, 4);
            using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4096, 4);
            using var work = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 32, 4);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, 6, 4);
            try
            {
                Assert.That(staticPool.Create() && dynamicPool.Create(), Is.True);
                var data = new uint[4096];
                for (int i = 0; i < data.Length; i++) data[i] = math.asuint(.9f - .03f * (i / 256));
                input.SetData(data);
                int fill = upload.FindKernel("UploadTestPools");
                upload.SetBuffer(fill, "_TestStaticData", input); upload.SetBuffer(fill, "_TestDynamicData", input);
                upload.SetTexture(fill, "_TestStaticPool", staticPool); upload.SetTexture(fill, "_TestDynamicPool", dynamicPool);
                upload.Dispatch(fill, 2, 2, 16);
                const uint dynamicDirty = 1u << 15;
                uint[] dirty = { 4u, dynamicDirty, 0u, 4u | dynamicDirty };
                var meta = new uint4[16]; var owner = new uint[16];
                for (int i = 0; i < 4; i++) { meta[i] = new uint4(10u | dirty[i], (uint)i + 1, 1, 8); owner[i] = (uint)i + 1; }
                metadata.SetData(meta); owners.SetData(owner);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 16);
                shader.SetInt("_VSMPrototypePageTableEntryCount", 16);
                shader.SetInt("_VSMPrototypePageSize", 4);
                shader.SetInt("_VSMPrototypePhysicalPagesPerRow", 4);
                shader.SetInt("_VSMPageOccupancySkipDisabled", 0);
                using var requestFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, metadata.count, 4);
                var demands = new uint[metadata.count];
                requestFlags.SetData(demands);
                int build = shader.FindKernel("VSMBuildPageWorkLists");
                shader.SetBuffer(build, "_VSMPageRequestFlags", requestFlags);
                shader.SetBuffer(build, "_VSMPrototypePageMetadata", metadata);
                using var pageTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, metadata.count, 4);
                pageTable.SetData(new uint[metadata.count]);
                shader.SetBuffer(build, "_VSMPrototypePageTable", pageTable);
                shader.SetBuffer(build, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(build, "_VSMPageWorkListRW", work);
                shader.SetBuffer(build, "_VSMPageWorkDispatchArgsRW", args);
                shader.Dispatch(build, 1, 1, 1);
                var dispatch = new uint[6]; args.GetData(dispatch);
                Assert.That(dispatch, Is.EqualTo(new uint[] { 1, 1, 3, 4, 1, 1 }));
                // A stale work-list entry must not clear, scan or publish a page
                // that was deferred. Verify both direct and indirect kernels.
                if (deferred) { meta[3].x |= 1u << 17; metadata.SetData(meta); }
                int clear = shader.FindKernel(indirect ? "VSMClearPhysicalPagesIndirect" : "VSMPrototypeClearPhysicalPages");
                if (indirect) shader.SetBuffer(clear, "_VSMPageWorkList", work);
                shader.SetBuffer(clear, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(clear, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetTexture(clear, "_VSMPrototypeStaticPhysicalPageRW", staticPool);
                shader.SetTexture(clear, "_VSMPrototypeDynamicPhysicalPageRW", dynamicPool);
                if (indirect) shader.DispatchIndirect(clear, args, VirtualShadowMapPrototypeRuntime.ClearWorkArgsOffset);
                else shader.Dispatch(clear, 1, 1, 16);
                var readStatic = AsyncGPUReadback.Request(staticPool);
                var readDynamic = AsyncGPUReadback.Request(dynamicPool);
                readStatic.WaitForCompletion(); readDynamic.WaitForCompletion();
                Assert.That(readStatic.hasError || readDynamic.hasError, Is.False);
                Assert.That(readStatic.layerCount, Is.EqualTo(16));
                Assert.That(readDynamic.layerCount, Is.EqualTo(16));
                for (int layer = 0; layer < 16; layer++)
                {
                    var actualStatic = readStatic.GetData<uint>(layer); var actualDynamic = readDynamic.GetData<uint>(layer);
                    for (int pixel = 0; pixel < 256; pixel++)
                    {
                        int slot = pixel / 16 / 4 * 4 + pixel % 16 / 4;
                        uint depth = data[layer * 256 + pixel];
                        bool updated = slot < 4 && !(deferred && slot == 3);
                        Assert.That(actualStatic[pixel], Is.EqualTo(updated && (dirty[slot] & 4u) != 0 ? 0u : depth));
                        Assert.That(actualDynamic[pixel], Is.EqualTo(updated && (dirty[slot] & dynamicDirty) != 0 ? 0u : depth));
                    }
                }
                int occupancy = shader.FindKernel(indirect ? "VSMReducePageOccupancyIndirect" : "VSMPrototypeReducePageOccupancy");
                if (indirect) shader.SetBuffer(occupancy, "_VSMPageWorkList", work);
                shader.SetBuffer(occupancy, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(occupancy, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetTexture(occupancy, "_VSMPrototypeStaticPhysicalPage", staticPool);
                shader.SetTexture(occupancy, "_VSMPrototypeDynamicPhysicalPage", dynamicPool);
                shader.SetInt("_VSMPageOccupancySkipDisabled", 0);
                if (indirect) shader.DispatchIndirect(occupancy, args, VirtualShadowMapPrototypeRuntime.OccupancyWorkArgsOffset);
                else shader.Dispatch(occupancy, 16, 1, 1);
                int finalize = shader.FindKernel("VSMPrototypeFinalizeDirtyPages");
                shader.SetBuffer(finalize, "_VSMPrototypePageMetadata", metadata);
                shader.Dispatch(finalize, 1, 1, 1);
                metadata.GetData(meta);
                for (int i = 0; i < 4; i++)
                {
                    if (deferred && i == 3)
                    {
                        Assert.That(meta[i].x, Is.EqualTo(10u | dirty[i] | (1u << 17)));
                        continue;
                    }
                    Assert.That(meta[i].x & (4u | dynamicDirty), Is.Zero);
                    Assert.That(meta[i].x & ((1u << 14) | (1u << 16)), Is.EqualTo((1u << 14) | (1u << 16)));
                    Assert.That((meta[i].x & (1u << 12)) != 0, Is.EqualTo((dirty[i] & 4u) != 0));
                    Assert.That((meta[i].x & (1u << 13)) != 0, Is.EqualTo((dirty[i] & dynamicDirty) != 0));
                    Assert.That((meta[i].w & dynamicDirty) != 0, Is.EqualTo((dirty[i] & dynamicDirty) != 0));
                    Assert.That((meta[i].w & 4u) != 0, Is.EqualTo((dirty[i] & 4u) != 0),
                        "Dynamic-only redraw must not appear as static invalidation in debug snapshots.");
                }
                if (deferred) return;
                var preserved = (uint4[])meta.Clone();
                shader.Dispatch(build, 1, 1, 1);
                args.GetData(dispatch);
                Assert.That(dispatch, Is.EqualTo(new uint[] { 1, 1, 0, 0, 1, 1 }));
                if (indirect) shader.DispatchIndirect(occupancy, args, VirtualShadowMapPrototypeRuntime.OccupancyWorkArgsOffset);
                else shader.Dispatch(occupancy, 16, 1, 1);
                metadata.GetData(meta);
                Assert.That(meta, Is.EqualTo(preserved));
                // Disabling cached occupancy must visit clean pages as well;
                // re-enabling must rescan their now-unknown pools.
                shader.SetInt("_VSMPageOccupancySkipDisabled", 1);
                shader.Dispatch(build, 1, 1, 1);
                args.GetData(dispatch);
                Assert.That(dispatch[3], Is.EqualTo(4u));
                if (indirect) shader.DispatchIndirect(occupancy, args, VirtualShadowMapPrototypeRuntime.OccupancyWorkArgsOffset);
                else shader.Dispatch(occupancy, 16, 1, 1);
                metadata.GetData(meta);
                for (int i = 0; i < 4; i++) Assert.That(meta[i].x & ((1u << 14) | (1u << 16)), Is.Zero);
                shader.SetInt("_VSMPageOccupancySkipDisabled", 0);
                shader.Dispatch(build, 1, 1, 1);
                args.GetData(dispatch);
                Assert.That(dispatch[3], Is.EqualTo(4u));
                if (indirect) shader.DispatchIndirect(occupancy, args, VirtualShadowMapPrototypeRuntime.OccupancyWorkArgsOffset);
                else shader.Dispatch(occupancy, 16, 1, 1);
                metadata.GetData(meta);
                Assert.That(meta, Is.EqualTo(preserved));
                int invalidateAll = shader.FindKernel("VSMPrototypeMarkDynamicPagesDirty");
                shader.SetBuffer(invalidateAll, "_VSMPrototypePageMetadata", metadata);
                shader.Dispatch(invalidateAll, 1, 1, 1);
                metadata.GetData(meta);
                for (int i = 0; i < meta.Length; i++)
                    Assert.That(meta[i].x, Is.EqualTo(preserved[i].x | (i < 4 ? dynamicDirty : 0u)));
            }
            finally
            {
                Object.DestroyImmediate(shader); Object.DestroyImmediate(upload);
                Object.DestroyImmediate(staticPool); Object.DestroyImmediate(dynamicPool);
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeFrameState_TracksReadinessActivationAndFallback()
        {
            try
            {
                VirtualShadowMapPrototypeRuntime.BeginFrame();
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.FrameState,
                    Is.EqualTo(VirtualShadowMapPrototypeFrameState.Disabled));
                Assert.That(VirtualShadowMapPrototypeRuntime.IsFramePrepared, Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.IsFrameActive, Is.False);

                VirtualShadowMapPrototypeRuntime.MarkPrepared();
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.FrameState,
                    Is.EqualTo(VirtualShadowMapPrototypeFrameState.Prepared));
                Assert.That(VirtualShadowMapPrototypeRuntime.IsFramePrepared, Is.True);

                VirtualShadowMapPrototypeRuntime.MarkReady(requiresStaticRefresh: true);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.FrameState,
                    Is.EqualTo(VirtualShadowMapPrototypeFrameState.Refreshing));

                VirtualShadowMapPrototypeRuntime.MarkReady(requiresStaticRefresh: false);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.FrameState,
                    Is.EqualTo(VirtualShadowMapPrototypeFrameState.Cached));

                VirtualShadowMapPrototypeRuntime.MarkActive();
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.FrameState,
                    Is.EqualTo(VirtualShadowMapPrototypeFrameState.Active));
                Assert.That(VirtualShadowMapPrototypeRuntime.IsFrameActive, Is.True);

                VirtualShadowMapPrototypeRuntime.MarkFallback(
                    VirtualShadowMapPrototypeFallbackReason.VirtualTextureUnavailable);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.FrameState,
                    Is.EqualTo(VirtualShadowMapPrototypeFrameState.Fallback));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.LastFallbackReason,
                    Is.EqualTo(
                        VirtualShadowMapPrototypeFallbackReason.VirtualTextureUnavailable));
                Assert.That(VirtualShadowMapPrototypeRuntime.IsFramePrepared, Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.IsFrameActive, Is.False);
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.BeginFrame();
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeFeedback_IsConsumedOnlyByItsCameraAndCurrentFrame()
        {
            const ulong cameraA = 0x10000002aul, cameraB = 0x20000002aul;
            try
            {
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(cameraA, 10);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 10), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraB, 10), Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 11), Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 9), Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(cameraA, 11), Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(cameraB, 10), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(cameraA, 10), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(cameraA, 0), Is.True);
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(cameraB, 10);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 10), Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraB, 10), Is.True);
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(cameraA, 0);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 0), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 1), Is.False);
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(cameraA, -1);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedback, Is.False);
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(0ul, 10);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedback, Is.False);
            }
            finally { VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(0ul, -1); }
        }

        [Test]
        public void VirtualShadowMapPrototypeFeedback_ReleaseClearsOwnerAndFrame()
        {
            VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(42ul, 10);
            VirtualShadowMapPrototypeRuntime.ReleaseResources();
            Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedback, Is.False);
            Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(42ul, 11), Is.False);
            Assert.That(VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(42ul, 11), Is.True);
        }

        [Test]
        public void VirtualShadowMapPrototypeFeedback_WarmOwnershipChecksAllocateZeroBytes()
        {
            try
            {
                for (int iteration = 0; iteration < 16; iteration++)
                {
                    VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(42ul, 10);
                    VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(42ul, 10);
                    VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(43ul, 11);
                }
                int hits = 0;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < 4096; iteration++)
                {
                    VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(42ul, 10);
                    if (VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(42ul, 10)
                        && !VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(43ul, 10)
                        && !VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(42ul, 11)
                        && VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(43ul, 11))
                        hits++;
                }
                long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(hits, Is.EqualTo(4096));
                Assert.That(bytes, Is.Zero);
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(0ul, -1);
            }
        }

        [TestCase(0)]
        [TestCase(10)]
        public void VirtualShadowMapPrototypeFeedback_ResetRemovesOtherCameraDemandAndEvictionProtection(int frame)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            ComputeShader source = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
            Assert.That(source, Is.Not.Null);
            ComputeShader shader = Object.Instantiate(source);
            using var receiverMasks = new VirtualShadowMapReceiverMaskTestBuffers(shader);
            try
            {
                var tableData = new uint[8];
                tableData[1] = 1u;
                tableData[3] = 2u;
                var metadataData = new TestPageMetadata[8];
                var demand = new uint[8];
                metadataData[1] = new TestPageMetadata
                {
                    Flags = 58u, EncodedPhysicalPage = 1u,
                    LastRequestedFrame = 100u, Reserved = 123u,
                };
                metadataData[3] = new TestPageMetadata
                {
                    Flags = 54u, EncodedPhysicalPage = 2u, LastRequestedFrame = 99u,
                };
                demand[5] = 1u;
                metadataData[5].LastRequestedFrame = 100u;
                var ownerData = new uint[] { 2u, 4u };
                var counterData = new uint[] { 2u, 0u, 0u, 0u };
                using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, sizeof(uint));
                using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, Marshal.SizeOf<TestPageMetadata>());
                using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(uint));
                using var counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, sizeof(uint));
                table.SetData(tableData);
                metadata.SetData(metadataData);
                owners.SetData(ownerData);
                counters.SetData(counterData);
                int reset = shader.FindKernel("VSMPrototypeResetReceiverFeedback");
                int allocate = shader.FindKernel("VSMPrototypeAllocatePages");
                shader.SetInt("_VSMProjectionCount", 1);
                shader.SetInt("_VSMPrototypePageTableEntryCount", 8);
                using var pressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint) * 4);
                pressure.SetData(new uint4[3]);
                shader.SetVector("_VSMReceiverQuality", Vector4.zero);
                shader.SetBuffer(reset, "_VSMPagePressureRW", pressure);
                using var requestFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, sizeof(uint));
                demand[1] = 1u;
                requestFlags.SetData(demand);
                shader.SetBuffer(reset, "_VSMPageRequestFlags", requestFlags);
                shader.SetBuffer(reset, "_VSMPrototypePageMetadata", metadata);
                shader.Dispatch(reset, 1, 1, 1);
                metadata.GetData(metadataData);
                requestFlags.GetData(demand);
                Assert.That(demand, Is.EqualTo(new uint[8]));
                Assert.That(metadataData[1].Flags, Is.EqualTo(58u));
                Assert.That(metadataData[1].EncodedPhysicalPage, Is.EqualTo(1u));
                Assert.That(metadataData[1].Reserved, Is.EqualTo(123u));
                Assert.That(metadataData[3].Flags, Is.EqualTo(54u));
                Assert.That(metadataData[5].Flags, Is.Zero);
                for (int page = 0; page < metadataData.Length; page++)
                    Assert.That(metadataData[page].LastRequestedFrame, Is.Zero);

                // Only the new producer requests page 7. Old residents remain evictable,
                // even when a rewind produces feedback at frame zero.
                demand[7] = 1u;
                metadataData[7].LastRequestedFrame = (uint)frame;
                metadata.SetData(metadataData);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 2);
                shader.SetInt("_VSMPrototypeFeedbackFrameIndex", frame);
                shader.SetBuffer(allocate, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(allocate, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(allocate, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(allocate, "_VSMPrototypeAllocatorCounters", counters);
                DispatchVSMAllocation(shader, allocate, 8, metadata, demand);
                table.GetData(tableData);
                metadata.GetData(metadataData);
                owners.GetData(ownerData);
                counters.GetData(counterData);
                Assert.That(tableData[1], Is.Zero);
                Assert.That(tableData[3], Is.EqualTo(2u));
                Assert.That(tableData[5], Is.Zero);
                Assert.That(tableData[7], Is.EqualTo(1u));
                Assert.That(metadataData[7].Flags, Is.EqualTo(54u | (1u << 15)));
                Assert.That(ownerData, Is.EqualTo(new uint[] { 8u, 4u }));
                Assert.That(counterData, Is.EqualTo(new uint[] { 2u, 1u, 1u, 0u }));
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeStableFrameTransitions_AllocateZeroBytes()
        {
            const int warmupCount = 16;
            const int iterationCount = 4096;
            for (int iteration = 0; iteration < warmupCount; iteration++)
            {
                VirtualShadowMapPrototypeRuntime.BeginFrame();
                VirtualShadowMapPrototypeRuntime.MarkPrepared();
                VirtualShadowMapPrototypeRuntime.MarkReady(requiresStaticRefresh: false);
                VirtualShadowMapPrototypeRuntime.MarkActive();
                VirtualShadowMapPrototypeRuntime.MarkFallback(
                    VirtualShadowMapPrototypeFallbackReason.RecordPreparationFailed);
            }

            int activeCount = 0;
            int fallbackCount = 0;
            long allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                VirtualShadowMapPrototypeRuntime.BeginFrame();
                VirtualShadowMapPrototypeRuntime.MarkPrepared();
                VirtualShadowMapPrototypeRuntime.MarkReady(requiresStaticRefresh: false);
                VirtualShadowMapPrototypeRuntime.MarkActive();
                if (VirtualShadowMapPrototypeRuntime.IsFrameActive)
                    activeCount++;
                VirtualShadowMapPrototypeRuntime.MarkFallback(
                    VirtualShadowMapPrototypeFallbackReason.RecordPreparationFailed);
                if (VirtualShadowMapPrototypeRuntime.LastFallbackReason ==
                    VirtualShadowMapPrototypeFallbackReason.RecordPreparationFailed)
                {
                    fallbackCount++;
                }
            }
            long allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread()
                - allocatedBefore;

            Assert.That(activeCount, Is.EqualTo(iterationCount));
            Assert.That(fallbackCount, Is.EqualTo(iterationCount));
            Assert.That(allocatedBytes, Is.Zero);
            VirtualShadowMapPrototypeRuntime.BeginFrame();
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
                    VirtualShadowMapPrototypeRuntime.StaticPhysicalPage.rt.width,
                    Is.EqualTo(1024));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.StaticPhysicalPage.rt.height,
                    Is.EqualTo(1024));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.DynamicPhysicalPage.rt.width,
                    Is.EqualTo(1024));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.DynamicPhysicalPage.rt.height,
                    Is.EqualTo(1024));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RasterDepth.rt.volumeDepth,
                    Is.EqualTo(64));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.PageTableEntryCount,
                    Is.EqualTo(64));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity,
                    Is.EqualTo(64));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.PageTableUpload,
                    Has.All.EqualTo(0u));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.PageMetadata.count,
                    Is.EqualTo(64));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.PhysicalPageOwners.count,
                    Is.EqualTo(64));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.AllocatorCounters.count,
                    Is.EqualTo(4));

                RTHandle staticPool = VirtualShadowMapPrototypeRuntime.StaticPhysicalPage;
                RTHandle dynamicPool = VirtualShadowMapPrototypeRuntime.DynamicPhysicalPage;
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.EnsureResources(512, 4),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.StaticPhysicalPage,
                    Is.SameAs(staticPool));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.DynamicPhysicalPage,
                    Is.SameAs(dynamicPool));
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.ReleaseResources();
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeResources_StableEnsureAllocatesZeroBytes()
        {
            Assume.That(
                VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(),
                Is.True);

            const int warmupCount = 16;
            const int iterationCount = 256;
            try
            {
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.EnsureResources(512, 4),
                    Is.True);
                for (int iteration = 0; iteration < warmupCount; iteration++)
                    VirtualShadowMapPrototypeRuntime.EnsureResources(512, 4);

                var requestMasks = VirtualShadowMapPrototypeRuntime.PageReceiverMasks;
                var completedMasks = VirtualShadowMapPrototypeRuntime.PhysicalReceiverMasks;
                int readyCount = 0;
                long allocatedBefore = global::System.GC
                    .GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < iterationCount; iteration++)
                {
                    if (VirtualShadowMapPrototypeRuntime.EnsureResources(512, 4))
                        readyCount++;
                }
                long allocatedBytes = global::System.GC
                    .GetAllocatedBytesForCurrentThread() - allocatedBefore;

                Assert.That(readyCount, Is.EqualTo(iterationCount));
                Assert.That(allocatedBytes, Is.Zero);
                Assert.That(VirtualShadowMapPrototypeRuntime.PageReceiverMasks, Is.SameAs(requestMasks));
                Assert.That(VirtualShadowMapPrototypeRuntime.PhysicalReceiverMasks, Is.SameAs(completedMasks));
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.ReleaseResources();
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeMeshletPageShader_CompilesPageClippedVariants()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            Shader shader = Shader.Find(CSMShadowPass.ShadowCasterShaderName);
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            try
            {
                material.EnableKeyword("VIVID_VSM_CASTER");
                material.EnableKeyword("VIVID_VSM_PAGE_CASTER");
                for (int variant = 0; variant < 4; variant++)
                {
                    CoreUtils.SetKeyword(material, "_ALPHATEST_ON", (variant & 1) != 0);
                    CoreUtils.SetKeyword(material, "VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE",
                        (variant & 2) != 0);
                    ShaderUtil.CompilePass(material, 0, true);
                }
                foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
                    Assert.That(message.severity.ToString(), Is.Not.EqualTo("Error"), message.message);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [TestCase(0, 0, 256)]
        [TestCase(0, 1, 256)]
        [TestCase(0, 2, 256)]
        [TestCase(0, 3, 256)]
        [TestCase(1, 0, 256)]
        [TestCase(0, 3, 1)]
        [TestCase(0, 3, 63)]
        [TestCase(0, 3, 64)]
        [TestCase(0, 3, 65)]
        [TestCase(0, 0, 4096)]
        [TestCase(1, 2, 1024)]
        public void VirtualShadowMapPrototypeMeshletPages_ClipLargeRequestsToRelevantPages(
            int casterLayer, int dirtyMode, int physicalCapacity)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
            Assert.That(shader, Is.Not.Null);
            using var receiverMasks = new VirtualShadowMapReceiverMaskTestBuffers(shader);
            int prepare = shader.FindKernel("VSMPrototypePrepareMeshletPageRequests");
            int cull = shader.FindKernel("VSMPrototypeCullMeshletsToPages");
            const int pageCount = 256;
            const int sourceCapacity = 24;
            const int requestCapacity = sourceCapacity * 4;
            int listCount = (int)VividRendererListID.Count;
            var tableData = new uint[pageCount];
            var metadataData = new TestPageMetadata[pageCount];
            var ownerData = new uint[physicalCapacity];
            var relevant = new bool[pageCount];
            int relevantCount = 0;
            for (int page = 0; page < pageCount; page++)
            {
                // Full-pool stress plus sparse mappings and non-contiguous physical owners.
                bool allocated = dirtyMode == 3 || page % 7 != 0;
                bool dirty = dirtyMode == 3 || (dirtyMode == 1 && page == 9)
                    || (dirtyMode == 2 && page % 11 == 9);
                int physical = (page * 73 + 19) % pageCount;
                allocated &= physical < physicalCapacity;
                tableData[page] = allocated ? (uint)physical + 1u : 0u;
                if (allocated) ownerData[physical] = (uint)page + 1u;
                metadataData[page].Flags = allocated ? 10u | (dirty ? (casterLayer == 0 ? 4u : 1u << 15) : 0u) : 0u;
                relevant[page] = allocated && dirty;
                if (relevant[page]) relevantCount++;
            }
            var sourceData = new VividMeshletRenderRequestPacked[sourceCapacity];
            var sourceArgsData = new VividIndirectDrawArgs[listCount * 4];
            int sourceIndex = 0;
            for (int cascade = 0; cascade < 4; cascade++)
            {
                for (int list = 0; list < listCount; list++)
                {
                    if (list != 0 && list != listCount - 1) continue;
                    sourceArgsData[cascade * listCount + list] = new VividIndirectDrawArgs
                    {
                        InstanceCount = 3, StartInstance = (uint)sourceIndex,
                    };
                    for (uint meshlet = 0; meshlet < 3; meshlet++)
                        sourceData[sourceIndex++].MeshletID = meshlet;
                }
            }
            var meshletData = new[]
            {
                new VividMeshlet { BoundingSphere = new float4(0.5f, 0.5f, 0, 0.5f) },
                new VividMeshlet { BoundingSphere = new float4(0.1875f, 0.1875f, 0, 0.001f) },
                new VividMeshlet { BoundingSphere = new float4(0.25f, 0.25f, 0, 0.2f) },
            };
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pageCount, 4);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pageCount, 16);
            using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, physicalCapacity, 4);
            using var sources = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sourceCapacity, 8);
            using var sourceArgs = new GraphicsBuffer(GraphicsBuffer.Target.Raw, listCount * 16, 4);
            using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, requestCapacity + 4, 16);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments,
                listCount * 8, 4);
            const int headerSize = VirtualShadowMapPrototypeRuntime.RasterPageHeaderSize;
            using var rasterPages = new GraphicsBuffer(GraphicsBuffer.Target.Structured, physicalCapacity + headerSize, 4);
            using var instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, Marshal.SizeOf<VividInstanceData>());
            using var meshlets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, Marshal.SizeOf<VividMeshlet>());
            var requestData = new uint4[requestCapacity + 4];
            for (int i = 0; i < requestData.Length; i++) requestData[i] = new uint4(0xeeeeeeeeu);
            table.SetData(tableData); metadata.SetData(metadataData); owners.SetData(ownerData);
            sources.SetData(sourceData); sourceArgs.SetData(sourceArgsData); requests.SetData(requestData);
            instances.SetData(new[] { new VividInstanceData { ObjectToWorldMatrix = float4x4.identity } });
            meshlets.SetData(meshletData);
            shader.SetInt("_VSMProjectionCount", 4);
            shader.SetInt("_VSMPrototypePageTableEntryCount", pageCount);
            shader.SetInt("_VSMPrototypePhysicalPageCapacity", physicalCapacity);
            shader.SetInt("_VSMPrototypeCasterLayer", casterLayer);
            shader.SetInt("_VSMPrototypeSourceRequestsPerCascadeCapacity", sourceCapacity / 4);
            shader.SetInt("_VSMPrototypeVirtualResolution", 1024);
            shader.SetInt("_VSMPrototypePagesPerAxis", 8);
            shader.SetInt("_VSMPrototypePageSize", 128);
            shader.SetInt("_InstanceDataCount", 1); shader.SetInt("_MeshletCount", 3);
            using var projections = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 160);
            var projectionData = new VirtualShadowMapProjection[4];
            for (int i = 0; i < projectionData.Length; i++)
                projectionData[i].WorldToShadow = Matrix4x4.identity;
            projections.SetData(projectionData);
            shader.SetBuffer(cull, "_VSMProjections", projections);
            foreach (int kernel in new[] { prepare, cull })
            {
                shader.SetBuffer(kernel, "_VSMPrototypePageTable", table);
                shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(kernel, "_VSMPrototypeSourceMeshletIndirectArgs", sourceArgs);
                shader.SetBuffer(kernel, "_VSMPrototypeMeshletPageIndirectArgs", args);
                shader.SetBuffer(kernel, "_VSMPrototypeMeshletRasterPages", rasterPages);
            }
            shader.SetBuffer(prepare, "_VSMPrototypePhysicalPageOwners", owners);
            shader.SetBuffer(cull, "_VSMPrototypeSourceMeshletRequests", sources);
            shader.SetBuffer(cull, "_VSMPrototypeMeshletPageRequests", requests);
            shader.SetBuffer(cull, "_InstanceData", instances); shader.SetBuffer(cull, "_Meshlets", meshlets);
            shader.Dispatch(prepare, 1, 1, 1);
            shader.Dispatch(cull, 1, 4, listCount);
            var argsData = new VividIndirectDrawArgs[listCount * 2];
            var rasterPageData = new uint[physicalCapacity + headerSize];
            requests.GetData(requestData); args.GetData(argsData); rasterPages.GetData(rasterPageData);
            var seenPages = new bool[pageCount];
            int expectedOffset = headerSize;
            int maxLevelCount = 0;
            for (int level = 0; level < VirtualShadowMapClipmapLayout.MaxLevels; level++)
            {
                int expectedCount = 0;
                for (int page = level * 64; page < (level + 1) * 64 && page < pageCount; page++)
                    if (relevant[page]) expectedCount++;
                Assert.That(rasterPageData[1 + level], Is.EqualTo(expectedCount));
                Assert.That(rasterPageData[1 + VirtualShadowMapClipmapLayout.MaxLevels + level], Is.EqualTo(expectedOffset));
                for (int i = 0; i < expectedCount; i++)
                {
                    uint page = rasterPageData[expectedOffset + i];
                    Assert.That(page / 64u, Is.EqualTo(level));
                    Assert.That(relevant[page] && !seenPages[page], Is.True);
                    seenPages[page] = true;
                }
                maxLevelCount = Mathf.Max(maxLevelCount, expectedCount);
                expectedOffset += expectedCount;
            }
            Assert.That(expectedOffset, Is.EqualTo(headerSize + relevantCount));
            Assert.That(rasterPageData[0], Is.EqualTo(maxLevelCount));
            var actual = new int[listCount * 3 * pageCount];
            for (int command = 0; command < argsData.Length; command++)
            {
                VividIndirectDrawArgs draw = argsData[command];
                bool large = command >= listCount;
                Assert.That(draw.InstanceCount, Is.LessThanOrEqualTo((uint)(sourceCapacity * pageCount)));
                for (uint local = 0; local < draw.InstanceCount; local++)
                {
                    uint address = large ? draw.StartInstance - 1u - local / rasterPageData[0]
                        : draw.StartInstance + local;
                    Assert.That(address, Is.LessThan(requestCapacity));
                    uint4 request = requestData[address];
                    uint page = request.z;
                    if (large)
                    {
                        uint level = request.w / 64u;
                        uint ordinal = local % rasterPageData[0];
                        if (ordinal >= rasterPageData[1u + level]) continue;
                        page = rasterPageData[rasterPageData[1u + VirtualShadowMapClipmapLayout.MaxLevels + level] + ordinal];
                    }
                    if (large && (page / 64 != request.w / 64
                        || page % 8 < request.z % 8 || page % 8 > request.w % 8
                        || page % 64 / 8 < request.z % 64 / 8 || page % 64 / 8 > request.w % 64 / 8))
                        continue;
                    Assert.That(request.x, Is.Zero);
                    Assert.That(request.y, Is.LessThan(3));
                    Assert.That(page, Is.LessThan(pageCount));
                    actual[((command % listCount) * 3 + (int)request.y) * pageCount + (int)page]++;
                }
            }
            for (int list = 0; list < listCount; list++)
            for (int meshlet = 0; meshlet < 3; meshlet++)
            for (int page = 0; page < pageCount; page++)
            {
                bool covered = meshlet == 0 || (meshlet == 1 ? page % 64 == 9
                    : page % 8 <= 3 && page % 64 / 8 <= 3);
                int expected = (list == 0 || list == listCount - 1) && covered && relevant[page] ? 1 : 0;
                Assert.That(actual[(list * 3 + meshlet) * pageCount + page], Is.EqualTo(expected));
            }
            for (int i = requestCapacity; i < requestData.Length; i++)
                Assert.That(requestData[i], Is.EqualTo(new uint4(0xeeeeeeeeu)));
        }

        [Test]
        public void VirtualShadowMapPrototypeMeshletPageBuffers_GrowOnceAndThenAllocateZeroBytes()
        {
            Assume.That(SystemInfo.supportsComputeShaders, Is.True);

            const int sourceRequestCapacity = 64;
            const int warmupCount = 16;
            const int iterationCount = 256;
            try
            {
                Assert.That(
                    VirtualShadowMapPrototypeRuntime
                        .EnsureMeshletPageRequestCapacity(
                            sourceRequestCapacity),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.MeshletPageRequests.count,
                    Is.EqualTo(
                        sourceRequestCapacity
                        * VirtualShadowMapPrototypeRuntime
                            .MaxPageRequestsPerMeshlet));
                GraphicsBuffer requestBuffer =
                    VirtualShadowMapPrototypeRuntime.MeshletPageRequests;
                GraphicsBuffer argsBuffer =
                    VirtualShadowMapPrototypeRuntime.MeshletPageIndirectArgs;
                GraphicsBuffer rasterPagesBuffer = VirtualShadowMapPrototypeRuntime.MeshletRasterPages;
                Assert.That(argsBuffer.count, Is.EqualTo((int)VividRendererListID.Count * 8));
                Assert.That(rasterPagesBuffer.count, Is.EqualTo(
                    VirtualShadowMapPrototypeRuntime.MaxPhysicalPageCount
                    + VirtualShadowMapPrototypeRuntime.RasterPageHeaderSize));

                for (int iteration = 0; iteration < warmupCount; iteration++)
                {
                    VirtualShadowMapPrototypeRuntime
                        .EnsureMeshletPageRequestCapacity(
                            sourceRequestCapacity);
                }

                int readyCount = 0;
                long allocatedBefore = global::System.GC
                    .GetAllocatedBytesForCurrentThread();
                for (int iteration = 0;
                     iteration < iterationCount;
                     iteration++)
                {
                    if (VirtualShadowMapPrototypeRuntime
                        .EnsureMeshletPageRequestCapacity(
                            sourceRequestCapacity))
                    {
                        readyCount++;
                    }
                }
                long allocatedBytes = global::System.GC
                    .GetAllocatedBytesForCurrentThread() - allocatedBefore;

                Assert.That(readyCount, Is.EqualTo(iterationCount));
                Assert.That(allocatedBytes, Is.Zero);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.MeshletPageRequests,
                    Is.SameAs(requestBuffer));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.MeshletPageIndirectArgs,
                    Is.SameAs(argsBuffer));
                Assert.That(VirtualShadowMapPrototypeRuntime.MeshletRasterPages,
                    Is.SameAs(rasterPagesBuffer));
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.ReleaseResources();
            }
            Assert.That(VirtualShadowMapPrototypeRuntime.MeshletRasterPages, Is.Null);
        }

        [Test]
        public void VirtualShadowMapPrototypeCache_ReusesMatchingStableState()
        {
            VirtualShadowMapPrototypeCacheKey key = CreateCacheKey();
            int initialHitCount = VirtualShadowMapPrototypeRuntime.StaticCacheHitCount;
            int initialRefreshCount = VirtualShadowMapPrototypeRuntime.StaticCacheRefreshCount;

            try
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresStaticCacheRefresh(key),
                    Is.True);

                VirtualShadowMapPrototypeRuntime.CommitStaticCache(key);

                Assert.That(VirtualShadowMapPrototypeRuntime.IsCacheValid, Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresStaticCacheRefresh(key),
                    Is.False);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.TryUseCachedStaticPages(key),
                    Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.LastFrameUsedCache, Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.StaticCacheHitCount,
                    Is.EqualTo(initialHitCount + 1));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.StaticCacheRefreshCount,
                    Is.EqualTo(initialRefreshCount + 1));
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeStableStaticCacheCheck_AllocatesZeroBytes()
        {
            const int iterationCount = 4096;
            VirtualShadowMapPrototypeCacheKey key = CreateCacheKey();
            VirtualShadowMapPrototypeRuntime.InvalidateCache();
            VirtualShadowMapPrototypeRuntime.CommitStaticCache(key);

            for (int iteration = 0; iteration < 16; iteration++)
            {
                VirtualShadowMapPrototypeRuntime.TryUseCachedStaticPages(CreateCacheKey());
                key.GetHashCode();
            }

            int hitCount = 0;
            long allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                VirtualShadowMapPrototypeCacheKey currentKey = CreateCacheKey();
                currentKey.GetHashCode();
                if (VirtualShadowMapPrototypeRuntime.TryUseCachedStaticPages(currentKey))
                    hitCount++;
            }
            long allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread()
                - allocatedBefore;

            Assert.That(hitCount, Is.EqualTo(iterationCount));
            Assert.That(allocatedBytes, Is.Zero);
            VirtualShadowMapPrototypeRuntime.InvalidateCache();
        }

        [Test]
        public void VirtualShadowMapPrototypeDynamicRefresh_DoesNotInvalidateStaticCache()
        {
            VirtualShadowMapPrototypeCacheKey key = CreateCacheKey();
            int initialDynamicRefreshCount =
                VirtualShadowMapPrototypeRuntime.DynamicRefreshCount;
            try
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
                VirtualShadowMapPrototypeRuntime.CommitStaticCache(key);

                VirtualShadowMapPrototypeRuntime.MarkDynamicPoolRefreshed();

                Assert.That(
                    VirtualShadowMapPrototypeRuntime.DynamicRefreshCount,
                    Is.EqualTo(initialDynamicRefreshCount + 1));
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresStaticCacheRefresh(key),
                    Is.False);
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
            }
        }

        [Test]
        public void ShadowCasterPreparation_InactivePathAllocatesZeroBytesAfterWarmup()
        {
            using var frame = new ContextContainer();
            frame.GetOrCreate<VividShadowData>();
            var pass = new CSMShadowPass();
            try
            {
                for (int i = 0; i < 32; i++) pass.Prepare(frame);
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++) pass.Prepare(frame);
                long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(bytes, Is.Zero);
            }
            finally { pass.Dispose(); }
        }

        [Test]
        public void UnityDynamicCache_InvalidatesCommittedOldAndCurrentCoverageIncludingRemoval()
        {
            var oldBounds = new Bounds(Vector3.zero, Vector3.one * 2);
            var movedBounds = new Bounds(Vector3.right * 10, Vector3.one * 2);
            var laterBounds = new Bounds(Vector3.right * 20, Vector3.one * 2);
            try
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
                var first = VirtualShadowMapPrototypeRuntime.BuildDynamicInvalidationBounds(default, true, oldBounds);
                Assert.That(first.Length, Is.EqualTo(1));
                VirtualShadowMapPrototypeRuntime.CommitDynamicUnityBounds(true, oldBounds);
                var stationary = VirtualShadowMapPrototypeRuntime.BuildDynamicInvalidationBounds(default, true, oldBounds);
                Assert.That(stationary.Length, Is.EqualTo(1), "Unjournaled material/mesh changes must still redraw inside current coverage.");
                var moved = VirtualShadowMapPrototypeRuntime.BuildDynamicInvalidationBounds(default, true, movedBounds);
                Assert.That(moved.Length, Is.EqualTo(2));
                Assert.That(moved[0].BoundsMin.xyz, Is.EqualTo((float3)oldBounds.min));
                Assert.That(moved[1].BoundsMax.xyz, Is.EqualTo((float3)movedBounds.max));
                // Preparing a frame is not a commit: an aborted pass must retain old shadows' coverage.
                var later = VirtualShadowMapPrototypeRuntime.BuildDynamicInvalidationBounds(default, true, laterBounds);
                Assert.That(later[0].BoundsMin.xyz, Is.EqualTo((float3)oldBounds.min));
                VirtualShadowMapPrototypeRuntime.CommitDynamicUnityBounds(true, laterBounds);
                var removed = VirtualShadowMapPrototypeRuntime.BuildDynamicInvalidationBounds(default, false, default);
                Assert.That(removed.Length, Is.EqualTo(1));
                Assert.That(removed[0].BoundsMax.xyz, Is.EqualTo((float3)laterBounds.max));
                VirtualShadowMapPrototypeRuntime.CommitDynamicUnityBounds(false, default);
                Assert.That(VirtualShadowMapPrototypeRuntime.BuildDynamicInvalidationBounds(default, false, default).Length, Is.Zero);
            }
            finally { VirtualShadowMapPrototypeRuntime.ReleaseResources(); }
        }

        [Test]
        public void UnityDynamicCache_MergesMeshletJournalAndResetsCoverageWithCache()
        {
            using var journal = new Unity.Collections.NativeArray<VividStaticShadowInvalidationBounds>(
                new[] { new VividStaticShadowInvalidationBounds { BoundsMin = new float4(-3), BoundsMax = new float4(3) } },
                Unity.Collections.Allocator.Temp);
            var bounds = new Bounds(Vector3.one * 10, Vector3.one);
            try
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
                var merged = VirtualShadowMapPrototypeRuntime.BuildDynamicInvalidationBounds(journal, true, bounds);
                Assert.That(merged.Length, Is.EqualTo(2));
                Assert.That(merged[0].BoundsMin, Is.EqualTo(journal[0].BoundsMin));
                VirtualShadowMapPrototypeRuntime.CommitDynamicUnityBounds(true, bounds);
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
                Assert.That(VirtualShadowMapPrototypeRuntime.BuildDynamicInvalidationBounds(default, false, default).Length, Is.Zero);
            }
            finally { VirtualShadowMapPrototypeRuntime.ReleaseResources(); }
        }

        [Test]
        public void UnityDynamicCache_WarmMergeAndUploadAllocateZeroBytes()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var bounds = new Bounds(Vector3.zero, Vector3.one);
            var moved = new Bounds(Vector3.one * 3, Vector3.one);
            try
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
                VirtualShadowMapPrototypeRuntime.CommitDynamicUnityBounds(true, bounds);
                for (int i = 0; i < 32; i++)
                    VirtualShadowMapPrototypeRuntime.UploadDynamicInvalidationBounds(
                        VirtualShadowMapPrototypeRuntime.BuildDynamicInvalidationBounds(default, true, moved));
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++)
                    VirtualShadowMapPrototypeRuntime.UploadDynamicInvalidationBounds(
                        VirtualShadowMapPrototypeRuntime.BuildDynamicInvalidationBounds(default, true, moved));
                long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(bytes, Is.Zero);
            }
            finally { VirtualShadowMapPrototypeRuntime.ReleaseResources(); }
        }

        [Test]
        public void DynamicCache_WarmUploadsAndKeyChecksAllocateZeroBytes()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            using var bounds = new Unity.Collections.NativeArray<VividStaticShadowInvalidationBounds>(2, Unity.Collections.Allocator.Temp);
            var key = CreateCacheKey();
            try
            {
                for (int i = 0; i < 32; i++)
                {
                    VirtualShadowMapPrototypeRuntime.UploadDynamicInvalidationBounds(bounds);
                    VirtualShadowMapPrototypeRuntime.CommitDynamicCache(key, 7u, false);
                    VirtualShadowMapPrototypeRuntime.RequiresFullDynamicCacheRefresh(key, false);
                }
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++)
                {
                    VirtualShadowMapPrototypeRuntime.UploadDynamicInvalidationBounds(bounds);
                    VirtualShadowMapPrototypeRuntime.CommitDynamicCache(key, 7u, false);
                    VirtualShadowMapPrototypeRuntime.RequiresFullDynamicCacheRefresh(key, false);
                }
                long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(bytes, Is.Zero);
            }
            finally { VirtualShadowMapPrototypeRuntime.ReleaseResources(); }
        }

        [Test]
        public void DynamicCache_InvalidatesChangedConfigurationAndLeavingUntrackedCasters()
        {
            var key = CreateCacheKey();
            try
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresFullDynamicCacheRefresh(key, false), Is.True);
                VirtualShadowMapPrototypeRuntime.CommitDynamicCache(key, 7u, false);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresFullDynamicCacheRefresh(key, false), Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.DynamicShadowRevision, Is.EqualTo(7u));
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresFullDynamicCacheRefresh(CreateCacheKey(staticShadowRevision: 8u), false), Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresFullDynamicCacheRefresh(CreateCacheKey(cameraEntityId: 43ul), false), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresFullDynamicCacheRefresh(CreateCacheKey(textureBindingRevision: 12u), false), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresFullDynamicCacheRefresh(key, true), Is.True);
                VirtualShadowMapPrototypeRuntime.CommitDynamicCache(key, 8u, true);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresFullDynamicCacheRefresh(key, false), Is.True);
                VirtualShadowMapPrototypeRuntime.CommitDynamicCache(key, 8u, false);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresFullDynamicCacheRefresh(key, false), Is.False);
            }
            finally { VirtualShadowMapPrototypeRuntime.InvalidateCache(); }
        }

        [Test]
        public void VirtualShadowMapPrototypeStaticCache_LocalizesContentButFullyInvalidatesConfiguration()
        {
            VirtualShadowMapPrototypeCacheKey key = CreateCacheKey();

            try
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
                VirtualShadowMapPrototypeRuntime.CommitStaticCache(key);

                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresStaticCacheRefresh(
                        CreateCacheKey(staticShadowRevision: 8u)),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresFullStaticCacheRefresh(
                        CreateCacheKey(staticShadowRevision: 8u)),
                    Is.False);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresStaticCacheRefresh(
                        CreateCacheKey(textureBindingRevision: 12u)),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresFullStaticCacheRefresh(
                        CreateCacheKey(textureBindingRevision: 12u)),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresStaticCacheRefresh(
                        CreateCacheKey(cameraEntityId: 43ul)),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresFullStaticCacheRefresh(
                        CreateCacheKey(cameraEntityId: 43ul)),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresStaticCacheRefresh(
                        CreateCacheKey(primitiveSceneToken: 2u)),
                    Is.True);

                Matrix4x4 movedCascade = Matrix4x4.identity;
                movedCascade.m03 = 1.0f / 2048.0f;
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresStaticCacheRefresh(
                        CreateCacheKey(cascade0: movedCascade)),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.RequiresFullStaticCacheRefresh(
                        CreateCacheKey(cascade0: movedCascade)),
                    Is.True);
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeCache_FullyInvalidatesChangedCullingInputs()
        {
            VirtualShadowMapPrototypeCacheKey key = CreateCacheKey();
            var contexts = new VividGPUCullingContext[4];
            string[] inputNames =
            {
                "Camera mask", "LOD projection", "LOD position", "LOD up", "LOD right",
                "Pixel width", "Pixel height", "Cull projection", "Cull view", "Cull position",
                "Receiver sphere", "Frustum planes", "Pass mask", "Perspective",
            };
            try
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
                VirtualShadowMapPrototypeRuntime.CommitStaticCache(key);
                for (int input = 0; input < inputNames.Length; input++)
                {
                    for (int cascade = 0; cascade < (input < 7 ? 1 : 4); cascade++)
                    {
                        System.Array.Copy(s_CacheCullingContexts, contexts, contexts.Length);
                        VividGPULODSelectionContext lod = CreateCacheLODContext();
                        VividGPUCullingContext culling = contexts[cascade];
                        int mask = -1;
                        switch (input)
                        {
                            case 0: mask = 1; break;
                            case 1: lod.ViewProjectionMatrix.c0.x += 0.01f; break;
                            case 2: lod.CameraPosition.x += 0.01f; break;
                            case 3: lod.CameraUp.x += 0.01f; break;
                            case 4: lod.CameraRight.y += 0.01f; break;
                            case 5: lod.ScreenSizePixels.x += 1.0f; break;
                            case 6: lod.ScreenSizePixels.y += 1.0f; break;
                            case 7: culling.ViewProjectionMatrix.c0.x += 0.01f; break;
                            case 8: culling.ViewMatrix.c0.x += 0.01f; break;
                            case 9: culling.CameraPosition.x += 0.01f; break;
                            case 10: culling.CullingSphereLS.w += 0.01f; break;
                            case 11: culling = CreateCacheCullingContext(cullNearPlane: true); break;
                            case 12: culling.PassMask = (int)VividInstancePassMask.Main; break;
                            case 13: culling.CameraIsPerspective = 1; break;
                        }
                        contexts[cascade] = culling;
                        VirtualShadowMapPrototypeCacheKey changed = CreateCacheKey(
                            cameraCullingMask: mask, lodSelectionContext: lod, cullingContexts: contexts);

                        Assert.That(changed.IsValid, Is.True, inputNames[input]);
                        Assert.That(key.Equals(changed), Is.False, inputNames[input]);
                        Assert.That(VirtualShadowMapPrototypeRuntime.RequiresStaticCacheRefresh(changed),
                            Is.True, inputNames[input]);
                        Assert.That(VirtualShadowMapPrototypeRuntime.RequiresFullStaticCacheRefresh(changed),
                            Is.True, inputNames[input]);
                        Assert.That(VirtualShadowMapPrototypeRuntime.TryUseCachedStaticPages(changed),
                            Is.False, inputNames[input]);
                    }
                }
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeCache_CopiesCullingStateAndIgnoresDispatchOffsets()
        {
            VividGPUCullingContext[] contexts = CreateCacheCullingContexts();
            VirtualShadowMapPrototypeCacheKey original = CreateCacheKey(cullingContexts: contexts);
            contexts[0].CullingSphereLS.w += 1.0f;
            Assert.That(original.Equals(CreateCacheKey()), Is.True);
            Assert.That(original.GetHashCode(), Is.EqualTo(CreateCacheKey().GetHashCode()));
            Assert.That(original.Equals(CreateCacheKey(cullingContexts: contexts)), Is.False);

            System.Array.Copy(s_CacheCullingContexts, contexts, contexts.Length);
            contexts[0].BaseStartInstance = 100u;
            contexts[1].MeshletListBuildJobsOffset = 200u;
            contexts[2].MeshletRenderRequestsOffset = 300u;
            contexts[3].Padding0 = 1u;
            VividGPULODSelectionContext lod = CreateCacheLODContext();
            lod.Padding0 = 1u;
            VirtualShadowMapPrototypeCacheKey offsetsOnly = CreateCacheKey(
                lodSelectionContext: lod, cullingContexts: contexts);
            Assert.That(original.Equals(offsetsOnly), Is.True);
            Assert.That(original.GetHashCode(), Is.EqualTo(offsetsOnly.GetHashCode()));

            VirtualShadowMapPrototypeCacheKey singleCascade = CreateCacheKey(
                cascadeCount: 1, cullingContexts: contexts);
            contexts[3].CullingSphereLS.w = float.NaN;
            VirtualShadowMapPrototypeCacheKey inactiveChanged = CreateCacheKey(
                cascadeCount: 1, cullingContexts: contexts);
            Assert.That(inactiveChanged.IsValid, Is.True);
            Assert.That(singleCascade.Equals(inactiveChanged), Is.True);
            Assert.That(singleCascade.GetHashCode(), Is.EqualTo(inactiveChanged.GetHashCode()));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void VirtualShadowMapPrototypeCache_RejectsNonFiniteCullingInputs(int input)
        {
            VividGPULODSelectionContext lod = CreateCacheLODContext();
            VividGPUCullingContext[] contexts = CreateCacheCullingContexts();
            switch (input)
            {
                case 0: lod.ViewProjectionMatrix.c0.x = float.NaN; break;
                case 1: lod.CameraPosition.x = float.NaN; break;
                case 2: lod.CameraUp.x = float.NaN; break;
                case 3: lod.CameraRight.x = float.NaN; break;
                case 4: lod.ScreenSizePixels.y = float.PositiveInfinity; break;
                case 5: contexts[0].ViewProjectionMatrix.c0.x = float.NaN; break;
                case 6: contexts[1].ViewMatrix.c0.x = float.NaN; break;
                case 7: contexts[2].CameraPosition.x = float.NaN; break;
                case 8: contexts[3].CullingSphereLS.w = float.NaN; break;
            }
            VirtualShadowMapPrototypeCacheKey key = CreateCacheKey(
                lodSelectionContext: lod, cullingContexts: contexts);
            Assert.That(key.IsValid, Is.False);
            Assert.That(VirtualShadowMapPrototypeRuntime.RequiresFullStaticCacheRefresh(key), Is.True);
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
                    VirtualShadowMapPrototypeRuntime.RequiresStaticCacheRefresh(key),
                    Is.True);

                VirtualShadowMapPrototypeRuntime.CommitStaticCache(key);

                Assert.That(VirtualShadowMapPrototypeRuntime.IsCacheValid, Is.False);
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.InvalidateCache();
            }
        }

        [Test]
        public void VirtualShadowMapPrototypeCacheKey_AcceptsEmptyStaticPool()
        {
            VirtualShadowMapPrototypeCacheKey key = CreateCacheKey(
                primitiveSceneToken: 0u,
                staticShadowRevision: 0u);

            Assert.That(key.IsValid, Is.True);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TestPageMetadata
        {
            public uint Flags;
            public uint EncodedPhysicalPage;
            public uint LastRequestedFrame;
            public uint Reserved;
        }

        private static VividGPULODSelectionContext CreateCacheLODContext()
        {
            return new VividGPULODSelectionContext
            {
                ViewProjectionMatrix = float4x4.identity,
                CameraPosition = new float4(0, 0, 0, 1),
                CameraUp = new float4(0, 1, 0, 0),
                CameraRight = new float4(1, 0, 0, 0),
                ScreenSizePixels = new float2(1920, 1080),
            };
        }

        private static VividGPUCullingContext CreateCacheCullingContext(bool cullNearPlane = false)
        {
            VividGPUDrivenCullingContextUtility.Build(
                Matrix4x4.identity, Matrix4x4.identity,
                Vector3.zero, Vector3.right, Vector3.up, new Vector2(2048, 2048),
                isPerspective: false, passMask: VividInstancePassMask.Shadows,
                cullingSphereWS: new Vector4(0, 0, 0, 10), cullAgainstNearPlane: cullNearPlane,
                out VividGPUCullingContext context, out _);
            return context;
        }

        private static VividGPUCullingContext[] CreateCacheCullingContexts()
        {
            var contexts = new VividGPUCullingContext[4];
            for (int cascade = 0; cascade < contexts.Length; cascade++)
                contexts[cascade] = CreateCacheCullingContext();
            return contexts;
        }

        private static VirtualShadowMapPrototypeCacheKey CreateCacheKey(
            ulong cameraEntityId = 42ul,
            uint primitiveSceneToken = 1u,
            uint staticShadowRevision = 7u,
            uint textureBindingRevision = 11u,
            Matrix4x4? cascade0 = null,
            int cameraCullingMask = -1,
            VividGPULODSelectionContext? lodSelectionContext = null,
            VividGPUCullingContext[] cullingContexts = null,
            int cascadeCount = 4)
        {
            return new VirtualShadowMapPrototypeCacheKey(
                cameraEntityId,
                primitiveSceneToken: primitiveSceneToken,
                staticShadowRevision: staticShadowRevision,
                textureBindingRevision: textureBindingRevision,
                cascadeCount: cascadeCount,
                virtualResolution: 2048,
                forcedMeshLODNodeDepth: 0,
                meshLODErrorThreshold: 1.0f,
                slopeScaleDepthBias: 2.0f,
                shadowCasterState: Vector4.zero,
                cameraCullingMask: cameraCullingMask,
                lodSelectionContext: lodSelectionContext ?? CreateCacheLODContext(),
                cullingContexts: cullingContexts ?? s_CacheCullingContexts,
                cascade0: cascade0 ?? Matrix4x4.identity,
                cascade1: Matrix4x4.identity,
                cascade2: Matrix4x4.identity,
                cascade3: Matrix4x4.identity);
        }
    }
}
