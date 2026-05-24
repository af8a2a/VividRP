using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
                Assert.That(binding.SpaceName, Is.EqualTo(desc.SpaceName));
                Assert.That(binding.PageTableBuffer, Is.Not.Null);
                Assert.That(binding.PageTableBuffer.count, Is.EqualTo(binding.ShaderParams.PageTableEntryCount));
                Assert.That(binding.PhysicalCache, Is.Not.Null);
                Assert.That(binding.PhysicalCache.width, Is.EqualTo(desc.PhysicalPageSize));
                Assert.That(binding.PhysicalCache.height, Is.EqualTo(desc.PhysicalPageSize));
                Assert.That(binding.PhysicalCache.depth, Is.EqualTo(desc.CachePageCount));
                Assert.That(binding.HasFeedback, Is.True);
                Assert.That(binding.FeedbackCounter.count, Is.EqualTo(2));
                Assert.That(binding.ShaderParams.SpaceId, Is.EqualTo(spaceId));
                Assert.That(binding.ShaderParams.PageSize, Is.EqualTo(desc.PageSize));
                Assert.That(binding.MipOffsets, Is.EqualTo(VirtualTextureSpaceUtility.BuildMipOffsets(
                    desc.VirtualPageCountX,
                    desc.VirtualPageCountY,
                    desc.MipCount)));
                Assert.That(VirtualTextureSystem.IsCameraFeedbackStateCreatedForTesting(camera), Is.True);
            }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(cameraGameObject);
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
            }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(activeCameraObject);
                Object.DestroyImmediate(backgroundCameraObject);
            }
        }

        [Test]
        public void Update_DefersBackgroundViewFeedback_UntilBackgroundCameraRenders()
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
                Assert.That(activeRequests.Count, Is.EqualTo(0));
                Assert.That(VirtualTextureStatsRegistry.LastStats.FaultCount, Is.EqualTo(0));

                VirtualTextureSystem.Update(CreateFrameData(backgroundCamera, frameIndex: 32), commandBuffer);

                Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var backgroundRequests), Is.True);
                Assert.That(backgroundRequests.Count, Is.EqualTo(1));
                Assert.That(backgroundRequests[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(1, 0, 0)));
                Assert.That(VirtualTextureStatsRegistry.LastStats.FaultCount, Is.EqualTo(1));
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

            try
            {
                VirtualTextureSystem.InjectCompletedReadbackStatsForTesting(
                    CameraType.Game,
                    3,
                    11,
                    requestKey);
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }

            VirtualTextureStats stats = VirtualTextureStatsRegistry.LastStats;
            Assert.That(stats.FaultCount, Is.EqualTo(1));
            Assert.That(stats.FeedbackOverflowCount, Is.EqualTo(3));
            Assert.That(stats.FallbackSampleCount, Is.EqualTo(11));
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
                Texture2DArray oldPhysicalCache = initialBinding.PhysicalCache;
                ComputeBuffer oldFeedbackRequests = initialBinding.FeedbackRequests;
                ComputeBuffer oldFeedbackCounter = initialBinding.FeedbackCounter;

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

                VirtualTextureSystem.Update(frameData, commandBuffer);
                VirtualTextureSpaceBinding updatedBinding = frameData.Get<VividVirtualTextureFrameData>().Bindings.Single();

                Assert.That(updatedBinding.SpaceId, Is.EqualTo(spaceId));
                Assert.That(updatedBinding.SpaceName, Is.EqualTo(updatedDesc.SpaceName));
                Assert.That(updatedBinding.PageTableBuffer.count, Is.EqualTo(updatedBinding.ShaderParams.PageTableEntryCount));
                Assert.That(updatedBinding.PhysicalCache.width, Is.EqualTo(updatedDesc.PhysicalPageSize));
                Assert.That(updatedBinding.PhysicalCache.height, Is.EqualTo(updatedDesc.PhysicalPageSize));
                Assert.That(updatedBinding.PhysicalCache.depth, Is.EqualTo(updatedDesc.CachePageCount));
                Assert.That(updatedBinding.ShaderParams.PageSize, Is.EqualTo(updatedDesc.PageSize));
                Assert.That(updatedBinding.ShaderParams.FeedbackCapacity, Is.EqualTo(updatedDesc.FeedbackCapacity));
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
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("FallbackFrame", cachePageCount: 1, maxUploadsPerFrame: 1));
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
                Texture2DArray physicalCache = binding.PhysicalCache;

                Assert.That(VirtualTextureSystem.IsCameraFeedbackStateCreatedForTesting(camera), Is.True);

                VirtualTextureSystem.Deinitialize();

                Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out _), Is.False);
                Assert.That(VirtualTextureSystem.IsCameraFeedbackStateCreatedForTesting(camera), Is.False);
                Assert.That(VirtualTextureStatsRegistry.LastStats.ActiveSpaceCount, Is.EqualTo(0));
                Assert.That(pageTableBuffer.IsValid(), Is.False);
                Assert.That(feedbackRequests.IsValid(), Is.False);
                Assert.That(feedbackCounter.IsValid(), Is.False);
                Assert.That(physicalCache == null, Is.True);
            }
            finally
            {
                commandBuffer.Dispose();
                Object.DestroyImmediate(cameraGameObject);
            }
        }

        [Test]
        public void VividRenderPipeline_DisposeExplicitlyDeinitializesVirtualTextureSystem()
        {
            string source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPipeline", "VividRenderPipeline.cs"));

            Assert.That(source, Does.Contain("VirtualTextureSystem.Deinitialize();"));
        }

        [Test]
        public void Update_ReusesFeedbackScratchCollectionsAndCommitCallbacks_ToAvoidPreRenderGc()
        {
            string systemSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "VirtualTexture", "VirtualTextureSystem.cs"));
            string feedbackSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "VirtualTexture", "VirtualTextureFeedback.cs"));
            string addressSpaceSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "VirtualTexture", "VTAddressSpace.cs"));
            string uploadSchedulerSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "VirtualTexture", "VTUploadScheduler.cs"));

            Assert.That(systemSource, Does.Contain("private static readonly VirtualTextureFeedbackProcessor.Scratch s_AggregationScratch = new();"));
            Assert.That(systemSource, Does.Contain("private static readonly List<VirtualTextureAggregatedFeedbackRequest> s_AggregatedRequests = new();"));
            Assert.That(systemSource, Does.Contain("VirtualTextureFeedbackProcessor.Aggregate("));
            Assert.That(systemSource, Does.Contain("cachePriorityViewId);"));
            Assert.That(systemSource, Does.Contain("ClearGroupedRequests();"));
            Assert.That(feedbackSource, Does.Contain("internal sealed class Scratch"));
            Assert.That(feedbackSource, Does.Contain("private static readonly IComparer<VirtualTextureAggregatedFeedbackRequest> s_RequestComparer = AggregatedRequestComparer.Instance;"));
            Assert.That(feedbackSource, Does.Contain("internal Dictionary<Camera, VirtualTextureFeedbackCameraState> EnumerateStates()"));
            Assert.That(feedbackSource, Does.Contain("internal Dictionary<int, VirtualTextureFeedbackBufferState> EnumerateSpaceStates()"));
            Assert.That(feedbackSource, Does.Contain("RequestsReadbackCallback = HandleRequestsReadback;"));
            Assert.That(feedbackSource, Does.Contain("AsyncGPUReadback.Request(pair.RequestsBuffer, pair.RequestsReadbackCallback);"));
            Assert.That(feedbackSource, Does.Not.Contain("request => HandleRequestsReadback"));
            Assert.That(feedbackSource, Does.Not.Contain("new ulong[data.Length]"));
            Assert.That(addressSpaceSource, Does.Contain("CommitCompletedUploads(this);"));
            Assert.That(addressSpaceSource, Does.Not.Contain("request => TryCommitRequestInternal"));
            Assert.That(uploadSchedulerSource, Does.Contain("CommitCompletedUploads(IVTUploadRequestCommitter committer)"));
            Assert.That(uploadSchedulerSource, Does.Not.Contain("Func<VTRequest"));
        }

        private static ContextContainer CreateFrameData(Camera camera, int frameIndex)
        {
            var frameData = new ContextContainer();
            VividCameraData cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.camera = camera;
            cameraData.actualWidth = 512;
            cameraData.actualHeight = 512;
            cameraData.pixelWidth = 512;
            cameraData.pixelHeight = 512;
            cameraData.pixelRect = new Rect(0f, 0f, 512f, 512f);
            cameraData.frameIndex = frameIndex;
            return frameData;
        }

        private static VirtualTextureSpaceDesc CreateDesc(string name, int cachePageCount, int maxUploadsPerFrame)
        {
            return new VirtualTextureSpaceDesc(
                name,
                pageSize: 128,
                borderSize: 4,
                virtualPageCountX: 4,
                virtualPageCountY: 4,
                mipCount: 3,
                cachePageCount: cachePageCount,
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: maxUploadsPerFrame,
                feedbackCapacity: 32);
        }

        private static string GetPackageFilePath(params string[] parts)
        {
            string customPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "Custom_URP"));
            if (Directory.Exists(customPath))
                return Path.Combine(customPath, Path.Combine(parts));

            string vividPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "VividRP"));
            if (Directory.Exists(vividPath))
                return Path.Combine(vividPath, Path.Combine(parts));

            string legacyPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "com.af8a2a.vividrp"));
            return Path.Combine(legacyPath, Path.Combine(parts));
        }
    }
}
