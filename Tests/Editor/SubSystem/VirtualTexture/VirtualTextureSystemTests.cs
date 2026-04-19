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
            Assert.That(stats.FreePageCount, Is.EqualTo(3));
            Assert.That(stats.ResidentPageCount, Is.EqualTo(0));
            Assert.That(stats.EvictionCount, Is.EqualTo(0));
            Assert.That(stats.FaultCount, Is.EqualTo(3));
            Assert.That(stats.DeduplicatedRequestCount, Is.EqualTo(2));
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
                GraphicsBuffer feedbackRequests = binding.FeedbackRequests;
                GraphicsBuffer feedbackCounter = binding.FeedbackCounter;
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
