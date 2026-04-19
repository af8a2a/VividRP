using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTexturePageTableTests
    {
        [TearDown]
        public void TearDown()
        {
            VirtualTextureSystem.Deinitialize();
        }

        [Test]
        public void PageTableEntry_PacksAndUnpacksExpectedBits()
        {
            var entry = new VirtualTexturePageTableEntry(
                physicalPageId: 17,
                resolvedMip: 3,
                resident: true,
                fallback: false,
                pendingUpload: true,
                locked: true);

            Assert.That(entry.PhysicalPageId, Is.EqualTo(17));
            Assert.That(entry.ResolvedMip, Is.EqualTo(3));
            Assert.That(entry.Resident, Is.True);
            Assert.That(entry.Fallback, Is.False);
            Assert.That(entry.PendingUpload, Is.True);
            Assert.That(entry.Locked, Is.True);
            Assert.That(entry.IsMapped, Is.True);
        }

        [Test]
        public void SpaceDesc_ComposesExpectedVTStackDesc()
        {
            var stackDesc = new VTStackDesc(
                pageSize: 128,
                borderSize: 4,
                cachePageCount: 16,
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: 8,
                feedbackCapacity: 64);
            var desc = new VirtualTextureSpaceDesc(
                "Stacked",
                virtualPageCountX: 8,
                virtualPageCountY: 4,
                mipCount: 3,
                stackDesc: stackDesc);

            Assert.That(desc.StackDesc, Is.EqualTo(stackDesc));
            Assert.That(desc.PageSize, Is.EqualTo(stackDesc.PageSize));
            Assert.That(desc.BorderSize, Is.EqualTo(stackDesc.BorderSize));
            Assert.That(desc.CachePageCount, Is.EqualTo(stackDesc.CachePageCount));
            Assert.That(desc.PhysicalPageSize, Is.EqualTo(stackDesc.PhysicalPageSize));
        }

        [Test]
        public void SpaceUtility_ComputesExpectedMipOffsetsAndFlatIndices()
        {
            int[] mipOffsets = VirtualTextureSpaceUtility.BuildMipOffsets(4, 4, 3);

            Assert.That(mipOffsets, Is.EqualTo(new[] { 0, 16, 20 }));

            var desc = CreateDesc("Flattening", 4, 4, 3, 4, 4);
            int flatIndex = VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, new VirtualTexturePageCoord(1, 1, 1));

            Assert.That(flatIndex, Is.EqualTo(19));
        }

        [Test]
        public void PageTable_MaterializesBestAncestorFallbackWithoutShaderRecursion()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("Fallback", 4, 4, 3, 4, 4));

            VirtualTextureUploadRequest rootRequest = RequestAndCommit(spaceId, new VirtualTexturePageCoord(0, 0, 2));

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(3, 2, 0),
                out VirtualTexturePageTableEntry rootFallbackEntry), Is.True);
            Assert.That(rootFallbackEntry.Fallback, Is.True);
            Assert.That(rootFallbackEntry.ResolvedMip, Is.EqualTo(2));
            Assert.That(rootFallbackEntry.PhysicalPageId, Is.EqualTo(rootRequest.PhysicalPageId));

            VirtualTextureUploadRequest childRequest = RequestAndCommit(spaceId, new VirtualTexturePageCoord(1, 1, 1));

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(3, 2, 0),
                out VirtualTexturePageTableEntry childFallbackEntry), Is.True);
            Assert.That(childFallbackEntry.Fallback, Is.True);
            Assert.That(childFallbackEntry.ResolvedMip, Is.EqualTo(1));
            Assert.That(childFallbackEntry.PhysicalPageId, Is.EqualTo(childRequest.PhysicalPageId));
        }

        private static VirtualTextureSpaceDesc CreateDesc(
            string name,
            int virtualPageCountX,
            int virtualPageCountY,
            int mipCount,
            int cachePageCount,
            int maxUploadsPerFrame)
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
                feedbackCapacity: 32);
        }

        private static VirtualTextureUploadRequest RequestAndCommit(int spaceId, VirtualTexturePageCoord coord)
        {
            RequestPages(spaceId, coord);

            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var requests), Is.True);
            VirtualTextureUploadRequest request = requests.Last();
            Assert.That(VirtualTextureSystem.CommitUpload(request), Is.True);
            return request;
        }

        private static void RequestPages(int spaceId, params VirtualTexturePageCoord[] coords)
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
