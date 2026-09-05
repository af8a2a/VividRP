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
                Is.EqualTo(VirtualShadowMapPrototypeRuntime.MaxPhysicalPageCount));
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
            int kernel = shader.FindKernel("VSMPrototypeAllocatePages");
            var pageTableData = new uint[8];
            var metadataData = new TestPageMetadata[8];
            metadataData[1].Flags = 1u;
            metadataData[1].LastRequestedFrame = 7u;
            metadataData[3].Flags = 1u;
            metadataData[3].LastRequestedFrame = 7u;
            metadataData[7].Flags = 1u;
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
            shader.Dispatch(kernel, 1, 1, 1);

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
            try
            {
                const uint requested = 1u;
                const uint allocatedDirty = 2u | 4u | 16u | 32u;
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
                    // Stale feedback must neither allocate nor prevent eviction.
                    metadataData[6].LastRequestedFrame = feedbackFrame - 1u;
                    for (int page = 0; page < metadataData.Length; page++)
                    {
                        metadataData[page].Flags |= requested;
                        if ((frame.Requests & (1u << page)) == 0u)
                            continue;
                        metadataData[page].LastRequestedFrame = feedbackFrame;
                        requestCount++;
                    }

                    metadata.SetData(metadataData);
                    shader.SetInt("_VSMPrototypeFeedbackFrameIndex", (int)feedbackFrame);
                    shader.Dispatch(allocateKernel, 1, 1, 1);
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
                        uint snapshot = metadataData[page].Reserved;
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
            try
            {
                const uint allocatedCached = (1u << 1) | (1u << 3);
                const uint allocatedDirty = (1u << 1) | (1u << 2);
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

                int kernel = shader.FindKernel("VSMPrototypeInvalidateStaticPages");
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
            finally
            {
                Object.DestroyImmediate(shader);
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
        public void VirtualShadowMapPrototypeFeedback_IsConsumedOnlyByItsCameraAndFollowingFrame()
        {
            const ulong cameraA = 0x10000002aul;
            const ulong cameraB = 0x20000002aul;
            try
            {
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(cameraA, 10);

                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedback, Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 11),
                    Is.True);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraB, 11),
                    Is.False);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 10),
                    Is.False);
                Assert.That(
                    VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 12),
                    Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(cameraA, 11), Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(cameraB, 10), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(cameraA, 10), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(cameraA, 0), Is.True);

                // Same-frame writes replace the owner, not just the frame number.
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(cameraB, 10);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 11), Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraB, 11), Is.True);
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(cameraA, 11);
                VirtualShadowMapPrototypeRuntime.BeginFrame();
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 12), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraB, 12), Is.False);

                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(cameraA, 0);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 1), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(cameraA, 0), Is.False);
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(cameraA, -1);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedback, Is.False);
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(0ul, 10);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverFeedback, Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(cameraA, 11), Is.True);
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(0ul, -1);
            }
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
                    VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(42ul, 11);
                    VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(43ul, 11);
                }
                int hits = 0;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < 4096; iteration++)
                {
                    VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(42ul, 10);
                    if (VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(42ul, 11)
                        && !VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(43ul, 11)
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
            try
            {
                var tableData = new uint[8];
                tableData[1] = 1u;
                tableData[3] = 2u;
                var metadataData = new TestPageMetadata[8];
                metadataData[1] = new TestPageMetadata
                {
                    Flags = 59u, EncodedPhysicalPage = 1u,
                    LastRequestedFrame = 100u, Reserved = 123u,
                };
                metadataData[3] = new TestPageMetadata
                {
                    Flags = 54u, EncodedPhysicalPage = 2u, LastRequestedFrame = 99u,
                };
                metadataData[5].Flags = 1u;
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
                shader.SetBuffer(reset, "_VSMPrototypePageMetadata", metadata);
                shader.Dispatch(reset, 1, 1, 1);
                metadata.GetData(metadataData);
                Assert.That(metadataData[1].Flags, Is.EqualTo(58u));
                Assert.That(metadataData[1].EncodedPhysicalPage, Is.EqualTo(1u));
                Assert.That(metadataData[1].Reserved, Is.EqualTo(123u));
                Assert.That(metadataData[3].Flags, Is.EqualTo(54u));
                Assert.That(metadataData[5].Flags, Is.Zero);
                for (int page = 0; page < metadataData.Length; page++)
                    Assert.That(metadataData[page].LastRequestedFrame, Is.Zero);

                // Only the new producer requests page 7. Old residents remain evictable,
                // even when a rewind produces feedback at frame zero.
                metadataData[7].Flags = 1u;
                metadataData[7].LastRequestedFrame = (uint)frame;
                metadata.SetData(metadataData);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 2);
                shader.SetInt("_VSMPrototypeFeedbackFrameIndex", frame);
                shader.SetBuffer(allocate, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(allocate, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(allocate, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(allocate, "_VSMPrototypeAllocatorCounters", counters);
                shader.Dispatch(allocate, 1, 1, 1);
                table.GetData(tableData);
                metadata.GetData(metadataData);
                owners.GetData(ownerData);
                counters.GetData(counterData);
                Assert.That(tableData[1], Is.Zero);
                Assert.That(tableData[3], Is.EqualTo(2u));
                Assert.That(tableData[5], Is.Zero);
                Assert.That(tableData[7], Is.EqualTo(1u));
                Assert.That(metadataData[7].Flags, Is.EqualTo(54u));
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

        [TestCase(0, 0)]
        [TestCase(0, 1)]
        [TestCase(0, 2)]
        [TestCase(0, 3)]
        [TestCase(1, 0)]
        public void VirtualShadowMapPrototypeMeshletPages_ClipLargeRequestsToRelevantPages(
            int casterLayer, int dirtyMode)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
            Assert.That(shader, Is.Not.Null);
            int prepare = shader.FindKernel("VSMPrototypePrepareMeshletPageRequests");
            int cull = shader.FindKernel("VSMPrototypeCullMeshletsToPages");
            const int pageCount = 256;
            const int sourceCapacity = 24;
            const int requestCapacity = sourceCapacity * 4;
            int listCount = (int)VividRendererListID.Count;
            var tableData = new uint[pageCount];
            var metadataData = new TestPageMetadata[pageCount];
            var ownerData = new uint[pageCount];
            var relevant = new bool[pageCount];
            int relevantCount = 0;
            for (int page = 0; page < pageCount; page++)
            {
                // Full-pool stress plus sparse mappings and non-contiguous physical owners.
                bool allocated = dirtyMode == 3 || page % 7 != 0;
                bool dirty = dirtyMode == 3 || (dirtyMode == 1 && page == 9)
                    || (dirtyMode == 2 && page % 11 == 9);
                int physical = (page * 73 + 19) % pageCount;
                tableData[page] = allocated ? (uint)physical + 1u : 0u;
                ownerData[physical] = allocated ? (uint)page + 1u : 0u;
                metadataData[page].Flags = allocated ? (dirty ? 6u : 10u) : 0u;
                relevant[page] = allocated && (casterLayer != 0 || dirty);
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
            using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pageCount, 4);
            using var sources = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sourceCapacity, 8);
            using var sourceArgs = new GraphicsBuffer(GraphicsBuffer.Target.Raw, listCount * 16, 4);
            using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, requestCapacity + 4, 16);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments,
                listCount * 8, 4);
            using var rasterPages = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pageCount + 1, 4);
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
            shader.SetInt("_VSMPrototypePhysicalPageCapacity", pageCount);
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
            var rasterPageData = new uint[pageCount + 1];
            requests.GetData(requestData); args.GetData(argsData); rasterPages.GetData(rasterPageData);
            Assert.That(rasterPageData[0], Is.EqualTo(relevantCount));
            var seenPages = new bool[pageCount];
            for (int i = 0; i < relevantCount; i++)
            {
                uint page = rasterPageData[i + 1];
                Assert.That(page, Is.LessThan(pageCount));
                Assert.That(relevant[page] && !seenPages[page], Is.True);
                seenPages[page] = true;
            }
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
                    uint page = large ? rasterPageData[1u + local % rasterPageData[0]] : request.z;
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
                    VirtualShadowMapPrototypeRuntime.MaxPhysicalPageCount + 1));

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
