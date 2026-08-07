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

        private sealed class PhysicalPoolOwner : IVTPhysicalPoolOwner
        {
            internal PhysicalPoolOwner(int spaceId)
            {
                SpaceId = spaceId;
            }

            public int SpaceId { get; }

            internal int LastInvalidatedPageIndex { get; private set; } = -1;

            public bool OnPhysicalPageInvalidated(int pageIndex, int generation)
            {
                LastInvalidatedPageIndex = pageIndex;
                return true;
            }
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
        public void PhysicalPageIdentity_UsesAConsistentProducerKeyForHashLookup()
        {
            var coord = new VirtualTexturePageCoord(0, 0, 0);
            var firstHandleIdentity = new VTPhysicalPageIdentity(new VTProducerHandle(1), "First", coord);
            var sameHandleIdentity = new VTPhysicalPageIdentity(new VTProducerHandle(1), "Second", coord);
            var namedIdentity = new VTPhysicalPageIdentity(VTProducerHandle.Invalid, "First", coord);
            var sameNamedIdentity = new VTPhysicalPageIdentity(VTProducerHandle.Invalid, "First", coord);

            Assert.That(firstHandleIdentity.Equals(sameHandleIdentity), Is.True);
            Assert.That(firstHandleIdentity.GetHashCode(), Is.EqualTo(sameHandleIdentity.GetHashCode()));
            Assert.That(namedIdentity.Equals(sameNamedIdentity), Is.True);
            Assert.That(namedIdentity.GetHashCode(), Is.EqualTo(sameNamedIdentity.GetHashCode()));
            Assert.That(firstHandleIdentity.Equals(namedIdentity), Is.False);
        }

        [Test]
        public void RegisterAddressSpace_SeparatesPhysicalPools_WhenLayerPhysicalGroupDiffers()
        {
            VirtualTextureSpaceDesc firstDesc = CreateLayeredDesc("PhysicalGroupA", normalPhysicalGroup: 0);
            VirtualTextureSpaceDesc secondDesc = CreateLayeredDesc("PhysicalGroupB", normalPhysicalGroup: 1);

            int firstSpaceId = VirtualTextureSystem.RegisterAddressSpace(firstDesc, new NamedProducer("GroupProducerA"));
            int secondSpaceId = VirtualTextureSystem.RegisterAddressSpace(secondDesc, new NamedProducer("GroupProducerB"));

            Assert.That(VirtualTextureSystem.GetPhysicalPoolCountForTesting(), Is.EqualTo(2));
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(firstSpaceId, out Texture2DArray firstCache), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(secondSpaceId, out Texture2DArray secondCache), Is.True);
            Assert.That(ReferenceEquals(firstCache, secondCache), Is.False);
        }

        [Test]
        public void RegisterAddressSpace_SeparatesPhysicalPools_WhenLayerFormatDiffers()
        {
            VirtualTextureSpaceDesc firstDesc = CreateLayeredDesc(
                "LayerFormatA",
                normalFormat: GraphicsFormat.R8G8B8A8_UNorm,
                normalPhysicalGroup: 1);
            VirtualTextureSpaceDesc secondDesc = CreateLayeredDesc(
                "LayerFormatB",
                normalFormat: GraphicsFormat.R8G8B8A8_SRGB,
                normalPhysicalGroup: 1);

            int firstSpaceId = VirtualTextureSystem.RegisterAddressSpace(firstDesc, new NamedProducer("FormatProducerA"));
            int secondSpaceId = VirtualTextureSystem.RegisterAddressSpace(secondDesc, new NamedProducer("FormatProducerB"));

            Assert.That(VirtualTextureSystem.GetPhysicalPoolCountForTesting(), Is.EqualTo(2));
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(firstSpaceId, out Texture2DArray firstCache), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(secondSpaceId, out Texture2DArray secondCache), Is.True);
            Assert.That(ReferenceEquals(firstCache, secondCache), Is.False);
        }

        [Test]
        public void RegisterAddressSpace_CreatesPhysicalTexturePerGroup()
        {
            VirtualTextureSpaceDesc desc = CreateLayeredDesc(
                "SplitPhysicalGroups",
                normalFormat: GraphicsFormat.R16G16B16A16_SFloat,
                normalPhysicalGroup: 1);

            int spaceId = VirtualTextureSystem.RegisterAddressSpace(desc, new NamedProducer("SplitGroupProducer"));

            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(spaceId, 0, out Texture2DArray baseCache), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(spaceId, 1, out Texture2DArray normalCache), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(spaceId, 2, out _), Is.False);
            Assert.That(ReferenceEquals(baseCache, normalCache), Is.False);
            Assert.That(baseCache.width, Is.EqualTo(desc.PhysicalPageSize));
            Assert.That(baseCache.height, Is.EqualTo(desc.PhysicalPageSize));
            Assert.That(baseCache.depth, Is.EqualTo(desc.CachePageCount));
            Assert.That(baseCache.graphicsFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(normalCache.width, Is.EqualTo(desc.PhysicalPageSize));
            Assert.That(normalCache.height, Is.EqualTo(desc.PhysicalPageSize));
            Assert.That(normalCache.depth, Is.EqualTo(desc.CachePageCount));
            Assert.That(normalCache.graphicsFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void LayerDesc_RejectsCompressedCacheFormatUntilBlockCodecIsAvailable()
        {
            Assert.That(
                () => new VTLayerDesc(
                    VTLayerSemantic.BaseColor,
                    GraphicsFormat.RGBA_DXT1_UNorm,
                    sRGB: false,
                    new Color32(0, 0, 0, 255)),
                Throws.ArgumentException.With.Message.Contains("block-compressed upload codec"));
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
        public void ProcessRequests_DoesNotAttachStaleIdentityAfterPhysicalPageReuse()
        {
            var producer = new NamedProducer("ReusedIdentityProducer");
            VirtualTextureSpaceDesc desc = CreateDesc("ReusedIdentityA", cachePageCount: 2);
            int firstSpaceId = VirtualTextureSystem.RegisterAddressSpace(desc, producer);
            int secondSpaceId = VirtualTextureSystem.RegisterAddressSpace(
                CreateDesc("ReusedIdentityB", cachePageCount: 2),
                producer);
            var firstCoord = new VirtualTexturePageCoord(0, 0, 0);
            var replacementCoord = new VirtualTexturePageCoord(1, 0, 0);

            RequestPage(firstSpaceId, firstCoord);
            RequestPage(firstSpaceId, replacementCoord);
            IssueFeedback(secondSpaceId, firstCoord);

            Assert.That(VirtualTextureSystem.GetPendingUploadCountForTesting(secondSpaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                secondSpaceId,
                firstCoord,
                out VirtualTexturePageTableEntry entry), Is.True);
            Assert.That(entry.PendingUpload, Is.True);
            Assert.That(entry.Resident, Is.False);
        }

        [Test]
        public void PhysicalPageLookup_PreservesDuplicateIdentityChainWhenFirstSlotIsFlushed()
        {
            VTPhysicalPool pool = CreatePhysicalPoolForTesting(pageCount: 2);
            var firstOwner = new PhysicalPoolOwner(1);
            var secondOwner = new PhysicalPoolOwner(2);
            var producerHandle = new VTProducerHandle(1);
            var coord = new VirtualTexturePageCoord(0, 0, 0);

            try
            {
                Assert.That(TryAllocatePhysicalPage(
                    pool,
                    firstOwner,
                    producerHandle,
                    pageIndex: 10,
                    coord,
                    frameIndex: 0,
                    out int firstPhysicalPageId), Is.True);
                Assert.That(TryAllocatePhysicalPage(
                    pool,
                    secondOwner,
                    producerHandle,
                    pageIndex: 20,
                    coord,
                    frameIndex: 0,
                    out int secondPhysicalPageId), Is.True);
                Assert.That(firstPhysicalPageId, Is.LessThan(secondPhysicalPageId));

                Assert.That(pool.TryFindPhysicalPage(
                    producerHandle,
                    "IndexedProducer",
                    coord,
                    out int foundPhysicalPageId,
                    out _), Is.True);
                Assert.That(foundPhysicalPageId, Is.EqualTo(firstPhysicalPageId));

                Assert.That(pool.FlushOwner(firstOwner), Is.EqualTo(1));
                Assert.That(pool.TryFindPhysicalPage(
                    producerHandle,
                    "IndexedProducer",
                    coord,
                    out foundPhysicalPageId,
                    out _), Is.True);
                Assert.That(foundPhysicalPageId, Is.EqualTo(secondPhysicalPageId));
            }
            finally
            {
                pool.Dispose();
            }
        }

        [Test]
        public void Touch_DeduplicatesLruMutationWithinTheSameFrame()
        {
            VTPhysicalPool pool = CreatePhysicalPoolForTesting(pageCount: 2);
            var owner = new PhysicalPoolOwner(1);
            var producerHandle = new VTProducerHandle(1);

            try
            {
                Assert.That(TryAllocatePhysicalPage(
                    pool,
                    owner,
                    producerHandle,
                    pageIndex: 10,
                    new VirtualTexturePageCoord(0, 0, 0),
                    frameIndex: 0,
                    out int firstPhysicalPageId), Is.True);
                Assert.That(TryAllocatePhysicalPage(
                    pool,
                    owner,
                    producerHandle,
                    pageIndex: 20,
                    new VirtualTexturePageCoord(1, 0, 0),
                    frameIndex: 0,
                    out int secondPhysicalPageId), Is.True);

                pool.Touch(firstPhysicalPageId, VirtualTextureViewId.Invalid, frameIndex: 1, updateAffinity: false);
                pool.Touch(secondPhysicalPageId, VirtualTextureViewId.Invalid, frameIndex: 1, updateAffinity: false);
                pool.Touch(firstPhysicalPageId, VirtualTextureViewId.Invalid, frameIndex: 1, updateAffinity: false);

                Assert.That(TryAllocatePhysicalPage(
                    pool,
                    owner,
                    producerHandle,
                    pageIndex: 30,
                    new VirtualTexturePageCoord(2, 0, 0),
                    frameIndex: 2,
                    out int reusedPhysicalPageId), Is.True);
                Assert.That(reusedPhysicalPageId, Is.EqualTo(firstPhysicalPageId));
                Assert.That(owner.LastInvalidatedPageIndex, Is.EqualTo(10));
            }
            finally
            {
                pool.Dispose();
            }
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

        private static VirtualTextureSpaceDesc CreateDesc(string name, int cachePageCount = 6)
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
                maxUploadsPerFrame: 4,
                feedbackCapacity: 32);
        }

        private static VirtualTextureSpaceDesc CreateLayeredDesc(
            string name,
            GraphicsFormat normalFormat = GraphicsFormat.R8G8B8A8_UNorm,
            int normalPhysicalGroup = 0)
        {
            var stackDesc = new VTStackDesc(
                pageSize: 128,
                borderSize: 4,
                cachePageCount: 6,
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
                        normalFormat,
                        sRGB: false,
                        new Color32(128, 128, 255, 255),
                        physicalGroup: normalPhysicalGroup),
                },
                maxUploadsPerFrame: 4,
                feedbackCapacity: 32);

            return new VirtualTextureSpaceDesc(
                name,
                virtualPageCountX: 4,
                virtualPageCountY: 4,
                mipCount: 3,
                stackDesc);
        }

        private static VTPhysicalPool CreatePhysicalPoolForTesting(int pageCount)
        {
            var layers = new[]
            {
                new VTLayerDesc(
                    VTLayerSemantic.BaseColor,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    sRGB: false,
                    new Color32(255, 255, 255, 255)),
            };
            return new VTPhysicalPool(
                "IndexedPool",
                new VTPhysicalPoolDesc(
                    pageSize: 4,
                    borderSize: 0,
                    pageCount,
                    layers));
        }

        private static bool TryAllocatePhysicalPage(
            VTPhysicalPool pool,
            IVTPhysicalPoolOwner owner,
            VTProducerHandle producerHandle,
            int pageIndex,
            in VirtualTexturePageCoord coord,
            int frameIndex,
            out int physicalPageId)
        {
            return pool.TryAllocatePage(
                owner,
                producerHandle,
                "IndexedProducer",
                pageIndex,
                pageMip: coord.Mip,
                coord,
                VirtualTextureViewId.Invalid,
                VirtualTextureViewId.Invalid,
                updateAffinity: false,
                frameIndex,
                locked: false,
                pendingUpload: false,
                out physicalPageId,
                out _,
                out _);
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
