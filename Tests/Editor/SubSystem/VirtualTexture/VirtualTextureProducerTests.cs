using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureProducerTests
    {
        [Test]
        public void CheckerSourceProducer_CopiesNeighborTexelsIntoInternalGutter()
        {
            VirtualTextureSpaceDesc desc = CreateDesc();
            var leftPage = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
            var rightPage = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
            var leftRequest = new VTRequest(
                1,
                new VirtualTexturePageCoord(0, 0, 0),
                0,
                1,
                1,
                0);
            var rightRequest = new VTRequest(
                1,
                new VirtualTexturePageCoord(1, 0, 0),
                1,
                1,
                1,
                0);

            VTCheckerSourcePageProducer.Instance.WritePage(desc, leftRequest, leftPage);
            VTCheckerSourcePageProducer.Instance.WritePage(desc, rightRequest, rightPage);

            int y = desc.BorderSize + 3;
            Color32 leftRightGutter = leftPage[GetPixelIndex(desc, desc.BorderSize + desc.PageSize, y)];
            Color32 rightInteriorStart = rightPage[GetPixelIndex(desc, desc.BorderSize, y)];
            Color32 rightLeftGutter = rightPage[GetPixelIndex(desc, desc.BorderSize - 1, y)];
            Color32 leftInteriorEnd = leftPage[GetPixelIndex(desc, desc.BorderSize + desc.PageSize - 1, y)];

            Assert.That(leftRightGutter, Is.EqualTo(rightInteriorStart));
            Assert.That(rightLeftGutter, Is.EqualTo(leftInteriorEnd));
        }

        [Test]
        public void CheckerSourceProducer_ClampsOuterGutterToVirtualTextureEdge()
        {
            VirtualTextureSpaceDesc desc = CreateDesc();
            var pixels = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
            var request = new VTRequest(
                1,
                new VirtualTexturePageCoord(0, 0, 0),
                0,
                1,
                1,
                0);

            VTCheckerSourcePageProducer.Instance.WritePage(desc, request, pixels);

            int y = desc.BorderSize + 2;
            Color32 outerLeftGutter = pixels[GetPixelIndex(desc, 0, y)];
            Color32 firstInterior = pixels[GetPixelIndex(desc, desc.BorderSize, y)];
            Color32 outerTopGutter = pixels[GetPixelIndex(desc, desc.BorderSize + 2, 0)];
            Color32 firstInteriorRow = pixels[GetPixelIndex(desc, desc.BorderSize + 2, desc.BorderSize)];

            Assert.That(outerLeftGutter, Is.EqualTo(firstInterior));
            Assert.That(outerTopGutter, Is.EqualTo(firstInteriorRow));
        }

        [Test]
        public void CheckerSourceProducer_EvaluatesPredictableSourceTexelsAcrossMips()
        {
            VirtualTextureSpaceDesc desc = CreateDesc();

            Color32 mip0 = VTCheckerSourcePageProducer.EvaluateSourceTexel(desc, 0, 9, 11);
            Color32 mip1 = VTCheckerSourcePageProducer.EvaluateSourceTexel(desc, 1, 9, 11);
            Color32 mip0Clamped = VTCheckerSourcePageProducer.EvaluateSourceTexel(desc, 0, -10, -20);
            Color32 mip0Origin = VTCheckerSourcePageProducer.EvaluateSourceTexel(desc, 0, 0, 0);

            Assert.That(mip0, Is.Not.EqualTo(mip1));
            Assert.That(mip0Clamped, Is.EqualTo(mip0Origin));
        }

        private static VirtualTextureSpaceDesc CreateDesc()
        {
            return new VirtualTextureSpaceDesc(
                "CheckerSource",
                pageSize: 8,
                borderSize: 2,
                virtualPageCountX: 2,
                virtualPageCountY: 2,
                mipCount: 2,
                cachePageCount: 4,
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: 1,
                feedbackCapacity: 16);
        }

        private static int GetPixelIndex(in VirtualTextureSpaceDesc desc, int x, int y)
        {
            return y * desc.PhysicalPageSize + x;
        }
    }
}
