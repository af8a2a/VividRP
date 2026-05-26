using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTexturePhysicalPoolTests
    {
        private sealed class NamedProducer : VTProducer
        {
            internal NamedProducer(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }

        [TearDown]
        public void TearDown()
        {
            VirtualTextureSystem.Deinitialize();
        }

        [Test]
        public void RegisterAddressSpace_SharesPhysicalPool_ForMatchingPoolDescriptor()
        {
            VirtualTextureSpaceDesc firstDesc = CreateDesc("SharedPoolA");
            VirtualTextureSpaceDesc secondDesc = CreateDesc("SharedPoolB");

            int firstSpaceId = VirtualTextureSystem.RegisterAddressSpace(firstDesc, new NamedProducer("ProducerA"));
            int secondSpaceId = VirtualTextureSystem.RegisterAddressSpace(secondDesc, new NamedProducer("ProducerB"));

            Assert.That(VirtualTextureSystem.GetPhysicalPoolCountForTesting(), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(firstSpaceId, out Texture2DArray firstCache), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(secondSpaceId, out Texture2DArray secondCache), Is.True);
            Assert.That(ReferenceEquals(firstCache, secondCache), Is.True);

            VTPhysicalPoolStats stats = VirtualTextureSystem.GetPhysicalPoolStatsForTesting();
            Assert.That(stats.ResidentPageCount, Is.EqualTo(2));
            Assert.That(stats.FreePageCount, Is.EqualTo(firstDesc.CachePageCount - 2));
            Assert.That(stats.LockedPageCount, Is.EqualTo(2));
        }

        [Test]
        public void ProcessRequests_ReusesResidentPhysicalPage_ForSameProducerAndPageIdentity()
        {
            var producer = new NamedProducer("SharedProducer");
            int firstSpaceId = VirtualTextureSystem.RegisterAddressSpace(CreateDesc("SharedIdentityA"), producer);
            int secondSpaceId = VirtualTextureSystem.RegisterAddressSpace(CreateDesc("SharedIdentityB"), producer);
            var coord = new VirtualTexturePageCoord(0, 0, 0);

            VirtualTextureUploadRequest firstRequest = RequestPage(firstSpaceId, coord);
            IssueFeedback(secondSpaceId, coord);

            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(secondSpaceId), Is.EqualTo(0));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                firstSpaceId,
                coord,
                out VirtualTexturePageTableEntry firstEntry), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                secondSpaceId,
                coord,
                out VirtualTexturePageTableEntry secondEntry), Is.True);
            Assert.That(firstEntry.Resident, Is.True);
            Assert.That(secondEntry.Resident, Is.True);
            Assert.That(secondEntry.PhysicalPageId, Is.EqualTo(firstRequest.PhysicalPageId));
            Assert.That(secondEntry.PhysicalPageId, Is.EqualTo(firstEntry.PhysicalPageId));

            VTPhysicalPoolStats stats = VirtualTextureSystem.GetPhysicalPoolStatsForTesting();
            Assert.That(stats.ResidentPageCount, Is.EqualTo(2));
        }

        [Test]
        public void FlushProducer_ClearsOnlyMatchingProducerPages()
        {
            var firstProducer = new NamedProducer("FlushProducerA");
            var secondProducer = new NamedProducer("FlushProducerB");
            int firstSpaceId = VirtualTextureSystem.RegisterAddressSpace(CreateDesc("FlushProducerA"), firstProducer);
            int secondSpaceId = VirtualTextureSystem.RegisterAddressSpace(CreateDesc("FlushProducerB"), secondProducer);
            var coord = new VirtualTexturePageCoord(0, 0, 0);

            RequestPage(firstSpaceId, coord);
            RequestPage(secondSpaceId, coord);

            int flushedCount = VirtualTextureSystem.FlushProducer(firstProducer);

            Assert.That(flushedCount, Is.EqualTo(2));
            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(firstSpaceId), Is.EqualTo(0));
            Assert.That(VirtualTextureSystem.GetResidentPageCountForTesting(secondSpaceId), Is.EqualTo(2));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                firstSpaceId,
                coord,
                out VirtualTexturePageTableEntry firstEntry), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                secondSpaceId,
                coord,
                out VirtualTexturePageTableEntry secondEntry), Is.True);
            Assert.That(firstEntry.Resident, Is.False);
            Assert.That(firstEntry.PendingUpload, Is.False);
            Assert.That(secondEntry.Resident, Is.True);
        }

        [Test]
        public void FlushRegion_RemovesOnlyMatchingSpaceBinding_WhenPhysicalPageIsShared()
        {
            var producer = new NamedProducer("RegionSharedProducer");
            int firstSpaceId = VirtualTextureSystem.RegisterAddressSpace(CreateDesc("FlushRegionA"), producer);
            int secondSpaceId = VirtualTextureSystem.RegisterAddressSpace(CreateDesc("FlushRegionB"), producer);
            var coord = new VirtualTexturePageCoord(0, 0, 0);

            RequestPage(firstSpaceId, coord);
            IssueFeedback(secondSpaceId, coord);

            int flushedCount = VirtualTextureSystem.FlushRegion(secondSpaceId, 0, new RectInt(0, 0, 1, 1));

            Assert.That(flushedCount, Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                firstSpaceId,
                coord,
                out VirtualTexturePageTableEntry firstEntry), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                secondSpaceId,
                coord,
                out VirtualTexturePageTableEntry secondEntry), Is.True);
            Assert.That(firstEntry.Resident, Is.True);
            Assert.That(secondEntry.Resident, Is.False);
            Assert.That(secondEntry.Fallback, Is.True);

            VTPhysicalPoolStats stats = VirtualTextureSystem.GetPhysicalPoolStatsForTesting();
            Assert.That(stats.ResidentPageCount, Is.EqualTo(2));
        }

        [Test]
        public void Update_ReportsPhysicalPoolStats()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("DebugPoolA");
            VirtualTextureSystem.RegisterAddressSpace(desc, new NamedProducer("DebugProducerA"));
            VirtualTextureSystem.RegisterAddressSpace(CreateDesc("DebugPoolB"), new NamedProducer("DebugProducerB"));

            UpdateOnce();

            VirtualTextureStats stats = VirtualTextureStatsRegistry.LastStats;
            Assert.That(stats.ActiveSpaceCount, Is.EqualTo(2));
            Assert.That(stats.PhysicalPoolCount, Is.EqualTo(1));
            Assert.That(stats.PhysicalPoolResidentPageCount, Is.EqualTo(2));
            Assert.That(stats.PhysicalPoolFreePageCount, Is.EqualTo(desc.CachePageCount - 2));
            Assert.That(stats.PhysicalPoolLockedPageCount, Is.EqualTo(2));
            Assert.That(stats.ResidentPageCount, Is.EqualTo(stats.PhysicalPoolResidentPageCount));
            Assert.That(stats.FreePageCount, Is.EqualTo(stats.PhysicalPoolFreePageCount));
        }

        private static VirtualTextureSpaceDesc CreateDesc(string name)
        {
            return new VirtualTextureSpaceDesc(
                name,
                pageSize: 128,
                borderSize: 4,
                virtualPageCountX: 4,
                virtualPageCountY: 4,
                mipCount: 3,
                cachePageCount: 6,
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: 4,
                feedbackCapacity: 32);
        }

        private static VirtualTextureUploadRequest RequestPage(
            int spaceId,
            in VirtualTexturePageCoord coord)
        {
            VirtualTextureUploadRequest request = GetLastPendingUpload(spaceId, coord);
            Assert.That(VirtualTextureSystem.CommitUpload(request), Is.True);
            return request;
        }

        private static VirtualTextureUploadRequest GetLastPendingUpload(
            int spaceId,
            in VirtualTexturePageCoord coord)
        {
            IssueFeedback(spaceId, coord);
            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var requests), Is.True);
            Assert.That(requests.Count, Is.GreaterThan(0));
            return requests.Last();
        }

        private static void IssueFeedback(int spaceId, params VirtualTexturePageCoord[] coords)
        {
            var commandBuffer = new CommandBuffer();
            try
            {
                foreach (VirtualTexturePageCoord coord in coords)
                    VirtualTextureSystem.InjectCompletedReadbackForTesting(
                        CameraType.Game,
                        VirtualTextureFeedbackProcessor.EncodeKey(spaceId, coord));

                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        private static void UpdateOnce()
        {
            var commandBuffer = new CommandBuffer();
            try
            {
                VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }
    }
}
