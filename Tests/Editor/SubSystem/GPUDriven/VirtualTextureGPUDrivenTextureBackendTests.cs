using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using VividRP.Editor;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureGPUDrivenTextureBackendTests
    {
        private sealed class TestCapabilities : IGPUDrivenVirtualTextureRuntimeCapabilities
        {
            internal bool SupportsCompute { get; set; } = true;

            internal bool SupportsLoadStore { get; set; } = true;

            internal CopyTextureSupport CopySupport { get; set; } =
                CopyTextureSupport.Basic | CopyTextureSupport.RTToTexture;

            public bool SupportsComputeShaders => SupportsCompute;

            public CopyTextureSupport CopyTextureSupport => CopySupport;

            public bool IsFormatSupported(GraphicsFormat format, GraphicsFormatUsage usage)
            {
                return SupportsLoadStore;
            }
        }

        private sealed class ManualFenceHandle : IVTUploadFenceHandle
        {
            public bool IsPassed { get; set; }
        }

        private sealed class ManualFenceFactory : IVTUploadFenceFactory
        {
            internal readonly System.Collections.Generic.List<ManualFenceHandle> Handles = new();

            public IVTUploadFenceHandle Create(CommandBuffer cmd)
            {
                var handle = new ManualFenceHandle();
                Handles.Add(handle);
                return handle;
            }
        }

        private ManualFenceFactory m_FenceFactory;

        [SetUp]
        public void SetUp()
        {
            VirtualTextureSystem.Deinitialize();
            m_FenceFactory = new ManualFenceFactory();
            VirtualTextureSystem.SetUploadFenceFactoryForTesting(m_FenceFactory);
        }

        [TearDown]
        public void TearDown()
        {
            VirtualTextureSystem.SetUploadFenceFactoryForTesting(null);
            VirtualTextureSystem.Deinitialize();
            m_FenceFactory = null;
        }

        [Test]
        public void Constructor_CreatesPrivateThreeLayerSharedSpace()
        {
            using var backend = new VirtualTextureGPUDrivenTextureBackend();

            Assert.That(backend.IsAvailable, Is.True, backend.UnavailableReason);
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.LayerCount, Is.EqualTo(3));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(0).Semantic, Is.EqualTo(VTLayerSemantic.BaseColor));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(1).Semantic, Is.EqualTo(VTLayerSemantic.Normal));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(2).Semantic, Is.EqualTo(VTLayerSemantic.Mask));
            Assert.That(
                backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(1).FallbackColor,
                Is.EqualTo(new Color32(128, 128, 255, 128)));
            Assert.That(
                VirtualTextureSystem.TryGetAllocationForTesting(
                    backend.VirtualTextureSpaceId,
                    out VTAllocatedVirtualTexture allocation),
                Is.True);
            Assert.That(allocation.AllocationId, Is.EqualTo(backend.VirtualTextureAllocationId));
            Assert.That(allocation.Description.PrivateSpace, Is.True);
        }

        [Test]
        public void Constructor_IsUnavailableWhenGpuProducerShaderIsMissing()
        {
            const string reason = "GPUDriven VT page producer compute shader resource is missing.";
            LogAssert.Expect(
                LogType.Warning,
                $"[VividRP] GPUDriven virtual texture backend is unavailable: {reason}");

            using var backend = new VirtualTextureGPUDrivenTextureBackend(null, new TestCapabilities());

            Assert.That(backend.IsAvailable, Is.False);
            Assert.That(backend.VirtualTextureSpaceId, Is.Zero);
            Assert.That(backend.UnavailableReason, Is.EqualTo(reason));
        }

        [Test]
        public void Constructor_IsUnavailableWhenComputeShadersAreUnsupported()
        {
            const string reason = "The active graphics device does not support compute shaders.";
            LogAssert.Expect(
                LogType.Warning,
                $"[VividRP] GPUDriven virtual texture backend is unavailable: {reason}");
            var capabilities = new TestCapabilities { SupportsCompute = false };

            using var backend = new VirtualTextureGPUDrivenTextureBackend(GetPageProducerCompute(), capabilities);

            Assert.That(backend.IsAvailable, Is.False);
            Assert.That(backend.VirtualTextureSpaceId, Is.Zero);
            Assert.That(backend.UnavailableReason, Is.EqualTo(reason));
        }

        [Test]
        public void Constructor_IsUnavailableWhenUavLoadStoreIsUnsupported()
        {
            var capabilities = new TestCapabilities { SupportsLoadStore = false };
            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    "GPUDriven virtual texture backend is unavailable: .*UAV load/store"));

            using var backend = new VirtualTextureGPUDrivenTextureBackend(GetPageProducerCompute(), capabilities);

            Assert.That(backend.IsAvailable, Is.False);
            Assert.That(backend.VirtualTextureSpaceId, Is.Zero);
            Assert.That(backend.UnavailableReason, Does.Contain("UAV load/store"));
        }

        [Test]
        public void Constructor_IsUnavailableWhenRenderTextureArrayCopyIsUnsupported()
        {
            const string reason = "The active graphics device cannot copy a RenderTexture array into the VT Texture2DArray cache.";
            LogAssert.Expect(
                LogType.Warning,
                $"[VividRP] GPUDriven virtual texture backend is unavailable: {reason}");
            var capabilities = new TestCapabilities { CopySupport = CopyTextureSupport.Basic };

            using var backend = new VirtualTextureGPUDrivenTextureBackend(GetPageProducerCompute(), capabilities);

            Assert.That(backend.IsAvailable, Is.False);
            Assert.That(backend.VirtualTextureSpaceId, Is.Zero);
            Assert.That(backend.UnavailableReason, Is.EqualTo(reason));
        }

        [Test]
        public void CreateSurfaceBinding_AllocatesOneAlignedRegionAndPacksLayerAndMaxMip()
        {
            Texture2D baseColor = null;
            Texture2D normal = null;
            Texture2D mask = null;
            try
            {
                baseColor = new Texture2D(256, 256);
                normal = new Texture2D(128, 128);
                mask = new Texture2D(64, 64);
                using var backend = new VirtualTextureGPUDrivenTextureBackend();

                uint revisionBefore = backend.BindingRevision;
                var binding = backend.CreateSurfaceBinding(new GPUDrivenSurfaceTextureSet(baseColor, normal, mask));

                Assert.That(binding.Flags, Is.EqualTo(
                    VividSurfaceBindingFlags.BaseColor
                    | VividSurfaceBindingFlags.Normal
                    | VividSurfaceBindingFlags.Mask));
                Assert.That(binding.BaseColorResource & 0xFFu, Is.EqualTo(0u));
                Assert.That(binding.NormalResource & 0xFFu, Is.EqualTo(1u));
                Assert.That(binding.MaskResource & 0xFFu, Is.EqualTo(2u));
                Assert.That(binding.BaseColorResource >> 8, Is.EqualTo(1u));
                Assert.That(binding.NormalResource >> 8, Is.EqualTo(1u));
                Assert.That(binding.MaskResource >> 8, Is.EqualTo(1u));
                Assert.That(binding.UVScaleBias, Is.EqualTo(new float4(2.0f / 128.0f, 2.0f / 128.0f, 0.0f, 0.0f)));
                Assert.That(backend.AtlasEntryCount, Is.EqualTo(1));
                Assert.That(backend.AllocatedPageCount, Is.EqualTo(4));
                Assert.That(backend.QueuedMipTailCount, Is.EqualTo(1));
                Assert.That(backend.ResidentMipTailCount, Is.Zero);
                var mipTailCoord = new VirtualTexturePageCoord(0, 0, 1);
                Assert.That(
                    VirtualTextureSystem.TryGetPageTableEntryForTesting(
                        backend.VirtualTextureSpaceId,
                        mipTailCoord,
                        out VirtualTexturePageTableEntry mipTailEntry),
                    Is.True);
                Assert.That(mipTailEntry.Resident, Is.False);
                Assert.That(mipTailEntry.PendingUpload, Is.True);
                Assert.That(mipTailEntry.Fallback, Is.True);
                Assert.That(mipTailEntry.Locked, Is.True);
                Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(backend.VirtualTextureSpaceId), Is.EqualTo(1));
                Assert.That(backend.BindingRevision, Is.GreaterThan(revisionBefore));
            }
            finally
            {
                Destroy(baseColor);
                Destroy(normal);
                Destroy(mask);
            }
        }

        [Test]
        public void CreateSurfaceBinding_DeduplicatesTextureSetAndKeepsRevisionStable()
        {
            Texture2D baseColor = null;
            try
            {
                baseColor = new Texture2D(128, 128);
                using var backend = new VirtualTextureGPUDrivenTextureBackend();
                var textures = new GPUDrivenSurfaceTextureSet(baseColor, null, null);

                VividSurfaceBindingData first = backend.CreateSurfaceBinding(textures);
                uint revisionAfterFirst = backend.BindingRevision;
                VividSurfaceBindingData second = backend.CreateSurfaceBinding(textures);

                Assert.That(second.BaseColorResource, Is.EqualTo(first.BaseColorResource));
                Assert.That(second.UVScaleBias, Is.EqualTo(first.UVScaleBias));
                Assert.That(backend.AtlasEntryCount, Is.EqualTo(1));
                Assert.That(backend.QueuedMipTailCount, Is.EqualTo(1));
                Assert.That(backend.ResidentMipTailCount, Is.Zero);
                Assert.That(backend.BindingRevision, Is.EqualTo(revisionAfterFirst));
            }
            finally
            {
                Destroy(baseColor);
            }
        }

        [Test]
        public void CreateSurfaceBinding_StreamedAsset_UsesSidecarAndCpuPageFinalizer()
        {
            Texture2D sourceTexture = new Texture2D(256, 256, TextureFormat.RGBA32, true);
            VividVirtualTextureAsset asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            VividVirtualTextureBuiltData builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            string streamDataPath = Path.Combine(Path.GetTempPath(), $"GPUDriven_{System.Guid.NewGuid():N}.stream");

            try
            {
                sourceTexture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
                VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
                {
                    SourceTexture = sourceTexture,
                    StreamDataPath = streamDataPath,
                    BuildProfile = VividVirtualTextureBuildProfile.GPUDrivenSurface,
                });
                Object.DestroyImmediate(sourceTexture);
                sourceTexture = null;

                using var backend = new VirtualTextureGPUDrivenTextureBackend();
                backend.BeginSurfaceBindingUpdate();
                var binding = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(asset, null, null, null));
                backend.EndSurfaceBindingUpdate();

                Assert.That(binding.Flags, Is.EqualTo(VividSurfaceBindingFlags.BaseColor));
                Assert.That(binding.BaseColorResource & 0xFFu, Is.EqualTo(0u));
                Assert.That(binding.BaseColorResource >> 8, Is.EqualTo(1u));
                Assert.That(binding.NormalResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(binding.MaskResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(backend.AtlasEntryCount, Is.EqualTo(1));
                Assert.That(backend.StreamedAtlasEntryCount, Is.EqualTo(1));

                UpdateOnce();
                Assert.That(VirtualTextureStatsRegistry.LastStats.CpuProducedPageCount, Is.EqualTo(1));
                Assert.That(VirtualTextureStatsRegistry.LastStats.GpuProducedPageCount, Is.Zero);
                Assert.That(VirtualTextureStatsRegistry.LastStats.GpuDispatchCount, Is.Zero);

                backend.BeginSurfaceBindingUpdate();
                backend.EndSurfaceBindingUpdate();
                Assert.That(backend.AtlasEntryCount, Is.Zero);
                Assert.That(backend.StreamedAtlasEntryCount, Is.Zero);
                Assert.That(backend.AllocatedPageCount, Is.Zero);
            }
            finally
            {
                if (sourceTexture != null)
                    Object.DestroyImmediate(sourceTexture);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(builtData);
                if (File.Exists(streamDataPath))
                    File.Delete(streamDataPath);
            }
        }

        [Test]
        public void SurfaceBindingUpdate_ReleasesUntouchedEntriesAndReusesAtlasRegion()
        {
            Texture2D firstTexture = null;
            Texture2D secondTexture = null;
            Texture2D replacementTexture = null;
            try
            {
                firstTexture = new Texture2D(256, 256);
                secondTexture = new Texture2D(256, 256);
                replacementTexture = new Texture2D(256, 256);
                using var backend = new VirtualTextureGPUDrivenTextureBackend();

                backend.BeginSurfaceBindingUpdate();
                VividSurfaceBindingData firstBinding = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(firstTexture, null, null));
                VividSurfaceBindingData secondBinding = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(secondTexture, null, null));
                backend.EndSurfaceBindingUpdate();

                Assert.That(backend.AtlasEntryCount, Is.EqualTo(2));
                Assert.That(backend.AllocatedPageCount, Is.EqualTo(8));
                Assert.That(backend.QueuedMipTailCount, Is.EqualTo(2));

                backend.BeginSurfaceBindingUpdate();
                VividSurfaceBindingData retainedBinding = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(secondTexture, null, null));
                backend.EndSurfaceBindingUpdate();

                Assert.That(retainedBinding.UVScaleBias, Is.EqualTo(secondBinding.UVScaleBias));
                Assert.That(backend.AtlasEntryCount, Is.EqualTo(1));
                Assert.That(backend.AllocatedPageCount, Is.EqualTo(4));
                Assert.That(backend.QueuedMipTailCount, Is.EqualTo(1));
                Assert.That(backend.GetStats().RegisteredResourceCount, Is.EqualTo(1));

                backend.BeginSurfaceBindingUpdate();
                backend.EndSurfaceBindingUpdate();
                VividSurfaceBindingData replacementBinding = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(replacementTexture, null, null));

                Assert.That(backend.AtlasEntryCount, Is.EqualTo(1));
                Assert.That(backend.AllocatedPageCount, Is.EqualTo(4));
                Assert.That(backend.QueuedMipTailCount, Is.EqualTo(1));
                Assert.That(replacementBinding.UVScaleBias, Is.EqualTo(firstBinding.UVScaleBias));
            }
            finally
            {
                Destroy(firstTexture);
                Destroy(secondTexture);
                Destroy(replacementTexture);
            }
        }

        [Test]
        public void SurfaceBindingUpdate_BatchesReleasedRegionsIntoOnePageTableRebuild()
        {
            Texture2D firstTexture = null;
            Texture2D secondTexture = null;
            try
            {
                firstTexture = new Texture2D(256, 256);
                secondTexture = new Texture2D(256, 256);
                using var backend = new VirtualTextureGPUDrivenTextureBackend();

                backend.BeginSurfaceBindingUpdate();
                backend.CreateSurfaceBinding(new GPUDrivenSurfaceTextureSet(firstTexture, null, null));
                backend.CreateSurfaceBinding(new GPUDrivenSurfaceTextureSet(secondTexture, null, null));
                backend.EndSurfaceBindingUpdate();
                int rebuildCountBeforeRelease = VirtualTextureSystem.GetPageTableRebuildCountForTesting(
                    backend.VirtualTextureSpaceId);

                backend.BeginSurfaceBindingUpdate();
                backend.EndSurfaceBindingUpdate();

                Assert.That(
                    VirtualTextureSystem.GetPageTableRebuildCountForTesting(backend.VirtualTextureSpaceId),
                    Is.EqualTo(rebuildCountBeforeRelease + 1));
                Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(backend.VirtualTextureSpaceId), Is.Zero);
                Assert.That(backend.AtlasEntryCount, Is.Zero);
                Assert.That(backend.AllocatedPageCount, Is.Zero);
                Assert.That(backend.QueuedMipTailCount, Is.Zero);
                Assert.That(backend.ResidentMipTailCount, Is.Zero);
            }
            finally
            {
                Destroy(firstTexture);
                Destroy(secondTexture);
            }
        }

        [Test]
        public void SurfaceBindingUpdate_CancelKeepsPreviousEntriesAndReleasesNewEntries()
        {
            Texture2D retainedTexture = null;
            Texture2D transientTexture = null;
            try
            {
                retainedTexture = new Texture2D(128, 128);
                transientTexture = new Texture2D(128, 128);
                using var backend = new VirtualTextureGPUDrivenTextureBackend();

                backend.BeginSurfaceBindingUpdate();
                VividSurfaceBindingData retainedBinding = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(retainedTexture, null, null));
                backend.EndSurfaceBindingUpdate();

                backend.BeginSurfaceBindingUpdate();
                backend.CreateSurfaceBinding(new GPUDrivenSurfaceTextureSet(retainedTexture, null, null));
                backend.CreateSurfaceBinding(new GPUDrivenSurfaceTextureSet(transientTexture, null, null));
                backend.CancelSurfaceBindingUpdate();

                VividSurfaceBindingData bindingAfterCancel = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(retainedTexture, null, null));
                Assert.That(bindingAfterCancel.UVScaleBias, Is.EqualTo(retainedBinding.UVScaleBias));
                Assert.That(backend.AtlasEntryCount, Is.EqualTo(1));
                Assert.That(backend.AllocatedPageCount, Is.EqualTo(1));
                Assert.That(backend.QueuedMipTailCount, Is.EqualTo(1));
                Assert.That(backend.GetStats().RegisteredResourceCount, Is.EqualTo(1));
            }
            finally
            {
                Destroy(retainedTexture);
                Destroy(transientTexture);
            }
        }

        [Test]
        public void CreateSurfaceBinding_NoTexturesUsesBackendNeutralFallback()
        {
            using var backend = new VirtualTextureGPUDrivenTextureBackend();

            VividSurfaceBindingData binding = backend.CreateSurfaceBinding(
                new GPUDrivenSurfaceTextureSet(null, null, null));

            Assert.That(binding.Flags, Is.EqualTo(VividSurfaceBindingFlags.None));
            Assert.That(binding.BaseColorResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
            Assert.That(binding.NormalResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
            Assert.That(binding.MaskResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
            Assert.That(binding.UVScaleBias, Is.EqualTo(new float4(1.0f, 1.0f, 0.0f, 0.0f)));
            Assert.That(backend.AtlasEntryCount, Is.Zero);
            Assert.That(backend.ResidentMipTailCount, Is.Zero);
        }

        [Test]
        public void CreateSurfaceBinding_RollsBackAtlasRegionWhenLockedMipTailCannotBeQueued()
        {
            var textures = new System.Collections.Generic.List<Texture2D>();
            try
            {
                using var backend = new VirtualTextureGPUDrivenTextureBackend();
                int availableTailPages = backend.VirtualTextureSpaceDesc.CachePageCount - 1;
                for (int textureIndex = 0; textureIndex < availableTailPages; textureIndex++)
                {
                    var texture = new Texture2D(1, 1);
                    textures.Add(texture);
                    VividSurfaceBindingData binding = backend.CreateSurfaceBinding(
                        new GPUDrivenSurfaceTextureSet(texture, null, null));
                    Assert.That(binding.Flags, Is.EqualTo(VividSurfaceBindingFlags.BaseColor));
                }

                int atlasEntryCountBeforeFailure = backend.AtlasEntryCount;
                int allocatedPageCountBeforeFailure = backend.AllocatedPageCount;
                uint revisionBeforeFailure = backend.BindingRevision;
                var rejectedTexture = new Texture2D(1, 1);
                textures.Add(rejectedTexture);
                LogAssert.Expect(
                    LogType.Warning,
                    new System.Text.RegularExpressions.Regex("GPUDriven VT mip tail .* could not be queued"));

                VividSurfaceBindingData rejectedBinding = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(rejectedTexture, null, null));

                Assert.That(rejectedBinding.Flags, Is.EqualTo(VividSurfaceBindingFlags.None));
                Assert.That(rejectedBinding.BaseColorResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(backend.AtlasEntryCount, Is.EqualTo(atlasEntryCountBeforeFailure));
                Assert.That(backend.AllocatedPageCount, Is.EqualTo(allocatedPageCountBeforeFailure));
                Assert.That(backend.QueuedMipTailCount, Is.EqualTo(availableTailPages));
                Assert.That(backend.BindingRevision, Is.EqualTo(revisionBeforeFailure));
            }
            finally
            {
                for (int textureIndex = 0; textureIndex < textures.Count; textureIndex++)
                    Destroy(textures[textureIndex]);
            }
        }

        [Test]
        public void CreateSurfaceBinding_EncodesClampAndSeedsLockedMipTail()
        {
            Texture2D baseColor = null;
            try
            {
                baseColor = new Texture2D(128, 128)
                {
                    wrapMode = TextureWrapMode.Clamp,
                };
                using var backend = new VirtualTextureGPUDrivenTextureBackend();

                VividSurfaceBindingData binding = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(baseColor, null, null));

                Assert.That(
                    binding.UVScaleBias,
                    Is.EqualTo(new float4(-1.0f / 128.0f, -1.0f / 128.0f, 0.0f, 0.0f)));
                Assert.That(backend.QueuedMipTailCount, Is.EqualTo(1));
                Assert.That(backend.ResidentMipTailCount, Is.Zero);
                var mipTailCoord = new VirtualTexturePageCoord(0, 0, 0);
                Assert.That(
                    VirtualTextureSystem.TryGetPageTableEntryForTesting(
                        backend.VirtualTextureSpaceId,
                        mipTailCoord,
                        out VirtualTexturePageTableEntry mipTailEntry),
                    Is.True);
                Assert.That(mipTailEntry.Resident, Is.False);
                Assert.That(mipTailEntry.PendingUpload, Is.True);
                Assert.That(mipTailEntry.Locked, Is.True);

                uint revisionBeforeUpload = backend.BindingRevision;
                UpdateOnce();
                Assert.That(m_FenceFactory.Handles, Has.Count.EqualTo(1));
                Assert.That(VirtualTextureStatsRegistry.LastStats.GpuProducedPageCount, Is.EqualTo(1));
                Assert.That(VirtualTextureStatsRegistry.LastStats.GpuDispatchCount, Is.EqualTo(1));
                Assert.That(VirtualTextureStatsRegistry.LastStats.CpuProducedPageCount, Is.Zero);

                m_FenceFactory.Handles[0].IsPassed = true;
                UpdateOnce();
                backend.PrepareFrame();

                Assert.That(
                    VirtualTextureSystem.TryGetPageTableEntryForTesting(
                        backend.VirtualTextureSpaceId,
                        mipTailCoord,
                        out VirtualTexturePageTableEntry residentEntry),
                    Is.True);
                Assert.That(residentEntry.Resident, Is.True);
                Assert.That(residentEntry.PendingUpload, Is.False);
                Assert.That(residentEntry.Locked, Is.True);
                Assert.That(backend.ResidentMipTailCount, Is.EqualTo(1));
                Assert.That(backend.BindingRevision, Is.EqualTo(revisionBeforeUpload));
            }
            finally
            {
                Destroy(baseColor);
            }
        }

        [Test]
        public void SurfaceTextureSet_UsesFirstAvailableLayerAndReportsFallbackModes()
        {
            Texture2D baseColor = null;
            Texture2D normal = null;
            Texture2D mask = null;
            try
            {
                baseColor = new Texture2D(1, 1) { wrapMode = TextureWrapMode.Clamp };
                normal = new Texture2D(1, 1) { wrapMode = TextureWrapMode.Repeat };
                mask = new Texture2D(1, 1) { wrapMode = TextureWrapMode.Mirror };

                var textures = new GPUDrivenSurfaceTextureSet(baseColor, normal, mask);

                Assert.That(textures.AddressMode, Is.EqualTo(GPUDrivenSurfaceAddressMode.Clamp));
                Assert.That(textures.HasMixedAddressModes, Is.True);
                Assert.That(textures.HasUnsupportedAddressMode, Is.True);
            }
            finally
            {
                Destroy(baseColor);
                Destroy(normal);
                Destroy(mask);
            }
        }

        [Test]
        public void SourceSampling_HonorsRepeatAndClampAfterLazyMipBuild()
        {
            Texture2D source = null;
            try
            {
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.SetPixels32(new[]
                {
                    new Color32(255, 0, 0, 255),
                    new Color32(0, 255, 0, 255),
                    new Color32(0, 0, 255, 255),
                    new Color32(255, 255, 255, 255),
                });
                source.Apply(false, false);

                var producer = new VTTexture2DPageProducer(source);
                Color32 repeatSample = producer.SampleSource(0, 1.25f, 0.25f, true);
                Color32 clampSample = producer.SampleSource(0, 1.25f, 0.25f, false);

                Assert.That(repeatSample, Is.EqualTo(new Color32(255, 0, 0, 255)));
                Assert.That(clampSample, Is.EqualTo(new Color32(0, 255, 0, 255)));
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void GpuPageProducerContract_WritesThreeLayersWithoutCpuPixelReadbackOrUpload()
        {
            string producerSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "SubSystem",
                "GPUDriven",
                "VirtualTexture",
                "GPUDrivenVirtualTextureProducer.cs"));
            string computeSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GPUDriven",
                "GPUDrivenVirtualTexturePageProducer.compute"));

            Assert.That(producerSource, Does.Contain("IVTGpuPageFinalizer"));
            Assert.That(producerSource, Does.Contain("DispatchCompute"));
            Assert.That(producerSource, Does.Contain("SourceMipOffsets"));
            Assert.That(producerSource, Does.Not.Contain("VTTexture2DPageProducer"));
            Assert.That(producerSource, Does.Not.Contain("GetPixels32"));
            Assert.That(producerSource, Does.Not.Contain("ReadPixels"));
            Assert.That(producerSource, Does.Not.Contain("SetPixels32"));
            Assert.That(producerSource, Does.Not.Contain(".Apply("));

            Assert.That(computeSource, Does.Contain("RWTexture2DArray<float4> _OutputPages"));
            Assert.That(computeSource, Does.Contain("[numthreads(8, 8, 1)]"));
            Assert.That(computeSource, Does.Contain("SampleLevel(sampler_LinearRepeat"));
            Assert.That(computeSource, Does.Contain("SampleLevel(sampler_LinearClamp"));
            Assert.That(computeSource, Does.Contain("EncodeLinearToSRGB"));
            Assert.That(computeSource, Does.Contain("_VTBaseColorFallback"));
            Assert.That(computeSource, Does.Contain("_VTNormalFallback"));
            Assert.That(computeSource, Does.Contain("_VTMaskFallback"));
            Assert.That(computeSource, Does.Contain("baseSlice + 0"));
            Assert.That(computeSource, Does.Contain("baseSlice + 1"));
            Assert.That(computeSource, Does.Contain("baseSlice + 2"));
        }

        [Test]
        public void GpuPageProducer_ComputesSourceMipOffsetFromVirtualAllocationScale()
        {
            Texture2D halfResolution = null;
            Texture2D matchingResolution = null;
            Texture2D doubleResolution = null;
            try
            {
                halfResolution = new Texture2D(64, 64);
                matchingResolution = new Texture2D(128, 128);
                doubleResolution = new Texture2D(256, 256);

                Assert.That(GPUDrivenVirtualTextureProducer.ComputeSourceMipOffset(
                    halfResolution,
                    pageCount: 1,
                    pageSize: 128), Is.EqualTo(-1));
                Assert.That(GPUDrivenVirtualTextureProducer.ComputeSourceMipOffset(
                    matchingResolution,
                    pageCount: 1,
                    pageSize: 128), Is.Zero);
                Assert.That(GPUDrivenVirtualTextureProducer.ComputeSourceMipOffset(
                    doubleResolution,
                    pageCount: 1,
                    pageSize: 128), Is.EqualTo(1));
            }
            finally
            {
                Destroy(halfResolution);
                Destroy(matchingResolution);
                Destroy(doubleResolution);
            }
        }

        [Test]
        public void SurfaceSamplingContract_UsesOneResolveContextForGradientsAndFeedback()
        {
            string source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VirtualTextureSurfaceSampling.hlsl"));

            Assert.That(source, Does.Contain("struct VividSurfaceSampleContext"));
            Assert.That(source, Does.Contain("VTComputeRequestedMipRangeGrad("));
            Assert.That(source, Does.Contain("VividGetSurfaceVirtualTextureMaxMip("));
            Assert.That(source, Does.Contain("VTWriteFeedback("));
            Assert.That(source, Does.Contain("VTWriteFallbackSample("));
            Assert.That(source, Does.Contain("VividSampleBaseColorGrad("));
            Assert.That(source, Does.Contain("VividSampleNormalGrad("));
            Assert.That(source, Does.Contain("VividSampleMaskGrad("));
            Assert.That(source, Does.Contain("VividSurfaceUsesClamp("));
            Assert.That(source, Does.Contain("saturate(uv)"));
            Assert.That(source, Does.Contain("frac(uv)"));
            Assert.That(source, Does.Contain("ddx(uv)"));
            Assert.That(source, Does.Contain("ddy(uv)"));
            Assert.That(source, Does.Not.Contain("GetBindlessTexture2D("));
        }

        [Test]
        public void BindlessSamplingContract_HonorsRepeatAndClampForImplicitAndGradientSamples()
        {
            string source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "BindlessSurfaceSampling.hlsl"));

            Assert.That(source, Does.Contain("VividSurfaceUsesClamp("));
            Assert.That(source, Does.Contain("sampler_LinearClamp"));
            Assert.That(source, Does.Contain("sampler_LinearRepeat"));
            Assert.That(source, Does.Contain("SAMPLE_TEXTURE2D_GRAD"));
        }

        private static string GetPackageFilePath(params string[] parts)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] candidates =
            {
                Path.Combine(projectRoot, "Packages", "Custom_URP"),
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate))
                    return Path.Combine(candidate, Path.Combine(parts));
            }

            return Path.Combine(candidates[2], Path.Combine(parts));
        }

        private static ComputeShader GetPageProducerCompute()
        {
            ComputeShader computeShader = PipelineResourceManager.Get<VividRPCoreResources>()
                ?.GPUDrivenVirtualTexturePageProducerCompute;
            Assert.That(computeShader, Is.Not.Null, "GPUDriven VT page producer resource was not synchronized.");
            return computeShader;
        }

        private static void UpdateOnce()
        {
            using var commandBuffer = new CommandBuffer();
            VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
        }

        private static void Destroy(Object value)
        {
            if (value != null)
                Object.DestroyImmediate(value);
        }
    }
}
