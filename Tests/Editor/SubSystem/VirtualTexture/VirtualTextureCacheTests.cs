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
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("FreeList", cachePageCount: 2));

            VirtualTextureUploadRequest first = RequestPage(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            VirtualTextureUploadRequest second = RequestPage(spaceId, new VirtualTexturePageCoord(1, 0, 0));

            Assert.That(first.PhysicalPageId, Is.EqualTo(0));
            Assert.That(second.PhysicalPageId, Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetFreePageCountForTesting(spaceId), Is.EqualTo(0));
        }

        [Test]
        public void Cache_EvictsLeastRecentlyUsedResidentPage()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("Lru", cachePageCount: 2));

            VirtualTextureUploadRequest first = RequestPage(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            VirtualTextureUploadRequest second = RequestPage(spaceId, new VirtualTexturePageCoord(1, 0, 0));

            IssueFeedback(spaceId, new VirtualTexturePageCoord(0, 0, 0));
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
            Assert.That(secondEntry.IsMapped, Is.False);
            Assert.That(thirdEntry.Resident, Is.True);
            Assert.That(thirdEntry.PhysicalPageId, Is.EqualTo(second.PhysicalPageId));
            Assert.That(first.PhysicalPageId, Is.Not.EqualTo(third.PhysicalPageId));
        }

        [Test]
        public void Cache_DoesNotEvictLockedPageUntilUnlocked()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("Locked", cachePageCount: 1));

            RequestPage(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            Assert.That(VirtualTextureSystem.SetPageLocked(spaceId, new VirtualTexturePageCoord(0, 0, 0), true), Is.True);

            IssueFeedback(spaceId, new VirtualTexturePageCoord(1, 0, 0));
            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(spaceId), Is.EqualTo(0));

            Assert.That(VirtualTextureSystem.SetPageLocked(spaceId, new VirtualTexturePageCoord(0, 0, 0), false), Is.True);
            VirtualTextureUploadRequest next = GetLastPendingUpload(spaceId, new VirtualTexturePageCoord(1, 0, 0));
            Assert.That(next.PhysicalPageId, Is.EqualTo(0));
        }

        [Test]
        public void CommitUpload_RejectsStaleGenerationAfterPhysicalPageReuse()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("Generation", cachePageCount: 1));

            VirtualTextureUploadRequest first = RequestPage(spaceId, new VirtualTexturePageCoord(0, 0, 0));
            VirtualTextureUploadRequest second = GetLastPendingUpload(spaceId, new VirtualTexturePageCoord(1, 0, 0));

            Assert.That(second.Generation, Is.Not.EqualTo(first.Generation));
            Assert.That(VirtualTextureSystem.CommitUpload(first), Is.False);
            Assert.That(VirtualTextureSystem.CommitUpload(second), Is.True);
        }

        private static VirtualTextureSpaceDesc CreateDesc(string name, int cachePageCount)
        {
            return new VirtualTextureSpaceDesc(
                name,
                pageSize: 128,
                borderSize: 4,
                virtualPageCountX: 4,
                virtualPageCountY: 1,
                mipCount: 1,
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

        private static VirtualTextureUploadRequest GetLastPendingUpload(int spaceId, VirtualTexturePageCoord coord)
        {
            IssueFeedback(spaceId, coord);
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
    }
}
