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
                CopyTextureSupport.Basic
                | CopyTextureSupport.RTToTexture
                | CopyTextureSupport.DifferentTypes;

            internal int MaximumTextureSize { get; set; } = 16384;

            public bool SupportsComputeShaders => SupportsCompute;

            public int MaxTextureSize => MaximumTextureSize;

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
        public void Constructor_CreatesPrivateFourLayerBcnSharedSpace()
        {
            using var backend = new VirtualTextureGPUDrivenTextureBackend();

            Assert.That(backend.IsAvailable, Is.True, backend.UnavailableReason);
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.LayerCount, Is.EqualTo(4));
            Assert.That(backend.VirtualTextureSpaceDesc.VirtualPageCountX, Is.EqualTo(256));
            Assert.That(backend.VirtualTextureSpaceDesc.VirtualPageCountY, Is.EqualTo(256));
            Assert.That(backend.VirtualTextureSpaceDesc.MipCount, Is.EqualTo(9));
            Assert.That(backend.VirtualTextureSpaceDesc.CachePageCount, Is.EqualTo(512));
            Assert.That(backend.VirtualTextureSpaceDesc.PageTableEntryCount, Is.EqualTo(87381));
            Assert.That(VirtualTextureGPUDrivenTextureBackend.MaxAllocationPageCount, Is.EqualTo(64));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(0).Semantic, Is.EqualTo(VTLayerSemantic.BaseColor));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(1).Semantic, Is.EqualTo(VTLayerSemantic.Normal));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(2).Semantic, Is.EqualTo(VTLayerSemantic.Mask));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(3).Semantic, Is.EqualTo(VTLayerSemantic.Height));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(0).GraphicsFormat, Is.EqualTo(GraphicsFormat.RGBA_BC7_SRGB));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(1).GraphicsFormat, Is.EqualTo(GraphicsFormat.RG_BC5_UNorm));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(2).GraphicsFormat, Is.EqualTo(GraphicsFormat.RGBA_BC7_UNorm));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(3).GraphicsFormat, Is.EqualTo(GraphicsFormat.R_BC4_UNorm));
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
            Assert.That(
                VirtualTextureSystem.TryGetPhysicalCacheForTesting(
                    backend.VirtualTextureSpaceId,
                    out Texture2D physicalAtlas),
                Is.True);
            Assert.That(physicalAtlas.dimension, Is.EqualTo(TextureDimension.Tex2D));
            Assert.That(physicalAtlas.width, Is.EqualTo(3128));
            Assert.That(physicalAtlas.height, Is.EqualTo(3128));
        }

        [TestCase(GPUDrivenVirtualTexturePhysicalPoolQuality.Low, 256)]
        [TestCase(GPUDrivenVirtualTexturePhysicalPoolQuality.Medium, 512)]
        [TestCase(GPUDrivenVirtualTexturePhysicalPoolQuality.High, 1024)]
        public void DescriptorProfile_MapsPhysicalPoolQualityWithoutChangingStreamingBudgets(
            GPUDrivenVirtualTexturePhysicalPoolQuality quality,
            int expectedCachePageCount)
        {
            GPUDrivenVirtualTextureDescriptorProfile profile =
                VirtualTextureGPUDrivenTextureBackend.ResolveDescriptorProfile(quality);

            Assert.That(profile.CachePageCount, Is.EqualTo(expectedCachePageCount));
        }

        [Test]
        public void DescriptorProfile_InvalidQualityFallsBackToMedium()
        {
            GPUDrivenVirtualTextureDescriptorProfile profile =
                VirtualTextureGPUDrivenTextureBackend.ResolveDescriptorProfile(
                    (GPUDrivenVirtualTexturePhysicalPoolQuality)99);

            Assert.That(profile.CachePageCount, Is.EqualTo(512));
        }

        [TestCase(3000, 256)]
        [TestCase(4096, 512)]
        [TestCase(8192, 1024)]
        public void SupportedDescriptorProfile_DowngradesHighToLargestTierThatFits(
            int maxTextureSize,
            int expectedCachePageCount)
        {
            GPUDrivenVirtualTextureDescriptorProfile requestedProfile =
                VirtualTextureGPUDrivenTextureBackend.ResolveDescriptorProfile(
                    GPUDrivenVirtualTexturePhysicalPoolQuality.High);

            GPUDrivenVirtualTextureDescriptorProfile supportedProfile =
                VirtualTextureGPUDrivenTextureBackend.ResolveSupportedDescriptorProfile(
                    requestedProfile,
                    maxTextureSize);

            Assert.That(supportedProfile.CachePageCount, Is.EqualTo(expectedCachePageCount));
        }

        [Test]
        public void Constructor_DowngradesUnsupportedHighProfileBeforeCreatingSpace()
        {
            const string missingShaderReason =
                "GPUDriven VT page producer compute shader resource is missing.";
            LogAssert.Expect(
                LogType.Warning,
                "[VividRP] GPUDriven virtual texture physical pool was reduced from 1024 to 512 pages "
                + "because the active device supports at most 4096x4096 2D textures.");
            LogAssert.Expect(
                LogType.Warning,
                $"[VividRP] GPUDriven virtual texture backend is unavailable: {missingShaderReason}");
            GPUDrivenVirtualTextureDescriptorProfile highProfile =
                VirtualTextureGPUDrivenTextureBackend.ResolveDescriptorProfile(
                    GPUDrivenVirtualTexturePhysicalPoolQuality.High);
            var capabilities = new TestCapabilities { MaximumTextureSize = 4096 };

            using var backend = new VirtualTextureGPUDrivenTextureBackend(
                null,
                capabilities,
                highProfile);

            Assert.That(backend.DescriptorProfile.CachePageCount, Is.EqualTo(512));
            Assert.That(backend.VirtualTextureSpaceDesc.CachePageCount, Is.EqualTo(512));
        }

        [Test]
        public void Constructor_AppliesRestrictedDescriptorProfileOverride()
        {
            const string reason = "GPUDriven VT page producer compute shader resource is missing.";
            LogAssert.Expect(
                LogType.Warning,
                $"[VividRP] GPUDriven virtual texture backend is unavailable: {reason}");
            var profile = new GPUDrivenVirtualTextureDescriptorProfile(cachePageCount: 7);

            using var backend = new VirtualTextureGPUDrivenTextureBackend(
                null,
                new TestCapabilities(),
                profile);

            Assert.That(backend.VirtualTextureSpaceDesc.CachePageCount, Is.EqualTo(7));
            Assert.That(backend.VirtualTextureSpaceDesc.MaxUploadsPerFrame, Is.EqualTo(16));
            Assert.That(backend.VirtualTextureSpaceDesc.FeedbackCapacity, Is.EqualTo(65536));
            Assert.That(backend.VirtualTextureSpaceDesc.NeighborPrefetchCount, Is.EqualTo(1));
            Assert.That(backend.VirtualTextureSpaceDesc.PageSize, Is.EqualTo(128));
            Assert.That(backend.VirtualTextureSpaceDesc.BorderSize, Is.EqualTo(4));
            Assert.That(backend.VirtualTextureSpaceDesc.VirtualPageCountX, Is.EqualTo(256));
            Assert.That(backend.VirtualTextureSpaceDesc.VirtualPageCountY, Is.EqualTo(256));
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
        public void Constructor_IsUnavailableWhenRenderTextureArrayToAtlasCopyIsUnsupported()
        {
            const string reason =
                "The active graphics device cannot copy RenderTexture array slices into the VT 2D tile atlas.";
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
                Assert.That(
                    binding.UVScaleBias,
                    Is.EqualTo(new float4(
                        2.0f / VirtualTextureGPUDrivenTextureBackend.AtlasPageCount,
                        2.0f / VirtualTextureGPUDrivenTextureBackend.AtlasPageCount,
                        0.0f,
                        0.0f)));
                Assert.That(backend.AtlasEntryCount, Is.EqualTo(1));
                Assert.That(backend.AllocatedPageCount, Is.EqualTo(4));
                Assert.That(backend.QueuedMipTailCount, Is.EqualTo(1));
                Assert.That(backend.QueuedMipTailPageCount, Is.EqualTo(1));
                Assert.That(backend.ResidentMipTailCount, Is.Zero);
                Assert.That(backend.ResidentMipTailPageCount, Is.Zero);
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
        public void CreateSurfaceBinding_AllocatesRectangularRegionAndQueuesEveryMinAxisTailPage()
        {
            Texture2D baseColor = null;
            try
            {
                baseColor = new Texture2D(1024, 256);
                using var backend = new VirtualTextureGPUDrivenTextureBackend();

                VividSurfaceBindingData binding = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(baseColor, null, null));

                Assert.That(binding.Flags, Is.EqualTo(VividSurfaceBindingFlags.BaseColor));
                Assert.That(binding.BaseColorResource >> 8, Is.EqualTo(1u));
                Assert.That(
                    binding.UVScaleBias,
                    Is.EqualTo(new float4(
                        8.0f / VirtualTextureGPUDrivenTextureBackend.AtlasPageCount,
                        2.0f / VirtualTextureGPUDrivenTextureBackend.AtlasPageCount,
                        0.0f,
                        0.0f)));
                Assert.That(backend.AllocatedPageCount, Is.EqualTo(16));
                Assert.That(backend.QueuedMipTailCount, Is.EqualTo(1));
                Assert.That(backend.QueuedMipTailPageCount, Is.EqualTo(4));
                Assert.That(backend.ResidentMipTailCount, Is.Zero);
                Assert.That(backend.ResidentMipTailPageCount, Is.Zero);

                for (int tailX = 0; tailX < 4; tailX++)
                {
                    Assert.That(
                        VirtualTextureSystem.TryGetPageTableEntryForTesting(
                            backend.VirtualTextureSpaceId,
                            new VirtualTexturePageCoord(tailX, 0, 1),
                            out VirtualTexturePageTableEntry tailEntry),
                        Is.True);
                    Assert.That(tailEntry.PendingUpload, Is.True);
                    Assert.That(tailEntry.Locked, Is.True);
                }

                UpdateOnce();
                Assert.That(VirtualTextureStatsRegistry.LastStats.GpuProducedPageCount, Is.EqualTo(4));
                for (int fenceIndex = 0; fenceIndex < m_FenceFactory.Handles.Count; fenceIndex++)
                    m_FenceFactory.Handles[fenceIndex].IsPassed = true;
                UpdateOnce();
                backend.PrepareFrame();

                Assert.That(backend.ResidentMipTailCount, Is.EqualTo(1));
                Assert.That(backend.ResidentMipTailPageCount, Is.EqualTo(4));

                backend.BeginSurfaceBindingUpdate();
                backend.EndSurfaceBindingUpdate();
                Assert.That(backend.AllocatedPageCount, Is.Zero);
                Assert.That(backend.QueuedMipTailCount, Is.Zero);
                Assert.That(backend.QueuedMipTailPageCount, Is.Zero);
                Assert.That(backend.ResidentMipTailCount, Is.Zero);
                Assert.That(backend.ResidentMipTailPageCount, Is.Zero);
            }
            finally
            {
                Destroy(baseColor);
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
            Texture2D sourceTexture = new Texture2D(512, 128, TextureFormat.RGBA32, true);
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
                Assert.That(binding.BaseColorResource >> 8, Is.Zero);
                Assert.That(binding.NormalResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(binding.MaskResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(
                    binding.UVScaleBias,
                    Is.EqualTo(new float4(
                        4.0f / VirtualTextureGPUDrivenTextureBackend.AtlasPageCount,
                        1.0f / VirtualTextureGPUDrivenTextureBackend.AtlasPageCount,
                        0.0f,
                        0.0f)));
                Assert.That(backend.AtlasEntryCount, Is.EqualTo(1));
                Assert.That(backend.StreamedAtlasEntryCount, Is.EqualTo(1));
                Assert.That(backend.AllocatedPageCount, Is.EqualTo(4));
                Assert.That(backend.QueuedMipTailPageCount, Is.EqualTo(4));

                UpdateOnce();
                Assert.That(VirtualTextureStatsRegistry.LastStats.CpuProducedPageCount, Is.EqualTo(4));
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
        public void CreateSurfaceBinding_ExpandedAtlasFitsSixteenMaxAllocationsAndReclaimsCapacity()
        {
            var textures = new System.Collections.Generic.List<Texture2D>();
            try
            {
                using var backend = new VirtualTextureGPUDrivenTextureBackend();
                int maxAllocationTextureWidth = VirtualTextureGPUDrivenTextureBackend.PageSize
                                                * VirtualTextureGPUDrivenTextureBackend.MaxAllocationPageCount;
                int maxAllocationCount = VirtualTextureGPUDrivenTextureBackend.VirtualPageCapacity
                                         / (VirtualTextureGPUDrivenTextureBackend.MaxAllocationPageCount
                                            * VirtualTextureGPUDrivenTextureBackend.MaxAllocationPageCount);

                for (int allocationIndex = 0; allocationIndex < maxAllocationCount; allocationIndex++)
                {
                    var wideTexture = new Texture2D(
                        maxAllocationTextureWidth,
                        1,
                        TextureFormat.RGBA32,
                        mipChain: false);
                    var tallTexture = new Texture2D(
                        1,
                        maxAllocationTextureWidth,
                        TextureFormat.RGBA32,
                        mipChain: false);
                    textures.Add(wideTexture);
                    textures.Add(tallTexture);
                    VividSurfaceBindingData binding = backend.CreateSurfaceBinding(
                        new GPUDrivenSurfaceTextureSet(wideTexture, tallTexture, null));

                    Assert.That(
                        binding.Flags,
                        Is.EqualTo(VividSurfaceBindingFlags.BaseColor | VividSurfaceBindingFlags.Normal));
                }

                Assert.That(maxAllocationCount, Is.EqualTo(16));
                Assert.That(backend.AtlasEntryCount, Is.EqualTo(16));
                Assert.That(backend.AllocatedPageCount, Is.EqualTo(
                    VirtualTextureGPUDrivenTextureBackend.VirtualPageCapacity));
                Assert.That(backend.LargestFreeAllocationPageCount, Is.Zero);
                Assert.That(backend.VirtualTextureSpaceDesc.CachePageCount, Is.EqualTo(512));
                GPUDrivenTextureBackendStats fullStats = backend.GetStats();
                Assert.That(fullStats.ResourceCapacity, Is.EqualTo(65536u));
                Assert.That(fullStats.AllocatedResourceCount, Is.EqualTo(65536u));
                Assert.That(fullStats.RegisteredResourceCount, Is.EqualTo(32));

                var rejectedWideTexture = new Texture2D(
                    maxAllocationTextureWidth,
                    1,
                    TextureFormat.RGBA32,
                    mipChain: false);
                var rejectedTallTexture = new Texture2D(
                    1,
                    maxAllocationTextureWidth,
                    TextureFormat.RGBA32,
                    mipChain: false);
                textures.Add(rejectedWideTexture);
                textures.Add(rejectedTallTexture);
                LogAssert.Expect(
                    LogType.Warning,
                    new System.Text.RegularExpressions.Regex(
                        "GPUDriven VT atlas is full.*Used 65536/65536 virtual pages.*0x0"));

                VividSurfaceBindingData rejectedBinding = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(rejectedWideTexture, rejectedTallTexture, null));

                Assert.That(rejectedBinding.Flags, Is.EqualTo(VividSurfaceBindingFlags.None));
                Assert.That(backend.AtlasAllocationFailureCount, Is.EqualTo(1));
                Assert.That(backend.LastAtlasAllocationFailureReason, Does.Contain("65536/65536"));

                backend.BeginSurfaceBindingUpdate();
                backend.EndSurfaceBindingUpdate();

                Assert.That(backend.AtlasEntryCount, Is.Zero);
                Assert.That(backend.AllocatedPageCount, Is.Zero);
                Assert.That(backend.LargestFreeAllocationPageCount, Is.EqualTo(
                    VirtualTextureGPUDrivenTextureBackend.MaxAllocationPageCount));

                var replacementWideTexture = new Texture2D(
                    maxAllocationTextureWidth,
                    1,
                    TextureFormat.RGBA32,
                    mipChain: false);
                var replacementTallTexture = new Texture2D(
                    1,
                    maxAllocationTextureWidth,
                    TextureFormat.RGBA32,
                    mipChain: false);
                textures.Add(replacementWideTexture);
                textures.Add(replacementTallTexture);
                VividSurfaceBindingData replacementBinding = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(replacementWideTexture, replacementTallTexture, null));

                Assert.That(
                    replacementBinding.Flags,
                    Is.EqualTo(VividSurfaceBindingFlags.BaseColor | VividSurfaceBindingFlags.Normal));
                Assert.That(replacementBinding.UVScaleBias.z, Is.Zero);
                Assert.That(replacementBinding.UVScaleBias.w, Is.Zero);
                Assert.That(backend.GetStats().AllocatedResourceCount, Is.EqualTo(4096u));
            }
            finally
            {
                for (int textureIndex = 0; textureIndex < textures.Count; textureIndex++)
                    Destroy(textures[textureIndex]);
            }
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
                var rejectedTexture = new Texture2D(256, 128);
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
                Assert.That(backend.QueuedMipTailPageCount, Is.EqualTo(availableTailPages));
                Assert.That(backend.BindingRevision, Is.EqualTo(revisionBeforeFailure));

                var replacementTexture = new Texture2D(1, 1);
                textures.Add(replacementTexture);
                VividSurfaceBindingData replacementBinding = backend.CreateSurfaceBinding(
                    new GPUDrivenSurfaceTextureSet(replacementTexture, null, null));
                Assert.That(replacementBinding.Flags, Is.EqualTo(VividSurfaceBindingFlags.BaseColor));
                Assert.That(backend.QueuedMipTailCount, Is.EqualTo(availableTailPages + 1));
                Assert.That(backend.QueuedMipTailPageCount, Is.EqualTo(availableTailPages + 1));
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
                    Is.EqualTo(new float4(
                        -1.0f / VirtualTextureGPUDrivenTextureBackend.AtlasPageCount,
                        -1.0f / VirtualTextureGPUDrivenTextureBackend.AtlasPageCount,
                        0.0f,
                        0.0f)));
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
                    pageCountX: 1,
                    pageCountY: 1,
                    pageSize: 128), Is.EqualTo(-1));
                Assert.That(GPUDrivenVirtualTextureProducer.ComputeSourceMipOffset(
                    matchingResolution,
                    pageCountX: 1,
                    pageCountY: 1,
                    pageSize: 128), Is.Zero);
                Assert.That(GPUDrivenVirtualTextureProducer.ComputeSourceMipOffset(
                    doubleResolution,
                    pageCountX: 1,
                    pageCountY: 1,
                    pageSize: 128), Is.EqualTo(1));
                Assert.That(GPUDrivenVirtualTextureProducer.ComputeSourceMipOffset(
                    doubleResolution,
                    pageCountX: 2,
                    pageCountY: 1,
                    pageSize: 128), Is.EqualTo(1));
            }
            finally
            {
                Destroy(halfResolution);
                Destroy(matchingResolution);
                Destroy(doubleResolution);
            }
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
