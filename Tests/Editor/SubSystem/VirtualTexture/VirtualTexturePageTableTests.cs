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
                locked: true,
                transitionPhase: 1);

            Assert.That(entry.PhysicalPageId, Is.EqualTo(17));
            Assert.That(entry.ResolvedMip, Is.EqualTo(3));
            Assert.That(entry.Resident, Is.True);
            Assert.That(entry.Fallback, Is.False);
            Assert.That(entry.PendingUpload, Is.True);
            Assert.That(entry.Locked, Is.True);
            Assert.That(entry.TransitionPhase, Is.EqualTo(1));
            Assert.That(entry.IsMapped, Is.True);
        }

        [Test]
        public void PageTableEntry_AllowsResidentPageToExposeStableFallbackDuringTransition()
        {
            var entry = new VirtualTexturePageTableEntry(
                physicalPageId: 9,
                resolvedMip: 2,
                resident: true,
                fallback: true,
                pendingUpload: false,
                locked: false);

            Assert.That(entry.Resident, Is.True);
            Assert.That(entry.Fallback, Is.True);
            Assert.That(entry.IsMapped, Is.True);
        }

        [Test]
        public void PageTableEntry_ReservesInvalidPhysicalIdAndUsesHighBitsForTransitionPhase()
        {
            Assert.That(VirtualTexturePageTableEntry.MaxPhysicalPageId,
                Is.EqualTo(VirtualTexturePageTableEntry.InvalidPhysicalPageId - 1));
            Assert.That(VirtualTexturePageTableEntry.TransitionPhaseBitCount, Is.EqualTo(2));
            Assert.That(VirtualTexturePageTableEntry.MaxTransitionPhase, Is.EqualTo(3));
            Assert.That(VirtualTexturePageTableEntry.ReservedBitCount, Is.Zero);
            Assert.That(
                () => new VirtualTexturePageTableEntry(
                    VirtualTexturePageTableEntry.InvalidPhysicalPageId,
                    resolvedMip: 0,
                    resident: true,
                    fallback: false,
                    pendingUpload: false,
                    locked: false),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(
                () => new VirtualTexturePageTableEntry(
                    physicalPageId: 0,
                    resolvedMip: 0,
                    resident: true,
                    fallback: false,
                    pendingUpload: false,
                    locked: false,
                    transitionPhase: 4),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
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
        public void StackDesc_ExposesLayerSemanticsFormatsAndFallbacks()
        {
            var baseFallback = new Color32(10, 20, 30, 255);
            var normalFallback = new Color32(128, 128, 255, 255);
            var stackDesc = new VTStackDesc(
                pageSize: 128,
                borderSize: 4,
                cachePageCount: 16,
                layers: new[]
                {
                    new VTLayerDesc(
                        VTLayerSemantic.BaseColor,
                        GraphicsFormat.R8G8B8A8_SRGB,
                        sRGB: true,
                        baseFallback,
                        physicalGroup: 0),
                    new VTLayerDesc(
                        VTLayerSemantic.Normal,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        sRGB: false,
                        normalFallback,
                        physicalGroup: 0),
                },
                maxUploadsPerFrame: 8,
                feedbackCapacity: 64);

            Assert.That(stackDesc.LayerCount, Is.EqualTo(2));
            Assert.That(stackDesc.GraphicsFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));
            Assert.That(stackDesc.SRGB, Is.True);
            Assert.That(stackDesc.FallbackColor, Is.EqualTo(baseFallback));
            Assert.That(stackDesc.TryGetLayerIndex(VTLayerSemantic.BaseColor, out int baseLayer), Is.True);
            Assert.That(baseLayer, Is.EqualTo(0));
            Assert.That(stackDesc.TryGetLayerIndex(VTLayerSemantic.Normal, out int normalLayer), Is.True);
            Assert.That(normalLayer, Is.EqualTo(1));
            Assert.That(stackDesc.GetLayer(normalLayer).SRGB, Is.False);
            Assert.That(stackDesc.GetLayer(normalLayer).FallbackColor, Is.EqualTo(normalFallback));
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
        public void SpaceDesc_PrecomputesExpandedPageTableCapacity()
        {
            VirtualTextureSpaceDesc desc = CreateDesc(
                "ExpandedCapacity",
                virtualPageCountX: 256,
                virtualPageCountY: 256,
                mipCount: 9,
                cachePageCount: 512,
                maxUploadsPerFrame: 16);

            Assert.That(desc.PageTableEntryCount, Is.EqualTo(87381));
            Assert.That(
                VirtualTextureSpaceUtility.GetTotalPageCount(256, 256, 9),
                Is.EqualTo(desc.PageTableEntryCount));
        }

        [Test]
        public void SpaceDesc_RejectsPageTablesWhoseBufferByteSizeWouldOverflow()
        {
            Assert.That(
                () => CreateDesc(
                    "PageTableOverflow",
                    virtualPageCountX: 65536,
                    virtualPageCountY: 8192,
                    mipCount: 1,
                    cachePageCount: 4,
                    maxUploadsPerFrame: 4),
                Throws.TypeOf<System.ArgumentOutOfRangeException>()
                    .With.Message.Contains("page table requires"));
        }

        [Test]
        public void SpaceDesc_RejectsDimensionsThatFeedbackCannotEncode()
        {
            Assert.That(
                () => CreateDesc(
                    "FeedbackOverflow",
                    VirtualTextureFeedbackProcessor.MaxPageCountPerDimension + 1,
                    virtualPageCountY: 1,
                    mipCount: 1,
                    cachePageCount: 4,
                    maxUploadsPerFrame: 4),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void SpaceUtility_ComputePageLocalUv_UsesLastPageEdge_WhenUvIsOne()
        {
            var uv = new Vector2(1f, 1f);

            VirtualTexturePageCoord pageCoord = VirtualTextureSpaceUtility.GetPageCoord(4, 4, 0, uv);
            Vector2 localUv = VirtualTextureSpaceUtility.ComputePageLocalUv(4, 4, pageCoord, uv);

            Assert.That(pageCoord, Is.EqualTo(new VirtualTexturePageCoord(3, 3, 0)));
            Assert.That(localUv.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(localUv.y, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void SpaceUtility_ComputePhysicalUVW_UsesNextPageStartAtTileBoundary()
        {
            var desc = CreateDesc("Addressing", 4, 4, 3, 4, 4);
            var entry = new VirtualTexturePageTableEntry(
                physicalPageId: 7,
                resolvedMip: 0,
                resident: true,
                fallback: false,
                pendingUpload: false,
                locked: false);

            Vector3 uvw = VirtualTextureSpaceUtility.ComputePhysicalUVW(desc, new Vector2(0.25f, 0.25f), entry);

            float expectedStart = (float)desc.BorderSize / desc.PhysicalPageSize;
            Assert.That(uvw.x, Is.EqualTo(expectedStart).Within(0.0001f));
            Assert.That(uvw.y, Is.EqualTo(expectedStart).Within(0.0001f));
            Assert.That(uvw.z, Is.EqualTo(7f));
        }

        [Test]
        public void SpaceUtility_ComputePhysicalUVW_DoesNotWrapAtMaxUv()
        {
            var desc = CreateDesc("AddressEdge", 4, 4, 3, 4, 4);
            var entry = new VirtualTexturePageTableEntry(
                physicalPageId: 5,
                resolvedMip: 0,
                resident: true,
                fallback: false,
                pendingUpload: false,
                locked: false);

            Vector3 uvw = VirtualTextureSpaceUtility.ComputePhysicalUVW(desc, new Vector2(1f, 1f), entry);

            float expectedEnd = (float)(desc.BorderSize + desc.PageSize) / desc.PhysicalPageSize;
            Assert.That(uvw.x, Is.EqualTo(expectedEnd).Within(0.0001f));
            Assert.That(uvw.y, Is.EqualTo(expectedEnd).Within(0.0001f));
            Assert.That(uvw.z, Is.EqualTo(5f));
        }

        [Test]
        public void SpaceUtility_ComputePhysicalUVW_OffsetsSliceByLayer()
        {
            var stackDesc = new VTStackDesc(
                pageSize: 128,
                borderSize: 4,
                cachePageCount: 8,
                layers: new[]
                {
                    new VTLayerDesc(
                        VTLayerSemantic.BaseColor,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        sRGB: false,
                        new Color32(0, 0, 0, 255)),
                    new VTLayerDesc(
                        VTLayerSemantic.Normal,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        sRGB: false,
                        new Color32(128, 128, 255, 255)),
                },
                maxUploadsPerFrame: 4,
                feedbackCapacity: 32);
            var desc = new VirtualTextureSpaceDesc("LayerUVW", 4, 4, 3, stackDesc);
            var entry = new VirtualTexturePageTableEntry(
                physicalPageId: 5,
                resolvedMip: 0,
                resident: true,
                fallback: false,
                pendingUpload: false,
                locked: false);

            Vector3 baseUv = VirtualTextureSpaceUtility.ComputePhysicalUVW(desc, new Vector2(0.5f, 0.5f), entry, 0);
            Vector3 normalUv = VirtualTextureSpaceUtility.ComputePhysicalUVW(desc, new Vector2(0.5f, 0.5f), entry, 1);

            Assert.That(baseUv.z, Is.EqualTo(10f));
            Assert.That(normalUv.z, Is.EqualTo(11f));
        }

        [Test]
        public void PageTable_MaterializesFixedTransitionAncestorWithoutShaderRecursion()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("Fallback", 4, 4, 3, 4, 4));

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 2),
                out VirtualTexturePageTableEntry rootEntry), Is.True);
            Assert.That(rootEntry.Resident, Is.True);
            Assert.That(rootEntry.Locked, Is.True);
            Assert.That(rootEntry.TransitionPhase,
                Is.EqualTo(VirtualTexturePageTableEntry.MaxTransitionPhase));

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(3, 2, 0),
                out VirtualTexturePageTableEntry rootFallbackEntry), Is.True);
            Assert.That(rootFallbackEntry.Fallback, Is.True);
            Assert.That(rootFallbackEntry.ResolvedMip, Is.EqualTo(2));
            Assert.That(rootFallbackEntry.PhysicalPageId, Is.EqualTo(rootEntry.PhysicalPageId));

            VirtualTextureUploadRequest childRequest = RequestAndCommit(spaceId, new VirtualTexturePageCoord(1, 1, 1));

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(3, 2, 0),
                out VirtualTexturePageTableEntry fixedFallbackEntry), Is.True);
            Assert.That(fixedFallbackEntry.Fallback, Is.True);
            Assert.That(fixedFallbackEntry.ResolvedMip, Is.EqualTo(2));
            Assert.That(fixedFallbackEntry.PhysicalPageId, Is.EqualTo(rootEntry.PhysicalPageId));
            Assert.That(
                fixedFallbackEntry.TransitionPhase,
                Is.EqualTo(VirtualTexturePageTableEntry.MaxTransitionPhase));

            VirtualTextureSystem.AdvancePageTransitionsForTesting(
                childRequest.RequestFrame
                + VTResidencyManager.ColdStartPageTransitionFrameCount);

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(3, 2, 0),
                out VirtualTexturePageTableEntry revealedFallbackEntry), Is.True);
            Assert.That(revealedFallbackEntry.Fallback, Is.True);
            Assert.That(revealedFallbackEntry.ResolvedMip, Is.EqualTo(1));
            Assert.That(revealedFallbackEntry.PhysicalPageId, Is.EqualTo(childRequest.PhysicalPageId));
        }

        [Test]
        public void PageTransition_ColdStartKeepsFixedAncestorUntilAcceleratedRevealAge()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(
                CreateDesc("StableAncestorGate", 4, 4, 3, 8, 4));
            _ = RequestAndCommit(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 1));
            VirtualTextureUploadRequest childRequest = RequestAndCommit(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 0));

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                childRequest.PageCoord,
                out VirtualTexturePageTableEntry stagedChildEntry), Is.True);
            Assert.That(stagedChildEntry.Resident, Is.True);
            Assert.That(stagedChildEntry.Fallback, Is.True);
            Assert.That(stagedChildEntry.PendingUpload, Is.False);
            Assert.That(stagedChildEntry.PhysicalPageId, Is.Not.EqualTo(childRequest.PhysicalPageId));
            Assert.That(
                stagedChildEntry.TransitionPhase,
                Is.EqualTo(VirtualTexturePageTableEntry.MaxTransitionPhase));

            VirtualTextureSystem.AdvancePageTransitionsForTesting(
                childRequest.RequestFrame
                + VTResidencyManager.ColdStartPageTransitionFrameCount
                - 1);

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                childRequest.PageCoord,
                out VirtualTexturePageTableEntry stillStagedChildEntry), Is.True);
            Assert.That(stillStagedChildEntry.Fallback, Is.True);
            Assert.That(
                stillStagedChildEntry.PhysicalPageId,
                Is.EqualTo(stagedChildEntry.PhysicalPageId));
            Assert.That(
                stillStagedChildEntry.ResolvedMip,
                Is.EqualTo(stagedChildEntry.ResolvedMip));

            VirtualTextureSystem.AdvancePageTransitionsForTesting(
                childRequest.RequestFrame
                + VTResidencyManager.ColdStartPageTransitionFrameCount);

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                childRequest.PageCoord,
                out VirtualTexturePageTableEntry revealedChildEntry), Is.True);
            Assert.That(revealedChildEntry.Fallback, Is.False);
            Assert.That(revealedChildEntry.PhysicalPageId, Is.EqualTo(childRequest.PhysicalPageId));
            Assert.That(
                revealedChildEntry.TransitionPhase,
                Is.EqualTo(VirtualTexturePageTableEntry.MaxTransitionPhase));
        }

        [Test]
        public void PageTransition_ColdStartCanStartSixteenPagesPerFrame()
        {
            Assert.That(VTResidencyManager.MaxTransitionStartsPerFrame, Is.EqualTo(8));
            Assert.That(VTResidencyManager.ColdStartMaxTransitionStartsPerFrame, Is.EqualTo(16));
            int spaceId = VirtualTextureSystem.RegisterSpace(
                CreateDesc("TransitionStartBudget", 16, 16, 5, 32, 16));
            var coords = new VirtualTexturePageCoord[16];
            for (int pageIndex = 0; pageIndex < coords.Length; pageIndex++)
                coords[pageIndex] = new VirtualTexturePageCoord(pageIndex % 4, pageIndex / 4, 2);

            RequestPages(spaceId, coords);
            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(
                spaceId,
                out var requests), Is.True);
            Assert.That(requests, Has.Count.EqualTo(coords.Length));
            int transitionStartFrame = requests[0].RequestFrame;
            foreach (VirtualTextureUploadRequest request in requests.ToArray())
                Assert.That(VirtualTextureSystem.CommitUpload(request), Is.True);

            int residentCount = 0;
            int queuedCount = 0;
            foreach (VirtualTexturePageCoord coord in coords)
            {
                Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                    spaceId,
                    coord,
                    out VirtualTexturePageTableEntry entry), Is.True);
                residentCount += entry.Resident ? 1 : 0;
                queuedCount += entry.PendingUpload ? 1 : 0;
            }

            Assert.That(
                residentCount,
                Is.EqualTo(VTResidencyManager.ColdStartMaxTransitionStartsPerFrame));
            Assert.That(queuedCount, Is.Zero);

            VirtualTextureSystem.AdvancePageTransitionsForTesting(transitionStartFrame + 1);
            foreach (VirtualTexturePageCoord coord in coords)
            {
                Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                    spaceId,
                    coord,
                    out VirtualTexturePageTableEntry entry), Is.True);
                Assert.That(entry.Resident, Is.True);
                Assert.That(entry.PendingUpload, Is.False);
            }
        }

        [Test]
        public void PageTransition_OlderPageRevealsWhileNewerPageInSameSpaceKeepsTransitioning()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(
                CreateDesc("TransitionPhaseBudget", 16, 16, 5, 24, 8));
            VirtualTextureUploadRequest olderRequest = RequestAndCommit(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 2));
            VirtualTextureUploadRequest newerRequest = RequestAndCommit(
                spaceId,
                new VirtualTexturePageCoord(1, 0, 2));
            Assert.That(newerRequest.RequestFrame, Is.GreaterThan(olderRequest.RequestFrame));

            VirtualTextureSystem.AdvancePageTransitionsForTesting(
                olderRequest.RequestFrame
                + VTResidencyManager.ColdStartPageTransitionFrameCount);
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                olderRequest.PageCoord,
                out VirtualTexturePageTableEntry olderEntry), Is.True);
            Assert.That(
                olderEntry.TransitionPhase,
                Is.EqualTo(VirtualTexturePageTableEntry.MaxTransitionPhase));
            Assert.That(olderEntry.Fallback, Is.False);
            Assert.That(olderEntry.PhysicalPageId, Is.EqualTo(olderRequest.PhysicalPageId));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                newerRequest.PageCoord,
                out VirtualTexturePageTableEntry newerEntry), Is.True);
            Assert.That(newerEntry.Fallback, Is.True);
            Assert.That(newerEntry.PhysicalPageId, Is.Not.EqualTo(newerRequest.PhysicalPageId));

            VirtualTextureSystem.AdvancePageTransitionsForTesting(
                newerRequest.RequestFrame
                + VTResidencyManager.ColdStartPageTransitionFrameCount);
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                newerRequest.PageCoord,
                out newerEntry), Is.True);
            Assert.That(newerEntry.Fallback, Is.False);
            Assert.That(newerEntry.PhysicalPageId, Is.EqualTo(newerRequest.PhysicalPageId));
            Assert.That(
                newerEntry.TransitionPhase,
                Is.EqualTo(VirtualTexturePageTableEntry.MaxTransitionPhase));
        }

        [Test]
        public void PageTransition_AfterColdStartUsesSteadyRevealAge()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(
                CreateDesc("SteadyTransitionAge", 8, 8, 4, 16, 8));
            _ = RequestAndCommit(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 2));
            AdvanceFrames(VTResidencyManager.ColdStartFrameCount);

            VirtualTextureUploadRequest steadyRequest = RequestAndCommit(
                spaceId,
                new VirtualTexturePageCoord(1, 0, 2));
            VirtualTextureSystem.AdvancePageTransitionsForTesting(
                steadyRequest.RequestFrame
                + VTResidencyManager.PageTransitionFrameCount
                - 1);

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                steadyRequest.PageCoord,
                out VirtualTexturePageTableEntry stagedEntry), Is.True);
            Assert.That(stagedEntry.Fallback, Is.True);
            Assert.That(stagedEntry.PhysicalPageId, Is.Not.EqualTo(steadyRequest.PhysicalPageId));

            VirtualTextureSystem.AdvancePageTransitionsForTesting(
                steadyRequest.RequestFrame
                + VTResidencyManager.PageTransitionFrameCount);

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                steadyRequest.PageCoord,
                out VirtualTexturePageTableEntry revealedEntry), Is.True);
            Assert.That(revealedEntry.Fallback, Is.False);
            Assert.That(revealedEntry.PhysicalPageId, Is.EqualTo(steadyRequest.PhysicalPageId));
        }

        [Test]
        public void PageTable_RebuildsDirtySubtreeAndQueuesOnlyChangedEntries()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("PartialPageTable", 8, 8, 4, 16, 4);
            int spaceId = VirtualTextureSystem.RegisterSpace(desc);
            int totalPageCount = VirtualTextureSpaceUtility.GetTotalPageCount(
                desc.VirtualPageCountX,
                desc.VirtualPageCountY,
                desc.MipCount);

            Assert.That(VirtualTextureSystem.GetPageTableLastRecomputedEntryCountForTesting(spaceId),
                Is.EqualTo(totalPageCount));
            Assert.That(VirtualTextureSystem.GetPageTableLastUploadedEntryCountForTesting(spaceId),
                Is.EqualTo(totalPageCount));
            Assert.That(VirtualTextureSystem.GetPageTableFullUploadCountForTesting(spaceId), Is.EqualTo(1));

            var coord = new VirtualTexturePageCoord(0, 0, 2);
            RequestPages(spaceId, coord);

            // One mip-2 page covers 1 + 4 + 16 entries down through mip zero. Only
            // the requested entry changes its pending flag before the RDG upload is recorded.
            Assert.That(VirtualTextureSystem.GetPageTableLastRecomputedEntryCountForTesting(spaceId), Is.EqualTo(21));
            Assert.That(VirtualTextureSystem.GetPageTableLastUploadedEntryCountForTesting(spaceId),
                Is.EqualTo(totalPageCount));
            Assert.That(VirtualTextureSystem.GetPageTablePendingUploadEntryCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPageTableSparseUploadCountForTesting(spaceId), Is.EqualTo(0));
            Assert.That(VirtualTextureSystem.GetPageTableFullUploadCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPageTableScatterUploadCountForTesting(spaceId), Is.EqualTo(0));
            Assert.That(VirtualTextureSystem.GetPageTableLegacySetDataCallCountForTesting(spaceId), Is.EqualTo(1));

            Assert.That(VirtualTextureSystem.TryCapturePendingPageTableUpdatesForTesting(
                spaceId,
                out VTPageTableScatterUpdate[] firstCapture,
                out int pendingVersion,
                out bool fullUpload), Is.True);
            Assert.That(firstCapture, Has.Length.EqualTo(1));
            Assert.That(fullUpload, Is.False);
            Assert.That(firstCapture[0].DestinationIndex, Is.EqualTo((uint)VirtualTextureSpaceUtility.GetFlatIndex(
                desc,
                VirtualTextureSpaceUtility.BuildMipOffsets(
                    desc.VirtualPageCountX,
                    desc.VirtualPageCountY,
                    desc.MipCount),
                coord)));
            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                coord,
                out VirtualTexturePageTableEntry pendingEntry), Is.True);
            Assert.That(firstCapture[0].PackedValue, Is.EqualTo(pendingEntry.PackedValue));

            // Capturing is transactional: an aborted graph leaves the same update pending.
            Assert.That(VirtualTextureSystem.TryCapturePendingPageTableUpdatesForTesting(
                spaceId,
                out VTPageTableScatterUpdate[] retryCapture,
                out int retryVersion,
                out bool retryFullUpload), Is.True);
            Assert.That(retryCapture, Is.EqualTo(firstCapture));
            Assert.That(retryVersion, Is.EqualTo(pendingVersion));
            Assert.That(retryFullUpload, Is.EqualTo(fullUpload));

            Assert.That(VirtualTextureSystem.CommitCapturedPageTableUpdatesForTesting(
                spaceId,
                pendingVersion,
                fullUpload,
                firstCapture.Length), Is.True);
            Assert.That(VirtualTextureSystem.GetPageTablePendingUploadEntryCountForTesting(spaceId), Is.EqualTo(0));
            Assert.That(VirtualTextureSystem.GetPageTableLastUploadedEntryCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPageTableSparseUploadCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPageTableScatterUploadCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPageTableLegacySetDataCallCountForTesting(spaceId), Is.EqualTo(1));

            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var requests), Is.True);
            Assert.That(VirtualTextureSystem.CommitUpload(requests.Last()), Is.True);
            Assert.That(VirtualTextureSystem.GetPageTableLastRecomputedEntryCountForTesting(spaceId), Is.EqualTo(21));
            Assert.That(VirtualTextureSystem.GetPageTablePendingUploadEntryCountForTesting(spaceId), Is.GreaterThan(0));

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(3, 3, 0),
                out VirtualTexturePageTableEntry descendantEntry), Is.True);
            Assert.That(descendantEntry.Fallback, Is.True);
            Assert.That(descendantEntry.ResolvedMip, Is.EqualTo(2));
        }

        [Test]
        public void PageTable_StaleScatterCommitLeavesNewerUpdatesPending()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("StaleScatter", 4, 4, 3, 8, 4));
            var coord = new VirtualTexturePageCoord(1, 1, 1);
            RequestPages(spaceId, coord);

            Assert.That(VirtualTextureSystem.TryCapturePendingPageTableUpdatesForTesting(
                spaceId,
                out VTPageTableScatterUpdate[] capturedUpdates,
                out int capturedVersion,
                out bool capturedFullUpload), Is.True);
            Assert.That(VirtualTextureSystem.TryGetPendingUploadRequests(spaceId, out var requests), Is.True);
            Assert.That(VirtualTextureSystem.CommitUpload(requests.Last()), Is.True);

            Assert.That(VirtualTextureSystem.CommitCapturedPageTableUpdatesForTesting(
                spaceId,
                capturedVersion,
                capturedFullUpload,
                capturedUpdates.Length), Is.False);
            Assert.That(VirtualTextureSystem.GetPageTablePendingUploadEntryCountForTesting(spaceId),
                Is.GreaterThan(0));
            Assert.That(VirtualTextureSystem.TryCapturePendingPageTableUpdatesForTesting(
                spaceId,
                out _,
                out int retryVersion,
                out _), Is.True);
            Assert.That(retryVersion, Is.Not.EqualTo(capturedVersion));
        }

        [Test]
        public void PageTable_HalfDirtyThresholdCapturesFullScatterUpload()
        {
            VirtualTextureSpaceDesc desc = CreateDesc("FullScatter", 2, 2, 2, 8, 8);
            int spaceId = VirtualTextureSystem.RegisterSpace(desc);

            RequestPages(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 0),
                new VirtualTexturePageCoord(1, 0, 0),
                new VirtualTexturePageCoord(0, 1, 0));

            Assert.That(VirtualTextureSystem.TryCapturePendingPageTableUpdatesForTesting(
                spaceId,
                out VTPageTableScatterUpdate[] updates,
                out int pendingVersion,
                out bool fullUpload), Is.True);
            Assert.That(fullUpload, Is.True);
            Assert.That(updates, Has.Length.EqualTo(desc.PageTableEntryCount));
            for (int updateIndex = 0; updateIndex < updates.Length; updateIndex++)
                Assert.That(updates[updateIndex].DestinationIndex, Is.EqualTo((uint)updateIndex));

            Assert.That(VirtualTextureSystem.CommitCapturedPageTableUpdatesForTesting(
                spaceId,
                pendingVersion,
                fullUpload,
                updates.Length), Is.True);
            Assert.That(VirtualTextureSystem.GetPageTableFullUploadCountForTesting(spaceId), Is.EqualTo(2));
            Assert.That(VirtualTextureSystem.GetPageTableScatterUploadCountForTesting(spaceId), Is.EqualTo(1));
            Assert.That(VirtualTextureSystem.GetPageTableLegacySetDataCallCountForTesting(spaceId), Is.EqualTo(1));
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

        private static void AdvanceFrames(int frameCount)
        {
            var commandBuffer = new CommandBuffer();
            try
            {
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    commandBuffer.Clear();
                    VirtualTextureSystem.Update(new ContextContainer(), commandBuffer);
                }
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }
    }
}
