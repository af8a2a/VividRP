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
        public void PageTable_MaterializesBestAncestorFallbackWithoutShaderRecursion()
        {
            int spaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("Fallback", 4, 4, 3, 4, 4));

            Assert.That(VirtualTextureSystem.TryGetPageTableEntryForTesting(
                spaceId,
                new VirtualTexturePageCoord(0, 0, 2),
                out VirtualTexturePageTableEntry rootEntry), Is.True);
            Assert.That(rootEntry.Resident, Is.True);
            Assert.That(rootEntry.Locked, Is.True);

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
