using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Unity.Jobs;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureSystemTests
    {
        private sealed class TestProducer : VTProducer
        {
            public string Name => nameof(TestProducer);
        }

        [TearDown]
        public void TearDown()
        {
            VirtualTextureSystem.Deinitialize();
        }

        [Test]
        public void Update_PopulatesFrameBindingAndCreatesFeedbackState_ForGameCamera()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("GameCamera", cachePageCount: 2, maxUploadsPerFrame: 1);
            int spaceId = VirtualTextureSystem.RegisterSpace(desc);

            var cameraGameObject = new GameObject("VTGameCamera");
            var commandBuffer = new CommandBuffer();

            try
            {
                Camera camera = cameraGameObject.AddComponent<Camera>();
                ContextContainer frameData = CreateFrameData(camera, frameIndex: 5);

                VirtualTextureSystem.Update(frameData, commandBuffer);

                VividVirtualTextureFrameData virtualTextureFrameData = frameData.Get<VividVirtualTextureFrameData>();
                Assert.That(virtualTextureFrameData, Is.Not.Null);
                Assert.That(virtualTextureFrameData.Bindings.Count, Is.EqualTo(1));

                VirtualTextureSpaceBinding binding = virtualTextureFrameData.Bindings.Single();
                Assert.That(binding.SpaceId, Is.EqualTo(spaceId));
                Assert.That(binding.BindingIndex, Is.EqualTo(0));
                Assert.That(binding.SpaceName, Is.EqualTo(desc.SpaceName));
                Assert.That(binding.AllocationId, Is.GreaterThan(0));
                Assert.That(binding.ProducerHandle.IsValid, Is.True);
                Assert.That(binding.PageTableBuffer, Is.Not.Null);
                Assert.That(binding.PageTableBuffer.count, Is.EqualTo(binding.ShaderParams.PageTableEntryCount));
                Assert.That(binding.PhysicalCache, Is.Not.Null);
                AssertPhysicalAtlas(binding.PhysicalCache, desc, groupLayerCount: 1);
                Assert.That(binding.HasFeedback, Is.True);
                Assert.That(binding.FeedbackCounter.count, Is.EqualTo(8));
                Assert.That(binding.FeedbackRequests.stride, Is.EqualTo(VirtualTextureCompactedFeedbackRequest.Stride));
                Assert.That(binding.FeedbackRequestCapacity, Is.EqualTo(desc.FeedbackCapacity));
                Assert.That(binding.ShaderParams.SpaceId, Is.EqualTo(spaceId));
                Assert.That(binding.ShaderParams.PageSize, Is.EqualTo(desc.PageSize));
                Assert.That(binding.MipOffsets, Is.EqualTo(VirtualTextureSpaceUtility.BuildMipOffsets(
                    desc.VirtualPageCountX,
                    desc.VirtualPageCountY,
                    desc.MipCount)));
                Assert.That(VirtualTextureSystem.IsCameraFeedbackStateCreatedForTesting(camera), Is.True);
                VTDebugStats stats = VirtualTextureStatsRegistry.LastStats.Stats;
                long bytesPerPage = (long)desc.PhysicalPageSize * desc.PhysicalPageSize * 4;
                Assert.That(stats.PhysicalPoolAllocatedByteCount, Is.EqualTo(bytesPerPage * 2));
                Assert.That(stats.PhysicalPoolResidentByteCount, Is.EqualTo(bytesPerPage));
                Assert.That(stats.PageTableByteCount, Is.EqualTo((long)desc.PageTableEntryCount * sizeof(uint)));
                Assert.That(
                    stats.GpuAllocatedByteCount,
                    Is.EqualTo(stats.PhysicalPoolAllocatedByteCount + stats.PageTableByteCount));
            }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(cameraGameObject);
            }
        }

        [Test]
        public void TryMakePageResident_ProducesAndLocksRequestedPageImmediately()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("ResidentPage", cachePageCount: 3, maxUploadsPerFrame: 1);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, VTProceduralPageProducer.Instance);
            var coord = new VirtualTexturePageCoord(1, 2, 0);

            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(
                VirtualTextureSystem.TryMakePageResident(spaceId, coord, locked: true, frameIndex: 7),
                Is.True);
            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(2));
            Assert.That(
                VirtualTextureSystem.TryGetPageTableEntryForTesting(
                    spaceId,
                    coord,
                    out VirtualTexturePageTableEntry entry),
                Is.True);
            Assert.That(entry.Resident, Is.True);
            Assert.That(entry.Fallback, Is.False);
            Assert.That(entry.PendingUpload, Is.False);
            Assert.That(entry.Locked, Is.True);
            Assert.That(entry.ResolvedMip, Is.EqualTo(0));
        }

        [Test]
        public void RequestRuntimeStateReset_DefersUntilUpdateAndPreservesRegisteredAllocation()
        {
            VirtualTextureSpaceDesc desc = CreateDesc(
                "ResetRuntimeState",
                cachePageCount: 4,
                maxUploadsPerFrame: 1);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(
                desc,
                VTProceduralPageProducer.Instance);
            var coord = new VirtualTexturePageCoord(1, 2, 0);
            Assert.That(
                VirtualTextureSystem.TryGetAllocationForTesting(
                    spaceId,
                    out VTAllocatedVirtualTexture allocation),
                Is.True);
            Assert.That(
                VirtualTextureSystem.TryMakePageResident(
                    spaceId,
                    coord,
                    locked: true,
                    frameIndex: 7),
                Is.True);
            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(2));
            Assert.That(
                VirtualTextureSystem.TryGetPhysicalCacheForTesting(
                    spaceId,
                    out Texture2D physicalAtlasBeforeReset),
                Is.True);
            int physicalAtlasWidth = physicalAtlasBeforeReset.width;
            int physicalAtlasHeight = physicalAtlasBeforeReset.height;
            GraphicsFormat physicalAtlasFormat = physicalAtlasBeforeReset.graphicsFormat;

            VirtualTextureSystem.RequestRuntimeStateReset();

            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(2));

            var commandBuffer = new CommandBuffer();
            try
            {
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }

            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.Zero);
            Assert.That(
                VirtualTextureSystem.TryGetAllocationForTesting(
                    spaceId,
                    out VTAllocatedVirtualTexture resetAllocation),
                Is.True);
            Assert.That(resetAllocation, Is.SameAs(allocation));
            Assert.That(
                VirtualTextureSystem.TryGetPhysicalCacheForTesting(
                    spaceId,
                    out Texture2D physicalAtlasAfterReset),
                Is.True);
            Assert.That(ReferenceEquals(physicalAtlasAfterReset, physicalAtlasBeforeReset), Is.False);
            Assert.That(physicalAtlasAfterReset.width, Is.EqualTo(physicalAtlasWidth));
            Assert.That(physicalAtlasAfterReset.height, Is.EqualTo(physicalAtlasHeight));
            Assert.That(physicalAtlasAfterReset.graphicsFormat, Is.EqualTo(physicalAtlasFormat));
            Assert.That(
                VirtualTextureSystem.TryGetPageTableEntryForTesting(
                    spaceId,
                    coord,
                    out VirtualTexturePageTableEntry entry),
                Is.True);
            Assert.That(entry.Fallback, Is.True);
            Assert.That(entry.ResolvedMip, Is.EqualTo(desc.MipCount - 1));
        }

        [Test]
        public void RegisterProducerAndAllocateVirtualTexture_CreateSampleableAllocationBinding()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("AllocatedVT", cachePageCount: 2, maxUploadsPerFrame: 1);
            var producer = new TestProducer();
            VTProducerHandle producerHandle = VirtualTextureSystem.RegisterProducer(desc, producer);
            VTAllocatedVirtualTexture allocation = VirtualTextureSystem.AllocateVirtualTexture(
                VTAllocationDesc.FromSpaceDesc(desc, producerHandle));
            var frameData = new ContextContainer();
            var commandBuffer = new CommandBuffer();

            try
            {
                Assert.That(producerHandle.IsValid, Is.True);
                Assert.That(allocation.IsValid, Is.True);
                Assert.That(allocation.ProducerHandle, Is.EqualTo(producerHandle));
                Assert.That(allocation.SpaceDesc, Is.EqualTo(desc));
                Assert.That(VirtualTextureSystem.TryGetAllocationForTesting(
                    allocation.SpaceId,
                    out VTAllocatedVirtualTexture storedAllocation), Is.True);
                Assert.That(storedAllocation, Is.SameAs(allocation));
                Assert.That(VirtualTextureSystem.TryGetProducerNameForTesting(
                    allocation.SpaceId,
                    out string producerName), Is.True);
                Assert.That(producerName, Is.EqualTo(nameof(TestProducer)));

                VirtualTextureSystem.Update(frameData, commandBuffer);

                VividVirtualTextureFrameData virtualTextureFrameData = frameData.Get<VividVirtualTextureFrameData>();
                Assert.That(virtualTextureFrameData.BindingCount, Is.EqualTo(1));
                Assert.That(virtualTextureFrameData.TryGetBinding(0, out VirtualTextureSpaceBinding binding), Is.True);
                Assert.That(binding.BindingIndex, Is.EqualTo(0));
                Assert.That(binding.AllocationId, Is.EqualTo(allocation.AllocationId));
                Assert.That(binding.ProducerHandle, Is.EqualTo(producerHandle));
                Assert.That(virtualTextureFrameData.TryGetBindingForAllocation(
                    allocation.AllocationId,
                    out VirtualTextureSpaceBinding allocationBinding), Is.True);
                Assert.That(allocationBinding.SpaceId, Is.EqualTo(allocation.SpaceId));
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        [Test]
        public void Update_PopulatesBindingTable_ForMultipleAllocatedVirtualTextures()
        {
            int firstSpaceId = VirtualTextureSystem.RegisterSpace(
                CreateDesc("BindingTableA", cachePageCount: 4, maxUploadsPerFrame: 1));
            int secondSpaceId = VirtualTextureSystem.RegisterSpace(
                CreateDesc("BindingTableB", cachePageCount: 4, maxUploadsPerFrame: 1));
            var frameData = new ContextContainer();
            var commandBuffer = new CommandBuffer();

            try
            {
                Assert.That(VirtualTextureSystem.TryGetAllocationForTesting(
                    firstSpaceId,
                    out VTAllocatedVirtualTexture firstAllocation), Is.True);
                Assert.That(VirtualTextureSystem.TryGetAllocationForTesting(
                    secondSpaceId,
                    out VTAllocatedVirtualTexture secondAllocation), Is.True);

                VirtualTextureSystem.Update(frameData, commandBuffer);

                VividVirtualTextureFrameData virtualTextureFrameData = frameData.Get<VividVirtualTextureFrameData>();
                Assert.That(virtualTextureFrameData.BindingCount, Is.EqualTo(2));
                Assert.That(virtualTextureFrameData.TryGetBinding(0, out VirtualTextureSpaceBinding firstBinding), Is.True);
                Assert.That(virtualTextureFrameData.TryGetBinding(1, out VirtualTextureSpaceBinding secondBinding), Is.True);
                Assert.That(firstBinding.BindingIndex, Is.EqualTo(0));
                Assert.That(secondBinding.BindingIndex, Is.EqualTo(1));
                Assert.That(firstBinding.AllocationId, Is.EqualTo(firstAllocation.AllocationId));
                Assert.That(secondBinding.AllocationId, Is.EqualTo(secondAllocation.AllocationId));
                Assert.That(virtualTextureFrameData.TryGetBindingForAllocation(
                    secondAllocation.AllocationId,
                    out VirtualTextureSpaceBinding lookupBinding), Is.True);
                Assert.That(lookupBinding.SpaceId, Is.EqualTo(secondSpaceId));
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        [TestCase(50, true, 8, true)]
        [TestCase(49, true, 8, false)]
        [TestCase(50, false, 8, false)]
        [TestCase(50, true, 7, false)]
        public void FeedbackPlatformSupport_RequiresSm5ReadbackAndAllEightOutputSlots(
            int graphicsShaderLevel,
            bool supportsAsyncGpuReadback,
            int supportedRandomWriteTargetCount,
            bool expected)
        {
            Assert.That(
                VirtualTextureSystem.IsFeedbackPlatformSupported(
                    graphicsShaderLevel,
                    supportsAsyncGpuReadback,
                    supportedRandomWriteTargetCount),
                Is.EqualTo(expected));
        }

        [Test]
        public void Update_SharesOneCompactedFeedbackStreamAcrossSpacesForCamera()
        {
            VirtualTextureSystem.RegisterSpace(CreateDesc(
                "SharedFeedbackA",
                cachePageCount: 4,
                maxUploadsPerFrame: 1,
                feedbackCapacity: 32));
            VirtualTextureSystem.RegisterSpace(CreateDesc(
                "SharedFeedbackB",
                cachePageCount: 8,
                maxUploadsPerFrame: 1,
                feedbackCapacity: 64));
            var cameraGameObject = new GameObject("VTSharedFeedbackCamera");
            var commandBuffer = new CommandBuffer();

            try
            {
                Camera camera = cameraGameObject.AddComponent<Camera>();
                ContextContainer frameData = CreateFrameData(camera, frameIndex: 21);

                VirtualTextureSystem.Update(frameData, commandBuffer);

                VividVirtualTextureFrameData virtualTextureFrameData =
                    frameData.Get<VividVirtualTextureFrameData>();
                Assert.That(virtualTextureFrameData.BindingCount, Is.EqualTo(2));
                VirtualTextureSpaceBinding first = virtualTextureFrameData.Bindings[0];
                VirtualTextureSpaceBinding second = virtualTextureFrameData.Bindings[1];
                Assert.That(second.FeedbackRequests, Is.SameAs(first.FeedbackRequests));
                Assert.That(second.FeedbackCounter, Is.SameAs(first.FeedbackCounter));
                Assert.That(second.FeedbackResidentHash, Is.SameAs(first.FeedbackResidentHash));
                Assert.That(first.FeedbackRequestCapacity, Is.EqualTo(96));
                Assert.That(second.FeedbackRequestCapacity, Is.EqualTo(96));
                Assert.That(first.FeedbackRequests.count, Is.EqualTo(96));
                Assert.That(
                    first.FeedbackResidentHashCapacity,
                    Is.EqualTo(VirtualTextureFeedbackBufferState.ResolveFeedbackHashCapacityForTesting(
                        feedbackCapacity: 96,
                        pageCapacity: 12)));
                Assert.That(second.FeedbackResidentHashCapacity, Is.EqualTo(first.FeedbackResidentHashCapacity));
            }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(cameraGameObject);
            }
        }

        [Test]
        public void Update_KeepsCompactedFeedbackStreamsSeparateAcrossCameras()
        {
            VirtualTextureSystem.RegisterSpace(CreateDesc(
                "PerCameraFeedback",
                cachePageCount: 4,
                maxUploadsPerFrame: 1,
                feedbackCapacity: 32));
            var firstCameraObject = new GameObject("VTFeedbackCameraA");
            var secondCameraObject = new GameObject("VTFeedbackCameraB");
            var commandBuffer = new CommandBuffer();

            try
            {
                Camera firstCamera = firstCameraObject.AddComponent<Camera>();
                Camera secondCamera = secondCameraObject.AddComponent<Camera>();
                ContextContainer firstFrameData = CreateFrameData(firstCamera, frameIndex: 31);
                ContextContainer secondFrameData = CreateFrameData(secondCamera, frameIndex: 31);

                VirtualTextureSystem.Update(firstFrameData, commandBuffer);
                commandBuffer.Clear();
                VirtualTextureSystem.Update(secondFrameData, commandBuffer);

                VirtualTextureSpaceBinding firstBinding =
                    firstFrameData.Get<VividVirtualTextureFrameData>().Bindings.Single();
                VirtualTextureSpaceBinding secondBinding =
                    secondFrameData.Get<VividVirtualTextureFrameData>().Bindings.Single();
                Assert.That(secondBinding.FeedbackRequests, Is.Not.SameAs(firstBinding.FeedbackRequests));
                Assert.That(secondBinding.FeedbackCounter, Is.Not.SameAs(firstBinding.FeedbackCounter));
                Assert.That(secondBinding.FeedbackResidentHash, Is.Not.SameAs(firstBinding.FeedbackResidentHash));
            }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(firstCameraObject);
                Object.DestroyImmediate(secondCameraObject);
            }
        }

        [Test]
        public void TryGetDefaultBinding_SkipsPrivateAllocations()
        {
            VirtualTextureSpaceDesc privateDesc = CreateDesc(
                "PrivateBinding",
                cachePageCount: 4,
                maxUploadsPerFrame: 1);
            VTProducerHandle privateProducer = VirtualTextureSystem.RegisterProducer(privateDesc, new TestProducer());
            VTAllocatedVirtualTexture privateAllocation = VirtualTextureSystem.AllocateVirtualTexture(
                new VTAllocationDesc(
                    privateDesc.SpaceName,
                    privateDesc,
                    privateProducer,
                    privateSpace: true));
            int publicSpaceId = VirtualTextureSystem.RegisterSpace(
                CreateDesc("PublicBinding", cachePageCount: 4, maxUploadsPerFrame: 1));
            var frameData = new ContextContainer();
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.Update(frameData, commandBuffer);

                VividVirtualTextureFrameData virtualTextureFrameData = frameData.Get<VividVirtualTextureFrameData>();
                Assert.That(virtualTextureFrameData.BindingCount, Is.EqualTo(2));
                Assert.That(
                    virtualTextureFrameData.TryGetBindingForAllocation(
                        privateAllocation.AllocationId,
                        out VirtualTextureSpaceBinding privateBinding),
                    Is.True);
                Assert.That(privateBinding.PrivateSpace, Is.True);
                Assert.That(virtualTextureFrameData.TryGetDefaultBinding(out VirtualTextureSpaceBinding defaultBinding), Is.True);
                Assert.That(defaultBinding.SpaceId, Is.EqualTo(publicSpaceId));
                Assert.That(defaultBinding.PrivateSpace, Is.False);
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        [Test]
        public void Update_DoesNotCreateFeedbackState_ForPreviewCamera()
        {
            VirtualTextureSystem.RegisterSpace(CreateDesc("PreviewCamera", cachePageCount: 2, maxUploadsPerFrame: 1));

            PreviewRenderUtility previewRenderUtility = new();
            var commandBuffer = new CommandBuffer();

            try
            {
                Camera previewCamera = previewRenderUtility.camera;
                Assert.That(previewCamera.cameraType, Is.EqualTo(CameraType.Preview));

                ContextContainer frameData = CreateFrameData(previewCamera, frameIndex: 8);

                VirtualTextureSystem.Update(frameData, commandBuffer);

                VividVirtualTextureFrameData virtualTextureFrameData = frameData.Get<VividVirtualTextureFrameData>();
                Assert.That(virtualTextureFrameData, Is.Not.Null);
                Assert.That(virtualTextureFrameData.Bindings.Count, Is.EqualTo(1));
                Assert.That(virtualTextureFrameData.Bindings[0].HasFeedback, Is.False);
                Assert.That(virtualTextureFrameData.Bindings[0].FeedbackRequests, Is.Null);
                Assert.That(virtualTextureFrameData.Bindings[0].FeedbackCounter, Is.Null);
                Assert.That(VirtualTextureSystem.IsCameraFeedbackStateCreatedForTesting(previewCamera), Is.False);
            }
            finally
            {
                commandBuffer.Dispose();
                previewRenderUtility.Cleanup();
            }
        }

        [Test]
        public void Update_RespectsMaxUploadsPerFrameAndReportsStats()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("UploadBudget", cachePageCount: 4, maxUploadsPerFrame: 1));

            ulong highPriority = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(2, 0, 0));
            ulong lowPriority = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(0, 0, 0));

            VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.SceneView, lowPriority);
            VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, highPriority, highPriority);

            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }

            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var requests), Is.True);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(2, 0, 0)));
            Assert.That(requests[0].Priority, Is.EqualTo(2));

            VirtualTextureStats stats = VirtualTextureStatsRegistry.LastStats;
            Assert.That(stats.ActiveSpaceCount, Is.EqualTo(1));
            Assert.That(stats.PendingUploadCount, Is.EqualTo(1));
            Assert.That(stats.FreePageCount, Is.EqualTo(2));
            Assert.That(stats.ResidentPageCount, Is.EqualTo(1));
            Assert.That(stats.EvictionCount, Is.EqualTo(0));
            Assert.That(stats.FaultCount, Is.EqualTo(3));
            Assert.That(stats.DeduplicatedRequestCount, Is.EqualTo(2));
            Assert.That(stats.FeedbackOverflowCount, Is.EqualTo(0));
            Assert.That(stats.InFlightUploadBatchCount, Is.EqualTo(0));
            Assert.That(stats.DuplicateUploadCount, Is.EqualTo(0));
            Assert.That(stats.SkippedUploadCount, Is.EqualTo(1));
            Assert.That(stats.FallbackSampleCount, Is.EqualTo(0));
        }

        [Test]
        public void Update_DoesNotReportResidentAccessFeedbackAsFaults()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc(
                "ResidentAccessStats",
                cachePageCount: 4,
                maxUploadsPerFrame: 1));
            ulong residentRoot = VirtualTextureFeedbackProcessor.EncodeKey(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 2));
            VirtualTextureSystem.InjectCompletedResidentAccessReadbackForTesting(
                CameraType.Game,
                residentRoot);
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }

            VirtualTextureStats stats = VirtualTextureStatsRegistry.LastStats;
            Assert.That(stats.FaultCount, Is.EqualTo(0));
            Assert.That(stats.DeduplicatedRequestCount, Is.EqualTo(1));
        }

        [Test]
        public void Update_PrioritizesActiveViewFeedback_WhenBackgroundViewHasMoreHits()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("ActiveViewPriority", cachePageCount: 4, maxUploadsPerFrame: 1));
            ulong activeViewRequest = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            ulong backgroundViewRequest = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(1, 0, 0));

            var activeCameraObject = new GameObject("VTActiveViewCamera");
            var backgroundCameraObject = new GameObject("VTBackgroundViewCamera");
            var commandBuffer = new CommandBuffer();

            try
            {
                Camera activeCamera = activeCameraObject.AddComponent<Camera>();
                Camera backgroundCamera = backgroundCameraObject.AddComponent<Camera>();

                VirtualTextureSystem.InjectCompletedReadbackForTesting(
                    backgroundCamera,
                    backgroundViewRequest,
                    backgroundViewRequest,
                    backgroundViewRequest);
                VirtualTextureSystem.InjectCompletedReadbackForTesting(activeCamera, activeViewRequest);

                VirtualTextureSystem.Update(CreateFrameData(activeCamera, frameIndex: 29), commandBuffer);

                Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var requests), Is.True);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 0)));
            Assert.That(requests[0].Priority, Is.EqualTo(1));
            Assert.That(requests[0].CameraPriority, Is.EqualTo(0));
            Assert.That(requests[0].IsActiveView, Is.True);
        }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(activeCameraObject);
                Object.DestroyImmediate(backgroundCameraObject);
            }
        }

        [Test]
        public void Update_PopulatesLayerStackBinding_ForBaseColorAndNormal()
        {
            var baseFallback = new Color32(4, 8, 12, 255);
            var normalFallback = new Color32(128, 128, 255, 255);
            var stackDesc = new VTStackDesc(
                pageSize: 128,
                borderSize: 4,
                cachePageCount: 2,
                layers: new[]
                {
                    new VTLayerDesc(
                        VTLayerSemantic.BaseColor,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        sRGB: true,
                        baseFallback),
                    new VTLayerDesc(
                        VTLayerSemantic.Normal,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        sRGB: false,
                        normalFallback),
                },
                maxUploadsPerFrame: 1,
                feedbackCapacity: 32);
            var desc = new VirtualTextureSpaceDesc("LayerBinding", 4, 4, 3, stackDesc);
            int spaceId = VirtualTextureSystem.RegisterSpace(desc);
            var frameData = new ContextContainer();
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.Update(frameData, commandBuffer);

                VirtualTextureSpaceBinding binding = frameData.Get<VividVirtualTextureFrameData>().Bindings.Single();
                Assert.That(binding.SpaceId, Is.EqualTo(spaceId));
                AssertPhysicalAtlas(binding.PhysicalCache, desc, stackDesc.LayerCount);
                Assert.That(binding.ShaderParams.LayerCount, Is.EqualTo(2));
                Assert.That(binding.ShaderParams.BaseColorLayerIndex, Is.EqualTo(0));
                Assert.That(binding.ShaderParams.NormalLayerIndex, Is.EqualTo(1));
                Assert.That(binding.ShaderParams.Layer0SRGB, Is.EqualTo(1));
                Assert.That(binding.ShaderParams.Layer1SRGB, Is.EqualTo(0));
                Assert.That(binding.ShaderParams.PhysicalGroup0LayerCount, Is.EqualTo(2));
                Assert.That(binding.ShaderParams.PhysicalGroup1LayerCount, Is.EqualTo(0));
                Assert.That(binding.ShaderParams.Layer0PhysicalGroup, Is.EqualTo(0));
                Assert.That(binding.ShaderParams.Layer1PhysicalGroup, Is.EqualTo(0));
                Assert.That(binding.ShaderParams.Layer0PhysicalLayerIndex, Is.EqualTo(0));
                Assert.That(binding.ShaderParams.Layer1PhysicalLayerIndex, Is.EqualTo(1));
                Assert.That(binding.LayerFallbacks[0], Is.EqualTo(ToVector(baseFallback)));
                Assert.That(binding.LayerFallbacks[1], Is.EqualTo(ToVector(normalFallback)));
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        [Test]
        public void Update_BindsPhysicalCachesAndShaderParams_ForSplitPhysicalGroups()
        {
            var stackDesc = new VTStackDesc(
                pageSize: 64,
                borderSize: 2,
                cachePageCount: 3,
                layers: new[]
                {
                    new VTLayerDesc(
                        VTLayerSemantic.BaseColor,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        sRGB: true,
                        new Color32(0, 0, 0, 255),
                        physicalGroup: 0),
                    new VTLayerDesc(
                        VTLayerSemantic.Normal,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        sRGB: false,
                        new Color32(128, 128, 255, 255),
                        physicalGroup: 1),
                },
                maxUploadsPerFrame: 1,
                feedbackCapacity: 32);
            var desc = new VirtualTextureSpaceDesc("SplitPhysicalBinding", 4, 4, 3, stackDesc);
            VirtualTextureSystem.RegisterSpace(desc);
            var frameData = new ContextContainer();
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.Update(frameData, commandBuffer);

                VirtualTextureSpaceBinding binding = frameData.Get<VividVirtualTextureFrameData>().Bindings.Single();
                Assert.That(binding.PhysicalCaches.Count, Is.EqualTo(2));
                Assert.That(binding.PhysicalCaches[0], Is.Not.Null);
                Assert.That(binding.PhysicalCaches[1], Is.Not.Null);
                Assert.That(ReferenceEquals(binding.PhysicalCaches[0], binding.PhysicalCaches[1]), Is.False);
                AssertPhysicalAtlas(binding.PhysicalCaches[0], desc, groupLayerCount: 1);
                AssertPhysicalAtlas(binding.PhysicalCaches[1], desc, groupLayerCount: 1);
                Assert.That(binding.ShaderParams.PhysicalGroup0LayerCount, Is.EqualTo(1));
                Assert.That(binding.ShaderParams.PhysicalGroup1LayerCount, Is.EqualTo(1));
                Assert.That(binding.ShaderParams.Layer0PhysicalGroup, Is.EqualTo(0));
                Assert.That(binding.ShaderParams.Layer1PhysicalGroup, Is.EqualTo(1));
                Assert.That(binding.ShaderParams.Layer0PhysicalLayerIndex, Is.EqualTo(0));
                Assert.That(binding.ShaderParams.Layer1PhysicalLayerIndex, Is.EqualTo(0));
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        [Test]
        public void Update_MergesBackgroundViewFeedback_DuringActiveCameraUpdate()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("ViewFeedbackIsolation", cachePageCount: 4, maxUploadsPerFrame: 2));
            ulong backgroundViewRequest = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(1, 0, 0));

            var activeCameraObject = new GameObject("VTActiveIsolationCamera");
            var backgroundCameraObject = new GameObject("VTBackgroundIsolationCamera");
            var commandBuffer = new CommandBuffer();

            try
            {
                Camera activeCamera = activeCameraObject.AddComponent<Camera>();
                Camera backgroundCamera = backgroundCameraObject.AddComponent<Camera>();

                VirtualTextureSystem.InjectCompletedReadbackForTesting(backgroundCamera, backgroundViewRequest);
                VirtualTextureSystem.Update(CreateFrameData(activeCamera, frameIndex: 31), commandBuffer);

                Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var activeRequests), Is.True);
                Assert.That(activeRequests.Count, Is.EqualTo(1));
                Assert.That(activeRequests[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(1, 0, 0)));
                Assert.That(VirtualTextureStatsRegistry.LastStats.FaultCount, Is.EqualTo(1));

                VirtualTextureSystem.Update(CreateFrameData(backgroundCamera, frameIndex: 32), commandBuffer);

                Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var backgroundRequests), Is.True);
                Assert.That(backgroundRequests.Count, Is.EqualTo(1));
                Assert.That(backgroundRequests[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(1, 0, 0)));
                Assert.That(VirtualTextureStatsRegistry.LastStats.FaultCount, Is.EqualTo(0));
            }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(activeCameraObject);
                Object.DestroyImmediate(backgroundCameraObject);
            }
        }

        [Test]
        public void Update_ReportsFeedbackOverflowAndFallbackSamples_WhenReadbackCountersIncludeDebugValues()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("FeedbackCounters", cachePageCount: 2, maxUploadsPerFrame: 1));
            ulong requestKey = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            var commandBuffer = new CommandBuffer();
            var frameData = new ContextContainer();

            try
            {
                VirtualTextureSystem.InjectCompletedReadbackStatsForTesting(
                    CameraType.Game,
                    3,
                    11,
                    requestKey);
                VirtualTextureSystem.Update(frameData, commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }

            VirtualTextureStats stats = VirtualTextureStatsRegistry.LastStats;
            Assert.That(stats.FaultCount, Is.EqualTo(1));
            Assert.That(stats.FeedbackOverflowCount, Is.EqualTo(3));
            Assert.That(stats.FallbackSampleCount, Is.EqualTo(11));
            Assert.That(stats.AdaptiveMipBias, Is.EqualTo(0.5f));
            Assert.That(frameData.Get<VividVirtualTextureFrameData>().AdaptiveMipBias, Is.EqualTo(0.5f));
            Assert.That(VirtualTextureSystem.GetAdaptiveMipBiasForTesting(), Is.EqualTo(0.5f));
        }

        [Test]
        public void Update_AppliesDebugMipBiasOverride_WithoutFreezingAdaptiveController()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc(
                "FixedAdaptiveMipBias",
                cachePageCount: 2,
                maxUploadsPerFrame: 1));
            ulong requestKey = VirtualTextureFeedbackProcessor.EncodeKey(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 0));
            var commandBuffer = new CommandBuffer();
            var frameData = new ContextContainer();

            try
            {
                VividRenderingDebugDisplaySettings.Data.virtualTextureAdaptiveMipBiasOverride = 2f;
                VirtualTextureSystem.InjectCompletedReadbackStatsForTesting(
                    CameraType.Game,
                    1,
                    0,
                    requestKey);

                VirtualTextureSystem.Update(frameData, commandBuffer);

                Assert.That(VirtualTextureStatsRegistry.LastStats.AdaptiveMipBias, Is.EqualTo(2f));
                Assert.That(
                    frameData.Get<VividVirtualTextureFrameData>().AdaptiveMipBias,
                    Is.EqualTo(2f));
                Assert.That(VirtualTextureSystem.GetAdaptiveMipBiasForTesting(), Is.EqualTo(0.5f));
            }
            finally
            {
                VividRenderingDebugDisplaySettings.Data.virtualTextureAdaptiveMipBiasOverride =
                    VividRenderingDebugSettingsData.DefaultVirtualTextureAdaptiveMipBiasOverride;
                commandBuffer.Dispose();
            }
        }

        [Test]
        public void Update_ZeroDebugFeedbackPressureOverrides_SuppressMeasuredPressureWithoutReplacingStats()
        {
            VirtualTextureSystem.RegisterSpace(CreateDesc(
                "FeedbackPressureOverrides",
                cachePageCount: 2,
                maxUploadsPerFrame: 1));
            var commandBuffer = new CommandBuffer();
            var frameData = new ContextContainer();

            try
            {
                VividRenderingDebugDisplaySettings.Data.virtualTextureFeedbackOverflowCountOverride = 0;
                VividRenderingDebugDisplaySettings.Data.virtualTextureFallbackSampleCountOverride = 0;
                VirtualTextureSystem.InjectCompletedReadbackStatsForTesting(
                    CameraType.Game,
                    3,
                    11);

                VirtualTextureSystem.Update(frameData, commandBuffer);

                VirtualTextureStats stats = VirtualTextureStatsRegistry.LastStats;
                Assert.That(stats.FeedbackOverflowCount, Is.EqualTo(3));
                Assert.That(stats.FallbackSampleCount, Is.EqualTo(11));
                Assert.That(stats.AdaptiveMipBias, Is.Zero);
                Assert.That(frameData.Get<VividVirtualTextureFrameData>().AdaptiveMipBias, Is.Zero);
                Assert.That(VirtualTextureSystem.GetAdaptiveMipBiasForTesting(), Is.Zero);
                Assert.That(VirtualTextureSystem.AdaptiveFeedbackOverflowInputCount, Is.Zero);
                Assert.That(VirtualTextureSystem.AdaptiveFallbackSampleInputCount, Is.Zero);
                Assert.That(VirtualTextureSystem.AdaptiveMeasuredFeedbackOverflowCount, Is.EqualTo(3));
                Assert.That(VirtualTextureSystem.AdaptiveMeasuredFallbackSampleCount, Is.EqualTo(11));
                Assert.That(VirtualTextureSystem.AdaptiveMeasuredFaultOverflowCount, Is.EqualTo(3));
                Assert.That(VirtualTextureSystem.AdaptiveMeasuredResidentOverflowCount, Is.Zero);
                Assert.That(
                    VirtualTextureSystem.AdaptiveMeasuredNonResidentFallbackSampleCount,
                    Is.EqualTo(11));
                Assert.That(VirtualTextureSystem.AdaptiveMeasuredResidentFallbackSampleCount, Is.Zero);
                Assert.That(
                    VirtualTextureSystem.AdaptiveMeasuredWeightedResolvedSampleCount,
                    Is.EqualTo(11));
                Assert.That(VirtualTextureSystem.AdaptiveFeedbackOverflowPressure, Is.Zero);
                Assert.That(VirtualTextureSystem.AdaptiveFallbackPressure, Is.Zero);
                Assert.That(VirtualTextureSystem.AdaptiveTotalPressure, Is.Zero);
                Assert.That(VirtualTextureSystem.AdaptiveTargetMipBias, Is.Zero);
                Assert.That(VirtualTextureSystem.AdaptiveFeedbackMeasurementWasFresh, Is.True);
                Assert.That(VirtualTextureSystem.AdaptiveLastFreshFeedbackOverflowCount, Is.EqualTo(3));
                Assert.That(VirtualTextureSystem.AdaptiveLastFreshFallbackSampleCount, Is.EqualTo(11));
                Assert.That(VirtualTextureSystem.AdaptiveLastFreshFaultOverflowCount, Is.EqualTo(3));
                Assert.That(VirtualTextureSystem.AdaptiveLastFreshResidentOverflowCount, Is.Zero);
                Assert.That(
                    VirtualTextureSystem.AdaptiveLastFreshNonResidentFallbackSampleCount,
                    Is.EqualTo(11));
                Assert.That(VirtualTextureSystem.AdaptiveLastFreshResidentFallbackSampleCount, Is.Zero);
                Assert.That(
                    VirtualTextureSystem.AdaptiveLastFreshWeightedResolvedSampleCount,
                    Is.EqualTo(11));
                Assert.That(VirtualTextureSystem.FeedbackRequestReadbackErrorCount, Is.Zero);
                Assert.That(VirtualTextureSystem.FeedbackCounterReadbackErrorCount, Is.Zero);
            }
            finally
            {
                VividRenderingDebugDisplaySettings.Data.virtualTextureFeedbackOverflowCountOverride =
                    VividRenderingDebugSettingsData.DefaultVirtualTextureFeedbackOverflowCountOverride;
                VividRenderingDebugDisplaySettings.Data.virtualTextureFallbackSampleCountOverride =
                    VividRenderingDebugSettingsData.DefaultVirtualTextureFallbackSampleCountOverride;
                commandBuffer.Dispose();
            }
        }

        [TestCase(1, -1)]
        [TestCase(-1, int.MaxValue)]
        public void Update_PositiveDebugFeedbackPressureOverrides_DriveAdaptiveBiasWithoutReplacingStats(
            int feedbackOverflowCountOverride,
            int fallbackSampleCountOverride)
        {
            VirtualTextureSystem.RegisterSpace(CreateDesc(
                "InjectedFeedbackPressure",
                cachePageCount: 2,
                maxUploadsPerFrame: 1));
            var commandBuffer = new CommandBuffer();
            var frameData = new ContextContainer();

            try
            {
                VividRenderingDebugDisplaySettings.Data.virtualTextureFeedbackOverflowCountOverride =
                    feedbackOverflowCountOverride;
                VividRenderingDebugDisplaySettings.Data.virtualTextureFallbackSampleCountOverride =
                    fallbackSampleCountOverride;

                VirtualTextureSystem.Update(frameData, commandBuffer);

                VirtualTextureStats stats = VirtualTextureStatsRegistry.LastStats;
                Assert.That(stats.FeedbackOverflowCount, Is.Zero);
                Assert.That(stats.FallbackSampleCount, Is.Zero);
                Assert.That(stats.AdaptiveMipBias, Is.EqualTo(0.5f));
                Assert.That(
                    frameData.Get<VividVirtualTextureFrameData>().AdaptiveMipBias,
                    Is.EqualTo(0.5f));
                Assert.That(VirtualTextureSystem.GetAdaptiveMipBiasForTesting(), Is.EqualTo(0.5f));
            }
            finally
            {
                VividRenderingDebugDisplaySettings.Data.virtualTextureFeedbackOverflowCountOverride =
                    VividRenderingDebugSettingsData.DefaultVirtualTextureFeedbackOverflowCountOverride;
                VividRenderingDebugDisplaySettings.Data.virtualTextureFallbackSampleCountOverride =
                    VividRenderingDebugSettingsData.DefaultVirtualTextureFallbackSampleCountOverride;
                commandBuffer.Dispose();
            }
        }

        [Test]
        public void Update_CarriesFeedbackConsumedByLaterCameraUpdateIntoNextControllerFrame()
        {
            VirtualTextureSystem.RegisterSpace(CreateDesc(
                "DeferredAdaptiveFeedback",
                cachePageCount: 2,
                maxUploadsPerFrame: 1));
            var cameraGameObject = new GameObject("DeferredAdaptiveFeedbackCamera");
            var commandBuffer = new CommandBuffer();

            try
            {
                Camera camera = cameraGameObject.AddComponent<Camera>();
                ContextContainer firstFrameData = CreateFrameData(camera, frameIndex: 17);

                VirtualTextureSystem.Update(firstFrameData, commandBuffer);
                VirtualTextureSystem.InjectCompletedReadbackStatsForTesting(
                    CameraType.Game,
                    feedbackOverflowCount: 3,
                    fallbackSampleCount: 11);
                VirtualTextureSystem.Update(firstFrameData, commandBuffer);

                Assert.That(VirtualTextureSystem.GetAdaptiveMipBiasForTesting(), Is.Zero);
                Assert.That(VirtualTextureSystem.AdaptiveFeedbackMeasurementWasFresh, Is.False);

                ContextContainer nextFrameData = CreateFrameData(camera, frameIndex: 18);
                VirtualTextureSystem.Update(nextFrameData, commandBuffer);

                Assert.That(VirtualTextureSystem.GetAdaptiveMipBiasForTesting(), Is.EqualTo(0.5f));
                Assert.That(VirtualTextureSystem.AdaptiveFeedbackMeasurementWasFresh, Is.True);
                Assert.That(VirtualTextureSystem.AdaptiveMeasuredFeedbackOverflowCount, Is.EqualTo(3));
                Assert.That(VirtualTextureSystem.AdaptiveMeasuredFallbackSampleCount, Is.EqualTo(11));
            }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(cameraGameObject);
            }
        }

        [Test]
        public void Update_SchedulesNeighborPrefetchWithinUploadBudget_WhenEnabled()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc(
                "NeighborPrefetch",
                cachePageCount: 8,
                maxUploadsPerFrame: 3,
                neighborPrefetchCount: 2));
            var requestedCoord = new VirtualTexturePageCoord(1, 1, 0);
            ulong requestKey = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, requestedCoord);
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, requestKey);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }

            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var requests), Is.True);
            Assert.That(requests.Count, Is.EqualTo(3));
            Assert.That(requests.Any(request => request.PageCoord.Equals(requestedCoord) && request.Priority == 1), Is.True);
            Assert.That(requests.Any(request => request.PageCoord.Equals(new VirtualTexturePageCoord(0, 1, 0)) && request.Priority == 0), Is.True);
            Assert.That(requests.Any(request => request.PageCoord.Equals(new VirtualTexturePageCoord(2, 1, 0)) && request.Priority == 0), Is.True);

            VirtualTextureStats stats = VirtualTextureStatsRegistry.LastStats;
            Assert.That(stats.PrefetchRequestCount, Is.EqualTo(2));
            Assert.That(stats.PendingMipGapSampleCount, Is.EqualTo(1));
            Assert.That(stats.PendingMipGapSum, Is.EqualTo(2));
            Assert.That(stats.PendingMipGapMax, Is.EqualTo(2));
            Assert.That(stats.PendingMipGapAverage, Is.EqualTo(2f));
        }

        [Test]
        public void Update_ColdStartPrefetchesNeighborsAtRequestedMip()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc(
                "RefinementNeighborPrefetch",
                cachePageCount: 8,
                maxUploadsPerFrame: 3,
                neighborPrefetchCount: 2,
                virtualPageCountX: 8,
                virtualPageCountY: 8,
                mipCount: 4));
            var sourceCoord = new VirtualTexturePageCoord(5, 5, 0);
            ulong requestKey = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, sourceCoord);
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, requestKey);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);

                Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(
                    spaceId,
                    out var refinementRequests), Is.True);
                Assert.That(refinementRequests, Has.Count.EqualTo(3));
                Assert.That(refinementRequests.All(request => request.PageCoord.Mip == 0), Is.True);
                Assert.That(refinementRequests.Any(
                    request => request.PageCoord.Equals(sourceCoord) && request.Priority == 1), Is.True);
                Assert.That(refinementRequests.Any(
                    request => request.PageCoord.Equals(new VirtualTexturePageCoord(4, 5, 0))
                               && request.Priority == 0), Is.True);
                Assert.That(refinementRequests.Any(
                    request => request.PageCoord.Equals(new VirtualTexturePageCoord(6, 5, 0))
                               && request.Priority == 0), Is.True);

                VirtualTextureUploadRequest sourceRequest = refinementRequests.Single(
                    request => request.PageCoord.Equals(sourceCoord));
                Assert.That(VirtualTextureSystem.CommitUpload(sourceRequest), Is.True);

                commandBuffer.Clear();
                VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, requestKey);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }

            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(
                spaceId,
                out var refinedRequests), Is.True);
            Assert.That(refinedRequests, Has.Count.EqualTo(2));
            Assert.That(refinedRequests.Any(
                request => request.PageCoord.Equals(sourceCoord)), Is.False);
            Assert.That(refinedRequests.Any(
                request => request.PageCoord.Equals(new VirtualTexturePageCoord(4, 5, 0))
                           && request.Priority == 0), Is.True);
            Assert.That(refinedRequests.Any(
                request => request.PageCoord.Equals(new VirtualTexturePageCoord(6, 5, 0))
                           && request.Priority == 0), Is.True);
        }

        [Test]
        public void Update_DoesNotEvictDemandPagesForNeighborPrefetch_WhenPoolIsFull()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc(
                "FullPoolPrefetch",
                cachePageCount: 2,
                maxUploadsPerFrame: 3,
                neighborPrefetchCount: 2));
            var requestedCoord = new VirtualTexturePageCoord(1, 1, 0);
            ulong requestKey = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, requestedCoord);
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, requestKey);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }

            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var requests), Is.True);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests[0].PageCoord, Is.EqualTo(requestedCoord));
            Assert.That(VirtualTextureStatsRegistry.LastStats.PrefetchRequestCount, Is.Zero);
            Assert.That(VirtualTextureStatsRegistry.LastStats.EvictionCount, Is.Zero);
        }

        [Test]
        public void Update_BiasesNeighborPrefetchTowardFeedbackMotion_WhenCentroidMoves()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc(
                "MotionPrefetch",
                cachePageCount: 8,
                maxUploadsPerFrame: 2,
                neighborPrefetchCount: 1));
            ulong firstKey = VirtualTextureFeedbackProcessor.EncodeKey(
                spaceId,
                new VirtualTexturePageCoord(1, 1, 0));
            ulong secondKey = VirtualTextureFeedbackProcessor.EncodeKey(
                spaceId,
                new VirtualTexturePageCoord(2, 1, 0));
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, firstKey);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);

                VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, secondKey);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }

            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var requests), Is.True);
            Assert.That(requests.Any(request => request.PageCoord.Equals(new VirtualTexturePageCoord(3, 1, 0))), Is.True);
            Assert.That(VirtualTextureStatsRegistry.LastStats.PrefetchRequestCount, Is.EqualTo(1));
        }

        [Test]
        public void DisplayStats_UsesFocusedViewSnapshot_WhenLastRenderedCameraDiffers()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("FocusedStats", cachePageCount: 4, maxUploadsPerFrame: 2));
            ulong focusedRequest = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            ulong backgroundRequest = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(1, 0, 0));

            var focusedCameraObject = new GameObject("VTFocusedStatsCamera");
            var backgroundCameraObject = new GameObject("VTBackgroundStatsCamera");
            var commandBuffer = new CommandBuffer();

            try
            {
                Camera focusedCamera = focusedCameraObject.AddComponent<Camera>();
                Camera backgroundCamera = backgroundCameraObject.AddComponent<Camera>();

                VirtualTextureStatsRegistry.SetFocusedViewOverrideForTesting(
                    VirtualTextureViewId.FromCamera(focusedCamera),
                    focusedCamera.cameraType);

                VirtualTextureSystem.InjectCompletedReadbackStatsForTesting(
                    focusedCamera,
                    1,
                    3,
                    focusedRequest);
                VirtualTextureSystem.Update(CreateFrameData(focusedCamera, frameIndex: 41), commandBuffer);

                VirtualTextureStats focusedStats = VirtualTextureStatsRegistry.DisplayStats;
                Assert.That(focusedStats.ViewId, Is.EqualTo(VirtualTextureViewId.FromCamera(focusedCamera)));
                Assert.That(focusedStats.ViewLabel, Does.Contain("VTFocusedStatsCamera"));
                Assert.That(focusedStats.CameraFrameIndex, Is.EqualTo(41));
                Assert.That(focusedStats.RenderSizeLabel, Is.EqualTo("512 x 512"));
                Assert.That(focusedStats.PixelSizeLabel, Is.EqualTo("512 x 512"));
                Assert.That(focusedStats.FeedbackSupported, Is.True);
                Assert.That(focusedStats.FeedbackCapacity, Is.EqualTo(32));
                Assert.That(focusedStats.FaultCount, Is.EqualTo(1));
                Assert.That(focusedStats.DeduplicatedRequestCount, Is.EqualTo(1));
                Assert.That(focusedStats.FeedbackOverflowCount, Is.EqualTo(1));
                Assert.That(focusedStats.FallbackSampleCount, Is.EqualTo(3));

                VirtualTextureSystem.InjectCompletedReadbackForTesting(
                    backgroundCamera,
                    backgroundRequest,
                    backgroundRequest);
                VirtualTextureSystem.Update(CreateFrameData(backgroundCamera, frameIndex: 42), commandBuffer);

                Assert.That(VirtualTextureStatsRegistry.LastStats.FaultCount, Is.EqualTo(2));

                VirtualTextureStats displayStats = VirtualTextureStatsRegistry.DisplayStats;
                Assert.That(displayStats.ViewId, Is.EqualTo(VirtualTextureViewId.FromCamera(focusedCamera)));
                Assert.That(displayStats.CameraFrameIndex, Is.EqualTo(41));
                Assert.That(displayStats.FaultCount, Is.EqualTo(1));
                Assert.That(displayStats.FallbackSampleCount, Is.EqualTo(3));
            }
            finally
            {
                VirtualTextureStatsRegistry.ClearFocusedViewOverrideForTesting();
                commandBuffer.Dispose();
                Object.DestroyImmediate(focusedCameraObject);
                Object.DestroyImmediate(backgroundCameraObject);
            }
        }

        [Test]
        public void DisplayStats_UsesSelectedCameraSnapshot_WhenStatsSourceIsSelectedCamera()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("SelectedStats", cachePageCount: 4, maxUploadsPerFrame: 2));
            ulong selectedRequest = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            ulong backgroundRequest = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(1, 0, 0));

            var selectedCameraObject = new GameObject("VTSelectedStatsCamera");
            var backgroundCameraObject = new GameObject("VTSelectedStatsBackgroundCamera");
            var unrenderedCameraObject = new GameObject("VTSelectedStatsUnrenderedCamera");
            var commandBuffer = new CommandBuffer();

            try
            {
                Camera selectedCamera = selectedCameraObject.AddComponent<Camera>();
                Camera backgroundCamera = backgroundCameraObject.AddComponent<Camera>();
                Camera unrenderedCamera = unrenderedCameraObject.AddComponent<Camera>();

                VirtualTextureSystem.InjectCompletedReadbackStatsForTesting(
                    selectedCamera,
                    2,
                    5,
                    selectedRequest);
                VirtualTextureSystem.Update(CreateFrameData(selectedCamera, frameIndex: 51), commandBuffer);

                VirtualTextureSystem.InjectCompletedReadbackForTesting(
                    backgroundCamera,
                    backgroundRequest,
                    backgroundRequest);
                VirtualTextureSystem.Update(CreateFrameData(backgroundCamera, frameIndex: 52), commandBuffer);

                VirtualTextureStats globalStats = VirtualTextureStatsRegistry.GetDisplayStats(
                    VirtualTextureStatsViewMode.Global,
                    null);
                Assert.That(globalStats.FaultCount, Is.EqualTo(2));

                VirtualTextureStats selectedStats = VirtualTextureStatsRegistry.GetDisplayStats(
                    VirtualTextureStatsViewMode.SelectedCamera,
                    selectedCamera);
                Assert.That(selectedStats.ViewId, Is.EqualTo(VirtualTextureViewId.FromCamera(selectedCamera)));
                Assert.That(selectedStats.ViewLabel, Does.Contain("VTSelectedStatsCamera"));
                Assert.That(selectedStats.CameraFrameIndex, Is.EqualTo(51));
                Assert.That(selectedStats.FaultCount, Is.EqualTo(1));
                Assert.That(selectedStats.FeedbackOverflowCount, Is.EqualTo(2));
                Assert.That(selectedStats.FallbackSampleCount, Is.EqualTo(5));

                VirtualTextureStats unavailableStats = VirtualTextureStatsRegistry.GetDisplayStats(
                    VirtualTextureStatsViewMode.SelectedCamera,
                    unrenderedCamera);
                Assert.That(unavailableStats.ViewLabel, Does.Contain("VTSelectedStatsUnrenderedCamera"));
                Assert.That(unavailableStats.StatusMessage, Does.Contain("No VT stats"));
            }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(selectedCameraObject);
                Object.DestroyImmediate(backgroundCameraObject);
                Object.DestroyImmediate(unrenderedCameraObject);
            }
        }

        [Test]
        public void RegisterAddressSpace_AssignsProducer_AndExposesVTRequests()
        {
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(
                CreateDesc("ProducerBound", cachePageCount: 2, maxUploadsPerFrame: 1),
                new TestProducer());
            ulong requestKey = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, requestKey);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);

                Assert.That(VirtualTextureSystem.TryGetProducerNameForTesting(spaceId, out string producerName), Is.True);
                Assert.That(producerName, Is.EqualTo(nameof(TestProducer)));
                Assert.That(VirtualTextureSystem.TryGetPendingRequests(spaceId, out var requests), Is.True);
                Assert.That(requests.Count, Is.EqualTo(1));
                Assert.That(requests[0].SpaceId, Is.EqualTo(spaceId));
                Assert.That(requests[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 0)));
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        [Test]
        public void RegisterOrReconfigureAddressSpace_RebuildsExistingSpace_WhenDescriptorChanges()
        {
            var producer = new TestProducer();
            VirtualTextureSpaceDesc initialDesc = CreateDesc("Reconfigure", cachePageCount: 2, maxUploadsPerFrame: 1);
            int spaceId = VirtualTextureSystem.RegisterOrReconfigureAddressSpace(initialDesc, producer);

            var cameraGameObject = new GameObject("VTReconfigureCamera");
            var commandBuffer = new CommandBuffer();

            try
            {
                Camera camera = cameraGameObject.AddComponent<Camera>();
                ContextContainer frameData = CreateFrameData(camera, frameIndex: 17);

                VirtualTextureSystem.Update(frameData, commandBuffer);
                VirtualTextureSpaceBinding initialBinding = frameData.Get<VividVirtualTextureFrameData>().Bindings.Single();
                GraphicsBuffer oldPageTableBuffer = initialBinding.PageTableBuffer;
                Texture2D oldPhysicalCache = initialBinding.PhysicalCache;
                ComputeBuffer oldFeedbackRequests = initialBinding.FeedbackRequests;
                ComputeBuffer oldFeedbackCounter = initialBinding.FeedbackCounter;
                ComputeBuffer oldFeedbackResidentHash = initialBinding.FeedbackResidentHash;

                var updatedDesc = new VirtualTextureSpaceDesc(
                    initialDesc.SpaceName,
                    pageSize: 64,
                    borderSize: 2,
                    virtualPageCountX: 8,
                    virtualPageCountY: 4,
                    mipCount: 4,
                    cachePageCount: 6,
                    graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                    maxUploadsPerFrame: 2,
                    feedbackCapacity: 64);

                int reconfiguredSpaceId = VirtualTextureSystem.RegisterOrReconfigureAddressSpace(updatedDesc, producer);

                Assert.That(reconfiguredSpaceId, Is.EqualTo(spaceId));
                Assert.That(oldPageTableBuffer.IsValid(), Is.False);
                Assert.That(oldPhysicalCache == null, Is.True);
                Assert.That(oldFeedbackRequests.IsValid(), Is.False);
                Assert.That(oldFeedbackCounter.IsValid(), Is.False);
                Assert.That(oldFeedbackResidentHash.IsValid(), Is.False);

                VirtualTextureSystem.Update(frameData, commandBuffer);
                VirtualTextureSpaceBinding updatedBinding = frameData.Get<VividVirtualTextureFrameData>().Bindings.Single();

                Assert.That(updatedBinding.SpaceId, Is.EqualTo(spaceId));
                Assert.That(updatedBinding.SpaceName, Is.EqualTo(updatedDesc.SpaceName));
                Assert.That(updatedBinding.PageTableBuffer.count, Is.EqualTo(updatedBinding.ShaderParams.PageTableEntryCount));
                AssertPhysicalAtlas(updatedBinding.PhysicalCache, updatedDesc, groupLayerCount: 1);
                Assert.That(updatedBinding.ShaderParams.PageSize, Is.EqualTo(updatedDesc.PageSize));
                Assert.That(updatedBinding.ShaderParams.FeedbackCapacity, Is.EqualTo(updatedDesc.FeedbackCapacity));
                Assert.That(updatedBinding.FeedbackRequestCapacity, Is.EqualTo(64));
                Assert.That(updatedBinding.FeedbackResidentHashCapacity, Is.EqualTo(128));
                Assert.That(updatedBinding.HasFeedback, Is.True);
            }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(cameraGameObject);
            }
        }

        [Test]
        public void Update_AdvancesFallbackFrameIndex_WhenCameraDataIsMissing()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("FallbackFrame", cachePageCount: 2, maxUploadsPerFrame: 1));
            ulong firstKey = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            ulong secondKey = VirtualTextureFeedbackProcessor.EncodeKey(spaceId, new VirtualTexturePageCoord(1, 0, 0));
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, firstKey);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);

                Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var firstRequests), Is.True);
                Assert.That(firstRequests.Count, Is.EqualTo(1));
                Assert.That(VirtualTextureSystem.CommitUpload(firstRequests[0]), Is.True);

                for (int frameOffset = 0;
                     frameOffset < VTPhysicalPool.FeedbackEvictionProtectionFrames;
                     frameOffset++)
                {
                    commandBuffer.Clear();
                    VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
                }

                VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, secondKey);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);

                Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var secondRequests), Is.True);
                Assert.That(secondRequests.Count, Is.EqualTo(1));
                Assert.That(secondRequests[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(1, 0, 0)));
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        [Test]
        public void Deinitialize_ClearsBindingsStatsAndFeedbackState()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("Shutdown", cachePageCount: 2, maxUploadsPerFrame: 1);
            int spaceId = VirtualTextureSystem.RegisterSpace(desc);

            var cameraGameObject = new GameObject("VTShutdownCamera");
            var commandBuffer = new CommandBuffer();

            try
            {
                Camera camera = cameraGameObject.AddComponent<Camera>();
                ContextContainer frameData = CreateFrameData(camera, frameIndex: 13);

                VirtualTextureSystem.Update(frameData, commandBuffer);

                VirtualTextureSpaceBinding binding = frameData.Get<VividVirtualTextureFrameData>().Bindings.Single();
                GraphicsBuffer pageTableBuffer = binding.PageTableBuffer;
                ComputeBuffer feedbackRequests = binding.FeedbackRequests;
                ComputeBuffer feedbackCounter = binding.FeedbackCounter;
                ComputeBuffer feedbackResidentHash = binding.FeedbackResidentHash;
                Texture2D physicalCache = binding.PhysicalCache;

                Assert.That(VirtualTextureSystem.IsCameraFeedbackStateCreatedForTesting(camera), Is.True);

                VirtualTextureSystem.Deinitialize();

                Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out _), Is.False);
                Assert.That(VirtualTextureSystem.IsCameraFeedbackStateCreatedForTesting(camera), Is.False);
                Assert.That(VirtualTextureStatsRegistry.LastStats.ActiveSpaceCount, Is.EqualTo(0));
                Assert.That(pageTableBuffer.IsValid(), Is.False);
                Assert.That(feedbackRequests.IsValid(), Is.False);
                Assert.That(feedbackCounter.IsValid(), Is.False);
                Assert.That(feedbackResidentHash.IsValid(), Is.False);
                Assert.That(physicalCache == null, Is.True);
            }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(cameraGameObject);
            }
        }

        [TestCase(1, 16)]
        [TestCase(8, 16)]
        [TestCase(9, 32)]
        [TestCase(512, 1024)]
        public void ResidentFeedbackHashCapacity_KeepsLoadFactorAtOrBelowOneHalf(
            int cachePageCount,
            int expectedCapacity)
        {
            Assert.That(
                VirtualTextureFeedbackBufferState.ResolveResidentHashCapacityForTesting(cachePageCount),
                Is.EqualTo(expectedCapacity));
        }

        [Test]
        public void Update_DoesNotAllocate_WhenAddressSpaceBindingsAreStable()
        {
            VirtualTextureSystem.RegisterSpace(CreateDesc("StableNoAlloc", cachePageCount: 2, maxUploadsPerFrame: 1));
            var frameData = new ContextContainer();
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.Update(frameData, commandBuffer);
                commandBuffer.Clear();
                VirtualTextureSystem.Update(frameData, commandBuffer);
                commandBuffer.Clear();

                var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
                VirtualTextureSystem.Update(frameData, commandBuffer);
                var allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

                Assert.That(allocatedBytes, Is.Zero);
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        [Test]
        public void ResidencyClassificationJob_ClassifiesStateAndResolvesResidentMipGap()
        {
            var inputs = new NativeArray<VTResidencyClassificationInput>(4, Allocator.TempJob);
            var pageStateFlags = new NativeArray<byte>(21, Allocator.TempJob);
            var mipOffsets = new NativeArray<int>(new[] { 0, 16, 20 }, Allocator.TempJob);
            var results = new NativeArray<VTResidencyClassificationResult>(4, Allocator.TempJob);

            try
            {
                inputs[0] = new VTResidencyClassificationInput(new VirtualTexturePageCoord(0, 0, 0));
                inputs[1] = new VTResidencyClassificationInput(new VirtualTexturePageCoord(1, 0, 0));
                inputs[2] = new VTResidencyClassificationInput(new VirtualTexturePageCoord(3, 3, 0));
                inputs[3] = new VTResidencyClassificationInput(new VirtualTexturePageCoord(4, 0, 0));
                pageStateFlags[0] = VTResidencyClassificationJob.ResidentFlag;
                pageStateFlags[1] = VTResidencyClassificationJob.PendingFlag;
                pageStateFlags[16] = VTResidencyClassificationJob.ResidentFlag;
                pageStateFlags[20] = VTResidencyClassificationJob.ResidentFlag;

                var job = new VTResidencyClassificationJob
                {
                    Inputs = inputs,
                    PageStateFlags = pageStateFlags,
                    MipOffsets = mipOffsets,
                    Results = results,
                    VirtualPageCountX = 4,
                    VirtualPageCountY = 4,
                    MipCount = 3,
                };
                job.Run(inputs.Length);

                AssertClassification(
                    results[0],
                    pageIndex: 0,
                    mipGap: 0,
                    VTResidencyRequestClassification.Resident);
                AssertClassification(
                    results[1],
                    pageIndex: 1,
                    mipGap: 1,
                    VTResidencyRequestClassification.Pending);
                AssertClassification(
                    results[2],
                    pageIndex: 15,
                    mipGap: 2,
                    VTResidencyRequestClassification.Missing);
                AssertClassification(
                    results[3],
                    pageIndex: -1,
                    mipGap: -1,
                    VTResidencyRequestClassification.Invalid);
            }
            finally
            {
                results.Dispose();
                mipOffsets.Dispose();
                pageStateFlags.Dispose();
                inputs.Dispose();
            }
        }

        [Test]
        public void Update_ReusesClassificationBuffers_AndSelectsParallelPathForLargeBatches()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc(
                "BurstClassification",
                cachePageCount: 4,
                maxUploadsPerFrame: 1,
                virtualPageCountX: 16,
                virtualPageCountY: 16,
                mipCount: 5,
                feedbackCapacity: 128));
            var requestKeys = new ulong[65];
            for (int requestIndex = 0; requestIndex < requestKeys.Length; requestIndex++)
            {
                requestKeys[requestIndex] = VirtualTextureFeedbackProcessor.EncodeKey(
                    spaceId,
                    new VirtualTexturePageCoord(requestIndex % 16, requestIndex / 16, 0));
            }

            var commandBuffer = new CommandBuffer();
            try
            {
                VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, requestKeys);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);

                Assert.That(
                    VirtualTextureSystem.WasLastResidencyClassificationParallelForTesting(spaceId),
                    Is.True);
                Assert.That(
                    VirtualTextureSystem.GetResidencyClassificationCapacityForTesting(spaceId),
                    Is.EqualTo(128));

                commandBuffer.Clear();
                VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, requestKeys[0]);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);

                Assert.That(
                    VirtualTextureSystem.WasLastResidencyClassificationParallelForTesting(spaceId),
                    Is.False);
                Assert.That(
                    VirtualTextureSystem.GetResidencyClassificationCapacityForTesting(spaceId),
                    Is.EqualTo(128));
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        private static ContextContainer CreateFrameData(Camera camera, int frameIndex)
        {
            var frameData = new ContextContainer();
            VividCameraData cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.SetCamera(camera);
            cameraData.actualWidth = 512;
            cameraData.actualHeight = 512;
            cameraData.pixelWidth = 512;
            cameraData.pixelHeight = 512;
            cameraData.pixelRect = new Rect(0f, 0f, 512f, 512f);
            cameraData.frameIndex = frameIndex;
            return frameData;
        }

        private static VirtualTextureSpaceDesc CreateDesc(
            string name,
            int cachePageCount,
            int maxUploadsPerFrame,
            int neighborPrefetchCount = 0,
            int virtualPageCountX = 4,
            int virtualPageCountY = 4,
            int mipCount = 3,
            int feedbackCapacity = 32)
        {
            return new VirtualTextureSpaceDesc(
                name,
                pageSize: 128,
                borderSize: 4,
                virtualPageCountX: virtualPageCountX,
                virtualPageCountY: virtualPageCountY,
                mipCount: mipCount,
                cachePageCount: cachePageCount,
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: maxUploadsPerFrame,
                feedbackCapacity: feedbackCapacity,
                neighborPrefetchCount: neighborPrefetchCount);
        }

        private static void AssertClassification(
            in VTResidencyClassificationResult result,
            int pageIndex,
            int mipGap,
            VTResidencyRequestClassification classification)
        {
            Assert.That(result.PageIndex, Is.EqualTo(pageIndex));
            Assert.That(result.MipGap, Is.EqualTo(mipGap));
            Assert.That(result.Classification, Is.EqualTo(classification));
        }

        private static void AssertPhysicalAtlas(
            Texture2D physicalAtlas,
            in VirtualTextureSpaceDesc desc,
            int groupLayerCount)
        {
            var layout = new VTPhysicalAtlasLayout(
                desc.PhysicalPageSize,
                desc.CachePageCount * groupLayerCount,
                SystemInfo.maxTextureSize);
            Assert.That(physicalAtlas.dimension, Is.EqualTo(TextureDimension.Tex2D));
            Assert.That(physicalAtlas.width, Is.EqualTo(layout.Width));
            Assert.That(physicalAtlas.height, Is.EqualTo(layout.Height));
            Assert.That(physicalAtlas.isReadable, Is.False);
        }

        private static Vector4 ToVector(Color32 color)
        {
            return new Vector4(
                color.r / 255f,
                color.g / 255f,
                color.b / 255f,
                color.a / 255f);
        }
    }
}
