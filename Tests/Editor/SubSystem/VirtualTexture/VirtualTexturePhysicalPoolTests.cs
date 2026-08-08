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
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(firstSpaceId, out Texture2D firstCache), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(secondSpaceId, out Texture2D secondCache), Is.True);
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
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(firstSpaceId, out Texture2D firstCache), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(secondSpaceId, out Texture2D secondCache), Is.True);
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
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(firstSpaceId, out Texture2D firstCache), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(secondSpaceId, out Texture2D secondCache), Is.True);
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

            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(spaceId, 0, out Texture2D baseCache), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(spaceId, 1, out Texture2D normalCache), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPhysicalCacheForTesting(spaceId, 2, out _), Is.False);
            Assert.That(ReferenceEquals(baseCache, normalCache), Is.False);
            AssertPhysicalAtlas(baseCache, desc, groupLayerCount: 1);
            Assert.That(baseCache.graphicsFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            AssertPhysicalAtlas(normalCache, desc, groupLayerCount: 1);
            Assert.That(normalCache.graphicsFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void PhysicalAtlasLayout_PacksGpuDrivenCapacityIntoNearSquareTexture()
        {
            var layout = new VTPhysicalAtlasLayout(
                physicalPageSize: 136,
                tileCount: 512 * 3,
                maxTextureSize: 8192);

            Assert.That(layout.TileCountX, Is.EqualTo(40));
            Assert.That(layout.TileCountY, Is.EqualTo(39));
            Assert.That(layout.Width, Is.EqualTo(5440));
            Assert.That(layout.Height, Is.EqualTo(5304));
            Assert.That(layout.GetTileRect(1535), Is.EqualTo(new RectInt(2040, 5168, 136, 136)));
        }

        [Test]
        public void PhysicalAtlasLayout_RejectsCapacityBeyondDeviceTextureLimit()
        {
            Assert.That(
                () => new VTPhysicalAtlasLayout(
                    physicalPageSize: 136,
                    tileCount: 512 * 3,
                    maxTextureSize: 4096),
                Throws.TypeOf<System.InvalidOperationException>()
                    .With.Message.Contains("requires 1536 atlas tiles"));
        }

        [Test]
        public void PhysicalPool_MapsPageAndLayerToStableAtlasTile()
        {
            var layers = new[]
            {
                new VTLayerDesc(
                    VTLayerSemantic.BaseColor,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    sRGB: false,
                    new Color32(255, 255, 255, 255)),
                new VTLayerDesc(
                    VTLayerSemantic.Normal,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    sRGB: false,
                    new Color32(128, 128, 255, 255)),
            };
            using var pool = new VTPhysicalPool(
                "AtlasMapping",
                new VTPhysicalPoolDesc(
                    pageSize: 4,
                    borderSize: 0,
                    pageCount: 4,
                    layers));

            Assert.That(pool.GetAtlasLayoutForGroup(0).TileCountX, Is.EqualTo(3));
            Assert.That(
                pool.GetPhysicalTileRect(physicalGroup: 0, physicalPageId: 2, physicalLayerIndex: 1),
                Is.EqualTo(new RectInt(8, 4, 4, 4)));
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
            for (int frameOffset = 0;
                 frameOffset < VTPhysicalPool.FeedbackEvictionProtectionFrames;
                 frameOffset++)
            {
                UpdateOnce();
            }
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
        public void ProcessRequests_ProtectsResidentBatchBeforeAllocatingHigherPriorityFault()
        {
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(
                CreateDesc("ResidentBatchProtection", cachePageCount: 2),
                new NamedProducer("ResidentBatchProtectionProducer"));
            var residentCoord = new VirtualTexturePageCoord(0, 0, 1);
            var missingFineCoord = new VirtualTexturePageCoord(0, 0, 0);

            RequestPage(spaceId, residentCoord);
            for (int frameOffset = 0;
                 frameOffset < VTPhysicalPool.FeedbackEvictionProtectionFrames;
                 frameOffset++)
            {
                UpdateOnce();
            }

            // GPU feedback can be stale by several frames. The whole resolved batch must be
            // touched before any fault is allowed to select an eviction candidate.
            IssueFeedback(spaceId, missingFineCoord, residentCoord);

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                residentCoord,
                out VirtualTexturePageTableEntry residentEntry), Is.True);
            Assert.That(residentEntry.Resident, Is.True);
            Assert.That(residentEntry.PendingUpload, Is.False);
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                missingFineCoord,
                out VirtualTexturePageTableEntry missingEntry), Is.True);
            Assert.That(missingEntry.Resident, Is.False);
            Assert.That(missingEntry.PendingUpload, Is.False);
            Assert.That(missingEntry.Fallback, Is.True);
            Assert.That(
                VirtualTextureSystem.GetPhysicalPoolStatsForTesting().EvictedPageCount,
                Is.Zero);
        }

        [Test]
        public void ProcessRequests_TouchesResolvedFallbackAncestorBeforeAllocatingFault()
        {
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(
                CreateDesc("FallbackAncestorTouch", cachePageCount: 3),
                new NamedProducer("FallbackAncestorTouchProducer"));
            var ancestorCoord = new VirtualTexturePageCoord(0, 0, 1);
            var victimCoord = new VirtualTexturePageCoord(3, 3, 0);
            var missingChildCoord = new VirtualTexturePageCoord(0, 0, 0);

            VirtualTextureUploadRequest ancestor = RequestPage(spaceId, ancestorCoord);
            VirtualTextureUploadRequest victim = RequestPage(spaceId, victimCoord);
            for (int frameOffset = 0;
                 frameOffset < VTPhysicalPool.FeedbackEvictionProtectionFrames;
                 frameOffset++)
            {
                UpdateOnce();
            }

            IssueFeedback(spaceId, missingChildCoord);

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                ancestorCoord,
                out VirtualTexturePageTableEntry ancestorEntry), Is.True);
            Assert.That(ancestorEntry.Resident, Is.True);
            Assert.That(ancestorEntry.PhysicalPageId, Is.EqualTo(ancestor.PhysicalPageId));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                victimCoord,
                out VirtualTexturePageTableEntry victimEntry), Is.True);
            Assert.That(victimEntry.Resident, Is.False);
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                missingChildCoord,
                out VirtualTexturePageTableEntry childEntry), Is.True);
            Assert.That(childEntry.PendingUpload, Is.True);
            Assert.That(childEntry.PhysicalPageId, Is.EqualTo(victim.PhysicalPageId));
        }

        [Test]
        public void ProcessRequests_RefinesTowardFaultByAtMostTwoMipsPerRequest()
        {
            var desc = new VirtualTextureSpaceDesc(
                "TwoMipRefinement",
                pageSize: 128,
                borderSize: 4,
                virtualPageCountX: 256,
                virtualPageCountY: 256,
                mipCount: 9,
                cachePageCount: 6,
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: 1,
                feedbackCapacity: 32);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(
                desc,
                new NamedProducer("TwoMipRefinementProducer"));
            var requestedCoord = new VirtualTexturePageCoord(173, 91, 0);
            var expectedRefinementCoords = new[]
            {
                new VirtualTexturePageCoord(2, 1, 6),
                new VirtualTexturePageCoord(10, 5, 4),
                new VirtualTexturePageCoord(43, 22, 2),
                requestedCoord,
            };

            foreach (VirtualTexturePageCoord expectedCoord in expectedRefinementCoords)
            {
                IssueFeedback(spaceId, requestedCoord);
                Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(
                    spaceId,
                    out var pendingRequests), Is.True);
                Assert.That(pendingRequests, Has.Count.EqualTo(1));
                Assert.That(pendingRequests[0].PageCoord, Is.EqualTo(expectedCoord));
                Assert.That(VirtualTextureSystem.CommitUpload(pendingRequests[0]), Is.True);
            }
        }

        [Test]
        public void ProcessRequests_MergesHitCountsForSharedRefinementAncestor()
        {
            var desc = new VirtualTextureSpaceDesc(
                "MergedRefinementHits",
                pageSize: 128,
                borderSize: 4,
                virtualPageCountX: 256,
                virtualPageCountY: 256,
                mipCount: 9,
                cachePageCount: 2,
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: 1,
                feedbackCapacity: 32);
            int spaceId = VirtualTextureSystem.RegisterAddressSpace(
                desc,
                new NamedProducer("MergedRefinementHitsProducer"));
            var firstFineCoord = new VirtualTexturePageCoord(1, 1, 0);
            var secondFineCoord = new VirtualTexturePageCoord(2, 2, 0);

            IssueFeedback(
                spaceId,
                firstFineCoord,
                firstFineCoord,
                secondFineCoord,
                secondFineCoord,
                secondFineCoord);

            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(
                spaceId,
                out var pendingRequests), Is.True);
            Assert.That(pendingRequests, Has.Count.EqualTo(1));
            Assert.That(pendingRequests[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 6)));
            Assert.That(pendingRequests[0].Priority, Is.EqualTo(5));
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
                RectInt firstPhysicalTile = pool.GetPhysicalTileRect(
                    physicalGroup: 0,
                    physicalPageId: firstPhysicalPageId,
                    physicalLayerIndex: 0);

                pool.Touch(firstPhysicalPageId, VirtualTextureViewId.Invalid, frameIndex: 1, updateAffinity: false);
                pool.Touch(secondPhysicalPageId, VirtualTextureViewId.Invalid, frameIndex: 1, updateAffinity: false);
                pool.Touch(firstPhysicalPageId, VirtualTextureViewId.Invalid, frameIndex: 1, updateAffinity: false);

                Assert.That(TryAllocatePhysicalPage(
                    pool,
                    owner,
                    producerHandle,
                    pageIndex: 30,
                    new VirtualTexturePageCoord(2, 0, 0),
                    frameIndex: 1 + VTPhysicalPool.FeedbackEvictionProtectionFrames - 1,
                    out _), Is.False);

                Assert.That(TryAllocatePhysicalPage(
                    pool,
                    owner,
                    producerHandle,
                    pageIndex: 30,
                    new VirtualTexturePageCoord(2, 0, 0),
                    frameIndex: 1 + VTPhysicalPool.FeedbackEvictionProtectionFrames,
                    out int reusedPhysicalPageId), Is.True);
                Assert.That(reusedPhysicalPageId, Is.EqualTo(firstPhysicalPageId));
                Assert.That(
                    pool.GetPhysicalTileRect(0, reusedPhysicalPageId, 0),
                    Is.EqualTo(firstPhysicalTile));
                Assert.That(owner.LastInvalidatedPageIndex, Is.EqualTo(10));
            }
            finally
            {
                pool.Dispose();
            }
        }

        [Test]
        public void EvictionCandidate_UsesMipOnlyToBreakSameFrameTouchTies()
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
                    new VirtualTexturePageCoord(0, 0, 1),
                    frameIndex: 1,
                    out int coarsePhysicalPageId), Is.True);
                Assert.That(TryAllocatePhysicalPage(
                    pool,
                    owner,
                    producerHandle,
                    pageIndex: 20,
                    new VirtualTexturePageCoord(0, 0, 0),
                    frameIndex: 1,
                    out int finePhysicalPageId), Is.True);

                Assert.That(TryAllocatePhysicalPage(
                    pool,
                    owner,
                    producerHandle,
                    pageIndex: 30,
                    new VirtualTexturePageCoord(1, 0, 0),
                    frameIndex: 1 + VTPhysicalPool.FeedbackEvictionProtectionFrames,
                    out int reusedPhysicalPageId), Is.True);

                Assert.That(reusedPhysicalPageId, Is.EqualTo(finePhysicalPageId));
                Assert.That(reusedPhysicalPageId, Is.Not.EqualTo(coarsePhysicalPageId));
                Assert.That(owner.LastInvalidatedPageIndex, Is.EqualTo(20));
            }
            finally
            {
                pool.Dispose();
            }
        }

        [Test]
        public void AsyncCommit_ProtectsNewlyVisiblePageUntilFeedbackCanTouchIt()
        {
            VTPhysicalPool pool = CreatePhysicalPoolForTesting(pageCount: 1);
            var owner = new PhysicalPoolOwner(1);
            var producerHandle = new VTProducerHandle(1);

            try
            {
                Assert.That(pool.TryAllocatePage(
                    owner,
                    producerHandle,
                    "IndexedProducer",
                    pageIndex: 10,
                    pageMip: 0,
                    new VirtualTexturePageCoord(0, 0, 0),
                    VirtualTextureViewId.Invalid,
                    VirtualTextureViewId.Invalid,
                    updateAffinity: false,
                    frameIndex: 1,
                    locked: false,
                    pendingUpload: true,
#if VT_DEBUG
                    default(VTPageRequestDebugInfo),
#endif
                    out int physicalPageId,
                    out int generation,
                    out _), Is.True);
                Assert.That(pool.TryCommitPage(
                    physicalPageId,
                    generation,
                    commitFrameIndex: 10), Is.True);

                for (int frameIndex = 10;
                     frameIndex < 10 + VTPhysicalPool.AsyncCommitEvictionProtectionFrames;
                     frameIndex++)
                {
                    Assert.That(TryAllocatePhysicalPage(
                        pool,
                        owner,
                        producerHandle,
                        pageIndex: 20,
                        new VirtualTexturePageCoord(1, 0, 0),
                        frameIndex,
                        out _), Is.False);
                }

                Assert.That(TryAllocatePhysicalPage(
                    pool,
                    owner,
                    producerHandle,
                    pageIndex: 20,
                    new VirtualTexturePageCoord(1, 0, 0),
                    frameIndex: 10 + VTPhysicalPool.AsyncCommitEvictionProtectionFrames,
                    out int reusedPhysicalPageId), Is.True);
                Assert.That(reusedPhysicalPageId, Is.EqualTo(physicalPageId));
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
#if VT_DEBUG
                default(VTPageRequestDebugInfo),
#endif
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
