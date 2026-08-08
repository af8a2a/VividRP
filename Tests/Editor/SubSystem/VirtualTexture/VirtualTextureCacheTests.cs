using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureCacheTests
    {
        [TearDown]
        public void TearDown()
        {
            VirtualTextureSystem.Deinitialize();
        }

        [Test]
        public void Cache_ReusesFreePagesBeforeEvicting()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("FreeList", cachePageCount: 3));

            VirtualTextureUploadRequest first = RequestPage(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            VirtualTextureUploadRequest second = RequestPage(spaceId, new VirtualTexturePageCoord(1, 0, 0));

            Assert.That(first.PhysicalPageId, Is.EqualTo(1));
            Assert.That(second.PhysicalPageId, Is.EqualTo(2));
            Assert.That(VirtualTextureSystem.GetFreePageCountForTesting(spaceId), Is.EqualTo(0));
        }

        [Test]
        public void Cache_EvictsLeastRecentlyUsedResidentPage()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("Lru", cachePageCount: 3));

            VirtualTextureUploadRequest first = RequestPage(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            VirtualTextureUploadRequest second = RequestPage(spaceId, new VirtualTexturePageCoord(1, 0, 0));

            IssueFeedback(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            AdvanceFrame(100);
            VirtualTextureUploadRequest third = GetLastPendingUpload(spaceId, new VirtualTexturePageCoord(2, 0, 0));
            Assert.That(third.PhysicalPageId, Is.EqualTo(second.PhysicalPageId));
            Assert.That(VirtualTextureSystem.CommitUpload(third), Is.True);

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 0),
                out VirtualTexturePageTableEntry firstEntry), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(1, 0, 0),
                out VirtualTexturePageTableEntry secondEntry), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(2, 0, 0),
                out VirtualTexturePageTableEntry thirdEntry), Is.True);

            Assert.That(firstEntry.Resident, Is.True);
            Assert.That(secondEntry.Resident, Is.False);
            Assert.That(secondEntry.Fallback, Is.True);
            Assert.That(secondEntry.ResolvedMip, Is.EqualTo(2));
            Assert.That(thirdEntry.Resident, Is.True);
            Assert.That(thirdEntry.PhysicalPageId, Is.EqualTo(second.PhysicalPageId));
            Assert.That(first.PhysicalPageId, Is.Not.EqualTo(third.PhysicalPageId));
        }

        [Test]
        public void Cache_DoesNotEvictLockedPageUntilUnlocked()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("Locked", cachePageCount: 2));

            VirtualTextureUploadRequest first = RequestPage(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            Assert.That(VirtualTextureSystem.SetPageLocked(spaceId, new VirtualTexturePageCoord(0, 0, 0), true), Is.True);

            IssueFeedback(spaceId, new VirtualTexturePageCoord(1, 0, 0));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(0));

            Assert.That(VirtualTextureSystem.SetPageLocked(spaceId, new VirtualTexturePageCoord(0, 0, 0), false), Is.True);
            AdvanceFrame(100);
            VirtualTextureUploadRequest next = GetLastPendingUpload(spaceId, new VirtualTexturePageCoord(1, 0, 0));
            Assert.That(next.PhysicalPageId, Is.EqualTo(first.PhysicalPageId));
        }

        [Test]
        public void CommitUpload_RejectsStaleGenerationAfterPhysicalPageReuse()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("Generation", cachePageCount: 2));

            VirtualTextureUploadRequest first = RequestPage(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            AdvanceFrame(100);
            VirtualTextureUploadRequest second = GetLastPendingUpload(spaceId, new VirtualTexturePageCoord(1, 0, 0));

            Assert.That(second.Generation, Is.Not.EqualTo(first.Generation));
            Assert.That(VirtualTextureSystem.CommitUpload(first), Is.False);
            Assert.That(VirtualTextureSystem.CommitUpload(second), Is.True);
        }

        [Test]
        public void Cache_PrioritizesOldestTouchFrameBeforeMip()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("AgeFirstEviction", cachePageCount: 3));

            VirtualTextureUploadRequest coarse = RequestPage(spaceId, new VirtualTexturePageCoord(0, 0, 1));
            VirtualTextureUploadRequest fine = RequestPage(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            AdvanceFrame(100);
            VirtualTextureUploadRequest replacement = GetLastPendingUpload(spaceId, new VirtualTexturePageCoord(2, 0, 0));
            Assert.That(VirtualTextureSystem.CommitUpload(replacement), Is.True);

            Assert.That(replacement.PhysicalPageId, Is.EqualTo(coarse.PhysicalPageId));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 1),
                out VirtualTexturePageTableEntry coarseEntry), Is.True);
            Assert.That(coarseEntry.Resident, Is.False);
            Assert.That(coarseEntry.Fallback, Is.True);
            Assert.That(coarseEntry.ResolvedMip, Is.EqualTo(2));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 0),
                out VirtualTexturePageTableEntry fineEntry), Is.True);
            Assert.That(fineEntry.Resident, Is.True);
            Assert.That(fineEntry.PhysicalPageId, Is.EqualTo(fine.PhysicalPageId));
        }

        [Test]
        public void Cache_ReusesTheSamePhysicalPageDeterministically_WhenOnlyOneDynamicSlotExists()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("DeterministicReuse", cachePageCount: 2));

            VirtualTextureUploadRequest first = RequestPage(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            AdvanceFrame(100);
            VirtualTextureUploadRequest second = GetLastPendingUpload(spaceId, new VirtualTexturePageCoord(1, 0, 0));
            Assert.That(VirtualTextureSystem.CommitUpload(second), Is.True);
            AdvanceFrame(200);
            VirtualTextureUploadRequest third = GetLastPendingUpload(spaceId, new VirtualTexturePageCoord(2, 0, 0));

            Assert.That(second.PhysicalPageId, Is.EqualTo(first.PhysicalPageId));
            Assert.That(third.PhysicalPageId, Is.EqualTo(first.PhysicalPageId));
        }

        [Test]
        public void Cache_PreservesActiveViewPages_WhenEvictingSharedCache()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("ViewAffinity", cachePageCount: 3));
            var activeCameraObject = new GameObject("VTActiveAffinityCamera");
            var backgroundCameraObject = new GameObject("VTBackgroundAffinityCamera");

            try
            {
                Camera activeCamera = activeCameraObject.AddComponent<Camera>();
                Camera backgroundCamera = backgroundCameraObject.AddComponent<Camera>();
                var activeCoord = new VirtualTexturePageCoord(3, 0, 0);
                var backgroundCoord = new VirtualTexturePageCoord(1, 0, 0);
                var replacementCoord = new VirtualTexturePageCoord(0, 0, 0);

                VirtualTextureUploadRequest activePage = RequestPage(activeCamera, 1, spaceId, activeCoord);
                VirtualTextureUploadRequest backgroundPage = RequestPage(backgroundCamera, 2, spaceId, backgroundCoord);
                VirtualTextureUploadRequest replacement = GetLastPendingUpload(
                    activeCamera,
                    2 + VTPhysicalPool.FeedbackEvictionProtectionFrames,
                    spaceId,
                    replacementCoord);

                Assert.That(replacement.PhysicalPageId, Is.EqualTo(backgroundPage.PhysicalPageId));
                Assert.That(replacement.PhysicalPageId, Is.Not.EqualTo(activePage.PhysicalPageId));
            }
            finally
            {
                Object.DestroyImmediate(activeCameraObject);
                Object.DestroyImmediate(backgroundCameraObject);
            }
        }

        [Test]
        public void Cache_PreservesFocusedViewPages_WhenBackgroundCameraRenders()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("FocusedViewAffinity", cachePageCount: 3));
            var focusedCameraObject = new GameObject("VTFocusedAffinityCamera");
            var backgroundCameraObject = new GameObject("VTBackgroundFocusedAffinityCamera");

            try
            {
                Camera focusedCamera = focusedCameraObject.AddComponent<Camera>();
                Camera backgroundCamera = backgroundCameraObject.AddComponent<Camera>();
                var focusedCoord = new VirtualTexturePageCoord(3, 0, 0);
                var backgroundCoord = new VirtualTexturePageCoord(1, 0, 0);
                var replacementCoord = new VirtualTexturePageCoord(0, 0, 0);

                VirtualTextureStatsRegistry.SetFocusedViewOverrideForTesting(
                    VirtualTextureViewId.FromCamera(focusedCamera),
                    focusedCamera.cameraType);

                VirtualTextureUploadRequest focusedPage = RequestPage(focusedCamera, 1, spaceId, focusedCoord);
                VirtualTextureUploadRequest backgroundPage = RequestPage(backgroundCamera, 2, spaceId, backgroundCoord);
                VirtualTextureUploadRequest replacement = GetLastPendingUpload(
                    backgroundCamera,
                    2 + VTPhysicalPool.FeedbackEvictionProtectionFrames,
                    spaceId,
                    replacementCoord);

                Assert.That(replacement.PhysicalPageId, Is.EqualTo(backgroundPage.PhysicalPageId));
                Assert.That(replacement.PhysicalPageId, Is.Not.EqualTo(focusedPage.PhysicalPageId));
            }
            finally
            {
                VirtualTextureStatsRegistry.ClearFocusedViewOverrideForTesting();
                Object.DestroyImmediate(focusedCameraObject);
                Object.DestroyImmediate(backgroundCameraObject);
            }
        }

        private static VirtualTextureSpaceDesc CreateDesc(string name, int cachePageCount)
        {
            return new VirtualTextureSpaceDesc(
                name,
                pageSize: 128,
                borderSize: 4,
                virtualPageCountX: 4,
                virtualPageCountY: 1,
                mipCount: 3,
                cachePageCount: cachePageCount,
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: 4,
                feedbackCapacity: 32);
        }

        private static VirtualTextureUploadRequest RequestPage(int spaceId, VirtualTexturePageCoord coord)
        {
            VirtualTextureUploadRequest request = GetLastPendingUpload(spaceId, coord);
            Assert.That(VirtualTextureSystem.CommitUpload(request), Is.True);
            return request;
        }

        private static VirtualTextureUploadRequest RequestPage(
            Camera camera,
            int frameIndex,
            int spaceId,
            VirtualTexturePageCoord coord)
        {
            VirtualTextureUploadRequest request = GetLastPendingUpload(camera, frameIndex, spaceId, coord);
            Assert.That(VirtualTextureSystem.CommitUpload(request), Is.True);
            return request;
        }

        private static VirtualTextureUploadRequest GetLastPendingUpload(int spaceId, VirtualTexturePageCoord coord)
        {
            IssueFeedback(spaceId, coord);
            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var requests), Is.True);
            Assert.That(requests.Count, Is.GreaterThan(0));
            return requests.Last();
        }

        private static VirtualTextureUploadRequest GetLastPendingUpload(
            Camera camera,
            int frameIndex,
            int spaceId,
            VirtualTexturePageCoord coord)
        {
            IssueFeedback(camera, frameIndex, spaceId, coord);
            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var requests), Is.True);
            Assert.That(requests.Count, Is.GreaterThan(0));
            return requests.Last();
        }

        private static void IssueFeedback(int spaceId, params VirtualTexturePageCoord[] coords)
        {
            var commandBuffer = new CommandBuffer();
            var frameData = new ContextContainer();

            try
            {
                foreach (VirtualTexturePageCoord coord in coords)
                    VirtualTextureSystem.InjectCompletedReadbackForTesting(CameraType.Game, VirtualTextureFeedbackProcessor.EncodeKey(spaceId, coord));

                VirtualTextureSystem.Update(frameData, commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        private static void IssueFeedback(
            Camera camera,
            int frameIndex,
            int spaceId,
            params VirtualTexturePageCoord[] coords)
        {
            var commandBuffer = new CommandBuffer();
            ContextContainer frameData = CreateFrameData(camera, frameIndex);

            try
            {
                foreach (VirtualTexturePageCoord coord in coords)
                    VirtualTextureSystem.InjectCompletedReadbackForTesting(
                        camera,
                        VirtualTextureFeedbackProcessor.EncodeKey(spaceId, coord));

                VirtualTextureSystem.Update(frameData, commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        private static void AdvanceFrame(int frameIndex)
        {
            var commandBuffer = new CommandBuffer();
            try
            {
                VirtualTextureSystem.Update(CreateFrameData(null, frameIndex), commandBuffer);
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
            cameraData.camera = camera;
            cameraData.actualWidth = 512;
            cameraData.actualHeight = 512;
            cameraData.pixelWidth = 512;
            cameraData.pixelHeight = 512;
            cameraData.pixelRect = new Rect(0f, 0f, 512f, 512f);
            cameraData.frameIndex = frameIndex;
            return frameData;
        }
    }
}
